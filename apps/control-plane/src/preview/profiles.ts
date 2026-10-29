import { realpathSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';

/** Where macOS keeps the sandbox launcher; never looked up on PATH. */
export const SANDBOX_EXEC = '/usr/bin/sandbox-exec';

/** What a type check, a build or a preview server of a project from a starter may touch. */
export interface BuildScope {
  /** The person's home folder: nothing in it is readable but what the scope names. */
  readonly home: string;
  /** The project's folder, read only. */
  readonly project: string;
  /** The starter's installed folder: its shared dependencies and the host's config, read only. */
  readonly starter: string;
  /** The host's cache for this check: Vite's cache, the build and the logs, the only place written. */
  readonly cache: string;
  /** The host's controller and hand models, read only, or null. */
  readonly profiles: string | null;
}

/**
 * The Seatbelt profile for `tsc`, `vite build` and `vite preview` of a project from a starter (ADR
 * 0037): no outbound connection at all (the preview server only answers on loopback), writes only
 * in the check's cache, and reads of the home folder refused but for the project, the starter and
 * the cache. The project's own code is transformed here, never run.
 */
export function buildProfile(scope: BuildScope): string {
  const readable = [
    scope.project,
    scope.starter,
    scope.cache,
    ...(scope.profiles ? [scope.profiles] : []),
  ];
  return [
    '(version 1)',
    '(allow default)',
    '(deny network-outbound)',
    '(deny file-write*)',
    `(allow file-write* ${subpaths([scope.cache])} ${DEVICES})`,
    `(deny file-read* ${subpaths([scope.home])})`,
    `(allow file-read* ${subpaths(readable)})`,
    `(allow file-read-metadata ${ancestors(readable)})`,
    '',
  ].join('\n');
}

/** What headless Chrome may touch while it opens the check's page. */
export interface ChromeScope {
  /** The person's home folder: nothing in it is readable but Chrome's own folder. */
  readonly home: string;
  /** Chrome's own folder for this run: its profile, HOME, temporary and socket folders. */
  readonly folder: string;
  /** The preview's port on loopback, the only place Chrome may connect to. */
  readonly port: number;
}

/**
 * The Seatbelt profile headless Chrome runs inside for the check (ADR 0037, AUDIT X1 to X1c): an
 * outbound connection only to the preview's port; writes only in Chrome's own folder; reads of the
 * home folder refused but Chrome's folder; Chrome's updater not started; no URL handed to another
 * app through LaunchServices (a page's `mailto:` once opened Mail); inbound connections only on
 * loopback and Chrome's own sockets. Chrome's own sandbox cannot start inside it, so this profile is
 * the boundary.
 */
export function chromeProfile(scope: ChromeScope): string {
  if (!Number.isInteger(scope.port) || scope.port < 1 || scope.port > 65535) {
    throw new Error(`not a port: ${scope.port}`);
  }
  const folder = subpaths([scope.folder]);
  return [
    '(version 1)',
    '(allow default)',
    '(deny network-outbound)',
    `(allow network-outbound (remote ip "localhost:${scope.port}"))`,
    '(deny file-write*)',
    `(allow file-write* ${folder} ${DEVICES})`,
    `(deny file-read* ${subpaths([scope.home])})`,
    `(allow file-read* ${folder})`,
    `(allow file-read-metadata ${ancestors([scope.folder])})`,
    '(deny process-exec (regex #"/GoogleUpdater\\.app/"))',
    '(deny lsopen)',
    '(deny network-inbound)',
    `(allow network-inbound (local ip "localhost:*") (local unix-socket ${folder}))`,
    '',
  ].join('\n');
}

const DEVICES = '(literal "/dev/null") (regex #"^/dev/tty") (regex #"^/dev/fd/")';

function subpaths(paths: readonly string[]): string {
  return paths.map((path) => `(subpath ${JSON.stringify(resolved(path))})`).join(' ');
}

function ancestors(paths: readonly string[]): string {
  return paths.map((path) => `(path-ancestors ${JSON.stringify(resolved(path))})`).join(' ');
}

/**
 * The path as the file system resolves it, through its nearest folder that exists, so one not yet
 * made under `/var` reads `/private/var` as Seatbelt will see it once made.
 */
function resolved(path: string): string {
  let existing = path;
  const rest: string[] = [];
  while (true) {
    try {
      return join(realpathSync.native(existing), ...rest.reverse());
    } catch {
      const parent = dirname(existing);
      if (parent === existing) return path;
      rest.push(basename(existing));
      existing = parent;
    }
  }
}
