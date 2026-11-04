import { createSystem, type World } from '@iwsdk/core';
import {
  BackSide,
  CanvasTexture,
  CircleGeometry,
  Color,
  CylinderGeometry,
  DirectionalLight,
  Float32BufferAttribute,
  BufferGeometry,
  Group,
  HemisphereLight,
  LatheGeometry,
  Mesh,
  MeshBasicMaterial,
  MeshPhysicalMaterial,
  NoToneMapping,
  PCFSoftShadowMap,
  PlaneGeometry,
  Points,
  PointsMaterial,
  RingGeometry,
  ShaderMaterial,
  SphereGeometry,
  TorusGeometry,
  Vector2,
  Vector3,
} from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { drift } from './motion.js';
import { toyMaterial } from './palette.js';
import { startAmbience } from './sound.js';
import { type WorldLook, type WorldName, worlds } from './worlds.js';

/**
 * The starter's look (ART7): one warm shadowed sun with a cool fill and a rim light, a sky from `sky` to `cloud`, a few
 * toy clouds, and a floating island to play on. The island's top is at y = 0, centred at (0, 0, -1.8), about 6 m
 * across, and its raised tier sits at the back. The player stands at the origin facing -z, toward the centre: in VR
 * that is where the headset starts, and the laptop camera looks the same way from just behind them, so what faces one
 * faces both. Put the game's objects in `gameIsland()`, on the top, in the world's own coordinates.
 */
export const ISLAND_CENTRE = { x: 0, z: -1.8 } as const;
export const ISLAND_RADIUS = 3.0;
/** Where the player stands: the origin, 1.8 m in front of the island's centre, facing -z. */
export const PLAYER = { x: 0, z: 0 } as const;

const FLOOR = {
  lobes: [
    [3, 0.06, 0.5],
    [5, 0.035, 1.3],
  ],
  steps: [0.93, 0.82],
  heights: [0.22, 0.2],
  thickness: 0.55,
  bevel: 0.14,
  centreLift: 0.06,
  tier: { radius: 1.0, height: 0.22, z: -2.15 },
} as const;

/** The raised tier at the back, in the world's coordinates: a stage for a sign or a goal; its top is at `top`. */
export const TIER = {
  x: ISLAND_CENTRE.x,
  z: ISLAND_CENTRE.z + FLOOR.tier.z,
  top: FLOOR.tier.height,
  radius: FLOOR.tier.radius,
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
let current: WorldLook = worlds.meadow;

/** The world `setupLook` was given (`src/worlds.ts`): the meadow until a game picks its own. */
export function currentWorld(): WorldLook {
  return current;
}
const movers: ((time: number) => void)[] = [];

/**
 * Moves a part of the world's own scenery every frame from the time the starter's system is given, never a clock of its
 * own, so the person's computer can step it: the clouds and `decorate()`'s breeze and butterflies use it. A game moves
 * its own objects in its own systems.
 */
export function ambientMotion(move: (time: number) => void): void {
  movers.push(move);
}

let latest = 0;

/**
 * The game's time in seconds: the `time` the starter's system was last given. For a press handler of `onSelect` or
 * `onTouch`, which is given no time; never a clock of the page's own, whose seconds are not the game's.
 */
export function gameTime(): number {
  return latest;
}

class AmbientSystem extends createSystem({}) {
  update(_delta: number, time: number): void {
    latest = time;
    for (const move of movers) move(time);
  }
}

/**
 * Where the game's objects go: a group in the world's own frame (the player at the origin, the island's centre at
 * `ISLAND_CENTRE`, its top at y = 0), never moved, so an object stays where it is put whether it hangs here or
 * `world.createTransformEntity(object)` moves it to the level's root, as that call does.
 */
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
  const ground = current.ground;
  const profile = [
    new Vector2(0, -FLOOR.thickness),
    new Vector2(R - 0.35, -FLOOR.thickness),
    new Vector2(R - 0.08, -FLOOR.thickness + 0.1),
    new Vector2(R, -FLOOR.bevel),
    new Vector2(R - 0.02, -0.03),
  ];
  const side = new Mesh(deform(new LatheGeometry(profile, 128)), toyMaterial(ground.side, { ground: true }));
  side.receiveShadow = true;
  group.add(side);
  const lipGeometry = new TorusGeometry(R - 0.02, 0.035, 12, 160);
  lipGeometry.rotateX(Math.PI / 2);
  const lip = new Mesh(deform(lipGeometry), toyMaterial(ground.lip, { ground: true, roughness: 0.9 }));
  lip.position.y = -0.08;
  group.add(lip);
  // Stepped soil layers under the island, each narrower and darker.
  let y = -FLOOR.thickness;
  for (const [i, k] of FLOOR.steps.entries()) {
    const h = FLOOR.heights[i] ?? 0.2;
    const colour = ground.steps[i] ?? ground.side;
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
      toyMaterial(ground.side, { ground: true }),
      toyMaterial(ground.top, { ground: true }),
      toyMaterial(ground.side, { ground: true }),
    ],
  );
  tier.position.set(0, FLOOR.tier.height / 2, FLOOR.tier.z);
  tier.castShadow = true;
  tier.receiveShadow = true;
  group.add(tier);
  // The top: leaf, a touch lighter at the centre, mixed toward the second green by a smooth noise.
  const topGeometry = new RingGeometry(0.001, R - 0.01, 128, 24);
  topGeometry.rotateX(-Math.PI / 2);
  const base = new Color(ground.top);
  const lifted = base.clone().offsetHSL(0, 0, FLOOR.centreLift);
  const patch = new Color(ground.patch);
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
    sheenColor: new Color(ground.sheen),
  });
  const top = new Mesh(deform(topGeometry), topMaterial);
  top.receiveShadow = true;
  group.add(top);
  // A rim of grass that rolls over the edge.
  const rimGeometry = new TorusGeometry(R - 0.01, 0.075, 16, 160);
  rimGeometry.rotateX(Math.PI / 2);
  const rim = new Mesh(deform(rimGeometry), toyMaterial(ground.rim, { ground: true, roughness: 0.8 }));
  rim.position.y = -0.02;
  group.add(rim);
  // A worn patch in front of the play area.
  const worn = new Mesh(
    new CircleGeometry(0.9, 48),
    toyMaterial(ground.worn, { ground: true, roughness: 0.9 }),
  );
  worn.rotation.x = -Math.PI / 2;
  worn.scale.set(1.5, 0.75, 1);
  worn.position.set(0.1, 0.004, 1.55);
  worn.receiveShadow = true;
  group.add(worn);
  return group;
}

/** A toy cloud of four puffs, as one mesh: one draw call. */
function cloud(x: number, y: number, z: number, size: number, colour: string): Mesh {
  const puffs = [
    [0, 0, 0, 1],
    [0.9, -0.15, 0, 0.75],
    [-0.9, -0.2, 0, 0.7],
    [0.35, 0.45, 0, 0.7],
  ].map(([a, b, c, r]) => new SphereGeometry(r, 32, 16).translate(a, b, c));
  const shape = mergeGeometries(puffs);
  // White, or the world's tint, with a self-light of its own colour, so it keeps that colour in the warm sun.
  const material = toyMaterial(colour, { ground: true, roughness: 0.9 });
  material.sheen = 0;
  material.emissive = new Color(colour);
  material.emissiveIntensity = 0.55;
  const puff = new Mesh(shape, material);
  puff.position.set(x, y, z);
  puff.scale.set(size, size * 0.8, size);
  return puff;
}

/** A small deterministic random sequence, so the same world draws the same stars on every load. */
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

/** The meadow's sky exactly as the starter has always drawn it, so a game that keeps the meadow keeps its pixels. */
function meadowSky(sky: WorldLook['sky']): Mesh {
  return new Mesh(
    new SphereGeometry(40, 32, 16),
    new ShaderMaterial({
      side: BackSide,
      depthWrite: false,
      uniforms: { top: { value: new Color(sky.top) }, horizon: { value: new Color(sky.horizon) } },
      vertexShader:
        'varying vec3 vP; void main(){ vP = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: [
        'uniform vec3 top; uniform vec3 horizon; varying vec3 vP;',
        'void main(){ float t = smoothstep(-0.02, 0.32, vP.y); gl_FragColor = vec4(mix(horizon, top, t), 1.0);',
        '#include <colorspace_fragment>',
        '}',
      ].join('\n'),
    }),
  );
}

/**
 * The sky: a dome from `top` at the top, through `middle` when given, to `horizon`, with the world's sun or moon drawn
 * into it as a disc and its glow, at no draw call of its own.
 */
function skyDome(sky: WorldLook['sky']): Mesh {
  const uniforms: Record<string, { value: unknown }> = {
    top: { value: new Color(sky.top) },
    horizon: { value: new Color(sky.horizon) },
  };
  const lines = ['uniform vec3 top; uniform vec3 horizon; varying vec3 vP;'];
  let colour = 'vec3 c = mix(horizon, top, t);';
  if (sky.middle !== undefined) {
    uniforms.middle = { value: new Color(sky.middle) };
    lines.push('uniform vec3 middle;');
    colour = 'vec3 c = t < 0.5 ? mix(horizon, middle, t * 2.0) : mix(middle, top, t * 2.0 - 1.0);';
  }
  let disc = '';
  if (sky.disc !== undefined) {
    uniforms.discAt = { value: new Vector3(...sky.disc.at).normalize() };
    uniforms.discColour = { value: new Color(sky.disc.colour) };
    uniforms.glowColour = { value: new Color(sky.disc.glow) };
    uniforms.discSize = { value: sky.disc.size };
    uniforms.glowStrength = { value: sky.disc.glowStrength };
    lines.push('uniform vec3 discAt; uniform vec3 discColour; uniform vec3 glowColour; uniform float discSize; uniform float glowStrength;');
    disc = [
      'float a = acos(clamp(dot(normalize(vP), discAt), -1.0, 1.0));',
      'c = mix(c, glowColour, glowStrength * 0.55 * exp(-a / (discSize * 2.2)));',
      'c = mix(c, discColour, 1.0 - smoothstep(discSize * 0.92, discSize, a));',
    ].join(' ');
  }
  return new Mesh(
    new SphereGeometry(40, 48, 24),
    new ShaderMaterial({
      side: BackSide,
      depthWrite: false,
      uniforms,
      vertexShader:
        'varying vec3 vP; void main(){ vP = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      // The colours are linear, as three.js holds them: the last line converts them for the screen, or the sky shows
      // darker and bluer than ART7's (#146CFF for #4FAEFF). three.js includes a chunk only on a line of its own.
      fragmentShader: [
        ...lines,
        `void main(){ float t = smoothstep(-0.02, 0.32, vP.y); ${colour} ${disc} gl_FragColor = vec4(c, 1.0);`,
        '#include <colorspace_fragment>',
        '}',
      ].join('\n'),
    }),
  );
}

/** Stars in the upper sky, as two layers of points (two draw calls); the bright layer twinkles slowly. */
function stars(count: number): Group {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 32;
  const context = canvas.getContext('2d');
  if (context !== null) {
    const gradient = context.createRadialGradient(16, 16, 0, 16, 16, 16);
    gradient.addColorStop(0, 'rgba(255,255,255,1)');
    gradient.addColorStop(0.35, 'rgba(255,248,226,0.85)');
    gradient.addColorStop(1, 'rgba(255,248,226,0)');
    context.fillStyle = gradient;
    context.fillRect(0, 0, 32, 32);
  }
  const sprite = new CanvasTexture(canvas);
  const random = seeded(7);
  const group = new Group();
  for (const [share, size] of [
    [0.8, 0.42],
    [0.2, 0.85],
  ] as const) {
    const positions: number[] = [];
    for (let i = 0; i < Math.round(count * share); i++) {
      const a = random() * Math.PI * 2;
      // Spread evenly over the sky above the horizon, none below it.
      const y = 0.04 + 0.96 * random();
      const r = Math.sqrt(1 - y * y);
      positions.push(Math.cos(a) * r * 36, y * 36, Math.sin(a) * r * 36);
    }
    const geometry = new BufferGeometry();
    geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
    const material = new PointsMaterial({
      size,
      map: sprite,
      transparent: true,
      depthWrite: false,
      color: '#FFFFFF',
      fog: false,
    });
    const layer = new Points(geometry, material);
    if (size > 0.5) ambientMotion((time) => {
      material.opacity = 0.75 + 0.25 * Math.sin(time * 1.3);
    });
    group.add(layer);
  }
  return group;
}

/**
 * A sea round the island, its top at `level`: deep near the island, the sky's colour far off, and a glittering path
 * toward the sun; and a ring of foam where the water meets the sand, breathing slowly. Two draw calls.
 */
function sea(water: NonNullable<WorldLook['ground']['sea']>, discAt: readonly [number, number, number] | undefined): Group {
  const group = new Group();
  const geometry = new CircleGeometry(38, 96, 0, Math.PI * 2);
  geometry.rotateX(-Math.PI / 2);
  const near = new Color(water.near);
  const far = new Color(water.far);
  const glitter = new Color(water.glitter);
  const sunAngle = discAt === undefined ? null : Math.atan2(discAt[2], discAt[0]);
  const colours: number[] = [];
  const position = geometry.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i);
    const z = position.getZ(i);
    const r = Math.hypot(x, z);
    const c = near.clone().lerp(far, Math.min(1, (r / 30) ** 0.7));
    if (sunAngle !== null && r > 4) {
      let d = Math.atan2(z, x) - sunAngle;
      d = Math.atan2(Math.sin(d), Math.cos(d));
      c.lerp(glitter, Math.exp(-(d * d) * 30) * Math.min(1, (r - 4) / 12) * 0.95);
    }
    colours.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  const surface = new Mesh(
    geometry,
    new MeshPhysicalMaterial({ color: '#FFFFFF', vertexColors: true, roughness: 0.28, metalness: 0, clearcoat: 0.6 }),
  );
  surface.position.y = water.level;
  surface.receiveShadow = true;
  group.add(surface);
  const foamGeometry = new TorusGeometry(ISLAND_RADIUS + 0.04, 0.07, 10, 160);
  foamGeometry.rotateX(Math.PI / 2);
  foamGeometry.scale(1, 0.4, 1);
  const foamMaterial = toyMaterial('#FFFFFF', { ground: true, roughness: 0.7 });
  foamMaterial.emissive = new Color('#FFFFFF');
  foamMaterial.emissiveIntensity = 0.35;
  const foam = new Mesh(deform(foamGeometry), foamMaterial);
  foam.position.y = water.level + 0.01;
  group.add(foam);
  ambientMotion((time) => {
    const s = 1 + 0.012 * Math.sin(time * 0.9);
    foam.scale.set(s, 1, s);
  });
  return group;
}

/** Low hills far off round the horizon, as one mesh: one draw call. */
function hills(colours: readonly [string, string]): Mesh {
  const random = seeded(11);
  const pieces: BufferGeometry[] = [];
  const shades = colours.map((c) => new Color(c));
  for (let i = 0; i < 18; i++) {
    const a = (i / 18) * Math.PI * 2 + random() * 0.2;
    const d = 27 + random() * 5;
    const r = 4.5 + random() * 3.5;
    const hill = new SphereGeometry(r, 24, 12, 0, Math.PI * 2, 0, Math.PI / 2);
    hill.scale(1.4, 0.32 + random() * 0.18, 1);
    hill.rotateY(-a);
    hill.translate(Math.cos(a) * d, -2.6, Math.sin(a) * d);
    const shade = shades[i % 2] ?? new Color('#88AA55');
    const count = hill.getAttribute('position').count;
    hill.setAttribute('color', new Float32BufferAttribute(new Array(count).fill([shade.r, shade.g, shade.b]).flat(), 3));
    pieces.push(hill);
  }
  const merged = mergeGeometries(pieces);
  const material = toyMaterial('#FFFFFF', { ground: true, roughness: 0.95 });
  material.vertexColors = true;
  material.sheen = 0;
  return new Mesh(merged ?? new BufferGeometry(), material);
}

/**
 * Sets up the starter's look once, after World.create, in the world you name (`src/worlds.ts`): `setupLook(world,
 * 'night')`, or a world of your own. With none it is the meadow, the starter's default, which every new game shares.
 * Returns the island to put the game on.
 */
export function setupLook(world: World, setting: WorldName | WorldLook = 'meadow'): { island: Group } {
  current = typeof setting === 'string' ? (worlds[setting] ?? worlds.meadow) : setting;
  const look = current;
  const renderer = world.renderer;
  // No tone mapping: ACES turns yellows to mustard and washes out greens; keep colours true.
  renderer.toneMapping = NoToneMapping;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = PCFSoftShadowMap;
  world.scene.environment = null;
  // HemisphereLight uses its world position as the sky direction: keep it on the scene root at (0, 1, 0).
  world.scene.add(new HemisphereLight(look.light.sky, look.light.ground, look.light.fill));
  // ART7 framed the island from +z, where the player stands: left unturned, its front faces the headset and the
  // laptop camera alike, and the clouds and the tier are in front of them.
  const stage = new Group();
  stage.position.set(ISLAND_CENTRE.x, 0, ISLAND_CENTRE.z);
  world.scene.add(stage);

  const sun = new DirectionalLight(look.light.sun, look.light.sunStrength);
  sun.position.set(...look.light.sunAt);
  sun.castShadow = true;
  sun.shadow.mapSize.set(1024, 1024);
  sun.shadow.radius = 5;
  sun.shadow.bias = -0.0006;
  Object.assign(sun.shadow.camera, { left: -4, right: 4, top: 4, bottom: -4, near: 0.5, far: 20 });
  stage.add(sun);
  stage.add(sun.target);
  const rim = new DirectionalLight(look.light.rim, look.light.rimStrength);
  rim.position.set(...(look.light.rimAt ?? [-2, 3, -5]));
  stage.add(rim);
  stage.add(rim.target);

  const plain = look.sky.middle === undefined && look.sky.disc === undefined;
  stage.add(plain ? meadowSky(look.sky) : skyDome(look.sky));
  if ((look.sky.stars ?? 0) > 0) stage.add(stars(look.sky.stars ?? 0));
  if (look.ground.sea !== undefined) stage.add(sea(look.ground.sea, look.sky.disc?.at));
  if (look.ground.hills !== undefined) stage.add(hills(look.ground.hills));
  const clouds = look.sky.clouds;
  if (clouds !== null)
    for (const [i, [x, y, z, size]] of [
      [-6, 2.2, -12, 1.0],
      [5.5, 2.8, -14, 1.2],
      [10, 1.4, -11, 0.8],
      [-10.5, 1.2, -10, 0.75],
      [0.5, 3.6, -20, 1.1],
    ].entries()) {
      const puff = cloud(x, y, z, size, clouds);
      stage.add(puff);
      // Each wanders around its own home, out of step with the others.
      ambientMotion((time) => {
        const away = drift(time, i * 1.9);
        puff.position.set(x + away.x, y + away.y, z);
      });
    }

  const ground = island();
  stage.add(ground);
  // The game's own frame is the world's, not the stage's: an interactive object leaves its group for the level's root.
  const play = new Group();
  world.scene.add(play);
  built = play;
  // index.html keeps the canvas clear over its sky until this first frame of the look, drawn after this update.
  let shown = false;
  ambientMotion(() => {
    if (shown) return;
    renderer.domElement.style.opacity = '1';
    shown = true;
  });
  startAmbience(look.sound, ambientMotion);
  world.registerSystem(AmbientSystem);
  return { island: play };
}
