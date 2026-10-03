import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  compileValidator,
  QUESTION_TEXT_LIMIT,
  type QuestionPrompt,
  questionTextLength,
  RUNTIME_EVENT_PAYLOADS,
} from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { capturingLogger } from '../testing/harness.ts';
import { createObservationSink } from './drafts.ts';
import type { Recorder } from './recorder.ts';
import { redaction, TRUNCATED } from './redaction.ts';

const validatePayload = compileValidator(RUNTIME_EVENT_PAYLOADS['runtime.approval.requested']);

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
    // A question's prompt loses it too; its options stay, since an answer names them.
    const option = { label: `Keep ${held}`, description: `Sends ${held}` };
    sink(
      observation('runtime.question.asked', {
        question_id: 'q',
        prompts: [{ key: 'q0', header: null, text: `Use ${held}?`, options: [option] }],
        answerable: true,
      }),
    );
    // The agent's own messages are reported as given (an open question).
    sink(observation('runtime.agent_message', { text: `I will use ${held}` }));
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
        {
          question_id: 'q',
          prompts: [{ key: 'q0', header: null, text: `Use ${MARK}?`, options: [option] }],
          answerable: true,
        },
        { text: `I will use ${held}` },
      ],
    );
  });

  test("are cut to their field's limit after redaction, never before, so no part of a held value is left", () => {
    const { sink, recorded } = recordingSink();
    // As the adapters now pass them: whole, with the held value across where the contract cuts.
    const summary = `${'x'.repeat(1990)}${held} and more`;
    const title = `${'x'.repeat(490)}${held} and more`;
    sink(
      observation('runtime.approval.requested', {
        approval_id: 'a',
        subject: { kind: 'tool_use', tool_name: 'Bash', summary },
      }),
    );
    sink(observation('runtime.tool.started', { tool_call_id: 't', tool_name: 'Bash', title }));
    sink(observation('runtime.test_run.started', { test_run_id: 'x', label: title }));
    sink(
      observation('runtime.test_run.completed', { test_run_id: 'x', outcome: 'failed', summary }),
    );
    const [approval, tool, started, completed] = recorded.map((draft) => draft.payload);
    const cutSummary = `${'x'.repeat(1990)}${MARK}`.slice(0, 2000 - TRUNCATED.length) + TRUNCATED;
    const cutTitle = `${'x'.repeat(490)}${MARK}`.slice(0, 500 - TRUNCATED.length) + TRUNCATED;
    assert.deepEqual(approval, {
      approval_id: 'a',
      subject: { kind: 'tool_use', tool_name: 'Bash', summary: cutSummary },
    });
    assert.deepEqual(tool, { tool_call_id: 't', tool_name: 'Bash', title: cutTitle });
    assert.deepEqual(started, { test_run_id: 'x', label: cutTitle });
    assert.deepEqual(completed, { test_run_id: 'x', outcome: 'failed', summary: cutSummary });
    for (const text of [cutSummary, cutTitle]) {
      assert.ok(!text.includes(held.slice(0, 8)), 'no prefix of the held value');
    }
  });

  test('are measured and cut in code points, as the contract counts, so a marker never pushes one past it', () => {
    // Eight characters, the shortest held value replaced, which its marker lengthens.
    const short = 'pw-12345';
    const { sink, recorded } = recordingSink([{ what: 'TEST_PASS', value: short }]);
    // 2000 code points with the emoji, 2001 UTF-16 units: it fits, until the marker lengthens it.
    const summary = `😀${'x'.repeat(1999 - short.length)}${short}`;
    sink(
      observation('runtime.approval.requested', {
        approval_id: 'a',
        subject: { kind: 'tool_use', tool_name: 'Bash', summary },
      }),
    );
    const prompt = `😀${'y'.repeat(3999 - short.length)}${short}`;
    sink(
      observation('runtime.question.asked', {
        question_id: 'q',
        prompts: [{ key: 'q0', header: null, text: prompt, options: [] }],
        answerable: true,
      }),
    );
    const [approval, question] = recorded.map((draft) => draft.payload);
    const cut = (approval as { subject: { summary: string } }).subject.summary;
    assert.equal(Array.from(cut).length, 2000);
    assert.ok(cut.startsWith('😀') && cut.endsWith(TRUNCATED));
    assert.ok(
      validatePayload({
        approval_id: 'a',
        subject: { kind: 'tool_use', tool_name: 'Bash', summary: cut },
      }).ok,
    );
    // A prompt the marker pushed past its limit is cut, and so can no longer be answered.
    const asked = question as { prompts: { text: string }[]; answerable: boolean };
    assert.equal(Array.from(asked.prompts[0]?.text ?? '').length, 4000);
    assert.equal(asked.answerable, false);
  });

  test('a question is fitted once, here, after redaction: no part of a held value is left, and a cut question cannot be answered', () => {
    const { sink, recorded } = recordingSink();
    const prompt = (key: string, text: string) => ({
      key,
      header: null,
      text,
      options: [],
      multiple: false,
      free_text: true,
      secret: false,
    });
    const ask = (prompts: ReturnType<typeof prompt>[]) => {
      sink(observation('runtime.question.asked', { question_id: 'q', prompts, answerable: true }));
      return recorded.at(-1)?.payload as {
        prompts: ReturnType<typeof prompt>[];
        answerable: boolean;
      };
    };
    // Across the field's limit of 4000, as an adapter now reports it: whole.
    const atField = ask([prompt('q0', `${'x'.repeat(3990)}${held} and more`)]);
    assert.equal(atField.answerable, false);
    assert.equal(
      atField.prompts[0]?.text,
      `${'x'.repeat(3990)}${MARK}`.slice(0, 4000 - TRUNCATED.length) + TRUNCATED,
    );
    // Across the 1000 a question past the 16,000 budget is shortened to.
    const filler = Array.from({ length: 4 }, (_, index) =>
      prompt(`q${index + 1}`, 'y'.repeat(4000)),
    );
    const atBudget = ask([prompt('q0', `${'x'.repeat(990)}${held} and more`), ...filler]);
    assert.equal(atBudget.answerable, false);
    assert.equal(
      atBudget.prompts[0]?.text,
      `${'x'.repeat(990)}${MARK}`.slice(0, 1000 - TRUNCATED.length) + TRUNCATED,
    );
    for (const asked of [atField, atBudget]) {
      assert.ok(!JSON.stringify(asked).includes(held.slice(0, 8)), 'no prefix of the held value');
    }
    // Within every limit until the marker lengthens it: the fitted question stays within the budget.
    const short = 'pw-12345';
    const { sink: shortSink, recorded: shortRecorded } = recordingSink([
      { what: 'TEST_PASS', value: short },
    ]);
    // Four keys of 2 and texts of 3998: exactly the budget.
    const full = Array.from({ length: 4 }, (_, index) =>
      prompt(`q${index}`, `${short}${'z'.repeat(3998 - short.length)}`),
    );
    assert.equal(questionTextLength(full), QUESTION_TEXT_LIMIT);
    shortSink(
      observation('runtime.question.asked', { question_id: 'q', prompts: full, answerable: true }),
    );
    const fittedFull = shortRecorded.at(-1)?.payload as {
      prompts: QuestionPrompt[];
      answerable: boolean;
    };
    assert.ok(questionTextLength(fittedFull.prompts) <= QUESTION_TEXT_LIMIT);
    assert.equal(fittedFull.answerable, false);
    // One that fits stays as it was, and answerable.
    const fits = ask([prompt('q0', 'Which database?')]);
    assert.deepEqual(fits, {
      question_id: 'q',
      prompts: [prompt('q0', 'Which database?')],
      answerable: true,
    });
  });

  test("a question too malformed to fit is the adapter's defect: logged and dropped, never thrown back", () => {
    const { logger, entries } = capturingLogger();
    const recorded: unknown[] = [];
    const sink = createObservationSink(
      { record: (draft: unknown) => recorded.push(draft) } as unknown as Recorder,
      logger,
      { project_id: 'p', workstream_id: 'w', execution_id: 'e' } as unknown as ExecutionContext,
      'r' as never,
      redaction(() => []),
    );
    assert.doesNotThrow(() =>
      sink(observation('runtime.question.asked', { question_id: 'q', prompts: [{ key: 'q0' }] })),
    );
    assert.equal(recorded.length, 0);
    assert.ok(entries.some((entry) => entry.message === 'runtime observation rejected'));
  });
});
