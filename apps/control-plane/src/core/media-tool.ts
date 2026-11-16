import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream, existsSync, realpathSync } from 'node:fs';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import type { MediaNotMade, MediaType } from '@halcyonic/contracts';
import { ancestors, SANDBOX_EXEC, SYSTEM_READS, subpaths } from '../seatbelt.ts';

/**
 * The one tool the host runs on a person's photo or video (ADR 0066): ffmpeg, at the absolute path the
 * person set (`HALCYONIC_FFMPEG`), whose executable's bytes, read through its real path, must have the
 * SHA-256 they recorded (`HALCYONIC_FFMPEG_SHA256`): a changed tool is never taken by itself. It runs
 * with arguments as an array, no shell, a time bound, a clean environment, and a seatbelt profile of
 * its own: no network, reading only its one input by literal path, its own and the system's
 * libraries, writing only one fresh folder. What it prints is read here for the length and size of a
 * video and never logged or journaled, since it names the file's own tags.
 */

export interface MediaToolSettings {
  /** The executable's absolute path as the person set it. */
  readonly ffmpeg: string;
  /** The SHA-256 the person recorded for its real path's bytes. */
  readonly sha256: string;
}

/** What a run made: files in its own folder, never in a project's. */
export type ToolRun<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly reason: MediaNotMade };

export interface MediaFacts {
  readonly width: number | null;
  readonly height: number | null;
  readonly durationMs: number | null;
}

/** The long side of a still, in pixels. */
export const STILL_SIDE = 1600;
/** The long side of a video's play copy, in pixels, and its bitrate's cap. */
export const PLAY_SIDE = 1280;
const PLAY_RATE = '6M';
/** Time bounds, per run. */
export const STRIP_MS = 60_000;
export const STILL_MS = 60_000;
export const PLAY_MS = 240_000;
/** Where in a video its stills are taken, as parts of its length. */
export const STILL_AT = [0.1, 0.35, 0.6, 0.85] as const;
/** The most of what the tool prints that is read, for the facts; the rest is dropped. */
const PRINTED_MAX = 64 * 1024;

/** The system's libraries the loader maps for any program. */
const MAPPED_SYSTEM = [
  '/usr/lib',
  '/System/Library/Frameworks',
  '/System/Library/PrivateFrameworks',
  '/System/Library/SubFrameworks',
  '/opt/homebrew/lib',
  '/usr/local/lib',
];
/** Homebrew's folders an installed ffmpeg loads its libraries from, besides the system's. */
const TOOL_LIBRARIES = [
  '/opt/homebrew/Cellar',
  '/opt/homebrew/opt',
  '/usr/local/Cellar',
  '/usr/local/opt',
];

export class MediaTool {
  readonly #settings: MediaToolSettings | null;
  readonly #work: string;

  /** `work` is a folder of the host's data folder, 0700, in which each run gets a fresh folder. */
  constructor(settings: MediaToolSettings | null, work: string) {
    this.#settings = settings;
    this.#work = work;
  }

  /** The tool's real path when it is set, there, and has the recorded digest; else why not. */
  async ready(): Promise<ToolRun<string>> {
    const settings = this.#settings;
    if (settings === null) return { ok: false, reason: 'tool_missing' };
    let real: string;
    try {
      real = realpathSync(settings.ffmpeg);
    } catch {
      return { ok: false, reason: 'tool_missing' };
    }
    const digest = await new Promise<string | null>((resolve) => {
      const hash = createHash('sha256');
      createReadStream(real)
        .on('data', (chunk) => hash.update(chunk))
        .on('error', () => resolve(null))
        .on('end', () => resolve(hash.digest('hex')));
    });
    if (digest === null) return { ok: false, reason: 'tool_missing' };
    if (digest !== settings.sha256.toLowerCase()) return { ok: false, reason: 'tool_changed' };
    return { ok: true, value: real };
  }

  /** Removes every run a stopped host left: its copies of a person's files and its profiles. */
  async clear(): Promise<void> {
    await rm(this.#work, { recursive: true, force: true });
  }

  /** A fresh folder for one medium's runs, removed by `done`. */
  async folder(): Promise<{ readonly path: string; readonly done: () => Promise<void> }> {
    await mkdir(this.#work, { recursive: true, mode: 0o700 });
    const path = await mkdtemp(join(this.#work, 'run-'));
    return { path, done: () => rm(path, { recursive: true, force: true }) };
  }

  /**
   * The copy to place: a video's first video and audio streams copied as they are, every container
   * and stream tag dropped; a photo re-encoded as a JPEG with no tags. Deterministic (`bitexact`), so
   * the same upload makes the same copy.
   */
  async strip(
    input: string,
    type: MediaType,
    out: string,
  ): Promise<ToolRun<{ path: string; facts: MediaFacts }>> {
    const video = type === 'video/mp4' || type === 'video/quicktime';
    const name = video ? (type === 'video/quicktime' ? 'copy.mov' : 'copy.mp4') : 'copy.jpg';
    const path = join(out, name);
    const args = video
      ? [
          ...['-i', input, '-map', '0:v:0', '-map', '0:a:0?', '-c', 'copy'],
          ...['-f', type === 'video/quicktime' ? 'mov' : 'mp4'],
          ...withoutTags(path),
        ]
      : [...['-i', input, '-frames:v', '1', '-update', '1', '-q:v', '3'], ...withoutTags(path)];
    const run = await this.#run(input, out, args, STRIP_MS);
    if (!run.ok) return run;
    return { ok: true, value: { path, facts: facts(run.value) } };
  }

  /** One JPEG still at `atMs` of a video (or of a photo, at 0), long side at most `STILL_SIDE`. */
  async still(input: string, atMs: number, out: string, index: number): Promise<ToolRun<string>> {
    const path = join(out, `still-${index}.jpg`);
    const scale = `scale=w='min(${STILL_SIDE},iw)':h='min(${STILL_SIDE},ih)':force_original_aspect_ratio=decrease`;
    const run = await this.#run(
      input,
      out,
      [
        // A photo is one picture: no seek, which on one image writes nothing and still exits 0.
        ...(atMs > 0 ? ['-ss', (atMs / 1000).toFixed(3)] : []),
        ...['-i', input, '-frames:v', '1', '-update', '1'],
        ...['-vf', scale, '-q:v', '3'],
        ...withoutTags(path),
      ],
      STILL_MS,
    );
    return run.ok ? { ok: true, value: path } : run;
  }

  /** A copy of a video any browser plays: H.264 and AAC, long side at most `PLAY_SIDE`, no tags. */
  async play(input: string, out: string): Promise<ToolRun<string>> {
    const path = join(out, 'play.mp4');
    const scale = `scale=w='min(${PLAY_SIDE},iw)':h='min(${PLAY_SIDE},ih)':force_original_aspect_ratio=decrease:force_divisible_by=2`;
    const run = await this.#run(
      input,
      out,
      [
        ...['-i', input, '-map', '0:v:0', '-map', '0:a:0?', '-vf', scale],
        ...['-c:v', 'libx264', '-preset', 'veryfast', '-crf', '23'],
        ...['-maxrate', PLAY_RATE, '-bufsize', '12M', '-pix_fmt', 'yuv420p'],
        ...['-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', '-f', 'mp4'],
        ...withoutTags(path),
      ],
      PLAY_MS,
    );
    return run.ok ? { ok: true, value: path } : run;
  }

  /** Runs the tool once under its profile; what it printed, kept in memory only, or why it failed. */
  async #run(
    input: string,
    out: string,
    args: readonly string[],
    boundMs: number,
  ): Promise<ToolRun<string>> {
    const ready = await this.ready();
    if (!ready.ok) return ready;
    const tool = ready.value;
    const home = join(out, 'home');
    const tmp = join(out, 'tmp');
    await mkdir(home, { recursive: true, mode: 0o700 });
    await mkdir(tmp, { recursive: true, mode: 0o700 });
    const profile = join(dirname(out), `${out.slice(dirname(out).length + 1)}.sb`);
    await writeFile(profile, mediaToolProfile({ tool, input, out }), { mode: 0o600 });
    return new Promise((resolve) => {
      const child = spawn(
        SANDBOX_EXEC,
        ['-f', profile, tool, '-nostdin', '-hide_banner', '-y', ...args],
        {
          detached: true,
          env: toolEnvironment(home, tmp),
          stdio: ['ignore', 'ignore', 'pipe'],
        },
      );
      let printed = '';
      child.stderr?.setEncoding('utf8');
      child.stderr?.on('data', (chunk: string) => {
        if (printed.length < PRINTED_MAX) printed += chunk.slice(0, PRINTED_MAX - printed.length);
      });
      let timedOut = false;
      const timer = setTimeout(() => {
        timedOut = true;
        try {
          if (child.pid !== undefined) process.kill(-child.pid, 'SIGKILL');
        } catch {
          child.kill('SIGKILL');
        }
      }, boundMs);
      child.once('error', () => {
        clearTimeout(timer);
        void rm(profile, { force: true });
        resolve({ ok: false, reason: 'unreadable' });
      });
      // On close, not exit: what it printed may still be arriving when it exits.
      child.once('close', (code) => {
        clearTimeout(timer);
        void rm(profile, { force: true });
        if (timedOut) resolve({ ok: false, reason: 'timed_out' });
        // A run that exits 0 but wrote no file made nothing.
        else if (code === 0 && existsSync(args[args.length - 1] ?? ''))
          resolve({ ok: true, value: printed });
        else resolve({ ok: false, reason: 'unreadable' });
      });
    });
  }
}

/**
 * The end of every run that writes a file the project or a runtime gets (AUDIT3 P1): every container,
 * stream and chapter tag dropped, written bit-exact so the same input makes the same file, then the
 * file. The one helper every output goes through, never set by hand per call.
 */
export function withoutTags(path: string): readonly string[] {
  return [
    ...['-map_metadata', '-1', '-map_metadata:s:v', '-1', '-map_metadata:s:a', '-1'],
    ...['-map_chapters', '-1'],
    ...['-fflags', '+bitexact', '-flags:v', '+bitexact', '-flags:a', '+bitexact'],
    path,
  ];
}

/** The tool's whole environment: none of the control plane's own reaches it (D2). */
export function toolEnvironment(home: string, tmp: string): Record<string, string> {
  return { PATH: '/usr/bin:/bin', HOME: home, TMPDIR: tmp, LANG: 'en_US.UTF-8' };
}

/**
 * The tool's profile: everything denied but what one run needs. It reads the one input by its
 * literal path (never a folder of the data folder, which holds the keys), the tool, its libraries and
 * the system's; it writes only its own fresh folder; no network, no other process.
 */
export function mediaToolProfile(scope: { tool: string; input: string; out: string }): string {
  const literal = (path: string) => `(literal ${JSON.stringify(path)})`;
  return [
    '(version 1)',
    '(deny default)',
    `(allow process-exec ${literal(scope.tool)})`,
    `(allow file-read* ${literal(scope.tool)} ${literal(realpathSync(scope.input))})`,
    `(allow file-read* file-test-existence ${subpaths(TOOL_LIBRARIES.filter(exists))})`,
    ...SYSTEM_READS,
    // The loader maps the tool's own and the system's libraries; reading them alone is not enough.
    `(allow file-map-executable ${literal(scope.tool)} ${subpaths([...MAPPED_SYSTEM, ...TOOL_LIBRARIES].filter(exists))})`,
    '(allow file-test-existence (subpath "/usr/lib") (subpath "/System/Library") (literal "/"))',
    '(allow mach-lookup (global-name "com.apple.system.opendirectoryd.libinfo"))',
    '(allow signal (target self))',
    `(allow file-read* file-write* ${subpaths([scope.out])})`,
    `(allow file-read-metadata ${ancestors([scope.out, scope.input, scope.tool])})`,
    '(allow sysctl-read)',
    '(allow process-info-pidinfo (target self))',
    '(deny network*)',
    '',
  ].join('\n');
}

function exists(path: string): boolean {
  try {
    realpathSync(path);
    return true;
  } catch {
    return false;
  }
}

/** A video's size and length from what the tool printed about its input; null where not printed. */
export function facts(printed: string): MediaFacts {
  const duration = /Duration: (\d+):(\d{2}):(\d{2})\.(\d{2})/.exec(printed);
  const size = /Stream #0:\d+[^\n]*: Video: [^\n]*?, (\d{2,5})x(\d{2,5})[, \n]/.exec(printed);
  const durationMs =
    duration === null
      ? null
      : ((Number(duration[1]) * 60 + Number(duration[2])) * 60 + Number(duration[3])) * 1000 +
        Number(duration[4]) * 10;
  return {
    width: size === null ? null : Number(size[1]),
    height: size === null ? null : Number(size[2]),
    durationMs,
  };
}
