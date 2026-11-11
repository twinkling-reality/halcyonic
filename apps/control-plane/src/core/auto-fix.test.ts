import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
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
import { MOCK_CAPABILITIES, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type {
  ExecutionContext,
  StartExecutionRequest,
  StartExecutionResult,
} from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import type { CheckResult } from '../preview/check.ts';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { AUTO_FIX_WORDS } from './auto-fix.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';

const [FEATURE] = DEMO_WORKSTREAMS as unknown as [(typeof DEMO_WORKSTREAMS)[number]];

/** The mock runtime, keeping what it was given, optionally holding an instruction back. */
class RecordingRuntime extends MockRuntimeAdapter {
  readonly given: string[] = [];
  /** While set, an instruction waits for it before the runtime takes it. */
  hold: Promise<void> | null = null;

  override startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    this.given.push(request.instruction);
    return super.startExecution(request);
  }

  override async sendInstruction(request: {
    execution: ExecutionContext;
    text: string;
  }): Promise<void> {
    this.given.push(request.text);
    if (this.hold !== null) await this.hold;
    return super.sendInstruction(request);
  }
}

const FAILED: CheckResult = {
  tsc: { exit: 2, seconds: 1 },
  build: { exit: 0, seconds: 2 },
  page: null,
  unknown: null,
  passed: false,
  laptop: null,
  vr: null,
  notes: { scanned: 'full', items: [] },
  ready: false,
};
const PASSED: CheckResult = { ...FAILED, tsc: { exit: 0, seconds: 1 }, passed: true, ready: true };
/** A check that failed with nothing an agent could act on: no step failed and no page ran. */
const UNEXPLAINED: CheckResult = { ...FAILED, tsc: { exit: 0, seconds: 1 } };
/** The page in the check, as the real run's starter showed it: drawn, but only the starter's own scene. */
const page = (edges: number) => ({
  exceptions: [],
  exception_count: 0,
  console_errors: 0,
  canvases: 1,
  edges: { at_3s: edges, at_10s: edges },
  drawn: false,
  own: { share: 0, reference: 'compared' as const, frame: null },
  pictures: [],
});
/** Type check and build passed; the page shows only the starter's scene: no game made yet, no fault. */
const STARTER_LOOK: CheckResult = { ...UNEXPLAINED, page: page(56_000) };
/** Type check and build passed; the page showed nothing at all: a fault. */
const BLANK: CheckResult = { ...UNEXPLAINED, page: page(0) };
const MARK = 'error TS2304: Cannot find name (the stand-in).';

async function until(check: () => boolean, what: string): Promise<void> {
  const deadline = Date.now() + 10_000;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
}

/** Lets the host's own asynchronous work (checks, microtasks) finish without moving virtual time. */
async function quiet(): Promise<void> {
  for (let i = 0; i < 20; i += 1) await new Promise((resolve) => setTimeout(resolve, 2));
}

function plane(
  t: TestContext,
  results: CheckResult[],
  options: { atRest?: boolean; dataDir?: string } = {},
) {
  const dataDir =
    options.dataDir ?? realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-auto-fix-')));
  if (options.dataDir === undefined)
    t.after(() => rmSync(dataDir, { recursive: true, force: true }));
  const folder = join(dataDir, 'projects', 'garden');
  const reopened = options.dataDir !== undefined;
  mkdirSync(folder, { recursive: true });
  const runtimes: RecordingRuntime[] = [];
  const harness = createTestControlPlane({
    path: join(dataDir, 'journal.db'),
    adapters: (time) => {
      const adapter = new RecordingRuntime({ scenarios: SCENARIOS, clock: time, scheduler: time });
      if (options.atRest === false)
        Object.assign(adapter, {
          descriptor: {
            ...adapter.descriptor,
            capabilities: { ...MOCK_CAPABILITIES, instruct_at_rest: false },
          },
        });
      runtimes.push(adapter);
      return [adapter];
    },
    previews: {
      dataDir,
      home: '/Users/someone',
      node: process.execPath,
      browser: { folder: '/fake/chrome-headless-shell', sha256: 'a'.repeat(64) },
      browserProblem: () => null,
      launcher: () => '/fake/bin/halcyonic-disclaim',
      starters: { trackedDir: '/repo/starters', installedDir: join(dataDir, 'starters') },
      confinesCodex: true,
      runCheck: async (check) => {
        mkdirSync(check.cache, { recursive: true });
        writeFileSync(join(check.cache, 'tsc.log'), `src/game.ts(3,1): ${MARK}\n`);
        writeFileSync(join(check.cache, 'build.log'), '');
        return results.shift() ?? FAILED;
      },
      serve: () => {
        throw new Error('no preview in this test');
      },
    },
  });
  const { controlPlane, commands, time } = harness;
  t.after(() => controlPlane.close());
  const runtime = runtimes[0] as RecordingRuntime;
  const projectId = '01a19000-0000-7000-8000-000000000002' as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const now = '2026-11-10T10:00:00.000Z' as never;
  if (!reopened)
    controlPlane.recorder.record(
      controlPlaneDraft(
        'project.created',
        scope,
        { name: 'Garden', location: { path: folder, name: 'garden', created: true } },
        now,
        NO_CAUSE,
      ),
    );
  if (!reopened)
    controlPlane.recorder.record(
      controlPlaneDraft(
        'project.starter_applied',
        scope,
        { starter_id: 'iwsdk-vr', lockfile: 'a'.repeat(64), files: 12, template: 'b'.repeat(64) },
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
  const task = (): WorkstreamId => {
    const created = controlPlane.commands.submit(
      commands.createWorkstream(projectId, FEATURE),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    return created.command.result.workstream_id;
  };
  /** A start from the person, played to the end of its turn. */
  const start = async (workstreamId: WorkstreamId = task()): Promise<ExecutionId> => {
    const base = commands.startExecution(workstreamId, FEATURE);
    controlPlane.commands.submit(
      { ...base, payload: { ...base.payload, instruction: 'Make a garden game.' } },
      'websocket',
    );
    await time.runUntilIdle();
    const id = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    if (id == null) throw new Error('no execution');
    return id;
  };
  /** What the host's file observer records at a turn's end, here that one file changed. */
  const changedFiles = (executionId: ExecutionId, total = 1) => {
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
          changed: { files: [], total },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        now,
        NO_CAUSE,
      ),
    );
  };
  const checksDone = () =>
    ofType('command.completed').filter(
      (event) =>
        controlPlane.projection.command(event.payload.command_id)?.command_type === 'project.check',
    ).length;
  const person = (command: CommandEnvelope) => controlPlane.commands.submit(command, 'websocket');
  const stop = (executionId: ExecutionId) =>
    person({
      ...commands.createProject('unused'),
      command_type: 'execution.interrupt',
      payload: { execution_id: executionId },
    } as CommandEnvelope);
  const instruct = (executionId: ExecutionId, text: string) =>
    person({
      ...commands.createProject('unused'),
      command_type: 'execution.send_instruction',
      payload: { execution_id: executionId, text },
    } as CommandEnvelope);
  const sent = () => ofType('execution.auto_fix.sent');
  const ended = () => ofType('execution.auto_fix.ended');
  return {
    dataDir,
    folder,
    projectId,
    now,
    controlPlane,
    time,
    runtime,
    task,
    start,
    changedFiles,
    checksDone,
    stop,
    instruct,
    sent,
    ended,
    ofType,
  };
}

describe('a failed check goes back to its agent on its own, within a cap (ADR 0044)', () => {
  test("a chain never starts after the person's Stop, even a Stop refused at rest", async (t) => {
    const p = plane(t, [FAILED]);
    const executionId = await p.start();
    // The turn ended; the person presses Stop before the check is in. Nothing runs, so it is refused.
    const refused = p.stop(executionId);
    assert.equal(refused.disposition, 'rejected');
    p.changedFiles(executionId);
    await until(() => p.checksDone() === 1, 'the automatic check');
    await until(() => p.ended().length === 1, 'the end of the chain');
    await quiet();
    assert.equal(p.sent().length, 0);
    assert.deepEqual(
      { ...p.ended()[0]?.payload, check_id: undefined },
      { check_id: undefined, attempts: 0, cap: 2, reason: 'person_stopped' },
    );
    assert.deepEqual(p.runtime.given, ['Make a garden game.']);
    assert.equal(p.controlPlane.projection.execution(executionId)?.auto_fix?.state, 'ended');
  });

  test('Stop during a try ends the chain at once, and no try follows it', async (t) => {
    const p = plane(t, [FAILED, FAILED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await until(
      () => p.controlPlane.projection.execution(executionId)?.status === 'running',
      'the try running',
    );
    assert.equal(p.stop(executionId).disposition, 'accepted');
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'person_stopped');
    assert.equal(p.ended()[0]?.payload.attempts, 1);
    await p.time.runUntilIdle();
    // The stopped turn changed files; its check fails again, and nothing is sent.
    p.changedFiles(executionId);
    await until(() => p.checksDone() === 2, 'the second check');
    await quiet();
    await p.time.runUntilIdle();
    assert.equal(p.sent().length, 1);
    assert.equal(p.runtime.given.length, 2);
  });

  test("Stop before a try's turn begins: the host stops the try as soon as it runs", async (t) => {
    const p = plane(t, [FAILED]);
    const executionId = await p.start();
    let release = () => {};
    p.runtime.hold = new Promise<void>((resolve) => {
      release = resolve;
    });
    p.changedFiles(executionId);
    await until(() => p.runtime.given.length === 2, 'the try reaching the runtime');
    p.stop(executionId);
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'person_stopped');
    p.runtime.hold = null;
    release();
    await until(
      () => p.ofType('runtime.turn.interrupted').length === 1,
      "the host's own interrupt of the try",
    );
    const interrupts = p
      .ofType('command.accepted')
      .filter((event) => event.payload.command.command_type === 'execution.interrupt');
    assert.equal(interrupts.at(-1)?.payload.received_via, 'internal');
    assert.equal(interrupts.at(-1)?.payload.principal, null);
    await p.time.runUntilIdle();
    assert.equal(p.sent().length, 1);
  });

  test("the try is the host's words and the check's lines; it ends when the next check passes", async (t) => {
    const p = plane(t, [FAILED, PASSED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    const try1 = p.runtime.given[1] ?? '';
    assert.ok(try1.startsWith(`${AUTO_FIX_WORDS}\n\n`), try1);
    assert.ok(try1.includes(MARK), try1);
    const accepted = p
      .ofType('command.accepted')
      .find((event) => event.payload.command.command_id === p.sent()[0]?.payload.command_id);
    assert.equal(accepted?.payload.received_via, 'internal');
    assert.equal(accepted?.payload.principal, null);
    // The journal keeps the host's words; the check's lines are never journaled (FX1).
    assert.deepEqual(accepted?.payload.command.payload, {
      execution_id: executionId,
      text: AUTO_FIX_WORDS,
    });
    assert.equal(p.ofType('execution.check_output_given').length, 1);
    assert.equal(p.controlPlane.projection.execution(executionId)?.auto_fix?.state, 'fixing');
    assert.deepEqual(p.controlPlane.projection.execution(executionId)?.auto_fix?.failed_steps, [
      'typecheck',
    ]);
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.deepEqual(
      { ...p.ended()[0]?.payload, check_id: undefined },
      { check_id: undefined, attempts: 1, cap: 2, reason: 'passed' },
    );
    assert.deepEqual(p.controlPlane.projection.execution(executionId)?.auto_fix, {
      state: 'ended',
      check_id: p.ended()[0]?.payload.check_id,
      attempt: 1,
      cap: 2,
      reason: 'passed',
      // What got fixed: the type check the try was sent for.
      failed_steps: ['typecheck'],
    });
  });

  test('it stops at the cap and says so', async (t) => {
    const p = plane(t, [FAILED, FAILED, FAILED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 2, 'the second try');
    assert.deepEqual(
      p.sent().map((event) => [event.payload.attempt, event.payload.cap]),
      [
        [1, 2],
        [2, 2],
      ],
    );
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end of the chain');
    await quiet();
    assert.equal(p.ended()[0]?.payload.reason, 'cap_reached');
    assert.deepEqual(p.controlPlane.projection.execution(executionId)?.auto_fix?.failed_steps, [
      'typecheck',
    ]);
    assert.equal(p.ended()[0]?.payload.attempts, 2);
    assert.equal(p.sent().length, 2);
    assert.equal(p.runtime.given.length, 3);
  });

  test("the person's own words end the chain, and their turn may start a new one", async (t) => {
    const p = plane(t, [FAILED, FAILED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await p.time.runUntilIdle();
    assert.equal(p.instruct(executionId, 'Make it blue.').disposition, 'accepted');
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'person_instructed');
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 2, 'a new chain after the person');
    assert.equal(p.sent()[1]?.payload.attempt, 1);
  });

  test('two tasks changed the project before its check: none is sent it', async (t) => {
    const p = plane(t, [FAILED]);
    const first = await p.start();
    const second = await p.start();
    p.changedFiles(first);
    p.changedFiles(second);
    await until(() => p.ended().length === 2, 'both ends');
    await quiet();
    assert.equal(p.sent().length, 0);
    assert.deepEqual(
      p.ended().map((event) => [event.execution_id, event.payload.reason]),
      [
        [first, 'several_tasks'],
        [second, 'several_tasks'],
      ],
    );
  });

  test('a runtime that takes no instruction at rest is sent nothing', async (t) => {
    const p = plane(t, [FAILED], { atRest: false });
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end');
    assert.equal(p.ended()[0]?.payload.reason, 'runtime_cannot');
    assert.equal(p.sent().length, 0);
  });

  test('a check that gives an agent nothing to act on is not sent', async (t) => {
    const p = plane(t, [UNEXPLAINED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end');
    assert.equal(p.ended()[0]?.payload.reason, 'nothing_to_send');
    assert.equal(p.sent().length, 0);
  });

  test("a page that shows only the starter's own scene is a game not made yet, not a fault: not sent", async (t) => {
    const p = plane(t, [FAILED, STARTER_LOOK]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    // The first try's words carry the type check's lines alone: the starter's look is no fault.
    assert.deepEqual(
      p
        .ofType('command.accepted')
        .find((event) => event.payload.command.command_id === p.sent()[0]?.payload.command_id)
        ?.payload.command.payload,
      { execution_id: executionId, text: AUTO_FIX_WORDS },
    );
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end');
    assert.equal(p.ended()[0]?.payload.reason, 'nothing_to_send');
    assert.equal(p.ended()[0]?.payload.attempts, 1);
    assert.equal(p.sent().length, 1);
  });

  test('a page that showed nothing at all is sent back, in those words', async (t) => {
    const p = plane(t, [BLANK]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the try');
    assert.equal(
      p.runtime.given[1],
      `${AUTO_FIX_WORDS}\n\nThe page showed nothing in the check's 10 seconds.`,
    );
  });

  test('a try that changed nothing ends the chain', async (t) => {
    const p = plane(t, [FAILED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await p.time.runUntilIdle();
    p.changedFiles(executionId, 0);
    await until(() => p.ended().length === 1, 'the end');
    assert.equal(p.ended()[0]?.payload.reason, 'no_change');
  });
});

describe('what AUDIT2 found (2026-11-10)', () => {
  test("the person's words, refused while a try runs, end the chain and no try follows them", async (t) => {
    const p = plane(t, [FAILED, FAILED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await until(
      () => p.controlPlane.projection.execution(executionId)?.status === 'running',
      'the try running',
    );
    // The mock takes no instruction while a turn runs, as Claude Agent: the person's words are refused.
    assert.equal(p.instruct(executionId, 'No, do something else instead.').disposition, 'rejected');
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'person_instructed');
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.checksDone() === 2, 'the second check');
    await quiet();
    assert.equal(p.sent().length, 1);
    assert.equal(p.ended().length, 1);
  });

  test('a chain under way at a restart ends once, and no try follows until the person speaks', async (t) => {
    const first = plane(t, [FAILED]);
    t.after(() => rmSync(first.dataDir, { recursive: true, force: true }));
    const executionId = await first.start();
    first.changedFiles(executionId);
    await until(() => first.sent().length === 1, 'the first try');
    // The try was delivered (its command has its outcome) and its turn runs when the host stops.
    const tryCommand = first.sent()[0]?.payload.command_id ?? '';
    await until(
      () => first.controlPlane.projection.command(tryCommand)?.status === 'completed',
      "the try's delivery",
    );
    await first.controlPlane.close();
    const second = plane(t, [FAILED], { dataDir: first.dataDir });
    // As the host's start does: what the restart made unknowable is recorded so.
    second.controlPlane.reconcile();
    await quiet();
    assert.deepEqual(
      second.ended().map((event) => [event.payload.reason, event.payload.attempts]),
      [['host_restarted', 1]],
    );
    second.changedFiles(executionId);
    await until(() => second.checksDone() === 2, 'a check after the restart');
    await quiet();
    assert.equal(second.sent().length, 1);
    assert.equal(second.ended().length, 1);
  });
});

describe("the automatic check of a task's own copy (ADR 0045)", () => {
  test("a turn that changed a task's copy has that copy checked, a turn in the project's folder the project", async (t) => {
    const p = plane(t, [PASSED, PASSED]);
    const { controlPlane, projectId, now } = p;
    const workstreamId = p.task();
    const where = { project_id: projectId, workstream_id: workstreamId, execution_id: null };
    const copy = join(p.dataDir, 'tasks', projectId, workstreamId, 'garden');
    mkdirSync(copy, { recursive: true });
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.copy_made',
        where,
        { directory: copy, base_tree: 'a'.repeat(64), files: 1 },
        now,
        NO_CAUSE,
      ),
    );
    // An execution of the task that worked in its copy and is at rest: recorded here directly,
    // as the mock runtime works in no folder.
    const executionId = '01a19000-0000-7000-8000-0000000000e1' as ExecutionId;
    const scope = { ...where, execution_id: executionId };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.created',
        scope,
        {
          runtime: {
            runtime_id: 'mock' as never,
            kind: 'mock',
            display_name: 'Mock',
            synthetic: true,
          },
          instruction: 'Make a garden game.',
          directory: copy,
        },
        now,
        NO_CAUSE,
      ),
    );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.start_failed',
        scope,
        { error: { code: 'test', message: 'Recorded at rest for the test.' } },
        now,
        NO_CAUSE,
      ),
    );
    p.changedFiles(executionId);
    await until(() => p.checksDone() === 1, "the copy's automatic check");
    const first = p.ofType('project.check.recorded')[0];
    assert.equal(first?.payload.workstream_id, workstreamId, "the task's copy was checked");
    assert.equal(controlPlane.projection.project(projectId)?.check?.workstream_id, workstreamId);

    // A task working in the project's own folder has the project checked.
    const executionIdHere = await p.start();
    p.changedFiles(executionIdHere);
    await until(() => p.checksDone() === 2, "the project's automatic check");
    assert.equal(p.ofType('project.check.recorded')[1]?.payload.workstream_id, undefined);
  });
});
