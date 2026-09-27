import assert from 'node:assert/strict';
import { randomBytes, randomUUID } from 'node:crypto';
import { readFileSync, statSync } from 'node:fs';
import { describe, test } from 'node:test';
import { compileValidator } from '@halcyonic/contracts';
import { SeorakClient } from './client.ts';
import { ESTIMATED_COST_NOTE, EvaluationResult } from './evaluation.ts';

/**
 * The real local plane, opt in: set HALCYONIC_SEORAK_CREDENTIAL_FILE to a file, mode 600, holding
 * an integration credential the owner issued for the audience http://127.0.0.1:4317/api/v1 with
 * the sessions:read and replay:read scopes. Set HALCYONIC_SEORAK_SESSION_ID as well to the id of a
 * Claude Code session Seorak captured, to evaluate it.
 *
 * The credential is read in this process and sent only to the plane, never printed. The file makes
 * at most six requests, far inside the credential's 60 a minute.
 */
const CREDENTIAL_FILE = process.env.HALCYONIC_SEORAK_CREDENTIAL_FILE;
const SESSION_ID = process.env.HALCYONIC_SEORAK_SESSION_ID;
const ORIGIN = 'http://127.0.0.1:4317';

const validateResult = compileValidator(EvaluationResult);

function credential(): string {
  assert.ok(CREDENTIAL_FILE);
  assert.equal(statSync(CREDENTIAL_FILE).mode & 0o077, 0, 'others can read the credential file');
  return readFileSync(CREDENTIAL_FILE, 'utf8').trim();
}

type Declared = true | readonly [Declared] | { readonly [key: string]: Declared };

const RANGE = { from: true, through: true } as const;
const ENVELOPE = {
  apiVersion: true,
  availability: { state: true, reason: true },
  coverage: {
    requested: RANGE,
    observed: RANGE,
    matchedSessionCount: true,
    includedSessionCount: true,
    complete: true,
    omissions: true,
  },
  freshness: { state: true, generatedAt: true, dataThrough: true, staleAt: true },
} as const;
const TARGET = { kind: true, period: RANGE, projectRef: true, sessionRef: true } as const;

/** Every key `@seorak/types` 0.2.0 declares on the three documents an evaluation reads. */
const DECLARED: Record<'resolve' | 'outcome' | 'lens', Declared> = {
  resolve: {
    ...ENVELOPE,
    session: {
      sessionRef: true,
      projectRef: true,
      agent: true,
      status: true,
      startedAt: true,
      endedAt: true,
      elapsedSeconds: true,
      toolCallCount: true,
      promptCount: true,
      costUsd: true,
      launcher: true,
    },
  },
  outcome: {
    ...ENVELOPE,
    sessionRef: true,
    outcome: {
      commitsLanded: true,
      uncommitted: {
        filesTouched: true,
        linesAdded: true,
        linesRemoved: true,
        generatedLinesExcluded: true,
      },
      lineSurvival: {
        rung: true,
        fate: true,
        rate: true,
        linesAuthored: true,
        linesSurviving: true,
        commitsChecked: true,
      },
      errorCount: true,
      firstErrorAt: true,
      endReason: true,
    },
  },
  lens: {
    ...ENVELOPE,
    result: {
      lens: true,
      level: true,
      target: TARGET,
      headline: true,
      rows: [
        {
          resultRef: true,
          label: true,
          metrics: [{ key: true, label: true, value: true, unit: true, tone: true }],
          share: true,
          elapsedMs: true,
          target: TARGET,
        },
      ],
      emptyReason: true,
      loadedSessionCount: true,
      momentCount: true,
      nextCursor: true,
    },
  },
};

/** The paths of keys in `value` that `declared` does not name. */
function undeclared(value: unknown, declared: Declared, path = ''): string[] {
  if (declared === true || value === null || typeof value !== 'object') return [];
  if (Array.isArray(declared))
    return Array.isArray(value)
      ? value.flatMap((item, index) => undeclared(item, declared[0], `${path}/${index}`))
      : [];
  return Object.entries(value).flatMap(([key, child]) => {
    const inner = (declared as Record<string, Declared>)[key];
    return inner === undefined ? [`${path}/${key}`] : undeclared(child, inner, `${path}/${key}`);
  });
}

describe('the real Seorak local plane', {
  skip: CREDENTIAL_FILE
    ? false
    : 'set HALCYONIC_SEORAK_CREDENTIAL_FILE to a Seorak integration credential file to run',
  timeout: 30_000,
}, () => {
  test('refuses a resolve that carries no credential', async () => {
    const response = await fetch(`${ORIGIN}/api/v1/sessions/resolve`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      redirect: 'error',
    });
    assert.equal(response.status, 401);
    assert.match(response.headers.get('www-authenticate') ?? '', /^Bearer\b/);
  });

  test('says not_found for a session it has not captured', async () => {
    const result = await new SeorakClient().evaluate('claude-agent', randomUUID(), {
      credential: credential(),
    });
    assert.deepEqual(
      result.availability === 'available' ? 'available' : [result.availability, result.reason.code],
      ['not_found', 'not_captured'],
    );
  });

  test('refuses a credential it did not issue', async () => {
    const stranger = `srkx_${randomBytes(32).toString('base64url')}`;
    const result = await new SeorakClient().evaluate('claude-agent', randomUUID(), {
      credential: stranger,
    });
    assert.deepEqual(
      result.availability === 'available' ? 'available' : [result.availability, result.reason.code],
      ['unauthorized', 'credential_rejected'],
    );
  });

  test('evaluates a captured session, reporting any key the published types do not declare', {
    skip: SESSION_ID ? false : 'set HALCYONIC_SEORAK_SESSION_ID to a captured Claude Code session',
  }, async (t) => {
    assert.ok(SESSION_ID);
    // Keep each answer's body to compare with the published types. Request headers are not kept.
    const bodies: [string, unknown][] = [];
    const original = globalThis.fetch;
    globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
      const response = await original(input, init);
      const url = input instanceof Request ? input.url : String(input);
      const body: unknown = await response
        .clone()
        .json()
        .catch(() => undefined);
      bodies.push([new URL(url).pathname, body]);
      return response;
    }) as typeof fetch;
    t.after(() => {
      globalThis.fetch = original;
    });

    const result = await new SeorakClient().evaluate('claude-agent', SESSION_ID, {
      credential: credential(),
    });
    globalThis.fetch = original;

    const document = (suffix: string) => bodies.find(([path]) => path.endsWith(suffix))?.[1];
    for (const [name, suffix] of [
      ['resolve', '/resolve'],
      ['outcome', '/outcome'],
      ['lens', '/replay/verification'],
    ] as const) {
      const extra = undeclared(document(suffix), DECLARED[name]);
      t.diagnostic(`${name}: keys beyond the published types: ${extra.join(', ') || 'none'}`);
    }
    const lens = document('/replay/verification') as {
      result: {
        rows: { label: string; metrics: { key: string; unit: string; value: unknown }[] }[];
      };
    } | null;
    for (const row of lens?.result?.rows ?? [])
      t.diagnostic(
        `verification row ${JSON.stringify(row.label)}: ${row.metrics
          .map((metric) => `${metric.key} (${metric.unit}, ${typeof metric.value})`)
          .join(', ')}`,
      );

    assert.ok(validateResult(result).ok);
    if (result.availability !== 'available') assert.fail(JSON.stringify(result));
    const { cost, outcome, verification } = result.evaluation;
    for (const [name, part] of Object.entries({ cost, outcome, verification }))
      t.diagnostic(
        `${name}: ${part.availability.state} (${part.availability.reason ?? 'no reason'}), ${part.freshness.state}, coverage ${part.coverage.complete ? 'complete' : `incomplete: ${part.coverage.omissions.join(', ')}`}`,
      );
    // Kinds, not values: which outcome fields Seorak has yet, and the form of its instants.
    const measure = (document('/outcome') as { outcome: Record<string, unknown> | null }).outcome;
    const kinds = Object.entries(measure ?? {}).map(
      ([key, value]) => `${key} ${value === null ? 'null' : typeof value}`,
    );
    t.diagnostic(`outcome fields: ${kinds.join(', ') || 'no outcome'}`);
    t.diagnostic(
      `verification lens: ${verification.lens?.by_kind.length ?? 'no'} kinds, empty reason ${JSON.stringify(verification.lens?.empty_reason ?? null)}`,
    );
    const instants =
      JSON.stringify(bodies).match(
        /"(generatedAt|dataThrough|staleAt|startedAt|endedAt|firstErrorAt)":"[^"]*"/g,
      ) ?? [];
    const other = instants.filter(
      (pair) => !/:"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z"$/.test(pair),
    );
    t.diagnostic(
      `${instants.length} instants, not in toISOString form: ${other.map((pair) => pair.split(':')[0]).join(', ') || 'none'}`,
    );
    assert.equal(cost.note, ESTIMATED_COST_NOTE);
    assert.equal(bodies.length, 3);
  });
});
