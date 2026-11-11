import assert from 'node:assert/strict';
import { describe, type TestContext, test } from 'node:test';
import {
  type CheckId,
  compileValidator,
  type PairingOpenedResponse,
  type ProjectId,
  ShareCodeResponse,
  SharingWaysResponse,
} from '@halcyonic/contracts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { shareCode } from '../preview/share-code.ts';
import { pairDevice } from '../testing/device.ts';
import { startTestServer, type TestServerOptions } from '../testing/harness.ts';
import { tlsRequest } from '../testing/tls-client.ts';

// The headset's reads for sharing (ADR 0054): which ways work on this Mac, and a live share's
// address as a code to scan, for the Mac's own client and a paired headset alike.

const validateWays = compileValidator(SharingWaysResponse);
const validateCode = compileValidator(ShareCodeResponse);
const PROJECT = '01a19000-0000-7000-8000-00000000aa21' as ProjectId;
const CHECK = '01a19000-0000-7000-8000-00000000cc21' as CheckId;
const LINK = 'https://bright-quiet-garden.trycloudflare.com';

async function start(t: TestContext, options: TestServerOptions = {}) {
  const server = await startTestServer(options);
  t.after(() => server.stop());
  const scope = { project_id: PROJECT, workstream_id: null, execution_id: null };
  const record = (type: string, payload: unknown) =>
    server.controlPlane.recorder.record(
      controlPlaneDraft(
        type as never,
        scope as never,
        payload as never,
        server.time.now().toISOString() as never,
        NO_CAUSE,
      ),
    );
  const get = (path: string) =>
    fetch(`${server.baseUrl}${path}`, { headers: { authorization: `Bearer ${server.token}` } });
  return { server, record, get };
}

describe("the headset's reads for sharing (ADR 0054)", () => {
  test('which ways work: neither, where this Mac has no previews, and only with the token', async (t) => {
    const { server, get } = await start(t);
    assert.equal((await fetch(`${server.baseUrl}/api/sharing`)).status, 401);
    const response = await get('/api/sharing');
    assert.equal(response.status, 200);
    const ways = validateWays(await response.json());
    assert.ok(ways.ok);
    assert.deepEqual(ways.value, { link: 'not_set_up', wifi: 'not_set_up' });
  });

  test("a live share's code is its address, and no share has none", async (t) => {
    const { record, get } = await start(t);
    record('project.created', { name: 'Garden', location: null });
    const none = await get(`/api/projects/${PROJECT}/share/code`);
    assert.equal(none.status, 404);
    assert.equal(
      ((await none.json()) as { error: { code: string } }).error.code,
      'share_not_found',
    );
    assert.equal((await get('/api/projects/not-an-id/share/code')).status, 400);
    record('project.preview.started', { check_id: CHECK, port: 47931 });
    record('project.share.started', { check_id: CHECK, url: LINK, way: 'link' });
    const live = await get(`/api/projects/${PROJECT}/share/code`);
    assert.equal(live.status, 200);
    const body = validateCode(await live.json());
    assert.ok(body.ok, body.ok ? '' : JSON.stringify(body.issues));
    assert.equal(body.value.check_id, CHECK);
    assert.equal(body.value.way, 'link');
    assert.deepEqual(body.value.code.rows, shareCode(LINK).rows);
    assert.equal(JSON.stringify(body.value).includes('trycloudflare'), false, 'never the address');
    record('project.share.stopped', { check_id: CHECK, reason: 'asked' });
    assert.equal((await get(`/api/projects/${PROJECT}/share/code`)).status, 404);
  });

  test('a paired headset reads both over the network listener', async (t) => {
    const { server, record } = await start(t, { network: {} });
    const network = server.network;
    assert.ok(network);
    record('project.created', { name: 'Garden', location: null });
    record('project.preview.started', { check_id: CHECK, port: 47931 });
    record('project.share.started', {
      check_id: CHECK,
      url: 'http://192.168.1.20:47970',
      way: 'wifi',
    });
    const opened = await fetch(`${server.baseUrl}/api/pairing`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}` },
    });
    const { code } = (await opened.json()) as PairingOpenedResponse;
    const paired = await pairDevice(network.target, code);
    const read = (path: string, headers: Record<string, string>) =>
      tlsRequest(network.target, {
        method: 'GET',
        path,
        headers,
        pin: network.identity.certificateSha256,
      });
    assert.equal((await read('/api/sharing', {})).status, 401);
    const auth = { authorization: `Bearer ${paired.credential}` };
    const ways = await read('/api/sharing', auth);
    assert.equal(ways.status, 200);
    assert.ok(validateWays(JSON.parse(ways.body.toString())).ok);
    const shared = await read(`/api/projects/${PROJECT}/share/code`, auth);
    assert.equal(shared.status, 200);
    const body = validateCode(JSON.parse(shared.body.toString()));
    assert.ok(body.ok);
    assert.equal(body.value.way, 'wifi');
    assert.deepEqual(body.value.code.rows, shareCode('http://192.168.1.20:47970').rows);
  });
});
