import { isAbsolute, join, relative, sep } from 'node:path';
import {
  type ApprovalPath,
  COMMAND_SCHEMA_VERSION,
  type CommandId,
  type EventEnvelope,
  type HostApprovalReason,
  type Timestamp,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { runsLater } from '@halcyonic/seatbelt';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import type { CommandService } from './command-service.ts';
import { type Cause, controlPlaneDraft, SUMMARY_LIMIT } from './drafts.ts';
import type { EventPublisher } from './publisher.ts';
import { resolvedPath } from './real-path.ts';
import type { Recorder } from './recorder.ts';

const CLIENT = { name: 'halcyonic-control-plane', version: null, device_label: null } as const;

/**
 * Per runtime kind, the tools the host may answer in a confined app: the shell, held by the closed profile whatever
 * its command says (the lead's ruling, 2026-11-13), and the file tools whose places its adapter gives as structure
 * (`ApprovalSubject.paths`). A runtime not named here, or a tool not named for it, always reaches the person. Codex
 * is absent: on a starter's confined project it asks nothing at its own layer (ADR 0041). OpenCode's edits inside its
 * folder ask nothing at its own layer and its reads of a declared original are its session's rules (`external_directory`);
 * what it still asks there is a shell command, whole.
 */
const ANSWERABLE: Readonly<
  Record<string, { readonly shell: string; readonly files: ReadonlySet<string> }>
> = {
  'claude-agent': {
    shell: 'Bash',
    files: new Set(['Write', 'Edit', 'NotebookEdit', 'Read', 'Glob', 'Grep']),
  },
  opencode: { shell: 'shell', files: new Set() },
};

/** What a confined app's folder reads besides itself: a starter's shared files and a VR version's original. */
export interface ConfinedReads {
  readonly readable: readonly string[];
  readonly originals?: readonly string[];
}

export interface HostAnswersDeps {
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly publisher: EventPublisher;
  readonly commands: Pick<CommandService, 'submit'>;
  /** What the folder reads besides itself when it is a confined app's (a starter's project), else null. */
  readonly confinedReads: (directory: string) => ConfinedReads | null;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  /** Whether the journal is the person's own (`live`); a fixture's journal is never appended to. */
  readonly live: boolean;
}

/** The host's answer to one request, or null when it reaches the person. */
/**
 * Whether the host answers a confined app's own work for a runtime of this kind: the same table `judgeRequest` reads,
 * so a start says no more about what its run will ask than the host will do (the recap is the review, AUDIT2's line
 * with the no-ask unit). Codex is not in it: a confined Codex run asks as before.
 */
export function answersOwnWork(runtimeKind: string): boolean {
  return Object.hasOwn(ANSWERABLE, runtimeKind);
}

export type HostJudgement = {
  readonly because: HostApprovalReason;
  readonly paths: ApprovalPath[];
};

/**
 * The host's own answer to a request in a confined app (the lead's ruling, 2026-11-13; AUDIT3's read of the shape):
 * work inside its own copy and reads of a declared original do not ask. Approved without a card only when the
 * execution's start recorded it confined, its folder is a starter's project, the request is complete, and either it
 * is a shell command (the closed profile holds it) or it is a file tool whose every place, resolved through links,
 * is in the task's folder (and, for a write, not something that runs later outside the sandbox) or is read in a
 * declared original or the starter's shared files. Anything else reaches the person as before. It reads the file
 * system only to resolve links.
 */
export function judgeRequest(
  request: {
    readonly runtimeKind: string;
    readonly toolName: string;
    readonly paths: readonly ApprovalPath[] | undefined;
    readonly complete: boolean;
  },
  directory: string,
  reads: ConfinedReads,
): HostJudgement | null {
  if (!request.complete) return null;
  const tools = Object.hasOwn(ANSWERABLE, request.runtimeKind)
    ? ANSWERABLE[request.runtimeKind]
    : undefined;
  if (tools === undefined) return null;
  if (request.toolName === tools.shell) return { because: 'confined_command', paths: [] };
  if (!tools.files.has(request.toolName)) return null;
  const named = request.paths;
  if (named === undefined || named.length === 0) return null;
  const copy = resolvedPath(directory);
  const readFolders = [...reads.readable, ...(reads.originals ?? [])].map(resolvedPath);
  const judged: ApprovalPath[] = [];
  let outsideCopy = false;
  for (const each of named) {
    const path = resolvedPath(isAbsolute(each.path) ? each.path : join(directory, each.path));
    judged.push({ path, access: each.access });
    if (inside(path, copy)) {
      if (each.access === 'write' && runsLater(relative(copy, path).split(sep).join('/')))
        return null;
      continue;
    }
    const readIn = readFolders.find((folder) => inside(path, folder));
    if (each.access === 'read' && readIn !== undefined) {
      // An original's version control is the kernel's to refuse; the host never answers it (AUDIT3 S2).
      if (relative(readIn, path).toLowerCase().split(sep).includes('.git')) return null;
      outsideCopy = true;
      continue;
    }
    return null;
  }
  return { because: outsideCopy ? 'reads_original' : 'inside_copy', paths: judged };
}

/** Whether `path` is `folder` or inside it, both resolved. */
function inside(path: string, folder: string): boolean {
  return path === folder || path.startsWith(folder.endsWith(sep) ? folder : `${folder}${sep}`);
}

/**
 * Follows the journal and answers, as the host, the requests `judgeRequest` approves: records
 * `execution.approval.answered_by_host` first, then approves through the same command a press sends. The runtime's
 * own `runtime.approval.resolved` stays its word that it applied it.
 */
export class HostAnswers {
  readonly #deps: HostAnswersDeps;
  #unsubscribe: (() => void) | null = null;
  #closed = false;

  constructor(deps: HostAnswersDeps) {
    this.#deps = deps;
  }

  attach(): void {
    if (!this.#deps.live) return;
    this.#unsubscribe = this.#deps.publisher.subscribe(({ event }) => this.#on(event));
  }

  close(): void {
    this.#closed = true;
    this.#unsubscribe?.();
    this.#unsubscribe = null;
  }

  /**
   * Whether the host will answer this request itself (`#judge`): a confined app's own work. HostDenials asks first, so
   * the two never both act on one request (AUDIT3 M1, 2026-11-13).
   */
  wouldAnswer(event: EventEnvelope): boolean {
    return !this.#closed && this.#deps.live && this.#judgeOnce(event) !== null;
  }

  /** Each request judged once and the result kept, so the denial's question and the answer act on one judgement. */
  readonly #judged = new Map<string, HostJudgement | null>();

  #judgeOnce(event: EventEnvelope): HostJudgement | null {
    const known = this.#judged.get(event.event_id);
    if (known !== undefined) return known;
    const judgement = this.#judge(event);
    this.#judged.set(event.event_id, judgement);
    // Both listeners ask within the same publish; nothing older is needed.
    if (this.#judged.size > 64) {
      const oldest = this.#judged.keys().next().value;
      if (oldest !== undefined) this.#judged.delete(oldest);
    }
    return judgement;
  }

  /**
   * The host's judgement of a request, or null. A request too long for a person to approve is judged as its agent app
   * reported it (`too_long.reported_complete`): in a confined copy no person approves, the profile holds the step.
   */
  #judge(event: EventEnvelope): HostJudgement | null {
    if (event.event_type !== 'runtime.approval.requested') return null;
    const executionId = event.execution_id;
    if (executionId === null) return null;
    const { projection } = this.#deps;
    if (!projection.executionConfined(executionId)) return null;
    const view = projection.execution(executionId);
    if (view === undefined || view.directory === null) return null;
    const reads = this.#deps.confinedReads(view.directory);
    if (reads === null) return null;
    const { subject, complete, too_long: tooLong } = event.payload;
    return judgeRequest(
      {
        runtimeKind: view.runtime.kind,
        toolName: subject.tool_name,
        paths: subject.paths,
        complete: complete || tooLong?.reported_complete === true,
      },
      view.directory,
      reads,
    );
  }

  #on(event: EventEnvelope): void {
    if (this.#closed || event.event_type !== 'runtime.approval.requested') return;
    const judgement = this.#judgeOnce(event);
    if (judgement === null) return;
    const approvalId = event.payload.approval_id;
    // After the request is projected, so the answer finds it pending.
    queueMicrotask(() => {
      if (this.#closed) return;
      try {
        this.#answer(event, approvalId, judgement);
      } catch {
        // Not the error itself: it can hold the paths judged. The request stays a card for the person.
        this.#deps.logger.error({ execution_id: event.execution_id }, 'host answer failed');
      }
    });
  }

  #answer(event: EventEnvelope, approvalId: string, judgement: HostJudgement): void {
    const executionId = event.execution_id;
    if (executionId === null || event.event_type !== 'runtime.approval.requested') return;
    const tooLong = event.payload.too_long;
    const pending = this.#deps.projection
      .execution(executionId)
      ?.pending_approvals.some((each) => each.approval_id === approvalId);
    if (pending !== true) return;
    const scope = {
      project_id: event.project_id,
      workstream_id: event.workstream_id,
      execution_id: executionId,
    };
    const cause = {
      correlation_id: event.correlation_id,
      causation_id: event.event_id,
    } as unknown as Cause;
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.approval.answered_by_host',
        scope as never,
        {
          approval_id: approvalId,
          because: judgement.because,
          paths: judgement.paths,
          // Longer than the record keeps: its first part is recorded, the whole is the project's own copy.
          ...(tooLong !== undefined &&
            tooLong.length > SUMMARY_LIMIT && { cut: { length: tooLong.length } }),
        },
        this.#now(),
        cause,
      ),
    );
    const commandId = this.#deps.ids.next() as CommandId;
    const outcome = this.#deps.commands.submit(
      {
        schema_version: COMMAND_SCHEMA_VERSION,
        command_id: commandId,
        issued_at: this.#now(),
        client: CLIENT,
        command_type: 'execution.respond_to_approval',
        payload: {
          execution_id: executionId,
          approval_id: approvalId,
          decision: 'approve',
          message: null,
        },
      },
      'internal',
      null,
    );
    this.#deps.logger.info(
      {
        execution_id: executionId,
        command_id: commandId,
        because: judgement.because,
        disposition: outcome.disposition,
      },
      'answered a request in a confined app',
    );
    // Not taken: the request goes back to the person as a card, never left hidden and waiting (AUDIT3 S3).
    if (outcome.disposition !== 'accepted')
      this.#deps.recorder.record(
        controlPlaneDraft(
          'execution.approval.host_answer_withdrawn',
          scope as never,
          { approval_id: approvalId },
          this.#now(),
          cause,
        ),
      );
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString() as Timestamp;
  }
}
