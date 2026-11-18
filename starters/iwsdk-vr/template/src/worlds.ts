import { groundPatch, palette, soilLip } from './palette.js';

/**
 * The worlds a game can be set in: each its own sky, light, ground, set dressing and sound, so two games never read as
 * one place. Pick the one that fits the game in `src/index.ts`, `setupLook(world, 'night')`, or make one of your own
 * from another, `setupLook(world, { ...worlds.seaside, sky: { ...worlds.seaside.sky, top: '#3C2F7A' } })`. With no
 * world named, it is the meadow: the starter's own island, which every new game shares until it picks its own.
 * The toys keep the palette's colours in every world; a world's colours are for its sky, ground and dressing.
 */
export interface WorldLook {
  /** Its name, for you: the check and the person's computer read only which world `setupLook` was given. */
  readonly name: string;
  readonly sky: {
    /** The sky straight up, at the horizon, and, when given, between the two (a sunset's pink). */
    readonly top: string;
    readonly middle?: string;
    readonly horizon: string;
    /** A sun or a moon drawn into the sky: toward `at` from the island, `size` its radius in radians. */
    readonly disc?: { readonly at: readonly [number, number, number]; readonly colour: string; readonly size: number; readonly glow: string; readonly glowStrength: number };
    /** How many stars, in the upper sky; 0 or absent for none. */
    readonly stars?: number;
    /** The toy clouds' colour, or null for no clouds (a photographed sky brings its own). */
    readonly clouds: string | null;
    /**
     * A photographed sky in place of the drawn one: `file`, a picture in `public/` of the sky's upper half and `below`
     * (a fraction of its height) of the lower, laid round the island and turned so its sun, at `sun` across the picture
     * from its left edge, stands toward `disc.at`. `top` and `horizon` stay the sky's colours for the haze, and the
     * drawn sky stands in until the picture loads. Make your own from any sky picture laid out the same way.
     */
    readonly picture?: { readonly file: string; readonly sun: number; readonly below: number };
  };
  readonly light: {
    /** The soft light from the sky and the ground. */
    readonly sky: string;
    readonly ground: string;
    readonly fill: number;
    /** The shadowed key light (the sun, or the moon), where it shines from round the island, and its strength. */
    readonly sun: string;
    readonly sunAt: readonly [number, number, number];
    readonly sunStrength: number;
    /** A light from behind that picks out edges. */
    readonly rim: string;
    readonly rimStrength: number;
    /** Where the rim light shines from round the island; behind and to the left when not given. */
    readonly rimAt?: readonly [number, number, number];
    /** The camera's exposure under the starter's tone mapping (1 when not given): lower for a darker hour. */
    readonly exposure?: number;
    /** How strongly the sky's own light (its reflections and its soft light from every side) falls on things; 1 when not given. */
    readonly environment?: number;
    /** The haze toward the horizon, as the fog's density (0.016 when not given, 0 for none): far things fade into the sky. */
    readonly haze?: number;
  };
  readonly ground: {
    /** The island's top, mixed toward `patch`, with a worn patch in front and a rim rolling over the edge. */
    readonly top: string;
    readonly patch: string;
    readonly worn: string;
    readonly rim: string;
    readonly sheen: string;
    /** The island's side, the band at its edge, and the two stepped layers under it. */
    readonly side: string;
    readonly lip: string;
    readonly steps: readonly [string, string];
    /** A sea round the island at this height (its top is at y = 0), or none: then the island floats. */
    readonly sea?: { readonly near: string; readonly far: string; readonly glitter: string; readonly level: number };
    /** Low hills far off round the horizon, in these colours, or none. */
    readonly hills?: readonly [string, string];
    /**
     * A carpet of grass blades over the island's top, from `colour` at the root to `tip`, in large patches of `dry`
     * where given, or none (sand, stone).
     */
    readonly grass?: { readonly colour: string; readonly tip: string; readonly dry?: string };
    /** Land round the island to the horizon in these two colours, fading into the haze, or none (the sea, or open sky). */
    readonly plain?: readonly [string, string];
  };
  /**
   * The dressing `decorate()` puts at the island's sides and back. Its counts mean the same in every world: `trees`
   * are round trees in the meadow, apple trees in the garden, palms at the seaside and moon trees at night; `bushes`
   * are bushes, pumpkins, rocks and crystals; `flowers` are flowers, sunflowers, shells and glowing mushrooms;
   * `butterflies` are butterflies, butterflies, gulls and swarms of fireflies. The garden has a fence too.
   */
  readonly dressing: 'meadow' | 'garden' | 'seaside' | 'night';
  /** The world's own sound, each from 0 (none) to 1, made in the browser: no file to load. */
  readonly sound: {
    readonly wind?: number;
    readonly birds?: number;
    readonly waves?: number;
    readonly gulls?: number;
    readonly crickets?: number;
  };
}

const meadow: WorldLook = {
  name: 'meadow',
  sky: {
    top: '#768CB7',
    horizon: '#C5CAD7',
    // A morning sun, ahead and to the left: one sun drives the key light, the shadows and the sky's light.
    disc: { at: [-0.62, 0.22, -1], colour: '#FFF4DC', size: 0.05, glow: '#FFE2A8', glowStrength: 1.0 },
    clouds: null,
    // A bright day with heaped cumulus (Poly Haven's "Kloofendal 48d Partly Cloudy (Pure Sky)", CC0; NOTICE).
    picture: { file: 'skies/meadow.webp', sun: 0.5947, below: 0.06 },
  },
  light: {
    sky: '#9CC8FF',
    ground: '#8FC46E',
    fill: 0.55,
    sun: '#FFE2B4',
    // High, as the photograph's sun is (48 degrees), and stronger over less of the sky's soft light, so the day has
    // a clear key and its shadows read (CAST, assets2).
    sunAt: [-4.4, 6.0, -5.6],
    sunStrength: 3.1,
    environment: 0.8,
    rim: '#CFE4FF',
    rimStrength: 0.6,
  },
  ground: {
    top: palette.leaf,
    patch: groundPatch,
    grass: { colour: '#4E9A36', tip: '#B4E36E', dry: '#A8B65A' },
    plain: ['#6DB548', '#559E3A'],
    hills: ['#6AAE47', '#58993C'],
    worn: '#A9DE6E',
    rim: palette.leaf,
    sheen: '#E8FFD8',
    side: palette.cocoa,
    lip: soilLip,
    steps: ['#8E5634', '#764528'],
  },
  dressing: 'meadow',
  sound: { wind: 0.4, birds: 0.5 },
};

/** A garden at golden hour: a low warm sun, peach sky, hills far off, apple trees, sunflowers, pumpkins and a fence. */
const garden: WorldLook = {
  name: 'garden',
  sky: {
    top: '#6E88AE',
    middle: '#F7B98A',
    horizon: '#C3BFB9',
    disc: { at: [-0.55, 0.1, -1], colour: '#FFF6D6', size: 0.06, glow: '#FFAA55', glowStrength: 1.3 },
    clouds: null,
    // A low sun through light cloud (Poly Haven's "Kloppenheim 06 (Pure Sky)", CC0; NOTICE).
    picture: { file: 'skies/garden.webp', sun: 0.6123, below: 0.06 },
  },
  light: {
    sky: '#FFD9AE',
    ground: '#9DB85A',
    fill: 0.62,
    sun: '#FFC783',
    sunAt: [4.6, 2.7, 2.6],
    sunStrength: 2.7,
    // Thinner haze than the meadow's: under a low sun a thick one turns the far hills to sand.
    haze: 0.016,
    rim: '#FFB08A',
    rimStrength: 0.8,
  },
  ground: {
    top: '#A3CC52',
    patch: '#88B443',
    grass: { colour: '#6E9636', tip: '#D2DC7A', dry: '#C2A65A' },
    plain: ['#93B65A', '#7EA34C'],
    worn: '#C9DD7E',
    rim: '#98C24C',
    sheen: '#FFF1C8',
    side: '#9A5B36',
    lip: '#6B3F24',
    steps: ['#84492A', '#6A3820'],
    hills: ['#93B65A', '#7EA34C'],
  },
  dressing: 'garden',
  sound: { wind: 0.25, birds: 0.9 },
};

/** A seaside at sunset: a sandy island in the sea, a big orange sun on the water, palms, rocks, shells and gulls. */
const seaside: WorldLook = {
  name: 'seaside',
  sky: {
    top: '#4B4EA8',
    middle: '#EE7F94',
    horizon: '#FFA867',
    // Drawn, not photographed: its sun stands low on the horizon over its own glint on the water (CAST, assets1).
    disc: { at: [0.3, 0.05, -1], colour: '#FFE7A6', size: 0.065, glow: '#FF8A4C', glowStrength: 1.2 },
    clouds: '#FFC4A8',
  },
  light: {
    sky: '#FFBFA4',
    ground: '#6FA3C9',
    fill: 0.68,
    sun: '#FFB27A',
    sunAt: [3.4, 2.4, 2.9],
    sunStrength: 2.4,
    rim: '#FFC08A',
    rimStrength: 0.9,
    rimAt: [1.6, 1.4, -6],
  },
  ground: {
    top: '#F4DCA6',
    patch: '#EACB8E',
    worn: '#F9E8C4',
    rim: '#EFD198',
    sheen: '#FFF4DE',
    side: '#D9AE76',
    lip: '#B98A55',
    steps: ['#B07A4E', '#946240'],
    sea: { near: '#1F86B8', far: '#7FA6D6', glitter: '#FFC27A', level: -0.3 },
  },
  dressing: 'seaside',
  sound: { waves: 0.9, wind: 0.2, gulls: 0.5 },
};

/** A starry night: a deep violet sky full of stars, a big moon, moon trees, crystals, glowing mushrooms, fireflies. */
const night: WorldLook = {
  name: 'night',
  sky: {
    top: '#0D1240',
    middle: '#1E2464',
    horizon: '#3E3A86',
    disc: { at: [-0.42, 0.2, -1], colour: '#FFF6DC', size: 0.05, glow: '#A3AEFF', glowStrength: 0.8 },
    stars: 900,
    clouds: null,
  },
  light: {
    sky: '#8C9BFF',
    ground: '#2F6E78',
    fill: 0.3,
    sun: '#DCE4FF',
    sunAt: [-3.2, 4.6, 3.0],
    sunStrength: 3.4,
    exposure: 0.72,
    // Little of the sky's light, so the moon's shadows read from the seat (CAST, turf1); the shadows are drawn either
    // way, so this costs the headset nothing.
    environment: 0.3,
    rim: '#B8C6FF',
    rimStrength: 1.6,
  },
  ground: {
    top: '#1A4A4A',
    patch: '#14383C',
    grass: { colour: '#123A3C', tip: '#2E6A62', dry: '#2A3E52' },
    plain: ['#163E40', '#102E36'],
    hills: ['#1C5552', '#173F4A'],
    worn: '#22585A',
    rim: '#1C4E4C',
    sheen: '#8C9CD8',
    side: '#5B4A86',
    lip: '#3F3360',
    steps: ['#4D3F75', '#3D3262'],
  },
  dressing: 'night',
  sound: { crickets: 0.6, wind: 0.15 },
};

/** The worlds by name. `meadow` is the default, the one every new game shares: pick another that fits the game. */
export const worlds = { meadow, garden, seaside, night } as const;

export type WorldName = keyof typeof worlds;
