import Type, { type Static } from 'typebox';
import { ProjectPath } from './checkpoints.ts';
import { Sha256Hex } from './devices.ts';
import { Nullable, Timestamp } from './primitives.ts';

/**
 * Photos and videos the person adds to a project (ADR 0066): the bytes go to the host first, through
 * the Mac's own page, which keeps them by their SHA-256 and never journals them; `project.media.add`
 * names that digest as its own payload, then copies the file into the project's folder under a name
 * the host chooses, and the host makes stills from it for agents that read only pictures. Its events
 * name the placed file, never the upload. The journal holds paths, types, sizes and digests, never
 * bytes, the person's file name or anything read from inside the file (where or when it was taken).
 */

const strict = { additionalProperties: false } as const;
const Count = Type.Integer({ minimum: 0, maximum: Number.MAX_SAFE_INTEGER });

/**
 * What a photo or video is, read from its bytes and their structure, never from its name: PNG, JPEG
 * and WebP as pictures are read; HEIC by its `ftyp` brand (heic, heix, mif1); MP4 (isom, iso2 to iso6,
 * mp41, mp42, avc1, M4V) and QuickTime (`qt  `) by theirs, with top-level boxes that end at the file's
 * end and a `moov` box.
 */
export const MediaType = Type.Union([
  Type.Literal('image/png'),
  Type.Literal('image/jpeg'),
  Type.Literal('image/webp'),
  Type.Literal('image/heic'),
  Type.Literal('video/mp4'),
  Type.Literal('video/quicktime'),
]);
export type MediaType = Static<typeof MediaType>;

/**
 * The largest video the host takes, in bytes: 80 MB, under the 100 MiB GitHub takes in one file. Every
 * bound here is a round number of MB as a Mac counts them (1,000,000 bytes), so the words that say it
 * are true there.
 */
export const MEDIA_VIDEO_BYTES = 80_000_000;
/** The largest photo the host takes, in bytes: 25 MB. */
export const MEDIA_PHOTO_BYTES = 25_000_000;
/** The longest video the host makes stills of, in milliseconds; a longer one is added without them. */
export const MEDIA_VIDEO_MS = 180_000;
/** The most photos and videos one project holds. */
export const MEDIA_PER_PROJECT = 50;
/**
 * The most bytes one project's photos and videos take, the files the host made from them counted: 240
 * MB, well under the 2 GiB a checkpoint (and so a task's copy) may hold.
 */
export const MEDIA_FOLDER_BYTES = 240_000_000;
/** The most stills the host makes of one video; a photo gets one. */
export const STILLS_PER_VIDEO = 4;

/** A file the host wrote into the project's folder: its path there, size and digest. */
export const MediaFile = Type.Object(
  { path: ProjectPath, size_bytes: Count, sha256: Sha256Hex },
  strict,
);
export type MediaFile = Static<typeof MediaFile>;

/** One still the host made of a photo or video: a JPEG, and where in the video it was taken (0 for a photo). */
export const MediaStill = Type.Object(
  { path: ProjectPath, size_bytes: Count, sha256: Sha256Hex, at_ms: Count },
  strict,
);
export type MediaStill = Static<typeof MediaStill>;

/**
 * Why the host made no stills or no play copy: its tool is not on this Mac, or is not the one whose
 * digest the person recorded (a pinned tool is never taken on a new digest by itself), the file could
 * not be read, the tool ran out of time, the video is longer than `MEDIA_VIDEO_MS`, the file made was past
 * its own bound (a play copy past `MEDIA_VIDEO_BYTES`, a still past `PICTURE_BYTES`), or there was no room
 * for it within `MEDIA_FOLDER_BYTES`.
 */
export const MediaNotMade = Type.Union([
  Type.Literal('tool_missing'),
  Type.Literal('tool_changed'),
  Type.Literal('unreadable'),
  Type.Literal('timed_out'),
  Type.Literal('too_long'),
  Type.Literal('too_large'),
  Type.Literal('no_room'),
]);
export type MediaNotMade = Static<typeof MediaNotMade>;

/**
 * A photo or video of the project, as the journal has it, newest first. `still` is the first still the
 * host made, `play` the copy of a video any browser plays; each null when not made (`prepared` says
 * whether the host has finished trying). `duration_ms` is null for a photo and where it was not read.
 * Whether the file is still in the folder is a read of the folder, never this view.
 */
export const ProjectMediaView = Type.Object(
  {
    path: ProjectPath,
    media_type: MediaType,
    size_bytes: Count,
    added_at: Timestamp,
    still: Nullable(ProjectPath),
    play: Nullable(ProjectPath),
    duration_ms: Nullable(Count),
    prepared: Type.Boolean(),
  },
  strict,
);
export type ProjectMediaView = Static<typeof ProjectMediaView>;

/** A photo or video given to a run, with what a card draws it by: its type and its first still, or null. */
export const ExecutionMediaView = Type.Object(
  { path: ProjectPath, media_type: MediaType, still: Nullable(ProjectPath) },
  strict,
);
export type ExecutionMediaView = Static<typeof ExecutionMediaView>;
