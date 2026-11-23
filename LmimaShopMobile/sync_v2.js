// Phase 4: Delta Sync and Push Engine (V2) - Enhanced with Auto-Pairing & Reconnect

async function pairDeviceWithServer(host, pairingCode) {
  if (!host) return false;
  let cleanHost = host.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');
  const url = 'http://' + cleanHost + '/api/pair';

  // رمز الاقتران مؤقت ويظهر في نافذة "اقتران الهاتف" على الحاسوب؛ لا يوجد رمز افتراضي
  const code = (pairingCode || '').trim();
  if (!code) return false;
  const existingDeviceId = localStorage.getItem('lmima_device_id') || ('mobile_' + Math.random().toString(36).substring(2, 10));

  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 6000);

    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
      body: JSON.stringify({
        pairing_code: code,
        device_name: 'هاتف ذكي (' + (navigator.userAgent.includes('Android') ? 'Android' : 'Mobile') + ')',
        device_id: existingDeviceId
      }),
      signal: controller.signal
    });
    clearTimeout(timeoutId);

    if (res.ok) {
      const data = await res.json();
      if (data && data.token) {
        localStorage.setItem('lmima_device_token', data.token);
        if (data.device_id) localStorage.setItem('lmima_device_id', data.device_id);
        console.log('[Sync] Device paired successfully with server. Token saved.');
        if (typeof updateConnectionDiagnostics === 'function') {
          updateConnectionDiagnostics();
        }
        return true;
      }
    } else {
      let msg = 'رمز الاقتران غير صحيح أو انتهت صلاحيته';
      try { const j = await res.json(); if (j && j.message) msg = j.message; } catch (e) {}
      if (typeof showToast === 'function') showToast('❌ ' + msg);
    }
  } catch (err) {
    console.warn('[Sync] Pairing failed:', err);
  }
  return false;
}

async function fetchDeltaSync(host, forceRetryWithPairing = true) {
  if (!host) return false;
  let cleanHost = host.trim().replace(/^https?:\/\//, '').replace(/\/$/, '');
  
  let cursor = await IDB.getMeta('sync_cursor') || '';
  let token = localStorage.getItem('lmima_device_token') || '';

  let url = 'http://' + cleanHost + '/api/sync?since=' + encodeURIComponent(cursor);

  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 8000);
    
    const headers = { 'Accept': 'application/json' };
    if (token) headers['Authorization'] = 'Bearer ' + token;

    const res = await fetch(url, { signal: controller.signal, headers });
    clearTimeout(timeoutId);

    if (!res.ok) {
      // الهاتف غير مقترن: لا نحاول اقتراناً تلقائياً (الرمز مؤقت ويجب إدخاله يدوياً أو بمسح QR)
      if (res.status === 401) {
        if (typeof handleUnauthorized === 'function') handleUnauthorized();
        if (typeof updateServerStatus === 'function') updateServerStatus(false, 'غير مقترن 🔒 أدخل رمز الاقتران');
        return false;
      }
      // If still not ok, fallback to live full sync
      console.warn("[Sync] Delta sync failed with status " + res.status + ", falling back to fetchLiveSync");
      if (typeof fetchLiveSync === 'function') {
        return await fetchLiveSync(cleanHost, false);
      }
      return false;
    }
    
    const delta = await res.json();
    
    // Check if full payload received (from CloudSyncService or /api/sync)
    if (delta && (delta.StoreInfo || delta.Products)) {
      if (typeof applySyncData === 'function') {
        applySyncData(delta, true);
      }
      // اللقطة الكاملة لا تحتوي المحذوفات، لذلك نستبدل النسخة المحلية بدل الدمج،
      // وإلا تعود المنتجات/الزبائن المحذوفون عند إعادة بناء الحالة من IndexedDB.
      // (المخزنان يُملآن من الخادم فقط؛ عمليات الهاتف غير المرسلة محفوظة في offline_queue)
      const prods = delta.Products || delta.products;
      if (prods && Array.isArray(prods) && prods.length > 0) {
        try { await IDB.replaceAll('products', prods.filter(p => p && p.uuid)); } catch (e) {}
      }
      const custs = delta.Customers || delta.customers;
      if (custs && Array.isArray(custs) && custs.length > 0) {
        try { await IDB.replaceAll('customers', custs.filter(c => c && c.uuid)); } catch (e) {}
      }
      if (typeof updateServerStatus === 'function') {
        updateServerStatus(true, `متصل ومُحدث (${new Date().toLocaleTimeString('fr-FR')})`);
      }
      return true;
    }
    
    // Apply Delta to IndexedDB
    if (delta.products && delta.products.length > 0) {
      await IDB.putBatch('products', delta.products);
    }
    if (delta.customers && delta.customers.length > 0) {
      await IDB.putBatch('customers', delta.customers);
    }
    if (delta.sales && delta.sales.length > 0) {
      await IDB.putBatch('sales', delta.sales);
    }
    
    if (delta.next_cursor) {
      await IDB.setMeta('sync_cursor', delta.next_cursor);
    }
    
    // Rebuild memory AppState for UI speed
    await rebuildAppStateFromIDB();
    if (typeof updateServerStatus === 'function') {
      updateServerStatus(true, `متصل ومُحدث (${new Date().toLocaleTimeString('fr-FR')})`);
    }
    return true;
  } catch (err) {
    console.error('[Sync] Delta sync network error:', err);
    // Fallback to fetchLiveSync on any error
    if (typeof fetchLiveSync === 'function') {
      return await fetchLiveSync(cleanHost, false);
    }
    return false;
  }
}

async function rebuildAppStateFromIDB() {
  try {
    const products = await IDB.getAll('products');
    const customers = await IDB.getAll('customers');
    
    if (!AppState.data) AppState.data = {};
    
    // Filter out soft-deleted
    if (products && products.length > 0) {
      AppState.data.Products = products.filter(p => !p.deleted_at);
    }
    if (customers && customers.length > 0) {
      AppState.data.Customers = customers.filter(c => !c.deleted_at);
    }
    
    // Calculate local stock display impact based on pending operations
    const pendingOps = await IDB.getAll('offline_queue');
    if (pendingOps && pendingOps.length > 0) {
      const stockMovements = pendingOps.filter(o => o.type === 'stock_movement' && o.status === 'pending');
      // المبيعات المعلقة تخصم من المخزون المعروض حتى يؤكدها الحاسوب
      pendingOps.filter(o => o.type === 'pos_checkout' && o.status === 'pending').forEach(op => {
        (op.payload?.items || []).forEach(it => {
          const p = (AppState.data.Products || []).find(pr => pr.Id === it.productId);
          if (p) p.StockQuantity = (p.StockQuantity || 0) - (it.quantity || 0);
        });
      });
      stockMovements.forEach(op => {
        if (AppState.data.Products) {
          const p = AppState.data.Products.find(pr => pr.uuid === op.payload.product_uuid);
          if (p) {
            p.StockQuantity = (p.StockQuantity || 0) + (op.payload.qty_delta || 0);
          }
        }
      });
    }
    
    // Re-render UI if functions exist
    if (typeof renderProductsTab === 'function') renderProductsTab();
    else if (typeof renderProducts === 'function') renderProducts();

    if (typeof renderCustomersTab === 'function') renderCustomersTab();
    else if (typeof renderCustomers === 'function') renderCustomers();

    if (typeof updateRecordsCountBadge === 'function') updateRecordsCountBadge();
  } catch (e) {
    console.error('[Sync] Error rebuilding AppState from IDB:', e);
  }
}

async function pushOfflineQueueV2() {
  if (AppState.isPushing) return false;
  AppState.isPushing = true;
  
  try {
    const queue = await IDB.getAll('offline_queue');
    // فقط العمليات التي يعالجها /api/sync/push؛ عمليات المنتجات والممونين تُعاد عبر flushOfflineQueue
    const pushTypes = (typeof V2_PUSH_TYPES !== 'undefined') ? V2_PUSH_TYPES : ['pos_checkout', 'create_invoice', 'stock_movement', 'upsert_customer', 'add_debt_payment'];
    const pending = queue ? queue.filter(q => q.status === 'pending' && pushTypes.includes(q.type)) : [];
    if (pending.length === 0) return true;
    
    let cleanHost = (AppState.serverIp || '127.0.0.1:8080').trim().replace(/^https?:\/\//, '').replace(/\/$/, '');
    const url = 'http://' + cleanHost + '/api/sync/push';
    
    const token = localStorage.getItem('lmima_device_token') || '';
    const headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' };
    if (token) headers['Authorization'] = 'Bearer ' + token;
    
    const payload = {
       device_id: localStorage.getItem('lmima_device_id') || 'mobile_1',
       operations: pending.map(p => ({
         op_id: p.op_id,
         type: p.type,
         payload: p.payload,
         client_created_at: p.client_created_at
       }))
    };
    
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 10000);
    const res = await fetch(url, {
       method: 'POST',
       headers,
       body: JSON.stringify(payload),
       signal: controller.signal
    });
    clearTimeout(timeoutId);
    
    if (res.status === 401) {
       if (typeof handleUnauthorized === 'function') handleUnauthorized();
       return false;
    }
    if (res.ok) {
       const result = await res.json();
       if (result.results) {
         for (let r of result.results) {
            if (r.status === 'applied' || r.status === 'duplicate') {
               await IDB.delete('offline_queue', r.op_id);
            } else if (r.status === 'rejected_conflict') {
               // محفوظة في نافذة التعارضات على الحاسوب للمراجعة
               await IDB.delete('offline_queue', r.op_id);
            } else if (r.status === 'error') {
               const op = pending.find(p => p.op_id === r.op_id);
               if (op) {
                  op.retry_count = (op.retry_count || 0) + 1;
                  op.last_error = r.message || '';
                  await IDB.put('offline_queue', op);
               }
            }
         }
       }
       if (typeof updateOfflineQueueBadge === 'function') updateOfflineQueueBadge();
       return true;
    }
  } catch (err) {
    console.warn('[Sync] Push failed, will retry later:', err);
  } finally {
    AppState.isPushing = false;
  }
  return false;
}
