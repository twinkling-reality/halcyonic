import { createHmac, randomBytes } from 'node:crypto';
import type {
  CatalogRepositoryRef,
  CheckId,
  CommandEnvelope,
  CommandFailure,
  CommandOf,
  CommandResult,
  CommandView,
  ExecutionId,
  ExecutionStatus,
  ExecutionView,
  Principal,
  ProjectId,
  ProjectLocation,
  ProjectLocationChoice,
  ReceivedVia,
  RejectionCode,
  RuntimeDescriptor,
  RuntimeRef,
  Timestamp,
  WorkstreamId,
} from '@halcyonic/contracts';
import {
  type Admission,
  admitCommand,
  type CommandScope,
  journaledRejection,
  type Projection,
} from '@halcyonic/domain';
import {
  type Clock,
  checkProjectLocation,
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeAdapter,
  type Scheduler,
} from '@halcyonic/runtime-core';
import type { Checkpointer } from '../checkpoints/checkpointer.ts';
import type { IdGenerator } from '../ids.ts';
import type { HostLocations } from '../locations.ts';
import type { Logger } from '../logger.ts';
import type { FileObserver } from '../observer/file-observer.ts';
import type { CheckOutput } from '../preview/check-output.ts';
import type { HostPreviews } from '../preview/preview.ts';
import { type HostStarters, VR_STARTER, vrVersionInstruction } from '../starters.ts';
import {
  type Cause,
  causedBy,
  controlPlaneDraft,
  createObservationSink,
  NO_CAUSE,
} from './drafts.ts';
import { type OllamaUnloader, ollamaModelFrom } from './ollama-unload.ts';
import { type Reattacher, SESSION_LOST } from './reattach.ts';
import type { Recorder } from './recorder.ts';
import { fitted, type Redaction, withinLimit } from './redaction.ts';
import type { RuntimeRegistry } from './runtime-registry.ts';

export type SubmitOutcome =
  | { readonly disposition: 'accepted' | 'rejected' | 'duplicate'; readonly command: CommandView }
  | { readonly disposition: 'conflict'; readonly command: null };

export type WorkspaceRequestCommand = CommandOf<
  'workspace.request.submit' | 'workspace.request.resume' | 'workspace.request.cancel'
>;
export interface WorkspaceRequestHandler {
  handle(command: WorkspaceRequestCommand, principal: Principal | null): CommandResult | null;
}

/**
 * Whether a link names a repository of the person's catalog as this computer last read it (ADR 0039),
 * so a client can link only what the person was shown. The catalog itself stays outside the core.
 */
export interface CatalogLinks {
  check(
    ref: CatalogRepositoryRef,
  ): { readonly ok: true } | { readonly ok: false; readonly message: string };
}

export interface CommandServiceDeps {
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly registry: RuntimeRegistry;
  /** Where projects may live on the host, and the directory policy runtimes ask. */
  readonly locations: HostLocations;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly scheduler: Scheduler;
  readonly logger: Logger;
  /** How long to wait for a runtime to confirm an action before recording it as failed. */
  readonly commandTimeoutMs: number;
  /** How a runtime's text loses credentials before it is journaled (redaction.ts). */
  readonly redaction: Redaction;
  /** Halcyonic's own undo (ADR 0036), or null when the host has not set it up. */
  readonly checkpoints?: Checkpointer | null;
  /** The host's checks and previews of starter projects (ADR 0037), or null when not set up. */
  readonly previews?: HostPreviews | null;
  /** The starters a new project can begin from (ADR 0037), or null when there are none. */
  readonly starters?: HostStarters | null;
  /** The Mac's own reading of what each turn changed (ADR 0038); none, as in some tests. */
  readonly files?: FileObserver | null;
  /** The person's catalog, as last read, for checking links; none when no catalog is set up. */
  readonly catalogLinks?: CatalogLinks | null;
  /** Unloads a local Ollama model that Halcyonic loaded once no execution uses it (UL1); none by default. */
  readonly ollamaUnload?: OllamaUnloader | null;
  /** Re-attaches what an earlier control plane started (ADR 0046); none for a fixture journal. */
  readonly reattacher?: Reattacher | null;
}

type Admitted = Admission & { admitted: true };

interface ActionOutcome {
  succeeded(): void;
  failed(failure: CommandFailure): void;
}

/**
 * How long a start, or an instruction that starts a turn from rest, waits for the Mac's reading of
 * its folder before going ahead without it.
 */
const BASELINE_WAIT_MS = 10_000;

/** What an execution that may still be working reads, until a runtime or a restart says otherwise. */
const ACTIVE_STATUSES: ReadonlySet<ExecutionStatus> = new Set([
  'starting',
  'running',
  'verifying',
  'waiting_for_human',
]);

/**
 * A reading the runtime waits for at most `BASELINE_WAIT_MS`, so a slow one never holds work up; past
 * that, `abandon` drops it, since taken late it may hold what the runtime already wrote, and the
 * turn says it has no earlier reading.
 */
function bounded(reading: Promise<void>, abandon: () => void): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const late = new Promise<void>((resolveLate) => {
    timer = setTimeout(() => {
      abandon();
      resolveLate();
    }, BASELINE_WAIT_MS);
  });
  return Promise.race([reading.finally(() => clearTimeout(timer)), late]);
}

/** How many refused answers' digests are kept to tell a resent answer from another one. */
const REFUSED_ANSWERS_KEPT = 1000;
/** How many resends of refused answers one principal may have compared in each fixed minute. */
const REFUSED_RESENDS_PER_MINUTE = 6;

/**
 * Carries a command from submission to a recorded outcome. Every command is journaled as
 * accepted or rejected before anything happens. Completion is recorded only when the runtime
 * confirms the action, and a timeout is recorded as a failure whose effect is unknown, because
 * the runtime may still have acted.
 */
export class CommandService {
  readonly #deps: CommandServiceDeps;
  #workspaceRequests: WorkspaceRequestHandler | null = null;
  /** The actual consumed host coordinator; absent means these commands are refused. */
  attachWorkspaceRequests(handler: WorkspaceRequestHandler): void {
    if (this.#workspaceRequests !== null) throw new Error('workspace coordinator already attached');
    this.#workspaceRequests = handler;
  }
  /**
   * Keyed digests of refused commands as sent, by id, with who sent them, for those the journal
   * keeps only in part (journaledRejection), so a reused id with another answer is still a
   * conflict. The key is drawn per process and never stored, so a digest cannot be tested against
   * guesses of a short secret. Gone after a restart, when such a command sent again is a conflict.
   */
  readonly #refusedDigests = new Map<string, { readonly digest: string; readonly by: string }>();
  /** Raw request identity is compared only for the original principal; no text is kept here. */
  readonly #requestDigests = new Map<string, { readonly digest: string; readonly by: string }>();
  readonly #digestKey = randomBytes(32);
  /** Resends of refused answers compared in the current fixed minute, by principal. */
  readonly #resends = new Map<string, { readonly minute: number; count: number }>();

  constructor(deps: CommandServiceDeps) {
    this.#deps = deps;
  }

  /**
   * `principal` is who the control plane authenticated: the local access token or a paired device.
   * It is null only for commands from inside the control plane, such as a fixture recorder.
   *
   * A device is authorized here, where the command would act, and not only when its request or
   * connection was opened: a device revoked since then has its command rejected (ADR 0017). A
   * duplicate changes nothing, so it is answered as one whoever sends it.
   */
  submit(
    submitted: CommandEnvelope,
    via: ReceivedVia,
    principal: Principal | null = null,
  ): SubmitOutcome {
    const command: CommandEnvelope =
      submitted.command_type === 'workspace.request.submit'
        ? {
            ...submitted,
            payload: {
              ...submitted.payload,
              text: fitted(this.#deps.redaction.held(submitted.payload.text), 32000),
            },
          }
        : submitted.command_type === 'workspace.request.resume' && submitted.payload.text !== null
          ? {
              ...submitted,
              payload: {
                ...submitted.payload,
                text: fitted(this.#deps.redaction.held(submitted.payload.text), 32000),
              },
            }
          : submitted;
    const known = this.#deps.projection.commandEnvelope(command.command_id);
    if (known !== undefined) {
      return (
        known.command_type.startsWith('workspace.request.')
          ? this.#sameRequest(submitted, principal)
          : this.#sameAsKnown(known, command, principal)
      )
        ? { disposition: 'duplicate', command: this.#view(command) }
        : { disposition: 'conflict', command: null };
    }

    if (command.command_type.startsWith('workspace.request.')) {
      this.#requestDigests.set(command.command_id, {
        digest: this.#digest(submitted),
        by: principalKey(principal),
      });
      if (this.#requestDigests.size > REFUSED_ANSWERS_KEPT) {
        const oldest = this.#requestDigests.keys().next().value;
        if (oldest !== undefined) this.#requestDigests.delete(oldest);
      }
    }
    const admission = this.#authorize(principal, this.#admit(command), command);
    const cause = causedBy(command.command_id);
    if (!admission.admitted) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'command.rejected',
          admission.scope,
          {
            command: this.#journaledRejection(command, principal),
            // Messages can quote what the client sent; the contract caps them, so they are cut.
            rejection: {
              code: admission.rejection.code,
              message: clip(admission.rejection.message, 'The command was refused.'),
            },
            received_via: via,
            principal,
          },
          this.#now(),
          cause,
        ),
      );
      this.#deps.logger.info(
        {
          command_id: command.command_id,
          command_type: command.command_type,
          rejection: admission.rejection.code,
        },
        'command rejected',
      );
      return { disposition: 'rejected', command: this.#view(command) };
    }

    this.#deps.recorder.record(
      controlPlaneDraft(
        'command.accepted',
        admission.scope,
        { command, policy: admission.policy, received_via: via, principal },
        this.#now(),
        cause,
      ),
    );
    this.#dispatch(command, admission, cause, principal);
    return { disposition: 'accepted', command: this.#view(command) };
  }

  /**
   * After a restart nothing that was in flight can still be observed: the start stopped every
   * runtime process the last control plane left. Each execution in flight is recorded unknown
   * until its session is re-attached (ADR 0046), every pending command failed with an unknown
   * effect; nothing is left "running" or "pending" forever. Returns the executions to re-attach.
   */
  reconcileAfterRestart(): { executions: number; commands: number; cut: ExecutionView[] } {
    // Active ones, and ones already unknown for another reason, a lost connection or a start whose
    // outcome is unknown: no runtime session survives a restart, and the start stops what a crash
    // left recorded, so each is unknown because of the restart, which a project's busy rule reads
    // as stopped. Left as it was, such a task would hold its project busy for good (U5).
    const all = this.#deps.projection.executions();
    const executions = all.filter(
      (view) =>
        ACTIVE_STATUSES.has(view.status) ||
        (view.status === 'unknown' &&
          view.status_reason?.code !== 'control_plane_restarted' &&
          view.status_reason?.code !== SESSION_LOST),
    );
    // Ones an earlier start already recorded unknown because of a restart, whose re-attach had not
    // settled when that control plane ended (ADR 0046): asked again, never recorded again.
    const unsettled = all.filter(
      (view) => view.status === 'unknown' && view.status_reason?.code === 'control_plane_restarted',
    );
    for (const view of executions) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'execution.state_unknown',
          {
            project_id: view.project_id,
            workstream_id: view.workstream_id,
            execution_id: view.execution_id,
          },
          {
            code: 'control_plane_restarted',
            message:
              'The control plane restarted and is no longer connected to the runtime session of this execution.',
          },
          this.#now(),
          NO_CAUSE,
        ),
      );
    }
    const commands = this.#deps.projection.pendingCommands();
    for (const command of commands) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'command.failed',
          {
            project_id: command.project_id,
            workstream_id: command.workstream_id,
            execution_id: command.execution_id,
          },
          {
            command_id: command.command_id,
            command_type: command.command_type,
            failure: {
              code: 'control_plane_restarted',
              message:
                'The control plane restarted before the runtime confirmed this command. It may or may not have taken effect.',
              effect: 'unknown',
            },
          },
          this.#now(),
          causedBy(command.command_id),
        ),
      );
    }
    return {
      executions: executions.length,
      commands: commands.length,
      cut: [...executions, ...unsettled],
    };
  }

  /** Rejects a command from a device that is no longer paired, whatever its admission. */
  #authorize(
    principal: Principal | null,
    admission: Admission,
    command: CommandEnvelope,
  ): Admission {
    if (command.command_type.startsWith('workspace.request.') && principal === null)
      return {
        admitted: false,
        scope: admission.scope,
        rejection: {
          code: 'invalid_state',
          message: 'Workspace requests require an authenticated principal.',
        },
      };
    if (
      (command.command_type === 'workspace.request.resume' ||
        command.command_type === 'workspace.request.cancel') &&
      principalKey(
        this.#deps.projection.workspaceRequestOwner(command.payload.request_id) ?? null,
      ) !== principalKey(principal)
    )
      return {
        admitted: false,
        scope: admission.scope,
        rejection: {
          code: 'request_not_found',
          message: 'This request does not belong to this principal.',
        },
      };
    if (principal?.kind !== 'device') return admission;
    const device = this.#deps.projection.device(principal.device_id);
    if (device !== undefined && device.revoked_at === null) return admission;
    return {
      admitted: false,
      scope: admission.scope,
      rejection: {
        code: 'device_revoked',
        message:
          'This device is not, or no longer, paired with the control plane, so its commands are not carried out.',
      },
    };
  }

  /**
   * The domain's admission, then what only the host can check: the folder a project is to be bound
   * to, the folder a start would run in, and the runtime's own options.
   */
  #admit(command: CommandEnvelope): Admission {
    if (command.command_type.startsWith('workspace.request.') && this.#workspaceRequests === null)
      return {
        admitted: false,
        scope: { project_id: null, workstream_id: null, execution_id: null },
        rejection: {
          code: 'capability_unsupported',
          message: 'The workspace coordinator is unavailable on this computer.',
        },
      };
    if (command.command_type === 'project.restore' && !this.#deps.checkpoints)
      return {
        admitted: false,
        scope: { project_id: command.payload.project_id, workstream_id: null, execution_id: null },
        rejection: {
          code: 'capability_unsupported',
          message: 'Undo is not set up on this computer (HALCYONIC_GIT_BIN).',
        },
      };
    if (
      (command.command_type === 'project.check' ||
        command.command_type.startsWith('project.preview.')) &&
      !this.#deps.previews
    )
      return {
        admitted: false,
        scope: { project_id: null, workstream_id: null, execution_id: null },
        rejection: {
          code: 'capability_unsupported',
          message: 'Checks are not set up on this computer: no starter is installed.',
        },
      };
    if (command.command_type === 'project.open_on_mac' && !this.#deps.previews?.canPlay)
      return {
        admitted: false,
        scope: { project_id: null, workstream_id: null, execution_id: null },
        rejection: {
          code: 'capability_unsupported',
          message: 'Playing on this Mac is not set up: no browser for it.',
        },
      };
    const admission = admitCommand(command, this.#deps.projection, this.#deps.registry);
    if (!admission.admitted) return admission;
    const refuse = (code: RejectionCode, message: string): Admission => ({
      admitted: false,
      scope: admission.scope,
      rejection: { code, message },
    });
    switch (command.command_type) {
      case 'project.link_catalog': {
        const links = this.#deps.catalogLinks;
        if (!links)
          return refuse(
            'capability_unsupported',
            'No catalog of your projects is set up on this computer.',
          );
        const known = links.check(command.payload.catalog);
        return known.ok ? admission : refuse('invalid_state', known.message);
      }
      case 'project.make_vr_version': {
        const choice = {
          kind: 'new_from_starter' as const,
          root: command.payload.root,
          folder_name: command.payload.folder_name,
          starter_id: VR_STARTER,
        };
        const starter = this.#deps.starters?.check(VR_STARTER) ?? {
          ok: false,
          message: 'This computer has no starters.',
        };
        if (!starter.ok) return refuse('capability_unsupported', starter.message);
        // The original must be one game's own folder (AUDIT M18C1-M1); the domain has checked it has one.
        const folder = this.#deps.projection.project(command.payload.original_project_id)?.location
          ?.path;
        const projects = this.#deps.projection
          .projects()
          .flatMap((project) => (project.location === null ? [] : [project.location.path]));
        const starters = this.#deps.starters;
        const refusal =
          folder === undefined
            ? 'The game has no folder to make a VR version from.'
            : starters === undefined || starters === null
              ? 'This computer has no starters.'
              : starters.originalRefusal(
                  folder,
                  this.#deps.locations.list().roots.map((root) => root.path),
                  projects,
                );
        if (refusal !== null) return refuse('location_not_allowed', refusal);
        const checked = this.#deps.locations.check(choice);
        return checked.ok ? admission : refuse(checked.code, checked.message);
      }
      case 'project.create':
      case 'project.set_location': {
        const choice = command.payload.location;
        if (choice === null) return admission;
        if (choice.kind === 'new_from_starter') {
          if (command.command_type === 'project.set_location')
            return refuse('capability_unsupported', 'A starter can only begin a new project.');
          const starter = this.#deps.starters?.check(choice.starter_id) ?? {
            ok: false,
            message: 'This computer has no starters.',
          };
          if (!starter.ok) return refuse('capability_unsupported', starter.message);
        }
        const checked = this.#deps.locations.check(choice);
        return checked.ok ? admission : refuse(checked.code, checked.message);
      }
      case 'execution.start': {
        if (admission.runtime?.uses_project_location === true) {
          const directory = this.#projectDirectory(admission.scope);
          const decision =
            directory === null
              ? null
              : checkProjectLocation(this.#deps.locations.policy, directory);
          if (decision === null) {
            return refuse('location_required', 'The project has no folder to work in.');
          }
          if (!decision.ok) return refuse(decision.code, decision.message);
        }
        const adapter = this.#deps.registry.adapter(command.payload.runtime_id);
        const options = adapter?.validateStartOptions(
          command.payload.options,
          command.payload.model_ref,
          command.payload.approval_level,
        );
        if (options === undefined || options.ok) return admission;
        return refuse('invalid_runtime_options', options.message);
      }
      default:
        return admission;
    }
  }

  /** The folder the admitted command's project is bound to, or null for none. */
  #projectDirectory(scope: CommandScope): string | null {
    if (scope.project_id === null) return null;
    return this.#deps.projection.project(scope.project_id)?.location?.path ?? null;
  }

  #dispatch(
    command: CommandEnvelope,
    admission: Admitted,
    cause: Cause,
    principal: Principal | null,
  ): void {
    const { recorder } = this.#deps;
    switch (command.command_type) {
      case 'workspace.request.submit':
      case 'workspace.request.resume':
      case 'workspace.request.cancel': {
        try {
          if (!this.#workspaceRequests)
            throw new RuntimeActionError(
              'coordinator_unavailable',
              'The workspace coordinator is unavailable.',
            );
          if (
            command.command_type !== 'workspace.request.submit' &&
            principalKey(
              this.#deps.projection.workspaceRequestOwner(command.payload.request_id) ?? null,
            ) !== principalKey(principal)
          )
            throw new RuntimeActionError(
              'request_not_found',
              'This request does not belong to this principal.',
            );
          this.#complete(
            command,
            admission.scope,
            this.#workspaceRequests.handle(command, principal),
          );
        } catch (error) {
          this.#fail(command, admission.scope, toFailure(error, this.#deps.redaction));
        }
        return;
      }
      case 'project.create': {
        const made = this.#createProject(
          command,
          admission.scope,
          cause,
          command.payload.name,
          command.payload.location,
        );
        if (made !== null)
          this.#complete(command, made, { kind: 'project_created', project_id: made.project_id });
        return;
      }
      case 'project.make_vr_version': {
        const original = this.#deps.projection.project(command.payload.original_project_id);
        const folder = original?.location?.path;
        if (original === undefined || folder === undefined) {
          this.#fail(command, admission.scope, {
            code: 'location_required',
            message: 'The game has no folder to make a VR version from.',
            effect: 'none',
          });
          return;
        }
        const made = this.#createProject(
          command,
          admission.scope,
          cause,
          `${original.name.slice(0, 197)} VR`,
          {
            kind: 'new_from_starter',
            root: command.payload.root,
            folder_name: command.payload.folder_name,
            starter_id: VR_STARTER,
          },
        );
        if (made === null) return;
        recorder.record(
          controlPlaneDraft(
            'project.made_from',
            made,
            {
              original_project_id: original.project_id,
              instruction: vrVersionInstruction(folder),
            },
            this.#now(),
            cause,
          ),
        );
        this.#complete(command, made, { kind: 'project_created', project_id: made.project_id });
        return;
      }
      case 'project.set_location': {
        const bound = this.#deps.locations.bind(command.payload.location);
        if (!bound.ok) {
          this.#fail(command, admission.scope, {
            code: bound.code,
            message: bound.message,
            effect: 'effect' in bound ? bound.effect : 'none',
          });
          return;
        }
        recorder.record(
          controlPlaneDraft(
            'project.location_set',
            { project_id: command.payload.project_id, workstream_id: null, execution_id: null },
            { location: bound.location },
            this.#now(),
            cause,
          ),
        );
        this.#complete(command, admission.scope, null);
        return;
      }
      case 'project.link_catalog':
      case 'project.unlink_catalog': {
        // The person's own link (ADR 0039): a local fact, complete as soon as it is recorded.
        const scope = {
          project_id: command.payload.project_id,
          workstream_id: null,
          execution_id: null,
        };
        const previous = this.#deps.projection.catalogLink(command.payload.project_id) ?? null;
        if (command.command_type === 'project.link_catalog') {
          recorder.record(
            controlPlaneDraft(
              'project.catalog_linked',
              scope,
              { catalog: command.payload.catalog, previous },
              this.#now(),
              cause,
            ),
          );
        } else {
          if (previous === null) throw new Error('admitted project.unlink_catalog with no link');
          recorder.record(
            controlPlaneDraft('project.catalog_unlinked', scope, { previous }, this.#now(), cause),
          );
        }
        this.#complete(command, admission.scope, null);
        return;
      }
      case 'project.restore': {
        const checkpoints = this.#deps.checkpoints;
        if (!checkpoints) throw new Error('admitted project.restore with undo off');
        // The host's own work, bounded by the store's own limits, not the runtime timeout.
        void checkpoints
          .restore(command)
          .then(
            (outcome) =>
              outcome.ok
                ? this.#complete(command, admission.scope, {
                    kind: 'project_restored',
                    checkpoint_id: command.payload.checkpoint_id,
                  })
                : this.#fail(command, admission.scope, outcome.failure),
            (error: unknown) => {
              this.#deps.logger.error(
                { err: error, command_id: command.command_id },
                'restore failed',
              );
              this.#fail(command, admission.scope, {
                code: 'restore_failed',
                message: 'The restore stopped on an error. Some files may already have changed.',
                effect: 'unknown',
              });
            },
          )
          // Recording can fail only when the journal is gone, as when the control plane stops;
          // the restart then records the restore as failed with an unknown effect.
          .catch((error: unknown) =>
            this.#deps.logger.error(
              { err: error, command_id: command.command_id },
              'restore outcome not recorded',
            ),
          );
        return;
      }
      case 'project.check':
      case 'project.preview.start':
      case 'project.preview.stop':
      case 'project.open_on_mac': {
        const previews = this.#deps.previews;
        if (!previews) throw new Error(`admitted ${command.command_type} with checks off`);
        const work =
          command.command_type === 'project.check'
            ? previews.check(command).then(({ check_id, passed, ready }) => ({
                kind: 'project_checked' as const,
                check_id,
                passed,
                ready,
              }))
            : command.command_type === 'project.preview.start'
              ? previews.start(command).then(({ check_id, port }) => ({
                  kind: 'preview_started' as const,
                  check_id,
                  port,
                }))
              : command.command_type === 'project.preview.stop'
                ? previews.stop(command).then(() => null)
                : previews.openOnMac(command).then(() => null);
        // The host's own work, bounded by the check's own limits, not the runtime timeout.
        void work
          .then(
            (result) => this.#complete(command, admission.scope, result),
            (error: unknown) => {
              this.#deps.logger.error(
                { err: error, command_id: command.command_id },
                `${command.command_type} failed`,
              );
              this.#fail(command, admission.scope, {
                code: command.command_type === 'project.check' ? 'check_failed' : 'preview_failed',
                message:
                  command.command_type === 'project.check'
                    ? 'The check stopped on an error before it could record what it found.'
                    : command.command_type === 'project.open_on_mac'
                      ? 'The game could not be played on the Mac.'
                      : 'The preview could not be started or stopped.',
                effect: 'unknown',
              });
            },
          )
          .catch((error: unknown) =>
            this.#deps.logger.error(
              { err: error, command_id: command.command_id },
              'outcome not recorded',
            ),
          );
        return;
      }
      case 'workstream.create': {
        const workstreamId = this.#deps.ids.next() as WorkstreamId;
        const scope = {
          project_id: command.payload.project_id,
          workstream_id: workstreamId,
          execution_id: null,
        };
        recorder.record(
          controlPlaneDraft(
            'workstream.created',
            scope,
            { title: command.payload.title, objective: command.payload.objective },
            this.#now(),
            cause,
          ),
        );
        this.#complete(command, scope, { kind: 'workstream_created', workstream_id: workstreamId });
        return;
      }
      case 'execution.start': {
        const { runtime, adapter } = this.#runtimeFor(admission);
        const { project_id, workstream_id } = admission.scope;
        if (project_id === null || workstream_id === null) {
          throw new Error('admitted execution.start without a resolved workstream');
        }
        const execution: ExecutionContext = {
          execution_id: this.#deps.ids.next() as ExecutionId,
          workstream_id,
          project_id,
        };
        const scope = { project_id, workstream_id, execution_id: execution.execution_id };
        // Admission checked it; the adapter asks the host's policy again before anything runs.
        const directory = runtime.uses_project_location
          ? this.#projectDirectory(admission.scope)
          : null;
        recorder.record(
          controlPlaneDraft(
            'execution.created',
            scope,
            { runtime: toRuntimeRef(runtime), instruction: command.payload.instruction, directory },
            this.#now(),
            cause,
          ),
        );
        this.#deps.reattacher?.started(execution.execution_id);
        const start = adapter.startExecution;
        if (start === undefined) {
          this.#recordStartFailure(command, scope, unimplemented('startExecution'));
          return;
        }
        const emit = createObservationSink(
          recorder,
          this.#deps.logger,
          execution,
          runtime.runtime_id,
          this.#deps.redaction,
        );
        // A failed check's first lines go after the person's words, once per check (FX1).
        const checked = this.#checkOutputFor(project_id);
        const run = () =>
          this.#perform(
            command,
            () =>
              start.call(adapter, {
                execution,
                instruction: withCheckOutput(command.payload.instruction, checked),
                options: command.payload.options,
                model_ref: command.payload.model_ref,
                ...(command.payload.approval_level !== undefined && {
                  approval_level: command.payload.approval_level,
                }),
                directory,
                emit,
              }),
            {
              succeeded: () => {
                this.#recordCheckOutput(scope, checked, cause);
                this.#complete(command, scope, {
                  kind: 'execution_created',
                  execution_id: execution.execution_id,
                });
              },
              failed: (failure) => this.#recordStartFailure(command, scope, failure),
            },
          );
        // Before the runtime is asked, outside its timeout: the folder's first reading for what each
        // turn changes (ADR 0038), a checkpoint of it (ADR 0036), and whether an Ollama model was
        // already loaded before Halcyonic started using it (UL1).
        const before: Promise<void>[] = [];
        const ollamaModel = ollamaModelFrom(command.payload.model_ref, command.payload.options);
        if (ollamaModel !== null && this.#deps.ollamaUnload) {
          before.push(this.#deps.ollamaUnload.beforeRun(execution.execution_id, ollamaModel));
        }
        if (directory !== null) {
          const files = this.#deps.files;
          if (files) {
            const id = execution.execution_id;
            before.push(bounded(files.baseline(id, directory), () => files.abandon(id)));
          }
          const checkpoints = this.#deps.checkpoints;
          if (checkpoints)
            before.push(
              checkpoints.beforeTask(project_id, execution.execution_id, directory, cause),
            );
        }
        if (before.length === 0) void run();
        else void Promise.all(before).then(run, run);
        return;
      }
      case 'execution.send_instruction':
      case 'execution.respond_to_approval':
      case 'execution.answer_question':
      case 'execution.interrupt': {
        const { adapter } = this.#runtimeFor(admission);
        const execution = toExecutionContext(admission.scope);
        // A failed check's first lines go after the person's words, once per check (FX1).
        const checked =
          command.command_type === 'execution.send_instruction'
            ? this.#checkOutputFor(execution.project_id)
            : null;
        const action = actionFor(command, adapter, execution, checked);
        const outcome: ActionOutcome = {
          succeeded: () => {
            this.#recordCheckOutput(execution, checked, cause);
            this.#complete(command, admission.scope, null);
          },
          failed: (failure) => this.#fail(command, admission.scope, failure),
        };
        if (action === undefined) {
          outcome.failed(unimplemented(command.command_type));
          return;
        }
        const ollamaModel =
          command.command_type === 'execution.send_instruction' && this.#deps.ollamaUnload
            ? (ollamaModelFrom(
                this.#deps.projection.execution(execution.execution_id)?.model_ref,
              ) ?? this.#deps.ollamaUnload.modelFor(execution.execution_id))
            : null;
        // An execution an earlier control plane started is re-attached first, before a model is
        // loaded or the folder read for it, or the action fails as lost, having done nothing (ADR 0046).
        const reattached = this.#deps.reattacher?.ensure(execution) ?? null;
        const attached = (work: () => Promise<void>) =>
          reattached === null ? work() : reattached.then(work, () => undefined);
        const startAction = () =>
          reattached === null
            ? this.#perform(command, action, outcome)
            : reattached.then(
                () => this.#perform(command, action, outcome),
                (error: unknown) => outcome.failed(toFailure(error, this.#deps.redaction)),
              );
        const before: Promise<void>[] = [];
        const unloader = this.#deps.ollamaUnload;
        if (ollamaModel !== null && unloader) {
          before.push(attached(() => unloader.beforeRun(execution.execution_id, ollamaModel)));
        }
        // An instruction that starts a turn from rest: the folder is read again first, so what
        // changed while no turn ran, a restore or a person's edit, is not the coming turn's (OBS).
        const view = this.#deps.projection.execution(execution.execution_id);
        const files = this.#deps.files;
        if (
          command.command_type === 'execution.send_instruction' &&
          files &&
          view?.directory != null &&
          (view.status === 'completed' || view.status === 'failed' || view.status === 'interrupted')
        ) {
          const { directory } = view;
          const id = execution.execution_id;
          before.push(
            attached(() => bounded(files.beforeTurn(id, directory), () => files.abandon(id))),
          );
        }
        if (before.length === 0) void startAction();
        else void Promise.all(before).then(startAction, startAction);
        return;
      }
      default: {
        const unhandled: never = command;
        throw new Error(
          `unhandled command ${String((unhandled as { command_type?: unknown }).command_type)}`,
        );
      }
    }
  }

  /** Waits for the runtime to confirm the action, bounded by the command timeout. */
  async #perform(
    command: CommandEnvelope,
    action: () => Promise<unknown>,
    outcome: ActionOutcome,
  ): Promise<void> {
    const deadline = new AbortController();
    const work = Promise.resolve()
      .then(action)
      .then(
        () => ({ kind: 'succeeded' }) as const,
        (error: unknown) => ({ kind: 'failed', error }) as const,
      );
    const timer = this.#deps.scheduler.sleep(this.#deps.commandTimeoutMs, deadline.signal).then(
      () => ({ kind: 'timed_out' }) as const,
      () => ({ kind: 'cancelled' }) as const,
    );
    const first = await Promise.race([work, timer]);
    deadline.abort();
    try {
      switch (first.kind) {
        case 'succeeded':
          outcome.succeeded();
          return;
        case 'failed':
          if (!(first.error instanceof RuntimeActionError)) {
            // A bug in an adapter: its type and where it happened, for the owner to find, never its
            // message (errorForLog in http/server.ts).
            this.#deps.logger.warn(
              { command_id: command.command_id, err: first.error },
              'runtime adapter failed unexpectedly',
            );
          }
          outcome.failed(toFailure(first.error, this.#deps.redaction));
          return;
        case 'timed_out':
          outcome.failed({
            code: 'timeout',
            message: `The runtime did not confirm the action within ${this.#deps.commandTimeoutMs} ms. It may still take effect.`,
            effect: 'unknown',
          });
          void work.then((late) =>
            this.#deps.logger.warn(
              { command_id: command.command_id, late_outcome: late.kind },
              'runtime answered after the command timed out',
            ),
          );
          return;
        case 'cancelled':
          return;
        default: {
          const unhandled: never = first;
          throw new Error(`unhandled outcome ${String((unhandled as { kind?: unknown }).kind)}`);
        }
      }
    } catch (error) {
      this.#deps.logger.error(
        { err: error, command_id: command.command_id },
        'failed to record a command outcome',
      );
    }
  }

  #recordStartFailure(
    command: CommandEnvelope,
    scope: CommandScope,
    failure: CommandFailure,
  ): void {
    const { project_id, workstream_id, execution_id } = scope;
    if (project_id !== null && workstream_id !== null && execution_id !== null) {
      const executionScope = { project_id, workstream_id, execution_id };
      // A failure whose effect is unknown may have started the runtime anyway, so the
      // execution becomes unknown rather than failed.
      this.#deps.recorder.record(
        failure.effect === 'none'
          ? controlPlaneDraft(
              'execution.start_failed',
              executionScope,
              { error: { code: failure.code, message: failure.message } },
              this.#now(),
              causedBy(command.command_id),
            )
          : controlPlaneDraft(
              'execution.state_unknown',
              executionScope,
              { code: 'start_outcome_unknown', message: failure.message },
              this.#now(),
              causedBy(command.command_id),
            ),
      );
    }
    this.#fail(command, scope, failure);
  }

  /**
   * Makes a project, in the folder the choice names when there is one, begun from a starter when it
   * names one, and records it; the project's scope, or null once the command has failed.
   */
  #createProject(
    command: CommandEnvelope,
    commandScope: CommandScope,
    cause: Cause,
    name: string,
    choice: ProjectLocationChoice | null,
  ): { project_id: ProjectId; workstream_id: null; execution_id: null } | null {
    const { recorder } = this.#deps;
    let location: ProjectLocation | null = null;
    if (choice !== null) {
      const bound = this.#deps.locations.bind(choice);
      if (!bound.ok) {
        this.#fail(command, commandScope, {
          code: bound.code,
          message: bound.message,
          effect: 'effect' in bound ? bound.effect : 'none',
        });
        return null;
      }
      location = bound.location;
    }
    let applied: {
      starter_id: string;
      lockfile: string;
      files: number;
      template: string;
    } | null = null;
    if (choice?.kind === 'new_from_starter' && location !== null) {
      const result = this.#deps.starters?.apply(choice.starter_id, location.path) ?? {
        ok: false as const,
        message: 'This computer has no starters.',
      };
      if (!result.ok) {
        this.#fail(command, commandScope, {
          code: 'starter_not_applied',
          message: `A folder was made at ${location.path}, but the starter's files could not be put in it, so no project was created: ${result.message}`,
          effect: 'unknown',
        });
        return null;
      }
      applied = {
        starter_id: choice.starter_id,
        lockfile: result.lockfile,
        files: result.files,
        template: result.template,
      };
    }
    const projectId = this.#deps.ids.next() as ProjectId;
    const scope = { project_id: projectId, workstream_id: null, execution_id: null };
    recorder.record(
      controlPlaneDraft('project.created', scope, { name, location }, this.#now(), cause),
    );
    if (applied !== null)
      recorder.record(
        controlPlaneDraft('project.starter_applied', scope, applied, this.#now(), cause),
      );
    return scope;
  }

  /**
   * The project's failed check's first lines for the next instruction to an agent in it, unless an
   * agent was already given that check's (FX1); null otherwise, or when they cannot be read.
   */
  #checkOutputFor(projectId: ProjectId): Checked | null {
    let found: Checked | null;
    try {
      found = this.#deps.previews?.failedCheckOutput(projectId) ?? null;
    } catch {
      return null;
    }
    if (found === null) return null;
    return this.#deps.projection.checkOutputGiven(projectId) === found.checkId ? null : found;
  }

  /** Records that the runtime was given a failed check's lines: which, and how many; never the text. */
  #recordCheckOutput(scope: ExecutionContext, checked: Checked | null, cause: Cause): void {
    if (checked === null) return;
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.check_output_given',
        scope,
        {
          check_id: checked.checkId,
          parts: [...checked.output.parts],
          lines: checked.output.lines,
          bytes: checked.output.bytes,
        },
        this.#now(),
        cause,
      ),
    );
  }

  #complete(command: CommandEnvelope, scope: CommandScope, result: CommandResult | null): void {
    this.#deps.recorder.record(
      controlPlaneDraft(
        'command.completed',
        scope,
        { command_id: command.command_id, command_type: command.command_type, result },
        this.#now(),
        causedBy(command.command_id),
      ),
    );
  }

  #fail(command: CommandEnvelope, scope: CommandScope, failure: CommandFailure): void {
    this.#deps.recorder.record(
      controlPlaneDraft(
        'command.failed',
        scope,
        {
          command_id: command.command_id,
          command_type: command.command_type,
          failure: { ...failure, message: clip(failure.message, 'The command failed.') },
        },
        this.#now(),
        causedBy(command.command_id),
      ),
    );
    this.#deps.logger.info(
      { command_id: command.command_id, command_type: command.command_type, failure: failure.code },
      'command failed',
    );
  }

  #runtimeFor(admission: Admitted): { runtime: RuntimeDescriptor; adapter: RuntimeAdapter } {
    const runtime = admission.runtime;
    const adapter = runtime === null ? undefined : this.#deps.registry.adapter(runtime.runtime_id);
    if (runtime === null || adapter === undefined) {
      throw new Error('admitted a runtime command without a registered runtime');
    }
    return { runtime, adapter };
  }

  /**
   * Whether a command sent again under a known id is the one recorded. A refused command the
   * journal keeps only in part is compared by its keyed digest, and is a conflict once that is gone.
   */
  #sameAsKnown(
    known: CommandEnvelope,
    command: CommandEnvelope,
    principal: Principal | null,
  ): boolean {
    if (canonicalJson(known) === canonicalJson(command)) return true;
    const kept = journaledRejection(command);
    if (kept === command || canonicalJson(known) !== canonicalJson(kept)) return false;
    // Every client sees the refusal, all but what was answered. Were anyone told whether a guess
    // matches, a short answer such as a code could be recovered by resending guesses, so only the
    // principal that sent it is compared, and only a few times a minute; everyone else gets 409.
    const refused = this.#refusedDigests.get(command.command_id);
    if (refused === undefined || refused.by !== principalKey(principal)) return false;
    if (!this.#mayCompareResend(principal)) return false;
    return refused.digest === this.#digest(command);
  }

  #sameRequest(command: CommandEnvelope, principal: Principal | null): boolean {
    const kept = this.#requestDigests.get(command.command_id);
    return (
      kept !== undefined &&
      kept.by === principalKey(principal) &&
      this.#mayCompareResend(principal) &&
      kept.digest === this.#digest(command)
    );
  }

  #mayCompareResend(principal: Principal | null): boolean {
    const minute = Math.floor(this.#deps.clock.now().getTime() / 60_000);
    const key = principalKey(principal);
    const window = this.#resends.get(key);
    if (window === undefined || window.minute !== minute) {
      this.#resends.set(key, { minute, count: 1 });
      return true;
    }
    window.count += 1;
    return window.count <= REFUSED_RESENDS_PER_MINUTE;
  }

  /** The command as a rejection journals it, keeping a digest of what the journal leaves out. */
  #journaledRejection(command: CommandEnvelope, principal: Principal | null): CommandEnvelope {
    const kept = journaledRejection(command);
    if (kept !== command) {
      this.#refusedDigests.set(command.command_id, {
        digest: this.#digest(command),
        by: principalKey(principal),
      });
      if (this.#refusedDigests.size > REFUSED_ANSWERS_KEPT) {
        const oldest = this.#refusedDigests.keys().next().value;
        if (oldest !== undefined) this.#refusedDigests.delete(oldest);
      }
    }
    return kept;
  }

  #digest(command: CommandEnvelope): string {
    return createHmac('sha256', this.#digestKey).update(canonicalJson(command)).digest('base64url');
  }

  #view(command: CommandEnvelope): CommandView {
    const view = this.#deps.projection.command(command.command_id);
    if (view === undefined)
      throw new Error(`command ${command.command_id} is missing from the projection`);
    return view;
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString();
  }
}

function actionFor(
  command: CommandEnvelope,
  adapter: RuntimeAdapter,
  execution: ExecutionContext,
  checked: Checked | null,
): (() => Promise<void>) | undefined {
  switch (command.command_type) {
    case 'execution.send_instruction': {
      const send = adapter.sendInstruction;
      const text = withCheckOutput(command.payload.text, checked);
      return send && (() => send.call(adapter, { execution, text }));
    }
    case 'execution.respond_to_approval': {
      const respond = adapter.respondToApproval;
      return (
        respond &&
        (() =>
          respond.call(adapter, {
            execution,
            approval_id: command.payload.approval_id,
            decision: command.payload.decision,
            message: command.payload.message,
          }))
      );
    }
    case 'execution.answer_question': {
      const answer = adapter.answerQuestion;
      return (
        answer &&
        (() =>
          answer.call(adapter, {
            execution,
            question_id: command.payload.question_id,
            answers: command.payload.answers,
          }))
      );
    }
    case 'execution.interrupt': {
      const interrupt = adapter.interrupt;
      return interrupt && (() => interrupt.call(adapter, { execution }));
    }
    default:
      return undefined;
  }
}

/** A failed check's lines for an agent (FX1), and the check they came from. */
interface Checked {
  readonly checkId: CheckId;
  readonly output: CheckOutput;
}

/** The person's words, then, apart, the Mac's own check when there is one to give. */
function withCheckOutput(words: string, checked: Checked | null): string {
  return checked === null ? words : `${words}\n\n${checked.output.text}`;
}

function toExecutionContext(scope: CommandScope): ExecutionContext {
  const { project_id, workstream_id, execution_id } = scope;
  if (project_id === null || workstream_id === null || execution_id === null) {
    throw new Error('admitted an execution command without a resolved execution');
  }
  return { project_id, workstream_id, execution_id };
}

function toRuntimeRef(runtime: RuntimeDescriptor): RuntimeRef {
  return {
    runtime_id: runtime.runtime_id,
    kind: runtime.kind,
    display_name: runtime.display_name,
    synthetic: runtime.synthetic,
  };
}

function unimplemented(action: string): CommandFailure {
  return {
    code: 'capability_unimplemented',
    message: `The runtime adapter declares support for ${action} but does not implement it.`,
    effect: 'none',
  };
}

const CODE_PATTERN = /^[a-z][a-z0-9_]{0,63}$/;

function toFailure(error: unknown, redaction: Redaction): CommandFailure {
  if (error instanceof RuntimeActionError) {
    return {
      code: CODE_PATTERN.test(error.code) ? error.code : 'runtime_error',
      // The runtime's own words, as Codex's error answer at start, credentials taken out.
      message: clip(redaction.errorText(error.message), 'The runtime refused the action.'),
      effect: error.effect,
    };
  }
  // An unexpected exception says nothing about what the runtime did, and its message is not the
  // adapter's own words: a parse error quotes what it read, which can be an instruction or agent
  // text. Only its type is told.
  return {
    code: 'adapter_error',
    message: `The runtime adapter failed unexpectedly (${error instanceof Error ? error.name : typeof error}).`,
    effect: 'unknown',
  };
}

/** A message the contract accepts: at most 2000 characters, cut with an ellipsis, never empty. */
function clip(message: string, fallback: string): string {
  const trimmed = message.trim();
  return trimmed.length === 0 ? fallback : withinLimit(trimmed);
}

/** JSON with object keys sorted, so equal commands compare equal regardless of key order. */
function canonicalJson(value: unknown): string {
  return JSON.stringify(value, (_key, inner: unknown) =>
    inner !== null && typeof inner === 'object' && !Array.isArray(inner)
      ? Object.fromEntries(Object.entries(inner).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)))
      : inner,
  );
}

/** Who sent a command, as one key: inside the control plane, the local token, or one device. */
function principalKey(principal: Principal | null): string {
  if (principal === null) return 'internal';
  return principal.kind === 'device' ? `device:${principal.device_id}` : 'local';
}
