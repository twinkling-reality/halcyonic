import type { EventEnvelope } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { CommandService } from './command-service.ts';
import type { EventPublisher } from './publisher.ts';

export interface InstructionQueueDeps {
  readonly projection: Projection;
  readonly commands: Pick<CommandService, 'deliverQueued' | 'failQueued'>;
  /** Whether the journal is the person's own (`live`); a fixture's journal is never appended to. */
  readonly live: boolean;
}

/** Why a waiting instruction was not sent, in the person's words, by its failure code. */
const NOT_SENT = {
  queued_turn_failed:
    'The turn ended in an error, so your instruction was not sent. Send it again if you still want it.',
  queued_stopped:
    'The turn did not finish (it was stopped, or your computer restarted), so your instruction was not sent.',
  queued_session_lost: "The agent's session could not be kept, so your instruction was not sent.",
  queued_files_restored: "The project's files were put back, so your instruction was not sent.",
} as const;

/**
 * The person's instructions to a runtime that cannot steer, held while a turn runs (ADR 0056). The
 * first is sent after a turn that completed, one per turn, in the order sent; after a turn that did
 * not complete, a Stop, a lost session or a restore, every one waiting fails unsent, with why. The
 * automatic check and the fix loop wait meanwhile, since a waiting instruction is a pending command.
 */
export class InstructionQueue {
  readonly #deps: InstructionQueueDeps;

  constructor(deps: InstructionQueueDeps) {
    this.#deps = deps;
  }

  attach(publisher: EventPublisher): () => void {
    return publisher.subscribe(({ event }) => {
      if (!this.#deps.live) return;
      // After the event's own subscribers and outside the write: the command service records.
      queueMicrotask(() => this.#on(event));
    });
  }

  #on(event: EventEnvelope): void {
    switch (event.event_type) {
      case 'runtime.turn.completed': {
        const first = this.#waiting(event.execution_id)[0];
        if (first !== undefined) this.#deps.commands.deliverQueued(first);
        return;
      }
      case 'runtime.turn.failed':
        this.#failAll(event.execution_id, 'queued_turn_failed');
        return;
      case 'runtime.turn.interrupted':
        this.#failAll(event.execution_id, 'queued_stopped');
        return;
      case 'execution.state_unknown':
        this.#failAll(event.execution_id, 'queued_session_lost');
        return;
      case 'command.accepted': {
        const command = event.payload.command;
        if (command.command_type === 'execution.interrupt')
          this.#failAll(command.payload.execution_id, 'queued_stopped');
        else if (command.command_type === 'project.restore') {
          for (const execution of this.#deps.projection.executions()) {
            if (execution.project_id === command.payload.project_id)
              this.#failAll(execution.execution_id, 'queued_files_restored');
          }
        }
        return;
      }
      default:
        return;
    }
  }

  #waiting(executionId: string): string[] {
    return (this.#deps.projection.execution(executionId)?.queued_instructions ?? []).map(
      (queued) => queued.command_id,
    );
  }

  #failAll(executionId: string, code: keyof typeof NOT_SENT): void {
    for (const commandId of this.#waiting(executionId))
      this.#deps.commands.failQueued(commandId, code, NOT_SENT[code]);
  }
}
