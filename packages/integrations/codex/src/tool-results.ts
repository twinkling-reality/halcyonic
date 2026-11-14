import { isAbsolute, relative, resolve } from 'node:path';
import {
  TOOL_RESULT_FILES,
  TOOL_RESULT_SOURCE_BYTES,
  type ToolResult,
  type ToolResultBody,
  type ToolResultFile,
} from '@halcyonic/contracts';

const absent = (
  availability: ToolResultBody['availability'],
  truncation: ToolResultBody['truncation'],
): ToolResultBody => ({ text: null, availability, complete: false, truncation });
function body(value: unknown): ToolResultBody {
  if (value === null || value === undefined) return absent('absent', 'absent');
  if (typeof value !== 'string') return absent('malformed', 'malformed');
  if (Buffer.byteLength(value, 'utf8') > TOOL_RESULT_SOURCE_BYTES)
    return absent('source_over_limit', 'source_limit');
  // Admitted whole native text. The host replaces held values before applying stored body bounds.
  return { text: value, availability: 'available', complete: true, truncation: 'none' };
}
/**
 * A command's output. Codex 0.157.0 leaves out what a command printed before it outlived the startup wait: in neither
 * `aggregatedOutput` nor any output delta, while the model gets it all. Such a command is the one whose `durationMs` is
 * above 0 (0 for every command that ended inside the wait), so its text is kept but never called whole
 * (validation/codex-capabilities.md, "A command's early output", 2026-11-14).
 */
function commandBody(item: Record<string, unknown>): ToolResultBody {
  const output = body(item.aggregatedOutput);
  // No duration on a command that ran says nothing either way; a declined command never ran.
  const outlived =
    typeof item.durationMs === 'number' ? item.durationMs > 0 : item.status !== 'declined';
  return output.text !== null && outlived
    ? { ...output, complete: false, truncation: 'source_cut' }
    : output;
}
function path(
  value: unknown,
  directory: string | null,
): { text: string | null; scope: ToolResultFile['path_scope'] } {
  if (
    typeof value !== 'string' ||
    !directory ||
    Buffer.byteLength(value, 'utf8') > TOOL_RESULT_SOURCE_BYTES ||
    /[\p{Cc}\\]/u.test(value) ||
    /^[A-Za-z]:/.test(value) ||
    value.startsWith('//') ||
    value.split('/').some((part) => part === '..') ||
    !value
  )
    return { text: null, scope: 'unknown' };
  const local = relative(directory, resolve(directory, value));
  if (!local || isAbsolute(local) || local === '..' || local.startsWith('../'))
    return { text: null, scope: 'outside_project' };
  return { text: local, scope: 'project_relative' };
}

/** Final live notification only; no filesystem reads, reconstruction or semantic interpretation. */
export function nativeToolResult(
  item: Record<string, unknown>,
  id: string,
  turn: string,
  directory: string | null,
): ToolResult | null {
  if (Array.from(id).length > 512 || Array.from(turn).length > 512) return null;
  const completion: ToolResult['completion'] = [
    'completed',
    'failed',
    'declined',
    'interrupted',
  ].includes(String(item.status))
    ? (item.status as ToolResult['completion'])
    : 'unknown';
  const common = {
    tool_call_id: id,
    turn_id: turn,
    completion,
    redaction: { policy: 'known_held_values' as const, replacements: 0 },
  };
  if (item.type === 'commandExecution')
    return {
      ...common,
      kind: 'command',
      exit_code:
        Number.isInteger(item.exitCode) &&
        Number(item.exitCode) >= -2147483648 &&
        Number(item.exitCode) <= 2147483647
          ? Number(item.exitCode)
          : null,
      output: commandBody(item),
    };
  if (item.type !== 'fileChange' || !Array.isArray(item.changes)) return null;
  const files: ToolResultFile[] = [];
  for (const raw of item.changes.slice(0, TOOL_RESULT_FILES)) {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) continue;
    const change = raw as Record<string, unknown>,
      kind = change.kind as Record<string, unknown> | null;
    if (!kind || !['add', 'update', 'delete'].includes(String(kind.type))) continue;
    const original = path(change.path, directory);
    const move =
      kind.type === 'update' && kind.move_path != null ? path(kind.move_path, directory) : null;
    files.push({
      kind: kind.type as ToolResultFile['kind'],
      path: original.text,
      path_scope: original.scope,
      path_complete: original.text !== null,
      move_path: move?.text ?? null,
      move_path_scope: move?.scope ?? null,
      move_path_complete: move === null ? null : move.text !== null,
      patch:
        original.scope === 'project_relative' && (!move || move.scope === 'project_relative')
          ? body(change.diff)
          : absent('omitted_scope', 'scope'),
    });
  }
  return {
    ...common,
    kind: 'file_changes',
    files,
    files_total: item.changes.length,
    list_complete: files.length === item.changes.length,
  };
}
