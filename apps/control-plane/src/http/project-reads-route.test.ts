import assert from 'node:assert/strict';
import { test } from 'node:test';
import { startTestServer } from '../testing/harness.ts';

test('the project reads refuse a malformed scope or path with 400, and anything not found or not set up with the same 404', async (t) => {
  const server = await startTestServer();
  t.after(() => server.stop());
  const journal = server.journal.info.journal_id;
  const project = '01920000-0000-7000-8000-000000000a01';
  const checkpoint = '01920000-0000-7000-8000-000000000a02';
  const get = (path: string, token: string | null = server.token) =>
    fetch(`${server.baseUrl}${path}`, {
      headers: token === null ? {} : { authorization: `Bearer ${token}` },
    });
  for (const path of [
    `/api/projects/not-an-id/file?journal_id=${journal}&path=a.ts`,
    `/api/projects/${project}/file?journal_id=${journal}&path=../a.ts`,
    `/api/projects/${project}/file?journal_id=${journal}&path=/etc/hosts`,
    `/api/projects/${project}/file?journal_id=${journal}`,
    `/api/projects/${project}/file?journal_id=${journal}&path=a.ts&extra=1`,
    `/api/projects/${project}/changes?journal_id=${journal}`,
    `/api/projects/${project}/changes?journal_id=${journal}&from=${checkpoint}&to=now`,
    `/api/projects/${project}/checks/${checkpoint}/output?journal_id=${journal}&step=page`,
    `/api/projects/${project}/history`,
    `/api/projects/${project}/history?journal_id=${journal}&limit=0`,
    `/api/projects/${project}/history?journal_id=${journal}&limit=51`,
    `/api/projects/${project}/history?journal_id=${journal}&limit=-1`,
    `/api/projects/${project}/history?journal_id=${journal}&before=not-an-id`,
    `/api/projects/${project}/history?journal_id=${journal}&page=2`,
    `/api/projects/not-an-id/history?journal_id=${journal}`,
  ]) {
    assert.equal((await get(path)).status, 400, path);
  }
  for (const path of [
    `/api/projects/${project}/file?journal_id=${journal}&path=a.ts`,
    `/api/projects/${project}/changes?journal_id=${journal}&from=${checkpoint}&to=${checkpoint}`,
    // `to` left out is the folder as it is now: well formed, and nothing is set up here.
    `/api/projects/${project}/changes?journal_id=${journal}&from=${checkpoint}`,
    `/api/projects/${project}/changes/file?journal_id=${journal}&from=${checkpoint}&path=a.ts`,
    `/api/projects/${project}/history?journal_id=${journal}&limit=50&before=${checkpoint}`,
    `/api/projects/${project}/changes/file?journal_id=${journal}&from=${checkpoint}&to=${checkpoint}&path=a.ts`,
    `/api/projects/${project}/checks/${checkpoint}/output?journal_id=${journal}&step=build`,
  ]) {
    const response = await get(path);
    assert.equal(response.status, 404, path);
    assert.equal(
      ((await response.json()) as { error: { code: string } }).error.code,
      'read_unavailable',
    );
  }
  assert.equal(
    (await get(`/api/projects/${project}/file?journal_id=${journal}&path=a.ts`, null)).status,
    401,
  );
});
