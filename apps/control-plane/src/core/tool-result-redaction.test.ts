import assert from 'node:assert/strict';
import { test } from 'node:test';
import { type ToolResult, toolResultProblem } from '@halcyonic/contracts';
import { redaction } from './redaction.ts';
import { normalizedToolResult } from './tool-result-redaction.ts';

const command = (text: string): ToolResult => ({
  kind: 'command',
  tool_call_id: 'tool',
  turn_id: 'turn',
  completion: 'completed',
  exit_code: 0,
  redaction: { policy: 'known_held_values', replacements: 0 },
  output: { text, availability: 'available', complete: true, truncation: 'none' },
});
test('whole admitted field is redacted before its boundary; unknown credentials remain owner work content', () => {
  const held = 'private-held-value';
  const source = command('x'.repeat(4090) + held + ' unknown-key');
  const result = normalizedToolResult(
    source,
    redaction(() => [{ what: 'held', value: held }]),
  );
  assert.equal(result.redaction.replacements, 1);
  assert.equal(JSON.stringify(result).includes('private-held'), false);
  assert.equal(result.kind === 'command' && result.output.complete, false);
  assert.equal(result.kind === 'command' && result.output.truncation, 'body_bound');
  assert.equal(toolResultProblem(result), null);
  const unknown = normalizedToolResult(command('Bearer abc123456-secret-not-held'), redaction());
  assert.equal(
    unknown.kind === 'command' && unknown.output.text,
    'Bearer abc123456-secret-not-held',
  );
  assert.equal(
    source.kind === 'command' && source.output.text?.includes(held),
    true,
    'source field is not mutated',
  );
});
test('Unicode bounds count codepoints and UTF8 bytes, and oversized sources are omitted whole', () => {
  const result = normalizedToolResult(command('😀'.repeat(5000)), redaction());
  assert.equal(result.kind === 'command' && Array.from(result.output.text ?? '').length, 4096);
  assert.equal(toolResultProblem(result), null);
  const omitted = normalizedToolResult(command('😀'.repeat(16385)), redaction());
  assert.equal(omitted.kind === 'command' && omitted.output.availability, 'source_over_limit');
  assert.equal(omitted.kind === 'command' && omitted.output.text, null);
});
test('aggregate escaping is bounded and coverage remains explicit for retained metadata', () => {
  const base = command('');
  const source: ToolResult = {
    ...base,
    kind: 'file_changes',
    files_total: 32,
    list_complete: true,
    files: Array.from({ length: 32 }, (_, i) => ({
      kind: 'add',
      path: `file${i}`,
      path_scope: 'project_relative',
      path_complete: true,
      move_path: null,
      move_path_scope: null,
      move_path_complete: null,
      patch: {
        text: '\u0001'.repeat(4096),
        availability: 'available',
        complete: true,
        truncation: 'none',
      },
    })),
  };
  const result = normalizedToolResult(source, redaction());
  assert.equal(toolResultProblem(result), null);
  assert.ok(Buffer.byteLength(JSON.stringify(result)) <= 65536);
  assert.equal(
    result.kind === 'file_changes' &&
      result.files.some((file) => file.patch.truncation === 'aggregate_bound'),
    true,
  );
  assert.equal(result.kind === 'file_changes' && result.files_total, 32);
});
