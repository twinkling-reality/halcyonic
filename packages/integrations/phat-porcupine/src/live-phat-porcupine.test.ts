import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import { test } from 'node:test';
import { CatalogResponse, compileValidator } from '@halcyonic/contracts';
import { PhatPorcupineClient } from './client.ts';
import { defaultPhatPorcupineHome } from './connection.ts';

/**
 * The real wire, opt in: set PHAT_PORCUPINE_CREDENTIAL_FILE to a file of mode 0600 holding the
 * consumer credential the person created for Halcyonic with Phat Porcupine's `consumer:create`, while
 * Phat Porcupine's Node server runs on this computer. It reads the catalog once and checks it against
 * Halcyonic's contract. It prints neither the credential nor any project, only counts and codes.
 */
const CREDENTIAL_FILE = process.env.PHAT_PORCUPINE_CREDENTIAL_FILE;

test("the running Phat Porcupine's catalog reads as Halcyonic's contract", {
  skip: CREDENTIAL_FILE === undefined ? 'set PHAT_PORCUPINE_CREDENTIAL_FILE to run' : false,
}, async (t) => {
  const path = CREDENTIAL_FILE as string;
  assert.equal(statSync(path).mode & 0o077, 0, 'the credential file is readable by its owner only');
  const client = new PhatPorcupineClient({
    home: defaultPhatPorcupineHome(),
    credential: () => readFileSync(path, 'utf8').trim(),
  });
  const answer = await client.catalog();
  assert.ok(compileValidator(CatalogResponse)(answer).ok);
  if (answer.availability !== 'available')
    assert.fail(`${answer.availability}: ${answer.reason.code}`);
  t.diagnostic(
    `projects ${answer.projects.length}, groups ${answer.groups.length}, sample ${answer.source.sample}, ` +
      `repositories checked ${answer.sync.repositories_at === null ? 'never' : 'yes'}`,
  );
  const again = await client.catalog();
  assert.equal(again.availability, 'available', 'a second read, with its ETag, answers too');
});
