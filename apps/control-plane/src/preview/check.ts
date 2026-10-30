import { type ChildProcess, execFile, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFile, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { get } from 'node:http';
import { dirname, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import type { CheckLaptop, CheckNotes, CheckVr } from '@halcyonic/contracts';
import { launchChrome } from './chrome.ts';
import { laptopStep, layerState, pipeDriver, vrStep } from './laptop-probe.ts';
import { decodePng, type Pixels } from './png.ts';
import { buildProfile, SANDBOX_EXEC } from './profiles.ts';
import { scanSource } from './scan.ts';

/** The host's files for a starter, copied beside its installed dependencies so the config resolves them. */
export const HOST_FILES = [
  'vite.host.config.mjs',
  'input-profiles.mjs',
  'input-profiles.json',
] as const;

/**
 * More sharp edges than this outside the welcome panel, in the picture at 3 s or at 10 s, means
 * something is drawn in view of the laptop camera: an empty starter has none, one small test box
 * about 2,700. It never means the game works: a scene of the wrong objects passes it too.
 */
export const DRAWN_EDGES = 200;

/** How long the page is watched, and when the two pictures are taken. */
export const PAGE_MS = 10_000;
export const EARLY_MS = 3_000;

const MAX_EXCEPTIONS = 5;
const MAX_EXCEPTION_CHARS = 300;
/** How many exception texts are kept to summarize; the rest are only counted. */
const KEPT_EXCEPTIONS = 100;

export interface CheckOptions {
  /** Node, by absolute path, to run the starter's own TypeScript and Vite. */
  readonly node: string;
  /** Chrome, by absolute path, or null: then the page is not opened and the check cannot pass. */
  readonly chrome: string | null;
  /** The person's home folder, whose contents both profiles keep out of reach. */
  readonly home: string;
  readonly project: string;
  /** The starter's installed folder: `node_modules` and the host's files. */
  readonly starter: string;
  /** The tracked host files to copy there (`starters/<id>/host`). */
  readonly hostSource: string;
  /** The tracked template (`starters/<id>/template`), whose input layer the project's is compared with. */
  readonly template: string;
  /** The host's cache for this check, outside the project; emptied first. */
  readonly cache: string;
  /** The host's controller and hand models, or null. */
  readonly profiles: string | null;
  /** A free loopback port for the built copy. */
  readonly port: number;
}

export interface CheckStep {
  /** The exit code, or null when the step was stopped at its limit. */
  readonly exit: number | null;
  readonly seconds: number;
}

export interface PagePicture {
  readonly at_ms: number;
  readonly file: string;
  readonly sha256: string;
}

export interface PageFindings {
  /** Uncaught exceptions, each its first two lines, clipped, at most five distinct. */
  readonly exceptions: readonly string[];
  readonly exception_count: number;
  readonly console_errors: number;
  readonly canvases: number;
  readonly edges: { readonly at_3s: number | null; readonly at_10s: number | null };
  /** Something is drawn in view; never that the game works. */
  readonly drawn: boolean;
  readonly pictures: readonly PagePicture[];
}

export interface CheckResult {
  readonly tsc: CheckStep;
  readonly build: CheckStep;
  /** Null when the build failed, Chrome is not set, or the page could not be opened. */
  readonly page: PageFindings | null;
  /** What could not be found out, in a few words, or null. */
  readonly unknown: string | null;
  /** Type check 0, build 0, no uncaught exception, something drawn. */
  readonly passed: boolean;
  /** The laptop step (M1.8b), or null when the page was not opened. */
  readonly laptop: CheckLaptop | null;
  /** The VR-entry step on a stand-in WebXR, or null when it did not run. */
  readonly vr: CheckVr | null;
  /** The scan of the project's source; notes only. */
  readonly notes: CheckNotes;
  /** Passed, and both steps ready; never that the game plays well, or plays on a headset. */
  readonly ready: boolean;
}

/** Whether a check passed: each of its findings, none assumed. */
export function passes(tsc: CheckStep, build: CheckStep, page: PageFindings | null): boolean {
  return (
    tsc.exit === 0 && build.exit === 0 && page !== null && page.exception_count === 0 && page.drawn
  );
}

/** The exceptions to report: distinct, each its first two lines, clipped, at most five. */
export function summarizeExceptions(texts: readonly string[]): string[] {
  const lines = texts.map((text) =>
    text
      .split('\n')
      .slice(0, 2)
      .map((line) => line.trim())
      .join(' ')
      .slice(0, MAX_EXCEPTION_CHARS),
  );
  return [...new Set(lines)].slice(0, MAX_EXCEPTIONS);
}

/** Whether the edge counts of the two pictures say something is drawn. */
export function drawnFrom(at3s: number | null, at10s: number | null): boolean {
  return Math.max(at3s ?? 0, at10s ?? 0) > DRAWN_EDGES;
}

/**
 * The count of sharp edges in a picture, outside the welcome panel's corner, in the host: the page
 * never sees the picture, so nothing it runs can change the count (AUDIT M18B-M1). Null when the PNG
 * cannot be read.
 */
export function edgeCount(png: Buffer): number | null {
  let pixels: Pixels;
  try {
    pixels = decodePng(png);
  } catch {
    return null;
  }
  const { width: w, height: h, rgba: d } = pixels;
  // The summed channel difference between two pixels' starting bytes.
  const apart = (a: number, b: number) =>
    Math.abs((d[a] ?? 0) - (d[b] ?? 0)) +
    Math.abs((d[a + 1] ?? 0) - (d[b + 1] ?? 0)) +
    Math.abs((d[a + 2] ?? 0) - (d[b + 2] ?? 0));
  let n = 0;
  for (let y = 0; y < h - 3; y += 1) {
    for (let x = 0; x < w - 3; x += 1) {
      if (x < 360 && y < 240) continue;
      const i = (y * w + x) * 4;
      if (apart(i, i + 12) > 40 || apart(i, i + 12 * w) > 40) n += 1;
    }
  }
  return n;
}

/** Type checks, builds, and opens the built copy of a project from a starter, each inside its profile. */
export async function runCheck(options: CheckOptions): Promise<CheckResult> {
  const { cache, starter, project, port } = options;
  // Both read the project in the host, before anything of it runs.
  const notes = await scanSource(project);
  const layer = await layerState(project, options.template);
  const steps = {
    notes,
    laptop: null,
    vr: null,
    ready: false,
  } as const satisfies Omit<CheckResult, 'tsc' | 'build' | 'page' | 'unknown' | 'passed'>;
  await rm(cache, { recursive: true, force: true });
  for (const sub of ['tmp', 'vite', 'dist', 'chrome'])
    await mkdir(join(cache, sub), { recursive: true, mode: 0o700 });
  await mkdir(join(starter, 'host'), { recursive: true });
  for (const file of HOST_FILES)
    await copyFile(join(options.hostSource, file), join(starter, 'host', file));
  const profile = join(cache, 'build.sb');
  await writeFile(
    profile,
    buildProfile({ home: options.home, project, starter, cache, profiles: options.profiles }),
    { mode: 0o600 },
  );
  const env: Record<string, string> = {
    PATH: '/usr/bin:/bin',
    TMPDIR: join(cache, 'tmp'),
    HALCYONIC_PREVIEW_ROOT: project,
    HALCYONIC_PREVIEW_PORT: String(port),
    HALCYONIC_PREVIEW_CACHE: join(cache, 'vite'),
    HALCYONIC_PREVIEW_DEPS: join(starter, 'node_modules'),
    ...(options.profiles ? { HALCYONIC_PREVIEW_PROFILES: options.profiles } : {}),
  };
  const config = join(starter, 'host', 'vite.host.config.mjs');
  const vite = join(starter, 'node_modules', 'vite', 'bin', 'vite.js');
  const sandboxed = (args: string[]) => ['-f', profile, options.node, ...args];
  const tsc = await step(
    sandboxed([
      join(starter, 'node_modules', 'typescript', 'bin', 'tsc'),
      '--noEmit',
      '-p',
      join(project, 'tsconfig.json'),
    ]),
    env,
    join(cache, 'tsc.log'),
    120_000,
  );
  const build = await step(
    sandboxed([
      vite,
      'build',
      '--configLoader',
      'native',
      '--config',
      config,
      '--outDir',
      join(cache, 'dist'),
      '--emptyOutDir',
    ]),
    env,
    join(cache, 'build.log'),
    180_000,
  );
  if (build.exit !== 0) return { tsc, build, page: null, unknown: null, passed: false, ...steps };
  if (options.chrome === null) {
    return {
      tsc,
      build,
      page: null,
      unknown: 'Chrome is not set, so the page was not opened.',
      passed: false,
      ...steps,
    };
  }
  const server = spawn(
    SANDBOX_EXEC,
    sandboxed([
      vite,
      'preview',
      '--configLoader',
      'native',
      '--config',
      config,
      '--outDir',
      join(cache, 'dist'),
    ]),
    { env, cwd: cache, stdio: 'ignore' },
  );
  try {
    if (!(await servesCopy(server, port, join(cache, 'dist'), 30_000))) {
      return {
        tsc,
        build,
        page: null,
        unknown: 'The built copy was not served.',
        passed: false,
        ...steps,
      };
    }
    const opened = await openPage(
      options.chrome,
      options.home,
      join(cache, 'chrome'),
      port,
      cache,
      layer,
    );
    if (opened === null) {
      return {
        tsc,
        build,
        page: null,
        unknown: 'Chrome did not open the page.',
        passed: false,
        ...steps,
      };
    }
    if (typeof opened === 'string')
      return { tsc, build, page: null, unknown: opened, passed: false, ...steps };
    const { page, laptop, vr } = opened;
    const passed = passes(tsc, build, page);
    return {
      tsc,
      build,
      page,
      unknown: null,
      passed,
      notes,
      laptop,
      vr,
      ready: passed && laptop.ready && vr?.ready === true,
    };
  } finally {
    await stopProcess(server);
  }
}

async function step(
  args: string[],
  env: Record<string, string>,
  log: string,
  timeoutMs: number,
): Promise<CheckStep> {
  const started = Date.now();
  return new Promise((resolve) => {
    execFile(
      SANDBOX_EXEC,
      args,
      // Run from the cache: a working folder inside the home folder would be closed to it.
      { env, cwd: dirname(log), timeout: timeoutMs, maxBuffer: 8 * 1024 * 1024 },
      (error, stdout, stderr) => {
        void writeFile(log, `${stdout}${stderr}`, { mode: 0o600 });
        const seconds = (Date.now() - started) / 1000;
        if (error === null) return resolve({ exit: 0, seconds });
        const code = (error as { code?: unknown }).code;
        resolve({ exit: typeof code === 'number' ? code : null, seconds });
      },
    );
  });
}

/**
 * Waits until `server` itself serves the built copy on 127.0.0.1:port (AUDIT M15B2-M1): it still runs,
 * every listener on the port is its own pid, and /index.html answers with the copy's bytes. Another
 * process holding the port fails it, whatever it answers.
 */
export async function servesCopy(
  server: ChildProcess,
  port: number,
  dist: string,
  timeoutMs: number,
): Promise<boolean> {
  const expected = await readFile(join(dist, 'index.html'));
  const ended = () => server.exitCode !== null || server.signalCode !== null;
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (ended() || server.pid === undefined) return false;
    const listeners = await listenersOf(port);
    if (listeners.length > 0) {
      if (listeners.some((pid) => pid !== server.pid)) return false;
      const body = await indexOf(port);
      if (body === null || !body.equals(expected) || ended()) return false;
      // Still its own once it answered: nothing took the port in between.
      const after = await listenersOf(port);
      return after.length > 0 && after.every((pid) => pid === server.pid);
    }
    await delay(100);
  }
  return false;
}

/** The pids listening on 127.0.0.1:port, by lsof; none when nothing listens. */
function listenersOf(port: number): Promise<number[]> {
  return new Promise((resolve) => {
    execFile(
      '/usr/sbin/lsof',
      ['-nP', `-iTCP@127.0.0.1:${port}`, '-sTCP:LISTEN', '-Fp'],
      { timeout: 10_000 },
      (_error, stdout) =>
        resolve(
          String(stdout)
            .split('\n')
            .filter((line) => /^p\d+$/.test(line))
            .map((line) => Number(line.slice(1))),
        ),
    );
  });
}

/** The bytes 127.0.0.1:port answers for /index.html, or null for anything but a 200. */
function indexOf(port: number): Promise<Buffer | null> {
  return new Promise((resolve) => {
    const request = get(
      { host: '127.0.0.1', port, path: '/index.html', timeout: 5_000 },
      (response) => {
        if (response.statusCode !== 200) {
          response.resume();
          return resolve(null);
        }
        const chunks: Buffer[] = [];
        let size = 0;
        response.on('data', (chunk: Buffer) => {
          size += chunk.length;
          if (size > 8 * 1024 * 1024) request.destroy();
          else chunks.push(chunk);
        });
        response.on('end', () => resolve(Buffer.concat(chunks)));
        // Whichever settles first wins; a cut-off answer is none.
        response.on('close', () => resolve(null));
      },
    );
    request.on('timeout', () => request.destroy());
    request.on('error', () => resolve(null));
    request.on('close', () => resolve(null));
  });
}

async function stopProcess(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return;
  const exited = new Promise<void>((resolve) => child.once('exit', () => resolve()));
  child.kill('SIGTERM');
  const forced = setTimeout(() => child.kill('SIGKILL'), 3000);
  await exited;
  clearTimeout(forced);
}

/**
 * Opens the page for 10 s in the sandboxed Chrome: exceptions, console errors, two pictures, edges;
 * then the laptop step in the same load and the VR-entry step in a second one. Null when it did not
 * open; a reason when the run was ended on purpose.
 */
async function openPage(
  chrome: string,
  home: string,
  folder: string,
  port: number,
  cache: string,
  layer: CheckLaptop['layer'],
): Promise<{ page: PageFindings; laptop: CheckLaptop; vr: CheckVr | null } | string | null> {
  let run: Awaited<ReturnType<typeof launchChrome>> | null = null;
  try {
    run = await launchChrome({ chrome, home, folder, port });
    const { devtools, session } = run;
    const exceptions: string[] = [];
    let exceptionCount = 0;
    let consoleErrors = 0;
    devtools.onEvent((message) => {
      if (message.sessionId !== session) return;
      const params = message.params ?? {};
      if (message.method === 'Runtime.exceptionThrown') {
        const details = params.exceptionDetails as {
          text?: string;
          exception?: { description?: string };
        };
        exceptionCount += 1;
        // The first ones are enough to summarize; a page throwing in a loop holds no more memory.
        if (exceptions.length < KEPT_EXCEPTIONS)
          exceptions.push(
            `${details.text ?? ''} ${details.exception?.description ?? ''}`.trim().slice(0, 4096),
          );
      } else if (message.method === 'Runtime.consoleAPICalled' && params.type === 'error') {
        consoleErrors += 1;
      }
    });
    for (const domain of ['Runtime.enable', 'Page.enable'])
      await devtools.send(domain, {}, session);
    // ?check turns on the input layer's check hook; a person's preview never has it.
    await devtools.send('Page.navigate', { url: `http://127.0.0.1:${port}/?check` }, session);
    const picture = async (
      atMs: number,
    ): Promise<{ edges: number | null; saved: PagePicture | null }> => {
      const shot = await devtools
        .send('Page.captureScreenshot', { format: 'png' }, session)
        .catch((): Record<string, unknown> => ({}));
      if (typeof shot.data !== 'string') return { edges: null, saved: null };
      const bytes = Buffer.from(shot.data, 'base64');
      const file = join(cache, `picture-${atMs / 1000}s.png`);
      await writeFile(file, bytes, { mode: 0o600 });
      return {
        edges: edgeCount(bytes),
        saved: { at_ms: atMs, file, sha256: createHash('sha256').update(bytes).digest('hex') },
      };
    };
    await delay(EARLY_MS);
    const early = await picture(EARLY_MS);
    await delay(PAGE_MS - EARLY_MS);
    // Counted through the DOM domain, which runs none of the page's code.
    const { root } = (await devtools.send('DOM.getDocument', { depth: 0 }, session)) as {
      root?: { nodeId?: number };
    };
    const found = (await devtools.send(
      'DOM.querySelectorAll',
      { nodeId: root?.nodeId ?? 0, selector: 'canvas' },
      session,
    )) as { nodeIds?: unknown[] };
    const late = await picture(PAGE_MS);
    const at3s = early.edges;
    const at10s = late.edges;
    // The page's findings are what it did in its first 10 s, before the check acts on it.
    const page = {
      exceptions: summarizeExceptions(exceptions),
      exception_count: exceptionCount,
      console_errors: consoleErrors,
      canvases: Array.isArray(found.nodeIds) ? found.nodeIds.length : 0,
      edges: { at_3s: at3s, at_10s: at10s },
      drawn: drawnFrom(at3s, at10s),
      pictures: [early.saved, late.saved].filter((saved): saved is PagePicture => saved !== null),
    } satisfies PageFindings;
    const laptop = await laptopStep(pipeDriver(devtools, session), layer).catch(
      (): CheckLaptop => ({
        layer,
        targets: 0,
        mouse: 'unknown',
        key: 'unknown',
        screen_changed: null,
        ready: false,
      }),
    );
    const vr = await vrStep(devtools, session, `http://127.0.0.1:${port}/`).catch(
      (): CheckVr => ({ offered: 'unknown', how: null, exceptions: 0, ready: false }),
    );
    if (devtools.overflowed) return 'Chrome sent more than the check reads.';
    return { page, laptop, vr };
  } catch {
    return run?.devtools.overflowed ? 'Chrome sent more than the check reads.' : null;
  } finally {
    await run?.stop();
  }
}
