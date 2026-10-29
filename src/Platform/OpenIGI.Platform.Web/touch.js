// The touch screen's controls: what a phone or a tablet has in place of the keyboard and the mouse.
//
// Nothing here reaches the game but the lines platform.js queues for a keyboard and a mouse, so the game
// cannot tell a thumb from a key: the stick on the left holds W, A, S and D down, a drag on the right is the
// mouse's motion, and each button is the key it is assigned by default (PlayerProfile.DefaultBindings). A
// player who assigns those actions other keys has to assign them back for the buttons to follow.
//
// Two layouts, as the game asks for the pointer: in the menus, where it shows a cursor, a touch is the
// cursor -- it moves there and clicks; in play, where it locks the pointer, the controls are shown.

/** How far a drag turns the view: mouse counts per CSS pixel. */
const LOOK = 1.6;
/** The stick's reach in CSS pixels, and the share of it a thumb moves before a direction is held. */
const STICK_RADIUS = 56;
const STICK_DEAD = 0.35;

/** The buttons in play: the key or mouse button each holds, where it sits, and what it says. */
const BUTTONS = [
  { id: 'fire', label: 'Fire', press: 'button 0', at: 'fire', look: true },
  { id: 'jump', label: 'Jump', press: 'key Space', at: 'a1' },
  { id: 'crouch', label: 'Crouch', press: 'key ControlRight', at: 'a2' },
  { id: 'reload', label: 'Reload', press: 'key Enter', at: 'a3' },
  { id: 'use', label: 'Use', press: 'key KeyE', at: 'a4' },
  { id: 'weapon', label: 'Weapon', press: 'button 2', at: 'a5' },
  { id: 'peek', label: 'Peek', press: 'key Numpad1', at: 'a6' },
  { id: 'pause', label: '❚❚', press: 'key Escape', at: 't1', small: true },
  { id: 'map', label: 'Map', press: 'key KeyC', at: 't2', small: true },
  { id: 'binoculars', label: 'Bino', press: 'key KeyB', at: 't3', small: true },
  { id: 'zoomin', label: '+', press: 'key PageUp', at: 't4', small: true },
  { id: 'zoomout', label: '−', press: 'key PageDown', at: 't5', small: true },
];

const STYLE = `
#touch { position: absolute; inset: 0; pointer-events: none; user-select: none; -webkit-user-select: none;
  -webkit-touch-callout: none; font: 600 12px/1 system-ui, sans-serif; color: rgba(255,255,255,0.85); }
#touch[hidden], #touch [hidden] { display: none; }
#touch .stick-zone { position: absolute; left: 0; bottom: 0; width: 42%; height: 70%; pointer-events: auto; touch-action: none; }
#touch .stick { position: absolute; width: ${STICK_RADIUS * 2}px; height: ${STICK_RADIUS * 2}px; margin: -${STICK_RADIUS}px 0 0 -${STICK_RADIUS}px;
  border-radius: 50%; border: 2px solid rgba(255,255,255,0.3); background: rgba(0,0,0,0.15); }
#touch .knob { position: absolute; left: 50%; top: 50%; width: 48px; height: 48px; margin: -24px 0 0 -24px;
  border-radius: 50%; background: rgba(255,255,255,0.35); }
#touch .button { position: absolute; display: flex; align-items: center; justify-content: center; pointer-events: auto;
  touch-action: none; width: 58px; height: 58px; border-radius: 50%; border: 2px solid rgba(255,255,255,0.35);
  background: rgba(0,0,0,0.3); }
#touch .button.small { width: 44px; height: 36px; border-radius: 10px; font-size: 11px; }
#touch .button.down { background: rgba(255,255,255,0.35); color: #000; }
#touch .button.fire { width: 84px; height: 84px; font-size: 14px; }
#touch [data-at="fire"] { right: calc(28px + env(safe-area-inset-right)); bottom: 36px; }
#touch [data-at="a1"] { right: calc(128px + env(safe-area-inset-right)); bottom: 22px; }
#touch [data-at="a2"] { right: calc(196px + env(safe-area-inset-right)); bottom: 22px; }
#touch [data-at="a3"] { right: calc(128px + env(safe-area-inset-right)); bottom: 92px; }
#touch [data-at="a4"] { right: calc(40px + env(safe-area-inset-right)); bottom: 136px; }
#touch [data-at="a5"] { right: calc(108px + env(safe-area-inset-right)); bottom: 162px; }
#touch [data-at="a6"] { right: calc(196px + env(safe-area-inset-right)); bottom: 92px; }
#touch [data-at^="t"] { top: calc(12px + env(safe-area-inset-top)); }
#touch [data-at="t1"] { right: calc(12px + env(safe-area-inset-right)); }
#touch [data-at="t2"] { right: calc(64px + env(safe-area-inset-right)); }
#touch [data-at="t3"] { right: calc(116px + env(safe-area-inset-right)); }
#touch [data-at="t4"] { right: calc(168px + env(safe-area-inset-right)); }
#touch [data-at="t5"] { right: calc(220px + env(safe-area-inset-right)); }
#touch .turn { position: absolute; inset: 0; display: none; align-items: center; justify-content: center; text-align: center;
  padding: 24px; background: rgba(0,0,0,0.85); font-size: 16px; pointer-events: auto; }
@media (orientation: portrait) { #touch .turn { display: flex; } }
`;

let root = null;
let play = null;
let canvas = null;
let push = null;
let position = null;
let locked = false;
let listening = null;
/** What each touch on the screen is doing, by pointer id: { kind, x, y, ... }. */
const touches = new Map();
/** The stick's keys held down now. */
const held = new Set();

/**
 * Starts listening for touches, and builds the controls over the canvas the first time one comes.
 * @param {HTMLCanvasElement} target The canvas the game draws to; the controls are laid over its parent.
 * @param {(line: string) => void} enqueue Queues one event line for the game (platform.js).
 * @param {(e: PointerEvent) => [string, string]} pointerPosition A touch's position in the canvas's pixels.
 */
export function install(target, enqueue, pointerPosition) {
  canvas = target;
  push = enqueue;
  position = pointerPosition;
  listening = new AbortController();
  const signal = listening.signal;

  // The canvas itself: in the menus a touch is the cursor, in play a drag turns the view. A mouse on the
  // same device still reaches the game through platform.js, and hides the controls again.
  canvas.addEventListener('pointerdown', e => {
    if (e.pointerType !== 'touch') {
      if (root) root.hidden = true;
      return;
    }
    e.preventDefault(); // No mouse events after it, which would click a second time.
    build();
    canvas.setPointerCapture?.(e.pointerId);
    const [x, y] = position(e);
    if (locked) {
      touches.set(e.pointerId, { kind: 'look', x: e.clientX, y: e.clientY });
    } else {
      touches.set(e.pointerId, { kind: 'cursor', x: e.clientX, y: e.clientY });
      push(`move 0 0 ${x} ${y}`);
      push('button 0 1');
    }
  }, { signal });
  canvas.addEventListener('pointermove', e => moved(e), { signal });
  canvas.addEventListener('pointerup', e => ended(e), { signal });
  canvas.addEventListener('pointercancel', e => ended(e), { signal });
  window.addEventListener('blur', releaseAll, { signal });
}

/** Stops listening and takes the controls away. */
export function uninstall() {
  releaseAll();
  listening?.abort();
  listening = null;
  root?.remove();
  root = null;
  play = null;
}

/** Whether the game has asked for the pointer to be locked: in play, rather than in a menu. */
export function setLocked(value) {
  if (locked === value) return;
  locked = value;
  releaseAll();
  if (play) play.hidden = !locked;
}

function moved(e) {
  const touch = touches.get(e.pointerId);
  if (!touch) return;
  e.preventDefault();
  const dx = e.clientX - touch.x;
  const dy = e.clientY - touch.y;
  const [x, y] = position(e);
  if (touch.kind === 'stick') {
    steer(touch, e);
    return;
  }
  touch.x = e.clientX;
  touch.y = e.clientY;
  if (touch.kind === 'cursor') push(`move ${dx} ${dy} ${x} ${y}`);
  else if (touch.kind === 'look' || touch.look) push(`move ${(dx * LOOK).toFixed(2)} ${(dy * LOOK).toFixed(2)} ${x} ${y}`);
}

function ended(e) {
  const touch = touches.get(e.pointerId);
  if (!touch) return;
  touches.delete(e.pointerId);
  if (touch.kind === 'cursor') push('button 0 0');
  if (touch.kind === 'stick') {
    steerTo(0, 0);
    touch.stick.hidden = true;
  }
  if (touch.kind === 'button') {
    push(`${touch.press} 0`);
    touch.element.classList.remove('down');
  }
}

/** Lets go of everything held: a phone call, the page hidden, the game switching between menu and play. */
function releaseAll() {
  for (const id of [...touches.keys()]) ended({ pointerId: id });
  steerTo(0, 0);
}

function steer(touch, e) {
  let dx = e.clientX - touch.originX;
  let dy = e.clientY - touch.originY;
  const length = Math.hypot(dx, dy);
  if (length > STICK_RADIUS) {
    dx *= STICK_RADIUS / length;
    dy *= STICK_RADIUS / length;
  }
  touch.knob.style.transform = `translate(${dx}px, ${dy}px)`;
  steerTo(dx / STICK_RADIUS, dy / STICK_RADIUS);
}

/** Holds down the keys the stick points at, and lets go of the rest. */
function steerTo(x, y) {
  const want = new Set();
  if (y < -STICK_DEAD) want.add('KeyW');
  if (y > STICK_DEAD) want.add('KeyS');
  if (x < -STICK_DEAD) want.add('KeyA');
  if (x > STICK_DEAD) want.add('KeyD');
  for (const code of held) if (!want.has(code)) push?.(`key ${code} 0`);
  for (const code of want) if (!held.has(code)) push?.(`key ${code} 1`);
  held.clear();
  for (const code of want) held.add(code);
}

/** The controls, laid over the canvas once the first touch shows there is a touch screen. */
function build() {
  if (root) {
    root.hidden = false;
    return;
  }

  const style = document.createElement('style');
  style.textContent = STYLE;
  document.head.append(style);

  root = document.createElement('div');
  root.id = 'touch';
  play = document.createElement('div');
  play.hidden = !locked;
  root.append(play);

  const zone = document.createElement('div');
  zone.className = 'stick-zone';
  const stick = document.createElement('div');
  stick.className = 'stick';
  stick.hidden = true;
  const knob = document.createElement('div');
  knob.className = 'knob';
  stick.append(knob);
  zone.append(stick);
  play.append(zone);

  // The stick appears where the thumb lands, so it is always under it.
  zone.addEventListener('pointerdown', e => {
    e.preventDefault();
    zone.setPointerCapture?.(e.pointerId);
    const box = zone.getBoundingClientRect();
    stick.style.left = `${e.clientX - box.left}px`;
    stick.style.top = `${e.clientY - box.top}px`;
    stick.hidden = false;
    knob.style.transform = '';
    touches.set(e.pointerId, { kind: 'stick', originX: e.clientX, originY: e.clientY, stick, knob });
  });
  zone.addEventListener('pointermove', moved);
  zone.addEventListener('pointerup', ended);
  zone.addEventListener('pointercancel', ended);

  for (const spec of BUTTONS) {
    const button = document.createElement('div');
    button.className = `button ${spec.id}${spec.small ? ' small' : ''}`;
    button.dataset.at = spec.at;
    button.textContent = spec.label;
    button.addEventListener('pointerdown', e => {
      e.preventDefault();
      button.setPointerCapture?.(e.pointerId);
      button.classList.add('down');
      push(`${spec.press} 1`);
      // Fire turns the view as it is dragged, so a thumb can aim and shoot at once.
      touches.set(e.pointerId, { kind: 'button', press: spec.press, element: button, look: spec.look, x: e.clientX, y: e.clientY });
    });
    button.addEventListener('pointermove', moved);
    button.addEventListener('pointerup', ended);
    button.addEventListener('pointercancel', ended);
    button.addEventListener('contextmenu', e => e.preventDefault());
    play.append(button);
  }

  const turn = document.createElement('div');
  turn.className = 'turn';
  turn.textContent = 'Turn your phone sideways to play.';
  root.append(turn);

  canvas.parentElement.append(root);
}
