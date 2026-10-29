/* LmimaShop V2 mobile — configuration and local session storage. */

export const VERSION = '2.0.0';
export const DEFAULT_PORT = '8090';

const SERVER_KEY = 'ls2_server';
const TOKEN_KEY = 'ls2_token';
const DEVICE_KEY = 'ls2_device';
const THEME_KEY = 'ls2_theme';

let tokenMemory = null;

function readLocal(key) {
  try {
    return localStorage.getItem(key);
  } catch (err) {
    console.error('localStorage read failed:', key, err);
    return null;
  }
}

function writeLocal(key, value) {
  try {
    localStorage.setItem(key, value);
    return true;
  } catch (err) {
    console.error('localStorage write failed:', key, err);
    return false;
  }
}

function removeLocal(key) {
  try {
    localStorage.removeItem(key);
  } catch (err) {
    console.error('localStorage remove failed:', key, err);
  }
}

/** Accept "192.168.1.5", "192.168.1.5:8090" or a full URL and return a clean origin. */
export function normalizeServerUrl(raw) {
  let value = String(raw || '').trim();
  if (!value) return '';
  if (!/^https?:\/\//i.test(value)) value = 'http://' + value;
  let parsed;
  try {
    parsed = new URL(value);
  } catch (err) {
    console.error('Invalid server URL:', raw, err);
    return '';
  }
  if (!parsed.hostname) return '';
  if (!parsed.port) parsed.port = DEFAULT_PORT;
  return parsed.origin;
}

export function getServerUrl() {
  return readLocal(SERVER_KEY) || null;
}

export function setServerUrl(raw) {
  const normalized = normalizeServerUrl(raw);
  if (!normalized) return null;
  writeLocal(SERVER_KEY, normalized);
  return normalized;
}

export function clearServerUrl() {
  removeLocal(SERVER_KEY);
}

/** Convenience suggestion for first run: saved server, else current host on port 8090. */
export function getSuggestedServer() {
  const saved = getServerUrl();
  if (saved) return saved;
  try {
    if (location.protocol === 'http:' || location.protocol === 'https:') {
      if (location.hostname && location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') {
        return normalizeServerUrl(location.origin);
      }
      if (location.port === DEFAULT_PORT) return normalizeServerUrl(location.origin);
      return normalizeServerUrl(location.hostname + ':' + DEFAULT_PORT);
    }
  } catch (err) {
    console.error('Could not suggest server:', err);
  }
  return '';
}

export function getToken() {
  if (tokenMemory) return tokenMemory;
  try {
    tokenMemory = sessionStorage.getItem(TOKEN_KEY) || null;
  } catch (err) {
    console.error('sessionStorage read failed:', err);
    tokenMemory = null;
  }
  return tokenMemory;
}

export function setToken(token, deviceId) {
  tokenMemory = token || null;
  try {
    if (token) sessionStorage.setItem(TOKEN_KEY, token);
    else sessionStorage.removeItem(TOKEN_KEY);
    if (deviceId !== undefined && deviceId !== null) {
      sessionStorage.setItem(DEVICE_KEY, String(deviceId));
    }
  } catch (err) {
    console.error('sessionStorage write failed:', err);
  }
}

export function getDeviceId() {
  try {
    return sessionStorage.getItem(DEVICE_KEY) || null;
  } catch (err) {
    console.error('sessionStorage device read failed:', err);
    return null;
  }
}

/** Clear token + device only (server URL is kept as a suggestion). */
export function clearSession() {
  tokenMemory = null;
  try {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(DEVICE_KEY);
  } catch (err) {
    console.error('sessionStorage clear failed:', err);
  }
}

export function getTheme() {
  return readLocal(THEME_KEY) || '';
}

export function setTheme(theme) {
  if (theme === 'dark' || theme === 'light') writeLocal(THEME_KEY, theme);
  else removeLocal(THEME_KEY);
}
