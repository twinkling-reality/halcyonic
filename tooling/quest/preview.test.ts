import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { controlPlaneDraft, NO_CAUSE } from '../../apps/control-plane/src/core/drafts.ts';
import {
  createTestControlPlane,
  startTestServer,
} from '../../apps/control-plane/src/testing/harness.ts';
import {
  type CheckFindings,
  PREVIEW_PORT_FIRST,
  PREVIEW_PORT_LAST,
  type ProjectId,
  type StoredEvent,
} from '../../packages/contracts/src/index.ts';
import { confinedPreview, journalEvents } from './preview.ts';

const PROJECT = '01a19000-0000-7000-8000-000000000001' as ProjectId;
const CHECK = '01a19000-0000-7000-8000-0000000000c1';
const OTHER_CHECK = '01a19000-0000-7000-8000-0000000000c2';
const COPY = '<!doctype html><title>the checked copy</title>';

const FINDINGS: CheckFindings = {
  tsc: { exit: 0, seconds: 1 },
  build: { exit: 0, seconds: 9 },
  page: null,
  unknown: null,
  passed: true,
  reads_confined: true,
};

interface Story {
  readonly findings?: Partial<CheckFindings>;
  /** The check the preview serves; CHECK by default. */
  readonly served?: string;
  /** A later check recorded after the preview started, with no stop between. */
  readonly laterCheck?: string;
  readonly port: number;
  readonly stopped?: boolean;
  readonly noPreview?: boolean;
}

type Recorder = ReturnType<typeof createTestControlPlane>['controlPlane']['recorder'];

/** Records a starter project's check and preview as the host would. */
function tell(recorder: Recorder, story: Story): void {
  const scope = { project_id: PROJECT, workstream_id: null, execution_id: null };
  const now = '2026-10-30T16:00:00.000Z' as never;
  const record = (type: string, payload: unknown) =>
    recorder.record(
      controlPlaneDraft(type as never, scope as never, payload as never, now, NO_CAUSE),
    );
  record('project.created', {
    name: 'Garden',
    location: { path: '/Users/someone/dev/garden', name: 'garden', created: true },
  });
  record('project.check.recorded', {
    check_id: CHECK,
    findings: { ...FINDINGS, ...story.findings },
  });
  if (story.noPreview) return;
  const served = story.served ?? CHECK;
  record('project.preview.started', { check_id: served, port: story.port });
  if (story.laterCheck !== undefined)
    record('project.check.recorded', { check_id: story.laterCheck, findings: FINDINGS });
  if (story.stopped) record('project.preview.stopped', { check_id: served, reason: 'asked' });
}

/** The journal the control plane keeps for `story`. */
function journal(story: Story): StoredEvent[] {
  const { controlPlane, journal } = createTestControlPlane();
  tell(controlPlane.recorder, story);
  return journal.read({ after: 0, limit: 1000 });
}

/** A server on the first free port of the preview range, in this process, answering `body`. */
async function listen(
  t: TestContext,
  body = COPY,
  { servesWorkers = false }: { servesWorkers?: boolean } = {},
): Promise<number> {
  for (let port = PREVIEW_PORT_FIRST; port <= PREVIEW_PORT_LAST; port += 1) {
    const server: Server = createServer((request, response) => {
      // As the host's preview server is to: no service worker script, so none can register.
      if (!servesWorkers && request.headers['service-worker'] === 'script') {
        response.statusCode = 403;
        response.end();
        return;
      }
      response.end(body);
    });
    const bound = await new Promise<boolean>((resolve) => {
      server.once('error', () => resolve(false));
      server.listen(port, '127.0.0.1', () => resolve(true));
    });
    if (bound) {
      t.after(() => new Promise<void>((resolve) => server.close(() => resolve())));
      return port;
    }
  }
  throw new Error('every preview port is in use on this Mac');
}

/** A port of the preview range that nothing holds: bound and closed again. */
async function freePort(): Promise<number> {
  for (let port = PREVIEW_PORT_FIRST; port <= PREVIEW_PORT_LAST; port += 1) {
    const server = createServer();
    const bound = await new Promise<boolean>((resolve) => {
      server.once('error', () => resolve(false));
      server.listen(port, '127.0.0.1', () => resolve(true));
    });
    if (bound) {
      await new Promise<void>((resolve) => server.close(() => resolve()));
      return port;
    }
  }
  throw new Error('every preview port is in use on this Mac');
}

interface Folder {
  readonly pid?: number;
  readonly recordCheck?: string;
  readonly noRecord?: boolean;
  readonly copy?: string;
}

/** A data directory holding the host's preview record and the checked copy. */
function dataDir(t: TestContext, port: number, folder: Folder = {}): string {
  const dir = mkdtempSync(join(tmpdir(), 'halcyonic-quest-preview-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const dist = join(dir, 'checks', PROJECT, CHECK, 'dist');
  mkdirSync(dist, { recursive: true });
  writeFileSync(join(dist, 'index.html'), folder.copy ?? COPY);
  if (!folder.noRecord) {
    mkdirSync(join(dir, 'previews'), { recursive: true });
    writeFileSync(
      join(dir, 'previews', `${PROJECT}.json`),
      JSON.stringify({
        pid: folder.pid ?? process.pid,
        port,
        check_id: folder.recordCheck ?? CHECK,
      }),
    );
  }
  return dir;
}

async function refused(gate: Promise<Awaited<ReturnType<typeof confinedPreview>>>, why: RegExp) {
  const said = await gate;
  assert.equal(said.ok, false, JSON.stringify(said));
  if (!said.ok) assert.match(said.reason, why);
}

describe('opening a preview on the headset: only a checked copy whose agent reads were confined', () => {
  test('a passed, confined check served by the host opens at 127.0.0.1, never localhost', async (t) => {
    const port = await listen(t);
    const gate = await confinedPreview(PROJECT, journal({ port }), dataDir(t, port));
    assert.deepEqual(gate, {
      ok: true,
      port,
      checkId: CHECK,
      url: `http://127.0.0.1:${port}/`,
    });
  });

  test('a project with no preview running is refused', async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(PROJECT, journal({ port, noPreview: true }), dataDir(t, port)),
      /no preview runs/,
    );
  });

  test('a preview the journal shows stopped is refused', async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(PROJECT, journal({ port, stopped: true }), dataDir(t, port)),
      /no preview runs/,
    );
  });

  test('a preview of another check than the latest is refused', async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(PROJECT, journal({ port, laterCheck: OTHER_CHECK }), dataDir(t, port)),
      /not the latest check/,
    );
  });

  test('a check that did not pass is refused', async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(PROJECT, journal({ port, findings: { passed: false } }), dataDir(t, port)),
      /did not pass/,
    );
  });

  test("a check whose agent's reads were not confined is refused", async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(
        PROJECT,
        journal({ port, findings: { reads_confined: false } }),
        dataDir(t, port),
      ),
      /reads were not confined/,
    );
  });

  test('a preview port outside the range is refused', async (t) => {
    const port = PREVIEW_PORT_LAST + 1;
    await refused(confinedPreview(PROJECT, journal({ port }), dataDir(t, port)), /outside/);
  });

  test('a project id that is not one is refused before any file is read', async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview('../previews', journal({ port }), dataDir(t, port)),
      /project id/,
    );
  });

  test('a port nothing listens on is refused', async (t) => {
    const port = await freePort();
    await refused(confinedPreview(PROJECT, journal({ port }), dataDir(t, port)), /nothing listens/);
  });

  test("a port another process listens on than the one the host's record names is refused", async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(PROJECT, journal({ port }), dataDir(t, port, { pid: 1 })),
      /not the host's preview server/,
    );
  });

  test('a host record of another check is refused', async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(PROJECT, journal({ port }), dataDir(t, port, { recordCheck: OTHER_CHECK })),
      /record/,
    );
  });

  test('no host record of the preview is refused', async (t) => {
    const port = await listen(t);
    await refused(
      confinedPreview(PROJECT, journal({ port }), dataDir(t, port, { noRecord: true })),
      /record/,
    );
  });

  test('a preview server that would serve a service worker script is refused', async (t) => {
    const port = await listen(t, COPY, { servesWorkers: true });
    await refused(confinedPreview(PROJECT, journal({ port }), dataDir(t, port)), /service worker/);
  });

  test('a page that is not the checked copy is refused', async (t) => {
    const port = await listen(t, '<title>something else</title>');
    await refused(
      confinedPreview(PROJECT, journal({ port }), dataDir(t, port)),
      /does not serve the checked copy/,
    );
  });
});

describe("reading the control plane's journal", () => {
  test('every page is read, each request from a server that proved it holds the token', async (t) => {
    const server = await startTestServer();
    t.after(() => server.stop());
    tell(server.controlPlane.recorder, { port: PREVIEW_PORT_FIRST });
    const expected = server.journal.read({ after: 0, limit: 1000 });
    assert.ok(expected.length >= 3);
    const asked: string[] = [];
    const watching: typeof fetch = (input, init) => {
      asked.push(String(input));
      return fetch(input, init);
    };
    const events = await journalEvents(watching, server.baseUrl, server.token, 2);
    assert.deepEqual(
      events.map((stored) => stored.position),
      expected.map((stored) => stored.position),
    );
    // A proof before each page.
    const pages = asked.filter((url) => url.includes('/api/events'));
    const proofs = asked.filter((url) => url.endsWith('/api/health'));
    assert.ok(pages.length >= 2, asked.join('\n'));
    assert.ok(proofs.length >= pages.length, asked.join('\n'));
  });

  test("the token goes to no server that can't prove it holds it", async (t) => {
    const seen: (string | undefined)[] = [];
    const impostor = createServer((request, response) => {
      seen.push(request.headers.authorization);
      response.end('{}');
    });
    await new Promise<void>((resolve) => impostor.listen(0, '127.0.0.1', () => resolve()));
    t.after(() => new Promise<void>((resolve) => impostor.close(() => resolve())));
    const address = impostor.address();
    assert.ok(address !== null && typeof address === 'object');
    await assert.rejects(
      journalEvents(fetch, `http://127.0.0.1:${address.port}`, 'a'.repeat(43)),
      /token was not sent/,
    );
    assert.ok(seen.length >= 1);
    assert.ok(
      seen.every((value) => value === undefined),
      'no request carried the token',
    );
  });
});
