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
import { BLANK_PAGE_EDGES } from '@halcyonic/domain';
import { MOCK_CAPABILITIES, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type {
  ExecutionContext,
  StartExecutionRequest,
  StartExecutionResult,
} from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import type { CheckOptions, CheckResult } from '../preview/check.ts';
import { DRAWN_EDGES, drawnFrom, passes } from '../preview/check.ts';
import { readingOf, readingPasses } from '../preview/laptop-probe.ts';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { AUTO_FIX_READING_WORDS, AUTO_FIX_WORDS } from './auto-fix.ts';
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
/** A type error, and a page that shows only the starter's scene: the try is sent for the type check alone. */
const TSC_AND_STARTER_LOOK: CheckResult = { ...FAILED, page: page(56_000) };
const MARK = 'error TS2304: Cannot find name (the stand-in).';
/** A check that passed, its laptop step pressed and answered, with the kit's words read at `px` high, a game's. */
const readAt = (px: number): CheckResult => ({
  ...PASSED,
  laptop: {
    layer: 'starter',
    targets: 1,
    mouse: 'reached',
    key: 'reached',
    screen_changed: true,
    reading: readingOf('starter', { sealed: true, texts: [px] }, null),
    ready: px >= 10,
  },
  ready: px >= 10,
});
/** Passed, but its words read 6 px high on a laptop: the second trigger (VRIFY, 2026-11-13). */
const UNREADABLE = readAt(6);
const READABLE = readAt(14);

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
  results: (CheckResult | ((check: CheckOptions) => CheckResult))[],
  options: { atRest?: boolean; dataDir?: string; trackedDir?: string } = {},
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
      starters: {
        trackedDir: options.trackedDir ?? '/repo/starters',
        installedDir: join(dataDir, 'starters'),
      },
      runCheck: async (check) => {
        mkdirSync(check.cache, { recursive: true });
        writeFileSync(join(check.cache, 'tsc.log'), `src/game.ts(3,1): ${MARK}\n`);
        writeFileSync(join(check.cache, 'build.log'), '');
        const next = results.shift() ?? FAILED;
        return typeof next === 'function' ? next(check) : next;
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
    // That check's own steps to fix: none, so the view never names the starter's look as a fault.
    assert.deepEqual(p.controlPlane.projection.execution(executionId)?.auto_fix?.failed_steps, []);
  });

  test("the view names only the steps a try was sent for, never the starter's look (AUDIT2)", async (t) => {
    const p = plane(t, [TSC_AND_STARTER_LOOK]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the try');
    assert.equal(p.runtime.given[1]?.startsWith(`${AUTO_FIX_WORDS}\n\n`), true);
    assert.ok(!(p.runtime.given[1] ?? '').includes('The page'), p.runtime.given[1]);
    assert.deepEqual(p.controlPlane.projection.execution(executionId)?.auto_fix?.failed_steps, [
      'typecheck',
    ]);
  });

  test("the blank page's edge threshold is the check's own", () => {
    assert.equal(BLANK_PAGE_EDGES, DRAWN_EDGES);
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

describe("a check whose starter's picture could not be made is never the agent's to fix (VRIFY, 2026-11-13)", () => {
  /** Everything seen passed; the starter's own picture to compare with could not be made, as the check reads it. */
  const unmade = (exceptions: string[] = []): CheckResult => {
    const own = { share: null, reference: 'unavailable' as const, frame: null };
    const seen = {
      ...page(56_000),
      exceptions,
      exception_count: exceptions.length,
      own,
      drawn: drawnFrom(56_000, 56_000, own),
    };
    const ok = { exit: 0, seconds: 1 };
    return {
      ...UNEXPLAINED,
      page: seen,
      unknown: 'Not known whether it draws its own look.',
      passed: passes(ok, UNEXPLAINED.build, seen),
    };
  };

  test('no try is sent or counted and no chain begins; the check is recorded not known, never failed', async (t) => {
    const p = plane(t, [unmade()]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.checksDone() === 1, 'the check');
    await quiet();
    const findings = p.controlPlane.projection.latestCheck(p.projectId)?.findings;
    assert.equal(findings?.page?.drawn, null, 'drawn is not known, never false');
    assert.equal(findings?.verdict, 'not_known');
    assert.equal(findings?.passed, false, 'passed keeps its meaning');
    assert.equal(p.sent().length, 0);
    assert.equal(p.ended().length, 0, 'not even a chain that never began is recorded');
    assert.equal(p.controlPlane.projection.execution(executionId)?.auto_fix, undefined);
  });

  test('a chain under way ends with the one try it made, and nothing more is sent', async (t) => {
    const p = plane(t, [FAILED, unmade()]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end');
    await quiet();
    assert.equal(p.ended()[0]?.payload.reason, 'nothing_to_send');
    assert.equal(p.ended()[0]?.payload.attempts, 1);
    assert.equal(p.sent().length, 1);
  });

  test('an exception beside it is a failure the check saw: recorded failed and sent back as before (C1)', async (t) => {
    const p = plane(t, [unmade(['Uncaught TypeError: boom'])]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the try');
    assert.equal(p.controlPlane.projection.latestCheck(p.projectId)?.findings.verdict, 'failed');
    assert.equal(
      p.runtime.given[1],
      `${AUTO_FIX_WORDS}\n\nThe page stopped on these errors:\n- Uncaught TypeError: boom`,
    );
    assert.doesNotMatch(p.runtime.given[1] ?? '', /not known|could not make/i);
  });
});

describe('a check that passed but does not read on a laptop goes back too, under the same cap (ADR 0044, VRIFY 2026-11-13)', () => {
  test('the reading alone starts a chain, in its own words with the numbers; it ends when the words read', async (t) => {
    const p = plane(t, [UNREADABLE, READABLE]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the try');
    assert.equal(
      p.runtime.given[1],
      `${AUTO_FIX_READING_WORDS}\n\nWords are drawn 6 px high at a laptop's 1280 by 800 view; a person reads at least 10 px. Bring the pages nearer the laptop camera or make the words larger (TYPE in src/pages.ts), so the laptop reads as the headset does.`,
    );
    assert.deepEqual(p.controlPlane.projection.execution(executionId)?.auto_fix?.failed_steps, [
      'reading',
    ]);
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'passed');
    assert.equal(p.ended()[0]?.payload.attempts, 1);
    assert.deepEqual(p.controlPlane.projection.execution(executionId)?.auto_fix?.failed_steps, [
      'reading',
    ]);
  });

  test('a failed check and then an unreadable one share the two tries: the cap counts both triggers', async (t) => {
    const p = plane(t, [FAILED, UNREADABLE, UNREADABLE]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try, for the type check');
    assert.ok((p.runtime.given[1] ?? '').startsWith(`${AUTO_FIX_WORDS}\n\n`));
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 2, 'the second try, for the reading');
    assert.ok((p.runtime.given[2] ?? '').startsWith(`${AUTO_FIX_READING_WORDS}\n\n`));
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
    assert.equal(p.ended()[0]?.payload.attempts, 2);
    assert.equal(p.sent().length, 2, 'no third try, whatever triggered it');
  });

  test('a check that passed and reads on a laptop, or whose reading is not known, sends nothing', async (t) => {
    const unknown: CheckResult = {
      ...READABLE,
      laptop: {
        ...(READABLE.laptop as NonNullable<CheckResult['laptop']>),
        reading: readingOf('starter', { sealed: true, texts: [] }, null),
        ready: true,
      },
    };
    for (const result of [READABLE, unknown]) {
      const p = plane(t, [result]);
      const executionId = await p.start();
      p.changedFiles(executionId);
      await p.time.runUntilIdle();
      await quiet();
      assert.equal(p.sent().length, 0);
      assert.equal(p.runtime.given.length, 1);
    }
  });

  /**
   * A check of this project whose kit files changed, its reading built as the real check builds it, from the last
   * measured reading the host hands it: a reading that cannot measure never clears one measured under the floor.
   */
  const kitChanged = (check: CheckOptions): CheckResult => {
    const reading = readingOf('changed', null, null, check.lastReading ?? null);
    const ready = readingPasses(reading);
    return {
      ...READABLE,
      laptop: { ...(READABLE.laptop as NonNullable<CheckResult['laptop']>), reading, ready },
      ready,
    };
  };

  test('measured under, then changed: not fixed, the next try says the last measurement; never "ready" (AUDIT3)', async (t) => {
    const p = plane(t, [UNREADABLE, kitChanged, kitChanged]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 2, 'the second try: not fixed');
    const second = p.runtime.given[2] ?? '';
    assert.ok(second.startsWith(`${AUTO_FIX_READING_WORDS}\n\n`), second);
    assert.match(
      second,
      /Words were drawn 6 px high at a laptop's 1280 by 800 view when last measured/,
    );
    assert.match(second, /can no longer measure them: a starter kit file that measures them/);
    assert.doesNotMatch(second, /ready/i);
    const latest = p.controlPlane.projection.latestCheck(p.projectId);
    assert.equal(latest?.findings.laptop?.reading?.last_measured_px, 6);
    assert.equal(latest?.findings.laptop?.ready, false);
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end of the chain');
    await quiet();
    assert.equal(
      p.ended()[0]?.payload.reason,
      'cap_reached',
      'never passed by becoming unmeasurable',
    );
  });

  test('measured under, then measured over: cleared; never measured and not known: nothing held', async (t) => {
    const p = plane(t, [UNREADABLE, READABLE, kitChanged]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the try');
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'passed');
    // The last measured reading is now over the floor, so a check that cannot measure holds nothing.
    await p.time.runUntilIdle();
    p.changedFiles(executionId);
    await until(() => p.checksDone() === 3, 'the third check');
    await quiet();
    const latest = p.controlPlane.projection.latestCheck(p.projectId);
    assert.equal(latest?.findings.laptop?.reading?.state, 'changed');
    assert.equal(latest?.findings.laptop?.reading?.last_measured_px, null);
    assert.equal(latest?.findings.laptop?.ready, true);
    assert.equal(p.sent().length, 1);
    const fresh = plane(t, [kitChanged]);
    const first = await fresh.start();
    fresh.changedFiles(first);
    await until(() => fresh.checksDone() === 1, 'the first check');
    await quiet();
    assert.equal(
      fresh.controlPlane.projection.latestCheck(fresh.projectId)?.findings.laptop?.ready,
      true,
    );
    assert.equal(fresh.sent().length, 0);
  });
});

describe("what the starter's check refuses goes back to the agent in the starter's words (VRIFY, 2026-11-12)", () => {
  test("a VR version showing its original's share picture fails its check with the starter's words, gets its try, and passes once it is gone", async (t) => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-refused-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const words = "This shows the original app's share picture. Bring the original's own view.";
    mkdirSync(join(root, 'starters', 'iwsdk-vr'), { recursive: true });
    writeFileSync(
      join(root, 'starters', 'iwsdk-vr', 'starter.json'),
      JSON.stringify({
        format: 1,
        title: 'A VR app',
        check_refuses: [
          { kind: 'original_share_image', words },
          { kind: 'unknown_kind', words: 'never' },
        ],
      }),
    );
    const p = plane(t, [PASSED, PASSED], { trackedDir: join(root, 'starters') });
    // The original web app, with its share picture, and the VR version made from it.
    const originalFolder = join(p.dataDir, 'projects', 'wind-chimes');
    mkdirSync(join(originalFolder, 'public'), { recursive: true });
    writeFileSync(
      join(originalFolder, 'index.html'),
      '<meta property="og:image" content="/og.png">\n',
    );
    const picture = Buffer.from('the original app share picture');
    writeFileSync(join(originalFolder, 'public', 'og.png'), picture);
    const originalId = '01a19000-0000-7000-8000-000000000009' as ProjectId;
    const originalScope = { project_id: originalId, workstream_id: null, execution_id: null };
    p.controlPlane.recorder.record(
      controlPlaneDraft(
        'project.created',
        originalScope,
        {
          name: 'Wind Chimes',
          location: { path: originalFolder, name: 'wind-chimes', created: false },
        },
        p.now,
        NO_CAUSE,
      ),
    );
    p.controlPlane.recorder.record(
      controlPlaneDraft(
        'project.made_from',
        { project_id: p.projectId, workstream_id: null, execution_id: null },
        { original_project_id: originalId, instruction: 'Make a VR version.' },
        p.now,
        NO_CAUSE,
      ),
    );
    mkdirSync(join(p.folder, 'public', 'textures'), { recursive: true });
    writeFileSync(join(p.folder, 'public', 'textures', 'hero.png'), picture);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the try');
    const refused = p.ofType('project.check.recorded')[0]?.payload.findings;
    assert.equal(refused?.passed, false);
    assert.equal(refused?.ready, false);
    assert.deepEqual(refused?.refusals, [
      { kind: 'original_share_image', words, file: 'public/textures/hero.png' },
    ]);
    assert.equal(
      p.runtime.given[1],
      `${AUTO_FIX_WORDS}\n\nThe check refused it:\n- ${words} (public/textures/hero.png)`,
    );
    // The agent takes the picture out; the next check refuses nothing and the chain ends passed.
    await p.time.runUntilIdle();
    rmSync(join(p.folder, 'public', 'textures', 'hero.png'));
    p.changedFiles(executionId);
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'passed');
    const second = p.ofType('project.check.recorded')[1]?.payload.findings;
    assert.equal(second?.passed, true);
    assert.equal(second?.refusals, undefined);
  });
});

describe('what AUDIT2 found (2026-11-10)', () => {
  test("the person's words while a try runs end the chain; the next try comes only after them", async (t) => {
    const p = plane(t, [FAILED, FAILED]);
    const executionId = await p.start();
    p.changedFiles(executionId);
    await until(() => p.sent().length === 1, 'the first try');
    await until(
      () => p.controlPlane.projection.execution(executionId)?.status === 'running',
      'the try running',
    );
    // The mock takes no instruction while a turn runs, as Claude Agent: the person's words wait for
    // the try's turn to end (ADR 0056), and end the chain all the same.
    const words = p.instruct(executionId, 'No, do something else instead.');
    assert.equal(words.disposition, 'accepted');
    assert.equal(p.ofType('execution.instruction.queued').length, 1);
    await until(() => p.ended().length === 1, 'the end of the chain');
    assert.equal(p.ended()[0]?.payload.reason, 'person_instructed');
    // The try's turn ends; the person's words are sent before anything of the host's.
    await p.time.runUntilIdle();
    await quiet();
    await p.time.runUntilIdle();
    assert.equal(p.runtime.given.at(-1), 'No, do something else instead.');
    // Their turn changed files: the check that follows it may begin a new chain, after their words.
    p.changedFiles(executionId);
    await until(() => p.checksDone() === 2, 'the second check');
    await quiet();
    assert.equal(p.runtime.given.indexOf('No, do something else instead.'), 2);
    // The second check failed as the first did: a new chain's first try, after the person's words.
    await until(() => p.sent().length === 2, 'the new chain');
    assert.equal(p.sent()[1]?.payload.attempt, 1, 'a new chain, not the old one');
    assert.equal(p.runtime.given.length >= 4, true);
    assert.equal(p.runtime.given.indexOf('No, do something else instead.') < 3, true);
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

describe('instructions held for a runtime that cannot steer (ADR 0056)', () => {
  /** Plays virtual time and the host's own work until `check` holds. */
  async function settle(p: ReturnType<typeof plane>, check: () => boolean, what: string) {
    for (let round = 0; round < 50 && !check(); round += 1) {
      await quiet();
      await p.time.runUntilIdle();
    }
    assert.ok(check(), `timed out waiting for ${what}`);
  }
  const position = (p: ReturnType<typeof plane>, match: (event: EventEnvelope) => boolean) =>
    p.ofType('command.accepted').length === 0
      ? -1
      : p.controlPlane.journal
          .read({ after: 0, limit: 5000 })
          .findIndex((stored) => match(stored.event));

  test('each waits for a completed turn, in order, and the check waits until none is left', async (t) => {
    const p = plane(t, [PASSED]);
    const executionId = await p.start();
    // A turn running: the person's words at rest set it going.
    assert.equal(p.instruct(executionId, 'Grow tulips.').disposition, 'accepted');
    await until(
      () => p.controlPlane.projection.execution(executionId)?.status === 'running',
      'the turn running',
    );
    const first = p.instruct(executionId, 'Paint them blue.');
    const second = p.instruct(executionId, 'And the fence.');
    assert.equal(first.disposition, 'accepted');
    assert.equal(first.command?.status, 'accepted', 'waiting, not sent');
    assert.equal(p.ofType('execution.instruction.queued').length, 2);
    assert.deepEqual(
      p.controlPlane.projection
        .execution(executionId)
        ?.queued_instructions?.map((queued) => queued.command_id),
      [first.command?.command_id, second.command?.command_id],
    );
    // The turn's files are seen; the check is needed, and waits for the queue.
    p.changedFiles(executionId);
    await settle(
      p,
      () => p.runtime.given.length === 4 && p.checksDone() === 1,
      'both sent, then the check',
    );
    assert.deepEqual(p.runtime.given, [
      'Make a garden game.',
      'Grow tulips.',
      'Paint them blue.',
      'And the fence.',
    ]);
    const lastSent = position(
      p,
      (event) =>
        event.event_type === 'command.completed' &&
        event.payload.command_id === second.command?.command_id,
    );
    const checkAccepted = position(
      p,
      (event) =>
        event.event_type === 'command.accepted' &&
        event.payload.command.command_type === 'project.check',
    );
    assert.ok(lastSent > 0 && checkAccepted > lastSent, 'no check before the last one was sent');
    assert.equal(p.controlPlane.projection.execution(executionId)?.queued_instructions, undefined);
  });

  test('Stop fails what waits, never sent; a dropped one is not sent either', async (t) => {
    const p = plane(t, []);
    const executionId = await p.start();
    p.instruct(executionId, 'Grow tulips.');
    await until(
      () => p.controlPlane.projection.execution(executionId)?.status === 'running',
      'the turn running',
    );
    const dropped = p.instruct(executionId, 'Paint them blue.');
    const stopped = p.instruct(executionId, 'And the fence.');
    const drop = p.controlPlane.commands.submit(
      {
        ...p.ofType('command.accepted')[0]?.payload.command,
        command_id: '01a19000-0000-7000-8000-0000000000d1',
        command_type: 'execution.drop_queued_instruction',
        payload: { execution_id: executionId, command_id: dropped.command?.command_id },
      } as CommandEnvelope,
      'websocket',
    );
    assert.equal(drop.disposition, 'accepted');
    await quiet();
    const failure = (id: string | undefined) =>
      p.ofType('command.failed').find((event) => event.payload.command_id === id)?.payload.failure;
    assert.deepEqual(
      { ...failure(dropped.command?.command_id), message: undefined },
      { code: 'queued_dropped', effect: 'none', message: undefined },
    );
    assert.equal(p.stop(executionId).disposition, 'accepted');
    await quiet();
    assert.deepEqual(
      { ...failure(stopped.command?.command_id), message: undefined },
      { code: 'queued_stopped', effect: 'none', message: undefined },
    );
    await settle(
      p,
      () => p.controlPlane.projection.execution(executionId)?.status !== 'running',
      'the stop',
    );
    await quiet();
    assert.deepEqual(p.runtime.given, ['Make a garden game.', 'Grow tulips.'], 'neither was sent');
    const again = p.controlPlane.commands.submit(
      {
        ...p.ofType('command.accepted')[0]?.payload.command,
        command_id: '01a19000-0000-7000-8000-0000000000d2',
        command_type: 'execution.drop_queued_instruction',
        payload: { execution_id: executionId, command_id: stopped.command?.command_id },
      } as CommandEnvelope,
      'websocket',
    );
    assert.equal(again.disposition, 'rejected', 'only a waiting one can be dropped');
  });

  test('once its send began it can no longer be dropped: it ends as a sent instruction does (AUDIT2)', async (t) => {
    const p = plane(t, []);
    const executionId = await p.start();
    p.instruct(executionId, 'Grow tulips.');
    await until(
      () => p.controlPlane.projection.execution(executionId)?.status === 'running',
      'the turn running',
    );
    const waiting = p.instruct(executionId, 'Paint them blue.');
    // The runtime holds the queued one's send open, as a re-attach or a model load would.
    let release = () => {};
    p.runtime.hold = new Promise<void>((resolve) => {
      release = resolve;
    });
    // In small steps, so the held send is not run on to its command's timeout.
    for (let step = 0; step < 400 && p.runtime.given.at(-1) !== 'Paint them blue.'; step += 1) {
      await p.time.advance(50);
      await quiet();
    }
    assert.equal(p.runtime.given.at(-1), 'Paint them blue.', 'its send under way');
    assert.equal(p.ofType('execution.instruction.dequeued').length, 1);
    assert.equal(p.controlPlane.projection.execution(executionId)?.queued_instructions, undefined);
    const drop = p.controlPlane.commands.submit(
      {
        ...p.ofType('command.accepted')[0]?.payload.command,
        command_id: '01a19000-0000-7000-8000-0000000000d3',
        command_type: 'execution.drop_queued_instruction',
        payload: { execution_id: executionId, command_id: waiting.command?.command_id },
      } as CommandEnvelope,
      'websocket',
    );
    assert.equal(drop.disposition, 'rejected', 'not waiting any more');
    p.runtime.hold = null;
    release();
    await settle(
      p,
      () =>
        p.controlPlane.projection.command(waiting.command?.command_id ?? '')?.status ===
        'completed',
      'its send confirmed',
    );
    assert.equal(
      p
        .ofType('command.failed')
        .some((event) => event.payload.command_id === waiting.command?.command_id),
      false,
      'never also journaled as not sent',
    );
  });

  test('a restart fails what was waiting as never sent, not as of unknown effect (AUDIT2 S1)', async (t) => {
    const first = plane(t, []);
    t.after(() => rmSync(first.dataDir, { recursive: true, force: true }));
    const executionId = await first.start();
    first.instruct(executionId, 'Grow tulips.');
    await until(
      () => first.controlPlane.projection.execution(executionId)?.status === 'running',
      'the turn running',
    );
    const waiting = first.instruct(executionId, 'Paint them blue.');
    assert.equal(first.ofType('execution.instruction.queued').length, 1);
    await first.controlPlane.close();
    const second = plane(t, [], { dataDir: first.dataDir });
    second.controlPlane.reconcile();
    await quiet();
    const failed = second
      .ofType('command.failed')
      .find((event) => event.payload.command_id === waiting.command?.command_id);
    assert.deepEqual(
      [failed?.payload.failure.code, failed?.payload.failure.effect],
      ['queued_stopped', 'none'],
    );
  });
});
