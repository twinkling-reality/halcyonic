import {
  TOOL_RESULT_BODY_CHARACTERS,
  TOOL_RESULT_BYTES,
  TOOL_RESULT_SOURCE_BYTES,
  type ToolResult,
  type ToolResultBody,
} from '@halcyonic/contracts';
import { fitted, type Redaction } from './redaction.ts';

/** Adapter observations are transient source data; only this held-redacted/bounded value is recorded. */
export function normalizedToolResult(source: ToolResult, redaction: Redaction): ToolResult {
  const result = structuredClone(source);
  let replacements = 0;
  const text = (raw: string, limit: number) => {
    if (typeof raw !== 'string') throw new Error('Malformed native result text.');
    const clean = redaction.heldDetailed(raw);
    replacements += clean.replacements;
    return { value: fitted(clean.text, limit), complete: Array.from(clean.text).length <= limit };
  };
  const cleanBody = (body: ToolResultBody): ToolResultBody => {
    if (body.text === null) return body;
    if (typeof body.text !== 'string') throw new Error('Malformed native result body.');
    if (Buffer.byteLength(body.text, 'utf8') > TOOL_RESULT_SOURCE_BYTES)
      return {
        text: null,
        availability: 'source_over_limit',
        complete: false,
        truncation: 'source_limit',
      };
    const clean = text(body.text, TOOL_RESULT_BODY_CHARACTERS);
    return {
      ...body,
      text: clean.value,
      complete: body.complete && clean.complete,
      truncation: clean.complete ? body.truncation : 'body_bound',
    };
  };
  if (result.kind === 'command') result.output = cleanBody(result.output);
  else
    for (const file of result.files) {
      if (file.path !== null && Buffer.byteLength(file.path, 'utf8') > TOOL_RESULT_SOURCE_BYTES) {
        file.path = null;
        file.path_scope = 'unknown';
        file.path_complete = false;
      }
      if (
        file.move_path !== null &&
        Buffer.byteLength(file.move_path, 'utf8') > TOOL_RESULT_SOURCE_BYTES
      ) {
        file.move_path = null;
        file.move_path_scope = 'unknown';
        file.move_path_complete = false;
      }
      if (file.path !== null) {
        const clean = text(file.path, 1024);
        file.path = clean.value;
        file.path_complete &&= clean.complete;
      }
      if (file.move_path !== null) {
        const clean = text(file.move_path, 1024);
        file.move_path = clean.value;
        file.move_path_complete = file.move_path_complete === true && clean.complete;
      }
      file.patch =
        file.path_scope === 'project_relative' &&
        (file.move_path_scope === null || file.move_path_scope === 'project_relative')
          ? cleanBody(file.patch)
          : { text: null, availability: 'omitted_scope', complete: false, truncation: 'scope' };
    }
  result.redaction = { policy: 'known_held_values', replacements };
  const size = () => Buffer.byteLength(JSON.stringify(result), 'utf8');
  const bodies =
    result.kind === 'command' ? [result.output] : result.files.map((file) => file.patch).reverse();
  for (const body of bodies) {
    if (size() <= TOOL_RESULT_BYTES) break;
    if (body.text !== null)
      Object.assign(body, {
        text: null,
        availability: 'absent',
        complete: false,
        truncation: 'aggregate_bound',
      });
  }
  if (result.kind === 'file_changes')
    while (size() > TOOL_RESULT_BYTES && result.files.length) {
      result.files.pop();
      result.list_complete = false;
    }
  if (size() > TOOL_RESULT_BYTES) throw new Error('Native result metadata exceeds its bound.');
  return result;
}
