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

const query = new URLSearchParams(location.search);
const args = (query.get('args') ?? '').split(' ').filter(Boolean);

// --- The landing page ----------------------------------------------------------------------------------

/** Shows one stage of the panel: 'pick', 'loading', 'ended' or 'failed'. */
function stage(name) {
  for (const element of document.querySelectorAll('.stage')) element.hidden = element.id !== `stage-${name}`;
}

/** The loading stage's line, with how far it has got as a fraction, or none while that is unknown. */
function progress(label, fraction = null, value = '') {
  $('progress-label').textContent = label;
  $('progress-value').textContent = value;
  $('progress').classList.toggle('indeterminate', fraction === null);
  $('progress').firstElementChild.style.width = fraction === null ? '' : `${Math.round(fraction * 100)}%`;
}

/** Why the folder just picked cannot be played from, or nothing to clear it. */
function pickError(message) {
  $('pick-error').textContent = message ?? '';
  $('pick-error').hidden = !message;
}

/** The page again, in place of the game: after it has ended, or when it could not start or went on. */
function showLanding(name, message) {
  canvas.hidden = true;
  landing.hidden = false;
  if (message) $('failed-text').textContent = message;
  stage(name);
}

$('again').addEventListener('click', () => location.reload());
$('retry').addEventListener('click', () => location.reload());

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
    return adapter ? { adapter } : { reason: 'This browser offers WebGPU but no graphics adapter for it. Check that hardware acceleration is on.' };
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

/**
 * Where the installation comes from: ?data=<url> when given, otherwise the folder the person picks --
 * asked again, with the reason, for as long as what they pick is not the game.
 */
async function chooseSource() {
  const data = query.get('data');
  if (data) return files.fromUrl(data.endsWith('/') ? data : `${data}/`);

  const pick = $('pick');
  const support = $('support');
  // Said only when the game cannot run here, and then before the person goes looking for the folder.
  const { adapter, reason } = await adapterReady;
  support.textContent = reason ?? '';
  support.hidden = Boolean(adapter);
  pick.disabled = !adapter;

  const open = async () => {
    if (window.showDirectoryPicker) {
      try {
        return await files.fromDirectory(await window.showDirectoryPicker({ id: 'openigi-game', mode: 'read' }));
      } catch (error) {
        if (error.name === 'AbortError') return null; // The person closed the picker.
        throw error;
      }
    }

    return new Promise((chosen, failed) => {
      const input = Object.assign(document.createElement('input'), { type: 'file', webkitdirectory: true });
      input.addEventListener('change', () => files.fromFileList(input.files).then(chosen, failed));
      input.addEventListener('cancel', () => chosen(null));
      input.click();
    });
  };

  return new Promise((resolve, reject) => {
    pick.addEventListener('click', async () => {
      if (pick.disabled) return;
      pick.disabled = true;
      pickError(null);
      try {
        const source = await open();
        if (source) {
          resolve(source);
          return;
        }
      } catch (error) {
        if (!(error instanceof files.InstallationError)) {
          reject(error);
          return;
        }
        pickError(error.message);
      }
      pick.disabled = false;
    });
  });
}

async function start() {
  stage('pick');
  const source = await chooseSource();

  stage('loading');
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
      showLanding('failed', `The game stopped: ${error.message}`);
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
    showLanding('ended');
    $('again').focus();
  };
  requestAnimationFrame(frame);
}

start().catch(error => {
  console.error(error);
  showLanding('failed', error.message);
});
