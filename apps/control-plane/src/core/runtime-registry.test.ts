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

test("each runtime says for itself whether it can run outside Halcyonic's sandbox and which names it reads (ADR 0057)", () => {
  const registry = new RuntimeRegistry();
  const real = (id: string, facts: Partial<Pick<MockRuntimeAdapter, never>> & object) => {
    const adapter = runtime(id, false);
    Object.defineProperty(adapter, 'descriptor', {
      value: { ...adapter.descriptor, synthetic: false },
    });
    Object.assign(adapter, facts);
    return adapter;
  };
  registry.register(real('inside', { runsOutsideSandbox: () => false }));
  registry.register(
    real('outside', {
      runsOutsideSandbox: () => true,
      ownsEnvironmentName: (name: string) => name.startsWith('OUTSIDE_'),
    }),
  );
  // One that does not say is taken as can, never as inside; a synthetic one touches no files.
  registry.register(real('silent', {}));
  registry.register(runtime('mock', false));
  assert.deepEqual(
    registry.outsideSandbox().map((d) => d.runtime_id),
    ['outside', 'silent'],
  );
  assert.equal(registry.ownsEnvironmentName('OUTSIDE_TOKEN'), true);
  assert.equal(registry.ownsEnvironmentName('API_KEY'), false);
});
