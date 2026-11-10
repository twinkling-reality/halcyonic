import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { compileValidator, parseEventEnvelope, parseWorldDescription } from './validation.ts';
import { ProjectView } from './views.ts';
import { WORLD_PLACES, WorldDescription } from './world.ts';

/** The world starter's own description: what every world begins as. */
const starting = JSON.parse(
  readFileSync(new URL('../../../starters/world/template/world.json', import.meta.url), 'utf8'),
) as Record<string, unknown>;

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

const refuses = (value: unknown, what: string) =>
  assert.equal(parseWorldDescription(value).ok, false, what);

describe("a world's description (ADR 0049)", () => {
  test("the starter's own description is one, and so is one with no band, no disc and a place", () => {
    assert.deepEqual(parseWorldDescription(starting), { ok: true, value: starting });
    assert.equal(parseWorldDescription(changed('sky.band', null)).ok, true);
    assert.equal(parseWorldDescription(changed('disc', null)).ok, true);
    assert.equal(
      parseWorldDescription(changed('places', [{ project: 'Garden', bearing: -90, distance: 12 }]))
        .ok,
      true,
    );
  });

  test('a member it does not name is refused, at the top and inside every part, and so is a missing one', () => {
    refuses({ ...starting, script: 'run()' }, 'an unknown member');
    refuses({ ...starting, model: 'castle.glb' }, 'a file to load');
    for (const part of ['sky', 'disc', 'clouds', 'mist', 'water', 'ground', 'wind'])
      refuses(changed(`${part}.texture`, 'https://example.invalid/sky.png'), `${part}.texture`);
    refuses(
      changed('places', [{ project: 'Garden', bearing: 0, distance: 5, open: '/etc/passwd' }]),
      'an unknown member of a place',
    );
    // JSON.parse makes `__proto__` an own member, which is one more unknown member.
    refuses(JSON.parse(`${JSON.stringify(starting).slice(0, -1)},"__proto__":{"x":1}}`), 'proto');
    for (const member of Object.keys(starting)) refuses(changed(member, null, true), member);
    refuses(changed('format', 2), 'another format');
  });

  test('a colour is six hexadecimal digits after #, and nothing else names one', () => {
    for (const colour of [
      'red',
      '#FFF',
      '#12345G',
      '#1234567',
      'rgb(0,0,0)',
      'url(https://example.invalid/a.png)',
      '#27405E\n',
      ' #27405E',
      '',
      7,
      null,
    ])
      refuses(changed('sky.zenith', colour), String(colour));
    assert.equal(parseWorldDescription(changed('sky.zenith', '#abcdef')).ok, true);
  });

  test('every number has a range, and a number too large to hold is outside it', () => {
    const outside: [string, number][] = [
      ['stars', -0.01],
      ['stars', 1.01],
      ['clouds.amount', 2],
      ['mist.amount', -1],
      ['water.ripple', 1.5],
      ['water.shine', -0.5],
      ['ground.radius', 1],
      ['ground.radius', 1000],
      ['wind.bearing', 181],
      ['wind.strength', 9],
      ['disc.bearing', -181],
      ['disc.height', -11],
      ['disc.height', 91],
      ['disc.size', 0],
      ['disc.size', 16],
      ['disc.glow', 1.1],
    ];
    for (const [path, value] of outside) refuses(changed(path, value), `${path} ${value}`);
    // 1e999 parses to Infinity, which no range holds.
    refuses(JSON.parse(JSON.stringify(starting).replace('"stars":0.25', '"stars":1e999')), 'huge');
    refuses(changed('stars', '0.5'), 'a number as text');
    refuses(changed('sky', []), 'a list where a part belongs');
    refuses(changed('clouds', null), 'a part that may not be null');
  });

  test('the places are capped, each within its ranges, its project a name of 1 to 200 characters', () => {
    const place = { project: 'Garden', bearing: 10, distance: 8 };
    assert.equal(
      parseWorldDescription(changed('places', Array(WORLD_PLACES).fill(place))).ok,
      true,
    );
    refuses(changed('places', Array(WORLD_PLACES + 1).fill(place)), 'one place too many');
    refuses(changed('places', Array(100_000).fill(place)), 'a count bomb');
    refuses(changed('places', [{ ...place, distance: 2 }]), 'too near');
    refuses(changed('places', [{ ...place, distance: 81 }]), 'too far');
    refuses(changed('places', [{ ...place, bearing: 360 }]), 'bearing');
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
    refuses(changed('sky', deep), 'deep nesting');
  });

  test('the schema names only numbers, colours, one name and the parts: no member that could hold a path or code', () => {
    const texts: string[] = [];
    const walk = (schema: unknown, path: string): void => {
      if (schema === null || typeof schema !== 'object') return;
      const node = schema as Record<string, unknown>;
      if (node.type === 'string') texts.push(`${path} ${String(node.pattern)}`);
      for (const [key, value] of Object.entries(node)) walk(value, `${path}/${key}`);
    };
    walk(WorldDescription, '');
    const free = texts.filter((text) => !text.endsWith('^#[0-9A-Fa-f]{6}$'));
    assert.deepEqual(
      free.map((text) => text.split(' ')[0]),
      ['/properties/places/items/properties/project'],
      "the one text that is not a colour is a place's project name",
    );
  });
});

describe('the world as the Mac observed it', () => {
  const trace = readFileSync(
    new URL('../../../fixtures/traces/multiple_workstreams.jsonl', import.meta.url),
    'utf8',
  )
    .trim()
    .split('\n')
    .map((line) => JSON.parse(line) as Record<string, unknown>);
  const created = trace.find((event) => event.event_type === 'project.created');
  const observed = (payload: unknown) =>
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

  test('where a rule broke is named by member names and list positions only', () => {
    for (const where of ['/sky/zenith', '/places/3/project', '/stars'])
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
