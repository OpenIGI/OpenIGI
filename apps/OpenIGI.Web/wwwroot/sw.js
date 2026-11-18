// The page itself, kept, so the game opens with no network once it has been played: the game's files are kept by
// game-files.js (Cache Storage 'openigi-data'), and this keeps what loads them -- the page, its scripts, the runtime.
//
// What is fetched from the network first, falling back to the copy kept: the page and the files beside it, whose names
// do not change from one release to the next, so a release is picked up at the next visit. What is kept and served
// from the copy: the runtime under _framework, whose every name carries its content's hash, so a copy is never stale --
// a new release asks for new names.
//
// Which files the page needs is said by the page (main.js) once the game has booted: every one of its own it fetched.
// That fills what was fetched before this worker ran (the first visit's page and scripts) and lets the runtime files of
// earlier releases go, which nothing else would ever remove.
//
// Each site this is served under (the stable one at /, the beta at /beta/) keeps its own copies: a release of one does
// not replace the other's.

const SCOPE = new URL(self.registration.scope);
const SHELL = `openigi-shell ${SCOPE.pathname}`;
const RUNTIME = `openigi-runtime ${SCOPE.pathname}`;
const FRAMEWORK = `${SCOPE.pathname}_framework/`;
const PAGE = SCOPE.href; // The page is kept under the site's own address, whatever its query (?game=igi2).

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

const ours = url => url.origin === SCOPE.origin && url.pathname.startsWith(SCOPE.pathname);
const isPage = url => url.pathname === SCOPE.pathname || url.pathname === `${SCOPE.pathname}index.html`;
const isRuntime = url => url.pathname.startsWith(FRAMEWORK);

/** Worth keeping: a whole, successful, same-site answer. */
const keepable = response => response && response.ok && response.status === 200 && response.type === 'basic';

/** The network's answer, kept on the way through; the copy kept when the network has none. */
async function networkFirst(request, key) {
  const cache = await caches.open(SHELL);
  try {
    const response = await fetch(request);
    if (keepable(response)) await cache.put(key, response.clone()).catch(() => {});
    return response;
  } catch (error) {
    const kept = await cache.match(key);
    if (kept) return kept;
    throw error;
  }
}

/** The copy kept; fetched and kept the first time. */
async function keptFirst(request) {
  const cache = await caches.open(RUNTIME);
  const kept = await cache.match(request.url);
  if (kept) return kept;
  const response = await fetch(request);
  if (keepable(response)) await cache.put(request.url, response.clone()).catch(() => {});
  return response;
}

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (!ours(url)) return; // The game's files, the master server, Discord: not this worker's.

  if (request.mode === 'navigate') {
    if (isPage(url)) event.respondWith(networkFirst(request, PAGE));
    return; // Any other page under the site (a download link) goes its own way.
  }

  if (isRuntime(url)) {
    event.respondWith(keptFirst(request));
    return;
  }

  // A file of the page's that is kept already: fresh when the network has it, the copy when not. One never kept goes to
  // the network as usual, until the page names it.
  event.respondWith((async () => {
    const cache = await caches.open(SHELL);
    if (!(await cache.match(url.href))) return fetch(request);
    return networkFirst(request, url.href);
  })());
});

/** The page's word on what it needs: keep any of those not kept yet, and let the runtime files nothing needs go. */
async function keepOnly(urls) {
  const wanted = urls.map(u => new URL(u, SCOPE)).filter(ours);
  const shell = await caches.open(SHELL);
  const runtime = await caches.open(RUNTIME);
  if (!(await shell.match(PAGE))) {
    const page = await fetch(PAGE).catch(() => null);
    if (keepable(page)) await shell.put(PAGE, page);
  }

  for (const url of wanted) {
    const cache = isRuntime(url) ? runtime : shell;
    if (isPage(url) || (await cache.match(url.href))) continue;
    const response = await fetch(url.href).catch(() => null);
    if (keepable(response)) await cache.put(url.href, response).catch(() => {});
  }

  // Runtime files of an earlier release: no page asks for them again.
  const needed = new Set(wanted.filter(isRuntime).map(u => u.href));
  if (needed.size === 0) return;
  for (const request of await runtime.keys()) {
    if (!needed.has(request.url)) await runtime.delete(request);
  }
}

self.addEventListener('message', event => {
  if (event.data?.type === 'keep' && Array.isArray(event.data.urls)) event.waitUntil(keepOnly(event.data.urls));
});
