// Phase 4: IndexedDB wrapper and Migration
const DB_NAME = 'LmimaShopDB';
const DB_VERSION = 1;

const IDB = {
  db: null,

  async init() {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, DB_VERSION);
      
      request.onupgradeneeded = (e) => {
        const db = e.target.result;
        if (!db.objectStoreNames.contains('products')) {
          db.createObjectStore('products', { keyPath: 'uuid' });
        }
        if (!db.objectStoreNames.contains('customers')) {
          db.createObjectStore('customers', { keyPath: 'uuid' });
        }
        if (!db.objectStoreNames.contains('sales')) {
          db.createObjectStore('sales', { keyPath: 'uuid' });
        }
        if (!db.objectStoreNames.contains('offline_queue')) {
          db.createObjectStore('offline_queue', { keyPath: 'op_id' });
        }
        if (!db.objectStoreNames.contains('meta')) {
          db.createObjectStore('meta', { keyPath: 'key' });
        }
      };

      request.onsuccess = (e) => {
        this.db = e.target.result;
        resolve(this.db);
      };

      request.onerror = (e) => reject(e.target.error);
    });
  },

  async getMeta(key) {
    const data = await this.get('meta', key);
    return data ? data.value : null;
  },

  async setMeta(key, value) {
    return this.put('meta', { key, value });
  },

  async get(storeName, key) {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeName, 'readonly');
      const req = tx.objectStore(storeName).get(key);
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  },

  async getAll(storeName) {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeName, 'readonly');
      const req = tx.objectStore(storeName).getAll();
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  },

  async put(storeName, item) {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeName, 'readwrite');
      const req = tx.objectStore(storeName).put(item);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
  },

  async putBatch(storeName, items) {
    if (!items || items.length === 0) return;
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeName, 'readwrite');
      const store = tx.objectStore(storeName);
      items.forEach(i => store.put(i));
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
  },

  // يستبدل محتوى المخزن كاملاً في معاملة واحدة (لا يبقى المخزن فارغاً إن فشلت الكتابة)
  async replaceAll(storeName, items) {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeName, 'readwrite');
      const store = tx.objectStore(storeName);
      store.clear();
      (items || []).forEach(i => store.put(i));
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  },

  async delete(storeName, key) {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeName, 'readwrite');
      const req = tx.objectStore(storeName).delete(key);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
  },

  async clear(storeName) {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction(storeName, 'readwrite');
      const req = tx.objectStore(storeName).clear();
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
    });
  }
};

function generateUUID() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
    var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
    return v.toString(16);
  });
}

// 1. One-time Migration from localStorage to IndexedDB
async function migrateFromV1ToV2() {
  const isMigrated = await IDB.getMeta('v1_migrated');
  if (isMigrated) return; // Already done

  console.log('Starting migration from LocalStorage to IndexedDB...');
  
  // A. Migrate Data (Products/Customers) just as a baseline if they exist
  const savedV1 = localStorage.getItem('lmima_sync_data');
  if (savedV1) {
    try {
      const parsed = JSON.parse(savedV1);
      if (parsed.Products) {
        // Ensure UUIDs for V1 products
        parsed.Products.forEach(p => { if (!p.uuid) p.uuid = generateUUID(); });
        await IDB.putBatch('products', parsed.Products);
      }
      if (parsed.Customers) {
        parsed.Customers.forEach(c => { if (!c.uuid) c.uuid = generateUUID(); });
        await IDB.putBatch('customers', parsed.Customers);
      }
    } catch (e) {
      console.error('Failed to migrate sync_data', e);
    }
  }

  // B. Migrate Offline Queue WITHOUT LOSING ANY PENDING SALES
  const oldQueue = localStorage.getItem('lmima_offline_queue');
  if (oldQueue) {
    try {
      const parsedQueue = JSON.parse(oldQueue);
      const newOps = [];
      
      parsedQueue.forEach(oldOp => {
        if (oldOp.type === 'checkout' && oldOp.data) {
          // البيع القديم يتحول إلى عملية بيع موحدة واحدة (فاتورة + مخزون + دين الزبون) يعالجها الحاسوب دون تكرار
          const d = oldOp.data;
          const items = (d.items || []).map(i => ({
            productId: i.productId || i.id || 0,
            productName: i.productName || i.name || '',
            quantity: i.quantity || i.qty || 1,
            unitPrice: i.unitPrice || i.price || 0,
            total: i.total || 0
          }));
          const total = d.totalAmount || d.total || items.reduce((s, i) => s + (i.total || 0), 0);
          const opId = generateUUID();
          newOps.push({
            op_id: opId,
            type: 'pos_checkout',
            status: 'pending',
            retry_count: 0,
            client_created_at: new Date(oldOp.timestamp || Date.now()).toISOString(),
            payload: {
              opId: opId,
              saleDate: new Date(oldOp.timestamp || Date.now()).toISOString(),
              cashierName: d.cashierName || d.cashier || 'تطبيق الهاتف',
              customerName: d.customerName || d.customer || 'زبون نقدي عابر',
              totalAmount: total,
              paidAmount: d.paidAmount != null ? d.paidAmount : total,
              debtAmount: d.debtAmount || 0,
              items: items
            }
          });
        }
      });
      
      if (newOps.length > 0) {
        await IDB.putBatch('offline_queue', newOps);
        console.log(`Migrated ${newOps.length} operations to IndexedDB.`);
      }
    } catch(e) {
      console.error('Failed to migrate offline queue', e);
    }
  }

  await IDB.setMeta('v1_migrated', true);
  
  // Cleanup LocalStorage
  localStorage.removeItem('lmima_sync_data');
  localStorage.removeItem('lmima_offline_queue');
  console.log('Migration complete. Legacy data removed.');
}
