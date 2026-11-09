// The player's identity in the browser: an ECDSA P-256 key pair made once and kept in IndexedDB, its private half never
// extractable -- the page can sign with it but nothing, this code included, can read it out. The game's own copy of the
// protocol (PlayerIdentities) says what is signed; this only signs and checks.
//
// Signing and checking are asynchronous in the browser; the game hands back the promise's result as a task it polls.

const DB = 'openigi-identity';
const STORE = 'keys';
const KEY = 'player';
const ALGORITHM = { name: 'ECDSA', namedCurve: 'P-256' };
const SIGN = { name: 'ECDSA', hash: 'SHA-256' };

let pair = null;
let publicRaw = null;
let loading = null;

function open() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function stored(db, mode, act) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const request = act(tx.objectStore(STORE));
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function load() {
  try {
    const db = await open();
    pair = await stored(db, 'readonly', s => s.get(KEY));
    if (!pair) {
      pair = await crypto.subtle.generateKey(ALGORITHM, false, ['sign', 'verify']);
      await stored(db, 'readwrite', s => s.put(pair, KEY));
    }
  } catch (e) {
    // A private window, or storage refused: a key that lasts the visit.
    console.warn('OpenIGI: the player identity could not be kept:', e);
    pair = await crypto.subtle.generateKey(ALGORITHM, false, ['sign', 'verify']);
  }

  publicRaw = new Uint8Array(await crypto.subtle.exportKey('raw', pair.publicKey));
}

function toBase64(bytes) {
  let text = '';
  for (const b of bytes) text += String.fromCharCode(b);
  return btoa(text);
}

function fromBase64(text) {
  const raw = atob(text);
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

export const imports = {
  // Starts loading at once; the game asks whether it is ready before it joins.
  start: () => {
    loading ??= load();
  },

  ready: () => publicRaw !== null,

  publicKey: () => (publicRaw ? toBase64(publicRaw) : ''),

  sign: async (dataBase64) => {
    await (loading ??= load());
    const signature = await crypto.subtle.sign(SIGN, pair.privateKey, fromBase64(dataBase64));
    return toBase64(new Uint8Array(signature));
  },

  verify: async (publicKeyBase64, dataBase64, signatureBase64) => {
    try {
      const key = await crypto.subtle.importKey('raw', fromBase64(publicKeyBase64), ALGORITHM, false, ['verify']);
      return await crypto.subtle.verify(SIGN, key, fromBase64(signatureBase64), fromBase64(dataBase64));
    } catch {
      return false;
    }
  },
};
