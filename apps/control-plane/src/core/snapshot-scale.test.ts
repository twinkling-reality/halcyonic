import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { performance } from 'node:perf_hooks';
import { after, test } from 'node:test';
import type { ExecutionId, ProjectId, Snapshot, WorkstreamId } from '@halcyonic/contracts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createTestControlPlane } from '../testing/harness.ts';

const PROJECTS = 30;
const TASKS = 200;

/** The mock runtime's scenarios that play a first round on their own, finishing or stopping to wait. */
const SCENARIOS = [
  'successful_feature',
  'failing_tests',
  'approval_required',
  'question_asked',
  'order_history_pagination',
  'order_confirmation_email',
  'sign_in_rate_limit',
  'runtime_error',
];

/** The largest message the headset's client takes (`LoopbackWebSocketTransport.DefaultMaxMessageBytes`). */
const HEADSET_MESSAGE_LIMIT = 16 * 1024 * 1024;

const directory = mkdtempSync(join(tmpdir(), 'halcyonic-snapshot-scale-'));
after(() => rmSync(directory, { recursive: true, force: true }));

/** The least of several timings of the same work, in milliseconds: other work on the machine only ever adds. */
function least(times: number, work: () => void): number {
  let best = Number.POSITIVE_INFINITY;
  for (let index = 0; index < times; index += 1) {
    const start = performance.now();
    work();
    best = Math.min(best, performance.now() - start);
  }
  return best;
}

test('a snapshot of 30 projects and 200 tasks, each after its first round, fits what the headset takes', async () => {
  const path = join(directory, 'journal.sqlite');
  const harness = createTestControlPlane({ path });
  const { controlPlane, commands, time } = harness;
  const template = DEMO_WORKSTREAMS[0];
  assert.ok(template);

  const projects: ProjectId[] = [];
  for (let index = 0; index < PROJECTS; index += 1) {
    const created = controlPlane.commands.submit(
      commands.createProject(`Project ${index + 1}`),
      'internal',
    );
    if (created.command?.result?.kind !== 'project_created') throw new Error('no project');
    projects.push(created.command.result.project_id);
  }
  for (let index = 0; index < TASKS; index += 1) {
    const project = projects[index % PROJECTS];
    assert.ok(project);
    const task = {
      ...template,
      title: `Task ${index + 1}: ${template.title}`,
      scenario: SCENARIOS[index % SCENARIOS.length] ?? 'successful_feature',
    };
    const created = controlPlane.commands.submit(
      commands.createWorkstream(project, task),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no task');
    const started = controlPlane.commands.submit(
      commands.startExecution(created.command.result.workstream_id, task),
      'internal',
    );
    assert.equal(started.disposition, 'accepted');
  }
  await time.runUntilIdle();

  const snapshot = controlPlane.snapshot();
  assert.equal(snapshot.projects.length, PROJECTS);
  assert.equal(snapshot.workstreams.length, TASKS);
  assert.equal(snapshot.executions.length, TASKS);
  const events = controlPlane.projection.position;

  // What the headset receives on connecting: the snapshot in its realtime message.
  const message = JSON.stringify({ type: 'snapshot', snapshot });
  const bytes = Buffer.byteLength(message, 'utf8');
  const built = least(20, () => controlPlane.snapshot());
  const serialized = least(20, () =>
    JSON.stringify({ type: 'snapshot', snapshot: controlPlane.snapshot() }),
  );
  const parsed = least(20, () => JSON.parse(message));
  await controlPlane.close();

  // A restart rebuilds every projection from the journal before it serves anyone.
  let rebuilt = Number.POSITIVE_INFINITY;
  for (let index = 0; index < 3; index += 1) {
    const start = performance.now();
    const again = createTestControlPlane({ path });
    rebuilt = Math.min(rebuilt, performance.now() - start);
    assert.equal(again.controlPlane.snapshot().workstreams.length, TASKS);
    await again.controlPlane.close();
  }

  const kib = (count: number) => `${(count / 1024).toFixed(0)} KiB`;
  const ms = (value: number) => `${value.toFixed(1)} ms`;
  console.log(
    `snapshot at scale: ${PROJECTS} projects, ${TASKS} tasks, ${events} events in the journal; ` +
      `the snapshot message ${kib(bytes)} (${kib(Buffer.byteLength(JSON.stringify(snapshot.executions)))} of it executions), ` +
      `built in ${ms(built)}, built and serialized in ${ms(serialized)}, parsed by Node in ${ms(parsed)}; ` +
      `the journal rebuilt in ${ms(rebuilt)}.`,
  );

  // The figures are recorded in docs/internal/validation/quest-3-performance.md; the size is a
  // property of the data, not the machine, so it is the one held here, with room for growth.
  assert.ok(
    bytes < HEADSET_MESSAGE_LIMIT / 8,
    `the snapshot message is ${kib(bytes)}, more than an eighth of the headset's ${kib(HEADSET_MESSAGE_LIMIT)}`,
  );
});

/** One task in a project of its own, started with `instruction` and played to rest by the mock runtime. */
async function startTask(
  harness: ReturnType<typeof createTestControlPlane>,
  instruction: string,
): Promise<{ workstreamId: WorkstreamId; executionId: ExecutionId }> {
  const { controlPlane, commands, time } = harness;
  const template = DEMO_WORKSTREAMS[0];
  assert.ok(template);
  const task = { ...template, instruction, scenario: 'successful_feature' };
  const project = controlPlane.commands.submit(commands.createProject('Waves'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const created = controlPlane.commands.submit(
    commands.createWorkstream(project.command.result.project_id, task),
    'internal',
  );
  if (created.command?.result?.kind !== 'workstream_created') throw new Error('no task');
  const workstreamId = created.command.result.workstream_id;
  const started = controlPlane.commands.submit(
    commands.startExecution(workstreamId, task),
    'internal',
  );
  assert.equal(started.disposition, 'accepted');
  await time.runUntilIdle();
  const executionId = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
  assert.ok(executionId);
  return { workstreamId, executionId };
}

/** What one task adds to a snapshot: its workstream's and executions' views, serialized. */
function shareOf(snapshot: Snapshot, workstreamId: WorkstreamId): number {
  const workstreams = snapshot.workstreams.filter((view) => view.workstream_id === workstreamId);
  const executions = snapshot.executions.filter((view) => view.workstream_id === workstreamId);
  return Buffer.byteLength(JSON.stringify([workstreams, executions]), 'utf8');
}

test("a task's share of the snapshot does not grow with its rounds (U6)", async () => {
  const harness = createTestControlPlane();
  const { controlPlane, commands, time } = harness;
  const { workstreamId, executionId } = await startTask(harness, 'Make the waves harder.');
  const first = shareOf(controlPlane.snapshot(), workstreamId);
  for (let round = 0; round < 40; round += 1) {
    const sent = controlPlane.commands.submit(
      commands.instruct(executionId, `Wave ${round + 2}, harder still.`),
      'internal',
    );
    assert.equal(sent.disposition, 'accepted');
    await time.runUntilIdle();
  }
  assert.equal(controlPlane.projection.execution(executionId)?.turn_count, 41);

  // A round adds to the journal and to history, never to the views: only counts and times change.
  const later = shareOf(controlPlane.snapshot(), workstreamId);
  assert.ok(
    Math.abs(later - first) <= 16,
    `the task's share went from ${first} to ${later} bytes over 40 rounds`,
  );
  await controlPlane.close();
});

test('at the longest instruction every event of its task carries it whole, and some 500 such tasks fill a snapshot (U6)', async () => {
  const harness = createTestControlPlane();
  const { controlPlane } = harness;
  const tools: number[] = [];
  const stop = controlPlane.publisher.subscribe((published) => {
    if (published.event.event_type !== 'runtime.tool.started') return;
    const message = {
      type: 'event',
      position: published.position,
      event: published.event,
      changes: published.changes,
    };
    tools.push(Buffer.byteLength(JSON.stringify(message), 'utf8'));
  });
  const longest = 'Make it harder. '.repeat(2000);
  assert.equal(longest.length, 32_000);
  const { workstreamId } = await startTask(harness, longest);
  stop();

  // The execution view rides with every event that changes it, its instruction whole: about
  // 34 KiB a tool's start at this length, 2.5 KiB at a 600-character one. Kept for now (U6, X4);
  // the risk is in the scale record.
  assert.ok(tools.length > 0);
  for (const bytes of tools)
    assert.ok(bytes > 32_000 && bytes < 32_000 + 4096, `a tool's start is ${bytes} bytes`);
  // 33,318 bytes on 2026-11-03: 503 such tasks fill it.
  const share = shareOf(controlPlane.snapshot(), workstreamId);
  assert.ok(
    Math.floor(HEADSET_MESSAGE_LIMIT / share) >= 480,
    `a task's share is ${share} bytes, so ${Math.floor(HEADSET_MESSAGE_LIMIT / share)} fill what the headset takes`,
  );
  await controlPlane.close();
});
