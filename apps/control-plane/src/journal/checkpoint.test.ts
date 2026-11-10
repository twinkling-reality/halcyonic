import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { deserialize, serialize } from 'node:v8';
import type { StoredEvent, WorkstreamId } from '@halcyonic/contracts';
import { Projection, type ProjectionState } from '@halcyonic/domain';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { parseTrace } from '../fixtures/trace.ts';
import { capturingLogger, createTestControlPlane, SCENARIOS } from '../testing/harness.ts';

const directory = mkdtempSync(join(tmpdir(), 'halcyonic-checkpoint-'));
after(() => rmSync(directory, { recursive: true, force: true }));
const TRACES = fileURLToPath(new URL('../../../../fixtures/traces/', import.meta.url));

/** A journal with every mock scenario run in a task of its own, some waiting, some finished. */
async function richJournal(path: string, checkpointEvery?: number) {
  const harness = createTestControlPlane({
    path,
    ...(checkpointEvery !== undefined && { checkpointEvery }),
  });
  const { controlPlane, commands } = harness;
  for (const scenario of SCENARIOS.keys()) {
    const project = controlPlane.commands.submit(commands.createProject(scenario), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const workstream = { ...(DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[0]), scenario };
    const created = controlPlane.commands.submit(
      commands.createWorkstream(project.command.result.project_id, workstream),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    controlPlane.commands.submit(
      commands.startExecution(created.command.result.workstream_id as WorkstreamId, workstream),
      'internal',
    );
  }
  await harness.time.advance(60_000);
  return harness;
}

function fold(events: readonly StoredEvent[], projection = new Projection()): Projection {
  for (const stored of events) projection.apply(stored);
  return projection;
}

function codes(entries: ReturnType<typeof capturingLogger>['entries']): string[] {
  return entries
    .filter((entry) => entry.message === 'checkpoint not taken; folding the whole journal')
    .map((entry) => String((entry.context as { reason?: unknown }).reason));
}

describe('a checkpoint of the projection (ADR 0050)', () => {
  test('a state taken at any position, restored and folded on, equals the whole fold: every field, every view', async () => {
    const harness = await richJournal(join(directory, 'rich.db'));
    const sources: [string, StoredEvent[]][] = [['scenarios', [...harness.journal.readAll()]]];
    await harness.controlPlane.close();
    for (const name of readdirSync(TRACES).filter((file) => file.endsWith('.jsonl'))) {
      const events = parseTrace(readFileSync(join(TRACES, name), 'utf8'), name);
      sources.push([name, events.map((event, index) => ({ position: index + 1, event }))]);
    }
    for (const [name, events] of sources) {
      assert.ok(events.length > 40, `${name} has events to fold`);
      const whole = fold(events);
      const step = Math.max(1, Math.floor(events.length / 25));
      for (let at = 1; at < events.length; at += step) {
        const restored = new Projection();
        restored.restoreState(
          deserialize(serialize(fold(events.slice(0, at)).exportState())) as ProjectionState,
        );
        assert.equal(restored.position, at);
        assert.throws(
          () => restored.restoreState(whole.exportState()),
          /before it applies anything/,
        );
        fold(events.slice(at), restored);
        // Every field, by value: v8 may encode equal strings and numbers differently after a round trip.
        assert.deepStrictEqual(
          restored.exportState(),
          whole.exportState(),
          `${name} at ${at}: the state`,
        );
        assert.deepEqual(restored.projects(), whole.projects(), `${name} at ${at}: projects`);
        assert.deepEqual(restored.workstreams(), whole.workstreams());
        assert.deepEqual(restored.executions(), whole.executions());
        assert.deepEqual(restored.commands(1_000_000), whole.commands(1_000_000));
        assert.deepEqual(restored.pendingCommands(), whole.pendingCommands());
        for (const project of whole.projects()) {
          assert.equal(
            restored.projectBusy(project.project_id),
            whole.projectBusy(project.project_id),
          );
        }
      }
    }
  });

  test('the state holds every field the projection declares, so none is forgotten', () => {
    const source = readFileSync(
      fileURLToPath(new URL(import.meta.resolve('@halcyonic/domain'))).replace(
        /index\.ts$/,
        'projection.ts',
      ),
      'utf8',
    );
    const body = source.slice(source.indexOf('export class Projection'));
    const declared = [...body.matchAll(/^ {2}(?:readonly )?#(\w+)\s*[=:]/gm)]
      .map((match) => match[1] as string)
      // Options, and cached view functions built from the state: not state.
      .filter(
        (name) =>
          ![
            'reportsToolActivity',
            'onCheckVisited',
            'view',
            'workstreamView',
            'projectView',
          ].includes(name),
      );
    const exported = Object.keys(new Projection().exportState()).filter((key) => key !== 'format');
    assert.deepEqual([...exported].sort(), [...new Set(declared)].sort());
  });

  test('a start after a clean close restores the checkpoint and folds only the events after it', async () => {
    const path = join(directory, 'restored.db');
    const first = await richJournal(path);
    const before = first.controlPlane.snapshot();
    const head = first.journal.head();
    await first.controlPlane.close();

    const { logger, entries } = capturingLogger();
    const second = createTestControlPlane({ path, time: first.time, logger });
    const rebuilt = entries.find((entry) => entry.message === 'state rebuilt from journal');
    assert.deepEqual(rebuilt?.context, {
      journal_id: first.journal.info.journal_id,
      events: 0,
      position: head,
      checkpoint: `restored at position ${head}`,
    });
    assert.deepEqual(
      { ...second.controlPlane.snapshot(), runtimes: [] },
      { ...before, runtimes: [] },
    );
    await second.controlPlane.close();
  });

  test('while it runs, a checkpoint is written every so many events', async () => {
    const path = join(directory, 'periodic.db');
    const harness = await richJournal(path, 40);
    await new Promise<void>((resolve) => setImmediate(resolve));
    const row = new DatabaseSync(path, { readOnly: true })
      .prepare('SELECT position FROM projection_checkpoint')
      .get() as { position: number } | undefined;
    assert.ok(row !== undefined && row.position >= 40, 'written before the close');
    await harness.controlPlane.close();
  });

  test('a checkpoint that does not match the journal is never taken: the start folds the whole journal', async () => {
    const tampers: [string, string, RegExp][] = [
      [
        'other code',
        `UPDATE projection_checkpoint SET code_identity = 'an older build'`,
        /other code/,
      ],
      [
        'another journal',
        `UPDATE projection_checkpoint SET journal_id = 'another'`,
        /another journal/,
      ],
      [
        'damaged bytes',
        `UPDATE projection_checkpoint SET state = zeroblob(length(state))`,
        /damaged/,
      ],
      [
        'unreadable bytes, their hash rewritten to match',
        `UPDATE projection_checkpoint SET state = x'ff00ff', state_sha256 = '${sha256Hex(Buffer.from([0xff, 0x00, 0xff]))}'`,
        /could not be read/,
      ],
      [
        'past the head',
        'UPDATE projection_checkpoint SET position = position + 10',
        /past the journal/,
      ],
      [
        'another event there',
        `UPDATE projection_checkpoint SET event_id = 'not-this-one'`,
        /another event/,
      ],
    ];
    for (const [what, sql, reason] of tampers) {
      const path = join(directory, `tampered-${what.replaceAll(' ', '-')}.db`);
      const first = await richJournal(path);
      const before = first.controlPlane.snapshot();
      const head = first.journal.head();
      await first.controlPlane.close();
      const db = new DatabaseSync(path);
      db.exec(sql);
      db.close();

      const { logger, entries } = capturingLogger();
      const second = createTestControlPlane({ path, time: first.time, logger });
      const reasons = codes(entries);
      assert.equal(reasons.length, 1, what);
      assert.match(reasons[0] ?? '', reason, what);
      const rebuilt = entries.find((entry) => entry.message === 'state rebuilt from journal');
      assert.equal(
        (rebuilt?.context as { events?: number } | undefined)?.events,
        head,
        `${what}: folded whole`,
      );
      assert.deepEqual(
        { ...second.controlPlane.snapshot(), runtimes: [] },
        { ...before, runtimes: [] },
        what,
      );
      await second.controlPlane.close();
    }
  });

  test('a state of another shape is refused before anything is taken', () => {
    const projection = new Projection();
    const state = new Projection().exportState();
    for (const broken of [
      { ...state, format: state.format + 1 },
      { ...state, commands: [] },
      { ...state, position: -1 },
      { ...state, devices: { devices: [], byCredential: new Map() } },
    ]) {
      assert.throws(() => projection.restoreState(broken as unknown as ProjectionState));
    }
    assert.equal(projection.position, 0);
    projection.restoreState(state);
  });

  test('a migration drops the checkpoint: a start after it folds the whole journal', async () => {
    const path = join(directory, 'migrated.db');
    const first = await richJournal(path);
    await first.controlPlane.close();
    const db = new DatabaseSync(path);
    assert.ok(db.prepare('SELECT 1 FROM projection_checkpoint').get() !== undefined);
    db.exec('PRAGMA user_version = 5');
    db.close();
    const { logger, entries } = capturingLogger();
    const second = createTestControlPlane({ path, time: first.time, logger });
    assert.deepEqual(codes(entries), [], 'none left to refuse');
    const rebuilt = entries.find((entry) => entry.message === 'state rebuilt from journal');
    assert.equal((rebuilt?.context as { checkpoint?: unknown } | undefined)?.checkpoint, null);
    await second.controlPlane.close();
  });
});

function sha256Hex(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex');
}
