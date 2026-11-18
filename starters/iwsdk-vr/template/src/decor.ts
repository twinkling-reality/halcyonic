import {
  AdditiveBlending,
  BoxGeometry,
  BufferGeometry,
  CanvasTexture,
  CapsuleGeometry,
  CircleGeometry,
  CatmullRomCurve3,
  Color,
  ConeGeometry,
  CylinderGeometry,
  DoubleSide,
  ExtrudeGeometry,
  Float32BufferAttribute,
  Group,
  IcosahedronGeometry,
  InstancedMesh,
  LatheGeometry,
  Mesh,
  type MeshPhysicalMaterial,
  Object3D,
  OctahedronGeometry,
  PlaneGeometry,
  Points,
  PointsMaterial,
  Shape,
  ShapeGeometry,
  SphereGeometry,
  SRGBColorSpace,
  TorusGeometry,
  TubeGeometry,
  Vector2,
  Vector3,
} from 'three';
import { mergeGeometries, mergeVertices } from 'three/addons/utils/BufferGeometryUtils.js';
import { bothSidesUp, grassBlade } from './grass.js';
import { withGrain } from './surface.js';
import { wordsOn } from './legible.js';
import { ambientMotion, contactShadow, currentWorld, gameIsland, gameTime, ISLAND_CENTRE, TIER } from './look.js';
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

/** How many of each kind of dressing `decorate()` puts on the island; the same names in every world. */
export interface DecorCounts {
  trees?: number;
  bushes?: number;
  flowers?: number;
  tufts?: number;
  butterflies?: number;
}

/**
 * Dressing for the island (ART7), in the world `setupLook` was given (`src/worlds.ts`): trees, bushes, flowers and
 * grass tufts at its sides and back, and a couple of flyers, the middle left clear. In the meadow they are round trees,
 * bushes, flowers and butterflies; in the garden apple trees, pumpkins, sunflowers, butterflies and a fence; at the
 * seaside palms, rocks, shells and gulls; at night moon trees, crystals, glowing mushrooms and fireflies. `src/index.ts`
 * calls it for every new game. A breeze sways the plants and the flyers loop in front of the player, by themselves.
 * Each prop is one mesh, so the whole island costs a headset about forty draw calls. For example
 * `decorate(gameIsland(), { trees: 2, butterflies: 0 })`.
 */
export function decorate(island: Group, counts: DecorCounts = {}): Group {
  // Laid out round the island's centre; `gameIsland()` is in the world's frame, with the player at the origin.
  const decor = new Group();
  decor.position.set(ISLAND_CENTRE.x, 0, ISLAND_CENTRE.z);
  const dressing = currentWorld().dressing;
  if (dressing === 'garden') gardenDressing(decor, counts);
  else if (dressing === 'seaside') seasideDressing(decor, counts);
  else if (dressing === 'night') nightDressing(decor, counts);
  else meadowDressing(decor, counts);
  island.add(decor);
  return decor;
}

function meadowDressing(decor: Group, counts: DecorCounts): void {
  const { trees = 3, bushes = 3, flowers = 6, tufts = 26, butterflies = 2 } = counts;
  for (const [x, z, size] of LAYOUT.trees.slice(0, trees)) {
    const tree = new Group();
    const parts = [part(barkTrunk(0.06, 0.08, 0.5, x + z), 'cocoa', [0, 0, 0])];
    for (const [a, b, c, r] of [
      [0, 0.72, 0, 0.36],
      [0.2, 0.6, 0.05, 0.26],
      [-0.2, 0.62, -0.02, 0.27],
    ] as const)
      parts.push(part(foliage(r, x * 3.1 + a * 7 + z), '#47B04A', [a, b, c]));
    // The trunk and crown sway together above a still shadow, which would dip into the grass if it leaned.
    const swaying = prop(parts, { natural: true, roughness: 0.82 });
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
    ].map(([a, c, r]) => part(foliage(r, x * 2.3 + a * 5 + z), '#3FA247', [a, r * 0.7 + c, 0]));
    const bush = prop(parts, { natural: true, roughness: 0.82 });
    bush.position.set(x, 0, z);
    bush.scale.setScalar(size * 1.4);
    decor.add(bush);
  }
  for (const [x, z, colour] of LAYOUT.flowers.slice(0, flowers)) {
    // A slim stem and two leaves under six cupped petals round a seeded centre: a flower, not five beads.
    const parts = [
      part(new CylinderGeometry(0.005, 0.008, 0.17, 6), '#3E8E34', [0, 0.085, 0]),
      part(grassBlade(0.03), '#4C9E3C', [0, 0.02, 0], [1, 0.09, 1], [0, 0.6, 0.5]),
      part(grassBlade(0.03), '#4C9E3C', [0, 0.04, 0], [1, 0.07, 1], [0, 3.6, -0.55]),
    ];
    for (let i = 0; i < 6; i++)
      parts.push(part(petal(0.05, 0.03), colour, [0, 0.17, 0], [1, 1, 1], [0, (i / 6) * Math.PI * 2, 1.15], ));
    parts.push(part(foliage(0.016, x + z), '#8A5A1E', [0, 0.172, 0], [1, 0.6, 1]));
    const flower = prop(parts, { natural: true, roughness: 0.6 });
    bothSidesUp(flower.material as MeshPhysicalMaterial);
    flower.scale.setScalar(1.5);
    flower.position.set(x, 0, z);
    decor.add(flower);
    // Rooted at its stem: the head nods with the breeze, a little across it too.
    ambientMotion((time) => {
      flower.rotation.z = sway(time, x, z);
      flower.rotation.x = 0.35 * sway(time + 0.9, z, x);
    });
  }
  grassTufts(decor, tufts, '#4FB045', 1);
  flyers(decor, butterflies);
}

/** Grass: every tuft one instance of the same three blades, one draw call for the island, leaning with the breeze. */
function grassTufts(decor: Group, tufts: number, colour: string, height: number): void {
  if (tufts <= 0) return;
  // Each tuft a fan of real blades (src/grass.ts), not cones: taller than the carpet round it.
  const blades = [0, 1, 2, 3, 4].map((k) =>
    part(grassBlade(0.02), colour, [(k - 2) * 0.012, 0, 0], [1, 0.15 + (k % 2) * 0.04, 1], [0, k * 1.2, (k - 2) * 0.22]),
  );
  const material = bothSidesUp(toyMaterial('#FFFFFF', { ground: true, roughness: 0.8 }));
  material.vertexColors = true;
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
      place.scale.set(1, height, 1);
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

/** The meadow's and the garden's butterflies, looping in front of the player. */
function flyers(decor: Group, butterflies: number): void {
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
}

/**
 * A material whose parts glow in their own colours: the emissive light takes each vertex's colour, so one mesh of
 * many coloured parts glows each in its own, at one draw call.
 */
function glowing(material: MeshPhysicalMaterial, strength: number): MeshPhysicalMaterial {
  material.emissive = new Color('#FFFFFF');
  material.emissiveIntensity = strength;
  material.onBeforeCompile = (shader) => {
    shader.fragmentShader = shader.fragmentShader.replace(
      'vec3 totalEmissiveRadiance = emissive;',
      'vec3 totalEmissiveRadiance = emissive * vColor;',
    );
  };
  material.customProgramCacheKey = () => 'glowing-vertex-colours';
  return material;
}

/** The garden's fence, its two arcs at the island's sides behind the trees, white pickets, as one mesh. */
function fence(decor: Group): void {
  const parts: Mesh[] = [];
  const R = 2.8;
  for (const [from, to] of [
    [-2.85, -1.95],
    [-1.19, -0.29],
  ] as const) {
    const posts = Math.round(((to - from) * R) / 0.2);
    let previous: [number, number] | null = null;
    for (let i = 0; i <= posts; i++) {
      const a = from + ((to - from) * i) / posts;
      const x = Math.cos(a) * R;
      const z = Math.sin(a) * R;
      const turn = -a + Math.PI / 2;
      parts.push(part(new BoxGeometry(0.06, 0.34, 0.03), '#FFF8EC', [x, 0.17, z], [1, 1, 1], [0, turn, 0]));
      parts.push(
        part(new ConeGeometry(0.043, 0.06, 4), '#FFF8EC', [x, 0.37, z], [1, 1, 0.5], [0, turn + Math.PI / 4, 0]),
      );
      if (previous !== null) {
        const [px, pz] = previous;
        const length = Math.hypot(x - px, z - pz);
        const along = Math.atan2(-(z - pz), x - px);
        for (const y of [0.1, 0.24])
          parts.push(
            part(new BoxGeometry(length, 0.035, 0.02), '#F2E6D2', [(x + px) / 2, y, (z + pz) / 2 - 0.0], [1, 1, 1], [
              0,
              along,
              0,
            ]),
          );
      }
      previous = [x, z];
    }
  }
  decor.add(prop(parts, { roughness: 0.5 }));
}

/** A garden at golden hour: apple trees, pumpkins, sunflowers facing the player, grass, butterflies and a fence. */
function gardenDressing(decor: Group, counts: DecorCounts): void {
  const { trees = 3, bushes = 3, flowers = 6, tufts = 26, butterflies = 2 } = counts;
  for (const [x, z, size] of LAYOUT.trees.slice(0, trees)) {
    const tree = new Group();
    const parts = [part(barkTrunk(0.06, 0.085, 0.55, x + z), '#8A5232', [0, 0, 0])];
    for (const [a, b, c, r] of [
      [0, 0.78, 0, 0.38],
      [0.22, 0.64, 0.06, 0.27],
      [-0.22, 0.66, -0.02, 0.28],
    ] as const)
      parts.push(part(foliage(r, x * 3.1 + a * 7 + z), '#5DA43C', [a, b, c]));
    for (const [a, b, c] of [
      [0.16, 0.86, 0.3],
      [-0.2, 0.74, 0.27],
      [0.05, 0.6, 0.34],
      [0.34, 0.62, 0.17],
      [-0.33, 0.8, 0.1],
    ] as const)
      parts.push(part(new SphereGeometry(0.05, 16, 12), 'berry', [a, b, c]));
    const swaying = prop(parts, { natural: true, roughness: 0.7 });
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
    const parts: Mesh[] = [];
    for (let i = 0; i < 6; i++) {
      const a = (i / 6) * Math.PI * 2;
      parts.push(
        part(new SphereGeometry(0.1, 20, 12), 'tangerine', [Math.cos(a) * 0.075, 0.11, Math.sin(a) * 0.075], [
          0.9,
          1,
          1.2,
        ], [0, -a, 0]),
      );
    }
    parts.push(part(new CylinderGeometry(0.014, 0.02, 0.07, 8), '#5E7A2E', [0, 0.23, 0], [1, 1, 1], [0, 0, 0.3]));
    parts.push(part(new SphereGeometry(0.07, 16, 8), '#4E9A3A', [0.13, 0.02, 0.1], [1.4, 0.2, 1]));
    const pumpkin = prop(parts, { roughness: 0.4 });
    pumpkin.position.set(x, 0, z);
    pumpkin.scale.setScalar(size * 1.3);
    pumpkin.add(contactShadow(0.2));
    decor.add(pumpkin);
  }
  for (const [i, [x, z]] of LAYOUT.flowers.slice(0, flowers).entries()) {
    const tall = 0.4 + (i % 3) * 0.07;
    const parts = [
      part(new CylinderGeometry(0.012, 0.016, tall, 8), '#4E9A3A', [0, tall / 2, 0]),
      part(grassBlade(0.07), '#4E9A3A', [0, tall * 0.4, 0], [1, 0.16, 1], [0, 0.4, -0.9]),
      part(grassBlade(0.07), '#4E9A3A', [0, tall * 0.55, 0], [1, 0.14, 1], [0, 3.5, 0.9]),
      // The seed head: a domed disc of seeds, darker toward the middle, facing the player.
      part(foliage(0.05, x + z), '#5A3418', [0, tall + 0.02, 0.02], [1, 1, 0.35]),
    ];
    // Two rings of long petals round the head, the back ring offset, each cupped and darker where it joins.
    for (const [ring, depth, offset] of [
      [0, 0.0, 0],
      [1, -0.01, Math.PI / 14],
    ] as const)
      for (let k = 0; k < 14; k++) {
        const a = (k / 14) * Math.PI * 2 + offset;
        parts.push(
          part(petal(0.075 - ring * 0.008, 0.026), ring === 0 ? 'sunny' : '#F2A81E', [0, tall + 0.02, 0.025 + depth], [1, 1, 1], [
            0.12,
            0,
            a - Math.PI / 2,
          ]),
        );
      }
    const flower = prop(parts, { natural: true, roughness: 0.65 });
    bothSidesUp(flower.material as MeshPhysicalMaterial);
    flower.position.set(x, 0, z);
    decor.add(flower);
    ambientMotion((time) => {
      flower.rotation.z = 0.6 * sway(time, x, z);
    });
  }
  fence(decor);
  grassTufts(decor, tufts, '#5FB23F', 1.1);
  flyers(decor, butterflies);
}

/** A seaside: palms and a striped umbrella, rocks, shells and starfish on the sand, dune grass, and gulls. */
function seasideDressing(decor: Group, counts: DecorCounts): void {
  const { trees = 3, bushes = 3, flowers = 6, tufts = 26, butterflies = 2 } = counts;
  for (const [i, [x, z, size]] of LAYOUT.trees.slice(0, trees).entries()) {
    const tree = new Group();
    const parts: Mesh[] = [];
    if (i === 2) {
      // A beach umbrella, its stripes alternating, in the back corner.
      parts.push(part(new CylinderGeometry(0.018, 0.018, 1.0, 10), '#FFF8EC', [0, 0.5, 0], [1, 1, 1], [0, 0, 0.12]));
      const canopy = new ConeGeometry(0.55, 0.2, 16, 1, true).toNonIndexed();
      const colours: number[] = [];
      const stripe = [new Color(palette.berry), new Color('#FFF8EC')];
      const per = canopy.getAttribute('position').count / 16;
      for (let v = 0; v < canopy.getAttribute('position').count; v++) {
        const c = stripe[Math.floor(v / per) % 2] ?? stripe[0];
        if (c !== undefined) colours.push(c.r, c.g, c.b);
      }
      canopy.setAttribute('color', new Float32BufferAttribute(colours, 3));
      const umbrella = prop(parts);
      const top = new Mesh(canopy, toyMaterial('#FFFFFF', { roughness: 0.5 }));
      (top.material as MeshPhysicalMaterial).vertexColors = true;
      (top.material as MeshPhysicalMaterial).side = DoubleSide;
      top.position.set(-0.06, 1.0, 0);
      top.rotation.z = 0.12;
      top.castShadow = true;
      tree.add(umbrella, top);
      tree.position.set(x, 0, z);
      tree.scale.setScalar(size * 1.25);
      tree.add(contactShadow(0.5));
      decor.add(tree);
      continue;
    }
    // A palm: a curving ringed trunk, fronds drooping all round, and coconuts.
    const lean = i === 0 ? 1 : -1;
    const curve = new CatmullRomCurve3(
      [0, 0.4, 0.8, 1.2].map((y) => new Vector3(lean * 0.18 * (y / 1.2) ** 2, y, 0)),
    );
    const tip = curve.getPoint(1);
    // One smooth trunk, its rings alternating in two browns along its length.
    const trunk = new TubeGeometry(curve, 24, 0.055, 12, false);
    const rings = [new Color('#B9875A'), new Color('#9A6A42')];
    const ringColours: number[] = [];
    for (let k = 0; k <= 24; k++)
      for (let v = 0; v <= 12; v++) {
        const c = rings[Math.floor(k / 3) % 2] ?? rings[0];
        if (c !== undefined) ringColours.push(c.r, c.g, c.b);
      }
    trunk.setAttribute('color', new Float32BufferAttribute(ringColours, 3));
    // Each ring of the palm's bark swells a little at its base, so the trunk reads as stacked rings, not a tube.
    const swell = trunk.getAttribute('position');
    const along = trunk.getAttribute('normal');
    for (let k = 0; k <= 24; k++)
      for (let v = 0; v <= 12; v++) {
        const i = k * 13 + v;
        const bump = 0.012 * (1 - (k % 3) / 3) + 0.003 * Math.sin(v * 2.1 + k);
        swell.setXYZ(i, swell.getX(i) + along.getX(i) * bump, swell.getY(i) + along.getY(i) * bump, swell.getZ(i) + along.getZ(i) * bump);
      }
    trunk.computeVertexNormals();
    // Bark with the relief of bark, ringed, matte: not a smooth tube.
    const bark = new Mesh(trunk, withGrain(toyMaterial('#FFFFFF', { ground: true, roughness: 0.88 }), 2, 0.9));
    (bark.material as MeshPhysicalMaterial).vertexColors = true;
    bark.castShadow = true;
    // Fronds arching out all round and drooping, each a rib with leaflets down both sides: a palm, not paper.
    const crown: Mesh[] = [];
    for (let f = 0; f < 11; f++) {
      const a = (f / 11) * Math.PI * 2 + 0.3;
      crown.push(part(frond(0.82 - (f % 3) * 0.08), f % 2 === 0 ? '#3E9A4E' : '#4FAE58', [tip.x, tip.y, tip.z], [1, 1, 1], [0, -a, 0.18 + (f % 3) * 0.08]));
    }
    const fronds = prop(crown, { natural: true, roughness: 0.7 });
    bothSidesUp(fronds.material as MeshPhysicalMaterial);
    for (const [a, b] of [
      [0.05, 0.05],
      [-0.05, 0.03],
      [0, -0.05],
    ] as const)
      parts.push(part(new SphereGeometry(0.045, 12, 10), 'cocoa', [tip.x + a, tip.y - 0.06, b]));
    const swaying = prop(parts, { roughness: 0.55, clearcoat: 0.15 });
    swaying.add(bark, fronds);
    tree.add(swaying);
    tree.position.set(x, 0, z);
    tree.scale.setScalar(size * 1.15);
    tree.add(contactShadow(0.4));
    decor.add(tree);
    ambientMotion((time) => {
      swaying.rotation.z = sway(time, x, z, ambient.treeAmp, ambient.treePeriod);
    });
  }
  for (const [i, [x, z, size]] of LAYOUT.bushes.slice(0, bushes).entries()) {
    const parts = [
      part(foliage(0.16, i * 3 + 1), '#9C988F', [0, 0.06, 0], [1.25, 0.62, 1]),
      part(foliage(0.1, i * 3 + 2), '#857F76', [0.17, 0.04, 0.05], [1, 0.7, 1], [0.4, i, 0]),
      part(foliage(0.07, i * 3 + 3), '#AFA99E', [-0.14, 0.03, 0.08], [1, 0.75, 1], [0, i * 2, 0.3]),
    ];
    // Weathered stone: lumpy, matte, with the relief of rock, settled into the sand.
    const rock = prop(parts, { natural: true, roughness: 0.92 });
    rock.position.set(x, 0, z);
    rock.scale.setScalar(size * 1.2);
    rock.add(contactShadow(0.3));
    decor.add(rock);
  }
  for (const [i, [x, z, colour]] of LAYOUT.flowers.slice(0, flowers).entries()) {
    let thing: Mesh;
    if (i % 2 === 0) {
      const outline = new Shape();
      for (let k = 0; k < 10; k++) {
        const r = k % 2 === 0 ? 0.075 : 0.032;
        const a = Math.PI / 2 + (k * Math.PI) / 5;
        if (k === 0) outline.moveTo(Math.cos(a) * r, Math.sin(a) * r);
        else outline.lineTo(Math.cos(a) * r, Math.sin(a) * r);
      }
      const shape = new ExtrudeGeometry(outline, {
        depth: 0.012,
        bevelEnabled: true,
        bevelThickness: 0.008,
        bevelSize: 0.008,
        bevelSegments: 2,
      });
      shape.rotateX(-Math.PI / 2);
      // Each arm rises along its middle and is studded with small bumps, as a starfish is; matte, never plastic.
      const top = shape.getAttribute('position');
      for (let k = 0; k < top.count; k++) {
        const px = top.getX(k);
        const pz = top.getZ(k);
        if (top.getY(k) <= 0.01) continue;
        const r = Math.hypot(px, pz);
        const a = Math.atan2(pz, px) - Math.PI / 2;
        const ridge = Math.max(0, Math.cos(a * 5)) ** 3;
        const bumps = 0.5 + 0.5 * Math.sin(px * 260) * Math.sin(pz * 260);
        top.setY(k, top.getY(k) + 0.012 * ridge * (1 - r / 0.08) + 0.003 * bumps);
      }
      shape.computeVertexNormals();
      thing = prop([part(shape, i % 4 === 0 ? '#D9783A' : '#C8506A', [0, 0.012, 0])], { natural: true, roughness: 0.78 });
      thing.rotation.y = i * 0.7;
    } else {
      const parts = [part(new SphereGeometry(0.06, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2), '#FFE3D3', [0, 0, 0], [1, 0.8, 1.2])];
      for (let k = -2; k <= 2; k++)
        parts.push(
          part(new CylinderGeometry(0.004, 0.004, 0.13, 6), colour === 'grape' ? '#E6A0B8' : '#F2B9A0', [k * 0.02, 0.03, 0], [
            1,
            1,
            1,
          ], [Math.PI / 2, 0, k * 0.25]),
        );
      thing = prop(parts, { natural: true, roughness: 0.6 });
      thing.rotation.y = i;
    }
    thing.scale.setScalar(1.6);
    thing.position.set(x, 0, z);
    decor.add(thing);
  }
  grassTufts(decor, Math.round(tufts * 0.6), '#C8C46A', 1.6);
  // Gulls: white birds with grey wings, wheeling high over the sides and back.
  for (const [i, [x, z, y, reach]] of LAYOUT.butterflies.slice(0, butterflies).entries()) {
    const body = prop([
      part(new CapsuleGeometry(0.03, 0.12, 4, 12), '#FFFFFF', [0, 0, 0], [1, 1, 1], [Math.PI / 2, 0, 0]),
      part(new SphereGeometry(0.032, 16, 12), '#FFFFFF', [0, 0.015, 0.1]),
      part(new ConeGeometry(0.012, 0.04, 8), 'sunny', [0, 0.012, 0.145], [1, 1, 1], [Math.PI / 2, 0, 0]),
    ]);
    const wing = (side: number) => {
      const lobe = new Shape();
      lobe.moveTo(0, 0.03);
      lobe.bezierCurveTo(0.1, 0.05, 0.2, 0.02, 0.26, -0.03);
      lobe.bezierCurveTo(0.16, -0.02, 0.08, -0.03, 0, -0.03);
      const geometry = new ShapeGeometry(lobe, 10);
      geometry.rotateX(Math.PI / 2);
      if (side < 0) geometry.rotateZ(Math.PI);
      const mesh = prop([part(geometry, '#C7CDD6', [0, 0, 0])]);
      (mesh.material as MeshPhysicalMaterial).side = DoubleSide;
      body.add(mesh);
      return mesh;
    };
    const right = wing(1);
    const left = wing(-1);
    decor.add(body);
    const phase = i * 2.4;
    ambientMotion((time) => {
      const at = flight(time * 0.6, phase, reach * 2.2);
      body.position.set(x * 1.4 + at.x, y + 1.3 + at.y, z - 0.6 + at.z);
      body.rotation.y = at.heading;
      const open = 0.35 * flap(time * 0.35, phase * 3);
      right.rotation.z = open;
      left.rotation.z = -open;
    });
  }
}

/** A starry night: moon trees, glowing crystals and mushrooms, teal grass and swarms of fireflies. */
function nightDressing(decor: Group, counts: DecorCounts): void {
  const { trees = 3, bushes = 3, flowers = 6, tufts = 26, butterflies = 2 } = counts;
  for (const [x, z, size] of LAYOUT.trees.slice(0, trees)) {
    const tree = new Group();
    const parts = [part(barkTrunk(0.05, 0.08, 0.6, x + z), '#4A3B6B', [0, 0, 0])];
    for (const [a, b, c, r, colour] of [
      [0, 0.86, 0, 0.3, '#24305A'],
      [0.18, 0.66, 0.04, 0.22, '#1E2A50'],
      [-0.17, 0.7, -0.02, 0.23, '#283664'],
      [0.02, 1.1, 0, 0.17, '#2C3A6C'],
    ] as const)
      parts.push(part(foliage(r, x * 3.1 + a * 7 + z), colour, [a, b, c]));
    const swaying = prop(parts, { natural: true, roughness: 0.6 });
    tree.add(swaying);
    tree.position.set(x, 0, z);
    tree.scale.setScalar(size);
    tree.add(contactShadow(0.4));
    decor.add(tree);
    ambientMotion((time) => {
      swaying.rotation.z = sway(time, x, z, ambient.treeAmp, ambient.treePeriod);
    });
  }
  for (const [i, [x, z, size]] of LAYOUT.bushes.slice(0, bushes).entries()) {
    // A cluster of six-sided shards leaning out from one root, each brighter toward its tip.
    const parts = [
      part(shard(0.045, 0.28), '#7E6BD6', [0, 0, 0], [1, 1, 1], [0, i, 0.08]),
      part(shard(0.035, 0.2), '#5C7FE0', [0.06, 0, 0.02], [1, 1, 1], [0, 0.4 + i, -0.42]),
      part(shard(0.03, 0.17), '#A58CF0', [-0.06, 0, 0.03], [1, 1, 1], [0, i * 2, 0.5]),
      part(shard(0.025, 0.12), '#6A9AF0', [0.02, 0, -0.06], [1, 1, 1], [0.45, i, 0.1]),
      part(shard(0.02, 0.09), '#8C78E6', [-0.03, 0, 0.07], [1, 1, 1], [-0.5, i, -0.2]),
    ];
    const crystal = prop(parts, { roughness: 0.12, clearcoat: 1 });
    glowing(crystal.material as MeshPhysicalMaterial, 0.4);
    crystal.position.set(x, 0, z);
    crystal.scale.setScalar(size * 1.3);
    crystal.add(contactShadow(0.25));
    decor.add(crystal);
  }
  // Muted caps over gills that glow, as night mushrooms do: the light comes from underneath, never a candy colour.
  const caps: readonly (readonly [string, string])[] = [
    ['#6E4E72', '#7FF2D8'],
    ['#4E5A82', '#9CC8FF'],
    ['#7A5A44', '#FFD78A'],
    ['#5A4A78', '#C4A2FF'],
    ['#6A6A52', '#A8FFB2'],
    ['#7A4A5A', '#FFB0D0'],
  ];
  for (const [i, [x, z]] of LAYOUT.flowers.slice(0, flowers).entries()) {
    const tall = 0.07 + (i % 3) * 0.03;
    const [cap, gills] = caps[i % caps.length] ?? ['#6E4E72', '#7FF2D8'];
    const parts = [
      part(new CylinderGeometry(0.011, 0.017, tall, 10), '#B8AFC4', [0, tall / 2, 0]),
      part(new CircleGeometry(0.046, 24).rotateX(Math.PI / 2), gills, [0, tall + 0.003, 0]),
      part(capShape(0.055, 0.042), cap, [0, tall - 0.002, 0]),
    ];
    const mushroom = prop(parts, { natural: true, roughness: 0.55 });
    bothSidesUp(mushroom.material as MeshPhysicalMaterial);
    glowing(mushroom.material as MeshPhysicalMaterial, 0.55);
    mushroom.position.set(x, 0, z);
    mushroom.scale.setScalar(2);
    decor.add(mushroom);
    ambientMotion((time) => {
      mushroom.rotation.z = 0.4 * sway(time, x, z);
    });
  }
  grassTufts(decor, tufts, '#3C9A8A', 1);
  // Fireflies: a swarm round each home, drifting and blinking slowly; one draw call for them all.
  const swarm = 7;
  const homes = LAYOUT.butterflies.slice(0, butterflies);
  if (homes.length > 0) {
    const material = glowing(toyMaterial('#FFFFFF', { roughness: 0.4 }), 1.4);
    material.vertexColors = true;
    const flies = new InstancedMesh(merge([part(new SphereGeometry(0.022, 10, 8), '#EAFF86', [0, 0, 0])]), material, homes.length * swarm);
    flies.frustumCulled = false;
    const place = new Object3D();
    const move = (time: number) => {
      for (const [h, [x, z, y]] of homes.entries())
        for (let k = 0; k < swarm; k++) {
          const a = k * 2.39996 + h;
          const wander = time * 0.7 + k * 0.9 + h * 3.1;
          place.position.set(
            x + Math.cos(a) * 0.5 + 0.18 * Math.sin(wander),
            y + 0.25 * Math.sin(a * 1.3) + 0.12 * Math.sin(wander * 1.7),
            z + Math.sin(a) * 0.5 + 0.12 * Math.cos(wander * 1.3),
          );
          place.scale.setScalar(0.6 + 0.4 * (0.5 + 0.5 * Math.sin(time * 2.2 + k * 1.7 + h)));
          place.updateMatrix();
          flies.setMatrixAt(h * swarm + k, place.matrix);
        }
      flies.instanceMatrix.needsUpdate = true;
    };
    // Each firefly's own glow: a tight soft halo round it, added light, as a true emitter has (CAST, seat31).
    const glowPositions = new Float32Array(homes.length * swarm * 3);
    const glowGeometry = new BufferGeometry();
    glowGeometry.setAttribute('position', new Float32BufferAttribute(glowPositions, 3));
    const halo = new Points(
      glowGeometry,
      new PointsMaterial({
        size: 0.14,
        map: fireflyGlow(),
        color: '#EAFF86',
        transparent: true,
        depthWrite: false,
        blending: AdditiveBlending,
        fog: false,
      }),
    );
    halo.frustumCulled = false;
    const shine = (time: number) => {
      move(time);
      for (let k = 0; k < homes.length * swarm; k++) {
        flies.getMatrixAt(k, place.matrix);
        place.position.setFromMatrixPosition(place.matrix);
        glowPositions.set([place.position.x, place.position.y, place.position.z], k * 3);
      }
      glowGeometry.getAttribute('position').needsUpdate = true;
    };
    shine(0);
    decor.add(flies, halo);
    ambientMotion(shine);
  }
}

/**
 * A crown of leaves, not a ball: a sphere of `r` pushed in and out by smooth noise (its own `seed`), so its outline
 * breaks into clumps, darker underneath and inside where the leaves shade each other.
 */
function foliage(r: number, seed: number): BufferGeometry {
  const base = new IcosahedronGeometry(r, 3);
  base.deleteAttribute('normal');
  base.deleteAttribute('uv');
  const shape = mergeVertices(base, 1e-5);
  const position = shape.getAttribute('position');
  const uv: number[] = [];
  const shade: number[] = [];
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i) / r;
    const y = position.getY(i) / r;
    const z = position.getZ(i) / r;
    const clumps =
      0.14 * Math.sin(5.1 * x + seed) * Math.sin(4.3 * y + 1.7 * seed) * Math.sin(4.7 * z + 0.6 * seed) +
      0.06 * Math.sin(11.3 * x + 2.1 * seed) * Math.sin(12.7 * z + seed) +
      0.03 * Math.sin(23.0 * y + 3.3 * seed);
    const k = 1 + clumps;
    position.setXYZ(i, x * r * k, y * r * k, z * r * k);
    uv.push(Math.atan2(z, x) / (Math.PI * 2) + 0.5, Math.asin(Math.max(-1, Math.min(1, y))) / Math.PI + 0.5);
    // Underneath and in the hollows the leaves shade each other.
    const under = Math.min(1, Math.max(0, (y + 1) / 1.6));
    shade.push((0.5 + 0.5 * under) * (0.82 + 1.3 * Math.max(-0.14, Math.min(0.14, clumps))));
  }
  shape.setAttribute('uv', new Float32BufferAttribute(uv, 2));
  shape.setAttribute('shade', new Float32BufferAttribute(shade, 1));
  shape.computeVertexNormals();
  return shape;
}

/**
 * A petal: a slim leaf of colour, rounded at its tip, pinched at its base and cupped, pointing up the y axis from the
 * origin; darker where it joins the flower. Lay it out with `part`'s turn.
 */
function petal(length: number, width: number): BufferGeometry {
  const shape = new PlaneGeometry(width, length, 2, 5).translate(0, length / 2, 0);
  const position = shape.getAttribute('position');
  const shade: number[] = [];
  for (let i = 0; i < position.count; i++) {
    const t = position.getY(i) / length;
    const across = position.getX(i) / (width / 2);
    // Widest two thirds along, pinched to the base, rounded to the tip, and cupped across.
    position.setX(i, position.getX(i) * Math.sin(Math.PI * Math.min(1, t * 0.75 + 0.06)) ** 0.8);
    position.setZ(i, width * 0.28 * (1 - across * across) + length * 0.12 * t * t);
    shade.push(0.62 + 0.38 * Math.min(1, t * 1.6));
  }
  shape.setAttribute('shade', new Float32BufferAttribute(shade, 1));
  shape.computeVertexNormals();
  return shape;
}

/** A mushroom's cap as one turned surface: a dome over a soft rim, open underneath. `r` its radius, `h` its height. */
function capShape(r: number, h: number): BufferGeometry {
  const profile = [
    new Vector2(0.001, h),
    new Vector2(r * 0.45, h * 0.94),
    new Vector2(r * 0.8, h * 0.7),
    new Vector2(r, h * 0.3),
    new Vector2(r * 0.96, h * 0.08),
    new Vector2(r * 0.84, h * 0.02),
  ];
  const cap = new LatheGeometry(profile, 28);
  const position = cap.getAttribute('position');
  const shade: number[] = [];
  for (let i = 0; i < position.count; i++) shade.push(0.7 + 0.3 * (position.getY(i) / h));
  cap.setAttribute('shade', new Float32BufferAttribute(shade, 1));
  return cap;
}

/** A crystal shard: a six-sided column under a pointed tip, brighter toward the tip as light gathers in it. */
function shard(r: number, h: number): BufferGeometry {
  const column = new CylinderGeometry(r, r * 1.08, h, 6, 1).translate(0, h / 2, 0);
  const tip = new ConeGeometry(r, h * 0.42, 6, 1).translate(0, h + h * 0.21, 0);
  const merged = mergeGeometries([column.toNonIndexed(), tip.toNonIndexed()]);
  if (merged === null) throw new Error('A crystal could not be made.');
  const position = merged.getAttribute('position');
  const shade: number[] = [];
  for (let i = 0; i < position.count; i++) shade.push(0.55 + 0.6 * Math.min(1, position.getY(i) / (h * 1.42)));
  merged.setAttribute('shade', new Float32BufferAttribute(shade, 1));
  merged.computeVertexNormals();
  return merged;
}

/**
 * A palm frond: a rib arching out along +x and drooping toward its tip, with narrow leaflets down both sides that
 * shorten toward the tip and hang a little, darker near the rib. `length` in metres.
 */
function frond(length: number): BufferGeometry {
  const positions: number[] = [];
  const shade: number[] = [];
  const rib = (t: number) => new Vector3(t * length, 0.12 * length * Math.sin(Math.PI * t * 0.5) - 0.55 * length * t * t, 0);
  const count = 22;
  for (let k = 1; k <= count; k++) {
    const t = k / (count + 1);
    const at = rib(t);
    const along = rib(t + 0.02).sub(at).normalize();
    const reach = 0.26 * length * (1 - 0.6 * t);
    for (const side of [1, -1]) {
      // A leaflet: a slim quad from the rib, out to the side, swept toward the tip and hanging a little.
      const out = new Vector3(along.x * 0.45, -0.35, side).normalize().multiplyScalar(reach);
      const w = along.clone().multiplyScalar(0.022 * length);
      const a = at.clone().sub(w);
      const b = at.clone().add(w);
      const c = at.clone().add(out).add(w.clone().multiplyScalar(0.3));
      const d = at.clone().add(out).sub(w.clone().multiplyScalar(0.3));
      for (const p of [a, b, c, a, c, d]) positions.push(p.x, p.y, p.z);
      for (const s of [0.6, 0.6, 1, 0.6, 1, 1]) shade.push(s);
    }
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new Float32BufferAttribute(new Array((positions.length / 3) * 2).fill(0), 2));
  geometry.setAttribute('shade', new Float32BufferAttribute(shade, 1));
  geometry.computeVertexNormals();
  return geometry;
}

/**
 * A trunk with the bark of one: a tapering column ridged up its length and knotted here and there, `rt` and `rb` its
 * radii at the top and the base, `h` its height, standing on y = 0.
 */
function barkTrunk(rt: number, rb: number, h: number, seed: number): BufferGeometry {
  const trunk = new CylinderGeometry(rt, rb, h, 20, 8).translate(0, h / 2, 0);
  const position = trunk.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i);
    const y = position.getY(i);
    const z = position.getZ(i);
    const r = Math.hypot(x, z);
    if (r < 1e-6) continue;
    const a = Math.atan2(z, x);
    const ridges = 0.1 * Math.sin(a * 9 + seed + y * 2) + 0.05 * Math.sin(a * 23 + seed * 2) + 0.06 * Math.sin(y * 31 + a * 3 + seed);
    position.setX(i, x * (1 + ridges));
    position.setZ(i, z * (1 + ridges));
  }
  trunk.computeVertexNormals();
  return trunk;
}

let fireflyPicture: CanvasTexture | null = null;

/** A firefly's halo: bright at its heart and gone within a few of its own widths. */
function fireflyGlow(): CanvasTexture {
  if (fireflyPicture !== null) return fireflyPicture;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 64;
  const context = canvas.getContext('2d');
  if (context !== null) {
    const gradient = context.createRadialGradient(32, 32, 0, 32, 32, 32);
    gradient.addColorStop(0, 'rgba(255,255,255,1)');
    gradient.addColorStop(0.18, 'rgba(255,255,255,0.75)');
    gradient.addColorStop(0.45, 'rgba(255,255,255,0.18)');
    gradient.addColorStop(1, 'rgba(255,255,255,0)');
    context.fillStyle = gradient;
    context.fillRect(0, 0, 64, 64);
  }
  fireflyPicture = new CanvasTexture(canvas);
  fireflyPicture.colorSpace = SRGBColorSpace;
  return fireflyPicture;
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
    // A part's own shade (a crown of leaves darker underneath) darkens its colour vertex by vertex.
    const shade = piece.getAttribute('shade');
    const colours: number[] = [];
    for (let i = 0; i < count; i++) {
      const s = shade === undefined ? 1 : shade.getX(i);
      colours.push(r * s, g * s, b * s);
    }
    piece.setAttribute('color', new Float32BufferAttribute(colours, 3));
    if (shade !== undefined) piece.deleteAttribute('shade');
    return piece;
  });
  // An extruded shape has no index; merged geometries must all have one or none.
  const flat = pieces.some((piece) => piece.index === null);
  const merged = mergeGeometries(flat ? pieces.map((piece) => (piece.index === null ? piece : piece.toNonIndexed())) : pieces);
  if (merged === null) throw new Error('The decor could not be merged.');
  return merged;
}

/** A prop of several coloured parts as one mesh, so it costs one draw call, casting and taking shadows. */
function prop(parts: readonly Mesh[], look: { roughness?: number; clearcoat?: number; natural?: boolean } = {}): Mesh {
  // A natural prop (a tree, a bush) is matte with a leaf's fine relief; a toy keeps its gloss.
  const material = toyMaterial('#FFFFFF', { roughness: look.roughness, ground: look.natural });
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
  // Its smaller words, the label's 54 px on a 512 px canvas 1.2 m wide, for the computer's check of a laptop's view.
  wordsOn(face, (54 * 1.2) / 512, 'sign');
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
