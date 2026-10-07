// Family Hub – device detection, per-screen settings and small browser helpers.

const APPEARANCE_KEY = 'familyhub.appearance';   // read by the boot script in App.razor before first paint

let trackingInstalled = false;
let lastPointerType = '';
const idle = { ref: null, ms: 0, timer: 0 };
const saver = { ref: null, ms: 0, timer: 0, asleep: false, since: 0 };
const WAKE_GRACE_MS = 800;   // a mouse still moving right after "Prøv" must not wake the screen at once

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

// Screen saver: calls dotnetRef.OnSleep() after the given minutes without activity (0 = only via showScreenSaver),
// and dotnetRef.OnWake() on the next touch, key or mouse movement. null = off.
// When it starts by itself and "Tilbage til forsiden" is on, the home screen is waiting underneath on waking.
export function setScreenSaver(dotnetRef, minutes) {
  saver.ref = dotnetRef;
  saver.ms = Math.max(0, minutes) * 60_000;
  if (!dotnetRef) saver.asleep = false;
  if (!saver.asleep) armScreenSaver();
}

export function showScreenSaver() {
  clearTimeout(saver.timer);
  sleep();
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
    onActivity();
  }, { capture: true, passive: true });

  // The screen saver wakes when the finger is lifted, not when it lands: it is still on top until then,
  // so the tap (and its click) can never reach a button underneath.
  document.addEventListener('pointerup', () => {
    if (saver.asleep) wake();
  }, { capture: true, passive: true });

  document.addEventListener('pointermove', e => {
    if (e.pointerType !== 'mouse' || Math.abs(e.movementX) + Math.abs(e.movementY) <= 2) return;
    html.classList.remove('hub-touch-input');
    if (!saver.asleep) {
      onActivity();
    } else if (performance.now() - saver.since > WAKE_GRACE_MS) {
      wake();
    }
  }, { passive: true });

  // On window, so it runs before every other key handler (fields, the on-screen keyboard).
  window.addEventListener('keydown', e => {
    if (saver.asleep) {
      // The key only wakes the screen – it must not type into a field or press a button underneath.
      e.preventDefault();
      e.stopImmediatePropagation();
      wake();
    }
    onActivity();
  }, { capture: true });

  document.addEventListener('wheel', () => {
    if (saver.asleep) wake();
    onActivity();
  }, { capture: true, passive: true });

  // A long press opens the browser's context menu – not wanted on a touch screen.
  document.addEventListener('contextmenu', e => {
    if (lastPointerType !== 'mouse' && !e.target.closest?.('input, textarea')) {
      e.preventDefault();
    }
  });
}

function onActivity() {
  resetIdle();
  if (!saver.asleep) armScreenSaver();
}

function resetIdle() {
  clearTimeout(idle.timer);
  if (idle.ms > 0 && idle.ref) {
    idle.timer = setTimeout(returnHome, idle.ms);
  }
}

function returnHome() {
  clearTimeout(idle.timer);
  idle.ref?.invokeMethodAsync('OnIdle').catch(() => { });
}

function armScreenSaver() {
  clearTimeout(saver.timer);
  if (saver.ms > 0 && saver.ref) {
    saver.timer = setTimeout(() => sleep(true), saver.ms);
  }
}

// automatic = the idle time ran out (not the "Prøv" button).
function sleep(automatic = false) {
  if (!saver.ref || saver.asleep) return;
  saver.asleep = true;
  saver.since = performance.now();
  // If the server can't draw it (connection lost), don't keep swallowing keys.
  saver.ref.invokeMethodAsync('OnSleep').catch(() => { saver.asleep = false; });

  // The screen saver may start before "Tilbage til forsiden" runs out – the waking touch must not find the old menu.
  if (automatic && idle.ms > 0) returnHome();
}

function wake() {
  saver.asleep = false;
  saver.ref?.invokeMethodAsync('OnWake').catch(() => { });
  armScreenSaver();
}
