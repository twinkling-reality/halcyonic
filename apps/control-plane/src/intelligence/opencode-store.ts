import { lstat } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { sandboxedSessionStore } from '@halcyonic/integration-opencode';

/**
 * Whether an OpenCode session is one Halcyonic's sandboxed OpenCode server kept in its own store,
 * which Salidium does not read as installed: Salidium reads `opencode/opencode.db` under its own
 * process's `XDG_DATA_HOME`, else under `~/.local/share` (salidium-consumer-contract.md), while on
 * macOS Halcyonic's OpenCode runs only inside its sandbox, with a data folder of its own (ADR 0028).
 *
 * Decided from metadata, never the store's contents: the store must be a regular file and every
 * folder from the sandbox's down to it a real folder, never a link, and the store must have been
 * made no later than the execution started, since the server that ran the session made it at its
 * first launch, before any session. A session from before the store, as one OpenCode ran before
 * its sandbox (2026-10-08) or on a host where it runs unsandboxed, is asked about as before.
 */
export async function inHalcyonicsOpenCodeStore(
  sandboxFolder: string,
  runtimeKind: string,
  startedAt: string,
): Promise<boolean> {
  if (runtimeKind !== 'opencode') return false;
  const started = Date.parse(startedAt);
  if (Number.isNaN(started)) return false;
  // Only the folders from the sandbox's down are checked; where the data directory itself lies is
  // the host's own choice.
  const sandbox = resolve(sandboxFolder);
  const store = sandboxedSessionStore(sandbox);
  const folders: string[] = [];
  for (let folder = dirname(store); ; folder = dirname(folder)) {
    folders.unshift(folder);
    if (folder === sandbox || folder === dirname(folder)) break;
  }
  try {
    for (const folder of folders) if (!(await lstat(folder)).isDirectory()) return false;
    const file = await lstat(store);
    return file.isFile() && file.birthtimeMs <= started;
  } catch {
    // A store that can't be read about is no store of Halcyonic's.
    return false;
  }
}
