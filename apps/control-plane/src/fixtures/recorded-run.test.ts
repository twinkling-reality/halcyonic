import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  compileValidator,
  type EventEnvelope,
  parseEventEnvelope,
  type RuntimeId,
  ServerMessage,
} from '@halcyonic/contracts';
import { END_HOLD_MS } from './demonstration.ts';
import {
  JUDGE_MUST_NOT_READ,
  MAX_GAP_MS,
  RECORDED_CLIENT,
  RECORDED_MODEL,
  RECORDED_RUNTIME_ID,
  RECORDED_RUNTIME_KIND,
  RECORDED_RUNTIME_NAME,
  type RecordedRun,
  RecordedRunError,
  type RecordedRunOptions,
  recordRecordedRun,
  sanitizeRecordedRun,
} from './recorded-run.ts';
import { parseTrace } from './trace.ts';

const ROOT = new URL('../../../../', import.meta.url);
const HOME = '/Users/someone';
const PROJECT = `${HOME}/work/storefront`;
const OPTIONS: RecordedRunOptions = { projectRoot: PROJECT, home: HOME, deny: ['Pemberton'] };
const validateServerMessage = compileValidator(ServerMessage);

/**
 * A committed mock trace dressed, for these tests only, as a real run would be journaled: a
 * runtime that is not synthetic and names its vendor, the project's folder in its instructions
 * and directory, and a model and client of its own. Synthetic text; no real run is read here.
 */
function realShaped(): EventEnvelope[] {
  const text = readFileSync(
    fileURLToPath(new URL('fixtures/traces/multiple_workstreams.jsonl', ROOT)),
    'utf8',
  );
  return parseTrace(text, 'multiple_workstreams.jsonl').map((event) => {
    const dressed = structuredClone(event);
    if (dressed.source.kind === 'runtime' && dressed.source.runtime_id === 'mock') {
      dressed.source.runtime_id = 'codex' as RuntimeId;
    }
    if (dressed.provenance.native_type !== null) {
      dressed.provenance.native_type = dressed.provenance.native_type.replace(/^mock\//, 'codex/');
    }
    if (dressed.event_type === 'execution.created') {
      dressed.payload.runtime = {
        runtime_id: 'codex' as RuntimeId,
        kind: 'codex',
        display_name: 'Codex 0.157.0',
        synthetic: false,
      };
      dressed.payload.directory = PROJECT;
      dressed.payload.instruction = `${dressed.payload.instruction} Work in ${PROJECT}/src.`;
    }
    if (dressed.event_type === 'command.accepted') {
      const command = dressed.payload.command;
      command.client = { name: 'some-rehearsal', version: '1', device_label: 'a desk' };
      if (command.command_type === 'execution.start') {
        command.payload.runtime_id = 'codex' as RuntimeId;
        command.payload.model_ref = 'ollama/qwen3.6:35b';
        command.payload.instruction = `${command.payload.instruction} Work in ${PROJECT}/src.`;
      }
    }
    return dressed;
  });
}

/** One event of the dressed trace, by type, to change in a test. */
function first<T extends EventEnvelope['event_type']>(
  events: readonly EventEnvelope[],
  type: T,
): Extract<EventEnvelope, { event_type: T }> {
  const found = events.find(
    (event): event is Extract<EventEnvelope, { event_type: T }> => event.event_type === type,
  );
  assert.ok(found, `the trace has a ${type}`);
  return found;
}

/** A command's output as a real runtime reports it: the test host names its vendor. */
function toolResult(events: EventEnvelope[]): EventEnvelope {
  const completed = first(events, 'runtime.tool.completed');
  return {
    ...completed,
    event_type: 'runtime.tool.result',
    payload: {
      tool_call_id: completed.payload.tool_call_id,
      turn_id: completed.payload.turn_id ?? null,
      completion: 'completed',
      redaction: { policy: 'known_held_values', replacements: 0 },
      kind: 'command',
      exit_code: 0,
      output: {
        text: 'Microsoft (R) Test Execution Command Line Tool\nPassed: 12',
        availability: 'available',
        complete: true,
        truncation: 'none',
      },
    },
  } as unknown as EventEnvelope;
}

function errorOf(action: () => unknown): RecordedRunError {
  try {
    action();
  } catch (error) {
    assert.ok(error instanceof RecordedRunError, String(error));
    return error;
  }
  assert.fail('expected a RecordedRunError');
}

describe('sanitizing a recorded real run', () => {
  test('only where files live, the runtime, the model and the client change', () => {
    const before = realShaped();
    const after = sanitizeRecordedRun(before, OPTIONS);
    assert.equal(after.length, before.length);
    for (const [index, event] of after.entries()) {
      const original = before[index] as EventEnvelope;
      assert.ok(parseEventEnvelope(event).ok, `event ${index} is still a valid envelope`);
      assert.equal(event.event_id, original.event_id);
      assert.equal(event.event_type, original.event_type);
      assert.equal(event.occurred_at, original.occurred_at);
      assert.equal(event.ingested_at, original.ingested_at);
      assert.deepEqual(event.provenance.epistemic, original.provenance.epistemic);
      assert.ok(!JSON.stringify(event).includes(HOME), `event ${index} keeps no home path`);
      if (event.source.kind === 'runtime')
        assert.equal(event.source.runtime_id, RECORDED_RUNTIME_ID);
      if (event.provenance.native_type !== null) {
        assert.match(event.provenance.native_type, /^recorded\//);
      }
    }
    const created = first(after, 'execution.created');
    assert.deepEqual(created.payload.runtime, {
      runtime_id: RECORDED_RUNTIME_ID,
      kind: RECORDED_RUNTIME_KIND,
      display_name: RECORDED_RUNTIME_NAME,
      synthetic: false,
    });
    assert.equal(created.payload.directory, 'the project folder');
    assert.match(created.payload.instruction, / Work in src\.$/);
    const [start] = after.flatMap((event) =>
      event.event_type === 'command.accepted' &&
      event.payload.command.command_type === 'execution.start'
        ? [event.payload.command]
        : [],
    );
    assert.ok(start?.command_type === 'execution.start');
    assert.equal(start.payload.runtime_id, RECORDED_RUNTIME_ID);
    assert.equal(start.payload.model_ref, RECORDED_MODEL);
    assert.deepEqual(start.client, RECORDED_CLIENT);
  });

  test('agent text is never edited', () => {
    const before = realShaped();
    const after = sanitizeRecordedRun(before, OPTIONS);
    const said = (events: EventEnvelope[]) =>
      events.flatMap((event) =>
        event.event_type === 'runtime.agent_message' ? [event.payload.text] : [],
      );
    assert.deepEqual(said(after), said(before));
  });

  test("a command's output is left out, in words the schema has, and its exit code kept", () => {
    const events = realShaped();
    const result = toolResult(events);
    assert.ok(parseEventEnvelope(result).ok, 'the stand-in result is a valid envelope');
    const cleaned = sanitizeRecordedRun([...events, result], OPTIONS).at(-1);
    assert.ok(cleaned !== undefined && parseEventEnvelope(cleaned).ok);
    assert.ok(cleaned.event_type === 'runtime.tool.result' && cleaned.payload.kind === 'command');
    assert.equal(cleaned.payload.exit_code, 0);
    assert.deepEqual(cleaned.payload.output, {
      text: null,
      availability: 'omitted_scope',
      complete: false,
      truncation: 'scope',
    });
  });

  test('refuses a run whose runtime is synthetic: a recorded run is real work', () => {
    const text = readFileSync(
      fileURLToPath(new URL('fixtures/traces/multiple_workstreams.jsonl', ROOT)),
      'utf8',
    );
    const mock = parseTrace(text, 'multiple_workstreams.jsonl');
    const error = errorOf(() => sanitizeRecordedRun(mock, OPTIONS));
    assert.match(error.message, /synthetic/);
  });

  const cases: readonly { name: string; text: string; reason: RegExp }[] = [
    { name: 'a brand as it is written', text: 'I built it with Unity.', reason: /Unity/ },
    { name: "a runtime's name in lower case", text: 'codex finished the edit.', reason: /codex/i },
    {
      name: 'a home path outside the project',
      text: `Ran ${HOME}/.tools/dotnet test.`,
      reason: /home/,
    },
    { name: 'any user folder', text: 'Read /Users/other/notes.txt.', reason: /user folder/ },
    { name: 'private folders', text: 'Wrote .private/notes.md.', reason: /private/ },
    { name: 'the local host', text: 'Listening on localhost.', reason: /local host/ },
    { name: 'an address', text: 'Reached 192.168.1.20.', reason: /address/ },
    { name: 'an email', text: 'Mailed dev@example.org.', reason: /email/ },
    { name: 'a URL', text: 'See https://example.org/page.', reason: /URL/ },
    {
      name: 'a credential shape',
      text: 'Used sk-ant-api03-AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCdEfGh.',
      reason: /credential/,
    },
    { name: 'a word the owner denies', text: 'Asked Pemberton.', reason: /denied/ },
    // Names AUDIT's probe (2026-10-28) found passing: models and products in lower or mixed case.
    { name: 'an open model by its tag', text: 'Ran gpt-oss:20b.', reason: /gpt/ },
    { name: "a model's family", text: 'Loaded llama3.1.', reason: /llama/ },
    { name: 'a model run into its version', text: 'Llama3 answered.', reason: /llama/ },
    { name: 'a hosted model', text: 'Compared with gpt-4o.', reason: /gpt/ },
    { name: 'a coding model', text: 'Tried deepseek-coder.', reason: /deepseek/ },
    { name: "the owner's other project", text: 'As Exulanica does.', reason: /exulanica/ },
    { name: 'a computer by its product name', text: 'On a MacBook Pro.', reason: /macbook/ },
  ];
  for (const { name, text, reason } of cases) {
    test(`fails closed, naming the field and never the text, on ${name}`, () => {
      const events = realShaped();
      const message = first(events, 'runtime.agent_message');
      message.payload.text = text;
      const error = errorOf(() => sanitizeRecordedRun(events, OPTIONS));
      const finding = error.findings.find((entry) => entry.field === 'payload.text');
      assert.ok(finding, `a finding on payload.text: ${error.message}`);
      assert.equal(finding.event_type, 'runtime.agent_message');
      assert.equal(finding.index, events.indexOf(message as EventEnvelope));
      assert.match(finding.reason, reason);
      assert.ok(!error.message.includes(text), 'the error never repeats the text');
    });
  }

  test('every key is scanned, and a refusal never prints it', () => {
    const events = realShaped();
    // A key can hide anywhere a free-form record is journaled, so every key is scanned, not only options'.
    const project = first(events, 'project.created') as unknown as {
      payload: Record<string, unknown>;
    };
    project.payload.Exulanica_notes = null;
    const error = errorOf(() => sanitizeRecordedRun(events, OPTIONS));
    const finding = error.findings.find((entry) => entry.reason.startsWith('a key'));
    assert.ok(finding, error.message);
    assert.equal(finding.field, 'payload');
    assert.ok(!error.message.includes('Exulanica'), 'the key itself is never printed');
  });

  test("a start's options are left out: the playback starts nothing", () => {
    const events = realShaped();
    const index = events.findIndex(
      (event) =>
        event.event_type === 'command.accepted' &&
        event.payload.command.command_type === 'execution.start',
    );
    const command = events[index];
    assert.ok(command?.event_type === 'command.accepted');
    assert.ok(command.payload.command.command_type === 'execution.start');
    command.payload.command.payload.options = { codex_max_output_tokens: 4096 };
    const cleaned = sanitizeRecordedRun(events, OPTIONS)[index];
    assert.ok(cleaned?.event_type === 'command.accepted');
    assert.ok(cleaned.payload.command.command_type === 'execution.start');
    assert.deepEqual(cleaned.payload.command.payload.options, {});
    assert.ok(parseEventEnvelope(cleaned).ok);
  });

  test("refuses without a deny list: the owner's names must always be checked", () => {
    const error = errorOf(() => sanitizeRecordedRun(realShaped(), { ...OPTIONS, deny: [] }));
    assert.match(error.message, /deny/);
  });

  test('a denied word is matched as a whole word, in any case', () => {
    const denying = { ...OPTIONS, deny: ['Chin'] };
    const events = realShaped();
    const message = first(events, 'runtime.agent_message');
    message.payload.text = 'The machine is matching its checks.';
    assert.doesNotThrow(() => sanitizeRecordedRun(events, denying));
    message.payload.text = 'Committed by g. chin.';
    const error = errorOf(() => sanitizeRecordedRun(events, denying));
    assert.ok(error.findings.some((finding) => finding.reason === 'a denied word'));
  });

  test('ordinary English that shares a word with a brand passes', () => {
    const events = realShaped();
    first(events, 'runtime.agent_message').payload.text =
      'The quest for fewer windows: an apple a day, as people react to the meta data.';
    assert.doesNotThrow(() => sanitizeRecordedRun(events, OPTIONS));
  });

  test('the names a judge must not read are the headset test’s, word for word', () => {
    const source = readFileSync(
      fileURLToPath(new URL('apps/xr/dotnet/Halcyonic.Client.Tests/JudgeWordsTests.cs', ROOT)),
      'utf8',
    );
    const block = /Brands\s*=\s*\{([^}]*)\}/.exec(source)?.[1];
    assert.ok(block, 'JudgeWordsTests declares its brands');
    const names = [...block.matchAll(/"([^"]+)"/g)].map((match) => match[1]);
    assert.deepEqual([...JUDGE_MUST_NOT_READ], names);
  });
});

describe('recording a recorded real run for the headset', () => {
  const record = () => recordRecordedRun(realShaped(), OPTIONS);

  test('plays the run with a hold wherever a person answered, and nothing else to answer', async () => {
    const recording = JSON.parse(await record()) as RecordedRun;
    assert.equal(recording.version, 3);
    assert.equal(recording.kind, 'recorded_run');
    assert.equal(recording.welcome.journal.origin, 'fixture');
    assert.deepEqual(
      recording.snapshot.snapshot.runtimes.map(({ runtime_id, synthetic, capabilities }) => ({
        runtime_id,
        synthetic,
        capabilities,
      })),
      [
        {
          runtime_id: RECORDED_RUNTIME_ID,
          synthetic: false,
          capabilities: {
            start_execution: false,
            instruct_at_rest: false,
            instruct_while_running: false,
            respond_to_approval: true,
            answer_question: false,
            interrupt: false,
          },
        },
      ],
    );
    assert.equal(recording.nodes.length, 2, 'one approval, so one hold');
    const [held, after] = recording.nodes;
    assert.ok(held && after);
    assert.deepEqual(held.ending, { snapshot: null, hold_ms: null });
    // The hold comes where the answer came: after the request, and after whatever other work
    // did while the request waited.
    const requested = held.events.flatMap(({ message }) =>
      message.event.event_type === 'runtime.approval.requested' ? [message.event] : [],
    );
    assert.equal(requested.length, 1);
    assert.equal(held.answers.length, 1);
    const answer = held.answers[0];
    assert.equal(answer?.after, held.events.length);
    assert.equal(answer?.node, 1);
    assert.equal(answer?.answer.kind, 'approve');
    assert.equal(answer?.answer.execution_id, requested[0]?.execution_id);
    assert.equal(answer?.answer.approval_id, requested[0]?.payload.approval_id);
    const resumed = after.events[0]?.message.event;
    assert.ok(resumed?.event_type === 'command.accepted');
    assert.equal(resumed.payload.command.command_type, 'execution.respond_to_approval');
    assert.equal(after.answers.length, 0);
    assert.equal(after.ending.hold_ms, END_HOLD_MS);
    assert.ok(after.ending.snapshot, 'the end holds a snapshot that offers nothing');
    assert.deepEqual(after.ending.snapshot.snapshot.runtimes, []);
  });

  test('keeps the real times in every event and shortens only the waits between them', async () => {
    const recording = JSON.parse(await record()) as RecordedRun;
    const sanitized = sanitizeRecordedRun(realShaped(), OPTIONS);
    const occurred = new Map(sanitized.map((event) => [event.event_id, event.occurred_at]));
    for (const node of recording.nodes) {
      let previous = 0;
      for (const { at_ms, message } of node.events) {
        assert.ok(validateServerMessage(message).ok, 'each message is one a control plane sends');
        assert.equal(message.event.occurred_at, occurred.get(message.event.event_id));
        assert.ok(at_ms - previous <= MAX_GAP_MS, `a wait of ${at_ms - previous} ms`);
        previous = at_ms;
      }
    }
  });

  test('the whole file names no brand and no home', async () => {
    const text = await record();
    assert.ok(!text.includes(HOME));
    for (const name of JUDGE_MUST_NOT_READ) {
      assert.ok(!new RegExp(`\\b${name}\\b`).test(text), `the recording names ${name}`);
    }
  });

  test('the same run always records the same text', async () => {
    assert.equal(await record(), await record());
  });
});
