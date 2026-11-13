import assert from 'node:assert/strict';
import { request } from 'node:http';
import { test } from 'node:test';
import type {
  ProjectView,
  RuntimeDescriptor,
  RunUsage,
  SpendingCapView,
  WorkstreamView,
} from '@halcyonic/contracts';
import { Projection } from '@halcyonic/domain';
import { EventBuilder } from '../../../../packages/domain/src/testing/events.ts';
import { startTestServer } from '../testing/harness.ts';
import {
  mountUsage,
  projectionSources,
  USAGE_WORDS,
  type UsageSources,
  usageState,
} from './usage-page.ts';

const runtime = (runtime_id: string, display_name: string, reports: boolean) =>
  ({
    runtime_id,
    display_name,
    ...(reports ? { reports_usage: true } : {}),
  }) as unknown as RuntimeDescriptor;

const project = (project_id: string, name: string, usage?: RunUsage) =>
  ({ project_id, name, ...(usage === undefined ? {} : { usage }) }) as unknown as ProjectView;

const task = (workstream_id: string, project_id: string, title: string, updated_at: string) =>
  ({ workstream_id, project_id, title, updated_at }) as unknown as WorkstreamView;

const used = (over: Partial<RunUsage> = {}): RunUsage => ({
  turns: 1,
  turns_ended: 1,
  tokens: { input: 30, cache_read: 3000, cache_write: 0, output: 40, reasoning: 10 },
  cost: { estimated_usd: 0.25, prices: 'list' },
  ...over,
});

function sources(options: {
  projects: ProjectView[];
  tasks: WorkstreamView[];
  usage?: Record<string, RunUsage>;
  latest?: Record<string, string>;
  limits?: Record<string, SpendingCapView>;
  hosted?: boolean;
}): UsageSources {
  return {
    projects: () => options.projects,
    tasks: (id) => options.tasks.filter((each) => each.project_id === id),
    taskUsage: (id) => options.usage?.[id] ?? null,
    latest: (each) => ({
      runtimeId: options.latest?.[each.workstream_id] ?? null,
      limit: options.limits?.[each.workstream_id] ?? null,
    }),
    runtimes: () => [runtime('agent-a', 'Agent A', true), runtime('agent-b', 'Agent B', false)],
    assistantHosted: () => options.hosted === true,
  };
}

test("a reported task shows its tokens without adding thinking twice, and the app's estimate with whose prices", () => {
  const state = usageState(
    sources({
      projects: [project('p1', 'Garden', used())],
      tasks: [task('w1', 'p1', 'Add a pond', '2026-11-13T10:00:00Z')],
      usage: { w1: used() },
      latest: { w1: 'agent-a' },
    }),
  );
  const row = state.projects[0]?.tasks[0];
  assert.equal(row?.app, 'Agent A');
  assert.deepEqual(row?.tokens, { value: '3,070', notes: [], known: true });
  assert.deepEqual(
    row?.cost,
    { value: '$0.25', notes: [], known: true },
    'whose prices is not on every row',
  );
  assert.deepEqual(state.projects[0]?.cost.notes, [USAGE_WORDS.prices.list]);
  assert.equal(state.projects[0]?.estimates, null, "said under the project's cost, not twice");
  assert.equal(row?.limit, null);
  assert.equal(state.projects[0]?.limits, false, 'no task has a limit, so no Limit column');
  assert.deepEqual(
    state.projects[0]?.breakdown?.map((part) => part.value),
    ['30', '3,000', '0', '40', '10'],
  );
  assert.equal(state.assistant, null, 'the assistant on this computer owes no line');
});

test('unknown stays unknown: not reported, not counted, no estimate, never zero', () => {
  const state = usageState(
    sources({
      projects: [project('p1', 'Garden', used({ cost: null }))],
      tasks: [
        task('w1', 'p1', 'Reports, none yet', '2026-11-13T10:03:00Z'),
        task('w2', 'p1', 'App counts none', '2026-11-13T10:02:00Z'),
        task('w3', 'p1', 'No estimate', '2026-11-13T10:01:00Z'),
        task('w4', 'p1', 'Never ran', '2026-11-13T10:00:00Z'),
      ],
      usage: { w3: used({ cost: null }) },
      latest: { w1: 'agent-a', w2: 'agent-b', w3: 'agent-a' },
    }),
  );
  const [reportsNone, countsNone, noEstimate, neverRan] = state.projects[0]?.tasks ?? [];
  assert.equal(reportsNone?.tokens.value, USAGE_WORDS.notReported);
  assert.equal(reportsNone?.cost.value, USAGE_WORDS.notReported);
  assert.equal(countsNone?.tokens.value, USAGE_WORDS.notCounted);
  assert.deepEqual(noEstimate?.cost, { value: USAGE_WORDS.notKnown, notes: [], known: false });
  assert.equal(neverRan?.app, null);
  assert.equal(
    neverRan?.tokens.value,
    USAGE_WORDS.noRuns,
    'a task that never ran is not said to be uncounted',
  );
  for (const row of state.projects[0]?.tasks ?? []) assert.notEqual(row.cost.value, '$0.00');
});

test('a sum missing a part never reads as the whole, and none given at all is Not Known', () => {
  const state = usageState(
    sources({
      projects: [
        project(
          'p1',
          'Garden',
          used({
            tokens: { input: 5, cache_read: null, cache_write: 0, output: 7, reasoning: null },
          }),
        ),
      ],
      tasks: [
        task('w1', 'p1', 'Half', '2026-11-13T10:01:00Z'),
        task('w2', 'p1', 'None', '2026-11-13T10:00:00Z'),
      ],
      usage: {
        w1: used({
          tokens: { input: 5, cache_read: null, cache_write: 0, output: 7, reasoning: null },
        }),
        w2: used({
          tokens: {
            input: null,
            cache_read: null,
            cache_write: null,
            output: null,
            reasoning: null,
          },
        }),
      },
      latest: { w1: 'agent-a', w2: 'agent-a' },
    }),
  );
  const card = state.projects[0];
  assert.deepEqual(card?.tokens, {
    value: '12',
    notes: ['At least this much', 'Some parts not reported'],
    known: true,
  });
  assert.equal(card?.breakdown?.[1]?.value, USAGE_WORDS.notGiven);
  assert.deepEqual(card?.tasks[1]?.tokens, {
    value: USAGE_WORDS.notKnown,
    notes: [],
    known: false,
  });
});

test('some turns unreported: the figure is the reported turns only, and says how many of the ended', () => {
  const state = usageState(
    sources({
      projects: [project('p1', 'Garden', used({ turns: 2, turns_ended: 3 }))],
      tasks: [task('w1', 'p1', 'Add a pond', '2026-11-13T10:00:00Z')],
      usage: { w1: used({ turns: 2, turns_ended: 3 }) },
      latest: { w1: 'agent-a' },
    }),
  );
  assert.deepEqual(state.projects[0]?.tokens.notes, [
    'At least this much',
    '2 of 3 turns reported',
  ]);
  assert.deepEqual(state.projects[0]?.tasks[0]?.tokens.notes, [
    'At least this much',
    '2 of 3 turns reported',
  ]);
  const both = usageState(
    sources({
      projects: [
        project(
          'p1',
          'Garden',
          used({
            turns: 2,
            turns_ended: 3,
            tokens: { input: 1, cache_read: null, cache_write: 0, output: 1, reasoning: null },
          }),
        ),
      ],
      tasks: [task('w1', 'p1', 'Add a pond', '2026-11-13T10:00:00Z')],
      latest: { w1: 'agent-a' },
    }),
  );
  assert.deepEqual(both.projects[0]?.tokens.notes, [
    'At least this much',
    'Some parts not reported',
    '2 of 3 turns reported',
  ]);
});

test('money: under a cent never reads $0.00, and a limit says its state in the host words', () => {
  const state = usageState(
    sources({
      projects: [
        project('p1', 'Garden', used({ cost: { estimated_usd: 0.004, prices: 'managed' } })),
      ],
      tasks: [
        task('w1', 'p1', 'Open', '2026-11-13T10:02:00Z'),
        task('w2', 'p1', 'Reached', '2026-11-13T10:01:00Z'),
        task('w3', 'p1', 'Waiting', '2026-11-13T10:00:00Z'),
      ],
      latest: { w1: 'agent-a', w2: 'agent-a', w3: 'agent-a' },
      usage: {
        w1: used({ cost: { estimated_usd: 0.2, prices: 'managed' } }),
        w2: used({ cost: { estimated_usd: 0.8, prices: 'list' } }),
      },
      limits: {
        w1: { limit_usd: 1, spent_usd: 0.2, state: 'open' },
        w2: { limit_usd: 0.5, spent_usd: 0.8, state: 'reached' },
        w3: { limit_usd: 2, spent_usd: null, state: 'waiting' },
      },
    }),
  );
  const card = state.projects[0];
  assert.deepEqual(
    card?.cost,
    { value: USAGE_WORDS.notKnown, notes: [USAGE_WORDS.mixed], known: false },
    'estimates by two kinds of prices are never added',
  );
  const tiny = usageState(
    sources({
      projects: [project('p1', 'Garden')],
      tasks: [task('w1', 'p1', 'Tiny', '2026-11-13T10:00:00Z')],
      usage: { w1: used({ cost: { estimated_usd: 0.004, prices: 'managed' } }) },
      latest: { w1: 'agent-a' },
    }),
  );
  assert.deepEqual(tiny.projects[0]?.cost, {
    value: '$0.004',
    notes: [USAGE_WORDS.prices.managed],
    known: true,
  });
  assert.equal(card?.limits, true);
  assert.equal(
    card?.estimates,
    "Each estimate is the agent app's own, by its list prices, or by your organisation's rates.",
    'two kinds of prices are both said, once',
  );
  assert.deepEqual(
    card?.tasks.map((row) => [row.limit?.value, row.limit?.notes]),
    [
      ['$1.00', []],
      ['$0.50', [USAGE_WORDS.limit.reached]],
      ['$2.00', [USAGE_WORDS.limit.waiting]],
    ],
  );
});

test('a project with no tasks is left out, newest work first, and the hosted assistant is said once', () => {
  const state = usageState(
    sources({
      projects: [project('p1', 'Older', used()), project('p2', 'Newer'), project('p3', 'Empty')],
      tasks: [
        task('w1', 'p1', 'One', '2026-11-12T10:00:00Z'),
        task('w2', 'p2', 'Two', '2026-11-13T10:00:00Z'),
      ],
      hosted: true,
    }),
  );
  assert.deepEqual(
    state.projects.map((card) => card.name),
    ['Newer', 'Older'],
  );
  assert.equal(state.assistant, USAGE_WORDS.assistant);
});

test('no word on the page joins facts on a dot or calls an estimate a bill', () => {
  const words = JSON.stringify(USAGE_WORDS);
  assert.doesNotMatch(words, /·|\bbill|\bcharge|\bspent/i);
});

/** A request to the page server as a browser would make it, with the headers given. */
function call(
  port: number,
  path: string,
  headers: Record<string, string> = {},
): Promise<{ status: number; headers: Record<string, unknown>; body: string }> {
  return new Promise((resolve, reject) => {
    const req = request(
      {
        host: '127.0.0.1',
        port,
        path,
        method: 'GET',
        headers: { host: `127.0.0.1:${port}`, 'sec-fetch-site': 'none', ...headers },
      },
      (res) => {
        let text = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => {
          text += chunk;
        });
        res.on('end', () =>
          resolve({ status: res.statusCode ?? 0, headers: res.headers, body: text }),
        );
      },
    );
    req.on('error', reject);
    req.end();
  });
}

test("the page's state reaches only the page's own session, never another site, and carries no name without it", async (t) => {
  const server = await startTestServer({
    page: {
      ports: [47950, 47959],
      mount: mountUsage(
        sources({
          projects: [project('p1', 'Secret Garden', used())],
          tasks: [task('w1', 'p1', 'Private title', '2026-11-13T10:00:00Z')],
          usage: { w1: used() },
          latest: { w1: 'agent-a' },
        }),
      ),
    },
  });
  t.after(() => server.stop());
  const page = server.page;
  assert.ok(page);
  const port = page.port;
  const link = new URL(page.link());
  const opened = await call(port, `${link.pathname}${link.search}`);
  const cookie = String(opened.headers['set-cookie']).split(';')[0] ?? '';
  const refused = [
    await call(port, '/setup/usage/state'),
    await call(port, '/setup/usage/state', { cookie: 'page=forged' }),
    await call(port, '/setup/usage/state', { cookie, origin: 'http://127.0.0.1:1' }),
    await call(port, '/setup/usage/state', { cookie, 'sec-fetch-site': 'cross-site' }),
  ];
  for (const answer of refused) {
    assert.equal(answer.status, 403);
    assert.doesNotMatch(answer.body, /Secret Garden|Private title/);
  }
  const own = await call(port, '/setup/usage/state', {
    cookie,
    origin: `http://127.0.0.1:${port}`,
    'sec-fetch-site': 'same-origin',
  });
  assert.equal(own.status, 200, own.body);
  assert.equal(JSON.parse(own.body).projects[0].tasks[0].title, 'Private title');
  const shown = await call(port, '/setup/usage', { cookie });
  assert.equal(shown.status, 200);
  assert.match(shown.body, /<h1>Usage<\/h1>/);
});

test("a project's tokens never read less than its tasks': one task leaving a part out drops nobody else's share", () => {
  const full = used({
    tokens: { input: 100, cache_read: 1000, cache_write: 400, output: 50, reasoning: null },
  });
  const partial = used({
    tokens: { input: 10, cache_read: 200, cache_write: null, output: 20, reasoning: null },
  });
  const state = usageState(
    sources({
      // The project's own sum leaves cache_write unknown, since one task did not give it.
      projects: [
        project(
          'p1',
          'Garden',
          used({
            turns: 2,
            turns_ended: 2,
            tokens: {
              input: 110,
              cache_read: 1200,
              cache_write: null,
              output: 70,
              reasoning: null,
            },
          }),
        ),
      ],
      tasks: [
        task('w1', 'p1', 'Full', '2026-11-13T10:01:00Z'),
        task('w2', 'p1', 'Partial', '2026-11-13T10:00:00Z'),
      ],
      usage: { w1: full, w2: partial },
      latest: { w1: 'agent-a', w2: 'agent-a' },
    }),
  );
  const card = state.projects[0];
  assert.deepEqual(
    card?.tasks.map((row) => row.tokens.value),
    ['1,550', '230'],
  );
  assert.deepEqual(card?.tokens, {
    value: '1,780',
    notes: ['At least this much', 'Some parts not reported'],
    known: true,
  });
  assert.deepEqual(
    card?.breakdown?.map((part) => [part.value, part.note]),
    [
      ['110', null],
      ['1,200', null],
      ['400', USAGE_WORDS.atLeast],
      ['70', null],
      [USAGE_WORDS.notGiven, null],
    ],
    'the parts add up to the total, the one a task left out marked at least',
  );
});

test("a project's estimate is its tasks' added up, at least where one gave none or a turn went unreported", () => {
  const state = usageState(
    sources({
      projects: [project('p1', 'Garden', used({ cost: null }))],
      tasks: [
        task('w1', 'p1', 'Priced', '2026-11-13T10:02:00Z'),
        task('w2', 'p1', 'No estimate', '2026-11-13T10:01:00Z'),
        task('w3', 'p1', 'Short', '2026-11-13T10:00:00Z'),
      ],
      usage: {
        w1: used({ cost: { estimated_usd: 0.59, prices: 'list' } }),
        w2: used({ cost: null }),
        w3: used({ turns: 1, turns_ended: 3, cost: { estimated_usd: 0.25, prices: 'list' } }),
      },
      latest: { w1: 'agent-a', w2: 'agent-a', w3: 'agent-a' },
    }),
  );
  const card = state.projects[0];
  assert.deepEqual(card?.cost, {
    value: '$0.84',
    notes: [USAGE_WORDS.atLeast, USAGE_WORDS.prices.list],
    known: true,
  });
  assert.deepEqual(
    card?.tasks.map((row) => [row.cost.value, row.cost.notes]),
    [
      ['$0.59', []],
      [USAGE_WORDS.notKnown, []],
      ['$0.25', [USAGE_WORDS.atLeast]],
    ],
  );
});

test('where turns went unreported every kind is at least, and whose prices goes under the table only when the cost cannot say it', () => {
  const state = usageState(
    sources({
      projects: [project('p1', 'Garden')],
      tasks: [
        task('w1', 'p1', 'Short', '2026-11-13T10:01:00Z'),
        task('w2', 'p1', 'Other prices', '2026-11-13T10:00:00Z'),
      ],
      usage: {
        w1: used({ turns: 1, turns_ended: 2 }),
        w2: used({ cost: { estimated_usd: 0.1, prices: 'managed' } }),
      },
      latest: { w1: 'agent-a', w2: 'agent-a' },
    }),
  );
  const card = state.projects[0];
  assert.deepEqual(
    card?.breakdown?.map((part) => part.note),
    Array(5).fill(USAGE_WORDS.atLeast),
  );
  assert.equal(
    card?.estimates,
    "Each estimate is the agent app's own, by its list prices, or by your organisation's rates.",
  );
});

test('a task that ran and reported nothing makes the project at least, says so, and its ended turns still count', () => {
  // AUDIT2's probe of 6116ced4d: task a reported, task b ran on an app that reports nothing; the domain's own
  // project sum counts every ended turn of both (1 of 3 reported).
  const a = used({
    tokens: { input: 1000, cache_read: 0, cache_write: 0, output: 0, reasoning: null },
    cost: { estimated_usd: 0.5, prices: 'list' },
  });
  const state = usageState(
    sources({
      projects: [project('p1', 'Garden', { ...a, turns: 1, turns_ended: 3 })],
      tasks: [
        task('a', 'p1', 'Reported', '2026-11-13T10:01:00Z'),
        task('b', 'p1', 'Silent', '2026-11-13T10:00:00Z'),
      ],
      usage: { a },
      latest: { a: 'agent-a', b: 'agent-b' },
    }),
  );
  const card = state.projects[0];
  assert.deepEqual(card?.tokens, {
    value: '1,000',
    notes: [USAGE_WORDS.atLeast, USAGE_WORDS.tasksMissing, '1 of 3 turns reported'],
    known: true,
  });
  assert.deepEqual(card?.cost, {
    value: '$0.50',
    notes: [USAGE_WORDS.atLeast, USAGE_WORDS.prices.list],
    known: true,
  });
  assert.ok(
    card?.breakdown?.every((part) => part.note === USAGE_WORDS.atLeast || part.note === null),
  );
  assert.equal(card?.breakdown?.[0]?.note, USAGE_WORDS.atLeast);
  assert.equal(card?.tasks[1]?.tokens.value, USAGE_WORDS.notCounted);
});

test('a project taken off the list is not on the page, nor its tasks or their use', () => {
  // AUDIT2's M4: the page reads only what a person may read (the projection's listed reads, ADR 0063).
  const b = new EventBuilder();
  const projection = new Projection({ reportsToolActivity: () => false });
  const apply = (...events: Parameters<Projection['apply']>[0][]) => {
    for (const event of events) projection.apply(event);
  };
  const report = (scope: Parameters<EventBuilder['runtimeEvent']>[0]) =>
    b.runtimeEvent(scope, 'runtime.usage.reported', {
      turn_id: 't1',
      tokens: { input: 10, cache_read: 0, cache_write: 0, output: 2, reasoning: null },
      cost: { estimated_usd: 0.25, prices: 'list' },
      counted: 'given',
    });
  const scopes = ['Kept', 'Taken Off'].map((name) => {
    const p = b.project(name);
    const w = b.workstream(p.projectId, `A task of ${name}`);
    const e = b.execution(p.projectId, w.workstreamId);
    apply(p.event, w.event, e.event);
    const scope = {
      projectId: p.projectId,
      workstreamId: w.workstreamId,
      executionId: e.executionId,
    };
    apply(
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }),
      report(scope),
      b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }),
    );
    return scope;
  });
  const off = scopes[1];
  assert.ok(off);
  const read = () =>
    usageState(projectionSources(projection, () => [runtime('mock', 'Mock', true)])).projects.map(
      (card) => card.name,
    );
  assert.deepEqual(read().sort(), ['Kept', 'Taken Off']);
  apply(
    b.controlPlane(
      'project.removed_from_list',
      { project_id: off.projectId, workstream_id: null, execution_id: null },
      {},
    ),
  );
  assert.deepEqual(read(), ['Kept'], 'taken off: neither the project nor its tasks nor their use');
});
