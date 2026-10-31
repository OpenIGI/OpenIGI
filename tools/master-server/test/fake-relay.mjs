// A stand-in coturn for the relay tests: answers a STUN binding, and a TURN allocate only with a login minted from its
// secret (the TURN REST scheme coturn's use-auth-secret checks), over TCP as the Worker's probe speaks it.
import { createHash, createHmac } from "node:crypto";
import net from "node:net";

const MAGIC = 0x2112a442;
const REALM = "openigi.com";
const NONCE = "fakenonce0123456";

function message(type, transaction, attributes = []) {
  const body = Buffer.concat(
    attributes.map(([t, value]) => {
      const head = Buffer.alloc(4);
      head.writeUInt16BE(t, 0);
      head.writeUInt16BE(value.length, 2);
      return Buffer.concat([head, value, Buffer.alloc((4 - (value.length % 4)) % 4)]);
    }),
  );
  const header = Buffer.alloc(20);
  header.writeUInt16BE(type, 0);
  header.writeUInt16BE(body.length, 2);
  header.writeUInt32BE(MAGIC, 4);
  transaction.copy(header, 8);
  return Buffer.concat([header, body]);
}

function attributes(bytes) {
  const out = new Map();
  for (let at = 20; at + 4 <= bytes.length; ) {
    const type = bytes.readUInt16BE(at);
    const length = bytes.readUInt16BE(at + 2);
    if (!out.has(type)) out.set(type, { value: bytes.subarray(at + 4, at + 4 + length), at });
    at += 4 + ((length + 3) & ~3);
  }
  return out;
}

const errorCode = (code) => Buffer.from([0, 0, Math.floor(code / 100), code % 100]);

function loginValid(bytes, attrs, secret) {
  const integrity = attrs.get(0x0008);
  const username = attrs.get(0x0006)?.value.toString();
  if (!integrity || !username || Number(username.split(":")[0]) < Date.now() / 1000) return false;
  const password = createHmac("sha1", secret).update(username).digest("base64");
  const key = createHash("md5").update(`${username}:${REALM}:${password}`).digest();
  const signed = Buffer.from(bytes.subarray(0, integrity.at));
  signed.writeUInt16BE(integrity.at - 20 + 24, 2);
  return createHmac("sha1", key).update(signed).digest().equals(integrity.value);
}

/** Starts a fake relay on 127.0.0.1 (any free port unless given); resolves to { port, logins, close() }. */
export function startFakeRelay(secret, port = 0) {
  const sockets = new Set();
  const relay = { logins: 0 };
  const server = net.createServer((socket) => {
    sockets.add(socket);
    socket.on("close", () => sockets.delete(socket));
    socket.on("error", () => {});
    let buffered = Buffer.alloc(0);
    socket.on("data", (data) => {
      buffered = Buffer.concat([buffered, data]);
      while (buffered.length >= 20 && buffered.length >= 20 + buffered.readUInt16BE(2)) {
        const bytes = buffered.subarray(0, 20 + buffered.readUInt16BE(2));
        buffered = buffered.subarray(bytes.length);
        const type = bytes.readUInt16BE(0);
        const transaction = bytes.subarray(8, 20);
        const attrs = attributes(bytes);
        if (type === 0x0001) {
          socket.write(message(0x0101, transaction, [[0x0020, Buffer.from([0, 1, 0, 0, 0, 0, 0, 0])]]));
        } else if (type === 0x0003 && !attrs.has(0x0008)) {
          socket.write(message(0x0113, transaction, [[0x0009, errorCode(401)], [0x0014, Buffer.from(REALM)], [0x0015, Buffer.from(NONCE)]]));
        } else if (type === 0x0003 && loginValid(bytes, attrs, secret)) {
          relay.logins++;
          socket.write(message(0x0103, transaction, [[0x000d, Buffer.from([0, 0, 2, 88])]]));
        } else {
          socket.write(message(0x0113, transaction, [[0x0009, errorCode(401)], [0x0014, Buffer.from(REALM)], [0x0015, Buffer.from(NONCE)]]));
        }
      }
    });
  });

  return new Promise((resolve) => {
    server.listen(port, "127.0.0.1", () => {
      relay.port = server.address().port;
      relay.close = () =>
        new Promise((done) => {
          for (const s of sockets) s.destroy();
          server.close(() => done());
        });
      resolve(relay);
    });
  });
}
