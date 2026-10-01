import { execFile, spawn } from 'node:child_process';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { TranscriptionEngine } from '@halcyonic/contracts';
import type { SpeechConfig } from '../config.ts';
import { writeWav } from './wav.ts';

/** How long one transcription may take before the engine is stopped. */
export const ENGINE_TIMEOUT_MS = 15_000;

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

/**
 * whisper.cpp's `whisper-cli`, launched once per clip with no environment and no port (ADR 0021):
 * English, Halcyonic's own words as the prompt, and Silero voice activity detection with half a
 * second around speech, so a clip with no speech gives no text. The clip is written to a private
 * temporary directory that is removed as soon as the engine exits.
 */
export class WhisperEngine implements SpeechEngine {
  readonly engine: TranscriptionEngine;
  readonly #config: SpeechConfig;
  readonly #prompt: string;
  readonly #timeoutMs: number;

  private constructor(config: SpeechConfig, version: string, timeoutMs: number) {
    this.#config = config;
    this.engine = { name: 'whisper.cpp', version };
    this.#prompt = `${PROMPT_WORDS.join(', ')}.`;
    this.#timeoutMs = timeoutMs;
  }

  /** Asks the binary for its version, so a binary that cannot answer stops startup. */
  static async open(
    config: SpeechConfig,
    timeoutMs: number = ENGINE_TIMEOUT_MS,
  ): Promise<WhisperEngine> {
    const output = await new Promise<string>((resolve, reject) => {
      execFile(config.binary, ['--version'], { env: {}, timeout: 5_000 }, (error, stdout) =>
        error === null ? resolve(stdout) : reject(error),
      );
    });
    const version = /whisper\.cpp version: (\S{1,64})/.exec(output)?.[1];
    if (version === undefined)
      throw new Error(`${config.binary} did not report a whisper.cpp version.`);
    return new WhisperEngine(config, version, timeoutMs);
  }

  async transcribe(wav: Buffer): Promise<EngineResult> {
    return this.#run(wav, true);
  }

  /**
   * One transcription of a second of silence without voice activity detection, which would skip
   * the model, so the GPU's shaders are ready before anyone speaks. Returns how long it took.
   */
  async warmUp(): Promise<number> {
    const started = performance.now();
    const result = await this.#run(writeWav(Buffer.alloc(32_000)), false);
    if (result.kind === 'failed') throw new Error(result.message);
    return Math.round(performance.now() - started);
  }

  async #run(wav: Buffer, detectSpeech: boolean): Promise<EngineResult> {
    const directory = await mkdtemp(join(tmpdir(), 'halcyonic-speech-'));
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
      return await this.#launch(args);
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  }

  /** Runs the engine and answers once its process has ended, so no transcription overlaps another. */
  #launch(args: string[]): Promise<EngineResult> {
    return new Promise((resolve) => {
      const child = spawn(this.#config.binary, args, {
        env: {},
        stdio: ['ignore', 'pipe', 'ignore'],
      });
      const chunks: Buffer[] = [];
      let length = 0;
      let stopped: string | null = null;
      const stop = (reason: string) => {
        stopped ??= reason;
        child.kill('SIGKILL');
      };
      const timer = setTimeout(
        () => stop(`The engine took longer than ${this.#timeoutMs / 1000} s.`),
        this.#timeoutMs,
      );
      child.stdout.on('data', (chunk: Buffer) => {
        length += chunk.length;
        if (length > MAX_OUTPUT_BYTES) stop('The engine wrote more than a transcript.');
        else chunks.push(chunk);
      });
      child.on('error', () => {
        clearTimeout(timer);
        resolve({ kind: 'failed', message: 'The engine could not be started.' });
      });
      child.on('close', (code) => {
        clearTimeout(timer);
        if (stopped !== null) resolve({ kind: 'failed', message: stopped });
        else if (code !== 0)
          resolve({ kind: 'failed', message: 'The engine stopped with an error.' });
        else {
          const text = Buffer.concat(chunks)
            .toString('utf8')
            .split(/\s+/)
            .filter(Boolean)
            .join(' ');
          resolve({ kind: 'text', text });
        }
      });
    });
  }
}
