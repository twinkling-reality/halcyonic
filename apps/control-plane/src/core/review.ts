import { createHash } from 'node:crypto';
import type { ProjectLocation, ProjectLocationChoice, RestorePreview } from '@halcyonic/contracts';

/**
 * The digest of a review (ADR 0051): the command type it is for, the command's arguments and the
 * facts the person was shown, as canonical JSON (keys sorted), SHA-256 in hex. The read that shows
 * the review answers it; a command carries it back; the control plane computes it again from what is
 * true when the command would act, and a difference means what the person read has changed.
 */
export function reviewDigest(commandType: string, args: unknown, facts: unknown): string {
  return createHash('sha256')
    .update(canonical({ command_type: commandType, args, facts }))
    .digest('hex');
}

/**
 * What a restore's review covers: the checkpoint, what the preview shows, and every path a restore
 * would change back, remove or return, each set whole, though the preview lists at most the first of
 * each: a change among the unlisted ones makes the review stale too (AUDIT2 S2).
 */
export function restoreReview(
  projectId: string,
  checkpointId: string,
  preview: RestorePreview,
  everyPath: {
    readonly changedBack: readonly string[];
    readonly removed: readonly string[];
    readonly returned: readonly string[];
  },
): string {
  const whole = (paths: readonly string[]) =>
    createHash('sha256')
      .update([...paths].sort().join('\0'))
      .digest('hex');
  return reviewDigest(
    'project.restore',
    { project_id: projectId, checkpoint_id: checkpointId },
    {
      preview,
      paths: {
        changed_back: whole(everyPath.changedBack),
        removed: whole(everyPath.removed),
        returned: whole(everyPath.returned),
      },
    },
  );
}

/** What a rebinding's review covers: the folder the project works in now, and the one chosen. */
export function locationReview(
  projectId: string,
  location: ProjectLocationChoice,
  current: ProjectLocation | null,
): string {
  return reviewDigest('project.set_location', { project_id: projectId, location }, { current });
}

function canonical(value: unknown): string {
  if (value === null || typeof value !== 'object') return JSON.stringify(value ?? null);
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  const entries = Object.entries(value as Record<string, unknown>)
    .filter(([, inner]) => inner !== undefined)
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
  return `{${entries.map(([key, inner]) => `${JSON.stringify(key)}:${canonical(inner)}`).join(',')}}`;
}
