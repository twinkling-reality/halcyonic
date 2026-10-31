import { Color, MeshPhysicalMaterial } from 'three';

/** Ten named kid colours (sRGB, ART7). They sit together, read on a headset, and stay apart in hue. */
export const palette = {
  leaf: '#7CCB4B', // grass, leaves
  sky: '#4FAEFF', // sky at the top, water
  cloud: '#D8F0FF', // the sky at the horizon, soft whites
  berry: '#FF4F64', // red: noses, apples, "hit"
  sunny: '#FFC531', // yellow: stars, coins, signs
  ocean: '#2F7FF5', // blue toys
  grape: '#9A62F2', // purple toys
  tangerine: '#FF8A2E', // orange: frames, rewards
  cocoa: '#A8673F', // soil, wood, moles
  ink: '#2B2340', // eyes, outlines, text
} as const;

export type PaletteName = keyof typeof palette;

/** The second green, mixed into the grass. */
export const groundPatch = '#5AAE3C';
/** The dark band at the island's edge. */
export const soilLip = '#6A4A2E';

/**
 * Soft plastic: glossy toys on a matte world. Give a palette name or a colour; pass `ground: true` for grass, soil
 * and foliage.
 */
export function toyMaterial(
  colour: PaletteName | string,
  options: { ground?: boolean; roughness?: number } = {},
): MeshPhysicalMaterial {
  const named = (palette as Record<string, string>)[colour] ?? colour;
  return new MeshPhysicalMaterial({
    color: named,
    roughness: options.roughness ?? (options.ground ? 0.85 : 0.3),
    metalness: 0,
    clearcoat: options.ground ? 0 : 0.45,
    clearcoatRoughness: 0.22,
    sheen: 0.4,
    sheenRoughness: 0.5,
    sheenColor: new Color('#FFFFFF'),
  });
}
