import { type ChildProcess, execFile, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFile, mkdir, rm, writeFile } from 'node:fs/promises';
import { connect } from 'node:net';
import { dirname, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { launchChrome } from './chrome.ts';
import { buildProfile, SANDBOX_EXEC } from './profiles.ts';

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
 * The page-side count of sharp edges in a PNG picture, outside the welcome panel's corner: the image
 * is decoded in the page itself (its policy allows `data:` images), so nothing else is needed.
 */
export function edgeCountExpression(png: string): string {
  return `(async () => {
    const img = new Image(); img.src = 'data:image/png;base64,${png}'; await img.decode();
    const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
    const g = c.getContext('2d'); g.drawImage(img, 0, 0);
    const d = g.getImageData(0, 0, c.width, c.height).data; const w = c.width; let n = 0;
    for (let y = 0; y < c.height - 3; y += 1) for (let x = 0; x < w - 3; x += 1) {
      if (x < 360 && y < 240) continue;
      const i = (y * w + x) * 4, r = i + 12, b = i + 12 * w;
      const dx = Math.abs(d[i] - d[r]) + Math.abs(d[i + 1] - d[r + 1]) + Math.abs(d[i + 2] - d[r + 2]);
      const dy = Math.abs(d[i] - d[b]) + Math.abs(d[i + 1] - d[b + 1]) + Math.abs(d[i + 2] - d[b + 2]);
      if (dx > 40 || dy > 40) n += 1;
    }
    return n; })()`;
}

/** Type checks, builds, and opens the built copy of a project from a starter, each inside its profile. */
export async function runCheck(options: CheckOptions): Promise<CheckResult> {
  const { cache, starter, project, port } = options;
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
  if (build.exit !== 0) return { tsc, build, page: null, unknown: null, passed: false };
  if (options.chrome === null) {
    return {
      tsc,
      build,
      page: null,
      unknown: 'Chrome is not set, so the page was not opened.',
      passed: false,
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
    if (!(await listening(port, 30_000))) {
      return { tsc, build, page: null, unknown: 'The built copy was not served.', passed: false };
    }
    const page = await openPage(options.chrome, options.home, join(cache, 'chrome'), port, cache);
    return page === null
      ? { tsc, build, page: null, unknown: 'Chrome did not open the page.', passed: false }
      : { tsc, build, page, unknown: null, passed: passes(tsc, build, page) };
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

async function listening(port: number, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const open = await new Promise<boolean>((resolve) => {
      const socket = connect(port, '127.0.0.1', () => {
        socket.end();
        resolve(true);
      });
      socket.on('error', () => resolve(false));
    });
    if (open) return true;
    await delay(250);
  }
  return false;
}

async function stopProcess(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return;
  const exited = new Promise<void>((resolve) => child.once('exit', () => resolve()));
  child.kill('SIGTERM');
  const forced = setTimeout(() => child.kill('SIGKILL'), 3000);
  await exited;
  clearTimeout(forced);
}

/** Opens the page for 10 s in the sandboxed Chrome: exceptions, console errors, two pictures, edges. */
async function openPage(chrome: string, home: string, folder: string, port: number, cache: string) {
  let run: Awaited<ReturnType<typeof launchChrome>> | null = null;
  try {
    run = await launchChrome({ chrome, home, folder, port });
    const { devtools, session } = run;
    const exceptions: string[] = [];
    let consoleErrors = 0;
    devtools.onEvent((message) => {
      if (message.sessionId !== session) return;
      const params = message.params ?? {};
      if (message.method === 'Runtime.exceptionThrown') {
        const details = params.exceptionDetails as {
          text?: string;
          exception?: { description?: string };
        };
        exceptions.push(`${details.text ?? ''} ${details.exception?.description ?? ''}`.trim());
      } else if (message.method === 'Runtime.consoleAPICalled' && params.type === 'error') {
        consoleErrors += 1;
      }
    });
    for (const domain of ['Runtime.enable', 'Page.enable'])
      await devtools.send(domain, {}, session);
    await devtools.send('Page.navigate', { url: `http://127.0.0.1:${port}/` }, session);
    const picture = async (
      atMs: number,
    ): Promise<{ png: string | null; saved: PagePicture | null }> => {
      const shot = await devtools
        .send('Page.captureScreenshot', { format: 'png' }, session)
        .catch((): Record<string, unknown> => ({}));
      const png = typeof shot.data === 'string' ? shot.data : null;
      if (png === null) return { png, saved: null };
      const bytes = Buffer.from(png, 'base64');
      const file = join(cache, `picture-${atMs / 1000}s.png`);
      await writeFile(file, bytes, { mode: 0o600 });
      return {
        png,
        saved: { at_ms: atMs, file, sha256: createHash('sha256').update(bytes).digest('hex') },
      };
    };
    await delay(EARLY_MS);
    const early = await picture(EARLY_MS);
    await delay(PAGE_MS - EARLY_MS);
    const counted = await devtools.send(
      'Runtime.evaluate',
      { expression: 'document.querySelectorAll("canvas").length', returnByValue: true },
      session,
    );
    const late = await picture(PAGE_MS);
    const edges = async (png: string | null) => {
      if (png === null) return null;
      const answer = await devtools.send(
        'Runtime.evaluate',
        { expression: edgeCountExpression(png), awaitPromise: true, returnByValue: true },
        session,
      );
      const value = (answer.result as { value?: unknown } | undefined)?.value;
      return typeof value === 'number' ? value : null;
    };
    const at3s = await edges(early.png);
    const at10s = await edges(late.png);
    const canvases = (counted.result as { value?: unknown } | undefined)?.value;
    return {
      exceptions: summarizeExceptions(exceptions),
      exception_count: exceptions.length,
      console_errors: consoleErrors,
      canvases: typeof canvases === 'number' ? canvases : 0,
      edges: { at_3s: at3s, at_10s: at10s },
      drawn: drawnFrom(at3s, at10s),
      pictures: [early.saved, late.saved].filter((saved): saved is PagePicture => saved !== null),
    } satisfies PageFindings;
  } catch {
    return null;
  } finally {
    await run?.stop();
  }
}
