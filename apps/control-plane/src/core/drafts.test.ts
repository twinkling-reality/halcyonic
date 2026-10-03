import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { capturingLogger } from '../testing/harness.ts';
import { createObservationSink } from './drafts.ts';
import type { Recorder } from './recorder.ts';
import { redaction } from './redaction.ts';

const MARK = '[redacted: OpenCode server password]';

const held = 'opencode-server-password-1';

function recordingSink(secrets = [{ what: 'OpenCode server password', value: held }]) {
  const recorded: { payload: unknown }[] = [];
  const recorder = {
    record: (draft: { payload: unknown }) => recorded.push(draft),
  } as unknown as Recorder;
  const execution = {
    project_id: 'p',
    workstream_id: 'w',
    execution_id: 'e',
  } as unknown as ExecutionContext;
  const sink = createObservationSink(
    recorder,
    capturingLogger().logger,
    execution,
    'r' as never,
    redaction(() => secrets),
  );
  return { sink, recorded };
}

const observation = (type: string, payload: unknown) =>
  ({
    type,
    payload,
    native_event_id: null,
    sequence: 1,
    occurred_at: '2026-10-02T00:00:00.000Z',
    provenance: 'reported',
  }) as unknown as RuntimeObservation;

describe("a runtime's observations", () => {
  test('lose credentials from a failure and a lost connection before they are journaled, and nothing else', () => {
    const { sink, recorded } = recordingSink();
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
    // At the limit already: "[redacted]" is longer than the short credential it replaces.
    sink(observation('runtime.connection.lost', { reason: `${'x'.repeat(1985)} Bearer a1b2c3d4` }));
    assert.deepEqual(
      recorded.map((draft) => draft.payload),
      [
        {
          turn_id: 't',
          error: {
            code: 'provider_error',
            message: `401 for Bearer [redacted]; password ${MARK}; asked "Fix the login page"`,
          },
        },
        { reason: `https://u:${MARK}@127.0.0.1:4096 closed` },
        // The agent's own account is reported as given; only error text is taken from.
        { text: `the agent says ${held}` },
        // Cut to the journal's limit after redaction lengthened it.
        { reason: `${'x'.repeat(1985)} Bearer [redac…` },
      ],
    );
    assert.equal((recorded[3]?.payload as { reason?: string } | undefined)?.reason?.length, 2000);
  });

  test('lose only exact copies of what Halcyonic holds from what a person reads to decide', () => {
    const { sink, recorded } = recordingSink();
    // Credential shapes that are not held stay: the person approves the command as the agent gave it.
    const command = `curl -H "Authorization: Bearer abc123def456" -u deploy:${held} https://user:pw1@example.test/ --data Zx9Yw8Vu7Ts6Rq5Po4Nm3Lk2Ji1Hg0Fe9Dc8 sk-proj-AbCdEf0123456789xyz`;
    const expected = command.replace(held, MARK);
    sink(
      observation('runtime.approval.requested', {
        approval_id: 'a',
        subject: { kind: 'tool_use', tool_name: 'Bash', summary: command },
      }),
    );
    sink(
      observation('runtime.tool.started', { tool_call_id: 't', tool_name: 'Bash', title: command }),
    );
    sink(
      observation('runtime.tool.started', { tool_call_id: 'u', tool_name: 'Read', title: null }),
    );
    sink(observation('runtime.test_run.started', { test_run_id: 'x', label: command }));
    sink(
      observation('runtime.test_run.completed', {
        test_run_id: 'x',
        outcome: 'failed',
        summary: `1 failed: expected ${held} to equal token abc123def456`,
      }),
    );
    // A question is the agent's own account, and so stays as given.
    sink(
      observation('runtime.question.asked', {
        question_id: 'q',
        prompts: [{ text: `Use ${held}?` }],
        answerable: true,
      }),
    );
    assert.deepEqual(
      recorded.map((draft) => draft.payload),
      [
        {
          approval_id: 'a',
          subject: { kind: 'tool_use', tool_name: 'Bash', summary: expected },
        },
        { tool_call_id: 't', tool_name: 'Bash', title: expected },
        { tool_call_id: 'u', tool_name: 'Read', title: null },
        { test_run_id: 'x', label: expected },
        {
          test_run_id: 'x',
          outcome: 'failed',
          summary: `1 failed: expected ${MARK} to equal token abc123def456`,
        },
        { question_id: 'q', prompts: [{ text: `Use ${held}?` }], answerable: true },
      ],
    );
  });

  test("are cut to their field's limit only when redaction lengthened them past it", () => {
    // Eight characters, the shortest held value replaced, which its marker lengthens.
    const short = 'pw-12345';
    const { sink, recorded } = recordingSink([{ what: 'TEST_PASS', value: short }]);
    const fitted = `${'x'.repeat(500 - short.length)}${short}`;
    const tooLong = `${'y'.repeat(501 - short.length)}${short}`;
    sink(
      observation('runtime.tool.started', { tool_call_id: 't', tool_name: 'Bash', title: fitted }),
    );
    sink(
      observation('runtime.tool.started', { tool_call_id: 'u', tool_name: 'Bash', title: tooLong }),
    );
    const [first, second] = recorded.map((draft) => (draft.payload as { title: string }).title);
    assert.equal(first?.length, 500);
    assert.ok(first?.endsWith('x[redact…'), first?.slice(-12));
    // Already past the contract's limit: left long, for the recorder to refuse as it did before.
    assert.equal(second, `${'y'.repeat(501 - short.length)}[redacted: TEST_PASS]`);
  });
});
