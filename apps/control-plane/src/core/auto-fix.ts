import {
  type AutoFixEndReason,
  COMMAND_SCHEMA_VERSION,
  type CommandEnvelope,
  type CommandId,
  type EventEnvelope,
  type ExecutionId,
  type ExecutionView,
  type ProjectId,
  type Timestamp,
} from '@halcyonic/contracts';
import {
  fitFix,
  fixSteps,
  folderInUse,
  foldersOverlap,
  laptopFix,
  type Projection,
  readingFix,
} from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import type { OwnChecks } from '../own-check/own-check.ts';
import type { HostPreviews } from '../preview/preview.ts';
import type { CommandService } from './command-service.ts';
import { type Cause, causedBy, controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import type { EventPublisher } from './publisher.ts';
import type { Recorder } from './recorder.ts';
import type { RuntimeRegistry } from './runtime-registry.ts';

/** How many times a failed check goes back to its agent on its own, per chain (ADR 0044). */
export const AUTO_FIX_CAP = 2;

/**
 * The host's own words for a try. The check's first lines follow them as FX1 gives them to any
 * instruction, marked as the Mac's own check; the page's errors, already journaled with the
 * check, follow here.
 */
export const AUTO_FIX_WORDS =
  '[Sent by your computer on its own, not by the person] Your computer checked this project after your last turn, and the check failed. Fix what it found, change nothing else, then end your turn.';
/** The host's words for a check that passed but does not read on a laptop (ADR 0044's second trigger). */
export const AUTO_FIX_READING_WORDS =
  '[Sent by your computer on its own, not by the person] Your computer checked this project after your last turn: it builds and runs, but a person cannot read it on a laptop. Fix what it found, change nothing else, then end your turn.';
/** The host's words for a check that passed and reads, but part of it does not fit a laptop's view (the same trigger). */
export const AUTO_FIX_FIT_WORDS =
  "[Sent by your computer on its own, not by the person] Your computer checked this project after your last turn: it builds and runs, but part of it does not fit a laptop's screen. Fix what it found, change nothing else, then end your turn.";

type ProjectCheck = NonNullable<ReturnType<Projection['latestCheck']>>;

const CLIENT = { name: 'halcyonic-control-plane', version: null, device_label: null } as const;

export interface AutoFixDeps {
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly publisher: EventPublisher;
  readonly commands: Pick<CommandService, 'submit'>;
  readonly registry: Pick<RuntimeRegistry, 'get'>;
  readonly previews: Pick<HostPreviews, 'failedCheckOutput'> | null;
  /** A project's own check (ADR 0055), or null where it is not set up. */
  readonly ownChecks?: Pick<OwnChecks, 'failedOutput'> | null;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  readonly cap?: number;
  /** Whether the journal is the person's own (`live`); a fixture's journal is never appended to. */
  readonly live: boolean;
}

/**
 * Sends a project's failed automatic check back to the agent whose turn changed its files, as
 * that agent's next instruction, up to a cap (ADR 0044). Every try and every end is journaled;
 * the person's Stop or their own words end it at once, and nothing is sent after a Stop, while
 * a person is needed, or to a runtime that does not take instructions at rest.
 */
export class AutoFix {
  readonly #deps: AutoFixDeps;
  readonly #cap: number;
  /** Per project, the executions whose turns changed its files since its last check began. */
  readonly #changed = new Map<ProjectId, Set<ExecutionId>>();
  /** The automatic checks under way, by command, with the executions each one follows. */
  readonly #checks = new Map<CommandId, { projectId: ProjectId; executions: Set<ExecutionId> }>();
  /** Executions the person asked to stop since their own last instruction to it. */
  readonly #stopAsked = new Set<ExecutionId>();
  /** Executions the person gave words that were refused, since their own last accepted instruction. */
  readonly #wordsRefused = new Set<ExecutionId>();
  /** The host's own interrupts of a try the person asked to stop before its turn began. */
  readonly #ownInterrupts = new Set<string>();
  readonly #interrupted = new Set<ExecutionId>();
  #unsubscribe: (() => void) | null = null;
  #closed = false;

  constructor(deps: AutoFixDeps) {
    this.#deps = deps;
    this.#cap = deps.cap ?? AUTO_FIX_CAP;
  }

  /** Ends any chain a restart cut short (no try is replayed), then follows the journal. */
  attach(): void {
    if (this.#deps.live)
      for (const view of this.#deps.projection.executions()) {
        if (view.auto_fix?.state === 'fixing') this.#end(view.execution_id, 'host_restarted', null);
      }
    this.#unsubscribe = this.#deps.publisher.subscribe(({ event }) => this.#on(event));
  }

  /**
   * The control plane's automatic check of `folder`, a task's own copy or the project's folder
   * (ADR 0045), is about to be submitted as `commandId`: it follows the executions whose turns
   * changed files there, and leaves those that worked in another task's copy for that one's check.
   */
  checkQueued(projectId: ProjectId, commandId: CommandId, folder: string | null = null): void {
    this.#checks.set(commandId, { projectId, executions: this.#take(projectId, folder) });
  }

  /** Takes the executions of the project that changed files in `folder`, or all for null. */
  #take(projectId: ProjectId, folder: string | null): Set<ExecutionId> {
    const all = this.#changed.get(projectId) ?? new Set<ExecutionId>();
    const taken = new Set<ExecutionId>();
    const rest = new Set<ExecutionId>();
    for (const id of all) {
      const directory = this.#deps.projection.execution(id)?.directory ?? null;
      const here = folder === null || directory === null || foldersOverlap(directory, folder);
      (here ? taken : rest).add(id);
    }
    if (rest.size > 0) this.#changed.set(projectId, rest);
    else this.#changed.delete(projectId);
    return taken;
  }

  /** The folder a check checks: the named task's own copy, else the project's folder. */
  #checkedFolder(projectId: ProjectId, workstreamId: string | undefined): string | null {
    const copy =
      workstreamId === undefined ? undefined : this.#deps.projection.workstream(workstreamId)?.copy;
    if (copy?.state === 'own') return copy.directory;
    return this.#deps.projection.project(projectId)?.location?.path ?? null;
  }

  close(): void {
    this.#closed = true;
    this.#unsubscribe?.();
    this.#unsubscribe = null;
  }

  #later(work: () => void): void {
    queueMicrotask(() => {
      if (this.#closed) return;
      try {
        work();
      } catch (error) {
        this.#deps.logger.error({ err: error }, 'auto fix step failed');
      }
    });
  }

  #fixing(executionId: ExecutionId): boolean {
    return this.#deps.projection.execution(executionId)?.auto_fix?.state === 'fixing';
  }

  #on(event: EventEnvelope): void {
    if (this.#closed) return;
    switch (event.event_type) {
      case 'execution.files.observed': {
        const { created, changed, deleted } = event.payload;
        const total = created.total + changed.total + deleted.total;
        if (total === 0) {
          if (this.#fixing(event.execution_id))
            this.#later(() => this.#end(event.execution_id, 'no_change', null));
          return;
        }
        const project = this.#deps.projection.project(event.project_id);
        if (project === undefined || project.location === null) return;
        // A starter's project, or one whose own check the person chose (ADR 0055).
        if (
          project.starter === null &&
          this.#deps.projection.ownCheck(event.project_id) === undefined
        )
          return;
        const set = this.#changed.get(event.project_id) ?? new Set<ExecutionId>();
        set.add(event.execution_id);
        this.#changed.set(event.project_id, set);
        return;
      }
      case 'command.accepted':
      case 'command.rejected': {
        const command = event.payload.command;
        const accepted = event.event_type === 'command.accepted';
        switch (command.command_type) {
          case 'project.check':
            // A check the person asked for takes the automatic one's place, as A1 does, for the
            // folder it checks (ADR 0045).
            if (accepted && !this.#checks.has(command.command_id))
              this.#take(
                command.payload.project_id,
                this.#checkedFolder(command.payload.project_id, command.payload.workstream_id),
              );
            if (!accepted && this.#checks.has(command.command_id)) {
              // A refused automatic check ends the chains it followed: no check, no next try.
              const executions = this.#checks.get(command.command_id)?.executions ?? new Set();
              this.#checks.delete(command.command_id);
              for (const executionId of executions)
                if (this.#fixing(executionId))
                  this.#later(() => this.#end(executionId, 'check_unavailable', null));
            }
            return;
          case 'execution.interrupt': {
            if (this.#ownInterrupts.has(command.command_id)) return;
            // The person's Stop, refused or not: no try follows it, and a try under way ends now.
            const executionId = command.payload.execution_id;
            this.#stopAsked.add(executionId);
            if (this.#fixing(executionId))
              this.#later(() => this.#end(executionId, 'person_stopped', null));
            return;
          }
          case 'execution.send_instruction': {
            if (this.#deps.projection.isAutoFixCommand(command.command_id)) return;
            const executionId = command.payload.execution_id;
            if (!accepted) {
              // The person's words, refused (a try runs on a runtime that takes none while running,
              // or the try's own command has no outcome yet), still end its chain and block the next
              // try, as their Stop does (AUDIT2, 2026-11-10).
              this.#wordsRefused.add(executionId);
              if (this.#fixing(executionId))
                this.#later(() => this.#end(executionId, 'person_instructed', null));
              return;
            }
            this.#wordsRefused.delete(executionId);
            this.#stopAsked.delete(executionId);
            this.#interrupted.delete(executionId);
            const projectId =
              event.project_id ?? this.#deps.projection.execution(executionId)?.project_id;
            if (projectId != null) this.#personInstructed(projectId);
            return;
          }
          case 'execution.start':
            if (accepted && event.project_id !== null) this.#personInstructed(event.project_id);
            return;
          default:
            return;
        }
      }
      case 'command.completed':
      case 'command.failed': {
        const commandId = event.payload.command_id;
        const failed = event.event_type === 'command.failed';
        if (this.#checks.has(commandId)) {
          this.#later(() => this.#evaluate(commandId, failed));
          return;
        }
        if (failed && this.#deps.projection.isAutoFixCommand(commandId)) {
          const executionId = this.#deps.projection.command(commandId)?.execution_id ?? null;
          if (executionId !== null) this.#later(() => this.#end(executionId, 'send_failed', null));
        }
        return;
      }
      case 'runtime.turn.started': {
        const executionId = event.execution_id;
        if (
          this.#stopAsked.has(executionId) &&
          this.#deps.projection.autoFixDriven(executionId) &&
          !this.#interrupted.has(executionId)
        ) {
          // The person asked to stop before the try's turn began: stop it as soon as it runs.
          this.#interrupted.add(executionId);
          this.#later(() => this.#interrupt(executionId));
        }
        return;
      }
      case 'runtime.turn.interrupted':
      case 'runtime.turn.failed':
      case 'execution.state_unknown':
      case 'runtime.connection.lost': {
        const executionId = event.execution_id;
        if (!this.#fixing(executionId)) return;
        const reason: AutoFixEndReason =
          event.event_type === 'runtime.turn.interrupted' && this.#stopAsked.has(executionId)
            ? 'person_stopped'
            : 'turn_failed';
        this.#later(() => this.#end(executionId, reason, null));
        return;
      }
      default:
        return;
    }
  }

  /** The person's own words to an agent in the project end every chain there. */
  #personInstructed(projectId: string): void {
    for (const view of this.#deps.projection.executions()) {
      if (view.project_id !== projectId || view.auto_fix?.state !== 'fixing') continue;
      const executionId = view.execution_id;
      this.#later(() => this.#end(executionId, 'person_instructed', null));
    }
  }

  #evaluate(commandId: CommandId, commandFailed: boolean): void {
    const entry = this.#checks.get(commandId);
    this.#checks.delete(commandId);
    if (entry === undefined) return;
    const { projection } = this.#deps;
    const executions = [...entry.executions].filter((id) => projection.execution(id) !== undefined);
    if (projection.project(entry.projectId)?.starter === null) {
      this.#evaluateOwn(entry.projectId, commandId, commandFailed, executions);
      return;
    }
    const check = commandFailed ? undefined : projection.latestCheck(entry.projectId);
    if (check === undefined) {
      for (const id of executions) this.#end(id, 'check_unavailable', null);
      return;
    }
    // A check that passed but does not read or fit on a laptop goes back too, under the same cap (ADR 0044, VRIFY
    // 2026-11-13).
    if (check.findings.passed && laptopFix(check.findings) === null) {
      for (const id of executions) if (this.#fixing(id)) this.#end(id, 'passed', check.check_id);
      return;
    }
    // A check whose only unknown is whether the page draws its own look, since the starter's picture could not be made,
    // is the product's failure, never the agent's (VRIFY, 2026-11-13): no try is sent or counted, and no chain begins. A
    // chain under way ends with the tries it made, as one whose check gave nothing to act on.
    if (check.findings.verdict === 'not_known') {
      for (const id of executions)
        if (this.#fixing(id)) this.#end(id, 'nothing_to_send', check.check_id);
      return;
    }
    if (executions.length !== 1) {
      // Never guess whose change failed the check.
      for (const id of executions) this.#end(id, 'several_tasks', check.check_id);
      return;
    }
    const [executionId] = executions as [ExecutionId];
    const view = projection.execution(executionId);
    if (view === undefined) return;
    const chain = view.auto_fix?.state === 'fixing' ? view.auto_fix : null;
    // A turn the host itself set going whose chain already ended starts no new chain.
    if (chain === null && projection.autoFixDriven(executionId)) return;
    const attempts = chain?.attempt ?? 0;
    const blocked = this.#blocked(view, attempts);
    if (blocked !== null) {
      this.#end(executionId, blocked, check.check_id);
      return;
    }
    const text = this.#instruction(entry.projectId, check);
    if (text === null) {
      this.#end(executionId, 'nothing_to_send', check.check_id);
      return;
    }
    this.#send(view, check, attempts + 1, text, causedBy(commandId));
  }

  /**
   * A project's own check (ADR 0055): as a starter's, except whose fault a failure is. A failure starts a
   * chain only when the run before the turn passed; one that timed out, met the sandbox's refusal, or
   * found the chosen command changed is not sent at all, since no agent is known to have broken it.
   */
  #evaluateOwn(
    projectId: ProjectId,
    commandId: CommandId,
    commandFailed: boolean,
    executions: ExecutionId[],
  ): void {
    const { projection } = this.#deps;
    const own = commandFailed ? undefined : projection.ownCheck(projectId);
    const latest = own?.latest ?? null;
    if (own === undefined || latest === null) {
      for (const id of executions) this.#end(id, 'check_unavailable', null);
      return;
    }
    if (latest.findings.passed) {
      for (const id of executions) if (this.#fixing(id)) this.#end(id, 'passed', latest.check_id);
      return;
    }
    if (executions.length !== 1) {
      for (const id of executions) this.#end(id, 'several_tasks', latest.check_id);
      return;
    }
    const [executionId] = executions as [ExecutionId];
    const view = projection.execution(executionId);
    if (view === undefined) return;
    const chain = view.auto_fix?.state === 'fixing' ? view.auto_fix : null;
    if (chain === null && projection.autoFixDriven(executionId)) return;
    const failed = latest.findings.runs.find((run) => run.exit !== 0);
    if (
      latest.findings.outcome !== 'ran' ||
      failed === undefined ||
      failed.exit === null ||
      failed.timed_out ||
      failed.refused
    ) {
      this.#end(executionId, 'check_refused', latest.check_id);
      return;
    }
    // Whose fault it is: only a failure right after a run of the same folder that passed is the turn's to fix, the
    // project's folder or the task's own copy, never another folder's (AUDIT3 6).
    const baseline = projection.ownCheckBaseline(projectId, latest.workstream_id ?? null);
    if (chain === null && baseline.passedBefore !== true) {
      this.#end(
        executionId,
        baseline.passedEarlier ? 'failing_before' : 'never_passed',
        latest.check_id,
      );
      return;
    }
    const attempts = chain?.attempt ?? 0;
    const blocked = this.#blocked(view, attempts);
    if (blocked !== null) {
      this.#end(executionId, blocked, latest.check_id);
      return;
    }
    let lines = false;
    try {
      lines = this.#deps.ownChecks?.failedOutput(projectId, view.workstream_id) != null;
    } catch {
      lines = false;
    }
    if (!lines) {
      this.#end(executionId, 'nothing_to_send', latest.check_id);
      return;
    }
    this.#send(
      view,
      { check_id: latest.check_id },
      attempts + 1,
      AUTO_FIX_WORDS,
      causedBy(commandId),
    );
  }

  #blocked(view: ExecutionView, attempts: number): AutoFixEndReason | null {
    if (this.#stopAsked.has(view.execution_id)) return 'person_stopped';
    if (this.#wordsRefused.has(view.execution_id)) return 'person_instructed';
    if (
      view.status === 'waiting_for_human' ||
      view.pending_approvals.length > 0 ||
      view.pending_questions.length > 0
    )
      return 'waiting_on_person';
    switch (view.status) {
      case 'completed':
        break;
      case 'starting':
      case 'running':
      case 'verifying':
        return 'project_busy';
      default:
        return 'turn_failed';
    }
    if (this.#deps.registry.get(view.runtime.runtime_id)?.capabilities.instruct_at_rest !== true)
      return 'runtime_cannot';
    // A task in its own copy waits only for work in that folder, as admission does (ADR 0045).
    const copy = this.#deps.projection.workstream(view.workstream_id)?.copy;
    const busy =
      copy?.state === 'own' && view.directory === copy.directory
        ? folderInUse(this.#deps.projection, copy.directory, view.workstream_id)
        : this.#deps.projection.projectBusy(view.project_id);
    if (busy !== null) return 'project_busy';
    if (attempts >= this.#cap) return 'cap_reached';
    return null;
  }

  /** The host's words, the page's errors and the starter's refusals; null when the check gave an agent nothing to act on. */
  #instruction(projectId: ProjectId, check: ProjectCheck): string | null {
    // One rule with the fix view's `failed_steps`: the steps a try is sent for (fixSteps).
    const steps = fixSteps(check.findings);
    let lines = false;
    try {
      lines = (this.#deps.previews?.failedCheckOutput(projectId) ?? null) !== null;
    } catch {
      lines = false;
    }
    const page = check.findings.page;
    let pagePart = '';
    if (steps.includes('page') && page !== null)
      pagePart =
        page.exceptions.length > 0
          ? `\n\nThe page stopped on these errors:\n${page.exceptions.map((line) => `- ${line}`).join('\n')}`
          : "\n\nThe page showed nothing in the check's 10 seconds.";
    // What the starter's check refuses, in the starter's own words, with the file it was found in.
    const refusals = check.findings.refusals ?? [];
    const refusedPart =
      refusals.length === 0
        ? ''
        : `\n\nThe check refused it:\n${refusals.map((each) => `- ${each.words} (${each.file})`).join('\n')}`;
    // The laptop reading under its floors and the kit's panels that do not fit, in words that may name the kit's parts
    // (AUDIT3 R4).
    const reading = steps.includes('reading') ? laptopFix(check.findings) : null;
    const readingPart = reading === null ? '' : `\n\n${reading}`;
    const codeSteps = steps.includes('typecheck') || steps.includes('build');
    if (!(codeSteps && lines) && pagePart === '' && refusedPart === '' && readingPart === '')
      return null;
    const head = !check.findings.passed
      ? AUTO_FIX_WORDS
      : readingFix(check.findings) === null && fitFix(check.findings) !== null
        ? AUTO_FIX_FIT_WORDS
        : AUTO_FIX_READING_WORDS;
    return head + pagePart + refusedPart + readingPart;
  }

  #send(
    view: ExecutionView,
    check: Pick<ProjectCheck, 'check_id'>,
    attempt: number,
    text: string,
    cause: Cause,
  ): void {
    const commandId = this.#deps.ids.next() as CommandId;
    const scope = {
      project_id: view.project_id,
      workstream_id: view.workstream_id,
      execution_id: view.execution_id,
    };
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.auto_fix.sent',
        scope,
        { check_id: check.check_id, attempt, cap: this.#cap, command_id: commandId },
        this.#now(),
        cause,
      ),
    );
    const command: CommandEnvelope = {
      schema_version: COMMAND_SCHEMA_VERSION,
      command_id: commandId,
      issued_at: this.#now(),
      client: CLIENT,
      command_type: 'execution.send_instruction',
      payload: { execution_id: view.execution_id, text },
    };
    const outcome = this.#deps.commands.submit(command, 'internal', null);
    this.#deps.logger.info(
      { execution_id: view.execution_id, command_id: commandId, attempt, cap: this.#cap },
      'sent a failed check back to its agent',
    );
    // A try refused at once never reached the agent, so it is not counted as one.
    if (outcome.disposition !== 'accepted')
      this.#end(view.execution_id, 'send_failed', null, { notSent: true });
  }

  #interrupt(executionId: ExecutionId): void {
    const commandId = this.#deps.ids.next() as CommandId;
    this.#ownInterrupts.add(commandId);
    this.#deps.commands.submit(
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: commandId,
        issued_at: this.#now(),
        client: CLIENT,
        command_type: 'execution.interrupt',
        payload: { execution_id: executionId },
      },
      'internal',
      null,
    );
  }

  /**
   * Records why the chain ended: a chain under way ends with its own numbers; with none under way,
   * a `checkId` records that none began for that check (0 tries), and without one nothing is recorded.
   */
  #end(
    executionId: ExecutionId,
    reason: AutoFixEndReason,
    checkId: string | null,
    options: { notSent?: boolean } = {},
  ): void {
    const view = this.#deps.projection.execution(executionId);
    if (view === undefined) return;
    const chain = view.auto_fix?.state === 'fixing' ? view.auto_fix : null;
    if (chain === null && checkId === null) return;
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.auto_fix.ended',
        {
          project_id: view.project_id,
          workstream_id: view.workstream_id,
          execution_id: view.execution_id,
        },
        {
          check_id: (checkId ?? chain?.check_id) as never,
          attempts: Math.max(0, (chain?.attempt ?? 0) - (options.notSent === true ? 1 : 0)),
          cap: chain?.cap ?? this.#cap,
          reason,
        },
        this.#now(),
        NO_CAUSE,
      ),
    );
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString() as Timestamp;
  }
}
