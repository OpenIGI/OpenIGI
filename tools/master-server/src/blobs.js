// Files kept in a Durable Object's own storage, in chunks under one prefix (<prefix>:0, <prefix>:1, ...): a value there may
// be no larger than storage takes, and a file is put, read and let go of a few dozen chunks at a time. Shared by the community
// maps (a map version's package) and the runs (a replay).

export const CHUNK = 120 * 1024;

// Storage takes at most this many keys in one get or delete, and a put this many entries.
const BATCH = 128;
const PUT_BATCH = 100;

// The keys a file's chunks are under.
const keysOf = (prefix, chunks) => Array.from({ length: chunks }, (_, i) => `${prefix}:${i}`);

// Keeps a file's bytes under a prefix. Returns how many chunks it took.
export async function putBlob(storage, prefix, bytes) {
  const chunks = Math.ceil(bytes.length / CHUNK);
  let entries = {};
  for (let i = 0; i < chunks; i++) {
    entries[`${prefix}:${i}`] = bytes.slice(i * CHUNK, (i + 1) * CHUNK);
    if (Object.keys(entries).length === PUT_BATCH) {
      await storage.put(entries);
      entries = {};
    }
  }

  if (Object.keys(entries).length > 0) {
    await storage.put(entries);
  }

  return chunks;
}

// A file's bytes back, of the size it was kept at; throws when a chunk is missing.
export async function getBlob(storage, prefix, chunks, size) {
  const keys = keysOf(prefix, chunks);
  const found = new Map();
  for (let i = 0; i < keys.length; i += BATCH) {
    for (const [k, v] of await storage.get(keys.slice(i, i + BATCH))) found.set(k, v);
  }

  const body = new Uint8Array(size);
  let at = 0;
  for (const k of keys) {
    const chunk = found.get(k);
    if (!chunk) {
      throw new Error(`The file under ${prefix} is missing ${k}.`);
    }

    body.set(new Uint8Array(chunk), at);
    at += chunk.byteLength;
  }

  return body;
}

// Lets go of a file's chunks.
export async function dropBlob(storage, prefix, chunks) {
  const keys = keysOf(prefix, chunks);
  for (let i = 0; i < keys.length; i += BATCH) await storage.delete(keys.slice(i, i + BATCH));
}
