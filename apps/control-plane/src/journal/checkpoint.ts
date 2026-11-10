import { createHash } from 'node:crypto';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deserialize, serialize } from 'node:v8';
import { PROJECTION_STATE_FORMAT, type Projection, type ProjectionState } from '@halcyonic/domain';
import type { EventJournal } from './journal.ts';
import { JOURNAL_SCHEMA_VERSION } from './sqlite-journal.ts';

/**
 * A checkpoint of the projection beside the journal (ADR 0050): the projection's whole state at a
 * position, so a start folds only the events after it. Only a cache of the journal, never trusted
 * over it: a start takes a checkpoint only when it was written for this journal, by this code, at a
 * position the journal still holds with the same event there, and its bytes are unchanged; anything
 * else, and the start folds the whole journal as it would with none.
 */

/** Events appended between two checkpoints the control plane writes while it runs. */
export const CHECKPOINT_EVERY = 50_000;

let identity: string | undefined;

/**
 * What a checkpoint must have been written by to be taken: every source file of the code that
 * reads and folds events (the domain and contracts packages), the journal's schema version, the
 * state's format and the V8 that serializes it. Any change to any of them, an upgrade included,
 * means one full fold, and a new checkpoint after it.
 */
export function checkpointCodeIdentity(): string {
  if (identity !== undefined) return identity;
  const hash = createHash('sha256');
  hash.update(
    `halcyonic projection checkpoint\0format ${PROJECTION_STATE_FORMAT}\0journal schema ${JOURNAL_SCHEMA_VERSION}\0v8 ${process.versions.v8}\0`,
  );
  for (const name of ['@halcyonic/domain', '@halcyonic/contracts']) {
    const root = dirname(fileURLToPath(import.meta.resolve(name)));
    for (const file of sourceFiles(root)) {
      hash.update(`${name}/${relative(root, file)}\0`);
      hash.update(readFileSync(file));
      hash.update('\0');
    }
  }
  identity = hash.digest('hex');
  return identity;
}

function sourceFiles(directory: string): string[] {
  const files: string[] = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) files.push(...sourceFiles(path));
    else if (entry.name.endsWith('.ts') && !entry.name.endsWith('.test.ts')) files.push(path);
  }
  return files.sort();
}

export type CheckpointRestore =
  | { readonly restored: true; readonly position: number }
  | { readonly restored: false; readonly reason: string | null };

/**
 * Restores `projection`, which has applied nothing, from the journal's checkpoint when it can be
 * trusted, and says why not otherwise (null when there is none). On any refusal the projection is
 * left as it was: nothing is taken until every check has passed.
 */
export function restoreCheckpoint(
  journal: EventJournal,
  projection: Projection,
): CheckpointRestore {
  let checkpoint: ReturnType<EventJournal['readCheckpoint']>;
  try {
    checkpoint = journal.readCheckpoint();
  } catch (error) {
    return refused(`the checkpoint could not be read: ${message(error)}`);
  }
  if (checkpoint === null) return { restored: false, reason: null };
  if (checkpoint.journalId !== journal.info.journal_id) {
    return refused('it was written for another journal');
  }
  if (checkpoint.codeIdentity !== checkpointCodeIdentity()) {
    return refused('it was written by other code');
  }
  if (!Number.isSafeInteger(checkpoint.position) || checkpoint.position < 1) {
    return refused('its position is not one a checkpoint is written at');
  }
  if (checkpoint.position > journal.head()) {
    return refused(`it is at position ${checkpoint.position}, past the journal's last event`);
  }
  let atPosition: string | undefined;
  try {
    const [stored] = journal.read({ after: checkpoint.position - 1, limit: 1 });
    atPosition = stored?.position === checkpoint.position ? stored.event.event_id : undefined;
  } catch (error) {
    return refused(`the event at its position could not be read: ${message(error)}`);
  }
  if (atPosition !== checkpoint.eventId) {
    return refused(`the journal holds another event at its position ${checkpoint.position}`);
  }
  const sha256 = createHash('sha256').update(checkpoint.state).digest('hex');
  if (sha256 !== checkpoint.stateSha256) return refused('its state is damaged');
  let state: ProjectionState;
  try {
    state = deserialize(checkpoint.state) as ProjectionState;
  } catch (error) {
    return refused(`its state could not be read: ${message(error)}`);
  }
  if (state?.position !== checkpoint.position) {
    return refused('its state is not at its position');
  }
  try {
    projection.restoreState(state);
  } catch (error) {
    return refused(`its state could not be taken: ${message(error)}`);
  }
  return { restored: true, position: checkpoint.position };
}

/**
 * Writes the projection's state as the journal's checkpoint, at the projection's position. Never
 * throws: a checkpoint is only a cache, and the journal holds everything without it. Resolves the
 * position written, or null when nothing was.
 */
export function writeCheckpoint(
  journal: EventJournal,
  projection: Projection,
  now: Date,
): { readonly position: number } | { readonly error: unknown } | null {
  const position = projection.position;
  if (position < 1) return null;
  try {
    const [stored] = journal.read({ after: position - 1, limit: 1 });
    if (stored?.position !== position) return null;
    journal.writeCheckpoint({
      position,
      eventId: stored.event.event_id,
      codeIdentity: checkpointCodeIdentity(),
      // The exported state is live: serialized here, in the same step, before anything else is
      // applied, so the bytes are the fold at `position` exactly.
      state: serialize(projection.exportState()),
      writtenAt: now.toISOString(),
    });
    return { position };
  } catch (error) {
    return { error };
  }
}

function refused(reason: string): CheckpointRestore {
  return { restored: false, reason };
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
