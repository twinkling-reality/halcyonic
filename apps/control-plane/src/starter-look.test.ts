import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';

// The starter's look as a headset wearer and the laptop camera see it. The template's three.js code runs only in a
// browser (the repository does not install the starter's dependencies), so its numbers and wiring are read here as
// the files say them; the host's check and the lane's frames show what they draw.
const template = new URL('../../../starters/iwsdk-vr/template/', import.meta.url);
const read = (path: string) => readFileSync(new URL(path, template), 'utf8');

interface Pose {
  readonly position: readonly [number, number, number];
  readonly lookAt: readonly [number, number, number];
}

const DEGREE = Math.PI / 180;

function laptopCamera(): Pose {
  const config = JSON.parse(read('iwsdk.config.json')) as { world: { render: { camera: Pose } } };
  return config.world.render.camera;
}

/** A `{ x: <n>, z: <n> }` constant of look.ts, by name. */
function islandPoint(name: string): { x: number; z: number } {
  const found = read('src/look.ts').match(
    new RegExp(`export const ${name} = \\{ x: (-?[\\d.]+), z: (-?[\\d.]+) \\} as const;`),
  );
  assert.ok(found, `look.ts exports ${name}`);
  return { x: Number(found[1]), z: Number(found[2]) };
}

describe("the starter's look faces the headset wearer (U1)", () => {
  test('the laptop camera stands just behind the wearer and looks the way they look, at the island', () => {
    // In VR the wearer stands at the XR origin, facing -z when the session starts; the laptop camera is the only
    // picture the check and the agent see, so it must look the same way or the headset shows the island's back.
    const { position, lookAt } = laptopCamera();
    const [x, y, z] = position;
    assert.ok(Math.abs(x) <= 0.5 && z > 0.5 && z <= 3.5, `behind the origin: ${position}`);
    assert.ok(y >= 1.4 && y <= 2.4, `about head height: ${y}`);
    const dx = lookAt[0] - x;
    const dy = lookAt[1] - y;
    const dz = lookAt[2] - z;
    const yaw = Math.atan2(dx, -dz);
    assert.ok(Math.abs(yaw) <= 5 * DEGREE, `facing -z within 5 degrees: ${yaw / DEGREE}`);
    const pitch = Math.atan2(-dy, Math.hypot(dx, dz));
    assert.ok(
      pitch >= 5 * DEGREE && pitch <= 25 * DEGREE,
      `looking down a little: ${pitch / DEGREE}`,
    );
    const centre = islandPoint('ISLAND_CENTRE');
    const radius = Number(read('src/look.ts').match(/export const ISLAND_RADIUS = ([\d.]+);/)?.[1]);
    assert.ok(
      Math.hypot(lookAt[0] - centre.x, lookAt[2] - centre.z) <= radius / 2,
      'at the middle of the island',
    );
  });

  test("the island keeps ART7's front toward the wearer: the stage is never turned", () => {
    const look = read('src/look.ts');
    assert.doesNotMatch(look, /stage\.rotation/);
    assert.doesNotMatch(look, /\(-4, 1\.5, -6\)/);
  });

  test("the player's spot is the XR origin, in the island's own coordinates", () => {
    const centre = islandPoint('ISLAND_CENTRE');
    const player = islandPoint('PLAYER');
    // The island group sits at ISLAND_CENTRE, unturned, so the origin is at minus the centre in its coordinates.
    assert.equal(player.x + centre.x, 0);
    assert.equal(player.z + centre.z, 0);
  });

  test('the agent is told where the player stands and which way things face', () => {
    const agents = read('AGENTS.md');
    assert.match(agents, /`PLAYER`/);
    assert.match(agents, /faces -z/);
    assert.match(agents, /toward \+z/);
    assert.doesNotMatch(agents, /in front of the camera/);
  });
});

describe("the starter's sky shows ART7's colours (U2)", () => {
  test('the sky shader converts its linear colours for the screen, after it mixes them', () => {
    // three.js holds the uniforms' colours linear; a ShaderMaterial writes what it computes unless it includes the
    // output conversion, and then ART7's #4FAEFF shows as #146CFF.
    const look = read('src/look.ts');
    const shader = look.slice(
      look.indexOf('fragmentShader'),
      look.indexOf('}),', look.indexOf('fragmentShader')),
    );
    const mixed = shader.indexOf('gl_FragColor = vec4(mix(horizon, top, t), 1.0);');
    const converted = shader.search(/^\s*'\s*#include <colorspace_fragment>',$/m);
    assert.ok(mixed > 0, 'the sky mixes horizon to top');
    assert.ok(
      converted > mixed,
      'then converts, on a line of its own (three.js includes only whole lines)',
    );
  });
});
