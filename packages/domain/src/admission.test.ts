import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeId,
} from '@halcyonic/contracts';
import { admitCommand, journaledRejection, type RuntimeCatalog } from './admission.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

const FULL: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: false,
  respond_to_approval: true,
  answer_question: false,
  interrupt: true,
};

function catalog(
  capabilities: RuntimeCapabilities = FULL,
  modelChoice: RuntimeDescriptor['model_choice'] = 'none',
  usesProjectLocation = false,
): RuntimeCatalog {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'mock' as RuntimeId,
    kind: 'mock',
    display_name: 'Mock',
    synthetic: true,
    capabilities,
    model_choice: modelChoice,
    uses_project_location: usesProjectLocation,
  };
  return { get: (id) => (id === 'mock' ? descriptor : undefined) };
}

function setup() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project();
  const workstream = b.workstream(project.projectId);
  const execution = b.execution(project.projectId, workstream.workstreamId);
  for (const stored of [project.event, workstream.event, execution.event]) projection.apply(stored);
  const scope = {
    projectId: project.projectId,
    workstreamId: workstream.workstreamId,
    executionId: execution.executionId,
  };
  let counter = 0;
  const base = () => {
    counter += 1;
    return {
      schema_version: 1 as const,
      command_id: `00000000-0000-4000-8000-${String(counter).padStart(12, '0')}` as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
    };
  };
  const commands = {
    instruct: (): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.send_instruction',
      payload: { execution_id: execution.executionId, text: 'Continue.' },
    }),
    interrupt: (): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.interrupt',
      payload: { execution_id: execution.executionId },
    }),
    approve: (approvalId: string): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.respond_to_approval',
      payload: {
        execution_id: execution.executionId,
        approval_id: approvalId,
        decision: 'approve',
        message: null,
      },
    }),
    start: (workstreamId: string, runtimeId = 'mock'): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.start',
      payload: {
        workstream_id: workstreamId as never,
        runtime_id: runtimeId as RuntimeId,
        instruction: 'Go.',
        options: {},
        model_ref: null,
      },
    }),
  };
  return { b, projection, scope, commands, workstream };
}

describe('command admission', () => {
  test('starting on a known workstream with a capable runtime is admitted as low consequence', () => {
    const { projection, commands, workstream } = setup();
    const admission = admitCommand(commands.start(workstream.workstreamId), projection, catalog());
    assert.equal(admission.admitted, true);
    assert.equal(admission.admitted && admission.policy, 'low_consequence');
    assert.equal(admission.scope.workstream_id, workstream.workstreamId);
  });

  test('a chosen model is admitted only for a runtime that lists its models', () => {
    const { projection, commands, workstream } = setup();
    const start = commands.start(workstream.workstreamId);
    const choosing = {
      ...start,
      payload: { ...(start.payload as object), model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4' },
    } as typeof start;
    const refused = admitCommand(choosing, projection, catalog());
    assert.deepEqual(refused.admitted ? null : refused.rejection, {
      code: 'capability_unsupported',
      message: 'Runtime mock does not offer a choice of model.',
    });
    const admitted = admitCommand(choosing, projection, catalog(FULL, 'listed'));
    // Whether the model is still listed is the runtime's to check, at the start itself.
    assert.equal(admitted.admitted, true);
  });

  test('a runtime that lists its models never chooses one by itself: a start must name one', () => {
    const { projection, commands, workstream } = setup();
    const refused = admitCommand(
      commands.start(workstream.workstreamId),
      projection,
      catalog(FULL, 'listed'),
    );
    assert.deepEqual(refused.admitted ? null : refused.rejection, {
      code: 'model_required',
      message: 'Choose a model: this runtime lists the models it can use.',
    });
    // A runtime without a list chooses its own model, as before.
    assert.equal(
      admitCommand(commands.start(workstream.workstreamId), projection, catalog()).admitted,
      true,
    );
  });

  test('a runtime that works in the project folder is refused for a project without one', () => {
    const { b, projection, commands, workstream } = setup();
    const refused = admitCommand(
      commands.start(workstream.workstreamId),
      projection,
      catalog(FULL, 'none', true),
    );
    assert.equal(refused.admitted ? null : refused.rejection.code, 'location_required');
    assert.equal(refused.scope.workstream_id, workstream.workstreamId);
    // A runtime that uses no folder starts anywhere, as before.
    assert.equal(
      admitCommand(commands.start(workstream.workstreamId), projection, catalog()).admitted,
      true,
    );

    const located = b.project('Located', { path: '/work/app', name: 'app', created: false });
    const inside = b.workstream(located.projectId);
    projection.apply(located.event);
    projection.apply(inside.event);
    const admitted = admitCommand(
      commands.start(inside.workstreamId),
      projection,
      catalog(FULL, 'none', true),
    );
    // The folder itself is the host's to check; the domain knows only that there is one.
    assert.equal(admitted.admitted, true);
  });

  test('setting a location is admitted only for a known project', () => {
    const { projection, scope } = setup();
    const setLocation = (projectId: string): CommandEnvelope => ({
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000aa' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'project.set_location',
      payload: {
        project_id: projectId as never,
        location: { kind: 'existing_folder', root: '/work', folder_name: 'app' },
      },
    });
    const admitted = admitCommand(setLocation(scope.projectId), projection, catalog());
    assert.equal(admitted.admitted && admitted.policy, 'low_consequence');
    assert.equal(admitted.scope.project_id, scope.projectId);
    const missing = admitCommand(
      setLocation('01920000-0000-7000-8000-00000000ffff'),
      projection,
      catalog(),
    );
    assert.equal(missing.admitted ? null : missing.rejection.code, 'project_not_found');
  });

  test('unknown targets and runtimes are rejected with the scope that did resolve', () => {
    const { projection, commands, workstream } = setup();
    const missing = admitCommand(
      commands.start('01920000-0000-7000-8000-00000000ffff'),
      projection,
      catalog(),
    );
    assert.deepEqual(missing.admitted ? null : missing.rejection.code, 'workstream_not_found');
    const noRuntime = admitCommand(
      commands.start(workstream.workstreamId, 'opencode'),
      projection,
      catalog(),
    );
    assert.equal(noRuntime.admitted ? null : noRuntime.rejection.code, 'runtime_not_found');
    assert.equal(noRuntime.scope.workstream_id, workstream.workstreamId);
  });

  test('a declared-unsupported capability is rejected, never emulated', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    const admission = admitCommand(commands.instruct(), projection, catalog());
    assert.equal(admission.admitted ? null : admission.rejection.code, 'capability_unsupported');
  });

  test('instructions are admitted at rest when supported, and refused before any session exists', () => {
    const { b, projection, scope, commands } = setup();
    const early = admitCommand(commands.instruct(), projection, catalog());
    assert.equal(early.admitted ? null : early.rejection.code, 'invalid_state');
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(admitCommand(commands.instruct(), projection, catalog()).admitted, true);
  });

  test('interrupt needs a running turn and is review required', () => {
    const { b, projection, scope, commands } = setup();
    const atRest = admitCommand(commands.interrupt(), projection, catalog());
    assert.equal(atRest.admitted ? null : atRest.rejection.code, 'invalid_state');
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    const running = admitCommand(commands.interrupt(), projection, catalog());
    assert.equal(running.admitted && running.policy, 'review_required');
    const unsupported = admitCommand(
      commands.interrupt(),
      projection,
      catalog({ ...FULL, interrupt: false }),
    );
    assert.equal(
      unsupported.admitted ? null : unsupported.rejection.code,
      'capability_unsupported',
    );
  });

  test('approvals must be pending on the execution', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    const none = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(none.admitted ? null : none.rejection.code, 'approval_not_found');
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
      }),
    );
    const pending = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(pending.admitted && pending.policy, 'review_required');
  });

  test('an approval on an unobservable execution cannot be answered', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
      }),
    );
    projection.apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Gone.' }));
    const admission = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(admission.admitted ? null : admission.rejection.code, 'invalid_state');
  });
});

describe('answering a question', () => {
  const ANSWERS: RuntimeCapabilities = { ...FULL, answer_question: true };
  const prompts = [
    {
      key: 'q0',
      header: 'Colour',
      text: 'Which colour?',
      options: [
        { label: 'red', description: null },
        { label: 'blue', description: null },
      ],
      multiple: false,
      free_text: true,
      secret: false,
    },
    {
      key: 'q1',
      header: 'Pages',
      text: 'Which pages?',
      options: [
        { label: 'Orders', description: null },
        { label: 'Settings', description: null },
      ],
      multiple: true,
      free_text: false,
      secret: false,
    },
  ];
  const answer = (answers: unknown, questionId = 'frm_1'): CommandEnvelope =>
    ({
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000b1' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'execution.answer_question',
      payload: { execution_id: undefined as never, question_id: questionId, answers },
    }) as CommandEnvelope;
  function asked(answerable = true, asking = prompts) {
    const context = setup();
    const { b, projection, scope } = context;
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.question.asked', {
        question_id: 'frm_1',
        prompts: asking,
        answerable,
      }),
    );
    const command = (answers: unknown, questionId?: string) => {
      const envelope = answer(answers, questionId);
      (envelope.payload as { execution_id: string }).execution_id = scope.executionId;
      return envelope;
    };
    return { ...context, command };
  }
  const good = [
    { key: 'q0', selected: ['blue'], text: null },
    { key: 'q1', selected: ['Orders', 'Settings'], text: null },
  ];

  test('answers that fit the questions are admitted as low consequence', () => {
    const { projection, command } = asked();
    const admission = admitCommand(command(good), projection, catalog(ANSWERS));
    assert.equal(admission.admitted && admission.policy, 'low_consequence');
    const typed = admitCommand(
      command([
        { key: 'q0', selected: [], text: 'green, as on the poster' },
        { key: 'q1', selected: ['Orders'], text: null },
      ]),
      projection,
      catalog(ANSWERS),
    );
    assert.equal(typed.admitted, true);
  });

  test('a runtime that cannot carry answers, or a question it cannot answer, is refused', () => {
    const { projection, command } = asked();
    const unsupported = admitCommand(command(good), projection, catalog(FULL));
    assert.equal(
      unsupported.admitted ? null : unsupported.rejection.code,
      'capability_unsupported',
    );
    const notHere = asked(false);
    const refused = admitCommand(notHere.command(good), notHere.projection, catalog(ANSWERS));
    assert.equal(refused.admitted ? null : refused.rejection.code, 'capability_unsupported');
    assert.match(refused.admitted ? '' : refused.rejection.message, /Stop the turn/);
  });

  test('a question asking for a secret is refused even if an adapter called it answerable', () => {
    const secret = asked(true, [
      prompts[0] as (typeof prompts)[0],
      { ...(prompts[1] as (typeof prompts)[0]), secret: true },
    ]);
    const admission = admitCommand(secret.command(good), secret.projection, catalog(ANSWERS));
    assert.equal(admission.admitted ? null : admission.rejection.code, 'capability_unsupported');
    assert.match(admission.admitted ? '' : admission.rejection.message, /secret/);
  });

  test('a refused answer is journaled with its keys only', () => {
    const { command } = asked();
    const typed = command([
      { key: 'q0', selected: [], text: 'hunter2' },
      { key: 'q1', selected: ['Orders'], text: null },
    ]);
    const journaled = journaledRejection(typed);
    assert.deepEqual(
      journaled.command_type === 'execution.answer_question' && journaled.payload.answers,
      [
        { key: 'q0', selected: [], text: null },
        { key: 'q1', selected: [], text: null },
      ],
    );
    assert.equal(JSON.stringify(journaled).includes('hunter2'), false);
    // Any other command is journaled whole.
    const interrupt = {
      ...typed,
      command_type: 'execution.interrupt',
      payload: { execution_id: (typed.payload as { execution_id: string }).execution_id },
    } as CommandEnvelope;
    assert.equal(journaledRejection(interrupt), interrupt);
  });

  test('a question that is not pending is not found', () => {
    const { projection, command } = asked();
    const missing = admitCommand(command(good, 'frm_other'), projection, catalog(ANSWERS));
    assert.equal(missing.admitted ? null : missing.rejection.code, 'question_not_found');
  });

  test('answers that do not fit the questions are refused, each with its reason', () => {
    const { projection, command } = asked();
    const cases: [unknown, RegExp][] = [
      [[good[0]], /Every question/],
      [[...good, { key: 'q9', selected: ['x'], text: null }], /does not ask/],
      [[good[0], good[0], good[1]], /answered twice/],
      [[{ key: 'q0', selected: ['green'], text: null }, good[1]], /does not offer/],
      [[{ key: 'q0', selected: ['red', 'blue'], text: null }, good[1]], /takes one/],
      [[good[0], { key: 'q1', selected: ['Orders'], text: 'and more' }], /only its options/],
      [[{ key: 'q0', selected: [], text: null }, good[1]], /unanswered/],
      [[{ key: 'q0', selected: ['red'], text: 'also blue' }, good[1]], /both chooses/],
      [[good[0], { key: 'q1', selected: ['Orders', 'Orders'], text: null }], /same option twice/],
    ];
    for (const [answers, reason] of cases) {
      const admission = admitCommand(command(answers), projection, catalog(ANSWERS));
      assert.equal(admission.admitted ? null : admission.rejection.code, 'invalid_answer');
      assert.match(admission.admitted ? '' : admission.rejection.message, reason);
    }
  });

  test('a question asked outside a turn can still be stopped, which withdraws it', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.question.asked', {
        question_id: 'req-late',
        prompts,
        answerable: false,
      }),
    );
    assert.equal(projection.executionFacts(scope.executionId)?.status, 'waiting_for_human');
    const stop = admitCommand(commands.interrupt(), projection, catalog(ANSWERS));
    assert.equal(stop.admitted, true);
    projection.apply(
      b.runtimeEvent(scope, 'runtime.question.resolved', {
        question_id: 'req-late',
        outcome: 'dismissed',
      }),
    );
    assert.equal(projection.executionFacts(scope.executionId)?.status, 'completed');
  });

  test('a question on an unobservable execution cannot be answered', () => {
    const { b, projection, scope, command } = asked();
    projection.apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Gone.' }));
    const admission = admitCommand(command(good), projection, catalog(ANSWERS));
    assert.equal(admission.admitted ? null : admission.rejection.code, 'invalid_state');
  });
});
