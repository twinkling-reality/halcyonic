import assert from 'node:assert/strict';
import { test } from 'node:test';
import type {
  ToolResultResponse,
  ToolResultSummary,
  ToolResultsResponse,
} from '@halcyonic/contracts';
import { boundedResultData, resultBodyData, resultListData } from './tool-results.ts';

const scope = {
  journal_id: '01920000-0000-7000-8000-000000000001' as never,
  project_id: '01920000-0000-7000-8000-000000000002' as never,
  workstream_id: '01920000-0000-7000-8000-000000000003' as never,
  execution_id: '01920000-0000-7000-8000-000000000004' as never,
} as const;
const summary: ToolResultSummary = {
  event_id: '01920000-0000-7000-8000-000000000005' as never,
  position: 20,
  occurred_at: '2026-10-13T00:00:00.000Z',
  runtime_id: 'runtime' as never,
  epistemic: 'observed',
  native_type: null,
  kind: 'command',
  completion: 'completed',
  title: 'check',
  exit_code: 0,
  files_total: null,
  body_available: true,
  retained_complete: true,
} as const;
const response = (text: string): ToolResultResponse => ({
  ...scope,
  summary,
  result: {
    kind: 'command',
    tool_call_id: 'tool',
    turn_id: 'turn',
    completion: 'completed',
    exit_code: 0,
    redaction: { policy: 'known_held_values', replacements: 0 },
    output: { text, availability: 'available', complete: true, truncation: 'none' },
  },
});
test('immutable escaped Unicode parts reconstruct only the retained field within the exact wrapper budget', () => {
  const source = response('"\\\u0001😀'.repeat(180));
  const first = JSON.parse(boundedResultData(resultBodyData(source))) as { parts: number };
  const parts: string[] = [];
  for (let part = 0; part < first.parts; part++) {
    const encoded = boundedResultData(resultBodyData(source, undefined, part));
    assert.ok(Buffer.byteLength(encoded) <= 1024);
    const value = JSON.parse(encoded);
    assert.ok(Buffer.byteLength(JSON.stringify(value.text)) <= 256);
    parts.push(value.text);
    assert.equal(value.excerpt_complete, first.parts === 1);
  }
  assert.equal(parts.join(''), source.result.kind === 'command' ? source.result.output.text : null);
  assert.equal(
    (resultBodyData(source, undefined, first.parts) as { reason: string }).reason,
    'part_out_of_range',
  );
  assert.equal(
    (resultBodyData(source, 0) as { reason: string }).reason,
    'file_selector_inappropriate',
  );
});
test('summary paging covers only retained recent metadata without bodies or JSON prefixes', () => {
  const source: ToolResultsResponse = {
    ...scope,
    availability: 'available',
    total: 40,
    earlier: 24,
    results: Array.from({ length: 16 }, (_, i) => ({
      ...summary,
      position: 100 - i,
      event_id: `01920000-0000-7000-8000-${String(i + 100).padStart(12, '0')}` as never,
    })),
  };
  let before: number | undefined;
  const positions: number[] = [];
  do {
    const text = boundedResultData(resultListData(source, before));
    assert.ok(Buffer.byteLength(text) <= 1024);
    const data = JSON.parse(text);
    positions.push(...data.results.map((item: { position: number }) => item.position));
    before = data.next_before ?? undefined;
  } while (before !== undefined);
  assert.deepEqual(
    positions,
    source.results.map((item) => item.position),
  );
  assert.equal(JSON.stringify(resultListData(source)).includes('output'), false);
  const exhausted = resultListData(source, 1) as {
    coverage: string;
    exhausted: boolean;
    earlier: number;
    results: unknown[];
  };
  assert.equal(exhausted.coverage, 'retained_latest');
  assert.equal(exhausted.exhausted, true);
  assert.equal(exhausted.earlier, 24);
  assert.deepEqual(exhausted.results, []);
});

test('file and part selectors refuse unavailable fields and metadata is omitted as a whole typed response', () => {
  const source = response('body');
  source.summary = { ...source.summary, kind: 'file_changes', exit_code: null, files_total: 1 };
  const common = {
    tool_call_id: 'tool',
    turn_id: 'turn',
    completion: 'completed' as const,
    redaction: { policy: 'known_held_values' as const, replacements: 0 },
  };
  source.result = {
    ...common,
    kind: 'file_changes',
    files_total: 1,
    list_complete: true,
    files: [
      {
        kind: 'update',
        path: 'original',
        path_scope: 'project_relative',
        path_complete: true,
        move_path: 'destination',
        move_path_scope: 'project_relative',
        move_path_complete: true,
        patch: { text: 'body', availability: 'available', complete: true, truncation: 'none' },
      },
    ],
  };
  assert.equal((resultBodyData(source) as { reason: string }).reason, 'file_selector_required');
  assert.equal(
    (resultBodyData(source, 1) as { reason: string }).reason,
    'file_selector_out_of_range',
  );
  const file = source.result.files[0];
  assert.ok(file);
  file.path = '\\'.repeat(1024);
  file.move_path = '"'.repeat(1024);
  file.patch.text = 'x'.repeat(4096);
  const data = JSON.parse(boundedResultData(resultBodyData(source, 0)));
  assert.equal(data.reason, 'result_metadata_bound');
  assert.equal(Object.hasOwn(data, 'excerpt'), false);
  assert.ok(Buffer.byteLength(JSON.stringify(data)) <= 1024);
});
