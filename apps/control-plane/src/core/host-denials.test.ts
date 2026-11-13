import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  ExecutionId,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { createTestControlPlane } from '../testing/harness.ts';
import { APPROVABLE_LIMIT, controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import { tooLongWords } from './host-denials.ts';

/** An agent app that can be sent decisions, declaring whether a denial's words reach its agent. */
function runtime(delivers: boolean): RuntimeAdapter {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'app' as RuntimeId,
    kind: 'stub',
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
    ...(delivers && { delivers_denial_message: true }),
  };
  return {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async () => ({ native_id: 'n' }),
    respondToApproval: async () => {},
    close: async () => {},
  };
}

async function run(delivers: boolean, confined: boolean) {
  const harness = createTestControlPlane({ adapters: () => [runtime(delivers)] });
  const { controlPlane, commands, time } = harness;
  const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const now = '2026-09-26T10:00:00.000Z' as never;
  const scope = {
    project_id: project.command.result.project_id as ProjectId,
    workstream_id: '01a19000-0000-7000-8000-0000000000d1' as WorkstreamId,
    execution_id: '01a19000-0000-7000-8000-0000000000e1' as ExecutionId,
  };
  controlPlane.recorder.record(
    controlPlaneDraft(
      'workstream.created',
      { ...scope, execution_id: null },
      { title: 'Build', objective: null },
      now,
      NO_CAUSE,
    ),
  );
  controlPlane.recorder.record(
    controlPlaneDraft(
      'execution.created',
      scope,
      {
        runtime: { runtime_id: 'app', kind: 'stub', display_name: 'App', synthetic: true },
        instruction: 'Build it.',
        directory: null,
      } as never,
      now,
      NO_CAUSE,
    ),
  );
  const requests = [
    { approval_id: 'short', summary: 'npx tsc --noEmit', complete: true },
    {
      approval_id: 'long',
      summary: `node -e "${'x'.repeat(5000)}"`,
      complete: false,
      length: 5011,
    },
  ];
  let sequence = 0;
  for (const [type, payload] of [
    ['runtime.execution.started', { native_id: 'n', confined }],
    ['runtime.turn.started', { turn_id: 't1' }],
    ...requests.map((each) => [
      'runtime.approval.requested',
      {
        approval_id: each.approval_id,
        subject: { kind: 'tool_use', tool_name: 'Bash', summary: each.summary },
        complete: each.complete,
        ...(each.length !== undefined && { too_long: { length: each.length } }),
      },
    ]),
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
  const events = [...controlPlane.journal.readAll()].map((stored) => stored.event);
  await controlPlane.close();
  const denied = events.filter((event) => event.event_type === 'execution.approval.denied_by_host');
  const sent = events.flatMap((event) => {
    if (event.event_type !== 'command.accepted') return [];
    const { command } = event.payload;
    if (command.command_type !== 'execution.respond_to_approval') return [];
    return [{ ...command.payload, at: events.indexOf(event) }];
  });
  return { events, denied, sent };
}

describe('a request too long to show a person whole is denied by the host, in every run (AUDIT3, 2026-11-13)', () => {
  for (const confined of [true, false]) {
    test(`recorded first, then the same deny a press sends, with words the agent can act on (${confined ? 'confined' : 'not confined'})`, async () => {
      const { events, denied, sent } = await run(true, confined);
      assert.equal(denied.length, 1);
      const first = denied[0];
      assert.ok(first?.event_type === 'execution.approval.denied_by_host');
      assert.deepEqual(first.payload, {
        approval_id: 'long',
        because: 'too_long_to_show',
        length: 5011,
        delivered: true,
      });
      assert.deepEqual(
        sent.map(({ approval_id, decision, message }) => ({ approval_id, decision, message })),
        [{ approval_id: 'long', decision: 'deny', message: tooLongWords(5011) }],
      );
      assert.ok(events.indexOf(first) < (sent[0]?.at ?? -1), 'recorded before the deny');
    });
  }

  test("where the agent's app carries a denial's words nowhere, the record says it was not told", async () => {
    const { denied } = await run(false, true);
    const first = denied[0];
    assert.ok(first?.event_type === 'execution.approval.denied_by_host');
    assert.equal(first.payload.delivered, false);
  });

  test('the words say why, name the bound and suggest no way round it (the lead, 19:46Z)', () => {
    const words = tooLongWords(5011);
    assert.match(words, /5011 characters/);
    assert.ok(
      words.includes(`a person can be shown at most ${APPROVABLE_LIMIT} whole, so it was not done`),
    );
    // No road round the guard while a write's approval shows only its path.
    assert.doesNotMatch(words, /file|script|several|Send it/);
    assert.doesNotMatch(words, /Halcyonic/, 'no product name in what the agent reads');
  });
});
