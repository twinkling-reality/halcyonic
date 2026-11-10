import Type, { type Static } from 'typebox';
import { Nullable, Text, Timestamp } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/** The starter a person's world begins from (ADR 0049): a description and a guide for its agent, no code. */
export const WORLD_STARTER_ID = 'world';

/** The description's file, at the top of the world project's folder. */
export const WORLD_FILE = 'world.json';

/** The most bytes of that file the Mac reads; a larger file is refused unread. */
export const WORLD_FILE_BYTES = 16 * 1024;

/** The most places a description holds. */
export const WORLD_PLACES = 8;

/** A colour as six hexadecimal digits after `#`: nothing else names a colour. */
const Colour = Type.String({ pattern: '^#[0-9A-Fa-f]{6}$' });

/** How much of something, from none (0) to all of it (1). */
const Amount = Type.Number({ minimum: 0, maximum: 1 });

/** A direction on the level, in degrees from straight ahead of the person: positive to their right. */
const Bearing = Type.Number({ minimum: -180, maximum: 180 });

/**
 * Where one project stands in the world. `project` is the project's name: a client compares it with
 * the names of the projects it knows and never draws it, opens it or uses it as a path.
 */
export const WorldPlace = Type.Object(
  {
    project: Text(200),
    bearing: Bearing,
    /** Meters from the person. */
    distance: Type.Number({ minimum: 3, maximum: 80 }),
  },
  strict,
);
export type WorldPlace = Static<typeof WorldPlace>;

/**
 * A person's world, as its file describes it (ADR 0049): data a client draws with parts of its own,
 * never code, a path, an address or a picture. Every number has a range, every colour is six
 * hexadecimal digits, the one list is capped and nothing else may be present; what a description
 * does not name, a client does not draw.
 */
export const WorldDescription = Type.Object(
  {
    format: Type.Literal(1),
    sky: Type.Object(
      {
        /** Straight up. */
        zenith: Colour,
        /** A colour low in the sky, between the two (a sunset's pink), or null for none. */
        band: Nullable(Colour),
        horizon: Colour,
      },
      strict,
    ),
    /** A sun or a moon, or null for neither. */
    disc: Nullable(
      Type.Object(
        {
          bearing: Bearing,
          /** Degrees above the horizon; below 0 it has set and only its glow shows. */
          height: Type.Number({ minimum: -10, maximum: 90 }),
          /** Its radius, in degrees. */
          size: Type.Number({ minimum: 0.25, maximum: 15 }),
          colour: Colour,
          glow: Amount,
        },
        strict,
      ),
    ),
    stars: Amount,
    clouds: Type.Object({ amount: Amount, colour: Colour }, strict),
    /** Haze at the horizon, where the water meets the sky. */
    mist: Type.Object({ amount: Amount, colour: Colour }, strict),
    water: Type.Object(
      {
        near: Colour,
        far: Colour,
        /** From still (0) to choppy (1). */
        ripple: Amount,
        /** How strongly it gives back the sky's light. */
        shine: Amount,
      },
      strict,
    ),
    /** The ground the person is on, round them. */
    ground: Type.Object(
      {
        colour: Colour,
        /** Meters. */
        radius: Type.Number({ minimum: 1.5, maximum: 12 }),
      },
      strict,
    ),
    wind: Type.Object({ bearing: Bearing, strength: Amount }, strict),
    places: Type.Array(WorldPlace, { maxItems: WORLD_PLACES }),
  },
  strict,
);
export type WorldDescription = Static<typeof WorldDescription>;

/**
 * Why the Mac did not take the file as a description: not there, not a regular file (a link is
 * never followed), larger than it reads, not UTF-8 text, not JSON, or JSON that is not a
 * description. `where` points at the first member that broke a rule, by the description's own
 * member names and list positions only, never by words from the file; `message` is the Mac's own
 * sentence, never the file's content.
 */
export const WorldRefused = Type.Object(
  {
    code: Type.Union([
      Type.Literal('missing'),
      Type.Literal('not_a_file'),
      Type.Literal('too_large'),
      Type.Literal('not_text'),
      Type.Literal('not_json'),
      Type.Literal('not_a_description'),
    ]),
    where: Nullable(Type.String({ pattern: '^(/[a-z_]{1,32}|/[0-9]{1,4}){1,8}$', maxLength: 120 })),
    message: Type.String({ minLength: 1, maxLength: 2000 }),
  },
  strict,
);
export type WorldRefused = Static<typeof WorldRefused>;

/**
 * What the Mac found when it read a world project's description file itself: the description, or
 * why the file is not one. Exactly one of the two is null.
 */
export const WorldObserved = Type.Object(
  {
    description: Nullable(WorldDescription),
    refused: Nullable(WorldRefused),
  },
  strict,
);
export type WorldObserved = Static<typeof WorldObserved>;

/**
 * A world project's description as the Mac last read it (ADR 0049). `description` is the newest
 * one that was valid, kept while a later reading is refused, so a bad edit never empties the world;
 * null until one was. `refused` says why the file as last read is not a description, or null when
 * that reading was valid. `observed_at` is when the Mac last read the file.
 */
export const ProjectWorldView = Type.Object(
  {
    description: Nullable(WorldDescription),
    refused: Nullable(WorldRefused),
    observed_at: Timestamp,
  },
  strict,
);
export type ProjectWorldView = Static<typeof ProjectWorldView>;
