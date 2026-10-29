/* LmimaShop V2 mobile — pairing, delta sync, idempotent push queue and SSE. */

import { api, ApiError, AuthError } from './api.js';
import { clearSession, getServerUrl, getToken } from './config.js';
import * as db from './db.js';
import { isDeleted, notify, state } from './store.js';
import { nowStamp, toast, uuid } from './ui.js';

const COLLECTIONS = [
  ['products', 'products'],
  ['categories', 'categories'],
  ['customers', 'customers'],
  ['customer_payments', 'customer_payments'],
  ['suppliers', 'suppliers'],
  ['sales', 'sales_cache'],
  ['sale_items', 'sale_items'],
  ['returns', 'returns'],
];

let deltaPromise = null;
let pushPromise = null;
let syncPromise = null;
let sseController = null;
let sseStopped = true;
let sseTimer = null;
let sseBackoff = 1000;
let scheduleTimer = null;

// The only op types the server understands. Anything else in the queue is corrupt
// (e.g. written by an old/foreign client) and must never block the whole queue.
const KNOWN_OP_TYPES = new Set([
  'product_save', 'product_delete', 'customer_save', 'customer_delete',
  'customer_payment_add', 'supplier_save', 'supplier_delete', 'sale_create', 'return_create',
]);

/// <summary>
/// Drops structurally invalid queue entries so a single bad row can never freeze sync.
/// Returns only the ops that are safe to send.
/// </summary>
async function discardCorruptOps(queue) {
  const valid = [];
  let dropped = 0;
  for (const op of queue) {
    const ok = op && typeof op === 'object'
      && typeof op.op_id === 'string' && op.op_id.length > 0
      && typeof op.type === 'string' && KNOWN_OP_TYPES.has(op.type)
      && typeof op.created_at === 'string'
      && op.payload && typeof op.payload === 'object' && !Array.isArray(op.payload);
    if (ok) {
      valid.push(op);
    } else {
      dropped += 1;
      console.error('Discarding corrupt offline op:', op);
      if (op && typeof op.op_id === 'string') {
        try { await db.removeOp(op.op_id); } catch (err) { console.warn('Could not remove corrupt op', err); }
      }
    }
  }
  if (dropped > 0) {
    toast('تم تجاهل ' + dropped + ' عملية تالفة في الطابور', 'warn', 6000);
    state.queueCount = Math.max(0, state.queueCount - dropped);
    notify();
  }
  return valid;
}

/* ---------- pairing ---------- */

export async function pairDevice(code) {
  const server = getServerUrl();
  if (!server) throw new ApiError('أدخل عنوان الخادم أولاً', { status: 0 });
  const result = await api.pair(code);
  await db.setMeta('server_url', server);
  await db.setMeta('device_id', result.device_id);
  state.serverUrl = server;
  state.token = result.token;
  await fetchDelta('');
  await rebuildStateFromDB();
  notify();
  toast('تم اقتران الجهاز بنجاح', 'success');
  return result;
}

/* ---------- delta pull ---------- */

export function fetchDelta(since = null) {
  if (deltaPromise) return deltaPromise;
  const cursor = since === null ? (state.cursor || '') : since;
  deltaPromise = (async () => {
    const payload = await api.sync(cursor);
    let changed = 0;
    for (const [key, storeName] of COLLECTIONS) {
      const rows = Array.isArray(payload[key]) ? payload[key] : [];
      if (!rows.length) continue;
      const toPut = [];
      const toDelete = [];
      for (const wrapped of rows) {
        const row = wrapped && wrapped.d ? wrapped.d : wrapped;
        if (!row) continue;
        const rowId = row.uuid || row.Uuid || row.id;
        if (!rowId) {
          console.warn('Skipping sync row without uuid:', key, row);
          continue;
        }
        row.uuid = rowId;
        const tombstone = Boolean(wrapped && wrapped.deleted) || isDeleted(row);
        if (tombstone) toDelete.push(rowId);
        else toPut.push(row);
      }
      if (toPut.length) await db.putMany(storeName, toPut);
      for (const rowId of toDelete) await db.del(storeName, rowId);
      changed += toPut.length + toDelete.length;
    }
    if (typeof payload.cursor === 'string' && payload.cursor) {
      await db.setMeta('cursor', payload.cursor);
      state.cursor = payload.cursor;
    }
    const syncedAt = nowStamp();
    await db.setMeta('last_sync', syncedAt);
    await db.setMeta('last_sync_rows', changed);
    state.lastSync = syncedAt;
    return changed;
  })();
  return deltaPromise.finally(() => {
    deltaPromise = null;
  });
}

/* ---------- local state rebuild (with optimistic pending stock) ---------- */

export async function rebuildStateFromDB() {
  const [products, categories, customers, payments, suppliers, sales, saleItems, returns, queue, cursor, lastSync] =
    await Promise.all([
      db.getAll('products'),
      db.getAll('categories'),
      db.getAll('customers'),
      db.getAll('customer_payments'),
      db.getAll('suppliers'),
      db.getAll('sales_cache'),
      db.getAll('sale_items'),
      db.getAll('returns'),
      db.getQueue(),
      db.getMeta('cursor', null),
      db.getMeta('last_sync', null),
    ]);

  const pendingStock = {};
  for (const op of queue) {
    const payload = op.payload || {};
    if (op.type === 'sale_create' && Array.isArray(payload.items)) {
      for (const item of payload.items) {
        const productUuid = item.product_uuid || item.uuid;
        if (!productUuid) continue;
        pendingStock[productUuid] = (pendingStock[productUuid] || 0) - Number(item.qty || 0);
      }
    } else if (op.type === 'return_create' && Array.isArray(payload.items)) {
      for (const item of payload.items) {
        const saleItem = saleItems.find((row) => (row.uuid || row.Uuid) === item.sale_item_uuid);
        const productUuid = saleItem && (saleItem.product_uuid || saleItem.ProductUuid);
        if (!productUuid) continue;
        pendingStock[productUuid] = (pendingStock[productUuid] || 0) + Number(item.qty || 0);
      }
    }
  }

  const byName = (a, b) => String(a.name || a.Name || '').localeCompare(String(b.name || b.Name || ''), 'ar');
  state.products = products.filter((row) => !isDeleted(row)).sort(byName);
  state.categories = categories.filter((row) => !isDeleted(row));
  state.customers = customers.filter((row) => !isDeleted(row)).sort((a, b) =>
    String(a.full_name || a.FullName || '').localeCompare(String(b.full_name || b.FullName || ''), 'ar'));
  state.customerPayments = payments.filter((row) => !isDeleted(row));
  state.suppliers = suppliers.filter((row) => !isDeleted(row));
  state.sales = sales.filter((row) => !isDeleted(row)).sort((a, b) =>
    String(b.created_at || b.CreatedAt || '').localeCompare(String(a.created_at || a.CreatedAt || '')));
  state.saleItems = saleItems.filter((row) => !isDeleted(row));
  state.returns = returns.filter((row) => !isDeleted(row));
  state.pendingStock = pendingStock;
  state.queueCount = queue.length;
  state.cursor = cursor || state.cursor;
  state.lastSync = lastSync || state.lastSync;
  state.ready = true;
  notify();
  return state;
}

/* ---------- push queue ---------- */

export async function enqueueOp(type, payload, opId = null) {
  const op = {
    op_id: opId || uuid(),
    type,
    created_at: nowStamp(),
    payload,
  };
  await db.enqueueOp(op);
  state.queueCount += 1;
  notify();
  return op;
}

export function pushQueue() {
  if (pushPromise) return pushPromise;
  pushPromise = doPushQueue().finally(() => {
    pushPromise = null;
  });
  return pushPromise;
}

async function doPushQueue() {
  const counters = { applied: 0, duplicate: 0, error: 0, remaining: 0 };
  for (let round = 0; round < 10; round += 1) {
    const queue = await db.getQueue();
    counters.remaining = queue.length;
    if (!queue.length) break;
    const sanitized = await discardCorruptOps(queue);
    if (!sanitized.length) continue;

    const ops = sanitized.slice(0, 50).map((op) => ({
      op_id: op.op_id,
      type: op.type,
      created_at: op.created_at,
      payload: op.payload,
    }));

    let payload;
    try {
      payload = await api.push(ops);
    } catch (err) {
      if (err instanceof ApiError && err.network) {
        console.warn('Push postponed — server unreachable:', err.message);
        state.online = false;
        notify();
        counters.remaining = queue.length;
        return counters;
      }
      console.error('Push batch failed:', err);
      if (!(err instanceof AuthError)) toast('تعذر إرسال العمليات المعلّقة: ' + err.message, 'error');
      counters.remaining = queue.length;
      throw err;
    }

    const results = Array.isArray(payload.results) ? payload.results : [];
    const byId = new Map(results.map((result) => [result.op_id, result]));
    const failed = [];
    for (const op of ops) {
      const result = byId.get(op.op_id);
      if (!result) {
        failed.push({ op, message: 'لا توجد نتيجة من الخادم' });
        counters.error += 1;
        await db.removeOp(op.op_id);
        continue;
      }
      if (result.status === 'applied' || result.status === 'duplicate') {
        await db.removeOp(op.op_id);
        counters[result.status] += 1;
      } else {
        const message = result.message || 'رفض الخادم العملية';
        failed.push({ op, message });
        counters.error += 1;
        await db.removeOp(op.op_id);
        toast('رفض الخادم عملية ' + op.type + ': ' + message, 'error', 6000);
        console.error('Server rejected op:', op.type, message, op.payload);
      }
    }
    if (failed.length) await rememberFailures(failed);
    if (queue.length <= ops.length) break;
  }

  await rebuildStateFromDB();
  const remaining = await db.getQueue();
  counters.remaining = remaining.length;
  state.queueCount = remaining.length;
  notify();

  if (counters.applied || counters.duplicate || counters.error) {
    try {
      await fetchDelta();
      await rebuildStateFromDB();
    } catch (err) {
      console.warn('Delta after push failed:', err);
    }
  }
  return counters;
}

async function rememberFailures(failed) {
  try {
    const previous = await db.getMeta('failed_ops', []);
    const list = Array.isArray(previous) ? previous : [];
    const merged = list.concat(failed.map((entry) => ({
      op_id: entry.op.op_id,
      type: entry.op.type,
      message: entry.message,
      at: nowStamp(),
    }))).slice(-20);
    await db.setMeta('failed_ops', merged);
  } catch (err) {
    console.error('Could not persist failed ops:', err);
  }
}

/* ---------- orchestration + SSE ---------- */

export async function syncNow(options = {}) {
  const quiet = Boolean(options.quiet);
  if (!getToken() || !getServerUrl()) return { pushed: null, changed: 0 };
  if (syncPromise) return syncPromise;
  syncPromise = (async () => {
    const pushed = await pushQueue();
    const changed = await fetchDelta();
    await rebuildStateFromDB();
    state.online = true;
    notify();
    if (!quiet) {
      toast(changed ? 'تمت المزامنة — ' + changed + ' تحديث' : 'كل شيء محدّث', 'success', 2400);
    }
    return { pushed, changed };
  })()
    .catch((err) => {
      console.error('Sync failed:', err);
      if (err instanceof ApiError && err.network) {
        state.online = false;
        notify();
        if (!quiet) toast('أنت غير متصل بالخادم — ستُرسل العمليات لاحقاً', 'warn', 4200);
      } else if (!(err instanceof AuthError) && !quiet) {
        toast(err.message || 'تعذرت المزامنة', 'error');
      }
      throw err;
    })
    .finally(() => {
      syncPromise = null;
    });
  return syncPromise;
}

function scheduleSync() {
  clearTimeout(scheduleTimer);
  scheduleTimer = setTimeout(() => {
    syncNow({ quiet: true }).catch((err) => console.warn('SSE-triggered sync failed:', err));
  }, 700);
}

export function listenEvents() {
  stopEvents();
  sseStopped = false;
  sseBackoff = 1000;
  void connectEvents();
}

export function stopEvents() {
  sseStopped = true;
  clearTimeout(sseTimer);
  clearTimeout(scheduleTimer);
  sseTimer = null;
  scheduleTimer = null;
  if (sseController) {
    try {
      sseController.abort();
    } catch (err) {
      console.warn('SSE abort warning:', err);
    }
    sseController = null;
  }
}

async function connectEvents() {
  if (sseStopped) return;
  const server = getServerUrl();
  const token = getToken();
  if (!server || !token) return;
  sseController = new AbortController();
  try {
    const response = await fetch(server + '/api/events', {
      method: 'GET',
      headers: { Authorization: 'Bearer ' + token, Accept: 'text/event-stream' },
      signal: sseController.signal,
      cache: 'no-store',
    });
    if (response.status === 401) {
      clearSession();
      window.dispatchEvent(new CustomEvent('auth:lost', { detail: { status: 401 } }));
      stopEvents();
      return;
    }
    if (!response.ok || !response.body) {
      throw new ApiError('فشل الاتصال بالبث (رمز ' + response.status + ')', { status: response.status });
    }
    sseBackoff = 1000;
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      let newline = buffer.indexOf('\n');
      while (newline >= 0) {
        const line = buffer.slice(0, newline).replace(/\r$/, '');
        buffer = buffer.slice(newline + 1);
        if (line.startsWith('data:')) {
          const data = line.slice(5).trim();
          if (data) {
            try {
              const event = JSON.parse(data);
              if (event && event.type === 'data_changed') scheduleSync();
            } catch (err) {
              console.error('Invalid SSE payload:', data, err);
            }
          }
        }
        newline = buffer.indexOf('\n');
      }
    }
    throw new Error('SSE stream ended');
  } catch (err) {
    if (sseStopped || (err && err.name === 'AbortError')) return;
    console.warn('SSE disconnected:', err.message || err);
    const delay = Math.min(sseBackoff, 30000);
    sseBackoff = Math.min(sseBackoff * 2, 30000);
    clearTimeout(sseTimer);
    sseTimer = setTimeout(() => {
      void connectEvents();
    }, delay);
  }
}

export function startSyncLoops() {
  listenEvents();
  syncNow({ quiet: true }).catch((err) => console.warn('Initial sync failed:', err));
}

export function forgetSessionLocal() {
  stopEvents();
  clearSession();
}
