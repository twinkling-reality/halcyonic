import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createThreadState, observe } from './events.ts';
import { nativeToolResult } from './tool-results.ts';

test('only native final fields supply exit and completion; a source over 64KiB is omitted whole', () => {
  const result = nativeToolResult(
    { type: 'commandExecution', status: 'failed', exitCode: 2, aggregatedOutput: 'checks passed' },
    'tool',
    'turn',
    '/project',
  );
  assert.equal(result?.completion, 'failed');
  assert.equal(result?.kind === 'command' && result.exit_code, 2);
  const large = nativeToolResult(
    { type: 'commandExecution', status: 'completed', aggregatedOutput: 'x'.repeat(65537) },
    'tool',
    'turn',
    '/project',
  );
  assert.equal(large?.kind === 'command' && large.output.text, null);
  assert.equal(large?.kind === 'command' && large.output.availability, 'source_over_limit');
  assert.equal(
    nativeToolResult({ type: 'commandExecution' }, 'x'.repeat(513), 'turn', '/project'),
    null,
  );
});

test('foreign, malformed, traversal and unknown rename paths omit patch content without filesystem access', () => {
  for (const path of [
    'C:/project/file',
    '//host/share/file',
    '../file',
    '../file',
    'a/../file',
    'file\u0000x',
    'file\nname',
    '/outside/file',
  ]) {
    const result = nativeToolResult(
      {
        type: 'fileChange',
        status: 'completed',
        changes: [{ path, kind: { type: 'update' }, diff: 'private patch' }],
      },
      'tool',
      'turn',
      '/project',
    );
    assert.equal(result?.kind === 'file_changes' && result.files[0]?.patch.text, null, path);
  }
  const result = nativeToolResult(
    {
      type: 'fileChange',
      status: 'declined',
      changes: [
        {
          path: 'src/file',
          kind: { type: 'update', move_path: '/outside/new' },
          diff: 'private patch',
        },
      ],
    },
    'tool',
    'turn',
    '/project',
  );
  assert.equal(result?.completion, 'declined');
  assert.equal(result?.kind === 'file_changes' && result.files[0]?.path, 'src/file');
  assert.equal(
    result?.kind === 'file_changes' && result.files[0]?.move_path_scope,
    'outside_project',
  );
  assert.equal(result?.kind === 'file_changes' && result.files[0]?.patch.text, null);
});

test('oversize identities produce no clipped tool correlation and cannot clear a valid pending tool', () => {
  const state = createThreadState(true, '/project');
  const item = {
    id: 'tool',
    type: 'commandExecution',
    command: 'check',
    status: 'completed',
    exitCode: 0,
    aggregatedOutput: 'done',
  };
  const send = (method: string, turnId: string, value: object = item) =>
    observe(
      state,
      {
        kind: 'notification',
        method,
        params: { threadId: 'thread', turnId, item: value },
        emittedAtMs: null,
      },
      new Date('2026-10-13T00:00:00Z'),
    );
  assert.equal(send('item/started', 'x'.repeat(513)).observations.length, 0);
  assert.equal(
    send('item/started', 'x'.repeat(260), { ...item, id: 'y'.repeat(260) }).observations.length,
    0,
    'Individually bounded identities cannot create an overbound composed native event ID.',
  );
  assert.equal(
    send('item/started', 'turn', { ...item, id: 'x'.repeat(513) }).observations.length,
    0,
  );
  assert.equal(send('item/started', 'turn').observations.length, 1);
  assert.equal(send('item/completed', 'x'.repeat(513)).observations.length, 0);
  assert.equal(state.tools.has('tool'), true);
  const final = send('item/completed', 'turn');
  assert.deepEqual(
    final.observations.map((value) => value.type),
    ['runtime.tool.result', 'runtime.tool.completed'],
  );
  assert.equal(state.tools.size, 0);
  assert.equal(send('item/completed', 'turn').observations.length, 0);
});
