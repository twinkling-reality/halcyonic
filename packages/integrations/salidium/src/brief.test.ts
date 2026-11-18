import assert from 'node:assert/strict';
import { describe, type TestContext, test } from 'node:test';
import { compileValidator, UnderstandingBriefResult } from '@halcyonic/contracts';
import { COMMAND_LABEL } from './brief.ts';
import { SalidiumClient } from './client.ts';
import { briefDocument, briefKey, consumerToken, FakeSalidium } from './testing/fake-salidium.ts';

type Json = Record<string, unknown>;

const validateResult = compileValidator(UnderstandingBriefResult);

const CLAUDE = { kind: 'claude-agent', id: '6f1c2a90-3b7e-4d15-9a2c-0e8b5d7f4c11' };
const BRIEF_PATH = `/consumer/v1/executions/claude-code/${CLAUDE.id}/brief`;

async function start(t: TestContext, minor = 2): Promise<FakeSalidium> {
  const fake = await FakeSalidium.start();
  t.after(() => fake.close());
  fake.minor = minor;
  fake.briefs.set(briefKey('claude-code', CLAUDE.id), briefDocument('claude-code', CLAUDE.id));
  return fake;
}

async function brief(fake: FakeSalidium, session = CLAUDE): Promise<UnderstandingBriefResult> {
  const client = new SalidiumClient({ home: fake.home, credential: fake.token, timeoutMs: 2_000 });
  const result = await client.brief(session.kind, session.id);
  const checked = validateResult(result);
  assert.ok(checked.ok, JSON.stringify(checked));
  return result;
}

function available(result: UnderstandingBriefResult) {
  assert.equal(result.availability, 'available', JSON.stringify(result));
  if (result.availability !== 'available') throw new Error('not available');
  return result.brief;
}

function reason(result: UnderstandingBriefResult): [string, string] {
  assert.notEqual(result.availability, 'available');
  return 'reason' in result ? [result.availability, result.reason.code] : ['', ''];
}

function stored(fake: FakeSalidium): Json {
  return fake.briefs.get(briefKey('claude-code', CLAUDE.id)) as Json;
}

describe("reading Salidium's brief", () => {
  test("asks by the runtime's own session id, once, without a steering brief, and maps every move", async (t) => {
    const fake = await start(t);
    const read = available(await brief(fake));
    const asked = fake.requests.filter((request) => request.path.endsWith('/brief'));
    assert.equal(asked.length, 1);
    assert.equal(asked[0]?.path, BRIEF_PATH);
    assert.equal(asked[0]?.query.get('steer'), '0');
    assert.equal(
      fake.requests.some((request) => request.path.endsWith('/lookup')),
      false,
    );

    assert.deepEqual(read.source, {
      system: 'salidium',
      synthetic: false,
      version: read.source.version,
      contract: { name: 'salidium.consumer', major: 1, minor: 2 },
      instance_id: fake.instanceId,
      generated_at: '2026-11-18T02:40:00.000Z',
    });
    assert.equal(read.archetype, 'blast-radius-edit');
    assert.deepEqual(
      read.moves.map((move) => [move.kind, move.epistemic]),
      [
        ['action-scope', 'observed'],
        ['mechanism-blast', 'observed'],
        ['epistemic-verification', 'reported'],
      ],
    );
    const [, how, checks] = read.moves;
    assert.equal(how?.plain_text, 'Touches 3 callers (not changed)');
    assert.deepEqual(how?.segments[1], {
      kind: 'token',
      text: null,
      token: 'caller-rollup',
      label: '3 callers',
      detail: 'not changed',
      epistemic: 'observed',
      tone: null,
      paths: ['src/page.tsx', 'src/api.ts', 'src/list.tsx'],
      lines_added: null,
      lines_removed: null,
      check_outcomes: [],
    });
    assert.deepEqual(checks?.segments[1]?.check_outcomes, ['pass', 'fail']);
    assert.equal(checks?.segments[1]?.epistemic, 'reported');
  });

  test("keeps a command token's place but never its words, in the token or in the plain text", async (t) => {
    const fake = await start(t);
    const [changes] = available(await brief(fake)).moves;
    const command = changes?.segments[3];
    assert.equal(command?.token, 'command');
    assert.equal(command?.label, COMMAND_LABEL);
    assert.deepEqual(command?.paths, []);
    assert.equal(changes?.plain_text, 'Edited orders.ts, running Command');
    assert.equal(JSON.stringify(changes).includes('hooks.example.com'), false);
    // The file token keeps its path and its lines.
    assert.deepEqual(
      [
        changes?.segments[1]?.paths,
        changes?.segments[1]?.lines_added,
        changes?.segments[1]?.lines_removed,
      ],
      [['src/orders.ts'], 140, 22],
    );
  });

  test('maps the footprint with nothing it does not give: no class, no caller tied to a file', async (t) => {
    const fake = await start(t);
    assert.deepEqual(available(await brief(fake)).footprint, {
      edited_files: [
        {
          path: 'src/orders.ts',
          status: 'verified',
          lines_added: 140,
          lines_removed: 22,
          module: 'src',
        },
        {
          path: 'src/orders.test.ts',
          status: 'unverified',
          lines_added: 40,
          lines_removed: null,
          module: 'src',
        },
      ],
      modules: [{ id: 'src', name: 'src', files: ['src/orders.ts', 'src/orders.test.ts'] }],
      untouched_callers: ['src/page.tsx', 'src/api.ts', 'src/list.tsx'],
      unverified_dependents: 3,
    });
    delete (stored(fake).brief as Json).move2Footprint;
    assert.equal(available(await brief(fake)).footprint, null);
  });

  test("reads the format Salidium's schema names as well as the one its daemon sends, and neither else", async (t) => {
    const fake = await start(t);
    stored(fake).format = 'salidium.execution-brief';
    available(await brief(fake));
    delete stored(fake).format;
    available(await brief(fake));
    stored(fake).format = 'salidium.session-report';
    assert.deepEqual(reason(await brief(fake)), ['incompatible', 'invalid_document']);
  });

  test('reads a word Salidium added later as unknown, and leaves out a kind of segment it added', async (t) => {
    const fake = await start(t);
    const wire = stored(fake).brief as Json;
    wire.archetype = 'long-haul';
    const [first] = wire.moves as Json[];
    (first as Json).kind = 'aside';
    const segments = (first as Json).segments as Json[];
    (segments[1] as Json).tokenKind = 'artifact';
    (segments[1] as Json).tone = 'glow';
    (segments[1] as Json).checkOutcomes = ['skipped'];
    segments.push({ kind: 'image', ref: 'img:1' });
    const read = available(await brief(fake));
    assert.equal(read.archetype, 'unknown');
    assert.equal(read.moves[0]?.kind, 'unknown');
    assert.equal(read.moves[0]?.segments.length, 4);
    assert.deepEqual(
      [read.moves[0]?.segments[1]?.token, read.moves[0]?.segments[1]?.tone],
      ['unknown', null],
    );
    assert.deepEqual(read.moves[0]?.segments[1]?.check_outcomes, ['unknown']);
  });

  test('refuses a brief that breaks the shape or names another session', async (t) => {
    const fake = await start(t);
    const wire = stored(fake).brief as Json;
    const moves = wire.moves as Json[];
    const [only] = moves;
    wire.moves = [only];
    assert.deepEqual(reason(await brief(fake)), ['incompatible', 'invalid_document']);
    wire.moves = moves;
    const segment = ((moves[0] as Json).segments as Json[])[1] as Json;
    delete segment.epistemic;
    assert.deepEqual(reason(await brief(fake)), ['incompatible', 'invalid_document']);
    segment.epistemic = 'guessed';
    assert.deepEqual(reason(await brief(fake)), ['incompatible', 'invalid_document']);
    segment.epistemic = 'observed';
    (stored(fake).execution as Json).sessionId = 'another-session';
    assert.deepEqual(reason(await brief(fake)), ['incompatible', 'invalid_document']);
  });

  test('says a Salidium before contract 1.2 does not offer the brief, without asking it', async (t) => {
    const fake = await start(t, 1);
    assert.deepEqual(reason(await brief(fake)), ['incompatible', 'brief_not_offered']);
    assert.equal(
      fake.requests.some((request) => request.path.endsWith('/brief')),
      false,
    );
  });

  test("answers not found for a session Salidium holds no brief of, and the report's refusals", async (t) => {
    const fake = await start(t);
    assert.deepEqual(reason(await brief(fake, { kind: 'codex', id: 'unknown-thread' })), [
      'not_found',
      'not_observed',
    ]);
    assert.deepEqual(reason(await brief(fake, { kind: 'mock', id: 'x' })), [
      'unavailable',
      'runtime_not_observed',
    ]);
    const client = new SalidiumClient({
      home: fake.home,
      credential: consumerToken(),
      timeoutMs: 2_000,
    });
    assert.deepEqual(reason(await client.brief(CLAUDE.kind, CLAUDE.id)), [
      'unauthorized',
      'credential_rejected',
    ]);
  });
});
