/**
 * Opt-in: Omnorium's own published answers (omnorium-catalog.md), read in place from its checkout
 * and never copied, served by the stand-in instance and read through the whole path a headset's
 * gallery uses: discovery and its proof, the credential, the client, the catalog source and
 * `GET /api/catalog`, then `project.link_catalog` against what was read.
 *
 *   OMNORIUM_CHECKOUT=<Omnorium checkout> node --test apps/control-plane/src/intelligence/omnorium-published.test.ts
 */
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import type { CatalogResponse } from '@halcyonic/contracts';
import { FakeOmnorium } from '@halcyonic/integration-omnorium/testing';
import type { FastifyInstance } from 'fastify';
import { registerRoutes } from '../http/routes.ts';
import { loadOrCreateAccessToken } from '../http/security.ts';
import { createHttpServer } from '../http/server.ts';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';
import { OMNORIUM_CREDENTIAL_FILE, OmnoriumCatalog } from './catalog.ts';
import { seorakEvaluation } from './evaluation.ts';
import { salidiumUnderstanding } from './understanding.ts';

const checkout = process.env.OMNORIUM_CHECKOUT;
const skip = checkout === undefined ? 'set OMNORIUM_CHECKOUT to an Omnorium checkout' : false;

/** One of Omnorium's published consumer fixtures, read where Omnorium keeps it. */
function published(name: string): unknown {
  return JSON.parse(
    readFileSync(join(checkout as string, 'shared/consumer/fixtures/v1', name), 'utf8'),
  );
}

describe("Omnorium's published answers through GET /api/catalog (opt-in)", { skip }, () => {
  let dataDir: string;
  let omnorium: FakeOmnorium;
  let app: FastifyInstance;
  let harness: ReturnType<typeof createTestControlPlane>;
  let base: string;
  let token: string;
  before(async () => {
    dataDir = mkdtempSync(join(tmpdir(), 'halcyonic-omnorium-published-'));
    ({ token } = await loadOrCreateAccessToken(dataDir));
    omnorium = await FakeOmnorium.start({ contracts: ['omnorium.catalog'] });
    writeFileSync(join(dataDir, OMNORIUM_CREDENTIAL_FILE), `${omnorium.token}\n`, { mode: 0o600 });
    const catalog = new OmnoriumCatalog({
      homes: [omnorium.home],
      credentialPath: join(dataDir, OMNORIUM_CREDENTIAL_FILE),
      protect: [dataDir],
      timeoutMs: 2_000,
    });
    harness = createTestControlPlane({ catalogLinks: catalog });
    app = await createHttpServer({ logLevel: 'silent', token });
    registerRoutes(app, harness.controlPlane, {
      understanding: salidiumUnderstanding({
        home: join(dataDir, 'salidium'),
        credentialPath: join(dataDir, 'salidium-credential'),
        codexHome: join(dataDir, 'codex-home'),
      }),
      evaluation: seorakEvaluation({
        credentialPath: join(dataDir, 'seorak-credential'),
        codexHome: join(dataDir, 'codex-home'),
      }),
      catalog,
    });
    await app.listen({ host: '127.0.0.1', port: 0 });
    const address = app.server.address();
    if (address === null || typeof address === 'string') throw new Error('server has no port');
    base = `http://127.0.0.1:${address.port}`;
  });
  after(async () => {
    await app?.close();
    await harness?.controlPlane.close();
    await omnorium?.close();
    if (dataDir !== undefined) rmSync(dataDir, { recursive: true, force: true });
  });

  async function gallery(): Promise<CatalogResponse> {
    const response = await fetch(`${base}/api/catalog`, {
      headers: { authorization: `Bearer ${token}` },
    });
    assert.equal(response.status, 200);
    return (await response.json()) as CatalogResponse;
  }

  async function command(commandType: string, payload: Record<string, unknown>) {
    const response = await fetch(`${base}/api/commands`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
      body: JSON.stringify({
        schema_version: 1,
        command_id: randomUUID(),
        command_type: commandType,
        issued_at: new Date().toISOString(),
        client: TEST_CLIENT,
        payload,
      }),
    });
    return (await response.json()) as {
      disposition: string;
      command: { project_id: string | null; rejection: { code: string; message: string } | null };
    };
  }

  test('the small sample reads as a sample of 38 projects, suggests no folder and is never linked', async () => {
    omnorium.refuse = null;
    omnorium.catalog = published('catalog-sample-small.json');
    omnorium.etag = '"sample-small"';
    const answer = await gallery();
    assert.equal(answer.availability, 'available');
    if (answer.availability !== 'available') return;
    assert.equal(answer.provenance, 'reported');
    assert.equal(answer.source.sample, true);
    assert.equal(answer.projects.length, 38);
    assert.ok(
      answer.projects.every(
        (project) =>
          project.local.project_ids.length === 0 && project.local.suggestions.length === 0,
      ),
      'no project is linked here, and a sample suggests no folder',
    );
    const repository = answer.projects.flatMap((project) => project.repositories)[0];
    assert.ok(repository !== undefined, 'the sample has repositories');
    const created = await command('project.create', {
      name: 'Linked to a sample?',
      location: null,
    });
    const projectId = created.command.project_id;
    const refused = await command('project.link_catalog', {
      project_id: projectId,
      catalog: {
        source: 'omnorium',
        repository_id: repository.repository_id,
        full_name: repository.full_name,
      },
    });
    assert.equal(refused.disposition, 'rejected');
    assert.equal(
      refused.command.rejection?.message,
      'A sample catalog is never linked to your projects.',
    );
  });

  test('the empty collection reads as available with no projects', async () => {
    omnorium.refuse = null;
    omnorium.catalog = published('catalog-empty.json');
    // Another catalog under another ETag, or the client rightly keeps the one it has.
    omnorium.etag = '"empty"';
    const answer = await gallery();
    assert.equal(answer.availability, 'available');
    assert.equal(answer.availability === 'available' ? answer.projects.length : -1, 0);
  });

  test("a first sync not done yet reads unavailable, and a refused credential unauthorized, in Halcyonic's words", async () => {
    omnorium.refuse = { status: 503, body: published('error-not-ready-503.json') };
    const notReady = await gallery();
    assert.equal(notReady.availability, 'unavailable');
    assert.equal(
      notReady.availability === 'unavailable' ? notReady.reason.code : null,
      'not_ready',
    );
    omnorium.refuse = { status: 401, body: published('error-unauthorized-401.json') };
    const refused = await gallery();
    assert.equal(refused.availability, 'unauthorized');
    // Omnorium's own message is never passed on.
    for (const answer of [notReady, refused]) {
      assert.doesNotMatch(answer.reason.message, /Sign in to Omnorium and sync/);
    }
  });
});
