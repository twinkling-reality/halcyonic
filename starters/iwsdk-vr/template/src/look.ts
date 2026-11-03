import { createSystem, type World } from '@iwsdk/core';
import {
  BackSide,
  CanvasTexture,
  CircleGeometry,
  Color,
  CylinderGeometry,
  DirectionalLight,
  Float32BufferAttribute,
  type BufferGeometry,
  Group,
  HemisphereLight,
  LatheGeometry,
  Mesh,
  MeshBasicMaterial,
  MeshPhysicalMaterial,
  NoToneMapping,
  PCFSoftShadowMap,
  PlaneGeometry,
  RingGeometry,
  ShaderMaterial,
  SphereGeometry,
  TorusGeometry,
  Vector2,
} from 'three';
import { drift } from './motion.js';
import { groundPatch, palette, soilLip, toyMaterial } from './palette.js';

/**
 * The starter's look (ART7): one warm shadowed sun with a cool fill and a rim light, a sky from `sky` to `cloud`, a few
 * toy clouds, and a floating island to play on. The island's top is at y = 0, centred at (0, 0, -1.8), about 6 m
 * across, and its raised tier sits at the back. The player stands at the origin facing -z, toward the centre: in VR
 * that is where the headset starts, and the laptop camera looks the same way from just behind them, so what faces one
 * faces both. Put the game's objects in `gameIsland()`, on the top.
 */
export const ISLAND_CENTRE = { x: 0, z: -1.8 } as const;
export const ISLAND_RADIUS = 3.0;
/** Where the player stands, in `gameIsland()`'s coordinates: the origin, 1.8 m in front of the centre, facing -z. */
export const PLAYER = { x: 0, z: 1.8 } as const;

const FLOOR = {
  lobes: [
    [3, 0.06, 0.5],
    [5, 0.035, 1.3],
  ],
  steps: [
    [0.93, 0.22, '#8E5634'],
    [0.82, 0.2, '#764528'],
  ],
  wornPatch: '#A9DE6E',
  thickness: 0.55,
  bevel: 0.14,
  centreLift: 0.06,
  tier: { radius: 1.0, height: 0.22, z: -2.15 },
} as const;

/** The island's soft outline: r(θ) = 1 + 0.06 sin(3θ + 0.5) + 0.035 sin(5θ + 1.3). */
const lobe = (theta: number) =>
  1 + FLOOR.lobes.reduce((sum, [k, a, p]) => sum + a * Math.sin(k * theta + p), 0);

function deform<G extends BufferGeometry>(geometry: G): G {
  if (geometry.type === 'LatheGeometry') geometry.rotateY(-Math.PI / 2);
  const position = geometry.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i);
    const z = position.getZ(i);
    const l = lobe(Math.atan2(z, x));
    position.setX(i, x * l);
    position.setZ(i, z * l);
  }
  geometry.computeVertexNormals();
  return geometry;
}

let shadowTexture: CanvasTexture | null = null;
let built: Group | null = null;
const movers: ((time: number) => void)[] = [];

/**
 * Moves a part of the world's own scenery every frame from the time the starter's system is given, never a clock of its
 * own, so the person's computer can step it: the clouds and `decorate()`'s breeze and butterflies use it. A game moves
 * its own objects in its own systems.
 */
export function ambientMotion(move: (time: number) => void): void {
  movers.push(move);
}

class AmbientSystem extends createSystem({}) {
  update(_delta: number, time: number): void {
    for (const move of movers) move(time);
  }
}

/** The island setupLook made: add the game's objects to it, so they stand on its top at y = 0. */
export function gameIsland(): Group {
  if (built === null) throw new Error('Call setupLook(world) in src/index.ts first.');
  return built;
}

/** A soft contact shadow for a prop of the given radius, to add at its feet: black to clear, 55% in the middle. */
export function contactShadow(radius: number): Mesh {
  if (shadowTexture === null) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 64;
    const context = canvas.getContext('2d');
    if (context !== null) {
      const gradient = context.createRadialGradient(32, 32, 0, 32, 32, 32);
      gradient.addColorStop(0, 'rgba(0,0,0,0.55)');
      gradient.addColorStop(1, 'rgba(0,0,0,0)');
      context.fillStyle = gradient;
      context.fillRect(0, 0, 64, 64);
    }
    shadowTexture = new CanvasTexture(canvas);
  }
  const blob = new Mesh(
    new PlaneGeometry(radius * 2, radius * 2),
    new MeshBasicMaterial({ map: shadowTexture, transparent: true, depthWrite: false }),
  );
  blob.rotation.x = -Math.PI / 2;
  blob.position.y = 0.006;
  return blob;
}

function island(): Group {
  const group = new Group();
  const R = ISLAND_RADIUS;
  const profile = [
    new Vector2(0, -FLOOR.thickness),
    new Vector2(R - 0.35, -FLOOR.thickness),
    new Vector2(R - 0.08, -FLOOR.thickness + 0.1),
    new Vector2(R, -FLOOR.bevel),
    new Vector2(R - 0.02, -0.03),
  ];
  const side = new Mesh(deform(new LatheGeometry(profile, 128)), toyMaterial('cocoa', { ground: true }));
  side.receiveShadow = true;
  group.add(side);
  const lipGeometry = new TorusGeometry(R - 0.02, 0.035, 12, 160);
  lipGeometry.rotateX(Math.PI / 2);
  const lip = new Mesh(deform(lipGeometry), toyMaterial(soilLip, { ground: true, roughness: 0.9 }));
  lip.position.y = -0.08;
  group.add(lip);
  // Stepped soil layers under the island, each narrower and darker.
  let y = -FLOOR.thickness;
  for (const [k, h, colour] of FLOOR.steps) {
    const step = [
      new Vector2(0, y - h),
      new Vector2(R * k - 0.12, y - h),
      new Vector2(R * k, y - h + 0.08),
      new Vector2(R * k, y),
    ];
    group.add(new Mesh(deform(new LatheGeometry(step, 128)), toyMaterial(colour, { ground: true })));
    y -= h;
  }
  // One raised tier at the back: a stage for a sign or a goal.
  const tier = new Mesh(
    new CylinderGeometry(FLOOR.tier.radius, FLOOR.tier.radius + 0.08, FLOOR.tier.height, 64),
    [
      toyMaterial('cocoa', { ground: true }),
      toyMaterial('leaf', { ground: true }),
      toyMaterial('cocoa', { ground: true }),
    ],
  );
  tier.position.set(0, FLOOR.tier.height / 2, FLOOR.tier.z);
  tier.castShadow = true;
  tier.receiveShadow = true;
  group.add(tier);
  // The top: leaf, a touch lighter at the centre, mixed toward the second green by a smooth noise.
  const topGeometry = new RingGeometry(0.001, R - 0.01, 128, 24);
  topGeometry.rotateX(-Math.PI / 2);
  const base = new Color(palette.leaf);
  const lifted = base.clone().offsetHSL(0, 0, FLOOR.centreLift);
  const patch = new Color(groundPatch);
  const colours: number[] = [];
  const position = topGeometry.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i);
    const z = position.getZ(i);
    const r = Math.hypot(x, z) / R;
    const n = 0.5 + 0.5 * Math.sin(x * 2.3 + Math.sin(z * 1.7) * 1.4) * Math.sin(z * 2.1 + Math.sin(x * 1.3));
    const c = lifted
      .clone()
      .lerp(base, Math.min(1, r * 1.1))
      .lerp(patch, n * 0.55);
    colours.push(c.r, c.g, c.b);
  }
  topGeometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  const topMaterial = new MeshPhysicalMaterial({
    color: '#FFFFFF',
    vertexColors: true,
    roughness: 0.85,
    metalness: 0,
    sheen: 0.8,
    sheenRoughness: 0.5,
    sheenColor: new Color('#E8FFD8'),
  });
  const top = new Mesh(deform(topGeometry), topMaterial);
  top.receiveShadow = true;
  group.add(top);
  // A rim of grass that rolls over the edge.
  const rimGeometry = new TorusGeometry(R - 0.01, 0.075, 16, 160);
  rimGeometry.rotateX(Math.PI / 2);
  const rim = new Mesh(deform(rimGeometry), toyMaterial('leaf', { ground: true, roughness: 0.8 }));
  rim.position.y = -0.02;
  group.add(rim);
  // A worn patch in front of the play area.
  const worn = new Mesh(
    new CircleGeometry(0.9, 48),
    toyMaterial(FLOOR.wornPatch, { ground: true, roughness: 0.9 }),
  );
  worn.rotation.x = -Math.PI / 2;
  worn.scale.set(1.5, 0.75, 1);
  worn.position.set(0.1, 0.004, 1.55);
  worn.receiveShadow = true;
  group.add(worn);
  return group;
}

function cloud(x: number, y: number, z: number, size: number): Group {
  const group = new Group();
  for (const [a, b, c, r] of [
    [0, 0, 0, 1],
    [0.9, -0.15, 0, 0.75],
    [-0.9, -0.2, 0, 0.7],
    [0.35, 0.45, 0, 0.7],
  ]) {
    // White with a white self-light, so it stays white in the warm sun.
    const material = toyMaterial('#FFFFFF', { ground: true, roughness: 0.9 });
    material.sheen = 0;
    material.emissive = new Color('#FFFFFF');
    material.emissiveIntensity = 0.55;
    const puff = new Mesh(new SphereGeometry(r, 32, 16), material);
    puff.position.set(a, b, c);
    group.add(puff);
  }
  group.position.set(x, y, z);
  group.scale.set(size, size * 0.8, size);
  return group;
}

/** Sets up the starter's look once, after World.create; returns the island to put the game on. */
export function setupLook(world: World): { island: Group } {
  const renderer = world.renderer;
  // No tone mapping: ACES turns yellows to mustard and washes out greens; keep colours true.
  renderer.toneMapping = NoToneMapping;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = PCFSoftShadowMap;
  world.scene.environment = null;
  // HemisphereLight uses its world position as the sky direction: keep it on the scene root at (0, 1, 0).
  world.scene.add(new HemisphereLight('#9CC8FF', '#8FC46E', 0.55));
  // ART7 framed the island from +z, where the player stands: left unturned, its front faces the headset and the
  // laptop camera alike, and the clouds and the tier are in front of them.
  const stage = new Group();
  stage.position.set(ISLAND_CENTRE.x, 0, ISLAND_CENTRE.z);
  world.scene.add(stage);

  const sun = new DirectionalLight('#FFE9C7', 2.45);
  sun.position.set(4.0, 4.4, 3.2);
  sun.castShadow = true;
  sun.shadow.mapSize.set(1024, 1024);
  sun.shadow.radius = 5;
  sun.shadow.bias = -0.0006;
  Object.assign(sun.shadow.camera, { left: -4, right: 4, top: 4, bottom: -4, near: 0.5, far: 20 });
  stage.add(sun);
  stage.add(sun.target);
  const rim = new DirectionalLight('#CFE4FF', 0.6);
  rim.position.set(-2, 3, -5);
  stage.add(rim);
  stage.add(rim.target);

  // The sky: a dome from `sky` at the top to `cloud` at the horizon.
  stage.add(
    new Mesh(
      new SphereGeometry(40, 32, 16),
      new ShaderMaterial({
        side: BackSide,
        depthWrite: false,
        uniforms: { top: { value: new Color(palette.sky) }, horizon: { value: new Color(palette.cloud) } },
        vertexShader:
          'varying vec3 vP; void main(){ vP = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
        // The colours are linear, as three.js holds them: the last line converts them for the screen, or the sky shows
        // darker and bluer than ART7's (#146CFF for #4FAEFF). three.js includes a chunk only on a line of its own.
        fragmentShader: [
          'uniform vec3 top; uniform vec3 horizon; varying vec3 vP;',
          'void main(){ float t = smoothstep(-0.02, 0.32, vP.y); gl_FragColor = vec4(mix(horizon, top, t), 1.0);',
          '#include <colorspace_fragment>',
          '}',
        ].join('\n'),
      }),
    ),
  );
  for (const [i, [x, y, z, size]] of [
    [-6, 2.2, -12, 1.0],
    [5.5, 2.8, -14, 1.2],
    [10, 1.4, -11, 0.8],
    [-10.5, 1.2, -10, 0.75],
    [0.5, 3.6, -20, 1.1],
  ].entries()) {
    const puff = cloud(x, y, z, size);
    stage.add(puff);
    // Each wanders around its own home, out of step with the others.
    ambientMotion((time) => {
      const away = drift(time, i * 1.9);
      puff.position.set(x + away.x, y + away.y, z);
    });
  }

  const ground = island();
  stage.add(ground);
  built = ground;
  world.registerSystem(AmbientSystem);
  return { island: ground };
}
