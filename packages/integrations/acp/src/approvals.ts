import { existsSync } from 'node:fs';
import { isAbsolute, resolve } from 'node:path';
import type { ApprovalChange, ApprovalPath, ApprovalSubject } from '@halcyonic/contracts';
import type { AcpProfile } from './profiles.ts';

/** The kinds of tool call ACP names that change files (`ToolKind`, protocol version 1). */
const FILE_KINDS = new Set(['edit', 'delete', 'move']);

/** The largest text a change carries whole (`ApprovalChange.before` and `after`). */
const CHANGE_TEXT_MAX = 32000;

/** At most this many places on a request (`ApprovalSubject.paths`); more and none are given. */
const PATHS_MAX = 8;

/** At most this many changes on a request (`ApprovalSubject.changes`); more and the request is not whole. */
const CHANGES_MAX = 16;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function text(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

/** The name a request goes by: the app's own tool name where it gives one, else ACP's kind. */
export function toolNameOf(toolCall: Record<string, unknown>): string {
  const name = text(toolCall.name);
  if (name !== null && name.trim() !== '') return name;
  const kind = text(toolCall.kind);
  return kind !== null && kind.trim() !== '' ? kind : 'tool';
}

/**
 * The changes a file request makes, from the ACP `diff` content the app put on it, or null where
 * they cannot be given whole and truly: a diff with no path or too long a text, a replacement the
 * profile does not know the reach of (ACP's `oldText` may be the whole file or the part replaced,
 * and the request does not say whether every occurrence is replaced), or no diff at all. The real
 * path is the host's to add.
 */
export function requestChanges(
  toolCall: Record<string, unknown>,
  profile: AcpProfile,
  folder: string,
): ApprovalChange[] | null {
  const content = Array.isArray(toolCall.content) ? toolCall.content : [];
  const diffs = content.map(record).filter((entry) => entry !== null && entry.type === 'diff');
  if (diffs.length === 0 || diffs.length > CHANGES_MAX) return null;
  const name = toolNameOf(toolCall);
  const changes: ApprovalChange[] = [];
  for (const diff of diffs) {
    if (diff === null) return null;
    const path = text(diff.path);
    const after = text(diff.newText);
    const before = diff.oldText === undefined || diff.oldText === null ? null : text(diff.oldText);
    if (path === null || path === '' || after === null) return null;
    if (diff.oldText !== undefined && diff.oldText !== null && before === null) return null;
    if (after.length > CHANGE_TEXT_MAX || (before?.length ?? 0) > CHANGE_TEXT_MAX) return null;
    const base = { path, real_path: null, to_path: null, cell: null } as const;
    if (before === null) {
      // The whole new file: a new one, or a rewrite of one already in the task's folder.
      const exists = existsSync(isAbsolute(path) ? path : resolve(folder, path));
      changes.push({
        ...base,
        kind: exists ? 'update' : 'add',
        shape: 'content',
        before: null,
        after,
        every: false,
      });
      continue;
    }
    if (!profile.replacesEveryOccurrence.includes(name)) return null;
    changes.push({ ...base, kind: 'update', shape: 'replace', before, after, every: true });
  }
  return changes;
}

/** Every place the request names, from ACP's `locations`, or null where there are none or too many. */
function requestPaths(toolCall: Record<string, unknown>): ApprovalPath[] | null {
  const locations = Array.isArray(toolCall.locations) ? toolCall.locations : [];
  if (locations.length === 0 || locations.length > PATHS_MAX) return null;
  const access = FILE_KINDS.has(text(toolCall.kind) ?? '') ? 'write' : 'read';
  const paths: ApprovalPath[] = [];
  for (const location of locations) {
    const path = text(record(location)?.path);
    if (path === null || path === '') return null;
    paths.push({ path, access });
  }
  return paths;
}

/**
 * What a permission request asks for, whole: a profile's shell tool as its command, said to run in
 * the shell that started in the task's folder, else the tool's whole input as JSON, else its title.
 * Complete only when that is everything the call says that acts: the shell's command beside only
 * inputs that do not act, or the whole input; and for a tool that changes files, every change
 * carried whole. A request without its input is never complete, so it can only be denied.
 */
export function approvalRequest(
  toolCall: Record<string, unknown>,
  profile: AcpProfile,
  folder: string,
): { readonly subject: ApprovalSubject; readonly complete: boolean } {
  const name = toolNameOf(toolCall);
  const input = record(toolCall.rawInput);
  const kind = text(toolCall.kind) ?? '';
  const changes = FILE_KINDS.has(kind) ? requestChanges(toolCall, profile, folder) : null;
  const paths = requestPaths(toolCall);
  const shell = profile.shell;
  let summary: string;
  let whole: boolean;
  if (
    shell !== null &&
    input !== null &&
    name === shell.tool &&
    typeof input[shell.command] === 'string'
  ) {
    const others = Object.keys(input).filter((key) => key !== shell.command);
    const background = shell.background !== null && input[shell.background] === true;
    const known = [...shell.quiet, ...(shell.background === null ? [] : [shell.background])];
    whole =
      others.every((key) => known.includes(key)) &&
      (shell.background === null ||
        input[shell.background] === undefined ||
        typeof input[shell.background] === 'boolean');
    summary = `[in its shell, which started in the task's folder${background ? ', in the background' : ''}] ${input[shell.command] as string}`;
  } else if (input !== null) {
    summary = JSON.stringify(input);
    whole = true;
  } else {
    const title = text(toolCall.title);
    summary = title !== null && title.trim() !== '' ? title : name;
    whole = false;
  }
  return {
    subject: {
      kind: 'tool_use',
      tool_name: name.slice(0, 128),
      summary,
      ...(paths !== null && { paths }),
      ...(changes !== null && { changes }),
    },
    complete: whole && name.length <= 128 && (!FILE_KINDS.has(kind) || changes !== null),
  };
}
