import type { HereAction } from '@halcyonic/contracts';

/** One command entry of what can be done here, as the host lists it (ADR 0060, `CommandService.here`). */
export type CommandEntry = Extract<HereAction, { kind: 'command' }>;

/**
 * What the assistant may propose: the entries the host marks sayable, never one that needs a secret (its value is
 * typed in a field, never passed through a model), and never one of the coordinator's own commands or an answer to a
 * runtime's approval or question, whatever a list says (ADR 0060, R6). The host's list says so already; the request
 * loop says it again, so a list that ever said otherwise could not make the model propose them.
 */
export function sayable(entries: readonly CommandEntry[]): CommandEntry[] {
  return entries.filter(
    (entry) =>
      entry.sayable &&
      !entry.needs.some((need) => need.kind === 'secret') &&
      !entry.command_type.startsWith('workspace.request.') &&
      entry.command_type !== 'execution.respond_to_approval' &&
      entry.command_type !== 'execution.answer_question',
  );
}
