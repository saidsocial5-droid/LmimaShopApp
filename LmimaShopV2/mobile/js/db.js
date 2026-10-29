/* LmimaShop V2 mobile — IndexedDB wrapper.
 * All reads/writes are async and every failure is surfaced to the caller.
 */

const DB_NAME = 'lmima_v2';
const DB_VERSION = 1;

export const ALL_STORES = [
  'products',
  'categories',
  'customers',
  'customer_payments',
  'suppliers',
  'sales_cache',
  'sale_items',
  'returns',
  'offline_queue',
  'meta',
];

const KEY_PATHS = {
  meta: 'key',
  offline_queue: 'op_id',
};

let dbPromise = null;

export function init() {
  if (dbPromise) return dbPromise;
  dbPromise = new Promise((resolve, reject) => {
    if (!('indexedDB' in window)) {
      reject(new Error('المتصفح لا يدعم IndexedDB'));
      return;
    }
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      for (const name of ALL_STORES) {
        if (!db.objectStoreNames.contains(name)) {
          db.createObjectStore(name, { keyPath: KEY_PATHS[name] || 'uuid' });
        }
      }
    };
    request.onsuccess = () => {
      const db = request.result;
      db.onversionchange = () => db.close();
      resolve(db);
    };
    request.onerror = () => {
      console.error('IndexedDB open failed:', request.error);
      reject(request.error || new Error('تعذر فتح قاعدة البيانات المحلية'));
    };
    request.onblocked = () => console.warn('IndexedDB upgrade blocked by another tab');
  });
  return dbPromise;
}

function withStore(store, mode, action) {
  return init().then((db) => new Promise((resolve, reject) => {
    let transaction;
    try {
      transaction = db.transaction(store, mode);
    } catch (err) {
      console.error('IndexedDB transaction failed:', store, err);
      reject(err);
      return;
    }
    const objectStore = transaction.objectStore(store);
    let request;
    try {
      request = action(objectStore);
    } catch (err) {
      console.error('IndexedDB action failed:', store, err);
      reject(err);
      return;
    }
    transaction.oncomplete = () => resolve(request ? request.result : undefined);
    transaction.onerror = () => {
      console.error('IndexedDB transaction error:', store, transaction.error);
      reject(transaction.error || new Error('خطأ في قاعدة البيانات المحلية'));
    };
    transaction.onabort = () => {
      console.error('IndexedDB transaction aborted:', store, transaction.error);
      reject(transaction.error || new Error('تم إلغاء العملية المحلية'));
    };
  }));
}

export function get(store, key) {
  return withStore(store, 'readonly', (os) => os.get(key));
}

export function put(store, value) {
  return withStore(store, 'readwrite', (os) => os.put(value));
}

export function del(store, key) {
  return withStore(store, 'readwrite', (os) => os.delete(key));
}

export function getAll(store) {
  return withStore(store, 'readonly', (os) => os.getAll());
}

export function count(store) {
  return withStore(store, 'readonly', (os) => os.count());
}

export function putMany(store, rows) {
  if (!Array.isArray(rows) || rows.length === 0) return Promise.resolve(0);
  return init().then((db) => new Promise((resolve, reject) => {
    let transaction;
    try {
      transaction = db.transaction(store, 'readwrite');
    } catch (err) {
      console.error('IndexedDB bulk transaction failed:', store, err);
      reject(err);
      return;
    }
    const os = transaction.objectStore(store);
    try {
      for (const row of rows) os.put(row);
    } catch (err) {
      console.error('IndexedDB bulk put failed:', store, err);
      reject(err);
      return;
    }
    transaction.oncomplete = () => resolve(rows.length);
    transaction.onerror = () => {
      console.error('IndexedDB bulk transaction error:', store, transaction.error);
      reject(transaction.error || new Error('خطأ في حفظ البيانات المحلية'));
    };
    transaction.onabort = () => reject(transaction.error || new Error('تم إلغاء حفظ البيانات'));
  }));
}

export function clearStore(store) {
  return withStore(store, 'readwrite', (os) => os.clear());
}

export async function getMeta(key, fallback = null) {
  const row = await get('meta', key);
  return row && row.value !== undefined ? row.value : fallback;
}

export function setMeta(key, value) {
  return put('meta', { key, value, updated_at: new Date().toISOString() });
}

export function enqueueOp(op) {
  return put('offline_queue', op);
}

export async function getQueue() {
  const rows = await getAll('offline_queue');
  return rows.sort((a, b) => String(a.created_at || '').localeCompare(String(b.created_at || '')));
}

export function removeOp(opId) {
  return del('offline_queue', opId);
}

export async function wipeData() {
  const db = await init();
  return new Promise((resolve, reject) => {
    const transaction = db.transaction(ALL_STORES, 'readwrite');
    for (const name of ALL_STORES) transaction.objectStore(name).clear();
    transaction.oncomplete = () => resolve(true);
    transaction.onerror = () => {
      console.error('IndexedDB wipe failed:', transaction.error);
      reject(transaction.error || new Error('تعذر مسح البيانات المحلية'));
    };
    transaction.onabort = () => reject(transaction.error || new Error('تم إلغاء مسح البيانات'));
  });
}
