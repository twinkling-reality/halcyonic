import {
  Color,
  DoubleSide,
  Float32BufferAttribute,
  InstancedBufferAttribute,
  InstancedMesh,
  type Material,
  MeshStandardMaterial,
  Object3D,
  PlaneGeometry,
  type BufferGeometry,
} from 'three';

/**
 * Grass as grass: thousands of thin bending blades, one draw call, darker at the root and lighter at the tip, moving
 * in the wind on the headset's own GPU (no work per frame on the CPU). The starter lays a carpet of it over each grass
 * world's island; `grassBlade()` is the one blade, for a tuft of your own.
 */

/** One blade, 1 m tall before scaling, rooted at y = 0, tapering to its tip and curving forward as it rises. */
export function grassBlade(width = 0.022): BufferGeometry {
  const blade = new PlaneGeometry(width, 1, 1, 4).translate(0, 0.5, 0);
  const position = blade.getAttribute('position');
  const shade: number[] = [];
  for (let i = 0; i < position.count; i++) {
    const y = position.getY(i);
    position.setX(i, position.getX(i) * (1 - y * 0.85));
    position.setZ(i, 0.1 * y * y);
    // The root sits in the shade of the blades round it; the tip catches the light.
    const s = 0.42 + 0.58 * y ** 0.8;
    shade.push(s, s, s);
  }
  blade.setAttribute('color', new Float32BufferAttribute(shade, 3));
  // Lit as the ground under it is, from above, whichever side faces the eye: a lawn, not a field of cards.
  const normal = blade.getAttribute('normal');
  for (let i = 0; i < normal.count; i++) normal.setXYZ(i, 0, 1, 0);
  return blade;
}

/**
 * Draws a material's blades from both sides, each side keeping the blade's upward normal (three.js would turn a back
 * face's normal over and light it from below). Returns the material.
 */
export function bothSidesUp<M extends Material>(material: M): M {
  material.side = DoubleSide;
  const before = material.onBeforeCompile.bind(material);
  material.onBeforeCompile = (shader, renderer) => {
    before(shader, renderer);
    shader.fragmentShader = shader.fragmentShader.replace(
      '#include <normal_fragment_begin>',
      '#include <normal_fragment_begin>\nnormal = normalize( vNormal );',
    );
  };
  return material;
}

/** A small deterministic random sequence, so the same island grows the same grass on every load. */
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

/** The grass material: each blade's own colour times its root-to-tip shade, swaying by the time it is given. */
function swayingGrass(): MeshStandardMaterial & { move(time: number): void } {
  const material = new MeshStandardMaterial({ vertexColors: true, side: DoubleSide, roughness: 0.82, metalness: 0 });
  const time = { value: 0 };
  material.onBeforeCompile = (shader) => {
    shader.uniforms.grassTime = time;
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', '#include <common>\nuniform float grassTime;')
      .replace(
        '#include <begin_vertex>',
        [
          '#include <begin_vertex>',
          // Gusts that roll across the island; the tip moves most, the root not at all.
          'vec3 grassRoot = vec3(instanceMatrix[3][0], 0.0, instanceMatrix[3][2]);',
          'float grassGust = sin(grassTime * 1.6 + grassRoot.x * 1.9 + grassRoot.z * 1.3) * 0.6 + sin(grassTime * 2.9 + grassRoot.x * 4.1) * 0.25;',
          'transformed.x += grassGust * 0.16 * position.y * position.y;',
          'transformed.z += grassGust * 0.06 * position.y * position.y;',
        ].join('\n'),
      );
    // Both faces keep the upward normal: three.js turns a back face's normal over, which would light half the blades
    // from below and draw them dark.
    shader.fragmentShader = shader.fragmentShader.replace(
      '#include <normal_fragment_begin>',
      '#include <normal_fragment_begin>\nnormal = normalize( vNormal );',
    );
  };
  material.customProgramCacheKey = () => 'halcyonic-grass';
  return Object.assign(material, {
    move(t: number): void {
      time.value = t;
    },
  });
}

/**
 * A carpet of grass over ground at y = 0: `count` blades wherever `grows(x, z)` says, each `height` metres tall or so
 * (`height(x, z)` for its own), in colours between `root` and `tip`. Returns the mesh and its `move` for `ambientMotion`.
 */
export function grassCarpet(options: {
  count: number;
  extent: number;
  grows: (x: number, z: number) => boolean;
  height: (x: number, z: number) => number;
  colour: string;
  tip: string;
  /** A third hue for large dry patches among the green, and where the key light comes from (x, z), to shade away from it. */
  dry?: string;
  light?: readonly [number, number];
  seed?: number;
}): { mesh: InstancedMesh; move(time: number): void } {
  const random = seeded(options.seed ?? 5);
  const material = swayingGrass();
  const mesh = new InstancedMesh(grassBlade(), material, options.count);
  const base = new Color(options.colour);
  const dry = options.dry === undefined ? null : new Color(options.dry);
  const toward = options.light === undefined ? null : Math.hypot(options.light[0], options.light[1]);
  const light =
    options.light === undefined || toward === null || toward === 0
      ? null
      : ([options.light[0] / toward, options.light[1] / toward] as const);
  const tip = new Color(options.tip);
  const colours = new Float32Array(options.count * 3);
  const place = new Object3D();
  let placed = 0;
  for (let tries = 0; placed < options.count && tries < options.count * 6; tries++) {
    const x = (random() * 2 - 1) * options.extent;
    const z = (random() * 2 - 1) * options.extent;
    if (!options.grows(x, z)) continue;
    const h = options.height(x, z) * (0.65 + random() * 0.7);
    place.position.set(x, 0, z);
    place.rotation.set(0, random() * Math.PI * 2, 0);
    place.scale.set(0.8 + random() * 0.6, h, 1);
    place.updateMatrix();
    mesh.setMatrixAt(placed, place.matrix);
    const c = base.clone().lerp(tip, random() * 0.7);
    // Large patches, metres across: lusher and drier ground, lighter and darker, so the lawn never reads as paint.
    const patch = 0.5 + 0.5 * Math.sin(x * 0.9 + Math.sin(z * 0.7) * 1.8) * Math.sin(z * 1.1 + Math.sin(x * 0.5) * 1.4);
    if (dry !== null) c.lerp(dry, Math.min(0.85, Math.max(0, patch - 0.4) * 1.9));
    const value = 0.66 + 0.52 * (0.5 + 0.5 * Math.sin(x * 1.7 + z * 0.6) * Math.sin(z * 1.3 - x * 0.4));
    // Toward the key light the lawn is brighter, away from it darker.
    const lit = light === null ? 1 : 0.86 + 0.14 * ((x * light[0] + z * light[1]) / options.extent);
    c.multiplyScalar(value * lit);
    c.offsetHSL((random() - 0.5) * 0.02, 0, (random() - 0.5) * 0.04);
    colours.set([c.r, c.g, c.b], placed * 3);
    placed++;
  }
  mesh.count = placed;
  mesh.instanceColor = new InstancedBufferAttribute(colours, 3);
  mesh.instanceMatrix.needsUpdate = true;
  mesh.computeBoundingSphere();
  mesh.receiveShadow = true;
  mesh.castShadow = false;
  return { mesh, move: (time) => material.move(time) };
}
