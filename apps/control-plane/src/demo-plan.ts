import type {
  ClientInfo,
  CommandId,
  CommandOf,
  ExecutionId,
  ProjectId,
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
    createProject: (name: string): CommandOf<'project.create'> => ({
      ...base(),
      command_type: 'project.create',
      payload: { name },
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
    ): CommandOf<'execution.start'> => ({
      ...base(),
      command_type: 'execution.start',
      payload: {
        workstream_id: workstreamId,
        runtime_id: MOCK_RUNTIME_ID,
        instruction: workstream.instruction,
        options: { scenario: workstream.scenario },
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
  };
}
