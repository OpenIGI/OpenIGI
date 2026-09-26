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
 * GameTitle.Igi1 (src/OpenIGI.Core/Titles/GameTitle.cs). Keep the two in step.
 */
const SIGNATURE_FILE = 'config.qvm';
const ASSET_ROOTS = ['missions', 'common', 'language', 'menusystem'];

/** The sequel's signature file (GameTitle.Igi2); its folder has everything above as well. */
const SEQUEL_SIGNATURE_FILE = 'igi2.exe';

/** A folder the game cannot be played from, with what to tell the person. */
export class InstallationError extends Error {
  constructor(message) {
    super(message);
    this.name = 'InstallationError';
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
    ? `"${picked}" is I.G.I. 2: Covert Strike. This page plays Project I.G.I.; choose its folder instead.`
    : `"${picked}" is not a Project I.G.I. installation. Choose the folder the game is installed in: the ` +
      'one holding IGI.exe, config.qvm and the missions folder (in a retail install, the "pc" folder ' +
      "inside the game's folder).");
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
