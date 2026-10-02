import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { performance } from 'node:perf_hooks';
import { after, test } from 'node:test';
import type { ProjectId } from '@halcyonic/contracts';
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

/** The largest message the headset's client takes (`ClientWebSocketTransport.DefaultMaxMessageBytes`). */
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
