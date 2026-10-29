/* LmimaShop V2 mobile — pairing hash/QR parsing. */

import { normalizeServerUrl } from './config.js';

/** Extract a pairing code from plain digits, a QR URL or a "#pair=..." hash. */
export function extractPairCode(text) {
  const raw = String(text || '').trim();
  if (!raw) return null;
  const direct = raw.match(/[#?&]pair=([A-Za-z0-9_-]+)/i);
  if (direct) return direct[1];
  if (/^\d{4,8}$/.test(raw)) return raw;
  try {
    const url = new URL(raw);
    const fromQuery = url.searchParams.get('pair');
    if (fromQuery) return String(fromQuery).trim();
    const fromHash = (url.hash || '').match(/pair=([A-Za-z0-9_-]+)/i);
    if (fromHash) return fromHash[1];
  } catch (err) {
    console.debug('Pair payload is not a URL:', err && err.message);
  }
  return null;
}

/** Extract the server origin from a QR URL (http/https only). */
export function extractServer(text) {
  const raw = String(text || '').trim();
  if (!raw) return null;
  try {
    const url = new URL(raw);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return null;
    return normalizeServerUrl(url.origin);
  } catch (err) {
    console.debug('Cannot extract server from pair payload:', err && err.message);
    return null;
  }
}

function readPairCodeFromLocation() {
  const hash = String(location.hash || '');
  const fromHash = hash.match(/pair=([A-Za-z0-9_-]+)/i);
  if (fromHash) return fromHash[1];
  try {
    return new URLSearchParams(location.search).get('pair');
  } catch (err) {
    console.error('Could not read pairing code from URL:', err);
    return null;
  }
}

function clearPairHash() {
  if (!location.hash) return;
  try {
    history.replaceState(null, '', location.pathname + location.search);
  } catch (err) {
    console.warn('Could not clear pairing hash:', err);
  }
}

/**
 * Read "#pair=CODE" on launch/hashchange, prefill the pairing form and suggest
 * the current origin as the server (QR URLs point at the desktop server).
 */
export function initQrPair(options = {}) {
  const codeInput = options.codeInput || null;
  const serverInput = options.serverInput || null;
  const onCode = typeof options.onCode === 'function' ? options.onCode : null;

  const apply = () => {
    const code = readPairCodeFromLocation();
    if (!code) return;
    if (codeInput) codeInput.value = code;
    if (serverInput) {
      const origin = normalizeServerUrl(location.origin);
      if (origin) serverInput.value = origin;
    }
    clearPairHash();
    if (onCode) onCode(code);
  };

  apply();
  window.addEventListener('hashchange', apply);
}
