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
import * as audio from './audio.js';
import * as files from './game-files.js';

const GAME_ROOT = '/game';

const $ = id => document.getElementById(id);
const canvas = $('view');
const landing = $('landing');
const play = $('play');
const change = $('change');

const query = new URLSearchParams(location.search);
const args = (query.get('args') ?? '').split(' ').filter(Boolean);

const HINT = 'The folder with IGI.exe in it, usually pc inside the installation.';

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
  device.onuncapturederror = e => console.error(`WebGPU: ${e.error.message}`);
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
      return await files.fromDirectory(await window.showDirectoryPicker({ id: 'openigi-game', mode: 'read' }));
    } catch (error) {
      if (error.name === 'AbortError') return null;
      throw error;
    }
  }

  return new Promise((chosen, failed) => {
    const input = Object.assign(document.createElement('input'), { type: 'file', webkitdirectory: true });
    input.addEventListener('change', () => files.fromFileList(input.files).then(chosen, failed));
    input.addEventListener('cancel', () => chosen(null));
    input.click();
  });
}

/**
 * The installation to play, settled on the landing page: ?data=<url> at once when given (for automated
 * runs); otherwise the person picks the folder, or plays the one remembered from their last visit, and
 * presses Play. Nothing starts until they do.
 */
async function chooseSource() {
  const data = query.get('data');
  if (data) return files.fromUrl(data.endsWith('/') ? data : `${data}/`);

  const { adapter, reason } = await adapterReady;
  if (!adapter) {
    play.disabled = true;
    offer('Choose game folder', reason, 'error');
    return new Promise(() => {}); // Nothing can be played here.
  }

  // What Play would start now: a folder just picked and found to be the game, or one remembered.
  let ready = null;
  let remembered = await files.rememberedFolder();
  if (remembered) offer('Play game', `Game folder: /${remembered.name}`, 'neutral', true);
  else offer('Choose game folder', HINT);

  return new Promise((settle, fail) => {
    // Once the installation is settled the buttons are the page's again (Play again, Reload).
    const listening = new AbortController();
    const resolve = source => { listening.abort(); settle(source); };
    const reject = error => { listening.abort(); fail(error); };

    let busy = false;
    const act = async choose => {
      if (busy) return;
      busy = true;
      play.disabled = true;
      try {
        if (!choose && ready) {
          resolve(ready);
          return;
        }

        if (!choose && remembered) {
          try {
            resolve(await files.reopen(remembered));
          } catch (error) {
            if (!(error instanceof files.InstallationError)) throw error;
            if (error.permission) {
              offer('Play game', error.message, 'error', true);
              return;
            }
            // Gone, or no longer the game: forgotten, and the person picks again.
            await files.forgetFolder();
            remembered = null;
            offer('Choose game folder', error.message, 'error');
          }
          return;
        }

        const picked = await pickFolder();
        if (picked) {
          ready = picked;
          if (picked.handle) await files.rememberFolder(picked.handle);
          offer('Play game', `Found the game in /${picked.name}. Ready.`, 'ok', true);
        }
      } catch (error) {
        if (!(error instanceof files.InstallationError)) {
          reject(error);
          return;
        }
        offer(ready || remembered ? 'Play game' : 'Choose game folder', error.message, 'error', Boolean(ready || remembered));
      } finally {
        busy = false;
        play.disabled = false;
      }
    };

    play.addEventListener('click', () => act(false), { signal: listening.signal });
    change.addEventListener('click', () => act(true), { signal: listening.signal });
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

  progress('Starting graphics');
  const { adapter, device } = await acquireDevice();
  globalThis.OpenIgiWebGpu.init({ adapter, device, canvas });

  progress('Starting the engine');
  const runtime = await dotnet.create();
  const fs = runtime.Module.FS;
  await writeHostAssets(fs);

  const mb = n => (n / 1048576).toFixed(0);
  const read = await files.load(source, fs, GAME_ROOT, (done, total) =>
    progress('Reading game files', total ? done / total : 0, `${mb(done)} of ${mb(total)} MB`));
  console.log(`Read ${read.files} files; ${read.deferred} wait for their missions.`);

  platform.install(canvas);
  runtime.setModuleImports('openigi-platform', platform.imports);
  runtime.setModuleImports('openigi-audio', audio.imports);
  runtime.setModuleImports('openigi-files', files.imports);

  progress('Starting the game');
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const game = exports.OpenIGI.Web.Program;

  // The canvas takes the page before Boot: the game sizes itself to it.
  landing.hidden = true;
  canvas.hidden = false;
  canvas.focus();
  game.Boot(args);

  // Frames stepped so far and the state of the sound, for anyone measuring from outside (an automated
  // run, the console).
  globalThis.openigi = { frames: 0, audio: audio.diagnostics };

  const frame = () => {
    let going;
    try {
      globalThis.openigi.frames++;
      going = game.Step();
    } catch (error) {
      platform.uninstall();
      device.destroy();
      offerReload('Reload', `The game stopped: ${error.message}`, 'error');
      throw error;
    }

    if (going) {
      requestAnimationFrame(frame);
      return;
    }

    // Quit. The game has shut itself down -- saved its settings, closed its sound -- and the rest is the
    // page's to release: the keyboard and pointer, the device, and the canvas still holding the last
    // picture, which the page replaces.
    platform.uninstall();
    device.destroy();
    offerReload('Play again', 'The game has closed. Your settings are saved.', 'neutral');
  };
  requestAnimationFrame(frame);
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

// What the header says about the browser, as soon as it is known.
adapterReady.then(({ adapter }) => {
  $('gpu').dataset.state = adapter ? 'ok' : 'no';
  $('gpu').textContent = adapter ? 'WebGPU ready' : 'No WebGPU';
});

start().catch(error => {
  console.error(error);
  offerReload('Reload', error.message, 'error');
});
