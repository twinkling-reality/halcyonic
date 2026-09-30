import assert from 'node:assert/strict';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  realpathSync,
  renameSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { compileValidator, LocationsResponse, MAX_LOCATION_FOLDERS } from '@halcyonic/contracts';
import { createHostLocations, MAX_SCANNED_ENTRIES } from './locations.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-locations-')));
after(() => rmSync(base, { recursive: true, force: true }));
const validLocations = compileValidator(LocationsResponse);

/** A project root with an app folder in it, and a private folder beside the root. */
function layout(name: string) {
  const top = join(base, name);
  const root = join(top, 'projects');
  const app = join(root, 'app');
  const outside = join(top, 'private');
  mkdirSync(app, { recursive: true });
  mkdirSync(outside, { recursive: true });
  return { root, app, outside, locations: createHostLocations([root]) };
}

describe('listing where projects may live', () => {
  test('lists each root with the visible folders directly inside it, sorted, and nothing else', () => {
    const { root, outside, locations } = layout('list');
    for (const folder of ['beta', 'Alpha', 'item10', 'item9', '.hidden', 'nested/inner']) {
      mkdirSync(join(root, folder), { recursive: true });
    }
    writeFileSync(join(root, 'a-file'), 'x');
    symlinkSync(outside, join(root, 'escape'));
    symlinkSync(join(root, 'beta'), join(root, 'alias'));
    const listed = locations.list();
    assert.ok(validLocations(listed).ok);
    assert.equal(listed.roots.length, 1);
    const [only] = listed.roots;
    assert.equal(only?.path, root);
    assert.equal(only?.name, 'projects');
    assert.equal(only?.status, 'available');
    assert.equal(only?.folders_truncated, false);
    assert.deepEqual(
      only?.folders.map((folder) => folder.name),
      ['Alpha', 'app', 'beta', 'item9', 'item10', 'nested'],
      'no hidden folder, file or symbolic link, and nothing deeper',
    );
    assert.equal(only?.folders.find((folder) => folder.name === 'app')?.path, join(root, 'app'));
  });

  test('a root with more folders than listed says so', () => {
    const { root, locations } = layout('many');
    for (let index = 0; index <= MAX_LOCATION_FOLDERS; index += 1) {
      mkdirSync(join(root, `folder-${String(index).padStart(3, '0')}`));
    }
    const [only] = locations.list().roots;
    assert.equal(only?.folders.length, MAX_LOCATION_FOLDERS);
    assert.equal(only?.folders_truncated, true);
  });

  test('a root holding more entries than are read is listed as truncated', () => {
    const { root, locations } = layout('crowded');
    for (let index = 0; index < MAX_SCANNED_ENTRIES; index += 1) {
      writeFileSync(join(root, `file-${index}`), '');
    }
    const [only] = locations.list().roots;
    assert.equal(only?.status, 'available');
    assert.equal(
      only?.folders_truncated,
      true,
      'the app folder and 10,000 files are 10,001 entries',
    );
    assert.ok((only?.folders.length ?? 0) <= 1);
  });

  test('a root that has gone is listed as missing, with nothing in it', () => {
    const { root, locations } = layout('gone');
    rmSync(root, { recursive: true });
    assert.deepEqual(locations.list().roots[0]?.status, 'missing');
    assert.deepEqual(locations.list().roots[0]?.folders, []);
  });

  test('with no roots nothing is listed', () => {
    assert.deepEqual(createHostLocations([]).list(), { roots: [] });
  });
});

describe('binding a project to an existing folder', () => {
  test('a folder directly inside a root, or the root itself, is bound by its real path', () => {
    const { root, app, locations } = layout('existing');
    const choice = { kind: 'existing_folder', root, folder_name: 'app' } as const;
    assert.deepEqual(locations.check(choice), { ok: true });
    assert.deepEqual(locations.bind(choice), {
      ok: true,
      location: { path: app, name: 'app', created: false },
    });
    assert.deepEqual(locations.bind({ kind: 'existing_folder', root, folder_name: null }), {
      ok: true,
      location: { path: root, name: 'projects', created: false },
    });
  });

  test('a folder named in another case is bound by the one spelling the file system has', (t) => {
    const { root, app, locations } = layout('case');
    if (!existsSync(join(root, 'APP'))) {
      t.skip('this volume is case-sensitive');
      return;
    }
    const upper = locations.bind({ kind: 'existing_folder', root, folder_name: 'APP' });
    const lower = locations.bind({ kind: 'existing_folder', root, folder_name: 'app' });
    assert.deepEqual(upper, lower);
    assert.deepEqual(upper, { ok: true, location: { path: app, name: 'app', created: false } });
    // The same folder by a new name is taken, whatever its case.
    const taken = locations.check({ kind: 'new_folder', root, folder_name: 'App' });
    assert.equal(taken.ok ? 'ok' : taken.code, 'location_exists');
  });

  test('a root is named as the host lists it, or as it was configured', () => {
    const top = join(base, 'configured');
    const real = join(top, 'real');
    mkdirSync(join(real, 'app'), { recursive: true });
    const link = join(top, 'link');
    symlinkSync(real, link);
    const locations = createHostLocations([link]);
    assert.equal(locations.list().roots[0]?.path, real);
    for (const root of [real, link]) {
      assert.equal(locations.check({ kind: 'existing_folder', root, folder_name: 'app' }).ok, true);
    }
  });

  test('what is not one of the host roots, or not directly inside one, is not allowed', () => {
    const { root, outside, locations } = layout('not-allowed');
    symlinkSync(outside, join(root, 'escape'));
    const refusals: [Parameters<typeof locations.check>[0], string][] = [
      [{ kind: 'existing_folder', root: outside, folder_name: null }, 'location_not_allowed'],
      [
        { kind: 'existing_folder', root: join(root, 'app'), folder_name: null },
        'location_not_allowed',
      ],
      [{ kind: 'existing_folder', root, folder_name: 'escape' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: '..' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: '.hidden' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: 'app/../..' }, 'location_not_allowed'],
      [{ kind: 'existing_folder', root, folder_name: 'missing' }, 'location_missing'],
    ];
    writeFileSync(join(root, 'a-file'), 'x');
    refusals.push([{ kind: 'existing_folder', root, folder_name: 'a-file' }, 'location_missing']);
    for (const [choice, code] of refusals) {
      const checked = locations.check(choice);
      assert.equal(checked.ok ? 'ok' : checked.code, code, JSON.stringify(choice));
      const bound = locations.bind(choice);
      assert.equal(bound.ok ? 'ok' : bound.code, code, JSON.stringify(choice));
      assert.ok(!bound.ok && bound.message.length > 0);
    }
  });

  test('with no roots configured nothing is allowed, and the refusal says the owner must allow one', () => {
    const { root } = layout('no-roots');
    const checked = createHostLocations([]).check({
      kind: 'existing_folder',
      root,
      folder_name: 'app',
    });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed');
    assert.match(checked.ok ? '' : checked.message, /HALCYONIC_PROJECT_ROOTS/);
  });

  test('a root that became unreadable lists as missing and refuses, and nothing throws', (t) => {
    const top = join(base, 'unreadable');
    const root = join(top, 'projects');
    mkdirSync(join(root, 'app'), { recursive: true });
    const locations = createHostLocations([root]);
    // Without search permission on its parent, every look at the root fails with EACCES.
    chmodSync(top, 0o000);
    t.after(() => chmodSync(top, 0o755));
    assert.deepEqual(locations.list().roots[0]?.status, 'missing');
    for (const choice of [
      { kind: 'existing_folder', root, folder_name: 'app' },
      { kind: 'new_folder', root, folder_name: 'fresh' },
    ] as const) {
      const checked = locations.check(choice);
      assert.equal(checked.ok ? 'ok' : checked.code, 'location_missing', choice.kind);
      const bound = locations.bind(choice);
      assert.equal(bound.ok ? 'ok' : bound.code, 'location_missing', choice.kind);
    }
    const decision = locations.policy(join(root, 'app'));
    assert.equal(decision.ok ? 'ok' : decision.code, 'location_missing');
  });

  test('a refusal quotes at most the start of a very long root', () => {
    const { locations } = layout('long-root');
    const checked = locations.check({
      kind: 'existing_folder',
      root: `/${'a'.repeat(4000)}`,
      folder_name: null,
    });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed');
    assert.ok(!checked.ok && checked.message.length < 400);
  });

  test('a root that has gone refuses its folders as missing', () => {
    const { root, locations } = layout('root-gone');
    rmSync(root, { recursive: true });
    const checked = locations.check({ kind: 'existing_folder', root, folder_name: 'app' });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_missing');
  });
});

describe('binding a project to a new folder', () => {
  test('the host makes the folder directly inside the root', () => {
    const { root, locations } = layout('new');
    const choice = { kind: 'new_folder', root, folder_name: 'storefront' } as const;
    assert.deepEqual(locations.check(choice), { ok: true });
    assert.equal(existsSync(join(root, 'storefront')), false, 'checking makes nothing');
    assert.deepEqual(locations.bind(choice), {
      ok: true,
      location: { path: join(root, 'storefront'), name: 'storefront', created: true },
    });
    assert.ok(statSync(join(root, 'storefront')).isDirectory());
  });

  test('a name already taken, by a folder, a file or a link, is refused and nothing is made', () => {
    const { root, outside, locations } = layout('taken');
    writeFileSync(join(root, 'notes'), 'x');
    symlinkSync(join(outside, 'nowhere'), join(root, 'dangling'));
    for (const name of ['app', 'notes', 'dangling']) {
      const bound = locations.bind({ kind: 'new_folder', root, folder_name: name });
      assert.equal(bound.ok ? 'ok' : bound.code, 'location_exists', name);
    }
    assert.equal(existsSync(join(outside, 'nowhere')), false, 'the link was not followed');
  });

  test("a name that is not a plain folder name, or a root that is not the host's, is not allowed", () => {
    const { root, outside, locations } = layout('bad-name');
    for (const [folderName, parent] of [
      ['..', root],
      ['.hidden', root],
      ['a/b', root],
      ['with space', root],
      ['fine', outside],
      ['fine', join(root, 'app')],
    ] as const) {
      const checked = locations.check({
        kind: 'new_folder',
        root: parent,
        folder_name: folderName,
      });
      assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed', folderName);
    }
    assert.equal(existsSync(join(outside, 'fine')), false);
  });

  test('a root replaced by a symbolic link after startup is refused, and nothing is made through it', () => {
    const { root, outside, locations } = layout('root-linked');
    renameSync(root, `${root}-moved`);
    symlinkSync(outside, root);
    for (const choice of [
      { kind: 'new_folder', root, folder_name: 'escape' },
      { kind: 'existing_folder', root, folder_name: null },
    ] as const) {
      const bound = locations.bind(choice);
      assert.equal(bound.ok ? 'ok' : bound.code, 'location_not_allowed', choice.kind);
    }
    assert.equal(existsSync(join(outside, 'escape')), false, 'no folder outside the roots');
    assert.equal(locations.list().roots[0]?.status, 'missing');
    assert.deepEqual(locations.list().roots[0]?.folders, []);
  });

  test('a root replaced by another folder at the same path is refused until the control plane restarts', () => {
    const { root, locations } = layout('root-replaced');
    renameSync(root, `${root}-old`);
    mkdirSync(join(root, 'app'), { recursive: true });
    const checked = locations.check({ kind: 'existing_folder', root, folder_name: 'app' });
    assert.equal(checked.ok ? 'ok' : checked.code, 'location_not_allowed');
    // A control plane started now takes the folder that is there.
    assert.equal(
      createHostLocations([root]).check({ kind: 'existing_folder', root, folder_name: 'app' }).ok,
      true,
    );
  });

  test('a folder made but not usable is reported with an unknown effect and left in place', () => {
    const { root } = layout('made-unusable');
    // A policy that refuses everything stands in for a change between the check and the folder.
    const locations = createHostLocations([root], (path) => ({
      ok: false,
      code: 'location_not_allowed',
      message: `${path} is not allowed now.`,
    }));
    const bound = locations.bind({ kind: 'new_folder', root, folder_name: 'storefront' });
    assert.equal(bound.ok ? 'ok' : bound.code, 'location_not_created');
    assert.equal(!bound.ok && 'effect' in bound ? bound.effect : null, 'unknown');
    assert.match(!bound.ok ? bound.message : '', /A folder was made at .*storefront/);
    assert.ok(existsSync(join(root, 'storefront')));
  });

  test('after a crash between making the folder and recording the project, the folder is chosen as existing', () => {
    const { root, locations } = layout('crash');
    const fresh = { kind: 'new_folder', root, folder_name: 'storefront' } as const;
    // The first attempt made the folder, then the control plane died before recording anything.
    assert.equal(locations.bind(fresh).ok, true);
    const retried = locations.check(fresh);
    assert.equal(retried.ok ? 'ok' : retried.code, 'location_exists');
    assert.match(retried.ok ? '' : retried.message, /Choose it as an existing folder/);
    assert.deepEqual(locations.bind({ kind: 'existing_folder', root, folder_name: 'storefront' }), {
      ok: true,
      location: { path: join(root, 'storefront'), name: 'storefront', created: false },
    });
  });
});
