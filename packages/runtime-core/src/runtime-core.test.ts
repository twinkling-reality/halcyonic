import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { RuntimeDescriptor, RuntimeId } from '@halcyonic/contracts';
import { capabilityProblems, type RuntimeAdapter } from './adapter.ts';
import { createVirtualTime, isAbortError } from './time.ts';

describe('virtual time', () => {
  test('timers fire in time order, then in creation order', async () => {
    const time = createVirtualTime(new Date('2026-09-26T00:00:00.000Z'));
    const fired: string[] = [];
    void time.sleep(20).then(() => fired.push('b'));
    void time.sleep(10).then(() => fired.push('a'));
    void time.sleep(20).then(() => fired.push('c'));
    await time.runUntilIdle();
    assert.deepEqual(fired, ['a', 'b', 'c']);
    assert.equal(time.now().toISOString(), '2026-09-26T00:00:00.020Z');
  });

  test('advance fires only the timers that are due', async () => {
    const time = createVirtualTime(new Date(0));
    const fired: number[] = [];
    void time.sleep(100).then(() => fired.push(100));
    void time.sleep(300).then(() => fired.push(300));
    await time.advance(150);
    assert.deepEqual(fired, [100]);
    assert.equal(time.now().getTime(), 150);
    assert.equal(time.pending(), 1);
  });

  test('an aborted sleep rejects with an AbortError and leaves no timer behind', async () => {
    const time = createVirtualTime(new Date(0));
    const controller = new AbortController();
    const sleeping = time.sleep(1000, controller.signal);
    controller.abort();
    await assert.rejects(sleeping, (error) => isAbortError(error));
    assert.equal(time.pending(), 0);
  });

  test('a loop that never goes idle is reported', async () => {
    const time = createVirtualTime(new Date(0));
    const forever = async () => {
      for (;;) await time.sleep(1);
    };
    void forever();
    await assert.rejects(time.runUntilIdle({ maxSteps: 50 }), /still busy/);
  });
});

describe('runtime adapter capabilities', () => {
  const descriptor = (
    interrupt: boolean,
    modelChoice: RuntimeDescriptor['model_choice'] = 'none',
  ): RuntimeDescriptor => ({
    runtime_id: 'test' as RuntimeId,
    kind: 'test',
    display_name: 'Test',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      interrupt,
    },
    model_choice: modelChoice,
    uses_project_location: false,
  });
  const base = {
    validateStartOptions: () => ({ ok: true }) as const,
    startExecution: async () => ({ native_id: null }),
    close: async () => {},
  };

  test('a declared capability without its method is reported', () => {
    const adapter: RuntimeAdapter = { ...base, descriptor: descriptor(true) };
    assert.deepEqual(capabilityProblems(adapter), [
      'capability interrupt is declared but interrupt is not implemented',
    ]);
  });

  test('a consistent adapter has no problems', () => {
    const adapter: RuntimeAdapter = {
      ...base,
      descriptor: descriptor(true),
      interrupt: async () => {},
    };
    assert.deepEqual(capabilityProblems(adapter), []);
    assert.deepEqual(capabilityProblems({ ...base, descriptor: descriptor(false) }), []);
  });

  test('a model choice without its list is reported, and one with it is consistent', () => {
    assert.deepEqual(capabilityProblems({ ...base, descriptor: descriptor(false, 'listed') }), [
      'model_choice listed is declared but listModels is not implemented',
    ]);
    const listing: RuntimeAdapter = {
      ...base,
      descriptor: descriptor(false, 'listed'),
      listModels: async () => [],
    };
    assert.deepEqual(capabilityProblems(listing), []);
  });
});
