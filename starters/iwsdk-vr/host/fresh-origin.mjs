// A preview's origin starts fresh in every browser that loads it. Previews share a few ports on 127.0.0.1, so an
// origin is shared with every earlier project served on that port; a browser keeps that origin's HTTP cache and
// storage between them. Every response is sent with Cache-Control: no-store, so no browser's HTTP cache can show an
// earlier check's bytes, and every top-level page load (Sec-Fetch-Dest: document) with Clear-Site-Data "storage" and
// "cache", so local storage, IndexedDB, the Cache API and any service worker an earlier game left are cleared before
// the page runs. The cost: a game's own saves do not survive a reload of its preview. Cookies are not cleared: on
// 127.0.0.1 they span every port, so clearing them would reset every other preview's too.

/** Sends Cache-Control: no-store on every response, and Clear-Site-Data on every top-level page load. */
export function freshOrigin(req, res, next) {
  res.setHeader('Cache-Control', 'no-store');
  if (req.headers['sec-fetch-dest'] === 'document') res.setHeader('Clear-Site-Data', '"storage", "cache"');
  next();
}
