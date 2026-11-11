import type { CommandOf, Principal, ReceivedVia } from '@halcyonic/contracts';
import { nameProblem, type SecretStore, valueProblem } from './store.ts';

/**
 * Sets a project secret as its caller (ADR 0057): the Mac's setup page, or a paired device or a
 * client on this Mac through `POST /api/projects/:id/secrets`. The value is staged in the store under
 * the command's id, then the name-only `project.secret.set` is submitted for the caller's principal;
 * its dispatch commits the value, and anything still staged after the submit (a refusal, a failure, a
 * retried id) is dropped. The value is never in what is submitted, recorded or returned.
 */
export function submitSecretSet<Outcome>(
  deps: {
    readonly store: SecretStore;
    readonly submit: (
      command: CommandOf<'project.secret.set'>,
      via: ReceivedVia,
      principal: Principal | null,
    ) => Outcome;
  },
  command: CommandOf<'project.secret.set'>,
  value: unknown,
  via: ReceivedVia,
  principal: Principal | null,
):
  | { readonly ok: false; readonly problem: string }
  | { readonly ok: true; readonly outcome: Outcome } {
  const problem = nameProblem(command.payload.name) ?? valueProblem(value);
  if (problem !== null) return { ok: false, problem };
  try {
    deps.store.stage(
      command.command_id,
      command.payload.project_id,
      command.payload.name,
      value as string,
    );
  } catch {
    return { ok: false, problem: "The secret couldn't be kept on this computer. Try again." };
  }
  try {
    return { ok: true, outcome: deps.submit(command, via, principal) };
  } finally {
    // Committed by an accepted set; anything still staged was refused, failed or a retry.
    deps.store.discard(command.command_id);
  }
}
