import assert from 'node:assert/strict';
import { type ChildProcess, spawn } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PassThrough } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import {
  type CommandEnvelope,
  type CommandId,
  PREVIEW_PORT_FIRST,
  type ProjectId,
} from '@halcyonic/contracts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import type { CheckOptions, CheckResult } from './check.ts';
import { type ChromeRun, DevToolsPipe } from './chrome.ts';
import type { HostPreviewsDeps, ServeArgs } from './preview.ts';

const PASSED: CheckResult = {
  tsc: { exit: 0, seconds: 1 },
  build: { exit: 0, seconds: 9 },
  page: {
    exceptions: [],
    exception_count: 0,
    console_errors: 0,
    canvases: 1,
    edges: { at_3s: 0, at_10s: 7451 },
    drawn: true,
    pictures: [{ at_ms: 10_000, file: '/nowhere/picture-10s.png', sha256: 'c'.repeat(64) }],
  },
  unknown: null,
  passed: true,
  laptop: {
    layer: 'starter',
    targets: 1,
    mouse: 'reached',
    key: 'reached',
    screen_changed: null,
    response_seen: true,
    exceptions_during: 0,
    ready: true,
  },
  vr: { offered: 'offered', how: 'offer_session', exceptions: 0, ready: true },
  notes: { scanned: 'full', items: [{ kind: 'window_listener', file: 'src/game.ts', line: 12 }] },
  ready: true,
};

async function until(check: () => boolean, what: string): Promise<void> {
  const deadline = Date.now() + 10_000;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

/** A stand-in for `vite preview`: serves the copy's index.html on 127.0.0.1 at the port it is given. */
const SERVER = `const [dist, port] = process.argv.slice(1);
require('node:http').createServer((_, res) => res.end(require('node:fs').readFileSync(dist + '/index.html')))
  .listen(Number(port), '127.0.0.1');`;

const serving = (args: ServeArgs) =>
  spawn(process.execPath, ['-e', SERVER, args.dist, args.env.HALCYONIC_PREVIEW_PORT ?? ''], {
    stdio: 'ignore',
  });

const alive = (children: ChildProcess[]) =>
  children.filter((child) => child.exitCode === null && child.signalCode === null).length;

interface PlaneOptions {
  readonly result?: CheckResult;
  /** Stands in for the server; by default one that serves the copy. */
  readonly serve?: (args: ServeArgs) => ChildProcess;
  /** Holds every check after the first until it resolves. */
  readonly hold?: Promise<void>;
  /** Play on my laptop: a stand-in browser launch, or none set up. */
  readonly play?: HostPreviewsDeps['play'];
}

/** A control plane with checks on, a starter project in it, and the check and server standing in. */
function plane(
  t: TestContext,
  { result = PASSED, serve = serving, hold, play }: PlaneOptions = {},
) {
  const dataDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-previews-')));
  t.after(() => rmSync(dataDir, { recursive: true, force: true }));
  const checked: CheckOptions[] = [];
  const served: ServeArgs[] = [];
  const children: ChildProcess[] = [];
  const { controlPlane, journal } = createTestControlPlane({
    previews: {
      dataDir,
      home: '/Users/someone',
      node: process.execPath,
      chrome: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
      starters: { trackedDir: '/repo/starters', installedDir: join(dataDir, 'starters') },
      confinesCodex: true,
      ...(play === undefined ? {} : { play }),
      runCheck: async (options) => {
        checked.push(options);
        if (checked.length > 1 && hold !== undefined) await hold;
        // As the real check does: the built copy in the check's own cache.
        mkdirSync(join(options.cache, 'dist'), { recursive: true });
        writeFileSync(join(options.cache, 'dist', 'index.html'), `<title>${options.cache}</title>`);
        return result;
      },
      serve: (args) => {
        served.push(args);
        const child = serve(args);
        children.push(child);
        return child;
      },
    },
  });
  t.after(async () => {
    await controlPlane.close();
    for (const child of children) child.kill('SIGKILL');
  });
  const projectId = '01a19000-0000-7000-8000-000000000001' as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const now = '2026-10-29T10:00:00.000Z' as never;
  controlPlane.recorder.record(
    controlPlaneDraft(
      'project.created',
      scope,
      {
        name: 'Garden',
        location: { path: '/Users/someone/dev/garden', name: 'garden', created: true },
      },
      now,
      NO_CAUSE,
    ),
  );
  controlPlane.recorder.record(
    controlPlaneDraft(
      'project.starter_applied',
      scope,
      {
        starter_id: 'iwsdk-vr',
        lockfile: 'a'.repeat(64),
        files: 12,
        template: 'b'.repeat(64),
      } as never,
      now,
      NO_CAUSE,
    ),
  );
  let n = 0;
  const submit = (
    type:
      | 'project.check'
      | 'project.preview.start'
      | 'project.preview.stop'
      | 'project.open_on_mac',
  ) => {
    n += 1;
    const command = {
      schema_version: 1,
      command_id: `00000000-0000-4000-9000-${String(n).padStart(12, '0')}` as CommandId,
      issued_at: '2026-10-29T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: type,
      payload: { project_id: projectId },
    } as CommandEnvelope;
    const outcome = controlPlane.commands.submit(command, 'internal');
    return { command, outcome };
  };
  const done = async (commandId: CommandId) => {
    await until(() => {
      const status = controlPlane.projection.command(commandId)?.status;
      return status === 'completed' || status === 'failed';
    }, 'the command to end');
    return controlPlane.projection.command(commandId);
  };
  const types = () =>
    journal.read({ after: 0, limit: 1000 }).map((stored): string => stored.event.event_type);
  return {
    controlPlane,
    journal,
    dataDir,
    projectId,
    submit,
    done,
    types,
    checked,
    served,
    children,
  };
}

describe('a checked game played on the Mac (Play on my laptop, P-A)', () => {
  /** A stand-in for the contained browser: answers every command; counts its starts and stops. */
  const standIn = () => {
    const runs = { started: 0, stopped: 0 };
    const launch = async (): Promise<ChromeRun> => {
      runs.started += 1;
      const toChrome = new PassThrough();
      const fromChrome = new PassThrough();
      toChrome.setEncoding('utf8');
      toChrome.on('data', (chunk: string) => {
        for (const text of chunk.split('\0').filter((part) => part !== ''))
          fromChrome.write(
            `${JSON.stringify({ id: (JSON.parse(text) as { id: number }).id, result: {} })}\0`,
          );
      });
      return {
        pid: process.pid,
        devtools: new DevToolsPipe(toChrome, fromChrome),
        session: 'page',
        stop: async () => {
          runs.stopped += 1;
        },
      };
    };
    return {
      runs,
      play: {
        browser: { app: '/fake/Google Chrome for Testing.app', sha256: 'a'.repeat(64) },
        browserProblem: () => null,
        launch,
        viewerWaitMs: 60_000,
      },
    };
  };

  test('it plays from the running preview, and stops with the preview, on a newer check, and with the host, each saying why', async (t) => {
    const { runs, play } = standIn();
    const { controlPlane, submit, done, types, journal } = plane(t, { play });
    const reasons = () =>
      journal
        .read({ after: 0, limit: 1000 })
        .flatMap((stored) =>
          stored.event.event_type === 'project.mac_play.stopped'
            ? [stored.event.payload.reason]
            : [],
        );
    const run = async (type: Parameters<typeof submit>[0]) => {
      const { command } = submit(type);
      const finished = await done(command.command_id);
      assert.equal(finished?.status, 'completed', `${type}: ${JSON.stringify(finished)}`);
    };
    await run('project.check');
    await run('project.preview.start');
    await run('project.open_on_mac');
    assert.equal(runs.started, 1);
    await run('project.preview.stop');
    assert.equal(runs.stopped, 1);
    await run('project.preview.start');
    await run('project.open_on_mac');
    await run('project.check');
    assert.equal(runs.stopped, 2, 'a newer check stops the play of the older one');
    await run('project.preview.start');
    await run('project.open_on_mac');
    assert.deepEqual(reasons(), ['preview_stopped', 'newer_check']);
    await controlPlane.previews?.close();
    assert.equal(runs.stopped, 3);
    assert.deepEqual(reasons(), ['preview_stopped', 'newer_check', 'host_stopped']);
    assert.equal(types().filter((type) => type === 'project.mac_play.started').length, 3);
  });

  test('it stops when its viewer closes, when the preview server exits, and at a restart, each saying why', async (t) => {
    const { runs, play } = standIn();
    const { controlPlane, submit, done, journal, projectId, children } = plane(t, { play });
    const reasons = () =>
      journal
        .read({ after: 0, limit: 1000 })
        .flatMap((stored) =>
          stored.event.event_type === 'project.mac_play.stopped'
            ? [stored.event.payload.reason]
            : [],
        );
    const run = async (type: Parameters<typeof submit>[0]) => {
      const finished = await done(submit(type).command.command_id);
      assert.equal(finished?.status, 'completed', `${type}: ${JSON.stringify(finished)}`);
    };
    const plays = controlPlane.previews?.plays;
    assert.ok(plays !== undefined);
    await run('project.check');
    await run('project.preview.start');
    await run('project.open_on_mac');
    const off = plays.watch(projectId, () => undefined);
    off();
    await until(() => reasons().length === 1, 'the play to stop once its viewer closed');
    assert.equal(controlPlane.projection.macPlay(projectId), undefined);
    await run('project.open_on_mac');
    children.at(-1)?.kill('SIGKILL');
    await until(() => reasons().length === 2, 'the play to stop with its preview server');
    await run('project.preview.start');
    await run('project.open_on_mac');
    assert.equal(runs.stopped, 2);
    // As at a start after a crash: the journal shows a play the host no longer runs.
    await controlPlane.previews?.reconcile();
    assert.deepEqual(reasons(), ['closed', 'preview_stopped', 'host_stopped']);
    assert.equal(controlPlane.projection.macPlay(projectId), undefined);
  });

  test('without a browser set up for it, a game is not played and the command says so', async (t) => {
    const { controlPlane, submit, done } = plane(t, { play: { browser: null } });
    for (const type of ['project.check', 'project.preview.start'] as const) {
      const { command } = submit(type);
      await done(command.command_id);
    }
    const { outcome } = submit('project.open_on_mac');
    assert.equal(outcome.command?.rejection?.code, 'capability_unsupported');
    await controlPlane.close();
  });
});

describe("the host's checks and previews (ADR 0037)", () => {
  test("reads_confined holds only while every execution's start recorded that it ran confined (S2)", async (t) => {
    const { controlPlane, submit, done, projectId } = plane(t);
    const now = '2026-10-29T10:01:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000a1' as never;
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { project_id: projectId, workstream_id: workstreamId, execution_id: null },
        { title: 'Make it', objective: null },
        now,
        NO_CAUSE,
      ),
    );
    let n = 0;
    /** A finished Codex execution whose start recorded `confined` as given, or nothing, as before S2. */
    const execution = (confined: boolean | undefined) => {
      n += 1;
      const executionId = `01a19000-0000-7000-8000-0000000000e${n}` as never;
      const scope = {
        project_id: projectId,
        workstream_id: workstreamId,
        execution_id: executionId,
      };
      controlPlane.recorder.record(
        controlPlaneDraft(
          'execution.created',
          scope,
          {
            runtime: {
              runtime_id: 'codex',
              kind: 'codex',
              display_name: 'Codex',
              synthetic: false,
            },
            instruction: 'Make it.',
            directory: '/Users/someone/dev/garden',
          } as never,
          now,
          NO_CAUSE,
        ),
      );
      const runtimeEvent = (eventType: string, payload: unknown, sequence: number) =>
        controlPlane.recorder.record({
          ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
          event_type: eventType,
          source: { kind: 'runtime', runtime_id: 'codex' },
          source_native_id: `t${n}:${sequence}`,
          sequence,
          payload,
        } as never);
      runtimeEvent(
        'runtime.execution.started',
        confined === undefined ? { native_id: `t${n}` } : { native_id: `t${n}`, confined },
        1,
      );
      runtimeEvent('runtime.turn.started', { turn_id: `u${n}` }, 2);
      runtimeEvent('runtime.turn.completed', { turn_id: `u${n}` }, 3);
    };
    const readsConfined = async () => {
      const { command } = submit('project.check');
      const finished = await done(command.command_id);
      assert.equal(finished?.status, 'completed', JSON.stringify(finished));
      return controlPlane.projection.latestCheck(projectId)?.findings.reads_confined;
    };
    execution(true);
    assert.equal(await readsConfined(), true);
    // An execution recorded before the fact existed reads not confined, and so the project does.
    execution(undefined);
    assert.equal(await readsConfined(), false);
  });

  test('a project whose Codex execution ran on the open server does not read confined (S2)', async (t) => {
    const { controlPlane, submit, done, projectId } = plane(t);
    const now = '2026-10-29T10:01:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000a2' as never;
    const executionId = '01a19000-0000-7000-8000-0000000000f1' as never;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { ...scope, execution_id: null },
        { title: 'Make it', objective: null },
        now,
        NO_CAUSE,
      ),
    );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.created',
        scope,
        {
          runtime: { runtime_id: 'codex', kind: 'codex', display_name: 'Codex', synthetic: false },
          instruction: 'Make it.',
          directory: '/Users/someone/dev/garden',
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    for (const [sequence, eventType, payload] of [
      [1, 'runtime.execution.started', { native_id: 'open', confined: false }],
      [2, 'runtime.turn.started', { turn_id: 'u' }],
      [3, 'runtime.turn.completed', { turn_id: 'u' }],
    ] as const)
      controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'codex' },
        source_native_id: `open:${sequence}`,
        sequence,
        payload,
      } as never);
    const { command } = submit('project.check');
    await done(command.command_id);
    assert.equal(controlPlane.projection.latestCheck(projectId)?.findings.reads_confined, false);
  });

  test('a check records what was found, with reads_confined, and completes with the outcome', async (t) => {
    const { controlPlane, submit, done, types, checked, projectId, dataDir } = plane(t);
    const { command } = submit('project.check');
    const finished = await done(command.command_id);
    assert.equal(finished?.status, 'completed');
    assert.equal(finished?.result?.kind, 'project_checked');
    const check = controlPlane.projection.latestCheck(projectId);
    assert.equal(check?.findings.passed, true);
    assert.equal(check?.findings.page?.drawn, true);
    // No execution in the project ran on anything but Codex, on a host that confines it.
    assert.equal(check?.findings.reads_confined, true);
    // The steps of M1.8b are journaled as found; ready is passed and both steps ready.
    assert.equal(check?.findings.ready, true);
    // An animated scene: the change is unknown from one load, and the response is seen against its twin.
    assert.equal(check?.findings.laptop?.screen_changed, null);
    assert.equal(check?.findings.laptop?.response_seen, true);
    assert.equal(check?.findings.laptop?.exceptions_during, 0);
    assert.equal(check?.findings.vr?.how, 'offer_session');
    assert.deepEqual(check?.findings.notes?.items, [
      { kind: 'window_listener', file: 'src/game.ts', line: 12 },
    ]);
    // The pictures stay on the computer: only their hashes are journaled.
    assert.deepEqual(check?.findings.page?.pictures, [{ at_ms: 10_000, sha256: 'c'.repeat(64) }]);
    assert.ok(types().includes('project.check.recorded'));
    assert.equal(checked[0]?.project, '/Users/someone/dev/garden');
    assert.equal(checked[0]?.hostSource, '/repo/starters/iwsdk-vr/host');
    assert.ok(checked[0]?.cache.startsWith(join(dataDir, 'checks', projectId)));
    assert.ok(checked[0]?.port !== undefined && checked[0].port >= PREVIEW_PORT_FIRST);
  });

  test('a preview serves the built copy of the passed check, never the live folder, and stops when asked', async (t) => {
    const { controlPlane, submit, done, types, served, projectId, dataDir } = plane(t);
    await done(submit('project.check').command.command_id);
    const started = await done(submit('project.preview.start').command.command_id);
    assert.equal(started?.result?.kind, 'preview_started');
    assert.equal(served[0]?.env.HALCYONIC_PREVIEW_ROOT, served[0]?.dist);
    assert.ok(served[0]?.dist.startsWith(join(dataDir, 'checks', projectId)));
    assert.ok(!Object.values(served[0]?.env ?? {}).includes('/Users/someone/dev/garden'));
    assert.equal(controlPlane.projection.preview(projectId)?.port, PREVIEW_PORT_FIRST);
    assert.ok(existsSync(join(dataDir, 'previews', `${projectId}.json`)));
    await done(submit('project.preview.stop').command.command_id);
    assert.equal(controlPlane.projection.preview(projectId), undefined);
    assert.ok(!existsSync(join(dataDir, 'previews', `${projectId}.json`)));
    assert.deepEqual(
      types().filter((type) => type.startsWith('project.preview.')),
      ['project.preview.started', 'project.preview.stopped'],
    );
  });

  test('a check that did not pass is recorded, and its copy is not served', async (t) => {
    const result: CheckResult = {
      ...PASSED,
      page: {
        ...(PASSED.page as NonNullable<CheckResult['page']>),
        exception_count: 1,
        exceptions: ['Uncaught TypeError'],
      },
      passed: false,
    };
    const { controlPlane, submit, done, projectId } = plane(t, { result });
    const checked = await done(submit('project.check').command.command_id);
    assert.equal(checked?.status, 'completed', 'a check completes whatever it finds');
    assert.equal(controlPlane.projection.latestCheck(projectId)?.findings.passed, false);
    const { outcome } = submit('project.preview.start');
    assert.equal(outcome.disposition, 'rejected');
  });

  test('a new check stops the preview of the old one first', async (t) => {
    const { submit, done, types } = plane(t);
    await done(submit('project.check').command.command_id);
    await done(submit('project.preview.start').command.command_id);
    await done(submit('project.check').command.command_id);
    const previews = types().filter(
      (type) => type.startsWith('project.preview.') || type === 'project.check.recorded',
    );
    assert.deepEqual(previews, [
      'project.check.recorded',
      'project.preview.started',
      'project.preview.stopped',
      'project.check.recorded',
    ]);
  });

  test('at a restart, a recorded preview is recorded stopped, and a pid is signalled only if it still serves that copy', async (t) => {
    const { controlPlane, submit, done, types, projectId, dataDir, served } = plane(t);
    await done(submit('project.check').command.command_id);
    await done(submit('project.preview.start').command.command_id);
    assert.ok(served.length === 1);
    await controlPlane.previews?.reconcile();
    // The stand-in server is node -e, not `vite preview` of the check's copy, so it is left alone.
    assert.equal(controlPlane.projection.preview(projectId), undefined);
    assert.ok(!existsSync(join(dataDir, 'previews', `${projectId}.json`)));
    assert.equal(types().filter((type) => type === 'project.preview.stopped').length, 1);
  });

  test('a server that exits at once is never recorded started, and the command fails', async (t) => {
    const { controlPlane, submit, done, types, projectId, dataDir } = plane(t, {
      serve: () => spawn('/usr/bin/false', [], { stdio: 'ignore' }),
    });
    await done(submit('project.check').command.command_id);
    const started = await done(submit('project.preview.start').command.command_id);
    assert.equal(started?.status, 'failed');
    assert.equal(controlPlane.projection.preview(projectId), undefined);
    assert.ok(!existsSync(join(dataDir, 'previews', `${projectId}.json`)));
    assert.ok(!types().some((type) => type.startsWith('project.preview.')));
  });

  test('a port another process answers fails the start, even with the same bytes, and the server is stopped', async (t) => {
    const other = createServer((_, res) => res.end(body));
    let body = '';
    await new Promise<void>((resolve) => other.listen(PREVIEW_PORT_FIRST, '127.0.0.1', resolve));
    t.after(() => other.close());
    const { controlPlane, submit, done, types, projectId, children, checked } = plane(t, {
      // Alive but never serving, as Vite is for a moment before strictPort makes it exit.
      serve: () => spawn('/bin/sleep', ['30'], { stdio: 'ignore' }),
    });
    await done(submit('project.check').command.command_id);
    body = `<title>${checked[0]?.cache}</title>`;
    const started = await done(submit('project.preview.start').command.command_id);
    assert.equal(started?.status, 'failed');
    assert.equal(controlPlane.projection.preview(projectId), undefined);
    assert.ok(!types().some((type) => type.startsWith('project.preview.')));
    assert.equal(alive(children), 0);
  });

  test('two starts at once: one preview, on one port, and close() leaves no server running', async (t) => {
    const { controlPlane, submit, done, types, children } = plane(t);
    await done(submit('project.check').command.command_id);
    const first = submit('project.preview.start');
    const second = submit('project.preview.start');
    const results = [await done(first.command.command_id), await done(second.command.command_id)];
    assert.deepEqual(results.map((result) => result?.status).sort(), ['completed', 'failed']);
    assert.equal(types().filter((type) => type === 'project.preview.started').length, 1);
    assert.equal(children.length, 1);
    await controlPlane.close();
    assert.equal(alive(children), 0);
  });

  test('close() stops a server still starting, and its preview is never recorded started', async (t) => {
    const { controlPlane, submit, done, types, children } = plane(t, {
      serve: () => spawn('/bin/sleep', ['30'], { stdio: 'ignore' }),
    });
    await done(submit('project.check').command.command_id);
    const start = submit('project.preview.start');
    await until(() => alive(children) === 1, 'the server to be spawned');
    // The previews' own close, as the control plane's close calls it, so the failure is still journaled.
    await controlPlane.previews?.close();
    assert.equal(alive(children), 0);
    assert.equal((await done(start.command.command_id))?.status, 'failed');
    assert.ok(!types().includes('project.preview.started'));
  });

  test('a close() as a start is submitted spawns no server at all (AUDIT M15B2-S4)', async (t) => {
    const { controlPlane, submit, done, types, children } = plane(t);
    await done(submit('project.check').command.command_id);
    const start = submit('project.preview.start');
    await controlPlane.previews?.close();
    assert.equal((await done(start.command.command_id))?.status, 'failed');
    assert.equal(children.length, 0);
    assert.ok(!types().includes('project.preview.started'));
  });

  test('no preview starts while a check of the project runs (AUDIT M15B2-S1)', async (t) => {
    let release = () => {};
    const hold = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { submit, done, types } = plane(t, { hold });
    await done(submit('project.check').command.command_id);
    const second = submit('project.check');
    const started = await done(submit('project.preview.start').command.command_id);
    assert.equal(started?.status, 'failed');
    release();
    await done(second.command.command_id);
    assert.ok(!types().includes('project.preview.started'));
  });
});
