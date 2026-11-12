import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  parseWorldDescription,
  WORLD_FILE,
  WORLD_STARTER_ID,
  WorldDescription,
} from '@halcyonic/contracts';
import { STARTER_DEFAULTS } from '@halcyonic/domain';
import { HostStarters } from './starters.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-world-starter-')));
after(() => rmSync(base, { recursive: true, force: true }));

const TRACKED = fileURLToPath(new URL('../../../starters', import.meta.url));
const NO_PACKAGES = '{"name":"s","lockfileVersion":3,"packages":{"":{"name":"s"}}}\n';
const ONE_PACKAGE =
  '{"name":"s","lockfileVersion":3,"packages":{"":{"name":"s"},"node_modules/dep":{"version":"1.0.0"}}}\n';

/** A tracked starter `plain` with a one-file template, as its `starter.json` and lockfile say. */
function tracked(name: string, manifest: Record<string, unknown>, lockfile: string) {
  const root = join(base, name);
  mkdirSync(join(root, 'tracked', 'plain', 'template'), { recursive: true });
  mkdirSync(join(root, 'project'), { recursive: true });
  writeFileSync(join(root, 'tracked', 'plain', 'starter.json'), JSON.stringify(manifest));
  writeFileSync(join(root, 'tracked', 'plain', 'package-lock.json'), lockfile);
  writeFileSync(join(root, 'tracked', 'plain', 'template', 'file.json'), '{}\n');
  const starters = new HostStarters({
    trackedDir: join(root, 'tracked'),
    installedDir: join(root, 'installed'),
    dataDir: join(root, 'data'),
    home: join(root, 'home'),
  });
  return { starters, project: join(root, 'project') };
}

describe('a starter with no packages (ADR 0049)', () => {
  test('that says so, with a lockfile that pins none, is installed as it stands and applies with no link', () => {
    const { starters, project } = tracked(
      'none',
      { format: 1, title: 'Plain', packages: 'none' },
      NO_PACKAGES,
    );
    assert.deepEqual(starters.list(), [{ starter_id: 'plain', title: 'Plain', installed: true }]);
    assert.deepEqual(starters.check('plain'), { ok: true });
    const applied = starters.apply('plain', project);
    assert.equal(applied.ok && applied.files, 1);
    assert.deepEqual(readdirSync(project), ['file.json']);
  });

  test('that says so while its lockfile pins a package is not installed, and applies nothing', () => {
    const { starters, project } = tracked(
      'claims',
      { format: 1, title: 'Plain', packages: 'none' },
      ONE_PACKAGE,
    );
    assert.equal(starters.list()[0]?.installed, false);
    assert.equal(starters.check('plain').ok, false);
    assert.equal(starters.apply('plain', project).ok, false);
    assert.deepEqual(readdirSync(project), []);
  });

  test('that does not say so still needs its installed set, however empty its lockfile', () => {
    const { starters } = tracked('silent', { format: 1, title: 'Plain' }, NO_PACKAGES);
    assert.equal(starters.list()[0]?.installed, false);
    assert.equal(starters.check('plain').ok, false);
  });
});

describe('the tracked world starter', () => {
  const starters = new HostStarters({
    trackedDir: TRACKED,
    installedDir: join(base, 'nothing-installed'),
    dataDir: join(base, 'data'),
    home: join(base, 'home'),
  });
  const template = join(TRACKED, WORLD_STARTER_ID, 'template');

  test('is listed installed with nothing installed, and holds a description and its guide, nothing else', () => {
    const listed = starters.list().find((starter) => starter.starter_id === WORLD_STARTER_ID);
    assert.equal(listed?.installed, true);
    assert.deepEqual(readdirSync(template).sort(), ['AGENTS.md', WORLD_FILE]);
    assert.equal(existsSync(join(TRACKED, WORLD_STARTER_ID, 'host')), false);
  });

  test('its description is one the contract takes', () => {
    const parsed = parseWorldDescription(
      JSON.parse(readFileSync(join(template, WORLD_FILE), 'utf8')),
    );
    assert.equal(parsed.ok, true, JSON.stringify(parsed));
  });

  test("its guide names every member of the description, with each number's range", () => {
    const guide = readFileSync(join(template, 'AGENTS.md'), 'utf8');
    const members: string[] = [];
    const ranges: string[] = [];
    // A row for every member that holds a value, for the part that may be null and for the list.
    const walk = (schema: unknown, path: string): void => {
      const node = schema as {
        properties?: Record<string, unknown>;
        items?: unknown;
        anyOf?: { properties?: unknown }[];
        minimum?: number;
        maximum?: number;
      };
      if (typeof node.minimum === 'number' && typeof node.maximum === 'number')
        ranges.push(`${path}: ${node.minimum} to ${node.maximum}`);
      const part = node.anyOf?.find((option) => option.properties !== undefined);
      if (part !== undefined) {
        members.push(path);
        walk(part, path);
      } else if (node.items !== undefined) {
        members.push(path);
        walk(node.items, `${path}[]`);
      } else if (node.properties !== undefined) {
        for (const [name, member] of Object.entries(node.properties))
          walk(member, path === '' ? name : `${path}.${name}`);
      } else members.push(path);
    };
    walk(WorldDescription, '');
    assert.equal(members.length, 27);
    for (const member of members) assert.ok(guide.includes(`\`${member}\``), member);
    // An amount (0 to 1) and a bearing (-180 to 180) are named once, by word; every other range on its member's row.
    for (const range of ranges) {
      const [member, bounds] = range.split(': ') as [string, string];
      const row = guide.split('\n').find((line) => line.startsWith(`| \`${member}\``)) ?? '';
      const word = bounds === '0 to 1' ? 'amount' : bounds === '-180 to 180' ? 'bearing' : bounds;
      assert.ok(row.includes(word), `${member}: ${bounds}`);
    }
    assert.match(guide, /Amounts run from 0 \(none\) to 1/);
    assert.match(guide, /from -180 to 180/);
    assert.match(guide, /at most 8/);
  });
});

test('a starter says what it is in its own starter.json, and a rule asks that, never its id (R2)', () => {
  const root = mkdtempSync(join(tmpdir(), 'halcyonic-declares-'));
  after(() => rmSync(root, { recursive: true, force: true }));
  const starters = new HostStarters({
    trackedDir: TRACKED,
    installedDir: join(root, 'installed'),
    dataDir: join(root, 'data'),
    home: join(root, 'home'),
  });
  assert.deepEqual(starters.declares(WORLD_STARTER_ID), {
    check: 'none',
    versions: 'none',
    copies: 'none',
    describes: 'world.json',
  });
  assert.deepEqual(
    starters.declares('iwsdk-vr'),
    STARTER_DEFAULTS,
    'one that declares nothing has a build',
  );
  // One this host does not have, or a path, has no build to check here (AUDIT2 S1).
  assert.deepEqual(starters.declares('no-such-starter'), { ...STARTER_DEFAULTS, check: 'none' });
  assert.deepEqual(
    starters.declares('../world'),
    { ...STARTER_DEFAULTS, check: 'none' },
    'never a path',
  );
});
