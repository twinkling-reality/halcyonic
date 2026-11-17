import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { arch, platform } from 'node:os';

/** A file Halcyonic was checked with, by its path under the data directory and its SHA-256. */
export interface PinnedFile {
  readonly path: string;
  readonly sha256: string;
  /**
   * Set where the pin is a whole folder's: `sha256` is then the digest of the folder's manifest, every entry's kind,
   * mode and path and every file's SHA-256 (as the browser's, AUDIT M1), so no file of it can change unseen.
   */
  readonly folder?: true;
}

export interface Pins {
  readonly opencode: PinnedFile;
  readonly codex: PinnedFile;
  readonly whisperModel: PinnedFile;
  readonly whisperVadModel: PinnedFile;
  /** The check's and the play's browser: chrome-headless-shell's binary, as the host copies it (S1). */
  readonly browser: PinnedFile;
  /** The agent apps over ACP (ADR 0067): the vendor's own builds, as the setup page unpacks them. */
  readonly kiro: PinnedFile;
  readonly antigravity: PinnedFile;
  /** Cursor's agent CLI: a launcher beside its own Node and its code, so pinned as its whole folder (AUDIT3 SU1). */
  readonly cursor: PinnedFile;
}

/**
 * The binaries and models Halcyonic was checked with on an Apple silicon Mac, as the runbook
 * installs them under the data directory: OpenCode 2.0.18 (opencode-capabilities.md), Codex 0.157.0
 * (codex-capabilities.md), the voice models (voice-transcription.md) and the check's and play's browser
 * (iwsdk-starter.md).
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
  // chrome-headless-shell 156.0.8078.4 for mac-arm64, as Playwright's chromium_headless_shell-1248 holds it
  // (validation/iwsdk-starter.md, S1). Not one file's hash: the digest of the manifest of the whole folder,
  // every entry's kind, mode and path and every file's SHA-256 (AUDIT M1), kept in
  // preview/browser-manifest.txt. The host's copy lives at <data>/browser/current.
  browser: {
    path: 'browser/current',
    sha256: '0966bb448b30d115b303849668c4ebf1f00cf739bc728e4b747c1a502ee00ef2',
  },
  // Kiro CLI 3.4.0's universal-darwin build and Antigravity CLI 1.5.10's darwin-arm build, each from its vendor's
  // own download, checked against the vendor's manifest (validation/acp-agents.md).
  kiro: {
    path: 'runtimes/kiro-3.4.0/kirocli/bin/kiro-cli',
    sha256: '65902eb19c9d657ddfb19dc1b89b72226d75411a529dc9d0cdccadd858b0db93',
  },
  antigravity: {
    path: 'runtimes/antigravity-1.5.10/antigravity',
    sha256: '780ee763a9938a37b45c7f9d6ae31747d7c574358fd10c64d3a1cdc36faf2798',
  },
  // Cursor's agent CLI 2026.11.13-eacade1 for darwin-arm64, from Cursor's own download: its `dist-package` folder as
  // the setup page unpacks it, 435 entries, by the digest of their manifest (validation/acp-agents.md).
  cursor: {
    path: 'runtimes/cursor-2026.11.13/dist-package',
    sha256: '8f6e33e46e9559c769f695fee357eee0c98e7b72e42e1de50279476cc697e6ee',
    folder: true,
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

/** Whether a file, or a folder pinned whole, is the one pinned: null where there is no pin for this processor. */
export async function matchesPin(path: string, pin: PinnedFile | null): Promise<boolean | null> {
  if (pin === null) return null;
  return (await pinnedDigest(path, pin)) === pin.sha256;
}

/** What a path's pin is compared with: its file's SHA-256, or its folder's manifest digest; null where unreadable. */
export async function pinnedDigest(path: string, pin: PinnedFile): Promise<string | null> {
  if (pin.folder !== true) return sha256File(path);
  // Loaded only for a folder's pin, so no plain file's check pulls in the preview's code.
  const { browserManifest, manifestDigest } = await import('./preview/play.ts');
  const manifest = browserManifest(path);
  return Array.isArray(manifest) ? manifestDigest(manifest) : null;
}
