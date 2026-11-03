import {
  BoxGeometry,
  type BufferGeometry,
  CanvasTexture,
  CapsuleGeometry,
  CatmullRomCurve3,
  Color,
  ConeGeometry,
  CylinderGeometry,
  DoubleSide,
  ExtrudeGeometry,
  Float32BufferAttribute,
  Group,
  InstancedMesh,
  LatheGeometry,
  Mesh,
  type MeshPhysicalMaterial,
  Object3D,
  PlaneGeometry,
  Shape,
  ShapeGeometry,
  SphereGeometry,
  SRGBColorSpace,
  TorusGeometry,
  TubeGeometry,
  Vector2,
  Vector3,
} from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { ambientMotion, contactShadow, gameIsland, gameTime, ISLAND_CENTRE, TIER } from './look.js';
import { ambient, confettiPiece, flap, flight, juice, pulse, sway } from './motion.js';
import { type PaletteName, palette, toyMaterial } from './palette.js';

/**
 * Where the decor stands, round the island's centre, as (x, z, ...): at its sides and back, with the middle, from 1 to
 * 3 m in front of the player (who stands at z = 1.8 here), left clear for the game.
 */
const LAYOUT = {
  trees: [
    [-2.2, -1.2, 1.1],
    [2.3, -1.0, 0.9],
    [-1.15, -2.05, 0.7],
  ],
  bushes: [
    [1.9, 1.6, 0.8],
    [-2.4, 1.2, 0.7],
    [0.9, -2.4, 0.6],
  ],
  flowers: [
    [-1.7, 1.55, 'berry'],
    [-1.5, 1.85, 'sunny'],
    [-1.2, 1.6, 'berry'],
    [1.75, 1.15, 'grape'],
    [1.5, 1.4, 'ocean'],
    [2.0, -0.3, 'tangerine'],
    [1.75, -0.05, 'sunny'],
    [-2.0, -0.4, 'grape'],
  ],
  tufts: [
    [-1.9, -0.6],
    [1.9, -0.2],
    [-1.3, 1.7],
    [1.6, 1.3],
    [-0.6, -1.5],
    [0.9, -1.7],
  ],
  butterflies: [
    [-0.95, 0.45, 0.75, 0.5, 'sunny', 'tangerine'],
    [1.1, -0.2, 0.95, 0.45, 'ocean', 'grape'],
  ],
} as const;

/**
 * Dressing for the island (ART7): round trees, bushes, flowers and grass tufts at its sides and back, and a couple of
 * butterflies, the middle left clear. `src/index.ts` calls it for every new game. A breeze sways the flowers, grass and
 * trees, and the butterflies loop in front of the player, by themselves. Each prop is one mesh, so the whole island
 * costs a headset about forty draw calls. For example `decorate(gameIsland(), { trees: 2, butterflies: 0 })`.
 */
export function decorate(
  island: Group,
  counts: { trees?: number; bushes?: number; flowers?: number; tufts?: number; butterflies?: number } = {},
): Group {
  const { trees = 3, bushes = 3, flowers = 6, tufts = 26, butterflies = 2 } = counts;
  // Laid out round the island's centre; `gameIsland()` is in the world's frame, with the player at the origin.
  const decor = new Group();
  decor.position.set(ISLAND_CENTRE.x, 0, ISLAND_CENTRE.z);
  for (const [x, z, size] of LAYOUT.trees.slice(0, trees)) {
    const tree = new Group();
    const parts = [part(new CylinderGeometry(0.06, 0.08, 0.5, 16), 'cocoa', [0, 0.25, 0])];
    for (const [a, b, c, r] of [
      [0, 0.72, 0, 0.36],
      [0.2, 0.6, 0.05, 0.26],
      [-0.2, 0.62, -0.02, 0.27],
    ] as const)
      parts.push(part(new SphereGeometry(r, 32, 16), '#47B04A', [a, b, c]));
    // The trunk and crown sway together above a still shadow, which would dip into the grass if it leaned.
    const swaying = prop(parts, { roughness: 0.6, clearcoat: 0.15 });
    tree.add(swaying);
    tree.position.set(x, 0, z);
    tree.scale.setScalar(size);
    tree.add(contactShadow(0.45));
    decor.add(tree);
    ambientMotion((time) => {
      swaying.rotation.z = sway(time, x, z, ambient.treeAmp, ambient.treePeriod);
    });
  }
  for (const [x, z, size] of LAYOUT.bushes.slice(0, bushes)) {
    const parts = [
      [0, 0, 0.2],
      [0.18, -0.03, 0.15],
      [-0.17, -0.04, 0.14],
    ].map(([a, c, r]) => part(new SphereGeometry(r, 24, 12), '#3FA247', [a, r * 0.7 + c, 0]));
    const bush = prop(parts, { roughness: 0.6 });
    bush.position.set(x, 0, z);
    bush.scale.setScalar(size * 1.4);
    decor.add(bush);
  }
  for (const [x, z, colour] of LAYOUT.flowers.slice(0, flowers)) {
    const parts = [part(new CylinderGeometry(0.012, 0.012, 0.16, 8), '#3E9E3A', [0, 0.08, 0])];
    for (let i = 0; i < 5; i++) {
      const a = (i / 5) * Math.PI * 2;
      parts.push(
        part(new SphereGeometry(0.035, 16, 8), colour, [Math.cos(a) * 0.04, 0.17, Math.sin(a) * 0.04], [1, 0.5, 1]),
      );
    }
    parts.push(part(new SphereGeometry(0.025, 16, 8), 'sunny', [0, 0.175, 0]));
    const flower = prop(parts);
    flower.position.set(x, 0, z);
    decor.add(flower);
    // Rooted at its stem: the head nods with the breeze, a little across it too.
    ambientMotion((time) => {
      flower.rotation.z = sway(time, x, z);
      flower.rotation.x = 0.35 * sway(time + 0.9, z, x);
    });
  }
  if (tufts > 0) {
    const blades = [0, 1, 2].map((k) =>
      part(new ConeGeometry(0.025, 0.12, 6), '#4FB045', [(k - 1) * 0.03, 0.06, 0], [1, 1, 1], [0, 0, (k - 1) * 0.35]),
    );
    const material = toyMaterial('#FFFFFF', { ground: true, roughness: 0.8 });
    material.vertexColors = true;
    // Every tuft is one instance of the same three blades: one draw call for the island's grass.
    const grass = new InstancedMesh(merge(blades), material, tufts);
    const spots = Array.from({ length: tufts }, (_, i) => {
      const [cx, cz] = LAYOUT.tufts[i % LAYOUT.tufts.length] as readonly [number, number];
      const a = i * 2.39996;
      const r = 0.12 + (i % 4) * 0.07;
      return { x: cx + Math.cos(a) * r, z: cz + Math.sin(a) * r, turn: a };
    });
    const place = new Object3D();
    const lean = (time: number) => {
      for (const [i, spot] of spots.entries()) {
        // Turned on its own, its root leaning with the breeze, the same way as every other: Rz(lean) after Ry(turn).
        place.position.set(spot.x, 0, spot.z);
        place.rotation.set(0, spot.turn, sway(time, spot.x, spot.z), 'ZYX');
        place.updateMatrix();
        grass.setMatrixAt(i, place.matrix);
      }
      grass.instanceMatrix.needsUpdate = true;
    };
    lean(0);
    grass.computeBoundingSphere();
    decor.add(grass);
    ambientMotion(lean);
  }
  for (const [i, [x, z, y, reach, upper, lower]] of LAYOUT.butterflies.slice(0, butterflies).entries()) {
    const flyer = butterfly([upper, lower]);
    decor.add(flyer.body);
    const phase = i * 2.4;
    ambientMotion((time) => {
      const at = flight(time, phase, reach);
      flyer.body.position.set(x + at.x, y + at.y, z + at.z);
      flyer.body.rotation.y = at.heading;
      const open = flap(time, phase * 3);
      flyer.right.rotation.z = open;
      flyer.left.rotation.z = -open;
    });
  }
  island.add(decor);
  return decor;
}

/** One part of a prop: a geometry in its own colour, placed in the prop. */
function part(
  geometry: BufferGeometry,
  colour: PaletteName | string,
  at: readonly [number, number, number],
  scale: readonly [number, number, number] = [1, 1, 1],
  turn: readonly [number, number, number] = [0, 0, 0],
): Mesh {
  const mesh = new Mesh(geometry, toyMaterial(colour));
  mesh.position.set(...at);
  mesh.scale.set(...scale);
  mesh.rotation.set(...turn);
  return mesh;
}

/** The parts placed and coloured into one geometry: each vertex carries its part's colour. */
function merge(parts: readonly Mesh[]): BufferGeometry {
  const pieces = parts.map((mesh) => {
    mesh.updateMatrix();
    const piece = mesh.geometry.clone().applyMatrix4(mesh.matrix);
    const { r, g, b } = (mesh.material as MeshPhysicalMaterial).color;
    const count = piece.getAttribute('position').count;
    piece.setAttribute('color', new Float32BufferAttribute(new Array(count).fill([r, g, b]).flat(), 3));
    return piece;
  });
  // An extruded shape has no index; merged geometries must all have one or none.
  const flat = pieces.some((piece) => piece.index === null);
  const merged = mergeGeometries(flat ? pieces.map((piece) => (piece.index === null ? piece : piece.toNonIndexed())) : pieces);
  if (merged === null) throw new Error('The decor could not be merged.');
  return merged;
}

/** A prop of several coloured parts as one mesh, so it costs one draw call, casting and taking shadows. */
function prop(parts: readonly Mesh[], look: { roughness?: number; clearcoat?: number } = {}): Mesh {
  const material = toyMaterial('#FFFFFF', { roughness: look.roughness });
  if (look.clearcoat !== undefined) material.clearcoat = look.clearcoat;
  material.vertexColors = true;
  const mesh = new Mesh(merge(parts), material);
  mesh.castShadow = true;
  mesh.receiveShadow = true;
  return mesh;
}

/** One wing's two lobes, the right one, hinged at x = 0 along the body; forward is +z. Broad and round, like a toy's. */
function wingShapes(): [Shape, Shape] {
  const upper = new Shape();
  upper.moveTo(0, 0.008);
  upper.bezierCurveTo(0.012, 0.045, 0.06, 0.07, 0.088, 0.052);
  upper.bezierCurveTo(0.108, 0.038, 0.1, 0.004, 0.072, -0.006);
  upper.bezierCurveTo(0.045, -0.014, 0.018, -0.01, 0, -0.004);
  const lower = new Shape();
  lower.moveTo(0, -0.004);
  lower.bezierCurveTo(0.022, -0.01, 0.062, -0.012, 0.066, -0.04);
  lower.bezierCurveTo(0.07, -0.068, 0.036, -0.082, 0.02, -0.062);
  lower.bezierCurveTo(0.01, -0.048, 0.004, -0.026, 0, -0.014);
  return [upper, lower];
}

/** A toy butterfly about 20 cm across: an ink body and two wings, each with a lobe of each colour; three meshes. */
function butterfly(wings: readonly [PaletteName, PaletteName]): { body: Mesh; left: Mesh; right: Mesh } {
  const parts = [
    part(new CapsuleGeometry(0.011, 0.06, 4, 12), 'ink', [0, 0, 0], [1, 1, 1], [Math.PI / 2, 0, 0]),
    part(new SphereGeometry(0.014, 16, 12), 'ink', [0, 0, 0.044]),
  ];
  for (const side of [-1, 1]) {
    parts.push(
      part(new CylinderGeometry(0.0018, 0.0018, 0.045, 6), 'ink', [side * 0.009, 0.016, 0.062], [1, 1, 1], [
        0.9,
        0,
        -side * 0.35,
      ]),
      part(new SphereGeometry(0.0045, 8, 6), 'ink', [side * 0.016, 0.031, 0.075]),
    );
  }
  const body = prop(parts);
  body.receiveShadow = false;
  const [upper, lower] = wingShapes();
  const wing = (left: boolean) => {
    const lobes = [upper, lower].map((shape, i) => {
      const geometry = new ShapeGeometry(shape, 12);
      // The shape is drawn flat in x and y, forward up: lay it flat with forward toward +z. The left wing is the right
      // one turned half round the body's axis, not scaled by -1, which would light it as if it faced away.
      geometry.rotateX(Math.PI / 2);
      if (left) geometry.rotateZ(Math.PI);
      return part(geometry, wings[i] ?? 'sunny', [0, 0, 0]);
    });
    const mesh = prop(lobes);
    (mesh.material as MeshPhysicalMaterial).side = DoubleSide;
    mesh.receiveShadow = false;
    body.add(mesh);
    return mesh;
  };
  return { body, right: wing(false), left: wing(true) };
}

/** How many bursts of confetti can fly at once; a new hit takes the oldest's place. */
const BURSTS = 4;
const CONFETTI_COLOURS: readonly PaletteName[] = ['berry', 'sunny', 'ocean', 'grape', 'tangerine', 'leaf'];
let confetti: { bursts: ({ x: number; y: number; z: number; time: number } | null)[]; next: number } | null = null;

/**
 * Confetti bursting from a hit at `at`, in the world's coordinates, now: on the game's own clock (`gameTime()`), so a
 * press handler, which is given no time, can call it. Up to four bursts fly at once, all one draw call.
 */
export function celebrate(at: { x: number; y: number; z: number }): void {
  if (confetti === null) confetti = confettiPool();
  confetti.bursts[confetti.next] = { x: at.x, y: at.y, z: at.z, time: gameTime() };
  confetti.next = (confetti.next + 1) % BURSTS;
}

function confettiPool(): NonNullable<typeof confetti> {
  const count = BURSTS * juice.pieces;
  // A thin box shows a face from every side, so the pieces need no double-sided shader of their own. Big enough to read
  // as paper from the laptop camera 4 m away, with a little light of their own so a piece seen edge-on is not dark.
  const material = toyMaterial('#FFFFFF');
  material.emissive = new Color('#FFFFFF');
  material.emissiveIntensity = 0.18;
  const mesh = new InstancedMesh(new BoxGeometry(0.075, 0.006, 0.048), material, count);
  // The pieces fly far from where the mesh began, so it is never culled by a stale bounding sphere.
  mesh.frustumCulled = false;
  mesh.castShadow = true;
  const colour = new Color();
  for (let i = 0; i < count; i++)
    mesh.setColorAt(i, colour.set(palette[CONFETTI_COLOURS[i % CONFETTI_COLOURS.length] ?? 'sunny']));
  const pool: NonNullable<typeof confetti> = { bursts: new Array(BURSTS).fill(null), next: 0 };
  const place = new Object3D();
  const move = (time: number) => {
    for (let b = 0; b < BURSTS; b++) {
      const burst = pool.bursts[b] ?? null;
      for (let i = 0; i < juice.pieces; i++) {
        const piece = burst === null ? null : confettiPiece(time - burst.time, i);
        if (burst === null || piece === null || piece.scale === 0) place.scale.setScalar(0);
        else {
          place.position.set(burst.x + piece.x, burst.y + piece.y, burst.z + piece.z);
          place.rotation.set(piece.rx, piece.ry, piece.rz);
          place.scale.setScalar(piece.scale);
        }
        place.updateMatrix();
        mesh.setMatrixAt(b * juice.pieces + i, place.matrix);
      }
    }
    mesh.instanceMatrix.needsUpdate = true;
  };
  move(0);
  gameIsland().add(mesh);
  ambientMotion(move);
  return pool;
}

/**
 * A big score sign on the raised tier at the back, facing the player: make it once, `const score = scoreSign('Score')`,
 * then call `score.show(points)` when the score changes; the board jumps each time, on the game's own clock. One label
 * and one number, dark on cream, readable from the far side of the island.
 */
export function scoreSign(label = 'Score'): { show(value: number | string): void } {
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 256;
  const context = canvas.getContext('2d');
  const texture = new CanvasTexture(canvas);
  texture.colorSpace = SRGBColorSpace;
  texture.anisotropy = 4;
  const draw = (value: number | string) => {
    if (context === null) return;
    const font = '"Arial Rounded MT Bold", ui-rounded, system-ui, sans-serif';
    context.fillStyle = '#FFF8EC';
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.fillStyle = palette.tangerine;
    context.font = `700 54px ${font}`;
    context.fillText(label, 256, 58);
    context.fillStyle = palette.ink;
    context.font = `900 148px ${font}`;
    context.fillText(String(value), 256, 166);
    texture.needsUpdate = true;
  };
  draw(0);
  const sign = new Group();
  sign.position.set(TIER.x, TIER.top, TIER.z + 0.2);
  // Two posts, the frame and its back as one mesh; the face as another, lit like the toys but never dark.
  const stand = prop([
    part(new CylinderGeometry(0.035, 0.04, 0.8, 12), 'cocoa', [-0.48, 0.4, -0.03]),
    part(new CylinderGeometry(0.035, 0.04, 0.8, 12), 'cocoa', [0.48, 0.4, -0.03]),
  ]);
  sign.add(stand);
  const board = new Group();
  board.position.y = 1.08;
  const frame = prop([part(new BoxGeometry(1.32, 0.72, 0.06), 'tangerine', [0, 0, 0])], { roughness: 0.4 });
  board.add(frame);
  const faceMaterial = toyMaterial('#FFFFFF', { roughness: 0.6 });
  faceMaterial.map = texture;
  faceMaterial.emissive = new Color('#FFFFFF');
  faceMaterial.emissiveMap = texture;
  faceMaterial.emissiveIntensity = 0.5;
  const face = new Mesh(new PlaneGeometry(1.2, 0.6), faceMaterial);
  // 2 cm proud of the frame: the template's near plane of 1 mm leaves about 2 mm of depth precision 6 m away.
  face.position.z = 0.05;
  board.add(face);
  sign.add(board);
  sign.add(contactShadow(0.6));
  gameIsland().add(sign);
  let changedAt = Number.NEGATIVE_INFINITY;
  ambientMotion((time) => {
    board.scale.setScalar(pulse(time - changedAt));
  });
  return {
    show(value) {
      draw(value);
      changedAt = gameTime();
    },
  };
}

/**
 * The toy kit (U8): friendly things to play with, each about 30 cm across in the palette's colours and one draw call,
 * so a game's things read at a glance rather than as bare spheres and cylinders. Each faces +z, toward the player.
 * Scale one with `.scale.setScalar(s)`, move it, and animate it whole with the curves in `src/motion.ts`.
 */

/** A round creature with big eyes, a nose and ears: a mole, a pet, a monster. Its feet are at y = 0. */
export function critter(colour: PaletteName | string = 'cocoa'): Mesh {
  const body = colour;
  const belly = '#F4E3CF';
  const parts = [
    part(new SphereGeometry(0.16, 32, 20), body, [0, 0.17, 0], [1, 1.08, 0.94]),
    // Well proud of the body, so the two surfaces meet at a clear angle rather than a jagged seam.
    part(new SphereGeometry(0.095, 24, 16), belly, [0, 0.13, 0.108], [1.08, 1, 0.64]),
    part(new SphereGeometry(0.048, 16, 12), body, [-0.105, 0.31, -0.01], [1, 1, 0.6]),
    part(new SphereGeometry(0.048, 16, 12), body, [0.105, 0.31, -0.01], [1, 1, 0.6]),
    part(new SphereGeometry(0.052, 20, 14), '#FFFFFF', [-0.058, 0.235, 0.118], [1, 1.12, 0.7]),
    part(new SphereGeometry(0.052, 20, 14), '#FFFFFF', [0.058, 0.235, 0.118], [1, 1.12, 0.7]),
    part(new SphereGeometry(0.03, 16, 12), 'ink', [-0.055, 0.232, 0.15], [1, 1.1, 0.6]),
    part(new SphereGeometry(0.03, 16, 12), 'ink', [0.061, 0.232, 0.15], [1, 1.1, 0.6]),
    part(new SphereGeometry(0.009, 8, 6), '#FFFFFF', [-0.047, 0.247, 0.167]),
    part(new SphereGeometry(0.009, 8, 6), '#FFFFFF', [0.069, 0.247, 0.167]),
    part(new SphereGeometry(0.024, 16, 12), 'berry', [0, 0.195, 0.158], [1.2, 0.9, 0.9]),
    part(new SphereGeometry(0.05, 16, 10), body, [-0.075, 0.02, 0.06], [1, 0.45, 1.3]),
    part(new SphereGeometry(0.05, 16, 10), body, [0.075, 0.02, 0.06], [1, 0.45, 1.3]),
  ];
  return prop(parts, { roughness: 0.42, clearcoat: 0.35 });
}

/** A bevelled five-pointed star with a happy face, standing upright, its centre at its origin. */
export function toyStar(colour: PaletteName | string = 'sunny'): Mesh {
  const outline = new Shape();
  for (let i = 0; i < 10; i++) {
    const r = i % 2 === 0 ? 0.15 : 0.068;
    const a = Math.PI / 2 + (i * Math.PI) / 5;
    if (i === 0) outline.moveTo(Math.cos(a) * r, Math.sin(a) * r);
    else outline.lineTo(Math.cos(a) * r, Math.sin(a) * r);
  }
  outline.closePath();
  const shape = new ExtrudeGeometry(outline, {
    depth: 0.04,
    bevelEnabled: true,
    bevelThickness: 0.02,
    bevelSize: 0.016,
    bevelSegments: 4,
    curveSegments: 1,
  });
  shape.center();
  const parts = [
    part(shape, colour, [0, 0, 0]),
    part(new SphereGeometry(0.014, 12, 8), 'ink', [-0.03, 0.01, 0.041], [0.8, 1.35, 0.5]),
    part(new SphereGeometry(0.014, 12, 8), 'ink', [0.03, 0.01, 0.041], [0.8, 1.35, 0.5]),
    part(new TorusGeometry(0.022, 0.0055, 8, 16, Math.PI), 'ink', [0, -0.018, 0.04], [1, 1, 0.5], [0, 0, Math.PI]),
  ];
  const star = prop(parts, { roughness: 0.28, clearcoat: 0.6 });
  // A star glows a little in its own colour, so it reads bright even turned from the sun.
  const material = star.material as MeshPhysicalMaterial;
  material.emissive = new Color(palette[colour as PaletteName] ?? colour);
  material.emissiveIntensity = 0.22;
  return star;
}

/** A shiny balloon with its knot and a curling string below, its centre at its origin. */
export function balloon(colour: PaletteName | string = 'berry'): Mesh {
  const curl = new CatmullRomCurve3([
    new Vector3(0, -0.17, 0),
    new Vector3(0.025, -0.28, 0.01),
    new Vector3(-0.02, -0.4, -0.01),
    new Vector3(0.015, -0.52, 0),
  ]);
  const parts = [
    part(new SphereGeometry(0.15, 32, 24), colour, [0, 0, 0], [0.92, 1.08, 0.92]),
    part(new ConeGeometry(0.026, 0.035, 12), colour, [0, -0.168, 0], [1, 1, 1], [Math.PI, 0, 0]),
    part(new TubeGeometry(curl, 24, 0.0035, 6), 'ink', [0, 0, 0]),
  ];
  return prop(parts, { roughness: 0.18, clearcoat: 0.8 });
}

/** A woven basket, open at the top, about 40 cm across; its base at y = 0. */
export function basket(colour: PaletteName | string = 'tangerine'): Mesh {
  const wall = [
    new Vector2(0.001, 0.0),
    new Vector2(0.15, 0.0),
    new Vector2(0.2, 0.08),
    new Vector2(0.215, 0.16),
    new Vector2(0.195, 0.16),
    new Vector2(0.18, 0.085),
    new Vector2(0.135, 0.02),
    new Vector2(0.001, 0.02),
  ];
  const bowl = new LatheGeometry(wall, 40);
  // Woven bands: every other ring of the weave a shade darker.
  const position = bowl.getAttribute('position');
  const light = new Color(palette[colour as PaletteName] ?? colour);
  const dark = light.clone().multiplyScalar(0.72);
  const bands: number[] = [];
  for (let i = 0; i < position.count; i++) {
    const y = position.getY(i);
    const a = Math.atan2(position.getZ(i), position.getX(i));
    const band = Math.floor(y / 0.032) + (Math.sin(a * 20) > 0 ? 1 : 0);
    const shade = band % 2 === 0 ? light : dark;
    bands.push(shade.r, shade.g, shade.b);
  }
  bowl.setAttribute('color', new Float32BufferAttribute(bands, 3));
  const rim = merge([part(new TorusGeometry(0.205, 0.016, 10, 48), 'cocoa', [0, 0.162, 0], [1, 1, 1], [Math.PI / 2, 0, 0])]);
  const merged = mergeGeometries([bowl.toNonIndexed(), rim.index === null ? rim : rim.toNonIndexed()]);
  if (merged === null) throw new Error('The basket could not be merged.');
  const material = toyMaterial('#FFFFFF', { roughness: 0.62 });
  material.vertexColors = true;
  const mesh = new Mesh(merged, material);
  mesh.castShadow = true;
  mesh.receiveShadow = true;
  return mesh;
}
