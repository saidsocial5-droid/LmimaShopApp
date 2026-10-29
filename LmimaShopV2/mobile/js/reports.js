/* LmimaShop V2 mobile — dashboard, sync status, shift note and returns. */

import { api, ApiError } from './api.js';
import {
  state, isDeleted, productName, productAvailable, saleTotal, saleTva, saleCreatedAt,
  saleInvoice, saleCustomerUuid, customerName,
} from './store.js';
import {
  el, clear, toast, money, rial, round2, formatDate, localDateKey, nowStamp, number,
  openModal, closeModal, setBusy, uuid,
} from './ui.js';
import { fetchDelta, rebuildStateFromDB, enqueueOp, pushQueue } from './sync.js';

let reportsReady = false;
let returnSale = null;

export function initReports() {
  if (reportsReady) return;
  reportsReady = true;

  document.getElementById('flush-queue').addEventListener('click', async (event) => {
    const button = event.currentTarget;
    setBusy(button, true);
    try {
      const counters = await pushQueue();
      await fetchDelta();
      await rebuildStateFromDB();
      toast('تم إرسال ' + (counters.applied + counters.duplicate) + ' عملية', 'success');
    } catch (err) {
      console.error('Manual queue flush failed:', err);
      toast(err.message || 'تعذر إرسال العمليات', 'error');
    } finally {
      setBusy(button, false);
      renderReports();
    }
  });

  document.getElementById('health-btn').addEventListener('click', () => checkHealth());
  document.getElementById('shift-btn').addEventListener('click', openShiftModal);
  document.getElementById('shift-ping').addEventListener('click', () => checkHealth());
  document.getElementById('return-confirm').addEventListener('click', confirmReturn);

  document.getElementById('recent-sales').addEventListener('click', (event) => {
    const button = event.target.closest('[data-return-sale]');
    if (!button) return;
    const sale = state.sales.find((row) => row.uuid === button.dataset.returnSale);
    if (sale) openReturnModal(sale);
  });
}

export function renderReports() {
  renderStats();
  renderQueueInfo();
  renderShiftStatus();
  renderRecentSales();
}

function renderStats() {
  const host = document.getElementById('report-cards');
  if (!host) return;
  clear(host);
  const today = localDateKey();
  const todaysSales = state.sales.filter((sale) => localDateKey(saleCreatedAt(sale)) === today);
  const revenue = round2(todaysSales.reduce((sum, sale) => sum + saleTotal(sale), 0));
  const tva = round2(todaysSales.reduce((sum, sale) => sum + saleTva(sale), 0));
  const average = todaysSales.length ? round2(revenue / todaysSales.length) : 0;
  const lowStock = state.products.filter((product) => productAvailable(product) <= 5).length;

  const cards = [
    { label: 'مبيعات اليوم', value: money(revenue), sub: rial(revenue), accent: true },
    { label: 'عدد الفواتير', value: String(todaysSales.length), sub: 'اليوم' },
    { label: 'متوسط الفاتورة', value: money(average), sub: 'اليوم' },
    { label: 'ضريبة اليوم', value: money(tva), sub: 'TVA' },
    { label: 'المنتجات', value: String(state.products.length), sub: 'مخزون منخفض: ' + lowStock },
    { label: 'عمليات معلّقة', value: String(state.queueCount), sub: state.queueCount ? 'بانتظار الإرسال' : 'لا شيء معلّق' },
  ];
  for (const card of cards) {
    host.appendChild(el('div', { class: 'stat-card' + (card.accent ? ' accent' : '') },
      el('span', { class: 'stat-label', text: card.label }),
      el('span', { class: 'stat-value money', text: card.value }),
      el('span', { class: 'stat-sub', text: card.sub })
    ));
  }
}

function renderQueueInfo() {
  const host = document.getElementById('queue-info');
  if (!host) return;
  clear(host);
  const health = state.health;
  const healthText = health
    ? (health.error ? 'خطأ: ' + health.error : (health.app || 'LmimaShop') + ' ' + (health.version || ''))
    : 'لم يُفحص بعد';
  const rows = [
    ['حالة الاتصال', state.online ? 'متصل' : 'غير متصل'],
    ['عنوان الخادم', state.serverUrl || '—'],
    ['آخر مزامنة', state.lastSync ? formatDate(state.lastSync, true) : 'لم تتم بعد'],
    ['عمليات في الانتظار', String(state.queueCount)],
    ['حالة الخادم', healthText],
  ];
  for (const [label, value] of rows) {
    host.appendChild(el('div', { class: 'kv' }, el('span', { text: label }), el('b', { text: value })));
  }
}

function renderShiftStatus() {
  const host = document.getElementById('shift-status');
  if (!host) return;
  host.textContent = 'الوردية تُفتح وتُغلق من تطبيق سطح المكتب. لا يوجد API للوردية في نسخة الجوال.';
}

function renderRecentSales() {
  const host = document.getElementById('recent-sales');
  if (!host) return;
  clear(host);
  const sales = state.sales.slice(0, 8);
  if (!sales.length) {
    host.appendChild(el('p', { class: 'empty' }, 'لا توجد مبيعات محفوظة بعد'));
    return;
  }
  for (const sale of sales) {
    const customerUuid = saleCustomerUuid(sale);
    const customer = state.customers.find((row) => row.uuid === customerUuid);
    const title = 'فاتورة ' + (saleInvoice(sale) || String(sale.uuid || '').slice(0, 8));
    const sub = formatDate(saleCreatedAt(sale), true) + (customer ? ' · ' + customerName(customer) : '');
    host.appendChild(el('div', { class: 'list-item' },
      el('div', { class: 'list-main' },
        el('div', { class: 'list-title', text: title }),
        el('div', { class: 'list-sub', text: sub }),
        el('div', { class: 'list-sub' }, el('span', { class: 'product-price money', text: money(saleTotal(sale)) }))
      ),
      el('div', { class: 'list-actions' },
        el('button', { class: 'btn btn-small', type: 'button', dataset: { returnSale: sale.uuid }, text: 'إرجاع' })
      )
    ));
  }
}

async function checkHealth() {
  try {
    const payload = await api.ping();
    state.health = { app: payload.app, version: payload.version, at: nowStamp() };
    toast('الخادم متصل — ' + (payload.app || 'LmimaShop') + ' ' + (payload.version || ''), 'success');
  } catch (err) {
    console.error('Server health check failed:', err);
    state.health = { error: err.message || 'تعذر الاتصال', at: nowStamp() };
    toast('الخادم غير متاح: ' + (err.message || 'تعذر الاتصال'), 'error', 4800);
  }
  renderReports();
  renderShiftBody();
}

function openShiftModal() {
  renderShiftBody();
  openModal('modal-shift');
}

function renderShiftBody() {
  const body = document.getElementById('shift-body');
  if (!body) return;
  clear(body);
  body.appendChild(el('p', { class: 'muted' },
    'فتح وإغلاق الصندوق (Z-Report) متاح من تطبيق سطح المكتب فقط، ولا توجد نقطة API مخصصة للوردية. ' +
    'يمكنك التحقق من اتصال الخادم من هنا قبل البيع.'
  ));
  body.appendChild(el('div', { class: 'kv' },
    el('span', {}, 'آخر فحص'),
    el('b', { text: state.health && state.health.at ? formatDate(state.health.at, true) : 'لم يُفحص بعد' })
  ));
  body.appendChild(el('div', { class: 'kv' },
    el('span', {}, 'النتيجة'),
    el('b', { text: state.health ? (state.health.error || ((state.health.app || '') + ' ' + (state.health.version || '')).trim() || 'متصل') : 'غير معروف' })
  ));
}

/* ---------- returns ---------- */

function saleItemsFor(sale) {
  return state.saleItems.filter((item) => {
    if (isDeleted(item)) return false;
    const saleUuid = item.sale_uuid || item.SaleUuid;
    return saleUuid === sale.uuid;
  });
}

async function openReturnModal(sale) {
  returnSale = sale;
  const host = document.getElementById('return-items');
  const summary = document.getElementById('return-summary');
  const errorEl = document.getElementById('return-error');
  const confirmButton = document.getElementById('return-confirm');
  clear(host);
  clear(summary);
  errorEl.textContent = '';
  document.getElementById('return-reason').value = '';
  host.appendChild(el('p', { class: 'empty' }, 'جارٍ تحميل أصناف الفاتورة...'));

  let items = saleItemsFor(sale);
  if (!items.length && state.online) {
    try {
      await fetchDelta();
      await rebuildStateFromDB();
      items = saleItemsFor(sale);
    } catch (err) {
      console.warn('Could not refresh sale items:', err);
    }
  }

  summary.appendChild(el('div', { class: 'kv' },
    el('span', {}, 'الفاتورة'),
    el('b', { text: saleInvoice(sale) || String(sale.uuid || '').slice(0, 8) })
  ));
  summary.appendChild(el('div', { class: 'kv' },
    el('span', {}, 'التاريخ'),
    el('b', { text: formatDate(saleCreatedAt(sale), true) })
  ));
  summary.appendChild(el('div', { class: 'kv' },
    el('span', {}, 'الإجمالي'),
    el('b', { class: 'money', text: money(saleTotal(sale)) })
  ));

  clear(host);
  const usable = items.filter((item) => item.uuid);
  if (!usable.length) {
    host.appendChild(el('p', { class: 'empty' }, 'تفاصيل الأصناف غير متاحة محلياً. زامن الخادم ثم أعد المحاولة.'));
    confirmButton.disabled = true;
    openModal('modal-return');
    return;
  }
  confirmButton.disabled = false;
  for (const item of usable) {
    const product = state.products.find((row) => row.uuid === (item.product_uuid || item.ProductUuid));
    const maxQty = number(item.qty !== undefined ? item.qty : (item.quantity !== undefined ? item.quantity : item.Quantity), 0);
    const price = number(item.unit_price !== undefined ? item.unit_price : item.UnitPrice, 0);
    host.appendChild(el('label', { class: 'return-item', dataset: { returnItem: item.uuid, max: String(maxQty) } },
      el('input', { type: 'checkbox' }),
      el('span', {},
        el('b', { text: product ? productName(product) : 'منتج' }),
        el('span', { class: 'muted', text: ' · ' + money(price) + ' · الكمية ' + round2(maxQty) })
      ),
      el('input', { class: 'input', type: 'number', min: '0.001', max: String(maxQty), step: '0.001', value: String(maxQty), inputmode: 'decimal' })
    ));
  }
  openModal('modal-return');
}

async function confirmReturn() {
  if (!returnSale) return;
  const host = document.getElementById('return-items');
  const errorEl = document.getElementById('return-error');
  const confirmButton = document.getElementById('return-confirm');
  errorEl.textContent = '';
  const selected = [];
  const rows = host.querySelectorAll('[data-return-item]');
  for (const row of rows) {
    const checkbox = row.querySelector('input[type="checkbox"]');
    const qtyInput = row.querySelector('input[type="number"]');
    if (!checkbox || !checkbox.checked) continue;
    const maxQty = number(row.dataset.max);
    const qty = round2(number(qtyInput ? qtyInput.value : 0));
    if (!(qty > 0) || qty > maxQty + 0.0001) {
      errorEl.textContent = 'كمية الإرجاع غير صحيحة';
      return;
    }
    selected.push({ sale_item_uuid: row.dataset.returnItem, qty });
  }
  if (!selected.length) {
    errorEl.textContent = 'اختر صنفاً واحداً على الأقل';
    return;
  }
  const payload = {
    op_id: uuid(),
    sale_uuid: returnSale.uuid,
    items: selected,
    reason: document.getElementById('return-reason').value.trim(),
  };

  setBusy(confirmButton, true);
  try {
    if (state.online) {
      try {
        const result = await api.createReturn(payload);
        closeModal('modal-return');
        toast('تم الإرجاع — المبلغ المسترد ' + money(result.refund_total || 0), 'success', 5200);
        try {
          await fetchDelta();
          await rebuildStateFromDB();
        } catch (err) {
          console.warn('Delta after return failed:', err);
        }
        returnSale = null;
        return;
      } catch (err) {
        if (!(err instanceof ApiError && (err.network || err.status >= 500))) throw err;
        console.warn('Return API unreachable — queueing:', err.message);
      }
    }
    await enqueueOp('return_create', payload, payload.op_id);
    closeModal('modal-return');
    toast('تم حفظ الإرجاع محلياً — سيُرسل عند عودة الاتصال', 'warn', 5200);
    await rebuildStateFromDB();
    returnSale = null;
    pushQueue().catch((err) => console.warn('Queued return push postponed:', err));
  } catch (err) {
    console.error('Return failed:', err);
    if (err && err.name === 'AuthError') return;
    errorEl.textContent = err.message || 'تعذر تسجيل الإرجاع';
    toast(errorEl.textContent, 'error', 5000);
  } finally {
    setBusy(confirmButton, false);
  }
}
