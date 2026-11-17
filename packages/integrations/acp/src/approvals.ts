import { existsSync } from 'node:fs';
import { isAbsolute, resolve } from 'node:path';
import type {
  ApprovalChange,
  ApprovalCommand,
  ApprovalPath,
  ApprovalSubject,
} from '@halcyonic/contracts';
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
  if (profile.fileInputs !== null)
    return changesFromInput(record(toolCall.rawInput), profile.fileInputs, folder);
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

/**
 * The change a file tool's input states whole, for a profile that names its fields (`AcpProfile.fileInputs`): a
 * replacement of one text by another, at every occurrence where its field says so, or a new file's whole text where
 * nothing is at that path yet. Null for any other input, so the request can only be denied.
 */
function changesFromInput(
  input: Record<string, unknown> | null,
  fields: NonNullable<AcpProfile['fileInputs']>,
  folder: string,
): ApprovalChange[] | null {
  if (input === null) return null;
  const base = { real_path: null, to_path: null, cell: null } as const;
  const fits = (value: unknown): value is string =>
    typeof value === 'string' && value.length <= CHANGE_TEXT_MAX;
  const { replace, create } = fields;
  const replacePath = input[replace.path];
  if (
    typeof replacePath === 'string' &&
    replacePath !== '' &&
    fits(input[replace.before]) &&
    fits(input[replace.after]) &&
    typeof input[replace.every] === 'boolean' &&
    Object.keys(input).every((key) =>
      [replace.path, replace.before, replace.after, replace.every].includes(key),
    )
  ) {
    return [
      {
        ...base,
        path: replacePath,
        kind: 'update',
        shape: 'replace',
        before: input[replace.before] as string,
        after: input[replace.after] as string,
        every: input[replace.every] as boolean,
      },
    ];
  }
  const createPath = input[create.path];
  if (
    typeof createPath === 'string' &&
    createPath !== '' &&
    fits(input[create.text]) &&
    Object.keys(input).every((key) => [create.path, create.text].includes(key)) &&
    !existsSync(isAbsolute(createPath) ? createPath : resolve(folder, createPath))
  ) {
    return [
      {
        ...base,
        path: createPath,
        kind: 'add',
        shape: 'content',
        before: null,
        after: input[create.text] as string,
        every: false,
      },
    ];
  }
  return null;
}

/**
 * A turn-level review of staged file changes (`AcpProfile.turnReview`): the files it lists and, for each, the change
 * its call carried when the person was asked for it. Complete only where every listed call's change is carried
 * whole; null where the request is not such a review.
 */
function turnReview(
  review: {
    readonly meta: unknown;
    readonly changesOf: (id: string) => readonly ApprovalChange[] | undefined;
  },
  profile: AcpProfile,
): { readonly subject: ApprovalSubject; readonly complete: boolean } | null {
  const spec = profile.turnReview;
  if (spec === null) return null;
  const meta = record(record(review.meta)?.[spec.meta]);
  if (meta === null || meta.type !== spec.type || !Array.isArray(meta.files)) return null;
  const files = meta.files.map(record);
  const paths: string[] = [];
  const changes: ApprovalChange[] = [];
  let whole = files.length > 0 && files.length <= CHANGES_MAX;
  for (const file of files) {
    const path = text(file?.path);
    const id = text(file?.toolCallId);
    if (path === null || path === '' || id === null) {
      whole = false;
      continue;
    }
    paths.push(path);
    // The change as it was carried when the person was asked for that call, never read again from the folder.
    const carried = review.changesOf(id);
    if (carried === undefined || !carried.every((change) => change.path === path)) whole = false;
    else changes.push(...carried);
  }
  return {
    subject: {
      kind: 'tool_use',
      tool_name: 'review',
      summary: `Review changes: ${paths.length === 0 ? 'no files named' : paths.join(', ')}`,
      ...(paths.length > 0 &&
        paths.length <= PATHS_MAX && {
          paths: paths.map((path) => ({ path, access: 'write' as const })),
        }),
      ...(whole && changes.length > 0 && { changes }),
    },
    complete: whole && changes.length > 0,
  };
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
 * A command request's summary from its parts alone (`ApprovalCommand`), so a card that draws the parts shows all it
 * says: the place only where a run of the app's version showed it, then the command whole.
 */
export function commandSummary(command: ApprovalCommand): string {
  const said = [
    ...(command.runs_in.kind === 'task_folder' ? ["in the task's folder"] : []),
    // Quoted, so no folder's name can read as the task's folder.
    ...(command.runs_in.kind === 'folder' ? [`in ${JSON.stringify(command.runs_in.path)}`] : []),
    ...(command.background ? ['in the background'] : []),
  ];
  return said.length === 0 ? command.text : `[${said.join(', ')}] ${command.text}`;
}

/**
 * What a permission request asks for, whole: a profile's shell tool as its command in its parts (AUDIT3 CP1 to CP5),
 * where its input is that command beside only inputs that do not act; else the tool's whole input as JSON, else its
 * title. Complete only when that is everything the call says that acts, and for a tool that changes files, every
 * change carried whole. A request without its input is never complete, so it can only be denied.
 */
export function approvalRequest(
  toolCall: Record<string, unknown>,
  profile: AcpProfile,
  folder: string,
  review: {
    readonly meta: unknown;
    readonly changesOf: (id: string) => readonly ApprovalChange[] | undefined;
  } | null = null,
): { readonly subject: ApprovalSubject; readonly complete: boolean } {
  const reviewed = review === null ? null : turnReview(review, profile);
  if (reviewed !== null) return reviewed;
  const name = toolNameOf(toolCall);
  const input = record(toolCall.rawInput);
  const kind = text(toolCall.kind) ?? '';
  const changes = FILE_KINDS.has(kind) ? requestChanges(toolCall, profile, folder) : null;
  const paths = requestPaths(toolCall);
  const command = shellCommand(input, name, profile, folder);
  let summary: string;
  let whole: boolean;
  if (command !== null) {
    summary = commandSummary(command);
    whole = true;
  } else if (input !== null) {
    summary = inputSummary(name, input);
    // A shell call whose input is more than its command and the inputs known not to act is shown whole but not
    // approved: what the rest does is not known.
    whole = profile.shell === null || name !== profile.shell.tool;
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
      ...(command !== null && { command }),
    },
    complete: whole && name.length <= 128 && (!FILE_KINDS.has(kind) || changes !== null),
  };
}

/**
 * A request that is no command, in words and then each of its inputs on a line of its own, never as JSON (AUDIT2,
 * 2026-11-17): "Use <tool>", then `name: value`, a text value whole as the app gave it, any other as the app wrote it.
 * A name or text that could pass for more than itself (a line break, a control character, a space at either end) is
 * quoted as JSON writes it, so no input can forge another line the person reads as its own (AUDIT3 IS1).
 */
export function inputSummary(tool: string, input: Record<string, unknown>): string {
  const lines = Object.entries(input).map(
    ([key, value]) =>
      `${plain(key)}: ${typeof value === 'string' ? plain(value) : JSON.stringify(value)}`,
  );
  return [`Use ${plain(tool)}`, ...lines].join('\n');
}

/** A text as it reads on one line: whole where it can only read as itself, else quoted as JSON writes it. */
function plain(text: string): string {
  // biome-ignore lint/suspicious/noControlCharactersInRegex: the control characters are what is looked for.
  return /[\u0000-\u001f\u007f\u2028\u2029]|^\s|\s$/.test(text) ? JSON.stringify(text) : text;
}

/**
 * The command a profile's shell tool would run, from its own structured input alone (AUDIT3 CP1): the command field
 * whole, never cut (CP5), and only where every other input is one that neither acts nor moves it (a folder field of
 * the app's own would, and is not known, so such an input is shown whole instead). Null otherwise.
 */
function shellCommand(
  input: Record<string, unknown> | null,
  name: string,
  profile: AcpProfile,
  folder: string,
): ApprovalCommand | null {
  const shell = profile.shell;
  if (shell === null || input === null || name !== shell.tool) return null;
  const line = input[shell.command];
  if (typeof line !== 'string' || line.trim() === '') return null;
  const known = [
    ...shell.quiet,
    ...(shell.background === null ? [] : [shell.background]),
    ...(shell.cwd === null ? [] : [shell.cwd]),
  ];
  if (!Object.keys(input).every((key) => key === shell.command || known.includes(key))) return null;
  const background = shell.background === null ? undefined : input[shell.background];
  if (background !== undefined && typeof background !== 'boolean') return null;
  // The app's own folder field (AUDIT3 CP3), resolved against the task's folder; a folder Halora cannot name, none.
  const named = shell.cwd === null ? undefined : input[shell.cwd];
  if (named !== undefined && (typeof named !== 'string' || named.trim() === '')) return null;
  return {
    text: line,
    runs_in:
      named === undefined
        ? { kind: shell.runsIn }
        : { kind: 'folder', path: resolve(folder, named) },
    background: background === true,
    input_to_running: false,
  };
}
