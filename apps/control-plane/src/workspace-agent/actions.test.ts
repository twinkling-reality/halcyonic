import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { CommandId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { type ActionEntry, buildProposed, type ListRow, listDigest, sayable } from './actions.ts';

const project = '01a1ce13-0000-7000-8000-0000000000aa' as ProjectId;
const task = '01a1ce13-0000-7000-8000-0000000000bb' as WorkstreamId;
const scope = { project_id: project, workstream_id: null, execution_id: null };
const envelope = {
  command_id: '01a1ce13-0000-7000-8000-0000000000cc' as CommandId,
  issued_at: '2026-11-12T05:00:00.000Z',
};
const version = (n: number): ListRow => ({
  kind: 'version',
  label: `Version ${n}`,
  detail: null,
  theirs: false,
  value: `01a1ce13-0000-7000-8000-00000000000${n}`,
});

test('a built command takes its ids from the binding and the rows read, and refuses one the model gives', () => {
  const restore: ActionEntry = {
    command_type: 'project.restore',
    title: 'Restore',
    description: 'Put the files back.',
    confirm: 'review',
    sayable: true,
    needs: [
      { name: 'checkpoint_id', kind: 'choice', label: 'Version', required: true, list: 'versions' },
    ],
  };
  const rows = () => [version(1), version(2)];
  const built = buildProposed(restore, scope, { checkpoint_id: 1 }, rows, envelope);
  assert.ok(built.ok);
  assert.deepEqual(built.command.payload, {
    project_id: project,
    checkpoint_id: version(2).value,
  });
  assert.deepEqual(buildProposed(restore, scope, { checkpoint_id: 2 }, rows, envelope), {
    ok: false,
    reason: 'no_such_choice',
    field: 'checkpoint_id',
  });
  assert.deepEqual(
    buildProposed(restore, scope, { checkpoint_id: 0, project_id: task }, rows, envelope),
    { ok: false, reason: 'not_needed', field: 'project_id' },
  );
});

test("an instruction is the person's own words, filled by the host, never a text of the model (AUDIT2 N4)", () => {
  const start: ActionEntry = {
    command_type: 'workstream.create',
    title: 'New Task',
    description: 'Start a task.',
    confirm: 'none',
    sayable: true,
    needs: [
      { name: 'title', kind: 'text', label: 'Title', required: true },
      { name: 'objective', kind: 'person_words', label: 'What to do', required: true },
    ],
  };
  const words = 'Make it night, with stars over the pond.';
  const built = buildProposed(start, scope, { title: 'Night' }, () => [], {
    ...envelope,
    person_words: words,
  });
  assert.ok(built.ok);
  assert.equal((built.command.payload as { objective: string }).objective, words);
  assert.deepEqual(
    buildProposed(start, scope, { title: 'Night', objective: 'my version' }, () => [], {
      ...envelope,
      person_words: words,
    }),
    { ok: false, reason: 'the_persons_words', field: 'objective' },
  );
});

test('several choices are indices, at most as many as the need allows, never the same twice', () => {
  const entry: ActionEntry = {
    command_type: 'project.restore',
    title: 'Restore',
    description: 'x',
    confirm: 'review',
    sayable: true,
    needs: [
      {
        name: 'checkpoint_id',
        kind: 'choices',
        label: 'Versions',
        required: true,
        list: 'versions',
        most: 1,
      },
    ],
  };
  const rows = () => [version(1), version(2)];
  assert.equal(buildProposed(entry, scope, { checkpoint_id: [0, 1] }, rows, envelope).ok, false);
  assert.equal(buildProposed(entry, scope, { checkpoint_id: [0, 0] }, rows, envelope).ok, false);
  assert.equal(buildProposed(entry, scope, { checkpoint_id: 0 }, rows, envelope).ok, false);
});

test('what may be proposed drops never-sayable commands and any entry that needs a secret, whatever the list says', () => {
  const base = { title: 'X', description: 'x', confirm: 'none' as const, sayable: true, needs: [] };
  const entries: ActionEntry[] = [
    { ...base, command_type: 'workstream.create' },
    { ...base, command_type: 'execution.respond_to_approval' },
    { ...base, command_type: 'execution.answer_question' },
    { ...base, command_type: 'workspace.request.cancel' },
    { ...base, command_type: 'project.check', sayable: false },
    {
      ...base,
      command_type: 'project.create',
      needs: [{ name: 'key', kind: 'secret', label: 'Key', required: true }],
    },
  ];
  assert.deepEqual(
    sayable(entries).map((entry) => entry.command_type),
    ['workstream.create'],
  );
});

test("a list's digest changes when a row changes, so a proposal never names a row that moved", () => {
  assert.equal(listDigest([version(1), version(2)]), listDigest([version(1), version(2)]));
  assert.notEqual(listDigest([version(1), version(2)]), listDigest([version(2), version(1)]));
});
