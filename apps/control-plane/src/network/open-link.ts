import { execFile } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { chmodSync, mkdirSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** Where the hand-over files live, in the data directory; only its owner can read them. */
export const OPEN_LINK_DIR = 'open-links';

/** How long a hand-over file stays: as long as the one-time code in it can be used. */
export const OPEN_LINK_KEEP_MS = 60_000;

export interface OpenLinkOptions {
  /** Opens a file in the person's default browser; on macOS, `open`. */
  readonly open?: (path: string) => void;
  readonly keepMs?: number;
  readonly now?: () => number;
}

/**
 * Removes hand-over files whose code has run out. `pnpm pair` exits as soon as it has opened one,
 * before its own timer could, so the control plane, which lives on, sweeps them too (AUDIT S1).
 */
export function sweepOpenLinks(
  dataDir: string,
  options: { readonly now?: () => number; readonly keepMs?: number } = {},
): void {
  const dir = join(dataDir, OPEN_LINK_DIR);
  const now = (options.now ?? Date.now)();
  const keepMs = options.keepMs ?? OPEN_LINK_KEEP_MS;
  let names: string[];
  try {
    names = readdirSync(dir);
  } catch {
    return;
  }
  for (const name of names) {
    const path = join(dir, name);
    try {
      if (now - statSync(path).mtimeMs > keepMs) rmSync(path, { force: true });
    } catch {
      // Gone meanwhile.
    }
  }
}

/**
 * Opens a link that carries a one-time code in the person's browser without putting it in any
 * process's arguments, where every process on the Mac can read it for a moment (AUDIT S4): the link
 * goes into a small page, mode 600, in a folder of the data directory only its owner can enter, and
 * the browser opens that file by its path, which tells nothing, and goes on to the link. The file is
 * removed once the code has run out, and older ones at the next call.
 *
 * @returns the file's path.
 */
export function openLinkPrivately(
  dataDir: string,
  link: string,
  options: OpenLinkOptions = {},
): string {
  const url = new URL(link);
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') {
    throw new Error('only a link to this computer is handed over');
  }
  const now = options.now ?? Date.now;
  const keepMs = options.keepMs ?? OPEN_LINK_KEEP_MS;
  const dir = join(dataDir, OPEN_LINK_DIR);
  mkdirSync(dir, { recursive: true, mode: 0o700 });
  chmodSync(dir, 0o700);
  sweepOpenLinks(dataDir, { now, keepMs });
  const path = join(dir, `${randomBytes(16).toString('hex')}.html`);
  // The URL's own characters only: a base64url code, digits and the fixed parts, so nothing breaks
  // out of the attribute.
  const href = url.href.replace(/[^A-Za-z0-9:/.?=_-]/g, '');
  writeFileSync(
    path,
    `<!doctype html><meta charset="utf-8"><meta name="referrer" content="no-referrer"><meta http-equiv="refresh" content="0;url=${href}"><title>Opening</title>\n`,
    { mode: 0o600, flag: 'wx' },
  );
  const open =
    options.open ??
    ((file: string) => {
      if (process.platform === 'darwin') execFile('/usr/bin/open', [file], () => {});
    });
  open(path);
  setTimeout(() => rmSync(path, { force: true }), keepMs).unref();
  return path;
}
