import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { ExecutionId, ProjectId, RuntimeId, WorkstreamId } from '@halcyonic/contracts';
import type {
  ExecutionContext,
  RuntimeAdapter,
  StartExecutionRequest,
  VirtualTime,
} from '@halcyonic/runtime-core';
import { createTestControlPlane } from '../testing/harness.ts';

/**
 * Several projects worked on at once, each by its own agent app (the owner's ask, lead 2026-11-17T18:51Z), on the
 * control plane: every run is its own, so an answer, a stop or a refusal reaches only the run it names (AUDIT3's MP1
 * and MP4).
 */

interface Calls {
  readonly answered: { execution: ExecutionContext; approval_id: string }[];
  readonly stopped: ExecutionContext[];
}

/** An agent app of `kind` that asks once for each run it starts and waits, keeping what it is sent. */
function askingApp(kind: string, time: VirtualTime, calls: Calls): RuntimeAdapter {
  const runtimeId = `${kind}-side` as RuntimeId;
  const runs = new Map<string, StartExecutionRequest['emit']>();
  let seq = 0;
  const send = (emit: StartExecutionRequest['emit'], type: string, payload: unknown) => {
    seq += 1;
    emit({
      native_event_id: `${kind}:${String(seq)}`,
      sequence: seq,
      occurred_at: time.now().toISOString() as never,
      type: type as never,
      payload: payload as never,
      provenance: { epistemic: 'observed', native_type: `${kind}/${type}` },
    });
  };
  return {
    descriptor: {
      runtime_id: runtimeId,
      kind,
      display_name: `${kind} side by side`,
      synthetic: true,
      capabilities: {
        start_execution: true,
        instruct_at_rest: false,
        instruct_while_running: false,
        respond_to_approval: true,
        answer_question: false,
        interrupt: true,
      },
      model_choice: 'none',
      uses_project_location: false,
      reports_tool_activity: false,
    },
    validateStartOptions: () => ({ ok: true }),
    async startExecution(request: StartExecutionRequest) {
      runs.set(request.execution.execution_id, request.emit);
      const id = request.execution.execution_id;
      queueMicrotask(() => {
        send(request.emit, 'runtime.execution.started', { native_id: `n-${id}` });
        send(request.emit, 'runtime.turn.started', { turn_id: `t-${id}` });
        send(request.emit, 'runtime.approval.requested', {
          approval_id: `ask-${kind}`,
          subject: { kind: 'tool_use', tool_name: 'shell', summary: `[${kind}] make test` },
          complete: true,
        });
      });
      return { native_id: `n-${id}` };
    },
    async respondToApproval(request) {
      calls.answered.push({ execution: request.execution, approval_id: request.approval_id });
      const emit = runs.get(request.execution.execution_id);
      if (emit !== undefined)
        send(emit, 'runtime.approval.resolved', {
          approval_id: request.approval_id,
          decision: request.decision === 'approve' ? 'approved' : 'denied',
        });
    },
    async interrupt(request) {
      calls.stopped.push(request.execution);
    },
    close: async () => {},
  };
}

describe('three projects worked on at once, each by its own agent app', () => {
  const KINDS = ['claude-agent', 'codex', 'opencode'] as const;

  async function threeAtOnce() {
    const calls: Calls = { answered: [], stopped: [] };
    const harness = createTestControlPlane({
      adapters: (time) => KINDS.map((kind) => askingApp(kind, time, calls)),
    });
    const { controlPlane, commands, time } = harness;
    const runs: { kind: string; project: ProjectId; task: WorkstreamId; run: ExecutionId }[] = [];
    for (const kind of KINDS) {
      const project = controlPlane.commands.submit(
        commands.createProject(`Side ${kind}`),
        'internal',
      );
      if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
      const projectId = project.command.result.project_id as ProjectId;
      const work = {
        title: `Work on ${kind}`,
        objective: null,
        instruction: 'Make it.',
        scenario: 'x',
      };
      const task = controlPlane.commands.submit(
        commands.createWorkstream(projectId, work as never),
        'internal',
      );
      if (task.command?.result?.kind !== 'workstream_created') throw new Error('no task');
      const taskId = task.command.result.workstream_id as WorkstreamId;
      const started = controlPlane.commands.submit(
        commands.startExecution(taskId, work as never, `${kind}-side` as RuntimeId),
        'internal',
      );
      assert.equal(started.disposition, 'accepted', JSON.stringify(started.command?.rejection));
      runs.push({ kind, project: projectId, task: taskId, run: '' as ExecutionId });
    }
    await time.runUntilIdle();
    for (const each of runs) {
      const run = controlPlane.projection.workstream(each.task)?.current_execution_id;
      if (run === null || run === undefined) throw new Error(`no run for ${each.kind}`);
      each.run = run as ExecutionId;
    }
    return { ...harness, calls, runs };
  }

  const waiting = (harness: Awaited<ReturnType<typeof threeAtOnce>>, run: ExecutionId) =>
    harness.controlPlane.projection
      .execution(run)
      ?.pending_approvals.map((each) => each.approval_id) ?? [];

  test('all three run at once, each waiting on its own ask', async () => {
    const harness = await threeAtOnce();
    for (const each of harness.runs) {
      const view = harness.controlPlane.projection.execution(each.run);
      assert.equal(view?.runtime.kind, each.kind);
      assert.equal(view?.status, 'waiting_for_human', each.kind);
      assert.deepEqual(waiting(harness, each.run), [`ask-${each.kind}`]);
    }
    assert.equal(new Set(harness.runs.map((each) => each.project)).size, 3);
  });

  test('a Yes answers only the run it names; the others stay waiting (MP1)', async () => {
    const harness = await threeAtOnce();
    const [claude, codex, opencode] = harness.runs as [
      (typeof harness.runs)[number],
      (typeof harness.runs)[number],
      (typeof harness.runs)[number],
    ];
    const yes = harness.controlPlane.commands.submit(
      harness.commands.approve(codex.run, 'ask-codex'),
      'internal',
    );
    assert.equal(yes.disposition, 'accepted', JSON.stringify(yes.command?.rejection));
    await harness.time.runUntilIdle();
    assert.deepEqual(
      harness.calls.answered.map((each) => [each.execution.execution_id, each.approval_id]),
      [[codex.run, 'ask-codex']],
    );
    assert.deepEqual(waiting(harness, codex.run), []);
    assert.deepEqual(waiting(harness, claude.run), ['ask-claude-agent']);
    assert.deepEqual(waiting(harness, opencode.run), ['ask-opencode']);
  });

  test("a Yes naming one run with another run's ask is refused and reaches no app (MP1)", async () => {
    const harness = await threeAtOnce();
    const [claude, codex] = harness.runs as [
      (typeof harness.runs)[number],
      (typeof harness.runs)[number],
    ];
    const crossed = harness.controlPlane.commands.submit(
      harness.commands.approve(claude.run, 'ask-codex'),
      'internal',
    );
    assert.equal(crossed.disposition, 'rejected');
    await harness.time.runUntilIdle();
    assert.deepEqual(harness.calls.answered, []);
    assert.deepEqual(waiting(harness, claude.run), ['ask-claude-agent']);
    assert.deepEqual(waiting(harness, codex.run), ['ask-codex']);
  });

  test('stopping one run stops only its own; the others keep waiting (MP4)', async () => {
    const harness = await threeAtOnce();
    const [claude, codex, opencode] = harness.runs as [
      (typeof harness.runs)[number],
      (typeof harness.runs)[number],
      (typeof harness.runs)[number],
    ];
    const stop = harness.controlPlane.commands.submit(
      harness.commands.interrupt(opencode.run),
      'internal',
    );
    assert.equal(stop.disposition, 'accepted', JSON.stringify(stop.command?.rejection));
    await harness.time.runUntilIdle();
    assert.deepEqual(
      harness.calls.stopped.map((each) => each.execution_id),
      [opencode.run],
    );
    assert.deepEqual(waiting(harness, claude.run), ['ask-claude-agent']);
    assert.deepEqual(waiting(harness, codex.run), ['ask-codex']);
  });
});
