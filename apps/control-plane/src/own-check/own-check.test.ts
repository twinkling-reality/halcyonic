import assert from 'node:assert/strict';
import {
  mkdirSync,
  mkdtempSync,
  readdirSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import type {
  CommandEnvelope,
  EventEnvelope,
  ExecutionId,
  ProjectId,
  WorkstreamId,
} from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type {
  ExecutionContext,
  StartExecutionRequest,
  StartExecutionResult,
} from '@halcyonic/runtime-core';
import { AUTO_FIX_WORDS } from '../core/auto-fix.ts';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { readFolder } from '../observer/file-observer.ts';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { managerOf, managersOn, readManifest, writtenBetween } from './own-check.ts';

const [FEATURE] = DEMO_WORKSTREAMS as unknown as [(typeof DEMO_WORKSTREAMS)[number]];

function scratch(t: TestContext, name: string): string {
  const folder = realpathSync(mkdtempSync(join(tmpdir(), `halcyonic-own-${name}-`)));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  return folder;
}

const manifest = (folder: string, scripts: Record<string, unknown>) =>
  writeFileSync(join(folder, 'package.json'), JSON.stringify({ name: 'scratch', scripts }));

describe("a project's own check: what its package.json offers (ADR 0055)", () => {
  test('only the five names, each a non-empty command of at most 2,000 characters', async (t) => {
    const folder = scratch(t, 'offer');
    manifest(folder, {
      test: 'node --test',
      lint: '',
      build: 'x'.repeat(2001),
      check: 42,
      dev: 'vite',
      postinstall: 'curl example.com',
      typecheck: 'tsc --noEmit',
    });
    const read = await readManifest(folder);
    assert.equal(read.ok, true);
    assert.deepEqual(read.ok ? [...read.scripts] : null, [
      ['test', 'node --test'],
      ['typecheck', 'tsc --noEmit'],
    ]);
  });

  test('no manifest, one that is not JSON, one without scripts, a link and a large one are refused', async (t) => {
    const folder = scratch(t, 'refuse');
    assert.deepEqual(await readManifest(folder), { ok: false, reason: 'no_manifest' });
    writeFileSync(join(folder, 'package.json'), '{ not json');
    assert.deepEqual(await readManifest(folder), { ok: false, reason: 'manifest_unreadable' });
    writeFileSync(join(folder, 'package.json'), '{"name":"x"}');
    assert.deepEqual(await readManifest(folder), { ok: false, reason: 'manifest_unreadable' });
    const elsewhere = scratch(t, 'elsewhere');
    manifest(elsewhere, { test: 'node --test' });
    rmSync(join(folder, 'package.json'));
    symlinkSync(join(elsewhere, 'package.json'), join(folder, 'package.json'));
    assert.deepEqual(await readManifest(folder), { ok: false, reason: 'manifest_unreadable' });
    rmSync(join(folder, 'package.json'));
    writeFileSync(
      join(folder, 'package.json'),
      JSON.stringify({ scripts: { test: 'x' }, pad: 'p'.repeat(300_000) }),
    );
    assert.deepEqual(await readManifest(folder), { ok: false, reason: 'manifest_unreadable' });
  });

  test("the manager is the lockfile's, npm with none", (t) => {
    const folder = scratch(t, 'lock');
    assert.equal(managerOf(folder), 'npm');
    writeFileSync(join(folder, 'yarn.lock'), '');
    assert.equal(managerOf(folder), 'yarn');
    writeFileSync(join(folder, 'pnpm-lock.yaml'), '');
    assert.equal(managerOf(folder), 'pnpm');
    const path = `${folder}:relative/bin`;
    mkdirSync(join(folder, 'bin'));
    assert.deepEqual(managersOn(path), { npm: null, pnpm: null, yarn: null });
  });

  test('what the check wrote: counts and the first paths, from readings before and after', async (t) => {
    const folder = scratch(t, 'written');
    writeFileSync(join(folder, 'kept.txt'), 'a');
    writeFileSync(join(folder, 'gone.txt'), 'a');
    const before = await readFolder(folder);
    writeFileSync(join(folder, 'kept.txt'), 'b');
    rmSync(join(folder, 'gone.txt'));
    writeFileSync(join(folder, 'coverage.json'), '{}');
    const after = await readFolder(folder);
    assert.ok(before.ok && after.ok);
    assert.deepEqual(writtenBetween(before.reading, after.reading), {
      created: 1,
      changed: 1,
      deleted: 1,
      paths: ['coverage.json', 'gone.txt', 'kept.txt'],
    });
    assert.equal(writtenBetween(null, after.reading), null);
  });
});

/** The mock runtime, keeping what it was given. */
class RecordingRuntime extends MockRuntimeAdapter {
  readonly given: string[] = [];

  override startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    this.given.push(request.instruction);
    return super.startExecution(request);
  }

  override async sendInstruction(request: {
    execution: ExecutionContext;
    text: string;
  }): Promise<void> {
    this.given.push(request.text);
    return super.sendInstruction(request);
  }
}

const managers = managersOn(process.env.PATH);
const runs = process.platform === 'darwin' && managers.npm !== null;

/** A test script: it fails, naming a mark, while `fail.flag` is in the folder, and writes `out.txt`. */
const SCRIPT =
  "node -e \"require('fs').writeFileSync('out.txt','ok');if(require('fs').existsSync('fail.flag')){console.error('MARK the flag is set');process.exit(1)}\"";

async function until(check: () => boolean, what: string): Promise<void> {
  const deadline = Date.now() + 60_000;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
}

function plane(
  t: TestContext,
  scripts: Record<string, string> = { test: SCRIPT },
  options: { scriptMs?: number } = {},
) {
  const dataDir = scratch(t, 'data');
  const folder = scratch(t, 'project');
  manifest(folder, scripts);
  const runtimes: RecordingRuntime[] = [];
  const harness = createTestControlPlane({
    path: join(dataDir, 'journal.db'),
    adapters: (time) => {
      const adapter = new RecordingRuntime({ scenarios: SCENARIOS, clock: time, scheduler: time });
      runtimes.push(adapter);
      return [adapter];
    },
    ownChecks: {
      dataDir,
      home: '/Users/someone',
      node: process.execPath,
      managers,
      ...(options.scriptMs === undefined ? {} : { scriptMs: options.scriptMs }),
    },
  });
  const { controlPlane, commands, time } = harness;
  t.after(() => controlPlane.close());
  const runtime = runtimes[0] as RecordingRuntime;
  const projectId = '01a19000-0000-7000-8000-0000000000a2' as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const now = '2026-11-11T10:00:00.000Z' as never;
  controlPlane.recorder.record(
    controlPlaneDraft(
      'project.created',
      scope,
      { name: 'Own', location: { path: folder, name: 'own', created: false } },
      now,
      NO_CAUSE,
    ),
  );
  const events = (): EventEnvelope[] =>
    controlPlane.journal.read({ after: 0, limit: 5000 }).map((stored) => stored.event);
  const ofType = <T extends EventEnvelope['event_type']>(type: T) =>
    events().filter((event): event is Extract<EventEnvelope, { event_type: T }> => {
      return event.event_type === type;
    });
  const person = (command: CommandEnvelope) => controlPlane.commands.submit(command, 'websocket');
  const choose = (names: string[] = ['test']) =>
    person({
      ...commands.createProject('unused'),
      command_type: 'project.own_check.choose',
      payload: { project_id: projectId, scripts: names },
    } as CommandEnvelope);
  const check = () =>
    person({
      ...commands.createProject('unused'),
      command_type: 'project.check',
      payload: { project_id: projectId },
    } as CommandEnvelope);
  const recorded = () => ofType('project.own_check.recorded');
  const start = async (): Promise<ExecutionId> => {
    const created = controlPlane.commands.submit(
      commands.createWorkstream(projectId, FEATURE),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const workstreamId: WorkstreamId = created.command.result.workstream_id;
    const started = commands.startExecution(workstreamId, FEATURE);
    person({ ...started, payload: { ...started.payload, instruction: 'Make it better.' } });
    await time.runUntilIdle();
    const id = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    if (id == null) throw new Error('no execution');
    return id;
  };
  /** What the host's file observer records at a turn's end: one file changed. */
  const turnChanged = (executionId: ExecutionId) => {
    const view = controlPlane.projection.execution(executionId);
    if (view === undefined) throw new Error('no execution');
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.files.observed',
        {
          project_id: view.project_id,
          workstream_id: view.workstream_id,
          execution_id: view.execution_id,
        },
        {
          turn_id: null,
          created: { files: [], total: 0 },
          changed: { files: [], total: 1 },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        now,
        NO_CAUSE,
      ),
    );
  };
  const ended = () => ofType('execution.auto_fix.ended');
  const sent = () => ofType('execution.auto_fix.sent');
  return {
    folder,
    projectId,
    controlPlane,
    runtime,
    time,
    choose,
    check,
    recorded,
    start,
    turnChanged,
    ended,
    sent,
  };
}

describe("a project's own check, run inside the agents' sandbox on this Mac (ADR 0055)", {
  skip: runs ? false : 'needs macOS and npm',
}, () => {
  test('choosing records the commands, then runs once; it passes, and what it wrote is its own', async (t) => {
    const p = plane(t);
    const chosen = p.choose();
    assert.equal(chosen.disposition, 'accepted');
    await until(() => p.recorded().length === 1, 'the first run');
    const [run] = p.recorded();
    assert.equal(run?.payload.first, true);
    assert.equal(run?.payload.findings.outcome, 'ran');
    assert.equal(run?.payload.findings.passed, true);
    assert.deepEqual(run?.payload.findings.written, {
      created: 1,
      changed: 0,
      deleted: 0,
      paths: ['out.txt'],
    });
    const view = p.controlPlane.projection.ownCheck(p.projectId);
    assert.equal(view?.manager, 'npm');
    assert.equal(view?.scripts[0]?.command, SCRIPT);
    assert.equal(view?.ever_passed, true);
    assert.equal(view?.latest?.files, 'current');
    // The person's own check runs again on their press.
    p.check();
    await until(() => p.recorded().length === 2, 'the second run');
    assert.equal(p.recorded()[1]?.payload.first, false);
  });

  test("a failure right after a passing run goes to the agent with the script's first lines", async (t) => {
    const p = plane(t);
    p.choose();
    await until(() => p.recorded().length === 1, 'the first run');
    const executionId = await p.start();
    writeFileSync(join(p.folder, 'fail.flag'), '');
    p.turnChanged(executionId);
    await until(() => p.sent().length === 1, 'the try');
    await p.time.runUntilIdle();
    const given = p.runtime.given.at(-1) ?? '';
    assert.ok(given.startsWith(AUTO_FIX_WORDS));
    assert.match(given, /The project's test script failed \(exit 1\)/);
    assert.match(given, /\| MARK the flag is set/);
    assert.equal(
      p.controlPlane.projection.execution(executionId)?.auto_fix?.failed_steps?.[0],
      'own_check',
    );
  });

  test("a check that has never passed here is not sent as the agent's, and says so", async (t) => {
    const p = plane(t);
    writeFileSync(join(p.folder, 'fail.flag'), '');
    p.choose();
    await until(() => p.recorded().length === 1, 'the first run');
    const executionId = await p.start();
    p.turnChanged(executionId);
    await until(() => p.ended().length === 1, 'the end');
    assert.equal(p.ended()[0]?.payload.reason, 'never_passed');
    assert.equal(p.sent().length, 0);
  });

  test('one that was already failing before the turn is not sent either', async (t) => {
    const p = plane(t);
    p.choose();
    await until(() => p.recorded().length === 1, 'the first run');
    writeFileSync(join(p.folder, 'fail.flag'), '');
    p.check();
    await until(() => p.recorded().length === 2, 'the failing run');
    const executionId = await p.start();
    p.turnChanged(executionId);
    await until(() => p.ended().length === 1, 'the end');
    assert.equal(p.ended()[0]?.payload.reason, 'failing_before');
    assert.equal(p.sent().length, 0);
  });

  test('a command changed since it was chosen is not run, and nothing is sent', async (t) => {
    const p = plane(t);
    p.choose();
    await until(() => p.recorded().length === 1, 'the first run');
    const executionId = await p.start();
    manifest(p.folder, { test: 'exit 0' });
    p.turnChanged(executionId);
    await until(() => p.ended().length === 1, 'the end');
    const last = p.recorded().at(-1);
    assert.equal(last?.payload.findings.outcome, 'command_changed');
    assert.deepEqual(last?.payload.findings.runs, []);
    assert.equal(p.ended()[0]?.payload.reason, 'check_refused');
  });

  test('the network, a write outside the folder and a port are refused, and read as refused', async (t) => {
    const outside = scratch(t, 'outside');
    const p = plane(t, {
      test: SCRIPT,
      check: `node -e "require('fs').writeFileSync('${join(outside, 'x')}','x')"`,
      lint: 'node -e "fetch(\'http://1.1.1.1\').then(()=>process.exit(0),e=>{console.error(e.cause?.code);process.exit(3)})"',
      build:
        "node -e \"require('net').createServer().listen(0,'127.0.0.1').on('error',e=>{console.error(e.code);process.exit(4)})\"",
    });
    for (const [name, exit] of [
      ['check', 1],
      ['lint', 3],
      ['build', 4],
    ] as const) {
      const before = p.recorded().length;
      p.choose([name]);
      await until(() => p.recorded().length === before + 1, name);
      const run = p.recorded().at(-1)?.payload.findings.runs[0];
      assert.equal(run?.exit, exit, name);
      assert.equal(run?.refused, true, name);
    }
    assert.deepEqual(readdirSync(outside), []);
  });

  test('a script past its limit is stopped with its group, and what it left running stops too', async (t) => {
    const p = plane(
      t,
      {
        // A child that would run for ever, and a script that never ends.
        test: `node -e "require('child_process').spawn(process.execPath,['-e','setInterval(()=>{},1000)'],{stdio:'ignore'});setInterval(()=>{},1000)"`,
      },
      { scriptMs: 1500 },
    );
    p.choose();
    await until(() => p.recorded().length === 1, 'the run');
    const run = p.recorded()[0]?.payload.findings.runs[0];
    assert.equal(run?.timed_out, true);
    assert.equal(run?.exit, null);
    assert.equal(p.recorded()[0]?.payload.findings.passed, false);
  });

  test('a project made from a starter, or one with no choice yet, is refused', (t) => {
    const p = plane(t);
    const unchosen = p.check();
    assert.equal(unchosen.disposition, 'rejected');
    assert.match(
      unchosen.command?.rejection?.message ?? '',
      /Choose a check for this project first/,
    );
    p.controlPlane.recorder.record(
      controlPlaneDraft(
        'project.starter_applied',
        { project_id: p.projectId, workstream_id: null, execution_id: null },
        { starter_id: 'iwsdk-vr', lockfile: 'a'.repeat(64), files: 1, template: 'b'.repeat(64) },
        '2026-11-11T10:00:00.000Z' as never,
        NO_CAUSE,
      ),
    );
    const starter = p.choose();
    assert.equal(starter.disposition, 'rejected');
  });
});
