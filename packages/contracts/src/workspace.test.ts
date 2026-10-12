import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { compileValidator } from './validation.ts';
import { WorkspaceContinuation, WorkspaceViewStore } from './workspace.ts';

const fixtures = JSON.parse(
  readFileSync(new URL('../../../fixtures/workspace/continuation.json', import.meta.url), 'utf8'),
) as {
  cases: { name: string; valid: boolean; value: unknown }[];
  text_limits: { symbol: string; count: number; valid: boolean }[];
};
const validate = compileValidator(WorkspaceContinuation);
const validateStore = compileValidator(WorkspaceViewStore);

test('shared Unicode draft limits count codepoints on both independent editors', () => {
  const sample = fixtures.cases[0]?.value as Record<string, unknown>;
  for (const vector of fixtures.text_limits) {
    for (const field of ['instruction', 'request']) {
      const draft = { text: vector.symbol.repeat(vector.count), spoken: false, part: 0 };
      assert.equal(
        validate({
          ...sample,
          [field]:
            field === 'request' ? { ...draft, request_id: null, pending_command_id: null } : draft,
        }).ok,
        vector.valid,
      );
    }
  }
});

test('local view storage retains strict ordinary continuations without a task-count limit', () => {
  const sample = fixtures.cases[0]?.value as Record<string, unknown>;
  assert.ok(validateStore({ version: 1, selected: null, views: [] }).ok);
  const store = { version: 1, selected: sample.handoff_id, views: [sample] };
  assert.ok(validateStore(store).ok);
  assert.ok(
    validateStore({
      ...store,
      views: Array.from({ length: 32 }, (_, index) => ({
        ...sample,
        handoff_id: `00000000-0000-0000-0000-${index.toString(16).padStart(12, '0')}`,
      })),
      selected: null,
    }).ok,
  );
  for (const invalid of [
    { ...store, version: 2 },
    { ...store, selected: 'not-an-id' },
    { ...store, views: {} },
    { ...store, views: [{ ...sample, approval_id: sample.handoff_id }] },
    { ...store, auto_send: true },
  ])
    assert.equal(validateStore(invalid).ok, false);
});

test('workspace continuation fixtures agree with the authoritative schema', () => {
  for (const sample of fixtures.cases) {
    assert.equal(validate(sample.value).ok, sample.valid, sample.name);
  }
});

test('an instruction draft has the command text bound, without authority fields', () => {
  const sample = fixtures.cases[0]?.value as Record<string, unknown>;
  assert.ok(
    validate({ ...sample, instruction: { text: 'x'.repeat(32000), spoken: true, part: 0 } }).ok,
  );
  assert.equal(
    validate({ ...sample, instruction: { text: 'x'.repeat(32001), spoken: true, part: 0 } }).ok,
    false,
  );
  for (const field of ['read', 'armed', 'auto_send', 'answers', 'credential', 'approval_id']) {
    assert.equal(validate({ ...sample, [field]: true }).ok, false, field);
    assert.equal(
      validate({ ...sample, instruction: { text: 'Draft', spoken: false, part: 0, [field]: true } })
        .ok,
      false,
      field,
    );
  }
});

test('request continuation has its own bounded draft and exact optional host reference without authority', () => {
  const sample = fixtures.cases[0]?.value as Record<string, unknown>;
  const request = {
    request_id: null,
    pending_command_id: null,
    text: 'x'.repeat(32000),
    spoken: true,
    part: 0,
  };
  assert.equal(validate({ ...sample, request }).ok, true);
  assert.equal(validate({ ...sample, request: { ...request, text: 'x'.repeat(32001) } }).ok, false);
  for (const field of ['read', 'armed', 'auto_send', 'answers', 'credential', 'approval_id'])
    assert.equal(validate({ ...sample, request: { ...request, [field]: true } }).ok, false, field);
  // Legacy absence is a local-reader migration, not permission for incomplete current envelopes.
  const legacy = { ...sample };
  delete legacy.request;
  assert.equal(validate(legacy).ok, false);
  const oldSurface = { ...sample };
  delete oldSurface.surface;
  assert.equal(validate(oldSurface).ok, false);
});
