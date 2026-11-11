import { createHash } from 'node:crypto';
import { closeSync, constants, fstatSync, openSync, readSync } from 'node:fs';
import { join } from 'node:path';
import { PROJECT_RULES_BYTES, type ProjectRulesFile } from '@halcyonic/contracts';
import { NO_LINK } from '../observer/file-observer.ts';
import { reviewDigest } from './review.ts';

/**
 * A project's own rules (E5): its `AGENTS.md`, else its `CLAUDE.md`, at the top of its folder. No
 * runtime loads them by itself under Halcyonic (Codex marks every project untrusted, OpenCode's server
 * leaves out project configuration, Claude Code loads the person's settings only), so they reach an
 * agent only where the person read them and chose them, and then the same way for every runtime.
 */

const FILES: readonly ProjectRulesFile[] = ['AGENTS.md', 'CLAUDE.md'];

export type RulesRead =
  | { readonly state: 'none' }
  | {
      readonly state: 'too_large' | 'unreadable';
      readonly file: ProjectRulesFile;
      readonly size: number | null;
    }
  | {
      readonly state: 'ok';
      readonly file: ProjectRulesFile;
      readonly text: string;
      readonly sha256: string;
      readonly size: number;
    };

/**
 * Reads the project's rules file once: the first of `AGENTS.md` and `CLAUDE.md` that is there, opened
 * without following a link anywhere in its path, judged and read through that one handle, at most
 * `PROJECT_RULES_BYTES`, valid UTF-8 with no NUL. A rules file that is there but cannot be passed on
 * stops the search: a `CLAUDE.md` never stands in for an `AGENTS.md` that is too large.
 */
export function readRules(folder: string): RulesRead {
  for (const file of FILES) {
    let handle: number;
    try {
      handle = openSync(join(folder, file), constants.O_RDONLY | constants.O_NONBLOCK | NO_LINK);
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') continue;
      return { state: 'unreadable', file, size: null };
    }
    try {
      const stat = fstatSync(handle);
      if (!stat.isFile()) return { state: 'unreadable', file, size: null };
      if (stat.size > PROJECT_RULES_BYTES) return { state: 'too_large', file, size: stat.size };
      const buffer = Buffer.alloc(stat.size + 1);
      let length = 0;
      while (length < buffer.length) {
        const read = readSync(handle, buffer, length, buffer.length - length, length);
        if (read === 0) break;
        length += read;
      }
      if (length > PROJECT_RULES_BYTES) return { state: 'too_large', file, size: length };
      const bytes = buffer.subarray(0, length);
      if (bytes.includes(0)) return { state: 'unreadable', file, size: length };
      let text: string;
      try {
        text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
      } catch {
        return { state: 'unreadable', file, size: length };
      }
      return {
        state: 'ok',
        file,
        text,
        sha256: createHash('sha256').update(bytes).digest('hex'),
        size: length,
      };
    } catch {
      return { state: 'unreadable', file, size: null };
    } finally {
      closeSync(handle);
    }
  }
  return { state: 'none' };
}

/** What the rules review covers (ADR 0051): the project, and the file and digest the person read. */
export function rulesReview(projectId: string, file: ProjectRulesFile, sha256: string): string {
  return reviewDigest('project.rules.set', { project_id: projectId }, { file, sha256 });
}

/**
 * The person's first instruction with the project's rules before it, marked as the project's own
 * words, not the person's and not Halcyonic's: the same for every runtime.
 */
export function withRules(instruction: string, file: ProjectRulesFile, text: string): string {
  return [
    `These are the project's own rules, from its ${file}. The person read them and chose to pass them on; they are the project's words, not the person's. They begin after this line and end at "End of the project's rules."`,
    '',
    text.replace(/\s+$/, ''),
    '',
    "End of the project's rules.",
    '',
    instruction,
  ].join('\n');
}
