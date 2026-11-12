import type { CommandEnvelope, CommandId } from '@halcyonic/contracts';
import { admitCommand } from '@halcyonic/domain';
import type { ControlPlane } from '../core/control-plane.ts';
import { reviewDigest } from '../core/review.ts';
import { type ActionEntry, type ActionScope, type ActionsHere, buildProposed } from './actions.ts';

/**
 * What can be done here as the host can say it today, before CORE's declarations beside `COMMAND_POLICY` and its
 * `offered()` split of admission (ADR 0060): one table of entries whose admission depends on the domain's checks
 * alone, each listed in a scope only when a probe of it, built with placeholder values, passes `admitCommand` there
 * now. So the list never holds what the host would refuse. Entries with host-side refusals beyond admission (a
 * check, a preview, a share, sending, publishing, restoring) wait for `offered()`; CORE moves this table beside the
 * policy with the rest.
 */
export const HOST_ENTRIES: readonly ActionEntry[] = [
  {
    command_type: 'workstream.create',
    title: 'New Task',
    description:
      "Start a new task in this project, with the person's own words as what it is to do.",
    confirm: 'none',
    sayable: true,
    needs: [
      { name: 'title', kind: 'text', label: 'Title', required: true },
      { name: 'objective', kind: 'person_words', label: 'What To Do', required: false },
    ],
  },
  {
    command_type: 'execution.send_instruction',
    title: 'Send',
    description: "Send the person's own words to this task's agent.",
    confirm: 'none',
    // The assistant sends the person's words to its bound task with continue_task (ADR 0030); the entry is for buttons.
    sayable: false,
    needs: [{ name: 'text', kind: 'person_words', label: 'Words', required: true }],
  },
];

/** A probe's placeholder for a need: the least value of its kind, never sent anywhere. */
const PROBE: Record<string, string | number | boolean> = {
  text: 'probe',
  person_words: 'probe',
  yes_no: false,
  count: 0,
};

export function hostActions(plane: ControlPlane): ActionsHere {
  const runtimes = { get: (runtimeId: string) => plane.registry.get(runtimeId) };
  return {
    actions(scope: ActionScope) {
      return HOST_ENTRIES.filter((entry) => {
        if (entry.needs.some((need) => !(need.kind in PROBE))) return false;
        const values = Object.fromEntries(
          entry.needs
            .filter((need) => need.kind !== 'person_words' && need.required)
            .map((need) => [need.name, PROBE[need.kind]]),
        );
        const probe = buildProposed(entry, scope, values, () => [], {
          command_id: '00000000-0000-7000-8000-000000000000' as CommandId,
          issued_at: plane.clock.now().toISOString(),
          person_words: 'probe',
        });
        return probe.ok && admitCommand(probe.command, plane.projection, runtimes).admitted;
      });
    },
    looks: () => [],
    list: () => [],
    review(command: CommandEnvelope) {
      const entry = HOST_ENTRIES.find(
        (candidate) => candidate.command_type === command.command_type,
      );
      if (!entry) return null;
      return {
        what_happens: entry.description,
        digest: reviewDigest(command.command_type, command.payload, {}),
      };
    },
  };
}
