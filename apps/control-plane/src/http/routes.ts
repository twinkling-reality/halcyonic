import {
  type CommandSubmissionResponse,
  compileValidator,
  EventsQuery,
  type EventsResponse,
  type HealthResponse,
  ProjectId,
  type ProjectsResponse,
  parseCommandEnvelope,
  type RuntimesResponse,
  type Snapshot,
  type WorkstreamsResponse,
} from '@halcyonic/contracts';
import type { FastifyInstance } from 'fastify';
import type { ControlPlane } from '../core/control-plane.ts';
import { errorBody } from './server.ts';

const validateEventsQuery = compileValidator(EventsQuery);
const validateProjectId = compileValidator(ProjectId);

/**
 * REST is for bootstrap, history and command submission. Live state flows over the WebSocket
 * at /realtime (see docs/internal/architecture/REALTIME.md).
 */
export function registerRoutes(app: FastifyInstance, controlPlane: ControlPlane): void {
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
      }),
    };
    return body;
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
    const outcome = controlPlane.commands.submit(parsed.value, 'http');
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
