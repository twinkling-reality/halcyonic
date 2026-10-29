import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { after, afterEach, before, describe, test } from 'node:test';
import { CatalogResponse, compileValidator } from '@halcyonic/contracts';
import { PhatPorcupineClient } from './client.ts';
import { consumerToken, FakePhatPorcupine, fixture } from './testing/fake-phat-porcupine.ts';

const valid = compileValidator(CatalogResponse);

function code(answer: CatalogResponse): string {
  return answer.availability === 'available'
    ? 'available'
    : `${answer.availability}:${answer.reason.code}`;
}

describe("Halcyonic's client of Phat Porcupine's catalog (ADR 0039)", () => {
  let fake: FakePhatPorcupine;
  let client: PhatPorcupineClient;
  before(async () => {
    fake = await FakePhatPorcupine.start();
    client = new PhatPorcupineClient({
      home: fake.home,
      credential: () => fake.token,
      timeoutMs: 2000,
    });
  });
  afterEach(() => {
    fake.refuse = null;
    fake.impersonate = null;
    fake.etag = '"catalog-1"';
    fake.requests.length = 0;
  });
  after(() => fake.close());

  test('reads the catalog after the instance proved itself, sending the credential only then', async () => {
    const answer = await client.catalog();
    assert.ok(valid(answer).ok);
    assert.equal(code(answer), 'available');
    assert.equal(answer.availability === 'available' && answer.projects.length, 3);
    assert.deepEqual(
      fake.requests.map((request) => [request.path, request.authorization !== undefined]),
      [
        ['/consumer/v1/discovery', false],
        ['/consumer/v1/catalog', true],
      ],
    );
  });

  test('to an impostor it sends no credential at all', async () => {
    for (const impersonate of ['proof', 'instance'] as const) {
      fake.impersonate = impersonate;
      fake.requests.length = 0;
      assert.equal(code(await client.catalog()), 'unavailable:instance_mismatch');
      assert.ok(
        fake.requests.every((request) => request.authorization === undefined),
        impersonate,
      );
    }
  });

  test('a credential that is not in its form is never sent', async () => {
    for (const credential of [
      null,
      '',
      'ppc_short',
      `Bearer ${consumerToken()}`,
      `${consumerToken()}\n`,
    ]) {
      const other = new PhatPorcupineClient({
        home: fake.home,
        credential: () => credential,
        timeoutMs: 2000,
      });
      fake.requests.length = 0;
      const answer = await other.catalog();
      assert.equal(answer.availability, 'unauthorized', JSON.stringify(credential));
      assert.ok(fake.requests.every((request) => request.authorization === undefined));
    }
  });

  test("each refusal is said by its code, in Halcyonic's words, never Phat Porcupine's", async () => {
    const wrong = new PhatPorcupineClient({
      home: fake.home,
      credential: () => consumerToken(),
      timeoutMs: 2000,
    });
    const refused = await wrong.catalog();
    assert.equal(code(refused), 'unauthorized:unauthorized');
    const cases: [number, unknown, string][] = [
      [503, fixture('error-not-ready-503.json'), 'unavailable:not_ready'],
      [429, { error: { code: 'rate_limited', message: 'x' } }, 'unavailable:rate_limited'],
      [500, { error: { code: 'internal', message: 'x' } }, 'unavailable:internal'],
      [421, { error: { code: 'host_not_allowed', message: 'x' } }, 'unavailable:host_not_allowed'],
      [
        403,
        { error: { code: 'origin_not_allowed', message: 'x' } },
        'unavailable:origin_not_allowed',
      ],
      [404, { error: { code: 'not_found', message: 'x' } }, 'incompatible:not_found'],
      [418, 'not json', 'unavailable:unexpected_status'],
    ];
    for (const [status, body, expected] of cases) {
      fake.refuse = { status, body };
      const answer = await client.catalog();
      assert.equal(code(answer), expected, String(status));
      assert.ok(valid(answer).ok);
      assert.ok(
        !JSON.stringify(answer).includes('Sign in'),
        "Phat Porcupine's own message is not passed on",
      );
    }
  });

  test('an unchanged catalog is answered by its ETag and the last one is kept', async () => {
    const first = await client.catalog();
    fake.requests.length = 0;
    const again = await client.catalog();
    assert.deepEqual(again, first);
    assert.equal(fake.requests.at(-1)?.path, '/consumer/v1/catalog');
    fake.etag = '"catalog-2"';
    fake.catalog = { ...(fixture('catalog-small.json') as object), projects: [] };
    const changed = await client.catalog();
    assert.equal(changed.availability === 'available' && changed.projects.length, 0);
    fake.catalog = fixture('catalog-small.json');
  });

  test('answers that run past the byte cap are incompatible, and no more is read', async () => {
    const capped = new PhatPorcupineClient({
      home: fake.home,
      credential: () => fake.token,
      timeoutMs: 2000,
      maxBytes: 1024,
    });
    assert.equal(code(await capped.catalog()), 'incompatible:answer_too_large');
  });

  test('without its discovery file it is not running', async () => {
    const missing = new PhatPorcupineClient({
      home: `${fake.home}-missing`,
      credential: () => fake.token,
      timeoutMs: 2000,
    });
    assert.equal(code(await missing.catalog()), 'unavailable:not_running');
  });
});

describe('a server that redirects', () => {
  test('is never followed, so the credential never goes elsewhere (AUDIT K4)', async () => {
    const fake = await FakePhatPorcupine.start();
    const seen: (string | undefined)[] = [];
    const elsewhere = createServer((request, response) => {
      seen.push(request.headers.authorization);
      response.end('{}');
    });
    await new Promise<void>((resolve) => elsewhere.listen(0, '127.0.0.1', resolve));
    try {
      const port = (elsewhere.address() as AddressInfo).port;
      fake.refuse = {
        status: 302,
        body: {},
        headers: { location: `http://127.0.0.1:${port}/consumer/v1/catalog` },
      };
      const client = new PhatPorcupineClient({
        home: fake.home,
        credential: () => fake.token,
        timeoutMs: 2000,
      });
      // A client that followed the Location would send the credential to the other server.
      const answer = await client.catalog();
      assert.equal(answer.availability, 'unavailable');
      assert.deepEqual(seen, [], `nothing reached port ${port}`);
    } finally {
      await new Promise<void>((resolve) => elsewhere.close(() => resolve()));
      await fake.close();
    }
  });
});
