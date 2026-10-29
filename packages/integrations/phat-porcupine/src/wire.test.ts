import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { CatalogResponse, compileValidator } from '@halcyonic/contracts';
import { isFailure } from './connection.ts';
import { fixture } from './testing/fake-phat-porcupine.ts';
import { readCatalog, toCatalogResponse, type WireCatalog } from './wire.ts';

const valid = compileValidator(CatalogResponse);
const INSTANCE = { instanceId: 'fixture-instance', port: 1, sample: false, testGitHub: false };

/** The first of a list, which a test of the fixture knows is there. */
function first<T>(items: T[]): T {
  const [item] = items;
  assert.ok(item);
  return item;
}

function small(change: (catalog: WireCatalog) => void = () => {}): unknown {
  const catalog = structuredClone(fixture('catalog-small.json')) as WireCatalog;
  change(catalog);
  return catalog;
}

function mapped(value: unknown) {
  const wire = readCatalog(value);
  assert.ok(!isFailure(wire), JSON.stringify(wire));
  const answer = toCatalogResponse(wire, INSTANCE);
  assert.ok(valid(answer).ok, JSON.stringify(valid(answer)));
  assert.equal(answer.availability, 'available');
  if (answer.availability !== 'available') throw new Error('unreachable');
  return answer;
}

describe("reading Phat Porcupine's catalog v1", () => {
  test("becomes Halcyonic's catalog, reported, with what this computer needs and nothing else", () => {
    const answer = mapped(small());
    assert.equal(answer.provenance, 'reported');
    assert.deepEqual(answer.source, {
      system: 'phat-porcupine',
      api_version: '1',
      synthetic: false,
      sample: false,
    });
    assert.deepEqual(answer.sync, {
      repositories_at: '2026-10-29T11:55:00.000Z',
      repositories_failed: false,
      activity_at: '2026-10-29T11:55:00.000Z',
      activity_failed: true,
    });
    assert.deepEqual(answer.groups, [
      { group_id: '7c1f2a10-0d1e-4b5a-9f00-000000000001', name: 'Toys', order: 0 },
    ]);
    const [chimes] = answer.projects;
    assert.ok(chimes);
    assert.equal(chimes.catalog_id, '1b4d0c2e-0000-4000-8000-000000000001');
    assert.deepEqual(
      chimes.repositories.map((repository) => [
        repository.repository_id,
        repository.full_name,
        repository.primary,
        repository.description,
      ]),
      [
        [8100001, 'lantern-lab/wind-chimes', true, 'Chimes that ring when the wind blows.'],
        [8100002, 'lantern-lab/wind-chimes-notes', false, null],
      ],
      'an empty description reads as none',
    );
    assert.deepEqual(chimes.state, {
      state: 'review_requested',
      active: true,
      last_activity_at: '2026-10-29T10:00:00.000Z',
      sessions: { running: 0, awaiting_input: 0 },
      unknown: ['agent_sessions'],
    });
    assert.deepEqual(chimes.attention, [
      {
        kind: 'review_requested',
        repository_id: 8100001,
        pull_request: { number: 4, title: 'Add a softer chime' },
        at: '2026-10-29T09:00:00.000Z',
      },
    ]);
    assert.deepEqual(chimes.local, { project_ids: [], suggestions: [] });
    const text = JSON.stringify(answer);
    for (const dropped of [
      'https://',
      '"user"',
      'fixture-instance',
      'R_fixture1',
      'nameIsOverride',
      '"accounts"',
    ])
      assert.ok(!text.includes(dropped), `${dropped} never reaches the headset`);
  });

  test('a sample or a stand-in for GitHub is marked a sample, by the catalog or by its instance', () => {
    assert.equal(mapped(small((c) => (c.sample = true))).source.sample, true);
    assert.equal(mapped(small((c) => (c.testGitHub = true))).source.sample, true);
    const wire = readCatalog(small());
    assert.ok(!isFailure(wire));
    const answer = toCatalogResponse(wire, { ...INSTANCE, testGitHub: true });
    assert.equal(answer.availability === 'available' && answer.source.sample, true);
  });

  test('fields and values it adds later are read as unknown, never refused', () => {
    const answer = mapped(
      small((c) => {
        Object.assign(c, { futureField: { anything: true } });
        first(c.projects).state.state = 'deploy_failed';
        first(first(c.projects).attention).kind = 'access_lost';
        Object.assign(first(first(c.projects).repositories), { futureSignal: 3 });
        c.apiVersion = '1.4';
      }),
    );
    assert.equal(answer.projects[0]?.state.state, 'deploy_failed');
    assert.equal(answer.projects[0]?.attention[0]?.kind, 'access_lost');
  });

  test('truncated says how much was left out', () => {
    assert.deepEqual(
      mapped(small((c) => (c.truncated = { projects: 3, repositories: 9 }))).truncated,
      { projects: 3, repositories: 9 },
    );
  });

  test('anything outside version 1 is incompatible, naming where', () => {
    const broken: [string, (catalog: WireCatalog) => void][] = [
      ['another major', (c) => (c.apiVersion = '2.0')],
      ['no projects', (c) => Reflect.deleteProperty(c, 'projects')],
      ['a name over its bound', (c) => (first(c.projects).name = 'n'.repeat(101))],
      [
        'too many attention facts',
        (c) => (first(c.projects).attention = Array(6).fill(first(first(c.projects).attention))),
      ],
      [
        'a repository name that is not GitHub’s',
        (c) => (first(first(c.projects).repositories).fullName = 'a/b/c'),
      ],
      ['a negative count', (c) => (first(c.projects).state.sessions.running = -1)],
      ['no repositories', (c) => (first(c.projects).repositories = [])],
    ];
    for (const [what, change] of broken) {
      const wire = readCatalog(small(change));
      const answer = isFailure(wire) ? wire : toCatalogResponse(wire, INSTANCE);
      assert.equal(answer.availability, 'incompatible', what);
      assert.ok(valid(answer).ok, what);
    }
  });
});
