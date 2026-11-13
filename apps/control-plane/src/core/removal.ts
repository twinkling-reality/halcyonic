import type { ActionReview, ActionReviewItem, ActionReviewWarning } from '@halcyonic/contracts';
import { type Projection, removalRefusal } from '@halcyonic/domain';
import { reviewDigest } from './review.ts';

/**
 * Taking a project off the list (ADR 0063): its review, read from the projection alone, so neither the
 * review nor the command touches a file. The card names the project by its name and its folder, so a name
 * misheard by the assistant is caught before the press (the lead, 2026-11-13), and its digest covers every
 * fact on it: a change to any of them makes a press on the old card stale.
 */

export type RemovalReview =
  | { readonly ok: true; readonly review: Omit<ActionReview, 'command_type' | 'said'> }
  | { readonly ok: false; readonly code: string; readonly message: string };

/** What the card shows, each as a count or a value, and nothing the digest leaves out. */
export interface RemovalFacts {
  readonly name: string;
  readonly folder: string | null;
  readonly tasks: number;
  /** Tasks whose copy holds changes not yet added to the folder, as last read. */
  readonly unadded: number;
  /** Tasks whose copy was never read, so it may hold changes. */
  readonly unread: number;
  readonly secrets: number;
  readonly published: string | null;
  /** The published game's address, or null when GitHub gave none and `published` names its repository. */
  readonly publishedAt: string | null;
}

/** The facts of taking `projectId` off the list now, or null when it is not on the list. */
export function removalFacts(projection: Projection, projectId: string): RemovalFacts | null {
  if (!projection.listed(projectId)) return null;
  const project = projection.project(projectId);
  if (project === undefined) return null;
  let unadded = 0;
  let unread = 0;
  const tasks = projection.workstreams(projectId);
  for (const task of tasks) {
    const copy = task.copy;
    if (copy?.state !== 'own') continue;
    if (copy.to_add === null) unread += 1;
    else if (copy.to_add.total > 0) unadded += 1;
  }
  return {
    name: project.name,
    folder: project.location?.path ?? null,
    tasks: tasks.length,
    unadded,
    unread,
    secrets: project.secrets?.length ?? 0,
    published:
      project.published === undefined
        ? null
        : (project.published.address ?? project.published.repository),
    publishedAt: project.published?.address ?? null,
  };
}

/** The digest a removal's press carries back, computed again from what is true at the press. */
export function removalDigest(projectId: string, facts: RemovalFacts): string {
  return reviewDigest('project.remove_from_list', { project_id: projectId }, facts);
}

const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

const clip = (text: string, most: number) => {
  const chars = Array.from(text);
  return chars.length <= most ? text : `${chars.slice(0, most - 1).join('')}…`;
};

/** The one review of taking `projectId` off the list (ADR 0060's card), or why it cannot be taken off now. */
export function removalReview(projection: Projection, projectId: string): RemovalReview {
  const facts = removalFacts(projection, projectId);
  const project = projection.project(projectId);
  if (facts === null || project === undefined)
    return { ok: false, code: 'project_not_found', message: 'This project is not on the list.' };
  const refusal = removalRefusal(projection, project);
  if (refusal !== null) return { ok: false, code: 'invalid_state', message: refusal };
  const items: ActionReviewItem[] = [];
  if (facts.tasks > 0)
    items.push({
      kind: 'task',
      name: count(facts.tasks, 'task', 'tasks'),
      note: 'They come back with the project',
      theirs: false,
    });
  if (facts.unadded > 0)
    items.push({
      kind: 'task',
      name: `${count(facts.unadded, 'task has', 'tasks have')} changes not added to the folder`,
      note: 'Kept in their own copies',
      theirs: false,
    });
  if (facts.unread > 0)
    items.push({
      kind: 'task',
      name: `${count(facts.unread, 'task', 'tasks')} may have changes not added to the folder`,
      note: 'Kept in their own copies',
      theirs: false,
    });
  if (facts.secrets > 0)
    items.push({
      kind: 'words',
      name: count(facts.secrets, 'secret', 'secrets'),
      note: 'Stay on this computer, used by nothing until it comes back',
      theirs: false,
    });
  const warnings: ActionReviewWarning[] = [];
  if (facts.published !== null)
    warnings.push({
      code: 'published',
      line: clip(
        facts.publishedAt === null
          ? `The game stays published from ${facts.published}. To take it down later, add its folder again.`
          : `The published game stays at ${facts.published}. To take it down later, add its folder again.`,
        200,
      ),
    });
  warnings.push(
    facts.folder === null
      ? { code: 'cannot_undo', line: 'It has no folder, so it cannot be brought back' }
      : { code: 'way_back', line: 'Add its folder again to bring it back' },
  );
  return {
    ok: true,
    review: {
      digest: removalDigest(projectId, facts),
      confirm_words: 'Remove From List',
      // With no folder the press is for good, so the headline says so, not only the warning (AUDIT2 S6).
      what_happens: clip(
        facts.folder === null
          ? `${clip(facts.name, 120)} leaves the list for good. It has no folder, so it cannot come back`
          : `${clip(facts.name, 120)} leaves the list. Its folder and everything in it stay as they are`,
        200,
      ),
      facts:
        facts.folder === null
          ? []
          : [{ kind: 'folder', value: clip(facts.folder, 400), theirs: true }],
      items,
      items_total: items.length,
      warnings,
    },
  };
}
