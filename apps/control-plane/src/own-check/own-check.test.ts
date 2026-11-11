import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
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
import {
  commandSha256,
  GO_COMMANDS,
  managerOf,
  managersOn,
  OwnChecks,
  readManifest,
  refusedAtEnd,
  writtenBetween,
} from './own-check.ts';

const [FEATURE] = DEMO_WORKSTREAMS as unknown as [(typeof DEMO_WORKSTREAMS)[number]];

function scratch(t: TestContext, name: string): string {
  const folder = realpathSync(mkdtempSync(join(tmpdir(), `halcyonic-own-${name}-`)));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  return folder;
}

/** A Go module of one package and one test, which passes or fails. */
function goModule(folder: string, passes: boolean): void {
  writeFileSync(join(folder, 'go.mod'), 'module example.com/probe\n\ngo 1.22\n');
  writeFileSync(
    join(folder, 'add.go'),
    'package probe\n\nfunc Add(a, b int) int { return a + b }\n',
  );
  writeFileSync(
    join(folder, 'add_test.go'),
    `package probe\n\nimport "testing"\n\nfunc TestAdd(t *testing.T) { if Add(1, 2) != ${passes ? 3 : 4} { t.Fatal("MARK bad sum") } }\n`,
  );
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

  test("the manager is the lockfile's, npm with none, go for a Go module with no package.json", async (t) => {
    const go = scratch(t, 'go');
    goModule(go, true);
    assert.equal(managerOf(go), 'go');
    const read = await readManifest(go);
    assert.deepEqual(read.ok ? [...read.scripts] : null, [
      ['test', 'go test ./...'],
      ['check', 'go vet ./...'],
    ]);
    const folder = scratch(t, 'lock');
    assert.equal(managerOf(folder), 'npm');
    writeFileSync(join(folder, 'yarn.lock'), '');
    assert.equal(managerOf(folder), 'yarn');
    writeFileSync(join(folder, 'pnpm-lock.yaml'), '');
    assert.equal(managerOf(folder), 'pnpm');
    const path = `${folder}:relative/bin`;
    mkdirSync(join(folder, 'bin'));
    assert.deepEqual(managersOn(path), { npm: null, pnpm: null, yarn: null, go: null });
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
  options: { scriptMs?: number; settleMs?: number; go?: boolean } = {},
) {
  const dataDir = scratch(t, 'data');
  const folder = scratch(t, 'project');
  if (options.go === true) goModule(folder, true);
  else manifest(folder, scripts);
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
      // The folder is read again after a run; a short settle keeps the suite quick.
      settleMs: options.settleMs ?? 50,
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
  /** The person's choice, carrying each command's SHA-256 as the host offers it now (AUDIT3 S1). */
  const choose = (names: string[] = ['test'], sha?: string) => {
    // A Go module's commands are the host's own; a package.json's are the project's.
    const manifest = existsSync(join(folder, 'package.json'))
      ? (JSON.parse(readFileSync(join(folder, 'package.json'), 'utf8')) as {
          scripts: Record<string, string>;
        })
      : { scripts: GO_COMMANDS as Record<string, string> };
    return person({
      ...commands.createProject('unused'),
      command_type: 'project.own_check.choose',
      payload: {
        project_id: projectId,
        scripts: names.map((name) => ({
          name,
          command_sha256: sha ?? commandSha256(manifest.scripts[name] ?? ''),
        })),
      },
    } as unknown as CommandEnvelope);
  };
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
  const failed = () => ofType('command.failed');
  return {
    folder,
    dataDir,
    commands,
    failed,
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
        // A child in the script's group that would beat for ever, and a script that never ends.
        test: 'node beating.js',
      },
      { scriptMs: 1500 },
    );
    writeFileSync(
      join(p.folder, 'beating.js'),
      "require('child_process').spawn(process.execPath, ['-e', \"setInterval(() => require('fs').appendFileSync('beat.txt', 'x'), 100)\"], { stdio: 'ignore' });\nsetInterval(() => {}, 1000);\n",
    );
    p.choose();
    await until(() => p.recorded().length === 1, 'the run');
    const run = p.recorded()[0]?.payload.findings.runs[0];
    assert.equal(run?.timed_out, true);
    assert.equal(run?.exit, null);
    assert.equal(p.recorded()[0]?.payload.findings.passed, false);
    // The child stopped with the group: its beat no longer grows (AUDIT3 S2).
    const size = () => readFileSync(join(p.folder, 'beat.txt'), 'utf8').length;
    const at = size();
    await new Promise((resolve) => setTimeout(resolve, 500));
    assert.equal(size(), at, 'the child stopped with the group');
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

  test('what runs is the chosen script alone: no pre or post script, and no shell a project .npmrc names (AUDIT3 1)', async (t) => {
    const p = plane(t, {
      pretest: "node -e \"require('fs').writeFileSync('pre.txt','x')\"",
      test: SCRIPT,
      posttest: "node -e \"require('fs').writeFileSync('post.txt','x')\"",
    });
    // A project .npmrc that would make every script a no-op that passes.
    writeFileSync(join(p.folder, '.npmrc'), 'script-shell=/usr/bin/true\n');
    writeFileSync(join(p.folder, 'fail.flag'), '');
    p.choose();
    await until(() => p.recorded().length === 1, 'the run');
    const findings = p.recorded()[0]?.payload.findings;
    assert.equal(findings?.passed, false, 'the test itself ran, and failed');
    assert.equal(findings?.runs[0]?.exit, 1);
    assert.deepEqual(
      readdirSync(p.folder)
        .filter((name) => name.endsWith('.txt'))
        .sort(),
      ['out.txt'],
    );
  });

  test("a run's own home starts empty every time: one script's ~/.npmrc never reaches the next (AUDIT3 2)", async (t) => {
    const p = plane(t, {
      test: SCRIPT,
      lint: "node -e \"require('fs').writeFileSync(require('os').homedir()+'/.npmrc','script-shell=/usr/bin/true')\"",
    });
    p.choose(['lint']);
    await until(() => p.recorded().length === 1, 'the lint run');
    writeFileSync(join(p.folder, 'fail.flag'), '');
    rmSync(join(p.folder, 'out.txt'), { force: true });
    p.choose(['test']);
    await until(() => p.recorded().length === 2, 'the test run');
    assert.equal(p.recorded()[1]?.payload.findings.passed, false);
    assert.ok(readdirSync(p.folder).includes('out.txt'), 'the test command ran');
  });

  test('a child holding the pipes does not hold the run, and the run says something outlived it (AUDIT3 3)', async (t) => {
    const p = plane(
      t,
      {
        test: "node -e \"require('child_process').spawn(process.execPath,['-e','setTimeout(()=>{},20000)'],{stdio:'inherit'})\"",
      },
      { scriptMs: 3000 },
    );
    const began = Date.now();
    p.choose();
    await until(() => p.recorded().length === 1, 'the run');
    assert.ok(Date.now() - began < 8000, 'recorded within the limit, not when the child ends');
    // npm waits for the child holding its output, so the run meets its limit: timed out, or, where the manager
    // exits first, marked as outlived. Either way it records, and the project is not held.
    const run = p.recorded()[0]?.payload.findings.runs[0];
    assert.ok(run?.timed_out === true || run?.left_running === true, JSON.stringify(run));
    assert.equal(p.recorded()[0]?.payload.findings.passed, false);
  });

  test('a write after the run, by a child that left the group, marks the run as not whole (AUDIT3 3)', async (t) => {
    const p = plane(t, { test: 'node late-child-start.js' }, { settleMs: 1500 });
    writeFileSync(
      join(p.folder, 'late-child-start.js'),
      "require('child_process').spawn(process.execPath, ['-e', \"setTimeout(() => require('fs').writeFileSync('late.txt', 'x'), 300)\"], { detached: true, stdio: 'ignore' }).unref();\n",
    );
    p.choose();
    await until(() => p.recorded().length === 1, 'the run');
    const run = p.recorded()[0]?.payload.findings.runs[0];
    assert.equal(run?.exit, 0);
    assert.equal(run?.left_running, true, 'the late write is seen');
  });

  test("a project .npmrc's node-options cannot make a failing check pass (AUDIT3 reread 2)", async (t) => {
    const p = plane(t);
    p.choose();
    await until(() => p.recorded().length === 1, 'the first run');
    writeFileSync(join(p.folder, 'fail.flag'), '');
    rmSync(join(p.folder, 'out.txt'), { force: true });
    writeFileSync(join(p.folder, 'pre.cjs'), 'process.exit(0)\n');
    writeFileSync(
      join(p.folder, '.npmrc'),
      `node-options=--require=${join(p.folder, 'pre.cjs')}\n`,
    );
    p.check();
    await until(() => p.recorded().length === 2, 'the second run');
    assert.equal(p.recorded()[1]?.payload.findings.passed, false);
    assert.ok(readdirSync(p.folder).includes('out.txt'), "the chosen command's body ran");
  });

  test('a check a crashed control plane left running is stopped at the next start (AUDIT3 reread 1)', async (t) => {
    const p = plane(
      t,
      { test: "node -e \"setInterval(()=>require('fs').appendFileSync('tick.txt','x'),200)\"" },
      {
        scriptMs: 60_000,
      },
    );
    p.choose();
    await until(() => readdirSync(p.folder).includes('tick.txt'), 'the script ticking');
    // The next start, on the same data folder, before this control plane has stopped anything.
    new OwnChecks({
      dataDir: p.dataDir,
      projection: p.controlPlane.projection,
      recorder: p.controlPlane.recorder,
      ids: { next: () => '01a19000-0000-7000-8000-0000000000d1' } as never,
      clock: p.controlPlane.clock,
      logger: { info() {}, warn() {}, error() {}, debug() {} } as never,
      home: '/Users/someone',
      node: process.execPath,
      managers,
    });
    const size = () => readFileSync(join(p.folder, 'tick.txt'), 'utf8').length;
    await new Promise((resolve) => setTimeout(resolve, 300));
    const at = size();
    await new Promise((resolve) => setTimeout(resolve, 800));
    assert.equal(size(), at, 'the left check stopped');
    assert.deepEqual(
      readdirSync(join(p.dataDir, 'own-checks', 'running')),
      [],
      'its record is gone',
    );
  });

  test('a check running when the control plane closes stops with it (AUDIT3 4)', async (t) => {
    const p = plane(t, {
      test: "node -e \"setInterval(()=>require('fs').appendFileSync('beat.txt','x'),100)\"",
    });
    p.choose();
    await until(() => readdirSync(p.folder).includes('beat.txt'), 'the script beating');
    await p.controlPlane.close();
    const size = () => readFileSync(join(p.folder, 'beat.txt'), 'utf8').length;
    const at = size();
    await new Promise((resolve) => setTimeout(resolve, 600));
    assert.equal(size(), at, 'nothing writes after the close');
    assert.deepEqual(
      readdirSync(join(p.dataDir, 'own-checks', 'running')),
      [],
      'no record is left',
    );
  });

  test('a failure is judged by the run before it of the same folder, never another folder (AUDIT3 6)', async (t) => {
    const p = plane(t);
    p.choose();
    await until(() => p.recorded().length === 1, 'the first run');
    writeFileSync(join(p.folder, 'fail.flag'), '');
    p.check();
    await until(() => p.recorded().length === 2, 'the failing run');
    // Another task's own copy passes meanwhile, as its check records it.
    const other = p.controlPlane.commands.submit(
      p.commands.createWorkstream(p.projectId, FEATURE),
      'internal',
    );
    const otherTask =
      other.command?.result?.kind === 'workstream_created'
        ? other.command.result.workstream_id
        : '';
    const last = p.recorded().at(-1)?.payload.findings;
    if (last === undefined) throw new Error('no run');
    p.controlPlane.recorder.record(
      controlPlaneDraft(
        'project.own_check.recorded',
        { project_id: p.projectId, workstream_id: null, execution_id: null },
        {
          check_id: '01a19000-0000-7000-8000-0000000000c1' as never,
          first: false,
          findings: { ...last, passed: true, runs: [] },
          workstream_id: otherTask as never,
        },
        '2026-11-11T10:00:00.000Z' as never,
        NO_CAUSE,
      ),
    );
    const executionId = await p.start();
    p.turnChanged(executionId);
    await until(() => p.ended().length === 1, 'the end');
    assert.equal(
      p.ended()[0]?.payload.reason,
      'failing_before',
      'the project folder was failing before the turn',
    );
    assert.equal(p.sent().length, 0);
  });

  test('a choice is refused when the command changed since the person read it (AUDIT3 S1)', async (t) => {
    const p = plane(t);
    const sent = p.choose(['test'], 'b'.repeat(64));
    assert.equal(sent.disposition, 'accepted');
    await until(() => p.failed().length === 1, 'the failure');
    assert.equal(p.failed()[0]?.payload.failure.code, 'command_changed');
    assert.equal(p.recorded().length, 0, 'nothing ran');
    assert.equal(p.controlPlane.projection.ownCheck(p.projectId), undefined, 'no choice recorded');
  });
});

describe("a refusal is read from the failed script's last lines alone (AUDIT3 S3)", () => {
  test("an EPERM in a passing test's log above a real failure is not a refusal", () => {
    const log = [
      'EPERM in a log line of an early test',
      ...Array.from({ length: 30 }, (_, i) => `ok ${i}`),
      'not ok 31 - the real failure',
    ].join('\n');
    assert.equal(refusedAtEnd(log), false);
    assert.equal(refusedAtEnd(`${log}\nError: EPERM: operation not permitted, connect`), true);
  });
});

describe("a Go module's own check, run inside the agents' sandbox on this Mac (ADR 0055)", {
  skip: process.platform === 'darwin' && managers.go !== null ? false : 'needs macOS and go',
}, () => {
  test('go test passes inside the profile, and a failing test goes to the agent with its FAIL line', async (t) => {
    const p = plane(t, {}, { go: true });
    p.choose(['test']);
    await until(() => p.recorded().length === 1, 'the first run');
    const first = p.recorded()[0]?.payload.findings;
    assert.equal(first?.manager, 'go');
    assert.equal(first?.passed, true, JSON.stringify(first));
    const executionId = await p.start();
    goModule(p.folder, false);
    p.turnChanged(executionId);
    await until(() => p.sent().length === 1, 'the try');
    await p.time.runUntilIdle();
    const given = p.runtime.given.at(-1) ?? '';
    assert.match(given, /The project's test script failed \(exit 1\)/);
    assert.match(given, /MARK bad sum/);
  });
});
