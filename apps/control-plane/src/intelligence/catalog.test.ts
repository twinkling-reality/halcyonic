import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { chmodSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, afterEach, before, describe, test } from 'node:test';
import { FakePhatPorcupine } from '@halcyonic/integration-phat-porcupine/testing';
import { PHAT_PORCUPINE_CREDENTIAL_FILE, PhatPorcupineCatalog } from './catalog.ts';

describe("the control plane's catalog from Phat Porcupine (ADR 0039)", () => {
  let fake: FakePhatPorcupine;
  let dataDir: string;
  let credentialPath: string;
  let catalog: PhatPorcupineCatalog;
  before(async () => {
    fake = await FakePhatPorcupine.start();
    dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-catalog-data-'));
    credentialPath = join(dataDir, PHAT_PORCUPINE_CREDENTIAL_FILE);
    catalog = new PhatPorcupineCatalog({
      home: fake.home,
      credentialPath,
      protect: [dataDir],
      timeoutMs: 2000,
    });
  });
  afterEach(() => {
    rmSync(credentialPath, { force: true });
    fake.requests.length = 0;
    fake.sample = false;
    fake.publish();
  });
  after(async () => {
    await fake.close();
    rmSync(dataDir, { recursive: true, force: true });
  });

  function save(token: string, mode: number): void {
    writeFileSync(credentialPath, `${token}\n`, { mode });
    chmodSync(credentialPath, mode);
  }

  test('without a credential file, it says how to set one up and asks Phat Porcupine nothing', async () => {
    const answer = await catalog.read();
    assert.equal(
      answer.availability === 'unauthorized' && answer.reason.code,
      'credential_missing',
    );
    assert.deepEqual(fake.requests, []);
  });

  test('a credential file another user could read is never used', async () => {
    for (const mode of [0o640, 0o604, 0o644]) {
      save(fake.token, mode);
      const answer = await catalog.read();
      assert.equal(
        answer.availability === 'unauthorized' && answer.reason.code,
        'credential_file_exposed',
        mode.toString(8),
      );
    }
    assert.deepEqual(fake.requests, []);
  });

  test('a link, a pipe or an oversized file is never read as the credential, and nothing waits on it', async () => {
    const real = join(dataDir, 'elsewhere');
    writeFileSync(real, `${fake.token}\n`, { mode: 0o600 });
    symlinkSync(real, credentialPath);
    let answer = await catalog.read();
    assert.equal(
      answer.availability === 'unauthorized' && answer.reason.code,
      'credential_not_a_file',
    );
    rmSync(credentialPath);
    rmSync(real);
    execFileSync('/usr/bin/mkfifo', ['-m', '600', credentialPath]);
    const started = Date.now();
    answer = await catalog.read();
    assert.equal(
      answer.availability === 'unauthorized' && answer.reason.code,
      'credential_not_a_file',
    );
    assert.ok(Date.now() - started < 1000, 'a pipe is never waited on');
    rmSync(credentialPath);
    save(`${fake.token}${'x'.repeat(2000)}`, 0o600);
    answer = await catalog.read();
    assert.equal(
      answer.availability === 'unauthorized' && answer.reason.code,
      'credential_unreadable',
    );
    assert.deepEqual(fake.requests, []);
  });

  test('with its own file, it reads the catalog, and the credential appears in no answer', async () => {
    save(fake.token, 0o600);
    const answer = await catalog.read();
    assert.equal(answer.availability, 'available');
    assert.ok(!JSON.stringify(answer).includes(fake.token));
    const missing = await new PhatPorcupineCatalog({
      home: `${fake.home}-gone`,
      credentialPath,
      protect: [dataDir],
      timeoutMs: 2000,
    }).read();
    assert.ok(!JSON.stringify(missing).includes(fake.token));
  });

  test('a link is admitted only for a repository the last catalog held, under the same name', async () => {
    const fresh = new PhatPorcupineCatalog({
      home: fake.home,
      credentialPath,
      protect: [dataDir],
      timeoutMs: 2000,
    });
    const ref = {
      source: 'phat-porcupine' as const,
      repository_id: 8100001,
      full_name: 'lantern-lab/wind-chimes',
    };
    assert.equal(fresh.check(ref).ok, false, 'nothing read yet');
    save(fake.token, 0o600);
    await fresh.read();
    assert.equal(fresh.check(ref).ok, true);
    assert.equal(fresh.check({ ...ref, repository_id: 9 }).ok, false);
    assert.equal(fresh.check({ ...ref, full_name: 'lantern-lab/other' }).ok, false);
    // A failed read keeps the last catalog: the person was shown it.
    rmSync(credentialPath);
    await fresh.read();
    assert.equal(fresh.check(ref).ok, true);
  });

  test("a sample catalog is never linked to the person's projects", async () => {
    fake.sample = true;
    fake.publish();
    save(fake.token, 0o600);
    const sampled = new PhatPorcupineCatalog({
      home: fake.home,
      credentialPath,
      protect: [dataDir],
      timeoutMs: 2000,
    });
    const answer = await sampled.read();
    assert.equal(answer.availability === 'available' && answer.source.sample, true);
    const check = sampled.check({
      source: 'phat-porcupine',
      repository_id: 8100001,
      full_name: 'lantern-lab/wind-chimes',
    });
    assert.equal(check.ok, false);
  });
});
