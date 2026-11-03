import {
  CapsuleGeometry,
  ConeGeometry,
  CylinderGeometry,
  DoubleSide,
  Group,
  Mesh,
  Shape,
  ShapeGeometry,
  SphereGeometry,
} from 'three';
import { ambientMotion, contactShadow } from './look.js';
import { ambient, flap, flight, sway } from './motion.js';
import { type PaletteName, toyMaterial } from './palette.js';

/**
 * Optional dressing for the island (ART7): round trees, bushes, flowers and grass tufts, in clusters at its sides and
 * back, the foreground left clear, and a couple of butterflies. It is what makes a game look made rather than started;
 * the game's own props bring in the palette's colours. A breeze sways the flowers, grass and trees, and the butterflies
 * loop in front of the player, by themselves. For example
 * `decorate(gameIsland(), { trees: 3, bushes: 3, flowers: 6, tufts: 26, butterflies: 2 })`.
 */
export function decorate(
  island: Group,
  counts: { trees?: number; bushes?: number; flowers?: number; tufts?: number; butterflies?: number } = {},
): Group {
  const { trees = 3, bushes = 3, flowers = 6, tufts = 26, butterflies = 2 } = counts;
  const decor = new Group();
  const shadowed = (group: Group) =>
    group.traverse((part) => {
      if (part instanceof Mesh) {
        part.castShadow = true;
        part.receiveShadow = true;
      }
    });
  for (const [x, z, size] of [
    [-2.2, -1.2, 1.1],
    [2.3, -1.0, 0.9],
    [-1.15, -2.05, 0.7],
  ].slice(0, trees)) {
    const tree = new Group();
    // The trunk and crown sway together above a still shadow, which would dip into the grass if it leaned.
    const swaying = new Group();
    const trunk = new Mesh(new CylinderGeometry(0.06, 0.08, 0.5, 16), toyMaterial('cocoa'));
    trunk.position.y = 0.25;
    swaying.add(trunk);
    for (const [a, b, c, r] of [
      [0, 0.72, 0, 0.36],
      [0.2, 0.6, 0.05, 0.26],
      [-0.2, 0.62, -0.02, 0.27],
    ]) {
      const crown = new Mesh(new SphereGeometry(r, 32, 16), toyMaterial('#47B04A'));
      crown.material.roughness = 0.6;
      if ('clearcoat' in crown.material) crown.material.clearcoat = 0.15;
      crown.position.set(a, b, c);
      swaying.add(crown);
    }
    shadowed(swaying);
    tree.add(swaying);
    tree.position.set(x, 0, z);
    tree.scale.setScalar(size);
    tree.add(contactShadow(0.45));
    decor.add(tree);
    ambientMotion((time) => {
      swaying.rotation.z = sway(time, x, z, ambient.treeAmp, ambient.treePeriod);
    });
  }
  for (const [x, z, size] of [
    [1.9, 1.6, 0.8],
    [-2.4, 1.2, 0.7],
    [0.9, -2.4, 0.6],
  ].slice(0, bushes)) {
    const bush = new Group();
    for (const [a, c, r] of [
      [0, 0, 0.2],
      [0.18, -0.03, 0.15],
      [-0.17, -0.04, 0.14],
    ]) {
      const ball = new Mesh(new SphereGeometry(r, 24, 12), toyMaterial('#3FA247'));
      ball.material.roughness = 0.6;
      ball.position.set(a, r * 0.7 + c, 0);
      bush.add(ball);
    }
    shadowed(bush);
    bush.position.set(x, 0, z);
    bush.scale.setScalar(size * 1.4);
    decor.add(bush);
  }
  const flowerSpots: [number, number, PaletteName][] = [
    [-1.7, 1.55, 'berry'],
    [-1.5, 1.85, 'sunny'],
    [-1.2, 1.6, 'berry'],
    [1.75, 1.15, 'grape'],
    [1.5, 1.4, 'ocean'],
    [2.0, -0.3, 'tangerine'],
    [1.75, -0.05, 'sunny'],
    [-2.0, -0.4, 'grape'],
  ];
  for (const [x, z, colour] of flowerSpots.slice(0, flowers)) {
    const flower = new Group();
    const stem = new Mesh(new CylinderGeometry(0.012, 0.012, 0.16, 8), toyMaterial('#3E9E3A'));
    stem.position.y = 0.08;
    flower.add(stem);
    for (let i = 0; i < 5; i++) {
      const petal = new Mesh(new SphereGeometry(0.035, 16, 8), toyMaterial(colour));
      const a = (i / 5) * Math.PI * 2;
      petal.position.set(Math.cos(a) * 0.04, 0.17, Math.sin(a) * 0.04);
      petal.scale.set(1, 0.5, 1);
      flower.add(petal);
    }
    const centre = new Mesh(new SphereGeometry(0.025, 16, 8), toyMaterial('sunny'));
    centre.position.y = 0.175;
    flower.add(centre);
    shadowed(flower);
    flower.position.set(x, 0, z);
    decor.add(flower);
    // Rooted at its stem: the head nods with the breeze, a little across it too.
    ambientMotion((time) => {
      flower.rotation.z = sway(time, x, z);
      flower.rotation.x = 0.35 * sway(time + 0.9, z, x);
    });
  }
  const clusters = [
    [-1.9, -0.6],
    [1.9, -0.2],
    [-1.3, 1.7],
    [1.6, 1.3],
    [-0.6, -1.5],
    [0.9, -1.7],
  ];
  for (let i = 0; i < tufts; i++) {
    const [cx, cz] = clusters[i % clusters.length] as [number, number];
    const a = i * 2.39996;
    const r = 0.12 + (i % 4) * 0.07;
    const x = cx + Math.cos(a) * r;
    const z = cz + Math.sin(a) * r;
    // The tuft turns on its own; its root leans with the breeze, the same way as every other.
    const root = new Group();
    const tuft = new Group();
    for (let k = 0; k < 3; k++) {
      const blade = new Mesh(new ConeGeometry(0.025, 0.12, 6), toyMaterial('#4FB045', { ground: true }));
      blade.material.roughness = 0.8;
      blade.position.set((k - 1) * 0.03, 0.06, 0);
      blade.rotation.z = (k - 1) * 0.35;
      tuft.add(blade);
    }
    tuft.rotation.y = a;
    root.add(tuft);
    root.position.set(x, 0, z);
    decor.add(root);
    ambientMotion((time) => {
      root.rotation.z = sway(time, x, z);
    });
  }
  // Where the butterflies loop, in the island's coordinates: either side of the play area, in front of the player.
  const homes: { x: number; y: number; z: number; reach: number; wings: [PaletteName, PaletteName] }[] = [
    { x: -0.95, y: 0.75, z: 0.45, reach: 0.5, wings: ['sunny', 'tangerine'] },
    { x: 1.1, y: 0.95, z: -0.2, reach: 0.45, wings: ['ocean', 'grape'] },
  ];
  for (const [i, home] of homes.slice(0, butterflies).entries()) {
    const flyer = butterfly(home.wings);
    decor.add(flyer.body);
    const phase = i * 2.4;
    ambientMotion((time) => {
      const at = flight(time, phase, home.reach);
      flyer.body.position.set(home.x + at.x, home.y + at.y, home.z + at.z);
      flyer.body.rotation.y = at.heading;
      const open = flap(time, phase * 3);
      flyer.right.rotation.z = open;
      flyer.left.rotation.z = -open;
    });
  }
  island.add(decor);
  return decor;
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

/** A toy butterfly about 20 cm across: an ink body and two wings, each with a lobe of each colour. */
function butterfly(wings: [PaletteName, PaletteName]): { body: Group; left: Group; right: Group } {
  const body = new Group();
  const ink = toyMaterial('ink');
  const thorax = new Mesh(new CapsuleGeometry(0.011, 0.06, 4, 12), ink);
  thorax.rotation.x = Math.PI / 2;
  body.add(thorax);
  const head = new Mesh(new SphereGeometry(0.014, 16, 12), ink);
  head.position.z = 0.044;
  body.add(head);
  for (const side of [-1, 1]) {
    const feeler = new Mesh(new CylinderGeometry(0.0018, 0.0018, 0.045, 6), ink);
    feeler.position.set(side * 0.009, 0.016, 0.062);
    feeler.rotation.set(0.9, 0, -side * 0.35);
    body.add(feeler);
    const tip = new Mesh(new SphereGeometry(0.0045, 8, 6), ink);
    tip.position.set(side * 0.016, 0.031, 0.075);
    body.add(tip);
  }
  const [upper, lower] = wingShapes();
  const lobes = (left: boolean) => {
    const wing = new Group();
    for (const [shape, colour] of [
      [upper, wings[0]],
      [lower, wings[1]],
    ] as const) {
      const geometry = new ShapeGeometry(shape, 12);
      // The shape is drawn flat in x and y, forward up: lay it flat with forward toward +z. The left wing is the right
      // one turned half round the body's axis, not scaled by -1, which would light it as if it faced away.
      geometry.rotateX(Math.PI / 2);
      if (left) geometry.rotateZ(Math.PI);
      const material = toyMaterial(colour);
      material.side = DoubleSide;
      wing.add(new Mesh(geometry, material));
    }
    body.add(wing);
    return wing;
  };
  const right = lobes(false);
  const left = lobes(true);
  body.traverse((part) => {
    if (part instanceof Mesh) part.castShadow = true;
  });
  return { body, left, right };
}
