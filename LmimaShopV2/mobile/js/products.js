/* LmimaShop V2 mobile — product catalog management (offline-first via ops). */

import * as db from './db.js';
import {
  state, isDeleted, productName, productBarcode, productCategory, productPrice, productCost,
  productStock, productTva, productWeighed, productAvailable, removeFromCart,
} from './store.js';
import {
  el, clear, toast, money, number, round2, openModal, closeModal, confirmDialog,
  nowStamp, uuid, setBusy,
} from './ui.js';
import { scanCode } from './scanner.js';
import { enqueueOp, pushQueue, rebuildStateFromDB } from './sync.js';

let productsReady = false;

export function initProducts() {
  if (productsReady) return;
  productsReady = true;

  document.getElementById('product-search').addEventListener('input', (event) => {
    state.search.products = event.target.value;
    renderProducts();
  });

  document.getElementById('product-add').addEventListener('click', () => openProductForm(null));

  document.getElementById('product-admin-list').addEventListener('click', async (event) => {
    const button = event.target.closest('[data-product-action]');
    if (!button) return;
    const product = state.products.find((row) => row.uuid === button.dataset.uuid);
    if (!product) {
      toast('المنتج غير موجود في البيانات المحلية', 'warn');
      return;
    }
    if (button.dataset.productAction === 'edit') openProductForm(product);
    if (button.dataset.productAction === 'delete') await deleteProduct(product);
  });

  document.getElementById('product-form').addEventListener('submit', saveProduct);

  document.getElementById('pf-scan').addEventListener('click', async () => {
    const decoded = await scanCode();
    if (!decoded) return;
    document.getElementById('pf-barcode').value = String(decoded).trim();
  });
}

export function renderProducts() {
  const host = document.getElementById('product-admin-list');
  if (!host) return;
  clear(host);
  const query = state.search.products.trim().toLowerCase();
  const rows = state.products.filter((product) => {
    if (isDeleted(product)) return false;
    if (!query) return true;
    return productName(product).toLowerCase().includes(query) ||
      productBarcode(product).toLowerCase().includes(query) ||
      productCategory(product).toLowerCase().includes(query);
  });
  if (!rows.length) {
    host.appendChild(el('p', { class: 'empty' }, 'لا توجد منتجات مطابقة. أضف منتجاً جديداً أو زامن الخادم.'));
    return;
  }
  for (const product of rows) {
    const available = productAvailable(product);
    host.appendChild(el('div', { class: 'list-item' },
      el('div', { class: 'list-main' },
        el('div', { class: 'list-title', text: productName(product) }),
        el('div', { class: 'list-sub', text: [productBarcode(product), productCategory(product)].filter(Boolean).join(' · ') || 'بدون باركود' }),
        el('div', { class: 'list-sub' },
          el('span', { text: money(productPrice(product)) }),
          ' · المخزون: ',
          el('span', { class: available <= 0 ? 'danger-text' : '', text: String(round2(available)) })
        )
      ),
      el('div', { class: 'list-actions' },
        el('button', { class: 'btn btn-small', type: 'button', dataset: { productAction: 'edit', uuid: product.uuid }, text: 'تعديل' }),
        el('button', { class: 'link-btn danger-text', type: 'button', dataset: { productAction: 'delete', uuid: product.uuid }, text: 'حذف' })
      )
    ));
  }
}

function fillCategoryList() {
  const list = document.getElementById('category-list');
  if (!list) return;
  clear(list);
  const names = new Set();
  for (const product of state.products) {
    const name = productCategory(product);
    if (name) names.add(name);
  }
  for (const category of state.categories) {
    const name = category.name || category.Name;
    if (name) names.add(String(name));
  }
  for (const name of Array.from(names).sort((a, b) => a.localeCompare(b, 'ar'))) {
    list.appendChild(el('option', { value: name }));
  }
}

function openProductForm(product) {
  fillCategoryList();
  const existing = product || null;
  document.getElementById('product-form-title').textContent = existing ? 'تعديل منتج' : 'منتج جديد';
  document.getElementById('pf-uuid').value = existing ? existing.uuid : '';
  document.getElementById('pf-name').value = existing ? productName(existing) : '';
  document.getElementById('pf-barcode').value = existing ? productBarcode(existing) : '';
  document.getElementById('pf-category').value = existing ? productCategory(existing) : '';
  document.getElementById('pf-cost').value = existing ? String(productCost(existing)) : '0';
  document.getElementById('pf-sell').value = existing ? String(productPrice(existing)) : '0';
  document.getElementById('pf-stock').value = existing ? String(productStock(existing)) : '0';
  document.getElementById('pf-tva').value = existing ? String(productTva(existing)) : '20';
  document.getElementById('pf-weighed').checked = existing ? productWeighed(existing) : false;
  document.getElementById('product-error').textContent = '';
  openModal('modal-product');
  setTimeout(() => document.getElementById('pf-name').focus(), 120);
}

async function saveProduct(event) {
  event.preventDefault();
  const errorEl = document.getElementById('product-error');
  errorEl.textContent = '';
  const name = document.getElementById('pf-name').value.trim();
  if (!name) {
    errorEl.textContent = 'اسم المنتج مطلوب';
    return;
  }
  const sellPrice = number(document.getElementById('pf-sell').value, -1);
  if (sellPrice < 0) {
    errorEl.textContent = 'سعر البيع غير صحيح';
    return;
  }
  const stockQty = number(document.getElementById('pf-stock').value, -1);
  if (stockQty < 0) {
    errorEl.textContent = 'المخزون لا يمكن أن يكون سالباً';
    return;
  }
  const productUuid = document.getElementById('pf-uuid').value || uuid();
  const existing = state.products.find((row) => row.uuid === productUuid) || null;
  const barcode = document.getElementById('pf-barcode').value.trim();
  const duplicate = state.products.find((row) => row.uuid !== productUuid && productBarcode(row) === barcode);
  if (barcode && duplicate) {
    errorEl.textContent = 'الباركود مستخدم لمنتج آخر: ' + productName(duplicate);
    return;
  }

  const payload = {
    uuid: productUuid,
    barcode,
    name,
    category: document.getElementById('pf-category').value.trim(),
    cost_price: round2(number(document.getElementById('pf-cost').value)),
    sell_price: round2(sellPrice),
    stock_qty: round2(stockQty),
    tva_rate: round2(number(document.getElementById('pf-tva').value)),
    is_weighed: document.getElementById('pf-weighed').checked,
  };
  if (existing && existing.image_path) payload.image_path = existing.image_path;

  const now = nowStamp();
  const row = Object.assign({}, existing || {}, payload, {
    uuid: productUuid,
    deleted_at: null,
    updated_at: now,
    version: existing ? number(existing.version, 1) + 1 : 1,
  });

  const saveButton = document.getElementById('product-save');
  setBusy(saveButton, true);
  try {
    await db.put('products', row);
    await enqueueOp('product_save', payload);
    await rebuildStateFromDB();
    closeModal('modal-product');
    toast(existing ? 'تم تحديث المنتج' : 'تم إضافة المنتج', 'success');
    pushQueue().catch((err) => console.warn('Product push postponed:', err));
  } catch (err) {
    console.error('Save product failed:', err);
    errorEl.textContent = err.message || 'تعذر حفظ المنتج';
    toast('تعذر حفظ المنتج', 'error');
  } finally {
    setBusy(saveButton, false);
  }
}

async function deleteProduct(product) {
  const confirmed = await confirmDialog('حذف "' + productName(product) + '"؟ سيُخفى من التطبيق ويُرسل الحذف للخادم.', {
    title: 'حذف منتج',
    danger: true,
    confirmText: 'حذف',
  });
  if (!confirmed) return;
  try {
    const row = Object.assign({}, product, { deleted_at: nowStamp() });
    await db.put('products', row);
    await enqueueOp('product_delete', { uuid: product.uuid });
    removeFromCart(product.uuid);
    await rebuildStateFromDB();
    toast('تم حذف المنتج', 'success');
    pushQueue().catch((err) => console.warn('Product delete push postponed:', err));
  } catch (err) {
    console.error('Delete product failed:', err);
    toast(err.message || 'تعذر حذف المنتج', 'error');
  }
}
