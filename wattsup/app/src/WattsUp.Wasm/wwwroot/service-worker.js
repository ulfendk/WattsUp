// In development, always fetch from the network — caching here would make iterating on a change
// harder (it wouldn't show up on the first reload). The MSBuild <ServiceWorker> item swaps this
// file out for service-worker.published.js at publish time; that one does the real caching.
self.addEventListener('fetch', () => { });
