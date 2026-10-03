import { randomBytes } from 'node:crypto';
import type {
  ClientInfo,
  CommandEnvelope,
  CommandPolicy,
  CommandType,
  EventEnvelope,
  ExecutionId,
  ProjectId,
  QuestionAnswer,
  QuestionView,
  RuntimeDescriptor,
  RuntimeId,
  RuntimeOptions,
  ServerMessage,
  WorkstreamId,
} from '@halcyonic/contracts';
import {
  compileValidator,
  EvaluationResponse,
  REALTIME_PROTOCOL_VERSION,
  UnderstandingResponse,
  UsageLimitsResponse,
} from '@halcyonic/contracts';
import { admitCommand, COMMAND_POLICY } from '@halcyonic/domain';
import { MockRuntimeAdapter, type Scenario } from '@halcyonic/integration-mock';
import {
  createVirtualTime,
  type OptionsValidation,
  type RuntimeAdapter,
  type StartExecutionRequest,
  type StartExecutionResult,
} from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { createCommandFactory, type DemoWorkstream } from '../demo-plan.ts';
import { registerRoutes } from '../http/routes.ts';
import { createHttpServer } from '../http/server.ts';
import { createSeededRandom, createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { silentLogger } from '../logger.ts';
import {
  DEMONSTRATION_STORIES,
  DemonstrationSources,
  type SourceStory,
} from './demonstration-sources.ts';

type WelcomeMessage = Extract<ServerMessage, { type: 'welcome' }>;
type SnapshotMessage = Extract<ServerMessage, { type: 'snapshot' }>;
type EventMessage = Extract<ServerMessage, { type: 'event' }>;

/**
 * An answer a person can give in the workspace. The recording holds a continuation for each one it
 * offers; `approval_id`, `text` and `label` are null where the kind has none. An instruction's
 * `label` is what a button offering it says, and its `text` exactly what was sent. An answer to an
 * agent's question (ADR 0022) is one of the question's options: `label` is the option, and
 * `question_id` and `answers` exactly what was sent.
 */
export type DemonstrationAnswer =
  | {
      readonly kind: 'approve' | 'deny';
      readonly execution_id: ExecutionId;
      readonly approval_id: string;
      readonly text: null;
      readonly label: null;
    }
  | {
      readonly kind: 'interrupt';
      readonly execution_id: ExecutionId;
      readonly approval_id: null;
      readonly text: null;
      readonly label: null;
    }
  | {
      readonly kind: 'instruct';
      readonly execution_id: ExecutionId;
      readonly approval_id: null;
      readonly text: string;
      readonly label: string;
    }
  | {
      readonly kind: 'answer';
      readonly execution_id: ExecutionId;
      readonly approval_id: null;
      readonly question_id: string;
      readonly answers: readonly QuestionAnswer[];
      readonly text: null;
      readonly label: string;
    };

/** An instruction the recording offers: what its button says, and exactly what is sent. */
export interface RecordedInstruction {
  readonly label: string;
  readonly text: string;
}

/**
 * A stretch of the recording between decisions. Its events play at their times from the moment the
 * node begins; after `after` of them, each answer listed there continues with its own node instead.
 * Once the last event has played, the ending's snapshot, if any, is sent, and the final state holds
 * for `hold_ms`, or until an answer when that is null, before the demonstration starts again.
 */
export interface DemonstrationNode {
  readonly events: readonly { readonly at_ms: number; readonly message: EventMessage }[];
  readonly answers: readonly {
    readonly after: number;
    readonly answer: DemonstrationAnswer;
    readonly node: number;
  }[];
  readonly ending: { readonly snapshot: SnapshotMessage | null; readonly hold_ms: number | null };
}

/**
 * One REST answer of the recording's control plane about an execution: the answer it gave once
 * `after` of node `node`'s events had played, at `read_at` by the recording's clock. It holds from
 * there along every path through that point until the next answer recorded for the execution.
 */
export interface RecordedAnswer<T> {
  readonly node: number;
  readonly after: number;
  readonly read_at: string;
  readonly response: T;
}

/** Recorded answers by execution id, each execution's in the order a path reaches them. */
export type RecordedAnswers<T> = Readonly<Record<string, readonly RecordedAnswer<T>[]>>;

/**
 * `GET /api/usage-limits` as the control plane answered it at `read_at`, the recording's own start.
 * A player moves every time in it by as long as has passed since `read_at` when it is read, so a
 * reading is always seen as long ago and resets as far ahead as when it was recorded.
 */
export interface RecordedUsageLimits {
  readonly read_at: string;
  readonly answer: UsageLimitsResponse;
}

/**
 * What the XR client plays when no control plane is configured or reachable: the realtime messages
 * the control plane sent while its scripted operator gave every answer the recording offers, as a
 * tree that shares its beginning. Its journal is a `fixture`, so every surface labels it as
 * recorded, and its runtimes are synthetic, so the work reads as simulated. Beside them it holds the
 * control plane's REST answers about each execution's understanding and evaluation, read through
 * its own routes from stand-ins for the sources and marked synthetic (ADR 0019), and its answer
 * about the practice agent's usage limits, read the same way (ADR 0012). A reader ignores
 * top-level keys it does not know, so a reader of this version without those answers still plays it.
 */
export interface Demonstration {
  readonly version: 2;
  /** The plan it was recorded from, relative to the repository root. */
  readonly source: string;
  readonly welcome: WelcomeMessage;
  /** The state at the beginning, sent at the start and each time the demonstration starts again. */
  readonly snapshot: SnapshotMessage;
  /** The recorded stretches; the first is the beginning, and every other one is some answer's. */
  readonly nodes: readonly DemonstrationNode[];
  /** `GET /api/executions/:execution_id/understanding` as the playback reaches each answer. */
  readonly understanding: RecordedAnswers<UnderstandingResponse>;
  /** `GET /api/executions/:execution_id/evaluation` as the playback reaches each answer. */
  readonly evaluation: RecordedAnswers<EvaluationResponse>;
  /** The practice agent's usage limits, the same along every path. */
  readonly usage_limits: RecordedUsageLimits;
}

const ROOT = new URL('../../../../', import.meta.url);

export const DEMONSTRATION_SOURCE = 'apps/control-plane/src/fixtures/demonstration.ts';

/** Where the Unity project bundles the demonstration, as a text asset under Resources. */
export const DEMONSTRATION_FILE = new URL(
  'apps/xr/Assets/Halcyonic/Resources/HalcyonicDemonstration.json',
  ROOT,
);

/** The scenarios it plays: the committed ones, which the mock runtime of `pnpm dev` also loads. */
export const DEMONSTRATION_SCENARIOS = new URL('fixtures/scenarios/', ROOT);

/** How long the final state holds while the recording offers instructions, before it starts again. */
export const AT_REST_HOLD_MS = 60_000;

/** How long the final state of a finished recording holds before it starts again. */
export const END_HOLD_MS = 20_000;

const START = new Date('2026-09-29T09:00:00.000Z');

/** Seeds the identifiers, so the same plan and scenarios always record the same demonstration. */
const SEED = 12;

/**
 * The runtime of the work a person directs. The recorder names it for the demonstration; it is the
 * mock runtime, synthetic like every mock, so every surface still labels its work as simulated.
 */
export const DIRECTED_RUNTIME_ID = 'demonstration' as RuntimeId;
export const DIRECTED_RUNTIME_NAME = 'Practice agent';

/** The runtime of the work shown beside it, which declares nothing to direct. */
export const WATCHED_RUNTIME_ID = 'demonstration-watch-only' as RuntimeId;
export const WATCHED_RUNTIME_NAME = 'Practice agent, watch only';

/** How the recording's scripted operator introduces itself in every command it sends. */
export const DEMONSTRATION_CLIENT: ClientInfo = {
  name: 'demonstration recorder',
  version: null,
  device_label: null,
};

/**
 * A workstream of the demonstration. Watched work runs on the watch-only runtime, from this long
 * after the beginning; the one directed workstream runs on the runtime a person directs, from this
 * long after all watched work has finished, so nothing that happens beside it multiplies its
 * branches.
 */
export type PlannedWorkstream = DemoWorkstream &
  (
    | { readonly role: 'watched'; readonly startAfterMs: number }
    | { readonly role: 'directed'; readonly startAfterWatchedMs: number }
  );

export interface DemonstrationPlan {
  readonly projectName: string;
  /** In the order they are created. */
  readonly workstreams: readonly PlannedWorkstream[];
  /**
   * The instructions offered once the directed work's first turn has ended, by how its approval was
   * answered. Each one has a turn scripted in the scenario. Nothing is offered after an interrupt or
   * a second turn: those end the recording.
   */
  readonly instructions: {
    readonly approve: readonly RecordedInstruction[];
    readonly deny: readonly RecordedInstruction[];
  };
}

/**
 * A short feature on a small web service, for the first minutes with the app: two pieces of work
 * finish on their own, then the third needs a decision, and its tests show a gap that an
 * instruction closes. Titles, instructions and agent text name no product, company or person.
 */
export const DEMONSTRATION_PLAN: DemonstrationPlan = {
  projectName: 'Storefront API',
  workstreams: [
    {
      role: 'watched',
      startAfterMs: 300,
      title: 'Paginate the order history endpoint',
      objective: 'GET /orders returns 50 orders at a time, with a cursor for the next page.',
      instruction: 'Add cursor pagination to GET /orders, 50 orders per page, with tests.',
      scenario: 'order_history_pagination',
    },
    {
      role: 'directed',
      startAfterWatchedMs: 400,
      title: 'Add rate limiting to the sign-in endpoint',
      objective:
        'Repeated failed sign-ins are refused for a while, and the limit survives a restart.',
      instruction:
        'Limit repeated failed attempts on POST /sign-in, keep the attempts in the database, and cover it with tests.',
      scenario: 'sign_in_rate_limit',
    },
    {
      role: 'watched',
      startAfterMs: 600,
      title: 'Send an order confirmation email',
      objective: 'Customers get an email with their order summary once checkout succeeds.',
      instruction: 'Queue an order confirmation email after a successful checkout, with tests.',
      scenario: 'order_confirmation_email',
    },
  ],
  instructions: {
    approve: [
      {
        label: 'Count per account too',
        text: 'Count failed attempts per account as well as per address.',
      },
      {
        label: 'Change the test instead',
        text: 'Change the lockout test to expect limits per address only.',
      },
    ],
    deny: [
      {
        label: 'Keep them in memory',
        text: 'Keep failed attempts in memory until the migration is approved.',
      },
    ],
  },
};

/**
 * Records the demonstration: runs the real control plane with the mock runtime under virtual time
 * with seeded identifiers, once for the beginning and again for every answer the recording offers,
 * each time from the start with the answers taken so far. Every run of a path shares the text of
 * its beginning, so the tree's beginning is identical and every state in it was computed by the
 * control plane's own journal, projection and publisher. The same plan and scenarios always
 * produce the same text.
 *
 * Wherever a workspace would offer an action, the recording holds that action's continuation, and
 * nowhere else: an instruction is offered only where the plan has recorded ones, and a path that
 * would otherwise leave work open to an action it has no answer for ends there, with its control
 * plane started again without runtimes, as `pnpm replay` serves a journal.
 */
export async function recordDemonstration(
  scenarios: ReadonlyMap<string, Scenario>,
  plan: DemonstrationPlan = DEMONSTRATION_PLAN,
): Promise<string> {
  checkPlan(scenarios, plan);
  const nodes: DemonstrationNode[] = [];
  const beginning = await runPath(scenarios, plan, []);

  const build = async (path: PathRun, decisions: readonly Decision[], from: number) => {
    const index = nodes.length;
    nodes.push(PLACEHOLDER);
    const start = from === 0 ? 0 : (path.events[from - 1]?.at ?? 0);
    const answers: DemonstrationNode['answers'][number][] = [];
    for (let played = from + 1; played <= path.events.length; played += 1) {
      if (!endsInstant(path.events, played)) continue;
      const last = played === path.events.length;
      const offered = last ? path.ending.answers : answersWithin(path, played);
      for (const answer of offered) {
        const decision: Decision = { after: played, at: path.events[played - 1]?.at ?? 0, answer };
        const branch = await runPath(scenarios, plan, [...decisions, decision]);
        assertSharedBeginning(path, branch, played);
        answers.push({
          after: played - from,
          answer,
          node: await build(branch, [...decisions, decision], played),
        });
      }
    }
    nodes[index] = {
      events: path.events.slice(from).map(({ at, message }) => ({ at_ms: at - start, message })),
      answers,
      ending: { snapshot: path.ending.snapshot, hold_ms: path.ending.hold_ms },
    };
    return index;
  };

  await build(beginning, [], 0);
  const prologue = (await demonstrationPrologue(scenarios, plan)).map(({ event }) => event);
  const answers = await recordAnswers(prologue, nodes, storiesOf(prologue, plan));
  const usageLimits = await recordUsageLimits(prologue, beginning.welcome.server_time);
  return serializeDemonstration({
    version: 2,
    source: DEMONSTRATION_SOURCE,
    welcome: beginning.welcome,
    snapshot: beginning.snapshot,
    nodes,
    understanding: answers.understanding,
    evaluation: answers.evaluation,
    usage_limits: usageLimits,
  });
}

const validateUnderstanding = compileValidator(UnderstandingResponse);
const validateEvaluation = compileValidator(EvaluationResponse);

/** What the stand-ins know of each workstream's scenario, by the workstream's id. */
function storiesOf(
  prologue: readonly EventEnvelope[],
  plan: DemonstrationPlan,
): ReadonlyMap<string, SourceStory> {
  const stories = new Map<string, SourceStory>();
  for (const event of prologue) {
    if (event.event_type !== 'workstream.created') continue;
    const planned = plan.workstreams.find(({ title }) => title === event.payload.title);
    const story = planned === undefined ? undefined : DEMONSTRATION_STORIES[planned.scenario];
    if (story === undefined) {
      throw new Error(`the stand-in sources know no story for ${event.payload.title}`);
    }
    stories.set(event.workstream_id, story);
  }
  return stories;
}

/**
 * Records the control plane's REST answers about every execution along every path of the tree:
 * for each node, a control plane without runtimes is given the journal up to the node's start
 * through its normal write path, as `pnpm replay` gives one a trace, and serves its own routes over
 * loopback HTTP, reading through to the stand-in sources. After each instant, every execution the
 * instant changed is asked about, once the stand-ins have observed its events so far. An answer is
 * kept only where it differs from the one that held before on that path, so the recording holds
 * exactly what the control plane would answer at every point of the playback.
 */
async function recordAnswers(
  prologue: readonly EventEnvelope[],
  nodes: readonly DemonstrationNode[],
  stories: ReadonlyMap<string, SourceStory>,
): Promise<{
  understanding: Record<string, RecordedAnswer<UnderstandingResponse>[]>;
  evaluation: Record<string, RecordedAnswer<EvaluationResponse>[]>;
}> {
  const understanding: Record<string, RecordedAnswer<UnderstandingResponse>[]> = {};
  const evaluation: Record<string, RecordedAnswer<EvaluationResponse>[]> = {};
  const sources = await DemonstrationSources.start();
  // The recording's own server and credential; neither reaches the recording.
  const token = randomBytes(32).toString('base64url');
  const visit = async (
    index: number,
    before: readonly EventEnvelope[],
    held: ReadonlyMap<string, string>,
  ): Promise<void> => {
    const node = nodes[index];
    if (node === undefined) throw new Error(`no node ${index}`);
    const replay = replayOf([...prologue, ...before]);
    const app = await createHttpServer({ logLevel: 'silent', token });
    registerRoutes(app, replay, sources.sources);
    const heldAfter = new Map<number, ReadonlyMap<string, string>>();
    try {
      await app.listen({ host: '127.0.0.1', port: 0 });
      const address = app.server.address();
      if (address === null || typeof address === 'string') throw new Error('no port');
      const now = new Map(held);
      let played = 0;
      while (played < node.events.length) {
        const at = node.events[played]?.at_ms;
        const changed: string[] = [];
        let readAt = '';
        for (;;) {
          const next = node.events[played];
          if (next === undefined || next.at_ms !== at) break;
          replay.recorder.import(next.message.event);
          for (const { execution_id } of next.message.changes.executions) {
            if (!changed.includes(execution_id)) changed.push(execution_id);
          }
          readAt = next.message.event.ingested_at;
          played += 1;
        }
        for (const executionId of changed) {
          const execution = replay.projection.execution(executionId);
          const story = execution && stories.get(execution.workstream_id);
          if (execution === undefined || story === undefined) {
            throw new Error(`no story for execution ${executionId}`);
          }
          const events = [...replay.journal.readAll()]
            .map(({ event }) => event)
            .filter((event) => event.execution_id === executionId);
          sources.observe(execution, story, events);
          const ask = (kind: 'understanding' | 'evaluation') =>
            fetch(`http://127.0.0.1:${address.port}/api/executions/${executionId}/${kind}`, {
              headers: { authorization: `Bearer ${token}` },
            });
          const read = await answerOf(await ask('understanding'), validateUnderstanding);
          keep(understanding, now, 'understanding', executionId, read, index, played, readAt);
          const measured = await answerOf(await ask('evaluation'), validateEvaluation);
          keep(evaluation, now, 'evaluation', executionId, measured, index, played, readAt);
        }
        heldAfter.set(played, new Map(now));
      }
    } finally {
      await app.close();
      await replay.close();
    }
    for (const { after, node: child } of node.answers) {
      const played = node.events.slice(0, after).map(({ message }) => message.event);
      await visit(child, [...before, ...played], heldAfter.get(after) ?? held);
    }
  };
  try {
    await visit(0, [], new Map());
  } finally {
    await sources.close();
  }
  return { understanding, evaluation };
}

const validateUsageLimits = compileValidator(UsageLimitsResponse);

/**
 * Records the control plane's answer about usage limits, read through its own route from the
 * stand-in for Seorak serving the practice agent's limits, and moves its times so the read happened
 * at `readAt`; the recording then holds the same file however late it is made.
 */
async function recordUsageLimits(
  prologue: readonly EventEnvelope[],
  readAt: string,
): Promise<RecordedUsageLimits> {
  const sources = await DemonstrationSources.start();
  const replay = replayOf(prologue);
  const token = randomBytes(32).toString('base64url');
  const app = await createHttpServer({ logLevel: 'silent', token });
  registerRoutes(app, replay, sources.sources);
  try {
    const now = Date.now();
    sources.serveUsage(now, DIRECTED_RUNTIME_NAME);
    await app.listen({ host: '127.0.0.1', port: 0 });
    const address = app.server.address();
    if (address === null || typeof address === 'string') throw new Error('no port');
    const response = await fetch(`http://127.0.0.1:${address.port}/api/usage-limits`, {
      headers: { authorization: `Bearer ${token}` },
    });
    const body: unknown = await response.json();
    if (response.status !== 200 || !validateUsageLimits(body).ok) {
      throw new Error(`the control plane answered ${response.status}: ${JSON.stringify(body)}`);
    }
    const answer = body as UsageLimitsResponse;
    if (answer.availability !== 'available' || !answer.source.synthetic) {
      throw new Error(`the stand-in for Seorak answered usage limits ${JSON.stringify(answer)}`);
    }
    const shift = Date.parse(readAt) - now;
    const moved = (at: string) => new Date(Date.parse(at) + shift).toISOString();
    return {
      read_at: readAt,
      answer: {
        ...answer,
        readings: answer.readings.map((reading) => ({
          ...reading,
          observed_at: moved(reading.observed_at),
          resets_at: moved(reading.resets_at),
        })) as typeof answer.readings,
      },
    };
  } finally {
    await app.close();
    await replay.close();
    await sources.close();
  }
}

/** A control plane without runtimes that holds this journal, written through its recorder. */
function replayOf(events: readonly EventEnvelope[]): ControlPlane {
  const time = createVirtualTime(START);
  const ids = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(SEED),
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
  for (const event of events) controlPlane.recorder.import(event);
  return controlPlane;
}

/**
 * The route's answer, which must be one the demonstration may show: the stand-in's, marked
 * synthetic, or the control plane's own word that the runtime has not named its session yet. A
 * stand-in's refusal would name a product without saying it is simulated, so none is recorded.
 */
async function answerOf<T extends UnderstandingResponse | EvaluationResponse>(
  response: Response,
  validate: (value: unknown) => { ok: boolean },
): Promise<T> {
  const body: unknown = await response.json();
  if (response.status !== 200 || !validate(body).ok) {
    throw new Error(`the control plane answered ${response.status}: ${JSON.stringify(body)}`);
  }
  const { result } = body as T;
  if (result.availability === 'available') {
    const source =
      'understanding' in result ? result.understanding.source : result.evaluation.source;
    if (!source.synthetic) throw new Error('a stand-in answered without being marked synthetic');
  } else if (result.availability !== 'not_found' || result.reason.code !== 'native_id_unknown') {
    throw new Error(
      `a stand-in answered ${result.availability} (${result.reason.code}): ${result.reason.message}`,
    );
  }
  return body as T;
}

/** Keeps an answer where it differs from the one that held for its execution before it. */
function keep<T>(
  answers: Record<string, RecordedAnswer<T>[]>,
  held: Map<string, string>,
  kind: string,
  executionId: string,
  response: T,
  node: number,
  after: number,
  readAt: string,
): void {
  const key = `${kind} ${executionId}`;
  const text = JSON.stringify(response);
  if (held.get(key) === text) return;
  held.set(key, text);
  answers[executionId] ??= [];
  answers[executionId].push({ node, after, read_at: readAt, response });
}

const PLACEHOLDER: DemonstrationNode = {
  events: [],
  answers: [],
  ending: { snapshot: null, hold_ms: null },
};

/** An answer taken on one path: after this many events since the beginning, at this time. */
interface Decision {
  readonly after: number;
  readonly at: number;
  readonly answer: DemonstrationAnswer;
}

/**
 * An action a workspace would offer, as the control plane admits it; instructions carry no text
 * yet. An answer to a question carries the option it gives.
 */
interface Admissible {
  readonly kind: DemonstrationAnswer['kind'];
  readonly execution_id: ExecutionId;
  readonly approval_id: string | null;
  readonly option?: {
    readonly question_id: string;
    readonly label: string;
    readonly answers: readonly QuestionAnswer[];
  };
}

/** One run of the control plane along a path of answers. */
interface PathRun {
  readonly welcome: WelcomeMessage;
  readonly snapshot: SnapshotMessage;
  /** Every event message after the beginning, with its time since the beginning. */
  readonly events: readonly { readonly at: number; readonly message: EventMessage }[];
  /** What a workspace would offer once each event has been applied. */
  readonly admissible: readonly (readonly Admissible[])[];
  readonly ending: {
    readonly snapshot: SnapshotMessage | null;
    readonly hold_ms: number | null;
    readonly answers: readonly DemonstrationAnswer[];
  };
}

/**
 * The journal before the beginning: the events that created the project and its workstreams, which
 * the recording carries only as the beginning's snapshot.
 */
export async function demonstrationPrologue(
  scenarios: ReadonlyMap<string, Scenario>,
  plan: DemonstrationPlan = DEMONSTRATION_PLAN,
): Promise<{ position: number; event: EventEnvelope }[]> {
  const { journal, controlPlane } = begin(scenarios, plan);
  try {
    return [...journal.readAll()].map(({ position, event }) => ({ position, event }));
  } finally {
    await controlPlane.close();
  }
}

/**
 * The control plane at the beginning of every path: under virtual time with seeded identifiers,
 * with both runtimes, the project and its workstreams created and nothing started, so a client
 * that plays the demonstration again keeps its characters.
 */
function begin(scenarios: ReadonlyMap<string, Scenario>, plan: DemonstrationPlan) {
  const time = createVirtualTime(START);
  const ids = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(SEED),
  });
  const commands = createCommandFactory(
    createUuidV7Generator({
      now: () => time.now().getTime(),
      random: createSeededRandom(SEED + 1),
    }),
    time,
    DEMONSTRATION_CLIENT,
  );
  const journal = openSqliteJournal({ path: ':memory:', originIfNew: 'fixture', ids });
  const mock = (runtimeId: RuntimeId, displayName: string) =>
    new MockRuntimeAdapter({ scenarios, runtimeId, displayName, clock: time, scheduler: time });
  const controlPlane = new ControlPlane({
    journal,
    adapters: [
      mock(DIRECTED_RUNTIME_ID, DIRECTED_RUNTIME_NAME),
      new WatchOnlyRuntime(mock(WATCHED_RUNTIME_ID, WATCHED_RUNTIME_NAME)),
    ],
    ids,
    clock: time,
    scheduler: time,
    logger: silentLogger,
    commandTimeoutMs: 30_000,
  });
  const submit = (command: CommandEnvelope) => {
    const outcome = controlPlane.commands.submit(command, 'internal');
    if (outcome.disposition !== 'accepted') {
      throw new Error(`the demonstration's ${command.command_type} was ${outcome.disposition}`);
    }
    return outcome.command;
  };
  const project = submit(commands.createProject(plan.projectName)).result;
  if (project?.kind !== 'project_created') throw new Error('the project was not created');
  const created = plan.workstreams.map((workstream) => {
    const result = submit(
      commands.createWorkstream(project.project_id as ProjectId, workstream),
    ).result;
    if (result?.kind !== 'workstream_created') throw new Error(`${workstream.title} is missing`);
    return { workstream, id: result.workstream_id };
  });
  return { time, ids, commands, journal, controlPlane, submit, created };
}

async function runPath(
  scenarios: ReadonlyMap<string, Scenario>,
  plan: DemonstrationPlan,
  decisions: readonly Decision[],
): Promise<PathRun> {
  const { time, ids, commands, journal, controlPlane, submit, created } = begin(scenarios, plan);
  let detached: ControlPlane | null = null;
  try {
    const directed = created.find(({ workstream }) => workstream.role === 'directed');
    if (directed === undefined || directed.workstream.role !== 'directed') {
      throw new Error('the plan directs no workstream');
    }
    const directedId = directed.id;
    const directedDelay = directed.workstream.startAfterWatchedMs;
    const watchedIds = new Set<WorkstreamId>(
      created.filter(({ workstream }) => workstream.role === 'watched').map(({ id }) => id),
    );

    const snapshot = controlPlane.snapshot();
    const beginning = time.now().getTime();
    const welcome: WelcomeMessage = {
      type: 'welcome',
      protocol: REALTIME_PROTOCOL_VERSION,
      journal: controlPlane.journal.info,
      head: snapshot.position,
      resumed: false,
      server_time: time.now().toISOString(),
      command_policies: commandPolicies(),
    };

    const events: { at: number; message: EventMessage }[] = [];
    const admissible: Admissible[][] = [];
    const watchedFinished = new Set<WorkstreamId>();
    controlPlane.publisher.subscribe(({ position, event, changes }) => {
      events.push({
        at: time.now().getTime() - beginning,
        message: { type: 'event', position, event, changes },
      });
      admissible.push(admissibleNow(controlPlane));
      if (event.event_type !== 'runtime.turn.completed' || !watchedIds.has(event.workstream_id)) {
        return;
      }
      watchedFinished.add(event.workstream_id);
      if (watchedFinished.size === watchedIds.size) {
        void time.sleep(directedDelay).then(() => {
          submit(commands.startExecution(directedId, directed.workstream, DIRECTED_RUNTIME_ID));
        });
      }
    });
    for (const { workstream, id } of created) {
      if (workstream.role !== 'watched') continue;
      void time.sleep(workstream.startAfterMs).then(() => {
        submit(commands.startExecution(id, workstream, WATCHED_RUNTIME_ID));
      });
    }

    for (const decision of decisions) {
      await time.advance(beginning + decision.at - time.now().getTime());
      if (events.length !== decision.after) {
        throw new Error(`a path diverged before its answer at event ${decision.after}`);
      }
      submit(answerCommand(commands, decision.answer));
    }
    await time.runUntilIdle();

    const final = admissible.at(-1) ?? [];
    const directedExecution = controlPlane.projection
      .executions()
      .find((execution) => execution.workstream_id === directedId);
    const instructions = offeredInstructions(plan, decisions);
    const recorded = {
      welcome,
      snapshot: { type: 'snapshot', snapshot } as const,
      events,
      admissible,
    };

    if (
      (directedExecution?.pending_approvals.length ?? 0) > 0 ||
      (directedExecution?.pending_questions.length ?? 0) > 0
    ) {
      // A person is needed: the recording holds until one answers.
      return {
        ...recorded,
        ending: { snapshot: null, hold_ms: null, answers: final.map(toAnswer) },
      };
    }
    if (
      directedExecution !== undefined &&
      instructions.length > 0 &&
      final.length === 1 &&
      final[0]?.kind === 'instruct'
    ) {
      // The turn has ended: the recording offers its instructions for a while.
      return {
        ...recorded,
        ending: {
          snapshot: null,
          hold_ms: AT_REST_HOLD_MS,
          answers: instructions.map(({ text, label }) => ({
            kind: 'instruct',
            execution_id: directedExecution.execution_id,
            approval_id: null,
            text,
            label,
          })),
        },
      };
    }

    // The recording has nothing more: its control plane starts again without runtimes, as a
    // replay serves a journal, so the final state holds with no action left to offer.
    await controlPlane.registry.closeAll(silentLogger);
    detached = new ControlPlane({
      journal,
      adapters: [],
      ids,
      clock: time,
      scheduler: time,
      logger: silentLogger,
      commandTimeoutMs: 30_000,
    });
    if (admissibleNow(detached).length > 0) throw new Error('a finished recording offers actions');
    return {
      ...recorded,
      ending: {
        snapshot: { type: 'snapshot', snapshot: detached.snapshot() },
        hold_ms: END_HOLD_MS,
        answers: [],
      },
    };
  } finally {
    if (detached === null) await controlPlane.close();
    else await detached.close();
  }
}

/**
 * What a workspace would offer now, asked of the control plane's own admission for every execution:
 * answering each pending approval, each option of each pending question, stopping the turn, and
 * instructing.
 */
function admissibleNow(controlPlane: ControlPlane): Admissible[] {
  const probe = (command: CommandEnvelope) =>
    admitCommand(command, controlPlane.projection, controlPlane.registry).admitted;
  const base = {
    schema_version: 1 as const,
    command_id: '00000000-0000-4000-8000-000000000000',
    issued_at: controlPlane.clock.now().toISOString(),
    client: DEMONSTRATION_CLIENT,
  };
  const found: Admissible[] = [];
  for (const execution of controlPlane.projection.executions()) {
    const execution_id = execution.execution_id;
    for (const approval of execution.pending_approvals) {
      for (const decision of ['approve', 'deny'] as const) {
        const answer = {
          ...base,
          command_type: 'execution.respond_to_approval' as const,
          payload: { execution_id, approval_id: approval.approval_id, decision, message: null },
        } as CommandEnvelope;
        if (probe(answer)) {
          found.push({ kind: decision, execution_id, approval_id: approval.approval_id });
        }
      }
    }
    for (const question of execution.pending_questions) {
      for (const option of questionOptions(question)) {
        const answer = {
          ...base,
          command_type: 'execution.answer_question' as const,
          payload: {
            execution_id,
            question_id: question.question_id,
            answers: [...option.answers],
          },
        } as CommandEnvelope;
        if (probe(answer)) found.push({ kind: 'answer', execution_id, approval_id: null, option });
      }
    }
    const interrupt = {
      ...base,
      command_type: 'execution.interrupt' as const,
      payload: { execution_id },
    } as CommandEnvelope;
    if (probe(interrupt)) found.push({ kind: 'interrupt', execution_id, approval_id: null });
    const instruct = {
      ...base,
      command_type: 'execution.send_instruction' as const,
      payload: { execution_id, text: 'Any instruction.' },
    } as CommandEnvelope;
    if (probe(instruct)) found.push({ kind: 'instruct', execution_id, approval_id: null });
  }
  return found;
}

/** The answers within a path, where something else happens next: every admissible action but instructing. */
function answersWithin(path: PathRun, played: number): DemonstrationAnswer[] {
  const admissible = path.admissible[played - 1] ?? [];
  if (admissible.some((entry) => entry.kind === 'instruct')) {
    throw new Error(
      `event ${played} leaves work open to instructions while more follows; the plan offers instructions only where a path ends`,
    );
  }
  return admissible.map(toAnswer);
}

/**
 * The answers a recording offers to a question: each option of its one prompt, chosen alone. The
 * plan's questions have one prompt with one choice and nothing to type (`checkPlan`), so a person
 * can only press one of them.
 */
function questionOptions(
  question: QuestionView,
): { question_id: string; label: string; answers: QuestionAnswer[] }[] {
  const prompt = question.prompts[0];
  if (!question.answerable || question.prompts.length !== 1 || prompt === undefined) return [];
  return prompt.options.map(({ label }) => ({
    question_id: question.question_id,
    label,
    answers: [{ key: prompt.key, selected: [label], text: null }],
  }));
}

function toAnswer(entry: Admissible): DemonstrationAnswer {
  switch (entry.kind) {
    case 'approve':
    case 'deny':
      if (entry.approval_id === null) throw new Error('an approval answer without an approval');
      return {
        kind: entry.kind,
        execution_id: entry.execution_id,
        approval_id: entry.approval_id,
        text: null,
        label: null,
      };
    case 'interrupt':
      return {
        kind: 'interrupt',
        execution_id: entry.execution_id,
        approval_id: null,
        text: null,
        label: null,
      };
    case 'instruct':
      throw new Error('an instruction needs its recorded text');
    case 'answer':
      if (entry.option === undefined) throw new Error('an answer without its option');
      return {
        kind: 'answer',
        execution_id: entry.execution_id,
        approval_id: null,
        question_id: entry.option.question_id,
        answers: entry.option.answers,
        text: null,
        label: entry.option.label,
      };
    default: {
      const unhandled: never = entry.kind;
      throw new Error(`unhandled answer ${String(unhandled)}`);
    }
  }
}

/** The recorded instructions on a path whose directed work is at rest: after its first turn only. */
function offeredInstructions(
  plan: DemonstrationPlan,
  decisions: readonly Decision[],
): RecordedInstruction[] {
  if (decisions.some(({ answer }) => answer.kind === 'interrupt' || answer.kind === 'instruct')) {
    return [];
  }
  const answered = decisions.find(
    ({ answer }) => answer.kind === 'approve' || answer.kind === 'deny',
  )?.answer.kind;
  if (answered === 'approve') return [...plan.instructions.approve];
  if (answered === 'deny') return [...plan.instructions.deny];
  return [];
}

function answerCommand(
  commands: ReturnType<typeof createCommandFactory>,
  answer: DemonstrationAnswer,
): CommandEnvelope {
  switch (answer.kind) {
    case 'approve':
      return commands.approve(answer.execution_id, answer.approval_id);
    case 'deny':
      return commands.deny(answer.execution_id, answer.approval_id, null);
    case 'interrupt':
      return commands.interrupt(answer.execution_id);
    case 'instruct':
      return commands.instruct(answer.execution_id, answer.text);
    case 'answer':
      return commands.answerQuestion(answer.execution_id, answer.question_id, answer.answers);
    default: {
      const unhandled: never = answer;
      throw new Error(`unhandled answer ${JSON.stringify(unhandled)}`);
    }
  }
}

/** A later instant starts after `played` events, or the path ends there. */
function endsInstant(events: PathRun['events'], played: number): boolean {
  const next = events[played];
  const last = events[played - 1];
  return next === undefined || last === undefined || next.at > last.at;
}

/** Every path that branches from another repeats its beginning exactly, identifiers included. */
function assertSharedBeginning(path: PathRun, branch: PathRun, played: number): void {
  const same =
    JSON.stringify([path.welcome, path.snapshot, path.events.slice(0, played)]) ===
    JSON.stringify([branch.welcome, branch.snapshot, branch.events.slice(0, played)]);
  if (!same) throw new Error(`a branch after event ${played} does not share its beginning`);
}

/**
 * One workstream is directed and every scenario exists; every recorded instruction must be one the
 * directed scenario scripts, or the mock would play its default turn instead; and every question it
 * asks has one prompt, one choice among its options and nothing to type or keep secret, so the
 * recording holds a continuation for everything a person can answer.
 */
function checkPlan(scenarios: ReadonlyMap<string, Scenario>, plan: DemonstrationPlan): void {
  const directed = plan.workstreams.filter(({ role }) => role === 'directed');
  if (directed.length !== 1) throw new Error('a demonstration directs exactly one workstream');
  for (const { scenario } of plan.workstreams) {
    if (!scenarios.has(scenario)) throw new Error(`no scenario ${scenario}`);
  }
  const scenario = scenarios.get(directed[0]?.scenario ?? '');
  const steps = [
    ...(scenario?.steps ?? []),
    ...(scenario?.instructions ?? []).flatMap((instruction) => instruction.steps),
  ];
  for (const step of steps) {
    if (!('await_answer' in step)) continue;
    const prompts = step.await_answer.prompts;
    const prompt = prompts[0];
    if (
      prompts.length !== 1 ||
      prompt === undefined ||
      prompt.multiple ||
      prompt.free_text ||
      prompt.secret ||
      prompt.options.length < 2 ||
      !step.await_answer.answerable
    ) {
      throw new Error(
        `${scenario?.id} asks a question the recording cannot answer for every choice: one prompt, one of at least two options, nothing typed`,
      );
    }
  }
  const scripted = new Set((scenario?.instructions ?? []).map(({ text }) => text));
  for (const { text } of [...plan.instructions.approve, ...plan.instructions.deny]) {
    if (!scripted.has(text)) {
      throw new Error(`${scenario?.id} scripts no turn for an instruction the plan offers`);
    }
  }
}

/** The command policies every realtime client is welcomed with (http/realtime.ts). */
function commandPolicies(): CommandPolicy[] {
  return Object.entries(COMMAND_POLICY).map(([commandType, policy]) => ({
    command_type: commandType as CommandType,
    policy,
  }));
}

/**
 * A simulated runtime that only starts work: it declares no other capability and implements none,
 * so nothing it runs can be directed and no client offers an action on it. It plays the mock
 * runtime's scenarios.
 */
class WatchOnlyRuntime implements RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor;
  readonly #mock: MockRuntimeAdapter;

  constructor(mock: MockRuntimeAdapter) {
    this.#mock = mock;
    this.descriptor = {
      ...mock.descriptor,
      capabilities: {
        start_execution: true,
        instruct_at_rest: false,
        instruct_while_running: false,
        respond_to_approval: false,
        answer_question: false,
        interrupt: false,
      },
    };
  }

  validateStartOptions(options: RuntimeOptions, modelRef: string | null): OptionsValidation {
    return this.#mock.validateStartOptions(options, modelRef);
  }

  startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    return this.#mock.startExecution(request);
  }

  close(): Promise<void> {
    return this.#mock.close();
  }
}

/** JSON with one event or answer per line, so a change to the recording reads like a change to a trace. */
function serializeDemonstration(demonstration: Demonstration): string {
  const json = (value: unknown) => JSON.stringify(value);
  const list = (name: string, items: readonly unknown[], last: boolean) => {
    const end = last ? '' : ',';
    if (items.length === 0) return [`      "${name}": []${end}`];
    return [
      `      "${name}": [`,
      items.map((item) => `        ${json(item)}`).join(',\n'),
      `      ]${end}`,
    ];
  };
  const nodes = demonstration.nodes.map((node, index) =>
    [
      '    {',
      ...list('events', node.events, false),
      ...list('answers', node.answers, false),
      `      "ending": ${json(node.ending)}`,
      index < demonstration.nodes.length - 1 ? '    },' : '    }',
    ].join('\n'),
  );
  const byExecution = (name: string, answers: RecordedAnswers<unknown>, last: boolean) => {
    const entries = Object.entries(answers);
    const end = last ? '' : ',';
    if (entries.length === 0) return [`  "${name}": {}${end}`];
    return [
      `  "${name}": {`,
      ...entries.map(([executionId, items], index) =>
        [
          `    ${json(executionId)}: [`,
          items.map((item) => `      ${json(item)}`).join(',\n'),
          index < entries.length - 1 ? '    ],' : '    ]',
        ].join('\n'),
      ),
      `  }${end}`,
    ];
  };
  return [
    '{',
    `  "version": ${json(demonstration.version)},`,
    `  "source": ${json(demonstration.source)},`,
    `  "welcome": ${json(demonstration.welcome)},`,
    `  "snapshot": ${json(demonstration.snapshot)},`,
    '  "nodes": [',
    ...nodes,
    '  ],',
    ...byExecution('understanding', demonstration.understanding, false),
    ...byExecution('evaluation', demonstration.evaluation, false),
    `  "usage_limits": ${json(demonstration.usage_limits)}`,
    '}',
    '',
  ].join('\n');
}
