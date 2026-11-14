import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import Type from 'typebox';
import Schema from 'typebox/schema';
import { renderCSharpContracts, renderObjectMembers } from './csharp.ts';
import {
  buildSchemaDocument,
  CatalogResponse,
  CheckFindings,
  CheckLaptop,
  COMMAND_VARIANTS,
  COMPANION_LINE_MAX,
  COMPANION_MAX_MESSAGES,
  COMPANION_NAME_MAX,
  COMPANION_PERSON_MAX,
  CompanionRepliesRequest,
  CompanionReplyResponse,
  compileValidator,
  DEVICE_EVENT_TYPES,
  ESTIMATED_COST_NOTE,
  EVENT_VARIANTS,
  EvaluationResult,
  fitQuestion,
  MESSAGE_NESTING_LIMIT,
  ProjectView,
  parseClientMessage,
  parseCommandEnvelope,
  parseEventEnvelope,
  QuestionPrompts,
  READING_FLOOR_PX,
  RUNTIME_EVENT_TYPES,
  RuntimeCapabilities,
  RuntimeDescriptor,
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

  test("a runtime's usage is its own count and must be reported, never observed", () => {
    const usage: Record<string, unknown> = {
      ...sampleEvent('runtime.agent_message'),
      event_type: 'runtime.usage.reported',
      payload: {
        turn_id: 't1',
        tokens: { input: 10, cache_read: null, cache_write: null, output: 4, reasoning: null },
        cost: null,
        counted: 'given',
      },
    };
    assert.equal(parseEventEnvelope(usage).ok, true);
    usage.provenance = { epistemic: 'observed', native_type: null };
    const parsed = parseEventEnvelope(usage);
    assert.deepEqual(parsed.ok ? null : parsed.issues[0]?.path, '/provenance/epistemic');
    const priced: Record<string, unknown> = {
      ...usage,
      provenance: { epistemic: 'reported', native_type: null },
    };
    priced.payload = {
      ...(usage.payload as object),
      cost: { estimated_usd: 0.01, prices: 'guess' },
    };
    assert.equal(parseEventEnvelope(priced).ok, false, 'no price kind outside the list');
    const bounded = (payload: object) =>
      parseEventEnvelope({ ...priced, payload: { ...(usage.payload as object), ...payload } }).ok;
    assert.equal(bounded({ cost: { estimated_usd: 2, prices: 'list' } }), true);
    assert.equal(
      bounded({ cost: { estimated_usd: -1, prices: 'list' } }),
      false,
      'no negative money',
    );
    assert.equal(
      bounded({ cost: { estimated_usd: 2e6, prices: 'list' } }),
      false,
      'nor past its bound',
    );
    const tokens = { input: -3, cache_read: null, cache_write: null, output: 4, reasoning: null };
    assert.equal(bounded({ tokens }), false, 'no negative count');
    assert.equal(bounded({ tokens: { ...tokens, input: 1.5 } }), false, 'nor a part of a token');
    assert.equal(bounded({ counted: 'guessed' }), false, 'no way of counting outside the list');
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

  test('the device event types are every device event variant', () => {
    const deviceVariants = EVENT_VARIANTS.map(
      (variant) => (variant.properties.event_type as { const: string }).const,
    ).filter((type) => type.startsWith('device.'));
    assert.deepEqual([...DEVICE_EVENT_TYPES].sort(), deviceVariants.sort());
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

  test('text must be well-formed Unicode: a lone surrogate is refused wherever it is', () => {
    const answer = (text: string, selected: string[] = []) =>
      parseCommandEnvelope({
        ...COMMAND,
        command_type: 'execution.answer_question',
        payload: {
          execution_id: COMMAND.payload.execution_id,
          question_id: 'q-1',
          answers: [{ key: 'q0', selected, text }],
        },
      });
    assert.equal(answer('Blue, and 😀 too').ok, true);
    for (const [parsed, path] of [
      [answer('Blue \udc00'), '/payload/answers/0/text'],
      [answer('Blue', ['\ud800']), '/payload/answers/0/selected/0'],
      [
        parseCommandEnvelope({ ...COMMAND, payload: { ...COMMAND.payload, text: 'Go \ud83d' } }),
        '/payload/text',
      ],
    ] as const) {
      assert.deepEqual(parsed.ok ? null : parsed.issues, [
        { path, message: 'must be well-formed Unicode text, without a lone surrogate' },
      ]);
    }
  });

  test('a message nesting deeper than the limit is refused without walking it all', () => {
    const start = (options: unknown) => ({
      ...COMMAND,
      command_type: 'execution.start',
      payload: {
        workstream_id: COMMAND.payload.execution_id,
        runtime_id: 'mock',
        instruction: 'Go.',
        options,
        model_ref: null,
      },
    });
    // Twenty thousand levels, which would exhaust the stack of a recursive walk.
    const deep = JSON.parse(`${'['.repeat(20_000)}${']'.repeat(20_000)}`) as unknown;
    const parsed = parseCommandEnvelope(start({ deep }));
    assert.equal(parsed.ok, false);
    const issue = parsed.ok ? null : parsed.issues[0];
    assert.equal(issue?.message, `must not nest more than ${MESSAGE_NESTING_LIMIT} levels deep`);
    assert.ok(issue?.path.startsWith('/payload/options/deep/0/0'));
    const message = parseClientMessage({ type: 'command', command: start({ deep }) });
    assert.match(message.ok ? '' : (message.issues[0]?.path ?? ''), /^\/command\/payload\/options/);
    const hello = parseClientMessage({ type: 'hello', protocol: 1, client: deep, resume: null });
    assert.equal(
      hello.ok ? null : hello.issues[0]?.path,
      '/client/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0/0',
    );
    // Options a runtime takes nest far less, and pass.
    let nested: unknown = 'leaf';
    for (let level = 0; level < 20; level += 1) nested = { level: nested };
    assert.equal(parseCommandEnvelope(start({ nested })).ok, true);
  });

  test('a start may say how often the agent asks, only as ask or never_ask, and may leave it out (ADR 0043)', () => {
    const start = (extra: object) => ({
      ...COMMAND,
      command_type: 'execution.start',
      payload: {
        workstream_id: COMMAND.payload.execution_id,
        runtime_id: 'mock',
        instruction: 'Go.',
        options: {},
        model_ref: null,
        ...extra,
      },
    });
    assert.equal(parseCommandEnvelope(start({})).ok, true, 'absent means ask, as before');
    assert.equal(parseCommandEnvelope(start({ approval_level: 'ask' })).ok, true);
    assert.equal(parseCommandEnvelope(start({ approval_level: 'never_ask' })).ok, true);
    for (const level of ['bypass', 'never', null, true])
      assert.equal(parseCommandEnvelope(start({ approval_level: level })).ok, false, String(level));
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

describe('the evaluation contract', () => {
  const validate = compileValidator(EvaluationResult);
  const read = {
    availability: { state: 'available', reason: null },
    coverage: {
      requested: { from: '2026-06-28', through: '2026-09-26' },
      observed: null,
      matched_sessions: 1,
      included_sessions: 1,
      complete: true,
      omissions: [],
    },
    freshness: {
      state: 'fresh',
      generated_at: '2026-09-26T18:00:00.000Z',
      data_through: null,
      stale_at: '2026-09-26T18:05:00.000Z',
    },
  };
  const evaluation = {
    source: { system: 'seorak', synthetic: false, api_version: 'v1' },
    cost: { ...read, estimated_usd: null, note: ESTIMATED_COST_NOTE },
    outcome: { ...read, measure: null },
    verification: {
      ...read,
      lens: {
        by_kind: [{ label: 'test', runs: 5, passed: 4, pass_rate: 0.8 }],
        empty_reason: null,
      },
    },
  };
  const available = (value: object) => validate({ availability: 'available', evaluation: value });

  test('says whether a stand-in rather than the source measured it', () => {
    const { synthetic: _synthetic, ...unsaid } = evaluation.source;
    assert.equal(available({ ...evaluation, source: unsaid }).ok, false);
    assert.ok(available({ ...evaluation, source: { ...evaluation.source, synthetic: true } }).ok);
  });

  test('keeps what the source does not know as null, and carries no score', () => {
    assert.ok(available(evaluation).ok);
    assert.equal(available({ ...evaluation, score: 0.9 }).ok, false);
    const { outcome: _outcome, ...partial } = evaluation;
    assert.equal(available(partial).ok, false);
  });

  test("labels every cost as an estimate with the source's note", () => {
    for (const note of ['', 'The amount billed.'])
      assert.equal(available({ ...evaluation, cost: { ...evaluation.cost, note } }).ok, false);
  });

  test('holds rates to fractions from 0 to 1', () => {
    for (const pass_rate of [80, -0.1]) {
      const lens = {
        by_kind: [{ label: 'test', runs: 5, passed: 4, pass_rate }],
        empty_reason: null,
      };
      assert.equal(available({ ...evaluation, verification: { ...read, lens } }).ok, false);
    }
  });

  test('names a value the source added later unknown, and nothing else it does not know', () => {
    const grown = {
      availability: { state: 'partial', reason: 'unknown' },
      coverage: { ...read.coverage, complete: false, omissions: ['unknown'] },
      freshness: read.freshness,
    };
    const measure = {
      commits_landed: null,
      uncommitted: null,
      line_survival: null,
      error_count: null,
      first_error_at: null,
      end_reason: 'unknown',
    };
    assert.ok(available({ ...evaluation, outcome: { ...grown, measure } }).ok);
    for (const reason of ['sampled', 'Unknown'])
      assert.equal(
        available({
          ...evaluation,
          outcome: { ...grown, availability: { state: 'partial', reason }, measure },
        }).ok,
        false,
        reason,
      );
  });
});

describe('the companion contract', () => {
  const validateReply = compileValidator(CompanionReplyResponse);
  const validateRequest = compileValidator(CompanionRepliesRequest);
  const ask = {
    next: 'ask',
    line: 'A tracker for race times fits a small web page.',
    view: 'unclear',
    question: { text: 'Who enters the times?', choices: ['Each runner', 'One organiser'] },
  };
  const reply = (value: object) =>
    validateReply({
      reply: value,
      provenance: 'reported',
      companion: { name: 'a-local-model:tag', served: 'this_mac' },
    });

  test('a reply is reported, from a model on this Mac, and never observed', () => {
    assert.ok(reply(ask).ok);
    const companion = { name: 'a-local-model:tag', served: 'this_mac' };
    for (const provenance of ['observed', 'inferred'])
      assert.equal(validateReply({ reply: ask, provenance, companion }).ok, false, provenance);
    const remote = { ...companion, served: 'remote' };
    assert.equal(
      validateReply({ reply: ask, provenance: 'reported', companion: remote }).ok,
      false,
    );
  });

  test('a reply asks one question or proposes, never both', () => {
    const proposal = { project_name: 'Race Times', first_task: 'Make one page.' };
    assert.ok(reply({ next: 'propose', line: 'Clear.', view: 'clear', proposal }).ok);
    assert.equal(reply({ ...ask, proposal }).ok, false);
    assert.equal(reply({ next: 'propose', line: 'Clear.', view: 'clear' }).ok, false);
  });

  test('bounds every part of a reply', () => {
    const choices = ['a', 'b', 'c', 'd', 'e'];
    assert.equal(reply({ ...ask, question: { text: 'Which?', choices } }).ok, false);
    assert.equal(reply({ ...ask, line: 'x'.repeat(COMPANION_LINE_MAX + 1) }).ok, false);
    assert.equal(reply({ ...ask, view: 'feasible' }).ok, false);
    const name = 'x'.repeat(COMPANION_NAME_MAX + 1);
    const proposal = { project_name: name, first_task: 'Make one page.' };
    assert.equal(reply({ next: 'propose', line: 'Clear.', view: 'clear', proposal }).ok, false);
  });

  test('a request carries the exchange so far, bounded, with nothing else', () => {
    const person = { from: 'person', text: 'something for my running club' };
    const companion = { from: 'companion', reply: ask };
    assert.ok(validateRequest({ start: 'idea', want: 'next', messages: [person] }).ok);
    assert.ok(validateRequest({ start: 'help', want: 'next', messages: [] }).ok);
    assert.ok(
      validateRequest({ start: 'idea', want: 'proposal', messages: [person, companion] }).ok,
    );
    const many = Array.from({ length: COMPANION_MAX_MESSAGES + 1 }, () => person);
    assert.equal(validateRequest({ start: 'idea', want: 'next', messages: many }).ok, false);
    const long = { from: 'person', text: 'x'.repeat(COMPANION_PERSON_MAX + 1) };
    assert.equal(validateRequest({ start: 'idea', want: 'next', messages: [long] }).ok, false);
    const folder = { ...person, folder: '/Users/someone/Projects' };
    assert.equal(validateRequest({ start: 'idea', want: 'next', messages: [folder] }).ok, false);
  });
});

describe('runtime descriptors', () => {
  const capabilities = Object.fromEntries(
    Object.keys(RuntimeCapabilities.properties).map((name) => [name, false]),
  );
  const descriptor = {
    runtime_id: 'codex',
    kind: 'codex',
    display_name: 'Codex 0.157.0',
    synthetic: false,
    capabilities,
    model_choice: 'listed',
    uses_project_location: true,
    reports_tool_activity: false,
  };
  const valid = compileValidator(RuntimeDescriptor);

  test('may declare whether approving a command can let it leave the sandbox, and need not', () => {
    assert.equal(valid(descriptor).ok, true, 'absent: not verified, says nothing');
    assert.equal(valid({ ...descriptor, approval_may_leave_sandbox: true }).ok, true);
    assert.equal(valid({ ...descriptor, approval_may_leave_sandbox: false }).ok, true);
    assert.equal(valid({ ...descriptor, approval_may_leave_sandbox: 'yes' }).ok, false);
    assert.equal(valid({ ...descriptor, approval_may_leave_sandbox: null }).ok, false);
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

  /** The names of definitions whose body also appears inlined somewhere else in the document. */
  function inlinedDefinitions(
    document: { $defs: Record<string, unknown> } & Record<string, unknown>,
  ): string[] {
    const canonical = (value: unknown): string =>
      JSON.stringify(value, (_key, inner: unknown) =>
        inner !== null && typeof inner === 'object' && !Array.isArray(inner)
          ? Object.fromEntries(Object.entries(inner).sort(([a], [b]) => a.localeCompare(b)))
          : inner,
      );
    const bodies = new Map(
      Object.entries(document.$defs).map(([name, body]) => [canonical(body), name]),
    );
    const found = new Set<string>();
    const visit = (value: unknown, isDefinition: boolean) => {
      if (value === null || typeof value !== 'object') return;
      if (!isDefinition) {
        const name = bodies.get(canonical(value));
        if (name !== undefined) found.add(name);
      }
      for (const inner of Array.isArray(value) ? value : Object.values(value)) visit(inner, false);
    };
    for (const [key, value] of Object.entries(document)) {
      if (key !== '$defs') visit(value, false);
    }
    for (const body of Object.values(document.$defs)) visit(body, true);
    return [...found].sort();
  }

  test('a definition inlined anywhere instead of referenced is found', () => {
    const point = { type: 'object', properties: { x: { type: 'number' } } };
    const synthetic = {
      $defs: {
        Point: point,
        Line: { type: 'object', properties: { from: { $ref: '#/$defs/Point' } } },
      },
      properties: {
        a: structuredClone(point),
        b: { properties: { x: { type: 'number' } }, type: 'object' },
      },
    };
    assert.deepEqual(inlinedDefinitions(synthetic), ['Point']);
    assert.deepEqual(inlinedDefinitions({ $defs: synthetic.$defs, properties: {} }), []);
  });

  /**
   * Where the document may grow too large, each bound one that contract changes made apart never
   * have to move, so two of them never edit the same line. Each event and command repeats its envelope's
   * fields, so those are bounded once; each variant and each other definition is bounded alone; the
   * whole document has a ceiling far above today's, a last stop rather than a count. Repetition by
   * inlining is checked directly, above.
   */
  const SCHEMA_BOUNDS = {
    /** One event variant, envelope and payload: the largest is about 5,100 bytes. */
    eventVariant: 8_000,
    /** One command variant: the largest is about 1,900 bytes. */
    commandVariant: 4_000,
    /** An event envelope's fields but its type and payload, repeated in every event: at most about 2,000. */
    eventEnvelopeFields: 2_600,
    /** A command envelope's fields but its type and payload, repeated in every command: about 400. */
    commandEnvelopeFields: 600,
    /** Any other definition: the largest, Understanding, is about 6,200 bytes. */
    definition: 12_000,
    /** The whole document, about 500,000 bytes on 2026-11-11. */
    document: 1_000_000,
  } as const;

  /** Each bound the document breaks, in words; none when it holds every one. */
  function schemaOverBounds(
    document: { $defs: Record<string, unknown> } & Record<string, unknown>,
    text: string,
  ): string[] {
    const size = (value: unknown) => JSON.stringify(value, null, 2).length;
    const over: string[] = [];
    if (text.length > SCHEMA_BOUNDS.document) over.push(`the document is ${text.length} bytes`);
    for (const [envelope, tag, variantBound, fieldsBound] of [
      [
        'EventEnvelope',
        'event_type',
        SCHEMA_BOUNDS.eventVariant,
        SCHEMA_BOUNDS.eventEnvelopeFields,
      ],
      [
        'CommandEnvelope',
        'command_type',
        SCHEMA_BOUNDS.commandVariant,
        SCHEMA_BOUNDS.commandEnvelopeFields,
      ],
    ] as const) {
      const variants = (document.$defs[envelope] as { anyOf?: unknown[] } | undefined)?.anyOf ?? [];
      for (const variant of variants as { properties?: Record<string, { const?: unknown }> }[]) {
        const properties = variant.properties ?? {};
        const name = String(properties[tag]?.const ?? '?');
        if (size(variant) > variantBound) over.push(`${name} is ${size(variant)} bytes`);
        const fields = Object.fromEntries(
          Object.entries(properties).filter(([key]) => key !== tag && key !== 'payload'),
        );
        if (size(fields) > fieldsBound) {
          over.push(`${name}'s envelope fields are ${size(fields)} bytes`);
        }
      }
    }
    for (const [name, body] of Object.entries(document.$defs)) {
      if (name === 'EventEnvelope' || name === 'CommandEnvelope') continue;
      if (size(body) > SCHEMA_BOUNDS.definition) over.push(`${name} is ${size(body)} bytes`);
    }
    return over;
  }

  test('each bound on the document finds what breaks it', () => {
    const fields = (bytes: number) => ({ schema_version: { const: 'x'.repeat(bytes) } });
    const variant = (tag: string, type: string, payloadBytes: number, fieldBytes = 0) => ({
      type: 'object',
      properties: {
        ...fields(fieldBytes),
        [tag]: { const: type },
        payload: { description: 'x'.repeat(payloadBytes) },
      },
    });
    const synthetic = (extra: Partial<{ event: unknown; command: unknown; other: unknown }>) => ({
      $defs: {
        EventEnvelope: {
          anyOf: [variant('event_type', 'small.event', 10), extra.event].filter(Boolean),
        },
        CommandEnvelope: {
          anyOf: [variant('command_type', 'small.command', 10), extra.command].filter(Boolean),
        },
        ...(extra.other === undefined ? {} : { Other: extra.other }),
      },
    });
    const holds = synthetic({});
    assert.deepEqual(schemaOverBounds(holds, JSON.stringify(holds)), []);
    const cases: [ReturnType<typeof synthetic>, RegExp][] = [
      [
        synthetic({ event: variant('event_type', 'big.event', 9_000) }),
        /^big\.event is \d+ bytes$/,
      ],
      [synthetic({ command: variant('command_type', 'big.command', 5_000) }), /^big\.command is/],
      [
        synthetic({ event: variant('event_type', 'wide.event', 10, 3_000) }),
        /^wide\.event's envelope fields are/,
      ],
      [
        synthetic({ command: variant('command_type', 'wide.command', 10, 800) }),
        /^wide\.command's envelope fields are/,
      ],
      [synthetic({ other: { description: 'x'.repeat(13_000) } }), /^Other is \d+ bytes$/],
    ];
    for (const [document, expected] of cases) {
      const over = schemaOverBounds(document, JSON.stringify(document));
      assert.equal(over.length, 1, over.join('; '));
      assert.match(over[0] ?? '', expected);
    }
    assert.match(
      schemaOverBounds(holds, 'x'.repeat(1_000_001)).join(),
      /the document is 1000001 bytes/,
    );
  });

  test('shared definitions are referenced rather than repeated, and the document is within its bounds', () => {
    const text = renderSchemaDocument();
    assert.ok(text.includes('"$ref": "#/$defs/CommandEnvelope"'));
    const document = buildSchemaDocument() as { $defs: Record<string, unknown> };
    assert.deepEqual(inlinedDefinitions(document), []);
    assert.deepEqual(schemaOverBounds(document, text), []);
    assert.ok(
      ((document.$defs.EventEnvelope as { anyOf?: unknown[] }).anyOf?.length ?? 0) ===
        EVENT_VARIANTS.length,
      'every event variant is bounded',
    );
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

  test('an optional member of any type reads as null when missing and is omitted when null', () => {
    const members = renderObjectMembers(
      'Probe',
      Type.Object({
        found: Type.Optional(Type.Object({ count: Type.Integer() })),
        names: Type.Optional(Type.Array(Type.String())),
        level: Type.Optional(Type.Integer()),
      }),
    ).join('\n');
    assert.match(
      members,
      /\[global::Newtonsoft\.Json\.JsonProperty\("found", Required = global::Newtonsoft\.Json\.Required\.Default, NullValueHandling = global::Newtonsoft\.Json\.NullValueHandling\.Ignore\)\]\s+public ProbeFound\? Found \{ get; set; \}/,
    );
    assert.match(
      members,
      /\[global::Newtonsoft\.Json\.JsonProperty\("names", Required = global::Newtonsoft\.Json\.Required\.Default, NullValueHandling = global::Newtonsoft\.Json\.NullValueHandling\.Ignore\)\]\s+public global::System\.Collections\.Generic\.List<string>\? Names \{ get; set; \}/,
    );
    assert.match(members, /public long\? Level \{ get; set; \}/);
    // A constant still cannot be optional: its absence would read as a different value.
    assert.throws(
      () => renderObjectMembers('Probe', Type.Object({ kind: Type.Optional(Type.Literal('x')) })),
      /a constant cannot be optional/,
    );
  });

  /**
   * Every optional member of the bindings, as `Class.json_name`, one per line and sorted, so two
   * contract changes made apart add different lines. A member made optional by mistake, or one
   * listed and no longer optional, fails the test by name.
   */
  const OPTIONAL_MEMBERS = [
    'AutoFixView.failed_steps', // the steps that failed in the check a fix chain answered
    'AvailableWorkspaceAgent.provider', // where a remote assistant runs, by its declared provider (ADR 0030, 2026-11-12)
    'CheckFindings.laptop', // M1.8b
    'CheckFindings.notes', // M1.8b
    'CheckFindings.own_permissions', // whether a check's browser had permissions of its own (S1)
    'CheckFindings.ready', // M1.8b
    'CheckFindings.refusals', // what a starter's check refuses, as its starter.json declares it (VRIFY, 2026-11-12)
    'CheckFindings.vr', // M1.8b
    'CheckLaptop.exceptions_during', // M1.8g
    'CheckLaptop.reading', // whether a person can read the app on a laptop (VRIFY, 2026-11-13)
    'CheckLaptop.response_seen', // M1.8g
    'CheckLaptop.response_unknown', // why a twin read was unknown (M1.8g-b)
    'CheckPage.own', // a check page's own pixels against the starter's (DRAWN)
    'CheckpointView.turn_id', // the turn an after_turn version follows (History)
    'Evaluation.activity', // Seorak's activity of a session
    'ExecutionApprovalAnsweredByHostPayload.cut', // a confined answer longer than the record keeps (M1)
    'ExecutionApprovalDeniedByHostPayload.length', // a too-long request's length; absent for one not recorded (ADR 0065)
    'ExecutionCreatedPayload.secret_names', // the project secrets a start gave (ADR 0057)
    'ExecutionSendInstructionPayload.attachments', // pictures with the words (A4)
    'ExecutionSendInstructionPayload.leave_plan', // carrying a plan out (PLAN 4a)
    'ExecutionSendInstructionPayload.spending_cap_usd', // a new limit to carry on under
    'ExecutionStartPayload.approval_level', // ADR 0043
    'ExecutionStartPayload.attachments', // pictures with the instruction (A4)
    'ExecutionStartPayload.plan', // a start in the runtime's own plan mode (PLAN)
    'ExecutionStartPayload.spending_cap_usd', // a spending limit, where the runtime offers one
    'ExecutionView.approval_level', // ADR 0043
    'ExecutionView.approvals', // how many requests the host answered and how many asked, absent until one
    'ExecutionView.auto_fix', // a failed check sent back on its own (ADR 0044)
    'ExecutionView.plan', // whether its start recorded a plan (PLAN 4a)
    'ExecutionView.plan_left', // whether the plan was left to carry it out (PLAN 4a)
    'ExecutionView.queued_instructions', // instructions waiting for the turn to end (ADR 0056)
    'ExecutionView.spending_cap', // its spending limit, absent for none
    'ExecutionView.unconfined_reason', // why its start was not counted confined (BOX-4, PLAN 4a)
    'ExecutionView.usage', // what its reported turns used, absent until one is reported
    'LocationRoot.label', // a location's label
    'OwnCopy.missing_at', // ADR 0045, amended: a start found the task's copy gone
    'ProjectCheckPayload.workstream_id', // the task whose copy a check checks (ADR 0045)
    'ProjectCheckRecordedPayload.workstream_id', // ADR 0045
    'ProjectCheckedResult.ready', // project_checked's ready
    'ProjectCheckpointRecordedPayload.turn_id', // History
    'ProjectCreatedResult.returned', // a project taken off the list, brought back (ADR 0063)
    'ProjectOwnCheckRecordedPayload.workstream_id', // ADR 0055, as ADR 0045
    'ProjectOwnCheckRunView.workstream_id', // ADR 0055
    'ProjectPublishPayload.make_public', // ADR 0039, amended: the first Send, its title, Make Public
    'ProjectRemoveFromListPayload.review', // the review a removal carries (ADR 0063)
    'ProjectRestorePayload.review', // the review a restore carries (ADR 0051)
    'ProjectRulesSetPayload.review', // the review turning a project's rules on carries (E5)
    'ProjectSendPayload.one_change', // Send One Change, offered on a refusal for an earlier version
    'ProjectSendPayload.repository_name', // ADR 0039, amended: the first Send, its title, Make Public
    'ProjectSendPayload.title', // ADR 0039, amended: the first Send, its title, Make Public
    'ProjectSendPayload.visibility', // ADR 0039, amended: the first Send, its title, Make Public
    'ProjectSetLocationPayload.review', // the review a rebinding carries (ADR 0051)
    'ProjectView.check', // M1.8e
    'ProjectView.mac_play', // M1.8e
    'ProjectView.own_check', // a project's own check (ADR 0055)
    'ProjectView.preview', // M1.8e
    'ProjectView.published', // a project's published game (ADR 0039)
    'ProjectView.removed_at', // when a project was taken off the list (ADR 0063)
    'ProjectView.rules', // a project's own rules while on (E5)
    'ProjectView.secrets', // a project's secrets by name (ADR 0057)
    'ProjectView.share', // a project's shared link (ADR 0054)
    'ProjectView.usage', // what its runs used, absent until one reports
    'ProjectView.world', // a world project's world (ADR 0049)
    'ProjectViewCheck.workstream_id', // ADR 0045
    'RuntimeApprovalRequestedPayload.too_long', // a request longer than a person can approve whole (long commands)
    'RuntimeDescriptor.approval_may_leave_sandbox', // a declaration
    'RuntimeDescriptor.delivers_denial_message', // a declaration: a denial's words reach the agent
    'RuntimeDescriptor.holds_instructions', // the host's own declaration (ADR 0056)
    'RuntimeDescriptor.offers_never_ask', // ADR 0043
    'RuntimeDescriptor.offers_plan', // a declaration (PLAN)
    'RuntimeDescriptor.offers_spending_cap', // a declaration
    'RuntimeDescriptor.reports_tool_results', // a declaration
    'RuntimeDescriptor.reports_usage', // a declaration
    'RuntimeDescriptor.resumes_after_restart', // ADR 0046
    'RuntimeDescriptor.secret_environment', // ADR 0057
    'RuntimeDescriptor.shows_changes', // a declaration: a file change's approval carries the change (ADR 0065)
    'RuntimeDescriptor.takes_pictures', // a declaration (A4)
    'RuntimeExecutionStartedPayload.approval_level', // ADR 0043
    'RuntimeExecutionStartedPayload.confined', // whether a start recorded it confined (S2)
    'RuntimeExecutionStartedPayload.plan', // whether a start recorded its plan mode (PLAN)
    'RuntimeExecutionStartedPayload.unconfined_reason', // why a start that asked for the sandbox ran without it (BOX-4)
    'RuntimeModel.picture_input', // what the runtime's list says a model takes (A4)
    'RuntimeModel.provider', // the declared provider a runtime reaches a model through, whose key it needs
    'RuntimeToolCompletedPayload.turn_id', // a completed tool's turn
    'SendReview.one_change', // Send One Change's own facts (ADR 0039, amended)
    'ToolUseSubject.changes', // every file a request would change, whole (ADR 0065)
    'ToolUseSubject.paths', // the places a request names, as structure, absent where they are not known
    'VersionEntry.pruned_at', // when the host stopped keeping a version (ADR 0036)
    'WorkspaceRequestChangedPayload.observed', // what the computer saw, when no reply came
    'WorkspaceRequestReply.provider', // where a remote reply ran, by its declared provider (ADR 0030, 2026-11-12)
    'WorkspaceRequestResumePayload.confirm', // the person's one press on a waiting proposal (ADR 0060)
    'WorkspaceRequestStep.made', // what a start's press has made so far (the recap is the review, 2026-11-13)
    'WorkspaceRequestStep.needs_choice', // the list a start needs a row of (the recap is the review, 2026-11-13)
    'WorkspaceRequestStep.place', // the client's place the assistant picked (ADR 0060)
    'WorkspaceRequestStep.proposal', // what the assistant proposed (ADR 0060)
    'WorkspaceRequestStepProposal.confirm_words', // the host's review words a proposal carries (2026-11-13)
    'WorkspaceRequestStepProposal.facts', // the host's review facts a proposal carries (2026-11-13)
    'WorkspaceRequestStepProposal.warnings', // the host's review warnings a proposal carries (2026-11-13)
    'WorkspaceRequestStepRecordedPayload.proposed_before', // the commands a start runs before its run (2026-11-13)
    'WorkspaceRequestStepRecordedPayload.proposed_command', // the command a proposal prepared (ADR 0060)
    'WorkspaceRequestSubmitPayload.act', // the person's own press on an entry (ADR 0060)
    'WorkspaceRequestSubmitPayload.places', // the places the client can go (ADR 0060)
    'WorkspaceRequestView.observed', // the same, on the view
    'WorkspaceRequestView.places', // the places the client can go (ADR 0060)
    'WorkstreamAddToProjectPayload.review', // the review an add carries (ADR 0051)
    'WorkstreamCopyChangesReadPayload.files', // how each file a copy's reading found changed (ADR 0045)
    'WorkstreamCopyChangesReadPayload.review', // the review a copy's reading is, for an add (ADR 0051)
    'WorkstreamCreatePayload.from_checkpoint', // the version a task starts from (ADR 0045)
    'WorkstreamCreatedPayload.from_checkpoint', // the version a task starts from (ADR 0045)
    'WorkstreamView.copy', // a task's own copy (ADR 0045)
    'WorkstreamView.from_checkpoint', // the version a task starts from (ADR 0045)
  ];

  /** The optional members of generated C#, as `Class.json_name`, each with its attribute and type. */
  function optionalMembers(
    source: string,
  ): { readonly name: string; readonly attribute: string; readonly declaration: string }[] {
    const lines = source.split('\n');
    const found: { name: string; attribute: string; declaration: string }[] = [];
    let owner = '?';
    lines.forEach((line, index) => {
      const opened = /^\s*public (?:sealed |abstract |partial )*class (\w+)/.exec(line);
      if (opened) owner = opened[1] ?? '?';
      const member =
        /\[(?:global::Newtonsoft\.Json\.)?JsonProperty\("(\w+)", Required = (?:global::Newtonsoft\.Json\.)?Required\.Default/.exec(
          line,
        );
      if (member) {
        found.push({
          name: `${owner}.${member[1]}`,
          attribute: line.trim(),
          declaration: (lines[index + 1] ?? '').trim(),
        });
      }
    });
    return found;
  }

  test('the optional members are exactly those listed, each reading null when missing and omitted when null', () => {
    assert.deepEqual(
      OPTIONAL_MEMBERS,
      [...OPTIONAL_MEMBERS].sort(),
      'keep the list sorted, so changes made apart touch different lines',
    );
    const found = optionalMembers(generated);
    assert.deepEqual(
      found.map((member) => member.name).sort(),
      OPTIONAL_MEMBERS,
      'a member made optional, or no longer optional, is named here',
    );
    for (const member of found) {
      assert.match(
        member.attribute,
        /NullValueHandling = (?:global::Newtonsoft\.Json\.)?NullValueHandling\.Ignore\)\]$/,
        member.name,
      );
      assert.match(member.declaration, /^public \S+\? \w+ \{ get; set; \}$/, member.name);
    }
  });

  test('a member optional by mistake, or one no longer optional, is found by name', () => {
    const probe = [
      'public sealed class Probe',
      '{',
      '    [JsonProperty("kept", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]',
      '    public string? Kept { get; set; }',
      '    [JsonProperty("needed", Required = Required.Always)]',
      '    public string Needed { get; set; }',
      '}',
    ].join('\n');
    assert.deepEqual(
      optionalMembers(probe).map((member) => member.name),
      ['Probe.kept'],
    );
    const loosened = probe.replace(
      '"needed", Required = Required.Always',
      '"needed", Required = Required.Default',
    );
    assert.deepEqual(
      optionalMembers(loosened).map((member) => member.name),
      ['Probe.kept', 'Probe.needed'],
    );
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

  test('a variant shape two unions share becomes a class under each union', () => {
    for (const [variant, union] of [
      ['NotFoundUnderstanding', 'UnderstandingResult'],
      ['NotFoundEvaluation', 'EvaluationResult'],
    ])
      assert.ok(generated.includes(`public sealed class ${variant} : ${union}`), variant);
  });
});

describe('fitting a question', () => {
  const prompt = {
    key: 'q0',
    header: 'Database',
    text: 'Which database?',
    options: [{ label: 'Staging', description: null }],
    multiple: false,
    free_text: true,
    secret: false,
  };
  const validate = compileValidator(QuestionPrompts);

  test("cuts each text to its field's limit, marks the cut, and leaves it unanswerable", () => {
    for (const long of [
      { key: 'k'.repeat(257) },
      { header: 'h'.repeat(201) },
      { text: '😀'.repeat(4001) },
      { options: [{ label: 'l'.repeat(201), description: null }] },
      { options: [{ label: 'Staging', description: 'd'.repeat(1001) }] },
    ]) {
      const fitted = fitQuestion([{ ...prompt, ...long }], true);
      assert.equal(fitted.answerable, false, Object.keys(long)[0]);
      assert.match(JSON.stringify(fitted.prompts), / \[truncated\]/);
      assert.ok(validate(fitted.prompts).ok, Object.keys(long)[0]);
    }
    // 4000 emoji are 4000 characters as the contract counts them: whole.
    const emoji = { ...prompt, text: '😀'.repeat(4000) };
    assert.deepEqual(fitQuestion([emoji], true), { prompts: [emoji], answerable: true });
  });

  test('gives back a question within every limit as it was, with its own answerability', () => {
    const prompts = [prompt];
    const fitted = fitQuestion(prompts, false);
    assert.equal(fitted.prompts, prompts);
    assert.equal(fitted.answerable, false);
    assert.equal(fitQuestion(prompts, true).answerable, true);
  });
});

describe('the catalog (ADR 0039)', () => {
  const ref = {
    source: 'omnorium',
    repository_id: 4_100_001,
    full_name: 'harbor-labs/tide.web',
  };
  const link = (catalog: unknown) =>
    parseCommandEnvelope({
      ...COMMAND,
      command_type: 'project.link_catalog',
      payload: { project_id: COMMAND.payload.execution_id, catalog },
    });

  test('a project links to one catalog repository by its numeric id and owner/name', () => {
    assert.equal(link(ref).ok, true);
    const unlink = parseCommandEnvelope({
      ...COMMAND,
      command_type: 'project.unlink_catalog',
      payload: { project_id: COMMAND.payload.execution_id },
    });
    assert.equal(unlink.ok, true);
  });

  test("owner/name follows GitHub's characters, so no path, URL or credential can pass as one", () => {
    for (const fullName of [
      'a/b',
      'a-1/.x',
      'a/..x',
      'A9/name_with.dots-1',
      `${'o'.repeat(39)}/${'n'.repeat(100)}`,
    ])
      assert.equal(link({ ...ref, full_name: fullName }).ok, true, fullName);
    for (const fullName of [
      'a/.',
      'a/..',
      '-a/b',
      'a/b/c',
      'a',
      '/b',
      'a/',
      'a b/c',
      'a/b c',
      'token@github.com/a/b',
      'https://github.com/a/b',
      'a:b/c',
      `${'o'.repeat(40)}/b`,
      `a/${'n'.repeat(101)}`,
    ])
      assert.equal(link({ ...ref, full_name: fullName }).ok, false, fullName);
    for (const id of [0, -1, 1.5]) assert.equal(link({ ...ref, repository_id: id }).ok, false);
    assert.equal(link({ ...ref, source: 'github' }).ok, false);
    assert.equal(link({ ...ref, url: 'https://github.com/a/b' }).ok, false, 'no other field');
  });

  test('the link events carry the catalog repository the person chose and the one it replaced', () => {
    const base = sampleEvent('project.created');
    const linked = parseEventEnvelope({
      ...base,
      event_type: 'project.catalog_linked',
      payload: { catalog: ref, previous: null },
    });
    assert.equal(linked.ok, true);
    const unlinked = parseEventEnvelope({
      ...base,
      event_type: 'project.catalog_unlinked',
      payload: { previous: ref },
    });
    assert.equal(unlinked.ok, true);
  });

  const validate = compileValidator(CatalogResponse);
  const project = {
    catalog_id: '6f1c0b2e-9d3a-4e7b-8c1d-2a3b4c5d6e7f',
    name: 'Tide',
    group_id: null,
    group_order: null,
    repositories: [
      {
        repository_id: 4_100_001,
        full_name: 'harbor-labs/tide-web',
        primary: true,
        visibility: 'private',
        fork: false,
        archived: false,
        language: 'TypeScript',
        description: null,
        pushed_at: '2026-10-29T10:00:00Z',
        access: 'ok',
      },
    ],
    state: {
      state: 'review_requested',
      active: true,
      last_activity_at: '2026-10-29T10:00:00Z',
      sessions: { running: 0, awaiting_input: 0 },
      unknown: [],
    },
    attention: [
      {
        kind: 'review_requested',
        repository_id: 4_100_001,
        pull_request: { number: 12, title: 'Add a tide chart' },
        at: '2026-10-29T09:00:00Z',
      },
    ],
    local: { project_ids: [], suggestions: [] },
  };
  const answer = {
    availability: 'available',
    provenance: 'reported',
    source: { system: 'omnorium', api_version: '1', synthetic: false, sample: false },
    generated_at: '2026-10-29T10:01:00Z',
    sync: {
      repositories_at: '2026-10-29T09:50:00Z',
      repositories_failed: false,
      activity_at: null,
      activity_failed: true,
    },
    groups: [],
    projects: [project],
    truncated: null,
  };

  test('an available catalog is reported by Omnorium, never observed', () => {
    assert.equal(validate(answer).ok, true);
    assert.equal(validate({ ...answer, provenance: 'observed' }).ok, false);
    assert.equal(validate({ ...answer, provenance: undefined }).ok, false);
    assert.equal(validate({ ...answer, sync: undefined }).ok, false, 'its age is always said');
  });

  test('a state Halcyonic does not know yet still reads, as Omnorium may add states', () => {
    const later = { ...project, state: { ...project.state, state: 'deploy_failed' } };
    assert.equal(validate({ ...answer, projects: [later] }).ok, true);
    const bad = { ...project, state: { ...project.state, state: 'Not A State' } };
    assert.equal(validate({ ...answer, projects: [bad] }).ok, false);
  });

  test('no link reaches the headset, and the bounds hold', () => {
    const withUrl = {
      ...project,
      repositories: [{ ...project.repositories[0], html_url: 'https://github.com/a/b' }],
    };
    assert.equal(validate({ ...answer, projects: [withUrl] }).ok, false);
    const pr = {
      ...project.attention[0],
      pull_request: { number: 12, title: 'x', url: 'https://x' },
    };
    assert.equal(validate({ ...answer, projects: [{ ...project, attention: [pr] }] }).ok, false);
    const tooMany = { ...project, attention: Array(6).fill(project.attention[0]) };
    assert.equal(validate({ ...answer, projects: [tooMany] }).ok, false);
    const none = { ...project, repositories: [] };
    assert.equal(validate({ ...answer, projects: [none] }).ok, false);
  });

  test('a folder suggestion names its rule, so it reads as inferred', () => {
    const suggested = {
      ...project,
      local: {
        project_ids: [],
        suggestions: [{ root: '/work', folder_name: 'tide-web', rule: 'github_origin_full_name' }],
      },
    };
    assert.equal(validate({ ...answer, projects: [suggested] }).ok, true);
    const unnamed = {
      ...project,
      local: { project_ids: [], suggestions: [{ root: '/work', folder_name: 'tide-web' }] },
    };
    assert.equal(validate({ ...answer, projects: [unnamed] }).ok, false);
  });

  test('every other answer carries no catalog, only why', () => {
    for (const availability of ['unavailable', 'unauthorized', 'incompatible'])
      assert.equal(
        validate({ availability, reason: { code: 'not_running', message: 'Not running.' } }).ok,
        true,
      );
    assert.equal(
      validate({ availability: 'unavailable', reason: { code: 'x', message: 'y' }, projects: [] })
        .ok,
      false,
    );
  });
});

describe("a check's laptop step says why a twin read was unknown (M1.8g-b)", () => {
  const validate = compileValidator(CheckLaptop);
  const laptop = {
    layer: 'starter',
    targets: 1,
    mouse: 'reached',
    key: 'reached',
    screen_changed: null,
    response_seen: null,
    ready: false,
  };
  test('one of five reasons, or absent on earlier checks; nothing else', () => {
    for (const reason of [
      'controls_differ',
      'press_differs_before',
      'repeat_differs',
      'out_of_time',
      'failed',
    ])
      assert.equal(validate({ ...laptop, response_unknown: reason }).ok, true, reason);
    assert.equal(validate(laptop).ok, true);
    assert.equal(validate({ ...laptop, response_unknown: null }).ok, true);
    assert.equal(validate({ ...laptop, response_unknown: 'flaky' }).ok, false);
  });
});

describe("a check's laptop step says whether a person can read the app on a laptop (VRIFY, 2026-11-13)", () => {
  const validate = compileValidator(CheckLaptop);
  const laptop = {
    layer: 'starter',
    targets: 1,
    mouse: 'reached',
    key: 'reached',
    screen_changed: true,
    ready: false,
  };
  const reading = {
    state: 'measured',
    smallest_px: 6,
    measured: 12,
    fill_share: 0.18,
    floor_px: READING_FLOOR_PX,
    last_measured_px: null,
    words: 'The words are too small to read on a laptop',
    detail: '6 px high; at least 10 needed',
  };
  test('measured, not known or changed, holding a failure last measured or not; absent on earlier checks', () => {
    assert.equal(validate(laptop).ok, true);
    assert.equal(validate({ ...laptop, reading }).ok, true);
    for (const state of ['measured', 'unknown', 'changed'])
      assert.equal(validate({ ...laptop, reading: { ...reading, state } }).ok, true, state);
    const unknown = {
      ...reading,
      state: 'unknown',
      smallest_px: null,
      measured: 0,
      fill_share: null,
      detail: null,
    };
    assert.equal(validate({ ...laptop, reading: unknown }).ok, true);
    assert.equal(validate({ ...laptop, reading: { ...unknown, last_measured_px: 6 } }).ok, true);
  });
  test('nothing else: no other state, no share past the whole, no empty words, no extra member', () => {
    for (const bad of [
      { ...reading, state: 'guessed' },
      { ...reading, fill: 'measured' },
      { ...reading, floor_fill: 0.32 },
      { ...reading, last_measured_px: -1 },
      { ...reading, fill_share: 1.2 },
      { ...reading, smallest_px: -1 },
      { ...reading, measured: 65 },
      { ...reading, words: '' },
      { ...reading, words: 'x'.repeat(201) },
      { ...reading, passed: true },
    ])
      assert.equal(
        validate({ ...laptop, reading: bad }).ok,
        false,
        JSON.stringify(bad).slice(0, 80),
      );
    const { words: _words, ...wordless } = reading;
    assert.equal(validate({ ...laptop, reading: wordless }).ok, false);
    const { last_measured_px: _held, ...unheld } = reading;
    assert.equal(validate({ ...laptop, reading: unheld }).ok, false);
  });
  test('the one floor is 10 px words', () => {
    assert.equal(READING_FLOOR_PX, 10);
  });
});

describe("whether a check's browser had permissions of its own (S1)", () => {
  const validate = compileValidator(CheckFindings);
  const findings = {
    tsc: { exit: 0, seconds: 1 },
    build: { exit: 0, seconds: 2 },
    page: null,
    unknown: null,
    passed: false,
    reads_confined: true,
  };
  test('true, false, or absent on earlier checks, which reads as not known; nothing else', () => {
    assert.equal(validate(findings).ok, true, JSON.stringify(validate(findings)));
    assert.equal(validate({ ...findings, own_permissions: true }).ok, true);
    assert.equal(validate({ ...findings, own_permissions: false }).ok, true);
    assert.equal(validate({ ...findings, own_permissions: null }).ok, false);
    assert.equal(validate({ ...findings, own_permissions: 'yes' }).ok, false);
  });
});

describe("a project's view with its check, preview and play (M1.8e, T2)", () => {
  const validate = compileValidator(ProjectView);
  const project = {
    project_id: '01a19000-0000-7000-8000-000000000001',
    name: 'Garden',
    location: null,
    starter: null,
    created_at: '2026-10-31T05:00:00.000Z',
    updated_at: '2026-10-31T05:00:00.000Z',
  };
  const checkId = '01a19000-0000-7000-8000-0000000000c1';
  const findings = {
    tsc: { exit: 0, seconds: 1 },
    build: { exit: 0, seconds: 2 },
    page: null,
    unknown: null,
    passed: false,
    reads_confined: false,
  };
  test('each member is optional, absent when there is none, never null, and carries when it ran and whether its files are still current', () => {
    assert.equal(validate(project).ok, true);
    const full = {
      ...project,
      check: {
        check_id: checkId,
        recorded_at: '2026-10-31T05:01:00.000Z',
        position: 4,
        files: 'current',
        findings,
      },
      preview: { check_id: checkId, port: 47931, files: 'current' },
      mac_play: { check_id: checkId, files: 'current' },
    };
    assert.equal(validate(full).ok, true, JSON.stringify(validate(full)));
    for (const files of ['unknown', 'current', 'changed']) {
      assert.equal(
        validate({
          ...full,
          check: { ...full.check, files },
          preview: { ...full.preview, files },
          mac_play: { ...full.mac_play, files },
        }).ok,
        true,
        files,
      );
    }
    assert.equal(validate({ ...full, check: { ...full.check, files: 'passed' } }).ok, false);
    assert.equal(validate({ ...full, check: { ...full.check, position: -1 } }).ok, false);
    for (const member of ['check', 'preview', 'mac_play'])
      assert.equal(validate({ ...project, [member]: null }).ok, false, member);
    assert.equal(
      validate({ ...full, preview: { check_id: checkId, port: 0, files: 'current' } }).ok,
      false,
    );
    assert.equal(
      validate({ ...full, mac_play: { check_id: checkId, files: 'current', link: 'x' } }).ok,
      false,
    );
    assert.equal(validate({ ...full, check: { check_id: checkId, findings } }).ok, false);
  });
});

describe('making a VR version of a project (M1.8c-1)', () => {
  const ORIGINAL = '0192f1a0-0000-7000-8000-0000000000aa';
  const make = (payload: Record<string, unknown>) =>
    parseCommandEnvelope({ ...COMMAND, command_type: 'project.make_vr_version', payload });

  test('the command names the original and where the new folder goes, never a path of its own', () => {
    const payload = {
      original_project_id: ORIGINAL,
      root: '/Users/someone/dev',
      folder_name: 'garden-vr',
    };
    assert.equal(make(payload).ok, true);
    assert.equal(make({ ...payload, folder_name: 'a/b' }).ok, false);
    assert.equal(make({ ...payload, starter_id: 'iwsdk-vr' }).ok, false);
    assert.equal(make({ root: payload.root, folder_name: payload.folder_name }).ok, false);
  });

  test('the new project records the original it was made from and the instruction the host wrote', () => {
    const base = sampleEvent('project.created');
    const event = {
      ...base,
      event_type: 'project.made_from',
      payload: {
        original_project_id: ORIGINAL,
        instruction: 'Make a VR version of the web app in /x.',
      },
    };
    assert.equal(parseEventEnvelope(event).ok, true);
    assert.equal(
      parseEventEnvelope({ ...event, payload: { original_project_id: ORIGINAL } }).ok,
      false,
    );
  });
});

describe("a task's own copy and adding its work (ADR 0045)", () => {
  const WORKSTREAM = '0192f1a0-0000-7000-8000-0000000000dd';
  const TREE = 'a'.repeat(64);
  const event = (event_type: string, payload: Record<string, unknown>) =>
    parseEventEnvelope({ ...sampleEvent('workstream.created'), event_type, payload }).ok;

  test('adding and fitting name only the task; the host decides everything else', () => {
    for (const command_type of ['workstream.add_to_project', 'workstream.fit_to_project']) {
      const make = (payload: Record<string, unknown>) =>
        parseCommandEnvelope({ ...COMMAND, command_type, payload }).ok;
      assert.equal(make({ workstream_id: WORKSTREAM }), true, command_type);
      assert.equal(make({ workstream_id: WORKSTREAM, folder: '/x' }), false, command_type);
      assert.equal(make({}), false, command_type);
    }
  });

  test('the copy, its reading, an add, a conflict and a fit carry trees, counts and paths, never contents', () => {
    const paths = { paths: ['src/a.ts'], total: 1 };
    assert.equal(
      event('workstream.copy_made', { directory: '/c/app', base_tree: TREE, files: 2 }),
      true,
    );
    assert.equal(
      event('workstream.copy_made', { directory: '/c/app', base_tree: 'abc', files: 2 }),
      false,
    );
    assert.equal(
      event('workstream.copy_refused', { code: 'linked_repository', message: 'No.' }),
      true,
    );
    assert.equal(event('workstream.copy_refused', { code: 'bored', message: 'No.' }), false);
    assert.equal(event('workstream.copy_changes_read', { tree: TREE, changed: paths }), true);
    assert.equal(
      event('workstream.copy_changes_read', { tree: TREE, changed: { paths: ['../x'], total: 1 } }),
      false,
      'a path never leaves the folder',
    );
    const added = {
      checkpoint_id: '0192f1a0-0000-7000-8000-0000000000ee',
      task_tree: TREE,
      project_tree: TREE,
      merged_tree: TREE,
      added: paths,
    };
    assert.equal(event('workstream.added', added), true);
    assert.equal(event('workstream.added', { ...added, contents: 'x' }), false);
    const conflicted = { files: [{ path: 'img.png', kind: 'binary' }], total: 1 };
    assert.equal(
      event('workstream.add_conflicted', { task_tree: TREE, project_tree: TREE, conflicted }),
      true,
    );
    assert.equal(
      event('workstream.add_conflicted', {
        task_tree: TREE,
        project_tree: TREE,
        conflicted: { files: [], total: 0 },
      }),
      false,
      'a conflict names at least one file',
    );
    assert.equal(
      event('workstream.fitted', { project_tree: TREE, fitted_tree: TREE, written: paths }),
      true,
    );
  });
});

describe('playing a checked game on the Mac (Play on my laptop, P-A)', () => {
  const PROJECT = '0192f1a0-0000-7000-8000-0000000000bb';
  const CHECK = '0192f1a0-0000-7000-8000-0000000000cc';
  test('the command names only the project; the host decides everything else', () => {
    const open = (payload: Record<string, unknown>) =>
      parseCommandEnvelope({ ...COMMAND, command_type: 'project.open_on_mac', payload });
    assert.equal(open({ project_id: PROJECT }).ok, true);
    assert.equal(open({ project_id: PROJECT, url: 'http://127.0.0.1:1/' }).ok, false);
    assert.equal(open({}).ok, false);
  });

  test('the window is recorded as started for a check, and as stopped with one of its reasons', () => {
    const base = sampleEvent('project.created');
    const event = (event_type: string, payload: Record<string, unknown>) =>
      parseEventEnvelope({ ...base, event_type, payload }).ok;
    assert.equal(event('project.mac_play.started', { check_id: CHECK }), true);
    for (const reason of ['closed', 'preview_stopped', 'newer_check', 'host_stopped', 'failed'])
      assert.equal(event('project.mac_play.stopped', { check_id: CHECK, reason }), true, reason);
    assert.equal(event('project.mac_play.stopped', { check_id: CHECK, reason: 'bored' }), false);
    assert.equal(event('project.mac_play.started', { check_id: CHECK, pid: 12 }), false);
  });
});

describe("the host's own denial of a request (ADR 0065)", () => {
  const denial = (payload: Record<string, unknown>) =>
    parseEventEnvelope({
      ...sampleEvent('execution.created'),
      event_type: 'execution.approval.denied_by_host',
      source: { kind: 'control_plane' },
      payload,
    }).ok;

  test('a request too long to show carries its length; one the journal could not record carries none', () => {
    assert.equal(
      denial({ approval_id: 'a', because: 'too_long_to_show', length: 5011, delivered: true }),
      true,
    );
    assert.equal(denial({ approval_id: 'a', because: 'not_recorded', delivered: false }), true);
    assert.equal(denial({ approval_id: 'a', because: 'bored', delivered: false }), false);
    assert.equal(denial({ approval_id: '', because: 'not_recorded', delivered: false }), false);
    assert.equal(
      denial({ approval_id: 'a', because: 'not_recorded', delivered: false, text: 'rm -rf /' }),
      false,
      'never the request itself',
    );
  });

  test('a question the journal could not record carries its id, the reason and whether the end was asked, never its words', () => {
    const ended = (payload: Record<string, unknown>) =>
      parseEventEnvelope({
        ...sampleEvent('execution.created'),
        event_type: 'execution.question.ended_by_host',
        source: { kind: 'control_plane' },
        payload,
      }).ok;
    assert.equal(
      ended({ question_id: 'q', because: 'not_recorded', interrupt_requested: true }),
      true,
    );
    assert.equal(
      ended({ question_id: 'q', because: 'not_recorded', interrupt_requested: false }),
      true,
    );
    assert.equal(
      ended({ question_id: 'q', because: 'too_long_to_show', interrupt_requested: false }),
      false,
    );
    assert.equal(
      ended({ question_id: '', because: 'not_recorded', interrupt_requested: false }),
      false,
    );
    assert.equal(ended({ question_id: 'q', because: 'not_recorded' }), false);
    assert.equal(
      ended({
        question_id: 'q',
        because: 'not_recorded',
        interrupt_requested: true,
        text: 'Which one?',
      }),
      false,
      'never the question itself',
    );
  });
});
