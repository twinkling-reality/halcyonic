import assert from 'node:assert/strict';
import { test } from 'node:test';
import { COMMAND_POLICY, COMMAND_SAYING } from './policy.ts';

test('every command says what it does in one plain line of the host’s own', () => {
  assert.deepEqual(Object.keys(COMMAND_SAYING).sort(), Object.keys(COMMAND_POLICY).sort());
  for (const [type, { description }] of Object.entries(COMMAND_SAYING)) {
    assert.ok(description.length > 0 && description.length <= 160, `${type}: one short line`);
    assert.match(description, /^[A-Z].*\.$/, `${type}: a sentence`);
    assert.doesNotMatch(description, /[\n—]|GitHub/, `${type}: one line, no dash, no brand`);
  }
});

test('the assistant may propose every command but a share, an approval or answer, a secret and its own request (R6)', () => {
  const never = Object.entries(COMMAND_SAYING)
    .filter(([, saying]) => !saying.sayable)
    .map(([type]) => type)
    .sort();
  assert.deepEqual(never, [
    'execution.answer_question',
    'execution.respond_to_approval',
    'project.secret.set',
    'project.share.start',
    'workspace.request.cancel',
    'workspace.request.resume',
    'workspace.request.submit',
  ]);
});
