import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import Schema from 'typebox/schema';
import { renderCSharpContracts } from './csharp.ts';
import {
  buildSchemaDocument,
  COMMAND_VARIANTS,
  EVENT_VARIANTS,
  parseClientMessage,
  parseCommandEnvelope,
  parseEventEnvelope,
  RUNTIME_EVENT_TYPES,
  renderSchemaDocument,
} from './index.ts';

const TRACE = ['multiple_workstreams.jsonl', 'failure_modes.jsonl'].flatMap((file) =>
  readFileSync(new URL(`../../../fixtures/traces/${file}`, import.meta.url), 'utf8')
    .trim()
    .split('\n')
    .map((line) => JSON.parse(line) as Record<string, unknown>),
);

function sampleEvent(
  type: string,
  where: (event: Record<string, unknown>) => boolean = () => true,
): Record<string, unknown> {
  const event = TRACE.find((candidate) => candidate.event_type === type && where(candidate));
  assert.ok(event, `the trace contains a matching ${type} event`);
  return structuredClone(event);
}

const COMMAND = {
  schema_version: 1,
  command_id: '0192f1a0-0000-4000-8000-000000000001',
  command_type: 'execution.send_instruction',
  issued_at: '2026-09-26T10:00:00.000Z',
  client: { name: 'test', version: null, device_label: null },
  payload: { execution_id: '0192f1a0-0000-7000-8000-000000000001', text: 'Continue.' },
};

describe('event envelopes', () => {
  test('every event in the recorded traces matches the contract', () => {
    for (const event of TRACE) {
      const parsed = parseEventEnvelope(event);
      assert.ok(parsed.ok, JSON.stringify(parsed.ok ? null : parsed.issues));
    }
  });

  test('timestamps must be canonical UTC with milliseconds', () => {
    const event = sampleEvent('project.created');
    for (const bad of [
      '2026-09-26T10:00:00Z',
      '2026-09-26T10:00:00.000+00:00',
      '2026-13-40T10:00:00.000Z',
    ]) {
      const parsed = parseEventEnvelope({ ...event, occurred_at: bad });
      assert.equal(parsed.ok, false, bad);
    }
  });

  test('issues name the offending field of the selected variant', () => {
    const unknown = parseEventEnvelope({ ...sampleEvent('project.created'), event_type: 'nope' });
    assert.deepEqual(unknown.ok ? null : unknown.issues, [
      { path: '/event_type', message: 'unknown event_type "nope"' },
    ]);
    const extra = parseEventEnvelope({ ...sampleEvent('project.created'), extra: true });
    assert.equal(extra.ok, false);
  });

  test('agent text is a claim and must be reported, never observed', () => {
    const message = sampleEvent('runtime.agent_message');
    message.provenance = { epistemic: 'observed', native_type: null };
    const parsed = parseEventEnvelope(message);
    assert.deepEqual(parsed.ok ? null : parsed.issues[0]?.path, '/provenance/epistemic');
  });

  test('inferred facts must name the rule that produced them', () => {
    const event = sampleEvent('runtime.test_run.started');
    event.provenance = { epistemic: 'inferred', native_type: null };
    assert.equal(parseEventEnvelope(event).ok, false);
    event.provenance = { epistemic: 'inferred', native_type: null, rule: 'test_command.v1' };
    assert.equal(parseEventEnvelope(event).ok, true);
  });

  test('a scope may not name an execution without its workstream', () => {
    const event = sampleEvent('command.accepted', (candidate) => candidate.execution_id !== null);
    event.workstream_id = null;
    const parsed = parseEventEnvelope(event);
    assert.deepEqual(parsed.ok ? null : parsed.issues[0]?.path, '/workstream_id');
  });

  test('the runtime payload registry matches the runtime event variants', () => {
    const runtimeVariants = EVENT_VARIANTS.map(
      (variant) => (variant.properties.event_type as { const: string }).const,
    ).filter((type) => type.startsWith('runtime.'));
    assert.deepEqual([...RUNTIME_EVENT_TYPES].sort(), runtimeVariants.sort());
  });
});

describe('commands and realtime messages', () => {
  test('a well-formed command parses', () => {
    assert.equal(parseCommandEnvelope(COMMAND).ok, true);
  });

  test('text must contain something other than whitespace', () => {
    const parsed = parseCommandEnvelope({
      ...COMMAND,
      payload: { ...COMMAND.payload, text: '   ' },
    });
    assert.deepEqual(parsed.ok ? null : parsed.issues[0]?.path, '/payload/text');
  });

  test('an invalid command inside a realtime message reports paths under /command', () => {
    const parsed = parseClientMessage({
      type: 'command',
      command: { ...COMMAND, command_type: 'execution.pause' },
    });
    assert.deepEqual(parsed.ok ? null : parsed.issues, [
      { path: '/command/command_type', message: 'unknown command_type "execution.pause"' },
    ]);
  });

  test('hello and ping parse; unknown message types do not', () => {
    const hello = {
      type: 'hello',
      protocol: 1,
      client: { name: 'xr', version: '0.1.0', device_label: 'Quest 3' },
      resume: null,
    };
    assert.equal(parseClientMessage(hello).ok, true);
    assert.equal(parseClientMessage({ type: 'ping', nonce: null }).ok, true);
    assert.equal(parseClientMessage({ type: 'subscribe' }).ok, false);
  });
});

describe('the language-neutral schema document', () => {
  test('the committed document is current', () => {
    const committed = readFileSync(
      new URL('../schema/halcyonic-contracts.schema.json', import.meta.url),
      'utf8',
    );
    assert.equal(
      committed,
      renderSchemaDocument(),
      'run pnpm contracts:emit and commit the result',
    );
  });

  test('it validates exactly what the TypeScript contracts validate', () => {
    const document = buildSchemaDocument() as { $defs: Record<string, unknown> };
    const validator = Schema.Compile({ $defs: document.$defs, $ref: '#/$defs/EventEnvelope' });
    for (const event of TRACE) assert.ok(validator.Check(event));
    const bad = { ...sampleEvent('runtime.agent_message'), payload: { text: '' } };
    assert.equal(validator.Check(bad), parseEventEnvelope(bad).ok);
  });

  test('shared definitions are referenced rather than repeated', () => {
    const text = renderSchemaDocument();
    assert.ok(text.includes('"$ref": "#/$defs/CommandEnvelope"'));
    assert.ok(text.length < 400_000, `document is ${text.length} bytes`);
  });
});

describe('the C# bindings', () => {
  const generated = renderCSharpContracts();

  test('the committed bindings are current', () => {
    const committed = readFileSync(
      new URL('../csharp/Runtime/HalcyonicContracts.g.cs', import.meta.url),
      'utf8',
    );
    assert.equal(committed, generated, 'run pnpm contracts:emit and commit the result');
  });

  test('every event and command type has a class the converters can create', () => {
    const tags = [
      ...EVENT_VARIANTS.map(
        (variant) => (variant.properties.event_type as { const: string }).const,
      ),
      ...COMMAND_VARIANTS.map(
        (variant) => (variant.properties.command_type as { const: string }).const,
      ),
    ];
    for (const tag of tags) assert.ok(generated.includes(`"${tag}" => new `), tag);
  });
});
