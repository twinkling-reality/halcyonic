import {
  CatalogResponse,
  CheckId,
  CheckpointId,
  type CheckpointsResponse,
  CheckStepOutputQuery,
  ChoicesQuery,
  type ChoicesResponse,
  COMMAND_SCHEMA_VERSION,
  CommandId,
  type CommandSubmissionResponse,
  CompanionReplyResponse,
  CompanionStatus,
  compileValidator,
  DEVICE_EVENT_TYPES,
  EvaluationResponse,
  type EvaluationResult,
  EventId,
  EventsQuery,
  type EventsResponse,
  ExecutionId,
  type GitHubPublishReviewResponse,
  type GitHubSendReviewResponse,
  type HealthResponse,
  HereQuery,
  type HereResponse,
  LocationReviewRequest,
  type LocationReviewResponse,
  type LocationsResponse,
  type OwnCheckFoundResponse,
  ProjectChangesQuery,
  ProjectFileChangeQuery,
  ProjectFileQuery,
  ProjectHistoryQuery,
  ProjectId,
  ProjectReadQuery,
  ProjectSecretSetRequest,
  type ProjectsResponse,
  type PublishedCodeResponse,
  parseCommandEnvelope,
  type RestorePreviewResponse,
  RuntimeId,
  type RuntimeModelsResponse,
  type RuntimesResponse,
  type ShareCodeResponse,
  type SharingWaysResponse,
  type Snapshot,
  type StartersResponse,
  ToolResultsQuery,
  TRANSCRIPTION_MAX_BYTES,
  UnderstandingResponse,
  type UnderstandingResult,
  UsageLimitsResponse,
  WorkspaceRequestId,
  type WorkstreamsResponse,
} from '@halcyonic/contracts';
import type { FastifyInstance } from 'fastify';
import type { FolderOrigin } from '../catalog-match.ts';
import type { Companion } from '../companion/companion.ts';
import type { ControlPlane } from '../core/control-plane.ts';
import { ProjectReads } from '../core/project-reads.ts';
import { type Redaction, withinLimit } from '../core/redaction.ts';
import { locationReview } from '../core/review.ts';
import { publishReviewOf, sendReviewOf } from '../core/reviews.ts';
import { MODEL_LIST_TIMEOUT_MS, readRuntimeModels } from '../core/runtime-models.ts';
import { ToolResults } from '../core/tool-results.ts';
import type { EvaluationSource } from '../intelligence/evaluation.ts';
import type { UnderstandingSource } from '../intelligence/understanding.ts';
import { shareCode } from '../preview/share-code.ts';
import { submitSecretSet } from '../secrets/set.ts';
import type { Transcriptions } from '../speech/transcriptions.ts';
import type { WorkspaceRequests } from '../workspace-agent/requests.ts';
import { errorBody } from './server.ts';

const validateEventsQuery = compileValidator(EventsQuery);
const validateCatalogResponse = compileValidator(CatalogResponse);
const validateProjectId = compileValidator(ProjectId);
const validateHereQuery = compileValidator(HereQuery);
const validateChoicesQuery = compileValidator(ChoicesQuery);
const validateExecutionId = compileValidator(ExecutionId);
const validateRuntimeId = compileValidator(RuntimeId);
const validateCheckpointId = compileValidator(CheckpointId);
const validateCheckId = compileValidator(CheckId);
const validateProjectChangesQuery = compileValidator(ProjectChangesQuery);
const validateProjectHistoryQuery = compileValidator(ProjectHistoryQuery);
const validateProjectFileChangeQuery = compileValidator(ProjectFileChangeQuery);
const validateProjectFileQuery = compileValidator(ProjectFileQuery);
const validateProjectReadQuery = compileValidator(ProjectReadQuery);
const validateCheckStepOutputQuery = compileValidator(CheckStepOutputQuery);
const validateWorkspaceRequestId = compileValidator(WorkspaceRequestId);
const validateWorkspaceCommandId = compileValidator(CommandId);
const validateUnderstandingResponse = compileValidator(UnderstandingResponse);
const validateEvaluationResponse = compileValidator(EvaluationResponse);
const validateUsageLimitsResponse = compileValidator(UsageLimitsResponse);
const validateCompanionStatus = compileValidator(CompanionStatus);
const validateCompanionReply = compileValidator(CompanionReplyResponse);
const validateToolResultsQuery = compileValidator(ToolResultsQuery);
const validateResultEventId = compileValidator(EventId);
const validateLocationReview = compileValidator(LocationReviewRequest);

/**
 * The largest request for a companion reply: 24,000 characters of exchange (ADR 0025), each of
 * which JSON may spell in up to six bytes, with room for the rest of the request.
 */
const COMPANION_BODY_LIMIT = 256 * 1024;

/**
 * Where the person's catalog comes from (ADR 0039): Omnorium on this computer, or the recorded
 * demonstration's stand-in. Its answer is read through and never journaled.
 */
export interface CatalogSource {
  read(): Promise<CatalogResponse>;
  /**
   * Which listed folders are clones of which repositories, by the folders' own git configuration
   * (catalog-match.ts); absent, no folder is suggested.
   */
  match?(listing: LocationsResponse): Promise<readonly FolderOrigin[]>;
}

export interface RouteSources {
  readonly understanding: UnderstandingSource;
  readonly evaluation: EvaluationSource;
  /** The person's catalog; absent, the gallery hears that none is set up. */
  readonly catalog?: CatalogSource;
  /** How long a runtime has to list its models; `MODEL_LIST_TIMEOUT_MS` by default. */
  readonly modelListTimeoutMs?: number;
  /** Speech to drafts, shared by both listeners so its bounds hold across them; absent, voice is off. */
  readonly transcriptions?: Transcriptions;
  /** Create's companion, shared by both listeners so its bounds hold across them; absent, it is off. */
  readonly companion?: Companion;
  readonly workspaceRequests?: WorkspaceRequests;
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
  const results = new ToolResults(controlPlane);
  for (const detail of [false, true]) {
    const path = `/api/executions/:execution_id/results${detail ? '/:event_id' : ''}`;
    app.get(path, async (request, reply) => {
      const params = request.params as Record<string, unknown>;
      const parsed = validateToolResultsQuery(request.query);
      if (
        !parsed.ok ||
        !validateExecutionId(params.execution_id).ok ||
        (detail && !validateResultEventId(params.event_id).ok)
      )
        return reply
          .code(400)
          .send(errorBody('invalid_request', 'The result scope or identifier is invalid.'));
      const body = detail
        ? results.read(
            params.execution_id as string,
            params.event_id as string,
            parsed.value,
            request.principal,
          )
        : results.list(params.execution_id as string, parsed.value, request.principal);
      if (!body)
        return reply
          .code(404)
          .send(
            errorBody('result_unavailable', 'Recorded results are unavailable for this selection.'),
          );
      return body;
    });
  }
  app.get('/api/workspace/commands/:command_id', async (request, reply) => {
    const id = (request.params as Record<string, unknown>).command_id;
    if (!validateWorkspaceCommandId(id).ok)
      return reply
        .code(400)
        .send(errorBody('invalid_command', 'The command identifier is invalid.'));
    const owner = controlPlane.projection.commandOwner(id as string);
    const principal = request.principal;
    const command = controlPlane.projection.command(id as string);
    const own =
      owner &&
      principal &&
      owner.kind === principal.kind &&
      (principal.kind === 'local' ||
        (owner.kind === 'device' &&
          owner.device_id === principal.device_id &&
          controlPlane.projection.device(principal.device_id)?.revoked_at === null));
    if (!own || !command?.command_type.startsWith('workspace.request.'))
      return reply
        .code(404)
        .send(
          errorBody(
            'command_not_found',
            'This workspace command is unavailable for this principal.',
          ),
        );
    return { journal_id: controlPlane.journal.info.journal_id, command };
  });

  app.get(
    '/api/workspace/agent',
    async () =>
      sources.workspaceRequests?.status() ?? {
        availability: 'unavailable',
        reason: {
          code: 'coordinator_unavailable',
          message: 'The workspace coordinator is not configured on this host.',
        },
      },
  );
  app.get('/api/workspace/requests/:request_id', async (request, reply) => {
    const id = (request.params as Record<string, unknown>).request_id;
    if (!validateWorkspaceRequestId(id).ok)
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'The request identifier is invalid.'));
    const view = sources.workspaceRequests?.read(id as string, request.principal);
    if (!view)
      return reply
        .code(404)
        .send(
          errorBody(
            'request_not_found',
            'This workspace request is unavailable for this principal.',
          ),
        );
    return { request: view };
  });

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

  // What can be done where the person stands (ADR 0060): one list, read by every screen and the assistant.
  app.get('/api/actions', async (request, reply) => {
    const query = validateHereQuery(request.query);
    if (!query.ok)
      return reply
        .code(400)
        .send(
          errorBody('invalid_request', 'project_id, workstream_id and execution_id must be ids.'),
        );
    const scope = {
      project_id: query.value.project_id ?? null,
      workstream_id: query.value.workstream_id ?? null,
      execution_id: query.value.execution_id ?? null,
    };
    const body: HereResponse = {
      scope,
      position: controlPlane.projection.position,
      actions: controlPlane.commands.here(scope),
    };
    return body;
  });

  // One of the host's lists, by index, for a choice or a list card (ADR 0060); the ids stay the host's.
  app.get('/api/actions/choices', async (request, reply) => {
    const query = validateChoicesQuery(request.query);
    if (!query.ok)
      return reply
        .code(400)
        .send(errorBody('invalid_request', 'list must be a list, and the scope ids must be ids.'));
    const scope = {
      project_id: query.value.project_id ?? null,
      workstream_id: query.value.workstream_id ?? null,
      execution_id: query.value.execution_id ?? null,
    };
    const body: ChoicesResponse | null = controlPlane.commands.choices(scope, query.value.list);
    if (body === null)
      return reply
        .code(404)
        .send(
          errorBody('read_unavailable', 'That list is not one this place has, or not here yet.'),
        );
    return body;
  });

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
  // device sees the folder names an agent it starts could read anyway, and which projects, all of
  // which it already sees, use each folder.
  app.get(
    '/api/locations',
    async (): Promise<LocationsResponse> =>
      controlPlane.locations.list(
        controlPlane.projection
          .projects()
          .flatMap((project) =>
            project.location === null
              ? []
              : [{ project_id: project.project_id, path: project.location.path }],
          ),
      ),
  );

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
      controlPlane.redaction,
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

  // The starters a new project can begin from (ADR 0037), read from disk on request.
  app.get(
    '/api/starters',
    async (): Promise<StartersResponse> => ({
      starters: controlPlane.starters?.list() ?? [],
    }),
  );

  // Halcyonic's own undo (ADR 0036): read through, never journaled.
  const undoOff = {
    availability: 'unavailable',
    reason: {
      code: 'undo_not_set_up',
      message: 'Undo is not set up on this computer (HALCYONIC_GIT_BIN).',
    },
  } as const;

  // Reads of a project's files for the headset (the parity map's gaps 5, 10 and 4): read only,
  // bounded, owner-journal principals only; anything not found or not allowed is the same 404.
  const reads = new ProjectReads(controlPlane);
  const unavailable = (reply: { code(code: number): { send(body: unknown): unknown } }) =>
    reply
      .code(404)
      .send(errorBody('read_unavailable', 'This read is unavailable for this selection.'));
  const invalid = (reply: { code(code: number): { send(body: unknown): unknown } }) =>
    reply.code(400).send(errorBody('invalid_request', 'The project, scope or path is invalid.'));
  app.get('/api/projects/:project_id/history', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    const query = validateProjectHistoryQuery(request.query);
    if (!validateProjectId(projectId).ok || !query.ok) return invalid(reply);
    return (await reads.history(projectId, query.value, request.principal)) ?? unavailable(reply);
  });
  app.get('/api/projects/:project_id/changes', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    const query = validateProjectChangesQuery(request.query);
    if (!validateProjectId(projectId).ok || !query.ok) return invalid(reply);
    return (await reads.changes(projectId, query.value, request.principal)) ?? unavailable(reply);
  });
  app.get('/api/projects/:project_id/changes/file', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    const query = validateProjectFileChangeQuery(request.query);
    if (!validateProjectId(projectId).ok || !query.ok) return invalid(reply);
    return (
      (await reads.fileChange(projectId, query.value, request.principal)) ?? unavailable(reply)
    );
  });
  app.get('/api/projects/:project_id/file', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    const query = validateProjectFileQuery(request.query);
    if (!validateProjectId(projectId).ok || !query.ok) return invalid(reply);
    return (await reads.file(projectId, query.value, request.principal)) ?? unavailable(reply);
  });
  // The pictures an instruction may carry (A4).
  app.get('/api/projects/:project_id/pictures', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    const query = validateProjectReadQuery(request.query);
    if (!validateProjectId(projectId).ok || !query.ok) return invalid(reply);
    return (await reads.pictures(projectId, query.value, request.principal)) ?? unavailable(reply);
  });
  // The project's own rules, to read before choosing them (E5).
  app.get('/api/projects/:project_id/rules', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    const query = validateProjectReadQuery(request.query);
    if (!validateProjectId(projectId).ok || !query.ok) return invalid(reply);
    return reads.rules(projectId, query.value, request.principal) ?? unavailable(reply);
  });
  app.get('/api/projects/:project_id/checks/:check_id/output', async (request, reply) => {
    const { project_id: projectId, check_id: checkId } = request.params as {
      project_id: string;
      check_id: string;
    };
    const query = validateCheckStepOutputQuery(request.query);
    if (!validateProjectId(projectId).ok || !validateCheckId(checkId).ok || !query.ok)
      return invalid(reply);
    return (
      reads.checkOutput(projectId, checkId, query.value, request.principal) ?? unavailable(reply)
    );
  });

  app.get('/api/projects/:project_id/checkpoints', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    if (!validateProjectId(projectId).ok) {
      return reply.code(400).send(errorBody('invalid_request', 'project_id must be a project id.'));
    }
    if (controlPlane.projection.project(projectId) === undefined) {
      return reply
        .code(404)
        .send(errorBody('project_not_found', `Project ${projectId} does not exist.`));
    }
    const checkpoints = controlPlane.checkpoints;
    const body: CheckpointsResponse = {
      project_id: projectId as ProjectId,
      result:
        checkpoints === null
          ? undoOff
          : { availability: 'available', checkpoints: checkpoints.checkpoints(projectId) },
    };
    return body;
  });

  app.get(
    '/api/projects/:project_id/checkpoints/:checkpoint_id/restore-preview',
    async (request, reply) => {
      const { project_id: projectId, checkpoint_id: checkpointId } = request.params as {
        project_id: string;
        checkpoint_id: string;
      };
      if (!validateProjectId(projectId).ok || !validateCheckpointId(checkpointId).ok) {
        return reply
          .code(400)
          .send(errorBody('invalid_request', 'The project or checkpoint id is invalid.'));
      }
      if (controlPlane.projection.project(projectId) === undefined) {
        return reply
          .code(404)
          .send(errorBody('project_not_found', `Project ${projectId} does not exist.`));
      }
      const checkpoints = controlPlane.checkpoints;
      if (checkpoints === null) {
        const body: RestorePreviewResponse = {
          project_id: projectId as ProjectId,
          result: undoOff,
        };
        return body;
      }
      if (controlPlane.projection.checkpoint(projectId, checkpointId) === undefined) {
        return reply
          .code(404)
          .send(
            errorBody(
              'checkpoint_not_found',
              `Checkpoint ${checkpointId} is not one of this project's.`,
            ),
          );
      }
      const previewed = await checkpoints.preview(
        projectId as ProjectId,
        checkpointId as CheckpointId,
      );
      const body: RestorePreviewResponse = {
        project_id: projectId as ProjectId,
        result: previewed.ok
          ? {
              availability: 'available',
              preview: previewed.preview,
              // What a restore carries back, so it acts only on what the person read (ADR 0051).
              review: previewed.review,
            }
          : {
              availability: 'unavailable',
              reason: {
                code: /^[a-z][a-z0-9_]{0,63}$/.test(previewed.code)
                  ? previewed.code
                  : 'preview_failed',
                message: previewed.message,
              },
            },
      };
      return body;
    },
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
        // Which devices are paired is the owner's to know: a paired device reads no device events,
        // as no realtime client receives them, and the positions they leave out are harmless gaps.
        excludeEventTypes: request.principal?.kind === 'device' ? DEVICE_EVENT_TYPES : [],
      }),
    };
    return body;
  });

  // Sharing a checked game (ADR 0054): which ways work on this Mac, read when the Share card opens,
  // and a live share's address as a code to scan, which the headset draws, never the address itself.
  app.get(
    '/api/sharing',
    async (): Promise<SharingWaysResponse> =>
      controlPlane.shares?.ways() ?? { link: 'not_set_up', wifi: 'not_set_up' },
  );
  app.get('/api/projects/:project_id/share/code', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    if (!validateProjectId(projectId).ok) return invalid(reply);
    const share = controlPlane.projection.share(projectId);
    if (share === undefined)
      return reply.code(404).send(errorBody('share_not_found', 'This game is not shared.'));
    const code = shareCode(share.url);
    const answer: ShareCodeResponse = {
      check_id: share.check_id,
      way: share.way,
      code: { size: code.size, rows: [...code.rows] },
    };
    return answer;
  });

  // A project's own check (ADR 0055): the scripts its package.json offers now, read before choosing.
  app.get('/api/projects/:project_id/own-check', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    if (!validateProjectId(projectId).ok) return invalid(reply);
    const project = controlPlane.projection.project(projectId);
    if (project === undefined)
      return reply.code(404).send(errorBody('project_not_found', 'This project does not exist.'));
    if (controlPlane.ownChecks === null || project.starter !== null)
      return reply
        .code(404)
        .send(
          errorBody(
            'own_check_unavailable',
            project.starter !== null
              ? 'A project made from a starter is checked by your computer itself.'
              : "A project's own check is not set up on this computer.",
          ),
        );
    const answer: OwnCheckFoundResponse = await controlPlane.ownChecks.found(
      projectId as ProjectId,
    );
    return answer;
  });

  // Provider usage limits, account wide: read through on request, never journaled (ADR 0010).
  app.get('/api/usage-limits', async (request): Promise<UsageLimitsResponse> => {
    const answer: UsageLimitsResponse = (await sources.evaluation.usageLimits?.()) ?? {
      availability: 'unavailable',
      reason: { code: 'not_configured', message: 'No usage limit source is configured.' },
    };
    if (validateUsageLimitsResponse(answer).ok) {
      return answer.availability === 'available'
        ? answer
        : { ...answer, reason: reasonWithoutCredentials(answer.reason, controlPlane.redaction) };
    }
    request.log.warn('usage limits do not match the contract');
    return {
      availability: 'incompatible',
      reason: {
        code: 'invalid_usage_limits',
        message: 'The usage limit source returned data outside the contract.',
      },
    };
  });

  // The person's catalog, read through on request and never journaled (ADR 0039). What it says about
  // this computer is never taken: each project's links come from this journal, and no folder is
  // suggested until the host matches folders itself.
  app.get('/api/catalog', async (request): Promise<CatalogResponse> => {
    let answer: CatalogResponse;
    try {
      answer = (await sources.catalog?.read()) ?? {
        availability: 'unavailable',
        reason: { code: 'not_configured', message: 'No catalog source is configured.' },
      };
    } catch {
      // Its error can quote what it read, a path or an address, so only the fact is logged.
      request.log.warn('the catalog source failed');
      return {
        availability: 'unavailable',
        reason: { code: 'source_failed', message: 'The catalog source could not be read.' },
      };
    }
    if (!validateCatalogResponse(answer).ok) {
      request.log.warn('the catalog does not match the contract');
      return {
        availability: 'incompatible',
        reason: {
          code: 'invalid_catalog',
          message: 'The catalog source returned data outside the contract.',
        },
      };
    }
    if (answer.availability !== 'available') {
      return { ...answer, reason: reasonWithoutCredentials(answer.reason, controlPlane.redaction) };
    }
    const linked = new Map<number, string[]>();
    for (const project of controlPlane.projection.projects()) {
      const link = controlPlane.projection.catalogLink(project.project_id);
      if (link == null) continue;
      linked.set(link.repository_id, [
        ...(linked.get(link.repository_id) ?? []),
        project.project_id,
      ]);
    }
    // Suggestions are the host's own, inferred by rule, and never for a sample: a folder whose
    // origin is the repository, or else one named like it whose origin is no repository found here.
    const listing = controlPlane.locations.list(
      controlPlane.projection
        .projects()
        .flatMap((project) =>
          project.location === null
            ? []
            : [{ project_id: project.project_id, path: project.location.path }],
        ),
    );
    const origins =
      answer.source.sample || sources.catalog?.match === undefined
        ? []
        : await sources.catalog.match(listing);
    const matchedPaths = new Set(origins.map((origin) => origin.path));
    const folders = listing.roots.flatMap((root) =>
      root.folders.map((folder) => ({ root: root.path, folder })),
    );
    const suggest = (repository: { readonly full_name: string }) => {
      const fullName = repository.full_name.toLowerCase();
      const name = fullName.slice(fullName.indexOf('/') + 1);
      return [
        ...origins
          .filter((origin) => origin.fullName === fullName)
          .map((origin) => ({
            root: origin.root,
            folder_name: origin.folderName,
            rule: 'github_origin_full_name' as const,
          })),
        ...(answer.source.sample || sources.catalog?.match === undefined
          ? []
          : folders
              .filter(
                ({ folder }) =>
                  !matchedPaths.has(folder.path) && folder.name.toLowerCase() === name,
              )
              .map(({ root, folder }) => ({
                root,
                folder_name: folder.name,
                rule: 'folder_name_equals_repository_name' as const,
              }))),
      ];
    };
    return {
      ...answer,
      projects: answer.projects.map((project) => {
        const projectIds = [
          ...new Set(
            project.repositories.flatMap(
              (repository) => linked.get(repository.repository_id) ?? [],
            ),
          ),
        ].slice(0, 100) as typeof project.local.project_ids;
        const seen = new Set<string>();
        const suggestions =
          projectIds.length > 0
            ? []
            : project.repositories
                .flatMap(suggest)
                .filter((suggestion) => {
                  const key = `${suggestion.root}\u0000${suggestion.folder_name}`;
                  if (seen.has(key)) return false;
                  seen.add(key);
                  return true;
                })
                .slice(0, 20);
        return { ...project, local: { project_ids: projectIds, suggestions } };
      }),
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
        : await sources.understanding.understand(
            execution.runtime.kind,
            execution.native_id,
            execution.started_at ?? execution.created_at,
          );
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
        : await sources.evaluation.evaluate(
            execution.runtime.kind,
            execution.native_id,
            execution.started_at ?? execution.created_at,
          );
    const body = { execution_id: execution.execution_id, result };
    if (validateEvaluationResponse(body).ok) {
      return { ...body, result: evaluationWithoutCredentials(result, controlPlane.redaction) };
    }
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

  // Create's companion (ADR 0025): whether it can be asked, and one reply to the exchange so far.
  // Neither the exchange nor the reply is journaled, stored or logged; the log carries codes,
  // counts and times only.
  app.get('/api/companion', async (request): Promise<CompanionStatus> => {
    const status: CompanionStatus = (await sources.companion?.status()) ?? {
      availability: 'unavailable',
      reason: {
        code: 'companion_not_set_up',
        message: 'The companion is not set up on this computer.',
      },
    };
    if (!validateCompanionStatus(status).ok) {
      request.log.warn('companion status does not match the contract');
      return {
        availability: 'unavailable',
        reason: { code: 'companion_not_running', message: 'The companion cannot be read.' },
      };
    }
    if (status.availability === 'unavailable') {
      request.log.info({ code: status.reason.code }, 'companion unavailable');
    }
    return status;
  });

  app.post(
    '/api/companion/replies',
    { bodyLimit: COMPANION_BODY_LIMIT },
    async (request, reply) => {
      if (sources.companion === undefined) {
        return reply
          .code(503)
          .send(errorBody('companion_not_set_up', 'The companion is not set up on this computer.'));
      }
      // The headset going away ends the turn, which closes the request to the model and stops it.
      const ended = new AbortController();
      const onClose = () => {
        if (!reply.raw.writableFinished) ended.abort();
      };
      reply.raw.once('close', onClose);
      const started = performance.now();
      let answer: Awaited<ReturnType<Companion['reply']>>;
      try {
        answer = await sources.companion.reply(request.principal, request.body, ended.signal);
      } finally {
        reply.raw.off('close', onClose);
      }
      const ms = Math.round(performance.now() - started);
      if (answer.kind === 'refused') {
        request.log.info({ code: answer.code, ms, ...answer.log }, 'companion refused');
        return reply
          .code(answer.status)
          .send(
            errorBody(answer.code, answer.message, answer.issues ? [...answer.issues] : undefined),
          );
      }
      if (!validateCompanionReply(answer.body).ok) {
        request.log.warn({ ms, ...answer.log }, 'companion reply does not match the contract');
        return reply
          .code(502)
          .send(errorBody('companion_unreadable', "The companion's answer could not be read."));
      }
      request.log.info(
        { next: answer.body.reply.next, view: answer.body.reply.view, ms, ...answer.log },
        'companion replied',
      );
      return answer.body;
    },
  );

  // What binding a project to another folder would do, before the person confirms it, and the
  // digest a project.set_location carries back (ADR 0051). Reads only.
  // A project secret's value, write-only, from this Mac or a paired device (ADR 0057): the name-only
  // command is submitted as the caller, and no route ever gives a value back.
  const validSecretSet = compileValidator(ProjectSecretSetRequest);
  app.post('/api/projects/:project_id/secrets', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    if (!validateProjectId(projectId).ok)
      return reply.code(404).send(errorBody('project_not_found', 'That is not a project.'));
    const parsed = validSecretSet(request.body);
    // Never the issues' own words: a value is in the body, so only where it went wrong.
    if (!parsed.ok)
      return reply.code(400).send(
        errorBody(
          'invalid_request',
          'The secret does not match the contract: a name of capitals, digits and _, and a value of 8 to 8192 characters.',
          parsed.issues.map((issue) => ({ path: issue.path, message: 'invalid' })),
        ),
      );
    const store = controlPlane.projectSecrets;
    if (store === null)
      return reply
        .code(503)
        .send(errorBody('unavailable', "This computer doesn't keep project secrets."));
    const body = parsed.value;
    const set = submitSecretSet(
      {
        store,
        submit: (command, via, principal) => controlPlane.commands.submit(command, via, principal),
      },
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: body.command_id,
        issued_at: body.issued_at,
        client: body.client,
        command_type: 'project.secret.set',
        payload: { project_id: projectId as ProjectId, name: body.name },
      },
      body.value,
      'http',
      request.principal,
    );
    if (!set.ok) return reply.code(422).send(errorBody('invalid_request', set.problem));
    const outcome = set.outcome;
    if (outcome.disposition === 'conflict')
      return reply
        .code(409)
        .send(
          errorBody(
            'command_id_conflict',
            'This command id was already used for a different command.',
          ),
        );
    const answer: CommandSubmissionResponse = {
      disposition: outcome.disposition,
      command: outcome.command,
    };
    return reply
      .code({ accepted: 202, rejected: 422, duplicate: 200 }[outcome.disposition])
      .send(answer);
  });

  // Publish's review and the published address as a code to scan (ADR 0039, amended).
  app.post(
    '/api/projects/:project_id/publish-review',
    async (request): Promise<GitHubPublishReviewResponse> => {
      const projectId = (request.params as { project_id: string }).project_id;
      if (!validateProjectId(projectId).ok)
        return {
          availability: 'unavailable',
          reason: { code: 'invalid_request', message: 'That is not a project.' },
        };
      return publishReviewOf(controlPlane, projectId as ProjectId);
    },
  );
  app.get('/api/projects/:project_id/published/code', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    if (!validateProjectId(projectId).ok) return invalid(reply);
    const published = controlPlane.projection.project(projectId)?.published;
    if (published === undefined || published.address === null)
      return reply.code(404).send(errorBody('not_published', 'This game is not published.'));
    const code = shareCode(published.address);
    const answer: PublishedCodeResponse = {
      check_id: published.check_id,
      address: published.address,
      code: { size: code.size, rows: [...code.rows] },
    };
    return answer;
  });

  // Send to GitHub's review (ADR 0039, amended): what a send would do, read before the press.
  app.post(
    '/api/projects/:project_id/github-review',
    async (request): Promise<GitHubSendReviewResponse> => {
      const projectId = (request.params as { project_id: string }).project_id;
      if (!validateProjectId(projectId).ok)
        return {
          availability: 'unavailable',
          reason: { code: 'invalid_request', message: 'That is not a project.' },
        };
      return sendReviewOf(controlPlane, projectId as ProjectId);
    },
  );
  app.post('/api/projects/:project_id/location-review', async (request, reply) => {
    const projectId = (request.params as { project_id: string }).project_id;
    if (!validateProjectId(projectId).ok) {
      return reply.code(400).send(errorBody('invalid_request', 'The project id is invalid.'));
    }
    const parsed = validateLocationReview(request.body);
    if (!parsed.ok) {
      return reply
        .code(400)
        .send(
          errorBody('invalid_request', 'The request does not match the contract.', parsed.issues),
        );
    }
    const project = controlPlane.projection.project(projectId);
    if (project === undefined) {
      return reply
        .code(404)
        .send(errorBody('project_not_found', `Project ${projectId} does not exist.`));
    }
    const body: LocationReviewResponse = {
      project_id: project.project_id,
      current: project.location,
      next: parsed.value.location,
      review: locationReview(projectId, parsed.value.location, project.location),
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

/**
 * An evaluation source's reason, its labels for the kinds of check it measured, and its note on a
 * verification that measured nothing, with credentials taken out as from any error text
 * (redaction.ts): they are the source's own words, and a provider's can echo a key Halcyonic never
 * held. Applied to an answer that matches the contract, and cut to the field's limit after.
 */
function reasonWithoutCredentials(
  reason: { readonly code: string; readonly message: string },
  redaction: Redaction,
): { code: string; message: string } {
  return { code: reason.code, message: withinLimit(redaction.errorText(reason.message)) };
}

function evaluationWithoutCredentials(
  result: EvaluationResult,
  redaction: Redaction,
): EvaluationResult {
  if (result.availability !== 'available') {
    return { ...result, reason: reasonWithoutCredentials(result.reason, redaction) };
  }
  const { verification } = result.evaluation;
  const lens = verification.lens;
  if (lens === null) return result;
  return {
    ...result,
    evaluation: {
      ...result.evaluation,
      verification: {
        ...verification,
        lens: {
          ...lens,
          by_kind: lens.by_kind.map((kind) => ({
            ...kind,
            label: withinLimit(redaction.errorText(kind.label), 120),
          })),
          empty_reason:
            lens.empty_reason === null
              ? null
              : withinLimit(redaction.errorText(lens.empty_reason), 600),
        },
      },
    },
  };
}

function toInteger(value: unknown, fallback: number): number {
  if (value === undefined) return fallback;
  if (typeof value !== 'string' || !/^\d+$/.test(value)) return Number.NaN;
  return Number(value);
}
