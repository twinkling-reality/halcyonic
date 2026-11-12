import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import {
  compileValidator,
  parseEventEnvelope,
  parseWorldDescription,
  parseWorldScene,
} from './validation.ts';
import { ProjectView } from './views.ts';
import { WORLD_LIGHTS, WORLD_PLACES, WorldDescription, type WorldScene } from './world.ts';

const tracked = (path: string) =>
  JSON.parse(readFileSync(new URL(`../../../starters/world/${path}`, import.meta.url), 'utf8'));

/** The world starter's own description: what every world begins as. */
const starting = tracked('template/world.json') as Record<string, unknown>;
/** The scene it begins in, as its manifest names it. */
const cylinder = tracked('scenes/cylinder.json') as WorldScene;
const scenes = new Map([[cylinder.scene, cylinder]]);
const LIGHTS = cylinder.lights.map((light) => light.light);
const PLACES = cylinder.places.map((place) => place.place);

/** A copy of the starting description with one member, named by its path, replaced or removed. */
function changed(path: string, value: unknown, remove = false): unknown {
  const copy = structuredClone(starting) as Record<string, unknown>;
  const names = path.split('.');
  let at: Record<string, unknown> = copy;
  for (const name of names.slice(0, -1)) at = at[name] as Record<string, unknown>;
  const last = names[names.length - 1] as string;
  if (remove) delete at[last];
  else at[last] = value;
  return copy;
}

const valid = (value: unknown) => parseWorldDescription(value, scenes);
const refuses = (value: unknown, what: string, where?: string) => {
  const result = valid(value);
  assert.equal(result.ok, false, what);
  if (where !== undefined) assert.equal(result.ok ? null : result.issues[0]?.path, where, what);
};

describe("a scene's manifest (ADR 0049)", () => {
  test('the tracked scene is one, and it names each light and place once, each with a line', () => {
    assert.equal(parseWorldScene(cylinder).ok, true);
    assert.equal(LIGHTS.length, 6);
    assert.equal(PLACES.length, WORLD_PLACES);
    const twice = structuredClone(cylinder);
    twice.lights.push({ ...cylinder.lights[0], when: 'all' } as never);
    assert.deepEqual(
      ((r) => (r.ok ? null : r.issues[0]?.path))(parseWorldScene(twice)),
      `/lights/${cylinder.lights.length}/light`,
    );
    const place = structuredClone(cylinder);
    place.places[1] = { ...place.places[0] } as never;
    assert.equal(parseWorldScene(place).ok, false);
    assert.equal(parseWorldScene({ ...cylinder, lights: [] }).ok, false, 'a scene with no light');
    for (const name of ['Sunset', 'far-left', '1st', '', '../x', 'x'.repeat(33)])
      assert.equal(
        parseWorldScene({ ...cylinder, lights: [{ light: name, says: 'x', when: 'day' }] }).ok,
        false,
        name,
      );
    assert.equal(
      parseWorldScene({ ...cylinder, lights: [{ light: 'x', says: 'x', when: 'dusk' }] }).ok,
      false,
    );
    assert.equal(parseWorldScene({ ...cylinder, picture: 'a.png' }).ok, false, 'an unknown member');
  });
});

describe("a world's description (ADR 0049, format 2)", () => {
  test("the starter's own description is one, and so is one at night with every place given", () => {
    assert.deepEqual(valid(starting), { ok: true, value: starting });
    assert.equal(valid(changed('night', 1)).ok, true);
    assert.equal(
      valid(
        changed(
          'places',
          PLACES.map((at, n) => ({ project: `Project ${n}`, at })),
        ),
      ).ok,
      true,
    );
  });

  test('it is valid only for a scene the computer has, with every light of its scene once, and only its places, each once', () => {
    refuses(changed('scene', 'castle'), 'a scene this computer does not have', '/scene');
    const lights = starting.lights as { light: string }[];
    refuses(changed('lights', lights.slice(1)), 'a light missing', '/lights');
    refuses(
      changed('lights', [...lights, lights[0]]),
      'a light twice',
      `/lights/${lights.length}/light`,
    );
    refuses(
      changed('lights', [...lights.slice(1), { ...lights[0], light: 'rain' }]),
      'a light its scene does not have',
      `/lights/${lights.length - 1}/light`,
    );
    refuses(
      changed('places', [{ project: 'Garden', at: 'castle' }]),
      'a place it does not have',
      '/places/0/at',
    );
    refuses(
      changed('places', [
        { project: 'Garden', at: 'tower' },
        { project: 'Portfolio', at: 'lake' },
        { project: 'Other', at: 'tower' },
      ]),
      'a place given twice',
      '/places/2/at',
    );
    assert.equal(parseWorldDescription(starting, new Map()).ok, false, 'no scenes at all');
  });

  test('a member it does not name is refused, at the top and inside every part, and so is a missing one', () => {
    refuses({ ...starting, script: 'run()' }, 'an unknown member');
    refuses({ ...starting, model: 'castle.glb' }, 'a file to load');
    refuses(changed('haze.texture', 'https://example.invalid/sky.png'), 'haze.texture');
    const lights = starting.lights as Record<string, unknown>[];
    refuses(
      changed('lights', [{ ...lights[0], texture: 'x.png' }, ...lights.slice(1)]),
      "a light's unknown member",
    );
    refuses(
      changed('places', [{ project: 'Garden', at: 'tower', open: '/etc/passwd' }]),
      "a place's",
    );
    // JSON.parse makes `__proto__` an own member, which is one more unknown member.
    refuses(JSON.parse(`${JSON.stringify(starting).slice(0, -1)},"__proto__":{"x":1}}`), 'proto');
    for (const member of Object.keys(starting)) refuses(changed(member, null, true), member);
    refuses(changed('format', 1), 'the first format');
    refuses(changed('format', 3), 'a later format');
  });

  test("format 1's sea is no longer a description: none of its parts is in the world", () => {
    const sea: Record<string, unknown> = {
      format: 1,
      sky: { zenith: '#27405E', band: null, horizon: '#F0C9A8' },
      disc: null,
      stars: 0.25,
      clouds: { amount: 0.2, colour: '#E9B7A6' },
      mist: { amount: 0.45, colour: '#E6C4B0' },
      water: { near: '#1B3148', far: '#6E8AA6', ripple: 0.25, shine: 0.7 },
      ground: { colour: '#2B2F36', radius: 3 },
      wind: { bearing: 60, strength: 0.2 },
      places: [],
    };
    refuses(sea, 'format 1');
    for (const member of ['sky', 'disc', 'stars', 'clouds', 'mist', 'water', 'ground', 'wind'])
      refuses({ ...starting, [member]: sea[member] }, member);
  });

  test('a colour is six hexadecimal digits after #, and nothing else names one', () => {
    const lights = starting.lights as Record<string, unknown>[];
    for (const colour of [
      'red',
      '#FFF',
      '#12345G',
      '#1234567',
      'rgb(0,0,0)',
      '#27405E\n',
      ' #27405E',
      '',
      7,
      null,
    ]) {
      refuses(changed('lights', [{ ...lights[0], colour }, ...lights.slice(1)]), String(colour));
      refuses(changed('haze.colour', colour), `haze ${String(colour)}`);
    }
    assert.equal(valid(changed('haze.colour', '#abcdef')).ok, true);
  });

  test('every number has a range, and a number too large to hold is outside it', () => {
    const lights = starting.lights as Record<string, unknown>[];
    const light = (brightness: unknown) =>
      changed('lights', [{ ...lights[0], brightness }, ...lights.slice(1)]);
    for (const [path, value] of [
      ['night', -0.01],
      ['night', 1.01],
      ['haze.thickness', -0.1],
      ['haze.thickness', 4.01],
    ] as const)
      refuses(changed(path, value), `${path} ${value}`);
    refuses(light(-0.01), 'a light below none');
    refuses(light(4.01), 'a light past four times');
    assert.equal(valid(light(0)).ok, true, 'a light off');
    assert.equal(valid(light(4)).ok, true, 'a light four times as bright');
    // 1e999 parses to Infinity, which no range holds.
    refuses(JSON.parse(JSON.stringify(starting).replace('"night":0', '"night":1e999')), 'huge');
    refuses(changed('night', '0.5'), 'a number as text');
    refuses(changed('haze', []), 'a list where a part belongs');
    refuses(changed('haze', null), 'a part that may not be null');
  });

  test("the lists are capped, and a place's project is a name of 1 to 200 characters", () => {
    const place = { project: 'Garden', at: 'tower' };
    refuses(
      changed(
        'places',
        Array.from({ length: WORLD_PLACES + 1 }, (_, n) => ({
          project: `P${n}`,
          at: PLACES[n % 8],
        })),
      ),
      'one place too many',
    );
    refuses(changed('places', Array(100_000).fill(place)), 'a count bomb');
    refuses(
      changed('lights', Array(WORLD_LIGHTS + 1).fill((starting.lights as unknown[])[0])),
      'one light too many',
    );
    for (const at of ['Tower', 'far-left', '', 3, null])
      refuses(changed('places', [{ ...place, at }]), `at ${String(at)}`);
    refuses(changed('places', [{ ...place, bearing: 10 }]), 'a bearing is no longer a place');
    refuses(changed('places', [{ ...place, project: '' }]), 'no name');
    refuses(changed('places', [{ ...place, project: '   ' }]), 'a blank name');
    refuses(changed('places', [{ ...place, project: 'x'.repeat(201) }]), 'a long name');
    refuses(changed('places', { 0: place }), 'not a list');
  });

  test('nothing but a description is one', () => {
    for (const value of [null, 1, 'world', [], [starting], {}])
      refuses(value, JSON.stringify(value));
    // Nesting as deep as the file allows is refused at its first member, never walked.
    let deep: unknown = 1;
    for (let depth = 0; depth < 5000; depth += 1) deep = [deep];
    refuses(changed('haze', deep), 'deep nesting');
  });

  test('the schema names only numbers, colours, names, one free name and the parts: no member that could hold a path or code', () => {
    const texts: string[] = [];
    const walk = (schema: unknown, path: string): void => {
      if (schema === null || typeof schema !== 'object') return;
      const node = schema as Record<string, unknown>;
      if (node.type === 'string') texts.push(`${path} ${String(node.pattern)}`);
      for (const [key, value] of Object.entries(node)) walk(value, `${path}/${key}`);
    };
    walk(WorldDescription, '');
    const free = texts.filter(
      (text) => !text.endsWith('^#[0-9A-Fa-f]{6}$') && !text.endsWith('^[a-z][a-z0-9_]{0,31}$'),
    );
    assert.deepEqual(
      free.map((text) => text.split(' ')[0]),
      ['/properties/places/items/properties/project'],
      "the one text that is neither a colour nor a name is a place's project name",
    );
  });
});

describe('the world as the Mac read it', () => {
  const trace = readFileSync(
    new URL('../../../fixtures/traces/multiple_workstreams.jsonl', import.meta.url),
    'utf8',
  )
    .trim()
    .split('\n')
    .map((line) => JSON.parse(line) as Record<string, unknown>);
  const created = trace.find((event) => event.event_type === 'project.created');
  const observed = (payload: unknown) =>
    parseEventEnvelope({ ...created, event_type: 'project.world.read', payload });
  const firstFormat = (payload: unknown) =>
    parseEventEnvelope({ ...created, event_type: 'project.world.observed', payload });
  const refused = { code: 'not_json', where: null, message: 'world.json is not JSON.' };

  test('the event holds a description or why there is none, never both and never neither', () => {
    assert.ok(created);
    assert.equal(observed({ description: starting, refused: null }).ok, true);
    assert.equal(observed({ description: null, refused }).ok, true);
    assert.equal(observed({ description: starting, refused }).ok, false);
    assert.equal(observed({ description: null, refused: null }).ok, false);
    assert.equal(observed({ description: { ...starting, script: 'x' }, refused: null }).ok, false);
  });

  test('a journal written before format 2 stays readable, and the old event holds no format 2 description', () => {
    const sea = {
      format: 1,
      sky: { zenith: '#27405E', band: null, horizon: '#F0C9A8' },
      disc: null,
      stars: 0.25,
      clouds: { amount: 0.2, colour: '#E9B7A6' },
      mist: { amount: 0.45, colour: '#E6C4B0' },
      water: { near: '#1B3148', far: '#6E8AA6', ripple: 0.25, shine: 0.7 },
      ground: { colour: '#2B2F36', radius: 3 },
      wind: { bearing: 60, strength: 0.2 },
      places: [{ project: 'Garden', bearing: -90, distance: 12 }],
    };
    assert.equal(firstFormat({ description: sea, refused: null }).ok, true);
    assert.equal(firstFormat({ description: null, refused }).ok, true);
    assert.equal(firstFormat({ description: sea, refused }).ok, false);
    assert.equal(firstFormat({ description: starting, refused: null }).ok, false);
    assert.equal(observed({ description: sea, refused: null }).ok, false);
  });

  test('where a rule broke is named by member names and list positions only', () => {
    for (const where of ['/sunset/colour', '/places/3/at', '/night'])
      assert.equal(observed({ description: null, refused: { ...refused, where } }).ok, true, where);
    for (const where of ['', '/', '/Sky', '/sky/../x', '/places/x y', 'sky', '/a/b/c/d/e/f/g/h/i'])
      assert.equal(
        observed({ description: null, refused: { ...refused, where } }).ok,
        false,
        where,
      );
    assert.equal(
      observed({ description: null, refused: { ...refused, code: 'unsafe' } }).ok,
      false,
    );
  });

  test("a project's view carries its world only when there is one, never null", () => {
    const validate = compileValidator(ProjectView);
    const project = {
      project_id: '01a19000-0000-7000-8000-000000000001',
      name: 'My World',
      location: null,
      starter: 'world',
      created_at: '2026-11-10T05:00:00.000Z',
      updated_at: '2026-11-10T05:00:00.000Z',
    };
    const world = { description: starting, refused: null, observed_at: '2026-11-10T05:01:00.000Z' };
    assert.equal(validate(project).ok, true);
    assert.equal(validate({ ...project, world }).ok, true);
    assert.equal(validate({ ...project, world: { ...world, refused } }).ok, true);
    assert.equal(
      validate({ ...project, world: { ...world, description: null, refused } }).ok,
      true,
    );
    assert.equal(validate({ ...project, world: null }).ok, false);
    assert.equal(
      validate({ ...project, world: { description: starting, refused: null } }).ok,
      false,
    );
  });
});
