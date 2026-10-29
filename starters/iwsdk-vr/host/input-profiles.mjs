// Controller models for a project made from the IWSDK starter, served by the host instead of a CDN.
// `@iwsdk/xr-input` builds every model's address from one constant naming cdn.jsdelivr.net; the host config
// rewrites that constant to a path of the preview's own origin and serves there only the files that
// input-profiles.json lists, each only while its size and SHA-256 equal the listing. The files live in the
// host's data directory, never in the project. Node built-ins only, so the repository can test it.
import { createHash } from 'node:crypto';
import { closeSync, constants, fstatSync, openSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/** The address prefix the rewritten constant points at, relative to the page. */
export const PROFILES_PREFIX = '__halcyonic/input-profiles';

const CDN_CONSTANT =
  "const DEFAULT_PROFILES_PATH = 'https://cdn.jsdelivr.net/npm/@webxr-input-profiles/assets@1.0/dist/profiles';";
const LOCAL_CONSTANT = `const DEFAULT_PROFILES_PATH = './${PROFILES_PREFIX}';`;

/** True for the one module that holds the constant, by its path inside the package. */
export function isProfilesModule(path) {
  return /[\\/]@iwsdk[\\/]xr-input[\\/]dist[\\/]gamepad[\\/]input-profiles\.js$/.test(path.split('?')[0]);
}

/** The module's code with the constant pointed at the preview's own origin; refuses code without it. */
export function rewriteProfilesModule(code) {
  const at = code.indexOf(CDN_CONSTANT);
  if (at === -1 || code.indexOf(CDN_CONSTANT, at + 1) !== -1) {
    throw new Error('The controller profiles module has changed; the host config must be checked again.');
  }
  return code.replace(CDN_CONSTANT, LOCAL_CONSTANT);
}

/**
 * The tracked listing: format 1, the package's licence notice, and each model's path under dist/profiles/, size
 * and SHA-256. The host's folder mirrors the package: `LICENSE.md` and `dist/profiles/...`.
 */
export function readManifest(path) {
  const manifest = JSON.parse(readFileSync(path, 'utf8'));
  const entry = (file, pattern) =>
    typeof file?.path === 'string' &&
    pattern.test(file.path) &&
    Number.isInteger(file.size) &&
    /^[0-9a-f]{64}$/.test(file.sha256);
  if (
    manifest.format !== 1 ||
    !entry(manifest.notice, /^LICENSE\.md$/) ||
    !Array.isArray(manifest.files) ||
    !manifest.files.every((file) => entry(file, /^dist\/profiles\/[a-z0-9-]+\/[a-z0-9-]+\.glb$/))
  ) {
    throw new Error('unknown input profiles listing');
  }
  return manifest;
}

/** The listed file an address names (`<profile>/<file>.glb` after the prefix), or null. */
export function listedFile(manifest, address) {
  return manifest.files.find((file) => file.path === `dist/profiles/${address}`) ?? null;
}

/**
 * The bytes of a listed file from the host's folder, or null: opened without following a link, read only if it is
 * a regular file of the listed size, returned only if its SHA-256 is the listed one.
 */
export function readVerified(folder, file) {
  let fd;
  try {
    fd = openSync(join(folder, file.path), constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  } catch {
    return null;
  }
  try {
    const stat = fstatSync(fd);
    if (!stat.isFile() || stat.size !== file.size) return null;
    const bytes = readFileSync(fd);
    if (bytes.length !== file.size) return null;
    return createHash('sha256').update(bytes).digest('hex') === file.sha256 ? bytes : null;
  } finally {
    closeSync(fd);
  }
}

/**
 * What the build adds to the app: each listed model the host's folder holds, verified, at the address the page
 * asks for, and the licence notice beside them when any model is added. Nothing without a folder.
 */
export function buildFiles(folder, manifest) {
  if (!folder) return [];
  const models = manifest.files
    .map((file) => ({ file, bytes: readVerified(folder, file) }))
    .filter(({ bytes }) => bytes !== null)
    .map(({ file, bytes }) => ({ fileName: `${PROFILES_PREFIX}/${file.path.slice('dist/profiles/'.length)}`, source: bytes }));
  const notice = models.length > 0 ? readVerified(folder, manifest.notice) : null;
  return notice === null ? models : [...models, { fileName: `${PROFILES_PREFIX}/LICENSE.md`, source: notice }];
}

/**
 * A Connect middleware for the dev and preview servers, mounted at `/${PROFILES_PREFIX}`: a listed, verified file
 * as a glTF binary, anything else 404. Without a folder from the host, every request is 404.
 */
export function profilesMiddleware(folder, manifest) {
  return (req, res) => {
    // Only an exact listed path matches, so no address can name another file.
    const address = (req.url ?? '').split('?')[0].replace(/^\//, '');
    const file = listedFile(manifest, address);
    const bytes = folder && file ? readVerified(folder, file) : null;
    if (bytes === null || req.method !== 'GET') {
      res.statusCode = 404;
      res.end();
      return;
    }
    res.setHeader('Content-Type', 'model/gltf-binary');
    res.setHeader('Content-Length', String(bytes.length));
    res.end(bytes);
  };
}
