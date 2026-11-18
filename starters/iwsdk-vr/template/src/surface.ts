import {
  CircleGeometry,
  Color,
  DataTexture,
  LinearMipmapLinearFilter,
  LinearFilter,
  Mesh,
  MeshPhysicalMaterial,
  NoColorSpace,
  RepeatWrapping,
  RGBAFormat,
  UnsignedByteType,
  Vector2,
  type BufferGeometry,
} from 'three';

/**
 * Surfaces with real detail, made in the browser with no file: the fine relief of grass, soil and bark as a normal map,
 * and water that ripples and mirrors the sky. Every material lit by the starter's sky light picks them up; a matte
 * colour alone reads as a toy.
 */

/** A small deterministic random sequence. */
function seeded(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Heights from layered value noise that wraps at the edges, so the map repeats with no seam. */
function heights(size: number, octaves: readonly (readonly [number, number])[], seed: number): Float32Array {
  const out = new Float32Array(size * size);
  const random = seeded(seed);
  for (const [cells, weight] of octaves) {
    const lattice = Array.from({ length: cells * cells }, () => random());
    const at = (x: number, y: number) => lattice[((y % cells) + cells) % cells * cells + (((x % cells) + cells) % cells)] ?? 0;
    for (let y = 0; y < size; y++)
      for (let x = 0; x < size; x++) {
        const fx = (x / size) * cells;
        const fy = (y / size) * cells;
        const ix = Math.floor(fx);
        const iy = Math.floor(fy);
        const sx = fx - ix;
        const sy = fy - iy;
        const ux = sx * sx * (3 - 2 * sx);
        const uy = sy * sy * (3 - 2 * sy);
        const top = at(ix, iy) * (1 - ux) + at(ix + 1, iy) * ux;
        const bottom = at(ix, iy + 1) * (1 - ux) + at(ix + 1, iy + 1) * ux;
        out[y * size + x] += (top * (1 - uy) + bottom * uy) * weight;
      }
  }
  return out;
}

/** A tangent-space normal map from heights that wrap, `strength` the slope it reads. */
function normalMap(size: number, height: Float32Array, strength: number): DataTexture {
  const data = new Uint8Array(size * size * 4);
  const h = (x: number, y: number) => height[((y + size) % size) * size + ((x + size) % size)] ?? 0;
  for (let y = 0; y < size; y++)
    for (let x = 0; x < size; x++) {
      const dx = (h(x + 1, y) - h(x - 1, y)) * strength;
      const dy = (h(x, y + 1) - h(x, y - 1)) * strength;
      const length = Math.hypot(dx, dy, 1);
      const i = (y * size + x) * 4;
      data[i] = Math.round((-dx / length * 0.5 + 0.5) * 255);
      data[i + 1] = Math.round((-dy / length * 0.5 + 0.5) * 255);
      data[i + 2] = Math.round((1 / length * 0.5 + 0.5) * 255);
      data[i + 3] = 255;
    }
  const texture = new DataTexture(data, size, size, RGBAFormat, UnsignedByteType);
  texture.wrapS = texture.wrapT = RepeatWrapping;
  texture.magFilter = LinearFilter;
  texture.minFilter = LinearMipmapLinearFilter;
  texture.generateMipmaps = true;
  texture.colorSpace = NoColorSpace;
  texture.anisotropy = 4;
  texture.needsUpdate = true;
  return texture;
}

let grain: DataTexture | null = null;
let ripples: DataTexture | null = null;

/** The fine relief of a natural surface (grass, soil, bark, stone): one map, shared, 256 square. */
export function surfaceGrain(): DataTexture {
  if (grain === null)
    grain = normalMap(
      256,
      heights(
        256,
        [
          [8, 0.45],
          [24, 0.3],
          [64, 0.25],
        ],
        3,
      ),
      6,
    );
  return grain;
}

/** Small waves on water: one map, shared, 256 square, softer than the ground's grain. */
export function waterRipples(): DataTexture {
  if (ripples === null)
    ripples = normalMap(
      256,
      heights(
        256,
        [
          [4, 0.55],
          [10, 0.3],
          [22, 0.15],
        ],
        9,
      ),
      4,
    );
  return ripples;
}

/**
 * Gives a natural material its surface: the grain as its normal map, repeated `repeat` times across the mesh's own
 * texture coordinates, at `depth` (0.25 a soft lawn, 0.6 bark or rock). Returns the material.
 */
export function withGrain<M extends MeshPhysicalMaterial>(material: M, repeat = 6, depth = 0.35): M {
  const map = surfaceGrain().clone();
  map.repeat.set(repeat, repeat);
  map.needsUpdate = true;
  material.normalMap = map;
  material.normalScale = new Vector2(depth, depth);
  return material;
}

/**
 * A water material: clear and smooth, its ripples moving under the sky's light so it mirrors the sky, the sun's glint
 * and the shore. `colour` is the water's own deep colour; pass `move` your clock to set the ripples drifting each frame.
 */
export function waterMaterial(colour = '#1F5F7A', repeat = 10): MeshPhysicalMaterial & { move(time: number): void } {
  const map = waterRipples().clone();
  map.repeat.set(repeat, repeat);
  map.needsUpdate = true;
  const second = waterRipples().clone();
  second.repeat.set(repeat * 0.63, repeat * 0.63);
  second.needsUpdate = true;
  const material = new MeshPhysicalMaterial({
    color: new Color(colour),
    roughness: 0.05,
    metalness: 0,
    ior: 1.333,
    clearcoat: 0.4,
    clearcoatRoughness: 0.06,
    normalMap: map,
    normalScale: new Vector2(0.55, 0.55),
    clearcoatNormalMap: second,
    clearcoatNormalScale: new Vector2(0.35, 0.35),
    envMapIntensity: 1.1,
  });
  return Object.assign(material, {
    move(time: number): void {
      map.offset.set(time * 0.011, time * 0.007);
      second.offset.set(-time * 0.006, time * 0.009);
    },
  });
}

/**
 * Water to put in a world (a lake, a pond, a pool): a flat disc of `radius` metres, its top at y = 0 where it stands,
 * that ripples and mirrors the sky. Pass its `move` to `ambientMotion`. For another outline give the shape's own
 * geometry, laid flat, in `geometry`.
 */
export function water(
  radius: number,
  options: { colour?: string; geometry?: BufferGeometry } = {},
): { mesh: Mesh; move(time: number): void } {
  const geometry = options.geometry ?? new CircleGeometry(radius, 96).rotateX(-Math.PI / 2);
  const material = waterMaterial(options.colour, Math.max(2, radius * 1.6));
  const mesh = new Mesh(geometry, material);
  mesh.receiveShadow = true;
  return { mesh, move: (time) => material.move(time) };
}
