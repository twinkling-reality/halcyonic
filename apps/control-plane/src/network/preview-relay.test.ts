import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer, type IncomingHttpHeaders, type Server } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import type { Projection } from '@halcyonic/domain';
import { pairDevice } from '../testing/device.ts';
import { startTestServer } from '../testing/harness.ts';
import { tlsRequest } from '../testing/tls-client.ts';
import { confinedPreviewTarget, type PreviewTarget } from './preview-relay.ts';

const PROJECT = '01920000-0000-7000-8000-000000000001';
const CHECK = '01920000-0000-7000-8000-0000000000c1';

/** The two reads the gate makes of the projection, as a project with this preview and check. */
function projectionWith(
  preview: { check_id: string; port: number } | undefined,
  check:
    | { check_id: string; passed: boolean; reads_confined: boolean; verdict?: string }
    | undefined,
): Projection {
  return {
    preview: () => preview,
    latestCheck: () =>
      check === undefined
        ? undefined
        : {
            check_id: check.check_id,
            findings: {
              passed: check.passed,
              reads_confined: check.reads_confined,
              ...(check.verdict === undefined ? {} : { verdict: check.verdict }),
            },
          },
  } as unknown as Projection;
}

function dataDirWith(t: TestContext, record: unknown): string {
  const dir = mkdtempSync(join(tmpdir(), 'halcyonic-relay-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  if (record !== undefined) {
    mkdirSync(join(dir, 'previews'));
    writeFileSync(join(dir, 'previews', `${PROJECT}.json`), JSON.stringify(record));
  }
  return dir;
}

describe('which preview reaches a paired headset (ADR 0052)', () => {
  test('only a running preview of the latest check, passed, with every agent confined, on its own port', (t) => {
    const good = { check_id: CHECK, port: 47981 };
    const record = { pid: 1, port: 47981, check_id: CHECK };
    const passed = { check_id: CHECK, passed: true, reads_confined: true };
    const dir = dataDirWith(t, record);
    assert.deepEqual(confinedPreviewTarget(projectionWith(good, passed), dir, PROJECT), {
      ok: true,
      port: 47981,
    });
    const code = (target: PreviewTarget) => (target.ok ? 'ok' : target.code);
    assert.equal(
      code(confinedPreviewTarget(projectionWith(undefined, passed), dir, PROJECT)),
      'preview_not_running',
    );
    assert.equal(
      code(
        confinedPreviewTarget(projectionWith(good, { ...passed, check_id: 'other' }), dir, PROJECT),
      ),
      'preview_not_latest',
    );
    assert.equal(
      code(confinedPreviewTarget(projectionWith(good, { ...passed, passed: false }), dir, PROJECT)),
      'preview_not_passed',
    );
    assert.equal(
      code(
        confinedPreviewTarget(
          projectionWith(good, { ...passed, reads_confined: false }),
          dir,
          PROJECT,
        ),
      ),
      'preview_not_confined',
    );
    assert.equal(
      code(
        confinedPreviewTarget(
          projectionWith({ check_id: CHECK, port: 47800 }, passed),
          dir,
          PROJECT,
        ),
      ),
      'preview_not_running',
      'never a port outside the previews',
    );
    assert.equal(
      code(confinedPreviewTarget(projectionWith(good, passed), dataDirWith(t, undefined), PROJECT)),
      'preview_not_running',
      'never without the host’s record of its server',
    );
    assert.equal(
      code(
        confinedPreviewTarget(
          projectionWith(good, passed),
          dataDirWith(t, { ...record, port: 47982 }),
          PROJECT,
        ),
      ),
      'preview_not_running',
    );
  });

  test('a Partly Checked preview reaches the headset as it plays on the laptop, a failed one never (CARBON A3-19)', (t) => {
    const good = { check_id: CHECK, port: 47981 };
    const dir = dataDirWith(t, { pid: 1, port: 47981, check_id: CHECK });
    const partly = { check_id: CHECK, passed: false, verdict: 'not_known', reads_confined: true };
    assert.deepEqual(confinedPreviewTarget(projectionWith(good, partly), dir, PROJECT), {
      ok: true,
      port: 47981,
    });
    const failed = confinedPreviewTarget(
      projectionWith(good, { ...partly, verdict: 'failed' }),
      dir,
      PROJECT,
    );
    assert.equal(failed.ok ? 'ok' : failed.code, 'preview_not_passed');
    const unconfined = confinedPreviewTarget(
      projectionWith(good, { ...partly, reads_confined: false }),
      dir,
      PROJECT,
    );
    assert.equal(unconfined.ok ? 'ok' : unconfined.code, 'preview_not_confined');
  });

  test("a task's preview reaches the headset only by the host's own record of it, of that task's latest check, passed and confined (limits audit #5 and #6)", (t) => {
    const TASK = '01920000-0000-7000-8000-0000000000a1';
    const OTHER = '01920000-0000-7000-8000-0000000000a2';
    const ELSEWHERE = '01920000-0000-7000-8000-0000000000a3';
    const TASK_CHECK = '01920000-0000-7000-8000-0000000000c2';
    const passed = { check_id: TASK_CHECK, passed: true, reads_confined: true };
    const taskProjection = (
      check: { check_id: string; passed: boolean; reads_confined: boolean } = passed,
      previews: Record<string, { check_id: string; port: number }> = {
        [TASK]: { check_id: TASK_CHECK, port: 47982 },
        [OTHER]: { check_id: TASK_CHECK, port: 47983 },
        [ELSEWHERE]: { check_id: TASK_CHECK, port: 47984 },
      },
    ) =>
      ({
        ...projectionWith(
          { check_id: CHECK, port: 47981 },
          { check_id: CHECK, passed: true, reads_confined: true },
        ),
        workstream: (id: string) =>
          [TASK, OTHER, ELSEWHERE].includes(id)
            ? { workstream_id: id, project_id: id === ELSEWHERE ? 'another' : PROJECT }
            : undefined,
        taskPreview: (id: string) => previews[id],
        latestTaskCheck: (id: string) =>
          previews[id] === undefined
            ? undefined
            : {
                check_id: check.check_id,
                findings: { passed: check.passed, reads_confined: check.reads_confined },
              },
      }) as unknown as Projection;
    const dir = dataDirWith(t, { pid: 1, port: 47981, check_id: CHECK });
    const task = (record: unknown) =>
      writeFileSync(join(dir, 'previews', `${PROJECT}.task.${TASK}.json`), JSON.stringify(record));
    task({ pid: 2, port: 47982, check_id: TASK_CHECK });
    for (const id of [ELSEWHERE])
      writeFileSync(
        join(dir, 'previews', `${PROJECT}.task.${id}.json`),
        JSON.stringify({ pid: 3, port: 47984, check_id: TASK_CHECK }),
      );
    const code = (target: PreviewTarget) => (target.ok ? `ok ${target.port}` : target.code);
    const gate = (projection: Projection, id: string | null) =>
      code(confinedPreviewTarget(projection, dir, PROJECT, undefined, id));
    assert.equal(gate(taskProjection(), TASK), 'ok 47982', "the task's own");
    assert.equal(gate(taskProjection(), null), 'ok 47981', 'without a task, the project preview');
    assert.equal(
      gate(taskProjection(), OTHER),
      'preview_not_running',
      'never without the host’s record of that task’s server',
    );
    assert.equal(
      gate(taskProjection(), ELSEWHERE),
      'preview_not_running',
      "another project's task",
    );
    assert.equal(
      gate(taskProjection(), '01920000-0000-7000-8000-0000000000ff'),
      'preview_not_running',
    );
    assert.equal(
      gate(taskProjection({ ...passed, check_id: 'newer' }), TASK),
      'preview_not_latest',
    );
    assert.equal(gate(taskProjection({ ...passed, passed: false }), TASK), 'preview_not_passed');
    assert.equal(
      gate(taskProjection({ ...passed, reads_confined: false }), TASK),
      'preview_not_confined',
    );
    task({ pid: 2, port: 47981, check_id: TASK_CHECK });
    assert.equal(
      gate(taskProjection(), TASK),
      'preview_not_running',
      'a record naming another port',
    );
    task({ pid: 2, port: 47982, check_id: CHECK });
    assert.equal(gate(taskProjection(), TASK), 'preview_not_running', 'a record of another check');
  });
});

describe('the preview relay on the network listener (ADR 0052)', () => {
  test('gives a paired device the preview’s files, and nothing the preview sets in a browser', async (t) => {
    // A stand-in preview server: it answers with what a built copy's server might, and more.
    const seen: { url: string; headers: IncomingHttpHeaders }[] = [];
    const preview: Server = createServer((request, response) => {
      seen.push({ url: request.url ?? '', headers: request.headers });
      response.writeHead(200, {
        'content-type': 'text/html',
        'content-length': String(Buffer.byteLength('<h1>Star garden</h1>')),
        'set-cookie': 'tracking=1',
        'access-control-allow-origin': '*',
        'cache-control': 'max-age=3600',
      });
      response.end(request.method === 'HEAD' ? undefined : '<h1>Star garden</h1>');
    });
    await new Promise<void>((resolve) => preview.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise((resolve) => preview.close(resolve)));
    const port = (preview.address() as { port: number }).port;
    let allowed: PreviewTarget = { ok: true, port };
    const server = await startTestServer({ network: { previewTarget: () => allowed } });
    t.after(() => server.stop());
    assert.ok(server.network);
    const opened = await fetch(`${server.baseUrl}/api/pairing`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}` },
    });
    const { code } = (await opened.json()) as { code: string };
    const paired = await pairDevice(server.network.target, code);
    const target = server.network.target;
    const pin = server.network.identity.certificateSha256;
    const get = (path: string, headers: Record<string, string> = {}, method = 'GET') =>
      tlsRequest(target, {
        method,
        path,
        headers: { authorization: `Bearer ${paired.credential}`, ...headers },
        pin,
      });

    const page = await get(`/api/projects/${PROJECT}/preview/assets/app.js?v=2`, {
      accept: 'text/html',
      cookie: 'x=1',
    });
    assert.equal(page.status, 200);
    assert.equal(page.body, '<h1>Star garden</h1>');
    assert.equal(page.headers.get('content-type'), 'text/html');
    assert.equal(
      page.headers.get('cache-control'),
      'no-store',
      "the headset's browser keeps nothing",
    );
    assert.equal(page.headers.get('set-cookie'), undefined);
    assert.equal(page.headers.get('access-control-allow-origin'), undefined);
    const asked = seen.at(-1);
    assert.equal(asked?.url, '/assets/app.js?v=2');
    assert.equal(asked?.headers.host, `127.0.0.1:${port}`);
    assert.equal(
      asked?.headers.authorization,
      undefined,
      'the credential never reaches the preview',
    );
    assert.equal(asked?.headers.cookie, undefined);
    assert.equal(asked?.headers.accept, 'text/html');

    assert.equal((await get(`/api/projects/${PROJECT}/preview`)).status, 200, 'the preview’s root');
    assert.equal((await get(`/api/projects/${PROJECT}/preview/`, {}, 'HEAD')).status, 200);
    assert.equal(
      (await get(`/api/projects/${PROJECT}/preview/sw.js`, { 'service-worker': 'script' })).status,
      403,
      'never a service worker script',
    );
    assert.equal(
      (await tlsRequest(target, { method: 'GET', path: `/api/projects/${PROJECT}/preview/`, pin }))
        .status,
      401,
      'a paired device only',
    );
    assert.equal(
      (
        await fetch(`${server.baseUrl}/api/projects/${PROJECT}/preview/`, {
          headers: { authorization: `Bearer ${server.token}` },
        })
      ).status,
      404,
      'not on loopback',
    );
    allowed = { ok: false, code: 'preview_not_confined', message: 'not confined' };
    const refused = await get(`/api/projects/${PROJECT}/preview/`);
    assert.equal(refused.status, 409);
    assert.equal(JSON.parse(refused.body).error.code, 'preview_not_confined');
    allowed = { ok: true, port: 1 };
    assert.equal(
      (await get(`/api/projects/${PROJECT}/preview/`)).status,
      502,
      'a preview not answering',
    );
  });

  test("relays a task's preview by its own path, asking the gate for that task, never one the page's query names (limits audit #5 and #6)", async (t) => {
    const TASK = '01920000-0000-7000-8000-0000000000a1';
    const seen: string[] = [];
    const preview: Server = createServer((request, response) => {
      seen.push(request.url ?? '');
      response.end('variant');
    });
    await new Promise<void>((resolve) => preview.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise((resolve) => preview.close(resolve)));
    const port = (preview.address() as { port: number }).port;
    const asked: [string, string | null][] = [];
    const server = await startTestServer({
      network: {
        previewTarget: (projectId, taskId = null) => {
          asked.push([projectId, taskId]);
          return taskId === TASK
            ? { ok: true, port }
            : { ok: false, code: 'preview_not_running', message: 'none' };
        },
      },
    });
    t.after(() => server.stop());
    assert.ok(server.network);
    const opened = await fetch(`${server.baseUrl}/api/pairing`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}` },
    });
    const { code } = (await opened.json()) as { code: string };
    const paired = await pairDevice(server.network.target, code);
    const target = server.network.target;
    const pin = server.network.identity.certificateSha256;
    const get = (path: string) =>
      tlsRequest(target, {
        method: 'GET',
        path,
        headers: { authorization: `Bearer ${paired.credential}` },
        pin,
      });
    const page = await get(`/api/projects/${PROJECT}/tasks/${TASK}/preview/assets/app.js?task=x`);
    assert.equal(page.status, 200);
    assert.equal(page.body, 'variant');
    assert.equal(seen.at(-1), '/assets/app.js?task=x', "the page's own query passes as it is");
    assert.deepEqual(asked.at(-1), [PROJECT, TASK]);
    assert.equal((await get(`/api/projects/${PROJECT}/tasks/${TASK}/preview`)).status, 200);
    assert.equal(seen.at(-1), '/');
    // Without a task, the project's own preview, as before; a query never picks a task.
    assert.equal((await get(`/api/projects/${PROJECT}/preview/?task=${TASK}`)).status, 409);
    assert.deepEqual(asked.at(-1), [PROJECT, null]);
    assert.equal((await get(`/api/projects/${PROJECT}/tasks/not-a-task/preview/`)).status, 400);
    assert.deepEqual(asked.at(-1), [PROJECT, null], 'a bad task id never reaches the gate');
  });
});
