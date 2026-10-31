// Just enough STUN and TURN (RFC 8489, RFC 8656) to tell from outside whether a relay works: a Binding request that
// proves coturn answers, then an authenticated Allocate that proves it holds the secret its logins are minted from.
// Spoken over TCP, the one transport a Worker can open; a STUN message over TCP is the message itself, back to back.

const MAGIC = 0x2112a442;

export const BINDING = 0x0001;
export const ALLOCATE = 0x0003;
const SUCCESS = 0x0100;
const ERROR = 0x0110;

const USERNAME = 0x0006;
const MESSAGE_INTEGRITY = 0x0008;
const ERROR_CODE = 0x0009;
const REQUESTED_TRANSPORT = 0x0019;
const REALM = 0x0014;
const NONCE = 0x0015;

const encoder = new TextEncoder();
const decoder = new TextDecoder();

/** A request: its type, a fresh transaction ID, and its attributes as [type, bytes] pairs. */
export function request(type, attributes = [], transaction = crypto.getRandomValues(new Uint8Array(12))) {
  return { type, transaction, attributes };
}

/** The message's bytes; with `key`, MESSAGE-INTEGRITY over everything before it, as the long-term credential asks. */
export async function encode(message, key = null) {
  const body = [];
  for (const [type, value] of message.attributes) {
    body.push(attribute(type, value));
  }

  let length = body.reduce((n, a) => n + a.length, 0);
  if (key) {
    length += 24;
  }

  const out = new Uint8Array(20 + length);
  const view = new DataView(out.buffer);
  view.setUint16(0, message.type);
  view.setUint16(2, length);
  view.setUint32(4, MAGIC);
  out.set(message.transaction, 8);
  let at = 20;
  for (const a of body) {
    out.set(a, at);
    at += a.length;
  }

  if (key) {
    const hmac = await crypto.subtle.importKey("raw", key, { name: "HMAC", hash: "SHA-1" }, false, ["sign"]);
    const mac = new Uint8Array(await crypto.subtle.sign("HMAC", hmac, out.subarray(0, at)));
    out.set(attribute(MESSAGE_INTEGRITY, mac), at);
  }

  return out;
}

function attribute(type, value) {
  const padded = (value.length + 3) & ~3;
  const out = new Uint8Array(4 + padded);
  const view = new DataView(out.buffer);
  view.setUint16(0, type);
  view.setUint16(2, value.length);
  out.set(value, 4);
  return out;
}

/** The length of the whole message at the front of `bytes`, once its header is there. */
export function messageLength(bytes) {
  return bytes.length < 20 ? null : 20 + new DataView(bytes.buffer, bytes.byteOffset).getUint16(2);
}

/** A message's type, transaction and attributes (a map of type to bytes, the first of each). */
export function decode(bytes) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.length < 20 || view.getUint32(4) !== MAGIC) {
    throw new Error("not a STUN message");
  }

  const attributes = new Map();
  const end = 20 + view.getUint16(2);
  for (let at = 20; at + 4 <= end; ) {
    const type = view.getUint16(at);
    const length = view.getUint16(at + 2);
    if (!attributes.has(type)) {
      attributes.set(type, bytes.slice(at + 4, at + 4 + length));
    }

    at += 4 + ((length + 3) & ~3);
  }

  return { type: view.getUint16(0), transaction: bytes.slice(8, 20), attributes };
}

/** ERROR-CODE as a number (401, 438, ...), or null. */
export function errorCode(message) {
  const value = message.attributes.get(ERROR_CODE);
  return value ? value[2] * 100 + value[3] : null;
}

/** Whether `reply` answers `sent`: same transaction, and the success or error class of the same method. */
function answers(reply, sent) {
  return reply.transaction.every((b, i) => b === sent.transaction[i]) && [sent.type | SUCCESS, sent.type | ERROR].includes(reply.type);
}

/**
 * Probes a relay over TCP: a Binding, then an Allocate with the login `username`/`password` (the TURN REST scheme's,
 * minted from the relay's secret). Resolves to null when both worked, or to what failed. The allocation dies with the
 * connection, closed straight after.
 */
export async function probe(connect, { host, port }, username, password, timeoutMs = 4000) {
  let socket;
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error("timed out")), timeoutMs);
  });

  try {
    socket = connect({ hostname: host, port });
    const exchange = (async () => {
      const writer = socket.writable.getWriter();
      const reader = socket.readable.getReader();
      let buffered = new Uint8Array(0);
      const roundTrip = async (message, key) => {
        await writer.write(await encode(message, key));
        for (;;) {
          const length = messageLength(buffered);
          if (length !== null && buffered.length >= length) {
            const reply = decode(buffered.slice(0, length));
            buffered = buffered.slice(length);
            if (answers(reply, message)) {
              return reply;
            }

            continue;
          }

          const { value, done } = await reader.read();
          if (done) {
            throw new Error("connection closed");
          }

          const joined = new Uint8Array(buffered.length + value.length);
          joined.set(buffered);
          joined.set(value, buffered.length);
          buffered = joined;
        }
      };

      const binding = await roundTrip(request(BINDING));
      if (binding.type !== (BINDING | SUCCESS)) {
        return "STUN binding refused";
      }

      // UDP relaying, the only kind a game asks for (and coturn here allows).
      const transport = [REQUESTED_TRANSPORT, new Uint8Array([17, 0, 0, 0])];
      const challenge = await roundTrip(request(ALLOCATE, [transport]));
      const realm = challenge.attributes.get(REALM);
      const nonce = challenge.attributes.get(NONCE);
      if (errorCode(challenge) !== 401 || !realm || !nonce) {
        return `TURN allocate without a login was not challenged (${errorCode(challenge) ?? "success"})`;
      }

      const key = new Uint8Array(await crypto.subtle.digest("MD5", encoder.encode(`${username}:${decoder.decode(realm)}:${password}`)));
      const attributes = [transport, [USERNAME, encoder.encode(username)], [REALM, realm], [NONCE, nonce]];
      const allocated = await roundTrip(request(ALLOCATE, attributes), key);
      if (allocated.type !== (ALLOCATE | SUCCESS)) {
        return `TURN login refused (${errorCode(allocated)}): is the relay's secret the one it was given?`;
      }

      return null;
    })();

    return await Promise.race([exchange, timeout]);
  } catch (error) {
    return error.message || String(error);
  } finally {
    clearTimeout(timer);
    try {
      socket?.close();
    } catch {
      // Never opened.
    }
  }
}
