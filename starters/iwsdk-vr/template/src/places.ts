import {
  AdditiveBlending,
  BackSide,
  CircleGeometry,
  Color,
  CylinderGeometry,
  Float32BufferAttribute,
  Group,
  Mesh,
  MeshBasicMaterial,
  SphereGeometry,
} from 'three';
import { glows } from './glow.js';

/**
 * Places, not islands: dark or dim surroundings for an app of music and light, built in the group `setupScene` returns
 * (`src/look.ts`), where light from `src/glow.ts` shows best. Each returns its group and a `move(time)` for its own slow
 * life; pass that to `ambientMotion`.
 */
export interface Place {
  readonly group: Group;
  /** The colour at the place's horizon, for anything that should fade into the distance (a lane, a far shore). */
  readonly horizon: Color;
  move(time: number): void;
}

/** A sky as a sphere round the person, from `top` overhead through `horizon` to `below` under the floor. */
function sky(top: Color, horizon: Color, below: Color, radius: number): Mesh {
  const geometry = new SphereGeometry(radius, 48, 24);
  const position = geometry.getAttribute('position');
  const colours: number[] = [];
  const c = new Color();
  for (let i = 0; i < position.count; i++) {
    const up = position.getY(i) / radius;
    if (up >= 0) c.copy(horizon).lerp(top, Math.min(1, up ** 0.4));
    else c.copy(horizon).lerp(below, Math.min(1, (-up) ** 0.5));
    colours.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  const mesh = new Mesh(
    geometry,
    new MeshBasicMaterial({ vertexColors: true, side: BackSide, depthWrite: false, fog: false, toneMapped: false, dithering: true }),
  );
  mesh.renderOrder = -1;
  return mesh;
}

/** A floor disc `radius` metres round the person, `centre` under them fading to `edge`. */
function floor(centre: Color, edge: Color, radius: number): Mesh {
  const geometry = new CircleGeometry(radius, 64, 0, Math.PI * 2);
  geometry.rotateX(-Math.PI / 2);
  const position = geometry.getAttribute('position');
  const colours: number[] = [];
  const c = new Color();
  for (let i = 0; i < position.count; i++) {
    const out = Math.hypot(position.getX(i), position.getZ(i)) / radius;
    c.copy(centre).lerp(edge, Math.min(1, out ** 0.5));
    colours.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  return new Mesh(geometry, new MeshBasicMaterial({ vertexColors: true, toneMapped: false, dithering: true }));
}

/** A fixed sequence, so a place is the same on every load. */
function seeded(seed: number): () => number {
  let state = seed;
  return () => {
    state = (state * 16807) % 2147483647;
    return state / 2147483647;
  };
}

/**
 * A dark hall: a floor of deep blue-black glass round the person, a night that has no edge, and a few motes of dust
 * drifting in the air, catching no light but their own. For a concert of light: every light in it is the app's own.
 * `tint` colours the dark a little (default a deep blue).
 */
export function darkHall(options: { tint?: Color; motes?: number } = {}): Place {
  const tint = options.tint ?? new Color('#1A2448');
  const group = new Group();
  const black = new Color('#010103');
  // The floor fades into the horizon's colour and the sky rises from it, so no line marks where they meet.
  const horizon = tint.clone().multiplyScalar(0.16);
  group.add(sky(black, horizon, horizon, 120));
  group.add(floor(tint.clone().multiplyScalar(0.1), horizon, 60));
  const count = options.motes ?? 160;
  const motes = glows(count, { facing: true, halo: 0.01, round: 1 });
  group.add(motes.object);
  const random = seeded(4242);
  const home = Array.from({ length: count }, () => [
    (random() - 0.5) * 16,
    0.3 + random() * 4.5,
    -0.5 - random() * 14,
    random() * 6.28,
    0.003 + random() * 0.005,
  ]);
  const dust = new Color('#8FA6FF');
  // Haze far off, low over the floor, so the dark has depth and is not a wall: one soft ring of the tint all round,
  // rising softly out of the floor with no edge, brightest three metres up and fading to nothing above.
  const ring = new CylinderGeometry(44, 44, 12, 96, 24, true);
  ring.translate(0, 6, 0);
  const at = ring.getAttribute('position');
  const glow: number[] = [];
  for (let i = 0; i < at.count; i++) {
    const y = at.getY(i);
    const k = (y < 3 ? (y / 3) ** 2 : Math.max(0, 1 - (y - 3) / 9) ** 2) * 0.16;
    glow.push(tint.r * k, tint.g * k, tint.b * k);
  }
  ring.setAttribute('color', new Float32BufferAttribute(glow, 3));
  const haze = new Mesh(
    ring,
    new MeshBasicMaterial({
      vertexColors: true,
      side: BackSide,
      transparent: true,
      blending: AdditiveBlending,
      depthWrite: false,
      toneMapped: false,
      dithering: true,
    }),
  );
  group.add(haze);
  return {
    group,
    horizon,
    move(time) {
      motes.begin();
      for (const [x, y, z, phase, size] of home as [number, number, number, number, number][]) {
        const drift = time * 0.03 + phase;
        motes.add(
          x + Math.sin(drift) * 0.4,
          y + Math.sin(drift * 0.7) * 0.25,
          z + Math.cos(drift * 0.8) * 0.4,
          size,
          size,
          dust,
          0.18 + 0.12 * Math.sin(time * 0.5 + phase * 3),
        );
      }
      motes.end();
    },
  };
}
