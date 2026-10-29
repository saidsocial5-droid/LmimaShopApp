/* LmimaShop V2 mobile — single app state, subscriptions and cart logic. */

import { getServerUrl, getToken } from './config.js';
import { number, round2 } from './ui.js';

export const state = {
  ready: false,
  online: typeof navigator === 'undefined' ? true : navigator.onLine,
  serverUrl: getServerUrl(),
  token: getToken(),
  tab: 'pos',
  products: [],
  categories: [],
  customers: [],
  customerPayments: [],
  suppliers: [],
  sales: [],
  saleItems: [],
  returns: [],
  pendingStock: {},
  queueCount: 0,
  cursor: null,
  lastSync: null,
  health: null,
  cart: [],
  cartCustomer: null,
  discountPct: 0,
  search: { pos: '', products: '', customers: '' },
  category: '',
};

const listeners = new Set();

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function notify() {
  for (const listener of Array.from(listeners)) {
    try {
      listener(state);
    } catch (err) {
      console.error('State listener failed:', err);
    }
  }
}

export function setState(patch) {
  Object.assign(state, patch);
  notify();
}

export function isDeleted(row) {
  if (!row) return true;
  return row.deleted_at !== null && row.deleted_at !== undefined ||
    row.DeletedAt !== null && row.DeletedAt !== undefined;
}

/* ---------- row helpers (tolerate snake_case and PascalCase) ---------- */

function pick(row, snake, pascal, fallback = null) {
  if (!row) return fallback;
  if (row[snake] !== undefined && row[snake] !== null) return row[snake];
  if (row[pascal] !== undefined && row[pascal] !== null) return row[pascal];
  return fallback;
}

export function productName(product) {
  return String(pick(product, 'name', 'Name', ''));
}

export function productBarcode(product) {
  return String(pick(product, 'barcode', 'Barcode', ''));
}

export function productCategory(product) {
  return String(pick(product, 'category', 'Category', ''));
}

export function productPrice(product) {
  return number(pick(product, 'sell_price', 'SellPrice', 0));
}

export function productCost(product) {
  return number(pick(product, 'cost_price', 'CostPrice', 0));
}

export function productStock(product) {
  return number(pick(product, 'stock_qty', 'StockQuantity', 0));
}

export function productTva(product) {
  return number(pick(product, 'tva_rate', 'TvaRate', 20));
}

export function productWeighed(product) {
  return Boolean(pick(product, 'is_weighed', 'IsWeighed', false));
}

export function productAvailable(product) {
  const pending = state.pendingStock[product.uuid] || 0;
  return productStock(product) + pending;
}

export function customerName(customer) {
  return String(pick(customer, 'full_name', 'FullName', 'زبون'));
}

export function customerPhone(customer) {
  return String(pick(customer, 'phone', 'Phone', ''));
}

export function customerDue(customer) {
  return number(pick(customer, 'due_balance', 'DueBalance', 0));
}

export function customerLimit(customer) {
  return number(pick(customer, 'credit_limit', 'CreditLimit', 0));
}

export function saleTotal(sale) {
  return number(pick(sale, 'total', 'Total', 0));
}

export function saleTva(sale) {
  return number(pick(sale, 'tva', 'Tva', pick(sale, 'vat', 'Vat', 0)));
}

export function salePaid(sale) {
  return number(pick(sale, 'paid', 'Paid', 0));
}

export function saleCreatedAt(sale) {
  return pick(sale, 'created_at', 'CreatedAt', '');
}

export function saleInvoice(sale) {
  return String(pick(sale, 'invoice_no', 'InvoiceNo', ''));
}

export function saleCustomerUuid(sale) {
  return pick(sale, 'customer_uuid', 'CustomerUuid', null);
}

export function customerPaymentAmount(payment) {
  return number(pick(payment, 'amount', 'Amount', 0));
}

export function customerPaymentCreatedAt(payment) {
  return pick(payment, 'created_at', 'CreatedAt', '');
}

/* ---------- cart ---------- */

export function cartSubtotal() {
  return round2(state.cart.reduce((sum, line) => sum + line.price * line.qty, 0));
}

export function cartTotals() {
  const subtotal = cartSubtotal();
  const pct = number(state.discountPct);
  const discount = round2(subtotal * pct / 100);
  let tva = 0;
  for (const line of state.cart) {
    const gross = line.price * line.qty;
    const afterDiscount = gross * (1 - pct / 100);
    tva += afterDiscount * (number(line.tva_rate) / 100);
  }
  tva = round2(tva);
  const total = round2(subtotal - discount + tva);
  return {
    subtotal,
    discount,
    tva,
    total,
    count: round2(state.cart.reduce((sum, line) => sum + line.qty, 0)),
    lines: state.cart.length,
  };
}

export function addToCart(product, qty = 1) {
  const quantity = number(qty);
  if (quantity <= 0) return { ok: false, message: 'الكمية غير صحيحة' };
  if (isDeleted(product)) return { ok: false, message: 'هذا المنتج محذوف' };
  const existing = state.cart.find((line) => line.uuid === product.uuid);
  const already = existing ? existing.qty : 0;
  const available = productAvailable(product);
  if (available <= 0) return { ok: false, message: 'لا يوجد مخزون من ' + productName(product) };
  if (already + quantity > available + 0.0001) {
    return { ok: false, message: 'الكمية المتوفرة ' + available + ' فقط' };
  }
  if (existing) {
    existing.qty = round2(existing.qty + quantity);
  } else {
    state.cart.push({
      uuid: product.uuid,
      name: productName(product),
      price: productPrice(product),
      qty: quantity,
      tva_rate: productTva(product),
      is_weighed: productWeighed(product),
    });
  }
  notify();
  return { ok: true };
}

export function setCartQty(uuidValue, qty) {
  const line = state.cart.find((item) => item.uuid === uuidValue);
  if (!line) return { ok: false, message: 'السطر غير موجود' };
  const quantity = number(qty);
  if (quantity <= 0) {
    removeFromCart(uuidValue);
    return { ok: true };
  }
  const product = state.products.find((item) => item.uuid === uuidValue);
  if (product) {
    const available = productAvailable(product);
    if (quantity > available + 0.0001) {
      return { ok: false, message: 'الكمية المتوفرة ' + available + ' فقط' };
    }
  }
  line.qty = round2(quantity);
  notify();
  return { ok: true };
}

export function removeFromCart(uuidValue) {
  state.cart = state.cart.filter((line) => line.uuid !== uuidValue);
  notify();
}

export function clearCart() {
  state.cart = [];
  state.cartCustomer = null;
  state.discountPct = 0;
  notify();
}

export function setDiscount(pct) {
  const value = number(pct);
  state.discountPct = value >= 0 && value <= 100 ? value : 0;
  notify();
}

export function setCartCustomer(customerUuid) {
  state.cartCustomer = customerUuid || null;
  notify();
}
