/* LmimaShop V2 mobile — typed HTTP client for the embedded desktop server.
 * The Bearer token always travels in the Authorization header, never in a URL.
 */

import { getServerUrl, getToken, setToken } from './config.js';

export class ApiError extends Error {
  constructor(message, details = {}) {
    super(message || 'خطأ غير معروف');
    this.name = 'ApiError';
    this.status = details.status || 0;
    this.body = details.body || null;
    this.network = Boolean(details.network);
  }
}

export class AuthError extends ApiError {
  constructor(message, details = {}) {
    super(message || 'انتهت صلاحية الجهاز. أعد الاقتران بالخادم.', details);
    this.name = 'AuthError';
    this.status = 401;
  }
}

export class NetworkError extends ApiError {
  constructor(message, details = {}) {
    super(message || 'تعذر الاتصال بالخادم', details);
    this.name = 'NetworkError';
    this.network = true;
  }
}

function buildUrl(path, baseUrl) {
  const base = baseUrl || getServerUrl();
  if (!base) throw new ApiError('لم يتم إعداد عنوان الخادم', { status: 0 });
  return String(base).replace(/\/+$/, '') + path;
}

function errorMessageFromBody(body, status) {
  if (body && typeof body === 'object') {
    if (typeof body.message === 'string' && body.message.trim()) return body.message;
    if (typeof body.error === 'string' && body.error.trim()) return body.error;
    if (typeof body.detail === 'string' && body.detail.trim()) return body.detail;
    if (typeof body.title === 'string' && body.title.trim()) return body.title;
  }
  return 'فشل الطلب (رمز ' + status + ')';
}

/**
 * Perform a JSON request with timeout and Bearer auth.
 * @param {string} path absolute API path, e.g. "/api/sync"
 * @param {{method?:string, body?:object|null, auth?:boolean, timeout?:number, baseUrl?:string|null}} options
 */
export async function request(path, options = {}) {
  const method = options.method || 'GET';
  const auth = options.auth !== false;
  const timeout = options.timeout || 15000;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  const headers = { Accept: 'application/json' };
  if (options.body !== undefined && options.body !== null) headers['Content-Type'] = 'application/json';
  const token = getToken();
  if (auth && token) headers.Authorization = 'Bearer ' + token;

  let url;
  try {
    url = buildUrl(path, options.baseUrl);
  } catch (err) {
    clearTimeout(timer);
    throw err;
  }

  let response;
  try {
    response = await fetch(url, {
      method,
      headers,
      body: options.body === undefined || options.body === null ? undefined : JSON.stringify(options.body),
      signal: controller.signal,
      cache: 'no-store',
    });
  } catch (err) {
    if (err && err.name === 'AbortError') {
      throw new NetworkError('انتهت مهلة الاتصال بالخادم', { network: true });
    }
    console.error('Network request failed:', method, url, err);
    throw new NetworkError('تعذر الاتصال بالخادم. تحقق من الشبكة.', { network: true });
  } finally {
    clearTimeout(timer);
  }

  const rawText = await response.text();
  let data = null;
  if (rawText) {
    try {
      data = JSON.parse(rawText);
    } catch (err) {
      console.error('Server returned non-JSON response:', response.status, rawText.slice(0, 200), err);
      data = { raw: rawText };
    }
  }

  if (response.status === 401) {
    if (auth) {
      window.dispatchEvent(new CustomEvent('auth:lost', { detail: { status: 401 } }));
    }
    throw new AuthError('انتهت صلاحية الجهاز. أعد الاقتران بالخادم.', { status: 401, body: data });
  }

  if (!response.ok) {
    throw new ApiError(errorMessageFromBody(data, response.status), {
      status: response.status,
      body: data,
    });
  }

  return data === null ? {} : data;
}

export const api = {
  async pair(code) {
    const payload = await request('/api/pair', {
      method: 'POST',
      body: { code: String(code || '').trim() },
      auth: false,
      timeout: 12000,
    });
    if (!payload || typeof payload.token !== 'string' || !payload.token) {
      throw new ApiError('استجابة اقتران غير صالحة من الخادم', { status: 0, body: payload });
    }
    setToken(payload.token, payload.device_id);
    return payload;
  },

  ping(baseUrl = null) {
    return request('/api/ping', {
      auth: false,
      timeout: 6000,
      baseUrl,
    });
  },

  sync(since = '') {
    const query = since ? '?since=' + encodeURIComponent(since) : '';
    return request('/api/sync' + query, { timeout: 25000 });
  },

  push(ops) {
    return request('/api/sync/push', {
      method: 'POST',
      body: { ops },
      timeout: 25000,
    });
  },

  checkout(payload) {
    return request('/api/checkout', {
      method: 'POST',
      body: payload,
      timeout: 25000,
    });
  },

  createReturn(payload) {
    return request('/api/returns', {
      method: 'POST',
      body: payload,
      timeout: 25000,
    });
  },

  productByBarcode(code) {
    return request('/api/products/by-barcode?code=' + encodeURIComponent(String(code || '').trim()));
  },

  customerStatement(uuid) {
    return request('/api/customers/' + encodeURIComponent(uuid) + '/statement');
  },
};
