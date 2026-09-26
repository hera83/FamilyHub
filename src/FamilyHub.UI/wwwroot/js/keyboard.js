// Family Hub – on-screen keyboard engine.
//
// OnScreenKeyboard.razor renders the keys and decides WHEN the keyboard is visible.
// This module does the typing itself, so a key press never waits for the server:
//   * tracks which text field has focus, and whether it was tapped with a finger
//   * inserts text at the caret and fires 'input' events, so Blazor bindings update
//   * keeps focus in the field while keys are pressed
//   * shift / caps lock, layers, auto-capitalisation and key repeat
//   * steps aside as soon as a physical keyboard is used

const TEXT_INPUT_TYPES = new Set(['text', 'search', 'email', 'url', 'tel', 'password', 'number']);
const SELECTION_TYPES = new Set(['text', 'search', 'url', 'tel', 'password']);
const MODIFIER_KEYS = new Set(['Shift', 'Control', 'Alt', 'AltGraph', 'Meta', 'CapsLock', 'Fn', 'OS', 'Unidentified']);
const RECENT_POINTER_MS = 1500;
const DOUBLE_TAP_MS = 350;
const REPEAT_DELAY_MS = 450;
const REPEAT_INTERVAL_MS = 60;
const RELEASE_DELAY_MS = 150;
const GUARD_INTERVAL_MS = 300;

const state = {
  dotnet: null,
  root: null,
  enabled: false,
  requireTouch: true,
  target: null,
  lastValue: '',           // the field's text as the keyboard last saw it
  shift: 'off',            // off | once | lock
  layer: 'letters',
  lastShiftTap: 0,
  lastPointerType: '',
  lastPointerTime: 0,
  pointerIsDown: false,
  physicalKeyboard: false,  // a real key was pressed – stay away until the next touch
  releaseTimer: 0,
  guardTimer: 0,
  repeatDelay: 0,
  repeatTimer: 0,
  resizeObserver: null,
};

export function init(dotnet, root, config) {
  dispose();
  state.dotnet = dotnet;
  state.root = root;
  applyConfig(config);

  document.addEventListener('pointerdown', onDocumentPointerDown, true);
  document.addEventListener('pointerup', onDocumentPointerUp, true);
  document.addEventListener('pointercancel', onDocumentPointerUp, true);
  document.addEventListener('focusin', onFocusIn, true);
  document.addEventListener('focusout', onFocusOut, true);
  document.addEventListener('keydown', onPhysicalKeyDown, true);

  root.addEventListener('pointerdown', onKeyPointerDown);
  root.addEventListener('mousedown', preventDefault);   // keeps focus (and caret) in the text field
  root.addEventListener('contextmenu', preventDefault);
  root.addEventListener('focusin', onKeyboardFocusIn);

  state.resizeObserver = new ResizeObserver(onKeyboardResize);
  state.resizeObserver.observe(root);

  const active = document.activeElement;
  if (state.enabled && !state.requireTouch && isEditable(active)) {
    serve(active);
  }
}

export function configure(config) {
  applyConfig(config);
  if (!state.enabled && state.target) {
    release(true);
  }
}

export function dispose() {
  document.removeEventListener('pointerdown', onDocumentPointerDown, true);
  document.removeEventListener('pointerup', onDocumentPointerUp, true);
  document.removeEventListener('pointercancel', onDocumentPointerUp, true);
  document.removeEventListener('focusin', onFocusIn, true);
  document.removeEventListener('focusout', onFocusOut, true);
  document.removeEventListener('keydown', onPhysicalKeyDown, true);

  if (state.root) {
    state.root.removeEventListener('pointerdown', onKeyPointerDown);
    state.root.removeEventListener('mousedown', preventDefault);
    state.root.removeEventListener('contextmenu', preventDefault);
    state.root.removeEventListener('focusin', onKeyboardFocusIn);
  }

  state.resizeObserver?.disconnect();
  state.resizeObserver = null;
  release(false);
  state.root = null;
  state.dotnet = null;
  setInset(0);
}

// ---------------------------------------------------------------- focus tracking

function applyConfig(config) {
  state.enabled = !!config?.enabled;
  state.requireTouch = config?.requireTouch !== false;
}

function onDocumentPointerDown(e) {
  state.pointerIsDown = true;
  state.lastPointerType = e.pointerType || 'mouse';
  state.lastPointerTime = performance.now();

  if (!state.enabled || insideKeyboard(e.target)) return;

  const field = editableFrom(e.target);
  if (!field) return;
  if (state.requireTouch && !isTouchLike(state.lastPointerType)) return;

  state.physicalKeyboard = false;
  // Stop the operating system's own keyboard before focus lands in the field.
  suppressNativeKeyboard(field);

  // Tapping the field that already has focus (e.g. after "Minimér") brings the keyboard back.
  if (field === document.activeElement) {
    serve(field);
  }
}

function onDocumentPointerUp() {
  state.pointerIsDown = false;
  stopRepeat();
  state.root?.querySelectorAll('.is-down').forEach(key => key.classList.remove('is-down'));
}

function onFocusIn(e) {
  const el = e.target;
  if (insideKeyboard(el)) return;

  if (shouldServe(el)) {
    serve(el);
  } else if (state.target && state.target !== el) {
    scheduleRelease();
  }
}

function onFocusOut(e) {
  if (e.target !== state.target) return;

  if (e.relatedTarget && insideKeyboard(e.relatedTarget)) {
    refocusTarget();
    return;
  }

  scheduleRelease();
}

function onKeyboardFocusIn(e) {
  // Keys never keep focus – hand it straight back to the text field.
  if (state.target && e.target !== state.target) refocusTarget();
}

function onPhysicalKeyDown(e) {
  if (!e.isTrusted || MODIFIER_KEYS.has(e.key)) return;

  state.physicalKeyboard = true;
  if (state.target) release(true);
}

function shouldServe(el) {
  if (!state.enabled || state.physicalKeyboard || !isEditable(el)) return false;
  if (!state.requireTouch) return true;
  return isTouchLike(state.lastPointerType) && performance.now() - state.lastPointerTime < RECENT_POINTER_MS;
}

function serve(el) {
  clearTimeout(state.releaseTimer);

  const previous = state.target;
  if (previous && previous !== el) {
    commitChange(previous);
    restoreInputMode(previous);
  }

  state.target = el;
  state.lastValue = el.value;
  suppressNativeKeyboard(el);
  if (previous !== el) setLayer('letters');
  updateAutoShift();
  startGuard();

  state.dotnet?.invokeMethodAsync('OnFieldFocused', describe(el)).catch(ignore);
}

function scheduleRelease() {
  clearTimeout(state.releaseTimer);
  state.releaseTimer = setTimeout(checkRelease, RELEASE_DELAY_MS);
}

function checkRelease() {
  const target = state.target;
  if (!target) return;

  // Let the current tap finish first, so a button's click lands before the layout changes.
  if (state.pointerIsDown) {
    scheduleRelease();
    return;
  }

  if (target.isConnected && document.activeElement === target) return;
  release(true);
}

function release(notify) {
  clearTimeout(state.releaseTimer);
  stopGuard();
  stopRepeat();

  const target = state.target;
  if (!target) return;

  state.target = null;
  commitChange(target);
  restoreInputMode(target);

  if (notify) {
    state.dotnet?.invokeMethodAsync('OnFieldReleased').catch(ignore);
  }
}

// Safety net: a field removed by a re-render does not always fire focusout.
// Also notices when Blazor changes the text (e.g. cleared after "Tilføj"), so auto-capitals stay right.
function startGuard() {
  if (state.guardTimer) return;
  state.guardTimer = setInterval(() => {
    const target = state.target;
    if (!target) {
      stopGuard();
    } else if (!state.pointerIsDown && (!target.isConnected || document.activeElement !== target)) {
      release(true);
    } else if (target.value !== state.lastValue) {
      state.lastValue = target.value;
      updateAutoShift();
    }
  }, GUARD_INTERVAL_MS);
}

function stopGuard() {
  clearInterval(state.guardTimer);
  state.guardTimer = 0;
}

function refocusTarget() {
  state.target?.focus({ preventScroll: true });
}

function suppressNativeKeyboard(el) {
  if (!el.hasAttribute('data-osk-inputmode')) {
    el.setAttribute('data-osk-inputmode', el.getAttribute('inputmode') ?? '');
  }
  el.setAttribute('inputmode', 'none');
}

function restoreInputMode(el) {
  if (!el.hasAttribute('data-osk-inputmode')) return;
  const original = el.getAttribute('data-osk-inputmode');
  if (original) {
    el.setAttribute('inputmode', original);
  } else {
    el.removeAttribute('inputmode');
  }
  el.removeAttribute('data-osk-inputmode');
}

function describe(el) {
  const type = (el.type || 'text').toLowerCase();
  const mode = (el.getAttribute('data-osk-inputmode') ?? el.getAttribute('inputmode') ?? '').toLowerCase();
  let layout = el.dataset.oskLayout || '';

  if (!layout) {
    if (el.tagName === 'TEXTAREA') layout = 'text';
    else if (type === 'email' || mode === 'email') layout = 'email';
    else if (type === 'url' || mode === 'url') layout = 'url';
    else if (type === 'tel' || mode === 'tel') layout = 'phone';
    else if (mode === 'decimal') layout = 'decimal';
    else if (type === 'number' || mode === 'numeric') layout = 'numeric';
    else layout = 'text';
  }

  return {
    layout,
    multiline: el.tagName === 'TEXTAREA',
    enterKeyHint: (el.getAttribute('enterkeyhint') || '').toLowerCase(),
    enterLabel: el.dataset.oskEnterLabel || '',
    label: labelOf(el),
  };
}

function labelOf(el) {
  const aria = el.getAttribute('aria-label');
  if (aria) return aria;
  const label = el.labels && el.labels[0];
  if (label) return label.textContent.trim();
  return el.getAttribute('placeholder') || '';
}

// ---------------------------------------------------------------- keys

function onKeyPointerDown(e) {
  const key = e.target.closest?.('[data-key]');
  if (!key || !state.root?.contains(key)) return;
  if (e.pointerType === 'mouse' && e.button !== 0) return;

  const target = state.target;
  if (!target || !target.isConnected) return;

  key.classList.add('is-down');
  const action = key.dataset.key;
  runKey(key, action);

  if (action === 'backspace' || action === 'left' || action === 'right') {
    startRepeat(() => runKey(key, action));
  }
}

function runKey(key, action) {
  switch (action) {
    case 'char':
      typeText(key.dataset.text || '');
      break;
    case 'space':
      typeText(' ');
      if (state.layer !== 'letters') {
        setLayer('letters');
        updateAutoShift();
      }
      break;
    case 'backspace':
      deleteBackward();
      break;
    case 'enter':
      pressEnter();
      break;
    case 'shift':
      pressShift();
      break;
    case 'layer':
      setLayer(key.dataset.layerTarget || 'letters');
      updateAutoShift();
      break;
    case 'left':
      moveCaret(-1);
      break;
    case 'right':
      moveCaret(1);
      break;
  }
}

function typeText(text) {
  const el = state.target;
  if (!el || !text) return;

  const value = state.shift !== 'off' && text.length === 1 ? text.toLocaleUpperCase('da-DK') : text;
  if (!insert(el, value)) return;

  if (state.shift === 'once') setShift('off');
  updateAutoShift();
}

function insert(el, text) {
  const max = el.maxLength;
  if (supportsSelection(el)) {
    const start = el.selectionStart;
    const end = el.selectionEnd;
    if (max >= 0 && el.value.length - (end - start) + text.length > max) {
      signalLimit();
      return false;
    }
    el.setRangeText(text, start, end, 'end');
  } else {
    if (max >= 0 && el.value.length + text.length > max) {
      signalLimit();
      return false;
    }
    el.value += text;
  }

  notifyInput(el, 'insertText', text);
  return true;
}

function deleteBackward() {
  const el = state.target;
  if (!el) return;

  if (supportsSelection(el)) {
    let start = el.selectionStart;
    const end = el.selectionEnd;
    if (start === end) {
      if (start === 0) return;
      // Remove a whole emoji (surrogate pair), not half of it.
      start -= start > 1 && isLowSurrogate(el.value.charCodeAt(start - 1)) ? 2 : 1;
    }
    el.setRangeText('', start, end, 'end');
  } else {
    if (!el.value) return;
    el.value = el.value.slice(0, -1);
  }

  notifyInput(el, 'deleteContentBackward', null);
  updateAutoShift();
}

function moveCaret(delta) {
  const el = state.target;
  if (!el || !supportsSelection(el)) return;

  const collapsed = el.selectionStart === el.selectionEnd;
  const from = delta < 0 ? el.selectionStart : el.selectionEnd;
  const position = collapsed ? Math.max(0, Math.min(el.value.length, from + delta)) : from;
  el.setSelectionRange(position, position);
  updateAutoShift();
}

function pressEnter() {
  const el = state.target;
  if (!el) return;

  if (el.tagName === 'TEXTAREA') {
    if (insert(el, '\n')) updateAutoShift();
    return;
  }

  // Let components react (e.g. HubTextField.OnEnter), exactly as with a physical Enter.
  const notHandled = fireSyntheticKey(el, 'Enter');
  commitChange(el);
  if (!notHandled || !el.isConnected) return;

  const hint = (el.getAttribute('enterkeyhint') || '').toLowerCase();
  if (hint === 'next') {
    const next = nextField(el);
    if (next) {
      next.focus();
      return;
    }
  }

  if (el.form && hasSubmitButton(el.form)) {
    el.form.requestSubmit();
  }

  if (el.dataset.oskEnter !== 'keep' && document.activeElement === el) {
    el.blur();
  }
}

function pressShift() {
  const now = performance.now();
  const doubleTap = now - state.lastShiftTap < DOUBLE_TAP_MS;
  state.lastShiftTap = now;

  if (state.shift === 'lock') {
    setShift('off');
    state.lastShiftTap = 0;
  } else if (doubleTap) {
    setShift('lock');
  } else {
    setShift(state.shift === 'off' ? 'once' : 'off');
  }
}

function setShift(value) {
  state.shift = value;
  state.root?.setAttribute('data-shift', value);
}

function setLayer(name) {
  state.layer = name;
  state.root?.setAttribute('data-layer', name);
  if (name !== 'letters' && state.shift !== 'off') setShift('off');
}

// Capital letter at the start of a sentence/word – as on a phone. Follows the field's autocapitalize.
function updateAutoShift() {
  if (state.shift === 'lock') return;

  const el = state.target;
  if (!el || state.layer !== 'letters' || !supportsSelection(el)) {
    setShift('off');
    return;
  }

  const mode = autoCapitalizeMode(el);
  const before = el.value.slice(0, el.selectionStart ?? el.value.length);
  let upper = false;
  if (mode === 'characters') upper = true;
  else if (mode === 'words') upper = before.length === 0 || /\s$/.test(before);
  else if (mode === 'sentences') upper = /^\s*$/.test(before) || /[.!?]\s+$/.test(before) || /\n\s*$/.test(before);

  setShift(upper ? 'once' : 'off');
}

function autoCapitalizeMode(el) {
  const attribute = (el.getAttribute('autocapitalize') || '').toLowerCase();
  if (attribute === 'none' || attribute === 'off') return 'off';
  if (attribute === 'on') return 'sentences';
  if (attribute) return attribute;
  return el.tagName === 'TEXTAREA' || el.type === 'text' ? 'sentences' : 'off';
}

function startRepeat(fn) {
  stopRepeat();
  state.repeatDelay = setTimeout(() => {
    state.repeatTimer = setInterval(fn, REPEAT_INTERVAL_MS);
  }, REPEAT_DELAY_MS);
}

function stopRepeat() {
  clearTimeout(state.repeatDelay);
  clearInterval(state.repeatTimer);
  state.repeatDelay = 0;
  state.repeatTimer = 0;
}

// ---------------------------------------------------------------- helpers

function notifyInput(el, inputType, data) {
  el.dataset.oskDirty = '1';
  state.lastValue = el.value;
  el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType, data }));
}

// Blazor's default @bind listens for 'change'. Programmatic edits do not raise it on blur,
// so fire it ourselves when the keyboard leaves a field it changed.
function commitChange(el) {
  if (el.dataset.oskDirty !== '1') return;
  delete el.dataset.oskDirty;
  if (el.isConnected) el.dispatchEvent(new Event('change', { bubbles: true }));
}

function fireSyntheticKey(el, key) {
  const init = { key, code: key, bubbles: true, cancelable: true };
  const notCanceled = el.dispatchEvent(new KeyboardEvent('keydown', init));
  el.dispatchEvent(new KeyboardEvent('keyup', init));
  return notCanceled;
}

function nextField(el) {
  const scope = el.form || document;
  const fields = [...scope.querySelectorAll('input, textarea')].filter(f => isEditable(f) && f.offsetParent !== null);
  const index = fields.indexOf(el);
  return index >= 0 ? fields[index + 1] ?? null : null;
}

function hasSubmitButton(form) {
  return !!form.querySelector('button[type="submit"], input[type="submit"], button:not([type])');
}

function signalLimit() {
  const root = state.root;
  if (!root) return;
  root.classList.remove('is-limit');
  void root.offsetWidth;   // restart the CSS animation
  root.classList.add('is-limit');
}

function onKeyboardResize() {
  const root = state.root;
  if (!root) return;

  const height = root.getBoundingClientRect().height;
  setInset(height);
  if (height > 0 && state.target) {
    requestAnimationFrame(() => state.target?.scrollIntoView({ block: 'nearest', inline: 'nearest' }));
  }
}

function setInset(px) {
  const html = document.documentElement;
  html.style.setProperty('--hub-osk-inset', `${Math.round(px)}px`);
  html.classList.toggle('hub-osk-open', px > 0);
}

function editableFrom(node) {
  const el = node?.closest?.('input, textarea');
  return isEditable(el) ? el : null;
}

function isEditable(el) {
  if (!el || el.disabled || el.readOnly) return false;
  if (el.closest('[data-osk="off"]')) return false;
  if (el.tagName === 'TEXTAREA') return true;
  if (el.tagName !== 'INPUT') return false;
  return TEXT_INPUT_TYPES.has((el.type || 'text').toLowerCase());
}

function supportsSelection(el) {
  return el.tagName === 'TEXTAREA' || SELECTION_TYPES.has((el.type || 'text').toLowerCase());
}

function insideKeyboard(node) {
  return !!(state.root && node && state.root.contains(node));
}

function isTouchLike(pointerType) {
  return pointerType === 'touch' || pointerType === 'pen';
}

function isLowSurrogate(code) {
  return code >= 0xdc00 && code <= 0xdfff;
}

function preventDefault(e) {
  e.preventDefault();
}

function ignore() { }
