import { lstat, readdir } from 'node:fs/promises';
import { join } from 'node:path';

/**
 * The folder in the data directory that is Codex's home, its `CODEX_HOME`, never the person's
 * `~/.codex`: mode 700, made by the adapter when missing, its `config.toml` written by
 * `pnpm mac-setup local-model` (local-models.md).
 */
export const CODEX_HOME_FOLDER = 'codex-home';

/** A Codex thread id, the only kind of id looked for on disk. */
const THREAD_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Whether a session is one Halcyonic's Codex wrote in its own home, which neither Salidium nor
 * Seorak reads (understanding-and-evaluation.md): a Codex thread whose rollout,
 * `rollout-<time>-<thread id>.jsonl`, is in the home's `sessions/YYYY/MM/DD` for the day the
 * execution started, the day before or the day after, by this Mac's local date, as Codex names the
 * folders. Decided by Codex's own record of the thread, since the journal does not say which home
 * an execution used: a thread Codex ran in the person's `~/.codex` before 2026-10-07 has no rollout
 * here and is asked about as before.
 *
 * Only names are read, never a file's contents, and no link is followed at any level: the home,
 * `sessions` and each date folder must be real folders and the rollout a regular file. The id is
 * checked to be a thread id before it reaches a path. A folder that can't be read holds nothing.
 */
export async function inHalcyonicsCodexHome(
  codexHome: string,
  runtimeKind: string,
  nativeId: string,
  startedAt: string,
): Promise<boolean> {
  if (runtimeKind !== 'codex' || !THREAD_ID.test(nativeId)) return false;
  const started = Date.parse(startedAt);
  if (Number.isNaN(started)) return false;
  const name = (entry: string) =>
    entry.startsWith('rollout-') &&
    entry.toLowerCase().endsWith(`-${nativeId.toLowerCase()}.jsonl`);
  const sessions = join(codexHome, 'sessions');
  if (!(await isFolder(codexHome)) || !(await isFolder(sessions))) return false;
  const date = new Date(started);
  for (const offset of [0, -1, 1]) {
    // By calendar day, so a day of 23 or 25 hours never skips a date.
    const day = new Date(date.getFullYear(), date.getMonth(), date.getDate() + offset);
    const parts = [
      String(day.getFullYear()).padStart(4, '0'),
      String(day.getMonth() + 1).padStart(2, '0'),
      String(day.getDate()).padStart(2, '0'),
    ];
    let folder = sessions;
    let real = true;
    for (const part of parts) {
      folder = join(folder, part);
      if (!(await isFolder(folder))) {
        real = false;
        break;
      }
    }
    if (!real) continue;
    try {
      const entries = await readdir(folder, { withFileTypes: true });
      if (entries.some((entry) => entry.isFile() && name(entry.name))) return true;
    } catch {
      // A folder that can't be read holds nothing.
    }
  }
  return false;
}

/** Whether a path is a real folder, never a link to one. */
async function isFolder(path: string): Promise<boolean> {
  try {
    return (await lstat(path)).isDirectory();
  } catch {
    return false;
  }
}
