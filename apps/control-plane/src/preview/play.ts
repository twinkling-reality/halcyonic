import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, lstatSync, readdirSync, readFileSync } from 'node:fs';
import { cp, mkdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { type ChromeRun, launchChrome } from './chrome.ts';
import { PlayViewer, type ViewerMessage } from './play-viewer.ts';
import { chromeProfile } from './profiles.ts';

/**
 * Play on my laptop (ADR 0037, P-A): a checked game's page runs on the Mac in a contained headless
 * browser, and Halcyonic shows its frames in a viewer of its own. The browser is a copy of Chrome for
 * Testing kept in Halcyonic's data folder, never the person's own Chrome, checked at every start; it
 * runs inside the check's own profile (outbound only to the preview's port, nothing of the home
 * folder), with the microphone and camera refused, every permission blocked in its own settings, and
 * only mouse and key input from the viewer.
 */

/** The only browser a game plays in: a separate build, whose permissions are never the person's Chrome's. */
/**
 * Every file chrome-headless-shell is made of. The check's and the play's browser is a copy of it in
 * Halcyonic's data folder (S1): Google's headless-only build of Chrome, which, unlike the full browser,
 * starts with LaunchServices, the window server and the privacy service refused. Never the person's
 * own Chrome.
 */
export const BROWSER_FILES: readonly string[] = [
  'chrome-headless-shell',
  'headless_command_resources.pak',
  'headless_lib_data.pak',
  'headless_lib_strings.pak',
  'icudtl.dat',
  'libEGL.dylib',
  'libGLESv2.dylib',
];

/** Every permission a game's page could ask the play's browser for, refused to it (S1). */
export const DENIED_PERMISSIONS: readonly string[] = [
  'microphone',
  'camera',
  'geolocation',
  'notifications',
  'midi',
  'clipboard-read',
  'clipboard-write',
  'display-capture',
  'local-fonts',
  'window-management',
  'idle-detection',
  'keyboard-lock',
  'pointer-lock',
  'storage-access',
];

/** The binary inside the browser copy. */
export function browserBinary(folder: string): string {
  return join(folder, 'chrome-headless-shell');
}

/**
 * The whole browser folder as a manifest (AUDIT M1): one line per entry, sorted by its relative path,
 * with its kind (`d` folder, `f` plain file), its mode and, for a plain file, its SHA-256. A link, or
 * anything but a plain file or folder, is never followed: it is returned by its path instead, and
 * refuses the folder (the pinned build holds none), as is a name with a control character or a
 * space, which the lines could not hold apart. Null when the folder is not there.
 */
export function browserManifest(
  folder: string,
): string[] | { readonly link: string } | { readonly other: string } | null {
  const lines: string[] = [];
  const walk = (relative: string): { link: string } | { other: string } | null => {
    for (const name of readdirSync(join(folder, relative)).sort()) {
      const path = relative === '' ? name : `${relative}/${name}`;
      // A name with a control character or a space could make two trees' lines read alike: refused.
      if ([...name].some((c) => c.charCodeAt(0) < 0x20 || c.charCodeAt(0) === 0x7f || c === ' '))
        return { other: path };
      const stats = lstatSync(join(folder, path));
      const mode = (stats.mode & 0o777).toString(8);
      if (stats.isSymbolicLink()) return { link: path };
      if (stats.isDirectory()) {
        lines.push(`d ${mode} ${path}`);
        const stop = walk(path);
        if (stop !== null) return stop;
      } else if (stats.isFile()) {
        const sha256 = createHash('sha256')
          .update(readFileSync(join(folder, path)))
          .digest('hex');
        lines.push(`f ${mode} ${path} ${sha256}`);
      } else {
        return { other: path };
      }
    }
    return null;
  };
  try {
    if (!lstatSync(folder).isDirectory()) return null;
    const stop = walk('');
    if (stop !== null) return stop;
  } catch {
    return null;
  }
  return lines.sort(byPath);
}

/** The pin over a manifest: the SHA-256 of its lines. */
export function manifestDigest(lines: readonly string[]): string {
  return createHash('sha256').update(lines.join('\n')).digest('hex');
}

/** The manifest of the pinned build (chrome-headless-shell 156.0.8078.4 for mac-arm64), as the repository keeps it. */
export const PINNED_BROWSER_MANIFEST: readonly string[] = readFileSync(
  new URL('./browser-manifest.txt', import.meta.url),
  'utf8',
)
  .split('\n')
  .filter((line) => line !== '');

/** The path of a manifest line. */
function pathOf(line: string): string {
  return line.split(' ')[2] ?? '';
}

function byPath(a: string, b: string): number {
  return pathOf(a) < pathOf(b) ? -1 : pathOf(a) > pathOf(b) ? 1 : 0;
}

/** The first entry, by path, that differs between two manifests, in words. */
function firstDifference(actual: readonly string[], expected: readonly string[]): string {
  const have = new Map(actual.map((line) => [pathOf(line), line]));
  const want = new Map(expected.map((line) => [pathOf(line), line]));
  for (const path of [...new Set([...have.keys(), ...want.keys()])].sort()) {
    if (!have.has(path)) return `${path} is missing`;
    if (!want.has(path)) return `${path} is extra`;
    if (have.get(path) !== want.get(path)) return `${path} has changed`;
  }
  return 'its entries are in another order';
}

/**
 * Why the browser copy may not run now, or null: every file chrome-headless-shell is named by must be
 * there, nothing in the folder may be a link or anything but a plain file or folder, and the digest
 * of its manifest must be the pin. When the expected manifest is known (the repository keeps the
 * pinned build's), the refusal names the first entry that differs, is extra or is missing.
 */
export function browserProblem(
  folder: string,
  sha256: string,
  expected: readonly string[] | null = manifestDigest(PINNED_BROWSER_MANIFEST) === sha256
    ? PINNED_BROWSER_MANIFEST
    : null,
): string | null {
  if (!existsSync(browserBinary(folder))) return `There is no browser at ${folder}.`;
  if (BROWSER_FILES.some((file) => !existsSync(join(folder, file))))
    return `The browser at ${folder} is not chrome-headless-shell.`;
  const manifest = browserManifest(folder);
  if (manifest === null) return `There is no browser at ${folder}.`;
  if ('link' in manifest) return `The browser at ${folder} holds a link, ${manifest.link}.`;
  if ('other' in manifest)
    return `The browser at ${folder} holds ${manifest.other}, which is not a plain file or folder.`;
  if (manifestDigest(manifest) === sha256) return null;
  if (expected !== null && manifestDigest(expected) === sha256)
    return `The browser at ${folder} has changed since it was set up: ${firstDifference(manifest, expected)}.`;
  return `The browser at ${folder} has changed since it was set up.`;
}

/**
 * Puts the browser copy in Halcyonic's data folder from `source`, the chrome-headless-shell folder the
 * owner named, unless a copy whose manifest's digest is `sha256` is there already, and returns its path.
 * The source itself is never run. Throws, leaving no copy, when the source is not that browser.
 */
export async function installBrowser(
  source: string,
  sha256: string,
  dataDir: string,
): Promise<string> {
  // Not named for its binary: macOS takes a folder named chrome-headless-shell (or headless-shell)
  // around it for an app, and the browser then registers with LaunchServices, which its profile
  // refuses, and aborts (S1).
  const copy = join(dataDir, 'browser', 'current');
  if (browserProblem(copy, sha256) === null) return copy;
  // A copy that is not the recorded browser is never kept, whether or not the source can replace it.
  await rm(join(dataDir, 'browser'), { recursive: true, force: true });
  const problem = browserProblem(source, sha256);
  if (problem !== null) throw new Error(problem);
  await mkdir(join(dataDir, 'browser'), { recursive: true, mode: 0o700 });
  // The source holds no link (checked above), so the copy is of plain files and folders only.
  await cp(source, copy, { recursive: true, preserveTimestamps: true });
  const copied = browserProblem(copy, sha256);
  if (copied !== null) {
    await rm(join(dataDir, 'browser'), { recursive: true, force: true });
    throw new Error(copied);
  }
  return copy;
}

/**
 * The play browser's Seatbelt profile: the check's, with the audio services sound out needs, and the
 * microphone and camera refused last.
 */
export function playProfile(scope: {
  readonly home: string;
  readonly folder: string;
  readonly port: number;
  readonly browser: string;
}): string {
  return [
    chromeProfile({ ...scope, sound: true }).trimEnd(),
    '(deny device-microphone)',
    '(deny device-camera)',
    '',
  ].join('\n');
}

/** The play browser's switches: headless, driven over a pipe, nothing remembered, sound allowed to start. */
export function playArguments(folder: string, port: number): string[] {
  return [
    '--headless=new',
    // Chrome's own sandbox cannot start inside Seatbelt; the outer profile is the boundary.
    '--no-sandbox',
    `--user-data-dir=${join(folder, 'profile')}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--use-angle=metal',
    '--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1',
    '--proxy-server=http://127.0.0.1:9',
    `--proxy-bypass-list=127.0.0.1:${port}`,
    '--force-webrtc-ip-handling-policy=disable_non_proxied_udp',
    '--disable-background-networking',
    '--disable-component-update',
    '--disable-domain-reliability',
    '--disable-sync',
    '--no-pings',
    '--use-mock-keychain',
    '--disable-crash-reporter',
    '--disable-breakpad',
    `--crash-dumps-dir=${join(folder, 'tmp')}`,
    '--disable-features=WebGPU,MediaRouter,AsyncDns,DnsOverHttps',
    '--autoplay-policy=no-user-gesture-required',
    // A permission request is answered no at once, never left waiting for a prompt no one sees (S1).
    '--deny-permission-prompts',
    // Media devices are stand-ins, never the Mac's microphone or camera, so asking for one is
    // answered (refused) at once instead of waiting on the system's devices (S1).
    '--use-fake-device-for-media-stream',
    '--window-size=1280,720',
    '--remote-debugging-pipe',
    'about:blank',
  ];
}

/** Chrome's content setting value for "block". */
const BLOCK = 2;

/**
 * The play profile's own settings (its `Default/Preferences`), written before the first start: every
 * permission a game could ask for blocked, printing off, downloads kept in the play folder unasked.
 * Never managed preferences, never the person's Chrome settings.
 */
export function playPreferences(folder: string): Record<string, unknown> {
  const blocked = [
    'notifications',
    'geolocation',
    'media_stream_mic',
    'media_stream_camera',
    'clipboard',
    'usb_guard',
    'hid_guard',
    'serial_guard',
    'bluetooth_guard',
    'midi_sysex',
    'window_placement',
    'local_fonts',
    'idle_detection',
    'keyboard_lock',
    'file_system_write_guard',
  ];
  return {
    profile: {
      default_content_setting_values: Object.fromEntries(blocked.map((name) => [name, BLOCK])),
    },
    printing: { enabled: false },
    download: { prompt_for_download: false, default_directory: join(folder, 'downloads') },
  };
}

/** The page's size the viewer may point at. */
const WIDTH = 1280;
const HEIGHT = 720;
const BUTTONS: Readonly<Record<string, number>> = { left: 1, right: 2, middle: 4 };
const MOUSE: Readonly<Record<string, string>> = {
  move: 'mouseMoved',
  down: 'mousePressed',
  up: 'mouseReleased',
};

/** A DevTools input call for the game's page: the only two methods the viewer can cause. */
export type PlayInput =
  | { readonly method: 'Input.dispatchMouseEvent'; readonly params: Record<string, unknown> }
  | { readonly method: 'Input.dispatchKeyEvent'; readonly params: Record<string, unknown> };

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const exactly = (value: Record<string, unknown>, keys: readonly string[]) =>
  Object.keys(value).every((key) => keys.includes(key));
const onPage = (n: unknown, max: number): n is number =>
  typeof n === 'number' && Number.isFinite(n) && n >= 0 && n <= max;
const shortText = (s: unknown, max: number): s is string =>
  typeof s === 'string' && s.length > 0 && s.length <= max;

/**
 * Turns one message from the viewer into the input call it stands for, or null. Only a mouse move,
 * press or release with a known button on the page, or a key down or up with a short key and code,
 * is accepted, with no other field; nothing else the viewer sends ever reaches the browser.
 */
/**
 * The page's input call for a viewer message, or null. `held` is the buttons the viewer holds down
 * now (left 1, right 2, middle 4), so a move during a drag carries them as a real drag's does.
 */
export function playInput(message: unknown, held = 0): PlayInput | null {
  if (!isRecord(message)) return null;
  if (message.type === 'mouse') {
    if (!exactly(message, ['type', 'action', 'x', 'y', 'button'])) return null;
    const type = typeof message.action === 'string' ? MOUSE[message.action] : undefined;
    if (type === undefined || !onPage(message.x, WIDTH) || !onPage(message.y, HEIGHT)) return null;
    if (message.action === 'move') {
      if (message.button !== undefined) return null;
      const pressed = Object.keys(BUTTONS).find((name) => (held & (BUTTONS[name] ?? 0)) !== 0);
      return {
        method: 'Input.dispatchMouseEvent',
        params: { type, x: message.x, y: message.y, button: pressed ?? 'none', buttons: held },
      };
    }
    const button = typeof message.button === 'string' ? BUTTONS[message.button] : undefined;
    if (button === undefined) return null;
    return {
      method: 'Input.dispatchMouseEvent',
      params: {
        type,
        x: message.x,
        y: message.y,
        button: message.button,
        buttons: message.action === 'down' ? button : 0,
        clickCount: 1,
      },
    };
  }
  if (message.type === 'key') {
    if (!exactly(message, ['type', 'action', 'key', 'code'])) return null;
    if (message.action !== 'down' && message.action !== 'up') return null;
    if (!shortText(message.key, 24) || !shortText(message.code, 24)) return null;
    const params: Record<string, unknown> = {
      type: message.action === 'down' ? 'keyDown' : 'keyUp',
      key: message.key,
      code: message.code,
    };
    // A printable key types its character, as a real key down does.
    if (message.action === 'down' && [...message.key].length === 1) params.text = message.key;
    return { method: 'Input.dispatchKeyEvent', params };
  }
  return null;
}

/** A play the host started, as its record in the data folder keeps it, so a crash can be cleaned up. */
export interface PlayRecord {
  readonly project: string;
  readonly pid: number;
  readonly check_id: string;
  readonly folder: string;
}

/** Every play record in the data folder; a record that cannot be read is skipped. */
export function readPlayRecords(dataDir: string): PlayRecord[] {
  const dir = join(dataDir, 'play');
  if (!existsSync(dir)) return [];
  const records: PlayRecord[] = [];
  for (const name of readdirSync(dir)) {
    if (!name.endsWith('.json')) continue;
    try {
      const value = JSON.parse(readFileSync(join(dir, name), 'utf8')) as Record<string, unknown>;
      if (
        typeof value.pid === 'number' &&
        typeof value.check_id === 'string' &&
        typeof value.folder === 'string'
      )
        records.push({
          project: name.slice(0, -5),
          pid: value.pid,
          check_id: value.check_id,
          folder: value.folder,
        });
    } catch {
      // Unreadable: nothing to stop by it.
    }
  }
  return records;
}

/** Why a play stopped, as the journal says it. */
export type PlayEnd = 'closed' | 'preview_stopped' | 'newer_check' | 'host_stopped' | 'failed';

export interface HostPlaysDeps {
  readonly dataDir: string;
  /** The person's home folder, which the play browser's profile keeps out of reach. */
  readonly home: string;
  /** The chrome-headless-shell copy in the data folder and its binary's recorded SHA-256, or null: not set up. */
  readonly browser: { readonly folder: string; readonly sha256: string } | null;
  readonly browserProblem?: (folder: string, sha256: string) => string | null;
  /** The launcher the browser starts through, checked now, or null: then no game is played (S1). */
  readonly launcher: () => string | null;
  readonly launch?: (options: Parameters<typeof launchChrome>[0]) => Promise<ChromeRun>;
  /** Called once each play ends, with why. */
  readonly onEnded: (project: string, checkId: string, reason: PlayEnd) => void;
  /** How long a play waits for a viewer, and runs once its last viewer has gone: none, it stops. */
  readonly viewerWaitMs?: number;
  /** For tests: the ports a play's viewer may take. */
  readonly viewerPorts?: readonly [number, number];
  /** How long a play outlives its last viewer, so a reload comes back to it. */
  readonly viewerGraceMs?: number;
  /** Whether the browser `pid` or a process it started sends sound out now, by CoreAudio's record; null when that cannot be read. */
  readonly audible?: (pid: number) => Promise<boolean | null>;
  /** How often the play's sound is read, and how long without any before it is said to have none. */
  readonly soundPollMs?: number;
  readonly soundSilentMs?: number;
}

/** What the computer shows of a game's sound: playing now, played and stopped, or none so far. */
export type PlaySound = 'playing' | 'quiet' | 'none';

interface RunningPlay {
  readonly checkId: string;
  readonly run: ChromeRun;
  readonly watchers: Set<(frame: string) => void>;
  timer: NodeJS.Timeout | null;
  viewer: PlayViewer | null;
  /** The buttons the viewer holds down now. */
  held: number;
  sound: PlaySound | undefined;
  soundTimer: NodeJS.Timeout | null;
}

/**
 * The host's plays on the Mac, at most one per project: each a contained headless browser on the
 * preview's page, its frames streamed to the viewers watching it, only mouse and key input taken from
 * them. A play stops when its viewers have gone (or none came), when it is asked to (the preview
 * stopped, a newer check), and when the host closes; a play a crash left is stopped at the next start.
 */
export class HostPlays {
  readonly #deps: HostPlaysDeps;
  readonly #plays = new Map<string, RunningPlay>();
  /** Projects whose play is starting, taken synchronously so two starts never share one. */
  readonly #starting = new Set<string>();
  #closed = false;

  constructor(deps: HostPlaysDeps) {
    this.#deps = deps;
  }

  /** What the computer shows of the game's sound, or undefined: too soon, or it cannot say. */
  sound(project: string): PlaySound | undefined {
    return this.#plays.get(project)?.sound;
  }

  /** The check whose game plays now, or undefined. */
  playing(project: string): string | undefined {
    return this.#plays.get(project)?.checkId;
  }

  /** Starts the play and its viewer; resolves with the viewer's one-time link. */
  async start(project: string, checkId: string, port: number): Promise<{ link: string }> {
    const browser = this.#deps.browser;
    if (browser === null) throw new Error('Playing on this Mac is not set up: no browser for it.');
    const problem = (this.#deps.browserProblem ?? browserProblem)(browser.folder, browser.sha256);
    if (problem !== null) throw new Error(problem);
    // A game is played only by a browser that has none of the app's macOS permissions.
    const launcher = this.#deps.launcher();
    if (launcher === null)
      throw new Error('Playing on this Mac needs its launcher: run pnpm mac-setup launcher.');
    if (this.#closed) throw new Error('The host is closing.');
    if (this.#plays.has(project) || this.#starting.has(project))
      throw new Error('This game is already playing on the Mac.');
    this.#starting.add(project);
    try {
      return await this.#start(project, checkId, port, browser, launcher);
    } finally {
      this.#starting.delete(project);
    }
  }

  async #start(
    project: string,
    checkId: string,
    port: number,
    browser: { readonly folder: string; readonly sha256: string },
    launcher: string,
  ): Promise<{ link: string }> {
    const folder = join(this.#deps.dataDir, 'play', project);
    await mkdir(join(folder, 'profile', 'Default'), { recursive: true, mode: 0o700 });
    await writeFile(
      join(folder, 'profile', 'Default', 'Preferences'),
      JSON.stringify(playPreferences(folder)),
      { mode: 0o600 },
    );
    const run = await (this.#deps.launch ?? launchChrome)({
      chrome: browserBinary(browser.folder),
      launcher,
      home: this.#deps.home,
      folder,
      port,
      profileText: playProfile({ home: this.#deps.home, folder, port, browser: browser.folder }),
      args: playArguments(folder, port),
    });
    // A close() during the awaits above has already stopped every play: keep none started after it.
    if (this.#closed) {
      await run.stop();
      throw new Error('The host is closing.');
    }
    const play: RunningPlay = {
      checkId,
      run,
      watchers: new Set(),
      timer: null,
      viewer: null,
      held: 0,
      sound: undefined,
      soundTimer: null,
    };
    this.#plays.set(project, play);
    try {
      // Kept beside the folder, never in the journal: a crash's leftover is found by it.
      await writeFile(
        join(this.#deps.dataDir, 'play', `${project}.json`),
        JSON.stringify({ pid: run.pid, check_id: checkId, folder }),
        { mode: 0o600 },
      );
      const { devtools, session } = run;
      devtools.onEvent((message) => {
        if (message.sessionId !== session || message.method !== 'Page.screencastFrame') return;
        const params = message.params as { data?: unknown; sessionId?: unknown } | undefined;
        void devtools
          .send('Page.screencastFrameAck', { sessionId: params?.sessionId }, session)
          .catch(() => undefined);
        if (typeof params?.data === 'string')
          for (const watcher of play.watchers) watcher(params.data);
      });
      await devtools.send('Page.enable', {}, session);
      await devtools.send(
        'Emulation.setDeviceMetricsOverride',
        { width: WIDTH, height: HEIGHT, deviceScaleFactor: 1, mobile: false },
        session,
      );
      // The preview's origin is refused every permission a game could ask for, the microphone and
      // camera first, before its page opens: chrome-headless-shell does not apply its own settings
      // to them, so the refusal is said to the browser itself (S1).
      for (const name of DENIED_PERMISSIONS)
        await devtools.send('Browser.setPermission', {
          permission: { name },
          setting: 'denied',
          origin: `http://127.0.0.1:${port}`,
        });
      await devtools.send('Page.navigate', { url: `http://127.0.0.1:${port}/` }, session);
      // Until the new page commits there is no page to screencast: try again, for up to 10 s.
      for (let attempt = 1; ; attempt += 1) {
        try {
          await devtools.send(
            'Page.startScreencast',
            { format: 'jpeg', quality: 80, maxWidth: WIDTH, maxHeight: HEIGHT, everyNthFrame: 1 },
            session,
          );
          break;
        } catch (error) {
          if (attempt >= 100 || !String(error).includes('Not attached to an active page'))
            throw error;
          await new Promise((resolve) => setTimeout(resolve, 100));
        }
      }
      // The viewer reaches only this play: its frames and its input, nothing of the host.
      play.viewer = await PlayViewer.open(
        {
          watch: (listener) => this.watch(project, listener),
          input: (message) => this.input(project, message),
        },
        this.#deps.viewerPorts === undefined ? {} : { ports: this.#deps.viewerPorts },
      );
      // Stopped while the viewer opened: it closes with the play.
      if (this.#plays.get(project) !== play) {
        await play.viewer.close();
        throw new Error('The play stopped as it started.');
      }
      this.#awaitViewer(project, play);
      this.#listen(project, play);
      return { link: play.viewer.link };
    } catch (error) {
      await this.stop(project, 'failed');
      throw error;
    }
  }

  /** Streams the play's frames (base64 JPEG) to `listener` until the returned function is called. */
  watch(project: string, listener: (frame: string) => void): () => void {
    const play = this.#plays.get(project);
    if (play === undefined) return () => undefined;
    play.watchers.add(listener);
    if (play.timer !== null) clearTimeout(play.timer);
    play.timer = null;
    return () => {
      play.watchers.delete(listener);
      if (play.watchers.size > 0 || this.#plays.get(project) !== play || play.timer !== null)
        return;
      // A reload closes the stream and opens another: the game waits a moment for it.
      play.timer = setTimeout(() => {
        if (this.#plays.get(project) === play && play.watchers.size === 0)
          void this.stop(project, 'closed');
      }, this.#deps.viewerGraceMs ?? 5000);
      play.timer.unref();
    };
  }

  /** Sends one viewer message to the page if it is a mouse or key input; true only when sent. */
  async input(project: string, message: unknown): Promise<boolean> {
    const play = this.#plays.get(project);
    if (play === undefined) return false;
    const call = playInput(message, play.held);
    if (call === null) return false;
    const { type, button } = call.params as { type?: unknown; button?: unknown };
    const bit = typeof button === 'string' ? (BUTTONS[button] ?? 0) : 0;
    if (type === 'mousePressed') play.held |= bit;
    if (type === 'mouseReleased') play.held &= ~bit;
    await play.run.devtools.send(call.method, call.params, play.run.session);
    return true;
  }

  async stop(project: string, reason: PlayEnd): Promise<void> {
    const play = this.#plays.get(project);
    if (play === undefined) return;
    this.#plays.delete(project);
    if (play.timer !== null) clearTimeout(play.timer);
    if (play.soundTimer !== null) clearInterval(play.soundTimer);
    play.watchers.clear();
    await play.viewer?.end({ type: 'ended', reason });
    await play.run.stop();
    await rm(join(this.#deps.dataDir, 'play', `${project}.json`), { force: true });
    this.#deps.onEnded(project, play.checkId, reason);
  }

  async close(): Promise<void> {
    this.#closed = true;
    await Promise.all([...this.#plays.keys()].map((project) => this.stop(project, 'host_stopped')));
  }

  /**
   * Stops every play a crash left: a recorded pid is stopped only while it still runs a command naming
   * its play folder, so a pid the system has since given to another process is left alone. Every record
   * is removed. Returns how many were stopped.
   */
  async sweep(): Promise<number> {
    let stopped = 0;
    for (const record of readPlayRecords(this.#deps.dataDir)) {
      if (commandOf(record.pid)?.includes(record.folder)) {
        try {
          process.kill(record.pid, 'SIGTERM');
          stopped += 1;
          await gone(record.pid, 3000);
          if (commandOf(record.pid) !== null) process.kill(record.pid, 'SIGKILL');
        } catch {
          // Gone already.
        }
      }
      await rm(join(this.#deps.dataDir, 'play', `${record.project}.json`), { force: true });
    }
    return stopped;
  }

  /**
   * Reads the game's sound from CoreAudio's own record of which processes send sound out (S1), never
   * from the page: playing while the browser or one of its processes does; none once a while has
   * passed with nothing; paused after it played. When that cannot be read, nothing is said.
   */
  #listen(project: string, play: RunningPlay): void {
    const started = Date.now();
    const silent = this.#deps.soundSilentMs ?? 10_000;
    // Without a reading of the computer's own, nothing is said about the game's sound.
    const audible = this.#deps.audible ?? (async () => null);
    let reading = false;
    play.soundTimer = setInterval(() => {
      if (reading) return;
      reading = true;
      void audible(play.run.pid)
        .then((heard) => {
          if (this.#plays.get(project) !== play || heard === null) return;
          const next: PlaySound | undefined = heard
            ? 'playing'
            : play.sound === 'playing' || play.sound === 'quiet'
              ? 'quiet'
              : Date.now() - started >= silent
                ? 'none'
                : undefined;
          if (next === undefined || next === play.sound) return;
          play.sound = next;
          play.viewer?.announce({ type: 'sound', state: next } satisfies ViewerMessage);
        })
        .catch(() => undefined)
        .finally(() => {
          reading = false;
        });
    }, this.#deps.soundPollMs ?? 2000);
    play.soundTimer.unref();
  }

  #awaitViewer(project: string, play: RunningPlay): void {
    play.timer = setTimeout(() => {
      if (this.#plays.get(project) === play && play.watchers.size === 0)
        void this.stop(project, 'closed');
    }, this.#deps.viewerWaitMs ?? 60_000);
    play.timer.unref();
  }
}

/** The command line a pid runs, or null when it runs nothing. */
function commandOf(pid: number): string | null {
  try {
    const out = execFileSync('/bin/ps', ['-o', 'command=', '-p', String(pid)], {
      encoding: 'utf8',
    }).trim();
    return out === '' ? null : out;
  } catch {
    return null;
  }
}

async function gone(pid: number, ms: number): Promise<void> {
  const until = Date.now() + ms;
  while (Date.now() < until && commandOf(pid) !== null)
    await new Promise((resolve) => setTimeout(resolve, 50));
}
