/* LmimaShop V2 mobile — DOM helpers, toasts, modals and formatting.
 * User data is NEVER injected through innerHTML: use el()/textContent.
 */

const TOAST_TYPES = ['info', 'success', 'error', 'warn'];
const modalCloseHooks = new Map();
let toastHost = null;
let uiReady = false;

export function el(tag, props = null, ...children) {
  const node = document.createElement(tag);
  if (props) {
    for (const [key, value] of Object.entries(props)) {
      if (value === undefined || value === null || value === false) continue;
      if (key === 'class' || key === 'className') node.className = value;
      else if (key === 'text') node.textContent = String(value);
      else if (key === 'dataset') Object.assign(node.dataset, value);
      else if (key === 'attrs') {
        for (const [attr, attrValue] of Object.entries(value)) {
          if (attrValue !== undefined && attrValue !== null) node.setAttribute(attr, String(attrValue));
        }
      } else if (key === 'on') {
        for (const [event, handler] of Object.entries(value)) node.addEventListener(event, handler);
      } else if (key === 'value') {
        node.value = value;
      } else if (typeof node[key] !== 'undefined' && key !== 'list') {
        try {
          node[key] = value;
        } catch (err) {
          console.error('Could not set element property:', key, err);
          node.setAttribute(key, String(value));
        }
      } else {
        node.setAttribute(key, String(value));
      }
    }
  }
  appendChildren(node, children);
  return node;
}

function appendChildren(node, children) {
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    if (Array.isArray(child)) {
      appendChildren(node, child);
    } else if (child instanceof Node) {
      node.appendChild(child);
    } else {
      node.appendChild(document.createTextNode(String(child)));
    }
  }
}

export function clear(node) {
  if (!node) return;
  while (node.firstChild) node.removeChild(node.firstChild);
}

export function setText(node, text) {
  if (node) node.textContent = text === null || text === undefined ? '' : String(text);
}

export function escapeHtml(value) {
  return String(value === null || value === undefined ? '' : value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

export function toast(message, type = 'info', duration = 3600) {
  const safeType = TOAST_TYPES.includes(type) ? type : 'info';
  if (!toastHost) {
    toastHost = el('div', { class: 'toast-host', attrs: { 'aria-live': 'polite', 'aria-atomic': 'true' } });
    document.body.appendChild(toastHost);
  }
  const item = el('div', { class: 'toast toast-' + safeType, attrs: { role: 'status' } }, String(message));
  toastHost.appendChild(item);
  const timer = setTimeout(() => {
    item.classList.add('toast-out');
    setTimeout(() => item.remove(), 220);
  }, duration);
  item.addEventListener('click', () => {
    clearTimeout(timer);
    item.remove();
  });
  return item;
}

export function openModal(id) {
  const modal = document.getElementById(id);
  if (!modal) {
    console.error('Modal not found:', id);
    toast('تعذر فتح النافذة المطلوبة', 'error');
    return;
  }
  modal.hidden = false;
  requestAnimationFrame(() => modal.classList.add('open'));
  document.body.classList.add('modal-open');
  const firstField = modal.querySelector('input:not([type="hidden"]), select, textarea, button');
  if (firstField) setTimeout(() => firstField.focus(), 80);
}

export function closeModal(id) {
  const modal = document.getElementById(id);
  if (!modal) return;
  const hook = modalCloseHooks.get(id);
  if (hook) {
    modalCloseHooks.delete(id);
    try {
      hook();
    } catch (err) {
      console.error('Modal close hook failed:', id, err);
    }
  }
  modal.classList.remove('open');
  modal.hidden = true;
  if (!document.querySelector('.modal.open')) document.body.classList.remove('modal-open');
}

export function closeAllModals() {
  document.querySelectorAll('.modal.open').forEach((modal) => {
    if (modal.id) closeModal(modal.id);
    else {
      modal.classList.remove('open');
      modal.hidden = true;
    }
  });
  document.body.classList.remove('modal-open');
}

export function onModalClose(id, handler) {
  modalCloseHooks.set(id, handler);
}

export function initUI() {
  if (uiReady) return;
  uiReady = true;
  document.addEventListener('click', (event) => {
    const closer = event.target.closest('[data-close]');
    if (!closer) return;
    const modal = closer.closest('.modal');
    if (!modal) return;
    const target = closer.getAttribute('data-close') || modal.id;
    if (target && document.getElementById(target)) {
      closeModal(target);
    } else if (modal.id) {
      closeModal(modal.id);
    }
  });
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    const open = document.querySelectorAll('.modal.open');
    if (!open.length) return;
    const top = open[open.length - 1];
    if (top.id) closeModal(top.id);
    else {
      top.classList.remove('open');
      top.hidden = true;
    }
  });
}

export function setBusy(button, busy) {
  if (!button) return;
  if (busy) {
    if (!button.dataset.originalText) button.dataset.originalText = button.textContent;
    button.disabled = true;
    button.textContent = 'جارٍ...';
  } else {
    button.disabled = false;
    if (button.dataset.originalText) {
      button.textContent = button.dataset.originalText;
      delete button.dataset.originalText;
    }
  }
}

export function confirmDialog(message, options = {}) {
  const title = options.title || 'تأكيد';
  const confirmText = options.confirmText || 'تأكيد';
  const cancelText = options.cancelText || 'إلغاء';
  const danger = Boolean(options.danger);
  return new Promise((resolve) => {
    const overlay = el('div', { class: 'modal open', id: 'modal-confirm-dynamic' });
    const finish = (value) => {
      overlay.classList.remove('open');
      overlay.remove();
      if (!document.querySelector('.modal.open')) document.body.classList.remove('modal-open');
      resolve(value);
    };
    overlay.appendChild(el('div', { class: 'modal-backdrop', on: { click: () => finish(false) } }));
    const card = el('div', { class: 'modal-card modal-card-small', attrs: { role: 'dialog', 'aria-modal': 'true' } },
      el('header', { class: 'modal-head' },
        el('h3', { text: title }),
        el('button', { class: 'icon-btn', type: 'button', attrs: { 'aria-label': 'إغلاق' }, on: { click: () => finish(false) }, text: '×' })
      ),
      el('div', { class: 'modal-body' }, el('p', { class: 'confirm-text', text: message })),
      el('footer', { class: 'modal-foot' },
        el('button', { class: 'btn btn-ghost', type: 'button', on: { click: () => finish(false) }, text: cancelText }),
        el('button', { class: danger ? 'btn btn-danger' : 'btn btn-primary', type: 'button', on: { click: () => finish(true) }, text: confirmText })
      )
    );
    overlay.appendChild(card);
    document.body.appendChild(overlay);
    document.body.classList.add('modal-open');
    const focusButton = card.querySelector('.btn-primary, .btn-danger');
    if (focusButton) setTimeout(() => focusButton.focus(), 60);
  });
}

export function promptNumber(options = {}) {
  const title = options.title || 'أدخل قيمة';
  const message = options.message || '';
  const confirmText = options.confirmText || 'تأكيد';
  const min = options.min === undefined ? 0 : options.min;
  const max = options.max === undefined ? null : options.max;
  const step = options.step || 'any';
  return new Promise((resolve) => {
    const overlay = el('div', { class: 'modal open', id: 'modal-prompt-dynamic' });
    const input = el('input', {
      class: 'input',
      type: 'number',
      inputmode: 'decimal',
      step,
      min,
      value: options.value === undefined ? '' : options.value,
      required: true,
    });
    if (max !== null) input.max = max;
    const error = el('p', { class: 'form-error' });
    const finish = (value) => {
      overlay.classList.remove('open');
      overlay.remove();
      if (!document.querySelector('.modal.open')) document.body.classList.remove('modal-open');
      resolve(value);
    };
    const submit = () => {
      const value = Number(input.value);
      if (!Number.isFinite(value) || value < min || (max !== null && value > max)) {
        error.textContent = 'القيمة غير صحيحة';
        return;
      }
      finish(value);
    };
    overlay.appendChild(el('div', { class: 'modal-backdrop', on: { click: () => finish(null) } }));
    const card = el('form', { class: 'modal-card modal-card-small', attrs: { role: 'dialog', 'aria-modal': 'true' } },
      el('header', { class: 'modal-head' },
        el('h3', { text: title }),
        el('button', { class: 'icon-btn', type: 'button', attrs: { 'aria-label': 'إغلاق' }, on: { click: () => finish(null) }, text: '×' })
      ),
      el('div', { class: 'modal-body' },
        message ? el('p', { class: 'muted', text: message }) : null,
        el('label', { class: 'field' }, el('span', { text: options.label || 'الكمية' }), input),
        error
      ),
      el('footer', { class: 'modal-foot' },
        el('button', { class: 'btn btn-ghost', type: 'button', on: { click: () => finish(null) }, text: 'إلغاء' }),
        el('button', { class: 'btn btn-primary', type: 'submit', text: confirmText })
      )
    );
    card.addEventListener('submit', (event) => {
      event.preventDefault();
      submit();
    });
    overlay.appendChild(card);
    document.body.appendChild(overlay);
    document.body.classList.add('modal-open');
    setTimeout(() => {
      input.focus();
      input.select();
    }, 60);
  });
}

/* ---------- formatting ---------- */

const moneyFormatter = new Intl.NumberFormat('en-US', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export function number(value, fallback = 0) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

export function round2(value) {
  return Math.round((number(value) + Number.EPSILON) * 100) / 100;
}

export function money(value) {
  return moneyFormatter.format(round2(number(value))) + ' د.م';
}

/** Display-only helper: 1 MAD = 20 Rial. */
export function rial(value) {
  return moneyFormatter.format(round2(number(value) * 20)) + ' ريال';
}

export function parseDate(value) {
  if (value instanceof Date) return value;
  if (!value) return null;
  if (typeof value === 'number') {
    const fromNumber = new Date(value);
    return Number.isNaN(fromNumber.getTime()) ? null : fromNumber;
  }
  const text = String(value).trim();
  const normalized = text.includes('T') ? text : text.replace(' ', 'T');
  let date = new Date(normalized);
  if (Number.isNaN(date.getTime())) date = new Date(text);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function formatDate(value, withTime = false) {
  const date = parseDate(value);
  if (!date) return '—';
  const dateText = new Intl.DateTimeFormat('ar-MA', { year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
  if (!withTime) return dateText;
  const timeText = new Intl.DateTimeFormat('ar-MA', { hour: '2-digit', minute: '2-digit' }).format(date);
  return dateText + ' ' + timeText;
}

export function localDateKey(value = new Date()) {
  const date = value instanceof Date ? value : parseDate(value);
  if (!date) return '';
  const pad = (part) => String(part).padStart(2, '0');
  return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate());
}

export function nowStamp() {
  const date = new Date();
  const pad = (part, size = 2) => String(part).padStart(size, '0');
  return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate()) +
    ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes()) + ':' + pad(date.getSeconds()) +
    '.' + pad(date.getMilliseconds(), 3);
}

export function debounce(fn, wait = 300) {
  let timer = null;
  return function debounced(...args) {
    clearTimeout(timer);
    timer = setTimeout(() => fn.apply(this, args), wait);
  };
}

export function uuid() {
  const cryptoApi = globalThis.crypto;
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') return cryptoApi.randomUUID();
  const bytes = new Uint8Array(16);
  if (cryptoApi && cryptoApi.getRandomValues) cryptoApi.getRandomValues(bytes);
  else for (let i = 0; i < bytes.length; i += 1) bytes[i] = Math.floor(Math.random() * 256);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return hex.slice(0, 8) + '-' + hex.slice(8, 12) + '-' + hex.slice(12, 16) + '-' +
    hex.slice(16, 20) + '-' + hex.slice(20);
}
