// The player's own files -- settings.cfg, the profiles (openigi-config.qvm), the save slots -- kept in the page's IndexedDB.
//
// The runtime's file system lives in memory and is gone with the tab, so a player who came back found none of it: no
// profile, no settings, no saves, and the installation's config.qvm read again as if new. Before the game boots, what
// was kept is written back into the user folder; while it runs, whatever it wrote there is kept again -- every few
// seconds, when the tab is hidden and when the game closes. The caches (shader, font, GI) are rebuilt on their own and
// stay out of it.

const DATABASE = 'openigi-user';
const STORE = 'files';
const EVERY = 3000;
const SKIPPED = new Set(['cache']);

let opened = null;

function database() {
  opened ??= new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  return opened;
}

function done(transaction) {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error);
    transaction.onabort = () => reject(transaction.error);
  });
}

// Every file under the folder, by its path below it, with its stamp: size and time written.
function walk(fs, root, below = '', into = new Map()) {
  let names;
  try {
    names = fs.readdir(root + below);
  } catch {
    return into;
  }

  for (const name of names) {
    if (name === '.' || name === '..' || (below === '' && SKIPPED.has(name))) continue;
    const relative = `${below}/${name}`;
    const stat = fs.stat(root + relative);
    if (fs.isDir(stat.mode)) {
      walk(fs, root, relative, into);
    } else if (fs.isFile(stat.mode)) {
      into.set(relative, `${stat.size}:${+stat.mtime}`);
    }
  }

  return into;
}

/**
 * Puts the kept files back into the user folder, and keeps them from then on.
 * @param {object} fs The runtime's Emscripten FS.
 * @param {string} root The game's user folder (SettingsPaths.GetUserDirectory).
 * @returns {Promise<{files: number, flush: () => Promise<void>}>} How many came back, and a flush to call as the game ends.
 */
export async function keep(fs, root) {
  root = root.replace(/\/+$/, '');
  let db = null;
  try {
    db = await database();
  } catch (error) {
    console.warn(`The player's files can't be kept here (${error?.message ?? error}): settings and saves last only this visit.`);
    return { files: 0, flush: async () => {} };
  }

  // Back into the folder.
  const read = db.transaction(STORE, 'readonly');
  const store = read.objectStore(STORE);
  const [keys, values] = await Promise.all([
    new Promise((resolve, reject) => { const r = store.getAllKeys(); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); }),
    new Promise((resolve, reject) => { const r = store.getAll(); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); }),
  ]);
  for (let i = 0; i < keys.length; i++) {
    const path = root + keys[i];
    fs.mkdirTree(path.slice(0, path.lastIndexOf('/')));
    fs.writeFile(path, values[i]);
  }

  // What the folder holds now is what is kept; from here on only a change is written.
  let known = walk(fs, root);
  let running = null;
  const sync = () => {
    running ??= (async () => {
      try {
        const now = walk(fs, root);
        const write = db.transaction(STORE, 'readwrite');
        const files = write.objectStore(STORE);
        let changes = 0;
        for (const [path, stamp] of now) {
          if (known.get(path) !== stamp) {
            files.put(fs.readFile(root + path), path);
            changes++;
          }
        }

        for (const path of known.keys()) {
          if (!now.has(path)) {
            files.delete(path);
            changes++;
          }
        }

        await done(write);
        known = now;
        if (changes) console.log(`Kept ${changes} change(s) to the player's files.`);
      } catch (error) {
        console.warn(`The player's files could not be kept: ${error?.message ?? error}`);
      } finally {
        running = null;
      }
    })();
    return running;
  };

  setInterval(sync, EVERY);
  document.addEventListener('visibilitychange', () => { if (document.hidden) sync(); });
  addEventListener('pagehide', () => sync());
  return { files: keys.length, flush: sync };
}
