// A phone's gyroscope, handed to the game as how far the screen turned since its last frame: 'gyro 0.0123 -0.0040', right and
// up in radians. Worked out as the game takes its events (take), not as readings or the screen's refreshes come: the game's
// frames keep their own time, and a turn counted on any other clock gave one frame two counts and the next none -- a view
// that lurched now and then however smooth the hand.
//
// What the turn does to the view -- whether it aims at all, how far, which way is up -- is the game's (GameHost.TouchControls),
// as the touch controls are. All the page does is read the sensor, put its turn in the screen's terms, and ask leave where a
// browser wants it asked.

// Around this the sensor is resting rather than turning: a phone held still drifts the view otherwise. Degrees a second. Not a
// hard cut -- a turn below it is scaled down towards nothing, so a slow turn starting or stopping fades rather than jumps.
const REST = 1.5;

// The fastest turn believed, degrees a second: a reading past it is a knock, not an aim.
const FASTEST = 1500;

// How quickly the turning speed follows the readings, in seconds: long enough to even out a phone's uneven reports, short
// enough that the view does not lag the hand.
const FOLLOW = 0.025;

// Readings stop arriving when the page is hidden or the sensor hiccups; after this long without one the turn is taken as
// stopped, rather than carried on at the last speed. Seconds.
const STALE = 0.12;

// The longest frame turned by: past it the page was in the background. Seconds.
const LONGEST = 0.1;

let canvas = null;
let wanted = false;
let listening = null;
let asking = null;
let lastTime = 0;
let lastArrival = 0;
let up = null;
let lastTake = 0;

// The screen's turning speed, right and up, degrees a second: what each frame turns the view by, for as long as the frame is.
let speedRight = 0;
let speedUp = 0;

const DEGREES = Math.PI / 180;

/** The screen's rotation from the device's own upright, in radians. */
function screenAngle() {
  const angle = screen.orientation?.angle ?? window.orientation ?? 0;
  return angle * DEGREES;
}

// The device's axes are its own -- x to its right, y to its top, z out of the screen -- whichever way it is held; the
// screen's are the picture's. Turned by the screen's angle, a phone held on its side either way reads the same.
function toScreen(x, y, z) {
  const angle = screenAngle();
  const c = Math.cos(angle), s = Math.sin(angle);
  return [x * c - y * s, x * s + y * c, z];
}

// Which of the rate's three numbers turns about which of the device's axes. The specification once had beta about x, gamma
// about y and alpha about z; Safari and Chrome both report alpha, beta, gamma about x, y, z, which the documentation now
// gives too. Rather than trust either, the page watches which order explains how gravity moved across the device: a vector
// fixed in the world, seen from a device turning at w, changes by -w x g. The order whose prediction misses least is used.
const ORDERS = [
  ['spec', r => [r.beta ?? 0, r.gamma ?? 0, r.alpha ?? 0]],
  ['xyz', r => [r.alpha ?? 0, r.beta ?? 0, r.gamma ?? 0]],
];
// The browsers' order to begin with. Starting from the likely one, the view turns the right way from the first reading, and
// the watching only overrides it when the motion clearly says otherwise. Starting an Android phone on the specification's
// order turned its tilt into a roll the view ignores: up and down did nothing.
const LIKELY = 1;
let misses = [0, 0];
let order = LIKELY;
let telling = 0;
let settled = false;

// Readings of a real turn before the order is decided.
const SETTLE = 40;
let lastGravity = null;
let lastRate = null;

// Below this the phone is barely turning, and every order predicts the same; degrees a second.
const TELLING = 25;

function learnOrder(rate, gravity, dt) {
  if (lastGravity && lastRate && dt > 0) {
    for (let i = 0; i < ORDERS.length; i++) {
      const [wx, wy, wz] = ORDERS[i][1](lastRate).map(v => v * DEGREES);
      if (Math.hypot(wx, wy, wz) < TELLING * DEGREES) continue;
      const [gx, gy, gz] = lastGravity;
      // -w x g, the change a fixed vector shows to a turning device.
      const predicted = [gx - (wy * gz - wz * gy) * dt, gy - (wz * gx - wx * gz) * dt, gz - (wx * gy - wy * gx) * dt];
      misses[i] += Math.hypot(predicted[0] - gravity[0], predicted[1] - gravity[1], predicted[2] - gravity[2]);
    }

    // Settled once, after enough turning, when one order clearly explains it better; then kept, since a change of mind in the
    // middle of play would swing the view. A draw keeps the specification's.
    if (!settled && ++telling >= SETTLE) {
      if (misses[1] < misses[0] * 0.6) order = 1;
      else if (misses[0] < misses[1] * 0.6) order = 0;
      settled = misses[1] < misses[0] * 0.6 || misses[0] < misses[1] * 0.6;
    }
  }

  lastGravity = gravity;
  lastRate = rate;
}

let debug = null;

function show(lines) {
  if (!debug) {
    debug = document.createElement('pre');
    debug.style.cssText = 'position:fixed;left:8px;top:8px;z-index:9;margin:0;padding:6px 8px;background:rgba(0,0,0,.7);'
      + 'color:#7fc28c;font:11px/1.3 ui-monospace,monospace;pointer-events:none';
    document.body.append(debug);
  }
  debug.textContent = lines.join('\n');
}

const debugging = new URLSearchParams(location.search).has('gyro-debug');

function motion(e) {
  const rate = e.rotationRate;
  if (!rate) return;

  // The reading's own time, which the sensor stamped, rather than when the page got round to it.
  const now = e.timeStamp / 1000;
  const dt = lastTime ? Math.min(Math.max(now - lastTime, 0), STALE) : 0;
  lastTime = now;
  lastArrival = performance.now() / 1000;

  // Which way is up, from gravity, smoothed so a jolt does not swing it. The sign of what browsers report differs between
  // them; the game is played with the screen upright, so up is whichever way has the screen's top in it.
  const g = e.accelerationIncludingGravity;
  if (g && g.x !== null) {
    const length = Math.hypot(g.x, g.y, g.z);
    if (length > 0.1) {
      learnOrder({ alpha: rate.alpha, beta: rate.beta, gamma: rate.gamma }, [g.x / length, g.y / length, g.z / length], dt);
      let [gx, gy, gz] = toScreen(g.x / length, g.y / length, g.z / length);
      if (gy < 0) { gx = -gx; gy = -gy; gz = -gz; }
      const fresh = [gx, gy, gz];
      up = up ? up.map((v, i) => v + (fresh[i] - v) * 0.1) : fresh;
    }
  }

  // The turn about each of the screen's axes, degrees a second.
  const [dx, dy, dz] = ORDERS[order][1](rate);
  const [wx, wy, wz] = toScreen(dx, dy, dz);

  // Yaw about the world's up rather than the screen's, so turning on the spot and rolling the phone both steer, however far
  // back it is tilted. Turning right is a turn the wrong way round up.
  const u = up ?? [0, 1, 0];
  const norm = Math.hypot(u[0], u[1], u[2]) || 1;
  const right = settle(-(wx * u[0] + wy * u[1] + wz * u[2]) / norm);
  const lift = settle(wx);

  // Followed rather than taken: the readings come on the sensor's clock, unevenly, and each of the game's frames turns by the
  // speed for as long as the frame lasted, so the view moves on the game's clock instead.
  const follow = dt > 0 ? 1 - Math.exp(-dt / FOLLOW) : 1;
  speedRight += (right - speedRight) * follow;
  speedUp += (lift - speedUp) * follow;

  if (debugging) {
    const f = v => (v ?? 0).toFixed(1).padStart(7);
    show([
      `order ${ORDERS[order][0]}${settled ? ' (settled)' : ''}  misses ${misses.map(m => m.toFixed(1)).join(' / ')}`,
      `screen ${(screenAngle() / DEGREES).toFixed(0)}deg`,
      `rate a${f(rate.alpha)} b${f(rate.beta)} g${f(rate.gamma)}`,
      `screen w x${f(wx)} y${f(wy)} z${f(wz)}`,
      `up ${u.map(v => v.toFixed(2)).join(' ')}`,
      `right ${f(right)}  up ${f(lift)}  deg/s`,
    ]);
  }
}

/** A speed with the resting sensor's noise faded out of it and a knock's spike capped. */
function settle(speed) {
  const size = Math.abs(speed);
  if (size > FASTEST) return Math.sign(speed) * FASTEST;
  return size < REST * 2 ? speed * Math.max(0, (size - REST) / REST) : speed;
}

/**
 * How far the screen turned since the game last asked: the speed for as long as the game's frame took, as an event line.
 * @returns {string | null} The line, or null when nothing turned.
 */
export function take() {
  if (!listening) return null;
  const now = performance.now() / 1000;
  const dt = lastTake ? Math.min(Math.max(now - lastTake, 0), LONGEST) : 0;
  lastTake = now;

  if (lastArrival && now - lastArrival > STALE) {
    speedRight = 0;
    speedUp = 0;
  }

  const yaw = speedRight * DEGREES * dt;
  const pitch = speedUp * DEGREES * dt;
  return yaw !== 0 || pitch !== 0 ? `gyro ${yaw.toFixed(6)} ${pitch.toFixed(6)}` : null;
}

function listen() {
  if (listening) return;
  listening = new AbortController();
  lastTime = 0;
  up = null;
  lastTake = 0;
  speedRight = 0;
  speedUp = 0;
  if (!settled) {
    misses = [0, 0];
    telling = 0;
  }
  lastGravity = null;
  lastRate = null;
  window.addEventListener('devicemotion', motion, { signal: listening.signal });
}

function stop() {
  listening?.abort();
  listening = null;
  asking?.abort();
  asking = null;
  debug?.remove();
  debug = null;
  speedRight = 0;
  speedUp = 0;
}

// Safari asks the player's leave before it reports motion, and only from inside a tap: the game's settings are on the
// canvas, and the game reads the tap a frame after it, outside it. So the leave is asked on the next finger lifted.
function askThenListen() {
  if (typeof DeviceMotionEvent === 'undefined' || typeof DeviceMotionEvent.requestPermission !== 'function') {
    listen();
    return;
  }

  if (asking) return;
  asking = new AbortController();
  const ask = () => {
    asking?.abort();
    asking = null;
    DeviceMotionEvent.requestPermission()
      .then(state => { if (state === 'granted' && wanted) listen(); })
      .catch(() => {});
  };
  canvas.addEventListener('touchend', ask, { signal: asking.signal });
}

/**
 * Starts the gyroscope's half of the page; nothing is read until the game asks for it.
 * @param {HTMLCanvasElement} target The canvas the game draws to.
 */
export function install(target) {
  canvas = target;
}

/** Stops reading the gyroscope. */
export function uninstall() {
  wanted = false;
  stop();
}

/** Whether the game wants the device's turning. */
export function setWanted(value) {
  wanted = value;
  if (!wanted) stop();
  else if (!listening && canvas) askThenListen();
}
