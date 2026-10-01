import {
  type CommandSubmissionResponse,
  compileValidator,
  DEVICE_EVENT_TYPES,
  EvaluationResponse,
  type EvaluationResult,
  EventsQuery,
  type EventsResponse,
  ExecutionId,
  type HealthResponse,
  type LocationsResponse,
  ProjectId,
  type ProjectsResponse,
  parseCommandEnvelope,
  RuntimeId,
  type RuntimeModelsResponse,
  type RuntimesResponse,
  type Snapshot,
  TRANSCRIPTION_MAX_BYTES,
  UnderstandingResponse,
  type UnderstandingResult,
  UsageLimitsResponse,
  type WorkstreamsResponse,
} from '@halcyonic/contracts';
import type { FastifyInstance } from 'fastify';
import type { ControlPlane } from '../core/control-plane.ts';
import { MODEL_LIST_TIMEOUT_MS, readRuntimeModels } from '../core/runtime-models.ts';
import type { EvaluationSource } from '../intelligence/evaluation.ts';
import type { UnderstandingSource } from '../intelligence/understanding.ts';
import type { Transcriptions } from '../speech/transcriptions.ts';
import { errorBody } from './server.ts';

const validateEventsQuery = compileValidator(EventsQuery);
const validateProjectId = compileValidator(ProjectId);
const validateExecutionId = compileValidator(ExecutionId);
const validateRuntimeId = compileValidator(RuntimeId);
const validateUnderstandingResponse = compileValidator(UnderstandingResponse);
const validateEvaluationResponse = compileValidator(EvaluationResponse);
const validateUsageLimitsResponse = compileValidator(UsageLimitsResponse);

export interface RouteSources {
  readonly understanding: UnderstandingSource;
  readonly evaluation: EvaluationSource;
  /** How long a runtime has to list its models; `MODEL_LIST_TIMEOUT_MS` by default. */
  readonly modelListTimeoutMs?: number;
  /** Speech to drafts, shared by both listeners so its bounds hold across them; absent, voice is off. */
  readonly transcriptions?: Transcriptions;
}

/**
 * REST is for bootstrap, history and command submission. Live state flows over the WebSocket
 * at /realtime (see docs/internal/architecture/REALTIME.md).
 */
export function registerRoutes(
  app: FastifyInstance,
  controlPlane: ControlPlane,
  sources: RouteSources,
): void {
  app.get('/api/health', async (): Promise<HealthResponse> => ({ status: 'ok' }));

  app.get('/api/snapshot', async (): Promise<Snapshot> => controlPlane.snapshot());

  app.get(
    '/api/projects',
    async (): Promise<ProjectsResponse> => ({
      journal: controlPlane.journal.info,
      position: controlPlane.projection.position,
      projects: controlPlane.projection.projects(),
    }),
  );

  app.get('/api/workstreams', async (request, reply) => {
    const projectId = (request.query as Record<string, unknown>).project_id;
    if (projectId !== undefined && !validateProjectId(projectId).ok) {
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'project_id must be a project identifier.'));
    }
    const body: WorkstreamsResponse = {
      journal: controlPlane.journal.info,
      position: controlPlane.projection.position,
      workstreams: controlPlane.projection.workstreams(projectId as string | undefined),
    };
    return body;
  });

  app.get(
    '/api/runtimes',
    async (): Promise<RuntimesResponse> => ({ runtimes: controlPlane.registry.descriptors() }),
  );

  // Where projects may live, read from the file system each time and never journaled. A paired
  // device sees the folder names an agent it starts could read anyway.
  app.get('/api/locations', async (): Promise<LocationsResponse> => controlPlane.locations.list());

  // Read through to the runtime's own list of models; nothing here is journaled (ADR 0016).
  app.get('/api/runtimes/:runtime_id/models', async (request, reply) => {
    const runtimeId = (request.params as { runtime_id: string }).runtime_id;
    if (!validateRuntimeId(runtimeId).ok) {
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'runtime_id must be a runtime identifier.'));
    }
    const adapter = controlPlane.registry.adapter(runtimeId);
    if (adapter === undefined) {
      return reply
        .code(404)
        .send(errorBody('runtime_not_found', `Runtime ${runtimeId} is not registered.`));
    }
    if (adapter.descriptor.model_choice !== 'listed') {
      return reply
        .code(404)
        .send(
          errorBody('models_not_listed', `Runtime ${runtimeId} does not offer a choice of model.`),
        );
    }
    const result = await readRuntimeModels(
      adapter,
      sources.modelListTimeoutMs ?? MODEL_LIST_TIMEOUT_MS,
    );
    if (result.availability === 'unavailable') {
      request.log.info(
        { runtime_id: runtimeId, reason: result.reason.code },
        'runtime models unavailable',
      );
    }
    const body: RuntimeModelsResponse = { runtime_id: adapter.descriptor.runtime_id, result };
    return body;
  });

  app.get('/api/events', async (request, reply) => {
    const query = request.query as Record<string, unknown>;
    const parsed = validateEventsQuery({
      after: toInteger(query.after, 0),
      limit: toInteger(query.limit, 200),
      workstream_id: typeof query.workstream_id === 'string' ? query.workstream_id : null,
    });
    if (!parsed.ok) {
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'The query parameters are invalid.', parsed.issues));
    }
    const body: EventsResponse = {
      journal: controlPlane.journal.info,
      head: controlPlane.journal.head(),
      events: controlPlane.journal.read({
        after: parsed.value.after,
        limit: parsed.value.limit,
        workstreamId: parsed.value.workstream_id,
        // Which devices are paired is the owner's to know: a paired device reads no device events,
        // as no realtime client receives them, and the positions they leave out are harmless gaps.
        excludeEventTypes: request.principal?.kind === 'device' ? DEVICE_EVENT_TYPES : [],
      }),
    };
    return body;
  });

  // Provider usage limits, account wide: read through on request, never journaled (ADR 0010).
  app.get('/api/usage-limits', async (request): Promise<UsageLimitsResponse> => {
    const answer: UsageLimitsResponse = (await sources.evaluation.usageLimits?.()) ?? {
      availability: 'unavailable',
      reason: { code: 'not_configured', message: 'No usage limit source is configured.' },
    };
    if (validateUsageLimitsResponse(answer).ok) return answer;
    request.log.warn('usage limits do not match the contract');
    return {
      availability: 'incompatible',
      reason: {
        code: 'invalid_usage_limits',
        message: 'The usage limit source returned data outside the contract.',
      },
    };
  });

  // Read through to the understanding provider; nothing here is journaled (ADR 0010).
  app.get('/api/executions/:execution_id/understanding', async (request, reply) => {
    const executionId = (request.params as { execution_id: string }).execution_id;
    if (!validateExecutionId(executionId).ok) {
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'execution_id must be an execution identifier.'));
    }
    const execution = controlPlane.projection.execution(executionId);
    if (execution === undefined) {
      return reply
        .code(404)
        .send(errorBody('execution_not_found', `Execution ${executionId} does not exist.`));
    }
    const result: UnderstandingResult =
      execution.native_id === null
        ? {
            availability: 'not_found',
            reason: {
              code: 'native_id_unknown',
              message: 'The runtime has not reported its session id yet.',
            },
          }
        : await sources.understanding.understand(execution.runtime.kind, execution.native_id);
    const body = { execution_id: execution.execution_id, result };
    if (validateUnderstandingResponse(body).ok) return body;
    request.log.warn({ execution_id: executionId }, 'understanding does not match the contract');
    return {
      execution_id: execution.execution_id,
      result: {
        availability: 'incompatible',
        reason: {
          code: 'invalid_understanding',
          message: 'The understanding provider returned data outside the contract.',
        },
      },
    };
  });

  // Read through to the evaluation provider; nothing here is journaled (ADR 0010).
  app.get('/api/executions/:execution_id/evaluation', async (request, reply) => {
    const executionId = (request.params as { execution_id: string }).execution_id;
    if (!validateExecutionId(executionId).ok) {
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'execution_id must be an execution identifier.'));
    }
    const execution = controlPlane.projection.execution(executionId);
    if (execution === undefined) {
      return reply
        .code(404)
        .send(errorBody('execution_not_found', `Execution ${executionId} does not exist.`));
    }
    const result: EvaluationResult =
      execution.native_id === null
        ? {
            availability: 'not_found',
            reason: {
              code: 'native_id_unknown',
              message: 'The runtime has not reported its session id yet.',
            },
          }
        : await sources.evaluation.evaluate(execution.runtime.kind, execution.native_id);
    const body = { execution_id: execution.execution_id, result };
    if (validateEvaluationResponse(body).ok) return body;
    request.log.warn({ execution_id: executionId }, 'evaluation does not match the contract');
    return {
      execution_id: execution.execution_id,
      result: {
        availability: 'incompatible',
        reason: {
          code: 'invalid_evaluation',
          message: 'The evaluation provider returned data outside the contract.',
        },
      },
    };
  });

  // The one route that takes anything but JSON (ADR 0021), in a scope of its own so no other route
  // gains a parser: one audio/wav clip, refused with 413 once it passes the limit, before more of
  // it is read. Neither the clip nor its text is logged.
  void app.register(async (scope) => {
    scope.removeAllContentTypeParsers();
    scope.addContentTypeParser(
      'audio/wav',
      { parseAs: 'buffer', bodyLimit: TRANSCRIPTION_MAX_BYTES },
      (_request, body, done) => done(null, body),
    );
    scope.post(
      '/api/transcriptions',
      { bodyLimit: TRANSCRIPTION_MAX_BYTES },
      async (request, reply) => {
        if (sources.transcriptions === undefined) {
          return reply
            .code(503)
            .send(errorBody('transcription_unavailable', 'Voice is not set up on this Mac.'));
        }
        const started = performance.now();
        const answer = await sources.transcriptions.transcribe(request.principal, request.body);
        const ms = Math.round(performance.now() - started);
        if (answer.kind === 'refused') {
          request.log.info({ code: answer.code, ms }, 'transcription refused');
          return reply.code(answer.status).send(errorBody(answer.code, answer.message));
        }
        request.log.info(
          { outcome: answer.body.outcome, audio_seconds: answer.seconds, ms },
          'transcribed a clip',
        );
        return answer.body;
      },
    );
  });

  app.post('/api/commands', async (request, reply) => {
    const parsed = parseCommandEnvelope(request.body);
    if (!parsed.ok) {
      return reply
        .code(400)
        .send(
          errorBody('invalid_command', 'The command does not match the contract.', parsed.issues),
        );
    }
    const outcome = controlPlane.commands.submit(parsed.value, 'http', request.principal);
    if (outcome.disposition === 'conflict') {
      return reply
        .code(409)
        .send(
          errorBody(
            'command_id_conflict',
            'This command id was already used for a different command.',
          ),
        );
    }
    const body: CommandSubmissionResponse = {
      disposition: outcome.disposition,
      command: outcome.command,
    };
    const status = { accepted: 202, rejected: 422, duplicate: 200 }[outcome.disposition];
    return reply.code(status).send(body);
  });
}

function toInteger(value: unknown, fallback: number): number {
  if (value === undefined) return fallback;
  if (typeof value !== 'string' || !/^\d+$/.test(value)) return Number.NaN;
  return Number(value);
}
