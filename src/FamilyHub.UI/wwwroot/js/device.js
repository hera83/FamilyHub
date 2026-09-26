// Family Hub – device detection, per-screen settings and small browser helpers.

const APPEARANCE_KEY = 'familyhub.appearance';   // read by the boot script in App.razor before first paint

let trackingInstalled = false;
let lastPointerType = '';
const idle = { ref: null, ms: 0, timer: 0 };

export function initialize(settingsKey) {
  installInteractionTracking();
  let settings = null;
  try {
    settings = localStorage.getItem(settingsKey);
  } catch {
    // Private mode or blocked storage – run with defaults.
  }
  return { profile: detectProfile(), settings };
}

export function detectProfile() {
  const matches = query => window.matchMedia(query).matches;
  const ua = navigator.userAgent || '';
  const isIPad = navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1;
  const isMobileOs = navigator.userAgentData?.mobile === true
    || /Android|iPhone|iPad|iPod/i.test(ua)
    || isIPad;

  return {
    maxTouchPoints: navigator.maxTouchPoints || 0,
    primaryPointerCoarse: matches('(pointer: coarse)'),
    anyPointerCoarse: matches('(any-pointer: coarse)'),
    anyPointerFine: matches('(any-pointer: fine)'),
    isMobileOs,
    platform: navigator.userAgentData?.platform || navigator.platform || '',
    screenWidth: window.screen?.width || 0,
    screenHeight: window.screen?.height || 0,
    pixelRatio: window.devicePixelRatio || 1,
  };
}

export function saveSettings(key, json) {
  try {
    localStorage.setItem(key, json);
    return true;
  } catch {
    return false;
  }
}

export function applyAppearance(theme, scalePercent) {
  const html = document.documentElement;
  const scale = String(scalePercent / 100);
  html.dataset.theme = theme;
  html.style.setProperty('--hub-scale', scale);
  try {
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify({ theme, scale }));
  } catch {
    // Not critical – only used to avoid a flash on the next page load.
  }
}

// Calls dotnetRef.OnIdle() after the given minutes without touch, mouse or keys. 0 = off.
export function setIdleTimeout(dotnetRef, minutes) {
  idle.ref = dotnetRef;
  idle.ms = Math.max(0, minutes) * 60_000;
  resetIdle();
}

export function scrollToTop(element) {
  element?.scrollTo?.({ top: 0, left: 0, behavior: 'instant' });
}

export function blurActiveElement() {
  const el = document.activeElement;
  if (el && el !== document.body) el.blur?.();
}

export function reload() {
  location.reload();
}

function installInteractionTracking() {
  if (trackingInstalled) return;
  trackingInstalled = true;

  const html = document.documentElement;

  // Hide the mouse cursor while the screen is used by touch (kiosk); it returns when a mouse moves.
  if (!window.matchMedia('(any-pointer: fine)').matches) {
    html.classList.add('hub-touch-input');
  }

  document.addEventListener('pointerdown', e => {
    lastPointerType = e.pointerType || 'mouse';
    html.classList.toggle('hub-touch-input', lastPointerType === 'touch' || lastPointerType === 'pen');
    resetIdle();
  }, { capture: true, passive: true });

  document.addEventListener('pointermove', e => {
    if (e.pointerType === 'mouse' && Math.abs(e.movementX) + Math.abs(e.movementY) > 2) {
      html.classList.remove('hub-touch-input');
    }
  }, { passive: true });

  document.addEventListener('keydown', resetIdle, { capture: true, passive: true });
  document.addEventListener('wheel', resetIdle, { capture: true, passive: true });

  // A long press opens the browser's context menu – not wanted on a touch screen.
  document.addEventListener('contextmenu', e => {
    if (lastPointerType !== 'mouse' && !e.target.closest?.('input, textarea')) {
      e.preventDefault();
    }
  });
}

function resetIdle() {
  clearTimeout(idle.timer);
  if (idle.ms > 0 && idle.ref) {
    idle.timer = setTimeout(() => idle.ref?.invokeMethodAsync('OnIdle').catch(() => { }), idle.ms);
  }
}
