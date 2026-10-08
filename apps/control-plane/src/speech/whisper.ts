import { type ChildProcess, execFile, spawn } from 'node:child_process';
import { rmSync } from 'node:fs';
import { lstat, mkdtemp, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { TranscriptionEngine } from '@halcyonic/contracts';
import type { SpeechConfig } from '../config.ts';
import { OwnWordsError } from '../logger.ts';
import { writeWav } from './wav.ts';

/** How long one transcription may take before the engine is stopped. */
export const ENGINE_TIMEOUT_MS = 15_000;

/**
 * How long the warm-up may take: the first run after whisper.cpp is built compiles its GPU
 * shaders, which took 23.5 s on the owner's Mac.
 */
export const WARM_UP_TIMEOUT_MS = 120_000;

/** How long the binary has to answer --version at startup: the native binary answers at once. */
export const VERSION_TIMEOUT_MS = 5_000;

/** The whisper.cpp release Halcyonic runs: its release archive's build reports 1.9.4-dev, a tag checkout's 1.9.4. */
const PINNED_VERSION = /^1\.9\.4(-dev)?$/;

/** Each clip waits for the engine in a temporary directory of its own with this prefix. */
const CLIP_PREFIX = 'halcyonic-speech-';

/** A clip directory older than this was left by a control plane that stopped mid-clip; longer than any run. */
const LEFTOVER_MS = 180_000;

/** The language every clip is transcribed in, as whisper.cpp and BCP 47 both name it. */
export const LANGUAGE = 'en';

/**
 * Halcyonic's own words, given to the engine as its prompt: the product's, its runtimes' and its
 * local models'. Measured to fix names the model otherwise mishears, and to break no other clip
 * (docs/internal/validation/voice-transcription.md).
 */
export const PROMPT_WORDS = ['Halcyonic', 'Claude Code', 'Codex', 'OpenCode', 'Ollama', 'Qwen'];

/** The most transcript read from the engine; a 30 s clip holds a few hundred bytes. */
const MAX_OUTPUT_BYTES = 64 * 1024;

/** A run in progress: its clip's directory and, once launched, the engine. */
interface Run {
  readonly directory: string;
  child?: ChildProcess;
}

/** Runs in progress, stopped and removed if the control plane exits during one. */
const running = new Set<Run>();
let stopsOnExit = false;

export type EngineResult =
  | { readonly kind: 'text'; readonly text: string }
  | { readonly kind: 'failed'; readonly message: string };

/** Turns one clip into text: whisper.cpp here, a script in tests. */
export interface SpeechEngine {
  readonly engine: TranscriptionEngine;
  transcribe(wav: Buffer): Promise<EngineResult>;
  /** Readies the engine before anyone speaks; returns how long it took, in milliseconds. */
  warmUp(): Promise<number>;
}

/** The speech engine's failure, in its own fixed words: "The engine took longer than 15 s.". */
export class SpeechEngineError extends OwnWordsError {
  override name = 'SpeechEngineError';
}

/**
 * whisper.cpp's `whisper-cli`, launched once per clip with no environment and no port (ADR 0021):
 * English, Halcyonic's own words as the prompt, and Silero voice activity detection with half a
 * second around speech, so a clip with no speech gives no text. The clip is written to a private
 * temporary directory that is removed as soon as the engine exits, or as the control plane exits
 * if it does first; one left by a control plane that was killed is removed at the next start. The
 * engine runs in a process group of its own, which a timeout kills whole.
 */
export class WhisperEngine implements SpeechEngine {
  readonly engine: TranscriptionEngine;
  readonly #config: SpeechConfig;
  readonly #prompt: string;
  readonly #timeoutMs: number;
  readonly #warmUpTimeoutMs: number;

  private constructor(
    config: SpeechConfig,
    version: string,
    timeoutMs: number,
    warmUpTimeoutMs: number,
  ) {
    this.#config = config;
    this.engine = { name: 'whisper.cpp', version };
    this.#prompt = `${PROMPT_WORDS.join(', ')}.`;
    this.#timeoutMs = timeoutMs;
    this.#warmUpTimeoutMs = warmUpTimeoutMs;
  }

  /**
   * Removes clips a killed control plane left behind, then asks the binary for its version, so a
   * binary that cannot answer, or is not the pinned release, stops startup.
   */
  static async open(
    config: SpeechConfig,
    timeoutMs: number = ENGINE_TIMEOUT_MS,
    warmUpTimeoutMs: number = WARM_UP_TIMEOUT_MS,
    versionTimeoutMs: number = VERSION_TIMEOUT_MS,
  ): Promise<WhisperEngine> {
    await removeLeftovers();
    const output = await new Promise<string>((resolve, reject) => {
      execFile(
        config.binary,
        ['--version'],
        { env: {}, timeout: versionTimeoutMs },
        (error, stdout) => {
          if (error === null) resolve(stdout);
          else if (error.killed) {
            reject(
              new Error(
                `${config.binary} did not answer --version within ${versionTimeoutMs / 1000} s.`,
              ),
            );
          } else reject(error);
        },
      );
    });
    const version = /whisper\.cpp version: (\S{1,64})/.exec(output)?.[1];
    if (version === undefined)
      throw new Error(`${config.binary} did not report a whisper.cpp version.`);
    if (!PINNED_VERSION.test(version)) {
      throw new Error(
        `${config.binary} is whisper.cpp ${version}; Halcyonic runs whisper.cpp 1.9.4.`,
      );
    }
    if (!stopsOnExit) {
      stopsOnExit = true;
      process.on('exit', stopRunning);
    }
    return new WhisperEngine(config, version, timeoutMs, warmUpTimeoutMs);
  }

  async transcribe(wav: Buffer): Promise<EngineResult> {
    return this.#run(wav, true, this.#timeoutMs);
  }

  /**
   * One transcription of a second of silence without voice activity detection, which would skip
   * the model, so the GPU's shaders are ready before anyone speaks. Returns how long it took.
   */
  async warmUp(): Promise<number> {
    const started = performance.now();
    const result = await this.#run(writeWav(Buffer.alloc(32_000)), false, this.#warmUpTimeoutMs);
    if (result.kind === 'failed') throw new SpeechEngineError(result.message);
    return Math.round(performance.now() - started);
  }

  async #run(wav: Buffer, detectSpeech: boolean, timeoutMs: number): Promise<EngineResult> {
    const run: Run = { directory: await mkdtemp(join(tmpdir(), CLIP_PREFIX)) };
    const directory = run.directory;
    running.add(run);
    try {
      const clip = join(directory, 'clip.wav');
      await writeFile(clip, wav, { mode: 0o600 });
      const args = [
        '-m',
        this.#config.model,
        '-l',
        LANGUAGE,
        '-nt',
        '-np',
        '--prompt',
        this.#prompt,
      ];
      if (detectSpeech) args.push('--vad', '-vm', this.#config.vadModel, '-vp', '500');
      args.push('-f', clip);
      return await this.#launch(args, timeoutMs, run);
    } finally {
      running.delete(run);
      await rm(directory, { recursive: true, force: true });
    }
  }

  /**
   * Runs the engine and answers once it has ended, so no transcription overlaps another. Stopped,
   * its whole process group is killed and the answer comes as the engine exits, whatever a
   * descendant still holds open.
   */
  #launch(args: string[], timeoutMs: number, run: Run): Promise<EngineResult> {
    return new Promise((resolve) => {
      const child = spawn(this.#config.binary, args, {
        env: {},
        stdio: ['ignore', 'pipe', 'ignore'],
        detached: true,
      });
      run.child = child;
      const chunks: Buffer[] = [];
      let length = 0;
      let stopped: string | null = null;
      let settled = false;
      const settle = (result: EngineResult) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        resolve(result);
      };
      const stop = (reason: string) => {
        if (stopped !== null) return;
        stopped = reason;
        killGroup(child);
        child.stdout.destroy();
      };
      const timer = setTimeout(
        () => stop(`The engine took longer than ${timeoutMs / 1000} s.`),
        timeoutMs,
      );
      child.stdout.on('data', (chunk: Buffer) => {
        length += chunk.length;
        if (length > MAX_OUTPUT_BYTES) stop('The engine wrote more than a transcript.');
        else chunks.push(chunk);
      });
      child.on('error', () =>
        settle({ kind: 'failed', message: 'The engine could not be started.' }),
      );
      child.on('exit', () => {
        if (stopped !== null) settle({ kind: 'failed', message: stopped });
      });
      child.on('close', (code) => {
        if (stopped !== null) settle({ kind: 'failed', message: stopped });
        else if (code !== 0)
          settle({ kind: 'failed', message: 'The engine stopped with an error.' });
        else {
          const text = Buffer.concat(chunks)
            .toString('utf8')
            .split(/\s+/)
            .filter(Boolean)
            .join(' ');
          settle({ kind: 'text', text });
        }
      });
    });
  }
}

/** Kills the engine and everything it started, which share its process group. */
function killGroup(child: ChildProcess): void {
  if (child.pid === undefined) return;
  try {
    process.kill(-child.pid, 'SIGKILL');
  } catch {
    // Already gone.
  }
}

/** As the control plane exits mid-clip: stops the engine and removes the clip, synchronously. */
function stopRunning(): void {
  for (const run of running) {
    if (run.child !== undefined) killGroup(run.child);
    rmSync(run.directory, { recursive: true, force: true });
  }
  running.clear();
}

/** Removes clip directories of this user's that a control plane killed mid-clip left behind. */
async function removeLeftovers(): Promise<void> {
  const base = tmpdir();
  const now = Date.now();
  for (const name of await readdir(base)) {
    if (!name.startsWith(CLIP_PREFIX)) continue;
    const path = join(base, name);
    const info = await lstat(path).catch(() => null);
    if (info === null || !info.isDirectory() || info.uid !== process.getuid?.()) continue;
    if (now - info.mtimeMs < LEFTOVER_MS) continue;
    await rm(path, { recursive: true, force: true });
  }
}
