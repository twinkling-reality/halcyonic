import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { arch, platform } from 'node:os';

/** A file Halcyonic was checked with, by its path under the data directory and its SHA-256. */
export interface PinnedFile {
  readonly path: string;
  readonly sha256: string;
}

export interface Pins {
  readonly opencode: PinnedFile;
  readonly codex: PinnedFile;
  readonly whisperModel: PinnedFile;
  readonly whisperVadModel: PinnedFile;
}

/**
 * The binaries and models Halcyonic was checked with on an Apple silicon Mac, as the runbook
 * installs them under the data directory: OpenCode 2.0.18 (opencode-capabilities.md), Codex 0.157.0
 * (codex-capabilities.md) and the voice models (voice-transcription.md).
 */
export const APPLE_SILICON_PINS: Pins = {
  opencode: {
    path: 'runtimes/opencode-2.0.18/node_modules/@opencode/cli-darwin-arm64/bin/opencode',
    sha256: '6759c7f86f807d63d3984ca1970f9fa663ee303a35bb344eb223c71e200a96bf',
  },
  codex: {
    path: 'runtimes/codex-0.157.0/node_modules/@openai/codex-darwin-arm64/vendor/aarch64-apple-darwin/bin/codex',
    sha256: 'ad0be20d04e2ba6146ecdb51d7f8b7b0fe15420a15dc9b0057518d858f1f3714',
  },
  whisperModel: {
    path: 'speech/models/ggml-large-v3-turbo-q5_0.bin',
    sha256: '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2',
  },
  whisperVadModel: {
    path: 'speech/models/ggml-silero-v6.2.0.bin',
    sha256: '2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987',
  },
};

/** The pins for this computer's platform and processor; null where Halcyonic has none. */
export function pinsForThisMac(): Pins | null {
  return platform() === 'darwin' && arch() === 'arm64' ? APPLE_SILICON_PINS : null;
}

/** A file's SHA-256 as lowercase hex, or null when it can't be read. */
export async function sha256File(path: string): Promise<string | null> {
  const hash = createHash('sha256');
  try {
    for await (const chunk of createReadStream(path)) hash.update(chunk as Buffer);
  } catch {
    return null;
  }
  return hash.digest('hex');
}

/** Whether a file is the one pinned: null where there is no pin for this processor. */
export async function matchesPin(path: string, pin: PinnedFile | null): Promise<boolean | null> {
  if (pin === null) return null;
  return (await sha256File(path)) === pin.sha256;
}
