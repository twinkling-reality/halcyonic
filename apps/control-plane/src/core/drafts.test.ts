import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { capturingLogger } from '../testing/harness.ts';
import { createObservationSink } from './drafts.ts';
import type { Recorder } from './recorder.ts';
import { redactSecrets } from './redaction.ts';

describe("a runtime's observations", () => {
  test('lose credentials from a failure and a lost connection before they are journaled, and nothing else', () => {
    const recorded: { payload: unknown }[] = [];
    const recorder = {
      record: (draft: { payload: unknown }) => recorded.push(draft),
    } as unknown as Recorder;
    const execution = {
      project_id: 'p',
      workstream_id: 'w',
      execution_id: 'e',
    } as unknown as ExecutionContext;
    const held = 'opencode-server-password-1';
    const sink = createObservationSink(
      recorder,
      capturingLogger().logger,
      execution,
      'r' as never,
      (text) => redactSecrets(text, [held]),
    );
    const observation = (type: string, payload: unknown) =>
      ({
        type,
        payload,
        native_event_id: null,
        sequence: 1,
        occurred_at: '2026-10-02T00:00:00.000Z',
        provenance: 'reported',
      }) as unknown as RuntimeObservation;
    sink(
      observation('runtime.turn.failed', {
        turn_id: 't',
        error: {
          code: 'provider_error',
          message: `401 for Bearer abc123def456ghi; password ${held}; asked "Fix the login page"`,
        },
      }),
    );
    sink(
      observation('runtime.connection.lost', { reason: `https://u:${held}@127.0.0.1:4096 closed` }),
    );
    sink(observation('runtime.message.completed', { text: `the agent says ${held}` }));
    assert.deepEqual(
      recorded.map((draft) => draft.payload),
      [
        {
          turn_id: 't',
          error: {
            code: 'provider_error',
            message: '401 for Bearer [redacted]; password [redacted]; asked "Fix the login page"',
          },
        },
        { reason: 'https://[redacted]@127.0.0.1:4096 closed' },
        // The agent's own account is reported as given; only error text is taken from.
        { text: `the agent says ${held}` },
      ],
    );
  });
});
