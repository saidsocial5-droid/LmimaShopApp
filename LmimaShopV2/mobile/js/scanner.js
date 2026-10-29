/* LmimaShop V2 mobile — camera/file QR scanner built on the vendored Html5Qrcode. */

import { toast, openModal, closeModal, onModalClose } from './ui.js';

let activeScanner = null;

async function stopScanner() {
  const scanner = activeScanner;
  activeScanner = null;
  if (!scanner) return;
  try {
    await scanner.stop();
  } catch (err) {
    console.warn('Scanner stop warning:', err);
  }
  try {
    scanner.clear();
  } catch (err) {
    console.warn('Scanner clear warning:', err);
  }
}

async function startScanner(finish) {
  const viewport = document.getElementById('scan-viewport');
  const fileInput = document.getElementById('scan-file');
  if (!viewport) {
    toast('منطقة الماسح غير موجودة', 'error');
    finish(null);
    return;
  }
  if (!window.Html5Qrcode) {
    toast('مكتبة قراءة QR غير محمّلة', 'error');
    finish(null);
    return;
  }
  try {
    activeScanner = new window.Html5Qrcode('scan-viewport', { verbose: false });
  } catch (err) {
    console.error('Scanner init failed:', err);
    toast('تعذر تشغيل الماسح', 'error');
    finish(null);
    return;
  }
  const scanner = activeScanner;
  try {
    await scanner.start(
      { facingMode: 'environment' },
      { fps: 10, qrbox: { width: 230, height: 230 } },
      (decodedText) => finish(decodedText),
      () => {}
    );
  } catch (err) {
    console.error('Camera start failed:', err);
    if (activeScanner) {
      toast('تعذر تشغيل الكاميرا. يمكنك اختيار صورة للرمز أو إدخاله يدوياً.', 'error', 4800);
    }
  }
  if (fileInput) {
    fileInput.onchange = async () => {
      const file = fileInput.files && fileInput.files[0];
      if (!file) return;
      try {
        const decoded = await scanner.scanFile(file, false);
        finish(decoded);
      } catch (err) {
        console.error('QR file scan failed:', err);
        toast('تعذر قراءة الرمز من الصورة', 'error');
      } finally {
        fileInput.value = '';
      }
    };
  }
}

/** Open the scanner modal and resolve with decoded text (or null when cancelled). */
export function scanCode() {
  return new Promise((resolve) => {
    let finished = false;
    let cleanup = null;
    const finish = (value) => {
      if (finished) return;
      finished = true;
      stopScanner().finally(() => {
        closeModal('modal-scan');
        resolve(value === undefined ? null : value);
      });
    };
    cleanup = () => {
      if (!finished) {
        finished = true;
        resolve(null);
      }
      void stopScanner();
    };
    onModalClose('modal-scan', () => {
      if (cleanup) cleanup();
      cleanup = null;
    });
    openModal('modal-scan');
    startScanner(finish).catch((err) => {
      console.error('Scanner unexpected failure:', err);
      finish(null);
    });
  });
}
