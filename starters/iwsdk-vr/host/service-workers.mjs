// No service worker may register on a preview's origin. The preview ports are shared: every project's preview is
// served from http://127.0.0.1 on one of a few ports, so a worker one game registers would stay on that origin and
// could answer a later project's preview with bytes the check never saw. A browser fetches every worker script, for
// a registration or an update, with the `Service-Worker: script` request header; such a request gets 404 and no body,
// so the registration fails. Storage a game left on the origin is not touched here.

/** Answers 404 to any request carrying the `Service-Worker` header, and passes every other request on. */
export function refuseServiceWorkers(req, res, next) {
  if (req.headers['service-worker'] === undefined) {
    next();
    return;
  }
  res.statusCode = 404;
  res.end();
}
