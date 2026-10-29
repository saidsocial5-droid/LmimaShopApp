// ==========================================================================

// LMIMA SHOP - Mobile Companion & Full POS Application

// Version: 3.3 - Full Point of Sale, Products, Customers, Suppliers & Live 2-Way Sync

// ==========================================================================

const AppState = {

  data: null,

  cart: [],

  cartCustomer: 'زبون نقدي عابر',

  cartPaymentType: 'cash', // 'cash' or 'credit'

  activeTab: 'pos',

  currentProductImage: null,

  signalRConnection: null,

  isSignalRConnected: false,

  posSearch: '',

  posCategory: 'all',

  posScanner: null,

  posIsScanning: false,

  posLastBarcode: null,

  posLastScanTime: 0,

  posScanLocked: false,

  modalScanner: null,

  modalIsScanning: false,

  productsScanner: null,

  productsIsScanning: false,

  productsLastBarcode: null,

  productsLastScanTime: 0,

  productsScanLocked: false,

  productsSearch: '',

  productsCategory: 'all',

  productsPage: 1,

  productsPageSize: 40,

  customersSearch: '',

  customersFilter: 'all',

  suppliersSearch: '',

  suppliersFilter: 'all',

  expandedInvoices: new Set(),

  expandedDebts: new Set(),

  serverIp: (window.location.port === '8080' && window.location.hostname && window.location.hostname !== 'localhost')

    ? `${window.location.hostname}:${window.location.port}`

    : (localStorage.getItem('lmima_server_ip') || '192.168.11.123:8080'),

  autoSyncEnabled: localStorage.getItem('lmima_auto_sync') !== 'false',

  autoSyncTimer: null,

  isSyncing: false,

  offlineQueue: JSON.parse(localStorage.getItem('lmima_offline_queue') || '[]'),

  lastCompletedSale: null

};

// ==========================================================================

// 1. التنبيهات الصوتية وتنسيق العملة المغربية (الدرهم والريال)

// ==========================================================================

function playBeep() {

  try {

    const audioCtx = new (window.AudioContext || window.webkitAudioContext)();

    const osc = audioCtx.createOscillator();

    const gain = audioCtx.createGain();

    osc.type = 'sine';

    osc.frequency.setValueAtTime(900, audioCtx.currentTime);

    gain.gain.setValueAtTime(0.2, audioCtx.currentTime);

    gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.12);

    osc.connect(gain);

    gain.connect(audioCtx.destination);

    osc.start();

    osc.stop(audioCtx.currentTime + 0.12);

  } catch (e) {}

}

function playSuccessSound() {

  try {

    const audioCtx = new (window.AudioContext || window.webkitAudioContext)();

    const osc = audioCtx.createOscillator();

    const gain = audioCtx.createGain();

    osc.type = 'triangle';

    osc.frequency.setValueAtTime(523.25, audioCtx.currentTime); // C5

    osc.frequency.setValueAtTime(659.25, audioCtx.currentTime + 0.1); // E5

    osc.frequency.setValueAtTime(783.99, audioCtx.currentTime + 0.2); // G5

    gain.gain.setValueAtTime(0.25, audioCtx.currentTime);

    gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.35);

    osc.connect(gain);

    gain.connect(audioCtx.destination);

    osc.start();

    osc.stop(audioCtx.currentTime + 0.35);

  } catch (e) {}

}

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

function showToast(msg) {

  const toast = document.getElementById('toast');

  if (!toast) return;

  toast.textContent = msg;

  toast.classList.add('show');

  setTimeout(() => toast.classList.remove('show'), 2800);

}

// ==========================================================================

// 2. إدارة النوافذ المنبثقة (Modals)

// ==========================================================================

function openModal(id) {

  const modal = document.getElementById(id);

  if (modal) {

    modal.classList.add('open');

    document.body.style.overflow = 'hidden';

  }

}

function closeModal(id) {

  const modal = document.getElementById(id);

  if (modal) {

    modal.classList.remove('open');

    document.body.style.overflow = '';

  }

  if (id === 'modal-product' && AppState.modalScanner) {

    stopModalScanner();

  }

}

// ==========================================================================

// 3. إدارة سلة المشتريات لنقطة البيع (POS Cart)

// ==========================================================================

function addToCart(product, qty = 1) {

  if (!product) return;

  const existing = AppState.cart.find(item => item.productId === product.Id);

  const stock = parseFloat(product.StockQuantity) || 9999;

  if (existing) {

    if (existing.quantity + qty > stock) {

      showToast(`⚠️ الكمية المتاحة في المخزن هي: ${stock}`);

    }

    existing.quantity += qty;

    existing.total = existing.quantity * existing.unitPrice;

  } else {

    AppState.cart.push({

      productId: product.Id,

      productName: product.Name,

      barcode: product.Barcode,

      unitPrice: parseFloat(product.Price) || 0,

      quantity: qty,

      total: (parseFloat(product.Price) || 0) * qty

    });

  }

  playBeep();

  showToast(`➕ أضيف للسلة: ${product.Name}`);

  updateCartUI();

}

function updateCartItemQty(productId, delta) {

  const item = AppState.cart.find(i => i.productId === productId);

  if (!item) return;

  item.quantity += delta;

  if (item.quantity <= 0) {

    removeFromCart(productId);

    return;

  }

  item.total = item.quantity * item.unitPrice;

  updateCartUI();

}

function removeFromCart(productId) {

  AppState.cart = AppState.cart.filter(i => i.productId !== productId);

  updateCartUI();

}

function clearCart() {

  if (AppState.cart.length === 0) return;

  AppState.cart = [];

  updateCartUI();

  showToast('تم إفراغ السلة');

}

function getCartTotals() {

  let subtotal = 0;

  let itemsCount = 0;

  AppState.cart.forEach(item => {

    subtotal += item.total;

    itemsCount += item.quantity;

  });

  return {

    subtotal,

    itemsCount,

    rial: Math.round(subtotal * 20)

  };

}

function updateCartUI() {

  const totals = getCartTotals();

  const bar = document.getElementById('floating-cart-bar');

  // تحديث شريط السلة العائم

  if (totals.itemsCount > 0) {

    if (bar) bar.style.display = 'flex';

    const cEl = document.getElementById('cart-bar-count');

    const dhEl = document.getElementById('cart-bar-total-dh');

    const rialEl = document.getElementById('cart-bar-total-rial');

    if (cEl) cEl.textContent = totals.itemsCount;

    if (dhEl) dhEl.textContent = formatDH(totals.subtotal);

    if (rialEl) rialEl.textContent = formatRial(totals.subtotal);

  } else {

    if (bar) bar.style.display = 'none';

    closeModal('modal-cart');

  }

  // تحديث محتوى نافذة السلة إذا كانت مفتوحة

  renderCartModalContent();

}

function renderCartModalContent() {

  const totals = getCartTotals();

  const itemsContainer = document.getElementById('cart-items-list');

  const countBadge = document.getElementById('modal-cart-items-count');

  const subtotalEl = document.getElementById('cart-subtotal-dh');

  const grandDhEl = document.getElementById('cart-grand-dh');

  const grandRialEl = document.getElementById('cart-grand-rial');

  if (countBadge) countBadge.textContent = totals.itemsCount;

  if (subtotalEl) subtotalEl.textContent = formatDH(totals.subtotal);

  if (grandDhEl) grandDhEl.textContent = formatDH(totals.subtotal);

  if (grandRialEl) grandRialEl.textContent = formatRial(totals.subtotal);

  // تحديث قائمة الزبناء في السلة

  populateCartCustomerSelect();

  if (!itemsContainer) return;

  if (AppState.cart.length === 0) {

    itemsContainer.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 30px;">السلة فارغة. اختر سلعاً من القائمة أو امسح الباركود</div>';

    return;

  }

  itemsContainer.innerHTML = AppState.cart.map(item => `

    <div class="cart-item-row">

      <div style="flex: 1;">

        <div class="cart-item-name">${item.productName}</div>

        <div class="cart-item-unit">${formatDH(item.unitPrice)} للواحدة (${formatRial(item.unitPrice)})</div>

      </div>

      <div class="cart-qty-ctrl">

        <button class="cart-qty-btn" onclick="updateCartItemQty(${item.productId}, -1)">−</button>

        <span class="cart-qty-val">${item.quantity}</span>

        <button class="cart-qty-btn" onclick="updateCartItemQty(${item.productId}, 1)">+</button>

      </div>

      <div class="cart-item-price-box">

        <div style="font-weight: 800; color: var(--primary-accent); font-size: 14px;">${formatDH(item.total)}</div>

        <div style="font-size: 10px; color: #b45309;">${formatRial(item.total)}</div>

      </div>

      <button onclick="removeFromCart(${item.productId})" style="background:none; border:none; color:#ef4444; font-size:16px; margin-right:8px; cursor:pointer;" title="حذف">✕</button>

    </div>

  `).join('');

}

function populateCartCustomerSelect() {

  const sel = document.getElementById('cart-customer-select');

  if (!sel) return;

  const currentVal = sel.value || AppState.cartCustomer;

  const customers = AppState.data?.Customers || [];

  let html = '<option value="زبون نقدي عابر">زبون نقدي عابر (افتراضي)</option>';

  customers.forEach(c => {

    const isSel = (c.FullName === currentVal) ? 'selected' : '';

    const debtStr = c.DueBalance > 0 ? ` [عليه: ${c.DueBalance.toFixed(0)} د.م.]` : '';

    html += `<option value="${c.FullName}" ${isSel} data-allow-credit="${c.AllowCredit}" data-due="${c.DueBalance}" data-limit="${c.CreditLimit}">${c.FullName}${debtStr}</option>`;

  });

  sel.innerHTML = html;

  checkCartCustomerPaymentOptions();

}

function checkCartCustomerPaymentOptions() {

  const sel = document.getElementById('cart-customer-select');

  const payBox = document.getElementById('cart-payment-type-box');

  const creditNotice = document.getElementById('cart-credit-warning');

  if (!sel || !payBox) return;

  const custName = sel.value;

  AppState.cartCustomer = custName;

  if (custName === 'زبون نقدي عابر' || custName === 'زبون نقدي') {

    payBox.style.display = 'none';

    AppState.cartPaymentType = 'cash';

    if (creditNotice) creditNotice.style.display = 'none';

  } else {

    payBox.style.display = 'block';

    if (creditNotice) creditNotice.style.display = (AppState.cartPaymentType === 'credit') ? 'block' : 'none';

  }

}

// ==========================================================================

// 4. إنهاء ودفع الفاتورة (Checkout & Change Calculation)

// ==========================================================================

let checkoutPaidValue = '';

function openCheckoutModal() {

  const totals = getCartTotals();

  if (totals.itemsCount === 0) {

    showToast('⚠️ السلة فارغة! اختر سلعاً للبيع أولاً');

    return;

  }

  closeModal('modal-cart');

  openModal('modal-checkout');

  const dueDh = document.getElementById('checkout-due-dh');

  const dueRial = document.getElementById('checkout-due-rial');

  const custBadge = document.getElementById('checkout-cust-badge');

  const cashSec = document.getElementById('checkout-cash-section');

  const creditSec = document.getElementById('checkout-credit-section');

  if (dueDh) dueDh.textContent = formatDH(totals.subtotal);

  if (dueRial) dueRial.textContent = formatRial(totals.subtotal);

  if (custBadge) custBadge.textContent = `الزبون: ${AppState.cartCustomer} • طريقة الدفع: ${AppState.cartPaymentType === 'credit' ? 'كريدي (دين)' : 'كاش نقداً'}`;

  if (AppState.cartPaymentType === 'credit') {

    if (cashSec) cashSec.style.display = 'none';

    if (creditSec) creditSec.style.display = 'block';

    const custObj = (AppState.data?.Customers || []).find(c => c.FullName === AppState.cartCustomer) || {};

    const oldDebt = parseFloat(custObj.DueBalance) || 0;

    const newDebt = oldDebt + totals.subtotal;

    const setT = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };

    setT('credit-card-cust-name', AppState.cartCustomer);

    setT('credit-card-old-debt', formatDH(oldDebt));

    setT('credit-card-sale-amt', '+' + formatDH(totals.subtotal));

    setT('credit-card-new-debt', formatDH(newDebt) + ' (' + formatRial(newDebt) + ')');

  } else {

    if (cashSec) cashSec.style.display = 'block';

    if (creditSec) creditSec.style.display = 'none';

    // افتراض الدفع تماماً بالمبلغ المستحق

    checkoutPaidValue = totals.subtotal.toFixed(2);

    const paidInp = document.getElementById('checkout-paid-input');

    if (paidInp) paidInp.value = checkoutPaidValue;

    updateCheckoutChange();

  }

}

function handleCheckoutKeypad(key) {

  const totals = getCartTotals();

  const paidInp = document.getElementById('checkout-paid-input');

  if (!paidInp) return;

  if (key === 'exact') {

    checkoutPaidValue = totals.subtotal.toFixed(2);

  } else if (key === 'C') {

    checkoutPaidValue = '';

  } else if (key === 'back') {

    checkoutPaidValue = checkoutPaidValue.slice(0, -1);

  } else if (key === '.') {

    if (!checkoutPaidValue.includes('.')) {

      checkoutPaidValue = (checkoutPaidValue === '') ? '0.' : checkoutPaidValue + '.';

    }

  } else {

    checkoutPaidValue += key;

  }

  paidInp.value = checkoutPaidValue || '0.00';

  updateCheckoutChange();

}

function handleQuickCashAdd(amount) {

  const totals = getCartTotals();

  let current = parseFloat(checkoutPaidValue) || 0;

  if (amount === 'exact') {

    checkoutPaidValue = totals.subtotal.toFixed(2);

  } else {

    const add = parseFloat(amount) || 0;

    checkoutPaidValue = (current + add).toFixed(2);

  }

  const paidInp = document.getElementById('checkout-paid-input');

  if (paidInp) paidInp.value = checkoutPaidValue;

  updateCheckoutChange();

}

function updateCheckoutChange() {

  const totals = getCartTotals();

  const paid = parseFloat(checkoutPaidValue) || 0;

  const change = Math.max(0, paid - totals.subtotal);

  const banner = document.getElementById('checkout-change-banner');

  const chDh = document.getElementById('checkout-change-dh');

  const chRial = document.getElementById('checkout-change-rial');

  if (chDh) chDh.textContent = formatDH(change);

  if (chRial) chRial.textContent = formatRial(change);

  if (banner) {

    if (paid < totals.subtotal) {

      banner.style.background = '#fef2f2';

      banner.style.borderColor = '#fecaca';

      banner.style.color = '#b91c1c';

      if (chDh) chDh.textContent = `متبقي: ${formatDH(totals.subtotal - paid)}`;

    } else {

      banner.style.background = '#ecfdf5';

      banner.style.borderColor = '#a7f3d0';

      banner.style.color = '#065f46';

    }

  }

}

async function confirmCheckout() {

  const totals = getCartTotals();

  if (totals.itemsCount === 0) return;

  const isCredit = (AppState.cartPaymentType === 'credit');

  const paid = isCredit ? 0 : (parseFloat(checkoutPaidValue) || totals.subtotal);

  const debt = isCredit ? totals.subtotal : Math.max(0, totals.subtotal - paid);

  const salePayload = {

    // معرف فريد للعملية: يمنع تسجيل نفس البيع مرتين إذا انقطع الاتصال أثناء الإرسال

    opId: generateUUID(),

    saleDate: new Date().toISOString(),

    cashierName: 'تطبيق الهاتف',

    customerName: AppState.cartCustomer,

    totalAmount: totals.subtotal,

    paidAmount: paid,

    debtAmount: debt,

    items: AppState.cart.map(item => ({

      productId: item.productId,

      productName: item.productName,

      barcode: item.barcode,

      quantity: item.quantity,

      unitPrice: item.unitPrice,

      total: item.total

    }))

  };

  showToast('جاري تسجيل الفاتورة وتحديث المخزون...');

  // محاولة الإرسال إلى الخادم

  const res = await postToServer('/api/pos/checkout', salePayload, 10000);

  // رفض من الحاسوب (مخزون غير كاف، تجاوز الكريدي...): لا نسجل البيع ونبقي السلة كما هي

  if (res && !res.success && res.rejected) {

    alert('❌ لم يتم تسجيل البيع:\n\n' + (res.error || 'رفض الحاسوب العملية'));

    return;

  }

  closeModal('modal-checkout');

  let invoiceId = Math.floor(1000 + Math.random() * 9000);

  if (res && res.success) {

    invoiceId = res.invoiceId || invoiceId;

    showToast('تم تسجيل البيع والمزامنة مع الحاسوب بنجاح! ⚡');

  } else {

    // انقطاع الاتصال أو هاتف غير مقترن: حفظ في طابور عدم الاتصال بنفس معرف العملية

    await queueOfflineAction('pos_checkout', salePayload);

    showToast('تم حفظ البيع محلياً وسيتم رفعه للحاسوب تلقائياً 📲');

  }

  // تحديث محلي فوري للبيانات لتبدو النتيجة فورية للمستخدم

  applyLocalSaleUpdate(salePayload, invoiceId);

  // إعداد وعرض الفاتورة

  AppState.lastCompletedSale = {

    invoiceId,

    date: new Date().toLocaleString('fr-FR'),

    customer: AppState.cartCustomer,

    method: isCredit ? 'كريدي (دين)' : 'كاش نقداً',

    items: [...AppState.cart],

    total: totals.subtotal,

    paid: paid,

    change: Math.max(0, paid - totals.subtotal)

  };

  playSuccessSound();

  clearCart();

  renderReceiptModal(AppState.lastCompletedSale);

  openModal('modal-receipt');

}

function applyLocalSaleUpdate(sale, invoiceId) {

  if (!AppState.data) return;

  // 1. تحديث ملخص اليوم

  if (!AppState.data.TodaySummary) AppState.data.TodaySummary = {};

  const s = AppState.data.TodaySummary;

  s.TotalSales = (s.TotalSales || 0) + sale.totalAmount;

  s.TotalSalesRial = s.TotalSales * 20;

  s.InvoicesCount = (s.InvoicesCount || 0) + 1;

  if (sale.debtAmount > 0) {

    s.CreditSales = (s.CreditSales || 0) + sale.totalAmount;

  } else {

    s.CashSales = (s.CashSales || 0) + sale.totalAmount;

  }

  // 2. تحديث مخزون المنتجات

  sale.items.forEach(it => {

    const prod = (AppState.data.Products || []).find(p => p.Id === it.productId);

    if (prod) {

      prod.StockQuantity = Math.max(0, (prod.StockQuantity || 0) - it.quantity);

    }

  });

  // 3. تحديث دين الزبون

  if (sale.debtAmount > 0) {

    const cust = (AppState.data.Customers || []).find(c => c.FullName === sale.customerName);

    if (cust) {

      cust.DueBalance = (cust.DueBalance || 0) + sale.debtAmount;

      cust.DueBalanceRial = cust.DueBalance * 20;

    }

  }

  // 4. إضافة الفاتورة لقائمة الفواتير الحديثة

  if (!AppState.data.RecentInvoices) AppState.data.RecentInvoices = [];

  AppState.data.RecentInvoices.unshift({

    InvoiceId: invoiceId,

    Date: new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }),

    Cashier: 'تطبيق الهاتف',

    Customer: sale.customerName,

    TotalAmount: sale.totalAmount,

    TotalAmountRial: sale.totalAmount * 20,

    PaymentMethod: sale.debtAmount > 0 ? 'كريدي' : 'كاش',

    ItemsCount: sale.items.length,

    Items: sale.items.map(i => ({

      ProductName: i.productName,

      Quantity: i.quantity,

      UnitPrice: i.unitPrice,

      SubTotal: i.total

    }))

  });

  saveDataLocally();

  renderPOSGrid();

  renderProductsTab();

  renderCustomersTab();

  renderDashboard();

}

function renderReceiptModal(sale) {

  if (!sale) return;

  const setT = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };

  setT('rec-store-name', AppState.data?.StoreInfo?.StoreName || 'متجر LMIMA SHOP');

  setT('rec-store-phone', AppState.data?.StoreInfo?.StorePhone || '');

  setT('rec-inv-id', '#' + sale.invoiceId);

  setT('rec-inv-date', sale.date);

  setT('rec-inv-customer', sale.customer);

  setT('rec-inv-method', sale.method);

  setT('rec-total-dh', formatDH(sale.total));

  setT('rec-total-rial', formatRial(sale.total));

  setT('rec-paid-dh', formatDH(sale.paid));

  setT('rec-change-dh', formatDH(sale.change));

  const itemsContainer = document.getElementById('rec-items-list');

  if (itemsContainer) {

    itemsContainer.innerHTML = sale.items.map(item => `

      <div class="rec-item-line">

        <span>${item.productName} × ${item.quantity}</span>

        <span>${formatDH(item.total)}</span>

      </div>

    `).join('');

  }

}

function shareReceiptViaWhatsApp() {

  const sale = AppState.lastCompletedSale;

  if (!sale) return;

  const storeName = AppState.data?.StoreInfo?.StoreName || 'متجر LMIMA SHOP';

  let text = `🧾 *فاتورة مشتريات - ${storeName}*\n`;

  text += `رقم الفاتورة: #${sale.invoiceId}\n`;

  text += `التاريخ: ${sale.date}\n`;

  text += `الزبون: ${sale.customer}\n`;

  text += `طريقة الأداء: ${sale.method}\n`;

  text += `---------------------------\n`;

  sale.items.forEach(it => {

    text += `• ${it.productName} (×${it.quantity}) = ${it.total.toFixed(2)} د.م.\n`;

  });

  text += `---------------------------\n`;

  text += `💰 *المجموع:* ${sale.total.toFixed(2)} د.م. (${Math.round(sale.total * 20).toLocaleString()} ريال)\n`;

  if (sale.paid > 0) {

    text += `💵 المدفوع: ${sale.paid.toFixed(2)} د.م.\n`;

    text += `🪙 الباقي: ${sale.change.toFixed(2)} د.م.\n`;

  }

  text += `\nشكراً لزيارتكم ونتمنى لكم يوماً طيباً!`;

  const custObj = (AppState.data?.Customers || []).find(c => c.FullName === sale.customer);

  const cleanPhone = custObj ? cleanMoroccanPhone(custObj.Phone) : '';

  const waUrl = cleanPhone ? `https://wa.me/${cleanPhone}?text=${encodeURIComponent(text)}` : `https://wa.me/?text=${encodeURIComponent(text)}`;

  window.open(waUrl, '_blank');

}

// ==========================================================================

// 5. رندرة واجهة نقطة البيع (POS Grid & Search)

// ==========================================================================

function renderPOSGrid() {

  const container = document.getElementById('pos-products-grid');

  if (!container) return;

  const products = AppState.data?.Products || [];

  const search = (AppState.posSearch || '').trim().toLowerCase();

  const selectedCat = AppState.posCategory;

  renderPOSCategoryChips(products);

  const filtered = products.filter(p => {

    if (selectedCat !== 'all' && p.Category !== selectedCat) return false;

    if (!search) return true;

    return (p.Name && p.Name.toLowerCase().includes(search)) ||

           (p.Barcode && p.Barcode.toLowerCase().includes(search)) ||

           (p.Category && p.Category.toLowerCase().includes(search));

  });

  const countBadge = document.getElementById('pos-items-count-badge');

  if (countBadge) countBadge.textContent = `${filtered.length} منتج متاح`;

  if (filtered.length === 0) {

    container.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: var(--text-muted); padding: 30px;">لا توجد سلع مطابقة للبحث أو الفئة</div>';

    return;

  }

  container.innerHTML = filtered.map(p => {

    const stock = parseFloat(p.StockQuantity) || 0;

    let stockClass = 'stock-in';

    let stockText = `${stock} متاح`;

    if (stock <= 0) {

      stockClass = 'stock-out';

      stockText = 'نفذ';

    } else if (stock <= 5) {

      stockClass = 'stock-low';

      stockText = `${stock} فقط`;

    }

    return `

      <div class="pos-card" onclick="quickAddProductFromCard('${p.Barcode}')">

        <div>

          <div class="pos-card-title">${p.Name}</div>

          <div class="pos-card-cat">${p.Category || 'عام'}</div>

        </div>

        <div>

          <div class="pos-card-price-dh">${formatDH(p.Price)}</div>

          <div class="pos-card-price-rial">${formatRial(p.Price)}</div>

          <div class="pos-card-footer">

            <span class="stock-badge ${stockClass}">${stockText}</span>

            <button class="pos-add-btn" onclick="event.stopPropagation(); quickAddProductFromCard('${p.Barcode}')">➕</button>

          </div>

        </div>

      </div>

    `;

  }).join('');

}

function renderPOSCategoryChips(products) {

  const chipsContainer = document.getElementById('pos-category-chips');

  if (!chipsContainer) return;

  const categories = {};

  products.forEach(p => {

    const cat = p.Category || 'عام';

    categories[cat] = (categories[cat] || 0) + 1;

  });

  const catNames = Object.keys(categories).sort();

  const allActive = (AppState.posCategory === 'all') ? 'active' : '';

  let html = `<button class="chip ${allActive}" data-cat="all">الكل (${products.length})</button>`;

  catNames.forEach(cat => {

    const active = (AppState.posCategory === cat) ? 'active' : '';

    html += `<button class="chip ${active}" data-cat="${cat}">${cat} (${categories[cat]})</button>`;

  });

  chipsContainer.innerHTML = html;

  chipsContainer.querySelectorAll('.chip').forEach(btn => {

    btn.addEventListener('click', () => {

      AppState.posCategory = btn.dataset.cat;

      renderPOSGrid();

    });

  });

}

function quickAddProductFromCard(barcode) {

  const p = (AppState.data?.Products || []).find(item => item.Barcode === barcode);

  if (p) {

    addToCart(p, 1);

  }

}

// ==========================================================================

// 6. كاميرا نقطة البيع للمسح السريع (POS Scanner)

// ==========================================================================

async function togglePOSCamera() {

  const container = document.getElementById('pos-camera-container');

  if (!container) return;

  if (AppState.posIsScanning) {

    stopPOSCamera();

  } else {

    container.style.display = 'block';

    startPOSCamera();

  }

}

async function startPOSCamera() {

  if (typeof Html5Qrcode === 'undefined') {

    showToast('جاري تحميل مشغل الكاميرا...');

    return;

  }

  if (AppState.productsIsScanning) {

    await stopProductsCamera();

  }

  if (AppState.modalIsScanning) {

    await stopProductModalCamera();

  }

  try {

    const html5QrCode = new Html5Qrcode('pos-scanner-viewport');

    AppState.posScanner = html5QrCode;

    AppState.posIsScanning = true;

    AppState.posScanLocked = false;

    const qrCodeSuccessCallback = (decodedText) => {

      handlePOSScannedBarcode(decodedText);

    };

    const config = { fps: 12, qrbox: { width: 260, height: 160 }, aspectRatio: 1.5 };

    await html5QrCode.start({ facingMode: 'environment' }, config, qrCodeSuccessCallback);

  } catch (err) {

    console.error('Camera error:', err);

    showToast('تعذر تشغيل الكاميرا. يرجى منح الإذن');

    stopPOSCamera();

  }

}

async function stopPOSCamera() {

  const container = document.getElementById('pos-camera-container');

  if (container) container.style.display = 'none';

  if (AppState.posScanner && AppState.posIsScanning) {

    try {

      await AppState.posScanner.stop();

      AppState.posScanner.clear();

    } catch (e) {}

  }

  AppState.posIsScanning = false;

  AppState.posScanner = null;

  AppState.posScanLocked = false;

}

async function handlePOSScannedBarcode(barcode) {

  barcode = (barcode || '').trim();

  if (!barcode) return;

  const now = Date.now();

  // 1. فحص القفل الحصري لمنع التكرار اللحظي الناتج عن سرعة الكاميرا
  if (AppState.posScanLocked) return;

  // 2. منع تكرار نفس الباركود قبل مرور ثانيتين كاملتين (مسحة واحدة لكل ضغطة/مرور)
  if (AppState.posLastBarcode === barcode && (now - AppState.posLastScanTime) < 2000) {
    return;
  }

  // 3. مسافة أمان لا تقل عن 800 مللي ثانية بين أي منتوجين مختلفين
  if ((now - AppState.posLastScanTime) < 800) {
    return;
  }

  // قفل الماسح حصرياً لتنفيذ إرسال واحد مؤكد
  AppState.posScanLocked = true;
  AppState.posLastBarcode = barcode;
  AppState.posLastScanTime = now;

  playBeep();

  if (navigator.vibrate) {
    try { navigator.vibrate(80); } catch(e){}
  }

  // تأثير وميض أخضر لتأكيد التقاط المسحة بنجاح
  const vp = document.getElementById('pos-scanner-viewport');
  if (vp) {
    vp.style.outline = '4px solid #10b981';
    vp.style.boxShadow = '0 0 15px rgba(16, 185, 129, 0.8)';
    setTimeout(() => {
      if (vp) {
        vp.style.outline = 'none';
        vp.style.boxShadow = 'none';
      }
    }, 450);
  }

  // 1. محاولة الإرسال لكاشير سطح المكتب أولاً (عبر SignalR أو HTTP)

  let sentToCashier = false;

  if (AppState.isSignalRConnected && AppState.signalRConnection) {

    try {

      await AppState.signalRConnection.invoke("SendBarcode", barcode);

      sentToCashier = true;

    } catch (err) {

      console.warn('SignalR send failed, trying HTTP fallback:', err);

    }

  }

  if (!sentToCashier && AppState.serverIp) {

    try {

      const res = await postToServer('/api/barcode', { barcode: barcode });

      if (res && res.success) {

        sentToCashier = true;

      }

    } catch (err) {

      console.warn('HTTP barcode send failed:', err);

    }

  }

  if (sentToCashier) {

    showToast(`✅ مسحة واحدة: تم إرسال (${barcode}) للكاشير 🚀`);

    playSuccessSound();

    setTimeout(() => {
      AppState.posScanLocked = false;
    }, 1200);

    return;

  }

  // 2. إذا تعذر الإرسال للكاشير (وضع محلي / غير متصل بالحاسوب)، التعامل معه في سلة الهاتف

  const products = AppState.data?.Products || [];

  const found = products.find(p => p.Barcode === barcode);

  if (found) {

    addToCart(found, 1);

    showToast(`✅ مسحة واحدة: أضيف للسلة (${found.Name})`);

  } else {

    showToast(`الباركود غير مسجل: ${barcode}`);

    // اقتراح إضافة المنتج

    if (confirm(`المنتج ذو الباركود (${barcode}) غير موجود. هل تريد إضافته الآن؟`)) {

      stopPOSCamera();

      openAddProductModal({ Barcode: barcode });

    }

  }

  setTimeout(() => {
    AppState.posScanLocked = false;
  }, 1200);

}

// ==========================================================================
// 7.0 كاميرا مسح باركود المنتج في صفحة المنتجات (بحث سريع وعرض)
// ==========================================================================

async function toggleProductsCamera() {
  const container = document.getElementById('products-camera-container');
  if (!container) return;

  if (AppState.productsIsScanning) {
    await stopProductsCamera();
  } else {
    if (AppState.posIsScanning) await stopPOSCamera();
    if (AppState.modalIsScanning) await stopProductModalCamera();

    container.style.display = 'block';
    await startProductsCamera();
  }
}

async function startProductsCamera() {
  if (typeof Html5Qrcode === 'undefined') {
    showToast('جاري تحميل مشغل الكاميرا...');
    return;
  }
  const viewport = document.getElementById('products-scanner-viewport');
  if (!viewport) return;

  try {
    const html5QrCode = new Html5Qrcode('products-scanner-viewport');
    AppState.productsScanner = html5QrCode;
    AppState.productsIsScanning = true;
    AppState.productsScanLocked = false;

    const qrCodeSuccessCallback = (decodedText) => {
      handleProductsScannedBarcode(decodedText);
    };
    const config = { fps: 12, qrbox: { width: 260, height: 160 }, aspectRatio: 1.5 };
    await html5QrCode.start({ facingMode: 'environment' }, config, qrCodeSuccessCallback);
  } catch (err) {
    console.error('Products Camera error:', err);
    showToast('تعذر تشغيل الكاميرا. يرجى منح الإذن للتطبيق');
    stopProductsCamera();
  }
}

async function stopProductsCamera() {
  const container = document.getElementById('products-camera-container');
  if (container) container.style.display = 'none';

  if (AppState.productsScanner && AppState.productsIsScanning) {
    try {
      await AppState.productsScanner.stop();
      AppState.productsScanner.clear();
    } catch (e) {}
  }
  AppState.productsIsScanning = false;
  AppState.productsScanner = null;
  AppState.productsScanLocked = false;
}

async function handleProductsScannedBarcode(barcode) {
  barcode = (barcode || '').trim();
  if (!barcode) return;

  const now = Date.now();
  if (AppState.productsScanLocked) return;
  if (AppState.productsLastBarcode === barcode && (now - AppState.productsLastScanTime) < 2000) return;
  if ((now - AppState.productsLastScanTime) < 800) return;

  AppState.productsScanLocked = true;
  AppState.productsLastBarcode = barcode;
  AppState.productsLastScanTime = now;

  playBeep();
  if (navigator.vibrate) {
    try { navigator.vibrate(80); } catch(e){}
  }

  // وميض تأكيد بصري
  const viewport = document.getElementById('products-scanner-viewport');
  if (viewport) {
    viewport.style.outline = '4px solid #0284c7';
    viewport.style.boxShadow = '0 0 15px rgba(2, 132, 199, 0.8)';
    setTimeout(() => {
      if (viewport) {
        viewport.style.outline = 'none';
        viewport.style.boxShadow = 'none';
      }
    }, 450);
  }

  // وضع الباركود في حقل البحث
  const searchInput = document.getElementById('products-search-input');
  if (searchInput) {
    searchInput.value = barcode;
  }
  const clearBtn = document.getElementById('btn-products-clear-search');
  if (clearBtn) clearBtn.style.display = 'block';

  AppState.productsSearch = barcode;
  AppState.productsPage = 1;
  renderProductsTab();

  // فحص وجود المنتج
  const products = AppState.data?.Products || [];
  const found = products.find(p => p.Barcode === barcode);

  if (found) {
    showToast(`📦 تم العثور على: ${found.Name} (الثمن: ${found.SellingPrice} د.م.) ✅`);
    setTimeout(() => {
      const card = document.querySelector(`.catalog-card[data-barcode="${barcode}"]`) || document.querySelector(`.catalog-card[data-id="${found.Id}"]`);
      if (card) {
        card.scrollIntoView({ behavior: 'smooth', block: 'center' });
        card.style.transition = 'all 0.3s ease';
        card.style.outline = '3px solid #10b981';
        setTimeout(() => { card.style.outline = 'none'; }, 2500);
      }
    }, 250);
  } else {
    showToast(`⚠️ لا يوجد منتج مسجل بهذا الباركود (${barcode})`);
    if (confirm(`المنتج ذو الباركود (${barcode}) غير موجود في قائمة المنتجات. هل تريد إضافته الآن؟`)) {
      stopProductsCamera();
      openAddProductModal({ Barcode: barcode });
    }
  }

  setTimeout(() => {
    AppState.productsScanLocked = false;
  }, 1200);
}

// ==========================================================================

// 7. إدارة المنتجات (Products Tab & CRUD)

// ==========================================================================

function renderProductsTab() {

  const container = document.getElementById('products-list-container');

  if (!container) return;

  const products = AppState.data?.Products || [];

  const search = (AppState.productsSearch || '').trim().toLowerCase();

  const selectedCat = AppState.productsCategory;

  renderProductsCategoryChips(products);

  const filtered = products.filter(p => {

    if (selectedCat !== 'all' && p.Category !== selectedCat) return false;

    if (!search) return true;

    return (p.Name && p.Name.toLowerCase().includes(search)) ||

           (p.Barcode && p.Barcode.toLowerCase().includes(search)) ||

           (p.Category && p.Category.toLowerCase().includes(search));

  });

  const setT = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };

  setT('products-visible-count', filtered.length);

  setT('products-total-badge', `إجمالي: ${products.length} منتج`);

  const maxItems = AppState.productsPage * AppState.productsPageSize;

  const paged = filtered.slice(0, maxItems);

  const loadMoreBtn = document.getElementById('btn-load-more-products');

  if (loadMoreBtn) {

    loadMoreBtn.style.display = (filtered.length > maxItems) ? 'block' : 'none';

  }

  if (paged.length === 0) {

    container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 30px;">لا توجد سلع مطابقة</div>';

    return;

  }

  container.innerHTML = paged.map(p => {

    const stock = parseFloat(p.StockQuantity) || 0;

    let stockClass = 'stock-in';

    let stockText = `${stock} متوفرة`;

    if (stock <= 0) { stockClass = 'stock-out'; stockText = 'نفذ'; }

    else if (stock <= 5) { stockClass = 'stock-low'; stockText = `قارب على النفاد (${stock})`; }

    return `

      <div class="catalog-card" data-barcode="${p.Barcode || ''}" data-id="${p.Id || ''}">

        <div class="catalog-header">

          <div>

            <div class="catalog-title">${p.Name}</div>

            <div class="catalog-barcode">باركود: <strong>${p.Barcode || 'بدون'}</strong> • الفئة: ${p.Category || 'عام'}</div>

          </div>

          <span class="stock-badge ${stockClass}">${stockText}</span>

        </div>

        <div class="catalog-prices">

          <div>

            <div style="font-size: 11px; color: var(--text-muted);">سعر البيع:</div>

            <div class="catalog-price-dh">${formatDH(p.Price)}</div>

          </div>

          <div style="text-align: left;">

            <div style="font-size: 11px; color: var(--text-muted);">بالريال:</div>

            <div class="catalog-price-rial">${formatRial(p.Price)}</div>

          </div>

        </div>

        <div class="card-actions-row">

          <button class="card-action-btn" style="background:#eff6ff; color:#2563eb;" onclick="quickAddProductFromCard('${p.Barcode}')">

            <span>🛒 بيع</span>

          </button>

          <button class="card-action-btn btn-clone" onclick="duplicateProduct(${p.Id})" title="نسخ واستنساخ هذا المنتج">

            <span>📋 نسخ</span>

          </button>

          <button class="card-action-btn btn-edit" onclick="editProduct(${p.Id})">

            <span>✏️ تعديل</span>

          </button>

          <button class="card-action-btn btn-delete" onclick="deleteProduct(${p.Id}, '${p.Name.replace(/'/g, "\\'")}')">

            <span>🗑️ حذف</span>

          </button>

        </div>

      </div>

    `;

  }).join('');

}

function renderProductsCategoryChips(products) {

  const chipsContainer = document.getElementById('products-category-chips');

  if (!chipsContainer) return;

  const categories = {};

  products.forEach(p => {

    const cat = p.Category || 'عام';

    categories[cat] = (categories[cat] || 0) + 1;

  });

  const catNames = Object.keys(categories).sort();

  const allActive = (AppState.productsCategory === 'all') ? 'active' : '';

  let html = `<button class="chip ${allActive}" data-cat="all">الكل (${products.length})</button>`;

  catNames.forEach(cat => {

    const active = (AppState.productsCategory === cat) ? 'active' : '';

    html += `<button class="chip ${active}" data-cat="${cat}">${cat} (${categories[cat]})</button>`;

  });

  chipsContainer.innerHTML = html;

  chipsContainer.querySelectorAll('.chip').forEach(btn => {

    btn.addEventListener('click', () => {

      AppState.productsCategory = btn.dataset.cat;

      AppState.productsPage = 1;

      renderProductsTab();

    });

  });

}

// ==========================================================================

// 7.1 كاميرا مسح باركود المنتج داخل نافذة الإضافة/التعديل

// ==========================================================================

async function toggleProductModalCamera() {

  const container = document.getElementById('prod-camera-container');

  if (!container) return;

  if (AppState.modalIsScanning) {

    stopProductModalCamera();

  } else {

    // إيقاف كاميرا نقطة البيع إذا كانت قيد التشغيل

    if (AppState.posIsScanning) {

      await stopPOSCamera();

    }

    container.style.display = 'block';

    startProductModalCamera();

  }

}

async function startProductModalCamera() {

  if (typeof Html5Qrcode === 'undefined') {

    showToast('جاري تحميل مشغل الكاميرا...');

    return;

  }

  const viewport = document.getElementById('prod-scanner-viewport');

  if (!viewport) return;

  try {

    const html5QrCode = new Html5Qrcode('prod-scanner-viewport');

    AppState.modalScanner = html5QrCode;

    AppState.modalIsScanning = true;

    const qrCodeSuccessCallback = (decodedText) => {

      handleProductModalScannedBarcode(decodedText);

    };

    const config = { fps: 15, qrbox: { width: 260, height: 160 }, aspectRatio: 1.5 };

    await html5QrCode.start({ facingMode: 'environment' }, config, qrCodeSuccessCallback);

  } catch (err) {

    console.error('Modal Camera error:', err);

    showToast('تعذر تشغيل الكاميرا: يرجى التأكد من منح الإذن للتطبيق');

    stopProductModalCamera();

  }

}

async function stopProductModalCamera() {

  const container = document.getElementById('prod-camera-container');

  if (container) container.style.display = 'none';

  if (AppState.modalScanner && AppState.modalIsScanning) {

    try {

      await AppState.modalScanner.stop();

      AppState.modalScanner.clear();

    } catch (e) {}

  }

  AppState.modalIsScanning = false;

  AppState.modalScanner = null;

}

const stopModalScanner = stopProductModalCamera;

function handleProductModalScannedBarcode(barcode) {

  barcode = (barcode || '').trim();

  if (!barcode) return;

  playBeep();

  const barInp = document.getElementById('prod-barcode');

  if (barInp) barInp.value = barcode;

  showToast(`تم مسح الباركود: ${barcode} ✅`);

  stopProductModalCamera();

  // فحص ما إذا كان المنتج مسجلاً مسبقاً

  const existing = (AppState.data?.Products || []).find(p => p.Barcode === barcode);

  if (existing) {

    showToast(`المنتج "${existing.Name}" مسجل مسبقاً بهذا الباركود`);

    const idInp = document.getElementById('prod-id');

    const nameInp = document.getElementById('prod-name');

    const catInp = document.getElementById('prod-category');

    const buyInp = document.getElementById('prod-buy-price');

    const sellInp = document.getElementById('prod-sell-price');

    const stockInp = document.getElementById('prod-stock');

    if (idInp) idInp.value = existing.Id;

    if (nameInp && !nameInp.value) nameInp.value = existing.Name;

    if (catInp && (!catInp.value || catInp.value === 'عام')) catInp.value = existing.Category || 'عام';

    if (buyInp && (!buyInp.value || buyInp.value === '0')) buyInp.value = existing.BuyPrice || 0;

    if (sellInp && (!sellInp.value || sellInp.value === '0')) {

      sellInp.value = existing.Price || 0;

      updateProdSellRial();

    }

    if (stockInp && (!stockInp.value || stockInp.value === '10')) stockInp.value = existing.StockQuantity || 0;

  } else {

    const nameInp = document.getElementById('prod-name');

    if (nameInp) nameInp.focus();

  }

}

function generateUniqueBarcode() {

  const existing = new Set((AppState.data?.Products || []).map(p => p.Barcode));

  let code = '';

  let attempts = 0;

  do {

    // باركود محلي EAN-13 يبدأ بـ 200 + 9 أرقام عشوائية

    const rand = Math.floor(100000000 + Math.random() * 900000000);

    const prefix = '200' + rand;

    let sum = 0;

    for (let i = 0; i < 12; i++) {

      const d = parseInt(prefix[i], 10);

      sum += (i % 2 === 0) ? d : d * 3;

    }

    const check = (10 - (sum % 10)) % 10;

    code = prefix + check;

    attempts++;

  } while (existing.has(code) && attempts < 100);

  return code;

}

function duplicateProduct(id) {

  const prod = (AppState.data?.Products || []).find(p => p.Id === id);

  if (!prod) {

    showToast('المنتج المراد نسخه غير موجود');

    return;

  }

  // توليد باركود تلقائي مميز للنسخة الجديدة

  const autoBarcode = generateUniqueBarcode();

  openAddProductModal({

    Id: 0,

    Barcode: autoBarcode,

    Name: prod.Name ? `${prod.Name} (نسخة)` : 'منتج منسوخ',

    Category: prod.Category || 'عام',

    BuyPrice: prod.BuyPrice || 0,

    Price: prod.Price || 0,

    StockQuantity: prod.StockQuantity || 10,

    isClone: true

  });

  playBeep();

  showToast(`تم استنساخ بيانات "${prod.Name}". يمكنك مراجعة الأسعار والحفظ 📋`);

}

// ==========================================================================
// Product Image Capture & Interactive Recadrage Editor
// ==========================================================================
const ImageEditor = {
  originalImg: null,
  angle: 0,
  flipH: false,
  brightness: 100,
  zoom: 1.0,
  ratio: '1:1', // '1:1', '4:3', '3:4', 'free'
  dispW: 0,
  dispH: 0,
  dispScale: 1.0,
  cropBox: { x: 0, y: 0, w: 100, h: 100 },
  isDragging: false,
  dragMode: null, // 'move', 'tl', 'tr', 'bl', 'br', 'tc', 'bc', 'ml', 'mr'
  startX: 0,
  startY: 0,
  startBox: { x: 0, y: 0, w: 0, h: 0 },
  listenersAttached: false,

  init: function(imgElement) {
    this.originalImg = imgElement;
    this.angle = 0;
    this.flipH = false;
    this.brightness = 100;
    this.zoom = 1.0;
    this.ratio = '1:1';

    const zoomSlider = document.getElementById('crop-zoom-slider');
    if (zoomSlider) zoomSlider.value = '1';
    const zoomVal = document.getElementById('crop-zoom-val');
    if (zoomVal) zoomVal.textContent = '1.0x';

    this.updateRatioUI();
    this.setupListeners();
    const self = this;
    requestAnimationFrame(function() {
      self.refreshDisplay(true);
      setTimeout(function() { self.refreshDisplay(false); }, 120);
    });
  },

  updateRatioUI: function() {
    const buttons = document.querySelectorAll('.btn-crop-ratio');
    buttons.forEach(btn => {
      if (btn.dataset.ratio === this.ratio) {
        btn.classList.add('active');
        btn.style.background = '#0284c7';
        btn.style.color = '#ffffff';
        btn.style.borderColor = '#0284c7';
      } else {
        btn.classList.remove('active');
        btn.style.background = '#f8fafc';
        btn.style.color = '#334155';
        btn.style.borderColor = '#cbd5e1';
      }
    });
  },

  setRatio: function(newRatio) {
    this.ratio = newRatio;
    this.updateRatioUI();

    const cx = this.cropBox.x + this.cropBox.w / 2;
    const cy = this.cropBox.y + this.cropBox.h / 2;

    if (newRatio === '1:1') {
      let s = Math.min(this.dispW, this.dispH, Math.max(this.cropBox.w, this.cropBox.h));
      this.cropBox.w = s;
      this.cropBox.h = s;
    } else if (newRatio === '4:3') {
      let nw = Math.min(this.dispW, Math.max(this.cropBox.w, 80));
      let nh = Math.round(nw * 3 / 4);
      if (nh > this.dispH) {
        nh = this.dispH;
        nw = Math.round(nh * 4 / 3);
      }
      this.cropBox.w = nw;
      this.cropBox.h = nh;
    } else if (newRatio === '3:4') {
      let nh = Math.min(this.dispH, Math.max(this.cropBox.h, 80));
      let nw = Math.round(nh * 3 / 4);
      if (nw > this.dispW) {
        nw = this.dispW;
        nh = Math.round(nw * 4 / 3);
      }
      this.cropBox.w = nw;
      this.cropBox.h = nh;
    }
    this.cropBox.x = Math.max(0, Math.min(this.dispW - this.cropBox.w, Math.round(cx - this.cropBox.w / 2)));
    this.cropBox.y = Math.max(0, Math.min(this.dispH - this.cropBox.h, Math.round(cy - this.cropBox.h / 2)));
    this.updateCropBoxDOM();
  },

  setZoom: function(val) {
    const newZoom = parseFloat(val) || 1.0;
    this.zoom = newZoom;
    const zoomVal = document.getElementById('crop-zoom-val');
    if (zoomVal) zoomVal.textContent = newZoom.toFixed(1) + 'x';
    this.refreshDisplay(false);
  },

  rotate: function(deg) {
    this.angle = (this.angle + deg) % 360;
    if (this.angle < 0) this.angle += 360;
    this.refreshDisplay(true);
  },

  flip: function() {
    this.flipH = !this.flipH;
    this.refreshDisplay(false);
  },

  toggleCropSquare: function() {
    this.setRatio(this.ratio === '1:1' ? 'free' : '1:1');
  },

  setBrightness: function(delta) {
    this.brightness = Math.max(30, Math.min(220, this.brightness + delta));
    this.refreshDisplay(false);
  },

  reset: function() {
    this.angle = 0;
    this.flipH = false;
    this.brightness = 100;
    this.zoom = 1.0;
    this.ratio = '1:1';
    const zoomSlider = document.getElementById('crop-zoom-slider');
    if (zoomSlider) zoomSlider.value = '1';
    const zoomVal = document.getElementById('crop-zoom-val');
    if (zoomVal) zoomVal.textContent = '1.0x';
    this.updateRatioUI();
    this.refreshDisplay(true);
  },

  refreshDisplay: function(resetBox) {
    if (!this.originalImg) return;
    const canvas = document.getElementById('image-editor-canvas');
    const wrapper = document.getElementById('crop-stage-wrapper');
    const content = document.getElementById('crop-stage-content');
    if (!canvas || !wrapper || !content) return;

    const wrapW = wrapper.clientWidth > 0 ? wrapper.clientWidth - 16 : 330;
    const wrapH = wrapper.clientHeight > 0 ? wrapper.clientHeight - 16 : 255;

    const isSideways = (this.angle === 90 || this.angle === 270);
    const natW = this.originalImg.naturalWidth || this.originalImg.width || 400;
    const natH = this.originalImg.naturalHeight || this.originalImg.height || 400;
    const effW = isSideways ? natH : natW;
    const effH = isSideways ? natW : natH;

    const fitScale = Math.min(wrapW / effW, wrapH / effH, 1.0);
    this.dispScale = fitScale * this.zoom;
    this.dispW = Math.max(40, Math.round(effW * this.dispScale));
    this.dispH = Math.max(40, Math.round(effH * this.dispScale));

    content.style.width = this.dispW + 'px';
    content.style.height = this.dispH + 'px';

    canvas.width = this.dispW;
    canvas.height = this.dispH;
    canvas.style.width = this.dispW + 'px';
    canvas.style.height = this.dispH + 'px';

    const ctx = canvas.getContext('2d');
    ctx.clearRect(0, 0, this.dispW, this.dispH);
    ctx.save();
    ctx.filter = 'brightness(' + this.brightness + '%)';
    ctx.translate(this.dispW / 2, this.dispH / 2);
    ctx.rotate((this.angle * Math.PI) / 180);
    if (this.flipH) ctx.scale(-1, 1);
    const drawW = isSideways ? this.dispH : this.dispW;
    const drawH = isSideways ? this.dispW : this.dispH;
    ctx.drawImage(this.originalImg, -drawW / 2, -drawH / 2, drawW, drawH);
    ctx.restore();

    if (resetBox) {
      if (this.ratio === '1:1') {
        const size = Math.round(Math.min(this.dispW, this.dispH) * 0.85);
        this.cropBox = {
          x: Math.round((this.dispW - size) / 2),
          y: Math.round((this.dispH - size) / 2),
          w: size,
          h: size
        };
      } else if (this.ratio === '4:3') {
        let w = Math.round(this.dispW * 0.85);
        let h = Math.round(w * 3 / 4);
        if (h > this.dispH * 0.9) {
          h = Math.round(this.dispH * 0.85);
          w = Math.round(h * 4 / 3);
        }
        this.cropBox = {
          x: Math.round((this.dispW - w) / 2),
          y: Math.round((this.dispH - h) / 2),
          w: w,
          h: h
        };
      } else if (this.ratio === '3:4') {
        let h = Math.round(this.dispH * 0.85);
        let w = Math.round(h * 3 / 4);
        if (w > this.dispW * 0.9) {
          w = Math.round(this.dispW * 0.85);
          h = Math.round(w * 4 / 3);
        }
        this.cropBox = {
          x: Math.round((this.dispW - w) / 2),
          y: Math.round((this.dispH - h) / 2),
          w: w,
          h: h
        };
      } else {
        const w = Math.round(this.dispW * 0.85);
        const h = Math.round(this.dispH * 0.85);
        this.cropBox = {
          x: Math.round((this.dispW - w) / 2),
          y: Math.round((this.dispH - h) / 2),
          w: w,
          h: h
        };
      }
    } else {
      this.cropBox.x = Math.max(0, Math.min(this.dispW - this.cropBox.w, this.cropBox.x));
      this.cropBox.y = Math.max(0, Math.min(this.dispH - this.cropBox.h, this.cropBox.y));
      this.cropBox.w = Math.min(this.dispW, this.cropBox.w);
      this.cropBox.h = Math.min(this.dispH, this.cropBox.h);
    }

    this.updateCropBoxDOM();
  },

  updateCropBoxDOM: function() {
    const boxEl = document.getElementById('crop-box');
    const badge = document.getElementById('crop-dim-badge');
    if (!boxEl) return;

    this.cropBox.w = Math.max(36, Math.min(this.dispW, this.cropBox.w));
    this.cropBox.h = Math.max(36, Math.min(this.dispH, this.cropBox.h));
    this.cropBox.x = Math.max(0, Math.min(this.dispW - this.cropBox.w, this.cropBox.x));
    this.cropBox.y = Math.max(0, Math.min(this.dispH - this.cropBox.h, this.cropBox.y));

    boxEl.style.left = this.cropBox.x + 'px';
    boxEl.style.top = this.cropBox.y + 'px';
    boxEl.style.width = this.cropBox.w + 'px';
    boxEl.style.height = this.cropBox.h + 'px';

    if (badge && this.dispScale > 0) {
      const actualW = Math.round(this.cropBox.w / this.dispScale);
      const actualH = Math.round(this.cropBox.h / this.dispScale);
      badge.textContent = `${actualW} × ${actualH} px`;
    }
  },

  setupListeners: function() {
    if (this.listenersAttached) return;
    this.listenersAttached = true;

    const boxEl = document.getElementById('crop-box');
    const wrapper = document.getElementById('crop-stage-wrapper');
    if (!boxEl || !wrapper) return;

    const self = this;

    function getCoords(e) {
      if (e.touches && e.touches.length > 0) {
        return { x: e.touches[0].clientX, y: e.touches[0].clientY };
      }
      return { x: e.clientX, y: e.clientY };
    }

    function onPointerDown(e) {
      const coords = getCoords(e);
      const target = e.target;
      if (target.classList.contains('crop-handle')) {
        self.dragMode = target.dataset.handle;
      } else if (target === boxEl || boxEl.contains(target)) {
        self.dragMode = 'move';
      } else {
        return;
      }
      self.isDragging = true;
      self.startX = coords.x;
      self.startY = coords.y;
      self.startBox = { ...self.cropBox };
      e.preventDefault();
      e.stopPropagation();
    }

    function onPointerMove(e) {
      if (!self.isDragging || !self.dragMode) return;
      e.preventDefault();
      const coords = getCoords(e);
      const dx = coords.x - self.startX;
      const dy = coords.y - self.startY;
      const sBox = self.startBox;
      const dispW = self.dispW;
      const dispH = self.dispH;

      const isLocked = (self.ratio !== 'free');
      let R = 1.0;
      if (self.ratio === '4:3') R = 4 / 3;
      else if (self.ratio === '3:4') R = 3 / 4;

      if (self.dragMode === 'move') {
        self.cropBox.x = Math.max(0, Math.min(dispW - sBox.w, sBox.x + dx));
        self.cropBox.y = Math.max(0, Math.min(dispH - sBox.h, sBox.y + dy));
      } else if (self.dragMode === 'br') {
        let nw = Math.max(36, Math.min(dispW - sBox.x, sBox.w + dx));
        let nh = isLocked ? Math.round(nw / R) : Math.max(36, Math.min(dispH - sBox.y, sBox.h + dy));
        if (isLocked && sBox.y + nh > dispH) {
          nh = dispH - sBox.y;
          nw = Math.round(nh * R);
        }
        self.cropBox.w = nw;
        self.cropBox.h = nh;
      } else if (self.dragMode === 'tl') {
        const anchorX = sBox.x + sBox.w;
        const anchorY = sBox.y + sBox.h;
        let nw = Math.max(36, Math.min(anchorX, sBox.w - dx));
        let nh = isLocked ? Math.round(nw / R) : Math.max(36, Math.min(anchorY, sBox.h - dy));
        if (isLocked && nh > anchorY) {
          nh = anchorY;
          nw = Math.round(nh * R);
        }
        self.cropBox.w = nw;
        self.cropBox.h = nh;
        self.cropBox.x = anchorX - nw;
        self.cropBox.y = anchorY - nh;
      } else if (self.dragMode === 'tr') {
        const anchorX = sBox.x;
        const anchorY = sBox.y + sBox.h;
        let nw = Math.max(36, Math.min(dispW - anchorX, sBox.w + dx));
        let nh = isLocked ? Math.round(nw / R) : Math.max(36, Math.min(anchorY, sBox.h - dy));
        if (isLocked && nh > anchorY) {
          nh = anchorY;
          nw = Math.round(nh * R);
        }
        self.cropBox.w = nw;
        self.cropBox.h = nh;
        self.cropBox.x = anchorX;
        self.cropBox.y = anchorY - nh;
      } else if (self.dragMode === 'bl') {
        const anchorX = sBox.x + sBox.w;
        const anchorY = sBox.y;
        let nw = Math.max(36, Math.min(anchorX, sBox.w - dx));
        let nh = isLocked ? Math.round(nw / R) : Math.max(36, Math.min(dispH - anchorY, sBox.h + dy));
        if (isLocked && anchorY + nh > dispH) {
          nh = dispH - anchorY;
          nw = Math.round(nh * R);
        }
        self.cropBox.w = nw;
        self.cropBox.h = nh;
        self.cropBox.x = anchorX - nw;
        self.cropBox.y = anchorY;
      } else if (self.dragMode === 'tc') {
        if (!isLocked) {
          let nh = Math.max(36, sBox.h - dy);
          let ny = sBox.y + dy;
          if (ny >= 0 && nh >= 36) {
            self.cropBox.y = ny;
            self.cropBox.h = nh;
          }
        } else {
          let nh = Math.max(36, Math.min(sBox.y + sBox.h, sBox.h - dy));
          let nw = Math.round(nh * R);
          if (sBox.x + nw > dispW) { nw = dispW - sBox.x; nh = Math.round(nw / R); }
          self.cropBox.y = sBox.y + sBox.h - nh;
          self.cropBox.h = nh;
          self.cropBox.w = nw;
        }
      } else if (self.dragMode === 'bc') {
        if (!isLocked) {
          self.cropBox.h = Math.max(36, Math.min(dispH - sBox.y, sBox.h + dy));
        } else {
          let nh = Math.max(36, Math.min(dispH - sBox.y, sBox.h + dy));
          let nw = Math.round(nh * R);
          if (sBox.x + nw > dispW) { nw = dispW - sBox.x; nh = Math.round(nw / R); }
          self.cropBox.h = nh;
          self.cropBox.w = nw;
        }
      } else if (self.dragMode === 'ml') {
        if (!isLocked) {
          let nw = Math.max(36, sBox.w - dx);
          let nx = sBox.x + dx;
          if (nx >= 0 && nw >= 36) {
            self.cropBox.x = nx;
            self.cropBox.w = nw;
          }
        } else {
          let nw = Math.max(36, Math.min(sBox.x + sBox.w, sBox.w - dx));
          let nh = Math.round(nw / R);
          if (sBox.y + nh > dispH) { nh = dispH - sBox.y; nw = Math.round(nh * R); }
          self.cropBox.x = sBox.x + sBox.w - nw;
          self.cropBox.w = nw;
          self.cropBox.h = nh;
        }
      } else if (self.dragMode === 'mr') {
        if (!isLocked) {
          self.cropBox.w = Math.max(36, Math.min(dispW - sBox.x, sBox.w + dx));
        } else {
          let nw = Math.max(36, Math.min(dispW - sBox.x, sBox.w + dx));
          let nh = Math.round(nw / R);
          if (sBox.y + nh > dispH) { nh = dispH - sBox.y; nw = Math.round(nh * R); }
          self.cropBox.w = nw;
          self.cropBox.h = nh;
        }
      }
      self.updateCropBoxDOM();
    }

    function onPointerUp() {
      self.isDragging = false;
      self.dragMode = null;
    }

    // Touch events for mobile screens
    wrapper.addEventListener('touchstart', onPointerDown, { passive: false });
    window.addEventListener('touchmove', onPointerMove, { passive: false });
    window.addEventListener('touchend', onPointerUp, { passive: false });
    window.addEventListener('touchcancel', onPointerUp, { passive: false });

    // Mouse events for desktop / emulator
    wrapper.addEventListener('mousedown', onPointerDown);
    window.addEventListener('mousemove', onPointerMove);
    window.addEventListener('mouseup', onPointerUp);

    // Aspect ratio selector tabs
    document.querySelectorAll('.btn-crop-ratio').forEach(btn => {
      btn.addEventListener('click', function() {
        self.setRatio(this.dataset.ratio || '1:1');
      });
    });

    // Zoom slider
    document.getElementById('crop-zoom-slider')?.addEventListener('input', function(e) {
      self.setZoom(e.target.value);
    });

    // Reset button
    document.getElementById('btn-editor-reset')?.addEventListener('click', function() {
      self.reset();
    });
  },

  exportBase64: function() {
    if (!this.originalImg) return null;

    const isSideways = (this.angle === 90 || this.angle === 270);
    const natW = this.originalImg.naturalWidth || this.originalImg.width || 400;
    const natH = this.originalImg.naturalHeight || this.originalImg.height || 400;
    const rotW = isSideways ? natH : natW;
    const rotH = isSideways ? natW : natH;

    // First generate the rotated/flipped/brightened master image
    const rotCanvas = document.createElement('canvas');
    rotCanvas.width = rotW;
    rotCanvas.height = rotH;
    const rotCtx = rotCanvas.getContext('2d');
    rotCtx.filter = 'brightness(' + this.brightness + '%)';
    rotCtx.translate(rotW / 2, rotH / 2);
    rotCtx.rotate((this.angle * Math.PI) / 180);
    if (this.flipH) rotCtx.scale(-1, 1);
    rotCtx.drawImage(this.originalImg, -natW / 2, -natH / 2, natW, natH);

    // Calculate crop rectangle in original resolution
    const scale = this.dispScale || 1.0;
    const srcX = Math.max(0, Math.min(rotW, Math.round(this.cropBox.x / scale)));
    const srcY = Math.max(0, Math.min(rotH, Math.round(this.cropBox.y / scale)));
    const srcW = Math.max(10, Math.min(rotW - srcX, Math.round(this.cropBox.w / scale)));
    const srcH = Math.max(10, Math.min(rotH - srcY, Math.round(this.cropBox.h / scale)));

    // Scale to standard high-quality output size (max 600px)
    const maxTarget = 600;
    let outW = srcW;
    let outH = srcH;
    if (outW > maxTarget || outH > maxTarget) {
      if (outW >= outH) {
        outH = Math.round(outH * (maxTarget / outW));
        outW = maxTarget;
      } else {
        outW = Math.round(outW * (maxTarget / outH));
        outH = maxTarget;
      }
    }

    const outCanvas = document.createElement('canvas');
    outCanvas.width = Math.max(30, outW);
    outCanvas.height = Math.max(30, outH);
    const outCtx = outCanvas.getContext('2d');
    outCtx.imageSmoothingEnabled = true;
    outCtx.imageSmoothingQuality = 'high';
    outCtx.drawImage(rotCanvas, srcX, srcY, srcW, srcH, 0, 0, outCanvas.width, outCanvas.height);

    return outCanvas.toDataURL('image/jpeg', 0.88);
  }
};

// ==========================================================================

// كاميرا التقاط صورة المنتوج مباشرة (HTML5 Web Camera)

// ==========================================================================

let productCameraStream = null;

async function openProductCameraCaptureModal() {

  const modal = document.getElementById('modal-camera-capture');

  const video = document.getElementById('camera-capture-video');

  if (!modal || !video) return;

  openModal('modal-camera-capture');

  try {

    let stream = null;

    try {

      stream = await navigator.mediaDevices.getUserMedia({

        video: { facingMode: { ideal: 'environment' } },

        audio: false

      });

    } catch (e1) {

      // Fallback to basic video constraint if environment facingMode fails

      stream = await navigator.mediaDevices.getUserMedia({

        video: true,

        audio: false

      });

    }

    productCameraStream = stream;

    video.srcObject = productCameraStream;

    await video.play();

  } catch (err) {

    console.error('Camera open error:', err);

    showToast('تعذر تشغيل الكاميرا: يرجى التأكد من صلاحية الكاميرا في الهاتف');

    closeProductCameraCaptureModal();

  }

}

function closeProductCameraCaptureModal() {

  if (productCameraStream) {

    productCameraStream.getTracks().forEach(track => track.stop());

    productCameraStream = null;

  }

  const video = document.getElementById('camera-capture-video');

  if (video) video.srcObject = null;

  closeModal('modal-camera-capture');

}

function snapProductCameraPhoto() {

  const video = document.getElementById('camera-capture-video');

  const canvas = document.getElementById('camera-capture-canvas');

  if (!video || !canvas) return;

  const width = video.videoWidth || 640;

  const height = video.videoHeight || 480;

  canvas.width = width;

  canvas.height = height;

  const ctx = canvas.getContext('2d');

  ctx.drawImage(video, 0, 0, width, height);

  const dataUrl = canvas.toDataURL('image/jpeg', 0.85);

  closeProductCameraCaptureModal();

  const img = new Image();

  img.onload = function() {

    openModal('modal-image-editor');

    setTimeout(function() {

      ImageEditor.init(img);

    }, 100);

  };

  img.src = dataUrl;

}

function handleProductImageSelected(file) {

  if (!file) return;

  const reader = new FileReader();

  reader.onload = function(e) {

    const img = new Image();

    img.onload = function() {

      openModal('modal-image-editor');

      setTimeout(function() {

        ImageEditor.init(img);

      }, 100);

    };

    img.src = e.target.result;

  };

  reader.readAsDataURL(file);

}

function setProductImagePreview(src) {

  AppState.currentProductImage = src;

  const preview = document.getElementById('prod-img-preview');

  const placeholder = document.getElementById('prod-img-placeholder');

  const btnEdit = document.getElementById('btn-edit-prod-img');

  const btnRemove = document.getElementById('btn-remove-prod-img');

  const btnDrive = document.getElementById('btn-save-img-to-drive');

  if (src) {

    if (preview) {

      preview.src = src;

      preview.style.display = 'block';

    }

    if (placeholder) placeholder.style.display = 'none';

    if (btnEdit) btnEdit.style.display = 'inline-flex';

    if (btnRemove) btnRemove.style.display = 'inline-flex';

    if (btnDrive) btnDrive.style.display = 'inline-flex';

  } else {

    if (preview) {

      preview.src = '';

      preview.style.display = 'none';

    }

    if (placeholder) placeholder.style.display = 'flex';

    if (btnEdit) btnEdit.style.display = 'none';

    if (btnRemove) btnRemove.style.display = 'none';

    if (btnDrive) btnDrive.style.display = 'none';

  }

}

// --- دعم مزامنة ورفع صور المنتجات إلى Google Drive ---

function getPendingDrivePhotos() {

  try {

    const raw = localStorage.getItem('lmima_pending_drive_photos');

    return raw ? JSON.parse(raw) : [];

  } catch (e) {

    return [];

  }

}

function savePendingDrivePhoto(barcode, category, name, imageBase64) {

  if (!barcode || !imageBase64) return;

  const list = getPendingDrivePhotos().filter(p => p.barcode !== barcode);

  list.push({

    barcode,

    category: category || 'عام',

    name: name || '',

    imageBase64,

    timestamp: Date.now()

  });

  try {

    localStorage.setItem('lmima_pending_drive_photos', JSON.stringify(list));

  } catch (e) {

    console.warn('Could not save photo to localStorage', e);

  }

  updatePendingDrivePhotosUI();

}

function removePendingDrivePhoto(barcode) {

  const list = getPendingDrivePhotos().filter(p => p.barcode !== barcode);

  try {

    localStorage.setItem('lmima_pending_drive_photos', JSON.stringify(list));

  } catch (e) {}

  updatePendingDrivePhotosUI();

}

function updatePendingDrivePhotosUI() {

  const list = getPendingDrivePhotos();

  const count = list.length;

  const countEl = document.getElementById('pending-drive-photos-count');

  if (countEl) countEl.textContent = count;

}

function base64ToBlob(base64, mimeType = 'image/jpeg') {

  const pure = base64.includes(',') ? base64.split(',')[1] : base64;

  const byteChars = atob(pure);

  const byteArrays = [];

  for (let offset = 0; offset < byteChars.length; offset += 512) {

    const slice = byteChars.slice(offset, offset + 512);

    const byteNumbers = new Array(slice.length);

    for (let i = 0; i < slice.length; i++) {

      byteNumbers[i] = slice.charCodeAt(i);

    }

    byteArrays.push(new Uint8Array(byteNumbers));

  }

  return new Blob(byteArrays, { type: mimeType });

}

async function saveSingleProductPhotoToDrive(barcode, category, name, imageBase64) {

  if (!imageBase64) {

    showToast('⚠️ لا توجد صورة للمنتوج لحفظها');

    return;

  }

  const code = (barcode || '').trim() || ('prod_' + Date.now());

  const cat = (category || 'عام').trim();

  const fileName = `${code}___${cat}.jpg`;

  showToast('جاري تصدير الصورة إلى Google Drive... ☁️');

  const blob = base64ToBlob(imageBase64, 'image/jpeg');

  // استخدام Web Share إذا كان مدعوماً

  if (navigator.share && navigator.canShare) {

    try {

      const file = new File([blob], fileName, { type: 'image/jpeg' });

      if (navigator.canShare({ files: [file] })) {

        await navigator.share({

          files: [file],

          title: `صورة منتج ${name || code}`,

          text: `صورة المنتج ${code} لمزامنتها مع الحاسوب`

        });

        showToast('تمت مشاركة الصورة بنجاح! اختر مجلد Google Drive ☁️');

        return;

      }

    } catch (err) {

      if (err.name === 'AbortError') return;

      console.warn('Share API not supported, downloading file instead:', err);

    }

  }

  // تنزيل ملف الصورة مباشرة باسم الباركود

  const a = document.createElement('a');

  a.href = URL.createObjectURL(blob);

  a.download = fileName;

  document.body.appendChild(a);

  a.click();

  document.body.removeChild(a);

  URL.revokeObjectURL(a.href);

  showToast(`تم تنزيل الصورة (${fileName})! احفظها بمجلد Google Drive ليسحبها الحاسوب تلقائياً ⚡`);

}

async function exportPendingPhotosToGoogleDrive() {

  const list = getPendingDrivePhotos();

  if (list.length === 0) {

    showToast('لا توجد صور منتجات معلقة للرفع حالياً ✅');

    return;

  }

  showToast(`جاري تجهيز حزمة ${list.length} صورة لـ Google Drive... ☁️`);

  const fileName = 'lmima_pending_photos.json';

  const jsonContent = JSON.stringify(list, null, 2);

  const blob = new Blob([jsonContent], { type: 'application/json' });

  if (navigator.share && navigator.canShare) {

    try {

      const file = new File([blob], fileName, { type: 'application/json' });

      if (navigator.canShare({ files: [file] })) {

        await navigator.share({

          files: [file],

          title: 'صور منتجات LMIMA SHOP لـ Google Drive',

          text: 'حزمة صور المنتجات المعلقة لمزامنتها مع الحاسوب'

        });

        showToast('تمت المشاركة! اختر Google Drive لحفظ الحزمة ☁️');

        return;

      }

    } catch (err) {

      if (err.name === 'AbortError') return;

    }

  }

  const a = document.createElement('a');

  a.href = URL.createObjectURL(blob);

  a.download = fileName;

  document.body.appendChild(a);

  a.click();

  document.body.removeChild(a);

  URL.revokeObjectURL(a.href);

  showToast('تم تنزيل حزمة الصور! احفظها في مجلد Google Drive ليقوم الحاسوب باستيرادها وحذفها تلقائياً ⚡');

}

function populateProductCategories(selectedCat) {

  const select = document.getElementById('prod-category-select');

  if (!select) return;

  const defaultCats = [

    'عام', 'مشروبات', 'معجنات', 'الحليب ومشتقاته', 'زيت', 'سكر', 'شاي وقهوة',

    'دقيق', 'سميدة', 'كسكس', 'سردين و طون', 'مدهونات', 'كاشير', 'فلان',

    'خميرة', 'تجميل', 'نظافة الجسم', 'نظافة عامة', 'منتوجات بالعبار', 'مكملات', 'اكسسوارات متنوعة'

  ];

  const existing = AppState.data?.Categories || [];

  const set = new Set();

  defaultCats.forEach(function(c) { set.add(c); });

  existing.forEach(function(c) { if (c) set.add(c); });

  if (selectedCat && selectedCat !== '__NEW__') set.add(selectedCat);

  const sorted = Array.from(set).sort();

  let html = sorted.map(function(c) {

    return '<option value="' + c + '"' + (c === selectedCat ? ' selected' : '') + '>' + c + '</option>';

  }).join('');

  html += '<option value="__NEW__">➕ إضافة فئة جديدة...</option>';

  select.innerHTML = html;

}

function openAddProductModal(product = null) {

  // إيقاف أي كاميرا نشطة قبل فتح النافذة

  stopPOSCamera();

  stopProductModalCamera();

  openModal('modal-product');

  const title = document.getElementById('modal-product-title');

  const cloneHint = document.getElementById('prod-modal-clone-hint');

  const idInp = document.getElementById('prod-id');

  const barInp = document.getElementById('prod-barcode');

  const nameInp = document.getElementById('prod-name');

  const catInp = document.getElementById('prod-category');

  const buyInp = document.getElementById('prod-buy-price');

  const sellInp = document.getElementById('prod-sell-price');

  const stockInp = document.getElementById('prod-stock');

  // تعبئة قائمة التصنيفات المقترحة

  populateProductCategories(product?.Category || 'عام');

  const newCatBox = document.getElementById('new-category-inline-box');

  if (newCatBox) newCatBox.style.display = 'none';

  // ضبط صورة المنتوج الحالية أو تفريغها

  const existingImg = product?.Image || (product?.ImagePath ? ('http://' + AppState.serverIp + '/' + product.ImagePath) : null);

  setProductImagePreview(existingImg);

  if (product && product.isClone) {

    if (title) title.textContent = '📋 استنساخ وإضافة منتج جديد';

    if (cloneHint) cloneHint.style.display = 'block';

    if (idInp) idInp.value = '0';

    if (barInp) barInp.value = product.Barcode || '';

    if (nameInp) nameInp.value = product.Name || '';

    if (catInp) catInp.value = product.Category || 'عام';

    if (buyInp) buyInp.value = product.BuyPrice || 0;

    if (sellInp) sellInp.value = product.Price || 0;

    if (stockInp) stockInp.value = product.StockQuantity || 10;

  } else if (product && product.Id) {

    if (title) title.textContent = '✏️ تعديل بيانات المنتج';

    if (cloneHint) cloneHint.style.display = 'none';

    if (idInp) idInp.value = product.Id;

    if (barInp) barInp.value = product.Barcode || '';

    if (nameInp) nameInp.value = product.Name || '';

    if (catInp) catInp.value = product.Category || '';

    if (buyInp) buyInp.value = product.BuyPrice || 0;

    if (sellInp) sellInp.value = product.Price || 0;

    if (stockInp) stockInp.value = product.StockQuantity || 0;

  } else {

    if (title) title.textContent = '➕ إضافة منتج جديد';

    if (cloneHint) cloneHint.style.display = 'none';

    if (idInp) idInp.value = '0';

    if (barInp) barInp.value = product?.Barcode || '';

    if (nameInp) nameInp.value = '';

    if (catInp) catInp.value = 'عام';

    if (buyInp) buyInp.value = '';

    if (sellInp) sellInp.value = '';

    if (stockInp) stockInp.value = '10';

  }

  updateProdSellRial();

}

function editProduct(id) {

  const prod = (AppState.data?.Products || []).find(p => p.Id === id);

  if (prod) openAddProductModal(prod);

}

function updateProdSellRial() {

  const sellInp = document.getElementById('prod-sell-price');

  const rialEl = document.getElementById('prod-sell-price-rial');

  if (sellInp && rialEl) {

    const val = parseFloat(sellInp.value) || 0;

    rialEl.textContent = formatRial(val);

  }

}

async function saveProductFromModal() {

  const id = parseInt(document.getElementById('prod-id')?.value || '0');

  const barcode = (document.getElementById('prod-barcode')?.value || '').trim();

  const name = (document.getElementById('prod-name')?.value || '').trim();

  let category = (document.getElementById('prod-category-select')?.value || 'عام').trim();

  if (category === '__NEW__') {

    category = (document.getElementById('new-category-input')?.value || '').trim() || 'عام';

  }

  const buyPrice = parseFloat(document.getElementById('prod-buy-price')?.value || '0');

  const sellPrice = parseFloat(document.getElementById('prod-sell-price')?.value || '0');

  const stock = parseFloat(document.getElementById('prod-stock')?.value || '0');

  const imageBase64 = AppState.currentProductImage || '';

  if (!name) {

    showToast('⚠️ يرجى كتابة اسم المنتج');

    return;

  }

  if (sellPrice <= 0) {

    showToast('⚠️ يرجى إدخال سعر بيع صحيح وموجب');

    return;

  }

  const payload = {

    id,

    barcode,

    name,

    category,

    purchasePrice: buyPrice,

    sellPrice: sellPrice,

    stockQuantity: stock,

    imageBase64: imageBase64

  };

  showToast('جاري حفظ المنتج...');

  closeModal('modal-product');

  const res = await postToServer('/api/products/save', payload);

  if (res && res.success) {

    showToast('تم حفظ المنتج وتحديث الحاسوب بنجاح! ⚡');

    if (imageBase64) {

      removePendingDrivePhoto(barcode);

    }

  } else {

    queueOfflineAction('product_save', payload);

    if (imageBase64) {

      savePendingDrivePhoto(barcode, category, name, imageBase64);

    }

    showToast('تم حفظ المنتج محلياً وسيتم رفعه للحاسوب 📲');

  }

  // تحديث محلي فوري

  if (!AppState.data) AppState.data = {};

  if (!AppState.data.Products) AppState.data.Products = [];

  const existingIdx = AppState.data.Products.findIndex(p => p.Id === id && id > 0);

  const oldProduct = (existingIdx >= 0) ? AppState.data.Products[existingIdx] : null;

  const updatedItem = {

    Id: (id > 0) ? id : (res?.id || Math.floor(1000 + Math.random() * 9000)),

    Barcode: barcode,

    Name: name,

    Category: category,

    BuyPrice: buyPrice,

    Price: sellPrice,

    PriceRial: sellPrice * 20,

    StockQuantity: stock,

    ImagePath: imageBase64 || oldProduct?.ImagePath || ''

  };

  if (existingIdx >= 0) {

    AppState.data.Products[existingIdx] = updatedItem;

  } else {

    AppState.data.Products.unshift(updatedItem);

  }

  saveDataLocally();

  renderPOSGrid();

  renderProductsTab();

}

async function deleteProduct(id, name) {

  if (!confirm(`هل أنت متأكد من حذف المنتج: "${name}"؟`)) return;

  showToast('جاري حذف المنتج...');

  const res = await postToServer('/api/products/delete', { id });

  if (res && res.success) {

    showToast('تم حذف المنتج بنجاح! 🗑️');

  } else {

    queueOfflineAction('product_delete', { id });

    showToast('تم الحذف محلياً وسيتم إبلاغ الحاسوب 📲');

  }

  if (AppState.data?.Products) {

    AppState.data.Products = AppState.data.Products.filter(p => p.Id !== id);

    saveDataLocally();

    renderPOSGrid();

    renderProductsTab();

  }

}

// ==========================================================================

// 8. إدارة الزبناء والديون (Customers Tab & CRUD)

// ==========================================================================

function renderCustomersTab() {

  const container = document.getElementById('customers-list-container');

  if (!container) return;

  const customers = AppState.data?.Customers || [];

  const search = (AppState.customersSearch || '').trim().toLowerCase();

  const filter = AppState.customersFilter;

  let totalDebt = 0;

  let debtsCount = 0;

  let zeroCount = 0;

  customers.forEach(c => {

    const bal = parseFloat(c.DueBalance) || 0;

    totalDebt += bal;

    if (bal > 0) debtsCount++;

    else zeroCount++;

  });

  const setT = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };

  setT('customers-total-debts-dh', formatDH(totalDebt));

  setT('customers-total-debts-rial', formatRial(totalDebt));

  setT('customers-count-badge', `${customers.length} زبون`);

  setT('cust-count-all', customers.length);

  setT('cust-count-debts', debtsCount);

  setT('cust-count-zero', zeroCount);

  const filtered = customers.filter(c => {

    const bal = parseFloat(c.DueBalance) || 0;

    if (filter === 'debts' && bal <= 0) return false;

    if (filter === 'zero' && bal > 0) return false;

    if (filter === 'over200' && bal < 200) return false;

    if (!search) return true;

    return (c.FullName && c.FullName.toLowerCase().includes(search)) ||

           (c.Phone && c.Phone.includes(search));

  });

  if (filtered.length === 0) {

    container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 30px;">لا يوجد زبائن مطابقون للبحث</div>';

    return;

  }

  container.innerHTML = filtered.map(c => {

    const bal = parseFloat(c.DueBalance) || 0;

    const hasPhone = c.Phone && c.Phone !== '-' && c.Phone.length >= 8;

    const cleanPhone = cleanMoroccanPhone(c.Phone);

    const storeName = AppState.data?.StoreInfo?.StoreName || 'متجر LMIMA SHOP';

    const waText = encodeURIComponent(`السلام عليكم ورحمة الله أخي ${c.FullName}،\nنذكركم باحترام بأن المبلغ المتبقي بذمتكم لدى ${storeName} هو:\n💰 ${bal.toFixed(2)} درهم (${Math.round(bal * 20).toLocaleString()} ريال).\nشكراً لتعاملكم معنا ونتمنى لكم يوماً طيباً.`);

    const waUrl = hasPhone ? `https://wa.me/${cleanPhone}?text=${waText}` : `https://wa.me/?text=${waText}`;

    // البحث عن مشتريات الكريدي إذا توفرت في CustomerDebts

    const debtObj = (AppState.data?.CustomerDebts || []).find(d => d.Id === c.Id);

    const purchases = debtObj?.RecentPurchases || [];

    const isExpanded = AppState.expandedDebts.has(c.Id);

    let purchasesHtml = '';

    if (purchases.length > 0) {

      purchasesHtml = `

        <table class="invoice-items-table" style="margin-top: 6px;">

          <thead>

            <tr>

              <th>التاريخ</th>

              <th>السلعة</th>

              <th style="text-align:center;">الكمية</th>

              <th style="text-align:left;">الثمن</th>

              <th style="text-align:left;">المجموع</th>

            </tr>

          </thead>

          <tbody>

            ${purchases.map(p => `

              <tr>

                <td style="font-size: 10px; color: var(--text-muted);">${p.Date || ''}</td>

                <td>${p.ProductName}</td>

                <td style="text-align:center; font-weight:700;">${p.Quantity}</td>

                <td style="text-align:left;">${formatDH(p.Price)}</td>

                <td style="text-align:left; font-weight:700;">${formatDH(p.SubTotal)}</td>

              </tr>

            `).join('')}

          </tbody>

        </table>

      `;

    } else {

      purchasesHtml = '<div style="color: var(--text-muted); font-size: 11px; text-align: center; padding: 6px;">لا توجد تفاصيل مشتريات بالكريدي مسجلة مؤخراً</div>';

    }

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

          <span>الفواتير: ${c.InvoiceCount || 0}</span>

          <span>${c.AllowCredit ? '✅ يسمح بالكريدي' : '🚫 لا يسمح بالكريدي'}</span>

        </div>

        <button class="card-action-btn btn-statement" style="width: 100%; margin-bottom: 6px;" onclick="openCustomerStatementModal(${c.Id})">

          <span>📑 فاتورة وكشف الحساب المفصل</span>

        </button>

        <button class="card-action-btn" style="width: 100%; margin-bottom: 6px;" onclick="toggleDebtorPurchases(${c.Id})">

          <span>${isExpanded ? '▲ إخفاء تفاصيل المشتريات' : `🛒 السلع المشتراة بالكريدي (${purchases.length}) ▼`}</span>

        </button>

        <div class="debtor-purchases-drawer ${isExpanded ? 'open' : ''}" id="debt-drawer-${c.Id}">

          ${purchasesHtml}

        </div>

        <div class="card-actions-row">

          ${bal > 0 ? `

            <button class="card-action-btn btn-pay" onclick="openPayDebtModal(${c.Id})">

              <span>💰 تسديد دين</span>

            </button>

          ` : ''}

          <a href="${waUrl}" target="_blank" class="card-action-btn" style="color: #16a34a;">

            <span>💬 واتساب</span>

          </a>

          ${hasPhone ? `

            <a href="tel:${c.Phone}" class="card-action-btn" style="color: #0284c7;">

              <span>📞 اتصال</span>

            </a>

          ` : ''}

          <button class="card-action-btn btn-edit" onclick="editCustomer(${c.Id})">

            <span>✏️ تعديل</span>

          </button>

          <button class="card-action-btn btn-delete" onclick="deleteCustomer(${c.Id}, '${c.FullName.replace(/'/g, "\\'")}')">

            <span>🗑️</span>

          </button>

        </div>

      </div>

    `;

  }).join('');

}

function toggleDebtorPurchases(custId) {

  if (AppState.expandedDebts.has(custId)) {

    AppState.expandedDebts.delete(custId);

  } else {

    AppState.expandedDebts.add(custId);

  }

  renderCustomersTab();

}

function openAddCustomerModal(customer = null) {

  openModal('modal-customer');

  const title = document.getElementById('modal-customer-title');

  const idInp = document.getElementById('cust-id');

  const nameInp = document.getElementById('cust-name');

  const phoneInp = document.getElementById('cust-phone');

  const limitInp = document.getElementById('cust-credit-limit');

  const allowChk = document.getElementById('cust-allow-credit');

  if (customer && customer.Id) {

    if (title) title.textContent = '✏️ تعديل بيانات الزبون';

    if (idInp) idInp.value = customer.Id;

    if (nameInp) nameInp.value = customer.FullName || '';

    if (phoneInp) phoneInp.value = customer.Phone || '';

    if (limitInp) limitInp.value = customer.CreditLimit || 1000;

    if (allowChk) allowChk.checked = (customer.AllowCredit !== false);

  } else {

    if (title) title.textContent = '➕ إضافة زبون جديد';

    if (idInp) idInp.value = '0';

    if (nameInp) nameInp.value = '';

    if (phoneInp) phoneInp.value = '';

    if (limitInp) limitInp.value = '1000';

    if (allowChk) allowChk.checked = true;

  }

}

function editCustomer(id) {

  const cust = (AppState.data?.Customers || []).find(c => c.Id === id);

  if (cust) openAddCustomerModal(cust);

}

async function saveCustomerFromModal() {

  const id = parseInt(document.getElementById('cust-id')?.value || '0');

  const name = (document.getElementById('cust-name')?.value || '').trim();

  const phone = (document.getElementById('cust-phone')?.value || '').trim();

  const limit = parseFloat(document.getElementById('cust-credit-limit')?.value || '0');

  const allowCredit = document.getElementById('cust-allow-credit')?.checked !== false;

  if (!name) {

    showToast('⚠️ يرجى كتابة اسم الزبون');

    return;

  }

  const payload = {

    id,

    fullName: name,

    phone,

    creditLimit: limit,

    allowCredit

  };

  showToast('جاري حفظ الزبون...');

  closeModal('modal-customer');

  const res = await postToServer('/api/customers/save', payload);

  if (res && res.success) {

    showToast('تم حفظ الزبون وتحديث الحاسوب بنجاح! ⚡');

  } else {

    queueOfflineAction('customer_save', payload);

    showToast('تم حفظ الزبون محلياً وسيتم رفعه للحاسوب 📲');

  }

  // تحديث محلي

  if (!AppState.data) AppState.data = {};

  if (!AppState.data.Customers) AppState.data.Customers = [];

  const existingIdx = AppState.data.Customers.findIndex(c => c.Id === id && id > 0);

  const updatedCust = {

    Id: (id > 0) ? id : (res?.id || Math.floor(1000 + Math.random() * 9000)),

    FullName: name,

    Phone: phone,

    DueBalance: (existingIdx >= 0) ? AppState.data.Customers[existingIdx].DueBalance : 0,

    DueBalanceRial: (existingIdx >= 0) ? AppState.data.Customers[existingIdx].DueBalanceRial : 0,

    CreditLimit: limit,

    AllowCredit: allowCredit,

    InvoiceCount: (existingIdx >= 0) ? AppState.data.Customers[existingIdx].InvoiceCount : 0

  };

  if (existingIdx >= 0) {

    AppState.data.Customers[existingIdx] = updatedCust;

  } else {

    AppState.data.Customers.unshift(updatedCust);

  }

  saveDataLocally();

  renderCustomersTab();

  populateCartCustomerSelect();

}

async function deleteCustomer(id, name) {

  if (!confirm(`هل أنت متأكد من حذف الزبون: "${name}"؟`)) return;

  showToast('جاري حذف الزبون...');

  const res = await postToServer('/api/customers/delete', { id });

  if (res && res.success) {

    showToast('تم حذف الزبون بنجاح! 🗑️');

  } else {

    queueOfflineAction('customer_delete', { id });

    showToast('تم الحذف محلياً وسيتم إبلاغ الحاسوب 📲');

  }

  if (AppState.data?.Customers) {

    AppState.data.Customers = AppState.data.Customers.filter(c => c.Id !== id);

    saveDataLocally();

    renderCustomersTab();

    populateCartCustomerSelect();

  }

}

// تسديد الديون مع لوحة الأرقام

let payDebtCurrentValue = '';

let payDebtMaxDue = 0;

function openPayDebtModal(customerId) {

  const cust = (AppState.data?.Customers || []).find(c => c.Id === customerId);

  if (!cust) return;

  payDebtMaxDue = parseFloat(cust.DueBalance) || 0;

  payDebtCurrentValue = payDebtMaxDue.toFixed(2);

  const idInp = document.getElementById('pay-cust-id');

  const nameEl = document.getElementById('pay-cust-name');

  const dueEl = document.getElementById('pay-cust-due');

  const dueRialEl = document.getElementById('pay-cust-due-rial');

  const amtInp = document.getElementById('pay-debt-amount-input');

  if (idInp) idInp.value = customerId;

  if (nameEl) nameEl.textContent = `الزبون: ${cust.FullName}`;

  if (dueEl) dueEl.textContent = formatDH(payDebtMaxDue);

  if (dueRialEl) dueRialEl.textContent = formatRial(payDebtMaxDue);

  if (amtInp) amtInp.value = payDebtCurrentValue;

  openModal('modal-pay-debt');

}

function handlePayDebtKeypad(key) {

  const amtInp = document.getElementById('pay-debt-amount-input');

  if (!amtInp) return;

  if (key === 'C') {

    payDebtCurrentValue = '';

  } else if (key === 'back') {

    payDebtCurrentValue = payDebtCurrentValue.slice(0, -1);

  } else if (key === '.') {

    if (!payDebtCurrentValue.includes('.')) {

      payDebtCurrentValue = (payDebtCurrentValue === '') ? '0.' : payDebtCurrentValue + '.';

    }

  } else if (key === '+50' || key === '+100') {

    let current = parseFloat(payDebtCurrentValue) || 0;

    let add = key === '+50' ? 50 : 100;

    payDebtCurrentValue = Math.min(payDebtMaxDue, current + add).toFixed(2);

  } else {

    payDebtCurrentValue += key;

  }

  amtInp.value = payDebtCurrentValue || '0.00';

}

async function confirmPayDebt() {

  const custId = parseInt(document.getElementById('pay-cust-id')?.value || '0');

  const amount = parseFloat(payDebtCurrentValue) || 0;

  if (custId <= 0 || amount <= 0) {

    showToast('⚠️ يرجى إدخال مبلغ تسديد صحيح');

    return;

  }

  if (amount > payDebtMaxDue) {

    showToast('⚠️ لا يمكن تسديد مبلغ أكبر من الدين المستحق!');

    return;

  }

  showToast('جاري تسجيل تسديد الدين...');

  closeModal('modal-pay-debt');

  const res = await postToServer('/api/customers/pay-debt', { id: custId, amount });

  if (res && res.success) {

    showToast('تم تسديد الدين وخصمه من حساب الزبون بنجاح! 💰');

  } else {

    queueOfflineAction('customer_pay_debt', { id: custId, amount });

    showToast('تم تسجيل التسديد محلياً وسيتم رفعه للحاسوب 📲');

  }

  // تحديث محلي

  const cust = (AppState.data?.Customers || []).find(c => c.Id === custId);

  if (cust) {

    cust.DueBalance = Math.max(0, cust.DueBalance - amount);

    cust.DueBalanceRial = cust.DueBalance * 20;

  }

  const custDebt = (AppState.data?.CustomerDebts || []).find(c => c.Id === custId);

  if (custDebt) {

    custDebt.DueBalance = Math.max(0, custDebt.DueBalance - amount);

    custDebt.DueBalanceRial = custDebt.DueBalance * 20;

  }

  playSuccessSound();

  saveDataLocally();

  renderCustomersTab();

  populateCartCustomerSelect();

}

// ==========================================================================

// 8.1. توليد فواتير وكشف حساب زبائن الكريدي (Customer Statements & Credit Invoices)

// ==========================================================================

let currentStatementData = null;

let currentStatementCustId = 0;

let currentStatementRange = 'all';

function openCustomerStatementModal(custId) {

  currentStatementCustId = custId;

  currentStatementRange = 'all';

  currentStatementData = null;

  // إرجاع الأزرار للحالة الافتراضية (الكل)

  document.querySelectorAll('.stmt-preset-btn').forEach(btn => {

    btn.classList.toggle('active', btn.dataset.range === 'all');

  });

  const customBox = document.getElementById('stmt-custom-dates-box');

  if (customBox) customBox.style.display = 'none';

  // تعيين تواريخ افتراضية للخانة المخصصة

  const todayStr = new Date().toISOString().split('T')[0];

  const dFrom = document.getElementById('stmt-date-from');

  const dTo = document.getElementById('stmt-date-to');

  if (dFrom) dFrom.value = todayStr;

  if (dTo) dTo.value = todayStr;

  openModal('modal-customer-statement');

  loadCustomerStatement(custId, 'all');

}

async function loadCustomerStatement(custId, rangeType, customFrom = null, customTo = null) {

  const spinner = document.getElementById('stmt-loading-spinner');

  const content = document.getElementById('stmt-content-body');

  if (spinner) spinner.style.display = 'block';

  if (content) content.style.opacity = '0.3';

  let fromStr = '';

  let toStr = '';

  const now = new Date();

  if (rangeType === 'today') {

    fromStr = now.toISOString().split('T')[0];

    toStr = fromStr;

  } else if (rangeType === 'week') {

    const d = new Date();

    d.setDate(d.getDate() - 7);

    fromStr = d.toISOString().split('T')[0];

    toStr = now.toISOString().split('T')[0];

  } else if (rangeType === 'month') {

    const d = new Date();

    d.setDate(d.getDate() - 30);

    fromStr = d.toISOString().split('T')[0];

    toStr = now.toISOString().split('T')[0];

  } else if (rangeType === 'custom') {

    fromStr = customFrom || '';

    toStr = customTo || '';

  }

  let statementResult = null;

  // 1. محاولة جلب الكشف الحي من خادم المتجر

  if (AppState.serverIp) {

    const cleanHost = AppState.serverIp.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

    let url = `http://${cleanHost}/api/customers/statement?id=${custId}`;

    if (fromStr) url += `&from=${encodeURIComponent(fromStr)}`;

    if (toStr) url += `&to=${encodeURIComponent(toStr)}`;

    const controller = new AbortController();

    const timeoutId = setTimeout(() => controller.abort(), 4000);

    try {

      const res = await fetch(url, { signal: controller.signal, headers: authHeaders() });

      clearTimeout(timeoutId);

      if (res.status === 401) handleUnauthorized();

      if (res.ok) {

        const json = await res.json();

        if (json && json.success) {

          statementResult = json;

        }

      }

    } catch (e) {

      clearTimeout(timeoutId);

    }

  }

  // 2. إذا كان الجهاز غير متصل بالخادم، توليد الكشف محلياً من بيانات التطبيق

  if (!statementResult) {

    const cust = (AppState.data?.Customers || []).find(c => c.Id === custId) || {};

    const debtObj = (AppState.data?.CustomerDebts || []).find(d => d.Id === custId) || {};

    const allPurchases = debtObj.RecentPurchases || [];

    const allPayments = debtObj.RecentPayments || [];

    // تصفية حسب التاريخ إذا وُجد

    let filteredPurchases = allPurchases;

    let filteredPayments = allPayments;

    if (fromStr || toStr) {

      filteredPurchases = allPurchases.filter(p => {

        if (!p.Date) return true;

        const pDate = p.Date.split(' ')[0];

        if (fromStr && pDate < fromStr) return false;

        if (toStr && pDate > toStr) return false;

        return true;

      });

      filteredPayments = allPayments.filter(pay => {

        if (!pay.PaymentDate) return true;

        const payDate = pay.PaymentDate.split(' ')[0];

        if (fromStr && payDate < fromStr) return false;

        if (toStr && payDate > toStr) return false;

        return true;

      });

    }

    let totPurchases = 0;

    filteredPurchases.forEach(p => totPurchases += (parseFloat(p.SubTotal) || 0));

    let totPaid = 0;

    filteredPayments.forEach(p => totPaid += (parseFloat(p.Amount) || 0));

    const curDue = parseFloat(cust.DueBalance ?? debtObj.DueBalance ?? 0);

    let periodLabel = 'كافة المشتريات والمدفوعات (منذ البداية)';

    if (fromStr && toStr) periodLabel = `من ${fromStr} إلى ${toStr}`;

    else if (fromStr) periodLabel = `من ${fromStr}`;

    else if (toStr) periodLabel = `حتى ${toStr}`;

    statementResult = {

      success: true,

      storeInfo: {

        storeName: AppState.data?.StoreInfo?.StoreName || 'متجر LMIMA SHOP',

        phone: '0600000000',

        address: 'المملكة المغربية',

        generatedAt: new Date().toLocaleString('fr-FR')

      },

      customer: {

        id: custId,

        name: cust.FullName || debtObj.FullName || 'زبون كريدي',

        phone: cust.Phone || debtObj.Phone || '-',

        creditLimit: cust.CreditLimit || 1000,

        dueBalance: curDue,

        dueBalanceRial: curDue * 20

      },

      filter: {

        from: fromStr,

        to: toStr,

        periodLabel: periodLabel

      },

      purchases: filteredPurchases.map((p, idx) => ({

        id: idx + 1,

        productName: p.ProductName,

        quantity: p.Quantity,

        unitPrice: p.Price,

        subTotal: p.SubTotal,

        date: p.Date

      })),

      payments: filteredPayments.map((pay, idx) => ({

        id: idx + 1,

        amount: pay.Amount,

        date: pay.PaymentDate,

        note: pay.Note || 'تسديد نقدي للكريدي',

        cashier: pay.CashierName || 'المسؤول'

      })),

      summary: {

        totalPurchases: totPurchases,

        totalPurchasesRial: totPurchases * 20,

        totalPaid: totPaid,

        totalPaidRial: totPaid * 20,

        currentBalance: curDue,

        currentBalanceRial: curDue * 20

      }

    };

  }

  currentStatementData = statementResult;

  renderCustomerStatement(statementResult);

  if (spinner) spinner.style.display = 'none';

  if (content) content.style.opacity = '1';

}

function renderCustomerStatement(data) {

  if (!data) return;

  const setT = (id, val) => {

    const el = document.getElementById(id);

    if (el) el.textContent = val;

  };

  setT('stmt-store-name', data.storeInfo?.storeName || 'متجر LMIMA SHOP');

  setT('stmt-generated-at', data.storeInfo?.generatedAt || new Date().toLocaleString('fr-FR'));

  setT('stmt-cust-name', data.customer?.name || '--');

  setT('stmt-cust-phone', data.customer?.phone || 'بدون هاتف');

  setT('stmt-period-label', data.filter?.periodLabel || 'كافة المشتريات والمدفوعات');

  setT('stmt-invoice-code', `#CR-${String(data.customer?.id || 1).padStart(4, '0')}`);

  // لائحة المشتريات

  const purchases = data.purchases || [];

  setT('stmt-purchases-count', purchases.length);

  const pTbody = document.getElementById('stmt-purchases-tbody');

  if (pTbody) {

    if (purchases.length === 0) {

      pTbody.innerHTML = `<tr><td colspan="5" style="text-align:center; color:#64748b; padding:12px;">لا توجد مشتريات مسجلة ضمن هذه الفترة الزمنية</td></tr>`;

    } else {

      pTbody.innerHTML = purchases.map(p => `

        <tr>

          <td style="font-size: 10px; color: #64748b;">${p.date || '--'}</td>

          <td style="font-weight: 600;">${p.productName}</td>

          <td style="text-align: center; font-weight: 700;">${p.quantity}</td>

          <td style="text-align: left;">${formatDH(p.unitPrice)}</td>

          <td style="text-align: left; font-weight: 700; color: #1e3a8a;">${formatDH(p.subTotal)}</td>

        </tr>

      `).join('');

    }

  }

  setT('stmt-purchases-total-cell', formatDH(data.summary?.totalPurchases || 0));

  // لائحة المبالغ المدفوعة (التسديدات)

  const payments = data.payments || [];

  setT('stmt-payments-count', payments.length);

  const payTbody = document.getElementById('stmt-payments-tbody');

  if (payTbody) {

    if (payments.length === 0) {

      payTbody.innerHTML = `<tr><td colspan="3" style="text-align:center; color:#64748b; padding:12px;">لا توجد مبالغ مدفوعة أو تسديدات مسجلة ضمن هذه الفترة</td></tr>`;

    } else {

      payTbody.innerHTML = payments.map(pay => `

        <tr>

          <td style="font-size: 10px; color: #64748b;">${pay.date || '--'}</td>

          <td>${pay.note || 'تسديد نقدي للكريدي'}</td>

          <td style="text-align: left; font-weight: 700; color: #059669;">${formatDH(pay.amount)}</td>

        </tr>

      `).join('');

    }

  }

  setT('stmt-payments-total-cell', formatDH(data.summary?.totalPaid || 0));

  // الملخص المالي

  setT('stmt-sum-purchases', formatDH(data.summary?.totalPurchases || 0));

  setT('stmt-sum-paid', `- ${formatDH(data.summary?.totalPaid || 0)}`);

  const due = data.summary?.currentBalance ?? data.customer?.dueBalance ?? 0;

  setT('stmt-sum-remaining-dh', formatDH(due));

  setT('stmt-sum-remaining-rial', formatRial(due));

}

function printCustomerStatement() {

  if (!currentStatementData) {

    showToast('⚠️ لا يوجد كشف حساب معروض حالياً للطباعة');

    return;

  }

  window.print();

}

function downloadCustomerStatement() {

  if (!currentStatementData) {

    showToast('⚠️ يرجى عرض الفاتورة أولاً قبل التحميل');

    return;

  }

  const sheet = document.getElementById('statement-printable-sheet');

  if (!sheet) return;

  const custName = currentStatementData.customer?.name || 'زبون';

  const cleanName = custName.replace(/[^a-zA-Z0-9\u0600-\u06FF_-]/g, '_');

  const dateStr = new Date().toISOString().split('T')[0];

  const fileName = `Facture_Credit_${cleanName}_${dateStr}.html`;

  const fullHtml = `<!DOCTYPE html>

<html lang="ar" dir="rtl">

<head>

  <meta charset="UTF-8">

  <meta name="viewport" content="width=device-width, initial-scale=1.0">

  <title>فاتورة كشف حساب - ${custName}</title>

  <link rel="preconnect" href="https://fonts.googleapis.com">

  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>

  <link href="https://fonts.googleapis.com/css2?family=Cairo:wght@400;600;700;800;900&display=swap" rel="stylesheet">

  <style>

    * { box-sizing: border-box; margin: 0; padding: 0; }

    body {

      font-family: 'Cairo', -apple-system, sans-serif;

      background: #f1f5f9;

      color: #1e293b;

      padding: 24px 12px;

      direction: rtl;

    }

    .container {

      max-width: 680px;

      margin: 0 auto;

      background: #ffffff;

      border: 1px solid #cbd5e1;

      border-radius: 12px;

      padding: 20px;

      box-shadow: 0 4px 16px rgba(0,0,0,0.06);

    }

    .stmt-paper-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 12px; }

    .stmt-store-title { font-size: 18px; font-weight: 900; color: #1e3a8a; }

    .stmt-store-sub { font-size: 11px; color: #64748b; margin-top: 2px; }

    .stmt-doc-badge { background: #1e3a8a; color: white; font-size: 12px; font-weight: 800; padding: 4px 10px; border-radius: 6px; display: inline-block; }

    .stmt-date-badge { font-size: 10px; color: #64748b; margin-top: 4px; text-align: left; }

    .stmt-divider { height: 2px; background: linear-gradient(90deg, #1e3a8a, #3b82f6); margin: 12px 0; border-radius: 2px; }

    .stmt-customer-box { background: #f8fafc; border: 1px dashed #cbd5e1; border-radius: 8px; padding: 10px 14px; margin-bottom: 14px; }

    .stmt-cust-row { display: flex; justify-content: space-between; align-items: center; font-size: 12px; margin-bottom: 4px; }

    .stmt-section-title { font-size: 13px; font-weight: 800; color: #0f172a; margin: 14px 0 6px; }

    .stmt-table-wrapper { border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden; margin-bottom: 10px; }

    .stmt-table { width: 100%; border-collapse: collapse; font-size: 11px; }

    .stmt-table th { background: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 10px; border-bottom: 1px solid #cbd5e1; text-align: right; }

    .stmt-table td { padding: 7px 10px; border-bottom: 1px solid #f1f5f9; }

    .stmt-table tr:last-child td { border-bottom: none; }

    .stmt-table tfoot td { background: #f8fafc; border-top: 1.5px solid #cbd5e1; padding: 8px 10px; }

    .stmt-summary-card { background: #f8fafc; border: 1.5px solid #cbd5e1; border-radius: 8px; padding: 12px 16px; margin-top: 14px; }

    .stmt-summary-row { display: flex; justify-content: space-between; align-items: center; font-size: 12px; margin-bottom: 6px; }

    .stmt-summary-divider { height: 1px; background: #cbd5e1; margin: 8px 0; }

    .stmt-summary-row.highlight { font-size: 15px; font-weight: 900; margin-top: 6px; }

    .stmt-footer-note { text-align: center; font-size: 11px; color: #64748b; margin-top: 16px; padding-top: 10px; border-top: 1px dashed #e2e8f0; }

    .print-bar { text-align: center; margin-bottom: 14px; }

    .print-btn { background: #1e3a8a; color: white; border: none; padding: 8px 18px; border-radius: 6px; font-family: inherit; font-size: 13px; font-weight: 700; cursor: pointer; }

    @media print { .print-bar { display: none; } body { padding: 0; background: white; } .container { border: none; box-shadow: none; padding: 0; } }

  </style>

</head>

<body>

  <div class="container">

    <div class="print-bar">

      <button class="print-btn" onclick="window.print()">🖨️ طباعة الفاتورة أو حفظها كـ PDF</button>

    </div>

    ${sheet.innerHTML}

  </div>

</body>

</html>`;

  const blob = new Blob([fullHtml], { type: 'text/html;charset=utf-8' });

  const downloadLink = document.createElement('a');

  downloadLink.href = URL.createObjectURL(blob);

  downloadLink.download = fileName;

  document.body.appendChild(downloadLink);

  downloadLink.click();

  document.body.removeChild(downloadLink);

  URL.revokeObjectURL(downloadLink.href);

  showToast(`تم تحميل الفاتورة (${fileName}) بنجاح 📥`);

}

function sendStatementViaWhatsApp() {

  if (!currentStatementData) {

    showToast('⚠️ لا توجد بيانات كشف حساب للإرسال');

    return;

  }

  const cust = currentStatementData.customer || {};

  const purchases = currentStatementData.purchases || [];

  const payments = currentStatementData.payments || [];

  const summary = currentStatementData.summary || {};

  const store = currentStatementData.storeInfo || {};

  const cleanPhone = cleanMoroccanPhone(cust.phone);

  const storeName = store.storeName || 'متجر LMIMA SHOP';

  const period = currentStatementData.filter?.periodLabel || 'كافة المشتريات';

  let msg = `*${storeName}* 🏪\n`;

  msg += `*فاتورة وكشف حساب الكريدي* 📑\n`;

  msg += `━━━━━━━━━━━━━━━━━━━━\n`;

  msg += `👤 *الزبون:* ${cust.name || ''}\n`;

  if (cust.phone && cust.phone !== '-') msg += `📱 *الهاتف:* ${cust.phone}\n`;

  msg += `📅 *الفترة:* ${period}\n`;

  msg += `🕒 *تاريخ التوليد:* ${store.generatedAt || new Date().toLocaleString('fr-FR')}\n`;

  msg += `━━━━━━━━━━━━━━━━━━━━\n\n`;

  // مشتريات

  msg += `🛒 *لائحة المشتريات (${purchases.length}):*\n`;

  if (purchases.length === 0) {

    msg += `(لا توجد مشتريات مسجلة خلال هذه الفترة)\n`;

  } else {

    const displayList = purchases.slice(0, 25);

    displayList.forEach(p => {

      msg += `• ${p.productName} × ${p.quantity} = ${p.subTotal?.toFixed(2)} د.م.\n`;

    });

    if (purchases.length > 25) {

      msg += `... ومشتريات أخرى (${purchases.length - 25} بنداً إضافياً)\n`;

    }

  }

  msg += `📊 *مجموع المشتريات:* ${summary.totalPurchases?.toFixed(2)} د.م. (${Math.round((summary.totalPurchases || 0) * 20).toLocaleString()} ريال)\n\n`;

  // دفعات

  msg += `💵 *المبالغ المسددة (${payments.length}):*\n`;

  if (payments.length === 0) {

    msg += `(لم يتم تسجيل دفعات خلال هذه الفترة)\n`;

  } else {

    payments.slice(0, 10).forEach(pay => {

      msg += `• ${pay.date ? pay.date.split(' ')[0] : ''}: ${pay.amount?.toFixed(2)} د.م. (${pay.note || 'تسديد'})\n`;

    });

    if (payments.length > 10) {

      msg += `... وتسديدات أخرى\n`;

    }

  }

  msg += `✅ *إجمالي المسدد:* ${summary.totalPaid?.toFixed(2)} د.م.\n\n`;

  // الصافي المتبقي

  const due = summary.currentBalance ?? cust.dueBalance ?? 0;

  msg += `━━━━━━━━━━━━━━━━━━━━\n`;

  msg += `⚠️ *صافي الكريدي المتبقي بذمتكم حالياً:*\n`;

  msg += `💰 *${due.toFixed(2)} درهم مغربي*\n`;

  msg += `🪙 *(${Math.round(due * 20).toLocaleString()} ريال)*\n`;

  msg += `━━━━━━━━━━━━━━━━━━━━\n`;

  msg += `نشكركم على حسن تعاملكم ووفائكم! 🙏\n${storeName}`;

  const waUrl = cleanPhone ? `https://wa.me/${cleanPhone}?text=${encodeURIComponent(msg)}` : `https://wa.me/?text=${encodeURIComponent(msg)}`;

  window.open(waUrl, '_blank');

}

// ==========================================================================

// 9. إدارة الممونين والشركات (Suppliers Tab & CRUD)

// ==========================================================================

function renderSuppliersTab() {

  const container = document.getElementById('suppliers-list-container');

  if (!container) return;

  const suppliers = AppState.data?.Suppliers || [];

  const search = (AppState.suppliersSearch || '').trim().toLowerCase();

  const filter = AppState.suppliersFilter;

  let totalDebt = 0;

  suppliers.forEach(s => totalDebt += (parseFloat(s.DueBalance) || 0));

  const setT = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };

  setT('suppliers-total-debts-dh', formatDH(totalDebt));

  setT('suppliers-total-debts-rial', formatRial(totalDebt));

  setT('suppliers-count-badge', `${suppliers.length} ممون`);

  setT('supp-count-all', suppliers.length);

  const filtered = suppliers.filter(s => {

    const bal = parseFloat(s.DueBalance) || 0;

    if (filter === 'debts' && bal <= 0) return false;

    if (filter === 'zero' && bal > 0) return false;

    if (!search) return true;

    return (s.FullName && s.FullName.toLowerCase().includes(search)) ||

           (s.Company && s.Company.toLowerCase().includes(search)) ||

           (s.Phone && s.Phone.includes(search));

  });

  if (filtered.length === 0) {

    container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 30px;">لا يوجد ممونون مطابقون للبحث</div>';

    return;

  }

  container.innerHTML = filtered.map(s => {

    const bal = parseFloat(s.DueBalance) || 0;

    const hasPhone = s.Phone && s.Phone !== '-' && s.Phone.length >= 8;

    const cleanPhone = cleanMoroccanPhone(s.Phone);

    const waUrl = hasPhone ? `https://wa.me/${cleanPhone}` : `https://wa.me/`;

    return `

      <div class="supplier-card">

        <div class="supplier-header">

          <div>

            <div class="supplier-name">🚚 ${s.FullName}</div>

            <div class="supplier-phone">🏢 ${s.Company || 'ممون عام'} • 📱 ${hasPhone ? s.Phone : 'بدون هاتف'}</div>

          </div>

          <div class="debt-amount-box">

            <div class="debt-dh">${formatDH(bal)}</div>

            <div class="debt-rial">${formatRial(bal)}</div>

          </div>

        </div>

        <div class="supplier-meta">

          <span>العنوان: ${s.Address || 'غير محدد'}</span>

          <span>${bal > 0 ? '⚠️ مستحقات واجبة له' : '✅ تم سداد كافة المستحقات'}</span>

        </div>

        <div class="card-actions-row">

          <a href="${waUrl}" target="_blank" class="card-action-btn" style="color: #16a34a;">

            <span>💬 واتساب</span>

          </a>

          ${hasPhone ? `

            <a href="tel:${s.Phone}" class="card-action-btn" style="color: #0284c7;">

              <span>📞 اتصال</span>

            </a>

          ` : ''}

          <button class="card-action-btn btn-edit" onclick="editSupplier(${s.Id})">

            <span>✏️ تعديل</span>

          </button>

          <button class="card-action-btn btn-delete" onclick="deleteSupplier(${s.Id}, '${s.FullName.replace(/'/g, "\\'")}')">

            <span>🗑️ حذف</span>

          </button>

        </div>

      </div>

    `;

  }).join('');

}

function openAddSupplierModal(supplier = null) {

  openModal('modal-supplier');

  const title = document.getElementById('modal-supplier-title');

  const idInp = document.getElementById('supp-id');

  const nameInp = document.getElementById('supp-name');

  const compInp = document.getElementById('supp-company');

  const phoneInp = document.getElementById('supp-phone');

  const addrInp = document.getElementById('supp-address');

  const dueInp = document.getElementById('supp-due');

  if (supplier && supplier.Id) {

    if (title) title.textContent = '✏️ تعديل بيانات الممون';

    if (idInp) idInp.value = supplier.Id;

    if (nameInp) nameInp.value = supplier.FullName || '';

    if (compInp) compInp.value = supplier.Company || '';

    if (phoneInp) phoneInp.value = supplier.Phone || '';

    if (addrInp) addrInp.value = supplier.Address || '';

    if (dueInp) dueInp.value = supplier.DueBalance || 0;

  } else {

    if (title) title.textContent = '➕ إضافة ممون أو شركة جديدة';

    if (idInp) idInp.value = '0';

    if (nameInp) nameInp.value = '';

    if (compInp) compInp.value = '';

    if (phoneInp) phoneInp.value = '';

    if (addrInp) addrInp.value = '';

    if (dueInp) dueInp.value = '0.00';

  }

}

function editSupplier(id) {

  const supp = (AppState.data?.Suppliers || []).find(s => s.Id === id);

  if (supp) openAddSupplierModal(supp);

}

async function saveSupplierFromModal() {

  const id = parseInt(document.getElementById('supp-id')?.value || '0');

  const name = (document.getElementById('supp-name')?.value || '').trim();

  const company = (document.getElementById('supp-company')?.value || '').trim();

  const phone = (document.getElementById('supp-phone')?.value || '').trim();

  const address = (document.getElementById('supp-address')?.value || '').trim();

  const due = parseFloat(document.getElementById('supp-due')?.value || '0');

  if (!name) {

    showToast('⚠️ يرجى كتابة اسم الممون');

    return;

  }

  const payload = {

    id,

    fullName: name,

    company,

    phone,

    email: '',

    address,

    dueBalance: due

  };

  showToast('جاري حفظ الممون...');

  closeModal('modal-supplier');

  const res = await postToServer('/api/suppliers/save', payload);

  if (res && res.success) {

    showToast('تم حفظ الممون وتحديث الحاسوب بنجاح! ⚡');

  } else {

    queueOfflineAction('supplier_save', payload);

    showToast('تم حفظ الممون محلياً وسيتم رفعه للحاسوب 📲');

  }

  // تحديث محلي

  if (!AppState.data) AppState.data = {};

  if (!AppState.data.Suppliers) AppState.data.Suppliers = [];

  const existingIdx = AppState.data.Suppliers.findIndex(s => s.Id === id && id > 0);

  const updatedSupp = {

    Id: (id > 0) ? id : (res?.id || Math.floor(1000 + Math.random() * 9000)),

    FullName: name,

    Company: company,

    Phone: phone,

    Email: '',

    Address: address,

    DueBalance: due,

    DueBalanceRial: due * 20

  };

  if (existingIdx >= 0) {

    AppState.data.Suppliers[existingIdx] = updatedSupp;

  } else {

    AppState.data.Suppliers.unshift(updatedSupp);

  }

  saveDataLocally();

  renderSuppliersTab();

}

async function deleteSupplier(id, name) {

  if (!confirm(`هل أنت متأكد من حذف الممون: "${name}"؟`)) return;

  showToast('جاري حذف الممون...');

  const res = await postToServer('/api/suppliers/delete', { id });

  if (res && res.success) {

    showToast('تم حذف الممون بنجاح! 🗑️');

  } else {

    queueOfflineAction('supplier_delete', { id });

    showToast('تم الحذف محلياً وسيتم إبلاغ الحاسوب 📲');

  }

  if (AppState.data?.Suppliers) {

    AppState.data.Suppliers = AppState.data.Suppliers.filter(s => s.Id !== id);

    saveDataLocally();

    renderSuppliersTab();

  }

}

// ==========================================================================

// 10. شاشة المبيعات والتقارير (Dashboard & Invoices)

// ==========================================================================

function renderDashboard() {

  const d = AppState.data?.TodaySummary || {};

  const dateEl = document.getElementById('today-date-display');

  if (dateEl) dateEl.textContent = d.Date || new Date().toLocaleDateString('fr-FR');

  const setT = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };

  setT('today-sales-dh', formatDH(d.TotalSales));

  setT('today-sales-rial', formatRial(d.TotalSales));

  setT('today-profit-dh', '+' + formatDH(d.EstimatedProfit));

  setT('today-profit-rial', formatRial(d.EstimatedProfit));

  setT('today-invoices-count', (d.InvoicesCount || 0) + ' فاتورة');

  setT('today-cash-dh', formatDH(d.CashSales));

  setT('today-cash-rial', formatRial(d.CashSales));

  setT('today-credit-dh', formatDH(d.CreditSales));

  setT('today-credit-rial', formatRial(d.CreditSales));

  renderInvoicesList();

  updateOfflineQueueBadge();

}

function renderInvoicesList() {

  const container = document.getElementById('invoices-list');

  if (!container) return;

  const invoices = AppState.data?.RecentInvoices || [];

  const search = (document.getElementById('invoice-search-input')?.value || '').trim().toLowerCase();

  const filtered = invoices.filter(inv => {

    if (!search) return true;

    const matchId = String(inv.InvoiceId).includes(search);

    const matchCust = inv.Customer && inv.Customer.toLowerCase().includes(search);

    const matchCashier = inv.Cashier && inv.Cashier.toLowerCase().includes(search);

    const matchItems = inv.Items && inv.Items.some(it => it.ProductName && it.ProductName.toLowerCase().includes(search));

    return matchId || matchCust || matchCashier || matchItems;

  });

  const badge = document.getElementById('invoices-counter-badge');

  if (badge) badge.textContent = `${filtered.length} فاتورة`;

  if (filtered.length === 0) {

    container.innerHTML = '<div style="text-align: center; color: var(--text-muted); padding: 24px;">لا توجد فواتير مطابقة</div>';

    return;

  }

  container.innerHTML = filtered.map(inv => {

    const isCredit = inv.PaymentMethod?.includes('كريدي') || (inv.Customer && !inv.Customer.includes('نقدي'));

    const badgeClass = isCredit ? 'badge-credit' : 'badge-cash';

    const badgeText = isCredit ? 'كريدي (دين)' : 'كاش (نقداً)';

    const isExpanded = AppState.expandedInvoices.has(inv.InvoiceId);

    const items = inv.Items || [];

    let itemsHtml = '';

    if (items.length > 0) {

      itemsHtml = `

        <table class="invoice-items-table">

          <thead>

            <tr>

              <th>السلعة</th>

              <th style="text-align:center;">الكمية</th>

              <th style="text-align:left;">الثمن</th>

              <th style="text-align:left;">المجموع</th>

            </tr>

          </thead>

          <tbody>

            ${items.map(it => `

              <tr>

                <td>${it.ProductName}</td>

                <td style="text-align:center; font-weight:700;">${it.Quantity}</td>

                <td style="text-align:left;">${formatDH(it.UnitPrice)}</td>

                <td style="text-align:left; font-weight:700;">${formatDH(it.SubTotal)}</td>

              </tr>

            `).join('')}

          </tbody>

        </table>

      `;

    }

    return `

      <div class="invoice-card">

        <div class="invoice-header-row" onclick="toggleInvoiceItems(${inv.InvoiceId})">

          <div>

            <div style="font-weight: 800; font-size: 13px;">فاتورة #${inv.InvoiceId} - ${inv.Customer || 'زبون نقدي'}</div>

            <div style="font-size: 11px; color: var(--text-muted); margin-top: 2px;">

              <span>🕒 ${inv.Date || ''}</span> • 

              <span class="badge ${badgeClass}">${badgeText}</span>

            </div>

          </div>

          <div style="text-align: left;">

            <div style="font-size: 15px; font-weight: 800; color: var(--primary-accent);">${formatDH(inv.TotalAmount)}</div>

            <div style="font-size: 11px; color: #b45309;">${formatRial(inv.TotalAmount)}</div>

          </div>

        </div>

        <button class="card-action-btn" style="width: 100%; margin-top: 6px;" onclick="toggleInvoiceItems(${inv.InvoiceId})">

          <span>${isExpanded ? '▲ إخفاء تفاصيل السلع' : `▼ تفاصيل السلع المشتراة (${items.length})`}</span>

        </button>

        <div class="invoice-items-drawer ${isExpanded ? 'open' : ''}">

          ${itemsHtml}

        </div>

      </div>

    `;

  }).join('');

}

function toggleInvoiceItems(invId) {

  if (AppState.expandedInvoices.has(invId)) {

    AppState.expandedInvoices.delete(invId);

  } else {

    AppState.expandedInvoices.add(invId);

  }

  renderInvoicesList();

}

// ==========================================================================

// 11. محرك المزامنة الفورية ثنائية الاتجاه وطابور عدم الاتصال

// ==========================================================================

// ترويسة المصادقة: كل طلب بيانات يحمل توكن الهاتف المقترن
function authHeaders(extra) {

  const headers = Object.assign({}, extra || {});

  const token = localStorage.getItem('lmima_device_token');

  if (token) headers['Authorization'] = 'Bearer ' + token;

  return headers;

}

function handleUnauthorized() {

  if (AppState._pairingWarned) return;

  AppState._pairingWarned = true;

  showToast('🔒 هذا الهاتف غير مقترن بالحاسوب. افتح "اقتران الهاتف" في الحاسوب وأدخل الرمز الظاهر.');

  setTimeout(() => { AppState._pairingWarned = false; }, 30000);

}

// تعيد: كائن JSON من الخادم (نجاح أو رفض)، أو null عند انقطاع الاتصال فقط

async function postToServer(endpoint, payload, timeoutMs) {

  if (!AppState.serverIp) return null;

  const cleanHost = AppState.serverIp.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

  const url = `http://${cleanHost}${endpoint}`;

  const controller = new AbortController();

  const timeoutId = setTimeout(() => controller.abort(), timeoutMs || 8000);

  try {

    const res = await fetch(url, {

      method: 'POST',

      headers: authHeaders({ 'Content-Type': 'application/json; charset=utf-8' }),

      body: JSON.stringify(payload),

      signal: controller.signal,

      mode: 'cors'

    });

    clearTimeout(timeoutId);

    if (res.status === 401) handleUnauthorized();

    let json = null;

    try { json = await res.json(); } catch (e) { json = null; }

    if (res.ok) return json || { success: true };

    return Object.assign({ success: false }, json || {}, { httpStatus: res.status });

  } catch (e) {

    clearTimeout(timeoutId);

  }

  return null;

}

// العمليات التي تُعاد كما هي إلى نفس نقطة الـ API عند عودة الاتصال

const OFFLINE_HTTP_ENDPOINTS = {

  product_save: '/api/products/save',

  product_delete: '/api/products/delete',

  customer_save: '/api/customers/save',

  customer_delete: '/api/customers/delete',

  supplier_save: '/api/suppliers/save',

  supplier_delete: '/api/suppliers/delete'

};

// العمليات التي يعالجها الحاسوب عبر /api/sync/push (مع منع التكرار بواسطة op_id)

const V2_PUSH_TYPES = ['pos_checkout', 'create_invoice', 'stock_movement', 'upsert_customer', 'add_debt_payment'];

async function queueOfflineAction(type, payload) {

  const base = {

    status: 'pending',

    retry_count: 0,

    client_created_at: new Date().toISOString()

  };

  if (type === 'pos_checkout') {

    // البيع كاملاً كعملية واحدة: الحاسوب يسجل الفاتورة ويخصم المخزون ويحدّث دين الزبون دفعة واحدة

    await IDB.put('offline_queue', Object.assign({}, base, {

      op_id: payload.opId || generateUUID(),

      type: 'pos_checkout',

      payload: payload

    }));

  }

  else if (type === 'customer_pay_debt') {

    const cust = (AppState.data?.Customers || []).find(c => c.Id === payload.id);

    await IDB.put('offline_queue', Object.assign({}, base, {

      op_id: generateUUID(),

      type: 'add_debt_payment',

      payload: {

        customer_uuid: cust?.uuid || null,

        customer_id: payload.id,

        amount: payload.amount,

        date: new Date().toISOString()

      }

    }));

  }

  else if (OFFLINE_HTTP_ENDPOINTS[type]) {

    await IDB.put('offline_queue', Object.assign({}, base, {

      op_id: generateUUID(),

      type: type,

      payload: payload

    }));

  }

  else {

    console.warn('Unsupported offline operation:', type);

  }

  updateOfflineQueueBadge();

  flushOfflineQueue();

}

async function flushOfflineQueue() {

  if (AppState.isFlushingQueue) return;

  AppState.isFlushingQueue = true;

  try {

    // 1. أولاً: الزبائن والمنتجات المضافة دون اتصال، حتى تجدها المبيعات المعلقة عند وصولها للحاسوب

    const queue = (await IDB.getAll('offline_queue')) || [];

    const httpOps = queue

      .filter(q => q.status === 'pending' && OFFLINE_HTTP_ENDPOINTS[q.type])

      .sort((a, b) => String(a.client_created_at).localeCompare(String(b.client_created_at)));

    for (const item of httpOps) {

      const res = await postToServer(OFFLINE_HTTP_ENDPOINTS[item.type], item.payload);

      if (res === null) break; // لا يوجد اتصال: نحاول لاحقاً

      if (res.success) {

        await IDB.delete('offline_queue', item.op_id);

        if (item.type === 'product_save' && item.payload?.barcode) {

          removePendingDrivePhoto(item.payload.barcode);

        }

      } else if (res.httpStatus === 401) {

        break; // يحتاج اقتران

      } else {

        item.retry_count = (item.retry_count || 0) + 1;

        item.last_error = res.error || '';

        await IDB.put('offline_queue', item);

      }

    }

    // 2. ثم المبيعات والتسديدات

    await pushOfflineQueueV2();

  } catch (e) {

    console.warn('flushOfflineQueue error:', e);

  } finally {

    AppState.isFlushingQueue = false;

  }

  updateOfflineQueueBadge();

}

// تصدير العمليات المعلقة كملف يُحفظ في Google Drive، فيستوردها الحاسوب تلقائياً (للعمل خارج المحل)

async function exportPendingOpsForDrive() {

  const queue = (await IDB.getAll('offline_queue')) || [];

  const ops = queue.filter(q => q.status === 'pending' && V2_PUSH_TYPES.includes(q.type));

  if (ops.length === 0) {

    showToast('لا توجد مبيعات أو تسديدات معلقة للتصدير ✅');

    return;

  }

  const payload = {

    device_id: localStorage.getItem('lmima_device_id') || 'mobile',

    exported_at: new Date().toISOString(),

    operations: ops.map(p => ({ op_id: p.op_id, type: p.type, payload: p.payload, client_created_at: p.client_created_at }))

  };

  const stamp = new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14);

  const fileName = `lmima_pending_ops_${stamp}.json`;

  const blob = new Blob([JSON.stringify(payload)], { type: 'application/json' });

  const file = new File([blob], fileName, { type: 'application/json' });

  try {

    if (navigator.canShare && navigator.canShare({ files: [file] })) {

      await navigator.share({ files: [file], title: fileName, text: 'احفظ هذا الملف في مجلد Lmima Shop على Google Drive' });

    } else {

      const a = document.createElement('a');

      a.href = URL.createObjectURL(blob);

      a.download = fileName;

      document.body.appendChild(a);

      a.click();

      setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);

    }

    showToast(`📤 تم تصدير ${ops.length} عملية. احفظ الملف في مجلد Google Drive الخاص بالمتجر، وسيستوردها الحاسوب تلقائياً دون تكرار.`);

  } catch (e) {

    if (e && e.name !== 'AbortError') showToast('تعذر تصدير الملف: ' + e.message);

  }

}

async function updateOfflineQueueBadge() {

  const btn = document.getElementById('btn-flush-offline-queue');

  const desc = document.getElementById('offline-queue-desc');

  const queue = await IDB.getAll('offline_queue');

  const count = queue.filter(q => q.status === 'pending').length;

  if (btn) {

    btn.textContent = count + ' عمليات معلقة';

    btn.className = count > 0 ? 'badge badge-credit' : 'badge badge-cash';

  }

  if (desc) {

    desc.textContent = count > 0 ? ('يوجد ' + count + ' عمليات مبيعات متوقفة حتى يعود الاتصال') : 'لا توجد عمليات معلقة حالياً';

  }

}

async function probeServerIp(ip, port = 8080) {

  try {

    const controller = new AbortController();

    const timeoutId = setTimeout(() => controller.abort(), 1200);

    const res = await fetch(`http://${ip}:${port}/api/ping?t=${Date.now()}`, {

      signal: controller.signal,

      mode: 'cors'

    });

    clearTimeout(timeoutId);

    if (res.ok) {

      const data = await res.json();

      if (data && data.status === 'online') {

        return `${ip}:${port}`;

      }

    }

  } catch (e) {}

  return null;

}

function setDiscoveredServer(serverHost) {

  const changed = (AppState.serverIp !== serverHost);

  AppState.serverIp = serverHost;

  localStorage.setItem('lmima_server_ip', serverHost);

  const inp = document.getElementById('sync-server-ip');

  if (inp) inp.value = serverHost;

  if (changed) {

    startSignalR();

    startSseConnection();

  }

}

async function autoDiscoverServer(showToasts = false) {

  const progressEl = document.getElementById('auto-discover-progress-text');

  if (progressEl) {

    progressEl.style.display = 'block';

    progressEl.textContent = '🔍 جاري فحص الشبكة والبحث عن الحاسوب...';

  }

  if (showToasts) showToast('🔍 جاري البحث عن الحاسوب بشبكة الـ Wi-Fi...');

  // 1. أولاً: فحص العنوان المخزن أو الافتراضي

  const currentHost = (AppState.serverIp || localStorage.getItem('lmima_server_ip') || '192.168.11.117:8080');

  const [cIp, cPort] = currentHost.replace(/^https?:\/\//, '').replace(/\/$/, '').split(':');

  if (cIp) {

    const ok = await probeServerIp(cIp, cPort || 8080);

    if (ok) {

      if (progressEl) progressEl.style.display = 'none';

      setDiscoveredServer(ok);

      if (showToasts) showToast(`تم الاتصال بالحاسوب: ${ok} 🟢`);

      return ok;

    }

  }

  // 2. إذا كان التطبيق مفتوحاً بالمتصفح على البورت 8080

  if (window.location.port === '8080' && window.location.hostname && window.location.hostname !== 'localhost') {

    const ok = await probeServerIp(window.location.hostname, window.location.port);

    if (ok) {

      if (progressEl) progressEl.style.display = 'none';

      setDiscoveredServer(ok);

      if (showToasts) showToast(`تم الاتصال بالحاسوب: ${ok} 🟢`);

      return ok;

    }

  }

  // 3. تحديد شبكات البحث (Subnets)

  const subnetsToScan = [];

  const parts = cIp ? cIp.split('.') : [];

  if (parts.length === 4) {

    subnetsToScan.push(`${parts[0]}.${parts[1]}.${parts[2]}`);

  }

  if (!subnetsToScan.includes('192.168.11')) subnetsToScan.push('192.168.11');

  if (!subnetsToScan.includes('192.168.1')) subnetsToScan.push('192.168.1');

  if (!subnetsToScan.includes('192.168.0')) subnetsToScan.push('192.168.0');

  if (!subnetsToScan.includes('192.168.8')) subnetsToScan.push('192.168.8');

  if (!subnetsToScan.includes('10.0.0')) subnetsToScan.push('10.0.0');

  // 4. الأرقام ذات الأولوية (راوترات المنازل والمحلات تمنح من 100 إلى 140، و117 موجود هنا!)

  const priorityIps = [117, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 118, 119, 120, 121, 122, 123, 124, 125, 128, 130, 150, 159, 2, 3, 4, 5, 10, 50];

  for (const subnet of subnetsToScan) {

    if (progressEl) progressEl.textContent = `جاري فحص النطاق ${subnet}.X...`;

    // فحص الأرقام ذات الأولوية بدفعات سريعة

    for (let i = 0; i < priorityIps.length; i += 20) {

      const batch = priorityIps.slice(i, i + 20).map(n => probeServerIp(`${subnet}.${n}`, 8080));

      const results = await Promise.all(batch);

      const found = results.find(r => !!r);

      if (found) {

        if (progressEl) progressEl.style.display = 'none';

        setDiscoveredServer(found);

        if (showToasts) showToast(`تم العثور على الحاسوب تلقائياً: ${found} 🟢`);

        return found;

      }

    }

    // فحص باقي العناوين حتى 254

    if (showToasts) {

      const remaining = [];

      for (let n = 1; n <= 254; n++) {

        if (!priorityIps.includes(n)) remaining.push(n);

      }

      for (let i = 0; i < remaining.length; i += 25) {

        const batch = remaining.slice(i, i + 25).map(n => probeServerIp(`${subnet}.${n}`, 8080));

        const results = await Promise.all(batch);

        const found = results.find(r => !!r);

        if (found) {

          if (progressEl) progressEl.style.display = 'none';

          setDiscoveredServer(found);

          showToast(`تم العثور على الحاسوب تلقائياً: ${found} 🟢`);

          return found;

        }

      }

    }

  }

  if (progressEl) progressEl.style.display = 'none';

  if (showToasts) showToast('تعذر العثور على الحاسوب تلقائياً. امسح الباركود 📷 أو أدخل IP يدوياً');

  return null;

}

async function fetchLiveSync(host, allowAutoDiscover = true) {

  if (!host) return false;

  let cleanHost = host.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

  const url = `http://${cleanHost}/api/sync?t=${Date.now()}`;

  const controller = new AbortController();

  const timeoutId = setTimeout(() => controller.abort(), 3500);

  try {

    const res = await fetch(url, { signal: controller.signal, mode: 'cors', headers: authHeaders() });

    clearTimeout(timeoutId);

    if (res.status === 401) handleUnauthorized();

    if (res.ok) {

      const json = await res.json();

      if (json && (json.StoreInfo || json.Products || json.TodaySummary)) {

        applySyncData(json, true);

        updateServerStatus(true, `متصل ومُحدث تلقائياً (${new Date().toLocaleTimeString('fr-FR')})`);

        flushOfflineQueue();

        return true;

      }

    }

  } catch (e) {

    clearTimeout(timeoutId);

  }

  // إذا فشل الاتصال بالعنوان المحدد، نقوم بالبحث التلقائي الذكي عن الحاسوب في الشبكة!

  if (allowAutoDiscover) {

    const found = await autoDiscoverServer(false);

    if (found && found !== cleanHost) {

      return pushOfflineQueueV2(); await fetchDeltaSync(found, false);

    }

  }

  return false;

}

function applySyncData(data, saveToStorage = true) {

  if (!data) return;

  AppState.data = data;

  if (saveToStorage) {

    saveDataLocally();

  }

  const ts = data.StoreInfo?.LastSyncTimestamp || new Date().toLocaleTimeString('fr-FR');

  updateSyncStatus('online', `🟢 متصل ومُحدث (${ts})`);

  if (data.StoreInfo?.StoreName) {

    const el = document.getElementById('store-name-display');

    if (el) el.textContent = data.StoreInfo.StoreName;

  }

  const prodCount = data.Products?.length || 0;

  const custCount = data.Customers?.length || data.CustomerDebts?.length || 0;

  const suppCount = data.Suppliers?.length || 0;

  const recEl = document.getElementById('records-count-text');

  if (recEl) recEl.textContent = `${prodCount} منتج • ${custCount} زبون • ${suppCount} ممون`;

  renderPOSGrid();

  renderProductsTab();

  renderCustomersTab();

  renderSuppliersTab();

  renderDashboard();

  populateCartCustomerSelect();

}

function saveDataLocally() {

  try {

    localStorage.setItem('lmima_sync_data', JSON.stringify(AppState.data));

  } catch (e) {}

}

function updateSyncStatus(state, text) {

  const statusEl = document.getElementById('sync-status-text');

  const pulseEl = document.getElementById('sync-pulse');

  const headerPulse = document.getElementById('header-status-badge');

  if (statusEl) statusEl.textContent = text;

  const cl = 'pulse-dot ' + (state === 'online' ? '' : (state === 'local' ? 'warning' : 'danger'));

  if (pulseEl) pulseEl.className = cl;

  if (headerPulse) headerPulse.className = cl;

}

function updateServerStatus(isOnline, descText) {

  const badge = document.getElementById('server-status-badge');

  const desc = document.getElementById('server-status-desc');

  if (desc) desc.textContent = descText;

  if (badge) {

    badge.className = isOnline ? 'badge badge-cash' : 'badge badge-credit';

    badge.textContent = isOnline ? 'متصل بالخادم' : 'غير متصل';

  }

}

// --- Phase 3: Server-Sent Events (SSE) Push Updates ---

let sseEventSource = null;

let sseReconnectDelay = 1000;

let isSseConnected = false;

function startSseConnection() {

  if (!AppState.serverIp) return;

  if (sseEventSource) {

    sseEventSource.close();

  }

  const cleanHost = AppState.serverIp.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

  const sseToken = localStorage.getItem('lmima_device_token');

  if (!sseToken) return; // البث اللحظي متاح للهواتف المقترنة فقط

  // EventSource لا يدعم الترويسات، لذلك يمرر التوكن في الرابط (مقبول لهذا المسار فقط)

  const url = 'http://' + cleanHost + '/api/events?token=' + encodeURIComponent(sseToken);

  console.log('Connecting to SSE: http://' + cleanHost + '/api/events');

  sseEventSource = new EventSource(url);

  sseEventSource.onopen = () => {

    console.log('SSE Connected successfully.');

    isSseConnected = true;

    sseReconnectDelay = 1000; // Reset backoff

    document.getElementById('sync-pulse')?.style.setProperty('background-color', '#10B981', 'important');

    const sseBadge = document.getElementById('diag-sse-badge');
    if (sseBadge) {
      sseBadge.textContent = 'نشط ومتصل 🟢';
      sseBadge.className = 'badge badge-cash';
    }

    // Once connected, fetch latest data to ensure we didn't miss anything during disconnect

    flushOfflineQueue(); fetchDeltaSync(AppState.serverIp);

  };

  sseEventSource.addEventListener('connected', (e) => {

    console.log('SSE Handshake:', e.data);

  });

  // الحاسوب نزّل إصداراً جديداً من التطبيق

  sseEventSource.addEventListener('app_update', () => { checkForAppUpdates(false); });

  sseEventSource.addEventListener('changed', (e) => {

    console.log('SSE Change Event received! Fetching live sync immediately...', e.data);

    // Instant fetch!

    flushOfflineQueue(); fetchDeltaSync(AppState.serverIp);

  });

  sseEventSource.onerror = (err) => {

    console.warn('SSE Connection Error. Retrying...', err);

    isSseConnected = false;

    const sseBadge = document.getElementById('diag-sse-badge');
    if (sseBadge) {
      sseBadge.textContent = 'غير متصل 🔴';
      sseBadge.className = 'badge badge-credit';
    }

    sseEventSource.close();

    // Exponential backoff with jitter

    const jitter = Math.random() * 500;

    setTimeout(() => {

      sseReconnectDelay = Math.min(sseReconnectDelay * 2, 30000); // max 30s

      startSseConnection();

    }, sseReconnectDelay + jitter);

  };

}

function startAutoSyncLoop() {
  if (AppState.autoSyncTimer) clearInterval(AppState.autoSyncTimer);
  if (!AppState.autoSyncEnabled) return;

  const intervalSetting = localStorage.getItem('lmima_sync_interval') || 'live';
  let intervalMs = 15000;
  if (intervalSetting === '15') intervalMs = 15000;
  else if (intervalSetting === '30') intervalMs = 30000;
  else if (intervalSetting === '60') intervalMs = 60000;
  else if (intervalSetting === '300') intervalMs = 300000;
  else if (intervalSetting === 'live') intervalMs = 10000;

  AppState.autoSyncTimer = setInterval(async () => {
    if (AppState.isSyncing) return;
    AppState.isSyncing = true;
    try {
      await pushOfflineQueueV2();
      if (!isSseConnected || intervalSetting !== 'live') {
        let ok = await fetchDeltaSync(AppState.serverIp);
        if (!ok && typeof fetchLiveSync === 'function') {
          await fetchLiveSync(AppState.serverIp, false);
        }
      }
      checkForAppUpdates();
    } catch (e) {}
    finally {
      AppState.isSyncing = false;
    }
  }, intervalMs);
}

window.addEventListener('online', async () => {
  showToast('تمت استعادة اتصال الشبكة 🌐 جاري التحديث التلقائي...');
  await pushOfflineQueueV2();
  await fetchDeltaSync(AppState.serverIp);
  startSseConnection();
});

document.addEventListener('visibilitychange', async () => {
  if (document.visibilityState === 'visible' && AppState.autoSyncEnabled) {
    await pushOfflineQueueV2();
    await fetchDeltaSync(AppState.serverIp);
  }
});

async function initData() {

  // 1. قراءة البيانات المحفوظة محلياً

    // Phase 4: Init IndexedDB and Migrate

  try {

    await IDB.init();

    await migrateFromV1ToV2();

    await rebuildAppStateFromIDB();

    // Attempt push immediately

    pushOfflineQueueV2();

  } catch (e) {

    console.error("IDB init error", e);

  }

    const saved = localStorage.getItem('lmima_sync_data');

  if (saved) {

    try {

      const parsed = JSON.parse(saved);

      applySyncData(parsed, false);

      updateSyncStatus('local', 'عرض البيانات المحفوظة محلياً...');

    } catch (e) {}

  }

  // 2. إذا توفرت بيانات مضمنة من المزامنة السحابية (Google Drive)

  if (window.LMIMA_SYNC_DATA && typeof window.LMIMA_SYNC_DATA === 'object') {

    const cloudTime = window.LMIMA_SYNC_DATA.StoreInfo?.LastSyncTimestamp || '';

    const localTime = AppState.data?.StoreInfo?.LastSyncTimestamp || '';

    // إذا كانت بيانات السحابة أحدث من البيانات المحلية أو لم تكن هناك بيانات محلية، قم بتطبيقها

    if (!AppState.data || cloudTime > localTime) {

      console.log('Applying newer cloud sync data...');

      applySyncData(window.LMIMA_SYNC_DATA, true);

    }

  }

  // 3. المزامنة المباشرة مع الخادم
  updateServerStatus(false, 'جاري الاتصال بخادم المتجر...');
  await flushOfflineQueue();
  let ok = await fetchDeltaSync(AppState.serverIp);
  if (!ok && typeof fetchLiveSync === 'function') {
    ok = await fetchLiveSync(AppState.serverIp, false);
  }

  if (!ok) {

    try {

      const relRes = await fetch('lmima_mobile_sync.json?t=' + Date.now());

      if (relRes.ok) {

        const relData = await relRes.json();

        applySyncData(relData, true);

      }

    } catch (e) {}

  }

  startSseConnection();

  startAutoSyncLoop();

  // 4. فحص التحديثات تلقائياً

  checkForAppUpdates();

}

// ==========================================================================

// 12.1 محرك التحديث التلقائي للتطبيق وتنزيل APK (Auto Update Engine)

// ==========================================================================

// يُضبط تلقائياً من وسم الإصدار عند بناء APK في GitHub Actions

const CURRENT_APP_VERSION = '3.4.0';

let lastUpdateCheckTime = 0;

function compareVersions(v1, v2) {

  const p1 = (v1 || '').split('.').map(n => parseInt(n) || 0);

  const p2 = (v2 || '').split('.').map(n => parseInt(n) || 0);

  for (let i = 0; i < Math.max(p1.length, p2.length); i++) {

    const num1 = p1[i] || 0;

    const num2 = p2[i] || 0;

    if (num1 > num2) return 1;

    if (num1 < num2) return -1;

  }

  return 0;

}

async function checkForAppUpdates(manual = false) {

  const autoUpdateEnabled = localStorage.getItem('lmima_auto_update') !== 'false';

  if (!manual && !autoUpdateEnabled) return;

  const now = Date.now();

  if (!manual && now - lastUpdateCheckTime < 25000) return;

  lastUpdateCheckTime = now;

  const host = AppState.serverIp;

  if (!host) return;

  const cleanHost = host.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

  const url = `http://${cleanHost}/api/version?t=${now}`;

  try {

    const controller = new AbortController();

    const timeoutId = setTimeout(() => controller.abort(), 4000);

    const res = await fetch(url, { signal: controller.signal, mode: 'cors' });

    clearTimeout(timeoutId);

    if (res.ok) {

      const info = await res.json();

      const serverVersion = info.version || CURRENT_APP_VERSION;

      // المقارنة مع إصدار التطبيق الفعلي، وفقط إذا كان ملف التحديث متوفراً في الحاسوب

      const isNewer = info.apkAvailable !== false && compareVersions(serverVersion, CURRENT_APP_VERSION) > 0;

      const badge = document.getElementById('app-version-badge');

      if (badge) badge.textContent = `v${CURRENT_APP_VERSION}`;

      const desc = document.getElementById('update-status-desc');

      if (desc) desc.textContent = `متصل بالخادم (الإصدار الحالي: v${CURRENT_APP_VERSION} | أحدث إصدار: v${serverVersion}) 🟢`;

      if (isNewer) {

        showAppUpdateBanner(serverVersion, info.message || 'يتوفر إصدار جديد مع ميزات محدثة ونقاط بيع أسرع.');

        if (manual) {

          showToast(`🚀 يتوفر تحديث جديد (v${serverVersion})! اضغط لتحديث التطبيق.`);

        }

      } else if (manual) {

        showToast(`أنت تستخدم أحدث إصدار من تطبيق LMIMA SHOP (v${CURRENT_APP_VERSION}) ✅`);

      }

    } else if (manual) {

      showToast('تعذر فحص التحديثات من الخادم حالياً');

    }

  } catch (err) {

    if (manual) {

      showToast('تعذر الاتصال بالخادم لفحص التحديثات');

    }

  }

}

function showAppUpdateBanner(version, message) {

  const banner = document.getElementById('app-update-banner');

  const title = document.getElementById('update-banner-title');

  const desc = document.getElementById('update-banner-desc');

  if (!banner) return;

  if (title) title.textContent = `🚀 يتوفر تحديث جديد للتطبيق (v${version})`;

  if (desc && message) desc.textContent = message;

  banner.style.display = 'flex';

}

function hideAppUpdateBanner() {

  const banner = document.getElementById('app-update-banner');

  if (banner) banner.style.display = 'none';

}

async function triggerAppUpdate() {

  showToast('جاري تحديث التطبيق وتثبيت الحزمة الجديدة... ⚡');

  // مسح الذاكرة المؤقتة Caches لتثبيت التحديث فورياً في PWA والمتصفح

  if ('caches' in window) {

    try {

      const keys = await caches.keys();

      await Promise.all(keys.map(k => caches.delete(k)));

    } catch (e) {}

  }

  // تنزيل وتثبيت الـ APK للهواتف وتطبيقات الأندرويد

  downloadApkUpdate();

  // إعادة تحميل الصفحة لتطبيق التحديث

  setTimeout(() => {

    hideAppUpdateBanner();

    window.location.reload(true);

  }, 1500);

}

function downloadApkUpdate() {

  const host = AppState.serverIp || '192.168.11.102:8080';

  const cleanHost = host.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

  const apkUrl = `http://${cleanHost}/LmimaShop.apk`;

  showToast('جاري تنزيل ملف التحديث LmimaShop.apk... ⬇️');

  const a = document.createElement('a');

  a.href = apkUrl;

  a.download = 'LmimaShop.apk';

  a.target = '_blank';

  document.body.appendChild(a);

  a.click();

  document.body.removeChild(a);

}

// ==========================================================================

// 12. إعداد المستمعين والأحداث العامة (Event Listeners)

// ==========================================================================

function setupEventListeners() {

  // التبديل بين التبويبات الرئيسية

  document.querySelectorAll('.nav-item').forEach(btn => {

    btn.addEventListener('click', () => {

      const target = btn.dataset.tab;

      document.querySelectorAll('.nav-item').forEach(b => b.classList.remove('active'));

      document.querySelectorAll('.tab-screen').forEach(s => s.classList.remove('active'));

      btn.classList.add('active');

      const targetScreen = document.getElementById('tab-' + target);

      if (targetScreen) targetScreen.classList.add('active');

      if (AppState.activeTab === 'pos' && target !== 'pos') {

        stopPOSCamera();

      }

      if (AppState.activeTab === 'products' && target !== 'products') {

        stopProductsCamera();

      }

      AppState.activeTab = target;

      window.scrollTo({ top: 0, behavior: 'smooth' });

    });

  });

  // أحداث السلة

  document.getElementById('btn-open-cart-from-bar')?.addEventListener('click', () => openModal('modal-cart'));

  document.getElementById('btn-cart-bar-checkout')?.addEventListener('click', () => openModal('modal-cart'));

  document.getElementById('btn-clear-cart')?.addEventListener('click', clearCart);

  document.getElementById('btn-proceed-checkout')?.addEventListener('click', openCheckoutModal);

  // اختيار الزبون في السلة

  document.getElementById('cart-customer-select')?.addEventListener('change', checkCartCustomerPaymentOptions);

  document.getElementById('btn-quick-add-cust-from-cart')?.addEventListener('click', () => {

    closeModal('modal-cart');

    openAddCustomerModal();

  });

  // اختيار طريقة الدفع كاش أو كريدي

  document.querySelectorAll('.payment-pill').forEach(btn => {

    btn.addEventListener('click', () => {

      document.querySelectorAll('.payment-pill').forEach(p => p.classList.remove('active'));

      btn.classList.add('active');

      AppState.cartPaymentType = btn.dataset.pay;

      checkCartCustomerPaymentOptions();

    });

  });

  // أحداث نافذة الدفع واحتساب الصرف

  document.querySelectorAll('#checkout-cash-section .keypad-btn').forEach(btn => {

    btn.addEventListener('click', () => handleCheckoutKeypad(btn.dataset.key));

  });

  document.querySelectorAll('.quick-cash-btn').forEach(btn => {

    btn.addEventListener('click', () => handleQuickCashAdd(btn.dataset.amt));

  });

  document.getElementById('btn-confirm-checkout')?.addEventListener('click', confirmCheckout);

  // أحداث الفاتورة

  document.getElementById('btn-share-receipt-wa')?.addEventListener('click', shareReceiptViaWhatsApp);

  document.getElementById('btn-print-receipt')?.addEventListener('click', () => window.print());

  // بحث نقطة البيع

  const posSearchInp = document.getElementById('pos-search-input');

  const posClearBtn = document.getElementById('btn-pos-clear-search');

  posSearchInp?.addEventListener('input', (e) => {

    AppState.posSearch = e.target.value;

    if (posClearBtn) posClearBtn.style.display = e.target.value ? 'flex' : 'none';

    renderPOSGrid();

  });

  posClearBtn?.addEventListener('click', () => {

    if (posSearchInp) posSearchInp.value = '';

    AppState.posSearch = '';

    posClearBtn.style.display = 'none';

    renderPOSGrid();

  });

  // كاميرا نقطة البيع
  document.getElementById('btn-pos-toggle-camera')?.addEventListener('click', togglePOSCamera);
  document.getElementById('btn-pos-close-camera')?.addEventListener('click', stopPOSCamera);

  // كاميرا صفحة المنتجات
  document.getElementById('btn-products-toggle-camera')?.addEventListener('click', toggleProductsCamera);
  document.getElementById('btn-products-close-camera')?.addEventListener('click', stopProductsCamera);

  // زر مسح حقل البحث في المنتجات
  document.getElementById('btn-products-clear-search')?.addEventListener('click', () => {
    const inp = document.getElementById('products-search-input');
    if (inp) inp.value = '';
    const btn = document.getElementById('btn-products-clear-search');
    if (btn) btn.style.display = 'none';
    AppState.productsSearch = '';
    AppState.productsPage = 1;
    renderProductsTab();
  });

  // بحث المنتجات
  document.getElementById('products-search-input')?.addEventListener('input', (e) => {
    AppState.productsSearch = e.target.value;
    const btn = document.getElementById('btn-products-clear-search');
    if (btn) btn.style.display = e.target.value.trim() ? 'block' : 'none';
    AppState.productsPage = 1;
    renderProductsTab();
  });

  document.getElementById('btn-load-more-products')?.addEventListener('click', () => {

    AppState.productsPage++;

    renderProductsTab();

  });

  // --- أحداث الفئات والصور للمنتوج ---

  document.getElementById('prod-category-select')?.addEventListener('change', function(e) {

    const box = document.getElementById('new-category-inline-box');

    if (e.target.value === '__NEW__') {

      if (box) box.style.display = 'block';

      document.getElementById('new-category-input')?.focus();

    } else {

      if (box) box.style.display = 'none';

    }

  });

  document.getElementById('btn-quick-add-category')?.addEventListener('click', function() {

    const box = document.getElementById('new-category-inline-box');

    if (box) {

      box.style.display = (box.style.display === 'none') ? 'block' : 'none';

      if (box.style.display === 'block') document.getElementById('new-category-input')?.focus();

    }

  });

  document.getElementById('btn-cancel-add-category')?.addEventListener('click', function() {

    const box = document.getElementById('new-category-inline-box');

    if (box) box.style.display = 'none';

    const sel = document.getElementById('prod-category-select');

    if (sel && sel.value === '__NEW__') sel.value = 'عام';

  });

  document.getElementById('btn-confirm-add-category')?.addEventListener('click', async function() {

    const inp = document.getElementById('new-category-input');

    const name = (inp?.value || '').trim();

    if (!name) {

      showToast('يرجى إدخال اسم الفئة');

      return;

    }

    if (!AppState.data) AppState.data = {};

    if (!AppState.data.Categories) AppState.data.Categories = [];

    if (!AppState.data.Categories.includes(name)) {

      AppState.data.Categories.push(name);

      saveDataLocally();

    }

    populateProductCategories(name);

    const box = document.getElementById('new-category-inline-box');

    if (box) box.style.display = 'none';

    if (inp) inp.value = '';

    showToast('تمت إضافة الفئة: ' + name + ' 🏷️');

    postToServer('/api/categories/save', { name }).catch(function() {});

  });

  // أحداث صور المنتوج والكاميرا والمعرض

  document.getElementById('btn-open-camera-modal')?.addEventListener('click', function() {

    openProductCameraCaptureModal();

  });

  document.getElementById('prod-img-box')?.addEventListener('click', function() {
    const preview = document.getElementById('prod-img-preview');
    if (!preview || preview.style.display === 'none' || !preview.src) {
      openProductCameraCaptureModal();
    } else {
      const img = new Image();
      img.onload = function() {
        openModal('modal-image-editor');
        setTimeout(function() { ImageEditor.init(img); }, 100);
      };
      img.src = preview.src;
    }
  });

  document.getElementById('btn-take-photo-snap')?.addEventListener('click', function() {

    snapProductCameraPhoto();

  });

  document.getElementById('prod-gallery-input')?.addEventListener('change', function(e) {

    const file = e.target.files?.[0];

    if (file) handleProductImageSelected(file);

    e.target.value = '';

  });

  document.getElementById('btn-edit-prod-img')?.addEventListener('click', function() {

    const prev = document.getElementById('prod-img-preview');

    if (prev && prev.src && prev.style.display !== 'none') {

      const img = new Image();

      img.onload = function() {

        openModal('modal-image-editor');

        setTimeout(function() { ImageEditor.init(img); }, 100);

      };

      img.src = prev.src;

    }

  });

  document.getElementById('btn-remove-prod-img')?.addEventListener('click', function() {

    if (confirm('هل تريد حذف صورة هذا المنتوج؟')) {

      setProductImagePreview(null);

      showToast('تم حذف صورة المنتوج');

    }

  });

  document.getElementById('btn-save-img-to-drive')?.addEventListener('click', function() {

    const barcode = (document.getElementById('prod-barcode')?.value || '').trim();

    const name = (document.getElementById('prod-name')?.value || '').trim();

    let category = (document.getElementById('prod-category-select')?.value || 'عام').trim();

    if (category === '__NEW__') {

      category = (document.getElementById('new-category-input')?.value || '').trim() || 'عام';

    }

    saveSingleProductPhotoToDrive(barcode, category, name, AppState.currentProductImage);

    if (barcode && AppState.currentProductImage) {

      savePendingDrivePhoto(barcode, category, name, AppState.currentProductImage);

    }

  });

  // أزرار معدل الصور (Canvas Editor)

  document.getElementById('btn-editor-rotate-left')?.addEventListener('click', function() { ImageEditor.rotate(-90); });

  document.getElementById('btn-editor-rotate-right')?.addEventListener('click', function() { ImageEditor.rotate(90); });

  document.getElementById('btn-editor-flip')?.addEventListener('click', function() { ImageEditor.flip(); });

  document.getElementById('btn-editor-crop-square')?.addEventListener('click', function() { ImageEditor.toggleCropSquare(); });

  document.getElementById('btn-editor-brighten')?.addEventListener('click', function() { ImageEditor.setBrightness(15); });

  document.getElementById('btn-editor-darken')?.addEventListener('click', function() { ImageEditor.setBrightness(-15); });

  document.getElementById('btn-editor-apply')?.addEventListener('click', function() {

    const b64 = ImageEditor.exportBase64();

    if (b64) {

      setProductImagePreview(b64);

      closeModal('modal-image-editor');

      showToast('تم تطبيق وحفظ صورة المنتوج! 📸');

    }

  });

  document.getElementById('btn-open-add-product')?.addEventListener('click', () => openAddProductModal());

  document.getElementById('btn-save-product')?.addEventListener('click', saveProductFromModal);

  document.getElementById('prod-sell-price')?.addEventListener('input', updateProdSellRial);

  document.getElementById('btn-scan-prod-barcode')?.addEventListener('click', toggleProductModalCamera);

  document.getElementById('btn-close-prod-camera')?.addEventListener('click', stopProductModalCamera);

  document.getElementById('btn-generate-barcode')?.addEventListener('click', () => {

    const code = generateUniqueBarcode();

    const barInp = document.getElementById('prod-barcode');

    if (barInp) {

      barInp.value = code;

      playBeep();

      showToast(`تم توليد باركود تلقائي: ${code} 🎲`);

    }

  });

  // بحث الزبناء

  document.getElementById('customers-search-input')?.addEventListener('input', (e) => {

    AppState.customersSearch = e.target.value;

    renderCustomersTab();

  });

  document.getElementById('btn-open-add-customer')?.addEventListener('click', () => openAddCustomerModal());

  document.getElementById('btn-save-customer')?.addEventListener('click', saveCustomerFromModal);

  // فلترة الزبناء

  document.querySelectorAll('#customers-filter-chips .chip').forEach(ch => {

    ch.addEventListener('click', () => {

      document.querySelectorAll('#customers-filter-chips .chip').forEach(c => c.classList.remove('active'));

      ch.classList.add('active');

      AppState.customersFilter = ch.dataset.filter;

      renderCustomersTab();

    });

  });

  // تسديد ديون الزبون

  document.querySelectorAll('#debt-keypad .keypad-btn').forEach(btn => {

    btn.addEventListener('click', () => handlePayDebtKeypad(btn.dataset.key));

  });

  document.getElementById('btn-pay-full-debt')?.addEventListener('click', () => {

    payDebtCurrentValue = payDebtMaxDue.toFixed(2);

    const amtInp = document.getElementById('pay-debt-amount-input');

    if (amtInp) amtInp.value = payDebtCurrentValue;

  });

  document.getElementById('btn-confirm-pay-debt')?.addEventListener('click', confirmPayDebt);

  // فترات وتواريخ كشف حساب وفاتورة الزبون

  document.querySelectorAll('.stmt-preset-btn').forEach(btn => {

    btn.addEventListener('click', () => {

      document.querySelectorAll('.stmt-preset-btn').forEach(b => b.classList.remove('active'));

      btn.classList.add('active');

      const range = btn.dataset.range;

      currentStatementRange = range;

      const customBox = document.getElementById('stmt-custom-dates-box');

      if (range === 'custom') {

        if (customBox) customBox.style.display = 'block';

      } else {

        if (customBox) customBox.style.display = 'none';

        if (currentStatementCustId) {

          loadCustomerStatement(currentStatementCustId, range);

        }

      }

    });

  });

  // تطبيق التاريخ المخصص

  document.getElementById('btn-apply-stmt-filter')?.addEventListener('click', () => {

    const fromVal = document.getElementById('stmt-date-from')?.value;

    const toVal = document.getElementById('stmt-date-to')?.value;

    if (currentStatementCustId) {

      loadCustomerStatement(currentStatementCustId, 'custom', fromVal, toVal);

    }

  });

  // أزرار الطبع والتحميل والواتساب لكشف الحساب

  document.getElementById('btn-stmt-print')?.addEventListener('click', printCustomerStatement);

  document.getElementById('btn-stmt-download')?.addEventListener('click', downloadCustomerStatement);

  document.getElementById('btn-stmt-whatsapp')?.addEventListener('click', sendStatementViaWhatsApp);

  // بحث الممونين

  document.getElementById('suppliers-search-input')?.addEventListener('input', (e) => {

    AppState.suppliersSearch = e.target.value;

    renderSuppliersTab();

  });

  document.getElementById('btn-open-add-supplier')?.addEventListener('click', () => openAddSupplierModal());

  document.getElementById('btn-save-supplier')?.addEventListener('click', saveSupplierFromModal);

  // فلترة الممونين

  document.querySelectorAll('#suppliers-filter-chips .chip').forEach(ch => {

    ch.addEventListener('click', () => {

      document.querySelectorAll('#suppliers-filter-chips .chip').forEach(c => c.classList.remove('active'));

      ch.classList.add('active');

      AppState.suppliersFilter = ch.dataset.filter;

      renderSuppliersTab();

    });

  });

  // بحث الفواتير

  document.getElementById('invoice-search-input')?.addEventListener('input', renderInvoicesList);

  // ==============================================

  // إعدادات الربط والمزامنة والتحيين (Connection & Sync Settings)

  // ==============================================

  // --- 1. ماسح باركود الربط مع الحاسوب بالكاميرا (QR Code Scanner) ---

  let qrPairScanner = null;

  let isQrPairScanning = false;

  async function startQrPairScanner() {

    if (typeof Html5Qrcode === 'undefined') {

      showToast('جاري تحميل مكتبة قراءة الباركود...');

      return;

    }

    openModal('modal-qr-connection-scanner');

    const feedback = document.getElementById('qr-scan-feedback');

    if (feedback) {

      feedback.style.display = 'none';

      feedback.textContent = '';

    }

    try {

      if (qrPairScanner) {

        try { await qrPairScanner.stop(); } catch(e){}

        qrPairScanner = null;

      }

      qrPairScanner = new Html5Qrcode('qr-connection-viewport');

      isQrPairScanning = true;

      const config = { fps: 15, qrbox: { width: 250, height: 250 }, aspectRatio: 1.0 };

      await qrPairScanner.start(

        { facingMode: 'environment' },

        config,

        async (decodedText) => {

          await handleScannedPairQrCode(decodedText);

        },

        (error) => {}

      );

    } catch (err) {

      console.error('QR pair scanner error:', err);

      showToast('تعذر تشغيل الكاميرا. يرجى منح الإذن للتطبيق لاستخدام الكاميرا');

      stopQrPairScanner();

    }

  }

  async function stopQrPairScanner() {

    if (qrPairScanner) {

      try {

        await qrPairScanner.stop();

      } catch (e) {}

      qrPairScanner = null;

    }

    isQrPairScanning = false;

    closeModal('modal-qr-connection-scanner');

  }

  async function handleScannedPairQrCode(decodedText) {

    if (!isQrPairScanning) return;

    isQrPairScanning = false;

    try {

      if (navigator.vibrate) navigator.vibrate([100, 50, 100]);

    } catch(e){}

    let host = '';

    let code = '';

    try {

      const parsed = JSON.parse(decodedText);

      host = parsed.server || parsed.url || parsed.host || '';

      code = parsed.pairing_code || parsed.code || '';

    } catch (e) {

      if (decodedText.startsWith('http://') || decodedText.startsWith('https://')) {

        try {

          const u = new URL(decodedText);

          host = u.host;

          if (u.searchParams.get('code')) code = u.searchParams.get('code');

        } catch (err) {

          host = decodedText.trim();

        }

      } else {

        host = decodedText.trim();

      }

    }

    host = host.replace(/^https?:\/\//i, '').replace(/\/$/, '');

    if (!host) {

      showToast('رمز QR غير صالح. يرجى مسح رمز برنامج LMIMA SHOP');

      isQrPairScanning = true;

      return;

    }

    const feedback = document.getElementById('qr-scan-feedback');

    if (feedback) {

      feedback.style.display = 'block';

      feedback.style.background = '#dcfce7';

      feedback.style.color = '#15803d';

      feedback.textContent = `✅ تم مسح الرمز بنجاح! الخادم: ${host}`;

    }

    showToast(`تم مسح الباركود بنجاح! جاري الربط بـ ${host} ⚡`);

    await stopQrPairScanner();

    // تعبئة الحقول في الواجهة

    const mIp = document.getElementById('sync-modal-server-ip');

    const dIp = document.getElementById('sync-server-ip');

    const mCode = document.getElementById('sync-modal-pairing-code');

    const dCode = document.getElementById('sync-dash-pairing-code');

    if (mIp) mIp.value = host;

    if (dIp) dIp.value = host;

    if (mCode) mCode.value = code;

    if (dCode) dCode.value = code;

    // بدء الاتصال والإقران والتحيين الفوري

    await handleConnectAction(host, code);

  }

  // --- 2. فتح نافذة الإعدادات وتحديث البيانات ---

  const openConnModal = () => {

    const ipInp = document.getElementById('sync-modal-server-ip');

    const pairInp = document.getElementById('sync-modal-pairing-code');

    const intervalSelect = document.getElementById('select-auto-sync-interval');

    const autoSyncChk = document.getElementById('chk-auto-sync-modal');

    if (ipInp) ipInp.value = AppState.serverIp || localStorage.getItem('lmima_server_ip') || '192.168.11.117:8080';

    if (pairInp) pairInp.value = '';

    if (intervalSelect) intervalSelect.value = localStorage.getItem('lmima_sync_interval') || 'live';

    if (autoSyncChk) autoSyncChk.checked = AppState.autoSyncEnabled;

    openModal('modal-connection-settings');

    updateConnectionDiagnostics();

  };

  document.getElementById('btn-header-qr-sync')?.addEventListener('click', startQrPairScanner);

  document.getElementById('btn-modal-start-qr-scan')?.addEventListener('click', startQrPairScanner);

  document.getElementById('btn-dash-scan-qr')?.addEventListener('click', startQrPairScanner);

  document.getElementById('btn-close-qr-scanner-x')?.addEventListener('click', stopQrPairScanner);

  document.getElementById('btn-close-qr-scanner-btn')?.addEventListener('click', stopQrPairScanner);

  document.getElementById('btn-open-conn-settings')?.addEventListener('click', openConnModal);

  document.getElementById('btn-banner-open-conn')?.addEventListener('click', openConnModal);

  // دالة تشخيص وتحديث حالة الاتصال اللحظية

  async function updateConnectionDiagnostics() {

    const httpBadge = document.getElementById('diag-http-badge');

    const sseBadge = document.getElementById('diag-sse-badge');

    const sigBadge = document.getElementById('diag-signalr-badge');

    const tokenBadge = document.getElementById('diag-token-badge');

    const queueBadge = document.getElementById('btn-diag-flush-queue');

    const queueDesc = document.getElementById('diag-queue-desc');

    const cleanHost = (AppState.serverIp || '192.168.11.117:8080').trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

    const startTime = Date.now();

    try {

      const res = await fetch(`http://${cleanHost}/api/health?t=${Date.now()}`, { signal: AbortSignal.timeout(2500) });

      const latency = Date.now() - startTime;

      if (res.ok) {

        if (httpBadge) {

          httpBadge.textContent = `متصل 🟢 (${latency}ms)`;

          httpBadge.className = 'badge badge-cash';

        }

      } else {

        if (httpBadge) {

          httpBadge.textContent = 'خطأ بالخادم 🔴';

          httpBadge.className = 'badge badge-credit';

        }

      }

    } catch (e) {

      if (httpBadge) {

        httpBadge.textContent = 'غير متصل 🔴';

        httpBadge.className = 'badge badge-credit';

      }

    }

    if (sseBadge) {

      sseBadge.textContent = isSseConnected ? 'نشط ومتصل 🟢' : 'غير متصل 🔴';

      sseBadge.className = isSseConnected ? 'badge badge-cash' : 'badge badge-credit';

    }

    if (sigBadge) {

      sigBadge.textContent = AppState.isSignalRConnected ? 'نشط ومتصل 🟢' : 'غير متصل 🔴';

      sigBadge.className = AppState.isSignalRConnected ? 'badge badge-cash' : 'badge badge-credit';

    }

    const hasToken = !!localStorage.getItem('lmima_device_token');

    if (tokenBadge) {

      tokenBadge.textContent = hasToken ? 'مقترن وموثوق ✅' : 'يحتاج اقتران ⚠️';

      tokenBadge.className = hasToken ? 'badge badge-cash' : 'badge badge-credit';

    }


    try {

      const queue = await IDB.getAll('offline_queue');

      const pending = queue ? queue.filter(q => q.status === 'pending') : [];

      if (queueBadge) queueBadge.textContent = `${pending.length} معلقة`;

      if (queueDesc) queueDesc.textContent = pending.length > 0 ? `يوجد ${pending.length} عمليات بانتظار الإرسال` : 'كل العمليات متزامنة ومسجلة بحاسوبك 🟢';

    } catch (e) {}

  }

  // إجراء 1: الربط والاتصال الأساسي

  async function handleConnectAction(targetHost, targetCode) {

    let host = targetHost || document.getElementById('sync-modal-server-ip')?.value || document.getElementById('sync-server-ip')?.value || '';

    host = host.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');

    if (!host) {

      showToast('يرجى إدخال عنوان IP الخاص بالحاسوب أو مسح الباركود 📷');

      return false;

    }

    const code = (targetCode || document.getElementById('sync-modal-pairing-code')?.value || document.getElementById('sync-dash-pairing-code')?.value || '').trim();

    AppState.serverIp = host;

    localStorage.setItem('lmima_server_ip', host);

    const modalInp = document.getElementById('sync-modal-server-ip');

    const dashInp = document.getElementById('sync-server-ip');

    if (modalInp) modalInp.value = host;

    if (dashInp) dashInp.value = host;

    showToast('جاري الاتصال والربط مع خادم الحاسوب...');

    updateServerStatus(false, 'جاري الاتصال والربط...');

    // الاقتران فقط عند إدخال رمز جديد؛ الهاتف المقترن سابقاً يحتفظ بتوكنه

    if (code) {

      await pairDeviceWithServer(host, code);

    } else if (!localStorage.getItem('lmima_device_token')) {

      showToast('🔒 أدخل رمز الاقتران الظاهر في نافذة "اقتران الهاتف" على الحاسوب');

    }

    startSignalR();

    startSseConnection();

    await flushOfflineQueue();

    let ok = await fetchDeltaSync(host);

    if (!ok && typeof fetchLiveSync === 'function') {

      ok = await fetchLiveSync(host, false);

    }

    if (ok) {

      showToast('تم الربط والاتصال بالحاسوب بنجاح! ⚡');

      updateServerStatus(true, `متصل ومُحدث (${new Date().toLocaleTimeString('fr-FR')})`);

    } else {

      showToast('تعذر الاتصال بالحاسوب. تأكد من تشغيل البرنامج واتصال الهاتف بنفس شبكة الـ Wi-Fi');

      updateServerStatus(false, 'تعذر الاتصال بالحاسوب');

    }

    updateConnectionDiagnostics();

    return ok;

  }

  // إجراء 2: إعادة الربط

  async function handleReconnectAction() {

    showToast('جاري إعادة تعيين الاتصال والربط من جديد...');

    if (sseEventSource) {

      try { sseEventSource.close(); } catch(e){}

    }

    isSseConnected = false;

    const host = AppState.serverIp || localStorage.getItem('lmima_server_ip') || '192.168.11.117:8080';

    await IDB.setMeta('sync_cursor', '');

    const ok = await handleConnectAction(host, '');

    if (ok) {

      showToast('تمت إعادة الربط بالخادم وتحديث الاتصال بنجاح! 🔄');

    }

  }

  // إجراء 3: تحيين وتحديث البيانات فوراً

  async function handleRefreshSyncAction() {

    showToast('جاري تحيين وتحديث بيانات المتجر فوراً...');

    await flushOfflineQueue();

    let ok = await fetchDeltaSync(AppState.serverIp);

    if (!ok && typeof fetchLiveSync === 'function') {

      ok = await fetchLiveSync(AppState.serverIp, false);

    }

    if (ok) {

      showToast('تم تحيين وتحديث كافة بيانات المتجر بنجاح! 🟢');

      updateServerStatus(true, `متصل ومُحدث (${new Date().toLocaleTimeString('fr-FR')})`);

    } else {

      showToast('فشل تحيين البيانات. تأكد من تشغيل الخادم بالحاسوب');

      updateServerStatus(false, 'تعذر الاتصال بالحاسوب');

    }

    updateConnectionDiagnostics();

  }

  // ربط أزرار الربط والتحيين وإعادة الربط

  document.getElementById('btn-modal-connect')?.addEventListener('click', () => handleConnectAction());

  document.getElementById('btn-test-server-sync')?.addEventListener('click', () => handleConnectAction());

  document.getElementById('btn-modal-test-ping')?.addEventListener('click', () => handleConnectAction());

  document.getElementById('btn-modal-reconnect')?.addEventListener('click', handleReconnectAction);

  document.getElementById('btn-dash-reconnect')?.addEventListener('click', handleReconnectAction);

  document.getElementById('btn-modal-refresh-sync')?.addEventListener('click', handleRefreshSyncAction);

  document.getElementById('btn-dash-refresh-sync')?.addEventListener('click', handleRefreshSyncAction);

  document.getElementById('btn-sync-now')?.addEventListener('click', handleRefreshSyncAction);

  document.getElementById('btn-modal-pair-device')?.addEventListener('click', async () => {

    const code = (document.getElementById('sync-modal-pairing-code')?.value || '').trim();

    if (!code) { showToast('أدخل رمز الاقتران الظاهر في الحاسوب'); return; }

    const host = document.getElementById('sync-modal-server-ip')?.value || AppState.serverIp;

    showToast('جاري إقران الهاتف بالخادم...');

    const ok = await pairDeviceWithServer(host, code);

    if (ok) {

      showToast('تم إقران الهاتف بالخادم بنجاح! ✅');

      await fetchDeltaSync(host);

    } else {

      showToast('فشل الإقران. تأكد من رمز الاقتران أو عنوان الخادم');

    }

    updateConnectionDiagnostics();

  });

  document.getElementById('btn-dash-pair-device')?.addEventListener('click', async () => {

    const code = (document.getElementById('sync-dash-pairing-code')?.value || '').trim();

    if (!code) { showToast('أدخل رمز الاقتران الظاهر في الحاسوب'); return; }

    const host = document.getElementById('sync-server-ip')?.value || AppState.serverIp;

    showToast('جاري إقران الهاتف بالخادم...');

    const ok = await pairDeviceWithServer(host, code);

    if (ok) {

      showToast('تم إقران الهاتف بنجاح! ✅');

      await fetchDeltaSync(host);

    } else {

      showToast('فشل الإقران. تأكد من الرمز وعنوان الخادم');

    }

    updateConnectionDiagnostics();

  });

  document.getElementById('btn-modal-auto-discover')?.addEventListener('click', async () => {

    const found = await autoDiscoverServer(true);

    if (found) {

      await handleConnectAction(found);

    }

  });

  document.getElementById('btn-auto-discover-pc')?.addEventListener('click', async () => {

    const found = await autoDiscoverServer(true);

    if (found) {

      await handleConnectAction(found);

    }

  });

  document.getElementById('btn-diag-flush-queue')?.addEventListener('click', async () => {

    await flushOfflineQueue();

    updateConnectionDiagnostics();

  });

  // تفعيل المزامنة التلقائية وخيارات التوقيت

  const updateAutoSyncState = (enabled) => {

    AppState.autoSyncEnabled = enabled;

    localStorage.setItem('lmima_auto_sync', enabled ? 'true' : 'false');

    const chk1 = document.getElementById('chk-auto-sync-loop');

    const chk2 = document.getElementById('chk-auto-sync-modal');

    if (chk1) chk1.checked = enabled;

    if (chk2) chk2.checked = enabled;

    if (enabled) {

      startSseConnection();

      startAutoSyncLoop();

      showToast('تم تفعيل المزامنة التلقائية المستمرة 🔄');

    } else {

      if (AppState.autoSyncTimer) clearInterval(AppState.autoSyncTimer);

      showToast('تم إيقاف المزامنة التلقائية');

    }

  };

  document.getElementById('chk-auto-sync-loop')?.addEventListener('change', (e) => updateAutoSyncState(e.target.checked));

  document.getElementById('chk-auto-sync-modal')?.addEventListener('change', (e) => updateAutoSyncState(e.target.checked));

  document.getElementById('select-auto-sync-interval')?.addEventListener('change', (e) => {

    localStorage.setItem('lmima_sync_interval', e.target.value);

    showToast(`تم ضبط دورة التحديث التلقائي (${e.target.options[e.target.selectedIndex].text})`);

    if (AppState.autoSyncEnabled) {

      startAutoSyncLoop();

    }

  });

  // استيراد ملف المتجر يدوياً

  const triggerStoreFileInput = () => {

    document.getElementById('sync-file-input')?.click();

  };

  document.getElementById('btn-modal-import-file')?.addEventListener('click', triggerStoreFileInput);

  document.getElementById('btn-dash-import-file')?.addEventListener('click', triggerStoreFileInput);

  document.getElementById('btn-flush-offline-queue')?.addEventListener('click', flushOfflineQueue);

  document.getElementById('btn-export-pending-drive')?.addEventListener('click', exportPendingOpsForDrive);

  // ملف المزامنة

  const fileInput = document.getElementById('sync-file-input');

  const triggerFile = () => fileInput?.click();

  document.getElementById('btn-open-file')?.addEventListener('click', triggerFile);

  document.getElementById('btn-settings-pick-file')?.addEventListener('click', triggerFile);

  document.getElementById('btn-export-drive-photos')?.addEventListener('click', exportPendingPhotosToGoogleDrive);

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

        alert('تعذر قراءة الملف. يرجى اختيار ملف lmima_mobile_sync.json صحيح.');

      }

    };

    reader.readAsText(file);

  });

  // تحديث التطبيق التلقائي والأحداث المرتبطة

  document.getElementById('btn-update-now')?.addEventListener('click', triggerAppUpdate);

  document.getElementById('btn-dismiss-update')?.addEventListener('click', hideAppUpdateBanner);

  document.getElementById('btn-check-updates-manual')?.addEventListener('click', () => {

    showToast('جاري فحص التحديثات من الخادم...');

    checkForAppUpdates(true);

  });

  document.getElementById('btn-download-apk-manual')?.addEventListener('click', downloadApkUpdate);

  document.getElementById('chk-auto-update')?.addEventListener('change', (e) => {

    localStorage.setItem('lmima_auto_update', e.target.checked ? 'true' : 'false');

    showToast(e.target.checked ? 'تم تفعيل فحص التحديثات التلقائي 🟢' : 'تم إيقاف فحص التحديثات التلقائي');

  });

}

// ==========================================================================

// 13. تشغيل Service Worker والتطبيق والتحديث الحي

// ==========================================================================

if ('serviceWorker' in navigator) {

  window.addEventListener('load', () => {

    navigator.serviceWorker.register('./sw.js').then((reg) => {

      // فحص وجود تحديث لملفات PWA

      reg.addEventListener('updatefound', () => {

        const newWorker = reg.installing;

        if (newWorker) {

          newWorker.addEventListener('statechange', () => {

            if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {

              showAppUpdateBanner('3.3', 'تم تحميل تحديث جديد للتطبيق في الخلفية! اضغط للتطبيق فورياً.');

            }

          });

        }

      });

    }).catch(() => {});

    // عند استبدال السيرفيس ووركر بنسخة أحدث يتم التحديث التلقائي

    let refreshing = false;

    navigator.serviceWorker.addEventListener('controllerchange', () => {

      if (!refreshing) {

        refreshing = true;

        window.location.reload();

      }

    });

  });

}

async function startSignalR() {

  if (typeof signalR === 'undefined') return;

  const ipParts = AppState.serverIp.split(':');

  const baseIp = ipParts[0];

  const hubUrl = `http://${baseIp}:8081/barcodeHub`;

  AppState.signalRConnection = new signalR.HubConnectionBuilder()

    .withUrl(hubUrl)

    .withAutomaticReconnect()

    .build();

  AppState.signalRConnection.on("SyncNow", () => {
    console.log("SignalR SyncNow command received! Fetching live sync...");
    showToast("جاري تحيين البيانات من الحاسوب فورا... 🔄");
    pushOfflineQueueV2();
    fetchDeltaSync(AppState.serverIp);
  });

  AppState.signalRConnection.onclose(() => {

    AppState.isSignalRConnected = false;

    updateSignalRUI();

  });

  try {

    await AppState.signalRConnection.start();

    AppState.isSignalRConnected = true;

    showToast('تم الاتصال لاسلكياً بنقطة البيع 💻');

  } catch (err) {

    console.error('SignalR error:', err);

    AppState.isSignalRConnected = false;

  }

  updateSignalRUI();

}

function updateSignalRUI() {

  const badge = document.getElementById('header-status-badge');

  const subtitle = document.getElementById('header-subtitle-text');

  if (badge && subtitle) {

    if (AppState.isSignalRConnected) {

      badge.style.backgroundColor = '#10b981';

      subtitle.innerText = 'متصل لاسلكياً (الكاشير)';

    } else {

      badge.style.backgroundColor = '#ef4444';

      subtitle.innerText = 'غير متصل بالكاشير';

    }

  }

}

document.addEventListener('DOMContentLoaded', () => {

  setupEventListeners();

  initData();

  updatePendingDrivePhotosUI();

  startSignalR();

  startSseConnection();

});

