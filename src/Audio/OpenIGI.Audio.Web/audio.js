// The page's half of the browser's audio device: a Web Audio graph the game drives by number.
//
// The device (WebAudioDevice, C#) keeps the model -- which sound is on which voice, how loud, where --
// and tells the page what each voice slot should do. The page keeps the objects: the context, a gain
// node per bus, a chain per voice slot and the decoded buffers. A voice is its slot index and a buffer
// is the id this file gave it; nothing else crosses.
//
//   source ─► stereo panner ─┐
//                            ├─► voice gain ─► bus gain ─► master ─► destination
//   source ─► 3D panner ─────┘
//
// Every voice has both panners wired in permanently and a source connects to one of them: the stereo
// panner for the original's flat pan, the 3D panner for a placed sound. A resident sound is one source
// node; a stream is a run of them, each scheduled to start the instant the one before it ends.

const BUS_COUNT = 4;

// Web Audio approaches a new gain over a time constant rather than stepping to it, which is what keeps
// a per-frame level change from clicking. Five milliseconds is inaudible as a delay.
const GAIN_SMOOTHING_SECONDS = 0.005;

// A browser will not make sound before the person has clicked, tapped or typed on the page. Any of these
// is enough, and the game needs one before anything happens anyway.
const GESTURES = ['keydown', 'pointerdown', 'mousedown', 'touchend'];

let context = null;
let master = null;
let analyser = null;
let buses = [];
let voices = [];
let buffers = [null];
let freeBufferIds = [];

function onGesture() {
  if (context && context.state !== 'running') context.resume().catch(() => {});
}

/**
 * Drops what would already have ended, once the context starts.
 *
 * A context that is not running holds every source it is given and lets them all go the instant it
 * starts, so the clicks of a menu used before the first gesture would arrive as one burst. A one-shot
 * older than its own length is dropped here; a loop or a stream -- the theme -- is kept and plays from
 * its start, and a one-shot still within its length plays late rather than not at all.
 */
function onStateChange() {
  if (context === null || context.state !== 'running') return;
  const now = performance.now();
  for (const voice of voices) {
    for (const source of [...voice.sources]) {
      if (source.heldSince === undefined) continue;
      const age = (now - source.heldSince) / 1000;
      source.heldSince = undefined;
      if (voice.stream === null && !source.loop && age > source.buffer.duration / voice.rate) {
        source.onended?.();
        try {
          source.stop();
        } catch {
          // Already over.
        }
      }
    }
  }
}

function armGestures() {
  for (const name of GESTURES) window.addEventListener(name, onGesture, { capture: true, passive: true });
}

function disarmGestures() {
  for (const name of GESTURES) window.removeEventListener(name, onGesture, { capture: true });
}

function makeVoice() {
  const gain = context.createGain();
  const stereo = context.createStereoPanner();
  const panner = context.createPanner();
  panner.panningModel = 'equalpower';
  panner.distanceModel = 'linear';
  panner.rolloffFactor = 1;
  stereo.connect(gain);
  panner.connect(gain);
  return { gain, stereo, panner, input: stereo, bus: -1, rate: 1, sources: [], stream: null, nextTime: 0, ended: true };
}

function setParam(param, value) {
  // A panner's or listener's position is an AudioParam in every current browser; the setter methods are
  // the older API that Safari kept longest.
  //
  // Only a change is written. Each write is an event on the parameter's automation timeline, which only
  // the running audio thread clears -- so the listener, set every frame, grew a timeline without end
  // while the context waited for a gesture, and the writes alone came to a tenth of the frame.
  if (param === undefined || param.openigiValue === value) return;
  param.openigiValue = value;
  param.value = value;
}

/** Decodes interleaved PCM -- 8-bit unsigned or 16-bit signed -- into an AudioBuffer, or null. */
function decode(sampleRate, channels, bits, bytes) {
  const frames = Math.floor(bytes.byteLength / (channels * (bits >> 3)));
  if (frames === 0) return null;
  let buffer;
  try {
    buffer = context.createBuffer(channels, frames, sampleRate);
  } catch {
    return null;
  }
  if (bits === 16) {
    // slice() gave a fresh buffer at offset zero, so the 16-bit view is aligned.
    const samples = new Int16Array(bytes.buffer, bytes.byteOffset, frames * channels);
    for (let c = 0; c < channels; c++) {
      const out = buffer.getChannelData(c);
      for (let i = 0, s = c; i < frames; i++, s += channels) out[i] = samples[s] / 32768;
    }
  } else {
    for (let c = 0; c < channels; c++) {
      const out = buffer.getChannelData(c);
      for (let i = 0, s = c; i < frames; i++, s += channels) out[i] = (bytes[s] - 128) / 128;
    }
  }
  return buffer;
}

function stopSources(voice) {
  for (const source of voice.sources) {
    source.onended = null;
    try {
      source.stop();
    } catch {
      // Never started, which cannot happen here; harmless if it did.
    }
    source.disconnect();
  }
  voice.sources = [];
}

function makeSource(voice, buffer) {
  const source = context.createBufferSource();
  source.buffer = buffer;
  source.playbackRate.value = voice.rate;
  source.connect(voice.input);
  source.onended = () => {
    source.onended = null;
    const at = voice.sources.indexOf(source);
    if (at >= 0) voice.sources.splice(at, 1);
    source.disconnect();
    if (voice.sources.length === 0 && voice.stream === null) voice.ended = true;
  };
  if (context.state !== 'running') source.heldSince = performance.now();
  voice.sources.push(source);
  return source;
}

/** What the runtime imports, registered under 'openigi-audio'. */
export const imports = {
  /**
   * Opens the context and makes the voice chains.
   * @param {number} sampleRate The output rate to ask for, or 0 for the browser's own.
   * @param {number} voiceCount How many voice slots to make.
   * @returns {number} The rate the context runs at, or 0 when the browser has no audio.
   */
  open: (sampleRate, voiceCount) => {
    imports.close();
    const Context = window.AudioContext ?? window.webkitAudioContext;
    if (!Context) return 0;
    try {
      context = sampleRate > 0 ? new Context({ sampleRate }) : new Context();
    } catch {
      // A rate the browser will not run at; take its own.
      try {
        context = new Context();
      } catch {
        context = null;
        return 0;
      }
    }

    master = context.createGain();
    master.connect(context.destination);

    // A meter on the mix, for anyone checking from outside that sound is actually leaving.
    analyser = context.createAnalyser();
    analyser.fftSize = 256;
    master.connect(analyser);

    buses = [];
    for (let i = 0; i < BUS_COUNT; i++) {
      const bus = context.createGain();
      bus.connect(master);
      buses.push(bus);
    }

    voices = [];
    for (let i = 0; i < voiceCount; i++) voices.push(makeVoice());

    armGestures();
    context.addEventListener('statechange', onStateChange);
    context.resume().catch(() => {});
    return context.sampleRate;
  },

  /** Closes the context and drops everything the page holds. */
  close: () => {
    if (context === null) return;
    disarmGestures();
    for (const voice of voices) stopSources(voice);
    const closing = context;
    context = null;
    master = null;
    analyser = null;
    buses = [];
    voices = [];
    buffers = [null];
    freeBufferIds = [];
    closing.close().catch(() => {});
  },

  setListener: (x, y, z, forwardX, forwardY, forwardZ, upX, upY, upZ) => {
    // Nothing is heard before the context runs, and the game sets the listener again every frame, so the
    // first frame after the gesture places it.
    if (context === null || context.state !== 'running') return;
    const listener = context.listener;
    if (listener.positionX !== undefined) {
      setParam(listener.positionX, x);
      setParam(listener.positionY, y);
      setParam(listener.positionZ, z);
      setParam(listener.forwardX, forwardX);
      setParam(listener.forwardY, forwardY);
      setParam(listener.forwardZ, forwardZ);
      setParam(listener.upX, upX);
      setParam(listener.upY, upY);
      setParam(listener.upZ, upZ);
    } else {
      listener.setPosition(x, y, z);
      listener.setOrientation(forwardX, forwardY, forwardZ, upX, upY, upZ);
    }
  },

  setBusGain: (category, gain) => {
    const bus = buses[category];
    if (bus) bus.gain.setTargetAtTime(gain, context.currentTime, GAIN_SMOOTHING_SECONDS);
  },

  /**
   * Decodes PCM into a buffer the page keeps.
   * @param {number} sampleRate The PCM's rate.
   * @param {number} channels One or two.
   * @param {number} bits Eight (unsigned) or sixteen (signed).
   * @param {{ slice: () => Uint8Array }} view The samples, a view of wasm memory valid during the call.
   * @returns {number} The buffer's id, or 0 when none could be made.
   */
  createBuffer: (sampleRate, channels, bits, view) => {
    if (context === null) return 0;
    const buffer = decode(sampleRate, channels, bits, view.slice());
    if (buffer === null) return 0;
    const id = freeBufferIds.pop() ?? buffers.length;
    buffers[id] = buffer;
    return id;
  },

  destroyBuffer: id => {
    if (id <= 0 || id >= buffers.length || buffers[id] === null) return;
    buffers[id] = null;
    freeBufferIds.push(id);
  },

  setVoice: (index, category, gain, rate, positional, pan, x, y, z, minimumDistance, maximumDistance) => {
    const voice = voices[index];
    if (!voice) return;
    const now = context.currentTime;

    if (voice.bus !== category) {
      voice.gain.disconnect();
      voice.gain.connect(buses[category]);
      voice.bus = category;
    }

    const level = voice.gain.gain;
    if (voice.sources.length === 0) {
      // Nothing is sounding, so the level can step to where the next sound starts.
      level.cancelScheduledValues(now);
      level.value = gain;
    } else {
      level.setTargetAtTime(gain, now, GAIN_SMOOTHING_SECONDS);
    }

    voice.rate = rate;
    for (const source of voice.sources) source.playbackRate.value = rate;

    const input = positional ? voice.panner : voice.stereo;
    if (input !== voice.input) {
      for (const source of voice.sources) {
        source.disconnect();
        source.connect(input);
      }
      voice.input = input;
    }

    if (positional) {
      const panner = voice.panner;
      if (panner.positionX !== undefined) {
        setParam(panner.positionX, x);
        setParam(panner.positionY, y);
        setParam(panner.positionZ, z);
      } else {
        panner.setPosition(x, y, z);
      }
      // The linear model divides by (max - reference), so the two must never be equal.
      const reference = Math.max(0.0001, minimumDistance);
      panner.refDistance = reference;
      panner.maxDistance = Math.max(maximumDistance, reference * 1.0001);
    } else {
      voice.stereo.pan.value = Math.max(-1, Math.min(1, pan));
    }
  },

  /** Starts a resident buffer on a voice. Before the first gesture it is held; see onStateChange. */
  play: (index, bufferId, looping) => {
    const voice = voices[index];
    if (!voice) return;
    stopSources(voice);
    voice.stream = null;
    const buffer = buffers[bufferId];
    if (!buffer) {
      voice.ended = true;
      return;
    }
    voice.ended = false;
    const source = makeSource(voice, buffer);
    source.loop = looping;
    source.start();
  },

  startStream: (index, sampleRate, channels) => {
    const voice = voices[index];
    if (!voice) return;
    stopSources(voice);
    voice.stream = { sampleRate, channels };
    voice.nextTime = 0;
    voice.ended = false;
  },

  /**
   * Schedules one chunk of a stream to start the instant the one before it ends.
   * @param {number} index The voice slot.
   * @param {{ slice: () => Uint8Array }} view Sixteen-bit interleaved samples, valid during the call.
   */
  queueStream: (index, view) => {
    const voice = voices[index];
    if (!voice || voice.stream === null) return;
    const buffer = decode(voice.stream.sampleRate, voice.stream.channels, 16, view.slice());
    if (buffer === null) return;

    // A queue that ran dry -- a long frame -- starts again from now. That is a click, where letting the
    // chunks keep their old times would be the music missing a beat and then playing everything late.
    const now = context.currentTime;
    if (voice.nextTime < now) voice.nextTime = now;

    const source = makeSource(voice, buffer);
    source.start(voice.nextTime);
    voice.nextTime += buffer.duration / voice.rate;
  },

  queuedStreamChunks: index => {
    const voice = voices[index];
    return voice ? voice.sources.length : 0;
  },

  hasEnded: index => {
    const voice = voices[index];
    return voice ? voice.ended : true;
  },

  stop: index => {
    const voice = voices[index];
    if (!voice) return;
    stopSources(voice);
    voice.stream = null;
    voice.ended = true;
  },
};

/**
 * What is going on, for a check from outside: the context's state and rate, how many voices sound,
 * and the level leaving the mix (a root mean square over the last few milliseconds; zero is silence).
 */
export function diagnostics() {
  if (context === null) return { state: 'closed', sampleRate: 0, sounding: 0, level: 0 };
  const samples = new Float32Array(analyser.fftSize);
  analyser.getFloatTimeDomainData(samples);
  let sum = 0;
  for (const sample of samples) sum += sample * sample;
  return {
    state: context.state,
    sampleRate: context.sampleRate,
    sounding: voices.reduce((n, voice) => n + voice.sources.length, 0),
    level: Math.sqrt(sum / samples.length),
    voices: voices.filter(voice => voice.sources.length > 0).map(voice => ({
      bus: voice.bus,
      gain: Number(voice.gain.gain.value.toFixed(3)),
      loop: voice.sources[0].loop,
      stream: voice.stream !== null,
      queued: voice.sources.length,
      held: voice.sources.filter(source => source.heldSince !== undefined).length,
    })),
  };
}
