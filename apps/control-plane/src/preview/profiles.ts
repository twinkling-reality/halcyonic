import { realpathSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { toolFoldersFor } from '@halcyonic/integration-codex';

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
  /** Node, by absolute path, whose installation is read so it runs; the running Node's when not named. */
  readonly node?: string;
}

/**
 * The system's own paths a build reads to run, and nothing of a person's, another app's or a drive's:
 * libraries, frameworks, time zones, the system's configuration and its standard devices, as the
 * profile of a confined agent allows them (Codex's minimal platform defaults, ADR 0041), so the check
 * reads no more of the Mac than the agent whose files it checks (AUDIT P1).
 */
const SYSTEM_READS: readonly string[] = [
  `(allow file-read* (literal "/") ${[
    '/Library/Apple',
    '/Library/Filesystems/NetFSPlugins',
    '/Library/Preferences',
    '/System/Library/Frameworks',
    '/System/Library/PrivateFrameworks',
    '/System/Library/SubFrameworks',
    '/System/iOSSupport/System/Library/Frameworks',
    '/System/iOSSupport/System/Library/PrivateFrameworks',
    '/System/iOSSupport/System/Library/SubFrameworks',
    '/usr/lib',
    '/usr/share',
    '/bin',
    '/sbin',
    '/usr/bin',
    '/usr/sbin',
    '/usr/libexec',
    '/etc',
    '/private/etc',
    '/private/var/db/timezone',
    '/opt/homebrew/lib',
    '/usr/local/lib',
    '/dev/fd',
  ]
    .map((path) => `(subpath "${path}")`)
    .join(' ')})`,
  `(allow file-read* ${[
    '/System/Library/CoreServices',
    '/System/Library/CoreServices/SystemVersion.plist',
    '/System/Library/CoreServices/.SystemVersionPlatform.plist',
    '/private/var/db/DarwinDirectory/local/recordStore.data',
    '/private/var/db/eligibilityd/eligibility.plist',
    '/dev/null',
    '/dev/zero',
    '/dev/random',
    '/dev/urandom',
    '/dev/autofs_nowait',
    '/dev/dtracehelper',
  ]
    .map((path) => `(literal "${path}")`)
    .join(' ')})`,
  '(allow file-read-metadata (literal "/etc") (literal "/tmp") (literal "/var") (literal "/dev") (regex #"^/dev/") (subpath "/var") (subpath "/private/var") (path-ancestors "/System/Volumes/Data/private"))',
  '(allow file-read-metadata (literal "/System/Volumes") (literal "/System/Volumes/Data") (literal "/System/Volumes/Data/Users"))',
];

/**
 * The Seatbelt profile for `tsc`, `vite build` and `vite preview` of a project from a starter (ADR
 * 0037): no outbound connection at all (the preview server only answers on loopback), writes only
 * in the check's cache, and reads closed by default (AUDIT P1): the system's own paths, Node's
 * installation outside the home folder, and the project, the starter, the cache and the models. The
 * project's own code is transformed here, never run, but its `tsconfig.json` and its sources name
 * what the compiler and Vite read, and what they read can come back: the type check's first lines
 * reach the agent's next instruction (FX1), and an asset Vite reads is served in the built copy.
 */
export function buildProfile(scope: BuildScope): string {
  const readable = [
    scope.project,
    scope.starter,
    scope.cache,
    ...(scope.profiles ? [scope.profiles] : []),
  ];
  const node = toolFoldersFor(scope.node ?? process.execPath);
  return [
    '(version 1)',
    '(allow default)',
    '(deny network-outbound)',
    '(deny file-write*)',
    `(allow file-write* ${subpaths([scope.cache])} ${DEVICES})`,
    '(deny file-read*)',
    ...SYSTEM_READS,
    `(allow file-read* ${subpaths(node)})`,
    `(allow file-read-metadata ${ancestors(node)})`,
    // Later rules win: Node's folders never open the home folder, which only the scope's own reach.
    `(deny file-read* ${subpaths([scope.home])})`,
    `(allow file-read* ${subpaths(readable)})`,
    `(allow file-read-metadata ${ancestors(readable)})`,
    // Nothing the build runs may open another app, read the pasteboard or send Apple events (AUDIT S2).
    '(deny lsopen)',
    '(deny mach-lookup (global-name-regex #"^com\\.apple\\.pasteboard\\."))',
    '(deny appleevent-send)',
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
  /** The browser's own folder, read whole: the copy in the data folder, under the home folder. */
  readonly browser: string;
  /** Whether it may reach the system's audio services, so a game's sound is heard (the play's, never the check's). */
  readonly sound?: boolean;
}

/**
 * The only system services and drivers the browser may reach (S1): its own processes' rendezvous,
 * and the GPU and Metal's compiler to draw. Found on macOS 26.7 by refusing everything and allowing
 * one at a time, then removing each that was not needed; never LaunchServices, the privacy service,
 * the window server or the pasteboard (docs/internal/validation/iwsdk-starter.md).
 */
export const BROWSER_SERVICES: readonly string[] = [
  '(allow mach-lookup (global-name-regex #"^(org\\.chromium\\.Chromium|com\\.google\\.(Chrome|chrome\\.for\\.testing))\\.MachPortRendezvousServer\\.[0-9]+$"))',
  '(allow mach-lookup (global-name "com.apple.MTLCompilerService"))',
  '(allow iokit-open (iokit-user-client-class "AGXDeviceUserClient"))',
  '(allow iokit-open (iokit-user-client-class "IOSurfaceRootUserClient"))',
];

/** The audio services sound out needs; the microphone stays refused by the play's own rules. */
export const SOUND_SERVICES: readonly string[] = [
  '(allow mach-lookup (global-name "com.apple.audio.audiohald"))',
  '(allow mach-lookup (global-name "com.apple.audio.AudioComponentRegistrar"))',
  '(allow mach-lookup (global-name "com.apple.audio.AudioSession"))',
];

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
    `(allow file-read* ${subpaths([scope.browser])})`,
    `(allow file-read-metadata ${ancestors([scope.browser])})`,
    '(deny mach-lookup)',
    '(deny iokit-open)',
    ...BROWSER_SERVICES,
    ...(scope.sound === true ? SOUND_SERVICES : []),
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
