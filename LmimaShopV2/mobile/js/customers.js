/* LmimaShop V2 mobile — customers, statements and debt payments. */

import { api, ApiError } from './api.js';
import * as db from './db.js';
import {
  state, isDeleted, customerName, customerPhone, customerDue, customerLimit,
  saleCustomerUuid, saleCreatedAt, saleInvoice,
  customerPaymentAmount, customerPaymentCreatedAt,
} from './store.js';
import {
  el, clear, toast, money, number, round2, formatDate, openModal, closeModal,
  confirmDialog, nowStamp, uuid, setBusy,
} from './ui.js';
import { enqueueOp, pushQueue, rebuildStateFromDB } from './sync.js';

let customersReady = false;
let statementCustomerUuid = null;

export function initCustomers() {
  if (customersReady) return;
  customersReady = true;

  document.getElementById('customer-search').addEventListener('input', (event) => {
    state.search.customers = event.target.value;
    renderCustomers();
  });

  document.getElementById('customer-add').addEventListener('click', () => openCustomerForm(null));

  document.getElementById('customer-list').addEventListener('click', async (event) => {
    const button = event.target.closest('[data-customer-action]');
    if (!button) return;
    const customer = state.customers.find((row) => row.uuid === button.dataset.uuid);
    if (!customer) {
      toast('الزبون غير موجود محلياً', 'warn');
      return;
    }
    const action = button.dataset.customerAction;
    if (action === 'edit') openCustomerForm(customer);
    if (action === 'statement') await openStatement(customer);
    if (action === 'payment') openDebtModal(customer);
    if (action === 'delete') await deleteCustomer(customer);
  });

  document.getElementById('customer-form').addEventListener('submit', saveCustomer);
  document.getElementById('debt-form').addEventListener('submit', saveDebtPayment);
  document.getElementById('statement-payment').addEventListener('click', () => {
    const customer = state.customers.find((row) => row.uuid === statementCustomerUuid);
    if (customer) openDebtModal(customer);
  });
}

export function renderCustomers() {
  const host = document.getElementById('customer-list');
  if (!host) return;
  clear(host);
  const query = state.search.customers.trim().toLowerCase();
  const rows = state.customers.filter((customer) => {
    if (isDeleted(customer)) return false;
    if (!query) return true;
    return customerName(customer).toLowerCase().includes(query) ||
      customerPhone(customer).toLowerCase().includes(query);
  });
  if (!rows.length) {
    host.appendChild(el('p', { class: 'empty' }, 'لا يوجد زبائن. أضف زبوناً جديداً أو زامن الخادم.'));
    return;
  }
  for (const customer of rows) {
    const due = customerDue(customer);
    host.appendChild(el('div', { class: 'list-item' },
      el('div', { class: 'list-main' },
        el('div', { class: 'list-title', text: customerName(customer) }),
        el('div', { class: 'list-sub', text: customerPhone(customer) || 'بدون هاتف' }),
        el('div', { class: 'list-sub' },
          el('span', { class: due > 0 ? 'badge danger' : 'badge', text: 'دين ' + money(due) })
        )
      ),
      el('div', { class: 'list-actions' },
        el('button', { class: 'btn btn-small', type: 'button', dataset: { customerAction: 'statement', uuid: customer.uuid }, text: 'كشف' }),
        el('button', { class: 'btn btn-small', type: 'button', dataset: { customerAction: 'payment', uuid: customer.uuid }, text: 'دفعة' }),
        el('button', { class: 'link-btn', type: 'button', dataset: { customerAction: 'edit', uuid: customer.uuid }, text: 'تعديل' }),
        el('button', { class: 'link-btn danger-text', type: 'button', dataset: { customerAction: 'delete', uuid: customer.uuid }, text: 'حذف' })
      )
    ));
  }
}

function openCustomerForm(customer) {
  const existing = customer || null;
  document.getElementById('customer-form-title').textContent = existing ? 'تعديل زبون' : 'زبون جديد';
  document.getElementById('cf-uuid').value = existing ? existing.uuid : '';
  document.getElementById('cf-name').value = existing ? customerName(existing) : '';
  document.getElementById('cf-phone').value = existing ? customerPhone(existing) : '';
  document.getElementById('cf-address').value = existing ? String(existing.address || existing.Address || '') : '';
  document.getElementById('cf-limit').value = existing ? String(customerLimit(existing)) : '0';
  document.getElementById('cf-notes').value = existing ? String(existing.notes || existing.Notes || '') : '';
  document.getElementById('customer-error').textContent = '';
  openModal('modal-customer');
  setTimeout(() => document.getElementById('cf-name').focus(), 120);
}

async function saveCustomer(event) {
  event.preventDefault();
  const errorEl = document.getElementById('customer-error');
  errorEl.textContent = '';
  const fullName = document.getElementById('cf-name').value.trim();
  if (!fullName) {
    errorEl.textContent = 'اسم الزبون مطلوب';
    return;
  }
  const customerUuid = document.getElementById('cf-uuid').value || uuid();
  const existing = state.customers.find((row) => row.uuid === customerUuid) || null;
  const payload = {
    uuid: customerUuid,
    full_name: fullName,
    phone: document.getElementById('cf-phone').value.trim(),
    address: document.getElementById('cf-address').value.trim(),
    credit_limit: round2(Math.max(0, number(document.getElementById('cf-limit').value))),
    notes: document.getElementById('cf-notes').value.trim(),
  };
  const now = new Date().toISOString();
  const row = Object.assign({}, existing || {}, payload, {
    uuid: customerUuid,
    deleted_at: null,
    updated_at: now,
    version: existing ? number(existing.version, 1) + 1 : 1,
  });

  const saveButton = document.getElementById('customer-save');
  setBusy(saveButton, true);
  try {
    await db.put('customers', row);
    await enqueueOp('customer_save', payload);
    await rebuildStateFromDB();
    closeModal('modal-customer');
    toast(existing ? 'تم تحديث الزبون' : 'تم إضافة الزبون', 'success');
    pushQueue().catch((err) => console.warn('Customer push postponed:', err));
  } catch (err) {
    console.error('Save customer failed:', err);
    errorEl.textContent = err.message || 'تعذر حفظ الزبون';
    toast('تعذر حفظ الزبون', 'error');
  } finally {
    setBusy(saveButton, false);
  }
}

async function deleteCustomer(customer) {
  const confirmed = await confirmDialog('حذف "' + customerName(customer) + '"؟', {
    title: 'حذف زبون',
    danger: true,
    confirmText: 'حذف',
  });
  if (!confirmed) return;
  try {
    await db.put('customers', Object.assign({}, customer, { deleted_at: new Date().toISOString() }));
    await enqueueOp('customer_delete', { uuid: customer.uuid });
    await rebuildStateFromDB();
    toast('تم حذف الزبون', 'success');
    pushQueue().catch((err) => console.warn('Customer delete push postponed:', err));
  } catch (err) {
    console.error('Delete customer failed:', err);
    toast(err.message || 'تعذر حذف الزبون', 'error');
  }
}

/* ---------- statement ---------- */

async function openStatement(customer) {
  statementCustomerUuid = customer.uuid;
  const title = document.getElementById('statement-title');
  title.textContent = 'كشف حساب — ' + customerName(customer);
  const body = document.getElementById('statement-body');
  clear(body);
  body.appendChild(el('p', { class: 'empty' }, 'جارٍ تحميل الكشف...'));
  openModal('modal-statement');
  let data = null;
  if (state.online) {
    try {
      data = await api.customerStatement(customer.uuid);
    } catch (err) {
      console.warn('Customer statement API failed, using cache:', err);
      if (!(err instanceof ApiError && (err.network || err.status >= 500))) {
        toast(err.message || 'تعذر تحميل كشف الحساب', 'warn');
      }
    }
  }
  const payload = data && data.entries ? data : buildLocalStatement(customer);
  renderStatement(payload);
}

function buildLocalStatement(customer) {
  const entries = [];
  const target = customer.uuid;
  for (const sale of state.sales) {
    if (saleCustomerUuid(sale) !== target) continue;
    const due = number(sale.due !== undefined ? sale.due : sale.Due);
    if (due <= 0) continue;
    entries.push({
      date: saleCreatedAt(sale),
      type: 'sale',
      ref: saleInvoice(sale),
      debit: due,
      credit: 0,
    });
  }
  for (const payment of state.customerPayments) {
    const paymentCustomer = payment.customer_uuid || payment.CustomerUuid;
    if (paymentCustomer !== target) continue;
    entries.push({
      date: customerPaymentCreatedAt(payment),
      type: 'payment',
      ref: 'دفعة',
      debit: 0,
      credit: customerPaymentAmount(payment),
    });
  }
  entries.sort((a, b) => String(a.date).localeCompare(String(b.date)));
  const knownMovement = entries.reduce((sum, entry) => sum + number(entry.debit) - number(entry.credit), 0);
  const opening = Math.max(0, round2(customerDue(customer) - knownMovement));
  let balance = opening;
  for (const entry of entries) {
    balance = round2(balance + number(entry.debit) - number(entry.credit));
    entry.balance = balance;
  }
  return { customer, opening_balance: opening, entries };
}

function renderStatement(data) {
  const body = document.getElementById('statement-body');
  clear(body);
  const customer = data.customer || {};
  const opening = number(data.opening_balance);
  const entries = Array.isArray(data.entries) ? data.entries : [];
  body.appendChild(el('div', { class: 'stack' },
    el('div', { class: 'kv' }, el('span', {}, 'الرصيد الافتتاحي'), el('b', { class: 'money', text: money(opening) })),
    el('div', { class: 'kv' }, el('span', {}, 'الرصيد الحالي'), el('b', { class: 'money danger-text', text: money(customerDue(customer)) })),
    el('div', { class: 'kv' }, el('span', {}, 'سقف الدين'), el('b', { class: 'money', text: customerLimit(customer) ? money(customerLimit(customer)) : 'بلا حد' }))
  ));
  if (!entries.length) {
    body.appendChild(el('p', { class: 'empty' }, 'لا توجد حركات مسجلة'));
    return;
  }
  const labels = { sale: 'بيع', payment: 'دفعة', return: 'إرجاع' };
  const table = el('table', { class: 'statement-table' },
    el('thead', {}, el('tr', {},
      el('th', {}, 'التاريخ'), el('th', {}, 'النوع'), el('th', {}, 'مرجع'),
      el('th', {}, 'مدين'), el('th', {}, 'دائن'), el('th', {}, 'الرصيد')
    ))
  );
  const tbody = el('tbody');
  for (const entry of entries) {
    tbody.appendChild(el('tr', {},
      el('td', { text: formatDate(entry.date) }),
      el('td', { text: labels[entry.type] || entry.type || '—' }),
      el('td', { text: String(entry.ref || '—') }),
      el('td', { class: 'num', text: money(entry.debit) }),
      el('td', { class: 'num', text: money(entry.credit) }),
      el('td', { class: 'num', text: money(entry.balance) })
    ));
  }
  table.appendChild(tbody);
  body.appendChild(table);
}

/* ---------- debt payment ---------- */

function openDebtModal(customer) {
  if (!customer) return;
  document.getElementById('debt-title').textContent = 'دفعة — ' + customerName(customer);
  document.getElementById('debt-customer').value = customer.uuid;
  const due = customerDue(customer);
  document.getElementById('debt-amount').value = due > 0 ? due.toFixed(2) : '';
  document.getElementById('debt-method').value = 'cash';
  document.getElementById('debt-notes').value = '';
  document.getElementById('debt-error').textContent = '';
  openModal('modal-debt');
  setTimeout(() => document.getElementById('debt-amount').focus(), 120);
}

async function saveDebtPayment(event) {
  event.preventDefault();
  const errorEl = document.getElementById('debt-error');
  errorEl.textContent = '';
  const customerUuid = document.getElementById('debt-customer').value;
  const customer = state.customers.find((row) => row.uuid === customerUuid);
  if (!customer) {
    errorEl.textContent = 'الزبون غير موجود';
    return;
  }
  const amount = round2(number(document.getElementById('debt-amount').value));
  if (!(amount > 0)) {
    errorEl.textContent = 'أدخل مبلغاً صحيحاً أكبر من صفر';
    return;
  }
  const method = document.getElementById('debt-method').value || 'cash';
  const notes = document.getElementById('debt-notes').value.trim();
  const paymentUuid = uuid();
  const now = nowStamp();
  const payment = {
    uuid: paymentUuid,
    customer_uuid: customerUuid,
    amount,
    method,
    notes,
    created_at: now,
    updated_at: now,
    deleted_at: null,
    version: 1,
  };
  const newDue = Math.max(0, round2(customerDue(customer) - amount));
  const updatedCustomer = Object.assign({}, customer, { updated_at: now, version: number(customer.version, 1) + 1 });
  if (customer.DueBalance !== undefined && customer.due_balance === undefined) updatedCustomer.DueBalance = newDue;
  else updatedCustomer.due_balance = newDue;

  const saveButton = document.getElementById('debt-save');
  setBusy(saveButton, true);
  try {
    await db.put('customer_payments', payment);
    await db.put('customers', updatedCustomer);
    await enqueueOp('customer_payment_add', { uuid: paymentUuid, customer_uuid: customerUuid, amount, method, notes });
    await rebuildStateFromDB();
    closeModal('modal-debt');
    toast('تم تسجيل الدفعة: ' + money(amount), 'success');
    pushQueue().catch((err) => console.warn('Debt payment push postponed:', err));
  } catch (err) {
    console.error('Save debt payment failed:', err);
    errorEl.textContent = err.message || 'تعذر تسجيل الدفعة';
    toast('تعذر تسجيل الدفعة', 'error');
  } finally {
    setBusy(saveButton, false);
  }
}
