// Starts the game in the page: WebGPU first, then the installation, then the runtime, then a frame per
// animation frame.
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

const canvas = document.getElementById('view');
const status = document.getElementById('status');
const pick = document.getElementById('pick');
const say = text => { status.textContent = text; };

const query = new URLSearchParams(location.search);
const args = (query.get('args') ?? '').split(' ').filter(Boolean);

async function acquireDevice() {
  if (!navigator.gpu) throw new Error('This browser has no WebGPU.');
  const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
  if (!adapter) throw new Error('This browser offers no WebGPU adapter.');

  // Everything the adapter has: the renderer reads back what the device got and uses what is there.
  const requiredLimits = {};
  for (const name in adapter.limits) requiredLimits[name] = adapter.limits[name];
  const device = await adapter.requestDevice({ requiredFeatures: [...adapter.features], requiredLimits });
  device.onuncapturederror = e => console.error(`WebGPU: ${e.error.message}`);
  device.lost.then(info => console.error(`WebGPU device lost: ${info.message}`));
  return { adapter, device };
}

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

/** Where the installation comes from: ?data=<url> when given, otherwise the folder the person picks. */
function chooseSource() {
  const data = query.get('data');
  if (data) return Promise.resolve(files.urlSource(data.endsWith('/') ? data : `${data}/`));

  return new Promise(resolve => {
    pick.hidden = false;
    say('Choose the folder Project I.G.I. is installed in.');
    pick.addEventListener('click', async () => {
      if (window.showDirectoryPicker) {
        resolve(files.directorySource(await window.showDirectoryPicker({ id: 'openigi-game', mode: 'read' })));
        return;
      }

      const input = Object.assign(document.createElement('input'), { type: 'file', webkitdirectory: true });
      input.addEventListener('change', () => resolve(files.fileListSource(input.files)));
      input.click();
    });
  });
}

async function start() {
  const source = await chooseSource();
  pick.hidden = true;

  say('Starting WebGPU…');
  const { adapter, device } = await acquireDevice();
  globalThis.OpenIgiWebGpu.init({ adapter, device, canvas });

  say('Starting the runtime…');
  const runtime = await dotnet.create();
  const fs = runtime.Module.FS;
  await writeHostAssets(fs);

  const mb = n => (n / 1048576).toFixed(0);
  const read = await files.load(source, fs, GAME_ROOT, (done, total) =>
    say(`Reading the installation: ${mb(done)} of ${mb(total)} MB`));
  console.log(`Read ${read.files} files; ${read.deferred} wait for their missions.`);

  platform.install(canvas);
  runtime.setModuleImports('openigi-platform', platform.imports);
  runtime.setModuleImports('openigi-audio', audio.imports);
  runtime.setModuleImports('openigi-files', files.imports);

  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const game = exports.OpenIGI.Web.Program;

  say('');
  canvas.focus();
  game.Boot(args);

  // Frames stepped so far and the state of the sound, for anyone measuring from outside (an automated
  // run, the console).
  globalThis.openigi = { frames: 0, audio: audio.diagnostics };

  const frame = () => {
    try {
      globalThis.openigi.frames++;
      if (game.Step()) requestAnimationFrame(frame);
      else say('The game has ended.');
    } catch (error) {
      say(`The game stopped: ${error.message}`);
      throw error;
    }
  };
  requestAnimationFrame(frame);
}

start().catch(error => {
  say(error.message);
  console.error(error);
});
