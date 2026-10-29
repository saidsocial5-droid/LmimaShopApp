/* LmimaShop V2 mobile — POS tab: catalog, cart, barcode scan and split payment. */

import { api, ApiError } from './api.js';
import * as db from './db.js';
import {
  state, isDeleted, productName, productBarcode, productCategory, productPrice,
  productTva, productWeighed, productAvailable, addToCart, setCartQty, removeFromCart,
  clearCart, setDiscount, cartTotals,
} from './store.js';
import {
  el, clear, toast, money, number, round2, confirmDialog, promptNumber,
} from './ui.js';
import { scanCode } from './scanner.js';
import { rebuildStateFromDB } from './sync.js';

let posReady = false;

function qtyText(value) {
  const qty = number(value);
  return Number.isInteger(qty) ? String(qty) : String(Number(qty.toFixed(3)));
}

function productById(id) {
  return state.products.find((product) => product.uuid === id) || null;
}

function filteredProducts() {
  const query = state.search.pos.trim().toLowerCase();
  return state.products.filter((product) => {
    if (isDeleted(product)) return false;
    if (state.category && productCategory(product) !== state.category) return false;
    if (!query) return true;
    return productName(product).toLowerCase().includes(query) ||
      productBarcode(product).toLowerCase().includes(query);
  });
}

export function initPOS() {
  if (posReady) return;
  posReady = true;

  document.getElementById('barcode-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const input = document.getElementById('barcode-input');
    const code = input.value.trim();
    if (!code) return;
    input.value = '';
    await handleBarcode(code);
    input.focus();
  });

  document.getElementById('pos-scan-btn').addEventListener('click', async () => {
    const decoded = await scanCode();
    if (decoded) await handleBarcode(decoded);
  });

  document.getElementById('pos-search').addEventListener('input', (event) => {
    state.search.pos = event.target.value;
    renderProductGrid();
  });

  document.getElementById('pos-categories').addEventListener('click', (event) => {
    const chip = event.target.closest('[data-category]');
    if (!chip) return;
    state.category = chip.dataset.category || '';
    renderCategoryChips();
    renderProductGrid();
  });

  document.getElementById('product-grid').addEventListener('click', async (event) => {
    const card = event.target.closest('[data-add]');
    if (!card) return;
    const product = productById(card.dataset.add);
    if (product) await addProductToCart(product);
  });

  document.getElementById('cart-lines').addEventListener('click', (event) => {
    const button = event.target.closest('[data-cart-action]');
    if (!button) return;
    const uuid = button.dataset.uuid;
    const line = state.cart.find((item) => item.uuid === uuid);
    if (!line) return;
    const action = button.dataset.cartAction;
    if (action === 'remove') {
      removeFromCart(uuid);
      renderPOS();
      return;
    }
    const nextQty = action === 'inc' ? line.qty + 1 : line.qty - 1;
    const result = setCartQty(uuid, round2(nextQty));
    if (!result.ok) toast(result.message, 'warn');
    renderPOS();
  });

  document.getElementById('cart-clear').addEventListener('click', async () => {
    if (!state.cart.length) return;
    const confirmed = await confirmDialog('هل تريد تفريغ السلة؟', { title: 'تفريغ السلة', danger: true, confirmText: 'تفريغ' });
    if (!confirmed) return;
    clearCart();
    renderPOS();
  });

  document.querySelectorAll('[data-discount]').forEach((button) => {
    button.addEventListener('click', () => {
      setDiscount(button.dataset.discount);
      renderPOS();
    });
  });
}

export async function handleBarcode(rawCode) {
  const code = String(rawCode || '').trim();
  if (!code) return;
  let product = state.products.find((row) => !isDeleted(row) && productBarcode(row) === code) || null;
  if (!product && state.online) {
    try {
      const row = await api.productByBarcode(code);
      if (row && row.uuid) {
        await db.put('products', row);
        await rebuildStateFromDB();
        product = productById(row.uuid);
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        toast('لا يوجد منتج بهذا الباركود', 'warn');
      } else if (err instanceof ApiError && err.network) {
        toast('لا يمكن البحث على الخادم — أنت غير متصل', 'warn');
      } else {
        console.error('Barcode lookup failed:', err);
        toast(err.message || 'تعذر البحث عن الباركود', 'error');
      }
    }
  }
  if (!product) {
    toast('لا يوجد منتج بهذا الباركود', 'warn');
    return;
  }
  await addProductToCart(product);
}

async function addProductToCart(product) {
  let quantity = 1;
  if (productWeighed(product)) {
    const entered = await promptNumber({
      title: productName(product),
      message: 'منتج بالوزن — أدخل الكمية أو الوزن',
      label: 'الكمية',
      value: 1,
      min: 0.001,
      confirmText: 'إضافة',
    });
    if (entered === null) return;
    quantity = entered;
  }
  const result = addToCart(product, quantity);
  if (!result.ok) {
    toast(result.message, 'warn');
    return;
  }
  renderPOS();
}

export function renderPOS() {
  renderCategoryChips();
  renderProductGrid();
  renderCart();
  const searchInput = document.getElementById('pos-search');
  if (searchInput && document.activeElement !== searchInput) searchInput.value = state.search.pos;
}

function renderCategoryChips() {
  const host = document.getElementById('pos-categories');
  if (!host) return;
  clear(host);
  const names = new Set();
  for (const product of state.products) {
    const name = productCategory(product);
    if (name) names.add(name);
  }
  for (const category of state.categories) {
    const name = category.name || category.Name;
    if (name) names.add(String(name));
  }
  const all = el('button', {
    class: 'chip-btn' + (state.category ? '' : ' is-active'),
    type: 'button', dataset: { category: '' }, text: 'الكل',
  });
  host.appendChild(all);
  for (const name of Array.from(names).sort((a, b) => a.localeCompare(b, 'ar'))) {
    host.appendChild(el('button', {
      class: 'chip-btn' + (state.category === name ? ' is-active' : ''),
      type: 'button', dataset: { category: name }, text: name,
    }));
  }
}

function renderProductGrid() {
  const grid = document.getElementById('product-grid');
  if (!grid) return;
  clear(grid);
  const products = filteredProducts();
  if (!products.length) {
    grid.appendChild(el('p', { class: 'empty' }, 'لا توجد منتجات. أضف منتجات من تبويب المنتجات أو زامن الخادم.'));
    return;
  }
  for (const product of products) {
    const available = productAvailable(product);
    const card = el('button', {
      class: 'product-card' + (available <= 0 ? ' out' : ''),
      type: 'button',
      dataset: { add: product.uuid },
      attrs: { 'aria-label': productName(product) },
    },
      el('span', { class: 'product-name', text: productName(product) }),
      el('span', { class: 'product-meta' },
        el('span', { class: 'product-price money', text: money(productPrice(product)) }),
        el('span', { class: 'stock' + (available <= 0 ? ' low' : ''), text: qtyText(available) })
      )
    );
    grid.appendChild(card);
  }
}

function renderCart() {
  const host = document.getElementById('cart-lines');
  if (!host) return;
  clear(host);
  if (!state.cart.length) {
    host.appendChild(el('p', { class: 'empty' }, 'السلة فارغة — أضف منتجات من الأعلى'));
  } else {
    for (const line of state.cart) {
      host.appendChild(el('div', { class: 'cart-line' },
        el('div', {},
          el('div', { class: 'line-name', text: line.name }),
          el('div', { class: 'line-price' },
            money(line.price) + ' × ' + qtyText(line.qty) + ' = ',
            el('span', { class: 'line-total money', text: money(line.price * line.qty) })
          )
        ),
        el('div', { class: 'qty-controls' },
          el('button', { class: 'qty-btn', type: 'button', dataset: { cartAction: 'dec', uuid: line.uuid }, attrs: { 'aria-label': 'إنقاص' }, text: '−' }),
          el('span', { class: 'qty-value', text: qtyText(line.qty) }),
          el('button', { class: 'qty-btn', type: 'button', dataset: { cartAction: 'inc', uuid: line.uuid }, attrs: { 'aria-label': 'زيادة' }, text: '+' }),
          el('button', { class: 'remove-btn', type: 'button', dataset: { cartAction: 'remove', uuid: line.uuid }, attrs: { 'aria-label': 'حذف' }, text: '×' })
        )
      ));
    }
  }
  const totals = cartTotals();
  document.getElementById('cart-subtotal').textContent = money(totals.subtotal);
  document.getElementById('cart-discount').textContent = money(totals.discount);
  document.getElementById('cart-tva').textContent = money(totals.tva);
  document.getElementById('cart-total').textContent = money(totals.total);
  document.getElementById('cart-count').textContent = qtyText(totals.count);
  document.getElementById('checkout-btn').disabled = totals.lines === 0;
  document.querySelectorAll('[data-discount]').forEach((button) => {
    button.classList.toggle('is-active', number(button.dataset.discount) === number(state.discountPct));
  });
}
