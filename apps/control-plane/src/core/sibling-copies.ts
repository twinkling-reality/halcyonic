import { isAbsolute, join, relative, sep } from 'node:path';
import type { ApprovalPath } from '@halcyonic/contracts';
import { resolvedPath } from './real-path.ts';

/** The host's own folders a task's run never reads: where the copies live, the data folder, and the person's home. */
export interface HostFolders {
  /** The folder every task's own copy lives in (ADR 0045), or null where copies are off. */
  readonly tasks: string | null;
  /** The control plane's data folder. */
  readonly data: string;
  /** The person's home, whose `.halcyonic*` folders are Halcyonic's own homes. */
  readonly home: string;
  /**
   * The host folders a run of this kind in this folder is given as its own (its confinement's temporary and
   * settings folders), which are never refused: in the data folder, but the run's.
   */
  readonly runOwn?: (runtimeKind: string, directory: string) => readonly string[];
}

/** Why the host refuses a request that reaches outside the task's own copy into a place no task may read. */
export type Intrusion = 'another_task_copy' | 'host_folder';

/**
 * What the agent is told, where its app carries a denial's words, for each refusal: why, and that it can go on.
 * AUDIT2's words (2026-11-18 03:13Z): true for a write as for a read, and for a folder as for a file.
 */
export const INTRUSION_WORDS: Readonly<Record<Intrusion, string>> = {
  another_task_copy:
    "This path is in another task's copy, which this task may not use, so the request was not carried out. Work in this task's own copy.",
  host_folder:
    "This path is in a folder this task may not use, so the request was not carried out. Work in this task's own copy.",
};

/**
 * Whether a request of a run in its task's own copy is one the host refuses itself (AUDIT3's ruling, 2026-11-18 HR1
 * to HR4): every place it names, resolved through links and compared case folded on the Mac, is inside the task's
 * own copy or the run's own host folders (`runOwn`), or else in another task's copy or a host folder (the data folder, or a
 * `.halcyonic*` folder in the person's home), and at least one is the latter. The sandbox refuses those places
 * anyway, so asking a person is no real choice, only a stall. A request that also names any other place (the
 * person's own folders, another project), a request not complete or cut, or one naming no place, is null: it reaches
 * the person as before. Never an answer that lets anything happen. It reads the file system only to resolve links.
 */
export function intrusionOf(
  request: {
    readonly paths: readonly ApprovalPath[] | undefined;
    readonly complete: boolean;
    readonly cut: boolean;
  },
  ownCopy: string,
  folders: HostFolders,
  runOwn: readonly string[] = [],
): Intrusion | null {
  if (!request.complete || request.cut) return null;
  const named = request.paths;
  if (named === undefined || named.length === 0) return null;
  const copy = folded(resolvedPath(ownCopy));
  const own = runOwn.map((folder) => folded(resolvedPath(folder)));
  const tasks = folders.tasks === null ? null : folded(resolvedPath(folders.tasks));
  const data = folded(resolvedPath(folders.data));
  const home = folded(resolvedPath(folders.home));
  let found: Intrusion | null = null;
  for (const each of named) {
    const path = folded(resolvedPath(isAbsolute(each.path) ? each.path : join(ownCopy, each.path)));
    if (inside(path, copy) || own.some((folder) => inside(path, folder))) continue;
    if (tasks !== null && inside(path, tasks)) {
      found = 'another_task_copy';
      continue;
    }
    if (inside(path, data) || inHalcyonicHome(path, home)) {
      found ??= 'host_folder';
      continue;
    }
    return null;
  }
  return found;
}

/** Paths compare case folded where the file system folds case, as the Mac's does. */
function folded(path: string): string {
  return process.platform === 'darwin' ? path.toLowerCase() : path;
}

/** Whether `path` is `folder` or inside it. */
function inside(path: string, folder: string): boolean {
  return path === folder || path.startsWith(folder.endsWith(sep) ? folder : `${folder}${sep}`);
}

/** Whether `path` is in a folder of the person's home whose name starts `.halcyonic`. */
function inHalcyonicHome(path: string, home: string): boolean {
  if (!inside(path, home) || path === home) return false;
  const [first] = relative(home, path).split(sep);
  return first?.startsWith('.halcyonic') === true;
}
