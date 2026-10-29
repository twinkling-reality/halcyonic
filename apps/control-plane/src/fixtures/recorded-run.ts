import type {
  ClientInfo,
  EventEnvelope,
  RuntimeDescriptor,
  RuntimeId,
  ServerMessage,
} from '@halcyonic/contracts';
import { REALTIME_PROTOCOL_VERSION } from '@halcyonic/contracts';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { redactSecrets } from '../core/redaction.ts';
import { createSeededRandom, createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { OwnWordsError, silentLogger } from '../logger.ts';
import {
  commandPolicies,
  type DemonstrationAnswer,
  type DemonstrationNode,
  END_HOLD_MS,
  serializeNodes,
} from './demonstration.ts';

type WelcomeMessage = Extract<ServerMessage, { type: 'welcome' }>;
type SnapshotMessage = Extract<ServerMessage, { type: 'snapshot' }>;
type EventMessage = Extract<ServerMessage, { type: 'event' }>;

export const RECORDED_RUN_SOURCE = 'apps/control-plane/src/fixtures/recorded-run.ts';

/** The neutral name a recorded run's runtime goes by, whatever product it was. */
export const RECORDED_RUNTIME_ID = 'recorded-agent' as RuntimeId;
export const RECORDED_RUNTIME_KIND = 'recorded';
export const RECORDED_RUNTIME_NAME = 'Coding agent';

/** What stands for the model the runtime reported using: a model reference has no spaces. */
export const RECORDED_MODEL = 'local-model';

/** Who sent the run's commands, as the recording says it: no client's own name or device. */
export const RECORDED_CLIENT: ClientInfo = {
  name: 'recorded run',
  version: null,
  device_label: null,
};

/** The longest wait the playback keeps between two events; each event keeps its real time. */
export const MAX_GAP_MS = 2500;

/**
 * The event types the recorder was written and checked for: what each one's strings can hold is
 * something its rewrites and scan judge. A journal holding any other type is refused, because a new
 * type may carry words no rule knows, such as a private repository's owner and name. Adding one here
 * is a review of what it journals. A device's pairing and revocation are left out on purpose: no
 * recording needs them, and a pairing carries a self-declared label and the fingerprint of the
 * computer's certificate.
 */
const LISTED_EVENT_TYPES: ReadonlySet<string> = new Set<EventEnvelope['event_type']>([
  'project.created',
  'project.location_set',
  'project.starter_applied',
  'project.checkpoint.recorded',
  'project.checkpoint.failed',
  'project.restored',
  'project.restore_incomplete',
  'workstream.created',
  'execution.created',
  'execution.files.observed',
  'execution.start_failed',
  'execution.state_unknown',
  'command.accepted',
  'command.rejected',
  'command.completed',
  'command.failed',
  'workspace.request.created',
  'workspace.request.changed',
  'workspace.request.step_recorded',
  'runtime.execution.started',
  'runtime.turn.started',
  'runtime.turn.completed',
  'runtime.turn.failed',
  'runtime.turn.interrupted',
  'runtime.approval.requested',
  'runtime.approval.resolved',
  'runtime.question.asked',
  'runtime.question.resolved',
  'runtime.tool.started',
  'runtime.tool.completed',
  'runtime.tool.result',
  'runtime.agent_message',
  'runtime.test_run.started',
  'runtime.test_run.completed',
  'runtime.connection.lost',
  'runtime.connection.restored',
  'runtime.model.used',
]);

/** What a path at or under the project's folder reads as once the folder itself is left out. */
const PROJECT_FOLDER = 'the project folder';

/**
 * Names a judge must not read: agent apps, models, companies, platforms and our own earlier
 * products. The same list, word for word, as the headset's `JudgeWordsTests`, which a test holds.
 * Matched as written, as that test does, so English words that share their letters still pass.
 */
export const JUDGE_MUST_NOT_READ = [
  'Claude',
  'Anthropic',
  'OpenAI',
  'Codex',
  'OpenCode',
  'GPT',
  'Gemini',
  'Llama',
  'Qwen',
  'Ollama',
  'Mistral',
  'Meta',
  'Quest',
  'Oculus',
  'Horizon',
  'Apple',
  'Mac',
  'macOS',
  'iPhone',
  'Android',
  'Google',
  'YouTube',
  'GitHub',
  'Microsoft',
  'Windows',
  'Unity',
  'Salidium',
  'Seorak',
  'Stripe',
  'Postgres',
  'PostgreSQL',
  'Redis',
  'Docker',
  'AWS',
  'npm',
  'React',
] as const;

/**
 * Names no English word shares, refused in any case: runtimes and models write their own names in
 * lower case in commands, paths and identifiers.
 */
const NAMES_IN_ANY_CASE = [
  'claude',
  'anthropic',
  'openai',
  'codex',
  'opencode',
  'qwen',
  'ollama',
  'gemini',
  'mistral',
  'oculus',
  'macos',
  'iphone',
  'android',
  'github',
  'youtube',
  'unity',
  'salidium',
  'seorak',
  'postgres',
  'docker',
  'boopy',
  // Found passing by AUDIT's probe on 2026-10-28: models by their tags, and products in mixed case.
  'gpt',
  'llama',
  'deepseek',
  'exulanica',
  'macbook',
];

/** Text a recorded run must not carry, by what it is; each test runs on one string value. */
const SHAPES: readonly { readonly reason: string; readonly test: (text: string) => boolean }[] = [
  { reason: 'a user folder', test: (text) => /\/Users\/|\/home\/|[A-Z]:\\Users\\/i.test(text) },
  { reason: 'a private folder', test: (text) => /\.private\b|\.claude\b|worktrees?\//.test(text) },
  {
    reason: 'the local host',
    test: (text) => /\blocalhost\b|\b127\.0\.0\.1\b|\[::1\]/i.test(text),
  },
  { reason: 'an address', test: (text) => /\b\d{1,3}(?:\.\d{1,3}){3}\b/.test(text) },
  { reason: 'an email', test: (text) => /[^\s@"'<>]+@[^\s@"'<>]+\.[a-z]{2,}/i.test(text) },
  { reason: 'a URL', test: (text) => /\b[a-z][a-z0-9+.-]*:\/\/\S/i.test(text) },
  { reason: 'a credential', test: (text) => redactSecrets(text, []) !== text },
];

/** Fields that hold identifiers Halcyonic or the runtime made, never words, so no shape applies. */
const IDENTIFIER_FIELDS = new Set([
  'event_id',
  'project_id',
  'workstream_id',
  'execution_id',
  'correlation_id',
  'causation_id',
  'command_id',
  'source_native_id',
  'native_id',
  'turn_id',
  'tool_call_id',
  'approval_id',
  'question_id',
  'journal_id',
]);

export interface RecordedRunOptions {
  /** The project's folder on the computer that ran it; paths under it are kept relative to it. */
  readonly projectRoot: string;
  /** That computer's home folder, which must not appear anywhere once the project's is gone. */
  readonly home: string;
  /**
   * Further words the recording must not carry, matched in any case: at least the owner's names
   * and any other person's, since an agent quoting a commit's author carries them. Required.
   */
  readonly deny: readonly string[];
}

/** Where a recorded run carries something a judge must not read: never the text itself. */
export interface RecordedRunFinding {
  /** The event's index in the run, from 0. */
  readonly index: number;
  readonly event_type: string;
  /** The field's path in the envelope, such as `payload.text`. */
  readonly field: string;
  readonly reason: string;
}

export class RecordedRunError extends OwnWordsError {
  readonly findings: readonly RecordedRunFinding[];

  constructor(message: string, findings: readonly RecordedRunFinding[] = []) {
    const listed = findings
      .slice(0, 20)
      .map(
        (finding) =>
          `\n  event ${finding.index} (${finding.event_type}) ${finding.field}: ${finding.reason}`,
      )
      .join('');
    super(`${message}${listed}`);
    this.name = 'RecordedRunError';
    this.findings = findings;
  }
}

/**
 * Prepares a real run's journal to be shown. The only changes are where files live, the runtime's
 * identity (a neutral id, kind and name, also in native types and commands), the model the runtime
 * reported, the client that sent the commands, and the output of commands, which is left out
 * (`omitted_scope`, exit codes kept) because tools print their makers' names.
 *
 * Where files live: the project folder's path is rewritten in every string, what the person and the
 * agent said included, wherever it stands as a whole path, that is followed by `/`, the end of the
 * text, white space, a quote, a bracket or punctuation that ends a sentence. A path under it becomes
 * relative to it, and the folder itself reads "the project folder". A folder that only starts with
 * its path, such as `<root>-old`, `<root>2` or `<root>.old`, is another folder, and a longer path
 * or a link that holds it, such as `/Volumes/Backup<root>/...` or `file://<root>/...`, is another
 * place: both are left as they are.
 *
 * Nothing else in instructions, agent messages, approval requests, tool titles, patches, provenance,
 * identifiers or times is edited: if any of them still carries a name a judge must not read, a home
 * or private path, an address, a URL, an email, a credential or a denied word, this throws with
 * every place it found, naming the field and the kind of text but never the text, and the run must
 * be recorded again. The rules find only what they list, so a person reads the whole recording too.
 * A journal holding an event type the recorder does not list is refused whole, naming the type.
 */
export function sanitizeRecordedRun(
  events: readonly EventEnvelope[],
  options: RecordedRunOptions,
): EventEnvelope[] {
  const root = options.projectRoot.replace(/\/+$/, '');
  if (root === '' || !root.startsWith('/')) {
    throw new RecordedRunError('The project folder must be an absolute path.');
  }
  if (options.deny.filter((word) => word.trim() !== '').length === 0) {
    throw new RecordedRunError("A deny list with at least the owner's names is required.");
  }
  const unlisted = [
    ...new Set(
      events
        .map((event) => event.event_type as string)
        .filter((type) => !LISTED_EVENT_TYPES.has(type)),
    ),
  ];
  if (unlisted.length > 0) {
    throw new RecordedRunError(
      `The run holds events the recorder is not written for, so nothing is written: ${unlisted.join(', ')}.`,
    );
  }
  const runtimes = new Map<string, string>();
  for (const event of events) {
    if (event.event_type !== 'execution.created') continue;
    const runtime = event.payload.runtime;
    if (runtime.synthetic) {
      throw new RecordedRunError(
        'A recorded run is real work: its runtime is synthetic, so record the demonstration instead.',
      );
    }
    runtimes.set(runtime.runtime_id, runtime.kind);
  }
  if (runtimes.size === 0) throw new RecordedRunError('The run started no execution.');

  // The root, not inside a longer path or a link, then `/` (taken away with it) or a lookahead
  // for what ends a whole path.
  const whole = new RegExp(
    `(?<![\\w.~/-])${literal(root)}(?:(/)|(?=$|[\\s"'\`,;:!?)\\]}>]|\\.(?:$|[\\s"'\`)\\]}>])))`,
    'g',
  );
  const relative = (text: string) =>
    text.replace(whole, (_match, slash: string | undefined) => (slash ? '' : PROJECT_FOLDER));
  const cleaned = events.map((event) => {
    const copy = rewrite(structuredClone(event), (text) => relative(text)) as EventEnvelope;
    if (copy.source.kind === 'runtime' && runtimes.has(copy.source.runtime_id)) {
      copy.source.runtime_id = RECORDED_RUNTIME_ID;
    }
    const native = copy.provenance.native_type;
    if (native !== null) {
      const slash = native.indexOf('/');
      const kind = slash < 0 ? native : native.slice(0, slash);
      if ([...runtimes.values()].includes(kind)) {
        copy.provenance.native_type = `recorded${slash < 0 ? '' : native.slice(slash)}`;
      }
    }
    switch (copy.event_type) {
      case 'execution.created':
        copy.payload.runtime = {
          runtime_id: RECORDED_RUNTIME_ID,
          kind: RECORDED_RUNTIME_KIND,
          display_name: RECORDED_RUNTIME_NAME,
          synthetic: false,
        };
        break;
      case 'runtime.model.used':
        copy.payload.model_ref = RECORDED_MODEL;
        break;
      case 'command.accepted': {
        const command = copy.payload.command;
        command.client = { ...RECORDED_CLIENT };
        if (command.command_type === 'execution.start') {
          if (runtimes.has(command.payload.runtime_id))
            command.payload.runtime_id = RECORDED_RUNTIME_ID;
          if (command.payload.model_ref !== null) command.payload.model_ref = RECORDED_MODEL;
          // A free-form record whose keys a runtime chose; the playback never starts work.
          command.payload.options = {};
        }
        break;
      }
      case 'runtime.tool.result':
        if (copy.payload.kind === 'command') {
          copy.payload.output = {
            text: null,
            availability: 'omitted_scope',
            complete: false,
            truncation: 'scope',
          };
        }
        break;
    }
    return copy;
  });

  const findings = cleaned.flatMap((event, index) => findingsIn(event, index, options));
  if (findings.length > 0) {
    throw new RecordedRunError(
      `The run carries ${findings.length} thing(s) a judge must not read; record it again.`,
      findings,
    );
  }
  return cleaned;
}

/** A word as a pattern that matches it literally. */
function literal(word: string): string {
  return word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Every string in a value, rewritten; objects and arrays keep their shape. */
function rewrite(value: unknown, change: (text: string) => string): unknown {
  if (typeof value === 'string') return change(value);
  if (Array.isArray(value)) return value.map((item) => rewrite(item, change));
  if (value !== null && typeof value === 'object') {
    const object = value as Record<string, unknown>;
    for (const key of Object.keys(object)) object[key] = rewrite(object[key], change);
  }
  return value;
}

function findingsIn(
  event: EventEnvelope,
  index: number,
  options: RecordedRunOptions,
): RecordedRunFinding[] {
  const found: RecordedRunFinding[] = [];
  const home = options.home.replace(/\/+$/, '');
  // Whole words in any case, so a surname is caught and the word it sits inside is not.
  const deny = options.deny
    .map((word) => word.trim())
    .filter((word) => word !== '')
    .map((word) => new RegExp(`(?<![\\p{L}\\p{N}])${literal(word)}(?![\\p{L}\\p{N}])`, 'iu'));
  /** What a string carries that a judge must not read; `identifier` skips the shapes, not the names. */
  const problems = (value: string, identifier: boolean): string[] => {
    const reasons: string[] = [];
    const named = JUDGE_MUST_NOT_READ.filter((name) => new RegExp(`\\b${name}\\b`).test(value));
    for (const name of named) reasons.push(`names ${name}`);
    for (const name of NAMES_IN_ANY_CASE) {
      if (named.some((brand) => brand.toLowerCase() === name)) continue;
      // Only at a word's start, so "community" is not "unity", while "qwen3" and "codex/" are.
      if (new RegExp(`(?<![a-z])${name}`, 'i').test(value))
        reasons.push(`names ${name} in some case`);
    }
    if (home !== '' && value.includes(home)) reasons.push('the home folder');
    if (identifier) return reasons;
    for (const shape of SHAPES) if (shape.test(value)) reasons.push(shape.reason);
    for (const word of deny) if (word.test(value)) reasons.push('a denied word');
    return reasons;
  };
  const add = (field: string, reason: string) =>
    found.push({ index, event_type: event.event_type, field, reason });
  const visit = (value: unknown, path: string, key: string) => {
    if (Array.isArray(value)) {
      for (const [at, item] of value.entries()) visit(item, `${path}[${at}]`, key);
      return;
    }
    if (value !== null && typeof value === 'object') {
      for (const [name, inner] of Object.entries(value)) {
        // A key is text too, and a refusal's field path would print it: a key with a problem is
        // reported at its parent and stands as "[a key]" in any path below it.
        const reasons = problems(name, false);
        for (const reason of reasons) add(path, `a key that ${reason}`);
        const segment = reasons.length > 0 ? '[a key]' : name;
        visit(inner, path === '' ? segment : `${path}.${segment}`, name);
      }
      return;
    }
    if (typeof value !== 'string') return;
    for (const reason of problems(value, IDENTIFIER_FIELDS.has(key))) add(path, reason);
  };
  visit(event, '', '');
  return found;
}

/**
 * A real run as the XR client plays it: version 3 of the demonstration's format, marked
 * `recorded_run`. Its journal is a `fixture`, so every surface labels it as recorded; its runtime
 * is the one real runtime, under its neutral name, so the work is not labelled simulated. It holds
 * no stand-in answers and no usage limits: nothing about the run is simulated.
 */
export interface RecordedRun {
  readonly version: 3;
  readonly kind: 'recorded_run';
  /** The code it was recorded with, relative to the repository root. */
  readonly source: string;
  readonly welcome: WelcomeMessage;
  readonly snapshot: SnapshotMessage;
  readonly nodes: readonly DemonstrationNode[];
}

/**
 * Records a real run for the headset. The run is sanitized first (`sanitizeRecordedRun`), then a
 * control plane without runtimes is given it through its normal write path, as `pnpm replay`
 * serves a trace, under virtual time, so every message is one the control plane's own journal,
 * projection and publisher computed. The waits between events are shortened to `MAX_GAP_MS` at
 * most; every event keeps the times it really happened at. Wherever someone answered the run's
 * approval request, the recording holds until a person answers, and continues as the run did with
 * the answer it was given: that is the only answer it holds, so it offers no other. After the run's
 * last event the final state holds, with nothing to answer, and the recording starts again.
 *
 * The beginning lists the runtime under its neutral name with one capability, answering
 * approvals: what the recording can play, so a client offers that answer at a hold and nothing else.
 * A run that asked the person a question is refused for now: the recording has no hold for it.
 */
export async function recordRecordedRun(
  trace: readonly EventEnvelope[],
  options: RecordedRunOptions,
): Promise<string> {
  const events = sanitizeRecordedRun(trace, options);
  if (events.some((event) => event.event_type === 'runtime.question.asked')) {
    throw new RecordedRunError('The run asked a question, which a recorded run cannot hold yet.');
  }
  const startAt = events.findIndex(
    (event) =>
      event.event_type === 'command.accepted' &&
      event.payload.command.command_type === 'execution.start',
  );
  if (startAt <= 0)
    throw new RecordedRunError('The run must create its project before it starts work.');
  const prologue = events.slice(0, startAt);
  const played = events.slice(startAt);

  const firstAt = Date.parse(played[0]?.ingested_at ?? '');
  const time = createVirtualTime(new Date(firstAt));
  const ids = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(firstAt),
  });
  const controlPlane = new ControlPlane({
    journal: openSqliteJournal({ path: ':memory:', originIfNew: 'fixture', ids }),
    adapters: [],
    ids,
    clock: time,
    scheduler: time,
    logger: silentLogger,
    commandTimeoutMs: 30_000,
  });
  try {
    for (const event of prologue) controlPlane.recorder.import(event);
    const beginning = controlPlane.snapshot();
    const welcome: WelcomeMessage = {
      type: 'welcome',
      protocol: REALTIME_PROTOCOL_VERSION,
      journal: controlPlane.journal.info,
      head: beginning.position,
      resumed: false,
      server_time: time.now().toISOString(),
      command_policies: commandPolicies(),
    };
    const start = time.now().getTime();
    const captured: { at: number; message: EventMessage }[] = [];
    controlPlane.publisher.subscribe(({ position, event, changes }) => {
      captured.push({
        at: time.now().getTime() - start,
        message: { type: 'event', position, event, changes },
      });
    });

    // Where someone answered an approval: the hold comes before the answer's command.
    const holds: { before: number; answer: DemonstrationAnswer }[] = [];
    let previous = firstAt;
    for (const event of played) {
      const at = Date.parse(event.ingested_at);
      await time.advance(Math.min(Math.max(0, at - previous), MAX_GAP_MS));
      previous = Math.max(previous, at);
      if (event.event_type === 'command.accepted') {
        const command = event.payload.command;
        if (command.command_type === 'execution.respond_to_approval') {
          holds.push({
            before: captured.length,
            answer: {
              kind: command.payload.decision,
              execution_id: command.payload.execution_id,
              approval_id: command.payload.approval_id,
              text: null,
              label: null,
            },
          });
        }
      }
      if (controlPlane.recorder.import(event).status !== 'recorded') {
        throw new RecordedRunError(`Event ${startAt + played.indexOf(event)} is in the run twice.`);
      }
    }
    if (captured.length !== played.length) {
      throw new RecordedRunError('The control plane did not publish every event of the run.');
    }

    const nodes: DemonstrationNode[] = [];
    const cuts = [0, ...holds.map(({ before }) => before), captured.length];
    for (let index = 0; index + 1 < cuts.length; index += 1) {
      const from = cuts[index] as number;
      const to = cuts[index + 1] as number;
      const origin = from === 0 ? 0 : (captured[from - 1]?.at ?? 0);
      const hold = holds[index];
      nodes.push({
        events: captured
          .slice(from, to)
          .map(({ at, message }) => ({ at_ms: at - origin, message })),
        answers:
          hold === undefined ? [] : [{ after: to - from, answer: hold.answer, node: index + 1 }],
        ending:
          hold === undefined
            ? {
                snapshot: { type: 'snapshot', snapshot: controlPlane.snapshot() },
                hold_ms: END_HOLD_MS,
              }
            : { snapshot: null, hold_ms: null },
      });
    }

    const recording: RecordedRun = {
      version: 3,
      kind: 'recorded_run',
      source: RECORDED_RUN_SOURCE,
      welcome,
      snapshot: {
        type: 'snapshot',
        snapshot: { ...beginning, runtimes: [recordedRuntime(holds.length > 0)] },
      },
      nodes,
    };
    return serializeRecordedRun(recording);
  } finally {
    await controlPlane.close();
  }
}

/**
 * The run's runtime as the recording plays it: real, under its neutral name, able to do only what
 * the recording holds an answer for.
 */
function recordedRuntime(answersApprovals: boolean): RuntimeDescriptor {
  return {
    runtime_id: RECORDED_RUNTIME_ID,
    kind: RECORDED_RUNTIME_KIND,
    display_name: RECORDED_RUNTIME_NAME,
    synthetic: false,
    capabilities: {
      start_execution: false,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: answersApprovals,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: true,
    reports_tool_activity: true,
  };
}

/** One event per line, as the demonstration is written, so a change reads like a change to a trace. */
function serializeRecordedRun(recording: RecordedRun): string {
  const json = (value: unknown) => JSON.stringify(value);
  return [
    '{',
    `  "version": ${json(recording.version)},`,
    `  "kind": ${json(recording.kind)},`,
    `  "source": ${json(recording.source)},`,
    `  "welcome": ${json(recording.welcome)},`,
    `  "snapshot": ${json(recording.snapshot)},`,
    '  "nodes": [',
    ...serializeNodes(recording.nodes),
    '  ]',
    '}',
    '',
  ].join('\n');
}
