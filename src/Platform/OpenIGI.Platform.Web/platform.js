// The page's half of the browser window: its input, queued for the game to take once a frame, and the
// few things the game asks of the page.
//
// A browser delivers input to handlers, between frames; the game reads input once, at the top of each
// frame. So every handler here only writes a line to a queue -- 'key KeyW 1', 'move 3 -1 640 360' -- and
// WebGameWindow (C#) reads the queue in PumpEvents. The words are what WebInputEvent.Parse reads.

import * as gyro from './gyro.js';
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

// An iPad (which says it is a Mac) or an iPhone, where the page never goes fullscreen: the iPad's Safari takes a game's
// quick thumbs in fullscreen for typing on a fake keyboard, warns and leaves it, and its swipe to leave takes the stick's
// finger. The iPhone's has no fullscreen for a page at all.
const appleTouch = /iPad|iPhone|iPod/.test(navigator.userAgent)
  || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);

// Whether a touch screen's page should be fullscreen: from the start (Play took the screen), until the game's own setting says
// windowed. A phone's browser drops fullscreen at a swipe from the edge, a notification or the back gesture, and its address
// bar then takes a fifth of the screen with nothing to bring fullscreen back -- a page may ask for it only inside a tap.
let screenWanted = !appleTouch && matchMedia('(pointer: coarse)').matches;

// Takes the screen back, from inside a finger's tap on the game, when something has taken it away.
function retakeScreen() {
  if (!screenWanted || appleTouch || document.fullscreenElement || !document.documentElement.requestFullscreen) return;
  document.documentElement.requestFullscreen({ navigationUI: 'hide' })
    .then(() => { if (touching) screen.orientation?.lock?.('landscape')?.catch?.(() => {}); })
    .catch(() => {});
}

// Every device pixel, a phone's three to the CSS pixel included: drawn at fewer, the browser scaled the picture up and
// the menus' small type smeared past reading.
const ratio = () => window.devicePixelRatio || 1;

// How far in from each edge the screen is clear of a notch, rounded corners and the home bar, in CSS pixels. The page
// covers the whole screen (viewport-fit=cover), so the browser says only through env() what of it is hidden: read
// off an element padded by those.
let safeProbe = null;
const safeInset = (side) => {
  if (!safeProbe) {
    safeProbe = document.createElement('div');
    safeProbe.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;'
      + 'padding:env(safe-area-inset-top,0px) env(safe-area-inset-right,0px) env(safe-area-inset-bottom,0px) env(safe-area-inset-left,0px)';
    document.body.appendChild(safeProbe);
  }
  const style = getComputedStyle(safeProbe);
  const value = parseFloat([style.paddingLeft, style.paddingTop, style.paddingRight, style.paddingBottom][side]);
  return Number.isFinite(value) ? value * ratio() : 0;
};
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
  if (wantLock) relock();
  else clearTimeout(lockRetry);
  if (!wantLock && document.pointerLockElement === canvas) document.exitPointerLock();
}

// Unadjusted movement is the sensor's own counts, without the desktop's pointer acceleration -- what a
// look control wants, and what the desktop build asks for as raw motion. Not every browser offers it.
//
// A browser refuses the lock for a second or so after the player's Escape took it away, and refuses it outside a press
// of a key or a button -- and Escape, the key that closes the pause menu, is the one key that does not count as a press.
// A refusal left the game believing it had the mouse while the pointer ran free, stopping at the screen's edge, until
// the player happened to click. So a refused lock is asked for again: shortly, a few times, and at every key and button.
const LOCK_RETRY_MS = 350;
const LOCK_RETRIES = 8;
let lockRetry = 0;
let lockRetries = 0;

function lock() {
  if (touching || !canvas) return;
  clearTimeout(lockRetry);
  const refused = () => {
    if (!wantLock || document.pointerLockElement === canvas || lockRetries >= LOCK_RETRIES) return;
    lockRetries++;
    lockRetry = setTimeout(lock, LOCK_RETRY_MS);
  };
  try {
    const request = canvas.requestPointerLock({ unadjustedMovement: true });
    request?.catch?.(error => {
      // No raw motion here: the ordinary lock. Anything else is the browser saying not now.
      if (error?.name !== 'NotSupportedError') return refused();
      try {
        canvas.requestPointerLock()?.catch?.(refused);
      } catch {
        refused();
      }
    });
  } catch {
    refused();
  }
}

// The lock again, from inside the player's own press, when the game wants it and the browser has not given it.
function relock() {
  if (!wantLock || !canvas || document.pointerLockElement === canvas) return;
  lockRetries = 0;
  lock();
}

// Escape held on, for the game, after the browser took it. A browser frees the pointer at Escape and keeps the press to
// itself: the page hears only that the lock has gone, which WebGameWindow hands the game as the key going down. How long
// it stays down is said here, because only the page can tell: until the key comes up, or stops repeating. A cutscene is
// skipped by holding Escape, and a key the game saw for a single frame never filled the hold -- the player had to let
// go, and press and hold again, with the pointer already free.
const ESCAPE_FIRST_REPEAT_MS = 650;
const ESCAPE_NEXT_REPEAT_MS = 200;
const ESCAPE_TAP_MS = 400;
let escapeHeld = 0;
let escapeUpAt = -Infinity;

function holdEscapeFor(ms) {
  clearTimeout(escapeHeld);
  escapeHeld = setTimeout(releaseEscape, ms);
}

function releaseEscape() {
  if (!escapeHeld) return;
  clearTimeout(escapeHeld);
  escapeHeld = 0;
  queue.push('key Escape 0');
}

// The lock has gone while the game wanted it: the key is down for the game, and let go at once when it has come up already.
function escapeTaken() {
  if (performance.now() - escapeUpAt < ESCAPE_TAP_MS) queue.push('key Escape 0');
  else holdEscapeFor(ESCAPE_FIRST_REPEAT_MS);
}

// Escape is the game's while the page is fullscreen, where the browser allows it (Chromium's Keyboard Lock): a press
// opens the pause menu instead of dropping the page out of fullscreen and the mouse out of the game, and holding the
// key is how the player leaves fullscreen. Elsewhere the browser keeps the key, and fullscreen is taken back at the
// next press.
const holdsEscape = Boolean(navigator.keyboard?.lock);

function holdEscape(held) {
  if (!holdsEscape) return;
  if (held) navigator.keyboard.lock(['Escape']).catch(() => {});
  else navigator.keyboard.unlock();
}

// The field a touch screen's keyboard types into, and whether the game wants typing now. In line mode (a line of chat) the
// field keeps the whole line -- the keyboard's own autocorrect, suggestions and Backspace work on it -- and says it to the game
// as it changes; otherwise each character goes as it is typed. Where on the canvas a touch raises the keyboard (the chat
// button), in canvas pixels; and how much of the canvas the keyboard covers, last said.
let textField = null;
let textForm = null;
let textWanted = false;
let lineMode = false;
let textTrigger = null;
let insetSaid = 0;

// Raises the keyboard: only ever from inside the player's own touch, the one kind of focus a phone's browser raises it for.
function raiseKeyboard() {
  if (textField && document.activeElement !== textField) textField.focus({ preventScroll: true });
}

// Whether a touch ended on the area that begins typing.
function onTrigger(clientX, clientY) {
  if (!textTrigger || !canvas) return false;
  const box = canvas.getBoundingClientRect();
  if (box.width <= 0 || box.height <= 0) return false;
  const x = (clientX - box.left) * canvas.width / box.width;
  const y = (clientY - box.top) * canvas.height / box.height;
  return x >= textTrigger.x && x <= textTrigger.x + textTrigger.w && y >= textTrigger.y && y <= textTrigger.y + textTrigger.h;
}

// How much of the canvas's bottom the keyboard covers, in canvas pixels, said to the game when it changes.
function sayInset() {
  const viewport = window.visualViewport;
  if (!viewport || !canvas) return;
  const box = canvas.getBoundingClientRect();
  const covered = Math.max(0, box.bottom - (viewport.offsetTop + viewport.height));
  const inset = box.height > 0 ? Math.round(covered * canvas.height / box.height) : 0;
  if (inset !== insetSaid) {
    insetSaid = inset;
    queue.push(`inset ${inset}`);
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

  // A field nobody sees, for a touch screen's keyboard: a browser raises one only for a field that is focused, and only
  // when the focus comes from the player's own touch -- so while the game wants typing, a touch on the game focuses it.
  // What is typed into it goes to the game as characters; it is emptied as it goes.
  // In a form, so the keyboard's return key (shown as Send) submits it: the game hears Enter, and the keyboard goes down.
  // At the top of the page, so focusing it never scrolls the page up under the keyboard; 16px, so iOS doesn't zoom.
  textForm = document.createElement('form');
  textForm.setAttribute('action', '#');
  textField = document.createElement('input');
  Object.assign(textField, { type: 'text', autocomplete: 'off', autocapitalize: 'characters', spellcheck: false });
  textField.setAttribute('enterkeyhint', 'send');
  textField.setAttribute('aria-hidden', 'true');
  Object.assign(textField.style, {
    position: 'fixed', left: '0', top: '0', width: '1px', height: '1px', opacity: '0', fontSize: '16px', border: '0', padding: '0',
  });
  textForm.appendChild(textField);
  document.body.appendChild(textForm);
  textField.addEventListener('input', () => {
    if (lineMode) {
      queue.push(`line ${textField.value}`);
      return;
    }

    for (const ch of textField.value) queue.push(`char ${ch}`);
    textField.value = '';
  }, { signal });
  let sent = false;
  textForm.addEventListener('submit', e => {
    e.preventDefault();
    sent = true;
    queue.push('key Enter 1', 'key Enter 0');
    textField.blur();
  }, { signal });
  textField.addEventListener('focus', () => { sent = false; }, { signal });
  // The keyboard put away without sending (its own hide key, Android's back, a tap elsewhere): the line is let go, as Escape
  // would -- unless a touch on the game gives the field back straight away.
  textField.addEventListener('blur', () => {
    if (!lineMode || sent) return;
    setTimeout(() => {
      if (textWanted && lineMode && document.activeElement !== textField) queue.push('key Escape 1', 'key Escape 0');
    }, 300);
  }, { signal });
  const raise = e => {
    const point = e.changedTouches?.[0] ?? e;
    if (textWanted || onTrigger(point.clientX, point.clientY)) raiseKeyboard();
  };
  canvas.addEventListener('touchend', raise, { signal });
  canvas.addEventListener('pointerup', e => {
    if (e.pointerType !== 'touch') return;
    raise(e);
    retakeScreen();
  }, { signal });
  window.visualViewport?.addEventListener('resize', sayInset, { signal });
  window.visualViewport?.addEventListener('scroll', sayInset, { signal });

  window.addEventListener('keydown', e => {
    if (PAGE_KEYS.has(e.code) || e.metaKey) return;
    // A character typed into the field comes through its input; the keys that edit (Backspace, Enter, Escape) as keys --
    // except in line mode, where the field edits its own line and its Enter submits the form; only Escape is the game's.
    if (e.target === textField && e.key.length === 1) return;
    if (e.target === textField && lineMode && e.key !== 'Escape') return;
    e.preventDefault();
    if (e.code === 'Escape' && escapeHeld) {
      // Still held: the browser repeats it. A press of its own ends the hold made on the browser's behalf first.
      if (e.repeat) return holdEscapeFor(ESCAPE_NEXT_REPEAT_MS);
      releaseEscape();
    }

    if (!e.repeat) {
      relock();
      if (e.code !== 'Escape') retakeScreen();
      queue.push(`key ${e.code} 1`);
    }
    if (e.key.length === 1 && !e.ctrlKey && !e.altKey) queue.push(`char ${e.key}`);
  }, { signal });
  window.addEventListener('keyup', e => {
    if (PAGE_KEYS.has(e.code) || e.metaKey) return;
    e.preventDefault();
    if (e.code === 'Escape') {
      escapeUpAt = performance.now();
      if (escapeHeld) return releaseEscape();
    }

    queue.push(`key ${e.code} 0`);
  }, { signal });

  canvas.addEventListener('pointerdown', e => { touching = e.pointerType === 'touch'; }, { signal });
  canvas.addEventListener('mousedown', e => {
    relock();
    if (!touching) retakeScreen();
    queue.push(`button ${e.button} 1`);
    e.preventDefault();
  }, { signal });
  window.addEventListener('mouseup', e => {
    queue.push(`button ${e.button} 0`);
    // The side buttons are the browser's back and forward: bound to an action in the game, they left the page instead.
    if (e.button > 2) e.preventDefault();
  }, { signal });
  canvas.addEventListener('auxclick', e => e.preventDefault(), { signal });
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

  document.addEventListener('pointerlockchange', () => {
    const locked = document.pointerLockElement === canvas;
    if (locked) {
      clearTimeout(lockRetry);
      lockRetries = 0;
    }

    queue.push(`lock ${locked ? 1 : 0}`);
    if (!locked && wantLock) escapeTaken();
  }, { signal });
  document.addEventListener('fullscreenchange', () => {
    const fullscreen = document.fullscreenElement !== null;
    holdEscape(fullscreen);
    // Where Escape is the game's, fullscreen is left only by holding it: the player meant to, and it is not taken back.
    if (!fullscreen && holdsEscape && !touching) screenWanted = false;
  }, { signal });
  holdEscape(document.fullscreenElement !== null);
  window.addEventListener('focus', () => queue.push('focus 1'), { signal });
  window.addEventListener('blur', () => queue.push('focus 0'), { signal });
  document.addEventListener('visibilitychange', () => queue.push(`focus ${document.hidden ? 0 : 1}`), { signal });
  window.addEventListener('pagehide', () => queue.push('close'), { signal });

  touch.install(canvas, line => queue.push(line), pointerPosition);
  gyro.install(canvas);

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
  gyro.uninstall();
  touch.uninstall();
  listening?.abort();
  textForm?.remove();
  textForm = null;
  textField = null;
  resizes?.disconnect();
  listening = null;
  resizes = null;
  queue = [];
  wantLock = false;
  clearTimeout(lockRetry);
  clearTimeout(escapeHeld);
  escapeHeld = 0;
  holdEscape(false);
  if (document.pointerLockElement === canvas) document.exitPointerLock();
  if (document.fullscreenElement) document.exitFullscreen?.().catch(() => {});
  if (canvas) canvas.style.cursor = '';
}

/** What the runtime imports, registered under 'openigi-platform'. */
export const imports = {
  canvasWidth: () => drawable()[0],
  canvasHeight: () => drawable()[1],
  pixelRatio: () => ratio(),
  safeInset: (side) => safeInset(side),
  hasFocus: () => document.hasFocus(),
  isFullscreen: () => document.fullscreenElement !== null,
  takeEvents: () => {
    const turned = gyro.take();
    if (turned) queue.push(turned);
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
    screenWanted = fullscreen && !appleTouch;
    if (fullscreen && !document.fullscreenElement && !appleTouch) document.documentElement.requestFullscreen?.().catch(() => {});
    if (!fullscreen && document.fullscreenElement) document.exitFullscreen?.().catch(() => {});
  },
  writeClipboard: text => { navigator.clipboard?.writeText(text).catch(() => {}); },
  setGyro: wanted => gyro.setWanted(wanted),
  setTextEntry: wanted => {
    textWanted = wanted;
    if (!wanted && textField && document.activeElement === textField) textField.blur();
  },
  setTextLine: (on, line) => {
    lineMode = on;
    if (!textField) return;
    textField.value = on ? line : '';
    Object.assign(textField, on
      ? { autocapitalize: 'sentences', spellcheck: true, autocomplete: 'on' }
      : { autocapitalize: 'characters', spellcheck: false, autocomplete: 'off' });
  },
  setTextTrigger: (x, y, w, h) => { textTrigger = w > 0 && h > 0 ? { x, y, w, h } : null; },
};
