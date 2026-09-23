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

/**
 * A folder the person picked (File System Access API).
 * @param {FileSystemDirectoryHandle} handle
 */
export function directorySource(handle) {
  return {
    name: handle.name,
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
      await walk(handle, '');
      return files;
    },
    async read(file) {
      return new Uint8Array(await (await file.entry.getFile()).arrayBuffer());
    },
  };
}

/**
 * A folder picked through a plain file input (webkitdirectory), where the API above is missing.
 * @param {FileList} list
 */
export function fileListSource(list) {
  const files = [...list].map(f => ({ path: f.webkitRelativePath.split('/').slice(1).join('/'), size: f.size, file: f }));
  return {
    name: list[0]?.webkitRelativePath.split('/')[0] ?? '',
    async list() { return files; },
    async read(file) { return new Uint8Array(await file.file.arrayBuffer()); },
  };
}

/**
 * An installation served over HTTP beside a manifest.json of { path, size } entries: for development and
 * for automated runs, where nothing can click a folder picker.
 * @param {string} base The URL of the folder, ending in a slash.
 */
export function urlSource(base) {
  return {
    name: base,
    async list() { return (await fetch(`${base}manifest.json`)).json(); },
    async read(file) {
      const response = await fetch(base + file.path.split('/').map(encodeURIComponent).join('/'));
      if (!response.ok) throw new Error(`${file.path}: ${response.status}`);
      return new Uint8Array(await response.arrayBuffer());
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
 * @param {object} from A source: directorySource, fileListSource or urlSource.
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
