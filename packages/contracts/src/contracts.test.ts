import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import Type from 'typebox';
import Schema from 'typebox/schema';
import { renderCSharpContracts, renderObjectMembers } from './csharp.ts';
import {
  buildSchemaDocument,
  CatalogResponse,
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
  parseClientMessage,
  parseCommandEnvelope,
  parseEventEnvelope,
  QuestionPrompts,
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

  test('shared definitions are referenced rather than repeated', () => {
    const text = renderSchemaDocument();
    assert.ok(text.includes('"$ref": "#/$defs/CommandEnvelope"'));
    // Repetition is checked directly; the bound only guards the document's growth (raised from
    // 400,000 for M1.8b's check findings, which repeat nothing).
    assert.deepEqual(
      inlinedDefinitions(buildSchemaDocument() as { $defs: Record<string, unknown> }),
      [],
    );
    assert.ok(text.length < 420_000, `document is ${text.length} bytes`);
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
      /\[JsonProperty\("found", Required = Required\.Default, NullValueHandling = NullValueHandling\.Ignore\)\]\s+public ProbeFound\? Found \{ get; set; \}/,
    );
    assert.match(
      members,
      /\[JsonProperty\("names", Required = Required\.Default, NullValueHandling = NullValueHandling\.Ignore\)\]\s+public List<string>\? Names \{ get; set; \}/,
    );
    assert.match(members, /public long\? Level \{ get; set; \}/);
    // A constant still cannot be optional: its absence would read as a different value.
    assert.throws(
      () => renderObjectMembers('Probe', Type.Object({ kind: Type.Optional(Type.Literal('x')) })),
      /a constant cannot be optional/,
    );
  });

  test('optional strings and declaration booleans read as null when missing and are omitted when null', () => {
    assert.match(
      generated,
      /\[JsonProperty\("label", Required = Required\.Default, NullValueHandling = NullValueHandling\.Ignore\)\]\s+public string\? Label \{ get; set; \}/,
    );
    assert.equal(
      generated.match(/Required = Required\.Default/g)?.length,
      13,
      "the optional members are a location's label, a completed tool's turn, two declarations, a check's laptop, vr, notes and ready (M1.8b), project_checked's ready, the laptop step's response_seen and exceptions_during (M1.8g), why a twin read was unknown (M1.8g-b), and whether an execution's start recorded it confined (S2)",
    );
    for (const member of [
      'Laptop',
      'Vr',
      'Notes',
      'Ready',
      'ResponseSeen',
      'ExceptionsDuring',
      'ResponseUnknown',
      'Confined',
    ])
      assert.match(generated, new RegExp(`public \\w+\\? ${member} \\{ get; set; \\}`), member);
    assert.match(
      generated,
      /\[JsonProperty\("reports_tool_results", Required = Required\.Default, NullValueHandling = NullValueHandling\.Ignore\)\]\s+public bool\? ReportsToolResults \{ get; set; \}/,
    );
    assert.match(
      generated,
      /\[JsonProperty\("approval_may_leave_sandbox", Required = Required\.Default, NullValueHandling = NullValueHandling\.Ignore\)\]\s+public bool\? ApprovalMayLeaveSandbox \{ get; set; \}/,
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
    source: 'phat-porcupine',
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
    source: { system: 'phat-porcupine', api_version: '1', synthetic: false, sample: false },
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

  test('an available catalog is reported by Phat Porcupine, never observed', () => {
    assert.equal(validate(answer).ok, true);
    assert.equal(validate({ ...answer, provenance: 'observed' }).ok, false);
    assert.equal(validate({ ...answer, provenance: undefined }).ok, false);
    assert.equal(validate({ ...answer, sync: undefined }).ok, false, 'its age is always said');
  });

  test('a state Halcyonic does not know yet still reads, as Phat Porcupine may add states', () => {
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
