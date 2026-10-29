// A touch screen's fingers, queued for the game as they come down, move and lift: 'touch 3 down 640 360'.
//
// The controls themselves -- the stick, the look, the buttons, the menus' pointer, the hold that skips a cutscene -- are the
// game's (TouchControls in OpenIGI.Presentation), drawn in its own look and pressing the keys the player's actions are bound
// to. All the page adds is the hint to turn a phone held upright on its side.

const STYLE = `
#turn-hint { position: absolute; inset: 0; display: none; align-items: center; justify-content: center; padding: 24px;
  text-align: center; background: rgba(0, 0, 0, 0.88); color: #ecebe6; font: 600 16px/1.4 system-ui, sans-serif; }
@media (orientation: portrait) and (pointer: coarse) { #turn-hint { display: flex; } }
`;

let listening = null;
let hint = null;

// Each finger by a small number of its own, counted up as fingers come down. The browser's pointer ids can be anything --
// Safari's are ten digits -- and the game reads the line's numbers as floats, which cannot tell two such ids apart: two
// fingers came through as one, and the second took the first's place on the stick. Counted rather than reused, so a tap
// lifting and the next finger landing in the same frame are never taken for one finger either.
const fingers = new Map();
let nextFinger = 0;

function finger(pointerId, lifting) {
  let number = fingers.get(pointerId);
  if (number === undefined) {
    number = nextFinger;
    nextFinger = (nextFinger + 1) % 1000000;
    fingers.set(pointerId, number);
  }
  if (lifting) fingers.delete(pointerId);
  return number;
}

/**
 * Starts listening for fingers on the canvas.
 * @param {HTMLCanvasElement} canvas The canvas the game draws to.
 * @param {(line: string) => void} enqueue Queues one event line for the game (platform.js).
 * @param {(e: PointerEvent) => [string, string]} position A finger's position in the canvas's pixels.
 */
export function install(canvas, enqueue, position) {
  listening = new AbortController();
  const signal = listening.signal;

  const send = (phase, e) => {
    if (e.pointerType !== 'touch') return false;
    e.preventDefault(); // No mouse events after it, which would click a second time.
    const [x, y] = position(e);
    enqueue(`touch ${finger(e.pointerId, phase === 'up')} ${phase} ${x} ${y}`);
    return true;
  };

  canvas.addEventListener('pointerdown', e => {
    if (send('down', e)) canvas.setPointerCapture?.(e.pointerId);
  }, { signal });
  canvas.addEventListener('pointermove', e => send('move', e), { signal });
  canvas.addEventListener('pointerup', e => send('up', e), { signal });
  canvas.addEventListener('pointercancel', e => send('up', e), { signal });
  canvas.addEventListener('contextmenu', e => e.preventDefault(), { signal });

  // The browser's own gestures, kept off the game. A second finger landing starts a pinch, and a phone's browser -- Safari
  // above all, which does not honour touch-action for it -- cancels the first finger to zoom the page: the thumb on the stick
  // is lost and the second finger comes down as a new one. Refusing the touch events themselves stops it everywhere.
  const refuse = e => e.preventDefault();
  for (const type of ['touchstart', 'touchmove', 'touchend']) canvas.addEventListener(type, refuse, { passive: false, signal });
  for (const type of ['gesturestart', 'gesturechange', 'gestureend']) document.addEventListener(type, refuse, { signal });
  document.addEventListener('dblclick', refuse, { signal });

  if (matchMedia('(pointer: coarse)').matches && !hint) {
    const style = document.createElement('style');
    style.textContent = STYLE;
    document.head.append(style);
    hint = document.createElement('div');
    hint.id = 'turn-hint';
    hint.textContent = 'Turn your phone on its side to play.';
    canvas.parentElement.append(hint);
  }
}

/** Stops listening. */
export function uninstall() {
  fingers.clear();
  listening?.abort();
  listening = null;
  hint?.remove();
  hint = null;
}
