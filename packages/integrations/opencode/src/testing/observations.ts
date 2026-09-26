import assert from 'node:assert/strict';
import {
  type ExecutionId,
  type ProjectId,
  parseEventEnvelope,
  type RuntimeId,
  type WorkstreamId,
} from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';

export const TEST_EXECUTION: ExecutionContext = {
  execution_id: '01920000-0000-7000-8000-000000000003' as ExecutionId,
  workstream_id: '01920000-0000-7000-8000-000000000002' as WorkstreamId,
  project_id: '01920000-0000-7000-8000-000000000001' as ProjectId,
};

/**
 * Asserts that observations would be journaled as they are: each forms a contract-valid event
 * the way the control plane builds one, agent text is `reported`, and native sequences only grow.
 */
export function assertValidObservations(
  observations: readonly RuntimeObservation[],
  execution: ExecutionContext = TEST_EXECUTION,
): void {
  let last = -1;
  for (const [index, observation] of observations.entries()) {
    const parsed = parseEventEnvelope({
      schema_version: 1,
      event_id: `01920000-0000-7000-8000-${String(index + 100).padStart(12, '0')}`,
      event_type: observation.type,
      ...execution,
      source: { kind: 'runtime', runtime_id: 'opencode' as RuntimeId },
      source_native_id: observation.native_event_id,
      sequence: observation.sequence,
      occurred_at: observation.occurred_at,
      ingested_at: observation.occurred_at,
      correlation_id: null,
      causation_id: null,
      provenance: observation.provenance,
      payload: observation.payload,
    });
    assert.ok(
      parsed.ok,
      `${observation.type}: ${JSON.stringify(parsed.ok ? null : parsed.issues)}`,
    );
    if (observation.type === 'runtime.agent_message') {
      assert.equal(observation.provenance.epistemic, 'reported');
    }
    if (observation.sequence !== null) {
      assert.ok(observation.sequence > last, `sequence ${observation.sequence} after ${last}`);
      last = observation.sequence;
    }
  }
}
