import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { createServer, request } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, test } from 'node:test';
import type { CheckId, EventEnvelope, ProjectId } from '@halcyonic/contracts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import { ProjectShares, pinnedLookup } from './share.ts';

// Sharing a checked game at a public address (F1), with a stand-in tunnel: a process that prints
// an address as the tunnel does and stays up until it is stopped.

const PROJECT = '01a19000-0000-7000-8000-00000000aa01' as ProjectId;
const CHECK = '01a19000-0000-7000-8000-00000000cc01' as CheckId;
const ADDRESS = 'https://bright-quiet-garden.trycloudflare.com';
const scope = { project_id: PROJECT, workstream_id: null, execution_id: null };
const now = '2026-11-11T10:00:00.000Z' as never;
const homes: string[] = [];

afterEach(() => {
  for (const home of homes.splice(0)) rmSync(home, { recursive: true, force: true });
});

function standInTunnel(prints: boolean) {
  return () =>
    spawn(
      process.execPath,
      [
        '-e',
        prints
          ? `process.stderr.write('INF |  ${ADDRESS}  |\\n'); setInterval(() => {}, 1000);`
          : 'setInterval(() => {}, 1000);',
      ],
      { stdio: ['ignore', 'ignore', 'pipe'] },
    );
}

function setup(options: { prints?: boolean; answers?: boolean } = {}) {
  const harness = createTestControlPlane({ logger: capturingLogger().logger });
  const { controlPlane } = harness;
  const record = <T extends Parameters<typeof controlPlaneDraft>[0]>(
    type: T,
    payload: Extract<EventEnvelope, { event_type: T }>['payload'],
  ) =>
    controlPlane.recorder.record(
      controlPlaneDraft(type, scope as never, payload as never, now, NO_CAUSE),
    );
  record('project.created', { name: 'Garden', location: null });
  record('project.preview.started', { check_id: CHECK, port: 47931 });
  const home = mkdtempSync(join(tmpdir(), 'halcyonic-share-'));
  homes.push(home);
  const shares = new ProjectShares({
    recorder: controlPlane.recorder,
    projection: controlPlane.projection,
    clock: harness.time,
    logger: capturingLogger().logger,
    tunnel: '/unused',
    home,
    spawnTunnel: standInTunnel(options.prints ?? true),
    answers: async () => options.answers ?? true,
    startTimeoutMs: 2000,
  });
  shares.attach(controlPlane.publisher);
  const types = () =>
    [...controlPlane.journal.readAll()]
      .map((stored) => stored.event.event_type)
      .filter((type) => type.startsWith('project.share.'));
  return { controlPlane, shares, record, types };
}

describe('sharing a checked game at an address anyone can open (F1)', () => {
  test('recorded only once the address answers; stopped when asked, and the address goes from the view', async (t) => {
    const { controlPlane, shares, types } = setup();
    t.after(() => shares.close());
    const shared = await shares.start(PROJECT, NO_CAUSE);
    assert.deepEqual(shared, { check_id: CHECK, url: ADDRESS });
    assert.deepEqual(controlPlane.projection.project(PROJECT)?.share, {
      check_id: CHECK,
      url: ADDRESS,
      files: 'unknown',
    });
    await assert.rejects(shares.start(PROJECT, NO_CAUSE), /already shared/);
    assert.equal(await shares.stop(PROJECT, 'asked', NO_CAUSE), true);
    assert.deepEqual(types(), ['project.share.started', 'project.share.stopped']);
    assert.equal(controlPlane.projection.project(PROJECT)?.share, undefined);
  });

  test('a preview that stops takes its shared link with it', async (t) => {
    const { controlPlane, shares, record } = setup();
    t.after(() => shares.close());
    await shares.start(PROJECT, NO_CAUSE);
    record('project.preview.stopped', { check_id: CHECK, reason: 'new_check' });
    for (let i = 0; i < 100 && controlPlane.projection.share(PROJECT) !== undefined; i += 1)
      await new Promise((resolve) => setTimeout(resolve, 10));
    assert.equal(controlPlane.projection.share(PROJECT), undefined);
    const stopped = [...controlPlane.journal.readAll()].find(
      (stored) => stored.event.event_type === 'project.share.stopped',
    )?.event;
    assert.equal(
      stopped?.event_type === 'project.share.stopped' && stopped.payload.reason,
      'preview_stopped',
    );
  });

  test('nothing is recorded where no address comes or it does not answer with the game', async () => {
    const silent = setup({ prints: false });
    await assert.rejects(silent.shares.start(PROJECT, NO_CAUSE), /no address/);
    assert.deepEqual(silent.types(), []);
    const unanswered = setup({ answers: false });
    await assert.rejects(unanswered.shares.start(PROJECT, NO_CAUSE), /did not answer/);
    assert.deepEqual(unanswered.types(), []);
    assert.equal(unanswered.controlPlane.projection.share(PROJECT), undefined);
  });

  test('a share a restart left recorded as live is recorded stopped: no tunnel survives one', () => {
    const { controlPlane, record, types } = setup();
    record('project.share.started', { check_id: CHECK, url: ADDRESS });
    const again = new ProjectShares({
      recorder: controlPlane.recorder,
      projection: controlPlane.projection,
      clock: { now: () => new Date() },
      logger: capturingLogger().logger,
      tunnel: '/unused',
      home: '/unused',
    });
    again.attach(controlPlane.publisher);
    assert.deepEqual(types(), ['project.share.started', 'project.share.stopped']);
    assert.equal(controlPlane.projection.share(PROJECT), undefined);
  });

  test("the address is reached where Cloudflare's resolver says, in either form Node asks for", async (t) => {
    const server = createServer((_req, res) => res.end('ok'));
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    t.after(() => server.close());
    const port = (server.address() as { port: number }).port;
    const status = await new Promise<number | string>((resolve) => {
      const req = request(
        `http://share.invalid:${port}/`,
        { lookup: pinnedLookup('127.0.0.1') },
        (res) => {
          res.resume();
          resolve(res.statusCode ?? 0);
        },
      );
      req.on('error', (error) => resolve(error.message));
      req.end();
    });
    assert.equal(status, 200);
    const single = await new Promise<string>((resolve) =>
      pinnedLookup('127.0.0.1')('share.invalid', {}, (_error, address) => resolve(String(address))),
    );
    assert.equal(single, '127.0.0.1');
  });
});
