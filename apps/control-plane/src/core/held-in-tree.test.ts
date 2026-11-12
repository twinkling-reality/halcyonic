import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { test } from 'node:test';
import { heldForms, heldIn, heldMessage } from './held-in-tree.ts';

// The forms a held secret is searched for in a tree that would leave the Mac (the lead, 2026-11-12),
// each as heldForms states it. Only made-up values: no real key is ever used.

const VALUE = 'svc+held/value=0123456789"abc\\def';
const held = [{ what: 'MY_SERVICE_KEY', value: VALUE }];
const forms = heldForms(held);
const found = (text: string | Buffer) =>
  heldIn(typeof text === 'string' ? Buffer.from(text) : text, forms);

test('a held value is found exactly, as an env file, quoted, JSON-escaped and URL-encoded', () => {
  assert.equal(found(`SERVICE=${VALUE}\n`), 'MY_SERVICE_KEY');
  assert.equal(found(`const key = '${VALUE}';`), 'MY_SERVICE_KEY');
  assert.equal(
    found(JSON.stringify({ key: VALUE })),
    'MY_SERVICE_KEY',
    'JSON escapes its quote and backslash',
  );
  assert.equal(found(`https://example.test/?k=${encodeURIComponent(VALUE)}`), 'MY_SERVICE_KEY');
  assert.equal(found('nothing of it here'), null);
});

test("a held value's base64 is found alone and inside a longer base64 text, at every alignment, standard and URL-safe", () => {
  for (let before = 0; before <= 5; before += 1)
    for (let after = 0; after <= 3; after += 1) {
      const bytes = Buffer.concat([randomBytes(before), Buffer.from(VALUE), randomBytes(after)]);
      assert.equal(
        found(bytes.toString('base64')),
        'MY_SERVICE_KEY',
        `${before} before, ${after} after`,
      );
      assert.equal(
        found(bytes.toString('base64url')),
        'MY_SERVICE_KEY',
        `URL-safe, ${before} before`,
      );
    }
});

test('a value too short to be told from a word is not searched for, and nothing held finds nothing', () => {
  assert.deepEqual(heldForms([{ what: 'PIN', value: '1234567' }]), []);
  assert.deepEqual(heldForms([]), []);
  assert.equal(
    heldIn(Buffer.from('1234567'), heldForms([{ what: 'PIN', value: ' 1234567 ' }])),
    null,
  );
});

test('the refusal names the file and which secret, never any of its value', () => {
  const message = heldMessage({ path: 'src/config.ts', what: 'MY_SERVICE_KEY' }, 'sent');
  assert.equal(
    message,
    'src/config.ts holds a secret this Mac keeps (MY_SERVICE_KEY), so nothing was sent. Take it out of the file, then try again.',
  );
  assert.ok(!message.includes('held/value'));
});
