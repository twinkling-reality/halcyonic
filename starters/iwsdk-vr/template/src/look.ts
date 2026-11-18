import { createSystem, VisibilityState, type World } from '@iwsdk/core';
import {
  BackSide,
  CanvasTexture,
  CircleGeometry,
  Color,
  CylinderGeometry,
  DirectionalLight,
  FogExp2,
  Float32BufferAttribute,
  BufferGeometry,
  Group,
  HemisphereLight,
  InstancedMesh,
  LatheGeometry,
  Mesh,
  MeshBasicMaterial,
  MeshPhysicalMaterial,
  NeutralToneMapping,
  NoToneMapping,
  Object3D,
  PCFSoftShadowMap,
  PlaneGeometry,
  PMREMGenerator,
  Points,
  PointsMaterial,
  RingGeometry,
  Scene,
  ShaderMaterial,
  SphereGeometry,
  Sprite,
  SpriteMaterial,
  SRGBColorSpace,
  type Texture,
  TorusGeometry,
  Vector2,
  Vector3,
} from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { seatPose, VIEW_CAMERA, type ViewPose, viewDrift } from './legible.js';
import { drift } from './motion.js';
import { toyMaterial } from './palette.js';
import { grassCarpet } from './grass.js';
import { waterMaterial, withGrain } from './surface.js';
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
/** The world `setupLook` or `setupScene` was given, for the kit's layouts that follow the headset session. */
let session: World | null = null;

/** Whether the person is in the headset now: false on a laptop, and before `setupLook` or `setupScene`. */
export function inHeadset(): boolean {
  return session !== null && session.visibilityState.value !== VisibilityState.NonImmersive;
}

/**
 * Calls `fn` now and each time the person enters or leaves the headset, with whether they are in it: how the kit's
 * `pages()` lays out six round the person in a headset and three across a laptop's view.
 */
export function onHeadset(fn: (headset: boolean) => void): void {
  if (session === null) {
    fn(false);
    return;
  }
  let was: boolean | null = null;
  session.visibilityState.subscribe((state) => {
    const now = state !== VisibilityState.NonImmersive;
    if (now === was) return;
    was = now;
    fn(now);
  });
}
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
 * `?seat` on the preview's address: on the first frame, once the app's own setup has run (its own laptop camera
 * included), the laptop view is moved to the person's own place, as the headset will see it (`seatPose`). A drag turns
 * it as before; without `?seat`, or in a headset, nothing changes.
 */
function seatOnFirstFrame(world: World): void {
  let first = true;
  ambientMotion(() => {
    if (!first) return;
    first = false;
    const pose = seatPose(
      typeof location === 'undefined' ? '' : location.search,
      inHeadset() || world.renderer.xr.isPresenting,
    );
    if (pose === null) return;
    world.camera.position.set(...pose.position);
    world.camera.lookAt(...pose.lookAt);
  });
}

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
  if (built === null) throw new Error('Call setupLook(world) or setupScene(world) in src/index.ts first.');
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
    new CylinderGeometry(FLOOR.tier.radius, FLOOR.tier.radius + 0.26, FLOOR.tier.height, 64),
    [
      toyMaterial(ground.side, { ground: true }),
      toyMaterial(ground.top, { ground: true }),
      toyMaterial(ground.side, { ground: true }),
    ],
  );
  tier.position.set(0, FLOOR.tier.height / 2, FLOOR.tier.z);
  tier.castShadow = true;
  tier.receiveShadow = true;
  // Its side is a bank of the island's own turf (or sand) sloping into the ground, not a painted plinth.
  const soil = withGrain(toyMaterial(ground.grass === undefined ? ground.top : ground.rim, { ground: true }), 5, 0.5);
  tier.material = [soil, toyMaterial(ground.top, { ground: true }), soil];
  group.add(tier);
  // No lip round its top: the bank's own turf rounds over into the lawn on top, as a bank does.
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
  // Grass hides most of the ground's relief; bare sand or earth shows its own, ripples and all.
  const top = new Mesh(
    deform(topGeometry),
    ground.grass === undefined ? withGrain(topMaterial, 11, 0.42) : withGrain(topMaterial, 5, 0.16),
  );
  top.receiveShadow = true;
  group.add(top);
  // Grass as grass on a grass world: blades over the top, short where the game is played, longer toward the edge,
  // none on the tier, so the stage stays clear for a sign or a goal.
  if (ground.grass !== undefined) {
    const carpet = grassCarpet({
      count: 9000,
      extent: R,
      colour: ground.grass.colour,
      tip: ground.grass.tip,
      dry: ground.grass.dry,
      light: [current.light.sunAt[0], current.light.sunAt[2]],
      grows: (x, z) => {
        const r = Math.hypot(x, z);
        if (r > (R - 0.06) * lobe(Math.atan2(z, x))) return false;
        return Math.hypot(x, z - FLOOR.tier.z) > FLOOR.tier.radius + 0.12;
      },
      height: (x, z) => 0.035 + 0.055 * Math.min(1, Math.hypot(x, z) / R) ** 1.5,
    });
    group.add(carpet.mesh);
    ambientMotion(carpet.move);
    // The tier's top has its own short lawn, kept low so a sign or a goal stands clear of it.
    const stage = grassCarpet({
      count: 900,
      extent: FLOOR.tier.radius,
      colour: ground.grass.colour,
      tip: ground.grass.tip,
      seed: 17,
      grows: (x, z) => Math.hypot(x, z) < FLOOR.tier.radius - 0.05,
      height: () => 0.03,
    });
    stage.mesh.position.set(0, FLOOR.tier.height, FLOOR.tier.z);
    group.add(stage.mesh);
    ambientMotion(stage.move);
  }
  // A rim of grass that rolls over the edge.
  const rimGeometry = new TorusGeometry(R - 0.01, 0.075, 16, 160);
  rimGeometry.rotateX(Math.PI / 2);
  const rim = new Mesh(deform(rimGeometry), toyMaterial(ground.rim, { ground: true, roughness: 0.8 }));
  rim.position.y = -0.02;
  // On a grass world the blades fringe the edge; a tube there reads as plastic. Bare ground keeps a low, soft lip.
  rim.scale.y = 0.4;
  if (ground.grass === undefined) group.add(rim);
  // No worn patch is laid: on grass the blades cover the play area, and on sand a paler disc read as a mat (AUDIT).
  return group;
}

let cloudPicture: CanvasTexture | null = null;

/**
 * A soft cloud's picture, drawn once in the browser: many soft puffs heaped on a flat base, lit white on top and
 * shaded grey-blue underneath, its edges fading to nothing, so it reads as vapour lit by the sky, not as a solid.
 */
function cloudTexture(): CanvasTexture {
  if (cloudPicture !== null) return cloudPicture;
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 256;
  const context = canvas.getContext('2d');
  if (context !== null) {
    const random = seeded(23);
    for (let i = 0; i < 70; i++) {
      // Heaped higher in the middle, spread wider at the base, and nothing below the base line.
      const across = (random() - 0.5) * 2;
      const x = 256 + across * 200;
      const lift = (1 - across * across) * 110;
      const y = 190 - random() * lift;
      const r = 26 + random() * (34 + lift * 0.35);
      const gradient = context.createRadialGradient(x, y, 0, x, y, r);
      gradient.addColorStop(0, 'rgba(255,255,255,0.55)');
      gradient.addColorStop(0.6, 'rgba(255,255,255,0.22)');
      gradient.addColorStop(1, 'rgba(255,255,255,0)');
      context.fillStyle = gradient;
      context.fillRect(0, 0, 512, 256);
    }
    // The flat base: cut softly under y = 196.
    const base = context.createLinearGradient(0, 180, 0, 214);
    base.addColorStop(0, 'rgba(0,0,0,0)');
    base.addColorStop(1, 'rgba(0,0,0,1)');
    context.globalCompositeOperation = 'destination-out';
    context.fillStyle = base;
    context.fillRect(0, 180, 512, 76);
    // Lit from above: the colour runs from white on top to a soft grey-blue under the billows.
    context.globalCompositeOperation = 'source-atop';
    const shade = context.createLinearGradient(0, 40, 0, 200);
    shade.addColorStop(0, 'rgba(255,255,255,1)');
    shade.addColorStop(0.55, 'rgba(244,246,252,1)');
    shade.addColorStop(1, 'rgba(176,186,206,1)');
    context.fillStyle = shade;
    context.fillRect(0, 0, 512, 256);
  }
  cloudPicture = new CanvasTexture(canvas);
  cloudPicture.colorSpace = SRGBColorSpace;
  return cloudPicture;
}

/**
 * A cloud as soft cards lit by the sky: two or three layered pictures of vapour, each turned to the eye, tinted by
 * the world's cloud colour and hazed with distance like everything else far off. Two or three draw calls, no geometry.
 */
function cloud(x: number, y: number, z: number, size: number, colour: string): Group {
  const group = new Group();
  const tint = new Color(colour);
  for (const [dx, dy, dz, w, a] of [
    [0, 0, 0, 7.5, 0.95],
    [1.6, -0.25, -0.4, 5.2, 0.8],
    [-1.9, -0.3, 0.3, 4.6, 0.75],
  ] as const) {
    const card = new Sprite(
      new SpriteMaterial({ map: cloudTexture(), color: tint, transparent: true, opacity: a, depthWrite: false, fog: true }),
    );
    card.scale.set(w * size, w * size * 0.5, 1);
    card.position.set(dx * size, dy * size, dz * size);
    group.add(card);
  }
  group.position.set(x, y, z);
  return group;
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
        'void main(){ float t = smoothstep(-0.02, 0.32, vP.y); vec3 c = mix(horizon, top, t);',
        // A band of light haze where the sky meets the land, and a dither so the gradient never bands on a headset.
        'c = mix(c, horizon * 1.12, (1.0 - smoothstep(0.0, 0.24, abs(vP.y))) * 0.72);',
        'c += (fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0;',
        'gl_FragColor = vec4(c, 1.0);',
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
      // A wide soft halo round it, as light scatters near the sun or the moon.
      'c += glowColour * glowStrength * (0.55 * exp(-a / (discSize * 3.5)) + 0.16 * exp(-a / 0.4));',
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
        `void main(){ float t = smoothstep(-0.02, 0.32, vP.y); ${colour} ${disc}`,
        // A band of light haze where the sky meets the land, and a dither so the gradient never bands on a headset.
        'c = mix(c, horizon * 1.12, (1.0 - smoothstep(0.0, 0.24, abs(vP.y))) * 0.72);',
        'c += (fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0;',
        'gl_FragColor = vec4(c, 1.0);',
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
function sea(
  water: NonNullable<WorldLook['ground']['sea']>,
  discAt: readonly [number, number, number] | undefined,
  horizon: string,
): Group {
  const group = new Group();
  // A dense ring mesh, so the fade to the horizon and the sun's path are drawn where they fall.
  const geometry = new RingGeometry(0.5, 38, 128, 64);
  geometry.rotateX(-Math.PI / 2);
  const near = new Color(water.near);
  const far = new Color(water.far);
  const glitter = new Color(water.glitter);
  const sky = new Color(horizon);
  const sunAngle = discAt === undefined ? null : Math.atan2(discAt[2], discAt[0]);
  const colours: number[] = [];
  const position = geometry.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i);
    const z = position.getZ(i);
    const r = Math.hypot(x, z);
    const c = near.clone().lerp(far, Math.min(1, (r / 30) ** 0.7));
    // Far out the sea takes the horizon's own colour, so it meets the sky in haze, not along a hard line.
    c.lerp(sky, Math.min(1, Math.max(0, (r - 14) / 20)) ** 1.15);
    if (sunAngle !== null && r > 4) {
      let d = Math.atan2(z, x) - sunAngle;
      d = Math.atan2(Math.sin(d), Math.cos(d));
      c.lerp(glitter, Math.exp(-(d * d) * 30) * Math.min(1, (r - 4) / 12) * 0.95);
    }
    colours.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  // Real water: its ripples drift under the sky's light, so it mirrors the sky, the sun's glint and the island.
  const surfaceMaterial = waterMaterial('#FFFFFF', 22);
  surfaceMaterial.vertexColors = true;
  ambientMotion((time) => surfaceMaterial.move(time));
  const surface = new Mesh(geometry, surfaceMaterial);
  surface.position.y = water.level;
  // Open water takes no shadow: a palm's shadow far out on the sea reads as a hole, not as shade.
  surface.receiveShadow = false;
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

/** The level of the land round the island, just under its top (y = 0): one continuous ground, no step. */
const PLAIN_LEVEL = -0.035;

/**
 * Land round the island to the horizon, as one mesh: patches of its two colours that fade, far off, into the horizon's
 * own colour, so the land meets the sky in haze with no edge.
 */
function plainLand(colours: readonly [string, string], horizon: string): Mesh {
  // A dense ring mesh: a circle has vertices only at its centre and rim, so colour drawn per vertex would blur away.
  const geometry = new RingGeometry(0.5, 38, 160, 120);
  // More rings near the island, where the eye is, than far off.
  geometry.rotateX(-Math.PI / 2);
  const [a, b] = colours.map((c) => new Color(c)) as [Color, Color];
  const far = new Color(horizon);
  // The third hue: the land's own colour gone dry and pale.
  const dry = a.clone().offsetHSL(-0.04, -0.18, 0.08);
  const colour: number[] = [];
  const position = geometry.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i);
    const z = position.getZ(i);
    const r = Math.hypot(x, z);
    // Large soft mottling, meadow and shade, so the land never reads as one floor.
    const n =
      0.5 +
      0.32 * Math.sin(x * 0.21 + Math.sin(z * 0.13) * 2.1) * Math.sin(z * 0.17 + Math.sin(x * 0.11) * 1.7) +
      0.18 * Math.sin(x * 0.53 + z * 0.41) * Math.sin(z * 0.61 - x * 0.27);
    const c = a.clone().lerp(b, Math.min(1, Math.max(0, n)));
    // Fields tens of metres across, each its own hue and value, with a dark hedgerow along their edges, so the
    // middle ground steps away from the eye instead of lying as one floor.
    const u = x * 0.94 + z * 0.34;
    const v = -x * 0.34 + z * 0.94;
    const fu = u / 7 + 0.4 * Math.sin(v * 0.09);
    const fv = v / 9 + 0.4 * Math.sin(u * 0.08);
    const cell = Math.floor(fu) * 7.13 + Math.floor(fv) * 3.71;
    const kind = Math.abs(Math.sin(cell * 12.9898) * 43758.5453) % 1;
    if (kind < 0.3) c.lerp(dry, 0.75);
    else if (kind < 0.55) c.lerp(b, 0.8);
    c.multiplyScalar(0.62 + 0.55 * (Math.abs(Math.sin(cell * 78.233) * 12345.678) % 1) + 0.12 * n);
    const edge = Math.min(Math.abs(fu - Math.round(fu)) * 7, Math.abs(fv - Math.round(fv)) * 9);
    if (r > 4.5 && edge < 0.75) c.lerp(b, 0.6).multiplyScalar(0.55);
    // Aerial perspective from a few metres out: lighter and greyer toward the horizon, then the horizon itself.
    c.lerp(far, Math.min(1, Math.max(0, (r - 10) / 40)) ** 1.1);
    colour.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colour, 3));
  const material = toyMaterial('#FFFFFF', { ground: true, roughness: 0.95 });
  material.vertexColors = true;
  material.sheen = 0;
  const land = new Mesh(geometry, withGrain(material, 40, 0.25));
  land.position.y = PLAIN_LEVEL;
  land.receiveShadow = true;
  return land;
}

/**
 * Far clumps of trees on the land between the island and the hills, as one instanced mesh (one draw call): each a
 * leafy crown low on the land, darker and hazed with distance like the hills, so the eye reads the land's depth.
 */
function farClumps(colour: string, horizon: string): InstancedMesh {
  const random = seeded(31);
  const crown = new SphereGeometry(1, 14, 9);
  const position = crown.getAttribute('position');
  for (let i = 0; i < position.count; i++) {
    const px = position.getX(i);
    const py = position.getY(i);
    const pz = position.getZ(i);
    const k = 1 + 0.16 * Math.sin(5 * px + 1) * Math.sin(4 * py + 2) * Math.sin(5 * pz);
    position.setXYZ(i, px * k, Math.max(-0.2, py) * k, pz * k);
  }
  crown.computeVertexNormals();
  // A tree at a distance: the crown lifted on a short dark trunk, darker underneath where it shades itself.
  crown.scale(1, 1.1, 1).translate(0, 1.15, 0);
  const crownShade: number[] = [];
  const lifted = crown.getAttribute('position');
  for (let i = 0; i < lifted.count; i++) {
    const s = 0.45 + 0.55 * Math.min(1, Math.max(0, (lifted.getY(i) - 0.35) / 1.4));
    crownShade.push(s, s, s);
  }
  crown.setAttribute('color', new Float32BufferAttribute(crownShade, 3));
  crown.deleteAttribute('uv');
  const trunk = new CylinderGeometry(0.08, 0.12, 0.7, 6).translate(0, 0.35, 0);
  trunk.deleteAttribute('uv');
  trunk.setAttribute('color', new Float32BufferAttribute(new Array(trunk.getAttribute('position').count).fill([0.32, 0.24, 0.2]).flat(), 3));
  const tree = mergeGeometries([crown.toNonIndexed(), trunk.toNonIndexed()]) ?? crown;
  const material = new MeshPhysicalMaterial({ color: '#FFFFFF', vertexColors: true, roughness: 0.95, metalness: 0 });
  const count = 90;
  const clumps = new InstancedMesh(tree, material, count + 30);
  const place = new Object3D();
  const base = new Color(colour);
  const sky = new Color(horizon);
  for (let i = 0; i < count; i++) {
    const a = random() * Math.PI * 2;
    // Never between the player and the game: none in the cone in front of the island.
    const d = 14 + random() * 17;
    const x = Math.cos(a) * d;
    const z = Math.sin(a) * d;
    const s = 0.22 + random() * 0.4;
    place.position.set(x, PLAIN_LEVEL, z);
    place.scale.set(s * (1 + random() * 0.8), s * (0.8 + random() * 0.5), s);
    place.rotation.set(0, random() * 6.28, 0);
    place.updateMatrix();
    clumps.setMatrixAt(i, place.matrix);
    clumps.setColorAt(i, base.clone().multiplyScalar(0.7 + random() * 0.3).lerp(sky, Math.min(0.6, (d - 12) / 26)));
  }
  // Nearer, between the island and the far trees: bushes low on the land and a few lone trees, so the eye steps
  // from the island to the hills.
  for (let i = count; i < count + 30; i++) {
    const a = random() * Math.PI * 2;
    const d = 5 + random() * 9;
    const lone = i % 6 === 0;
    const s = lone ? 0.55 + random() * 0.3 : 0.22 + random() * 0.18;
    place.position.set(Math.cos(a) * d, PLAIN_LEVEL - (lone ? 0 : 0.55 * s), Math.sin(a) * d);
    place.scale.set(s * (1.1 + random() * 0.6), s * (lone ? 1 : 0.6), s);
    place.rotation.set(0, random() * 6.28, 0);
    place.updateMatrix();
    clumps.setMatrixAt(i, place.matrix);
    clumps.setColorAt(i, base.clone().multiplyScalar(0.62 + random() * 0.25).lerp(sky, Math.min(0.35, (d - 5) / 30)));
  }
  clumps.instanceMatrix.needsUpdate = true;
  clumps.computeBoundingSphere();
  return clumps;
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
    // Rooted below the plain round the island, so each rises out of the land, never floats over it.
    hill.translate(Math.cos(a) * d, PLAIN_LEVEL - 0.9, Math.sin(a) * d);
    // Far off, the hills take on the sky's colour: their edges soften into the haze.
    const shade = (shades[i % 2] ?? new Color('#88AA55')).clone().lerp(new Color(currentWorld().sky.horizon), 0.38);
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
 * The world's sky as light: its sky dome, its ground below the horizon and its sun as a bright disc, blurred once into
 * an environment map (PMREM) that every lit material reads for its soft light and its reflections. Drawn once at setup,
 * never per frame.
 */
function skyLight(renderer: World['renderer'], look: WorldLook): Texture {
  const scene = new Scene();
  const plain = look.sky.middle === undefined && look.sky.disc === undefined;
  scene.add(plain ? meadowSky(look.sky) : skyDome(look.sky));
  // The ground below the horizon, dimmer than the grass in the sun: the light it sends back up.
  const ground = new Mesh(
    new CircleGeometry(60, 48).rotateX(-Math.PI / 2),
    // Mostly grey, a little of the ground's colour: a green bounce would tint every cloud and gloss green.
    new MeshBasicMaterial({ color: new Color(look.ground.sea?.far ?? look.ground.top).lerp(new Color('#7A7A72'), 0.7).multiplyScalar(0.4) }),
  );
  ground.position.y = -0.5;
  scene.add(ground);
  // The sun (or the moon) as a small, very bright disc, so gloss and water catch its glint.
  const toward = new Vector3(...look.light.sunAt).normalize();
  const disc = new Mesh(
    new SphereGeometry(1.4, 16, 8),
    new MeshBasicMaterial({ color: new Color(look.light.sun).multiplyScalar(look.light.sunStrength * 6) }),
  );
  disc.position.copy(toward.multiplyScalar(30));
  scene.add(disc);
  const pmrem = new PMREMGenerator(renderer);
  const target = pmrem.fromScene(scene, 0.02);
  pmrem.dispose();
  return target.texture;
}

/**
 * Sets up the starter's look once, after World.create, in the world you name (`src/worlds.ts`): `setupLook(world,
 * 'night')`, or a world of your own. With none it is the meadow, the starter's default, which every new game shares.
 * Returns the island to put the game on.
 */
export function setupLook(world: World, setting: WorldName | WorldLook = 'meadow'): { island: Group } {
  current = typeof setting === 'string' ? (worlds[setting] ?? worlds.meadow) : setting;
  session = world;
  seatOnFirstFrame(world);
  const look = current;
  const renderer = world.renderer;
  // Khronos PBR Neutral tone mapping: highlights roll off like film, while colours below them stay as given (ACES
  // turned yellows to mustard and washed out greens).
  renderer.toneMapping = NeutralToneMapping;
  renderer.toneMappingExposure = look.light.exposure ?? 1;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = PCFSoftShadowMap;
  // The sky lights the world: a blurred image of the world's own sky and ground falls on every lit material as soft
  // light from every side and as its reflections, so water mirrors the sky and gloss catches it.
  world.scene.environment = skyLight(renderer, look);
  world.scene.environmentIntensity = 0.55 * (look.light.environment ?? 1);
  // Haze toward the horizon: far hills and clouds fade into the sky's own colour at the horizon.
  const haze = look.light.haze ?? 0.024;
  world.scene.fog = haze > 0 ? new FogExp2(new Color(look.sky.horizon), haze) : null;
  // HemisphereLight uses its world position as the sky direction: keep it on the scene root at (0, 1, 0). The sky's
  // light above carries most of the soft light now, so this one only lifts the shade.
  world.scene.add(new HemisphereLight(look.light.sky, look.light.ground, look.light.fill * 0.35));
  // ART7 framed the island from +z, where the player stands: left unturned, its front faces the headset and the
  // laptop camera alike, and the clouds and the tier are in front of them.
  const stage = new Group();
  stage.position.set(ISLAND_CENTRE.x, 0, ISLAND_CENTRE.z);
  world.scene.add(stage);

  // A stronger sun than before over less soft light: shade with depth, its highlights rolled off by the tone mapping.
  const sun = new DirectionalLight(look.light.sun, look.light.sunStrength * 1.3);
  sun.position.set(...look.light.sunAt);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.radius = 5;
  sun.shadow.bias = -0.0004;
  sun.shadow.normalBias = 0.02;
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
  if (look.ground.sea !== undefined) stage.add(sea(look.ground.sea, look.sky.disc?.at, look.sky.horizon));
  if (look.ground.plain !== undefined && look.ground.sea === undefined) {
    stage.add(plainLand(look.ground.plain, look.sky.horizon));
    stage.add(farClumps(look.ground.hills?.[1] ?? look.ground.plain[1], look.sky.horizon));
  }
  if (look.ground.hills !== undefined) stage.add(hills(look.ground.hills));
  const clouds = look.sky.clouds;
  if (clouds !== null)
    for (const [i, [x, y, z, size]] of [
      [-9, 4.0, -22, 1.0],
      [8, 4.8, -26, 1.2],
      [17, 3.2, -21, 0.8],
      [-18, 3.0, -19, 0.75],
      [1, 6.0, -30, 1.1],
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

/**
 * Sets up a view brought from another app, not a game ("A view from another app" in AGENTS.md), once, after
 * World.create, instead of `setupLook`: the same frame, the same clock for `gameTime()` and motion, and the canvas shown
 * from the first frame, but none of the starter's sky, lights, island, clouds or sound. The view brings its own, over
 * `background`, a sphere 180 m round the person. Returns the group to build it in, the one `gameIsland()` returns, in
 * the world's own frame.
 */
export function setupScene(world: World, background = '#000000'): { root: Group } {
  // On a laptop the view is seen from a step behind the person's own place, looking down at the pages and the rows
  // under them (`VIEW_CAMERA`), not from the game's camera behind them in iwsdk.config.json: World.create placed that
  // one before this runs, and a VR session sets its own pose and puts this one back when it ends.
  world.camera.position.set(...VIEW_CAMERA.position);
  world.camera.lookAt(...VIEW_CAMERA.lookAt);
  session = world;
  const renderer = world.renderer;
  // On a laptop it drifts a few degrees round the pages and back over its first seconds, on the game's time (`DRIFT`),
  // until the person's first drag, key or press stops it where it is; in a headset it never moves.
  // A game that places its own laptop camera after this (a piano seen from inside its curve) keeps it: the drift
  // stands down when its first frame finds the camera away from VIEW_CAMERA.
  // ?seat moves it to the person's own place first, so the drift then stands down there too.
  seatOnFirstFrame(world);
  let first = true;
  ambientMotion((time) => {
    let placed: ViewPose | undefined;
    if (first) {
      first = false;
      const camera = world.camera;
      const look = camera.getWorldDirection(new Vector3());
      const at = camera.position;
      const reach = Math.hypot(VIEW_CAMERA.lookAt[0] - at.x, VIEW_CAMERA.lookAt[1] - at.y, VIEW_CAMERA.lookAt[2] - at.z);
      placed = {
        position: [at.x, at.y, at.z],
        lookAt: [at.x + look.x * reach, at.y + look.y * reach, at.z + look.z * reach],
      };
    }
    const pose = viewDrift.step(time, inHeadset() || renderer.xr.isPresenting, placed);
    if (pose === null) return;
    world.camera.position.set(...pose.position);
    world.camera.lookAt(...pose.lookAt);
  });
  renderer.toneMapping = NoToneMapping;
  world.scene.environment = null;
  world.scene.fog = null;
  // The SDK's environment system clears scene.background, so the colour is a sphere round the person, as the starter's
  // own skies are, inside the camera's far plane (200 m).
  const backdrop = new Mesh(
    new SphereGeometry(180, 32, 16),
    new MeshBasicMaterial({ color: new Color(background), side: BackSide, depthWrite: false, fog: false }),
  );
  backdrop.renderOrder = -1;
  world.scene.add(backdrop);
  const root = new Group();
  world.scene.add(root);
  built = root;
  let shown = false;
  ambientMotion(() => {
    if (shown) return;
    renderer.domElement.style.opacity = '1';
    shown = true;
  });
  world.registerSystem(AmbientSystem);
  return { root };
}
