import assert from 'node:assert/strict';
import { randomBytes, randomUUID } from 'node:crypto';
import { after, before, describe, test } from 'node:test';
import {
  type CatalogProject,
  type CatalogResponse,
  CatalogResponse as CatalogResponseSchema,
  compileValidator,
} from '@halcyonic/contracts';
import type { FastifyInstance } from 'fastify';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';
import { type CatalogSource, registerRoutes } from './routes.ts';
import { createHttpServer } from './server.ts';

const valid = compileValidator(CatalogResponseSchema);

function project(
  id: string,
  repositoryId: number,
  local?: CatalogProject['local'],
): CatalogProject {
  return {
    catalog_id: id,
    name: `Project ${id}`,
    group_id: null,
    group_order: null,
    repositories: [
      {
        repository_id: repositoryId,
        full_name: `acme/app-${repositoryId}`,
        primary: true,
        visibility: 'private',
        fork: false,
        archived: false,
        language: null,
        description: null,
        pushed_at: null,
        access: 'ok',
      },
    ],
    state: {
      state: 'active',
      active: true,
      last_activity_at: null,
      sessions: { running: 0, awaiting_input: 0 },
      unknown: [],
    },
    attention: [],
    local: local ?? { project_ids: [], suggestions: [] },
  };
}

function available(projects: CatalogProject[]): CatalogResponse {
  return {
    availability: 'available',
    provenance: 'reported',
    source: { system: 'phat-porcupine', api_version: '1', synthetic: false, sample: false },
    generated_at: '2026-10-29T10:00:00Z',
    sync: {
      repositories_at: null,
      repositories_failed: false,
      activity_at: null,
      activity_failed: false,
    },
    groups: [],
    projects,
    truncated: null,
  };
}

/** A control plane whose catalog source answers what the test sets, served over loopback. */
class Server {
  answer: CatalogResponse | (() => Promise<CatalogResponse>) | null = null;
  readonly harness = createTestControlPlane();
  readonly token = randomBytes(32).toString('base64url');
  #app: FastifyInstance | null = null;
  base = '';

  async start(withSource: boolean): Promise<void> {
    const source: CatalogSource = {
      read: async () => {
        const answer = this.answer;
        if (answer === null) throw new Error('no answer set');
        return typeof answer === 'function' ? answer() : structuredClone(answer);
      },
    };
    const app = await createHttpServer({ logLevel: 'silent', token: this.token });
    registerRoutes(app, this.harness.controlPlane, {
      understanding: { understand: async () => assert.fail('not asked') },
      evaluation: { evaluate: async () => assert.fail('not asked') },
      ...(withSource && { catalog: source }),
    });
    await app.listen({ host: '127.0.0.1', port: 0 });
    const address = app.server.address();
    if (address === null || typeof address === 'string') throw new Error('no port');
    this.base = `http://127.0.0.1:${address.port}`;
    this.#app = app;
  }

  async get(): Promise<{ status: number; body: CatalogResponse }> {
    const response = await fetch(`${this.base}/api/catalog`, {
      headers: { authorization: `Bearer ${this.token}` },
    });
    return { status: response.status, body: (await response.json()) as CatalogResponse };
  }

  async command(
    commandType: string,
    payload: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const response = await fetch(`${this.base}/api/commands`, {
      method: 'POST',
      headers: { authorization: `Bearer ${this.token}`, 'content-type': 'application/json' },
      body: JSON.stringify({
        schema_version: 1,
        command_id: randomUUID(),
        command_type: commandType,
        issued_at: '2026-10-29T10:00:00.000Z',
        client: TEST_CLIENT,
        payload,
      }),
    });
    return (await response.json()) as Record<string, unknown>;
  }

  async stop(): Promise<void> {
    await this.#app?.close();
    await this.harness.controlPlane.close();
  }
}

describe('GET /api/catalog with no catalog source (ADR 0039)', () => {
  const server = new Server();
  before(() => server.start(false));
  after(() => server.stop());

  test('says it is not set up, and asks for a credential', async () => {
    assert.equal((await fetch(`${server.base}/api/catalog`)).status, 401);
    const { status, body } = await server.get();
    assert.equal(status, 200);
    assert.ok(valid(body).ok);
    assert.equal(body.availability, 'unavailable');
    assert.equal(body.availability === 'unavailable' && body.reason.code, 'not_configured');
  });
});

describe('GET /api/catalog read through a catalog source', () => {
  const server = new Server();
  before(() => server.start(true));
  after(() => server.stop());

  test("fills each project's links from this computer's own journal, and no folder suggestion", async () => {
    await server.command('project.create', { name: 'Linked', location: null });
    const projectId = server.harness.controlPlane
      .snapshot()
      .projects.find((entry) => entry.name === 'Linked')?.project_id;
    assert.ok(projectId);
    const linked = await server.command('project.link_catalog', {
      project_id: projectId,
      catalog: { source: 'phat-porcupine', repository_id: 7, full_name: 'acme/app-7' },
    });
    assert.equal(linked.disposition, 'accepted');

    // What the source says about this computer is never taken: the links are the journal's.
    server.answer = available([
      project('c7', 7, {
        project_ids: ['01920000-0000-7000-8000-00000000ffff' as never],
        suggestions: [{ root: '/elsewhere', folder_name: 'app', rule: 'github_origin_full_name' }],
      }),
      project('c8', 8),
    ]);
    const { body } = await server.get();
    assert.ok(valid(body).ok);
    assert.equal(body.availability, 'available');
    if (body.availability !== 'available') return;
    assert.deepEqual(
      body.projects.map((entry) => entry.local),
      [
        { project_ids: [projectId], suggestions: [] },
        { project_ids: [], suggestions: [] },
      ],
    );
    assert.equal(body.provenance, 'reported');
  });

  test('is never journaled', async () => {
    server.answer = available([project('c1', 1)]);
    const head = server.harness.journal.head();
    await server.get();
    await server.get();
    assert.equal(server.harness.journal.head(), head);
  });

  test("an answer that breaks the contract is incompatible, and the source's text is not passed on", async () => {
    server.answer = { ...available([project('c1', 1)]), provenance: 'observed' } as never;
    const { body } = await server.get();
    assert.ok(valid(body).ok);
    assert.equal(body.availability, 'incompatible');
    assert.doesNotMatch(JSON.stringify(body), /Project c1/);
  });

  test('an unavailable answer keeps its code', async () => {
    server.answer = {
      availability: 'unavailable',
      reason: { code: 'not_running', message: 'Phat Porcupine is not running.' },
    };
    const { body } = await server.get();
    assert.equal(body.availability === 'unavailable' && body.reason.code, 'not_running');
  });

  test('a source that throws is unavailable, never a server error', async () => {
    server.answer = async () => {
      throw new Error('boom /Users/someone/secret');
    };
    const { status, body } = await server.get();
    assert.equal(status, 200);
    assert.equal(body.availability, 'unavailable');
    assert.doesNotMatch(JSON.stringify(body), /someone|boom/);
  });
});
