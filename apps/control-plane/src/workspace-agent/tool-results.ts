import type { ToolResultResponse, ToolResultsResponse } from '@halcyonic/contracts';

const bytes = (value: unknown) => Buffer.byteLength(JSON.stringify(value), 'utf8');
const omitted = (reason: string) => ({ availability: 'unavailable', complete: false, reason });
/** Whole typed wrappers only; a JSON prefix is neither valid data nor useful evidence. */
export function boundedResultData(value: unknown): string {
  return JSON.stringify(bytes(value) <= 1024 ? value : omitted('result_metadata_bound'));
}
function chunks(text: string): string[] {
  const parts: string[] = [];
  let part = '';
  for (const character of text) {
    if (bytes(part + character) > 256) {
      parts.push(part);
      part = '';
    }
    part += character;
  }
  parts.push(part);
  return parts;
}
export function resultListData(source: ToolResultsResponse, before?: number): unknown {
  // A cursor pages only the retained latest sixteen, never implies coverage of earlier history.
  const remaining = source.results.filter(
    (result) => before === undefined || result.position < before,
  );
  const shown: unknown[] = [];
  const wrapper = () => ({
    availability: source.availability,
    total: source.total,
    recent: source.results.length,
    earlier: source.earlier,
    coverage: 'retained_latest',
    exhausted: remaining.length === 0,
    results: shown,
    omitted: remaining.length - shown.length,
    next_before:
      shown.length < remaining.length ? (remaining[shown.length - 1]?.position ?? null) : null,
  });
  for (const item of remaining) {
    const entry = {
      event_id: item.event_id,
      position: item.position,
      kind: item.kind,
      completion: item.completion,
      exit_code: item.exit_code,
      files_total: item.files_total,
      body_available: item.body_available,
      retained_complete: item.retained_complete,
    };
    shown.push(entry);
    if (bytes(wrapper()) > 1024) {
      shown.pop();
      break;
    }
  }
  return bytes(wrapper()) <= 1024 ? wrapper() : omitted('result_metadata_bound');
}
export function resultBodyData(source: ToolResultResponse, fileIndex?: number, part = 0): unknown {
  const result = source.result;
  if (result.kind === 'command' && fileIndex !== undefined)
    return omitted('file_selector_inappropriate');
  if (result.kind === 'file_changes' && fileIndex === undefined)
    return omitted('file_selector_required');
  const file = result.kind === 'file_changes' ? result.files[fileIndex as number] : undefined;
  if (result.kind === 'file_changes' && !file) return omitted('file_selector_out_of_range');
  const body = result.kind === 'command' ? result.output : file?.patch;
  if (!body) return omitted('result_field_unavailable');
  const parts = body.text === null ? [] : chunks(body.text);
  if ((part !== 0 && body.text === null) || (body.text !== null && part >= parts.length))
    return omitted('part_out_of_range');
  const pathParts = file?.path === null || file?.path === undefined ? [] : chunks(file.path);
  const moveParts =
    file?.move_path === null || file?.move_path === undefined ? [] : chunks(file.move_path);
  const value = {
    event_id: source.summary.event_id,
    position: source.summary.position,
    kind: result.kind,
    completion: result.completion,
    exit_code: result.kind === 'command' ? result.exit_code : null,
    file_index: fileIndex ?? null,
    files_total: result.kind === 'file_changes' ? result.files_total : null,
    list_complete: result.kind === 'file_changes' ? result.list_complete : null,
    path_excerpt: pathParts[0] ?? null,
    path_excerpt_complete: file === undefined ? null : file.path_complete && pathParts.length <= 1,
    path_scope: file?.path_scope ?? null,
    move_path_excerpt: moveParts[0] ?? null,
    move_path_excerpt_complete:
      file === undefined
        ? null
        : file.move_path_complete === null
          ? null
          : file.move_path_complete && moveParts.length <= 1,
    move_path_scope: file?.move_path_scope ?? null,
    availability: body.availability,
    stored_complete: body.complete,
    truncation: body.truncation,
    redaction: result.redaction,
    part,
    parts: parts.length,
    text: parts[part] ?? null,
    excerpt_complete: body.text !== null && parts.length === 1,
  };
  return bytes(value) <= 1024 ? value : omitted('result_metadata_bound');
}
