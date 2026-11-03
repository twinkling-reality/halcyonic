import { toolFoldersFor } from '@halcyonic/integration-codex';
import { ancestors, SYSTEM_READS, subpaths } from '../seatbelt.ts';

export { SANDBOX_EXEC } from '../seatbelt.ts';

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
 * the boundary, and what it leaves is what a renderer running the agent's page would keep after an
 * exploit (AUDIT P2): no terminal, no signal to a process outside the profile, and no reads of the
 * folders that hold a person's, a drive's or another app's files.
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
    `(allow file-write* ${folder} ${CHROME_DEVICES})`,
    `(deny file-read* ${subpaths([scope.home, ...CHROME_READS_REFUSED])})`,
    `(allow file-read* ${folder})`,
    `(allow file-read-metadata ${ancestors([scope.folder])})`,
    // A terminal would let native code type into the shell that owns it (TIOCSTI), and the browser
    // has none in its own session; nothing below allows one again.
    '(deny file-read* file-write* file-ioctl (regex #"^/dev/tty"))',
    '(deny signal)',
    '(allow signal (target same-sandbox))',
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

/** The devices Chrome may write: `/dev/null` and its own descriptors, never a terminal (AUDIT P2). */
const CHROME_DEVICES = '(literal "/dev/null") (regex #"^/dev/fd/")';

/**
 * Folders whose reads the browser never needs and refuses (AUDIT P2): every person's home folder,
 * drives and network shares, every app's temporary and cache folders, and the package managers' data
 * and configuration. Its own folder and the browser's are allowed again after them.
 */
const CHROME_READS_REFUSED: readonly string[] = [
  '/Users',
  '/Volumes',
  '/private/tmp',
  '/private/var/folders',
  '/opt/homebrew/etc',
  '/opt/homebrew/var',
  '/usr/local/etc',
  '/usr/local/var',
  '/Library/Application Support',
  '/Library/Keychains',
];
