import assert from 'node:assert/strict';
import { test } from 'node:test';
import { type CommandEntry, sayable } from './actions.ts';

test('what may be proposed drops never-sayable commands and any entry that needs a secret, whatever the list says', () => {
  const base = {
    kind: 'command' as const,
    title: 'X',
    description: 'x',
    confirm: 'none' as const,
    undo: null,
    sayable: true,
    needs: [],
  };
  const entries: CommandEntry[] = [
    { ...base, command_type: 'workstream.create' },
    { ...base, command_type: 'execution.respond_to_approval' },
    { ...base, command_type: 'execution.answer_question' },
    { ...base, command_type: 'workspace.request.cancel' },
    { ...base, command_type: 'project.check', sayable: false },
    {
      ...base,
      command_type: 'project.create',
      needs: [{ name: 'key', kind: 'secret', label: 'Key', required: true, list: null, max: null }],
    },
  ];
  assert.deepEqual(
    sayable(entries).map((entry) => entry.command_type),
    ['workstream.create'],
  );
});
