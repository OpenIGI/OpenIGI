// The installation, read from where the person keeps it into the runtime's in-memory file system, where
// the game reads it by path as it would read a disk.
//
// Everything is read up front except the large files of the missions -- a level's terrain, lightmaps and
// textures, a few dozen megabytes each -- which are read when the mission is chosen: the load gate
// (WebLoadGate.cs) asks, and this starts reading and answers yes once they are all in place. The small
// files of every mission are read up front because the game scans them all at start-up to learn what
// the missions are.

/** A mission's file at or above this size waits until the mission is chosen. */
const LARGE = 256 * 1024;

/** What is never read: texture packs (out of scope here), and whatever else lives beside the game. */
const IGNORED_FOLDERS = new Set(['overrides']);
const TOP_LEVEL_FILE = /\.(qvm|cfg|txt|ini|dat)$|^igi\.exe$/i;

const key = path => path.toLowerCase();

// --- Finding the installation -----------------------------------------------------------------------
//
// Nothing is read until the folder is known to be the game's. The person may pick the folder above it --
// a retail install keeps the game in a "pc" folder inside the one it was installed to -- so the folders
// directly inside the picked one are tried as well, "pc" first.

/**
 * What proves a folder is a Project I.G.I. installation: the signature file and the asset folders of
 * GameTitle.Igi1 (src/Core/OpenIGI.Core/Titles/GameTitle.cs). Keep the two in step.
 */
const SIGNATURE_FILE = 'config.qvm';
const ASSET_ROOTS = ['missions', 'common', 'language', 'menusystem'];

/** The sequel's signature file (GameTitle.Igi2); its folder has everything above as well. */
const SEQUEL_SIGNATURE_FILE = 'igi2.exe';

/** A folder the game cannot be played from, with what to tell the person. */
export class InstallationError extends Error {
  /**
   * @param {string} message
   * @param {boolean} permission Whether it is only that the browser was not allowed to read the folder,
   *   which asking again may change, rather than that the folder is not the game or is gone.
   */
  constructor(message, permission = false) {
    super(message);
    this.name = 'InstallationError';
    this.permission = permission;
  }
}

/**
 * What a folder's top level makes it.
 * @param {Map<string, string>} kinds Its entries' kinds ('file' or 'directory') by lower-cased name.
 * @returns {'igi1' | 'igi2' | null}
 */
function judge(kinds) {
  if (kinds.get(SEQUEL_SIGNATURE_FILE) === 'file') return 'igi2';
  if (kinds.get(SIGNATURE_FILE) === 'file' && ASSET_ROOTS.every(name => kinds.get(name) === 'directory')) return 'igi1';
  return null;
}

/** "pc" first, then the rest by name. */
function candidateOrder(a, b) {
  const rank = name => (name.toLowerCase() === 'pc' ? 0 : 1);
  return rank(a) - rank(b) || a.localeCompare(b);
}

/**
 * The installation among the picked folder and the folders directly inside it.
 * @param {string} picked The picked folder's name, for the message.
 * @param {Array<{ name: string, kinds: () => Promise<Map<string, string>> }>} candidates
 *   The picked folder (named '') and then its sub-folders, each with a way to list its top level.
 * @returns {Promise<string>} The name of the one that is the installation: '' for the picked folder.
 */
async function locate(picked, candidates) {
  let sequel = false;
  for (const candidate of candidates) {
    const verdict = judge(await candidate.kinds());
    if (verdict === 'igi1') return candidate.name;
    sequel ||= verdict === 'igi2';
  }

  throw new InstallationError(sequel
    ? `/${picked} is I.G.I. 2: Covert Strike. Choose Project I.G.I.'s folder instead.`
    : `No Project I.G.I. installation in /${picked}. Choose the folder with IGI.exe in it, usually pc.`);
}

/**
 * The installation in a flat list of paths relative to the picked folder, as a file input or a manifest
 * gives them.
 * @param {string} picked The picked folder's name, for the message.
 * @param {Array<{ path: string }>} files
 * @returns {Promise<string>} The prefix to strip from every path: '' or 'pc/', for instance.
 */
async function locateInList(picked, files) {
  // One pass over the list: the top level of the picked folder, and of each folder directly inside it.
  const top = new Map();
  const inside = new Map();
  const note = (kinds, name, kind) => {
    const lower = name.toLowerCase();
    if (kinds.get(lower) !== 'directory') kinds.set(lower, kind);
  };
  for (const { path } of files) {
    const parts = path.split('/');
    note(top, parts[0], parts.length > 1 ? 'directory' : 'file');
    if (parts.length > 1) {
      if (!inside.has(parts[0])) inside.set(parts[0], new Map());
      note(inside.get(parts[0]), parts[1], parts.length > 2 ? 'directory' : 'file');
    }
  }

  const candidates = [{ name: '', kinds: async () => top }];
  for (const name of [...inside.keys()].sort(candidateOrder)) {
    candidates.push({ name, kinds: async () => inside.get(name) });
  }

  const found = await locate(picked, candidates);
  return found ? `${found}/` : '';
}

/** The files under a prefix, their paths made relative to it. */
function rebase(files, prefix) {
  if (!prefix) return files;
  return files
    .filter(file => file.path.startsWith(prefix))
    .map(file => ({ ...file, path: file.path.slice(prefix.length) }));
}

// --- Sources -------------------------------------------------------------------------------------------
//
// Each is made by an async function that finds the installation first, and throws an InstallationError
// when there is none before a single file is read, so the page can say so and let the person pick again.

/** A folder's top-level entries by lower-cased name. */
async function entriesOf(dir) {
  const entries = new Map();
  for await (const [name, entry] of dir.entries()) entries.set(name.toLowerCase(), entry);
  return entries;
}

const kindsOf = entries => new Map([...entries].map(([name, entry]) => [name, entry.kind]));

/**
 * A folder the person picked (File System Access API).
 * @param {FileSystemDirectoryHandle} handle
 */
export async function fromDirectory(handle) {
  // Only the top levels of the picked folder and of the folders directly inside it are listed to find
  // the installation; nothing is walked until it is found.
  const top = await entriesOf(handle);
  const candidates = [{ name: '', kinds: async () => kindsOf(top) }];
  const folders = [...top.values()].filter(entry => entry.kind === 'directory');
  for (const folder of folders.sort((a, b) => candidateOrder(a.name, b.name))) {
    candidates.push({ name: folder.name, kinds: async () => kindsOf(await entriesOf(folder)) });
  }

  const found = await locate(handle.name, candidates);
  const root = found ? await handle.getDirectoryHandle(found) : handle;

  return {
    name: found ? `${handle.name}/${found}` : handle.name,
    /** The folder the person picked, which can be remembered for their next visit. */
    handle,
    async list() {
      const files = [];
      const walk = async (dir, prefix) => {
        for await (const [name, entry] of dir.entries()) {
          const path = prefix ? `${prefix}/${name}` : name;
          if (entry.kind === 'directory') {
            if (!prefix && IGNORED_FOLDERS.has(name.toLowerCase())) continue;
            await walk(entry, path);
          } else {
            files.push({ path, size: (await entry.getFile()).size, entry });
          }
        }
      };
      await walk(root, '');
      return files;
    },
    async read(file) {
      return new Uint8Array(await (await file.entry.getFile()).arrayBuffer());
    },
  };
}

// --- The folder, remembered ------------------------------------------------------------------------------
//
// A picked folder's handle can be kept in the page's IndexedDB, so a returning player is offered Play instead
// of the picker. The browser does not carry the permission over: reading the folder again needs a fresh
// yes, which it will only ask for from a click -- so a remembered folder is opened from the Play button,
// never as the page loads.

const DATABASE = 'openigi';
const STORE = 'folders';
const GAME_FOLDER = 'game';

function database() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function transact(mode, work) {
  const db = await database();
  try {
    return await new Promise((resolve, reject) => {
      const transaction = db.transaction(STORE, mode);
      const request = work(transaction.objectStore(STORE));
      transaction.oncomplete = () => resolve(request.result);
      transaction.onerror = () => reject(transaction.error);
    });
  } finally {
    db.close();
  }
}

/** Keeps the folder a game was just played from, for the next visit. */
export async function rememberFolder(handle) {
  try {
    await transact('readwrite', store => store.put(handle, GAME_FOLDER));
  } catch (error) {
    console.warn(`The game folder could not be remembered: ${error}`); // A private window, for one.
  }
}

/** The folder remembered from the last visit, or null. */
export async function rememberedFolder() {
  try {
    return (await transact('readonly', store => store.get(GAME_FOLDER))) ?? null;
  } catch {
    return null;
  }
}

/** Forgets the remembered folder. */
export async function forgetFolder() {
  try {
    await transact('readwrite', store => store.delete(GAME_FOLDER));
  } catch {
    // Nothing was remembered, or nothing can be.
  }
}

/**
 * Opens a remembered folder again. Asks the browser for permission to read it when it does not hold it
 * still, so it must be called from a click.
 * @param {FileSystemDirectoryHandle} handle
 */
export async function reopen(handle) {
  if ((await handle.queryPermission({ mode: 'read' })) !== 'granted'
    && (await handle.requestPermission({ mode: 'read' })) !== 'granted') {
    throw new InstallationError(`Permission to read /${handle.name} was not given. Press Play to be asked again.`, true);
  }

  try {
    return await fromDirectory(handle);
  } catch (error) {
    if (error.name === 'NotFoundError') throw new InstallationError(`/${handle.name} is no longer there. Choose the game folder again.`);
    throw error;
  }
}

/**
 * A folder picked through a plain file input (webkitdirectory), where the API above is missing. The
 * browser has listed every file under the picked folder by then; none of them is read here.
 * @param {FileList} list
 */
export async function fromFileList(list) {
  const picked = list[0]?.webkitRelativePath.split('/')[0] ?? '';
  const all = [...list].map(f => ({ path: f.webkitRelativePath.split('/').slice(1).join('/'), size: f.size, file: f }));
  const prefix = await locateInList(picked, all);
  const files = rebase(all, prefix);
  return {
    name: prefix ? `${picked}/${prefix.slice(0, -1)}` : picked,
    async list() { return files; },
    async read(file) { return new Uint8Array(await file.file.arrayBuffer()); },
  };
}

/**
 * An installation served over HTTP beside a manifest.json of { path, size } entries: for development and
 * for automated runs, where nothing can click a folder picker.
 * @param {string} base The URL of the folder, ending in a slash.
 */
export async function fromUrl(base) {
  const response = await fetch(`${base}manifest.json`);
  if (!response.ok) throw new InstallationError(`${base}manifest.json could not be fetched (${response.status}).`);
  const all = await response.json();
  const prefix = await locateInList(base, all);
  const files = rebase(all, prefix);
  const at = base + prefix.split('/').filter(Boolean).map(part => `${encodeURIComponent(part)}/`).join('');
  return {
    name: at,
    async list() { return files; },
    async read(file) {
      const reply = await fetch(at + file.path.split('/').map(encodeURIComponent).join('/'));
      if (!reply.ok) throw new Error(`${file.path}: ${reply.status}`);
      return new Uint8Array(await reply.arrayBuffer());
    },
  };
}

// --- The hosted installation -------------------------------------------------------------------------------
//
// The game's files served beside the site, packed by tools/web-data/pack.py into a few bundles: "base", read
// before the game starts, and one per mission with its large files, read when the mission is chosen. A bundle
// is named by its hash, so once downloaded it is kept in the page's Cache Storage and never fetched again;
// index.json, the one file that changes, is asked for afresh each visit and kept too, for a visit offline.

const DATA_CACHE = 'openigi-data';
const INDEX = 'index.json';

/** The page's cache of downloaded bundles, or null where the browser keeps none (no secure context). */
async function dataCache() {
  try {
    return globalThis.caches ? await caches.open(DATA_CACHE) : null;
  } catch {
    return null;
  }
}

const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

async function sha256(bytes) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return [...digest].map(b => b.toString(16).padStart(2, '0')).join('');
}

/**
 * A bundle's bytes as served, downloaded whole. A dropped connection -- a phone between masts, a laptop
 * waking -- is picked up where it stopped with a Range request, a few times, waiting longer each time.
 * @param {(got: number) => void} report How many bytes are in so far.
 */
async function download(url, size, report) {
  const chunks = [];
  let got = 0;
  for (let attempt = 0; ; attempt++) {
    try {
      const reply = await fetch(url, { cache: 'no-store', headers: got ? { Range: `bytes=${got}-` } : {} });
      if (!reply.ok) throw new Error(`${reply.status}`);
      if (got && reply.status !== 206) {
        // The server sent it all again rather than the rest.
        chunks.length = 0;
        got = 0;
      }
      const reader = reply.body.getReader();
      for (let part = await reader.read(); !part.done; part = await reader.read()) {
        chunks.push(part.value);
        got += part.value.length;
        report(got);
      }
      if (got >= size) break;
      throw new Error(`ended at ${got} of ${size} bytes`);
    } catch (error) {
      if (attempt >= 6) throw new Error(`${url} could not be downloaded: ${error.message}`);
      await pause(Math.min(1000 * 2 ** attempt, 15000));
    }
  }

  const bytes = new Uint8Array(got);
  let at = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, at);
    at += chunk.length;
  }
  return bytes;
}

async function gunzip(bytes) {
  const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/** The hosted index: fetched afresh, or the one kept from the last visit when the network has none. */
async function hostedIndex(base) {
  const cache = await dataCache();
  const url = new URL(INDEX, base).href;
  try {
    const reply = await fetch(url, { cache: 'no-cache' });
    if (!reply.ok) throw new Error(`${reply.status}`);
    await cache?.put(url, reply.clone()).catch(() => {});
    return await reply.json();
  } catch (error) {
    const kept = await cache?.match(url);
    if (kept) return kept.json();
    throw new InstallationError(`The game's files could not be fetched (${error.message}). Check the connection and try again.`);
  }
}

/** The bundles a page still holds from an earlier index, deleted. */
async function forgetStale(base, index) {
  const cache = await dataCache();
  if (!cache) return;
  const keep = new Set([INDEX, ...Object.values(index.bundles).map(b => b.url)].map(name => new URL(name, base).href));
  for (const request of await cache.keys()) {
    if (request.url.startsWith(base) && !keep.has(request.url)) await cache.delete(request);
  }
}

/**
 * What playing the hosted game needs to download now, in bytes: the bundle read before the game starts, all
 * of them less what is already kept, and all of them. Null when the files cannot be reached.
 */
export async function hostedStatus(base) {
  try {
    const index = await hostedIndex(base);
    const cache = await dataCache();
    const kept = async b => Boolean(await cache?.match(new URL(b.url, base).href));
    let start = 0;
    let all = 0;
    let total = 0;
    for (const [name, bundle] of Object.entries(index.bundles)) {
      total += bundle.size;
      if (await kept(bundle)) continue;
      all += bundle.size;
      if (name === 'base') start += bundle.size;
    }
    return { start, all, total };
  } catch {
    return null;
  }
}

/** Deletes every downloaded bundle. */
export async function clearHosted() {
  try {
    await globalThis.caches?.delete(DATA_CACHE);
  } catch {
    // Nothing kept, or nothing can be.
  }
}

/**
 * The installation the site hosts.
 * @param {string} base The URL of the folder holding index.json, ending in a slash.
 */
export async function fromHosted(base) {
  const index = await hostedIndex(base);
  forgetStale(base, index).catch(() => {});
  navigator.storage?.persist?.().catch(() => {});

  const kept = new Map(); // Bundle name -> promise of its served bytes being in the cache (or in hand).
  const unpacked = new Map(); // Bundle name -> promise of its files' bytes, while files are left to read.
  const left = new Map(); // Bundle name -> how many of its files are still to be read.
  for (const file of index.files) left.set(file.bundle, (left.get(file.bundle) ?? 0) + 1);

  const source = {
    name: 'openigi.com',
    hosted: true,
    /** Called as a bundle comes in: (bundle name, bytes so far, its size). */
    onDownload: null,

    async list() { return index.files; },

    async read(file) {
      if (!unpacked.has(file.bundle)) unpacked.set(file.bundle, fetchBundle(file.bundle).then(gunzip));
      const bytes = await unpacked.get(file.bundle);
      const part = bytes.slice(file.offset, file.offset + file.size);
      // The last of a bundle's files read: its bytes are let go.
      const remaining = left.get(file.bundle) - 1;
      left.set(file.bundle, remaining);
      if (remaining === 0) unpacked.delete(file.bundle);
      return part;
    },

    /**
     * Downloads every mission's bundle into the cache, one after another, while the game is played -- so a
     * mission chosen later starts at once. Skipped when the browser says data is to be saved.
     */
    async prefetch() {
      const connection = navigator.connection;
      if (connection?.saveData || /(^|-)(2g|3g)$/.test(connection?.effectiveType ?? '')) return;
      const order = name => Number(name.match(/(\d+)$/)?.[1] ?? 0);
      const names = Object.keys(index.bundles).filter(name => name !== 'base').sort((a, b) => order(a) - order(b));
      for (const name of names) {
        try {
          await keep(name);
        } catch (error) {
          console.warn(`Downloading ahead stopped: ${error.message}`);
          return;
        }
      }
    },
  };

  /** A bundle's served bytes: from the cache, or downloaded, checked and put in it. */
  async function fetchBundle(name) {
    const bundle = index.bundles[name];
    const url = new URL(bundle.url, base).href;
    const cache = await dataCache();
    const hit = await cache?.match(url);
    if (hit) return new Uint8Array(await hit.arrayBuffer());

    // Downloaded ahead already, or being: wait for that rather than downloading it twice.
    if (kept.has(name)) {
      const held = await kept.get(name).catch(() => null);
      if (held) return held;
      const again = await cache?.match(url);
      if (again) return new Uint8Array(await again.arrayBuffer());
    }

    const bytes = await downloadChecked(name);
    await store(url, bytes);
    return bytes;
  }

  /** A bundle put in the cache without being unpacked; resolves to its bytes only where none can be kept. */
  function keep(name) {
    if (!kept.has(name)) {
      kept.set(name, (async () => {
        const url = new URL(index.bundles[name].url, base).href;
        const cache = await dataCache();
        if (await cache?.match(url)) return null;
        const bytes = await downloadChecked(name);
        return (await store(url, bytes)) ? null : bytes;
      })());
      kept.get(name).catch(() => kept.delete(name));
    }
    return kept.get(name);
  }

  async function downloadChecked(name) {
    const bundle = index.bundles[name];
    const url = new URL(bundle.url, base).href;
    const bytes = await download(url, bundle.size, got => source.onDownload?.(name, got, bundle.size));
    if ((await sha256(bytes)) !== bundle.sha256) throw new Error(`${bundle.url} arrived damaged; reload to try again.`);
    return bytes;
  }

  /** Whether the bytes were kept: a full disk or a private window keeps nothing, and the game plays on. */
  async function store(url, bytes) {
    const cache = await dataCache();
    if (!cache) return false;
    try {
      await cache.put(url, new Response(bytes, { headers: { 'Content-Type': 'application/octet-stream' } }));
      return true;
    } catch (error) {
      console.warn(`A downloaded bundle could not be kept: ${error.message}`);
      return false;
    }
  }

  return source;
}

let fs = null;
let root = '/game';
let source = null;
const missions = new Map();

function wanted(file) {
  const parts = file.path.split('/');
  if (parts.length === 1) return TOP_LEVEL_FILE.test(parts[0]);
  return !IGNORED_FOLDERS.has(parts[0].toLowerCase());
}

/** The folders that are missions: those that hold a mission.qvm. A shared folder beside them is not. */
function missionFolders(files) {
  return new Set(files
    .filter(f => /(^|\/)mission\.qvm$/i.test(f.path))
    .map(f => key(f.path.slice(0, f.path.lastIndexOf('/')))));
}

/** The mission folder a large file waits for, or null for one read up front. */
function deferredTo(file, folders) {
  if (file.size < LARGE) return null;
  for (let at = file.path.lastIndexOf('/'); at > 0; at = file.path.lastIndexOf('/', at - 1)) {
    const folder = key(file.path.slice(0, at));
    if (folders.has(folder)) return folder;
  }
  return null;
}

function write(file, bytes) {
  const path = `${root}/${file.path}`;
  fs.mkdirTree(path.slice(0, path.lastIndexOf('/')));
  fs.writeFile(path, bytes);
}

async function readAll(files, progress) {
  let done = 0;
  const total = files.reduce((sum, f) => sum + f.size, 0);
  // A few at a time: one read at once leaves the disk idle between them, and all at once holds every
  // file's bytes twice while they are written.
  const queue = [...files];
  const worker = async () => {
    for (let file = queue.shift(); file; file = queue.shift()) {
      write(file, await source.read(file));
      done += file.size;
      progress?.(done, total, file.path);
    }
  };
  await Promise.all(Array.from({ length: 8 }, worker));
}

/**
 * Reads the installation into the file system, all but the missions' large files.
 * @param {object} from A source: fromDirectory, fromFileList or fromUrl.
 * @param {object} fileSystem The runtime's Emscripten FS.
 * @param {string} at Where the game reads its installation.
 * @param {(done: number, total: number, path: string) => void} progress
 */
export async function load(from, fileSystem, at, progress) {
  source = from;
  fs = fileSystem;
  root = at;

  const files = (await source.list()).filter(wanted);
  const folders = missionFolders(files);
  const now = [];
  for (const file of files) {
    const mission = deferredTo(file, folders);
    if (mission === null) {
      now.push(file);
      continue;
    }

    if (!missions.has(mission)) missions.set(mission, { files: [], state: 'waiting' });
    missions.get(mission).files.push(file);
  }

  await readAll(now, progress);
  return { files: now.length, deferred: files.length - now.length };
}

/** Starts reading a mission's large files, if nothing has yet. */
function fetchMission(mission) {
  mission.state = 'reading';
  readAll(mission.files).then(
    () => { mission.state = 'ready'; },
    error => { mission.state = 'failed'; console.error(`A mission's files could not be read: ${error}`); });
}

/** What the runtime imports, registered under 'openigi-files'. */
export const imports = {
  missionReady: directory => {
    const mission = missions.get(key(directory));
    if (!mission) return true;
    if (mission.state === 'waiting') fetchMission(mission);
    return mission.state === 'ready';
  },
};
