import {
  BoxGeometry,
  Color,
  Float32BufferAttribute,
  Group,
  Mesh,
  MeshStandardMaterial,
  PlaneGeometry,
  SRGBColorSpace,
  type Texture,
} from 'three';

/** How a height picture stores its heights: brightness, or Terrarium's metres in red, green and blue. */
export type HeightEncoding = 'grey' | 'terrarium';

/** Heights as numbers, one per cell, row by row from the north-west corner: a raw grid such as a `.f32` file. */
export interface HeightGrid {
  readonly values: ArrayLike<number>;
  readonly width: number;
  readonly height: number;
}

export interface ReliefOptions {
  /** How the picture stores heights: 'grey' (brighter is higher) or 'terrarium' (R * 256 + G + B / 256 - 32768 m). */
  readonly encoding?: HeightEncoding;
  /** The table's width and depth in metres. */
  readonly width?: number;
  readonly depth?: number;
  /** How tall the highest point stands above the lowest, in metres. */
  readonly height?: number;
  /** A picture of the same ground (satellite, a map) to lay over it; without one it is coloured by height. */
  readonly picture?: Texture | null;
  /** Grid cells along each side: more is finer and costs more. */
  readonly segments?: number;
}

/** Low ground to high: green valley, dry slopes, rock, snow. */
const TINTS = ['#4E7A3A', '#9C8A55', '#8A6E52', '#B7AFA6', '#F4F4F2'].map((hex) => new Color(hex));

function tint(t: number, into: Color): Color {
  const at = Math.min(Math.max(t, 0), 1) * (TINTS.length - 1);
  const i = Math.min(Math.floor(at), TINTS.length - 2);
  return into.copy(TINTS[i] as Color).lerp(TINTS[i + 1] as Color, at - i);
}

/** The picture's heights, one per pixel, as the encoding says. */
function heightsOf(
  heights: Texture | HeightGrid,
  encoding: HeightEncoding,
): { values: ArrayLike<number>; w: number; h: number } {
  if (!('isTexture' in heights)) return { values: heights.values, w: heights.width, h: heights.height };
  const picture = heights;
  const image = picture.image as CanvasImageSource & { width: number; height: number };
  const canvas = document.createElement('canvas');
  canvas.width = image.width;
  canvas.height = image.height;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (context === null) throw new Error('This browser cannot read a height picture.');
  context.drawImage(image, 0, 0);
  const rgba = context.getImageData(0, 0, canvas.width, canvas.height).data;
  const values = new Float32Array(canvas.width * canvas.height);
  for (let i = 0; i < values.length; i += 1) {
    const r = rgba[i * 4] ?? 0;
    const g = rgba[i * 4 + 1] ?? 0;
    const b = rgba[i * 4 + 2] ?? 0;
    values[i] = encoding === 'terrarium' ? r * 256 + g + b / 256 - 32768 : (r + g + b) / 3;
  }
  return { values, w: canvas.width, h: canvas.height };
}

/**
 * A relief table of real ground from a height picture, or from a grid of heights (`HeightGrid`): the land raised in
 * 3D on a dark base, lit by the scene's lights, its top at y = 0 of the returned group, centred on it. Place the group, for example at table height in
 * front of the person: `table.position.set(0, 0.9, -1.6)`. A height picture must be loaded (from `src/assets.ts`).
 */
export function relief(heights: Texture | HeightGrid, options: ReliefOptions = {}): Group {
  const { encoding = 'grey', width = 1.6, depth = 1.6, height = 0.25, picture = null, segments = 192 } = options;
  const { values, w, h } = heightsOf(heights, encoding);
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (let i = 0; i < values.length; i += 1) {
    const value = values[i] ?? Number.NaN;
    if (!Number.isFinite(value)) continue;
    low = Math.min(low, value);
    high = Math.max(high, value);
  }
  const span = high > low ? high - low : 1;
  const ground = new PlaneGeometry(width, depth, segments, segments);
  ground.rotateX(-Math.PI / 2);
  const position = ground.getAttribute('position');
  const uv = ground.getAttribute('uv');
  const colours = new Float32Array(position.count * 3);
  const colour = new Color();
  for (let i = 0; i < position.count; i += 1) {
    const x = Math.min(w - 1, Math.round(uv.getX(i) * (w - 1)));
    const y = Math.min(h - 1, Math.round((1 - uv.getY(i)) * (h - 1)));
    const value = values[y * w + x] ?? low;
    const t = Number.isFinite(value) ? (value - low) / span : 0;
    // A few millimetres over the base, so the lowest ground never fights the base's top.
    position.setY(i, 0.004 + t * height);
    tint(t, colour).toArray(colours, i * 3);
  }
  ground.computeVertexNormals();
  if (picture) picture.colorSpace = SRGBColorSpace;
  else ground.setAttribute('color', new Float32BufferAttribute(colours, 3));
  const land = new Mesh(
    ground,
    new MeshStandardMaterial({ map: picture, vertexColors: picture === null, roughness: 0.95 }),
  );
  land.castShadow = true;
  land.receiveShadow = true;
  const base = new Mesh(
    new BoxGeometry(width, 0.06, depth),
    new MeshStandardMaterial({ color: '#1C1F26', roughness: 0.8 }),
  );
  base.position.y = -0.03;
  const table = new Group();
  table.add(base, land);
  return table;
}
