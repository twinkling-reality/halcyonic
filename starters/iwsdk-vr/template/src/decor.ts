import { ConeGeometry, CylinderGeometry, Group, Mesh, SphereGeometry } from 'three';
import { contactShadow } from './look.js';
import { palette, toyMaterial } from './palette.js';

/**
 * Optional dressing for the island (ART7): round trees, bushes, flowers and grass tufts, in clusters at its sides and
 * back, the foreground left clear. It is what makes a game look made rather than started; the game's own props bring
 * in the palette's colours. For example `decorate(gameIsland(), { trees: 3, bushes: 3, flowers: 6, tufts: 26 })`.
 */
export function decorate(
  island: Group,
  counts: { trees?: number; bushes?: number; flowers?: number; tufts?: number } = {},
): Group {
  const { trees = 3, bushes = 3, flowers = 6, tufts = 26 } = counts;
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
    const trunk = new Mesh(new CylinderGeometry(0.06, 0.08, 0.5, 16), toyMaterial('cocoa'));
    trunk.position.y = 0.25;
    tree.add(trunk);
    for (const [a, b, c, r] of [
      [0, 0.72, 0, 0.36],
      [0.2, 0.6, 0.05, 0.26],
      [-0.2, 0.62, -0.02, 0.27],
    ]) {
      const crown = new Mesh(new SphereGeometry(r, 32, 16), toyMaterial('#47B04A'));
      crown.material.roughness = 0.6;
      if ('clearcoat' in crown.material) crown.material.clearcoat = 0.15;
      crown.position.set(a, b, c);
      tree.add(crown);
    }
    shadowed(tree);
    tree.position.set(x, 0, z);
    tree.scale.setScalar(size);
    tree.add(contactShadow(0.45));
    decor.add(tree);
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
  const flowerSpots: [number, number, keyof typeof palette][] = [
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
    const tuft = new Group();
    for (let k = 0; k < 3; k++) {
      const blade = new Mesh(new ConeGeometry(0.025, 0.12, 6), toyMaterial('#4FB045', { ground: true }));
      blade.material.roughness = 0.8;
      blade.position.set((k - 1) * 0.03, 0.06, 0);
      blade.rotation.z = (k - 1) * 0.35;
      tuft.add(blade);
    }
    tuft.position.set(cx + Math.cos(a) * r, 0, cz + Math.sin(a) * r);
    tuft.rotation.y = a;
    decor.add(tuft);
  }
  island.add(decor);
  return decor;
}
