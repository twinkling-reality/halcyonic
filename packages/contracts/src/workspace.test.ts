import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { compileValidator } from './validation.ts';
import { WorkspaceContinuation } from './workspace.ts';

const fixtures = JSON.parse(
  readFileSync(new URL('../../../fixtures/workspace/continuation.json', import.meta.url), 'utf8'),
) as { cases: { name: string; valid: boolean; value: unknown }[] };
const validate = compileValidator(WorkspaceContinuation);

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
