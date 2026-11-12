import { createHash, randomBytes } from 'node:crypto';
import { constants, readdirSync, readFileSync } from 'node:fs';
import { open, realpath, rename, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  type ProjectId,
  type ProjectWorldView,
  parseWorldDescription,
  parseWorldDescriptionFormat1,
  parseWorldScene,
  type Timestamp,
  WORLD_FILE,
  WORLD_FILE_BYTES,
  WORLD_STARTER_ID,
  type WorldDescription,
  type WorldDescriptionFormat1,
  type WorldLight,
  type WorldObserved,
  type WorldRefused,
  type WorldScene,
} from '@halcyonic/contracts';
import type { Projection, StarterCatalog } from '@halcyonic/domain';
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

/** A file's bytes at the top of `folder`, or why they were not read. At most `WORLD_FILE_BYTES` are ever read. */
async function readBytes(
  folder: string,
  name = WORLD_FILE,
): Promise<Buffer | WorldRefused['code']> {
  let root: string;
  try {
    root = await realpath(folder);
  } catch {
    return 'missing';
  }
  let handle: Awaited<ReturnType<typeof open>>;
  try {
    // A link is refused, at the name and, on macOS, anywhere above it; a FIFO is never waited on.
    handle = await open(join(root, name), constants.O_RDONLY | constants.O_NONBLOCK | NO_LINK);
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
  const light = (l: WorldLight): WorldLight => ({
    light: l.light,
    brightness: l.brightness,
    colour: l.colour,
  });
  return {
    format: 2,
    scene: d.scene,
    night: d.night,
    lights: d.lights.map(light),
    haze: { thickness: d.haze.thickness, colour: d.haze.colour },
    places: d.places.map((place) => ({ project: place.project, at: place.at })),
  };
}

/** What the Mac read: a reading to record, or a world made before format 2 to bring forward first. */
type Reading =
  | { readonly kind: 'read'; readonly observed: WorldObserved }
  | { readonly kind: 'format1'; readonly description: WorldDescriptionFormat1 };

/** Reads and validates the description in `folder` against `scenes`: the description, or why the file is not one. */
export async function readWorld(
  folder: string,
  scenes: ReadonlyMap<string, WorldScene>,
  file = WORLD_FILE,
): Promise<WorldObserved> {
  const reading = await read(folder, scenes, file);
  return reading.kind === 'read' ? reading.observed : refused('not_a_description', '/format');
}

async function read(
  folder: string,
  scenes: ReadonlyMap<string, WorldScene>,
  file: string,
): Promise<Reading> {
  const done = (observed: WorldObserved): Reading => ({ kind: 'read', observed });
  const bytes = await readBytes(folder, file);
  if (typeof bytes === 'string') return done(refused(bytes));
  let text: string;
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return done(refused('not_text'));
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    // A syntax error, or nesting too deep to parse: either way not a description.
    return done(refused('not_json'));
  }
  const first = parseWorldDescriptionFormat1(parsed);
  if (first.ok) return { kind: 'format1', description: first.value };
  const validated = parseWorldDescription(parsed, scenes);
  if (!validated.ok) {
    const path = validated.issues[0]?.path ?? '';
    return done(refused('not_a_description', WHERE.test(path) && path.length <= 120 ? path : null));
  }
  return done({ description: inOrder(validated.value), refused: null });
}

/** What the world starter gives the reader: its scenes, its starting description and its guide. */
export interface WorldStarter {
  readonly scenes: ReadonlyMap<string, WorldScene>;
  /** The starter's own description, whose scene a world made before format 2 is brought into. */
  readonly starting: WorldDescription;
  /** The starter's guide for a world's agent, as it writes it now. */
  readonly guide: Buffer;
}

/** The world starter's guide as format 1 wrote it, by its SHA-256: only that guide is replaced. */
const FORMAT1_GUIDE = 'c0358eefc345217bf434e33561ac8b40fecb4bb24c606fd95a9f524e713e180b';

/**
 * Reads the tracked world starter: each scene's manifest under `scenes/` (one file a scene, named by
 * it), the template's description, validated against them, and its guide. A starter that does not
 * hold together is an error at start, never a world shown wrong.
 */
export function loadWorldStarter(
  starterDir = fileURLToPath(new URL(`../../../../starters/${WORLD_STARTER_ID}`, import.meta.url)),
): WorldStarter {
  const scenes = new Map<string, WorldScene>();
  for (const name of readdirSync(join(starterDir, 'scenes')).sort()) {
    if (!name.endsWith('.json')) continue;
    const scene = parseWorldScene(
      JSON.parse(readFileSync(join(starterDir, 'scenes', name), 'utf8')),
    );
    if (!scene.ok || `${scene.value.scene}.json` !== name)
      throw new Error(
        `the world starter's scene ${name} is not a scene's manifest named by its scene`,
      );
    scenes.set(scene.value.scene, scene.value);
  }
  const starting = parseWorldDescription(
    JSON.parse(readFileSync(join(starterDir, 'template', WORLD_FILE), 'utf8')),
    scenes,
  );
  if (!starting.ok) throw new Error("the world starter's own description is not one");
  return {
    scenes,
    starting: starting.value,
    guide: readFileSync(join(starterDir, 'template', 'AGENTS.md')),
  };
}

/** A colour's relative luminance, in linear light. */
function luminance(hex: string): number {
  const channel = (at: number) => {
    const c = Number.parseInt(hex.slice(at, at + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

function smooth(edge0: number, edge1: number, x: number): number {
  const t = Math.max(0, Math.min(1, (x - edge0) / (edge1 - edge0)));
  return t * t * (3 - 2 * t);
}

/**
 * A world made before format 2, in the starter's scene, keeping what maps: how far night had come (read
 * from its sky's brightness and its sun or moon below the horizon, as the headset read it then) and its
 * places in order, given the scene's places in the manifest's order. Everything else is the starter's.
 */
export function broughtForward(
  old: WorldDescriptionFormat1,
  starter: WorldStarter,
): WorldDescription {
  const sky = 0.75 * luminance(old.sky.zenith) + 0.25 * luminance(old.sky.horizon);
  let night = 1 - smooth(0.006, 0.05, sky);
  if (old.disc !== null && old.disc.height < 0)
    night = Math.max(night, smooth(0, -10, old.disc.height));
  const scene = starter.scenes.get(starter.starting.scene);
  const places = scene?.places ?? [];
  return {
    ...starter.starting,
    night: Math.round(night * 100) / 100,
    places: old.places
      .slice(0, places.length)
      .map((place, index) => ({ project: place.project, at: places[index]?.place as string })),
  };
}

/**
 * Replaces a file at the top of `folder` whole: written beside it under a new name, through no link,
 * then renamed over it, so a reader sees the old file or the new one, never half of either.
 */
async function replaceFile(folder: string, name: string, content: Buffer): Promise<void> {
  const root = await realpath(folder);
  const temporary = join(root, `.${name}.${randomBytes(6).toString('hex')}.new`);
  const handle = await open(
    temporary,
    constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL | NO_LINK,
    0o644,
  );
  try {
    await handle.writeFile(content);
    await handle.sync();
  } catch (failure) {
    await handle.close();
    await rm(temporary, { force: true });
    throw failure;
  }
  await handle.close();
  await rename(temporary, join(root, name));
}

/**
 * Brings a world made before format 2 forward in its own folder, once: its new description, and its
 * guide when it is the format 1 guide as the starter wrote it. Which guide was there is all it reports.
 */
async function bringForward(
  folder: string,
  old: WorldDescriptionFormat1,
  starter: WorldStarter,
  file: string,
): Promise<{ guide: 'replaced' | 'kept'; kept: string }> {
  // The person's own file first, whole and unchanged, beside the new one (AUDIT F1): named by its hash, so
  // the same file is kept once, and a copy already there is never written over.
  const bytes = await readBytes(folder, file);
  if (typeof bytes === 'string') throw new Error('the format 1 file could not be read again');
  const dot = file.lastIndexOf('.');
  const stem = dot > 0 ? file.slice(0, dot) : file;
  const extension = dot > 0 ? file.slice(dot) : '';
  const kept = `${stem}.format1-${createHash('sha256').update(bytes).digest('hex').slice(0, 12)}${extension}`;
  const already = await readBytes(folder, kept);
  if (typeof already === 'string') await replaceFile(folder, kept, bytes);
  else if (!already.equals(bytes)) throw new Error('a different file holds the kept name');
  const description = broughtForward(old, starter);
  await replaceFile(folder, file, Buffer.from(`${JSON.stringify(description, null, 2)}\n`));
  const guide = await readBytes(folder, 'AGENTS.md');
  if (
    typeof guide !== 'string' &&
    createHash('sha256').update(guide).digest('hex') === FORMAT1_GUIDE
  ) {
    await replaceFile(folder, 'AGENTS.md', starter.guide);
    return { guide: 'replaced', kept };
  }
  return { guide: 'kept', kept };
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
  /** The world starter's scenes, starting description and guide; the tracked starter's by default. */
  readonly starter?: WorldStarter;
  /** What each starter declares (R2): a project is read when its starter says which file describes it. */
  readonly starters?: StarterCatalog | null;
}

/** A world reader attached to the Mac's events. */
export interface WorldReader {
  /** Stops listening. */
  readonly detach: () => void;
  /**
   * Reads every world project that has no reading of format 2, as when the Mac starts on a journal
   * written before it: a world made before format 2 is brought forward then, not at its next turn.
   * Settles once each reading is recorded.
   */
  readonly readUnread: () => Promise<void>;
}

/**
 * Journals a world project's description when the Mac has read it: once the world starter's files
 * are in the project's folder, after each turn the Mac observed there, and when `readUnread` is
 * asked, only when the reading differs from the last one recorded. A project is a world only by its
 * starter, and only its own folder, as the host resolved it, is read. Readings of one project never
 * overlap.
 */
export function attachWorldReader(attachment: WorldReaderAttachment): WorldReader {
  const starter = attachment.starter ?? loadWorldStarter();
  const queues = new Map<ProjectId, Promise<void>>();
  /** The file a project's starter says describes it, or null when it declares none (or the host has no starters). */
  const described = (starterId: string | null): string | null =>
    starterId === null ? null : (attachment.starters?.declares(starterId).describes ?? null);
  const enqueue = (projectId: ProjectId, folder: string, file: string): Promise<void> => {
    const previous = queues.get(projectId) ?? Promise.resolve();
    const scope = { project_id: projectId, workstream_id: null, execution_id: null };
    const now = () => attachment.clock.now().toISOString() as Timestamp;
    const next = previous
      .then(async () => {
        let reading = await read(folder, starter.scenes, file);
        if (reading.kind === 'format1') {
          // A world made before format 2 is brought forward once, never shown as the stock one.
          const { guide, kept } = await bringForward(folder, reading.description, starter, file);
          attachment.recorder.record(
            controlPlaneDraft(
              'project.world.brought_forward',
              scope,
              { from_format: 1, kept, scene: starter.starting.scene, guide },
              now(),
              NO_CAUSE,
            ),
          );
          reading = await read(folder, starter.scenes, file);
        }
        const observed =
          reading.kind === 'read' ? reading.observed : refused('not_a_description', '/format');
        if (same(attachment.projection.world(projectId), observed)) return;
        attachment.recorder.record(
          controlPlaneDraft('project.world.read', scope, observed, now(), NO_CAUSE),
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
    return next;
  };
  const detach = attachment.publisher.subscribe(({ event }) => {
    if (
      event.event_type !== 'project.starter_applied' &&
      event.event_type !== 'execution.files.observed'
    )
      return;
    const projectId = event.project_id;
    const project = attachment.projection.project(projectId);
    const file = described(project?.starter ?? null);
    if (project === undefined || file === null || project.location === null) return;
    const folder = project.location.path;
    // A turn in another folder than the project's own says nothing about its world.
    if (
      event.event_type === 'execution.files.observed' &&
      attachment.projection.execution(event.execution_id)?.directory !== folder
    )
      return;
    void enqueue(projectId, folder, file);
  });
  const readUnread = async (): Promise<void> => {
    const waiting: Promise<void>[] = [];
    for (const project of attachment.projection.projects()) {
      const file = described(project.starter);
      if (file === null || project.location === null) continue;
      if (attachment.projection.world(project.project_id) !== undefined) continue;
      waiting.push(enqueue(project.project_id, project.location.path, file));
    }
    await Promise.all(waiting);
  };
  return { detach, readUnread };
}
