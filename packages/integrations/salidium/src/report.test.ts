import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { compileValidator } from '@halcyonic/contracts';
import { toUnderstanding } from './report.ts';
import { fixture } from './testing/fake-salidium.ts';
import { Understanding } from './understanding.ts';
import {
  validateDiscovery,
  validateReport,
  type WireDiscovery,
  type WireEpistemic,
  type WireReport,
} from './wire.ts';

type Json = Record<string, unknown>;

const validateUnderstanding = compileValidator(Understanding);

function discovery(): WireDiscovery {
  const parsed = validateDiscovery(fixture('consumer-discovery'));
  assert.ok(parsed.ok);
  return parsed.value;
}

function report(document: Json): WireReport {
  const parsed = validateReport(document);
  assert.ok(parsed.ok, JSON.stringify(parsed));
  return parsed.value;
}

function understand(document: Json): Understanding {
  const understanding = toUnderstanding(report(document), discovery());
  const checked = validateUnderstanding(understanding);
  assert.ok(checked.ok, JSON.stringify(checked));
  return understanding;
}

/** Every `epistemic` in the understanding except the two the contract fixes. */
function variableClasses(value: unknown, path = ''): string[] {
  if (typeof value !== 'object' || value === null) return [];
  return Object.entries(value).flatMap(([key, child]) => {
    const at = `${path}/${key}`;
    if (key === 'epistemic')
      return at.endsWith('/coverage/epistemic') || at === '/explanation/epistemic'
        ? []
        : [child as string];
    return variableClasses(child, at);
  });
}

/** Sets every provenance the contract lets vary, all to one class. */
function withEveryClass(document: Json, epistemic: WireEpistemic): Json {
  const copy = structuredClone(document) as {
    verdict: Json;
    latestStatement: Json;
    changes: { files: { reason: Json }[] };
    verification: { latestByMethod: Json[]; statements: Json[] };
    review: { groups: { items: Json[] }[] };
    remaining: { items: Json[] };
  };
  copy.verdict.provenance = epistemic;
  copy.latestStatement.provenance = epistemic;
  for (const file of copy.changes.files) file.reason.provenance = epistemic;
  for (const run of copy.verification.latestByMethod) run.provenance = epistemic;
  for (const line of copy.verification.statements) line.provenance = epistemic;
  for (const group of copy.review.groups)
    for (const item of group.items) item.provenance = epistemic;
  for (const item of copy.remaining.items) item.provenance = epistemic;
  return copy as unknown as Json;
}

describe('mapping a Salidium report onto an understanding', () => {
  test('maps both retained reports onto a valid understanding', () => {
    const verified = understand(fixture('session-report-verified'));
    assert.equal(verified.verdict.headline, '3 files changed, unverified');
    assert.deepEqual(
      verified.changes.files.map((file) => file.path),
      ['src/payments/refunds.ts', 'src/checkout/RetryWorker.ts', 'src/payments/ChargeService.ts'],
    );
    assert.deepEqual(verified.verification.unverified_files, ['src/payments/refunds.ts']);
    assert.equal(verified.explanation.content?.how.root, 'ChargeService.ts');

    const failing = understand(fixture('session-report-failing'));
    assert.equal(failing.verdict.headline, 'Waiting for you');
    assert.deepEqual(failing.verification.latest_by_method[0]?.exit, {
      code: 1,
      observation: 'explicit',
    });
    assert.deepEqual(
      failing.review.groups.map((group) => group.rule),
      ['waiting-permission', 'verification-failed'],
    );
  });

  test('keeps every epistemic class exactly as Salidium stated it', () => {
    const understanding = understand(fixture('session-report-verified'));
    assert.equal(understanding.verdict.epistemic, 'observed');
    assert.equal(understanding.latest_statement?.epistemic, 'reported');
    assert.deepEqual(
      understanding.changes.files.map((file) => [file.coverage.epistemic, file.reason?.epistemic]),
      [
        ['inferred', 'reported'],
        ['inferred', 'reported'],
        ['inferred', 'reported'],
      ],
    );
    assert.deepEqual(
      understanding.verification.latest_by_method.map((run) => run.epistemic),
      ['observed'],
    );
    assert.deepEqual(
      understanding.verification.statements.map((line) => line.epistemic),
      ['reported'],
    );
    assert.deepEqual(
      understanding.review.groups.map((group) => group.items.map((item) => item.epistemic)),
      [['observed'], ['inferred']],
    );
    assert.deepEqual(
      understanding.remaining.items.map((item) => item.epistemic),
      ['planned'],
    );
    assert.equal(understanding.explanation.epistemic, 'explained');
  });

  test('never upgrades a claim: the class Salidium sends is the class Halcyonic shows', () => {
    const classes: WireEpistemic[] = ['observed', 'reported', 'inferred', 'planned', 'explained'];
    for (const epistemic of classes) {
      const understanding = understand(
        withEveryClass(fixture('session-report-verified'), epistemic),
      );
      const shown = variableClasses(understanding);
      assert.equal(shown.length, 10);
      assert.deepEqual(new Set(shown), new Set([epistemic]));
    }
  });

  test('names the Salidium instance, contract and evidence the conclusions came from', () => {
    assert.deepEqual(understand(fixture('session-report-verified')).source, {
      system: 'salidium',
      version: '0.5.0',
      contract: { name: 'salidium.consumer', major: 1, minor: 0 },
      instance_id: '5f0e2b7c9a1d4e3f8b6a0c2d4e6f8a1b',
      generated_at: '2026-09-20T16:20:00.000Z',
      evidence_sequence: 20,
    });
  });

  test('keeps what Salidium does not know as null rather than inventing it', () => {
    const understanding = understand(fixture('session-report-failing'));
    assert.deepEqual(understanding.explanation, {
      status: 'disabled',
      current: false,
      based_on_sequence: null,
      generated_at: null,
      model: null,
      epistemic: 'explained',
      content: null,
    });
    const [file] = understanding.changes.files;
    assert.equal(file?.reason, null);
    assert.equal(file?.coverage.by, null);
    assert.equal(understanding.verification.latest_by_method[0]?.counts?.skipped, null);
  });

  test('carries a model-written explanation as explained, never as evidence', () => {
    const { explanation } = understand(fixture('session-report-verified'));
    assert.equal(explanation.epistemic, 'explained');
    assert.equal(explanation.current, true);
    assert.equal(explanation.based_on_sequence, 19);
    assert.deepEqual(explanation.content?.why.chain, [
      'Two charges for one order',
      'The card is billed twice',
    ]);
    assert.equal(explanation.content?.approach_change, null);
  });

  test('renames the two exit observations Salidium spells with hyphens', () => {
    for (const [wire, shown] of [
      ['inferred-success', 'inferred_success'],
      ['inferred-failure', 'inferred_failure'],
      ['unknown', 'unknown'],
    ]) {
      const document = fixture('session-report-failing') as {
        verification: { latestByMethod: { exit: Json }[] };
      };
      const [run] = document.verification.latestByMethod;
      if (run) run.exit = { code: null, observation: wire };
      const understanding = understand(document as unknown as Json);
      assert.deepEqual(understanding.verification.latest_by_method[0]?.exit, {
        code: null,
        observation: shown,
      });
    }
  });

  test('lets nothing Salidium adds later reach the understanding', () => {
    const document = fixture('session-report-verified');
    const session = document.session as Json;
    const verdict = document.verdict as Json;
    const understanding = understand({
      ...document,
      prompt: 'ADDEDCANARY',
      session: { ...session, title: 'ADDEDCANARY' },
      verdict: { ...verdict, extra: 'ADDEDCANARY' },
    });
    assert.equal(JSON.stringify(understanding).includes('ADDEDCANARY'), false);
  });
});
