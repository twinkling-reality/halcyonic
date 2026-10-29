import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { compileValidator, StartersResponse } from '@halcyonic/contracts';
import { HostStarters } from '../starters.ts';
import { startTestServer } from '../testing/harness.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-starters-route-')));
after(() => rmSync(base, { recursive: true, force: true }));
const valid = compileValidator(StartersResponse);

describe('listing starters over HTTP', () => {
  test('none without starters, and each tracked one with whether it is installed', async () => {
    const plain = await startTestServer();
    const none = await fetch(`${plain.baseUrl}/api/starters`, {
      headers: { authorization: `Bearer ${plain.token}` },
    });
    assert.deepEqual(await none.json(), { starters: [] });
    await plain.stop();

    const tracked = join(base, 'tracked');
    const installed = join(base, 'installed');
    mkdirSync(join(tracked, 'vr', 'template'), { recursive: true });
    writeFileSync(
      join(tracked, 'vr', 'starter.json'),
      JSON.stringify({ format: 1, title: 'A VR app' }),
    );
    writeFileSync(join(tracked, 'vr', 'package-lock.json'), '{}\n');
    mkdirSync(join(installed, 'vr', 'node_modules'), { recursive: true });
    writeFileSync(
      join(installed, 'vr', 'installed.json'),
      JSON.stringify({ format: 1, lockfile: createHash('sha256').update('{}\n').digest('hex') }),
    );
    const server = await startTestServer({
      starters: new HostStarters({ trackedDir: tracked, installedDir: installed }),
    });
    const listed = await fetch(`${server.baseUrl}/api/starters`, {
      headers: { authorization: `Bearer ${server.token}` },
    });
    const body = await listed.json();
    assert.ok(valid(body).ok);
    assert.deepEqual(body, {
      starters: [{ starter_id: 'vr', title: 'A VR app', installed: true }],
    });
    assert.equal((await fetch(`${server.baseUrl}/api/starters`)).status, 401);
    await server.stop();
  });
});
