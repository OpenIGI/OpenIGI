// The installation, read from where the person keeps it into the runtime's in-memory file system, where
// the game reads it by path as it would read a disk.
//
// Everything is read up front except the large files of the missions -- a level's terrain, lightmaps and
// textures, a few dozen megabytes each -- which are read when the mission is chosen: the load gate
// (WebLoadGate.cs) asks, and this starts reading and answers yes once they are all in place. The small
// files of every mission are read up front because the game scans them all at start-up to learn what
// the missions are. Hosted data says for itself what waits and for which folders (pack.py's "for"): an
// I.G.I. 2 location's shared pack waits for the first of its missions, and the gameplay sounds for any.
// Either game is read the same way; which one it is comes from the folder (judge) or the hosted index.

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
 * What proves a folder is an installation of each game: the signature file and the asset folders of
 * GameTitle.Igi1 and GameTitle.Igi2 (src/Core/OpenIGI.Core/Titles/GameTitle.cs). Keep the two in step.
 * The sequel first: its folder has the first game's signature file as well.
 */
const ASSET_ROOTS = ['missions', 'common', 'language', 'menusystem'];
const SIGNATURES = [
  { title: 'igi2', file: 'igi2.exe' },
  { title: 'igi1', file: 'config.qvm' },
];

/**
 * The games, by id, as the page names them. hosted: whether the site serves the game's files; a game it does not is
 * played only from the person's own installation.
 */
export const TITLES = {
  igi1: { name: 'Project I.G.I.', exe: 'IGI.exe', hosted: true },
  igi2: { name: 'I.G.I. 2: Covert Strike', exe: 'igi2.exe', hosted: false },
};

/**
 * How each game's own folder is split into what is read up front and what waits, as tools/web-data/pack.py splits the
 * hosted files -- keep the two in step. shared: files every mission waits for and the menu does not. locationPacks:
 * whether a location's shared folder (missions/<location>/common) waits for the first of its missions, as I.G.I. 2's
 * do; read up front, every location's would be in memory at once.
 */
const SPLITS = {
  igi1: { shared: [], locationPacks: false },
  igi2: { shared: ['common/sounds/sounds.res'], locationPacks: true },
};

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
  if (!ASSET_ROOTS.every(name => kinds.get(name) === 'directory')) return null;
  return SIGNATURES.find(signature => kinds.get(signature.file) === 'file')?.title ?? null;
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
 * @param {string} wanted The game the page is set to, which wins when the folder holds both.
 * @returns {Promise<{ name: string, title: string }>} The one that is the installation ('' for the picked
 *   folder) and which game it is.
 */
async function locate(picked, candidates, wanted) {
  let other = null;
  for (const candidate of candidates) {
    const title = judge(await candidate.kinds());
    if (title === wanted) return { name: candidate.name, title };
    other ??= title && { name: candidate.name, title };
  }
  if (other) return other;

  const { name, exe } = TITLES[wanted];
  throw new InstallationError(`No ${name} installation in /${picked}. Choose the folder with ${exe} in it.`);
}

/**
 * The installation in a flat list of paths relative to the picked folder, as a file input or a manifest
 * gives them.
 * @param {string} picked The picked folder's name, for the message.
 * @param {Array<{ path: string }>} files
 * @param {string} wanted The game the page is set to.
 * @returns {Promise<{ prefix: string, title: string }>} The prefix to strip from every path ('' or 'pc/', for
 *   instance) and which game it is.
 */
async function locateInList(picked, files, wanted) {
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

  const { name, title } = await locate(picked, candidates, wanted);
  return { prefix: name ? `${name}/` : '', title };
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
 * @param {string} wanted The game the page is set to; a folder of the other game is taken as that game.
 */
export async function fromDirectory(handle, wanted) {
  // Only the top levels of the picked folder and of the folders directly inside it are listed to find
  // the installation; nothing is walked until it is found.
  const top = await entriesOf(handle);
  const candidates = [{ name: '', kinds: async () => kindsOf(top) }];
  const folders = [...top.values()].filter(entry => entry.kind === 'directory');
  for (const folder of folders.sort((a, b) => candidateOrder(a.name, b.name))) {
    candidates.push({ name: folder.name, kinds: async () => kindsOf(await entriesOf(folder)) });
  }

  const { name: found, title } = await locate(handle.name, candidates, wanted);
  const root = found ? await handle.getDirectoryHandle(found) : handle;

  return {
    name: found ? `${handle.name}/${found}` : handle.name,
    title,
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
/** Each game's folder is kept under its own key; the first game's under the key it has always had. */
const folderKey = title => (title === 'igi1' ? 'game' : `game:${title}`);

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
export async function rememberFolder(title, handle) {
  try {
    await transact('readwrite', store => store.put(handle, folderKey(title)));
  } catch (error) {
    console.warn(`The game folder could not be remembered: ${error}`); // A private window, for one.
  }
}

/** The folder of a game remembered from the last visit, or null. */
export async function rememberedFolder(title) {
  try {
    return (await transact('readonly', store => store.get(folderKey(title)))) ?? null;
  } catch {
    return null;
  }
}

/** Forgets a game's remembered folder. */
export async function forgetFolder(title) {
  try {
    await transact('readwrite', store => store.delete(folderKey(title)));
  } catch {
    // Nothing was remembered, or nothing can be.
  }
}

/** Whether a remembered folder can be read without asking: the browser kept the permission, so no click is needed. */
export async function readable(handle) {
  try {
    return (await handle.queryPermission({ mode: 'read' })) === 'granted';
  } catch {
    return false;
  }
}

/**
 * Opens a remembered folder again. Asks the browser for permission to read it when it does not hold it
 * still, so it must be called from a click.
 * @param {FileSystemDirectoryHandle} handle
 * @param {string} wanted The game it was remembered for.
 */
export async function reopen(handle, wanted) {
  if ((await handle.queryPermission({ mode: 'read' })) !== 'granted'
    && (await handle.requestPermission({ mode: 'read' })) !== 'granted') {
    throw new InstallationError(`Permission to read /${handle.name} was not given. Press Play to be asked again.`, true);
  }

  try {
    return await fromDirectory(handle, wanted);
  } catch (error) {
    if (error.name === 'NotFoundError') throw new InstallationError(`/${handle.name} is no longer there. Choose the game folder again.`);
    throw error;
  }
}

/**
 * A folder picked through a plain file input (webkitdirectory), where the API above is missing. The
 * browser has listed every file under the picked folder by then; none of them is read here.
 * @param {FileList} list
 * @param {string} wanted The game the page is set to.
 */
export async function fromFileList(list, wanted) {
  const picked = list[0]?.webkitRelativePath.split('/')[0] ?? '';
  const all = [...list].map(f => ({ path: f.webkitRelativePath.split('/').slice(1).join('/'), size: f.size, file: f }));
  const { prefix, title } = await locateInList(picked, all, wanted);
  const files = rebase(all, prefix);
  return {
    name: prefix ? `${picked}/${prefix.slice(0, -1)}` : picked,
    title,
    async list() { return files; },
    async read(file) { return new Uint8Array(await file.file.arrayBuffer()); },
  };
}

/**
 * An installation served over HTTP beside a manifest.json of { path, size } entries: for development and
 * for automated runs, where nothing can click a folder picker.
 * @param {string} base The URL of the folder, ending in a slash.
 * @param {string} wanted The game the page is set to.
 */
export async function fromUrl(base, wanted) {
  const response = await fetch(`${base}manifest.json`);
  if (!response.ok) throw new InstallationError(`${base}manifest.json could not be fetched (${response.status}).`);
  const all = await response.json();
  const { prefix, title } = await locateInList(base, all, wanted);
  const files = rebase(all, prefix);
  const at = base + prefix.split('/').filter(Boolean).map(part => `${encodeURIComponent(part)}/`).join('');
  return {
    name: at,
    title,
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

/** Deletes every bundle downloaded from one game's folder, leaving the other game's. */
export async function clearHosted(base) {
  try {
    const cache = await dataCache();
    if (!cache) return;
    for (const request of await cache.keys()) {
      if (request.url.startsWith(base)) await cache.delete(request);
    }
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
  const counts = new Map(); // Bundle name -> how many files it holds.
  for (const file of index.files) counts.set(file.bundle, (counts.get(file.bundle) ?? 0) + 1);
  const left = new Map(); // Bundle name -> how many of its files are still to be read this time.

  const source = {
    name: 'openigi.com',
    hosted: true,
    title: index.title ?? 'igi1',

    /**
     * The folders each bundle's files wait for, as pack.py wrote them ("*" for any mission's), or null for an
     * index older than that, whose split is the page's own (deferredTo).
     */
    waitsFor: Object.values(index.bundles).some(b => b.for)
      ? Object.fromEntries(Object.entries(index.bundles).map(([name, b]) => [name, b.for ?? []]))
      : null,
    /** Called as a bundle comes in: (bundle name, bytes so far, its size). */
    onDownload: null,

    async list() { return index.files; },

    async read(file) {
      // Unpacked once per reading of its files: again after a mission's files were let go (evict).
      if (!unpacked.has(file.bundle)) {
        unpacked.set(file.bundle, fetchBundle(file.bundle).then(gunzip));
        left.set(file.bundle, counts.get(file.bundle));
      }
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
      // What every mission needs first, then the missions in their order: by name with its numbers read as
      // numbers, so level10 follows level9 and a location's shared pack ("...-common") comes before its levels.
      const shared = name => (index.bundles[name].for?.includes('*') ? 0 : 1);
      const names = Object.keys(index.bundles)
        .filter(name => name !== 'base')
        .sort((a, b) => shared(a) - shared(b) || a.localeCompare(b, 'en', { numeric: true }));
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

// What waits to be read: groups of files, each read whole when a folder that needs it is asked for. A group is
// a mission folder's large files, or for hosted data a bundle. needs maps a folder (lower-cased) to the groups
// it waits for; everyMission holds those every mission waits for.
const groups = new Map(); // Name -> { files, state: 'waiting' | 'reading' | 'ready' | 'failed' }
const needs = new Map(); // Folder -> Set of group names.
const everyMission = new Set();

/**
 * How many of the folders last asked for keep their files in memory. The file system is the runtime's heap,
 * which cannot grow past 4 GB, and an I.G.I. 2 level is up to a hundred megabytes: the files of a mission
 * left a while ago are deleted, and read again (from the download cache, for hosted data) if it is chosen
 * again. A mission asks for its own folder and its location's shared one, and a co-op game may borrow
 * another level's pack, so this keeps three missions' worth.
 */
const RESIDENT_FOLDERS = 6;
const recent = []; // Folders asked for, the latest first.

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

/**
 * The groups of a source that says nothing of its own about what waits (a folder, or an index older than pack.py's
 * "for"): each file's group put on it as file.group, and the folders each group waits for -- pack.py's split, made here.
 * @returns {Object<string, string[]>} Group name -> the folders that wait for it ("*" for any mission).
 */
function folderGroups(files, title) {
  const split = SPLITS[title] ?? SPLITS.igi1;
  const missions = missionFolders(files);
  const packs = split.locationPacks
    ? new Set([...missions].map(folder => `${folder.slice(0, folder.lastIndexOf('/'))}/common`))
    : new Set();
  const waitsFor = {};
  for (const file of files) {
    const path = key(file.path);
    file.group = null;
    if (split.shared.includes(path)) {
      file.group = 'shared';
      waitsFor.shared = ['*'];
      continue;
    }
    if (file.size < LARGE) continue;
    for (let at = path.lastIndexOf('/'); at > 0; at = path.lastIndexOf('/', at - 1)) {
      const folder = path.slice(0, at);
      if (missions.has(folder) || packs.has(folder)) {
        file.group = folder;
        waitsFor[folder] = [folder];
        break;
      }
    }
  }
  return waitsFor;
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
function defer(group, file) {
  if (!groups.has(group)) groups.set(group, { files: [], state: 'waiting' });
  groups.get(group).files.push(file);
}

function need(folder, group) {
  if (folder === '*') {
    everyMission.add(group);
    return;
  }
  if (!needs.has(key(folder))) needs.set(key(folder), new Set());
  needs.get(key(folder)).add(group);
}

/**
 * Reads the installation into the file system, all but what waits for a mission: the missions' large files,
 * or for hosted data the bundles the index says wait.
 * @param {object} from A source: fromHosted, fromDirectory, fromFileList or fromUrl.
 * @param {object} fileSystem The runtime's Emscripten FS.
 * @param {string} at Where the game reads its installation.
 * @param {(done: number, total: number, path: string) => void} progress
 */
export async function load(from, fileSystem, at, progress) {
  source = from;
  fs = fileSystem;
  root = at;

  const files = (await source.list()).filter(wanted);
  // Hosted data names its groups (bundles) and what waits for them; anything else is split here the same way.
  const waitsFor = source.waitsFor ?? folderGroups(files, source.title);
  const groupOf = source.waitsFor ? file => file.bundle : file => file.group;
  for (const [group, folders] of Object.entries(waitsFor)) folders.forEach(folder => need(folder, group));
  const now = [];
  for (const file of files) {
    const group = groupOf(file);
    if (group && waitsFor[group]?.length) defer(group, file);
    else now.push(file);
  }

  await readAll(now, progress);
  placeSignature();
  return { files: now.length, deferred: files.length - now.length };
}

/**
 * The game's signature file, empty, where the source has none: the host takes a folder without it for no
 * installation, and nothing reads it -- so the hosted data does not ship a three-megabyte executable.
 */
function placeSignature() {
  const file = SIGNATURES.find(s => s.title === source.title)?.file;
  if (!file) return;
  const existing = fs.readdir(root).find(name => name.toLowerCase() === file);
  if (!existing) fs.writeFile(`${root}/${file}`, new Uint8Array(0));
}

/** Starts reading a group's files, if nothing has yet. */
function fetchGroup(group) {
  group.state = 'reading';
  readAll(group.files).then(
    () => { group.state = 'ready'; },
    error => { group.state = 'failed'; console.error(`A mission's files could not be read: ${error}`); });
}

/** Deletes the files of every group no folder kept in memory needs, so that they are read again when next asked for. */
function evict() {
  const kept = new Set(everyMission);
  for (const folder of recent) needs.get(folder)?.forEach(name => kept.add(name));
  for (const [name, group] of groups) {
    if (kept.has(name) || group.state !== 'ready') continue;
    for (const file of group.files) {
      try {
        fs.unlink(`${root}/${file.path}`);
      } catch {
        // Already gone.
      }
    }
    group.state = 'waiting';
    console.log(`Let go of ${name}'s files; they are read again if it is chosen again.`);
  }
}

/** Notes a folder as the latest asked for; when that pushes one out of memory, its files go. */
function touch(folder) {
  if (recent[0] === folder) return;
  const at = recent.indexOf(folder);
  if (at >= 0) recent.splice(at, 1);
  recent.unshift(folder);
  if (recent.length > RESIDENT_FOLDERS) {
    recent.length = RESIDENT_FOLDERS;
    evict();
  }
}

/** What the runtime imports, registered under 'openigi-files'. */
export const imports = {
  /** Whether everything a mission or location folder waits for is in place; asking starts reading it. */
  missionReady: directory => {
    const folder = key(directory);
    const names = [...(needs.get(folder) ?? []), ...everyMission];
    if (names.length === 0) return true;
    touch(folder);
    let ready = true;
    for (const name of names) {
      const group = groups.get(name);
      if (!group) continue;
      if (group.state === 'waiting') fetchGroup(group);
      ready &&= group.state === 'ready';
    }
    return ready;
  },
};
