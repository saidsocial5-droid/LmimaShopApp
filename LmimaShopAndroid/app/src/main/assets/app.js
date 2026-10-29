// ==========================================
// LMIMA SHOP - Mobile Companion Application
// ==========================================

const AppState = {
  data: null,
  activeTab: 'dashboard',
  scanner: null,
  isScanning: false,
  activeDebtFilter: 'all',
  profitUnlocked: false
};

// 1. تشغيل الصوت عند مسح الباركود بنجاح (Web Audio API)
function playBeep() {
  try {
    const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
    const osc = audioCtx.createOscillator();
    const gain = audioCtx.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(880, audioCtx.currentTime); // 880 Hz
    gain.gain.setValueAtTime(0.15, audioCtx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.12);
    osc.connect(gain);
    gain.connect(audioCtx.destination);
    osc.start();
    osc.stop(audioCtx.currentTime + 0.12);
  } catch (e) {
    console.log('Audio beep not allowed yet:', e);
  }
}

// 2. دوال تنسيق العملة المغربية (درهم وريال)
function formatDH(num) {
  const n = parseFloat(num) || 0;
  return n.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' د.م.';
}

function formatRial(num) {
  const n = parseFloat(num) || 0;
  const rial = Math.round(n * 20);
  return rial.toLocaleString('fr-FR') + ' ريال';
}

function cleanMoroccanPhone(phone) {
  if (!phone) return '';
  let clean = phone.replace(/[^0-9]/g, '');
  if (clean.startsWith('0')) {
    clean = '212' + clean.substring(1);
  } else if (!clean.startsWith('212') && clean.length === 9) {
    clean = '212' + clean;
  }
  return clean;
}

// 3. إظهار إشعار سريع (Toast)
function showToast(msg) {
  const toast = document.getElementById('toast');
  if (!toast) return;
  toast.textContent = msg;
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), 2800);
}

// 4. تحميل البيانات (سحابياً / محلياً)
async function initData() {
  let loadedData = null;

  // 1. فحص إذا كان ملف المزامنة مضمناً عبر سكريبت نافذة المتصفح
  if (window.LMIMA_SYNC_DATA && typeof window.LMIMA_SYNC_DATA === 'object') {
    loadedData = window.LMIMA_SYNC_DATA;
    console.log('[Sync] Loaded from window.LMIMA_SYNC_DATA');
  }

  // 2. فحص محاولة القراءة عبر fetch (إذا كان يعمل عبر خادم ويب)
  if (!loadedData) {
    try {
      const res = await fetch('lmima_mobile_sync.json?t=' + Date.now());
      if (res.ok) {
        loadedData = await res.json();
        console.log('[Sync] Loaded from fetch lmima_mobile_sync.json');
      }
    } catch (e) {
      console.log('[Sync] Fetch failed, checking localStorage fallback');
    }
  }

  // 3. فحص التخزين المحلي في حال عدم وجود إنترنت
  if (!loadedData) {
    const saved = localStorage.getItem('lmima_sync_data');
    if (saved) {
      try {
        loadedData = JSON.parse(saved);
        console.log('[Sync] Loaded from localStorage cache');
      } catch (e) {}
    }
  }

  if (loadedData) {
    applySyncData(loadedData, false);
  } else {
    updateSyncStatus('offline', 'لم يتم العثور على بيانات المزامنة بعد');
  }
}

function applySyncData(data, saveToStorage = true) {
  AppState.data = data;
  if (saveToStorage) {
    try {
      localStorage.setItem('lmima_sync_data', JSON.stringify(data));
    } catch (e) {}
  }

  // تحديث حالة المزامنة
  const ts = data.StoreInfo?.LastSyncTimestamp || new Date().toLocaleTimeString();
  updateSyncStatus('online', `🟢 متصل ومُحدث (${ts})`);

  // تحديث الهيدر
  if (data.StoreInfo?.StoreName) {
    document.getElementById('store-name-display').textContent = data.StoreInfo.StoreName;
  }

  // تحديث العدادات
  const prodCount = data.Products?.length || 0;
  const debtCount = data.CustomerDebts?.length || 0;
  document.getElementById('records-count-text').textContent = `${prodCount} منتج • ${debtCount} زبون`;

  // رندرة الشاشات
  renderDashboard();
  renderDebts();
  renderProductsCatalog();
  renderSettings();
}

function updateSyncStatus(state, text) {
  const statusEl = document.getElementById('sync-status-text');
  const pulseEl = document.getElementById('sync-pulse');
  if (statusEl) statusEl.textContent = text;
  if (pulseEl) {
    pulseEl.className = 'pulse-dot ' + (state === 'online' ? '' : (state === 'local' ? 'warning' : 'danger'));
  }
}

// 5. رندرة شاشة المبيعات (Dashboard)
function renderDashboard() {
  const d = AppState.data?.TodaySummary || {};
  document.getElementById('today-date-display').textContent = d.Date || new Date().toLocaleDateString('fr-FR');
  document.getElementById('today-sales-dh').textContent = formatDH(d.TotalSales);
  document.getElementById('today-sales-rial').textContent = formatRial(d.TotalSales);

  document.getElementById('today-profit-dh').textContent = '+' + formatDH(d.EstimatedProfit);
  document.getElementById('today-profit-rial').textContent = formatRial(d.EstimatedProfit);

  document.getElementById('today-invoices-count').textContent = (d.InvoicesCount || 0) + ' فاتورة';
  document.getElementById('today-cash-dh').textContent = formatDH(d.CashSales);
  document.getElementById('today-cash-rial').textContent = formatRial(d.CashSales);
  document.getElementById('today-credit-dh').textContent = formatDH(d.CreditSales);
  document.getElementById('today-credit-rial').textContent = formatRial(d.CreditSales);

  renderInvoicesList();
}

function renderInvoicesList() {
  const container = document.getElementById('invoices-list');
  const invoices = AppState.data?.RecentInvoices || [];
  const search = (document.getElementById('invoice-search-input')?.value || '').trim().toLowerCase();

  const filtered = invoices.filter(inv => {
    if (!search) return true;
    return String(inv.InvoiceId).includes(search) || 
           (inv.Customer && inv.Customer.toLowerCase().includes(search)) ||
           (inv.Cashier && inv.Cashier.toLowerCase().includes(search));
  });

  document.getElementById('invoices-counter-badge').textContent = `${filtered.length} فاتورة`;

  if (filtered.length === 0) {
    container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 24px;">لا توجد فواتير مطابقة</div>';
    return;
  }

  container.innerHTML = filtered.map(inv => {
    const isCredit = inv.PaymentMethod?.includes('كريدي') || (inv.Customer && !inv.Customer.includes('نقدي'));
    const badgeClass = isCredit ? 'badge-credit' : 'badge-cash';
    const badgeText = isCredit ? 'كريدي (دين)' : 'كاش (نقداً)';

    return `
      <div class="invoice-card">
        <div class="invoice-info">
          <h4>فاتورة #${inv.InvoiceId} - ${inv.Customer || 'زبون نقدي'}</h4>
          <div class="invoice-sub">
            <span>🕒 ${inv.Date || ''}</span> • 
            <span class="badge ${badgeClass}">${badgeText}</span> • 
            <span>${inv.ItemsCount || 1} منتج</span>
          </div>
        </div>
        <div class="invoice-amount-box">
          <div class="invoice-amount-dh">${formatDH(inv.TotalAmount)}</div>
          <div class="invoice-amount-rial">${formatRial(inv.TotalAmount)}</div>
        </div>
      </div>
    `;
  }).join('');
}

// 6. رندرة شاشة ديون الزبائن (Debts)
function renderDebts() {
  const debtors = AppState.data?.CustomerDebts || [];
  const search = (document.getElementById('debt-search-input')?.value || '').trim().toLowerCase();
  const filter = AppState.activeDebtFilter;

  let totalDebt = 0;
  debtors.forEach(c => totalDebt += (parseFloat(c.DueBalance) || 0));

  document.getElementById('total-debts-dh').textContent = formatDH(totalDebt);
  document.getElementById('total-debts-rial').textContent = formatRial(totalDebt);
  document.getElementById('debtors-count-badge').textContent = `${debtors.length} زبون`;
  document.getElementById('count-all-debts').textContent = debtors.length;

  const filtered = debtors.filter(c => {
    const bal = parseFloat(c.DueBalance) || 0;
    if (filter === 'over100' && bal < 100) return false;
    if (filter === 'over300' && bal < 300) return false;
    if (filter === 'over500' && bal < 500) return false;

    if (!search) return true;
    return (c.FullName && c.FullName.toLowerCase().includes(search)) ||
           (c.Phone && c.Phone.includes(search));
  });

  const container = document.getElementById('debtors-list');
  if (filtered.length === 0) {
    container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 30px;">لا يوجد زبائن مطابقون للبحث</div>';
    return;
  }

  container.innerHTML = filtered.map(c => {
    const bal = parseFloat(c.DueBalance) || 0;
    const rial = Math.round(bal * 20);
    const hasPhone = c.Phone && c.Phone !== '-' && c.Phone.length >= 8;
    const cleanPhone = cleanMoroccanPhone(c.Phone);

    const storeName = AppState.data?.StoreInfo?.StoreName || 'متجر LMIMA SHOP';
    const waText = encodeURIComponent(`السلام عليكم ورحمة الله أخي ${c.FullName}،\nنذكركم باحترام بأن المبلغ المتبقي بذمتكم لدى ${storeName} هو:\n💰 ${bal.toFixed(2)} درهم (${rial.toLocaleString()} ريال).\nشكراً لتعاملكم معنا ونتمنى لكم يوماً طيباً.`);
    const waUrl = hasPhone ? `https://wa.me/${cleanPhone}?text=${waText}` : `https://wa.me/?text=${waText}`;

    return `
      <div class="debtor-card">
        <div class="debtor-header">
          <div>
            <div class="debtor-name">👤 ${c.FullName}</div>
            <div class="debtor-phone">📱 ${hasPhone ? c.Phone : 'بدون رقم هاتف'}</div>
          </div>
          <div class="debt-amount-box">
            <div class="debt-dh">${formatDH(bal)}</div>
            <div class="debt-rial">${formatRial(bal)}</div>
          </div>
        </div>

        <div class="debtor-meta">
          <span>سقف الدين: ${formatDH(c.CreditLimit)}</span>
          <span>آخر دين: ${c.LastCreditDate || 'غير مسجل'}</span>
          <span>عدد المعاملات: ${c.InvoiceCount || 0}</span>
        </div>

        <div class="debtor-actions">
          <a href="${waUrl}" target="_blank" class="action-btn btn-whatsapp">
            <span>💬 تذكير واتساب</span>
          </a>
          ${hasPhone ? `
            <a href="tel:${c.Phone}" class="action-btn btn-call">
              <span>📞 اتصال هاتفي</span>
            </a>
          ` : `
            <button class="action-btn" style="background: #e2e8f0; color: #94a3b8;" disabled>
              <span>📞 بدون رقم</span>
            </button>
          `}
        </div>
      </div>
    `;
  }).join('');
}

// 7. رندرة وتصفح قائمة المنتجات (Scanner / Catalog)
function renderProductsCatalog() {
  const container = document.getElementById('products-catalog-list');
  const products = AppState.data?.Products || [];
  const search = (document.getElementById('product-search-input')?.value || '').trim().toLowerCase();

  document.getElementById('products-catalog-count').textContent = products.length;

  const filtered = products.filter(p => {
    if (!search) return true;
    return (p.Name && p.Name.toLowerCase().includes(search)) ||
           (p.Barcode && p.Barcode.toLowerCase().includes(search)) ||
           (p.Category && p.Category.toLowerCase().includes(search));
  }).slice(0, 30); // عرض أول 30 منتج للتصفح السريع

  if (filtered.length === 0) {
    container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 20px;">لا توجد منتجات مطابقة</div>';
    return;
  }

  container.innerHTML = filtered.map(p => {
    const stock = parseFloat(p.StockQuantity) || 0;
    let stockBadgeClass = 'stock-in';
    let stockText = `${stock} قطعة متوفرة`;
    if (stock <= 0) {
      stockBadgeClass = 'stock-out';
      stockText = 'نفذ من المخزون';
    } else if (stock <= 5) {
      stockBadgeClass = 'stock-low';
      stockText = `قارب على النفاد (${stock})`;
    }

    return `
      <div class="product-card" style="margin-bottom: 8px; padding: 12px; cursor: pointer;" onclick="selectProductFromCatalog('${p.Barcode}')">
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <div>
            <div style="font-size: 14px; font-weight: 700;">${p.Name}</div>
            <div style="font-size: 11px; color: var(--text-muted); font-family: monospace;">باركود: ${p.Barcode}</div>
          </div>
          <div style="text-align: left;">
            <div style="font-size: 15px; font-weight: 800; color: var(--success);">${formatDH(p.Price)}</div>
            <div style="font-size: 11px; color: #b45309; font-weight: 600;">${formatRial(p.Price)}</div>
          </div>
        </div>
        <div style="margin-top: 6px; display: flex; justify-content: space-between; align-items: center;">
          <span style="font-size: 10px; color: var(--text-muted);">${p.Category || 'عام'}</span>
          <span class="stock-badge ${stockBadgeClass}" style="font-size: 10px; padding: 2px 6px;">${stockText}</span>
        </div>
      </div>
    `;
  }).join('');
}

function selectProductFromCatalog(barcode) {
  const p = (AppState.data?.Products || []).find(item => item.Barcode === barcode);
  if (p) {
    showProductResult(p);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}

function showProductResult(p) {
  document.getElementById('scanned-product-result').style.display = 'block';
  document.getElementById('res-prod-name').textContent = p.Name;
  document.getElementById('res-prod-barcode').textContent = p.Barcode;
  document.getElementById('res-prod-category').textContent = 'الفئة: ' + (p.Category || 'عام');

  document.getElementById('res-prod-price-dh').textContent = formatDH(p.Price);
  document.getElementById('res-prod-price-rial').textContent = formatRial(p.Price);

  const stock = parseFloat(p.StockQuantity) || 0;
  const badgeEl = document.getElementById('res-prod-stock-badge');
  if (stock <= 0) {
    badgeEl.className = 'stock-badge stock-out';
    badgeEl.textContent = 'نفذ من المخزون';
  } else if (stock <= 5) {
    badgeEl.className = 'stock-badge stock-low';
    badgeEl.textContent = `قارب على الانتهاء (${stock})`;
  } else {
    badgeEl.className = 'stock-badge stock-in';
    badgeEl.textContent = `${stock} قطعة متوفرة`;
  }

  const buy = parseFloat(p.BuyPrice) || 0;
  const sell = parseFloat(p.Price) || 0;
  const profit = sell - buy;

  document.getElementById('res-prod-buy-dh').textContent = formatDH(buy);
  document.getElementById('res-prod-profit-dh').textContent = '+' + formatDH(profit) + ' (' + formatRial(profit) + ')';

  playBeep();
  showToast(`تم العثور على: ${p.Name}`);
}

// 8. تشغيل قارئ الباركود بالكاميرا (Html5Qrcode)
async function startScanner() {
  if (typeof Html5Qrcode === 'undefined') {
    showToast('مكتبة الكاميرا قيد التحميل، يرجى الانتظار...');
    return;
  }

  try {
    const html5QrCode = new Html5Qrcode('scanner-viewport');
    AppState.scanner = html5QrCode;

    document.getElementById('scanner-placeholder').style.display = 'none';
    document.getElementById('btn-start-camera').style.display = 'none';
    document.getElementById('btn-stop-camera').style.display = 'flex';
    AppState.isScanning = true;

    const qrCodeSuccessCallback = (decodedText, decodedResult) => {
      console.log('Barcode scanned:', decodedText);
      handleScannedBarcode(decodedText);
    };

    const config = { fps: 15, qrbox: { width: 260, height: 160 }, aspectRatio: 1.5 };
    await html5QrCode.start({ facingMode: 'environment' }, config, qrCodeSuccessCallback);
  } catch (err) {
    console.error('Camera start error:', err);
    showToast('تعذر تشغيل الكاميرا: تأكد من منح الإذن للمتصفح');
    stopScanner();
  }
}

async function stopScanner() {
  if (AppState.scanner && AppState.isScanning) {
    try {
      await AppState.scanner.stop();
      AppState.scanner.clear();
    } catch (e) {}
  }
  AppState.isScanning = false;
  AppState.scanner = null;

  const ph = document.getElementById('scanner-placeholder');
  if (ph) ph.style.display = 'block';
  const btnStart = document.getElementById('btn-start-camera');
  if (btnStart) btnStart.style.display = 'flex';
  const btnStop = document.getElementById('btn-stop-camera');
  if (btnStop) btnStop.style.display = 'none';
}

function handleScannedBarcode(barcode) {
  barcode = barcode.trim();
  const products = AppState.data?.Products || [];
  const found = products.find(p => p.Barcode === barcode);

  if (found) {
    showProductResult(found);
  } else {
    playBeep();
    showToast(`الباركود غير مسجل: ${barcode}`);
    document.getElementById('product-search-input').value = barcode;
    renderProductsCatalog();
  }
}

// 9. رندرة الإعدادات
function renderSettings() {
  const d = AppState.data || {};
  document.getElementById('settings-sync-time').textContent = d.StoreInfo?.LastSyncTimestamp || 'غير محدد';
  document.getElementById('settings-products-count').textContent = d.Products?.length || 0;
  document.getElementById('settings-debtors-count').textContent = d.CustomerDebts?.length || 0;
}

// 10. إعداد الأحداث والتبويبات
function setupEventListeners() {
  // شريط التبويبات السفلي
  document.querySelectorAll('.nav-item').forEach(btn => {
    btn.addEventListener('click', () => {
      const target = btn.dataset.tab;
      document.querySelectorAll('.nav-item').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-screen').forEach(s => s.classList.remove('active'));

      btn.classList.add('active');
      const targetScreen = document.getElementById('tab-' + target);
      if (targetScreen) targetScreen.classList.add('active');

      if (AppState.activeTab === 'scanner' && target !== 'scanner') {
        stopScanner();
      }
      AppState.activeTab = target;
      window.scrollTo({ top: 0, behavior: 'smooth' });
    });
  });

  // أزرار الكاميرا
  document.getElementById('btn-start-camera')?.addEventListener('click', startScanner);
  document.getElementById('btn-stop-camera')?.addEventListener('click', stopScanner);

  // إظهار/إخفاء سعر الشراء والأرباح
  document.getElementById('btn-toggle-profit')?.addEventListener('click', () => {
    AppState.profitUnlocked = !AppState.profitUnlocked;
    const drawer = document.getElementById('res-profit-drawer');
    const btn = document.getElementById('btn-toggle-profit');
    if (AppState.profitUnlocked) {
      drawer.classList.add('visible');
      btn.textContent = '🔓 إخفاء رأس المال';
    } else {
      drawer.classList.remove('visible');
      btn.textContent = '🔒 إظهار سعر الشراء والربح';
    }
  });

  // البحث في الفواتير
  document.getElementById('invoice-search-input')?.addEventListener('input', () => {
    renderInvoicesList();
  });

  // البحث في الديون
  document.getElementById('debt-search-input')?.addEventListener('input', () => {
    renderDebts();
  });

  // أزرار تصفية الديون (Filter chips)
  document.querySelectorAll('.chip').forEach(ch => {
    ch.addEventListener('click', () => {
      document.querySelectorAll('.chip').forEach(c => c.classList.remove('active'));
      ch.classList.add('active');
      AppState.activeDebtFilter = ch.dataset.filter;
      renderDebts();
    });
  });

  // البحث في المنتجات
  document.getElementById('product-search-input')?.addEventListener('input', (e) => {
    const query = e.target.value.trim();
    if (query.length > 0) {
      const found = (AppState.data?.Products || []).find(p => p.Barcode === query);
      if (found) {
        showProductResult(found);
      }
    }
    renderProductsCatalog();
  });

  // اختيار ملف المزامنة من Google Drive يدpipeاً
  const fileInput = document.getElementById('sync-file-input');
  const openFileBtn = document.getElementById('btn-open-file');
  const settingsPickBtn = document.getElementById('btn-settings-pick-file');

  const triggerFilePick = () => fileInput?.click();
  openFileBtn?.addEventListener('click', triggerFilePick);
  settingsPickBtn?.addEventListener('click', triggerFilePick);

  fileInput?.addEventListener('change', (e) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = (event) => {
      try {
        let content = event.target.result;
        if (content.startsWith('window.LMIMA_SYNC_DATA =')) {
          content = content.replace(/^window\.LMIMA_SYNC_DATA\s*=\s*/, '').replace(/;$/, '');
        }
        const parsed = JSON.parse(content);
        applySyncData(parsed, true);
        showToast('تم استيراد ملف المزامنة بنجاح! 🎉');
      } catch (err) {
        alert('تعذر قراءة الملف. تأكد من اختيار ملف lmima_mobile_sync.json أو lmima_mobile_sync.js الصحيح.');
      }
    };
    reader.readAsText(file);
  });

  // زر التحديث اليدوي
  const reloadData = async () => {
    showToast('جاري تحديث البيانات...');
    await initData();
    showToast('تم التحديث بنجاح');
  };

  document.getElementById('btn-refresh-sync')?.addEventListener('click', reloadData);
  document.getElementById('btn-settings-reload-data')?.addEventListener('click', reloadData);
}

// 11. تسجيل Service Worker للعمل أوفلاين
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('./sw.js').then((reg) => {
      console.log('[SW] Registered successfully:', reg.scope);
    }).catch((err) => {
      console.log('[SW] Registration failed:', err);
    });
  });
}

// بدء تشغيل التطبيق
document.addEventListener('DOMContentLoaded', () => {
  setupEventListeners();
  initData();
});