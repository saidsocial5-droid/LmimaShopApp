/* LmimaShop V2 mobile — payment modal, split payment, change and sale submission. */

import { api, ApiError } from './api.js';
import * as db from './db.js';
import { state, cartTotals, customerName, customerDue, setCartCustomer, clearCart, notify } from './store.js';
import {
  el, clear, toast, money, rial, number, round2, openModal, closeModal, setBusy,
} from './ui.js';
import { enqueueOp, pushQueue, fetchDelta, rebuildStateFromDB } from './sync.js';
import { renderPOS } from './pos.js';

let paymentReady = false;

export function initPayment() {
  if (paymentReady) return;
  paymentReady = true;

  document.getElementById('checkout-btn').addEventListener('click', openPaymentModal);
  document.getElementById('payment-confirm').addEventListener('click', confirmPayment);
  document.getElementById('pay-customer').addEventListener('change', (event) => {
    setCartCustomer(event.target.value);
  });

  for (const id of ['pay-cash', 'pay-card', 'pay-credit']) {
    document.getElementById(id).addEventListener('input', updatePaymentMeta);
  }

  document.getElementById('quick-cash').addEventListener('click', (event) => {
    const button = event.target.closest('[data-quick]');
    if (!button) return;
    const totals = cartTotals();
    const value = button.dataset.quick === 'exact' ? totals.total : number(button.dataset.quick);
    document.getElementById('pay-cash').value = round2(value).toFixed(2);
    updatePaymentMeta();
  });
}

function qtyText(value) {
  const qty = number(value);
  return Number.isInteger(qty) ? String(qty) : String(Number(qty.toFixed(3)));
}

function openPaymentModal() {
  if (!state.cart.length) {
    toast('السلة فارغة', 'warn');
    return;
  }
  const totals = cartTotals();
  const summary = document.getElementById('payment-summary');
  clear(summary);
  summary.appendChild(el('div', { class: 'row' },
    el('span', {}, 'عدد القطع'),
    el('b', { text: qtyText(totals.count) })
  ));
  summary.appendChild(el('div', { class: 'row' },
    el('span', {}, 'المجموع قبل الضريبة'),
    el('b', { class: 'money', text: money(totals.subtotal - totals.discount) })
  ));
  summary.appendChild(el('div', { class: 'row' },
    el('span', {}, 'الضريبة TVA'),
    el('b', { class: 'money', text: money(totals.tva) })
  ));
  summary.appendChild(el('div', { class: 'row total' },
    el('span', {}, 'الإجمالي'),
    el('b', { class: 'money', text: money(totals.total) })
  ));
  summary.appendChild(el('div', { class: 'row' },
    el('span', {}, 'بالريال (×20)'),
    el('b', { class: 'money', text: rial(totals.total) })
  ));

  const select = document.getElementById('pay-customer');
  clear(select);
  select.appendChild(el('option', { value: '', text: 'بدون زبون (نقدي / بطاقة)' }));
  for (const customer of state.customers) {
    const due = customerDue(customer);
    const label = customerName(customer) + (due > 0 ? ' — دين ' + money(due) : '');
    select.appendChild(el('option', { value: customer.uuid, text: label }));
  }
  select.value = state.cartCustomer || '';

  document.getElementById('pay-cash').value = totals.total.toFixed(2);
  document.getElementById('pay-card').value = '0.00';
  document.getElementById('pay-credit').value = '0.00';
  document.getElementById('payment-error').textContent = '';

  const quick = document.getElementById('quick-cash');
  clear(quick);
  for (const value of [5, 10, 20, 50, 100, 200]) {
    quick.appendChild(el('button', { class: 'chip-btn', type: 'button', dataset: { quick: String(value) }, text: value + ' د.م' }));
  }
  quick.appendChild(el('button', { class: 'chip-btn', type: 'button', dataset: { quick: 'exact' }, text: 'المبلغ تماماً' }));

  updatePaymentMeta();
  openModal('modal-payment');
}

function paymentAmounts() {
  return {
    cash: Math.max(0, number(document.getElementById('pay-cash').value)),
    card: Math.max(0, number(document.getElementById('pay-card').value)),
    credit: Math.max(0, number(document.getElementById('pay-credit').value)),
  };
}

function updatePaymentMeta() {
  const totals = cartTotals();
  const { cash, card, credit } = paymentAmounts();
  const cashDue = Math.max(0, totals.total - card - credit);
  const change = Math.max(0, cash - cashDue);
  const paid = cash + card + credit - change;
  const remaining = Math.max(0, totals.total - paid);
  const changeEl = document.getElementById('pay-change');
  const remainingEl = document.getElementById('pay-remaining');
  const rialEl = document.getElementById('pay-rial');
  if (changeEl) changeEl.textContent = money(change);
  if (remainingEl) remainingEl.textContent = money(remaining);
  if (rialEl) rialEl.textContent = rial(totals.total);
}

async function confirmPayment() {
  const errorEl = document.getElementById('payment-error');
  errorEl.textContent = '';
  const totals = cartTotals();
  if (!state.cart.length) {
    errorEl.textContent = 'السلة فارغة';
    return;
  }
  const { cash, card, credit } = paymentAmounts();
  const customerUuid = document.getElementById('pay-customer').value || null;
  if (credit > 0 && !customerUuid) {
    errorEl.textContent = 'اختر زبوناً لتسجيل البيع الآجل';
    return;
  }
  if (card + credit > totals.total + 0.005) {
    errorEl.textContent = 'مبلغ البطاقة + الآجل أكبر من إجمالي الفاتورة';
    return;
  }
  const cashDue = Math.max(0, totals.total - card - credit);
  if (cash + 0.005 < cashDue) {
    errorEl.textContent = 'المبلغ النقدي أقل من المطلوب';
    return;
  }
  const payments = [];
  if (cash > 0) payments.push({ method: 'cash', amount: round2(cash) });
  if (card > 0) payments.push({ method: 'card', amount: round2(card) });
  if (credit > 0) payments.push({ method: 'credit', amount: round2(credit) });

  const payload = {
    op_id: globalThis.crypto && typeof globalThis.crypto.randomUUID === 'function'
      ? globalThis.crypto.randomUUID()
      : uuidFallback(),
    cashier: localStorage.getItem('ls2_cashier') || 'تطبيق الجوال',
    customer_uuid: customerUuid,
    items: state.cart.map((line) => ({
      product_uuid: line.uuid,
      qty: line.qty,
      unit_price: line.price,
      discount_pct: 0,
    })),
    payments,
    discount_pct: number(state.discountPct),
  };

  await submitSale(payload);
}

function uuidFallback() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
    const random = Math.floor(Math.random() * 16);
    const value = char === 'x' ? random : (random & 0x3) | 0x8;
    return value.toString(16);
  });
}

async function submitSale(payload) {
  const confirmButton = document.getElementById('payment-confirm');
  setBusy(confirmButton, true);
  try {
    if (state.online) {
      try {
        const result = await api.checkout(payload);
        await adjustCachedStock(payload.items);
        closeModal('modal-payment');
        clearCart();
        toast('تم البيع — فاتورة ' + (result.invoice_no || '—') + ' · الباقي ' + money(result.change || 0), 'success', 5200);
        if (number(result.due) > 0) toast('سُجّل دين على الزبون: ' + money(result.due), 'info', 5200);
        try {
          await fetchDelta();
          await rebuildStateFromDB();
        } catch (err) {
          console.warn('Post-sale delta failed:', err);
        }
        renderPOS();
        return;
      } catch (err) {
        const retryable = err instanceof ApiError && (err.network || err.status >= 500);
        if (!retryable) throw err;
        console.warn('Checkout unreachable — queueing sale instead:', err.message);
      }
    }

    await enqueueOp('sale_create', payload, payload.op_id);
    for (const item of payload.items) {
      state.pendingStock[item.product_uuid] = (state.pendingStock[item.product_uuid] || 0) - number(item.qty);
    }
    notify();
    closeModal('modal-payment');
    clearCart();
    toast('تم حفظ البيع محلياً — سيُرسل تلقائياً عند عودة الاتصال', 'warn', 5200);
    pushQueue().catch((err) => console.warn('Queued sale push postponed:', err));
    renderPOS();
  } catch (err) {
    console.error('Sale failed:', err);
    if (err && err.name === 'AuthError') return;
    const message = err.message || 'تعذر إتمام البيع';
    errorElSafe(message);
    toast(message, 'error', 5200);
  } finally {
    setBusy(confirmButton, false);
  }
}

function errorElSafe(message) {
  const errorEl = document.getElementById('payment-error');
  if (errorEl) errorEl.textContent = message;
}

async function adjustCachedStock(items) {
  for (const item of items) {
    try {
      const product = await db.get('products', item.product_uuid);
      if (!product) continue;
      const current = number(product.stock_qty !== undefined ? product.stock_qty : product.StockQuantity);
      const updated = Math.max(0, round2(current - number(item.qty)));
      if (product.stock_qty !== undefined) product.stock_qty = updated;
      else product.StockQuantity = updated;
      await db.put('products', product);
    } catch (err) {
      console.error('Optimistic stock update failed:', item.product_uuid, err);
    }
  }
}
