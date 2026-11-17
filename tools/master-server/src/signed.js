// Requests signed by an installation's key, as SignedRequest.cs makes them: an ECDSA P-256 signature (r then s) over the
// method, path, time and the body's SHA-256, a line each, made within ten minutes of now. Shared by everything that takes
// a request only from a key: the community maps (the map publisher's key) and accounts (the player's key, the one game
// servers know a player by).
//
// The key comes in X-OpenIGI-Key as base64, either as a SubjectPublicKeyInfo (MapIdentity.cs) or as the 65 bytes of an
// uncompressed point (IPlayerIdentity.cs). Its id is the first sixteen hex digits of the SHA-256 of exactly those bytes,
// so a player key's id is PlayerIdentities.IdOf's.

export const MAX_SKEW_SECONDS = 600;
const RAW_POINT_SIZE = 65;

// A request refused, with the status to answer it with.
export class Refusal extends Error {
  constructor(status, message, problems = undefined) {
    super(message);
    this.status = status;
    this.problems = problems;
  }
}

// The key that signed a request, checked: { id, raw }. Throws a Refusal (401) when it isn't signed, or not well.
export async function signedBy(request, url, body, now = Date.now()) {
  const publicKey = request.headers.get("x-openigi-key");
  const signature = request.headers.get("x-openigi-signature");
  const time = Number(request.headers.get("x-openigi-time"));
  if (!publicKey || !signature || !Number.isFinite(time)) {
    throw new Refusal(401, "Sign the request with your installation's key.");
  }

  if (Math.abs(now / 1000 - time) > MAX_SKEW_SECONDS) {
    throw new Refusal(401, "The request was signed too long ago (or your clock is wrong).");
  }

  let keyBytes;
  let key;
  try {
    keyBytes = fromBase64(publicKey);
    const raw = keyBytes.length === RAW_POINT_SIZE && keyBytes[0] === 4;
    key = await crypto.subtle.importKey(raw ? "raw" : "spki", keyBytes, { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
  } catch {
    throw new Refusal(401, "The key is not one.");
  }

  let good = false;
  try {
    const text = signedText(request.method, url.pathname, time, await sha256Hex(body));
    good = await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, fromBase64(signature), new TextEncoder().encode(text));
  } catch {
    good = false;
  }

  if (!good) {
    throw new Refusal(401, "The signature does not check out.");
  }

  return { id: (await sha256Hex(keyBytes)).slice(0, 16), raw: keyBytes.length === RAW_POINT_SIZE };
}

// The text a signature is over.
export function signedText(method, path, time, bodyHash) {
  return `${method.toUpperCase()}\n${path}\n${time}\n${bodyHash}`;
}

export async function sha256Hex(bytes) {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
  return [...digest].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function fromBase64(text) {
  const raw = atob(text);
  return Uint8Array.from(raw, (c) => c.charCodeAt(0));
}

export function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json; charset=utf-8" } });
}
