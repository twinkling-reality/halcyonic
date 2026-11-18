import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  ExecutionId,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { createTestControlPlane } from '../testing/harness.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import { INTRUSION_WORDS } from './sibling-copies.ts';

const root = mkdtempSync(join(tmpdir(), 'halcyonic-sibling-denials-'));
after(() => rmSync(root, { recursive: true, force: true }));
const home = join(root, 'home');
const tasks = join(home, '.halcyonic-tasks');
const own = join(tasks, 'garden', 'task-1');
const sibling = join(tasks, 'garden', 'task-2');
const person = join(home, 'code', 'garden');
for (const folder of [own, sibling, person]) mkdirSync(folder, { recursive: true });

/** An agent app in a task's copy that can be sent decisions and carries a denial's words to its agent. */
function runtime(): RuntimeAdapter {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'app' as RuntimeId,
    kind: 'claude-agent',
    display_name: 'App',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: true,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: false,
    reports_tool_activity: false,
    delivers_denial_message: true,
  };
  return {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async () => ({ native_id: 'n' }),
    respondToApproval: async () => {},
    close: async () => {},
  };
}

/** One run in task-1's own copy asking to read `path`, with or without the host's folders known. */
async function ask(path: string, withFolders: boolean, inOwnCopy = true) {
  const harness = createTestControlPlane({
    adapters: () => [runtime()],
    ...(withFolders && { hostFolders: { tasks, data: join(home, '.halcyonic'), home } }),
  });
  const { controlPlane, commands, time } = harness;
  const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const now = '2026-09-26T10:00:00.000Z' as never;
  const scope = {
    project_id: project.command.result.project_id as ProjectId,
    workstream_id: '01a19000-0000-7000-8000-0000000000d1' as WorkstreamId,
    execution_id: '01a19000-0000-7000-8000-0000000000e1' as ExecutionId,
  };
  const record = (type: string, payload: unknown, withExecution: boolean) =>
    controlPlane.recorder.record(
      controlPlaneDraft(
        type as never,
        (withExecution ? scope : { ...scope, execution_id: null }) as never,
        payload as never,
        now,
        NO_CAUSE,
      ),
    );
  record('workstream.created', { title: 'Build', objective: null }, false);
  record('workstream.copy_made', { directory: own, base_tree: 'a'.repeat(64), files: 3 }, false);
  record(
    'execution.created',
    {
      runtime: { runtime_id: 'app', kind: 'claude-agent', display_name: 'App', synthetic: true },
      instruction: 'Build it.',
      directory: inOwnCopy ? own : person,
    },
    true,
  );
  let sequence = 0;
  for (const [type, payload] of [
    ['runtime.execution.started', { native_id: 'n', confined: false }],
    ['runtime.turn.started', { turn_id: 't1' }],
    [
      'runtime.approval.requested',
      {
        approval_id: 'read-1',
        subject: {
          kind: 'tool_use',
          tool_name: 'Read',
          summary: path,
          paths: [{ path, access: 'read' }],
        },
        complete: true,
      },
    ],
  ] as [string, unknown][]) {
    sequence += 1;
    controlPlane.recorder.record({
      ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
      event_type: type,
      source: { kind: 'runtime', runtime_id: 'app' },
      source_native_id: `n:${sequence}`,
      sequence,
      payload,
    } as never);
  }
  await time.runUntilIdle();
  const pending = controlPlane.projection.execution(scope.execution_id)?.pending_approvals ?? [];
  const events = [...controlPlane.journal.readAll()].map((stored) => stored.event);
  await controlPlane.close();
  const denied = events.filter((event) => event.event_type === 'execution.approval.denied_by_host');
  const sent = events.flatMap((event) => {
    if (event.event_type !== 'command.accepted') return [];
    const { command } = event.payload;
    return command.command_type === 'execution.respond_to_approval' ? [command.payload] : [];
  });
  return { denied, sent, pending };
}

describe("a swarm task's read of a sibling task's copy is denied by the host, so the agent carries on (AUDIT3 HR1 to HR5)", () => {
  const theirs = join(sibling, 'src', 'index.ts');

  test('without the host knowing its folders, the read waits on a person: the stall VRIFY found', async () => {
    const { denied, sent, pending } = await ask(theirs, false);
    assert.equal(denied.length, 0);
    assert.equal(sent.length, 0);
    assert.deepEqual(
      pending.map((each) => each.approval_id),
      ['read-1'],
    );
  });

  test("recorded as the host's denial with its reason, then denied with words the agent can act on", async () => {
    const { denied, sent } = await ask(theirs, true);
    assert.equal(denied.length, 1);
    const first = denied[0];
    assert.ok(first?.event_type === 'execution.approval.denied_by_host');
    assert.deepEqual(first.payload, {
      approval_id: 'read-1',
      because: 'another_task_copy',
      delivered: true,
    });
    assert.deepEqual(
      sent.map(({ approval_id, decision, message }) => ({ approval_id, decision, message })),
      [{ approval_id: 'read-1', decision: 'deny', message: INTRUSION_WORDS.another_task_copy }],
    );
  });

  test("a read of the person's own folders still reaches the person", async () => {
    const { denied, sent, pending } = await ask(join(person, 'src', 'index.ts'), true);
    assert.equal(denied.length, 0);
    assert.equal(sent.length, 0);
    assert.equal(pending.length, 1);
  });

  test("a run that is not in its task's own copy is never judged", async () => {
    const { denied, pending } = await ask(theirs, true, false);
    assert.equal(denied.length, 0);
    assert.equal(pending.length, 1);
  });
});
