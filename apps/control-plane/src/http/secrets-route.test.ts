import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, type TestContext, test } from 'node:test';
import type { PairingOpenedResponse, ProjectId } from '@halcyonic/contracts';
import { pairDevice } from '../testing/device.ts';
import { startTestServer } from '../testing/harness.ts';
import { tlsRequest } from '../testing/tls-client.ts';

// The write-only way to set a project secret (ADR 0057): `POST /api/projects/:id/secrets`, from this
// Mac or a paired device. Test values only.

const base = mkdtempSync(join(tmpdir(), 'halcyonic-secret-route-'));
after(() => rmSync(base, { recursive: true, force: true }));
let made = 0;
const VALUE = 'test-value-not-real-0008';
const OTHER = 'test-value-not-real-0009';

async function serve(t: TestContext, options: { secrets?: boolean; network?: boolean } = {}) {
  made += 1;
  const dataDir = join(base, `data-${made}`);
  const server = await startTestServer({
    ...(options.secrets === false ? {} : { projectSecrets: { dataDir } }),
    ...(options.network === true ? { network: {} } : {}),
  });
  t.after(() => server.stop());
  const created = server.controlPlane.commands.submit(
    server.commands.createProject('Garden'),
    'internal',
  );
  const result = created.command?.result;
  const projectId = (result?.kind === 'project_created' ? result.project_id : '') as ProjectId;
  let ids = 0;
  const request = (overrides: Record<string, unknown> = {}) => {
    ids += 1;
    return {
      command_id: `01a19000-0000-7000-8000-${String(ids).padStart(12, '0')}`,
      issued_at: '2026-11-11T20:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      name: 'PROJECT_TEST_KEY',
      value: VALUE,
      ...overrides,
    };
  };
  const post = (body: unknown, token = server.token) =>
    fetch(`${server.baseUrl}/api/projects/${projectId}/secrets`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
      body: JSON.stringify(body),
    });
  const journal = () => JSON.stringify([...server.controlPlane.journal.readAll()]);
  return { server, dataDir, projectId, request, post, journal };
}

describe('setting a project secret by its own request (ADR 0057)', () => {
  test('from this Mac: the value goes to the store, the command carries the name, and no answer holds the value', async (t) => {
    const { server, dataDir, projectId, request, post, journal } = await serve(t);
    const answer = await post(request());
    assert.equal(answer.status, 202);
    const text = await answer.text();
    assert.equal(text.includes(VALUE), false, 'the answer holds no value');
    const body = JSON.parse(text) as { command: { status: string; principal?: unknown } };
    assert.equal(body.command.status, 'completed');
    assert.equal(
      readFileSync(join(dataDir, 'secrets', projectId, 'PROJECT_TEST_KEY'), 'utf8'),
      VALUE,
    );
    assert.deepEqual(server.controlPlane.projection.project(projectId)?.secrets, [
      'PROJECT_TEST_KEY',
    ]);
    assert.equal(journal().includes(VALUE), false, 'the journal holds no value');
    // A retried id is the same command: the first value stays.
    const first = request();
    assert.equal((await post(first)).status, 202);
    const retried = await post({ ...first, value: OTHER });
    assert.equal(retried.status, 200);
    assert.equal(((await retried.json()) as { disposition: string }).disposition, 'duplicate');
    assert.equal(
      readFileSync(join(dataDir, 'secrets', projectId, 'PROJECT_TEST_KEY'), 'utf8'),
      VALUE,
    );
    assert.equal(journal().includes(OTHER), false);
  });

  test('refusals in words, never echoing the value, and nothing kept', async (t) => {
    const { server, projectId, request, post } = await serve(t);
    const short = await post(request({ value: 'short' }));
    assert.equal(short.status, 400);
    assert.equal((await short.text()).includes('short"'), false, 'the value is not echoed');
    const reserved = await post(request({ name: 'PATH' }));
    assert.equal(reserved.status, 422);
    assert.match(await reserved.text(), /PATH is a name the Mac or an agent uses/);
    const lower = await post(request({ name: 'lower_case' }));
    assert.equal(lower.status, 400);
    assert.equal((await post(request(), 'not-the-token')).status, 401);
    const unknown = await fetch(
      `${server.baseUrl}/api/projects/01a19000-0000-7000-8000-0000000000aa/secrets`,
      {
        method: 'POST',
        headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
        body: JSON.stringify(request()),
      },
    );
    assert.equal(unknown.status, 422);
    assert.equal(server.controlPlane.projection.project(projectId)?.secrets, undefined);
    assert.deepEqual(server.controlPlane.projectSecrets?.held(), []);
  });

  test('without a secret store this computer says so', async (t) => {
    const { request, post } = await serve(t, { secrets: false });
    const answer = await post(request());
    assert.equal(answer.status, 503);
  });

  test('from a paired headset over the network listener, recorded as that device', async (t) => {
    const { server, dataDir, projectId, request, journal } = await serve(t, { network: true });
    const network = server.network;
    assert.ok(network);
    const opened = await fetch(`${server.baseUrl}/api/pairing`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}` },
    });
    const { code } = (await opened.json()) as PairingOpenedResponse;
    const paired = await pairDevice(network.target, code);
    const send = (headers: Record<string, string>) =>
      tlsRequest(network.target, {
        method: 'POST',
        path: `/api/projects/${projectId}/secrets`,
        headers: { 'content-type': 'application/json', ...headers },
        body: Buffer.from(JSON.stringify(request())),
        pin: network.identity.certificateSha256,
      });
    assert.equal((await send({})).status, 401);
    const answer = await send({ authorization: `Bearer ${paired.credential}` });
    assert.equal(answer.status, 202, answer.body);
    assert.equal(answer.body.includes(VALUE), false);
    assert.equal(
      readFileSync(join(dataDir, 'secrets', projectId, 'PROJECT_TEST_KEY'), 'utf8'),
      VALUE,
    );
    const accepted = [...server.controlPlane.journal.readAll()].find(
      (each) =>
        each.event.event_type === 'command.accepted' &&
        (each.event.payload as { command: { command_type: string } }).command.command_type ===
          'project.secret.set',
    );
    const payload = accepted?.event.payload as { principal?: unknown } | undefined;
    assert.deepEqual(payload?.principal, {
      kind: 'device',
      device_id: paired.deviceId,
    });
    assert.equal(journal().includes(VALUE), false);
  });
});
