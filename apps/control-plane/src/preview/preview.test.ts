import assert from 'node:assert/strict';
import { type ChildProcess, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PassThrough } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import {
  type CommandEnvelope,
  type CommandId,
  PREVIEW_PORT_FIRST,
  PREVIEW_PORT_LAST,
  type ProjectId,
} from '@halcyonic/contracts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { templateDigest } from '../starters.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import { type CheckOptions, type CheckResult, HOST_FILES, summarizeExceptions } from './check.ts';
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
    own: { share: 0.02, reference: 'compared', frame: null },
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
  /** Play on my laptop: a stand-in browser launch. */
  readonly play?: HostPreviewsDeps['play'];
  /** The browser copy, standing in, its own check passing; null for none set up. */
  readonly browser?: HostPreviewsDeps['browser'];
  /** The launcher, checked now: by default one in place (S1). */
  readonly launcher?: string | null;
  /**
   * The starters on disk, and the template digest the project began from (DRAWN); by default a
   * tracked folder that is not there, so no starter's picture can be made.
   */
  readonly starters?: {
    readonly trackedDir: string;
    readonly installedDir: string;
    readonly began: string;
  };
  /** Whether the stand-in check makes the starter's own picture when asked; by default it does. */
  readonly reference?: 'made' | 'fails';
  /** What Halcyonic holds, which nothing journaled may carry. */
  readonly secrets?: () => readonly { readonly what: string; readonly value: string }[];
  /** Runs inside every check once its copy is built, where the real check serves it. */
  readonly during?: (options: CheckOptions) => Promise<void>;
}

/** The starter's own picture the stand-in check makes (DRAWN). */
const FRAME = Buffer.from('a stand-in for the starter frame');

/** A tracked starter on disk: its template, host files and lockfile, installed and recorded. */
function trackedStarter(root: string) {
  const trackedDir = join(root, 'starters');
  const installedDir = join(root, 'installed');
  const tracked = join(trackedDir, 'iwsdk-vr');
  mkdirSync(join(tracked, 'template', 'src'), { recursive: true });
  mkdirSync(join(tracked, 'host'), { recursive: true });
  writeFileSync(join(tracked, 'starter.json'), JSON.stringify({ format: 1, title: 'A VR app' }));
  const lock = '{"name":"starter","lockfileVersion":3,"packages":{}}\n';
  writeFileSync(join(tracked, 'package-lock.json'), lock);
  writeFileSync(join(tracked, 'template', 'index.html'), '<html></html>\n');
  writeFileSync(join(tracked, 'template', 'src', 'index.ts'), 'export {};\n');
  for (const file of HOST_FILES) writeFileSync(join(tracked, 'host', file), `${file}\n`);
  mkdirSync(join(installedDir, 'iwsdk-vr', 'node_modules'), { recursive: true });
  writeFileSync(
    join(installedDir, 'iwsdk-vr', 'installed.json'),
    JSON.stringify({ format: 1, lockfile: createHash('sha256').update(lock).digest('hex') }),
  );
  const template = templateDigest(join(tracked, 'template'));
  assert.notEqual(template, null);
  return { trackedDir, installedDir, template: template as string };
}

/** A control plane with checks on, a starter project in it, and the check and server standing in. */
function plane(
  t: TestContext,
  {
    result = PASSED,
    serve = serving,
    hold,
    play,
    browser = { folder: '/fake/chrome-headless-shell', sha256: 'a'.repeat(64) },
    launcher = '/fake/bin/halcyonic-disclaim',
    starters,
    reference = 'made',
    secrets,
    during,
  }: PlaneOptions = {},
) {
  const dataDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-previews-')));
  t.after(() => rmSync(dataDir, { recursive: true, force: true }));
  const checked: CheckOptions[] = [];
  const served: ServeArgs[] = [];
  const children: ChildProcess[] = [];
  const { controlPlane, journal } = createTestControlPlane({
    ...(secrets === undefined ? {} : { secrets }),
    previews: {
      dataDir,
      home: '/Users/someone',
      node: process.execPath,
      browser,
      browserProblem: () => null,
      launcher: () => launcher,
      starters: starters ?? {
        trackedDir: '/repo/starters',
        installedDir: join(dataDir, 'starters'),
      },
      confinesCodex: true,
      ...(play === undefined ? {} : { play }),
      runCheck: async (options) => {
        checked.push(options);
        if (checked.length > 1 && hold !== undefined) await hold;
        // As the real check does: the built copy in the check's own cache.
        mkdirSync(join(options.cache, 'dist'), { recursive: true });
        writeFileSync(join(options.cache, 'dist', 'index.html'), `<title>${options.cache}</title>`);
        await during?.(options);
        if (options.reference.kind !== 'making') return result;
        // Making the starter's own picture (DRAWN): its frame, kept in the check's cache.
        if (reference === 'fails' || result.page === null)
          return { ...result, page: null, passed: false, unknown: 'Chrome did not open the page.' };
        const file = join(options.cache, 'own-frame.png');
        writeFileSync(file, FRAME);
        const sha256 = createHash('sha256').update(FRAME).digest('hex');
        return {
          ...result,
          page: {
            ...result.page,
            own: { share: null, reference: 'unavailable', frame: { at_ms: 6000, file, sha256 } },
          },
        };
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
        template: starters?.began ?? 'b'.repeat(64),
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
        launch,
        viewerWaitMs: 60_000,
        viewerGraceMs: 50,
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

  test('without a browser set up for it, or without its launcher, a game is not played and the command says so', async (t) => {
    for (const options of [{ browser: null }, { launcher: null }]) {
      const { controlPlane, submit, done } = plane(t, options);
      for (const type of ['project.check', 'project.preview.start'] as const) {
        const { command } = submit(type);
        await done(command.command_id);
      }
      const { outcome } = submit('project.open_on_mac');
      assert.equal(
        outcome.command?.rejection?.code,
        'capability_unsupported',
        JSON.stringify(options),
      );
      await controlPlane.close();
    }
  });
});

describe("whether a check's browser had none of the app's macOS permissions (S1)", () => {
  test('through the launcher it is recorded true; without it the check still runs, recorded false; with no browser, not at all', async (t) => {
    const recorded = async (options: PlaneOptions) => {
      const { controlPlane, submit, done, checked } = plane(t, options);
      await done(submit('project.check').command.command_id);
      const findings = controlPlane.projection.latestCheck(
        '01a19000-0000-7000-8000-000000000001' as ProjectId,
      )?.findings;
      return { findings, options: checked[0] };
    };
    const through = await recorded({});
    assert.equal(through.findings?.own_permissions, true);
    assert.equal(through.options?.launcher, '/fake/bin/halcyonic-disclaim');
    assert.equal(through.options?.chrome, '/fake/chrome-headless-shell/chrome-headless-shell');
    const without = await recorded({ launcher: null });
    assert.equal(without.findings?.own_permissions, false, 'recorded as run without it');
    assert.equal(without.options?.launcher, null);
    assert.equal(without.findings?.passed, true, 'the check ran as before');
    const none = await recorded({ browser: null });
    assert.equal(none.options?.chrome, null);
    assert.equal(Object.hasOwn(none.findings ?? {}, 'own_permissions'), false, 'no browser ran');
  });
});

describe("the starter's own picture a check compares a project's with (DRAWN)", () => {
  const rooted = (t: TestContext) => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-reference-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return trackedStarter(root);
  };

  test('it is made once, from the tracked template in a folder of the host, kept and reused; each check compares with it', async (t) => {
    const starters = rooted(t);
    const { controlPlane, submit, done, checked, dataDir, projectId } = plane(t, {
      starters: { ...starters, began: starters.template },
    });
    await done(submit('project.check').command.command_id);
    assert.equal(checked.length, 2, 'the reference, then the project');
    const [making, first] = checked;
    assert.equal(making?.reference.kind, 'making');
    const references = join(dataDir, 'checks', 'references', 'iwsdk-vr');
    assert.ok(making?.project.startsWith(`${references}/`), making?.project);
    assert.ok(making?.cache.startsWith(`${references}/`), making?.cache);
    assert.equal(making?.template, join(starters.trackedDir, 'iwsdk-vr', 'template'));
    assert.equal(first?.project, '/Users/someone/dev/garden');
    assert.deepEqual(first?.reference, { kind: 'compared', png: FRAME });
    assert.deepEqual(controlPlane.projection.latestCheck(projectId)?.findings.page?.own, {
      share: 0.02,
      reference: 'compared',
    });
    // Only the picture is kept: the copy of the template and its check's cache are gone.
    const [key] = readdirSync(references);
    assert.deepEqual(readdirSync(join(references, key ?? '')).sort(), ['frame.png']);
    await done(submit('project.check').command.command_id);
    assert.equal(checked.length, 3, 'the next check reuses it');
    assert.deepEqual(checked[2]?.reference, { kind: 'compared', png: FRAME });
  });

  test("the starter's check records its server while it serves too, and both records go with their checks (AUDIT S1)", async (t) => {
    const starters = rooted(t);
    const seen: { name: string; cache: string; record: unknown }[] = [];
    const { submit, done, dataDir, projectId } = plane(t, {
      starters: { ...starters, began: starters.template },
      during: async (options) => {
        const name = options.reference.kind === 'making' ? 'reference' : 'check';
        await options.onServer?.(4242);
        const file = join(dataDir, 'previews', `${projectId}.${name}.json`);
        const record = existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : null;
        seen.push({ name, cache: options.cache, record });
      },
    });
    await done(submit('project.check').command.command_id);
    assert.deepEqual(
      seen.map(({ name }) => name),
      ['reference', 'check'],
    );
    for (const { name, cache, record } of seen)
      assert.deepEqual(record, { pid: 4242, copy: join(cache, 'dist') }, `${name}'s record`);
    assert.deepEqual(readdirSync(join(dataDir, 'previews')), [], 'no record once the check ended');
  });

  test('a project begun from another template than the tracked one is not compared, and its check is told so', async (t) => {
    const starters = rooted(t);
    const { submit, done, checked } = plane(t, {
      starters: { ...starters, began: 'e'.repeat(64) },
    });
    await done(submit('project.check').command.command_id);
    assert.equal(checked.length, 1, 'no reference made for it');
    assert.deepEqual(checked[0]?.reference, { kind: 'other_template' });
  });

  test('one that cannot be made is unavailable, never guessed, and is tried again at the next check', async (t) => {
    const starters = rooted(t);
    const { submit, done, checked, dataDir } = plane(t, {
      starters: { ...starters, began: starters.template },
      reference: 'fails',
    });
    await done(submit('project.check').command.command_id);
    assert.equal(checked.length, 2);
    assert.equal(checked[1]?.reference.kind, 'unavailable');
    const references = join(dataDir, 'checks', 'references', 'iwsdk-vr');
    assert.deepEqual(existsSync(references) ? readdirSync(references) : [], [], 'nothing kept');
    await done(submit('project.check').command.command_id);
    assert.equal(checked.length, 4, 'made again, then the project');
    assert.equal(checked[2]?.reference.kind, 'making');
  });

  test('with no tracked template to read, there is none to compare with, and nothing is made', async (t) => {
    const { submit, done, checked } = plane(t);
    await done(submit('project.check').command.command_id);
    assert.equal(checked.length, 1);
    assert.equal(checked[0]?.reference.kind, 'unavailable');
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
    // The first port of the range the system does not already hold: another process may hold 47930.
    const port = controlPlane.projection.preview(projectId)?.port ?? 0;
    assert.ok(port >= PREVIEW_PORT_FIRST && port <= PREVIEW_PORT_LAST, String(port));
    assert.equal((started?.result as { port?: number } | undefined)?.port, port);
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

  test("a page's exception texts are journaled without what Halcyonic holds or what looks like a key (P1b)", async (t) => {
    // A page can throw what its build pulled in, and its words are journaled for every paired device.
    const held = `omn_${'Zr8tYw3nQp5k'.repeat(2)}`;
    const shaped = `sk-proj-${'a1B2c3D4'.repeat(3)}`;
    // As long as the check keeps one, which naming the secret lengthens.
    const long = `Uncaught Error: ${'x'.repeat(255)} ${held}`;
    assert.equal(long.length, 300);
    const result: CheckResult = {
      ...PASSED,
      page: {
        ...(PASSED.page as NonNullable<CheckResult['page']>),
        exception_count: 3,
        exceptions: [`Uncaught Error: ${held}`, `Uncaught TypeError: ${shaped} was refused`, long],
      },
      passed: false,
    };
    const { controlPlane, submit, done, projectId, journal } = plane(t, {
      result,
      secrets: () => [{ what: 'Omnorium credential', value: held }],
    });
    await done(submit('project.check').command.command_id);

    const exceptions = controlPlane.projection.latestCheck(projectId)?.findings.page?.exceptions;
    assert.deepEqual(exceptions?.slice(0, 2), [
      'Uncaught Error: [redacted: Omnorium credential]',
      'Uncaught TypeError: [redacted] was refused',
    ]);
    // Longer once a secret is named, it is cut again to what a finding may hold.
    assert.ok((exceptions?.[2]?.length ?? 0) <= 300);
    const journaled = JSON.stringify(journal.read({ after: 0, limit: 1000 }));
    assert.ok(!journaled.includes(held) && !journaled.includes(shaped));
  });

  test("a held secret across an exception's 300th character is taken out whole before the text is cut (P1b, AUDIT)", async (t) => {
    // AUDIT's probe: Chrome's texts through the check's own summary, the secret where the cut falls.
    const hex = 'a3f9c2e1b4d6078f9e2a1c3b5d7e9f0a1b2c3d4e';
    const token = 'Qm7xT2vR9pL4kW8nZ3cY6hJ1sF5dG0bA7eU2iO9qX4t';
    const at = (secret: string, kept: number) => {
      const head = 'Uncaught TypeError: Failed to fetch https://tiles.example.com/v4/tiles?';
      const pad = 300 - kept - head.length - 'key='.length;
      const params = 'lang=en&'.repeat(Math.ceil(pad / 8)).slice(0, pad);
      return `${head}${params}key=${secret}&format=png\n    at load (http://127.0.0.1:47930/assets/index.js:1:1)`;
    };
    const result: CheckResult = {
      ...PASSED,
      page: {
        ...(PASSED.page as NonNullable<CheckResult['page']>),
        exception_count: 3,
        exceptions: summarizeExceptions([at(hex, 39), at(token, 31), at(hex, 40)]),
      },
      passed: false,
    };
    const { controlPlane, submit, done, projectId, journal } = plane(t, {
      result,
      secrets: () => [
        { what: 'Maps key', value: hex },
        { what: 'access token', value: token },
      ],
    });
    await done(submit('project.check').command.command_id);

    const kept = controlPlane.projection.latestCheck(projectId)?.findings.page?.exceptions ?? [];
    assert.equal(kept.length, 3);
    assert.ok(kept[0]?.includes('key=[redacted: Maps key]'), kept[0]);
    // Named, and the name itself may then be what the cut falls in.
    assert.ok(kept[1]?.includes('key=[redacted: '), kept[1]);
    for (const text of kept) assert.ok(text.length <= 300, text);
    const journaled = JSON.stringify(journal.read({ after: 0, limit: 1000 }));
    assert.ok(!journaled.includes(hex.slice(0, 8)), 'nothing of the key');
    assert.ok(!journaled.includes(token.slice(0, 8)), 'nothing of the token');
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

  test("a check's own server is recorded while it serves, so the next start stops one a crash left serving (AUDIT S1)", async (t) => {
    let release = () => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const servers: ChildProcess[] = [];
    const { controlPlane, submit, done, projectId, dataDir, checked } = plane(t, {
      during: async (options) => {
        // What a restart reads of the real one: `vite preview` of the check's own copy.
        const server = spawn(
          process.execPath,
          [
            '-e',
            'setInterval(() => {}, 1000)',
            'vite.js',
            'preview',
            '--outDir',
            join(options.cache, 'dist'),
          ],
          { stdio: 'ignore' },
        );
        servers.push(server);
        t.after(() => server.kill('SIGKILL'));
        if (server.pid !== undefined) await options.onServer?.(server.pid);
        // The second check is still serving when the control plane stops without a word.
        if (servers.length > 1) await held;
        server.kill('SIGTERM');
      },
    });
    const record = join(dataDir, 'previews', `${projectId}.check.json`);
    await done(submit('project.check').command.command_id);
    assert.ok(!existsSync(record), 'a check that ends takes its record with it');

    const second = submit('project.check');
    await until(
      () => servers.length === 2 && existsSync(record),
      "the second check's server recorded",
    );
    const server = servers[1] as ChildProcess;
    assert.deepEqual(JSON.parse(readFileSync(record, 'utf8')), {
      pid: server.pid,
      copy: join(checked[1]?.cache ?? '', 'dist'),
    });
    // What the control plane's next start does.
    await controlPlane.previews?.reconcile();
    await until(() => server.exitCode !== null || server.signalCode !== null, 'the server to stop');
    assert.equal(server.signalCode, 'SIGTERM');
    assert.ok(!existsSync(record));
    release();
    await done(second.command.command_id);
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
    t.after(() => other.close());
    const { controlPlane, submit, done, types, projectId, children, checked } = plane(t, {
      // Another process takes the port after the host found it free, and answers there; the
      // server itself is alive but never serving, as Vite is for a moment before strictPort exits.
      serve: (args) => {
        other.listen(Number(args.env.HALCYONIC_PREVIEW_PORT), '127.0.0.1');
        return spawn('/bin/sleep', ['30'], { stdio: 'ignore' });
      },
    });
    await done(submit('project.check').command.command_id);
    body = `<title>${checked[0]?.cache}</title>`;
    const started = await done(submit('project.preview.start').command.command_id);
    assert.equal(started?.status, 'failed');
    assert.equal(controlPlane.projection.preview(projectId), undefined);
    assert.ok(!types().some((type) => type.startsWith('project.preview.')));
    assert.equal(alive(children), 0);
  });

  test('a port the system already holds is skipped: the preview takes the next free one, and the holder is left alone', async (t) => {
    const holder = createServer((_, res) => res.end('another program'));
    // Held here, or already by another process: either way the host must not take it.
    await new Promise<void>((resolve) => {
      holder.once('error', () => resolve());
      holder.listen(PREVIEW_PORT_FIRST, '127.0.0.1', () => resolve());
    });
    t.after(() => holder.close());
    const { controlPlane, submit, done, projectId } = plane(t);
    await done(submit('project.check').command.command_id);
    const started = await done(submit('project.preview.start').command.command_id);
    assert.equal(started?.status, 'completed', JSON.stringify(started));
    const port = controlPlane.projection.preview(projectId)?.port ?? 0;
    assert.ok(port > PREVIEW_PORT_FIRST && port <= PREVIEW_PORT_LAST, String(port));
    if (holder.listening) {
      const answer = await fetch(`http://127.0.0.1:${PREVIEW_PORT_FIRST}/`);
      assert.equal(await answer.text(), 'another program');
    }
  });

  test('with every port of the range held, a start fails, nothing is spawned and nothing is recorded', async (t) => {
    const { submit, done, types, children } = plane(t);
    await done(submit('project.check').command.command_id);
    const holders: ReturnType<typeof createServer>[] = [];
    t.after(() => {
      for (const holder of holders) if (holder.listening) holder.close();
    });
    for (let port = PREVIEW_PORT_FIRST; port <= PREVIEW_PORT_LAST; port += 1) {
      const holder = createServer();
      await new Promise<void>((resolve) => {
        holder.once('error', () => resolve());
        holder.listen(port, '127.0.0.1', () => resolve());
      });
      holders.push(holder);
    }
    const started = await done(submit('project.preview.start').command.command_id);
    assert.equal(started?.status, 'failed');
    assert.equal(children.length, 0);
    assert.ok(!types().some((type) => type.startsWith('project.preview.')));
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

  test('once a turn changes files, the check and its preview read changed and neither starts a preview or a play until checked again (T2)', async (t) => {
    const { controlPlane, submit, done, projectId, dataDir } = plane(t);
    await done(submit('project.check').command.command_id);
    await done(submit('project.preview.start').command.command_id);
    assert.equal(controlPlane.projection.latestCheck(projectId)?.files, 'current');
    assert.equal(controlPlane.projection.preview(projectId)?.files, 'current');

    const now = '2026-10-29T10:02:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000c1' as never;
    const executionId = '01a19000-0000-7000-8000-0000000000d1' as never;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { ...scope, execution_id: null },
        { title: 'Change it', objective: null },
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
          instruction: 'Make the sky blue.',
          directory: '/Users/someone/dev/garden',
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    for (const [sequence, eventType, payload] of [
      [1, 'runtime.execution.started', { native_id: 'c1', confined: true }],
      [2, 'runtime.turn.started', { turn_id: 'u1' }],
      [3, 'runtime.turn.completed', { turn_id: 'u1' }],
    ] as const)
      controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'codex' },
        source_native_id: `c1:${sequence}`,
        sequence,
        payload,
      } as never);
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.files.observed',
        scope,
        {
          turn_id: 'u1',
          created: { files: [], total: 0 },
          changed: {
            files: [{ path: 'src/game.ts', bytes: 512, lines_before: 10, lines_after: 12 }],
            total: 1,
          },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        now,
        NO_CAUSE,
      ),
    );

    assert.equal(controlPlane.projection.latestCheck(projectId)?.files, 'changed');
    assert.equal(controlPlane.projection.preview(projectId)?.files, 'changed');
    assert.equal(submit('project.open_on_mac').outcome.command?.rejection?.code, 'invalid_state');
    await done(submit('project.preview.stop').command.command_id);

    await until(
      () =>
        controlPlane.projection.latestCheck(projectId)?.files === 'current' &&
        controlPlane.projection.projectBusy(projectId) === null,
      'the check after the change to finish',
    );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.files.observed',
        scope,
        {
          turn_id: 'u1b',
          created: { files: [], total: 0 },
          changed: {
            files: [{ path: 'src/game.ts', bytes: 540, lines_before: 12, lines_after: 13 }],
            total: 1,
          },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        now,
        NO_CAUSE,
      ),
    );
    assert.equal(submit('project.preview.start').outcome.command?.rejection?.code, 'invalid_state');

    await until(
      () =>
        controlPlane.projection.latestCheck(projectId)?.files === 'current' &&
        controlPlane.projection.projectBusy(projectId) === null,
      'the check to finish before starting a preview',
    );
    assert.equal(
      (await done(submit('project.preview.start').command.command_id))?.status,
      'completed',
    );
    assert.equal(controlPlane.projection.preview(projectId)?.files, 'current');

    // A failed check whose files later changed does not give stale compiler output.
    const failedId = '01a19000-0000-7000-8000-0000000000f9' as never;
    const failedCache = join(dataDir, 'checks', projectId, failedId);
    mkdirSync(failedCache, { recursive: true });
    writeFileSync(join(failedCache, 'tsc.log'), 'src/game.ts(1,1): error TS1005: stale.\n');
    controlPlane.recorder.record(
      controlPlaneDraft(
        'project.check.recorded',
        { project_id: projectId, workstream_id: null, execution_id: null },
        {
          check_id: failedId,
          findings: {
            ...PASSED,
            tsc: { exit: 2, seconds: 1 },
            page: null,
            passed: false,
            reads_confined: true,
            laptop: null,
            vr: null,
            ready: false,
          },
        },
        now,
        NO_CAUSE,
      ),
    );
    assert.notEqual(controlPlane.previews?.failedCheckOutput(projectId), null);
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.files.observed',
        scope,
        {
          turn_id: 'u2',
          created: { files: [], total: 0 },
          changed: {
            files: [{ path: 'src/game.ts', bytes: 600, lines_before: 12, lines_after: 14 }],
            total: 1,
          },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        now,
        NO_CAUSE,
      ),
    );
    assert.equal(controlPlane.previews?.failedCheckOutput(projectId), null);
  });

  test('after a turn that changed files in a starter project, the host checks the project on its own, journaled as an internal command; a turn with no file changes does not (A1)', async (t) => {
    const { controlPlane, journal, projectId, checked } = plane(t);
    const now = '2026-10-29T10:03:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000c2' as never;
    const executionId = '01a19000-0000-7000-8000-0000000000d2' as never;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { ...scope, execution_id: null },
        { title: 'Build it', objective: null },
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
          instruction: 'Add a lantern.',
          directory: '/Users/someone/dev/garden',
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    for (const [sequence, eventType, payload] of [
      [1, 'runtime.execution.started', { native_id: 'a1', confined: true }],
      [2, 'runtime.turn.started', { turn_id: 'u1' }],
      [3, 'runtime.turn.completed', { turn_id: 'u1' }],
    ] as const)
      controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'codex' },
        source_native_id: `a1:${sequence}`,
        sequence,
        payload,
      } as never);
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.files.observed',
        scope,
        {
          turn_id: 'u1',
          created: { files: [], total: 0 },
          changed: {
            files: [{ path: 'src/game.ts', bytes: 512, lines_before: 10, lines_after: 12 }],
            total: 1,
          },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        now,
        NO_CAUSE,
      ),
    );

    await until(
      () =>
        checked.length === 1 &&
        controlPlane.projection.latestCheck(projectId)?.files === 'current' &&
        controlPlane.projection.projectBusy(projectId) === null,
      'the host to check the project after the turn changed files',
    );
    const accepted = journal
      .read({ after: 0, limit: 1000 })
      .map((stored) => stored.event)
      .find(
        (event) =>
          event.event_type === 'command.accepted' &&
          event.payload.command.command_type === 'project.check',
      );
    assert.equal(
      (accepted?.payload as { received_via?: string } | undefined)?.received_via,
      'internal',
    );

    // A turn that changed no files does not start a check.
    for (const [sequence, eventType, payload] of [
      [4, 'runtime.turn.started', { turn_id: 'u2' }],
      [5, 'runtime.turn.completed', { turn_id: 'u2' }],
    ] as const)
      controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'codex' },
        source_native_id: `a1:${sequence}`,
        sequence,
        payload,
      } as never);
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.files.observed',
        scope,
        {
          turn_id: 'u2',
          created: { files: [], total: 0 },
          changed: { files: [], total: 0 },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        now,
        NO_CAUSE,
      ),
    );
    await new Promise((resolve) => setTimeout(resolve, 40));
    assert.equal(checked.length, 1);
  });

  test('while a check of the project is already running, file-changing turns wait and coalesce into one follow-up check once idle (A1)', async (t) => {
    let release = () => {};
    const hold = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { controlPlane, submit, done, projectId, checked } = plane(t, { hold });
    await done(submit('project.check').command.command_id);
    assert.equal(checked.length, 1);

    // Start a second check that holds in runCheck.
    const runningCheck = submit('project.check');
    await until(() => checked.length === 2, 'the held check to start');

    const now = '2026-10-29T10:04:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000c3' as never;
    const executionId = '01a19000-0000-7000-8000-0000000000d3' as never;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { ...scope, execution_id: null },
        { title: 'Refine it', objective: null },
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
          instruction: 'Adjust lighting.',
          directory: '/Users/someone/dev/garden',
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    for (const [sequence, eventType, payload] of [
      [1, 'runtime.execution.started', { native_id: 'a2', confined: true }],
      [2, 'runtime.turn.started', { turn_id: 'u1' }],
      [3, 'runtime.turn.completed', { turn_id: 'u1' }],
    ] as const)
      controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'codex' },
        source_native_id: `a2:${sequence}`,
        sequence,
        payload,
      } as never);
    for (const turnId of ['u1', 'u2']) {
      controlPlane.recorder.record(
        controlPlaneDraft(
          'execution.files.observed',
          scope,
          {
            turn_id: turnId,
            created: { files: [], total: 0 },
            changed: {
              files: [{ path: 'src/game.ts', bytes: 600, lines_before: 10, lines_after: 12 }],
              total: 1,
            },
            deleted: { files: [], total: 0 },
            not_read: 0,
            skipped: null,
          },
          now,
          NO_CAUSE,
        ),
      );
    }
    await new Promise((resolve) => setTimeout(resolve, 30));
    assert.equal(checked.length, 2, 'no second check starts while one is running');

    release();
    await done(runningCheck.command.command_id);
    await until(
      () =>
        checked.length === 3 &&
        controlPlane.projection.latestCheck(projectId)?.files === 'current' &&
        controlPlane.projection.projectBusy(projectId) === null,
      'the single follow-up check to finish after the held check ends',
    );
    assert.equal(checked.length, 3);
  });
});

describe('a world project beside a starter app (ADR 0049)', () => {
  test("a turn that changed a world's files starts no build check, and the app's own turn still does", async (t) => {
    const { controlPlane, journal, projectId, checked } = plane(t);
    const now = '2026-11-10T10:00:00.000Z' as never;
    const worldId = '01a19000-0000-7000-8000-0000000000e1' as ProjectId;
    const world = { project_id: worldId, workstream_id: null, execution_id: null };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'project.created',
        world,
        {
          name: 'My World',
          location: { path: '/Users/someone/dev/world', name: 'world', created: true },
        },
        now,
        NO_CAUSE,
      ),
    );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'project.starter_applied',
        world,
        {
          starter_id: 'world',
          lockfile: 'a'.repeat(64),
          files: 2,
          template: 'b'.repeat(64),
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    /** A task in the project whose one turn changed a file, as the Mac observed it. */
    const turn = (of: ProjectId, directory: string, tag: string, path: string) => {
      const scope = {
        project_id: of,
        workstream_id: `01a19000-0000-7000-8000-0000000000${tag}1` as never,
        execution_id: `01a19000-0000-7000-8000-0000000000${tag}2` as never,
      };
      controlPlane.recorder.record(
        controlPlaneDraft(
          'workstream.created',
          { ...scope, execution_id: null },
          { title: 'Change it', objective: null },
          now,
          NO_CAUSE,
        ),
      );
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
            instruction: 'Make it night.',
            directory,
          } as never,
          now,
          NO_CAUSE,
        ),
      );
      for (const [sequence, eventType, payload] of [
        [1, 'runtime.execution.started', { native_id: tag, confined: true }],
        [2, 'runtime.turn.started', { turn_id: 'u1' }],
        [3, 'runtime.turn.completed', { turn_id: 'u1' }],
      ] as const)
        controlPlane.recorder.record({
          ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
          event_type: eventType,
          source: { kind: 'runtime', runtime_id: 'codex' },
          source_native_id: `${tag}:${sequence}`,
          sequence,
          payload,
        } as never);
      controlPlane.recorder.record(
        controlPlaneDraft(
          'execution.files.observed',
          scope,
          {
            turn_id: 'u1',
            created: { files: [], total: 0 },
            changed: { files: [{ path, bytes: 512, lines_before: 10, lines_after: 12 }], total: 1 },
            deleted: { files: [], total: 0 },
            not_read: 0,
            skipped: null,
          },
          now,
          NO_CAUSE,
        ),
      );
    };
    const checksAsked = () =>
      journal
        .read({ after: 0, limit: 1000 })
        .map((stored) => stored.event)
        .filter(
          (event) =>
            (event.event_type === 'command.accepted' || event.event_type === 'command.rejected') &&
            event.payload.command.command_type === 'project.check',
        );

    turn(worldId, '/Users/someone/dev/world', 'a', 'world.json');
    await new Promise((resolve) => setTimeout(resolve, 80));
    assert.equal(checked.length, 0, 'no check ran for the world');
    assert.equal(checksAsked().length, 0, 'none was asked, so none was refused either');

    turn(projectId, '/Users/someone/dev/garden', 'b', 'src/game.ts');
    await until(() => checked.length === 1, 'the app to be checked after its own turn');
    assert.deepEqual(
      checksAsked().map((event) => event.project_id),
      [projectId],
    );
  });
});
