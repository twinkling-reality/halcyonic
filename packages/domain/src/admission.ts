import type {
  CommandEnvelope,
  CommandRejection,
  ExecutionId,
  PolicyCategory,
  ProjectId,
  RejectionCode,
  RuntimeCapabilities,
  RuntimeDescriptor,
  WorkstreamId,
} from '@halcyonic/contracts';
import { COMMAND_POLICY } from './policy.ts';
import type { ExecutionFacts, Projection } from './projection.ts';

export interface CommandScope {
  project_id: ProjectId | null;
  workstream_id: WorkstreamId | null;
  execution_id: ExecutionId | null;
}

export type Admission =
  | {
      admitted: true;
      policy: PolicyCategory;
      scope: CommandScope;
      /** The runtime the command will be dispatched to, when it targets one. */
      runtime: RuntimeDescriptor | null;
    }
  | { admitted: false; rejection: CommandRejection; scope: CommandScope };

export interface RuntimeCatalog {
  get(runtimeId: string): RuntimeDescriptor | undefined;
}

const NO_SCOPE: CommandScope = { project_id: null, workstream_id: null, execution_id: null };

/**
 * Decides whether a well-formed command may be carried out now. Pure: it reads the projection
 * and the declared runtime capabilities and changes nothing. Runtime-specific option checks
 * happen afterwards in the adapter, because only the adapter knows its options.
 */
export function admitCommand(
  command: CommandEnvelope,
  projection: Projection,
  runtimes: RuntimeCatalog,
): Admission {
  const policy = COMMAND_POLICY[command.command_type];
  switch (command.command_type) {
    case 'project.create':
      return { admitted: true, policy, scope: NO_SCOPE, runtime: null };

    case 'workstream.create': {
      const projectId = command.payload.project_id;
      if (projection.project(projectId) === undefined) {
        return reject(NO_SCOPE, 'project_not_found', `Project ${projectId} does not exist.`);
      }
      return {
        admitted: true,
        policy,
        scope: { ...NO_SCOPE, project_id: projectId },
        runtime: null,
      };
    }

    case 'execution.start': {
      const workstream = projection.workstream(command.payload.workstream_id);
      if (workstream === undefined) {
        return reject(
          NO_SCOPE,
          'workstream_not_found',
          `Workstream ${command.payload.workstream_id} does not exist.`,
        );
      }
      const scope: CommandScope = {
        project_id: workstream.project_id,
        workstream_id: workstream.workstream_id,
        execution_id: null,
      };
      const runtime = runtimes.get(command.payload.runtime_id);
      if (runtime === undefined) {
        return reject(
          scope,
          'runtime_not_found',
          `Runtime ${command.payload.runtime_id} is not registered.`,
        );
      }
      const missing = requireCapability(runtime, 'start_execution', scope);
      if (missing !== undefined) return missing;
      if (command.payload.model_ref !== null && runtime.model_choice !== 'listed') {
        return reject(
          scope,
          'capability_unsupported',
          `Runtime ${runtime.runtime_id} does not offer a choice of model.`,
        );
      }
      return { admitted: true, policy, scope, runtime };
    }

    case 'execution.send_instruction': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      if (!facts.hasNativeSession) {
        return reject(
          scope,
          'invalid_state',
          'The execution never started, so it cannot be instructed.',
        );
      }
      switch (facts.status) {
        case 'completed':
        case 'failed':
        case 'interrupted': {
          const missing = requireCapability(runtime, 'instruct_at_rest', scope);
          return missing ?? { admitted: true, policy, scope, runtime };
        }
        case 'running':
        case 'verifying':
        case 'waiting_for_human': {
          const missing = requireCapability(runtime, 'instruct_while_running', scope);
          return missing ?? { admitted: true, policy, scope, runtime };
        }
        case 'starting':
        case 'unknown':
          return reject(
            scope,
            'invalid_state',
            `The execution is ${facts.status}; instructions cannot be delivered now.`,
          );
        default: {
          const unhandled: never = facts.status;
          throw new Error(`unhandled status ${String(unhandled)}`);
        }
      }
    }

    case 'execution.respond_to_approval': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      const missing = requireCapability(runtime, 'respond_to_approval', scope);
      if (missing !== undefined) return missing;
      if (!facts.pendingApprovalIds.includes(command.payload.approval_id)) {
        return reject(
          scope,
          'approval_not_found',
          `Approval ${command.payload.approval_id} is not pending on this execution.`,
        );
      }
      if (facts.status !== 'waiting_for_human') {
        return reject(
          scope,
          'invalid_state',
          `The execution is ${facts.status}; the approval cannot be answered now.`,
        );
      }
      return { admitted: true, policy, scope, runtime };
    }

    case 'execution.interrupt': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      const missing = requireCapability(runtime, 'interrupt', scope);
      if (missing !== undefined) return missing;
      if (
        facts.status !== 'running' &&
        facts.status !== 'verifying' &&
        facts.status !== 'waiting_for_human'
      ) {
        return reject(
          scope,
          'invalid_state',
          `The execution is ${facts.status}; there is no running turn to interrupt.`,
        );
      }
      return { admitted: true, policy, scope, runtime };
    }

    default: {
      const unhandled: never = command;
      throw new Error(`unhandled command ${JSON.stringify(unhandled)}`);
    }
  }
}

type ResolvedExecution =
  | { found: true; facts: ExecutionFacts; runtime: RuntimeDescriptor; scope: CommandScope }
  | { found: false; rejection: Admission & { admitted: false } };

function resolveExecution(
  executionId: string,
  projection: Projection,
  runtimes: RuntimeCatalog,
): ResolvedExecution {
  const facts = projection.executionFacts(executionId);
  if (facts === undefined) {
    return {
      found: false,
      rejection: reject(
        NO_SCOPE,
        'execution_not_found',
        `Execution ${executionId} does not exist.`,
      ),
    };
  }
  const scope: CommandScope = {
    project_id: facts.projectId,
    workstream_id: facts.workstreamId,
    execution_id: facts.executionId,
  };
  const runtime = runtimes.get(facts.runtimeId);
  if (runtime === undefined) {
    return {
      found: false,
      rejection: reject(
        scope,
        'runtime_not_found',
        `Runtime ${facts.runtimeId} for this execution is not registered.`,
      ),
    };
  }
  return { found: true, facts, runtime, scope };
}

function requireCapability(
  runtime: RuntimeDescriptor,
  capability: keyof RuntimeCapabilities,
  scope: CommandScope,
): (Admission & { admitted: false }) | undefined {
  if (runtime.capabilities[capability]) return undefined;
  return reject(
    scope,
    'capability_unsupported',
    `Runtime ${runtime.runtime_id} does not support ${capability}.`,
  );
}

function reject(
  scope: CommandScope,
  code: RejectionCode,
  message: string,
): Admission & { admitted: false } {
  return { admitted: false, rejection: { code, message }, scope };
}
