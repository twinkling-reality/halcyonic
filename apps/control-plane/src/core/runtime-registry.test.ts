import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { RuntimeDescriptor, RuntimeId } from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { RuntimeRegistry } from './runtime-registry.ts';

/** A mock runtime described as one that steers or not, and claiming `holds_instructions` or not. */
function runtime(id: string, steers: boolean, claims = false): MockRuntimeAdapter {
  const adapter = new MockRuntimeAdapter({ scenarios: SCENARIOS, runtimeId: id as RuntimeId });
  const descriptor: RuntimeDescriptor = {
    ...adapter.descriptor,
    capabilities: { ...adapter.descriptor.capabilities, instruct_while_running: steers },
    ...(claims && { holds_instructions: true }),
  };
  Object.defineProperty(adapter, 'descriptor', { value: descriptor });
  return adapter;
}

test('the host, never an adapter, says which runtimes hold an instruction for the turn to end (ADR 0056)', () => {
  const registry = new RuntimeRegistry();
  registry.register(runtime('steers', true, true));
  registry.register(runtime('cannot-steer', false));
  const holds = () =>
    Object.fromEntries(registry.descriptors().map((d) => [d.runtime_id, d.holds_instructions]));
  // Where the queue does not run, as on a recording's journal: none, whatever an adapter says.
  assert.deepEqual(holds(), { steers: undefined, 'cannot-steer': undefined });
  registry.holdsInstructions = true;
  assert.deepEqual(holds(), { steers: undefined, 'cannot-steer': true });
  assert.equal(registry.get('cannot-steer')?.holds_instructions, true);
  assert.equal(registry.get('steers')?.holds_instructions, undefined);
});

test('a control plane on the person own journal says it, as its admission holds the instruction', (t) => {
  const { controlPlane } = createTestControlPlane({
    adapters: () => [runtime('cannot-steer', false), runtime('steers', true)],
  });
  t.after(() => controlPlane.close());
  assert.equal(controlPlane.journal.info.origin, 'live');
  assert.deepEqual(
    controlPlane.registry.descriptors().map((d) => [d.runtime_id, d.holds_instructions]),
    [
      ['cannot-steer', true],
      ['steers', undefined],
    ],
  );
});
