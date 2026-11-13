import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, type TestContext, test } from 'node:test';
import { compileValidator, type ProjectId, SendReviewResponse } from '@halcyonic/contracts';
import { createHostLocations } from '../locations.ts';
import { standInGitHub } from '../testing/github-stand-in.ts';
import { startTestServer } from '../testing/harness.ts';

// Send to GitHub through the control plane (ADR 0039, amended): the review read, then the person's
// command, against a stand-in GitHub only.

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-github-route-')));
after(() => rmSync(base, { recursive: true, force: true }));
const validReview = compileValidator(SendReviewResponse);

async function serve(t: TestContext, name: string, withGitHub = true) {
  const root = join(base, name);
  const stand = standInGitHub(root);
  const server = await startTestServer({
    locations: createHostLocations([stand.projects]),
    ...(withGitHub && {
      github: {
        git: '/usr/bin/git',
        gh: stand.ghPath,
        dataDir: join(root, 'data'),
        env: { HOME: stand.home },
      },
    }),
  });
  t.after(() => server.stop());
  const created = server.controlPlane.commands.submit(
    server.commands.createProject('Garden', {
      kind: 'existing_folder',
      root: stand.projects,
      folder_name: 'garden',
    }),
    'internal',
  );
  const result = created.command?.result;
  const projectId = (result?.kind === 'project_created' ? result.project_id : '') as ProjectId;
  const review = async (asked: object = { title: 'Lanterns' }) => {
    const response = await fetch(`${server.baseUrl}/api/projects/${projectId}/send-review`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}`, 'content-type': 'application/json' },
      body: JSON.stringify(asked),
    });
    const body = (await response.json()) as unknown;
    assert.ok(validReview(body).ok, JSON.stringify(body));
    return body as SendReviewResponse;
  };
  return { server, stand, projectId, review };
}

let next = 0;
function send(projectId: ProjectId, review: string, title: string | null = 'Lanterns') {
  next += 1;
  return {
    schema_version: 1 as const,
    command_id: `01920000-0000-7000-8000-${String(next).padStart(12, '0')}` as never,
    issued_at: '2026-11-11T10:00:00.000Z' as never,
    client: { name: 'test', version: null, device_label: null },
    command_type: 'project.send' as const,
    payload: { project_id: projectId, ...(title === null ? {} : { title }), review },
  };
}

async function settled(server: Awaited<ReturnType<typeof startTestServer>>, id: string) {
  for (let tries = 0; tries < 500; tries += 1) {
    const view = server.controlPlane.projection.command(id);
    if (view !== undefined && view.status !== 'accepted') return view;
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
  assert.fail('the send never settled');
}

describe('Send to GitHub through the control plane (ADR 0039, amended)', {
  skip: process.platform !== 'darwin',
}, () => {
  test("the review is read first, the person's send pushes and opens a pull request, and the journal holds only the facts", async (t) => {
    const { server, stand, projectId, review } = await serve(t, 'sends');
    const nothing = await review();
    assert.equal(nothing.availability === 'unavailable' && nothing.reason.code, 'nothing_to_send');
    writeFileSync(join(stand.folder, 'app.txt'), 'lanterns\n');
    const read = await review();
    assert.ok(read.availability === 'available');
    const accepted = server.controlPlane.commands.submit(
      send(projectId, read.send.review),
      'internal',
    );
    assert.equal(accepted.disposition, 'accepted');
    const done = await settled(server, accepted.command?.command_id ?? '');
    assert.equal(done.status, 'completed', JSON.stringify(done.failure));
    assert.equal(done.result?.kind === 'sent' && done.result.new_branch, read.send.new_branch);
    assert.equal(
      done.result?.kind === 'sent' && done.result.review_request_url,
      'https://github.com/owner/garden/pull/7',
    );
    const recorded = [...server.controlPlane.journal.readAll()]
      .map((stored) => stored.event)
      .find((event) => event.event_type === 'project.sent');
    assert.ok(recorded?.event_type === 'project.sent');
    assert.deepEqual(recorded.payload.remote, { host: 'github.com', path: 'owner/garden' });
    assert.equal(recorded.payload.branch, 'main');
    assert.equal(
      stand.git(stand.origin, 'rev-parse', `refs/heads/${read.send.new_branch}`),
      recorded.payload.commit,
    );
    const journal = JSON.stringify([...server.controlPlane.journal.readAll()]);
    assert.equal(
      journal.includes('example.test'),
      false,
      'nothing git printed or knows of the person',
    );
    assert.equal(journal.includes('file://'), false, 'no address');
    assert.match(readFileSync(stand.ghLog, 'utf8'), /pr create --repo owner\/garden/);
  });

  test('one send of a project at a time: a second press while one is under way is told so, and the first goes (the lead, 20:46Z)', async (t) => {
    const { server, stand, projectId, review } = await serve(t, 'twice');
    writeFileSync(join(stand.folder, 'app.txt'), 'lanterns\n');
    const read = await review();
    assert.ok(read.availability === 'available');
    const first = server.controlPlane.commands.submit(
      send(projectId, read.send.review),
      'internal',
    );
    const second = server.controlPlane.commands.submit(
      send(projectId, read.send.review),
      'internal',
    );
    assert.equal(first.disposition, 'accepted');
    assert.equal(second.disposition, 'rejected', JSON.stringify(second.command?.status));
    assert.deepEqual(second.command?.rejection, {
      code: 'invalid_state',
      message: 'Send for Review in this project is still under way. Send it once it has finished.',
    });
    const done = await settled(server, first.command?.command_id ?? '');
    assert.equal(done.status, 'completed', JSON.stringify(done.failure));
    // Once it has finished, the next press is taken.
    writeFileSync(join(stand.folder, 'app.txt'), 'more lanterns\n');
    const again = await review();
    assert.ok(again.availability === 'available');
    const third = server.controlPlane.commands.submit(
      send(projectId, again.send.review),
      'internal',
    );
    assert.equal(third.disposition, 'accepted');
    assert.equal((await settled(server, third.command?.command_id ?? '')).status, 'completed');
  });

  test("with no title given, the review shows the host's own and the send goes with it", async (t) => {
    const { server, stand, projectId, review } = await serve(t, 'titled');
    writeFileSync(join(stand.folder, 'app.txt'), 'lanterns\n');
    const read = await review({});
    assert.ok(read.availability === 'available');
    assert.equal(read.send.title, 'Garden', "no task yet: the project's name");
    const bad = await review({ title: 'x', extra: true });
    assert.equal(bad.availability === 'unavailable' && bad.reason.code, 'invalid_request');
    const accepted = server.controlPlane.commands.submit(
      send(projectId, read.send.review, null),
      'internal',
    );
    const done = await settled(server, accepted.command?.command_id ?? '');
    assert.equal(done.status, 'completed', JSON.stringify(done.failure));
    const commit = done.result?.kind === 'sent' ? done.result.commit : '';
    assert.equal(stand.git(stand.origin, 'log', '-1', '--format=%s', commit), 'Garden');
  });

  test('a stale review sends nothing, and a computer without the sender says so', async (t) => {
    const { server, stand, projectId, review } = await serve(t, 'stale');
    writeFileSync(join(stand.folder, 'app.txt'), 'lanterns\n');
    const read = await review();
    assert.ok(read.availability === 'available');
    writeFileSync(join(stand.folder, 'app.txt'), 'changed again\n');
    const sent = server.controlPlane.commands.submit(send(projectId, read.send.review), 'internal');
    const done = await settled(server, sent.command?.command_id ?? '');
    assert.equal(done.failure?.code, 'review_stale');
    assert.throws(() => stand.git(stand.origin, 'rev-parse', `refs/heads/${read.send.new_branch}`));
    const off = await serve(t, 'off', false);
    const unavailable = await off.review();
    assert.equal(
      unavailable.availability === 'unavailable' && unavailable.reason.code,
      'unavailable',
    );
  });
});
