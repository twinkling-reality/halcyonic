import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { EventEnvelope } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import { capturingLogger } from '../testing/harness.ts';
import { InstructionQueue } from './instruction-queue.ts';
import { EventPublisher } from './publisher.ts';

// The queue's own rules on each event (ADR 0056); the whole flow runs with a real control plane in
// auto-fix.test.ts ("instructions held for a runtime that cannot steer").

const EXECUTION = 'e-1';
const OTHER = 'e-2';

function setup(live = true) {
  const waiting = new Map<string, string[]>([
    [EXECUTION, ['c-1', 'c-2']],
    [OTHER, ['c-9']],
  ]);
  const delivered: string[] = [];
  const failed: [string, string][] = [];
  const projection = {
    execution: (id: string) => {
      const queued = waiting.get(id) ?? [];
      return queued.length === 0
        ? { queued_instructions: undefined }
        : { queued_instructions: queued.map((command_id) => ({ command_id, queued_at: 'now' })) };
    },
    executions: () => [
      { execution_id: EXECUTION, project_id: 'p-1' },
      { execution_id: OTHER, project_id: 'p-2' },
    ],
  } as unknown as Projection;
  const commands = {
    deliverQueued: (id: string) => {
      delivered.push(id);
      return true;
    },
    failQueued: (id: string, code: string) => {
      failed.push([id, code]);
      for (const [execution, queued] of waiting)
        waiting.set(
          execution,
          queued.filter((each) => each !== id),
        );
    },
  };
  const publisher = new EventPublisher(capturingLogger().logger);
  new InstructionQueue({ projection, commands, live }).attach(publisher);
  const publish = async (event: Record<string, unknown>) => {
    publisher.publish({
      position: 1,
      event: event as unknown as EventEnvelope,
      changes: {} as never,
    });
    await new Promise((resolve) => setImmediate(resolve));
  };
  return { delivered, failed, publish };
}

describe('the instruction queue', () => {
  test('a completed turn sends the first one waiting, only the first', async () => {
    const { delivered, failed, publish } = setup();
    await publish({ event_type: 'runtime.turn.completed', execution_id: EXECUTION });
    assert.deepEqual(delivered, ['c-1']);
    assert.deepEqual(failed, []);
  });

  test('a failed turn, a cut one, a lost session, a Stop and a restore each fail all waiting, with why', async () => {
    const cases: [Record<string, unknown>, string, string[]][] = [
      [
        { event_type: 'runtime.turn.failed', execution_id: EXECUTION },
        'queued_turn_failed',
        ['c-1', 'c-2'],
      ],
      [
        { event_type: 'runtime.turn.interrupted', execution_id: EXECUTION },
        'queued_stopped',
        ['c-1', 'c-2'],
      ],
      [
        { event_type: 'execution.state_unknown', execution_id: EXECUTION },
        'queued_session_lost',
        ['c-1', 'c-2'],
      ],
      [
        {
          event_type: 'command.accepted',
          payload: {
            command: { command_type: 'execution.interrupt', payload: { execution_id: EXECUTION } },
          },
        },
        'queued_stopped',
        ['c-1', 'c-2'],
      ],
      [
        {
          event_type: 'command.accepted',
          payload: { command: { command_type: 'project.restore', payload: { project_id: 'p-2' } } },
        },
        'queued_files_restored',
        ['c-9'],
      ],
    ];
    for (const [event, code, ids] of cases) {
      const { delivered, failed, publish } = setup();
      await publish(event);
      assert.deepEqual(
        failed,
        ids.map((id) => [id, code]),
        code,
      );
      assert.deepEqual(delivered, [], 'nothing sent after it');
    }
  });

  test("a fixture's journal is never acted on", async () => {
    const { delivered, failed, publish } = setup(false);
    await publish({ event_type: 'runtime.turn.completed', execution_id: EXECUTION });
    await publish({ event_type: 'runtime.turn.failed', execution_id: EXECUTION });
    assert.deepEqual([delivered, failed], [[], []]);
  });
});
