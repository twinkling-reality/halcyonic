/**
 * A place from a person's own picture: the colours of a photo or a frame of a video, read as a sky over a ground, so a
 * place (`openLand` in `src/places.ts`) can take its light from where the person was. Pure numbers here; the browser
 * part that reads a picture's pixels and shows it in the scene is `coloursOfPicture` and `pictureWindow`.
 */

/** Colours as `#RRGGBB` (sRGB), as the kit's other places take them. */
export interface PictureColours {
  /** The sky overhead: the picture's top rows. */
  readonly top: string;
  /** The sky halfway down to the horizon. */
  readonly middle: string;
  /** The sky just above the horizon: its glow. */
  readonly horizon: string;
  /** The ground just below the horizon, far off. */
  readonly far: string;
  /** The ground at the bottom of the picture, near the person. */
  readonly near: string;
  /** The brightest light in the sky (the sun, or where it set). */
  readonly light: string;
  /** Where the horizon was found, from 0 at the picture's top to 1 at its bottom. */
  readonly horizonAt: number;
}

/** An hour to move the picture's light to: as it was, or dusk (a deep blue overhead, a warm low glow, a dim ground). */
export type Hour = 'as-is' | 'dusk';

type Rgb = [number, number, number];

const luma = (c: Rgb) => 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];

function hex(c: Rgb): string {
  const byte = (v: number) =>
    Math.round(Math.min(1, Math.max(0, v)) * 255)
      .toString(16)
      .padStart(2, '0');
  return `#${byte(c[0])}${byte(c[1])}${byte(c[2])}`.toUpperCase();
}

function mix(a: Rgb, b: Rgb, t: number): Rgb {
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
}

/** The mean of rows [from, to) of the row means. */
function band(rows: readonly Rgb[], from: number, to: number): Rgb {
  const a = Math.max(0, Math.min(rows.length - 1, Math.floor(from)));
  const b = Math.max(a + 1, Math.min(rows.length, Math.ceil(to)));
  const sum: Rgb = [0, 0, 0];
  for (let r = a; r < b; r++) {
    const row = rows[r] as Rgb;
    sum[0] += row[0];
    sum[1] += row[1];
    sum[2] += row[2];
  }
  return [sum[0] / (b - a), sum[1] / (b - a), sum[2] / (b - a)];
}

/**
 * Lift a colour that has a hue a little away from grey (a photo's colours read flatter in a headset than on a phone),
 * keeping its hue and lightness; a colour with no hue to speak of stays grey, never tinted.
 */
function clear(c: Rgb): Rgb {
  const max = Math.max(...c);
  const min = Math.min(...c);
  const light = (max + min) / 2;
  const chroma = max - min;
  if (chroma < 0.04 || light <= 0 || light >= 1) return c;
  const target = Math.min(1, chroma * 1.25, 2 * Math.min(light, 1 - light));
  const k = target / chroma;
  return [light + (c[0] - light) * k, light + (c[1] - light) * k, light + (c[2] - light) * k];
}

const DUSK_TOP: Rgb = [0.055, 0.05, 0.18];
const DUSK_MIDDLE: Rgb = [0.29, 0.18, 0.39];
const DUSK_GLOW: Rgb = [0.85, 0.5, 0.33];

function toDusk(c: PictureColoursRgb): PictureColoursRgb {
  return {
    top: mix(c.top, DUSK_TOP, 0.75),
    middle: mix(c.middle, DUSK_MIDDLE, 0.6),
    horizon: mix(c.horizon, DUSK_GLOW, 0.45),
    far: mix(c.far, DUSK_MIDDLE, 0.15).map((v) => v * 0.72) as Rgb,
    near: mix(c.near, DUSK_GLOW, 0.1).map((v) => v * 0.62) as Rgb,
    light: mix(c.light, DUSK_GLOW, 0.5),
    horizonAt: c.horizonAt,
  };
}

interface PictureColoursRgb {
  top: Rgb;
  middle: Rgb;
  horizon: Rgb;
  far: Rgb;
  near: Rgb;
  light: Rgb;
  horizonAt: number;
}

/**
 * Read a picture as a sky over a ground. `pixels` are RGBA bytes row by row from the top left (a canvas's
 * `getImageData(...).data`), `width` by `height`. The horizon is the row where the picture darkens most going down,
 * looked for between a sixth and five sixths of the way down; above it is sky, below it ground.
 */
export function pictureColours(
  pixels: ArrayLike<number>,
  width: number,
  height: number,
  options: { hour?: Hour } = {},
): PictureColours {
  if (width < 1 || height < 6 || pixels.length < width * height * 4) {
    throw new RangeError(`pictureColours needs at least 1 x 6 RGBA pixels, given ${width} x ${height}`);
  }
  const rows: Rgb[] = [];
  for (let y = 0; y < height; y++) {
    const sum: Rgb = [0, 0, 0];
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 4;
      sum[0] += (pixels[i] as number) / 255;
      sum[1] += (pixels[i + 1] as number) / 255;
      sum[2] += (pixels[i + 2] as number) / 255;
    }
    rows.push([sum[0] / width, sum[1] / width, sum[2] / width]);
  }
  // The horizon: the largest drop in brightness over a few rows, between a sixth and five sixths of the way down.
  const reach = Math.max(1, Math.round(height / 24));
  let at = Math.round(height / 2);
  let drop = -Infinity;
  for (let y = Math.ceil(height / 6); y <= Math.floor((height * 5) / 6); y++) {
    const above = band(rows, y - reach, y);
    const below = band(rows, y, y + reach);
    const d = luma(above) - luma(below);
    if (d > drop) {
      drop = d;
      at = y;
    }
  }
  // The brightest sky pixels, the top fiftieth by brightness, for the light.
  const sky: { l: number; c: Rgb }[] = [];
  for (let y = 0; y < at; y++) {
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 4;
      const c: Rgb = [(pixels[i] as number) / 255, (pixels[i + 1] as number) / 255, (pixels[i + 2] as number) / 255];
      sky.push({ l: luma(c), c });
    }
  }
  sky.sort((a, b) => b.l - a.l);
  const brightest = sky.slice(0, Math.max(1, Math.round(sky.length / 50)));
  const light: Rgb = [0, 0, 0];
  for (const s of brightest) {
    light[0] += s.c[0] / brightest.length;
    light[1] += s.c[1] / brightest.length;
    light[2] += s.c[2] / brightest.length;
  }

  const rgb: PictureColoursRgb = {
    top: band(rows, 0, Math.max(1, at * 0.2)),
    middle: band(rows, at * 0.4, at * 0.7),
    horizon: band(rows, at - Math.max(1, at * 0.12), at),
    far: band(rows, at, at + Math.max(1, (height - at) * 0.15)),
    near: band(rows, height - Math.max(1, (height - at) * 0.25), height),
    light,
    horizonAt: at / height,
  };
  const out = options.hour === 'dusk' ? toDusk(rgb) : rgb;
  return {
    top: hex(clear(out.top)),
    middle: hex(clear(out.middle)),
    horizon: hex(clear(out.horizon)),
    far: hex(clear(out.far)),
    near: hex(clear(out.near)),
    light: hex(clear(out.light)),
    horizonAt: out.horizonAt,
  };
}

/** How high a swell stands at phase `s` (0..1 along the wind), from 0 to 1: soft hills at `crest` 0, dunes at 1. */
export function swellAt(s: number, crest: number): number {
  const soft = 0.5 - 0.5 * Math.cos(s * Math.PI * 2);
  // A dune rises slowly on the side the wind comes from and falls steeply on the far side.
  const sharp = s < 0.78 ? (s / 0.78) ** 1.6 : (1 - (s - 0.78) / 0.22) ** 1.3;
  return soft + (sharp - soft) * crest;
}

/** The shape of open land: its height at any point, and the way its wind blows (a unit direction on the ground). */
export interface LandShape {
  height(x: number, z: number): number;
  readonly wind: readonly [number, number];
}

/**
 * Long swells of ground running across the wind, `swell` metres high near the person (6; they grow a little toward the
 * horizon, to about 1.6 times at 110 m), `crest` 1 for dunes and 0 for soft hills (0.8), the wind blowing nearly from
 * the low sun's direction `sun` (radians, 0 ahead of the person along -z, positive to their right; 1.25 by default), so
 * the crests run across the light: faces lit toward the sun, shadow in their lee. Level ground within 3 m of the
 * person, rising to full swells by 12 m; each swell's height changes along its crest, so no two crests repeat. The
 * same for the same `seed`.
 */
export function landShape(options: { swell?: number; crest?: number; sun?: number; seed?: number } = {}): LandShape {
  const swell = options.swell ?? 6;
  const crest = Math.min(1, Math.max(0, options.crest ?? 0.8));
  let state = options.seed ?? 7;
  const random = () => {
    state = (state * 16807) % 2147483647;
    return state / 2147483647;
  };
  const wind = (options.sun ?? 1.25) + 0.3;
  const wx = Math.sin(wind);
  const wz = -Math.cos(wind);
  const waves = [
    { length: 41, height: 1, shift: random() },
    { length: 17, height: 0.42, shift: random() },
    { length: 83, height: 0.8, shift: random() },
  ];
  const most = waves.reduce((sum, w) => sum + w.height, 0);
  return {
    wind: [wx, wz],
    height(x, z) {
      const r = Math.hypot(x, z);
      const level = Math.min(1, Math.max(0, (r - 3) / 9));
      if (level === 0) return 0;
      const along = x * wx + z * wz;
      const across = -x * wz + z * wx;
      let h = 0;
      for (let i = 0; i < waves.length; i++) {
        const w = waves[i] as (typeof waves)[number];
        // Crests bend slowly along their length, as a dune field's do, never ruled straight.
        const bend =
          0.18 * Math.sin(across / (w.length * 2.3) + w.shift * 6.28) + 0.07 * Math.sin(across / (w.length * 0.9));
        const s = (((along / w.length + bend + w.shift) % 1) + 1) % 1;
        // Taller here and lower there along each crest, so the field never repeats like teeth.
        const along2 =
          0.74 + 0.26 * Math.sin(across / (w.length * 1.7) + w.shift * 11) * Math.sin(along / (w.length * 3.1) + w.shift * 5);
        // Far off, crests soften, as distance and haze round them, and no sharp edge is cut into steps.
        h += swellAt(s, crest * (1 - 0.5 * Math.min(1, Math.max(0, (r - 30) / 50)))) * w.height * along2;
      }
      const smooth = level * level * (3 - 2 * level);
      // Lower again toward the rim at 110 m, so the land meets the sky low and softly, never cut off high.
      const rim = 1 - 0.5 * Math.min(1, Math.max(0, (r - 85) / 25)) ** 2;
      return smooth * swell * (h / most) * (1 + r / 140) * rim;
    },
  };
}
