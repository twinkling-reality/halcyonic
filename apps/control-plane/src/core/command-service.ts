import { createHmac, randomBytes } from 'node:crypto';
import type {
  CatalogRepositoryRef,
  CheckId,
  CheckpointId,
  CommandEnvelope,
  CommandFailure,
  CommandId,
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
import { PAGES_BRANCH, WORLD_STARTER_ID } from '@halcyonic/contracts';
import {
  type Admission,
  admitCommand,
  type CommandScope,
  folderInUse,
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
import { pathList } from '../checkpoints/checkpointer.ts';
import type { ProjectKeeping } from '../checkpoints/keeping.ts';
import type { MergeConflict } from '../checkpoints/store.ts';
import {
  type AddOutcome,
  type FitOutcome,
  type PrepareResult,
  readingOf,
  type TaskCopies,
} from '../checkpoints/task-copies.ts';
import type { IdGenerator } from '../ids.ts';
import type { HostLocations } from '../locations.ts';
import type { Logger } from '../logger.ts';
import type { FileObserver } from '../observer/file-observer.ts';
import { OwnCheckRefused, type OwnChecks } from '../own-check/own-check.ts';
import type { CheckOutput } from '../preview/check-output.ts';
import type { HostPreviews } from '../preview/preview.ts';
import { type ProjectShares, ShareRefused } from '../preview/share.ts';
import { givenSecrets, type SecretStore } from '../secrets/store.ts';
import type { GitHubSender } from '../sending/github.ts';
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
import { addReview, locationReview } from './review.ts';
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
  /** Whether the instruction queue runs, so an instruction during a turn may wait for it (ADR 0056). */
  readonly queuesInstructions?: boolean;
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
  /** Every task's own copy of its project's folder (ADR 0045); absent, tasks work in the project's. */
  readonly copies?: TaskCopies | null;
  /** Show in Finder and Save a Copy on the Mac (F3); absent, both are refused. */
  readonly keeping?: ProjectKeeping | null;
  /** Shared links to a running preview (F1); absent, sharing is refused. */
  readonly shares?: ProjectShares | null;
  /** Send to GitHub with the person's own git and gh (ADR 0039, amended); absent, it is refused. */
  readonly github?: GitHubSender | null;
  /** The person's project secrets (ADR 0057); absent, a set fails and no start gets a secret. */
  readonly projectSecrets?: SecretStore | null;
  /** The host's checks and previews of starter projects (ADR 0037), or null when not set up. */
  readonly previews?: HostPreviews | null;
  /** A project's own check, chosen by the person (ADR 0055), or null when not set up. */
  readonly ownChecks?: OwnChecks | null;
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
    const admission = this.#reviewed(
      principal,
      this.#authorize(principal, this.#admit(command), command),
      command,
    );
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
    // Instructions still waiting were never sent: they fail as not sent, effect none, before the
    // pass below fails what may have reached a runtime as unknown (ADR 0056, AUDIT2 S1).
    for (const view of all) {
      for (const queued of view.queued_instructions ?? []) {
        this.failQueued(
          queued.command_id,
          'queued_stopped',
          'The turn did not finish (it was stopped, or your computer restarted), so your instruction was not sent.',
        );
      }
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
  /**
   * The review a command that changes a project's files or folder rests on (ADR 0051): a paired
   * device's `project.restore`, or its `project.set_location` that rebinds a project that has a
   * folder, must carry the digest of what the person read; a rebinding whose digest, from anyone,
   * no longer matches what is true now is refused. A restore's digest is checked in the restore's
   * own turn, against what it would change then, since that is read from the files.
   */
  #reviewed(
    principal: Principal | null,
    admission: Admission,
    command: CommandEnvelope,
  ): Admission {
    if (!admission.admitted) return admission;
    const refuse = (code: 'review_required' | 'review_stale', message: string): Admission => ({
      admitted: false,
      scope: admission.scope,
      rejection: { code, message },
    });
    const device = principal?.kind === 'device';
    if (command.command_type === 'project.restore') {
      if (device && command.payload.review === undefined)
        return refuse(
          'review_required',
          'Putting files back from a paired device needs the review the person read first.',
        );
      return admission;
    }
    if (command.command_type === 'workstream.add_to_project') {
      // The add's review is the latest reading of the task's copy, which its view shows.
      const workstreamId = command.payload.workstream_id;
      const copy = this.#deps.projection.workstream(workstreamId)?.copy;
      const tree = this.#deps.projection.reviewedTree(workstreamId);
      const current =
        copy?.state === 'own' && copy.to_add_files !== null && tree !== null
          ? addReview(workstreamId, copy.base_tree, tree, copy.to_add_files)
          : null;
      const review = command.payload.review;
      if (review === undefined)
        return device
          ? refuse(
              'review_required',
              "Adding a task's work from a paired device needs the review of its files the person read first.",
            )
          : admission;
      return review === current
        ? admission
        : refuse(
            'review_stale',
            "The task's files changed since they were shown. Look at them again, then add.",
          );
    }
    if (command.command_type !== 'project.set_location') return admission;
    const current = this.#deps.projection.project(command.payload.project_id)?.location ?? null;
    const review = command.payload.review;
    if (review === undefined) {
      return device && current !== null
        ? refuse(
            'review_required',
            'Moving a project to another folder from a paired device needs the review the person read first.',
          )
        : admission;
    }
    return review === locationReview(command.payload.project_id, command.payload.location, current)
      ? admission
      : refuse('review_stale', 'The project changed since the review was read. Read it again.');
  }

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
    if (command.command_type === 'project.own_check.choose' && !this.#deps.ownChecks)
      return {
        admitted: false,
        scope: { project_id: command.payload.project_id, workstream_id: null, execution_id: null },
        rejection: {
          code: 'capability_unsupported',
          message: "A project's own check is not set up on this computer.",
        },
      };
    // A project not made from a starter runs its own check, which needs no starter (ADR 0055).
    const ownCheck =
      command.command_type === 'project.check' &&
      this.#deps.projection.project(command.payload.project_id)?.starter === null;
    if (ownCheck && !this.#deps.ownChecks)
      return {
        admitted: false,
        scope: { project_id: command.payload.project_id, workstream_id: null, execution_id: null },
        rejection: {
          code: 'capability_unsupported',
          message: "A project's own check is not set up on this computer.",
        },
      };
    if (
      ((command.command_type === 'project.check' && !ownCheck) ||
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
    const admission = admitCommand(command, this.#deps.projection, {
      get: (runtimeId) => this.#deps.registry.get(runtimeId),
      queuesInstructions: this.#deps.queuesInstructions === true,
    });
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
          // A task with a copy of its own works only there (ADR 0045), and only where copies are
          // on and its agent is held to that folder.
          const copy =
            admission.scope.workstream_id === null
              ? undefined
              : this.#deps.projection.workstream(admission.scope.workstream_id)?.copy;
          if (copy?.state === 'own') {
            if (!this.#deps.copies)
              return refuse(
                'invalid_state',
                "This task's copy is off on this computer. Turn task copies on to go on with it, or start another task.",
              );
            if (!this.#deps.copies.holds(command.payload.runtime_id, copy.directory))
              return refuse(
                'capability_unsupported',
                'This task works in its own copy, and this agent is not kept to one folder on this computer. Start it with an agent that is, or start another task.',
              );
          }
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

  /** Records what an add did, then its command's outcome (ADR 0045). */
  #added(
    command: CommandEnvelope,
    scope: CommandScope,
    where: { project_id: ProjectId; workstream_id: WorkstreamId; execution_id: null },
    outcome: AddOutcome,
    cause: Cause,
  ): void {
    switch (outcome.kind) {
      case 'unreviewed':
        // The person sees the copy as it is now, then presses again.
        this.#deps.recorder.record(
          controlPlaneDraft(
            'workstream.copy_changes_read',
            where,
            readingOf(
              where.workstream_id,
              outcome.base,
              outcome.taskTree,
              outcome.changed,
              outcome.files,
            ),
            this.#now(),
            cause,
          ),
        );
        this.#fail(command, scope, {
          code: 'changed_since_review',
          message:
            "This task's files changed since they were last shown, so nothing was added. Look at them again, then add.",
          effect: 'none',
        });
        return;
      case 'nothing':
        this.#fail(command, scope, {
          code: 'nothing_to_add',
          message: 'This task changed nothing the project does not already have.',
          effect: 'none',
        });
        return;
      case 'conflicted': {
        const conflicted = conflictedFiles(outcome.conflicts);
        if (conflicted !== null)
          this.#deps.recorder.record(
            controlPlaneDraft(
              'workstream.add_conflicted',
              where,
              { task_tree: outcome.taskTree, project_tree: outcome.projectTree, conflicted },
              this.#now(),
              cause,
            ),
          );
        this.#fail(command, scope, {
          code: 'changed_in_both',
          message: 'The project and this task changed the same places, so nothing was added.',
          effect: 'none',
        });
        return;
      }
      case 'added':
        this.#deps.recorder.record(
          controlPlaneDraft(
            'workstream.added',
            where,
            {
              checkpoint_id: outcome.checkpointId as CheckpointId,
              task_tree: outcome.taskTree,
              project_tree: outcome.projectTree,
              merged_tree: outcome.mergedTree,
              added: pathList(outcome.added),
            },
            this.#now(),
            cause,
          ),
        );
        this.#complete(command, scope, {
          kind: 'workstream_added',
          checkpoint_id: outcome.checkpointId as CheckpointId,
          files: outcome.added.length,
        });
        return;
      case 'failed':
        this.#fail(command, scope, {
          code: outcome.code,
          message: outcome.message,
          effect: outcome.written ? 'unknown' : 'none',
        });
        return;
      default: {
        const unhandled: never = outcome;
        throw new Error(`unhandled add outcome ${String(unhandled)}`);
      }
    }
  }

  /**
   * Records a fit, completes its command, then asks the task's agent to fit its work, where its
   * runtime takes an instruction at rest; otherwise the person starts it (ADR 0045).
   */
  #fitted(
    command: CommandEnvelope,
    scope: CommandScope,
    where: { project_id: ProjectId; workstream_id: WorkstreamId; execution_id: null },
    outcome: FitOutcome,
    cause: Cause,
  ): void {
    if (outcome.kind === 'failed') {
      this.#fail(command, scope, {
        code: outcome.code,
        message: outcome.message,
        effect: outcome.written ? 'unknown' : 'none',
      });
      return;
    }
    this.#deps.recorder.record(
      controlPlaneDraft(
        'workstream.fitted',
        where,
        {
          project_tree: outcome.projectTree,
          fitted_tree: outcome.fittedTree,
          written: pathList(outcome.written),
        },
        this.#now(),
        cause,
      ),
    );
    this.#complete(
      command,
      scope,
      outcome.written.length === 0
        ? null
        : { kind: 'workstream_fitted', files: outcome.written.length },
    );
    const text = fitInstruction(outcome.conflicts);
    const current =
      this.#deps.projection.workstream(where.workstream_id)?.current_execution_id ?? null;
    if (text === null || current === null) return;
    const execution = this.#deps.projection.execution(current);
    if (
      !execution ||
      !this.#deps.registry.get(execution.runtime.runtime_id)?.capabilities.instruct_at_rest
    )
      return;
    this.submit(
      {
        schema_version: command.schema_version,
        command_id: this.#deps.ids.next() as CommandId,
        issued_at: this.#now(),
        client: { name: 'halcyonic-control-plane', version: null, device_label: null },
        command_type: 'execution.send_instruction',
        payload: { execution_id: current as ExecutionId, text },
      },
      'internal',
      null,
    );
  }

  /** The folder of the task's own copy, or null while it has none (ADR 0045). */
  #ownCopy(workstreamId: string): string | null {
    const copy = this.#deps.projection.workstream(workstreamId)?.copy;
    return copy?.state === 'own' ? copy.directory : null;
  }

  /** The version a task starts from, with its tree, or null for one from the folder as it is. */
  #versionOf(workstreamId: WorkstreamId): {
    readonly checkpointId: string;
    readonly tree: string;
    readonly files: number;
    readonly recordedAt: string | null;
  } | null {
    const workstream = this.#deps.projection.workstream(workstreamId);
    const from = workstream?.from_checkpoint;
    if (workstream === undefined || from === undefined) return null;
    const checkpoint = this.#deps.projection
      .checkpoints(workstream.project_id)
      .find((candidate) => candidate.checkpoint_id === from);
    // A version the journal no longer holds still never falls back to the folder as it is.
    return {
      checkpointId: from,
      tree: checkpoint?.tree ?? '',
      files: checkpoint?.files ?? 0,
      recordedAt: checkpoint?.recorded_at ?? null,
    };
  }

  /**
   * Makes the task's copy and records it, or records why it has none; resolves to the folder the
   * start works in: the copy, else the project's folder, asked again of the folder rule just before,
   * since starts beside it were admitted while the copy was being made (FOLDER, ADR 0045).
   */
  async #makeCopy(
    copies: TaskCopies,
    scope: CommandScope,
    projectFolder: string,
    runtimeId: string,
    cause: Cause,
  ): Promise<
    | { readonly ok: true; readonly directory: string }
    | { readonly ok: false; readonly failure: CommandFailure }
  > {
    const { project_id, workstream_id } = scope;
    if (project_id === null || workstream_id === null)
      return { ok: true, directory: projectFolder };
    const where = { project_id, workstream_id, execution_id: null };
    // A task from a version: its copy, turned into that version, or no start at all; it never falls
    // back to the project's folder, which holds another version (ADR 0045).
    const version = this.#versionOf(workstream_id);
    if (version !== null) {
      if (!copies.holds(runtimeId, projectFolder))
        return {
          ok: false,
          failure: {
            code: 'capability_unsupported',
            message:
              "This task starts from an older version, so it works only in a copy of its own, and this task's agent can't be kept to one folder on this computer. Choose another agent.",
            effect: 'none',
          },
        };
      if (version.tree === '')
        return {
          ok: false,
          failure: {
            code: 'checkpoint_not_found',
            message: 'The version this task starts from is no longer kept.',
            effect: 'none',
          },
        };
      let fromVersion: PrepareResult;
      try {
        fromVersion = await copies.prepare(project_id, workstream_id, projectFolder, version);
      } catch {
        this.#deps.logger.warn({ workstream_id }, 'task copy from a version failed');
        fromVersion = { ok: false, code: 'copy_failed', message: 'The copy of the folder failed.' };
      }
      if (!fromVersion.ok)
        return {
          ok: false,
          failure: { code: fromVersion.code, message: fromVersion.message, effect: 'none' },
        };
      this.#deps.recorder.record(
        controlPlaneDraft(
          'workstream.copy_made',
          where,
          {
            directory: fromVersion.directory,
            base_tree: fromVersion.baseTree,
            files: fromVersion.files,
          },
          this.#now(),
          cause,
        ),
      );
      return { ok: true, directory: fromVersion.directory };
    }
    // A world changes when the person speaks to it (ADR 0049): its tasks change the project in place,
    // so the world the headset shows follows each turn. They get no copy, and work in the project's
    // folder one at a time, as before copies (ADR 0045).
    if (this.#deps.projection.project(project_id)?.starter === WORLD_STARTER_ID) {
      const busy = folderInUse(this.#deps.projection, projectFolder, workstream_id);
      return busy === null
        ? { ok: true, directory: projectFolder }
        : { ok: false, failure: { code: 'folder_in_use', message: busy.message, effect: 'none' } };
    }
    let made: PrepareResult;
    try {
      // A runtime not held to the folder it is given could write other tasks' copies from one.
      made = copies.holds(runtimeId, projectFolder)
        ? await copies.prepare(project_id, workstream_id, projectFolder)
        : {
            ok: false,
            code: 'runtime_unconfined',
            message:
              "This task's agent is not kept to one folder on this computer, so the task works in the project's folder, one task at a time.",
          };
    } catch {
      // What failed may name the person's folders, so only the task is logged.
      this.#deps.logger.warn({ workstream_id }, 'task copy failed');
      made = { ok: false, code: 'copy_failed', message: 'The copy of the folder failed.' };
    }
    if (made.ok) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'workstream.copy_made',
          where,
          { directory: made.directory, base_tree: made.baseTree, files: made.files },
          this.#now(),
          cause,
        ),
      );
      return { ok: true, directory: made.directory };
    }
    this.#deps.recorder.record(
      controlPlaneDraft(
        'workstream.copy_refused',
        where,
        { code: made.code, message: made.message },
        this.#now(),
        cause,
      ),
    );
    const busy = folderInUse(this.#deps.projection, projectFolder, workstream_id);
    if (busy !== null)
      return {
        ok: false,
        failure: { code: 'folder_in_use', message: busy.message, effect: 'none' },
      };
    return { ok: true, directory: projectFolder };
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
      case 'project.share.start':
      case 'project.share.stop': {
        // An address for the checked build the preview serves (F1, ADR 0054), the person's own step.
        const scope = admission.scope;
        const shares = this.#deps.shares;
        const projectId = command.payload.project_id;
        if (!shares) {
          this.#fail(command, scope, {
            code: 'unavailable',
            message: 'This computer cannot share a game.',
            effect: 'none',
          });
          return;
        }
        if (command.command_type === 'project.share.stop') {
          void shares.stop(projectId, 'asked', cause).then(
            () => this.#complete(command, scope, null),
            () =>
              this.#fail(command, scope, {
                code: 'share_failed',
                message: 'The shared link could not be stopped.',
                effect: 'unknown',
              }),
          );
          return;
        }
        const way = command.payload.way;
        void shares.start(projectId, way, cause).then(
          (shared) =>
            this.#complete(command, scope, {
              kind: 'share_started',
              check_id: shared.check_id,
              url: shared.url,
              way: shared.way,
            }),
          (error: unknown) =>
            this.#fail(
              command,
              scope,
              error instanceof ShareRefused
                ? { code: error.code, message: error.message, effect: 'none' }
                : {
                    code: 'share_failed',
                    message:
                      way === 'link'
                        ? 'The game could not be shared: no link answered. Try again.'
                        : 'The game could not be shared on the Wi-Fi: its address did not answer. Try again.',
                    effect: 'none',
                  },
            ),
        );
        return;
      }
      case 'project.publish':
      case 'project.unpublish': {
        // The person's own reviewed publish of a checked game (ADR 0039, amended); never automatic.
        const scope = admission.scope;
        const projectId = command.payload.project_id;
        const folder = this.#projectDirectory(scope);
        const github = this.#deps.github;
        if (!github || folder === null) {
          this.#fail(command, scope, {
            code: 'unavailable',
            message: "Publishing to GitHub isn't set up on this computer.",
            effect: 'none',
          });
          return;
        }
        const where = { project_id: projectId, workstream_id: null, execution_id: null };
        const lost = () =>
          this.#fail(command, scope, {
            code: 'publish_failed',
            message: 'The change could not be finished. Look on GitHub before trying again.',
            effect: 'unknown',
          });
        if (command.command_type === 'project.unpublish') {
          const published = this.#deps.projection.project(projectId)?.published;
          if (published === undefined)
            throw new Error('admitted project.unpublish with nothing published');
          void github.unpublish(projectId, folder, published).then((done) => {
            if (!done.ok) {
              this.#fail(command, scope, {
                code: done.code,
                message: done.message,
                effect: 'none',
              });
              return;
            }
            this.#deps.recorder.record(
              controlPlaneDraft(
                'project.unpublished',
                where,
                { repository: done.repository, branch: PAGES_BRANCH },
                this.#now(),
                cause,
              ),
            );
            this.#complete(command, scope, null);
          }, lost);
          return;
        }
        const check = this.#deps.projection.latestCheck(projectId);
        if (check === undefined) {
          this.#fail(command, scope, {
            code: 'invalid_state',
            message: 'Only a passed check can be published. Check the game first.',
            effect: 'none',
          });
          return;
        }
        const built = this.#deps.previews?.builtCopy(projectId, check.check_id) ?? null;
        const replaces = this.#deps.projection.project(projectId)?.published?.commit ?? null;
        void github
          .publish(projectId, folder, check.check_id, built, replaces, command.payload.review)
          .then((done) => {
            if (!done.ok) {
              this.#fail(command, scope, {
                code: done.code,
                message: done.message,
                effect: 'none',
              });
              return;
            }
            this.#deps.recorder.record(
              controlPlaneDraft(
                'project.published',
                where,
                {
                  repository: done.publish.repository,
                  check_id: check.check_id,
                  branch: PAGES_BRANCH,
                  commit: done.commit,
                  address: done.address,
                },
                this.#now(),
                cause,
              ),
            );
            this.#complete(command, scope, {
              kind: 'published',
              commit: done.commit,
              address: done.address,
            });
          }, lost);
        return;
      }
      case 'project.send_to_github': {
        // The person's own reviewed send (ADR 0039, amended): never an agent's, never automatic.
        const scope = admission.scope;
        const folder = this.#projectDirectory(scope);
        const github = this.#deps.github;
        if (!github || folder === null) {
          this.#fail(command, scope, {
            code: 'unavailable',
            message: "Sending to GitHub isn't set up on this computer.",
            effect: 'none',
          });
          return;
        }
        const others = this.#deps.projection
          .projects()
          .filter((project) => project.project_id !== command.payload.project_id)
          .flatMap((project) => (project.location === null ? [] : [project.location.path]));
        void github
          .send(
            command.payload.project_id,
            folder,
            others,
            command.payload.title,
            command.payload.review,
          )
          .then(
            (sent) => {
              if (!sent.ok) {
                this.#fail(command, scope, {
                  code: sent.code,
                  message: sent.message,
                  effect: 'none',
                });
                return;
              }
              this.#deps.recorder.record(
                controlPlaneDraft(
                  'project.sent_to_github',
                  {
                    project_id: command.payload.project_id,
                    workstream_id: null,
                    execution_id: null,
                  },
                  {
                    repository: sent.send.repository,
                    branch: sent.send.branch,
                    base_commit: sent.send.base_commit,
                    new_branch: sent.send.new_branch,
                    commit: sent.commit,
                    pull_request_url: sent.pullRequestUrl,
                  },
                  this.#now(),
                  cause,
                ),
              );
              this.#complete(command, scope, {
                kind: 'sent_to_github',
                new_branch: sent.send.new_branch,
                commit: sent.commit,
                pull_request_url: sent.pullRequestUrl,
              });
            },
            () =>
              this.#fail(command, scope, {
                code: 'send_failed',
                message: 'The send could not be finished. Look on GitHub before sending again.',
                effect: 'unknown',
              }),
          );
        return;
      }
      case 'project.show_in_finder':
      case 'project.save_copy': {
        // Keeping what was made (F3): the project's own folder, never a task's copy.
        const scope = admission.scope;
        const folder = this.#projectDirectory(scope);
        const keeping = this.#deps.keeping;
        if (!keeping || folder === null) {
          this.#fail(command, scope, {
            code: 'unavailable',
            message: 'This computer cannot show or save the project.',
            effect: 'none',
          });
          return;
        }
        const failed = () =>
          this.#fail(command, scope, {
            code: 'save_failed',
            message: 'The project could not be shown or saved.',
            effect: 'unknown',
          });
        if (command.command_type === 'project.show_in_finder') {
          void keeping.show(folder).then((shown) => {
            if (shown.ok) this.#complete(command, scope, null);
            else
              this.#fail(command, scope, {
                code: shown.code,
                message: shown.message,
                effect: 'none',
              });
          }, failed);
        } else {
          const others = this.#deps.projection
            .projects()
            .filter((project) => project.project_id !== command.payload.project_id)
            .flatMap((project) => (project.location === null ? [] : [project.location.path]));
          void keeping.save(command.payload.project_id, folder, others).then((saved) => {
            if (saved.ok)
              this.#complete(command, scope, {
                kind: 'project_saved',
                file: saved.file,
                files: saved.files,
                bytes: saved.bytes,
              });
            else
              this.#fail(command, scope, {
                code: saved.code,
                message: saved.message,
                effect: 'none',
              });
          }, failed);
        }
        return;
      }
      case 'workstream.add_to_project':
      case 'workstream.fit_to_project': {
        // The person's own step that brings a task's work into its project (ADR 0045).
        const scope = admission.scope;
        const workstream = this.#deps.projection.workstream(command.payload.workstream_id);
        const copy = workstream?.copy;
        const folder = this.#projectDirectory(scope);
        const copies = this.#deps.copies;
        const checkpoints = this.#deps.checkpoints;
        if (!copies || !checkpoints || copy?.state !== 'own' || folder === null || !workstream) {
          this.#fail(command, scope, {
            code: 'unavailable',
            message: "This computer does not keep tasks' copies, so there is nothing to add.",
            effect: 'none',
          });
          return;
        }
        const request = {
          copy: copy.directory,
          base: copy.base_tree,
          folder,
          reviewed: this.#deps.projection.reviewedTree(workstream.workstream_id),
        };
        const where = {
          project_id: workstream.project_id,
          workstream_id: workstream.workstream_id,
          execution_id: null,
        };
        const unexpected = () =>
          this.#fail(command, scope, {
            code: 'store_failed',
            message: "The task's work could not be added.",
            effect: 'unknown',
          });
        if (command.command_type === 'workstream.add_to_project') {
          void copies
            .add(workstream.project_id, workstream.workstream_id, request, () =>
              checkpoints.beforeAdd(workstream.project_id, folder, cause),
            )
            .then((outcome) => this.#added(command, scope, where, outcome, cause), unexpected);
        } else {
          void copies
            .fit(workstream.project_id, workstream.workstream_id, request)
            .then((outcome) => this.#fitted(command, scope, where, outcome, cause), unexpected);
        }
        return;
      }
      case 'project.secret.set':
      case 'project.secret.remove': {
        // Names only (ADR 0057): the value is the store's, staged by this Mac's page for this command.
        const scope = admission.scope;
        const { project_id: projectId, name } = command.payload;
        const store = this.#deps.projectSecrets;
        const where = { project_id: projectId, workstream_id: null, execution_id: null };
        if (command.command_type === 'project.secret.remove') {
          store?.remove(projectId, name);
          recorder.record(
            controlPlaneDraft('project.secret.removed', where, { name }, this.#now(), cause),
          );
          this.#complete(command, scope, null);
          return;
        }
        const kept = store?.commit(command.command_id, projectId, name) ?? null;
        if (kept === null) {
          this.#fail(command, scope, {
            code: 'unavailable',
            message:
              'A secret is set with its value in its own request, so this command alone sets nothing.',
            effect: 'none',
          });
          return;
        }
        recorder.record(
          controlPlaneDraft(
            'project.secret.stored',
            where,
            { name, replaced: kept.replaced },
            this.#now(),
            cause,
          ),
        );
        this.#complete(command, scope, null);
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
      case 'project.own_check.choose':
      case 'project.check':
      case 'project.preview.start':
      case 'project.preview.stop':
      case 'project.open_on_mac': {
        const own = this.#deps.ownChecks;
        // A project's own check (ADR 0055): the host's own work too, bounded by each script's limit.
        if (
          own &&
          (command.command_type === 'project.own_check.choose' ||
            (command.command_type === 'project.check' &&
              this.#deps.projection.project(command.payload.project_id)?.starter === null))
        ) {
          const run =
            command.command_type === 'project.own_check.choose'
              ? own.choose(command)
              : own.check(command as CommandOf<'project.check'>);
          void run
            .then(
              ({ check_id, passed }) =>
                this.#complete(command, admission.scope, {
                  kind: 'project_checked',
                  check_id,
                  passed,
                }),
              (error: unknown) => {
                this.#deps.logger.error(
                  { err: error, command_id: command.command_id },
                  `${command.command_type} failed`,
                );
                this.#fail(
                  command,
                  admission.scope,
                  error instanceof OwnCheckRefused
                    ? { code: error.code, message: error.message, effect: 'none' }
                    : {
                        code: 'check_failed',
                        message:
                          'The check stopped on an error before it could record what it found.',
                        effect: 'unknown',
                      },
                );
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
        if (command.command_type === 'project.own_check.choose')
          throw new Error('admitted project.own_check.choose with own checks off');
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
        // A task from a version needs a copy of its own and the version kept (ADR 0045).
        if (
          command.payload.from_checkpoint !== undefined &&
          (!this.#deps.copies || !this.#deps.checkpoints)
        ) {
          this.#fail(command, admission.scope, {
            code: 'capability_unsupported',
            message: 'Starting a task from a version needs task copies and undo on this computer.',
            effect: 'none',
          });
          return;
        }
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
            {
              title: command.payload.title,
              objective: command.payload.objective,
              ...(command.payload.from_checkpoint !== undefined && {
                from_checkpoint: command.payload.from_checkpoint,
              }),
            },
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
        let firstFromVersion: string | null = null;
        // Admission checked it; the adapter asks the host's policy again before anything runs.
        const begin = (directory: string | null): void => {
          // The project's secrets as they are at this start, for a runtime that takes them (ADR 0057).
          const given = givenSecrets(
            this.#deps.projectSecrets,
            this.#deps.projection,
            project_id,
            runtime,
          );
          const names = Object.keys(given).sort();
          recorder.record(
            controlPlaneDraft(
              'execution.created',
              scope,
              {
                runtime: toRuntimeRef(runtime),
                instruction: command.payload.instruction,
                directory,
                ...(names.length > 0 && { secret_names: names }),
              },
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
          const checked = this.#checkOutputFor(project_id, workstream_id);
          const run = () =>
            this.#perform(
              command,
              () =>
                start.call(adapter, {
                  execution,
                  instruction: withCheckOutput(
                    withVersionNote(command.payload.instruction, firstFromVersion),
                    checked,
                  ),
                  options: command.payload.options,
                  model_ref: command.payload.model_ref,
                  ...(command.payload.approval_level !== undefined && {
                    approval_level: command.payload.approval_level,
                  }),
                  ...(command.payload.plan === true && { plan: true }),
                  directory,
                  ...(names.length > 0 && { environment: given }),
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
        };
        // A task works in its own copy of the project's folder, made before its first start (ADR 0045).
        const projectFolder = runtime.uses_project_location
          ? this.#projectDirectory(admission.scope)
          : null;
        const own = this.#ownCopy(workstream_id);
        const copies = this.#deps.copies;
        // A task from a version works only in its own copy of that version, never the folder as it is.
        const version = this.#versionOf(workstream_id);
        // Its agent is told which version it works on, on the task's first run that has a turn: a
        // start that failed before its turn leaves the next one to say it (AUDIT3 S3).
        const turnedBefore =
          this.#deps.projection
            .workstream(workstream_id)
            ?.execution_ids.some(
              (id) => (this.#deps.projection.execution(id)?.turn_count ?? 0) > 0,
            ) ?? false;
        firstFromVersion = version !== null && !turnedBefore ? version.recordedAt : null;
        if (version !== null && own === null && (projectFolder === null || !copies)) {
          this.#fail(command, admission.scope, {
            code: 'capability_unsupported',
            message:
              'This task starts from an older version, so it needs a copy of its own, which this computer cannot make for this agent.',
            effect: 'none',
          });
          return;
        }
        if (projectFolder !== null && own === null && copies) {
          void this.#makeCopy(
            copies,
            admission.scope,
            projectFolder,
            runtime.runtime_id,
            cause,
          ).then((where) => {
            if (where.ok) begin(where.directory);
            else this.#fail(command, admission.scope, where.failure);
          });
          return;
        }
        begin(projectFolder === null ? null : (own ?? projectFolder));
        return;
      }
      case 'execution.drop_queued_instruction': {
        // Dropped before it was sent: its own command fails with nothing done (ADR 0056).
        const dropped = this.#deps.projection.commandEnvelope(command.payload.command_id);
        if (dropped !== undefined)
          this.#fail(dropped, admission.scope, {
            code: 'queued_dropped',
            message: 'You dropped this instruction before it was sent.',
            effect: 'none',
          });
        this.#complete(command, admission.scope, null);
        return;
      }
      case 'execution.send_instruction':
      case 'execution.respond_to_approval':
      case 'execution.answer_question':
      case 'execution.interrupt': {
        if (command.command_type === 'execution.send_instruction' && admission.queued === true) {
          // Held for the turn to end (ADR 0056): the instruction queue sends it, or fails it unsent.
          const waiting =
            this.#deps.projection.execution(command.payload.execution_id)?.queued_instructions ??
            [];
          recorder.record(
            controlPlaneDraft(
              'execution.instruction.queued',
              toExecutionContext(admission.scope),
              { command_id: command.command_id, position: waiting.length + 1 },
              this.#now(),
              causedBy(command.command_id),
            ),
          );
          return;
        }
        const { adapter } = this.#runtimeFor(admission);
        const execution = toExecutionContext(admission.scope);
        // A failed check's first lines go after the person's words, once per check (FX1).
        const checked =
          command.command_type === 'execution.send_instruction'
            ? this.#checkOutputFor(execution.project_id, execution.workstream_id)
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
  #checkOutputFor(projectId: ProjectId, workstreamId: WorkstreamId | null): Checked | null {
    let found: Checked | null;
    try {
      // A project not made from a starter has its own check's lines, if any (ADR 0055).
      found =
        this.#deps.projection.project(projectId)?.starter === null
          ? (this.#deps.ownChecks?.failedOutput(projectId, workstreamId) ?? null)
          : (this.#deps.previews?.failedCheckOutput(projectId, workstreamId) ?? null);
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

  /**
   * Sends an instruction that waited for its turn to end (ADR 0056), as an instruction at rest is
   * sent: the same reads before the turn and the same completion once the runtime confirms. False
   * when it is no longer waiting or its runtime is gone, and then it is failed unsent.
   */
  deliverQueued(commandId: string): boolean {
    const command = this.#deps.projection.commandEnvelope(commandId);
    if (command?.command_type !== 'execution.send_instruction') return false;
    const view = this.#deps.projection.execution(command.payload.execution_id);
    const scope: CommandScope = {
      project_id: view?.project_id ?? null,
      workstream_id: view?.workstream_id ?? null,
      execution_id: command.payload.execution_id,
    };
    const runtime =
      view === undefined ? undefined : this.#deps.registry.get(view.runtime.runtime_id);
    if (
      view === undefined ||
      runtime === undefined ||
      !(view.queued_instructions ?? []).some((queued) => queued.command_id === commandId)
    ) {
      if (view !== undefined)
        this.failQueued(
          commandId,
          'queued_runtime_gone',
          'Its agent is no longer here, so it was not sent.',
        );
      return false;
    }
    // No longer waiting from here: a drop or a restore cannot fail it unsent once its send began
    // (AUDIT2); it ends as any sent instruction does.
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.instruction.dequeued',
        toExecutionContext(scope),
        { command_id: command.command_id },
        this.#now(),
        causedBy(command.command_id),
      ),
    );
    this.#dispatch(
      command,
      { admitted: true, policy: 'low_consequence', scope, runtime },
      causedBy(command.command_id),
      null,
    );
    return true;
  }

  /** Fails an instruction that waited for its turn, never sent, with why (ADR 0056). */
  failQueued(commandId: string, code: string, message: string): void {
    const command = this.#deps.projection.commandEnvelope(commandId);
    if (command?.command_type !== 'execution.send_instruction') return;
    const view = this.#deps.projection.execution(command.payload.execution_id);
    if (!(view?.queued_instructions ?? []).some((queued) => queued.command_id === commandId))
      return;
    this.#fail(
      command,
      {
        project_id: view?.project_id ?? null,
        workstream_id: view?.workstream_id ?? null,
        execution_id: command.payload.execution_id,
      },
      { code, message, effect: 'none' },
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
      const leave = command.payload.leave_plan === true;
      return (
        send && (() => send.call(adapter, { execution, text, ...(leave && { leave_plan: true }) }))
      );
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

/**
 * The person's words, then, apart, the Mac's own line on a task's first start from a version (ADR
 * 0045): which version its folder holds, and that the project's folder is not changed by it.
 */
function withVersionNote(words: string, recordedAt: string | null | undefined): string {
  if (recordedAt === undefined || recordedAt === null) return words;
  return `${words}\n\nFrom Halcyonic: this task's folder is a copy of the project as it was in the version of ${recordedAt}, not as it is now. The project's own folder changes only when the person adds this task's work.`;
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

/**
 * The files where a task and its project met, as the contract carries them: those with names it
 * can list, at most a hundred, and how many there were. Null when none can be listed.
 */
function conflictedFiles(
  conflicts: readonly MergeConflict[],
): { files: { path: string; kind: MergeConflict['kind'] }[]; total: number } | null {
  const listed = pathList(conflicts.map((conflict) => conflict.path)).paths;
  const files = conflicts
    .filter((conflict) => listed.includes(conflict.path))
    .map((conflict) => ({ path: conflict.path, kind: conflict.kind }));
  return files.length === 0 ? null : { files, total: conflicts.length };
}

/**
 * What the task's agent is asked after a fit (ADR 0045): the files where its work and the project's
 * met, how they are marked, and what to keep. Null when they met nowhere. The names are the files'
 * own, which an agent chose: they are given to the agent, never shown as Halcyonic's words.
 */
export function fitInstruction(conflicts: readonly MergeConflict[]): string | null {
  if (conflicts.length === 0) return null;
  const marked = conflicts.filter((conflict) => conflict.kind === 'text').map((c) => c.path);
  const replaced = conflicts.filter((conflict) => conflict.kind !== 'text').map((c) => c.path);
  const lines = [
    'The project changed in the same places as your work, and its changes are now in your folder too.',
  ];
  if (marked.length > 0)
    lines.push(
      `In ${marked.join(', ')}, each place where both changed is marked: your version and the project's sit between <<<<<<<, ======= and >>>>>>> lines. Keep what both meant, remove the marks, and change nothing else.`,
    );
  if (replaced.length > 0)
    lines.push(
      `${replaced.join(', ')} could not be marked and now hold the project's version. Redo your change to them if it is still needed.`,
    );
  return lines.join(' ').slice(0, 32000);
}
