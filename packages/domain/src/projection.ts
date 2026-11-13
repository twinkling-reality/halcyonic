import type {
  ApprovalLevel,
  ApprovalView,
  AssistantHostedModel,
  CatalogRepositoryRef,
  ChangedFiles,
  CheckFilesState,
  CheckFindings,
  CheckId,
  CheckpointId,
  CheckpointView,
  CommandEnvelope,
  CommandFailure,
  CommandId,
  CommandOf,
  CommandRejection,
  CommandResult,
  CommandStatus,
  CommandView,
  DeviceView,
  EntityChanges,
  EventEnvelope,
  EventId,
  ExecutionId,
  ExecutionStatus,
  ExecutionView,
  HistoryEntry,
  HistoryFolder,
  OwnCheckFindings,
  OwnCheckManager,
  OwnCheckScript,
  Principal,
  ProjectId,
  ProjectLocation,
  ProjectOwnCheckView,
  ProjectPublishedView,
  ProjectView,
  ProjectWorldView,
  QuestionView,
  RuntimeEvent,
  RuntimeOptions,
  SecretName,
  ShareWay,
  SpendingCapView,
  StoredEvent,
  Timestamp,
  ToolResultSummary,
  WorkspaceRequestStep,
  WorkspaceRequestView,
  WorkstreamCopy,
  WorkstreamId,
  WorkstreamView,
} from '@halcyonic/contracts';
import { fitQuestion, SPENDING_CAP_REACHED, TOOL_RESULT_RECENT } from '@halcyonic/contracts';
import { deriveAttention } from './attention.ts';
import { DeviceRegistry, type DeviceRegistryState } from './devices.ts';
import {
  addTurnUsage,
  capOnTurnEnded,
  capOnUsage,
  createExecutionState,
  deriveExecutionStatus,
  type ExecutionState,
  endTurn,
  newSpendingCap,
  projectUsage,
  spendingAllowance,
  spendingCapView,
  toExecutionView,
} from './execution-state.ts';
import { fixSteps } from './fix-steps.ts';
import { foldersOverlap } from './folders.ts';
import { COMMAND_SAYING } from './policy.ts';
import { shownQuestions } from './questions.ts';

/** Something in the journal that the projection could not apply as written. */
export interface ProjectionNote {
  code: 'unknown_entity' | 'duplicate_entity' | 'out_of_order' | 'turn_mismatch';
  event_id: EventId;
  message: string;
}

export interface ApplyResult {
  /** Current state of every entity the event changed. */
  changes: EntityChanges;
  notes: ProjectionNote[];
}

/** What command admission and restart reconciliation need to know about an execution. */
export interface ExecutionFacts {
  executionId: ExecutionId;
  projectId: ProjectId;
  workstreamId: WorkstreamId;
  runtimeId: string;
  /** The folder the runtime was given to work in, or null where it was given none or none was recorded. */
  directory: string | null;
  status: ExecutionStatus;
  hasNativeSession: boolean;
  activeTurnId: string | null;
  hasActiveTurn: boolean;
  pendingApprovals: readonly ApprovalView[];
  pendingQuestions: readonly QuestionView[];
  /** True while it is a plan: its start recorded one, and it has not been left (PLAN). */
  inPlan: boolean;
  /** Its spending limit's state, `none` without one (lead's ruling, 2026-11-13). */
  spendingCap: 'none' | SpendingCapView['state'];
}

const ACTIVE_STATUSES: ReadonlySet<ExecutionStatus> = new Set([
  'starting',
  'running',
  'verifying',
  'waiting_for_human',
]);

interface ProjectState {
  readonly projectId: ProjectId;
  readonly name: string;
  location: ProjectLocation | null;
  starter: string | null;
  /** The digest of the starter's template the project began from (DRAWN); kept out of the project's view. */
  starterTemplate: string | null;
  /** The catalog repository the person linked (ADR 0039); kept out of the project's view. */
  catalog: CatalogRepositoryRef | null;
  /** The project this one was made as a VR version of (M1.8c-1); kept out of the project's view. */
  madeFrom: ProjectId | null;
  /** The project's own rules while on (E5): the file and the digest the person chose. */
  rules?: { readonly file: 'AGENTS.md' | 'CLAUDE.md'; readonly sha256: string };
  /**
   * When the person took it off the list (ADR 0063). Every read of the host's own keeps the project whole;
   * only what a person or a client reads leaves it out, by `listed`.
   */
  removedAt?: Timestamp;
  readonly createdAt: Timestamp;
  updatedAt: Timestamp;
}

/** A project's latest check, as the host recorded it, and whether its files are still current (T2). */
export interface ProjectCheck {
  readonly check_id: CheckId;
  readonly findings: CheckFindings;
  readonly recorded_at: Timestamp;
  readonly position: number;
  readonly files: CheckFilesState;
  /** The task whose own copy was checked (ADR 0045), or null for the project's own folder. */
  readonly workstream_id: WorkstreamId | null;
}

/** A project's running preview, and whether the check it serves still describes the current files (T2). */
export interface ProjectPreview {
  readonly check_id: CheckId;
  readonly port: number;
  readonly files: CheckFilesState;
}

interface CheckState<F extends CheckFindings | OwnCheckFindings = CheckFindings> {
  readonly checkId: CheckId;
  readonly findings: F;
  readonly recordedAt: Timestamp;
  readonly position: number;
  readonly unobservedTurns: Set<ExecutionId>;
  tainted: boolean;
  changed: boolean;
  /**
   * The folder checked: a task's own copy or the project's (ADR 0045); null where it was not known,
   * which every turn in the project reaches, as before copies.
   */
  readonly folder: string | null;
  /** The task whose own copy was checked, or null for the project's own folder. */
  readonly workstreamId: WorkstreamId | null;
}

/** A check of either kind: the Mac's own of a starter's project, or a project's own check (ADR 0055). */
type AnyCheckState = CheckState<CheckFindings | OwnCheckFindings>;

/** A project's own check (ADR 0055): what the person chose, and its runs' state. */
interface OwnCheckState {
  readonly manager: OwnCheckManager;
  readonly scripts: readonly OwnCheckScript[];
  readonly chosenAt: Timestamp;
  latest: (CheckState<OwnCheckFindings> & { readonly first: boolean }) | null;
  /** Whether any run of this choice passed, the latest included. */
  everPassed: boolean;
  /** Whether the run before the latest passed; null when the latest is the first. */
  passedBefore: boolean | null;
  /** Whether any run before the latest passed. */
  passedEarlier: boolean;
  /**
   * Each folder's own runs, the project's folder by '' and each task's own copy by its task (AUDIT3 6): whether its
   * latest run passed, whether the run before that passed, and whether any before the latest did. The baseline a
   * failure is judged by is the folder's own, never another folder's.
   */
  readonly byFolder: Map<
    string,
    { latest: boolean | null; before: boolean | null; earlier: boolean; ever: boolean }
  >;
}

function checkFilesState(state: AnyCheckState): CheckFilesState {
  if (state.changed) return 'changed';
  if (state.tainted || state.unobservedTurns.size > 0) return 'unknown';
  return 'current';
}

interface WorkstreamState {
  readonly workstreamId: WorkstreamId;
  readonly projectId: ProjectId;
  readonly title: string;
  readonly objective: string | null;
  readonly executionIds: ExecutionId[];
  readonly createdAt: Timestamp;
  updatedAt: Timestamp;
  /** The task's own copy of the project's folder, or why it has none; null before either (ADR 0045). */
  copy: WorkstreamCopy | null;
  /**
   * The copy's tree its latest reading was of, which its view's `to_add` shows: what the person
   * reviewed, and all an add may bring (ADR 0045). Null when nothing was read since the base moved.
   */
  reviewedTree: string | null;
  /** The project's version the task starts from, or null for one from the folder as it is (ADR 0045). */
  fromCheckpoint: CheckpointId | null;
}

/**
 * Why an execution is unknown when the reason is a restart, which stopped every runtime process:
 * `control_plane_restarted` until its session is re-attached, `session_lost` once it cannot be
 * (ADR 0046). Neither holds its project busy: nothing of it runs any more.
 */
export const RESTART_REASONS: ReadonlySet<string> = new Set([
  'control_plane_restarted',
  'session_lost',
]);

function stoppedByRestart(code: string | undefined): boolean {
  return code !== undefined && RESTART_REASONS.has(code);
}

/**
 * How many of a task's tool results stay readable by their event id: the last 256, about 0.4 KB
 * each, oldest dropped (core audit 2026-11-03). The headset is only handed ids from the list of
 * the last 16, so none a person saw is gone this soon.
 */
export const TOOL_RESULT_HISTORY = 256;

/** A busy task's status in words, for the busy rule's refusals (core audit 2026-11-03). */
const BUSY_WORDS: Readonly<Record<ExecutionStatus, string>> = {
  starting: 'starting',
  running: 'running',
  verifying: 'checking its work',
  waiting_for_human: 'waiting for you',
  completed: 'finished',
  failed: 'stopped by a failure',
  interrupted: 'stopped',
  unknown: 'in an unknown state',
};

/**
 * Bumped whenever `ProjectionState`'s shape changes, so no older checkpoint is taken for it. 2: a
 * task's own copy and a check's folder and task (ADR 0045). 3: each project's shared link (F1).
 * 4: how each file a task's copy would add changed (`to_add_files`). 5: each project's history.
 * 6: the digest of a task's copy's latest reading (`to_add_review`, ADR 0051). 7: whether an
 * execution's start recorded the runtime's plan mode (`plan`, PLAN). 8: whether it left the plan
 * (`planLeft`) and why its start was not counted confined (`unconfinedReason`). 9: each
 * execution's queued instructions (ADR 0056). 10: each project's own check (ADR 0055). 11: the
 * version a task starts from (`from_checkpoint`, ADR 0045). 12: what the computer saw of a request
 * that ended with no reply (`observed`, LOOP C). 13: each project's published game (ADR 0039,
 * amended). 14: each project's secrets by name (ADR 0057). 15: each project's rules while on (`rules`, E5).
 * 16: when a start found a task's copy gone (`missing_at`, ADR 0045). 17: who can see each repository
 * a first send of Halcyonic's made (ADR 0039, amended). 18: a world's description is format 2 and only
 * `project.world.read` gives it (ADR 0049). 19: a request's prepared commands hold proposals and presses beside
 * instructions (ADR 0060); each raised past the base's again at the rebase. 20: where the person chose the assistant
 * to run (`assistantModel`, ADR 0030, 2026-11-12). 21: what each execution's reported turns used (`usage`).
 * 22: the spending limit each execution runs under (`spendingCapUsd`). 23: when a project was taken off the list
 * (`removedAt`, ADR 0063). 24: how many of each
 * execution's requests the host answered itself and how many asked (`approvals`). 25: the commands a start's press
 * runs before its run (`before`, the recap is the review).
 */
export const PROJECTION_STATE_FORMAT = 25;

/**
 * A projection's whole state at a position, as `exportState` gives it, for a checkpoint (ADR 0050).
 * Opaque to everything but the projection.
 */
export interface ProjectionState {
  readonly format: number;
  readonly position: number;
  readonly projects: ReadonlyMap<unknown, unknown>;
  readonly workstreams: ReadonlyMap<unknown, unknown>;
  readonly executions: ReadonlyMap<unknown, unknown>;
  readonly commands: ReadonlyMap<unknown, unknown>;
  readonly executionsByProject: ReadonlyMap<unknown, unknown>;
  readonly pendingCommands: ReadonlySet<unknown>;
  readonly checkpoints: ReadonlyMap<unknown, unknown>;
  readonly history: ReadonlyMap<unknown, unknown>;
  readonly checks: ReadonlyMap<unknown, unknown>;
  readonly checksById: ReadonlyMap<unknown, unknown>;
  readonly openChecks: ReadonlyMap<unknown, unknown>;
  readonly ownChecks: ReadonlyMap<unknown, unknown>;
  readonly previews: ReadonlyMap<unknown, unknown>;
  readonly macPlays: ReadonlyMap<unknown, unknown>;
  readonly shares: ReadonlyMap<unknown, unknown>;
  readonly published: ReadonlyMap<unknown, unknown>;
  readonly secrets: ReadonlyMap<unknown, unknown>;
  readonly madeRepositories: ReadonlyMap<unknown, unknown>;
  readonly checkOutputs: ReadonlyMap<unknown, unknown>;
  readonly resultStarts: ReadonlyMap<unknown, unknown>;
  readonly results: ReadonlyMap<unknown, unknown>;
  readonly inferredTurnOutcomes: ReadonlyMap<unknown, unknown>;
  readonly turnStarts: ReadonlyMap<unknown, unknown>;
  readonly turnObservations: ReadonlyMap<unknown, unknown>;
  readonly requests: ReadonlyMap<unknown, unknown>;
  readonly worlds: ReadonlyMap<unknown, unknown>;
  readonly autoFixCommands: ReadonlySet<unknown>;
  readonly devices: DeviceRegistryState;
  /** Where the person chose the assistant to run: null for this computer's model. */
  readonly assistantModel: AssistantHostedModel | null;
}

/** What re-attaching an execution after a restart needs from the journal (ADR 0046). */
export interface ReattachBounds {
  readonly runtime_id: string;
  readonly native_id: string;
  readonly options: RuntimeOptions;
  readonly model_ref: string | null;
  readonly approval_level?: ApprovalLevel;
  /** Whether its start recorded the runtime's own plan mode (PLAN). */
  readonly plan: boolean;
  /** What is left of its spending limit for the next launch, null for none; absent without a limit. */
  readonly spending_allowance_usd?: number | null;
  readonly confined: boolean;
  readonly directory: string | null;
  readonly cut_turn: { readonly turn_id: string | null } | null;
  readonly last_sequence: number | null;
}

interface CommandState {
  readonly envelope: CommandEnvelope;
  readonly principal: Principal | null;
  status: CommandStatus;
  projectId: ProjectId | null;
  workstreamId: WorkstreamId | null;
  executionId: ExecutionId | null;
  updatedAt: Timestamp;
  /** The journal position of its outcome, or null while it awaits one. */
  finishedAt: number | null;
  rejection: CommandRejection | null;
  failure: CommandFailure | null;
  result: CommandResult | null;
}

class ChangeSet {
  readonly projects = new Set<ProjectId>();
  readonly workstreams = new Set<WorkstreamId>();
  readonly executions = new Set<ExecutionId>();
  readonly commands = new Set<CommandId>();
}

export interface ProjectionOptions {
  /**
   * Whether the runtime with this id reports every tool call it makes (its descriptor's
   * `reports_tool_activity`). Without it, or for a runtime it does not know, none does, so a
   * running execution's `tool_activity` is `unknown` rather than `none`.
   */
  readonly reportsToolActivity?: (runtimeId: string) => boolean;
  /** For tests: called each time an event visits a check to update whether its files are current. */
  readonly onCheckVisited?: () => void;
}

/**
 * Current state, rebuilt by applying journaled events in position order. Applying the same
 * journal always produces the same state; that is what makes the journal the source of truth.
 */
export class Projection {
  #position = 0;
  readonly #reportsToolActivity: (runtimeId: string) => boolean;
  readonly #onCheckVisited: (() => void) | undefined;
  readonly #projects = new Map<ProjectId, ProjectState>();
  readonly #workstreams = new Map<WorkstreamId, WorkstreamState>();
  readonly #executions = new Map<ExecutionId, ExecutionState>();
  readonly #commands = new Map<CommandId, CommandState>();
  /** Each project's executions, so a rule about one project reads only its own (core audit 2026-11-03). */
  readonly #executionsByProject = new Map<ProjectId, ExecutionState[]>();
  /** Commands accepted and still awaiting an outcome, in the order they were accepted. */
  readonly #pendingCommands = new Set<CommandState>();
  /** Checkpoints the host took, by project, oldest first (ADR 0036). */
  readonly #checkpoints = new Map<ProjectId, CheckpointView[]>();
  /**
   * Each project's history, oldest first: its versions, the ones not kept, and its restores, which
   * nothing else in current state keeps. A version's changed files are counted by its reader.
   */
  readonly #history = new Map<ProjectId, HistoryEntry[]>();
  /** Each project's latest check (ADR 0037). */
  readonly #checks = new Map<ProjectId, CheckState>();
  /** Every check recorded for each project, so a preview or play of an older check knows its file state (T2). */
  readonly #checksById = new Map<ProjectId, Map<CheckId, CheckState>>();
  /**
   * Each project's checks whose files are not known to have changed: the ones a turn, a reading or a
   * restore can still update. A changed check stays changed, so it leaves, and a round costs the same
   * however many checks came before (#8a).
   */
  readonly #openChecks = new Map<ProjectId, Set<AnyCheckState>>();
  /** Each project's own check, once the person chose one (ADR 0055). */
  readonly #ownChecks = new Map<ProjectId, OwnCheckState>();
  /** Each project's running preview, by the check it serves. */
  readonly #previews = new Map<ProjectId, { readonly check_id: CheckId; readonly port: number }>();
  /** The check whose game plays on the Mac now, per project (ADR 0037). */
  readonly #macPlays = new Map<ProjectId, CheckId>();
  /** Each project's shared link now (F1): the check its preview serves and the public address. */
  /** Each project's game as last published to the person's GitHub Pages (ADR 0039, amended). */
  readonly #published = new Map<ProjectId, ProjectPublishedView>();
  /** Each project's secrets by name, sorted (ADR 0057): never a value, which only the store holds. */
  readonly #secrets = new Map<ProjectId, readonly SecretName[]>();
  /**
   * Each repository a send of Halcyonic's made for a project, and who can see it as far as the journal
   * says: as it was made, then public once a publish made it so. Nothing else changes it here.
   */
  readonly #madeRepositories = new Map<
    ProjectId,
    { readonly repository: string; readonly visibility: 'private' | 'public' }
  >();
  readonly #shares = new Map<
    ProjectId,
    { readonly checkId: CheckId; readonly url: string; readonly way: ShareWay }
  >();
  /** The failed check whose output an agent was last given in each project (FX1). */
  readonly #checkOutputs = new Map<ProjectId, CheckId>();
  /** Each world project's description as the Mac last read it (ADR 0049). */
  readonly #worlds = new Map<ProjectId, ProjectWorldView>();
  /** The instructions the host sent on its own for a failed check (ADR 0044). */
  readonly #autoFixCommands = new Set<CommandId>();
  readonly #devices = new DeviceRegistry();
  #assistantModel: AssistantHostedModel | null = null;
  readonly #resultStarts = new Map<
    string,
    Map<string, { turn: string | null; title: string | null }>
  >();
  readonly #results = new Map<
    string,
    { total: number; recent: ToolResultSummary[]; history: Map<string, ToolResultSummary> }
  >();
  readonly #inferredTurnOutcomes = new Map<
    string,
    { position: number; turnId: string | null; startPosition: number | null; rule: string }[]
  >();
  readonly #turnStarts = new Map<string, number>();
  readonly #turnObservations = new Map<
    string,
    { position: number; type: string; turnId: string | null; startPosition: number | null }[]
  >();
  readonly #requests = new Map<
    string,
    {
      view: WorkspaceRequestView;
      principal: Principal | null;
      /**
       * Each step's prepared command: an instruction (`continue_task`), or, on a step that holds a `proposal`, the
       * command a proposal or a press prepared (ADR 0060). One map, so the saved state keeps its shape.
       */
      prepared: Map<
        string,
        {
          command: CommandEnvelope;
          step: WorkspaceRequestStep;
          /** On a start's step: the commands its press runs before `command`, in order. */
          before?: CommandEnvelope[];
        }
      >;
      bindings: Map<
        string,
        {
          tool: string;
          command: CommandId | null;
          position: number;
          turn: string | null;
          active: boolean;
        }
      >;
    }
  >();

  /**
   * The projection's whole state, for a checkpoint beside the journal (ADR 0050): every field the
   * events build, as live references that keep the objects they share shared. Serialize it at
   * once (`v8.serialize` keeps maps, sets and shared references), before anything else is applied.
   */
  exportState(): ProjectionState {
    return {
      format: PROJECTION_STATE_FORMAT,
      position: this.#position,
      projects: this.#projects,
      workstreams: this.#workstreams,
      executions: this.#executions,
      commands: this.#commands,
      executionsByProject: this.#executionsByProject,
      pendingCommands: this.#pendingCommands,
      checkpoints: this.#checkpoints,
      history: this.#history,
      checks: this.#checks,
      checksById: this.#checksById,
      openChecks: this.#openChecks,
      ownChecks: this.#ownChecks,
      previews: this.#previews,
      macPlays: this.#macPlays,
      shares: this.#shares,
      published: this.#published,
      secrets: this.#secrets,
      madeRepositories: this.#madeRepositories,
      checkOutputs: this.#checkOutputs,
      resultStarts: this.#resultStarts,
      results: this.#results,
      inferredTurnOutcomes: this.#inferredTurnOutcomes,
      turnStarts: this.#turnStarts,
      turnObservations: this.#turnObservations,
      requests: this.#requests,
      worlds: this.#worlds,
      autoFixCommands: this.#autoFixCommands,
      devices: this.#devices.exportState(),
      assistantModel: this.#assistantModel,
    };
  }

  /**
   * Takes a state `exportState` gave, deserialized, into a projection that has applied nothing,
   * as if it had folded the journal up to the state's position. Throws, having changed nothing,
   * on a state of another shape; the caller then folds the whole journal.
   */
  restoreState(state: ProjectionState): void {
    if (this.#position !== 0 || this.#projects.size > 0 || this.#commands.size > 0) {
      throw new Error('a projection restores a state only before it applies anything');
    }
    if (
      state.format !== PROJECTION_STATE_FORMAT ||
      !Number.isSafeInteger(state.position) ||
      state.position < 0 ||
      !(state.projects instanceof Map) ||
      !(state.workstreams instanceof Map) ||
      !(state.executions instanceof Map) ||
      !(state.commands instanceof Map) ||
      !(state.executionsByProject instanceof Map) ||
      !(state.pendingCommands instanceof Set) ||
      !(state.checkpoints instanceof Map) ||
      !(state.history instanceof Map) ||
      !(state.checks instanceof Map) ||
      !(state.checksById instanceof Map) ||
      !(state.openChecks instanceof Map) ||
      !(state.previews instanceof Map) ||
      !(state.macPlays instanceof Map) ||
      !(state.shares instanceof Map) ||
      !(state.published instanceof Map) ||
      !(state.secrets instanceof Map) ||
      !(state.madeRepositories instanceof Map) ||
      !(state.checkOutputs instanceof Map) ||
      !(state.resultStarts instanceof Map) ||
      !(state.results instanceof Map) ||
      !(state.inferredTurnOutcomes instanceof Map) ||
      !(state.turnStarts instanceof Map) ||
      !(state.turnObservations instanceof Map) ||
      !(state.requests instanceof Map) ||
      !(state.worlds instanceof Map) ||
      !(state.autoFixCommands instanceof Set) ||
      !(state.devices.devices instanceof Map) ||
      !(state.devices.byCredential instanceof Map)
    ) {
      throw new Error('the state is not one this projection exported');
    }
    this.#devices.restoreState(state.devices);
    this.#assistantModel = state.assistantModel;
    for (const [key, value] of state.projects as Map<never, never>) this.#projects.set(key, value);
    for (const [key, value] of state.workstreams as Map<never, never>)
      this.#workstreams.set(key, value);
    for (const [key, value] of state.executions as Map<never, never>)
      this.#executions.set(key, value);
    for (const [key, value] of state.commands as Map<never, never>) this.#commands.set(key, value);
    for (const [key, value] of state.executionsByProject as Map<never, never>)
      this.#executionsByProject.set(key, value);
    for (const value of state.pendingCommands as Set<never>) this.#pendingCommands.add(value);
    for (const [key, value] of state.checkpoints as Map<never, never>)
      this.#checkpoints.set(key, value);
    for (const [key, value] of state.history as Map<never, never>) this.#history.set(key, value);
    for (const [key, value] of state.checks as Map<never, never>) this.#checks.set(key, value);
    for (const [key, value] of state.checksById as Map<never, never>)
      this.#checksById.set(key, value);
    for (const [key, value] of state.openChecks as Map<never, never>)
      this.#openChecks.set(key, value);
    for (const [key, value] of state.ownChecks as Map<never, never>)
      this.#ownChecks.set(key, value);
    for (const [key, value] of state.previews as Map<never, never>) this.#previews.set(key, value);
    for (const [key, value] of state.macPlays as Map<never, never>) this.#macPlays.set(key, value);
    for (const [key, value] of state.shares as Map<never, never>) this.#shares.set(key, value);
    for (const [key, value] of state.published as Map<never, never>)
      this.#published.set(key, value);
    for (const [key, value] of state.secrets as Map<never, never>) this.#secrets.set(key, value);
    for (const [key, value] of state.madeRepositories as Map<never, never>)
      this.#madeRepositories.set(key, value);
    for (const [key, value] of state.checkOutputs as Map<never, never>)
      this.#checkOutputs.set(key, value);
    for (const [key, value] of state.resultStarts as Map<never, never>)
      this.#resultStarts.set(key, value);
    for (const [key, value] of state.results as Map<never, never>) this.#results.set(key, value);
    for (const [key, value] of state.inferredTurnOutcomes as Map<never, never>)
      this.#inferredTurnOutcomes.set(key, value);
    for (const [key, value] of state.turnStarts as Map<never, never>)
      this.#turnStarts.set(key, value);
    for (const [key, value] of state.turnObservations as Map<never, never>)
      this.#turnObservations.set(key, value);
    for (const [key, value] of state.requests as Map<never, never>) this.#requests.set(key, value);
    for (const [key, value] of state.worlds as Map<never, never>) this.#worlds.set(key, value);
    for (const value of state.autoFixCommands as Set<never>) this.#autoFixCommands.add(value);
    this.#position = state.position;
  }

  constructor(options: ProjectionOptions = {}) {
    this.#reportsToolActivity = options.reportsToolActivity ?? (() => false);
    this.#onCheckVisited = options.onCheckVisited;
  }

  #view = (state: ExecutionState): ExecutionView =>
    toExecutionView(state, this.#reportsToolActivity(state.runtime.runtime_id));

  /** Position of the last applied event, 0 before any. */
  get position(): number {
    return this.#position;
  }

  apply(stored: StoredEvent): ApplyResult {
    if (stored.position <= this.#position) {
      throw new Error(
        `projection received position ${stored.position} after ${this.#position}; events must be applied in journal order`,
      );
    }
    this.#position = stored.position;
    const changes = new ChangeSet();
    const notes: ProjectionNote[] = [];
    const beforeTurn =
      stored.event.execution_id === null
        ? undefined
        : this.#executions.get(stored.event.execution_id)?.activeTurn;
    const hadActive = Boolean(beforeTurn);
    this.#dispatch(stored.event, changes, notes);
    const event = stored.event;
    if (
      notes.length === 0 &&
      event.event_type === 'runtime.tool.started' &&
      event.provenance.epistemic === 'observed'
    ) {
      const starts =
        this.#resultStarts.get(event.execution_id) ??
        new Map<string, { turn: string | null; title: string | null }>();
      starts.set(event.payload.tool_call_id, {
        turn: beforeTurn?.turnId ?? null,
        title: event.payload.title,
      });
      // Missing native completions grant no evidence; retain a bounded set of possible correlations.
      const oldest = starts.keys().next().value;
      if (starts.size > 128 && oldest !== undefined) starts.delete(oldest);
      this.#resultStarts.set(event.execution_id, starts);
    }
    if (notes.length === 0 && event.event_type === 'runtime.tool.result') {
      const result = event.payload;
      const started = this.#resultStarts.get(event.execution_id)?.get(result.tool_call_id);
      const bodies =
        result.kind === 'command' ? [result.output] : result.files.map((file) => file.patch);
      const state = this.#results.get(event.execution_id) ?? {
        total: 0,
        recent: [] as ToolResultSummary[],
        history: new Map<string, ToolResultSummary>(),
      };
      state.total++;
      const summary: ToolResultSummary = {
        event_id: event.event_id,
        position: stored.position,
        occurred_at: event.occurred_at,
        runtime_id: event.source.runtime_id,
        epistemic: 'observed',
        native_type: event.provenance.native_type,
        title: boundTitle(started?.title),
        kind: result.kind,
        completion: result.completion,
        exit_code: result.kind === 'command' ? result.exit_code : null,
        files_total: result.kind === 'file_changes' ? result.files_total : null,
        body_available: bodies.some((body) => body.availability === 'available'),
        retained_complete:
          bodies.every((body) => body.complete) &&
          (result.kind === 'command' ||
            (result.list_complete &&
              result.files.every(
                (file) =>
                  file.path_complete &&
                  (file.move_path_complete === null || file.move_path_complete),
              ))),
      };
      state.recent.unshift(summary);
      state.history.set(event.event_id, summary);
      // Only the last results of a task stay readable by id; an older one reads as not found.
      if (state.history.size > TOOL_RESULT_HISTORY) {
        const oldest = state.history.keys().next().value;
        if (oldest !== undefined) state.history.delete(oldest);
      }
      state.recent.length = Math.min(state.recent.length, TOOL_RESULT_RECENT);
      this.#results.set(event.execution_id, state);
    }
    if (
      notes.length === 0 &&
      event.event_type === 'runtime.tool.completed' &&
      event.payload.turn_id != null &&
      this.#resultStarts.get(event.execution_id)?.get(event.payload.tool_call_id)?.turn ===
        event.payload.turn_id
    )
      this.#resultStarts.get(event.execution_id)?.delete(event.payload.tool_call_id);
    if (
      notes.length === 0 &&
      event.event_type === 'runtime.turn.started' &&
      event.provenance.epistemic !== 'reported'
    )
      this.#turnStarts.set(event.execution_id, stored.position);
    if (
      notes.length === 0 &&
      event.provenance.epistemic === 'observed' &&
      (event.event_type === 'runtime.turn.started' ||
        event.event_type === 'runtime.turn.completed' ||
        event.event_type === 'runtime.turn.failed' ||
        event.event_type === 'runtime.turn.interrupted')
    ) {
      if (event.event_type === 'runtime.turn.started')
        this.#turnStarts.set(event.execution_id, stored.position);
      const list = this.#turnObservations.get(event.execution_id) ?? [];
      list.push({
        position: stored.position,
        type: event.event_type,
        turnId:
          event.event_type === 'runtime.turn.started'
            ? event.payload.turn_id
            : (event.payload.turn_id ?? beforeTurn?.turnId ?? null),
        startPosition:
          event.event_type === 'runtime.turn.started'
            ? stored.position
            : hadActive
              ? (this.#turnStarts.get(event.execution_id) ?? null)
              : null,
      });
      this.#turnObservations.set(event.execution_id, list);
    }
    if (
      notes.length === 0 &&
      event.provenance.epistemic === 'inferred' &&
      (event.event_type === 'runtime.turn.completed' ||
        event.event_type === 'runtime.turn.failed' ||
        event.event_type === 'runtime.turn.interrupted')
    ) {
      const list = this.#inferredTurnOutcomes.get(event.execution_id) ?? [];
      list.push({
        position: stored.position,
        turnId: event.payload.turn_id ?? beforeTurn?.turnId ?? null,
        startPosition: hadActive ? (this.#turnStarts.get(event.execution_id) ?? null) : null,
        rule: event.provenance.rule,
      });
      this.#inferredTurnOutcomes.set(event.execution_id, list);
    }
    return { changes: this.#materialize(changes), notes };
  }

  // Reads -----------------------------------------------------------------------------------

  project(projectId: string): ProjectView | undefined {
    const state = this.#projects.get(projectId as ProjectId);
    return state === undefined ? undefined : this.#projectView(state);
  }

  /**
   * Whether the project is on the list (ADR 0063): it exists and was not taken off. The one question every
   * place a person or a client reads from asks, so a project taken off, its tasks and their executions are
   * left out there. The host's own reads (secrets kept, folders denied to agents, a VR version's original)
   * never ask it: they keep the project whole.
   */
  listed(projectId: string): boolean {
    const state = this.#projects.get(projectId as ProjectId);
    return state !== undefined && state.removedAt === undefined;
  }

  /** The project this one was made as a VR version of (M1.8c-1), or null. */
  madeFrom(projectId: string): ProjectId | null {
    return this.#projects.get(projectId as ProjectId)?.madeFrom ?? null;
  }

  /** The digest of the starter's template the project began from (DRAWN), or null. */
  starterTemplate(projectId: string): string | null {
    return this.#projects.get(projectId as ProjectId)?.starterTemplate ?? null;
  }

  /**
   * The catalog repository the person linked the project to, null when none, undefined for an
   * unknown project. Only the control plane reads it: clients are to see links through its catalog
   * read (ADR 0039), not in the project's view.
   */
  catalogLink(projectId: string): CatalogRepositoryRef | null | undefined {
    const state = this.#projects.get(projectId as ProjectId);
    if (state === undefined) return undefined;
    return state.catalog === null ? null : { ...state.catalog };
  }

  /**
   * The tree of the task's copy its latest reading was of: what its `to_add` shows, and so what the
   * person reviewed (ADR 0045); null when there is none. A task kept from before has none.
   */
  reviewedTree(workstreamId: string): string | null {
    return this.#workstreams.get(workstreamId as WorkstreamId)?.reviewedTree ?? null;
  }

  workstream(workstreamId: string): WorkstreamView | undefined {
    const state = this.#workstreams.get(workstreamId as WorkstreamId);
    return state === undefined ? undefined : this.#workstreamView(state);
  }

  /**
   * What one task's runs used: `projectUsage` over that task's executions, the same sum as its project's, so a
   * task and its project never count by two rules (the Mac's Usage page). Null where none of its runs reported.
   */
  workstreamUsage(workstreamId: string): ReturnType<typeof projectUsage> {
    const state = this.#workstreams.get(workstreamId as WorkstreamId);
    if (state === undefined) return null;
    const runs = this.#executionsByProject.get(state.projectId) ?? [];
    return projectUsage(runs.filter((run) => run.workstreamId === state.workstreamId));
  }

  execution(executionId: string): ExecutionView | undefined {
    const state = this.#executions.get(executionId as ExecutionId);
    return state === undefined ? undefined : this.#view(state);
  }

  /** Whether the execution's start recorded that it ran confined (S2); false otherwise or unknown. */
  executionConfined(executionId: string): boolean {
    return this.#executions.get(executionId as ExecutionId)?.confined === true;
  }

  /**
   * What re-attaching an execution after a restart needs from the journal (ADR 0046): its native
   * id, the bounds it started with and the turn in flight, if any. Null when there is no such
   * execution, the runtime never reported a native id, or its start command is not in the journal.
   */
  reattachBounds(executionId: string): ReattachBounds | null {
    const state = this.#executions.get(executionId as ExecutionId);
    if (state === undefined || state.nativeId === null || state.startCommandId === null)
      return null;
    const start = this.#commands.get(state.startCommandId)?.envelope;
    if (start?.command_type !== 'execution.start') return null;
    return {
      runtime_id: state.runtime.runtime_id,
      native_id: state.nativeId,
      options: start.payload.options,
      model_ref: start.payload.model_ref,
      ...(state.approvalLevel !== null && { approval_level: state.approvalLevel }),
      // Still a plan only until it was left: a re-attach then expects the session off its plan mode.
      plan: state.plan && !state.planLeft,
      ...(state.spendingCap != null && { spending_allowance_usd: spendingAllowance(state) }),
      confined: state.confined,
      directory: state.directory,
      cut_turn: state.activeTurn === null ? null : { turn_id: state.activeTurn.turnId },
      last_sequence: state.lastSequence,
    };
  }
  executionResultSummaries(executionId: string): { total: number; summaries: ToolResultSummary[] } {
    const state = this.#results.get(executionId);
    return { total: state?.total ?? 0, summaries: structuredClone(state?.recent ?? []) };
  }
  executionResultSummary(executionId: string, eventId: string): ToolResultSummary | undefined {
    const summary = this.#results.get(executionId)?.history.get(eventId);
    return summary ? structuredClone(summary) : undefined;
  }

  workspaceRequest(id: string): WorkspaceRequestView | undefined {
    const request = this.#requests.get(id);
    return request ? structuredClone(request.view) : undefined;
  }
  workspaceRequests(): WorkspaceRequestView[] {
    return [...this.#requests.values()].map((request) => structuredClone(request.view));
  }
  workspaceRequestOwner(id: string): Principal | null | undefined {
    const owner = this.#requests.get(id)?.principal;
    return owner ? { ...owner } : owner;
  }
  workspacePreparedCommand(
    id: string,
    step: string,
  ): CommandOf<'execution.send_instruction'> | undefined {
    const prepared = this.#requests.get(id)?.prepared.get(step);
    return prepared &&
      !prepared.step.proposal &&
      prepared.command.command_type === 'execution.send_instruction'
      ? structuredClone(prepared.command)
      : undefined;
  }
  /**
   * The command a proposal or a press prepared on one step, with that step as it was recorded (ADR 0060), and on a
   * start's step the commands its press runs first.
   */
  workspaceProposal(
    id: string,
    step: string,
  ):
    | { command: CommandEnvelope; step: WorkspaceRequestStep; before?: CommandEnvelope[] }
    | undefined {
    const prepared = this.#requests.get(id)?.prepared.get(step);
    return prepared?.step.proposal ? structuredClone(prepared) : undefined;
  }

  /** All action receipts remain discoverable when the bounded public view evicts them. */
  workspacePreparedActions(
    id: string,
  ): { command: CommandOf<'execution.send_instruction'>; step: WorkspaceRequestStep }[] {
    return [...(this.#requests.get(id)?.prepared.values() ?? [])].flatMap((value) =>
      !value.step.proposal && value.command.command_type === 'execution.send_instruction'
        ? [
            structuredClone(
              value as {
                command: CommandOf<'execution.send_instruction'>;
                step: WorkspaceRequestStep;
              },
            ),
          ]
        : [],
    );
  }

  /** Only lifecycle observations the projection actually applied; ignored journal events are not outcome evidence. */
  executionTurnObservations(
    id: string,
    after: number,
    limit: number,
  ): { position: number; type: string; turnId: string | null; startPosition: number | null }[] {
    return (this.#turnObservations.get(id) ?? [])
      .filter((value) => value.position > after)
      .slice(0, limit)
      .map((value) => ({ ...value }));
  }

  /** Accepted reconstruction is kept as inferred, never promoted to an observed outcome. */
  executionInferredTurnOutcomes(
    id: string,
    after: number,
  ): { position: number; turnId: string | null; startPosition: number | null; rule: string }[] {
    return (this.#inferredTurnOutcomes.get(id) ?? [])
      .filter((value) => value.position > after)
      .map((value) => ({ ...value }));
  }

  /** Completed Resume commands are durable human direction, not fabricated model replies. */
  workspaceRequestDirections(id: string): string[] {
    return [...this.#commands.values()].flatMap((value) =>
      value.status === 'completed' &&
      value.envelope.command_type === 'workspace.request.resume' &&
      value.envelope.payload.request_id === id &&
      value.envelope.payload.text !== null
        ? [value.envelope.payload.text]
        : [],
    );
  }

  command(commandId: string): CommandView | undefined {
    const state = this.#commands.get(commandId as CommandId);
    return state === undefined ? undefined : toCommandView(state);
  }

  commandEnvelope(commandId: string): CommandEnvelope | undefined {
    return this.#commands.get(commandId as CommandId)?.envelope;
  }

  /** Original journaled sender, including a rejected command; legacy absent principals grant no read. */
  commandOwner(commandId: string): Principal | null {
    const owner = this.#commands.get(commandId as CommandId)?.principal;
    return owner ? { ...owner } : null;
  }

  projects(): ProjectView[] {
    return Array.from(this.#projects.values(), this.#projectView);
  }

  /** The projects on the list (ADR 0063), for what a person or a client reads. */
  listedProjects(): ProjectView[] {
    return this.projects().filter((project) => project.removed_at === undefined);
  }

  /** The tasks of projects on the list (ADR 0063), all or one project's, for what a person or a client reads. */
  listedWorkstreams(projectId?: string): WorkstreamView[] {
    return this.workstreams(projectId).filter((workstream) => this.listed(workstream.project_id));
  }

  /** The executions of projects on the list (ADR 0063), for what a person or a client reads. */
  listedExecutions(): ExecutionView[] {
    return this.executions().filter((execution) => this.listed(execution.project_id));
  }

  /**
   * The commands `commands` gives, less those of a project taken off the list but its own removal, whose
   * outcome a client reconnecting after it still reads (ADR 0063).
   */
  listedCommands(finishedLimit: number): CommandView[] {
    return this.commands(finishedLimit).filter(
      (command) =>
        command.project_id === null ||
        command.command_type === 'project.remove_from_list' ||
        this.listed(command.project_id),
    );
  }

  workstreams(projectId?: string): WorkstreamView[] {
    if (projectId === undefined) {
      return Array.from(this.#workstreams.values(), this.#workstreamView);
    }
    const views: WorkstreamView[] = [];
    for (const ws of this.#workstreams.values()) {
      if (ws.projectId === projectId) views.push(this.#workstreamView(ws));
    }
    return views;
  }

  executions(): ExecutionView[] {
    return Array.from(this.#executions.values(), this.#view);
  }

  /**
   * Commands still awaiting an outcome, plus the `finishedLimit` whose outcomes came last, in the
   * order they were journaled. The last to finish are kept however long they waited, so a client
   * that reconnects holds the outcomes a connected one just saw (U6).
   */
  commands(finishedLimit: number): CommandView[] {
    if (this.#commands.size <= finishedLimit) {
      return Array.from(this.#commands.values(), toCommandView);
    }
    const finishes: number[] = [];
    for (const command of this.#commands.values()) {
      if (command.finishedAt !== null) finishes.push(command.finishedAt);
    }
    finishes.sort((a, b) => b - a);
    const earliestKept =
      finishedLimit > 0 ? (finishes[finishedLimit - 1] ?? 0) : Number.POSITIVE_INFINITY;
    const views: CommandView[] = [];
    for (const command of this.#commands.values()) {
      if (command.finishedAt === null || command.finishedAt >= earliestKept)
        views.push(toCommandView(command));
    }
    return views;
  }

  executionFacts(executionId: string): ExecutionFacts | undefined {
    const state = this.#executions.get(executionId as ExecutionId);
    return state === undefined ? undefined : toFacts(state);
  }

  /** Executions the projection believes are doing something right now. */
  activeExecutions(): ExecutionFacts[] {
    return [...this.#executions.values()]
      .filter((state) => ACTIVE_STATUSES.has(deriveExecutionStatus(state)))
      .map(toFacts);
  }

  /**
   * Executions whose agent may be at work: active, or unknown for any reason but a restart, which no
   * runtime session survives. What holds a folder (FOLDER), as `projectBusy` counts a project.
   */
  executionsAtWork(): ExecutionFacts[] {
    return [...this.#executions.values()]
      .filter((state) => {
        const status = deriveExecutionStatus(state);
        if (ACTIVE_STATUSES.has(status)) return true;
        return status === 'unknown' && state.unknownReason?.code !== 'control_plane_restarted';
      })
      .map(toFacts);
  }

  /** The project's checkpoints, oldest first. */
  checkpoints(projectId: string): CheckpointView[] {
    return [...(this.#checkpoints.get(projectId as ProjectId) ?? [])];
  }

  /**
   * The project's history, oldest first. A version's `changed_files` is always null here: counting
   * needs the checkpoint store, so its reader counts.
   */
  history(projectId: string): readonly HistoryEntry[] {
    return this.#history.get(projectId as ProjectId) ?? [];
  }

  /**
   * The checkpoints the store may stop keeping now, oldest first, by ADR 0036's rule of 2026-11-11:
   * none until more than `keep + slack` are kept, then every one but the newest `keep`, and never one
   * that is still needed. Needed are each folder's newest version; every version of a task still at
   * work, whose undo starts there; the version that undoes the newest restore; every version taken
   * before an add, which undoes it; every version of a task's copy while it is the task's own; and any
   * checkpoint a restore still waiting to run names.
   */
  prunableCheckpoints(projectId: string, keep: number, slack: number): CheckpointId[] {
    const id = projectId as ProjectId;
    const kept = this.#checkpoints.get(id) ?? [];
    if (kept.length <= keep + slack) return [];
    const needed = new Set<string>(kept.slice(-keep).map((view) => view.checkpoint_id));
    const versions = (this.#history.get(id) ?? []).filter(
      (entry): entry is Extract<HistoryEntry, { kind: 'version' }> =>
        entry.kind === 'version' && entry.pruned_at === undefined,
    );
    const newestOfFolder = new Map<string, string>();
    for (const version of versions) {
      const folder =
        version.folder.kind === 'project' ? 'project' : `copy:${version.folder.workstream_id}`;
      newestOfFolder.set(folder, version.checkpoint.checkpoint_id);
      const executionId = version.checkpoint.execution_id;
      const execution = executionId === null ? undefined : this.#executions.get(executionId);
      if (execution !== undefined && ACTIVE_STATUSES.has(deriveExecutionStatus(execution)))
        needed.add(version.checkpoint.checkpoint_id);
      if (version.checkpoint.reason === 'before_add') needed.add(version.checkpoint.checkpoint_id);
      if (
        version.folder.kind === 'task_copy' &&
        this.#workstreams.get(version.folder.workstream_id)?.copy?.state === 'own'
      )
        needed.add(version.checkpoint.checkpoint_id);
    }
    for (const newest of newestOfFolder.values()) needed.add(newest);
    // The version a task starts from, until its copy is made into it (ADR 0045, item 8).
    for (const workstream of this.#workstreams.values()) {
      if (
        workstream.projectId === id &&
        workstream.fromCheckpoint !== null &&
        workstream.copy?.state !== 'own'
      )
        needed.add(workstream.fromCheckpoint);
    }
    const lastRestore = (this.#history.get(id) ?? []).findLast(
      (entry) => entry.kind === 'restored' || entry.kind === 'restore_incomplete',
    );
    if (
      lastRestore !== undefined &&
      (lastRestore.kind === 'restored' || lastRestore.kind === 'restore_incomplete') &&
      lastRestore.before_restore_checkpoint_id !== null
    )
      needed.add(lastRestore.before_restore_checkpoint_id);
    for (const command of this.#pendingCommands) {
      if (command.envelope.command_type === 'project.restore')
        needed.add(command.envelope.payload.checkpoint_id);
    }
    return kept.filter((view) => !needed.has(view.checkpoint_id)).map((view) => view.checkpoint_id);
  }

  /** Whose folder an execution's checkpoint holds: a task's own copy where it works in one. */
  #historyFolder(executionId: ExecutionId | null): HistoryFolder {
    if (executionId === null) return { kind: 'project' };
    const execution = this.#executions.get(executionId);
    const copy =
      execution === undefined ? undefined : this.#workstreams.get(execution.workstreamId)?.copy;
    return copy?.state === 'own' && execution?.directory === copy.directory
      ? { kind: 'task_copy', workstream_id: execution.workstreamId }
      : { kind: 'project' };
  }

  #addHistory(projectId: ProjectId, entry: HistoryEntry): void {
    const list = this.#history.get(projectId) ?? [];
    list.push(entry);
    this.#history.set(projectId, list);
  }

  /** A check of the project by its id, the latest or an earlier one, or undefined. */
  checkFindings(projectId: string, checkId: string): CheckFindings | undefined {
    return this.#checksById.get(projectId as ProjectId)?.get(checkId as CheckId)?.findings;
  }

  /**
   * The folder a check checked, a task's own copy or the project's (ADR 0045); null where it was not
   * known, and undefined for a check not held here.
   */
  checkFolder(projectId: string, checkId: string): string | null | undefined {
    return this.#checksById.get(projectId as ProjectId)?.get(checkId as CheckId)?.folder;
  }

  /**
   * A check's state as it is recorded: the folder it checked, and the turns still running there,
   * whose changes it cannot have seen.
   */
  #freshCheck<F extends CheckFindings | OwnCheckFindings>(
    projectId: ProjectId,
    workstreamId: WorkstreamId | null,
    checkId: CheckId,
    findings: F,
    recordedAt: Timestamp,
  ): CheckState<F> {
    const unobservedTurns = new Set<ExecutionId>();
    let tainted = this.restoreInProgress(projectId);
    const checkedTask = workstreamId;
    const own = checkedTask === null ? undefined : this.#workstreams.get(checkedTask)?.copy;
    const folder =
      own?.state === 'own'
        ? own.directory
        : (this.#projects.get(projectId)?.location?.path ?? null);
    for (const state of this.#executionsByProject.get(projectId) ?? []) {
      if (state.directory === null) continue;
      // Only work in the folder checked reaches it (ADR 0045).
      if (folder !== null && !foldersOverlap(folder, state.directory)) continue;
      const status = deriveExecutionStatus(state);
      // A restart stops every runtime process, so a turn one cut short runs no longer (#13).
      if (status === 'unknown' && stoppedByRestart(state.unknownReason?.code)) continue;
      if (state.activeTurn !== null) unobservedTurns.add(state.executionId);
      if (status === 'unknown') tainted = true;
    }
    return {
      checkId,
      findings,
      recordedAt,
      position: this.#position,
      unobservedTurns,
      tainted,
      changed: false,
      folder,
      workstreamId: checkedTask,
    };
  }

  /**
   * The project's own check (ADR 0055) as the person chose it, its latest run, and what came before
   * that run: whether the run before it passed (null for the first), and whether any did.
   */
  ownCheck(projectId: string):
    | (ProjectOwnCheckView & {
        readonly passed_before: boolean | null;
        readonly passed_earlier: boolean;
      })
    | undefined {
    const own = this.#ownChecks.get(projectId as ProjectId);
    if (own === undefined) return undefined;
    const latest = own.latest;
    return {
      manager: own.manager,
      scripts: [...own.scripts],
      chosen_at: own.chosenAt,
      latest:
        latest === null
          ? null
          : {
              check_id: latest.checkId,
              recorded_at: latest.recordedAt,
              position: latest.position,
              files: checkFilesState(latest),
              first: latest.first,
              findings: latest.findings,
              ...(latest.workstreamId === null ? {} : { workstream_id: latest.workstreamId }),
            },
      ever_passed: own.everPassed,
      passed_before: own.passedBefore,
      passed_earlier: own.passedEarlier,
    };
  }

  /**
   * The baseline of the folder a project's own check checked (AUDIT3 6): the project's folder for null, else the task's
   * own copy. Whether its run before the latest passed (null where the latest is its first), and whether any earlier
   * run of it passed. A folder with no run of its own has none.
   */
  ownCheckBaseline(
    projectId: string,
    workstreamId: string | null,
  ): { readonly passedBefore: boolean | null; readonly passedEarlier: boolean } {
    const folder = this.#ownChecks.get(projectId as ProjectId)?.byFolder.get(workstreamId ?? '');
    return folder === undefined
      ? { passedBefore: null, passedEarlier: false }
      : { passedBefore: folder.before, passedEarlier: folder.earlier };
  }

  /** The repository a first send of Halcyonic's made for the project, and who can see it, if one did. */
  madeRepository(
    projectId: string,
  ): { readonly repository: string; readonly visibility: 'private' | 'public' } | undefined {
    return this.#madeRepositories.get(projectId as ProjectId);
  }

  /** The project's latest check, or undefined when it has none. */
  latestCheck(projectId: string): ProjectCheck | undefined {
    const state = this.#checks.get(projectId as ProjectId);
    return state === undefined
      ? undefined
      : {
          check_id: state.checkId,
          findings: state.findings,
          recorded_at: state.recordedAt,
          position: state.position,
          files: checkFilesState(state),
          // A check kept from before copies has none: the project's own folder.
          workstream_id: state.workstreamId ?? null,
        };
  }

  /** The project's running preview, or undefined when none runs. */
  preview(projectId: string): ProjectPreview | undefined {
    const running = this.#previews.get(projectId as ProjectId);
    return running === undefined
      ? undefined
      : {
          check_id: running.check_id,
          port: running.port,
          files: this.#filesOfCheck(projectId as ProjectId, running.check_id),
        };
  }

  /** A check of the project as the projection holds it, or undefined for one not known here. */
  #checkFindings(projectId: string, checkId: string): CheckFindings | undefined {
    return this.#checksById.get(projectId as ProjectId)?.get(checkId as CheckId)?.findings;
  }

  /** Whether the command is an instruction the host sent on its own for a failed check (ADR 0044). */
  isAutoFixCommand(commandId: string): boolean {
    return this.#autoFixCommands.has(commandId as CommandId);
  }

  /** Whether the execution's latest accepted instruction was one the host sent on its own (ADR 0044). */
  autoFixDriven(executionId: string): boolean {
    return this.#executions.get(executionId as ExecutionId)?.autoFixDriven ?? false;
  }

  /** The failed check whose output was last given to an agent in the project, or undefined (FX1). */
  checkOutputGiven(projectId: string): CheckId | undefined {
    return this.#checkOutputs.get(projectId as ProjectId);
  }

  /** The project's shared link now, or undefined (F1). */
  share(
    projectId: string,
  ): { readonly check_id: CheckId; readonly url: string; readonly way: ShareWay } | undefined {
    const share = this.#shares.get(projectId as ProjectId);
    return share === undefined
      ? undefined
      : { check_id: share.checkId, url: share.url, way: share.way };
  }

  /** The check whose game plays on the Mac now, or undefined. */
  macPlay(projectId: string): CheckId | undefined {
    return this.#macPlays.get(projectId as ProjectId);
  }

  /** The project's world as the Mac last read its description file, or undefined (ADR 0049). */
  world(projectId: string): ProjectWorldView | undefined {
    return this.#worlds.get(projectId as ProjectId);
  }

  /** One of the project's checkpoints, or undefined when it is another project's or none. */
  checkpoint(projectId: string, checkpointId: string): CheckpointView | undefined {
    return this.#checkpoints
      .get(projectId as ProjectId)
      ?.find((checkpoint) => checkpoint.checkpoint_id === checkpointId);
  }

  /**
   * Why the project's files may be changing now, or null when nothing of Halcyonic's can be writing
   * them: an execution that is starting, running, verifying or waiting; one whose state became
   * unknown other than by a restart, after which no runtime session survives; or a command in the
   * project still waiting for its outcome, such as a start or a restore. `except` is a command of the
   * project's that does not count, as a removal does not when it reads again before it records, and
   * `requests` false leaves out the assistant's own request commands, which write nothing (ADR 0063).
   */
  projectBusy(projectId: string, except?: string, requests = true): string | null {
    return this.projectBusyReason(projectId, except, requests)?.sentence ?? null;
  }

  /**
   * `projectBusy` with what is busy: a task, which has a Stop, named by its title (AUDIT2 S2, 2026-11-13), or a
   * command still waiting for its outcome, which has none. The sentence says the press did nothing; each caller
   * adds its own next step, a Stop only for a task (AUDIT2 S1).
   */
  projectBusyReason(
    projectId: string,
    except?: string,
    requests = true,
  ): { readonly kind: 'task' | 'command'; readonly sentence: string } | null {
    for (const state of this.#executionsByProject.get(projectId as ProjectId) ?? []) {
      const status = deriveExecutionStatus(state);
      const title = this.#workstreams.get(state.workstreamId)?.title ?? 'A task';
      if (ACTIVE_STATUSES.has(status))
        return {
          kind: 'task',
          sentence: `${title} is ${BUSY_WORDS[status]} in this project, so this was not done.`,
        };
      if (status === 'unknown' && !stoppedByRestart(state.unknownReason?.code))
        return {
          kind: 'task',
          sentence: `${title} may still be running in this project (its state is unknown), so this was not done.`,
        };
    }
    for (const command of this.#pendingCommands) {
      if (
        command.projectId === projectId &&
        command.envelope.command_id !== except &&
        (requests || !command.envelope.command_type.startsWith('workspace.request.'))
      )
        // By the button's own words, never the command's internal name (the lead, 20:46Z).
        // The press did nothing, and every caller adds its own next step (AUDIT2, 22:14Z).
        return {
          kind: 'command',
          sentence: `${COMMAND_SAYING[command.envelope.command_type].title} in this project is still under way, so this was not done.`,
        };
    }
    return null;
  }

  /** Whether a restore of the project's files has been accepted and has no outcome yet. */
  restoreInProgress(projectId: string): boolean {
    for (const command of this.#pendingCommands) {
      if (command.envelope.command_type === 'project.restore' && command.projectId === projectId)
        return true;
    }
    return false;
  }

  pendingCommands(): CommandView[] {
    return Array.from(this.#pendingCommands, toCommandView);
  }

  /** Every device ever paired, revoked ones included. */
  devices(): DeviceView[] {
    return this.#devices.devices();
  }

  /** Where the person chose the assistant to run, or null for this computer's model (never chosen counts as null). */
  assistantModel(): AssistantHostedModel | null {
    return this.#assistantModel;
  }

  device(deviceId: string): DeviceView | undefined {
    return this.#devices.device(deviceId);
  }

  /** The device a credential was issued to, revoked or not, by the credential's SHA-256. */
  deviceForCredential(credentialSha256: string): DeviceView | undefined {
    return this.#devices.deviceForCredential(credentialSha256);
  }

  // Application ------------------------------------------------------------------------------

  #dispatch(event: EventEnvelope, changes: ChangeSet, notes: ProjectionNote[]): void {
    switch (event.event_type) {
      case 'workspace.request.created': {
        const view = event.payload.request;
        if (this.#requests.has(view.request_id)) {
          notes.push(note('duplicate_entity', event, 'request already exists'));
          return;
        }
        // A whole-project request binds neither a task nor an execution (ADR 0030, 2026-11-10).
        const wholeProject = view.workstream_id === null && view.execution_id === null;
        const execution =
          view.execution_id === null ? undefined : this.#executions.get(view.execution_id);
        if (
          view.steps.length !== 0 ||
          view.earlier_steps !== 0 ||
          view.status !== 'running' ||
          view.reply !== null ||
          (wholeProject
            ? view.project_id !== null && !this.#projects.has(view.project_id)
            : execution?.projectId !== view.project_id ||
              execution?.workstreamId !== view.workstream_id) ||
          view.project_id !== event.project_id ||
          view.workstream_id !== event.workstream_id ||
          view.execution_id !== event.execution_id
        ) {
          notes.push(note('unknown_entity', event, 'request has inconsistent work binding'));
          return;
        }
        this.#requests.set(view.request_id, {
          view: structuredClone(view),
          principal: event.payload.principal ? { ...event.payload.principal } : null,
          prepared: new Map(),
          bindings: new Map(),
        });
        return;
      }
      case 'workspace.request.changed':
      case 'workspace.request.step_recorded': {
        const request = this.#requests.get(event.payload.request_id);
        if (
          !request ||
          request.view.project_id !== event.project_id ||
          request.view.workstream_id !== event.workstream_id ||
          request.view.execution_id !== event.execution_id
        ) {
          notes.push(note('unknown_entity', event, 'request binding does not match'));
          return;
        }
        if (event.event_type === 'workspace.request.changed') {
          request.view.status = event.payload.status;
          request.view.reply = structuredClone(event.payload.reply);
          // What the computer saw stands only on the change that ended the request with no new reply; any later
          // change (a resume, a reply) takes it away, so it never stands beside a reply.
          if (event.payload.observed === undefined) delete request.view.observed;
          else request.view.observed = structuredClone(event.payload.observed);
        } else {
          const step = event.payload.step,
            instruction = event.payload.prepared_command,
            proposed = event.payload.proposed_command,
            before = event.payload.proposed_before;
          if (
            instruction &&
            (request.view.execution_id === null ||
              instruction.payload.execution_id !== request.view.execution_id ||
              instruction.command_id !== step.command_id)
          ) {
            notes.push(note('unknown_entity', event, 'prepared request command does not match'));
            return;
          }
          // A proposal's or a press's command (ADR 0060): only on a step that holds the proposal, named by its id.
          if (
            proposed &&
            (instruction ||
              !step.proposal ||
              proposed.command_id !== step.command_id ||
              proposed.command_type !== step.proposal.command_type)
          ) {
            notes.push(note('unknown_entity', event, 'proposed request command does not match'));
            return;
          }
          // A start's earlier commands stand only beside its own start command, never alone or beside an instruction.
          if (
            before &&
            (proposed?.command_type !== 'execution.start' ||
              before.some(
                (earlier) =>
                  earlier.command_id === proposed.command_id ||
                  (earlier.command_type !== 'project.create' &&
                    earlier.command_type !== 'workstream.create'),
              ))
          ) {
            notes.push(note('unknown_entity', event, 'a start step carries other commands'));
            return;
          }
          const command: CommandEnvelope | null = instruction ?? proposed ?? null;
          const binding = request.bindings.get(step.step_id),
            retained = request.prepared.get(step.step_id);
          if (
            (binding &&
              (binding.tool !== step.tool_name ||
                binding.command !== step.command_id ||
                binding.position !== step.observation_position ||
                binding.turn !== step.active_turn_id ||
                binding.active !== step.had_active_turn)) ||
            step.observation_after <
              (retained?.step.observation_after ?? step.observation_position) ||
            step.observation_after > this.#position ||
            (command && retained && JSON.stringify(retained.command) !== JSON.stringify(command)) ||
            (before && retained && JSON.stringify(retained.before) !== JSON.stringify(before)) ||
            (step.command_id !== null && !command && !retained) ||
            (command &&
              [...request.prepared.entries()].some(
                ([id, action]) =>
                  id !== step.step_id && action.command.command_id === command.command_id,
              ))
          ) {
            notes.push(note('out_of_order', event, 'request step identity cannot change'));
            return;
          }
          request.bindings.set(step.step_id, {
            tool: step.tool_name,
            command: step.command_id,
            position: step.observation_position,
            turn: step.active_turn_id,
            active: step.had_active_turn,
          });
          const at = request.view.steps.findIndex((previous) => previous.step_id === step.step_id);
          if (at < 0 && request.view.steps.length >= 64) {
            request.view.steps.shift();
            request.view.earlier_steps++;
          }
          if (at < 0) request.view.steps.push(structuredClone(step));
          else request.view.steps[at] = structuredClone(step);
          const prepared = retained?.command ?? command;
          const earlier = retained?.before ?? before;
          if (prepared)
            request.prepared.set(step.step_id, {
              command: structuredClone(prepared),
              step: structuredClone(step),
              ...(earlier ? { before: structuredClone(earlier) } : {}),
            });
        }
        request.view.updated_at = event.ingested_at;
        return;
      }
      case 'project.created': {
        if (this.#projects.has(event.project_id)) {
          notes.push(note('duplicate_entity', event, `project ${event.project_id} already exists`));
          return;
        }
        this.#projects.set(event.project_id, {
          projectId: event.project_id,
          name: event.payload.name,
          location: event.payload.location,
          starter: null,
          starterTemplate: null,
          catalog: null,
          madeFrom: null,
          createdAt: event.occurred_at,
          updatedAt: event.ingested_at,
        });
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.location_set': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.location = event.payload.location;
        project.updatedAt = event.ingested_at;
        this.#updateProjectChecks(event.project_id, changes, (check) => {
          check.changed = true;
        });
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.removed_from_list': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        if (project.removedAt !== undefined) {
          notes.push(
            note('out_of_order', event, `project ${event.project_id} is already off the list`),
          );
          return;
        }
        project.removedAt = event.occurred_at;
        project.updatedAt = event.ingested_at;
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.returned': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        if (project.removedAt === undefined) {
          notes.push(note('out_of_order', event, `project ${event.project_id} is on the list`));
          return;
        }
        delete project.removedAt;
        project.location = event.payload.location;
        project.updatedAt = event.ingested_at;
        // The folder was bound again, as `project.location_set` binds one: its checks may no longer hold.
        this.#updateProjectChecks(event.project_id, changes, (check) => {
          check.changed = true;
        });
        // A client dropped the project's tasks when it went; the change that brings it back carries them all.
        changes.projects.add(event.project_id);
        for (const workstream of this.#workstreams.values())
          if (workstream.projectId === event.project_id)
            changes.workstreams.add(workstream.workstreamId);
        for (const execution of this.#executionsByProject.get(event.project_id) ?? [])
          changes.executions.add(execution.executionId);
        return;
      }
      case 'project.catalog_linked':
      case 'project.catalog_unlinked': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.catalog =
          event.event_type === 'project.catalog_linked' ? { ...event.payload.catalog } : null;
        project.updatedAt = event.ingested_at;
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.starter_applied': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.starter = event.payload.starter_id;
        project.starterTemplate = event.payload.template;
        project.updatedAt = event.ingested_at;
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.made_from': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.madeFrom = event.payload.original_project_id;
        project.updatedAt = event.ingested_at;
        return;
      }
      case 'project.checkpoint.recorded': {
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        const list = this.#checkpoints.get(event.project_id) ?? [];
        const view: CheckpointView = {
          checkpoint_id: event.payload.checkpoint_id,
          project_id: event.project_id,
          reason: event.payload.reason,
          execution_id: event.payload.execution_id,
          recorded_at: event.occurred_at,
          tree: event.payload.tree,
          files: event.payload.files,
          project_head: event.payload.project_head,
          ...(event.payload.turn_id === undefined ? {} : { turn_id: event.payload.turn_id }),
        };
        list.push(view);
        this.#checkpoints.set(event.project_id, list);
        const folder = this.#historyFolder(event.payload.execution_id);
        const same = (entry: HistoryEntry) =>
          entry.kind === 'version' &&
          entry.folder.kind === folder.kind &&
          (folder.kind === 'project' ||
            (entry.folder.kind === 'task_copy' &&
              entry.folder.workstream_id === folder.workstream_id));
        const previous = (this.#history.get(event.project_id) ?? []).findLast(same);
        this.#addHistory(event.project_id, {
          kind: 'version',
          entry_id: event.event_id,
          at: event.occurred_at,
          checkpoint: view,
          folder,
          previous: previous?.kind === 'version' ? previous.checkpoint.checkpoint_id : null,
          changed_files: null,
        });
        return;
      }
      case 'project.own_check.chosen': {
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        // A new choice starts again: no run of it has passed yet (ADR 0055).
        this.#ownChecks.set(event.project_id, {
          manager: event.payload.manager,
          scripts: event.payload.scripts,
          chosenAt: event.occurred_at,
          latest: null,
          everPassed: false,
          passedBefore: null,
          passedEarlier: false,
          byFolder: new Map(),
        });
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.own_check.recorded': {
        const own = this.#ownChecks.get(event.project_id);
        if (own === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} has no own check`));
          return;
        }
        const run = this.#freshCheck(
          event.project_id,
          event.payload.workstream_id ?? null,
          event.payload.check_id,
          event.payload.findings,
          event.occurred_at,
        );
        own.passedBefore = own.latest === null ? null : own.latest.findings.passed;
        own.passedEarlier = own.everPassed;
        if (event.payload.findings.passed) own.everPassed = true;
        const key = event.payload.workstream_id ?? '';
        const folder = own.byFolder.get(key) ?? {
          latest: null,
          before: null,
          earlier: false,
          ever: false,
        };
        own.byFolder.set(key, {
          latest: event.payload.findings.passed,
          before: folder.latest,
          earlier: folder.ever,
          ever: folder.ever || event.payload.findings.passed,
        });
        const open = this.#openChecks.get(event.project_id) ?? new Set<AnyCheckState>();
        if (own.latest !== null) open.delete(own.latest);
        own.latest = { ...run, first: event.payload.first };
        open.add(own.latest);
        this.#openChecks.set(event.project_id, open);
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.check.recorded': {
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        const checkState = this.#freshCheck(
          event.project_id,
          event.payload.workstream_id ?? null,
          event.payload.check_id,
          event.payload.findings,
          event.occurred_at,
        );
        this.#checks.set(event.project_id, checkState);
        const byId = this.#checksById.get(event.project_id) ?? new Map<CheckId, CheckState>();
        const open = this.#openChecks.get(event.project_id) ?? new Set<AnyCheckState>();
        const replaced = byId.get(event.payload.check_id);
        if (replaced !== undefined) open.delete(replaced);
        byId.set(event.payload.check_id, checkState);
        open.add(checkState);
        this.#checksById.set(event.project_id, byId);
        this.#openChecks.set(event.project_id, open);
        changes.projects.add(event.project_id);
        return;
      }
      case 'execution.check_output_given':
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        this.#checkOutputs.set(event.project_id, event.payload.check_id);
        return;
      case 'execution.instruction.dequeued': {
        // Its send began: no longer waiting, so no longer droppable or failed unsent (ADR 0056).
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        execution.queued = (execution.queued ?? []).filter(
          (queued) => queued.command_id !== event.payload.command_id,
        );
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'execution.instruction.pictures_read':
        // A record of what the host read and handed on (A4); the command's own events end it.
        return;
      case 'execution.instruction.queued': {
        // The person's instruction waits for the turn to end (ADR 0056); its words are in its command.
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        execution.queued = [
          ...(execution.queued ?? []),
          { command_id: event.payload.command_id, queued_at: event.occurred_at },
        ];
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'execution.approval.answered_by_host': {
        // The host approved it itself: no card for the person, counted as went ahead (the lead, 2026-11-13).
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        if (!execution.pendingApprovals.has(event.payload.approval_id)) {
          notes.push(
            note('unknown_entity', event, `approval ${event.payload.approval_id} is not pending`),
          );
          return;
        }
        // Kept pending for admission until the runtime resolves it; never shown as a card. A request too long for a
        // person is the host's to approve here, under the closed profile, since no person reads it (AUDIT3 M1).
        execution.hostAnswered ??= new Set();
        execution.hostAnswered.add(event.payload.approval_id);
        const answered = execution.pendingApprovals.get(event.payload.approval_id);
        if (answered !== undefined && !answered.approvable)
          execution.pendingApprovals.set(event.payload.approval_id, {
            ...answered,
            approvable: true,
          });
        execution.approvals = {
          wentAhead: (execution.approvals?.wentAhead ?? 0) + 1,
          asked: Math.max(0, (execution.approvals?.asked ?? 1) - 1),
        };
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'execution.approval.denied_by_host': {
        // The host denied it itself; the runtime's own resolution takes the card away (AUDIT3, 2026-11-13).
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'execution.approval.host_answer_withdrawn': {
        // The host's answer was not taken: the request is a card for the person after all (AUDIT3 S3).
        const execution = this.#executions.get(event.execution_id);
        if (execution?.hostAnswered?.delete(event.payload.approval_id) !== true) {
          notes.push(
            note(
              'unknown_entity',
              event,
              `approval ${event.payload.approval_id} was not answered by the host`,
            ),
          );
          return;
        }
        execution.approvals = {
          wentAhead: Math.max(0, (execution.approvals?.wentAhead ?? 1) - 1),
          asked: (execution.approvals?.asked ?? 0) + 1,
        };
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'execution.auto_fix.sent':
      case 'execution.auto_fix.ended': {
        // A failed check sent back to its agent on its own (ADR 0044).
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        const { check_id, cap } = event.payload;
        const findings = this.#checkFindings(event.project_id, check_id);
        // A project's own check (ADR 0055), while it is the latest run the projection keeps.
        const ownLatest = this.#ownChecks.get(event.project_id)?.latest;
        const ownRun = ownLatest?.checkId === check_id ? ownLatest : undefined;
        const failed: ('typecheck' | 'build' | 'page' | 'own_check')[] | undefined =
          ownRun !== undefined
            ? ownRun.findings.passed
              ? []
              : ['own_check']
            : findings === undefined
              ? undefined
              : fixSteps(findings);
        if (event.event_type === 'execution.auto_fix.sent') {
          this.#autoFixCommands.add(event.payload.command_id);
          execution.autoFix = {
            state: 'fixing',
            check_id,
            attempt: event.payload.attempt,
            cap,
            reason: null,
            ...(failed !== undefined && { failed_steps: failed }),
          };
        } else {
          const { attempts, reason } = event.payload;
          // A chain that ended on a passing check keeps what it fixed; else the ending check's own.
          const steps =
            (ownRun?.findings.passed ?? findings?.passed) === true &&
            execution.autoFix?.state === 'fixing'
              ? execution.autoFix.failed_steps
              : failed;
          execution.autoFix = {
            state: 'ended',
            check_id,
            attempt: attempts,
            cap,
            reason,
            ...(steps !== undefined && { failed_steps: [...steps] }),
          };
        }
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'project.preview.started':
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        this.#previews.set(event.project_id, {
          check_id: event.payload.check_id,
          port: event.payload.port,
        });
        changes.projects.add(event.project_id);
        return;
      case 'project.preview.stopped':
        if (this.#previews.get(event.project_id)?.check_id === event.payload.check_id) {
          this.#previews.delete(event.project_id);
          changes.projects.add(event.project_id);
        }
        return;
      case 'project.share.started':
        this.#shares.set(event.project_id, {
          checkId: event.payload.check_id,
          url: event.payload.url,
          way: event.payload.way,
        });
        if (this.#projects.has(event.project_id)) changes.projects.add(event.project_id);
        return;
      case 'project.share.stopped':
        if (this.#shares.get(event.project_id)?.checkId === event.payload.check_id) {
          this.#shares.delete(event.project_id);
          changes.projects.add(event.project_id);
        }
        return;
      case 'project.published':
        this.#published.set(event.project_id, {
          repository: event.payload.repository,
          check_id: event.payload.check_id,
          commit: event.payload.commit,
          address: event.payload.address,
          published_at: event.occurred_at,
        });
        if (this.#projects.has(event.project_id)) changes.projects.add(event.project_id);
        return;
      case 'project.unpublished':
        if (this.#published.delete(event.project_id)) changes.projects.add(event.project_id);
        return;
      case 'project.secret.stored':
      case 'project.secret.removed': {
        const name = event.payload.name;
        const kept = (this.#secrets.get(event.project_id) ?? []).filter((each) => each !== name);
        const names = event.event_type === 'project.secret.stored' ? [...kept, name].sort() : kept;
        if (names.length === 0) this.#secrets.delete(event.project_id);
        else this.#secrets.set(event.project_id, names);
        if (this.#projects.has(event.project_id)) changes.projects.add(event.project_id);
        return;
      }
      case 'project.sent':
      case 'project.sent_to_github':
        // The journal's fact (ADR 0039, amended; the second as journals before 2026-11-12 hold it); the
        // command's result carries the review request.
        return;
      case 'project.repository_made':
        // Who can see it, which Publish's review reads; a later first send of the folder goes into it.
        this.#madeRepositories.set(event.project_id, {
          repository: event.payload.repository,
          visibility: event.payload.visibility,
        });
        return;
      case 'project.repository_made_public': {
        const made = this.#madeRepositories.get(event.project_id);
        // Owner and name are case-insensitive on GitHub, as gh and a folder's origin may spell them.
        if (
          made !== undefined &&
          made.repository.toLowerCase() === event.payload.repository.toLowerCase()
        )
          this.#madeRepositories.set(event.project_id, { ...made, visibility: 'public' });
        return;
      }
      case 'project.repository_made_private': {
        const made = this.#madeRepositories.get(event.project_id);
        if (
          made !== undefined &&
          made.repository.toLowerCase() === event.payload.repository.toLowerCase()
        )
          this.#madeRepositories.set(event.project_id, { ...made, visibility: 'private' });
        return;
      }
      case 'project.mac_play.started':
        this.#macPlays.set(event.project_id, event.payload.check_id);
        if (this.#projects.has(event.project_id)) changes.projects.add(event.project_id);
        return;
      case 'project.mac_play.stopped':
        if (this.#macPlays.get(event.project_id) === event.payload.check_id) {
          this.#macPlays.delete(event.project_id);
          changes.projects.add(event.project_id);
        }
        return;
      case 'project.rules.updated': {
        // On or off by the person's press, or off by the host at a start that found the file changed (E5).
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        const { on, file, sha256 } = event.payload;
        if (on && file !== null && sha256 !== null) project.rules = { file, sha256 };
        else delete project.rules;
        project.updatedAt = event.ingested_at;
        changes.projects.add(event.project_id);
        return;
      }
      case 'execution.rules_given':
        // A record of what the host put before a task's first instruction (E5); nothing to keep.
        return;
      case 'project.world.observed':
        // Format 1, written before 2026-11-12: kept readable, and nothing of it is taken (ADR 0049).
        return;
      case 'project.world.brought_forward':
        // What the world is now arrives as its reading, `project.world.read`, which follows it.
        return;
      case 'project.world.read': {
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        // A refused reading keeps the last valid description: a bad edit never empties the world.
        this.#worlds.set(event.project_id, {
          description:
            event.payload.description ?? this.#worlds.get(event.project_id)?.description ?? null,
          refused: event.payload.refused,
          observed_at: event.occurred_at,
        });
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.checkpoints.pruned': {
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        const gone = new Set<string>(event.payload.checkpoint_ids);
        const list = this.#checkpoints.get(event.project_id) ?? [];
        this.#checkpoints.set(
          event.project_id,
          list.filter((view) => !gone.has(view.checkpoint_id)),
        );
        const history = this.#history.get(event.project_id) ?? [];
        this.#history.set(
          event.project_id,
          history.map((entry) =>
            entry.kind === 'version' &&
            entry.pruned_at === undefined &&
            gone.has(entry.checkpoint.checkpoint_id)
              ? { ...entry, pruned_at: event.occurred_at }
              : entry,
          ),
        );
        return;
      }
      case 'project.checkpoint.failed':
      case 'project.restored':
      case 'project.restore_incomplete':
        // Facts for the record and for clients; current state keeps the checkpoints and whether a check's files changed.
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        this.#addHistory(event.project_id, historyEntryOf(event));
        if (
          event.event_type === 'project.restored' &&
          (event.payload.changed_back.total > 0 ||
            event.payload.removed.total > 0 ||
            event.payload.returned.total > 0)
        ) {
          this.#updateProjectChecks(event.project_id, changes, (check) => {
            check.changed = true;
          });
        } else if (event.event_type === 'project.restore_incomplete' && event.payload.written) {
          this.#updateProjectChecks(event.project_id, changes, (check) => {
            check.tainted = true;
          });
        }
        return;
      case 'workstream.created': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        if (this.#workstreams.has(event.workstream_id)) {
          notes.push(
            note('duplicate_entity', event, `workstream ${event.workstream_id} already exists`),
          );
          return;
        }
        this.#workstreams.set(event.workstream_id, {
          workstreamId: event.workstream_id,
          projectId: event.project_id,
          title: event.payload.title,
          objective: event.payload.objective,
          executionIds: [],
          createdAt: event.occurred_at,
          updatedAt: event.ingested_at,
          copy: null,
          reviewedTree: null,
          fromCheckpoint: event.payload.from_checkpoint ?? null,
        });
        project.updatedAt = event.ingested_at;
        changes.workstreams.add(event.workstream_id);
        changes.projects.add(event.project_id);
        return;
      }
      case 'workstream.copy_made':
      case 'workstream.copy_refused':
      case 'workstream.copy_missing':
      case 'workstream.copy_found':
      case 'workstream.copy_changes_read':
      case 'workstream.added':
      case 'workstream.add_conflicted':
      case 'workstream.fitted':
        this.#applyCopy(event, changes, notes);
        return;
      case 'execution.created': {
        const workstream = this.#workstreams.get(event.workstream_id);
        if (workstream === undefined || workstream.projectId !== event.project_id) {
          notes.push(
            note('unknown_entity', event, `workstream ${event.workstream_id} does not exist`),
          );
          return;
        }
        if (this.#executions.has(event.execution_id)) {
          notes.push(
            note('duplicate_entity', event, `execution ${event.execution_id} already exists`),
          );
          return;
        }
        this.#executions.set(
          event.execution_id,
          createExecutionState({
            executionId: event.execution_id,
            workstreamId: event.workstream_id,
            projectId: event.project_id,
            runtime: event.payload.runtime,
            instruction: event.payload.instruction,
            directory: event.payload.directory,
            createdAt: event.occurred_at,
            startCommandId: event.causation_id as CommandId | null,
          }),
        );
        workstream.executionIds.push(event.execution_id);
        const start = this.#commands.get(event.causation_id as CommandId)?.envelope;
        if (
          start?.command_type === 'execution.start' &&
          start.payload.spending_cap_usd !== undefined
        ) {
          (this.#executions.get(event.execution_id) as ExecutionState).spendingCap = newSpendingCap(
            start.payload.spending_cap_usd,
          );
        }
        const inProject = this.#executionsByProject.get(event.project_id) ?? [];
        inProject.push(this.#executions.get(event.execution_id) as ExecutionState);
        this.#executionsByProject.set(event.project_id, inProject);
        workstream.updatedAt = event.ingested_at;
        changes.executions.add(event.execution_id);
        changes.workstreams.add(event.workstream_id);
        return;
      }
      case 'execution.files.observed': {
        // A record of what the Mac saw in the folder; it also says whether a prior check's files are still current (T2).
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        const changedFiles =
          event.payload.created.total > 0 ||
          event.payload.changed.total > 0 ||
          event.payload.deleted.total > 0;
        const incompleteRead = event.payload.skipped !== null || event.payload.not_read > 0;
        this.#updateProjectChecks(
          event.project_id,
          changes,
          (check) => {
            if (changedFiles) {
              check.changed = true;
            } else if (incompleteRead) {
              check.tainted = true;
            } else if (execution.activeTurn === null) {
              check.unobservedTurns.delete(event.execution_id);
            }
          },
          execution.directory ?? undefined,
        );
        return;
      }
      case 'execution.start_failed':
      case 'execution.state_unknown': {
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        if (event.event_type === 'execution.start_failed') {
          execution.startFailure = event.payload.error;
        } else {
          execution.unknownReason = { code: event.payload.code, message: event.payload.message };
          if (execution.directory !== null) {
            this.#updateProjectChecks(
              execution.projectId,
              changes,
              (check) => {
                check.tainted = true;
              },
              execution.directory,
            );
          }
        }
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'command.accepted':
      case 'command.rejected': {
        const commandId = event.payload.command.command_id;
        if (this.#commands.has(commandId)) {
          notes.push(note('duplicate_entity', event, `command ${commandId} already recorded`));
          return;
        }
        const accepted = event.event_type === 'command.accepted';
        const state: CommandState = {
          envelope: event.payload.command,
          principal: event.payload.principal ? { ...event.payload.principal } : null,
          status: accepted ? 'accepted' : 'rejected',
          projectId: event.project_id,
          workstreamId: event.workstream_id,
          executionId: event.execution_id,
          updatedAt: event.ingested_at,
          finishedAt: accepted ? null : this.#position,
          rejection: accepted ? null : event.payload.rejection,
          failure: null,
          result: null,
        };
        this.#commands.set(commandId, state);
        if (accepted) this.#pendingCommands.add(state);
        changes.commands.add(commandId);
        if (accepted && event.payload.command.command_type === 'execution.send_instruction') {
          const execution = this.#executions.get(event.payload.command.payload.execution_id);
          if (execution !== undefined)
            execution.autoFixDriven = this.#autoFixCommands.has(commandId);
        }
        return;
      }
      case 'command.completed':
      case 'command.failed': {
        const command = this.#commands.get(event.payload.command_id);
        if (command === undefined) {
          notes.push(
            note('unknown_entity', event, `command ${event.payload.command_id} was never accepted`),
          );
          return;
        }
        this.#pendingCommands.delete(command);
        // A queued instruction leaves the queue once sent and confirmed, or once it is not sent.
        const waiting =
          command.executionId === null ? undefined : this.#executions.get(command.executionId);
        if (waiting?.queued?.some((queued) => queued.command_id === event.payload.command_id)) {
          waiting.queued = waiting.queued.filter(
            (queued) => queued.command_id !== event.payload.command_id,
          );
          this.#touchExecution(waiting, event.ingested_at, changes);
        }
        if (event.event_type === 'command.completed') {
          command.status = 'completed';
          command.result = event.payload.result;
          // A task carried on under a new limit runs under it once the runtime confirmed the instruction.
          const sent = command.envelope;
          if (
            sent.command_type === 'execution.send_instruction' &&
            sent.payload.spending_cap_usd !== undefined
          ) {
            const carried = this.#executions.get(sent.payload.execution_id);
            if (carried !== undefined) {
              // A new span: the amount raised, counting from now.
              carried.spendingCap = newSpendingCap(
                sent.payload.spending_cap_usd,
                carried.spendingCap,
              );
              this.#touchExecution(carried, event.ingested_at, changes);
            }
          }
        } else {
          command.status = 'failed';
          command.failure = event.payload.failure;
        }
        command.projectId = event.project_id ?? command.projectId;
        command.workstreamId = event.workstream_id ?? command.workstreamId;
        command.executionId = event.execution_id ?? command.executionId;
        command.updatedAt = event.ingested_at;
        command.finishedAt = this.#position;
        changes.commands.add(event.payload.command_id);
        return;
      }
      case 'assistant.model.chosen':
        // The person's own choice of where the assistant runs: no project's entity either.
        this.#assistantModel = event.payload.model;
        return;
      case 'device.paired':
      case 'device.revoked':
        // Devices are no project's entity, so the event changes none that clients hold.
        notes.push(...this.#devices.apply(event));
        return;
      case 'runtime.execution.started':
      case 'runtime.turn.started':
      case 'runtime.turn.completed':
      case 'runtime.turn.failed':
      case 'runtime.turn.interrupted':
      case 'runtime.approval.requested':
      case 'runtime.approval.resolved':
      case 'runtime.question.asked':
      case 'runtime.question.resolved':
      case 'runtime.tool.started':
      case 'runtime.tool.completed':
      case 'runtime.tool.result':
      case 'runtime.agent_message':
      case 'runtime.test_run.started':
      case 'runtime.test_run.completed':
      case 'runtime.connection.lost':
      case 'runtime.connection.restored':
      case 'runtime.model.used':
      case 'runtime.plan.left':
      case 'runtime.usage.reported':
        this.#applyRuntime(event, changes, notes);
        return;
      default: {
        const unhandled: never = event;
        throw new Error(
          `unhandled event ${String((unhandled as { event_type?: unknown }).event_type)}`,
        );
      }
    }
  }

  /**
   * A task's own copy (ADR 0045): made once, or refused at a start without one; read, added, found
   * overlapping and fitted only while it is the task's own. Anything else is noted and ignored.
   */
  #applyCopy(
    event: Extract<EventEnvelope, { event_type: `workstream.${string}` }>,
    changes: ChangeSet,
    notes: ProjectionNote[],
  ): void {
    const workstream = this.#workstreams.get(event.workstream_id);
    if (workstream === undefined || workstream.projectId !== event.project_id) {
      notes.push(note('unknown_entity', event, `workstream ${event.workstream_id} does not exist`));
      return;
    }
    const copy = workstream.copy;
    if (
      event.event_type === 'workstream.copy_made' ||
      event.event_type === 'workstream.copy_refused'
    ) {
      if (copy?.state === 'own') {
        notes.push(note('duplicate_entity', event, 'the task already has a copy of its own'));
        return;
      }
      workstream.reviewedTree = null;
      workstream.copy =
        event.event_type === 'workstream.copy_made'
          ? {
              state: 'own',
              directory: event.payload.directory,
              made_at: event.occurred_at,
              base_tree: event.payload.base_tree,
              to_add: null,
              to_add_files: null,
              to_add_review: null,
              to_add_read_at: null,
              last_added_at: null,
              conflict: null,
            }
          : { state: 'refused', code: event.payload.code, refused_at: event.occurred_at };
    } else {
      if (copy?.state !== 'own') {
        notes.push(note('unknown_entity', event, 'the task has no copy of its own'));
        return;
      }
      switch (event.event_type) {
        case 'workstream.copy_missing':
          copy.missing_at = event.occurred_at;
          break;
        case 'workstream.copy_found':
          delete copy.missing_at;
          break;
        case 'workstream.copy_changes_read':
          copy.to_add = clonePathList(event.payload.changed);
          // A reading recorded before readings said how each file changed leaves it unknown.
          copy.to_add_files =
            event.payload.files === undefined ? null : cloneChangedFiles(event.payload.files);
          copy.to_add_review = event.payload.review ?? null;
          copy.to_add_read_at = event.occurred_at;
          workstream.reviewedTree = event.payload.tree;
          break;
        case 'workstream.added': {
          // The project's folder holds new files: a check of it, or around it, is of files changed.
          const folder = this.#projects.get(event.project_id)?.location?.path;
          if (folder !== undefined)
            this.#updateProjectChecks(
              event.project_id,
              changes,
              (check) => {
                check.changed = true;
              },
              folder,
            );
          copy.base_tree = event.payload.task_tree;
          workstream.reviewedTree = event.payload.task_tree;
          copy.to_add = { paths: [], total: 0 };
          copy.to_add_files = { files: [], total: 0 };
          // Nothing is left to add, so there is nothing to review.
          copy.to_add_review = null;
          copy.to_add_read_at = event.occurred_at;
          copy.last_added_at = event.occurred_at;
          copy.conflict = null;
          break;
        }
        case 'workstream.add_conflicted':
          copy.conflict = {
            files: event.payload.conflicted.files.map((file) => ({ ...file })),
            total: event.payload.conflicted.total,
          };
          break;
        case 'workstream.fitted':
          this.#updateProjectChecks(
            event.project_id,
            changes,
            (check) => {
              check.changed = true;
            },
            copy.directory,
          );
          // What the copy now differs by is read again after the agent's turn.
          copy.base_tree = event.payload.project_tree;
          workstream.reviewedTree = null;
          copy.to_add = null;
          copy.to_add_files = null;
          copy.to_add_review = null;
          copy.to_add_read_at = null;
          copy.conflict = null;
          break;
        default:
          break;
      }
    }
    workstream.updatedAt = event.ingested_at;
    changes.workstreams.add(workstream.workstreamId);
  }

  #applyRuntime(event: RuntimeEvent, changes: ChangeSet, notes: ProjectionNote[]): void {
    const execution = this.#executions.get(event.execution_id);
    if (execution === undefined) {
      notes.push(note('unknown_entity', event, `execution ${event.execution_id} does not exist`));
      return;
    }
    if (
      (event.event_type === 'runtime.tool.started' ||
        event.event_type === 'runtime.tool.completed') &&
      (event.project_id !== execution.projectId ||
        event.workstream_id !== execution.workstreamId ||
        event.source.runtime_id !== execution.runtime.runtime_id)
    ) {
      notes.push(note('turn_mismatch', event, 'Tool activity does not match its execution scope.'));
      return;
    }
    if (event.event_type === 'runtime.tool.completed' && event.payload.turn_id != null) {
      const start = this.#resultStarts.get(event.execution_id)?.get(event.payload.tool_call_id);
      if (start && start.turn !== event.payload.turn_id) {
        notes.push(
          note('turn_mismatch', event, 'Tool completion does not match its observed start.'),
        );
        return;
      }
    }
    if (event.event_type === 'runtime.tool.result') {
      const start = this.#resultStarts.get(event.execution_id)?.get(event.payload.tool_call_id);
      if (
        event.provenance.epistemic !== 'observed' ||
        !start ||
        start.turn === null ||
        start.turn !== event.payload.turn_id ||
        event.source.runtime_id !== execution.runtime.runtime_id ||
        event.project_id !== execution.projectId ||
        event.workstream_id !== execution.workstreamId
      ) {
        notes.push(
          note(
            'turn_mismatch',
            event,
            'Native result does not match its observed tool start and execution scope.',
          ),
        );
        return;
      }
    }
    if (event.sequence !== null) {
      if (execution.lastSequence !== null && event.sequence <= execution.lastSequence) {
        notes.push(
          note(
            'out_of_order',
            event,
            `sequence ${event.sequence} is not after ${execution.lastSequence}; state unchanged`,
          ),
        );
        return;
      }
      execution.lastSequence = event.sequence;
    }

    // Any observation other than a lost connection proves the runtime is observable again.
    if (event.event_type !== 'runtime.connection.lost') {
      execution.unknownReason = null;
      execution.runtimeStarted = true;
    }

    switch (event.event_type) {
      case 'runtime.execution.started':
        execution.nativeId = event.payload.native_id ?? execution.nativeId;
        execution.confined = event.payload.confined === true;
        execution.approvalLevel = event.payload.approval_level ?? null;
        execution.plan = event.payload.plan === true;
        execution.unconfinedReason = event.payload.unconfined_reason ?? null;
        execution.startedAt ??= event.occurred_at;
        break;
      case 'runtime.turn.started':
        execution.activeTurn = { turnId: event.payload.turn_id };
        execution.turnCount += 1;
        // A call opened between turns, whose end may never come, is not this turn's.
        execution.activeTools.clear();
        execution.startedAt ??= event.occurred_at;
        if (execution.directory !== null) {
          this.#updateProjectChecks(
            execution.projectId,
            changes,
            (check) => {
              check.unobservedTurns.add(execution.executionId);
            },
            execution.directory,
          );
        }
        break;
      case 'runtime.turn.completed':
      case 'runtime.turn.failed':
      case 'runtime.turn.interrupted': {
        const active = execution.activeTurn?.turnId ?? null;
        if (active !== null && event.payload.turn_id !== null && active !== event.payload.turn_id) {
          notes.push(
            note(
              'turn_mismatch',
              event,
              `turn ${event.payload.turn_id} ended while turn ${active} is active; state unchanged`,
            ),
          );
          return;
        }
        if (event.event_type === 'runtime.turn.completed') {
          endTurn(execution, 'completed', null);
          if (execution.spendingCap != null) capOnTurnEnded(execution.spendingCap);
        } else if (event.event_type === 'runtime.turn.failed') {
          endTurn(execution, 'failed', event.payload.error);
          if (execution.spendingCap != null) capOnTurnEnded(execution.spendingCap);
          if (execution.spendingCap != null && event.payload.error.code === SPENDING_CAP_REACHED) {
            execution.spendingCap.stoppedAt = { spentUsd: execution.spendingCap.spentUsd };
          }
        } else {
          endTurn(execution, 'interrupted', null);
          if (execution.spendingCap != null) capOnTurnEnded(execution.spendingCap);
        }
        if (execution.directory !== null) {
          this.#updateProjectChecks(
            execution.projectId,
            changes,
            (check) => {
              check.unobservedTurns.add(execution.executionId);
            },
            execution.directory,
          );
        }
        break;
      }
      case 'runtime.approval.requested':
        execution.pendingApprovals.set(event.payload.approval_id, {
          approval_id: event.payload.approval_id,
          subject: event.payload.subject,
          approvable: event.payload.complete,
          requested_at: event.occurred_at,
        });
        execution.approvals = {
          wentAhead: execution.approvals?.wentAhead ?? 0,
          asked: (execution.approvals?.asked ?? 0) + 1,
        };
        break;
      case 'runtime.approval.resolved':
        execution.pendingApprovals.delete(event.payload.approval_id);
        execution.hostAnswered?.delete(event.payload.approval_id);
        break;
      case 'runtime.question.asked': {
        const fitted = fitQuestion(event.payload.prompts, event.payload.answerable);
        execution.pendingQuestions.set(event.payload.question_id, {
          question_id: event.payload.question_id,
          prompts: [...fitted.prompts],
          answerable: fitted.answerable,
          asked_at: event.occurred_at,
        });
        break;
      }
      case 'runtime.question.resolved':
        execution.pendingQuestions.delete(event.payload.question_id);
        break;
      case 'runtime.tool.started':
        execution.activeTools.set(event.payload.tool_call_id, {
          tool_call_id: event.payload.tool_call_id,
          tool_name: event.payload.tool_name,
          title: event.payload.title,
          started_at: event.occurred_at,
        });
        break;
      case 'runtime.tool.completed':
        execution.activeTools.delete(event.payload.tool_call_id);
        break;
      case 'runtime.tool.result':
        // Read-only recorded evidence; never a test event or semantic task outcome.
        break;
      case 'runtime.agent_message':
        // Agent text is history and a claim, not state. It changes nothing but `updated_at`.
        break;
      case 'runtime.test_run.started':
        execution.activeTestRun = {
          test_run_id: event.payload.test_run_id,
          label: event.payload.label,
          started_at: event.occurred_at,
        };
        break;
      case 'runtime.test_run.completed': {
        const started =
          execution.activeTestRun?.test_run_id === event.payload.test_run_id
            ? execution.activeTestRun
            : null;
        if (started !== null) execution.activeTestRun = null;
        execution.lastTestRun = {
          test_run_id: event.payload.test_run_id,
          label: started?.label ?? null,
          outcome: event.payload.outcome,
          summary: event.payload.summary,
          completed_at: event.occurred_at,
        };
        break;
      }
      case 'runtime.connection.lost':
        execution.unknownReason = {
          code: 'runtime_connection_lost',
          message: event.payload.reason,
        };
        if (execution.directory !== null) {
          this.#updateProjectChecks(
            execution.projectId,
            changes,
            (check) => {
              check.tainted = true;
            },
            execution.directory,
          );
        }
        break;
      case 'runtime.connection.restored':
        // Observable again, which every runtime event other than a loss already records above;
        // what changed meanwhile arrives in the events that follow.
        // Nothing it could be handed now (the sum not known, a turn in flight unreported, nothing
        // left): it waits for a limit that counts from now, the cut turn ending after this or not.
        if (execution.spendingCap != null && spendingAllowance(execution) === null) {
          execution.spendingCap.waiting = true;
        }
        break;
      case 'runtime.model.used':
        execution.modelRef = event.payload.model_ref;
        break;
      case 'runtime.plan.left':
        execution.planLeft = true;
        break;
      case 'runtime.usage.reported':
        execution.usage = addTurnUsage(execution.usage ?? null, event.payload);
        if (execution.spendingCap != null) capOnUsage(execution.spendingCap, event.payload);
        break;
      default: {
        const unhandled: never = event;
        throw new Error(
          `unhandled runtime event ${String((unhandled as { event_type?: unknown }).event_type)}`,
        );
      }
    }
    this.#touchExecution(execution, event.ingested_at, changes);
    // The project's usage reads its runs' reports and, once one reported, their ended turns too.
    const ended =
      event.event_type === 'runtime.turn.completed' ||
      event.event_type === 'runtime.turn.failed' ||
      event.event_type === 'runtime.turn.interrupted';
    if (
      this.#projects.has(execution.projectId) &&
      (event.event_type === 'runtime.usage.reported' ||
        (ended &&
          (this.#executionsByProject.get(execution.projectId) ?? []).some(
            (each) => each.usage != null,
          )))
    ) {
      changes.projects.add(execution.projectId);
    }
  }

  #touchExecution(execution: ExecutionState, at: Timestamp, changes: ChangeSet): void {
    execution.updatedAt = at;
    changes.executions.add(execution.executionId);
    const workstream = this.#workstreams.get(execution.workstreamId);
    if (workstream !== undefined) {
      workstream.updatedAt = at;
      changes.workstreams.add(workstream.workstreamId);
    }
  }

  #workstreamView = (state: WorkstreamState): WorkstreamView => {
    const executions: ExecutionState[] = [];
    for (const id of state.executionIds) {
      const execution = this.#executions.get(id);
      if (execution !== undefined) executions.push(execution);
    }
    const current = executions.at(-1);
    return {
      workstream_id: state.workstreamId,
      project_id: state.projectId,
      title: state.title,
      objective: state.objective,
      status: current === undefined ? 'created' : deriveExecutionStatus(current),
      attention: deriveAttention(executions, current),
      current_execution_id: current?.executionId ?? null,
      execution_ids: [...state.executionIds],
      // A task kept from before copies has no copy field at all: as none.
      ...(state.copy == null ? {} : { copy: cloneCopy(state.copy) }),
      ...(state.fromCheckpoint == null ? {} : { from_checkpoint: state.fromCheckpoint }),
      created_at: state.createdAt,
      updated_at: state.updatedAt,
    };
  };

  /** The project with its latest check, its preview and its play, each only when there is one (M1.8e, T2). */
  #projectView = (state: ProjectState): ProjectView => {
    const view: ProjectView = toProjectView(state);
    const check = this.latestCheck(state.projectId);
    if (check !== undefined) {
      view.check = {
        check_id: check.check_id,
        recorded_at: check.recorded_at,
        position: check.position,
        files: check.files,
        findings: check.findings,
        ...(check.workstream_id === null ? {} : { workstream_id: check.workstream_id }),
      };
    }
    const preview = this.preview(state.projectId);
    if (preview !== undefined) {
      view.preview = {
        check_id: preview.check_id,
        port: preview.port,
        files: preview.files,
      };
    }
    const play = this.#macPlays.get(state.projectId);
    if (play !== undefined) {
      view.mac_play = {
        check_id: play,
        files: this.#filesOfCheck(state.projectId, play),
      };
    }
    const published = this.#published.get(state.projectId);
    if (published !== undefined) view.published = { ...published };
    const usage = projectUsage(this.#executionsByProject.get(state.projectId) ?? []);
    if (usage !== null) view.usage = usage;
    const secrets = this.#secrets.get(state.projectId);
    if (secrets !== undefined) view.secrets = [...secrets];
    const share = this.#shares.get(state.projectId);
    if (share !== undefined) {
      view.share = {
        check_id: share.checkId,
        url: share.url,
        way: share.way,
        files: this.#filesOfCheck(state.projectId, share.checkId),
      };
    }
    const world = this.#worlds.get(state.projectId);
    if (world !== undefined) view.world = world;
    const own = this.ownCheck(state.projectId);
    if (own !== undefined) {
      const { passed_before: _before, passed_earlier: _earlier, ...shown } = own;
      view.own_check = shown;
    }
    if (state.rules !== undefined) view.rules = { ...state.rules };
    if (state.removedAt !== undefined) view.removed_at = state.removedAt;
    return view;
  };

  #filesOfCheck(projectId: ProjectId, checkId: CheckId): CheckFilesState {
    const state = this.#checksById.get(projectId)?.get(checkId);
    return state === undefined ? 'unknown' : checkFilesState(state);
  }

  /**
   * `directory`, where given, is the folder the change happened in: only checks of that folder, or
   * of one inside it or around it, are reached (ADR 0045); a check whose folder is not known always.
   */
  #updateProjectChecks(
    projectId: ProjectId,
    changes: ChangeSet,
    mutate: (state: AnyCheckState) => void,
    directory?: string,
  ): void {
    const checks = this.#openChecks.get(projectId);
    if (checks === undefined || checks.size === 0) return;
    const latestId = this.#checks.get(projectId)?.checkId;
    const previewId = this.#previews.get(projectId)?.check_id;
    const playId = this.#macPlays.get(projectId);
    const shareId = this.#shares.get(projectId)?.checkId;
    const ownId = this.#ownChecks.get(projectId)?.latest?.checkId;
    let viewChanged = false;
    for (const state of checks) {
      this.#onCheckVisited?.();
      if (
        directory !== undefined &&
        state.folder != null &&
        !foldersOverlap(state.folder, directory)
      )
        continue;
      const before = checkFilesState(state);
      mutate(state);
      const after = checkFilesState(state);
      if (
        before !== after &&
        (state.checkId === latestId ||
          state.checkId === previewId ||
          state.checkId === playId ||
          state.checkId === shareId ||
          state.checkId === ownId)
      ) {
        viewChanged = true;
      }
      // Changed reads changed whatever comes next, so nothing later needs to visit it.
      if (state.changed) checks.delete(state);
    }
    if (viewChanged && this.#projects.has(projectId)) changes.projects.add(projectId);
  }

  #materialize(changes: ChangeSet): EntityChanges {
    // A project taken off the list is carried only as its own view, with `removed_at`, so a client drops
    // it; nothing of its tasks reaches a client while it is off (ADR 0063).
    const listed = (state: { readonly projectId: ProjectId }) => this.listed(state.projectId);
    return {
      projects: pickViews(changes.projects, this.#projects, this.#projectView),
      workstreams: pickViews(changes.workstreams, this.#workstreams, this.#workstreamView, listed),
      executions: pickViews(changes.executions, this.#executions, this.#view, listed),
      commands: pickViews(changes.commands, this.#commands, toCommandView),
    };
  }
}

function boundTitle(title: string | null | undefined): string | null {
  if (title == null || title.length <= 180) return title ?? null;
  const chars = Array.from(title);
  return chars.length <= 180 ? title : `${chars.slice(0, 179).join('')}…`;
}

function pickViews<K, S, V>(
  ids: Set<K>,
  map: Map<K, S>,
  view: (state: S) => V,
  shown: (state: S) => boolean = () => true,
): V[] {
  if (ids.size === 0) return [];
  const out: V[] = [];
  for (const id of ids) {
    const state = map.get(id);
    if (state !== undefined && shown(state)) out.push(view(state));
  }
  return out;
}

function note(code: ProjectionNote['code'], event: EventEnvelope, message: string): ProjectionNote {
  return { code, event_id: event.event_id, message };
}

function toProjectView(state: ProjectState): ProjectView {
  return {
    project_id: state.projectId,
    name: state.name,
    location: state.location,
    starter: state.starter,
    created_at: state.createdAt,
    updated_at: state.updatedAt,
  };
}

function toCommandView(state: CommandState): CommandView {
  return {
    command_id: state.envelope.command_id,
    command_type: state.envelope.command_type,
    status: state.status,
    project_id: state.projectId,
    workstream_id: state.workstreamId,
    execution_id: state.executionId,
    issued_at: state.envelope.issued_at,
    updated_at: state.updatedAt,
    rejection: state.rejection,
    failure: state.failure,
    result: state.result,
  };
}

function toFacts(state: ExecutionState): ExecutionFacts {
  return {
    executionId: state.executionId,
    projectId: state.projectId,
    workstreamId: state.workstreamId,
    runtimeId: state.runtime.runtime_id,
    directory: state.directory,
    status: deriveExecutionStatus(state),
    hasNativeSession: state.runtimeStarted,
    activeTurnId: state.activeTurn?.turnId ?? null,
    hasActiveTurn: state.activeTurn !== null,
    pendingApprovals: [...state.pendingApprovals.values()],
    // Only those shown can be answered: one behind them waits its turn.
    pendingQuestions: shownQuestions(state.pendingQuestions),
    inPlan: state.plan && !state.planLeft,
    spendingCap: spendingCapView(state)?.state ?? 'none',
  };
}

function clonePathList(list: { paths: readonly string[]; total: number }): {
  paths: string[];
  total: number;
} {
  return { paths: [...list.paths], total: list.total };
}

function cloneChangedFiles(list: ChangedFiles): ChangedFiles {
  return { files: list.files.map((file) => ({ ...file })), total: list.total };
}

function cloneCopy(copy: WorkstreamCopy): WorkstreamCopy {
  if (copy.state === 'refused') return { ...copy };
  return {
    ...copy,
    to_add: copy.to_add === null ? null : clonePathList(copy.to_add),
    to_add_files: copy.to_add_files === null ? null : cloneChangedFiles(copy.to_add_files),
    conflict:
      copy.conflict === null
        ? null
        : { files: copy.conflict.files.map((file) => ({ ...file })), total: copy.conflict.total },
  };
}

/** A history entry of a version not kept or of a restore, as its event says it. */
function historyEntryOf(
  event: Extract<
    EventEnvelope,
    { event_type: 'project.checkpoint.failed' | 'project.restored' | 'project.restore_incomplete' }
  >,
): HistoryEntry {
  const at = { entry_id: event.event_id, at: event.occurred_at };
  switch (event.event_type) {
    case 'project.checkpoint.failed':
      return {
        kind: 'not_kept',
        ...at,
        reason: event.payload.reason,
        execution_id: event.payload.execution_id,
        failure: { ...event.payload.failure },
      };
    case 'project.restored':
      return {
        kind: 'restored',
        ...at,
        checkpoint_id: event.payload.checkpoint_id,
        before_restore_checkpoint_id: event.payload.before_restore_checkpoint_id,
        changed_back: event.payload.changed_back.total,
        removed: event.payload.removed.total,
        returned: event.payload.returned.total,
        project_head_moved: event.payload.project_head_moved,
      };
    case 'project.restore_incomplete':
      return {
        kind: 'restore_incomplete',
        ...at,
        checkpoint_id: event.payload.checkpoint_id,
        before_restore_checkpoint_id: event.payload.before_restore_checkpoint_id,
        written: event.payload.written,
        not_restored: event.payload.not_restored.total,
        failure: { ...event.payload.failure },
      };
  }
}
