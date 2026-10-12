// SENECARE service worker — template served by /sw.js with the current asset versions injected.
// Hand-written (no Workbox build step): the whole strategy fits in this file.
//
//  - App shell (offline page, CSS, JS, fonts, icons): precached at install, cache-first.
//  - Pages: network-first, falling back to the last copy, then to the offline page.
//  - Data fragments (neighborhood search, GET /api): stale-while-revalidate.
//  - Offline-capable forms: if a POST fails for lack of network, it is saved in the outbox
//    and sent by Background Sync (or when a page sees the network come back).
//  - Updates: the new worker waits until the user taps « Mettre à jour ».

const VERSION = "__VERSION__";
const PRECACHE = __PRECACHE__;
const OFFLINE_POSTS = __OFFLINE_POSTS__;
const OFFLINE_PAGE = "/hors-ligne";

const SHELL_CACHE = `sc-shell-${VERSION}`;
const PAGES_CACHE = "sc-pages";
const DATA_CACHE = "sc-data";

importScripts(PRECACHE.find((url) => url.startsWith("/js/outbox.js")));

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(SHELL_CACHE).then((cache) => cache.addAll(PRECACHE)));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();
      await Promise.all(names.filter((n) => n.startsWith("sc-shell-") && n !== SHELL_CACHE).map((n) => caches.delete(n)));
      await self.clients.claim();
    })(),
  );
});

self.addEventListener("message", (event) => {
  const type = event.data?.type;
  if (type === "SKIP_WAITING") self.skipWaiting();
  if (type === "SIGNED_OUT") {
    // Shared phones: nothing personal stays on the device after signing out.
    event.waitUntil(Promise.all([caches.delete(PAGES_CACHE), caches.delete(DATA_CACHE), self.scOutbox.clear()]));
  }
});

self.addEventListener("sync", (event) => {
  if (event.tag === "sc-outbox") event.waitUntil(flushAndNotify());
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  if (request.method === "POST" && request.mode === "navigate" && OFFLINE_POSTS.includes(url.pathname)) {
    event.respondWith(postOrQueue(request));
    return;
  }

  if (request.method !== "GET") return;

  if (request.mode === "navigate") {
    event.respondWith(networkFirstPage(request));
  } else if (/^\/(css|js|lib|fonts|img)\//.test(url.pathname) || url.pathname === "/manifest.webmanifest") {
    event.respondWith(cacheFirst(request));
  } else if (url.pathname.startsWith("/quartiers/") || url.pathname.startsWith("/api/")) {
    event.respondWith(staleWhileRevalidate(request, event));
  }
});

async function networkFirstPage(request) {
  try {
    const response = await fetch(request);
    if (response.ok && response.type === "basic" && !response.redirected) {
      const cache = await caches.open(PAGES_CACHE);
      cache.put(request, response.clone());
    }
    return response;
  } catch {
    return (await caches.match(request, { ignoreSearch: false })) ?? (await caches.match(OFFLINE_PAGE));
  }
}

async function cacheFirst(request) {
  const cached = await caches.match(request);
  if (cached) return cached;
  const response = await fetch(request);
  if (response.ok) (await caches.open(SHELL_CACHE)).put(request, response.clone());
  return response;
}

async function staleWhileRevalidate(request, event) {
  const cache = await caches.open(DATA_CACHE);
  const cached = await cache.match(request);
  const refresh = fetch(request)
    .then((response) => {
      if (response.ok) cache.put(request, response.clone());
      return response;
    })
    .catch(() => cached);
  if (cached) {
    event.waitUntil(refresh);
    return cached;
  }
  return refresh;
}

async function postOrQueue(request) {
  const copy = request.clone();
  try {
    return await fetch(request);
  } catch {
    const form = await copy.formData();
    await self.scOutbox.add(new URL(copy.url).pathname, self.scOutbox.fromFormData(form), "");
    if (self.registration.sync) {
      await self.registration.sync.register("sc-outbox").catch(() => {});
    }
    return Response.redirect(`${OFFLINE_PAGE}?en-attente=1`, 303);
  }
}

async function flushAndNotify() {
  const pending = await self.scOutbox.flush();
  const clients = await self.clients.matchAll({ includeUncontrolled: true });
  clients.forEach((client) => client.postMessage({ type: "OUTBOX", pending }));
  if (pending > 0) throw new Error("Outbox not empty: retry later");
}
