import { constants } from 'node:fs';
import { open, realpath } from 'node:fs/promises';
import { join } from 'node:path';
import {
  type ProjectId,
  type ProjectWorldView,
  parseWorldDescription,
  type Timestamp,
  WORLD_FILE,
  WORLD_FILE_BYTES,
  WORLD_STARTER_ID,
  type WorldDescription,
  type WorldObserved,
  type WorldRefused,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import type { Logger } from '../logger.ts';
import { NO_LINK } from './file-observer.ts';

/**
 * The Mac's own reading of a world project's description (ADR 0049). The file is data: it is read
 * whole or not at all, bounded, through no link, parsed as JSON and validated against the contract,
 * and only what the contract names is journaled, in the contract's own order. Nothing in it is run,
 * opened or followed, and no word from it enters a message or a log line.
 */

const MESSAGES: Readonly<Record<WorldRefused['code'], string>> = {
  missing: `The world's folder has no ${WORLD_FILE}.`,
  not_a_file: `${WORLD_FILE} is not a regular file, so the Mac did not read it.`,
  too_large: `${WORLD_FILE} is larger than the ${WORLD_FILE_BYTES} bytes the Mac reads.`,
  not_text: `${WORLD_FILE} is not UTF-8 text.`,
  not_json: `${WORLD_FILE} is not JSON.`,
  not_a_description: `${WORLD_FILE} is not a world's description: a member is missing, unknown or outside its range.`,
};

/** A place in the description, by its own member names and list positions only. */
const WHERE = /^(\/[a-z_]{1,32}|\/[0-9]{1,4}){1,8}$/;

function refused(code: WorldRefused['code'], where: string | null = null): WorldObserved {
  return { description: null, refused: { code, where, message: MESSAGES[code] } };
}

/** The file's bytes, or why they were not read. At most `WORLD_FILE_BYTES` are ever read. */
async function readBytes(folder: string): Promise<Buffer | WorldRefused['code']> {
  let root: string;
  try {
    root = await realpath(folder);
  } catch {
    return 'missing';
  }
  let handle: Awaited<ReturnType<typeof open>>;
  try {
    // A link is refused, at the name and, on macOS, anywhere above it; a FIFO is never waited on.
    handle = await open(
      join(root, WORLD_FILE),
      constants.O_RDONLY | constants.O_NONBLOCK | NO_LINK,
    );
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    return code === 'ENOENT' || code === 'ENOTDIR' ? 'missing' : 'not_a_file';
  }
  try {
    const stats = await handle.stat();
    if (!stats.isFile()) return 'not_a_file';
    if (stats.size > WORLD_FILE_BYTES) return 'too_large';
    // One byte more than allowed, so a file that grew after the stat is still refused.
    const buffer = Buffer.alloc(WORLD_FILE_BYTES + 1);
    let length = 0;
    while (length < buffer.length) {
      const { bytesRead } = await handle.read(buffer, length, buffer.length - length, length);
      if (bytesRead === 0) break;
      length += bytesRead;
    }
    return length > WORLD_FILE_BYTES ? 'too_large' : buffer.subarray(0, length);
  } catch {
    return 'not_a_file';
  } finally {
    await handle.close();
  }
}

/** The description with only the contract's members, in the contract's order. */
function inOrder(d: WorldDescription): WorldDescription {
  return {
    format: 1,
    sky: { zenith: d.sky.zenith, band: d.sky.band, horizon: d.sky.horizon },
    disc:
      d.disc === null
        ? null
        : {
            bearing: d.disc.bearing,
            height: d.disc.height,
            size: d.disc.size,
            colour: d.disc.colour,
            glow: d.disc.glow,
          },
    stars: d.stars,
    clouds: { amount: d.clouds.amount, colour: d.clouds.colour },
    mist: { amount: d.mist.amount, colour: d.mist.colour },
    water: {
      near: d.water.near,
      far: d.water.far,
      ripple: d.water.ripple,
      shine: d.water.shine,
    },
    ground: { colour: d.ground.colour, radius: d.ground.radius },
    wind: { bearing: d.wind.bearing, strength: d.wind.strength },
    places: d.places.map((place) => ({
      project: place.project,
      bearing: place.bearing,
      distance: place.distance,
    })),
  };
}

/** Reads and validates the description in `folder`: the description, or why the file is not one. */
export async function readWorld(folder: string): Promise<WorldObserved> {
  const bytes = await readBytes(folder);
  if (typeof bytes === 'string') return refused(bytes);
  let text: string;
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return refused('not_text');
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    // A syntax error, or nesting too deep to parse: either way not a description.
    return refused('not_json');
  }
  const validated = parseWorldDescription(parsed);
  if (!validated.ok) {
    const path = validated.issues[0]?.path ?? '';
    return refused('not_a_description', WHERE.test(path) && path.length <= 120 ? path : null);
  }
  return { description: inOrder(validated.value), refused: null };
}

/** Whether a reading says what the project's world already says, so there is nothing new to record. */
function same(last: ProjectWorldView | undefined, now: WorldObserved): boolean {
  if (last === undefined) return false;
  if (now.refused !== null)
    return (
      last.refused !== null &&
      last.refused.code === now.refused.code &&
      last.refused.where === now.refused.where
    );
  return (
    last.refused === null && JSON.stringify(last.description) === JSON.stringify(now.description)
  );
}

export interface WorldReaderAttachment {
  readonly publisher: EventPublisher;
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly clock: Clock;
  readonly logger: Logger;
}

/**
 * Journals a world project's description when the Mac has read it: once the world starter's files
 * are in the project's folder, and after each turn the Mac observed there, only when the reading
 * differs from the last one recorded. A project is a world only by its starter, and only its own
 * folder, as the host resolved it, is read. Readings of one project never overlap.
 */
export function attachWorldReader(attachment: WorldReaderAttachment): () => void {
  const queues = new Map<ProjectId, Promise<void>>();
  return attachment.publisher.subscribe(({ event }) => {
    if (
      event.event_type !== 'project.starter_applied' &&
      event.event_type !== 'execution.files.observed'
    )
      return;
    const projectId = event.project_id;
    const project = attachment.projection.project(projectId);
    if (project?.starter !== WORLD_STARTER_ID || project.location === null) return;
    const folder = project.location.path;
    // A turn in another folder than the project's own says nothing about its world.
    if (
      event.event_type === 'execution.files.observed' &&
      attachment.projection.execution(event.execution_id)?.directory !== folder
    )
      return;
    const previous = queues.get(projectId) ?? Promise.resolve();
    const next = previous
      .then(() => readWorld(folder))
      .then((observed) => {
        if (same(attachment.projection.world(projectId), observed)) return;
        attachment.recorder.record(
          controlPlaneDraft(
            'project.world.observed',
            { project_id: projectId, workstream_id: null, execution_id: null },
            observed,
            attachment.clock.now().toISOString() as Timestamp,
            NO_CAUSE,
          ),
        );
      })
      .catch((failure: unknown) => {
        // Only which kind of failure: its own words could carry the file's.
        const kind = failure instanceof Error ? failure.name : typeof failure;
        attachment.logger.error({ project_id: projectId, kind }, 'world not recorded');
      });
    queues.set(projectId, next);
    void next.then(() => {
      if (queues.get(projectId) === next) queues.delete(projectId);
    });
  });
}
