// Caches the app shell (via the hashed asset manifest the SDK generates at publish time) so
// repeat visits and offline reloads of an already-visited page work. Deliberately does NOT cache
// anything to the public price/tariff APIs — that data must always come from a real network
// fetch, or fail visibly, never a stale cached response. Offline access to prices already seen is
// handled by IndexedDB (see indexedDbInterop.js), which is the correct layer for that; this
// service worker's job is strictly the app shell.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', (event) => event.waitUntil(onInstall(event)));
self.addEventListener('activate', (event) => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', (event) => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

// Hosts that must never be served from this cache, whatever the fetch mode — see the file header.
const neverCacheHosts = ['api.energidataservice.dk', 'api.eloverblik.dk'];

async function onInstall() {
    console.info('WattsUp service worker: install');
    const assetsRequests = self.assetsManifest.assets
        .filter((asset) => offlineAssetsInclude.some((pattern) => pattern.test(asset.url)))
        .filter((asset) => !offlineAssetsExclude.some((pattern) => pattern.test(asset.url)))
        .map((asset) => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await cache.addAll(assetsRequests);
}

async function onActivate() {
    console.info('WattsUp service worker: activate');
    const cacheKeys = await caches.keys();
    await Promise.all(
        cacheKeys.filter((key) => key.startsWith(cacheNamePrefix) && key !== cacheName).map((key) => caches.delete(key)));
}

async function onFetch(event) {
    const requestUrl = new URL(event.request.url);
    if (neverCacheHosts.includes(requestUrl.host)) {
        return fetch(event.request);
    }

    if (event.request.method === 'GET') {
        const shouldServeIndexHtml = event.request.mode === 'navigate';
        const cache = await caches.open(cacheName);
        const cachedResponse = await cache.match(shouldServeIndexHtml ? 'index.html' : event.request);
        if (cachedResponse) {
            return cachedResponse;
        }
    }

    return fetch(event.request);
}
