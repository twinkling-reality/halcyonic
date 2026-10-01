import type {
  CommandEnvelope,
  CommandRejection,
  ExecutionId,
  PolicyCategory,
  ProjectId,
  QuestionAnswer,
  QuestionPrompt,
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

    case 'project.set_location': {
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
      // A runtime that lists its models never chooses one by itself, where it could pick a
      // hosted model nobody chose.
      if (command.payload.model_ref === null && runtime.model_choice === 'listed') {
        return reject(
          scope,
          'model_required',
          'Choose a model: this runtime lists the models it can use.',
        );
      }
      const location = projection.project(workstream.project_id)?.location ?? null;
      if (runtime.uses_project_location && location === null) {
        return reject(
          scope,
          'location_required',
          `Runtime ${runtime.runtime_id} works in the project's folder, and the project has none. Choose a folder for the project first.`,
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

    case 'execution.answer_question': {
      const target = resolveExecution(command.payload.execution_id, projection, runtimes);
      if (!target.found) return target.rejection;
      const { facts, runtime, scope } = target;
      const missing = requireCapability(runtime, 'answer_question', scope);
      if (missing !== undefined) return missing;
      const question = facts.pendingQuestions.find(
        (pending) => pending.question_id === command.payload.question_id,
      );
      if (question === undefined) {
        return reject(
          scope,
          'question_not_found',
          `Question ${command.payload.question_id} is not shown for an answer on this execution: it was resolved, or waits behind the questions shown.`,
        );
      }
      // A secret is never carried, whatever the adapter said (ADR 0022).
      if (question.prompts.some((prompt) => prompt.secret)) {
        return reject(
          scope,
          'capability_unsupported',
          'This question asks for something secret, which Halcyonic never sends. Stop the turn to go on.',
        );
      }
      if (!question.answerable) {
        return reject(
          scope,
          'capability_unsupported',
          'This question cannot be answered through Halcyonic. Stop the turn to go on.',
        );
      }
      if (facts.status !== 'waiting_for_human') {
        return reject(
          scope,
          'invalid_state',
          `The execution is ${facts.status}; the question cannot be answered now.`,
        );
      }
      const problem = answerProblem(question.prompts, command.payload.answers);
      if (problem !== null) return reject(scope, 'invalid_answer', problem);
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

/**
 * Why answers do not fit the questions they answer, or null when they do: one answer per question,
 * each choosing only offered labels, one unless several are allowed, and typed text only where the
 * question takes it.
 */
/**
 * A rejected command as it is journaled. The journal is the audit record and goes to every client,
 * so a refused answer keeps only the keys it named: what was chosen or typed may be what the
 * question should never have received, such as a secret.
 */
export function journaledRejection(command: CommandEnvelope): CommandEnvelope {
  if (command.command_type !== 'execution.answer_question') return command;
  return {
    ...command,
    payload: {
      ...command.payload,
      answers: command.payload.answers.map((answer) => ({
        key: answer.key,
        selected: [],
        text: null,
      })),
    },
  };
}

export function answerProblem(
  prompts: readonly QuestionPrompt[],
  answers: readonly QuestionAnswer[],
): string | null {
  const byKey = new Map(prompts.map((prompt) => [prompt.key, prompt]));
  const answered = new Set<string>();
  for (const answer of answers) {
    const prompt = byKey.get(answer.key);
    if (prompt === undefined) return 'An answer names a question this request does not ask.';
    if (answered.has(answer.key)) return 'A question is answered twice.';
    answered.add(answer.key);
    const labels = new Set(prompt.options.map((option) => option.label));
    if (answer.selected.some((label) => !labels.has(label))) {
      return 'An answer chooses an option the question does not offer.';
    }
    if (new Set(answer.selected).size !== answer.selected.length) {
      return 'An answer chooses the same option twice.';
    }
    if (!prompt.multiple && answer.selected.length > 1) {
      return 'An answer chooses several options for a question that takes one.';
    }
    if (answer.text !== null && !prompt.free_text) {
      return 'An answer types text for a question that takes only its options.';
    }
    if (answer.selected.length === 0 && answer.text === null) {
      return 'An answer leaves a question unanswered.';
    }
    if (!prompt.multiple && answer.selected.length === 1 && answer.text !== null) {
      return 'An answer both chooses an option and types text for a question that takes one answer.';
    }
  }
  if (answered.size !== prompts.length) return 'Every question of the request must be answered.';
  return null;
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
