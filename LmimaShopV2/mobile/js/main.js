/* LmimaShop V2 mobile — bootstrap, routing, pairing and settings. */

import {
  VERSION, clearServerUrl, clearSession, getDeviceId, getServerUrl, getSuggestedServer,
  getTheme, setServerUrl, setTheme, getToken,
} from './config.js';
import {
  initUI, toast, el, clear, confirmDialog, closeAllModals, setBusy, formatDate,
} from './ui.js';
import { scanCode } from './scanner.js';
import * as db from './db.js';
import { state, subscribe, notify } from './store.js';
import { pairDevice, rebuildStateFromDB, syncNow, startSyncLoops, stopEvents } from './sync.js';
import { initQrPair, extractPairCode, extractServer } from './qr-pair.js';
import { initPOS, renderPOS } from './pos.js';
import { initPayment } from './payment.js';
import { initProducts, renderProducts } from './products.js';
import { initCustomers, renderCustomers } from './customers.js';
import { initReports, renderReports } from './reports.js';

const TAB_TITLES = {
  pos: 'نقطة البيع',
  products: 'المنتجات',
  customers: 'الزبائن',
  reports: 'لوحة التحكم',
  settings: 'الإعدادات',
};

let wired = false;
let pairing = false;

function applyTheme(theme) {
  if (theme === 'dark' || theme === 'light') document.documentElement.dataset.theme = theme;
  else delete document.documentElement.dataset.theme;
}

function renderHeader() {
  const dot = document.getElementById('conn-dot');
  const text = document.getElementById('conn-text');
  if (!dot || !text) return;
  const online = Boolean(state.online && state.token);
  dot.classList.toggle('online', online);
  if (!online) text.textContent = 'غير متصل';
  else if (state.queueCount > 0) text.textContent = 'متصل · ' + state.queueCount + ' معلّق';
  else text.textContent = 'متصل';
}

function showPairing(message = '') {
  pairing = true;
  stopEvents();
  const appScreen = document.getElementById('screen-app');
  const pairScreen = document.getElementById('screen-pairing');
  if (appScreen) appScreen.hidden = true;
  if (pairScreen) pairScreen.hidden = false;
  const serverInput = document.getElementById('pair-server');
  if (serverInput && !serverInput.value) serverInput.value = getSuggestedServer();
  const errorEl = document.getElementById('pair-error');
  if (errorEl) errorEl.textContent = message;
  const versionEl = document.getElementById('pair-version');
  if (versionEl) versionEl.textContent = 'الإصدار ' + VERSION;
  window.scrollTo({ top: 0 });
  setTimeout(() => {
    const codeInput = document.getElementById('pair-code');
    if (codeInput && !codeInput.value) codeInput.focus();
  }, 150);
}

async function enterApp() {
  const appScreen = document.getElementById('screen-app');
  const pairScreen = document.getElementById('screen-pairing');
  if (pairScreen) pairScreen.hidden = true;
  if (appScreen) appScreen.hidden = false;
  pairing = false;
  state.serverUrl = getServerUrl();
  state.token = getToken();
  state.online = navigator.onLine;
  try {
    await rebuildStateFromDB();
  } catch (err) {
    console.error('Could not load local data:', err);
    toast('تعذر تحميل البيانات المحلية: ' + (err.message || ''), 'error', 5000);
  }
  switchTab(state.tab || 'pos', false);
  renderHeader();
  startSyncLoops();
}

function switchTab(tab, scroll = true) {
  const selected = TAB_TITLES[tab] ? tab : 'pos';
  state.tab = selected;
  document.querySelectorAll('.panel').forEach((panel) => {
    panel.hidden = panel.dataset.panel !== selected;
  });
  document.querySelectorAll('.tab').forEach((button) => {
    const active = button.dataset.tab === selected;
    button.classList.toggle('is-active', active);
    if (active) button.setAttribute('aria-current', 'page');
    else button.removeAttribute('aria-current');
  });
  const title = document.getElementById('header-title');
  if (title) title.textContent = TAB_TITLES[selected];
  renderTab(selected);
  if (scroll) window.scrollTo({ top: 0, behavior: 'smooth' });
}

function renderTab(tab) {
  if (tab === 'pos') renderPOS();
  else if (tab === 'products') renderProducts();
  else if (tab === 'customers') renderCustomers();
  else if (tab === 'reports') renderReports();
  else if (tab === 'settings') renderSettings();
}

/* ---------- settings ---------- */

function settingsCard(title) {
  const children = Array.prototype.slice.call(arguments, 1);
  return el('section', { class: 'card settings-section' },
    el('div', { class: 'card-head' }, el('h2', { text: title })),
    ...children
  );
}

function kv(label, value) {
  return el('div', { class: 'kv' }, el('span', { text: label }), el('b', { text: value }));
}

function renderSettings() {
  const host = document.getElementById('settings-content');
  if (!host) return;
  clear(host);
  const theme = getTheme();
  const themeLabel = theme === 'dark' ? 'داكن' : theme === 'light' ? 'فاتح' : 'تلقائي (النظام)';

  host.appendChild(settingsCard('الخادم والجهاز',
    kv('عنوان الخادم', state.serverUrl || '—'),
    kv('معرّف الجهاز', getDeviceId() || '—'),
    kv('حالة الجلسة', getToken() ? 'مقترن' : 'غير مقترن'),
    kv('إصدار التطبيق', VERSION),
    el('div', { class: 'toolbar' },
      el('button', { class: 'btn btn-primary btn-small', type: 'button', dataset: { setting: 'sync' }, text: 'مزامنة الآن' }),
      el('button', { class: 'btn btn-ghost btn-small', type: 'button', dataset: { setting: 'resync' }, text: 'إعادة مزامنة كاملة' })
    )
  ));

  host.appendChild(settingsCard('المزامنة والبيانات',
    kv('عمليات في الانتظار', String(state.queueCount)),
    kv('آخر مزامنة', state.lastSync ? formatDate(state.lastSync, true) : 'لم تتم بعد'),
    el('div', { class: 'toolbar' },
      el('button', { class: 'btn btn-ghost btn-small', type: 'button', dataset: { setting: 'flush' }, text: 'إرسال العمليات المعلّقة' })
    )
  ));

  host.appendChild(settingsCard('المظهر',
    el('div', { class: 'settings-row' },
      el('div', { class: 'grow' },
        el('b', {}, 'مظهر التطبيق'),
        el('small', {}, 'الوضع الحالي: ' + themeLabel)
      ),
      el('button', { class: 'btn btn-small', type: 'button', dataset: { setting: 'theme' }, text: 'تبديل المظهر' })
    )
  ));

  host.appendChild(settingsCard('الجهاز',
    el('p', { class: 'muted' }, 'نسيان الجهاز يحذف رمز الجلسة وعنوان الخادم والبيانات المحلية من هذا الجوال. ستحتاج رمز اقتران جديداً من سطح المكتب.'),
    el('button', { class: 'btn btn-danger btn-block', type: 'button', dataset: { setting: 'forget' }, text: 'نسيان الجهاز وحذف البيانات' })
  ));
}

function cycleTheme() {
  const current = getTheme();
  const next = current === '' ? 'dark' : current === 'dark' ? 'light' : '';
  setTheme(next);
  applyTheme(next);
  toast('المظهر: ' + (next === 'dark' ? 'داكن' : next === 'light' ? 'فاتح' : 'تلقائي'), 'info', 2000);
  renderSettings();
}

async function forgetDevice() {
  const confirmed = await confirmDialog(
    'سيتم حذف رمز الجلسة وعنوان الخادم وكل البيانات المحلية. ستحتاج رمز اقتران جديداً. هل تريد المتابعة؟',
    { title: 'نسيان الجهاز', danger: true, confirmText: 'حذف ونسيان' }
  );
  if (!confirmed) return;
  try {
    stopEvents();
    clearSession();
    clearServerUrl();
    await db.wipeData();
    state.token = null;
    state.serverUrl = null;
    state.products = [];
    state.categories = [];
    state.customers = [];
    state.customerPayments = [];
    state.suppliers = [];
    state.sales = [];
    state.saleItems = [];
    state.returns = [];
    state.pendingStock = {};
    state.queueCount = 0;
    state.cursor = null;
    state.lastSync = null;
    state.health = null;
    state.cart = [];
    state.cartCustomer = null;
    state.discountPct = 0;
    state.tab = 'pos';
    notify();
    closeAllModals();
    showPairing('تم نسيان الجهاز. أدخل رمز اقتران جديداً من سطح المكتب.');
    toast('تم حذف بيانات الجهاز المحلية', 'success');
  } catch (err) {
    console.error('Forget device failed:', err);
    toast('تعذر حذف البيانات: ' + (err.message || ''), 'error', 5000);
  }
}

/* ---------- pairing ---------- */

async function handlePairSubmit(event) {
  event.preventDefault();
  if (pairing === 'busy') return;
  const serverInput = document.getElementById('pair-server');
  const codeInput = document.getElementById('pair-code');
  const errorEl = document.getElementById('pair-error');
  errorEl.textContent = '';
  const server = setServerUrl(serverInput.value);
  if (!server) {
    errorEl.textContent = 'أدخل عنوان خادم صالحاً، مثل http://192.168.1.10:8090';
    serverInput.focus();
    return;
  }
  serverInput.value = server;
  const code = codeInput.value.replace(/\D/g, '').slice(0, 6);
  codeInput.value = code;
  if (code.length !== 6) {
    errorEl.textContent = 'رمز الاقتران يجب أن يكون 6 أرقام';
    codeInput.focus();
    return;
  }
  const button = document.getElementById('pair-submit');
  pairing = 'busy';
  setBusy(button, true);
  try {
    await pairDevice(code);
    pairing = false;
    await enterApp();
  } catch (err) {
    console.error('Pairing failed:', err);
    pairing = true;
    if (err && err.status === 429) errorEl.textContent = 'محاولات كثيرة. انتظر دقيقة ثم أعد المحاولة.';
    else if (err && err.status === 401) errorEl.textContent = 'الرمز غير صحيح أو منتهي الصلاحية. اطلب رمزاً جديداً من سطح المكتب.';
    else errorEl.textContent = err.message || 'تعذر الاقتران بالخادم';
    toast(errorEl.textContent, 'error', 5000);
  } finally {
    if (pairing === 'busy') pairing = true;
    setBusy(button, false);
  }
}

async function handlePairScan() {
  const text = await scanCode();
  if (!text) return;
  const code = extractPairCode(text);
  const server = extractServer(text);
  if (code) document.getElementById('pair-code').value = code;
  if (server) document.getElementById('pair-server').value = server;
  if (!code) {
    toast('لم يُعثر على رمز اقتران في الرمز الممسوح', 'error');
    return;
  }
  toast('تمت تعبئة رمز الاقتران — اضغط "اقتران الجهاز"', 'info', 4000);
  document.getElementById('pair-submit').focus();
}

/* ---------- global wiring ---------- */

function wireEvents() {
  if (wired) return;
  wired = true;

  document.getElementById('pair-form').addEventListener('submit', handlePairSubmit);
  document.getElementById('pair-scan').addEventListener('click', () => {
    handlePairScan().catch((err) => {
      console.error('Pair scan failed:', err);
      toast('تعذر فتح الماسح', 'error');
    });
  });

  document.getElementById('tabbar').addEventListener('click', (event) => {
    const button = event.target.closest('[data-tab]');
    if (button) switchTab(button.dataset.tab);
  });

  document.getElementById('header-sync').addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await syncNow();
    } catch (err) {
      console.warn('Manual sync failed:', err);
    } finally {
      button.disabled = false;
      renderHeader();
      renderTab(state.tab);
    }
  });

  document.getElementById('settings-content').addEventListener('click', async (event) => {
    const button = event.target.closest('[data-setting]');
    if (!button) return;
    const action = button.dataset.setting;
    if (action === 'theme') {
      cycleTheme();
      return;
    }
    if (action === 'forget') {
      await forgetDevice();
      return;
    }
    if (action === 'sync' || action === 'flush') {
      setBusy(button, true);
      try {
        await syncNow();
      } catch (err) {
        console.warn('Settings sync failed:', err);
      } finally {
        setBusy(button, false);
        renderSettings();
        renderHeader();
      }
      return;
    }
    if (action === 'resync') {
      setBusy(button, true);
      try {
        await db.setMeta('cursor', '');
        state.cursor = '';
        await syncNow({ quiet: true });
        await rebuildStateFromDB();
        toast('تمت إعادة المزامنة الكاملة', 'success');
      } catch (err) {
        console.error('Full resync failed:', err);
        toast('تعذرت إعادة المزامنة: ' + (err.message || ''), 'error');
      } finally {
        setBusy(button, false);
        renderSettings();
        renderTab(state.tab);
      }
    }
  });

  window.addEventListener('online', () => {
    state.online = true;
    notify();
    toast('عاد الاتصال — جارٍ المزامنة', 'success', 2200);
    syncNow({ quiet: true }).catch((err) => console.warn('Reconnect sync failed:', err));
  });

  window.addEventListener('offline', () => {
    state.online = false;
    notify();
    toast('وضع عدم الاتصال — تُحفظ العمليات محلياً', 'warn', 3500);
  });

  window.addEventListener('auth:lost', () => {
    console.warn('Authentication lost; re-pairing required');
    stopEvents();
    clearSession();
    state.token = null;
    closeAllModals();
    showPairing('انتهت صلاحية الاقتران. اطلب رمزاً جديداً من تطبيق سطح المكتب.');
    toast('انتهت صلاحية الجهاز — أعد الاقتران', 'warn', 5500);
  });

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState !== 'visible') return;
    if (!getToken() || !getServerUrl()) return;
    syncNow({ quiet: true }).catch((err) => console.warn('Visibility sync failed:', err));
  });

  subscribe(() => {
    renderHeader();
    if (state.tab === 'pos') renderPOS();
    else if (state.tab === 'products') renderProducts();
    else if (state.tab === 'customers') renderCustomers();
    else if (state.tab === 'reports') renderReports();
  });
}

async function boot() {
  applyTheme(getTheme());
  initUI();
  wireEvents();
  document.getElementById('pair-version').textContent = 'الإصدار ' + VERSION;
  try {
    await db.init();
  } catch (err) {
    console.error('Local database unavailable:', err);
    toast('تعذر فتح قاعدة البيانات المحلية — العمل دون اتصال غير مضمون', 'error', 6000);
  }
  initPOS();
  initPayment();
  initProducts();
  initCustomers();
  initReports();
  initQrPair({
    codeInput: document.getElementById('pair-code'),
    serverInput: document.getElementById('pair-server'),
    onCode: () => {
      document.getElementById('pair-submit').focus();
    },
  });

  if ('serviceWorker' in navigator && /^https?:$/.test(location.protocol)) {
    const registerWorker = () => {
      navigator.serviceWorker.register('./sw.js').catch((err) => {
        console.error('Service worker registration failed:', err);
      });
    };
    if (document.readyState === 'complete') registerWorker();
    else window.addEventListener('load', registerWorker, { once: true });
  }

  if (!getToken() || !getServerUrl()) {
    showPairing();
    return;
  }
  await enterApp();
}

boot().catch((err) => {
  console.error('App boot failed:', err);
  toast('تعذر بدء التطبيق: ' + (err.message || ''), 'error', 6000);
  showPairing();
});
