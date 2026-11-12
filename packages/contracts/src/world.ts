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

/** How strong a light or the air is against the world as it was made: none (0), as made (1), four times (4). */
const Strength = Type.Number({ minimum: 0, maximum: 4 });

/**
 * Format 1's place (ADR 0049 before 2026-11-12): kept only so that journals written before format 2
 * stay readable. The Mac no longer records it.
 */
export const WorldPlaceFormat1 = Type.Object(
  {
    project: Text(200),
    bearing: Bearing,
    distance: Type.Number({ minimum: 3, maximum: 80 }),
  },
  strict,
);
export type WorldPlaceFormat1 = Static<typeof WorldPlaceFormat1>;

/**
 * Format 1's description, an open sea under a sky (ADR 0049 before 2026-11-12): kept only so that
 * journals written before format 2 stay readable. The Mac no longer records it and no client draws it,
 * since the world that is drawn has none of its parts.
 */
export const WorldDescriptionFormat1 = Type.Object(
  {
    format: Type.Literal(1),
    sky: Type.Object({ zenith: Colour, band: Nullable(Colour), horizon: Colour }, strict),
    disc: Nullable(
      Type.Object(
        {
          bearing: Bearing,
          height: Type.Number({ minimum: -10, maximum: 90 }),
          size: Type.Number({ minimum: 0.25, maximum: 15 }),
          colour: Colour,
          glow: Amount,
        },
        strict,
      ),
    ),
    stars: Amount,
    clouds: Type.Object({ amount: Amount, colour: Colour }, strict),
    mist: Type.Object({ amount: Amount, colour: Colour }, strict),
    water: Type.Object({ near: Colour, far: Colour, ripple: Amount, shine: Amount }, strict),
    ground: Type.Object(
      { colour: Colour, radius: Type.Number({ minimum: 1.5, maximum: 12 }) },
      strict,
    ),
    wind: Type.Object({ bearing: Bearing, strength: Amount }, strict),
    places: Type.Array(WorldPlaceFormat1, { maxItems: WORLD_PLACES }),
  },
  strict,
);
export type WorldDescriptionFormat1 = Static<typeof WorldDescriptionFormat1>;

/** The most lights a scene has: the headset mixes eight layers of light at once. */
export const WORLD_LIGHTS = 8;

/**
 * A name a scene gives itself, one of its lights or one of its places: lower case letters, digits and
 * underscores, starting with a letter. Never a path, an address or a person's words.
 */
export const WorldName = Type.String({ pattern: '^[a-z][a-z0-9_]{0,31}$' });

/**
 * When a scene's light shines: by `day`, fading as night comes; by `night`, coming as it does; or at
 * `all` times.
 */
export const WorldLightWhen = Type.Union([
  Type.Literal('day'),
  Type.Literal('night'),
  Type.Literal('all'),
]);
export type WorldLightWhen = Static<typeof WorldLightWhen>;

/** One of a scene's lights, as its manifest names it: what a person would call it, and when it shines. */
export const WorldSceneLight = Type.Object(
  { light: WorldName, says: Text(200), when: WorldLightWhen },
  strict,
);
export type WorldSceneLight = Static<typeof WorldSceneLight>;

/** One of a scene's places, each a part of its land with its own light: what a person would call it. */
export const WorldScenePlace = Type.Object({ place: WorldName, says: Text(200) }, strict);
export type WorldScenePlace = Static<typeof WorldScenePlace>;

/**
 * A scene: a world rendered once on the Mac as separate lights, which ships its pictures with this
 * manifest beside them (ADR 0049). The manifest names the scene's lights and places, each with one plain
 * line; a description is valid for a scene only by its manifest, the headset mixes the layers it names
 * and the agent's guide promises only what it names. A later scene costs pictures and a manifest, not a
 * contract change.
 */
export const WorldScene = Type.Object(
  {
    format: Type.Literal(1),
    scene: WorldName,
    /** What the scene is, in one plain line. */
    says: Text(400),
    lights: Type.Array(WorldSceneLight, { minItems: 1, maxItems: WORLD_LIGHTS }),
    places: Type.Array(WorldScenePlace, { maxItems: WORLD_PLACES }),
  },
  strict,
);
export type WorldScene = Static<typeof WorldScene>;

/**
 * One of the world's lights, as a person changes it by speaking: which of its scene's lights, how bright
 * against the world as it was made, and the colour it is tinted, `#FFFFFF` keeping its own.
 */
export const WorldLight = Type.Object(
  { light: WorldName, brightness: Strength, colour: Colour },
  strict,
);
export type WorldLight = Static<typeof WorldLight>;

/** The air between the person and the far land: how thick against the world as made, and its glow's tint. */
export const WorldHaze = Type.Object({ thickness: Strength, colour: Colour }, strict);
export type WorldHaze = Static<typeof WorldHaze>;

/**
 * One project's place in the world: `project` is the project's name, which a client compares with the
 * names of the projects it knows and never draws, opens or uses as a path; `at` is which of the scene's
 * places is its, each used once.
 */
export const WorldPlace = Type.Object({ project: Text(200), at: WorldName }, strict);
export type WorldPlace = Static<typeof WorldPlace>;

/**
 * A person's world, as its file describes it (ADR 0049, format 2 since 2026-11-12): data, never code, a
 * path, an address or a picture. It names its scene, and every member moves something the person sees:
 * the scene was rendered as separate lights that the headset mixes by these values, so a change shows
 * with no new render. `night` runs from dusk (0) to night (1): the scene's night lights come with it, and
 * its day lights fade by the square of what is left of dusk, so halfway reads as halfway. Valid for its scene only when it gives each of the scene's lights exactly once and only
 * the scene's places, each once (`parseWorldDescription` with the scene's manifest); every number has a
 * range, every colour is six hexadecimal digits, the lists are capped and nothing else may be present.
 */
export const WorldDescription = Type.Object(
  {
    format: Type.Literal(2),
    scene: WorldName,
    night: Amount,
    lights: Type.Array(WorldLight, { maxItems: WORLD_LIGHTS }),
    haze: WorldHaze,
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
 * Format 1's reading (ADR 0049 before 2026-11-12), the payload of `project.world.observed`: kept only so
 * that journals written before format 2 stay readable. The Mac no longer records it, and the projection
 * takes nothing from it.
 */
export const WorldObservedFormat1 = Type.Object(
  {
    description: Nullable(WorldDescriptionFormat1),
    refused: Nullable(WorldRefused),
  },
  strict,
);
export type WorldObservedFormat1 = Static<typeof WorldObservedFormat1>;

/**
 * The Mac brought a world made before format 2 forward, once (ADR 0049): it kept the person's format 1
 * file whole beside it first, under `kept` (its name and the first twelve hexadecimal digits of its
 * SHA-256, so the same file is kept once), then wrote the format 2 description that keeps what maps
 * (how far night had come, its places in order) for the starter's scene, and replaced the world's guide
 * when it was the format 1 guide as the starter wrote it (`replaced`), or left a guide someone had
 * changed (`kept`). Its reading follows as `project.world.read`.
 */
export const WorldBroughtForward = Type.Object(
  {
    from_format: Type.Literal(1),
    /** The old file's name beside the new one: nothing the person's world said is lost. */
    kept: Type.String({ pattern: '^[A-Za-z0-9._-]{1,96}$' }),
    scene: WorldName,
    guide: Type.Union([Type.Literal('replaced'), Type.Literal('kept')]),
  },
  strict,
);
export type WorldBroughtForward = Static<typeof WorldBroughtForward>;

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
