import type {
  ClientInfo,
  CommandId,
  CommandOf,
  ExecutionId,
  ProjectId,
  ProjectLocationChoice,
  QuestionAnswer,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import type { IdGenerator } from './ids.ts';

export interface DemoWorkstream {
  readonly title: string;
  readonly objective: string;
  readonly instruction: string;
  /** The mock runtime scenario that plays this workstream's first turn. */
  readonly scenario: string;
}

/**
 * Three workstreams that together exercise every status the XR client must present: work that
 * succeeds, work that claims completion without passing verification, and work that stops for a
 * human decision. Used by `pnpm demo` and by fixture recording.
 */
export const DEMO_PROJECT_NAME = 'Halcyonic demo';

export const DEMO_WORKSTREAMS: readonly DemoWorkstream[] = [
  {
    title: 'Add password reset',
    objective: 'Users can reset a forgotten password through an emailed, single-use link.',
    instruction: 'Implement the password reset flow with single-use email tokens, and add tests.',
    scenario: 'successful_feature',
  },
  {
    title: 'Fix flaky checkout tests',
    objective: 'The checkout suite passes reliably in CI.',
    instruction: 'Find and fix the cause of the flaky checkout tests.',
    scenario: 'failing_tests',
  },
  {
    title: 'Move sessions to their own table',
    objective: 'Sessions live in a dedicated table indexed by user id.',
    instruction: 'Write and apply a migration that moves sessions into their own table.',
    scenario: 'approval_required',
  },
];

export const MOCK_RUNTIME_ID = 'mock' as RuntimeId;

/** Builds well-formed commands the way any client would. */
export function createCommandFactory(ids: IdGenerator, clock: Clock, client: ClientInfo) {
  const base = () => ({
    schema_version: 1 as const,
    command_id: ids.next() as CommandId,
    issued_at: clock.now().toISOString(),
    client,
  });
  return {
    createProject: (
      name: string,
      location: ProjectLocationChoice | null = null,
    ): CommandOf<'project.create'> => ({
      ...base(),
      command_type: 'project.create',
      payload: { name, location },
    }),
    setLocation: (
      projectId: ProjectId,
      location: ProjectLocationChoice,
    ): CommandOf<'project.set_location'> => ({
      ...base(),
      command_type: 'project.set_location',
      payload: { project_id: projectId, location },
    }),
    createWorkstream: (
      projectId: ProjectId,
      workstream: DemoWorkstream,
    ): CommandOf<'workstream.create'> => ({
      ...base(),
      command_type: 'workstream.create',
      payload: {
        project_id: projectId,
        title: workstream.title,
        objective: workstream.objective,
      },
    }),
    startExecution: (
      workstreamId: WorkstreamId,
      workstream: DemoWorkstream,
      runtimeId: RuntimeId = MOCK_RUNTIME_ID,
    ): CommandOf<'execution.start'> => ({
      ...base(),
      command_type: 'execution.start',
      payload: {
        workstream_id: workstreamId,
        runtime_id: runtimeId,
        instruction: workstream.instruction,
        options: { scenario: workstream.scenario },
        model_ref: null,
      },
    }),
    approve: (
      executionId: ExecutionId,
      approvalId: string,
    ): CommandOf<'execution.respond_to_approval'> => ({
      ...base(),
      command_type: 'execution.respond_to_approval',
      payload: {
        execution_id: executionId,
        approval_id: approvalId,
        decision: 'approve',
        message: null,
      },
    }),
    deny: (
      executionId: ExecutionId,
      approvalId: string,
      message: string | null,
    ): CommandOf<'execution.respond_to_approval'> => ({
      ...base(),
      command_type: 'execution.respond_to_approval',
      payload: { execution_id: executionId, approval_id: approvalId, decision: 'deny', message },
    }),
    instruct: (
      executionId: ExecutionId,
      text: string,
    ): CommandOf<'execution.send_instruction'> => ({
      ...base(),
      command_type: 'execution.send_instruction',
      payload: { execution_id: executionId, text },
    }),
    answerQuestion: (
      executionId: ExecutionId,
      questionId: string,
      answers: readonly QuestionAnswer[],
    ): CommandOf<'execution.answer_question'> => ({
      ...base(),
      command_type: 'execution.answer_question',
      payload: { execution_id: executionId, question_id: questionId, answers: [...answers] },
    }),
    interrupt: (executionId: ExecutionId): CommandOf<'execution.interrupt'> => ({
      ...base(),
      command_type: 'execution.interrupt',
      payload: { execution_id: executionId },
    }),
  };
}

/** What the scripted operator does with one workstream while a trace is recorded. */
export interface OperatorScript {
  readonly approval: 'approve' | 'deny';
  /** Instructs the running turn this long after starting it; runtimes without the capability refuse. */
  readonly instructAfterMs: number | null;
  /** Interrupts the running turn this long after starting it. */
  readonly interruptAfterMs: number | null;
}

export interface TracePlan {
  /** File name under fixtures/traces. */
  readonly file: string;
  /** Seeds the identifiers, so each trace is reproducible and distinct from the others. */
  readonly seed: number;
  readonly projectName: string;
  readonly workstreams: readonly (DemoWorkstream & { readonly operator: OperatorScript })[];
}

const APPROVES: OperatorScript = {
  approval: 'approve',
  instructAfterMs: null,
  interruptAfterMs: null,
};

/**
 * The recorded traces. The first is the demo; the second holds the states a client must present
 * when work goes wrong: a failed turn, a runtime that became unreachable, an interrupted turn with
 * a refused instruction, and a denied approval.
 */
export const TRACE_PLANS: readonly TracePlan[] = [
  {
    file: 'multiple_workstreams.jsonl',
    seed: 1,
    projectName: DEMO_PROJECT_NAME,
    workstreams: DEMO_WORKSTREAMS.map((workstream) => ({ ...workstream, operator: APPROVES })),
  },
  {
    file: 'failure_modes.jsonl',
    seed: 3,
    projectName: 'Halcyonic failure modes',
    workstreams: [
      {
        title: 'Speed up the dashboard render',
        objective: 'The dashboard renders in under 100 ms.',
        instruction: 'Profile the dashboard render and remove the slowest step.',
        scenario: 'runtime_error',
        operator: APPROVES,
      },
      {
        title: 'Run the integration suite',
        objective: 'The integration suite passes on main.',
        instruction: 'Run the full integration suite and fix what fails.',
        scenario: 'agent_disconnect',
        operator: APPROVES,
      },
      {
        title: 'Refactor the session store',
        objective: 'Session reads and writes go through one interface.',
        instruction: 'Move session reads and writes behind a SessionStore interface.',
        scenario: 'successful_feature',
        operator: { approval: 'approve', instructAfterMs: 1500, interruptAfterMs: 3000 },
      },
      {
        title: 'Drop the legacy sessions table',
        objective: 'The legacy sessions table is gone.',
        instruction: 'Write and apply a migration that drops the legacy sessions table.',
        scenario: 'approval_required',
        operator: { approval: 'deny', instructAfterMs: null, interruptAfterMs: null },
      },
    ],
  },
];
