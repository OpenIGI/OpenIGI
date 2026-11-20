// Starts the game in the page: WebGPU first, then the installation, then the runtime, then a frame per
// animation frame -- and gives the page back when the game ends.
//
// The order is forced by the browser. The adapter and the device are promises, and nothing inside the
// runtime can wait on one, so they are acquired here and handed to the WebGPU bridge before the game
// asks for them. The installation is read before Boot because the host reads its files by path from the
// first frame.

import { dotnet } from './_framework/dotnet.js';
import './wgpu-bridge.js';
import * as platform from './platform.js';
import * as rtc from './rtc.js';
import * as identity from './identity.js';
import * as audio from './audio.js';
import * as files from './game-files.js';
import * as userFiles from './user-files.js';

const GAME_ROOT = '/game';

// Every file the page loads is listed for the service worker once the game is up (keepShell), so the list must not stop at
// the browser's default of 250 entries: the runtime alone is more, and a short list would let the worker drop files the page
// still needs.
performance.setResourceTimingBufferSize?.(10000);

const $ = id => document.getElementById(id);
const canvas = $('view');
const landing = $('landing');
const play = $('play');
const change = $('change');

const query = new URLSearchParams(location.search);
const args = (query.get('args') ?? '').split(' ').filter(Boolean);
// A friend's link: ?join=CODE plays in their room, ?host opens one, ?party=CODE joins their party (which then follows its
// leader from room to room); ?name= is who you play as.
if (query.get('join')) args.push('-join', query.get('join'));
const party = (query.get('party') ?? '').replace(/[^A-Za-z0-9]/g, '');
if (party) args.push('+party_join', party);
if (query.has('host')) args.push('-host');
if (query.get('name')) args.push('-name', query.get('name'));

// --- Which game ----------------------------------------------------------------------------------------
//
// The browser card plays either game: ?game=igi2 links to one, and the switch's last choice is kept for the next
// visit. A Discord Activity plays the first game unless its link says otherwise -- multiplayer is that game's.

const GAME_KEY = 'openigi-game';
const IN_DISCORD = query.has('frame_id') && query.has('instance_id');

// The page kept for a visit with no network (sw.js): not inside Discord's frame, which reaches everything through its proxy,
// not for an automated run's served folder (?data=), and not when asked not to (?sw=0).
const OFFLINE_SHELL = 'serviceWorker' in navigator && window.isSecureContext && !IN_DISCORD
  && !query.has('data') && query.get('sw') !== '0';
if (OFFLINE_SHELL) {
  navigator.serviceWorker.register('sw.js').catch(error => console.warn(`The page can't be kept for offline play: ${error.message}`));
}

/** Tells the service worker every file of the page's the game loaded, so it keeps them all and lets older ones go. */
function keepShell() {
  if (!OFFLINE_SHELL) return;
  navigator.serviceWorker.ready.then(registration => {
    // The game's own files are not the page's: game-files.js keeps those, and a data host on the page's own address (a local
    // test server's /data/) would otherwise have them copied here a second time.
    const data = hostedData();
    const urls = performance.getEntriesByType('resource').map(entry => entry.name)
      .filter(name => name.startsWith(registration.scope) && !name.startsWith(data) && !name.startsWith(`${registration.scope}data/`));
    registration.active?.postMessage({ type: 'keep', urls });
  }).catch(() => {});
}

function savedGame() {
  try {
    return localStorage.getItem(GAME_KEY);
  } catch {
    return null; // Storage blocked: the default.
  }
}

let game = [query.get('game'), IN_DISCORD ? null : savedGame()].find(id => id in files.TITLES) ?? 'igi1';

// Sent here by the game's own switcher: played at once. Taken off the address, so reloading by hand later lands on
// the page as usual.
const SWITCHED = query.has('play');
if (SWITCHED) {
  const address = new URL(location.href);
  address.searchParams.delete('play');
  history.replaceState(null, '', address.href);
}

/** Shows a game as the chosen one, and keeps the choice. */
function showGame(id) {
  game = id;
  $('game-name').textContent = files.TITLES[id].name;
  for (const button of $('games').querySelectorAll('button')) button.setAttribute('aria-pressed', String(button.dataset.game === id));
  $('files-point').textContent = files.TITLES[id].hosted
    ? "Nothing to install: the game's files download once and are kept for the next visit."
    : 'Plays from your own copy of the game: choose its folder once and it is remembered.';
  try {
    localStorage.setItem(GAME_KEY, id);
  } catch {
    // Not kept; chosen again next visit.
  }
}
showGame(game);

const hint = () => `The folder ${files.TITLES[game].name} is installed in${game === 'igi1' ? ' (usually the pc folder inside it)' : ''}.`;

// --- Inside a Discord Activity --------------------------------------------------------------------------
//
// Discord opens the page in its own frame with ?frame_id and ?instance_id. There the page reaches other sites only through
// Discord's proxy, by the paths the app's URL mappings name (/master, /data), and WebRTC is not allowed: the SDK rewrites
// every request to the master and the game's files onto those paths, and the voice channel's players share one room that
// carries the game itself (rooms.js), its code made from the Activity's instance -- the first one in hosts it, the rest
// join it.

const DISCORD_APP = '1534229764519497768';

/** The Discord SDK once the Activity is set up: the frame opens another site only through it. */
let discordSdk = null;

$('elsewhere').addEventListener('click', event => {
  if (!discordSdk) return;
  event.preventDefault();
  discordSdk.commands.openExternalLink({ url: event.currentTarget.href }).catch(error => console.warn('Discord link', error));
});

async function enterDiscord() {
  const { DiscordSDK, patchUrlMappings } = await import('./discord-sdk.js');
  patchUrlMappings([
    { prefix: '/master', target: 'master.openigi.com' },
    { prefix: '/data', target: 'data.openigi.com' },
  ]);
  const sdk = new DiscordSDK(DISCORD_APP);
  await sdk.ready();
  discordSdk = sdk;
  const instance = sdk.instanceId;

  // Who is playing: Discord asks once to share the player's name, then never again. They play under it; without it, as
  // the game's own name.
  try {
    const { code } = await sdk.commands.authorize({ client_id: DISCORD_APP, response_type: 'code', state: '', prompt: 'none', scope: ['identify'] });
    // Signed with this browser's player key, so the same sign-in links the game here to the account.
    const body = JSON.stringify({ code });
    const traded = await fetch('https://master.openigi.com/v1/auth/discord/activity', {
      method: 'POST',
      headers: { 'content-type': 'application/json', ...await signedHeaders('POST', '/v1/auth/discord/activity', body) },
      body,
    });
    if (traded.ok) {
      const { access_token: accessToken, account } = await traded.json();
      await sdk.commands.authenticate({ access_token: accessToken });
      if (!args.includes('-name') && account?.name) args.push('-name', account.name);
    }
  } catch (error) {
    console.warn('Discord sign-in', error);
  }

  const reply = await fetch(`https://master.openigi.com/v1/rooms/instance/${encodeURIComponent(instance)}`);
  const room = reply.ok ? await reply.json() : null;
  if (args.includes('-join') || args.includes('-host')) return;

  // Someone in the voice channel already hosts: straight into their game. Otherwise the menu, where co-op hosted is the
  // channel's room for the others to come into.
  if (room?.hosted) args.push('-join', room.code);
  args.push('-instance', instance);
}

/** The headers that sign a request to the master with this browser's player key (SignedRequest.cs, signed.js). */
async function signedHeaders(method, path, body) {
  identity.imports.start();
  const time = Math.floor(Date.now() / 1000);
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(body)));
  const hash = [...digest].map(b => b.toString(16).padStart(2, '0')).join('');
  const text = new TextEncoder().encode(`${method}\n${path}\n${time}\n${hash}`);
  let raw = '';
  for (const b of text) raw += String.fromCharCode(b);
  const signature = await identity.imports.sign(btoa(raw));
  return { 'x-openigi-key': identity.imports.publicKey(), 'x-openigi-signature': signature, 'x-openigi-time': String(time) };
}

if (IN_DISCORD) {
  try {
    await enterDiscord();
  } catch (error) {
    console.error('Discord', error);
  }
}

/**
 * Where the site hosts a game's files (tools/web-data/pack.py), a folder per game; ?data-host=<url> points
 * elsewhere, with {game} standing for the game's id (a URL without it is taken for the chosen game's folder).
 */
const hostedData = (id = game) => new URL((query.get('data-host') ?? 'https://data.openigi.com/{game}/').replaceAll('{game}', id), location.href)
  .href.replace(/\/?$/, '/');

/** Whether the chosen game's files come from the site; a game it does not serve is played from the person's own folder. */
const siteServes = () => files.TITLES[game].hosted;

// Files an earlier page downloaded for a game the site no longer serves, let go: I.G.I. 2 was hosted for a while.
for (const [id, title] of Object.entries(files.TITLES)) {
  if (!title.hosted) files.clearHosted(hostedData(id)).catch(() => {});
}

// --- The landing page ----------------------------------------------------------------------------------

/** The action under the text: the button's label, the line below it, and whether a folder can be changed. */
function offer(label, status, tone = 'neutral', canChange = false) {
  $('play-label').textContent = label;
  $('status').textContent = status;
  $('status').dataset.tone = tone;
  change.hidden = !canChange;
  $('action').hidden = false;
  $('loading').hidden = true;
}

/** The loading line in place of the action, with how far it has got as a fraction, or none while unknown. */
function progress(label, fraction = null, value = '') {
  $('action').hidden = true;
  $('loading').hidden = false;
  $('progress-label').textContent = label;
  $('progress-value').textContent = value;
  $('progress').classList.toggle('indeterminate', fraction === null);
  $('progress').firstElementChild.style.width = fraction === null ? '' : `${Math.round(fraction * 100)}%`;
}

/** The page again, in place of the game. */
function showLanding() {
  canvas.hidden = true;
  landing.hidden = false;
}

// --- WebGPU --------------------------------------------------------------------------------------------

/**
 * The adapter, asked for as the page opens so the person learns whether the game can run here before they
 * go looking for its folder. Null, with the reason, where it cannot.
 */
const adapterReady = (async () => {
  if (!window.isSecureContext) return { reason: 'This page has to be opened over https:// for the browser to allow WebGPU.' };
  if (!navigator.gpu) return { reason: 'This browser has no WebGPU. Use a current Chrome or Edge.' };
  try {
    const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
    return adapter ? { adapter } : { reason: 'This browser offers WebGPU but no graphics adapter. Check that hardware acceleration is on.' };
  } catch (error) {
    return { reason: `WebGPU could not start: ${error.message}` };
  }
})();

async function acquireDevice() {
  const { adapter, reason } = await adapterReady;
  if (!adapter) throw new Error(reason);

  // Everything the adapter has: the renderer reads back what the device got and uses what is there.
  const requiredLimits = {};
  for (const name in adapter.limits) requiredLimits[name] = adapter.limits[name];
  const device = await adapter.requestDevice({ requiredFeatures: [...adapter.features], requiredLimits });
  // Each distinct error once, then a count now and again: a broken pipeline repeats the same two errors every frame, and
  // writing them all to the console lagged the game it was reporting on.
  const seen = new Map();
  device.onuncapturederror = e => {
    const message = e.error.message;
    const count = (seen.get(message) ?? 0) + 1;
    seen.set(message, count);
    if (count === 1) console.error(`WebGPU: ${message}`);
    else if (count % 600 === 0) console.error(`WebGPU: the error above again (${count} times so far).`);
  };
  device.lost.then(info => {
    // Destroyed on purpose once the game has ended; anything else is a fault worth saying.
    if (info.reason !== 'destroyed') console.error(`WebGPU device lost: ${info.message}`);
  });
  return { adapter, device };
}

// --- Choosing the installation -------------------------------------------------------------------------

/** Asks for the game's folder; null when the person closes the picker. Throws an InstallationError. */
async function pickFolder() {
  if (window.showDirectoryPicker) {
    try {
      return await files.fromDirectory(await window.showDirectoryPicker({ id: game === 'igi1' ? 'openigi-game' : `openigi-${game}`, mode: 'read' }), game);
    } catch (error) {
      if (error.name === 'AbortError') return null;
      // A frame of another site (Discord's) may not show the picker: the plain file input below may still be allowed.
      if (error.name !== 'SecurityError') throw error;
    }
  }

  return new Promise((chosen, failed) => {
    const input = Object.assign(document.createElement('input'), { type: 'file', webkitdirectory: true });
    input.addEventListener('change', () => files.fromFileList(input.files, game).then(chosen, failed));
    input.addEventListener('cancel', () => chosen(null));
    input.click();
  });
}

// What the runtime last said on the error console: an exception nothing caught is printed there before the game
// stops, and the stop itself says only that it stopped. Kept to show beside it.
const lastErrors = [];
for (const level of ["error", "warn"]) {
  const original = console[level].bind(console);
  console[level] = (...parts) => {
    const line = parts.map(String).join(" ").slice(0, 600);
    if (level === "error" || /exception|abort|memory|assert/i.test(line)) {
      lastErrors.push(line);
      if (lastErrors.length > 8) lastErrors.shift();
    }
    original(...parts);
  };
}

/** On a phone or a tablet: the page goes fullscreen and sideways as Play is pressed, while the press allows it. */
const touchDevice = matchMedia('(pointer: coarse)').matches;

// An iPad (which says it is a Mac) or an iPhone. The iPhone's Safari has no fullscreen for a page; the iPad's has, and
// treats a game's quick thumbs in it as someone typing on a fake keyboard -- it warns and leaves fullscreen, and its
// swipe and pinch to leave take the finger on the stick. The touch controls went dead on an iPad and only there.
const appleTouch = /iPad|iPhone|iPod/.test(navigator.userAgent)
  || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);

function takeTheScreen() {
  if (!touchDevice || appleTouch || document.fullscreenElement) return;
  document.documentElement.requestFullscreen?.({ navigationUI: 'hide' })
    .then(() => screen.orientation?.lock?.('landscape'))
    .catch(() => {});
}

const megabytes = n => (n / 1048576).toFixed(0);

/** What the line under Play says about the hosted files: what is left to download, or that nothing is. */
function hostedLine(status) {
  if (status.start === 0 && status.all === 0) return 'The game is downloaded. Ready.';
  if (status.start === 0) return `Ready. The other missions download as you play (${megabytes(status.all)} MB).`;
  return `Downloads ${megabytes(status.start)} MB to start, ${megabytes(status.all)} MB in all, once.`;
}

/**
 * The installation to play, settled on the landing page: ?data=<url> at once when given (for automated
 * runs); otherwise the files the site hosts, or -- when asked for, or when the site's cannot be reached --
 * the person's own folder, picked or remembered from their last visit. Nothing starts until Play is pressed.
 */
async function chooseSource() {
  const gameButtons = [...$('games').querySelectorAll('button')];
  const lockGame = () => gameButtons.forEach(button => { button.disabled = true; });

  const data = query.get('data');
  if (data) {
    lockGame();
    return files.fromUrl(data.endsWith('/') ? data : `${data}/`, game);
  }

  const { adapter, reason } = await adapterReady;
  if (!adapter) {
    play.disabled = true;
    offer('Play now', reason, 'error');
    return new Promise(() => {}); // Nothing can be played here.
  }

  const own = $('own');
  const clear = $('clear');
  const elsewhere = $('elsewhere');
  // Each of these is the chosen game's, and is read again when the switch changes it. A game the site does not serve
  // has no hosted status: its own folder is the only way to play it.
  const statusOfSite = async () => (siteServes() ? files.hostedStatus(hostedData()) : null);
  let hosted = await statusOfSite();
  // The site's files unless the person chooses their own folder.
  let ownChosen = false;
  let folderMode = hosted === null;

  // What Play would start now in folder mode: a folder just picked and found to be the game, or one remembered.
  let ready = null;
  let remembered = await files.rememberedFolder(game);
  const offerFolder = () => {
    if (remembered) offer('Play now', `Game folder: /${remembered.name}`, 'neutral', true);
    else if (hosted === null && siteServes()) offer('Choose game folder', `The game's files could not be reached. ${hint()}`);
    else offer('Choose game folder', hint());
    own.hidden = true;
    clear.hidden = true;
    // Discord's frame may refuse a folder picker of any kind; the site in a browser of its own always offers one.
    elsewhere.hidden = !IN_DISCORD;
    elsewhere.href = `https://openigi.com/?game=${game}`;
  };
  const offerHosted = status => {
    offer('Play now', hostedLine(status), status.start === 0 ? 'ok' : 'neutral');
    own.hidden = false;
    clear.hidden = status.all === status.total;
    elsewhere.hidden = true;
  };
  if (folderMode) offerFolder();
  else offerHosted(hosted);

  // Inside a Discord Activity the voice channel came to play, and a game's switcher has just sent the page here
  // (?play): straight in, no Play to press -- with the site's files, or a remembered folder the browser still lets the
  // page read (asking again needs a click, which only Play can give).
  if (IN_DISCORD || SWITCHED) {
    if (!folderMode) {
      lockGame();
      return files.fromHosted(hostedData());
    }
    if (remembered && await files.readable(remembered)) {
      try {
        const source = await files.reopen(remembered, game);
        lockGame();
        return source;
      } catch (error) {
        if (!(error instanceof files.InstallationError)) throw error;
        // Gone or changed: Play below asks again.
      }
    }
  }

  return new Promise((settle, fail) => {
    // Once the installation is settled the buttons are the page's again (Play again, Reload).
    const listening = new AbortController();
    const resolve = source => { listening.abort(); lockGame(); settle(source); };
    const reject = error => { listening.abort(); lockGame(); fail(error); };

    let busy = false;

    /** The other game chosen: its own files, its own remembered folder, and the same choice of where from. */
    const switchTo = async id => {
      if (busy || id === game) return;
      busy = true;
      play.disabled = true;
      try {
        showGame(id);
        hosted = await statusOfSite();
        remembered = await files.rememberedFolder(game);
        ready = null;
        folderMode = ownChosen || hosted === null;
        if (folderMode) offerFolder();
        else offerHosted(hosted);
      } finally {
        busy = false;
        play.disabled = false;
      }
    };

    const act = async choose => {
      if (busy) return;
      busy = true;
      play.disabled = true;
      try {
        if (!choose && !folderMode) {
          resolve(await files.fromHosted(hostedData()));
          return;
        }

        if (!choose && ready) {
          resolve(ready);
          return;
        }

        if (!choose && remembered) {
          try {
            resolve(await files.reopen(remembered, game));
          } catch (error) {
            if (!(error instanceof files.InstallationError)) throw error;
            if (error.permission) {
              offer('Play now', error.message, 'error', true);
              return;
            }
            // Gone, or no longer the game: forgotten, and the person picks again.
            await files.forgetFolder(game);
            remembered = null;
            offer('Choose game folder', error.message, 'error');
          }
          return;
        }

        const picked = await pickFolder();
        if (picked) {
          // A folder of the other game is played as that game: the switch follows the folder.
          if (picked.title !== game) {
            showGame(picked.title);
            remembered = await files.rememberedFolder(game);
          }
          ready = picked;
          if (picked.handle) await files.rememberFolder(picked.title, picked.handle);
          offer('Play now', `Found ${files.TITLES[picked.title].name} in /${picked.name}. Ready.`, 'ok', true);
        }
      } catch (error) {
        if (!(error instanceof files.InstallationError)) {
          reject(error);
          return;
        }
        if (!folderMode) offer('Play now', error.message, 'error');
        else offer(ready || remembered ? 'Play now' : 'Choose game folder', error.message, 'error', Boolean(ready || remembered));
      } finally {
        busy = false;
        play.disabled = false;
      }
    };

    for (const button of gameButtons) {
      button.addEventListener('click', () => switchTo(button.dataset.game), { signal: listening.signal });
    }
    play.addEventListener('click', () => { takeTheScreen(); act(false); }, { signal: listening.signal });
    change.addEventListener('click', () => act(true), { signal: listening.signal });
    own.addEventListener('click', () => {
      ownChosen = true;
      folderMode = true;
      clear.hidden = true;
      offerFolder();
    }, { signal: listening.signal });
    clear.addEventListener('click', async () => {
      await files.clearHosted(hostedData());
      clear.hidden = true;
      if (!folderMode) offerHosted((await statusOfSite()) ?? hosted);
    }, { signal: listening.signal });
  });
}

// --- Starting ------------------------------------------------------------------------------------------

/**
 * The host's own assets -- fonts, colour grades, weather -- served beside the page and listed in
 * host-assets.json, written where the host reads them: /Assets, the application's directory being the root.
 */
async function writeHostAssets(fs) {
  const paths = await (await fetch('host-assets.json')).json();
  await Promise.all(paths.map(async path => {
    const response = await fetch(path);
    if (!response.ok) throw new Error(`${path}: ${response.status}`);
    fs.mkdirTree(`/${path.slice(0, path.lastIndexOf('/'))}`);
    fs.writeFile(`/${path}`, new Uint8Array(await response.arrayBuffer()));
  }));
}

/** After the game, or instead of it: the page offers a reload, which starts again from the landing page. */
function offerReload(label, status, tone) {
  showLanding();
  offer(label, status, tone);
  play.disabled = false;
  play.onclick = () => location.reload();
  play.focus();
}

async function start() {
  const source = await chooseSource();
  // The game the files are, which the host is told; an explicit -title in ?args= stands.
  if (!args.includes('-title')) args.push('-title', source.title);

  progress('Starting graphics');
  const { adapter, device } = await acquireDevice();
  globalThis.OpenIgiWebGpu.init({ adapter, device, canvas });

  progress('Starting the engine');
  const runtime = await dotnet.create();
  const fs = runtime.Module.FS;
  await writeHostAssets(fs);

  if (source.hosted) {
    // The bundle read before the game starts comes in first, with how fast; a mission's comes in when the
    // mission is chosen, with the game's own wait for it on the screen.
    const began = performance.now();
    source.onDownload = (name, got, size) => {
      if (name !== 'base') return;
      const rate = got / Math.max(0.001, (performance.now() - began) / 1000);
      progress('Downloading game files', got / size, `${megabytes(got)} of ${megabytes(size)} MB · ${(rate / 1048576).toFixed(1)} MB/s`);
    };
  }
  const read = await files.load(source, fs, GAME_ROOT, (done, total) =>
    progress(source.hosted ? 'Unpacking game files' : 'Reading game files', total ? done / total : 0, `${megabytes(done)} of ${megabytes(total)} MB`));
  console.log(`Read ${read.files} files; ${read.deferred} wait for their missions.`);
  if (source.hosted) source.onDownload = null;

  platform.install(canvas);
  runtime.setModuleImports('openigi-platform', platform.imports);
  runtime.setModuleImports('openigi-rtc', rtc.imports);
  runtime.setModuleImports('openigi-identity', identity.imports);
  runtime.setModuleImports('openigi-audio', audio.imports);
  runtime.setModuleImports('openigi-files', files.imports);
  // The switcher's other game (WebLinks.OpenGame): the page holds one game's files at a time, so it reloads into it,
  // after the player's files -- a sign-in made a moment ago among them -- are kept.
  let keptFiles = null;
  runtime.setModuleImports('openigi-page', {
    openGame: id => {
      if (!(id in files.TITLES)) return false;
      showGame(id);
      const next = new URL(location.href);
      next.searchParams.set('game', id);
      next.searchParams.set('play', '');
      next.searchParams.delete('data'); // A folder served for automated runs is one game's.
      Promise.resolve(keptFiles?.flush()).catch(() => {}).finally(() => location.assign(next.href));
      return true;
    },
  });

  progress('Starting the game');
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const game = exports.OpenIGI.Web.Program;

  // The player's own profile, settings and saves from an earlier visit, back where the game reads them before it boots.
  const kept = await userFiles.keep(fs, game.UserDirectory());
  keptFiles = kept;
  if (kept.files) console.log(`Restored ${kept.files} of the player's files.`);

  // The canvas takes the page before Boot: the game sizes itself to it.
  landing.hidden = true;
  canvas.hidden = false;
  canvas.focus();
  game.Boot(args);

  // Frames stepped so far and the state of the sound, for anyone measuring from outside (an automated
  // run, the console).
  globalThis.openigi = { frames: 0, audio: audio.diagnostics };

  // The missions' files, downloaded ahead while the menu is up, so the first mission chosen starts at once.
  if (source.hosted) setTimeout(() => source.prefetch(), 3000);

  // Everything the page needed has loaded by now: kept for a visit with no network.
  keepShell();

  // In a room the game can't stop when its tab is hidden: a host that stopped would stop everyone's game, and a joiner
  // that stopped would be dropped. A hidden tab gets no animation frames and its timers are held to one a second, but a
  // worker's timers run on, so while hidden the next frame comes from a worker's tick instead. Whether it is in a room is the
  // game's to say, asked only while hidden: a room is as often hosted or joined from the menu as from the command line.
  const inRoom = () => game.InSession();
  let ticker = null;
  let pending = 0;
  const next = () => {
    if (document.hidden && inRoom()) {
      if (!ticker) {
        const tick = 'let t; onmessage = () => { clearTimeout(t); t = setTimeout(() => postMessage(0), 16); };';
        ticker = new Worker(URL.createObjectURL(new Blob([tick], { type: 'text/javascript' })));
        ticker.onmessage = () => frame();
      }

      ticker.postMessage(0);
    } else {
      pending = requestAnimationFrame(frame);
    }
  };

  // Hidden with an animation frame still asked for: that frame won't come, so the worker takes over now.
  document.addEventListener('visibilitychange', () => {
    if (document.hidden && pending && inRoom()) {
      cancelAnimationFrame(pending);
      pending = 0;
      next();
    }
  });

  const frame = () => {
    pending = 0;
    let going;
    try {
      globalThis.openigi.frames++;
      going = game.Step();
    } catch (error) {
      platform.uninstall();
      device.destroy();
      // With where it stopped, as far as the browser says: the message alone names a trap, not what trapped.
      const where = String(error.stack || '').split('\n').filter(line => line && !line.includes(error.message)).slice(0, 12).join('\n');
      const said = lastErrors.join('\n');
      offerReload('Reload', `The game stopped: ${error.message}${said ? `\n${said}` : ''}${where ? `\n${where}` : ''}`, 'error');
      throw error;
    }

    if (going) {
      next();
      return;
    }

    // Quit. The game has shut itself down -- saved its settings, closed its sound -- and the rest is the
    // page's to release: the keyboard and pointer, the device, and the canvas still holding the last
    // picture, which the page replaces.
    platform.uninstall();
    device.destroy();
    kept.flush();
    offerReload('Play again', 'The game has closed. Your settings are saved.', 'neutral');
  };
  next();
}

// The macOS and Linux install command, copied by its button. The code is selectable by hand as well, for
// a browser that refuses the clipboard.
$('install-copy').addEventListener('click', async event => {
  const button = event.currentTarget;
  try {
    await navigator.clipboard.writeText($('install-command').textContent);
    button.textContent = 'Copied';
  } catch {
    getSelection().selectAllChildren($('install-command'));
    button.textContent = 'Select';
  }
  setTimeout(() => { button.textContent = 'Copy'; }, 1500);
});

// --- The community -------------------------------------------------------------------------------------
//
// The Discord server's own live numbers -- its name, who is online, a few faces, its invite -- read from the
// server's widget feed and drawn in the page's look rather than in Discord's frame. The cards link out
// already; this only fills them in, and leaves them as they are when a feed cannot be read.

const DISCORD_GUILD = '1534217331478106193';

async function showDiscord() {
  const reply = await fetch(`https://discord.com/api/guilds/${DISCORD_GUILD}/widget.json`);
  if (!reply.ok) return;
  const server = await reply.json();

  if (server.instant_invite) {
    for (const link of document.querySelectorAll('[data-discord-link]')) link.href = server.instant_invite;
  }
  if (server.name) $('discord-name').textContent = server.name;
  if (Number.isFinite(server.presence_count)) {
    $('discord-online').textContent = `${server.presence_count} online`;
    $('discord-online').hidden = false;
  }

  const faces = $('discord-avatars');
  for (const member of (server.members ?? []).filter(m => m.avatar_url).slice(0, 5)) {
    const img = Object.assign(document.createElement('img'), { src: member.avatar_url, alt: '', loading: 'lazy', referrerPolicy: 'no-referrer' });
    faces.append(img);
  }
}

async function showGitHub() {
  const reply = await fetch('https://api.github.com/repos/OpenIGI/OpenIGI');
  if (!reply.ok) return;
  const { stargazers_count: stars } = await reply.json();
  if (!Number.isFinite(stars)) return;
  $('github-stars').textContent = `★ ${stars}`;
  $('github-stars').hidden = false;
}

// Outside sites the page can't reach from inside Discord's frame.
if (!IN_DISCORD) {
  showDiscord().catch(() => {});
  showGitHub().catch(() => {});
}

// What the header says about the browser, as soon as it is known.
adapterReady.then(({ adapter }) => {
  $('gpu').dataset.state = adapter ? 'ok' : 'no';
  $('gpu').textContent = adapter ? 'WebGPU ready' : 'No WebGPU';
});

start().catch(error => {
  console.error(error);
  offerReload('Reload', error.message, 'error');
});
