import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import type { CommandEnvelope, CommandId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type {
  ExecutionContext,
  StartExecutionRequest,
  StartExecutionResult,
} from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import type { CheckResult } from '../preview/check.ts';
import { capturingLogger, createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';

const [FEATURE] = DEMO_WORKSTREAMS as unknown as [(typeof DEMO_WORKSTREAMS)[number]];

/** The mock runtime, keeping the text each start and instruction gave it. */
class RecordingRuntime extends MockRuntimeAdapter {
  readonly given: string[] = [];

  override startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    this.given.push(request.instruction);
    return super.startExecution(request);
  }

  override sendInstruction(request: { execution: ExecutionContext; text: string }): Promise<void> {
    this.given.push(request.text);
    return super.sendInstruction(request);
  }
}

const FAILED: CheckResult = {
  tsc: { exit: 2, seconds: 1 },
  build: { exit: 1, seconds: 2 },
  page: null,
  unknown: null,
  passed: false,
  laptop: null,
  vr: null,
  notes: { scanned: 'full', items: [] },
  ready: false,
};
const PASSED: CheckResult = {
  ...FAILED,
  tsc: { exit: 0, seconds: 1 },
  build: { exit: 0, seconds: 2 },
};
/** A line of the stand-in type check, unlike anything else in the run, to find it wherever it went. */
const MARK = 'error TS1434: Unexpected keyword or identifier (the stand-in).';

async function until(check: () => boolean, what: string): Promise<void> {
  const deadline = Date.now() + 10_000;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

function plane(t: TestContext, results: CheckResult[]) {
  const dataDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-check-output-')));
  t.after(() => rmSync(dataDir, { recursive: true, force: true }));
  const folder = join(dataDir, 'projects', 'garden');
  mkdirSync(folder, { recursive: true });
  const runtime: RecordingRuntime[] = [];
  const { logger, entries: logged } = capturingLogger();
  const harness = createTestControlPlane({
    logger,
    adapters: (time) => {
      const adapter = new RecordingRuntime({ scenarios: SCENARIOS, clock: time, scheduler: time });
      runtime.push(adapter);
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
      // As the real check does: each step's output in the check's own folder, paths as tsc gives them.
      runCheck: async (options) => {
        mkdirSync(options.cache, { recursive: true });
        writeFileSync(
          join(options.cache, 'tsc.log'),
          `${join(folder, 'src/game.ts')}(81,3): ${MARK}\n`,
        );
        writeFileSync(join(options.cache, 'build.log'), 'error during build:\nUnexpected token\n');
        return results.shift() ?? FAILED;
      },
      serve: () => {
        throw new Error('no preview in this test');
      },
    },
  });
  const { controlPlane, commands, time } = harness;
  t.after(() => controlPlane.close());
  const projectId = '01a19000-0000-7000-8000-000000000001' as ProjectId;
  const scope = { project_id: projectId, workstream_id: null, execution_id: null };
  const now = '2026-10-31T10:00:00.000Z' as never;
  controlPlane.recorder.record(
    controlPlaneDraft(
      'project.created',
      scope,
      { name: 'Garden', location: { path: folder, name: 'garden', created: true } },
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
  const created = controlPlane.commands.submit(
    commands.createWorkstream(projectId, FEATURE),
    'internal',
  );
  if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
  const workstreamId: WorkstreamId = created.command.result.workstream_id;
  let n = 0;
  const check = async () => {
    n += 1;
    const command = {
      schema_version: 1,
      command_id: `00000000-0000-4000-9000-${String(n).padStart(12, '0')}` as CommandId,
      issued_at: '2026-10-31T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'project.check',
      payload: { project_id: projectId },
    } as CommandEnvelope;
    controlPlane.commands.submit(command, 'internal');
    await until(() => {
      const status = controlPlane.projection.command(command.command_id)?.status;
      return status === 'completed' || status === 'failed';
    }, 'the check');
  };
  const start = async (instruction: string) => {
    const base = commands.startExecution(workstreamId, FEATURE);
    controlPlane.commands.submit(
      { ...base, payload: { ...base.payload, instruction } },
      'internal',
    );
    await time.runUntilIdle();
    return controlPlane.projection.workstream(workstreamId)?.current_execution_id ?? '';
  };
  const instruct = async (executionId: string, text: string) => {
    const sent = controlPlane.commands.submit(
      {
        ...commands.createProject('unused'),
        command_type: 'execution.send_instruction' as const,
        payload: { execution_id: executionId as never, text },
      },
      'internal',
    );
    assert.equal(sent.disposition, 'accepted', JSON.stringify(sent.command?.rejection));
    await time.runUntilIdle();
  };
  const given = () =>
    controlPlane.journal
      .read({ after: 0, limit: 1000 })
      .map((stored) => stored.event)
      .filter((event) => event.event_type === 'execution.check_output_given');
  return { controlPlane, runtime, logged, projectId, check, start, instruct, given };
}

describe("a failed check's output goes with the person's next instruction, once (FX1)", () => {
  test("the agent gets the person's words, then the Mac's check apart; the journal keeps only the words", async (t) => {
    const { controlPlane, runtime, logged, projectId, check, start, instruct, given } = plane(t, [
      FAILED,
    ]);
    await check();
    const latest = controlPlane.projection.latestCheck(projectId);
    assert.equal(latest?.findings.passed, false);
    const executionId = await start('It does not work yet, can you fix it so I can play?');
    const first = runtime[0]?.given[0] ?? '';
    assert.ok(first.startsWith('It does not work yet, can you fix it so I can play?\n\n'), first);
    assert.ok(
      first.includes("[The Mac's own check of this project, not the person's words]"),
      first,
    );
    assert.ok(first.includes(`./src/game.ts(81,3): ${MARK}`), first);
    assert.ok(first.includes('The build failed (exit 1). Its first lines:'), first);
    assert.ok(!first.includes(controlPlane.projection.project(projectId)?.location?.path ?? '?'));
    // The journal: the person's words alone, and the fact of what was given, never its text.
    const execution = controlPlane.journal
      .read({ after: 0, limit: 1000 })
      .map((stored) => stored.event)
      .find((event) => event.event_type === 'execution.created');
    assert.equal(
      (execution?.payload as { instruction?: string } | undefined)?.instruction,
      'It does not work yet, can you fix it so I can play?',
    );
    const facts = given();
    assert.equal(facts.length, 1);
    assert.deepEqual(
      { ...(facts[0]?.payload as object), bytes: undefined },
      { check_id: latest?.check_id, parts: ['typecheck', 'build'], lines: 3, bytes: undefined },
    );
    assert.ok(
      JSON.stringify(controlPlane.journal.read({ after: 0, limit: 1000 })).indexOf(MARK) === -1,
    );
    // Once per check: the next instruction is the person's words only.
    await instruct(executionId, 'Make the moles pink.');
    assert.equal(runtime[0]?.given[1], 'Make the moles pink.');
    assert.equal(given().length, 1);
    // Never logged.
    assert.ok(!logged.some((line) => JSON.stringify(line).includes(MARK)));
  });

  test('a newer failed check is given again; a passed check gives nothing', async (t) => {
    const { runtime, check, start, instruct, given } = plane(t, [FAILED, FAILED, PASSED]);
    await check();
    const executionId = await start('Fix it.');
    assert.equal(given().length, 1);
    await check();
    await instruct(executionId, 'Try again.');
    assert.ok(runtime[0]?.given[1]?.startsWith('Try again.\n\n'));
    assert.equal(given().length, 2);
    await check();
    await instruct(executionId, 'Now make it blue.');
    assert.equal(runtime[0]?.given[2], 'Now make it blue.');
    assert.equal(given().length, 2);
  });
});
