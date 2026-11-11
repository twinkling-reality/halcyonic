import { constants, realpathSync } from 'node:fs';
import { open } from 'node:fs/promises';
import { join, sep } from 'node:path';
import {
  CHECK_STEP_OUTPUT_LINE_CHARS,
  CHECK_STEP_OUTPUT_LINES,
  type CheckId,
  type CheckpointId,
  type CheckStepOutputQuery,
  type CheckStepOutputResponse,
  PROJECT_CHANGE_FILES,
  PROJECT_FILE_BYTES,
  PROJECT_FILE_DIFF_BYTES,
  PROJECT_FILE_DIFF_LINES,
  type Principal,
  type ProjectChangesQuery,
  type ProjectChangesResponse,
  type ProjectFileChangeQuery,
  type ProjectFileChangeResponse,
  type ProjectFileQuery,
  type ProjectFileResponse,
  type ProjectId,
  type Timestamp,
} from '@halcyonic/contracts';
import { secretShapedPath } from '@halcyonic/runtime-core';
import { NO_LINK } from '../observer/file-observer.ts';
import { checkLogLines } from '../preview/check-output.ts';
import type { ControlPlane } from './control-plane.ts';

/**
 * Reads of a project's files for the headset (the parity map's gaps 5, 10 and 4): what changed
 * between two checkpoints and one file's change, one file of the folder as it is now, and a check
 * step's output. Read only, for any runtime; owner-journal principals only, as the tool result reads;
 * every text bounded and cleared of the secrets Halcyonic holds, and given as outside words.
 */
export class ProjectReads {
  readonly #plane: ControlPlane;

  constructor(plane: ControlPlane) {
    this.#plane = plane;
  }

  async changes(
    projectId: string,
    query: ProjectChangesQuery,
    principal: Principal | null,
  ): Promise<ProjectChangesResponse | undefined> {
    const checkpoints = this.#plane.checkpoints;
    if (checkpoints === null || !this.#allowed(projectId, query.journal_id, principal))
      return undefined;
    if (!this.#ownCheckpoints(projectId, query.from, query.to)) return undefined;
    const compared = await checkpoints.compare(projectId, query.from, query.to);
    if (!compared.ok) return undefined;
    const files = compared.files.slice(0, PROJECT_CHANGE_FILES).map((file) => ({
      path: file.path,
      change: file.change,
      added_lines: file.addedLines,
      removed_lines: file.removedLines,
      binary: file.binary,
    }));
    return {
      project_id: projectId as ProjectId,
      from: query.from,
      to: query.to,
      files,
      files_total: compared.files.length,
      complete: files.length === compared.files.length,
      source: 'project_files',
    };
  }

  async fileChange(
    projectId: string,
    query: ProjectFileChangeQuery,
    principal: Principal | null,
  ): Promise<ProjectFileChangeResponse | undefined> {
    const checkpoints = this.#plane.checkpoints;
    if (checkpoints === null || !this.#allowed(projectId, query.journal_id, principal))
      return undefined;
    if (!this.#ownCheckpoints(projectId, query.from, query.to)) return undefined;
    const withheld = withheldPath(query.path);
    const read = await checkpoints.compareFile(
      projectId,
      query.from,
      query.to,
      query.path,
      withheld ? 0 : PROJECT_FILE_DIFF_BYTES,
    );
    if (!read.ok) return undefined;
    let diff: string | null = null;
    let complete = read.complete;
    if (!withheld && read.diff !== null) {
      const lines = read.diff.split('\n');
      if (lines.length > PROJECT_FILE_DIFF_LINES + 1) complete = false;
      diff = this.#plane.redaction.held(lines.slice(0, PROJECT_FILE_DIFF_LINES).join('\n'));
    }
    return {
      project_id: projectId as ProjectId,
      from: query.from,
      to: query.to,
      path: query.path,
      change: read.file.change,
      binary: read.file.binary,
      withheld,
      diff,
      complete: withheld || read.file.binary ? true : complete,
      source: 'project_files',
    };
  }

  async file(
    projectId: string,
    query: ProjectFileQuery,
    principal: Principal | null,
  ): Promise<ProjectFileResponse | undefined> {
    if (!this.#allowed(projectId, query.journal_id, principal)) return undefined;
    const folder = this.#plane.projection.project(projectId)?.location?.path;
    if (folder === undefined || !safeRelative(query.path)) return undefined;
    const full = join(folder, query.path);
    if (!full.startsWith(folder + sep)) return undefined;
    // Real paths, compared without case: the Mac's volumes usually ignore it (AUDIT2 S4).
    const lower = full.toLowerCase();
    for (const own of this.#plane.protectedFolders) {
      const mine = (realpathOr(own) ?? own).toLowerCase();
      if (lower === mine || lower.startsWith(mine + sep) || mine.startsWith(lower + sep))
        return undefined;
    }
    // Opened once, never through a link anywhere in its path (`NO_LINK`), and judged and read through
    // that one handle, so nothing swapped in after a look is read.
    const handle = await open(full, constants.O_RDONLY | constants.O_NONBLOCK | NO_LINK).catch(
      () => null,
    );
    if (handle === null) return undefined;
    try {
      const stat = await handle.stat();
      if (!stat.isFile()) return undefined;
      const common = {
        project_id: projectId as ProjectId,
        path: query.path,
        size_bytes: stat.size,
        modified_at: stat.mtime.toISOString() as Timestamp,
        source: 'project_files' as const,
      };
      if (withheldPath(query.path)) return { ...common, kind: 'withheld', text: null, lines: null };
      if (stat.size > PROJECT_FILE_BYTES)
        return { ...common, kind: 'too_large', text: null, lines: null };
      const buffer = Buffer.alloc(stat.size + 1);
      let length = 0;
      while (length < buffer.length) {
        const { bytesRead } = await handle.read(buffer, length, buffer.length - length, length);
        if (bytesRead === 0) break;
        length += bytesRead;
      }
      if (length > PROJECT_FILE_BYTES)
        return { ...common, kind: 'too_large', text: null, lines: null };
      const bytes = buffer.subarray(0, length);
      const text = utf8(bytes);
      if (text === null)
        return { ...common, size_bytes: length, kind: 'binary', text: null, lines: null };
      return {
        ...common,
        size_bytes: length,
        kind: 'text',
        text: this.#plane.redaction.held(text),
        lines: text === '' ? 0 : text.split('\n').length - (text.endsWith('\n') ? 1 : 0),
      };
    } catch {
      return undefined;
    } finally {
      await handle.close();
    }
  }

  checkOutput(
    projectId: string,
    checkId: string,
    query: CheckStepOutputQuery,
    principal: Principal | null,
  ): CheckStepOutputResponse | undefined {
    const previews = this.#plane.previews;
    if (previews === null || !this.#allowed(projectId, query.journal_id, principal))
      return undefined;
    const step = previews.checkStepLog(projectId as ProjectId, checkId as CheckId, query.step);
    if (step === null) return undefined;
    const read =
      step.log === null
        ? { lines: [], total: 0 }
        : checkLogLines(
            step.log,
            step.places,
            CHECK_STEP_OUTPUT_LINES,
            CHECK_STEP_OUTPUT_LINE_CHARS,
          );
    const lines = read.lines.map((line) =>
      this.#plane.redaction.held(line).slice(0, CHECK_STEP_OUTPUT_LINE_CHARS),
    );
    return {
      project_id: projectId as ProjectId,
      check_id: checkId as CheckId,
      step: query.step,
      exit: step.exit,
      lines,
      lines_total: read.total,
      // A step that ran with no readable log is not complete; one that never ran has nothing more.
      complete: step.exit === null || (step.log !== null && lines.length === read.total),
      source: 'check_output',
    };
  }

  /** Owner-journal principals only (the local owner or a still-paired device), of this journal. */
  #allowed(projectId: string, journalId: string, principal: Principal | null): boolean {
    if (principal === null) return false;
    if (
      principal.kind === 'device' &&
      this.#plane.projection.device(principal.device_id)?.revoked_at !== null
    )
      return false;
    return (
      journalId === this.#plane.journal.info.journal_id &&
      this.#plane.projection.project(projectId) !== undefined
    );
  }

  /** Both checkpoints are the project's own, as its journal records them. */
  #ownCheckpoints(projectId: string, from: CheckpointId, to: CheckpointId): boolean {
    const ids = new Set(this.#plane.projection.checkpoints(projectId).map((c) => c.checkpoint_id));
    return ids.has(from) && ids.has(to);
  }
}

/**
 * Whether a project-relative path names a file that may hold secrets: the one list every place uses
 * (runtime-core `secretShapedPath`), by name only.
 */
export const withheldPath = secretShapedPath;

/** A relative path inside the folder: no leading `/`, no `.` or `..` part, no empty part, no NUL. */
function safeRelative(path: string): boolean {
  return (
    path.length > 0 &&
    !path.startsWith('/') &&
    !path.includes('\0') &&
    path.split('/').every((part) => part !== '' && part !== '.' && part !== '..')
  );
}

/** The bytes as text when they are valid UTF-8 with no NUL, else null. */
function utf8(bytes: Buffer): string | null {
  if (bytes.includes(0)) return null;
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return null;
  }
}

function realpathOr(path: string): string | null {
  try {
    return realpathSync(path);
  } catch {
    return null;
  }
}
