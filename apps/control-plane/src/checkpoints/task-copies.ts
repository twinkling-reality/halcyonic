import { execFile } from 'node:child_process';
import { lstat, mkdir, realpath, rm } from 'node:fs/promises';
import { basename, join } from 'node:path';
import type { CopyRefusalCode } from '@halcyonic/contracts';
import type { CheckpointStore } from './store.ts';
import { within } from './store.ts';

/**
 * Every task's own copy of its project's folder (ADR 0045): an APFS clone, made just before the
 * task's first start, in a folder of Halcyonic's own beside its data folder,
 * `<directory>/<project id>/<task id>/<the folder's own name>`. Ignored files come too, so
 * dependencies and builds work. The copy's base is a tree in the project's checkpoint store, taken
 * of the copy as it was made, so the store's limits, its own git and its confinement decide what a
 * copy may hold. Nothing is written to the project's folder or its `.git`.
 */

export interface TaskCopiesOptions {
  /** The folder copies live in. Never inside a project's folder nor Halcyonic's data folder. */
  readonly directory: string;
  readonly store: Pick<CheckpointStore, 'take'>;
  /** A UUID naming the base's tree in the store, as a checkpoint's id does. */
  readonly nextId: () => string;
  /** Folders no copied project may hold, such as Halcyonic's data folder. */
  readonly protect?: readonly string[];
  /** How long one copy may take before it is stopped and refused. */
  readonly timeoutMs?: number;
}

export type PrepareResult =
  | {
      readonly ok: true;
      readonly directory: string;
      readonly baseTree: string;
      readonly files: number;
    }
  | { readonly ok: false; readonly code: CopyRefusalCode; readonly message: string };

const ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const CP = '/bin/cp';
const DEFAULT_TIMEOUT_MS = 10 * 60_000;

export class TaskCopies {
  readonly #directory: string;
  readonly #store: Pick<CheckpointStore, 'take'>;
  readonly #nextId: () => string;
  readonly #protect: readonly string[];
  readonly #timeoutMs: number;

  constructor(options: TaskCopiesOptions) {
    this.#directory = options.directory;
    this.#store = options.store;
    this.#nextId = options.nextId;
    this.#protect = [options.directory, ...(options.protect ?? [])];
    this.#timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  }

  /** The folder copies live in, as configured. */
  get directory(): string {
    return this.#directory;
  }

  /**
   * Makes the task's copy of `folder`, the project's folder as the host resolved it, and keeps its
   * base. A copy left by an earlier attempt that never recorded one is replaced. Refused, with
   * nothing left behind, where a copy could still act on the person's repository, could hold
   * Halcyonic's own data, or is more than a checkpoint keeps.
   */
  async prepare(projectId: string, workstreamId: string, folder: string): Promise<PrepareResult> {
    if (!ID_PATTERN.test(projectId) || !ID_PATTERN.test(workstreamId))
      throw new Error('not a project or task id');
    const source = await lstat(folder).catch(() => null);
    if (source === null || !source.isDirectory())
      return refuse('location_missing', "The project's folder is not there.");
    const git = await lstat(join(folder, '.git')).catch(() => null);
    if (git !== null && !git.isDirectory())
      return refuse(
        'linked_repository',
        "The folder's .git is a file, so a copy would still change the repository it points to.",
      );
    const real = await realpath(folder);
    for (const path of this.#protect) {
      if (within(real, await resolvedOrSelf(path)))
        return refuse(
          'holds_host_data',
          "The folder holds Halcyonic's own data, which is never copied.",
        );
    }

    await mkdir(this.#directory, { recursive: true, mode: 0o700 });
    const root = await realpath(this.#directory);
    const parent = join(root, projectId, workstreamId);
    await mkdir(parent, { recursive: true, mode: 0o700 });
    // The task's own folder must be ours all the way down, never a link out of the copies' folder.
    if ((await realpath(parent)) !== parent)
      return refuse('copy_failed', "The task's folder for its copy is not Halcyonic's own.");
    const target = join(parent, basename(real));
    await rm(target, { recursive: true, force: true });

    const copied = await this.#clone(real, target);
    if (!copied.ok) {
      await rm(target, { recursive: true, force: true });
      return copied;
    }
    const base = await this.#store.take(projectId, target, this.#nextId());
    if (!base.ok) {
      await rm(target, { recursive: true, force: true });
      return refuse(refusalOf(base.code), base.message);
    }
    return { ok: true, directory: target, baseTree: base.tree, files: base.files };
  }

  /** `cp -c` clones with clonefile(2), keeping modes, times and links; it fails rather than copy bytes. */
  #clone(
    source: string,
    target: string,
  ): Promise<{ ok: true } | { ok: false; code: CopyRefusalCode; message: string }> {
    return new Promise((resolvePromise) => {
      execFile(
        CP,
        ['-c', '-p', '-R', source, target],
        { env: { PATH: '/usr/bin:/bin', LC_ALL: 'C' }, timeout: this.#timeoutMs },
        (error, _stdout, stderr) => {
          if (error === null) return resolvePromise({ ok: true });
          const said = String(stderr);
          resolvePromise(
            /clonefile|not supported|cross-device/i.test(said)
              ? {
                  ok: false,
                  code: 'cannot_clone',
                  message: "The folder's disk cannot make a copy that shares its files.",
                }
              : { ok: false, code: 'copy_failed', message: 'The copy of the folder failed.' },
          );
        },
      );
    });
  }
}

function refuse(code: CopyRefusalCode, message: string): PrepareResult {
  return { ok: false, code, message };
}

/** What a failed base checkpoint means for the copy. */
function refusalOf(code: string): CopyRefusalCode {
  switch (code) {
    case 'nested_repository':
    case 'too_large':
    case 'holds_host_data':
    case 'location_missing':
      return code;
    default:
      return 'copy_failed';
  }
}

async function resolvedOrSelf(path: string): Promise<string> {
  try {
    return await realpath(path);
  } catch {
    return path;
  }
}
