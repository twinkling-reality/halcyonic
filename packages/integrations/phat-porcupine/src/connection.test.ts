import assert from 'node:assert/strict';
import { chmodSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, afterEach, before, describe, test } from 'node:test';
import { connect, defaultCatalogHomes, isFailure, readDiscoveryFile } from './connection.ts';
import { FakePhatPorcupine } from './testing/fake-phat-porcupine.ts';

/** The discovery document's single contract entry, to change in a test. */
function entry(document: Record<string, unknown>): Record<string, unknown> {
  const [first] = document.contracts as Record<string, unknown>[];
  assert.ok(first);
  document.contracts = [first];
  return first;
}

function code(result: object): string | undefined {
  return isFailure(result) ? `${result.availability}:${result.reason.code}` : undefined;
}

describe("Omnorium's (formerly Phat Porcupine's) homes", () => {
  test('are [OMNORIUM_HOME], else [PHAT_PORCUPINE_HOME], else [~/.omnorium, ~/.phat-porcupine]', () => {
    assert.deepEqual(defaultCatalogHomes({ OMNORIUM_HOME: '/x/new' }, '/home/a'), ['/x/new']);
    assert.deepEqual(defaultCatalogHomes({ PHAT_PORCUPINE_HOME: '/x/old' }, '/home/a'), ['/x/old']);
    assert.deepEqual(
      defaultCatalogHomes({ OMNORIUM_HOME: '/x/new', PHAT_PORCUPINE_HOME: '/x/old' }, '/home/a'),
      ['/x/new'],
    );
    assert.deepEqual(defaultCatalogHomes({}, '/home/a'), [
      '/home/a/.omnorium',
      '/home/a/.phat-porcupine',
    ]);
  });
});

describe('the discovery file (AUDIT K2)', () => {
  let fake: FakePhatPorcupine;
  before(async () => {
    fake = await FakePhatPorcupine.start();
  });
  afterEach(() => {
    chmodSync(fake.discoveryPath, 0o600);
    fake.contracts = ['omnorium.catalog', 'phat-porcupine.catalog'];
    fake.publish();
  });
  after(() => fake.close());

  test('is read when it is a file of this user that nobody else can write', async () => {
    const file = await readDiscoveryFile(fake.home);
    assert.equal(code(file), undefined);
    assert.ok(!isFailure(file));
    assert.equal(file.port, fake.port);
    assert.equal(file.instanceId, fake.instanceId);
  });

  test('accepts omnorium.catalog or phat-porcupine.catalog at major 1, preferring omnorium.catalog', async () => {
    fake.contracts = ['omnorium.catalog'];
    fake.publish();
    const omnoriumOnly = await readDiscoveryFile(fake.home);
    assert.equal(code(omnoriumOnly), undefined);
    assert.ok(!isFailure(omnoriumOnly));
    assert.equal(omnoriumOnly.port, fake.port);

    fake.contracts = ['phat-porcupine.catalog'];
    fake.publish();
    const legacyOnly = await readDiscoveryFile(fake.home);
    assert.equal(code(legacyOnly), undefined);
    assert.ok(!isFailure(legacyOnly));
    assert.equal(legacyOnly.port, fake.port);

    fake.publish((document) => {
      document.contracts = [
        {
          name: 'phat-porcupine.catalog',
          major: 1,
          minor: 0,
          baseUrl: 'http://127.0.0.1:1/consumer/v1',
        },
        {
          name: 'omnorium.catalog',
          major: 1,
          minor: 0,
          baseUrl: `http://127.0.0.1:${fake.port}/consumer/v1`,
        },
      ];
    });
    const preferred = await readDiscoveryFile(fake.home);
    assert.equal(code(preferred), undefined);
    assert.ok(!isFailure(preferred));
    assert.equal(preferred.port, fake.port, 'prefers omnorium.catalog even when listed second');

    fake.publish((document) => {
      document.contracts = [
        {
          name: 'other.catalog',
          major: 1,
          minor: 0,
          baseUrl: `http://127.0.0.1:${fake.port}/consumer/v1`,
        },
      ];
    });
    assert.equal(code(await readDiscoveryFile(fake.home)), 'incompatible:unsupported_contract');
  });

  test('missing means Phat Porcupine is not running', async () => {
    const empty = mkdtempSync(join(tmpdir(), 'halcyonic-pp-empty-'));
    try {
      assert.equal(code(await readDiscoveryFile(empty)), 'unavailable:not_running');
    } finally {
      rmSync(empty, { recursive: true, force: true });
    }
  });

  test('a link, or a file another user could change, is never trusted', async () => {
    const elsewhere = mkdtempSync(join(tmpdir(), 'halcyonic-pp-link-'));
    try {
      writeFileSync(join(elsewhere, 'real.json'), '{}', { mode: 0o600 });
      symlinkSync(join(elsewhere, 'real.json'), join(elsewhere, 'consumer.json'));
      assert.equal(code(await readDiscoveryFile(elsewhere)), 'unavailable:discovery_unsafe');
    } finally {
      rmSync(elsewhere, { recursive: true, force: true });
    }
    for (const mode of [0o620, 0o602, 0o666]) {
      chmodSync(fake.discoveryPath, mode);
      assert.equal(
        code(await readDiscoveryFile(fake.home)),
        'unavailable:discovery_unsafe',
        mode.toString(8),
      );
    }
  });

  test('names exactly a loopback address and contract major 1, and a secret of 32 bytes', async () => {
    const cases: [(document: Record<string, unknown>) => void, string][] = [
      [
        (d) => (entry(d).baseUrl = 'http://localhost:1/consumer/v1'),
        'incompatible:base_url_not_allowed',
      ],
      [
        (d) => (entry(d).baseUrl = 'http://127.0.0.1:1/consumer/v1/'),
        'incompatible:base_url_not_allowed',
      ],
      [
        (d) => (entry(d).baseUrl = 'https://127.0.0.1:1/consumer/v1'),
        'incompatible:base_url_not_allowed',
      ],
      [
        (d) => (entry(d).baseUrl = 'http://user@127.0.0.1:1/consumer/v1'),
        'incompatible:base_url_not_allowed',
      ],
      [(d) => (entry(d).major = 2), 'incompatible:unsupported_contract'],
      [
        (d) => (d.secret = Buffer.alloc(16).toString('base64url')),
        'incompatible:invalid_discovery',
      ],
      [(d) => (d.instanceId = 7), 'incompatible:invalid_discovery'],
    ];
    for (const [change, expected] of cases) {
      fake.publish(change);
      assert.equal(code(await readDiscoveryFile(fake.home)), expected, expected);
    }
    writeFileSync(fake.discoveryPath, 'not json', { mode: 0o600 });
    assert.equal(code(await readDiscoveryFile(fake.home)), 'incompatible:invalid_discovery');
  });
});

describe('the instance proves itself before any credential (AUDIT K3)', () => {
  let fake: FakePhatPorcupine;
  before(async () => {
    fake = await FakePhatPorcupine.start();
  });
  afterEach(() => {
    fake.impersonate = null;
    fake.requests.length = 0;
  });
  after(() => fake.close());

  test('with a fresh challenge each time, answered by the secret only its own account can read', async () => {
    const first = await connect(fake.home, { timeoutMs: 2000 });
    const second = await connect(fake.home, { timeoutMs: 2000 });
    assert.equal(code(first), undefined);
    assert.equal(code(second), undefined);
    const challenges = fake.requests.map((request) => request.path);
    assert.deepEqual(challenges, ['/consumer/v1/discovery', '/consumer/v1/discovery']);
    assert.ok(
      fake.requests.every((request) => request.authorization === undefined),
      'discovery carries no credential',
    );
  });

  test('a wrong proof or another instance is refused', async () => {
    for (const impersonate of ['proof', 'instance'] as const) {
      fake.impersonate = impersonate;
      assert.equal(
        code(await connect(fake.home, { timeoutMs: 2000 })),
        'unavailable:instance_mismatch',
        impersonate,
      );
    }
  });

  test('a stale file whose port nothing answers is unreachable', async () => {
    const stale = await FakePhatPorcupine.start();
    const port = stale.port;
    await stale.close();
    const again = mkdtempSync(join(tmpdir(), 'halcyonic-pp-stale-'));
    try {
      chmodSync(again, 0o700);
      writeFileSync(
        join(again, 'consumer.json'),
        JSON.stringify({
          instanceId: 'gone',
          secret: Buffer.alloc(32).toString('base64url'),
          sample: false,
          testGitHub: false,
          contracts: [
            {
              name: 'phat-porcupine.catalog',
              major: 1,
              minor: 0,
              baseUrl: `http://127.0.0.1:${port}/consumer/v1`,
            },
          ],
        }),
        { mode: 0o600 },
      );
      assert.equal(code(await connect(again, { timeoutMs: 2000 })), 'unavailable:unreachable');
    } finally {
      rmSync(again, { recursive: true, force: true });
    }
  });
});
