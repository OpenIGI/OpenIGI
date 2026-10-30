// The page's half of the browser window: its input, queued for the game to take once a frame, and the
// few things the game asks of the page.
//
// A browser delivers input to handlers, between frames; the game reads input once, at the top of each
// frame. So every handler here only writes a line to a queue -- 'key KeyW 1', 'move 3 -1 640 360' -- and
// WebGameWindow (C#) reads the queue in PumpEvents. The words are what WebInputEvent.Parse reads.

import * as touch from './touch.js';

let canvas = null;
let queue = [];
let listening = null;
let resizes = null;
let wantLock = false;
let pointerVisible = true;

// Whether the player is on a touch screen: the last pointer down on the canvas was a finger, or, before any, the screen's
// main pointer is one. Play asks for the pointer lock, and Chrome on Android grants it to a page a finger has just tapped --
// after which the fingers no longer reach the page as touches, and every control in play goes dead while the menus, which
// do not lock, still work. A finger has nothing to lock, so the lock is asked for only from a mouse.
let touching = matchMedia('(pointer: coarse)').matches;

// Every device pixel, a phone's three to the CSS pixel included: drawn at fewer, the browser scaled the picture up and
// the menus' small type smeared past reading.
const ratio = () => window.devicePixelRatio || 1;
const drawable = () => {
  const box = canvas.getBoundingClientRect();
  return [Math.max(1, Math.round(box.width * ratio())), Math.max(1, Math.round(box.height * ratio()))];
};

// Keys the page keeps: reload, fullscreen and the developer tools, which a player expects to reach while a
// game has the keyboard. Everything else is the game's, and the browser's own use of it is stopped. F11
// was the game's too, so once a game was running the browser never saw it and would not go fullscreen;
// in the game it only re-bakes the lighting, a developer's key with no place in a browser.
const PAGE_KEYS = new Set(['F5', 'F11', 'F12']);

function pointerPosition(e) {
  const box = canvas.getBoundingClientRect();
  return [((e.clientX - box.left) * ratio()).toFixed(1), ((e.clientY - box.top) * ratio()).toFixed(1)];
}

function applyPointer() {
  canvas.style.cursor = pointerVisible && !wantLock ? 'default' : 'none';
  if (wantLock && document.pointerLockElement !== canvas) lock();
  if (!wantLock && document.pointerLockElement === canvas) document.exitPointerLock();
}

// Unadjusted movement is the sensor's own counts, without the desktop's pointer acceleration -- what a
// look control wants, and what the desktop build asks for as raw motion. Not every browser offers it.
function lock() {
  if (touching) return;
  try {
    const request = canvas.requestPointerLock({ unadjustedMovement: true });
    request?.catch?.(() => canvas.requestPointerLock()?.catch?.(() => {}));
  } catch {
    // Refused outside a click; asked again on the next one.
  }
}

/**
 * Starts listening on the page.
 * @param {HTMLCanvasElement} target The canvas the game draws to.
 */
export function install(target) {
  canvas = target;
  canvas.tabIndex = 0;

  // Every listener hangs off one signal, so that uninstall takes them all away together.
  listening = new AbortController();
  const signal = listening.signal;

  window.addEventListener('keydown', e => {
    if (PAGE_KEYS.has(e.code) || e.metaKey) return;
    e.preventDefault();
    if (!e.repeat) queue.push(`key ${e.code} 1`);
    if (e.key.length === 1 && !e.ctrlKey && !e.altKey) queue.push(`char ${e.key}`);
  }, { signal });
  window.addEventListener('keyup', e => {
    if (PAGE_KEYS.has(e.code) || e.metaKey) return;
    e.preventDefault();
    queue.push(`key ${e.code} 0`);
  }, { signal });

  canvas.addEventListener('pointerdown', e => { touching = e.pointerType === 'touch'; }, { signal });
  canvas.addEventListener('mousedown', e => {
    if (wantLock && document.pointerLockElement !== canvas) lock();
    queue.push(`button ${e.button} 1`);
    e.preventDefault();
  }, { signal });
  window.addEventListener('mouseup', e => queue.push(`button ${e.button} 0`), { signal });
  canvas.addEventListener('contextmenu', e => e.preventDefault(), { signal });
  window.addEventListener('mousemove', e => {
    const [x, y] = pointerPosition(e);
    queue.push(`move ${e.movementX} ${e.movementY} ${x} ${y}`);
  }, { signal });
  canvas.addEventListener('wheel', e => {
    // Notches, positive away from the player: a pixel delta is about a hundred to the notch, a line
    // delta three.
    const notches = -e.deltaY / (e.deltaMode === 0 ? 100 : e.deltaMode === 1 ? 3 : 1);
    queue.push(`wheel ${notches}`);
    e.preventDefault();
  }, { passive: false, signal });

  document.addEventListener('pointerlockchange', () =>
    queue.push(`lock ${document.pointerLockElement === canvas ? 1 : 0}`), { signal });
  window.addEventListener('focus', () => queue.push('focus 1'), { signal });
  window.addEventListener('blur', () => queue.push('focus 0'), { signal });
  document.addEventListener('visibilitychange', () => queue.push(`focus ${document.hidden ? 0 : 1}`), { signal });
  window.addEventListener('pagehide', () => queue.push('close'), { signal });

  touch.install(canvas, line => queue.push(line), pointerPosition);

  resizes = new ResizeObserver(() => {
    const [width, height] = drawable();
    queue.push(`resize ${width} ${height}`);
  });
  resizes.observe(canvas);
}

/**
 * Stops listening, once the game has ended: the page has the keyboard and the pointer back, and nothing is
 * queued for a game that will never take it.
 */
export function uninstall() {
  touch.uninstall();
  listening?.abort();
  resizes?.disconnect();
  listening = null;
  resizes = null;
  queue = [];
  wantLock = false;
  if (document.pointerLockElement === canvas) document.exitPointerLock();
  if (document.fullscreenElement) document.exitFullscreen?.().catch(() => {});
  if (canvas) canvas.style.cursor = '';
}

/** What the runtime imports, registered under 'openigi-platform'. */
export const imports = {
  canvasWidth: () => drawable()[0],
  canvasHeight: () => drawable()[1],
  hasFocus: () => document.hasFocus(),
  isFullscreen: () => document.fullscreenElement !== null,
  takeEvents: () => {
    const taken = queue;
    queue = [];
    return taken;
  },
  setTitle: title => { document.title = title; },
  setPointer: (visible, locked) => {
    pointerVisible = visible;
    wantLock = locked;
    applyPointer();
  },
  requestFullscreen: fullscreen => {
    // The page rather than the canvas, so the touch controls laid over the canvas come along.
    if (fullscreen && !document.fullscreenElement) document.documentElement.requestFullscreen?.().catch(() => {});
    if (!fullscreen && document.fullscreenElement) document.exitFullscreen?.().catch(() => {});
  },
  writeClipboard: text => { navigator.clipboard?.writeText(text).catch(() => {}); },
};
