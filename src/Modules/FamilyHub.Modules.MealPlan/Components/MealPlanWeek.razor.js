// Gestures for the meal plan's week (MealPlanWeek.razor):
//  - Swipe sideways anywhere on the week → previous/next week.
//  - Hold a dinner (finger) or drag it (mouse) → drop it on another day to move it; a planned day swaps.
// Both are shortcuts – the arrows and "Flyt til" in the dinner dialog do the same, so nothing depends on a gesture.
// Pointer events, so it works the same with a finger on the kitchen screen and a mouse on the laptop.

const HOLD_MS = 350;          // finger: how long to hold a dinner before it lifts
const MOVE_TOLERANCE = 10;    // px of wobble allowed while holding / before a mouse drag starts
const SWIPE_MIN = 80;         // px sideways for a swipe
const SWIPE_MAX_MS = 800;     // a swipe is a quick movement

export function attach(root, dotnet) {
  let press = null;           // the current pointer press
  let drag = null;            // the dinner being dragged
  let suppressClick = false;  // the click that follows a drag or swipe must not open the day

  function onPointerDown(e) {
    if (press || (e.pointerType === 'mouse' && e.button !== 0)) {
      return;
    }

    const dinner = e.target.closest('[data-dinner]');
    press = { id: e.pointerId, x: e.clientX, y: e.clientY, time: performance.now(), type: e.pointerType, dinner, timer: 0 };
    if (dinner && e.pointerType !== 'mouse') {
      press.timer = setTimeout(() => startDrag(press.x, press.y), HOLD_MS);
    }
  }

  function onPointerMove(e) {
    if (!press || e.pointerId !== press.id) {
      return;
    }

    if (drag) {
      e.preventDefault();
      moveDrag(e.clientX, e.clientY);
      return;
    }

    if (Math.hypot(e.clientX - press.x, e.clientY - press.y) > MOVE_TOLERANCE) {
      clearTimeout(press.timer);
      if (press.dinner && press.type === 'mouse') {
        startDrag(e.clientX, e.clientY);
      } else {
        press.dinner = null; // the finger moved before the hold: this is a swipe or a scroll, not a drag
      }
    }
  }

  function onPointerUp(e) {
    if (!press || e.pointerId !== press.id) {
      return;
    }

    clearTimeout(press.timer);
    if (drag) {
      suppressClick = true;
      finishDrag();
    } else {
      const dx = e.clientX - press.x;
      const dy = e.clientY - press.y;
      const quick = performance.now() - press.time < SWIPE_MAX_MS;
      if (quick && Math.abs(dx) > SWIPE_MIN && Math.abs(dx) > 1.5 * Math.abs(dy)) {
        suppressClick = true;
        dotnet.invokeMethodAsync('Swipe', dx < 0 ? 1 : -1);
      }
    }

    press = null;
    if (suppressClick) {
      // With pointer capture the browser may send no click at all – never swallow a later, real tap.
      setTimeout(() => { suppressClick = false; }, 400);
    }
  }

  function onPointerCancel(e) {
    if (!press || e.pointerId !== press.id) {
      return;
    }

    clearTimeout(press.timer);
    cleanupDrag();
    press = null;
  }

  // Capture phase on the week: runs before Blazor sees the click, so a drag or swipe never also opens the day.
  function onClick(e) {
    if (suppressClick) {
      suppressClick = false;
      e.preventDefault();
      e.stopPropagation();
    }
  }

  // Once a dinner is lifted, the finger moves it instead of scrolling the page.
  function onTouchMove(e) {
    if (drag) {
      e.preventDefault();
    }
  }

  function onContextMenu(e) {
    if (drag || press?.dinner) {
      e.preventDefault();
    }
  }

  function startDrag(x, y) {
    if (!press?.dinner || drag) {
      return;
    }

    const source = press.dinner;
    const day = source.closest('[data-day]');
    const rect = source.getBoundingClientRect();
    const ghost = source.cloneNode(true);
    ghost.removeAttribute('data-dinner');
    ghost.classList.add('mp-dinner--ghost');
    ghost.setAttribute('aria-hidden', 'true');
    ghost.style.width = `${rect.width}px`;
    document.body.appendChild(ghost);

    drag = { ghost, day, from: source.dataset.dinner, target: null, offsetX: x - rect.left, offsetY: y - rect.top };
    day.classList.add('mp-day--dragging');
    root.classList.add('mp-week--dragging');
    try {
      root.setPointerCapture(press.id);
    } catch {
      // The pointer may already be gone.
    }

    navigator.vibrate?.(10);
    moveDrag(x, y);
  }

  function moveDrag(x, y) {
    drag.ghost.style.transform = `translate(${x - drag.offsetX}px, ${y - drag.offsetY}px) scale(1.04)`;

    const under = document.elementFromPoint(x, y)?.closest('[data-day]');
    const target = under && root.contains(under) && under !== drag.day ? under : null;
    if (target !== drag.target) {
      drag.target?.classList.remove('mp-day--target');
      target?.classList.add('mp-day--target');
      drag.target = target;
    }
  }

  function finishDrag() {
    const from = drag.from;
    const to = drag.target?.dataset.day;
    cleanupDrag();
    if (to && to !== from) {
      dotnet.invokeMethodAsync('Move', from, to);
    }
  }

  function cleanupDrag() {
    if (!drag) {
      return;
    }

    drag.ghost.remove();
    drag.day.classList.remove('mp-day--dragging');
    drag.target?.classList.remove('mp-day--target');
    root.classList.remove('mp-week--dragging');
    drag = null;
  }

  root.addEventListener('pointerdown', onPointerDown);
  window.addEventListener('pointermove', onPointerMove, { passive: false });
  window.addEventListener('pointerup', onPointerUp);
  window.addEventListener('pointercancel', onPointerCancel);
  root.addEventListener('click', onClick, true);
  root.addEventListener('touchmove', onTouchMove, { passive: false });
  root.addEventListener('contextmenu', onContextMenu);

  return {
    dispose() {
      clearTimeout(press?.timer);
      cleanupDrag();
      root.removeEventListener('pointerdown', onPointerDown);
      window.removeEventListener('pointermove', onPointerMove);
      window.removeEventListener('pointerup', onPointerUp);
      window.removeEventListener('pointercancel', onPointerCancel);
      root.removeEventListener('click', onClick, true);
      root.removeEventListener('touchmove', onTouchMove);
      root.removeEventListener('contextmenu', onContextMenu);
    },
  };
}
