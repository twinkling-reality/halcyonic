import type {
  EvaluationResult,
  EventEnvelope,
  ExecutionView,
  UnderstandingResult,
} from '@halcyonic/contracts';
import { SalidiumClient } from '@halcyonic/integration-salidium';
import { FakeSalidium } from '@halcyonic/integration-salidium/testing';
import { SeorakClient } from '@halcyonic/integration-seorak';
import { type CapturedSession, FakeSeorak } from '@halcyonic/integration-seorak/testing';
import type { EvaluationSource } from '../intelligence/evaluation.ts';
import type { UnderstandingSource } from '../intelligence/understanding.ts';

/**
 * The recorded demonstration's understanding and evaluation sources (ADR 0019): stand-ins for
 * Salidium and Seorak that speak their real contracts, Salidium's consumer contract v1 and Seorak's
 * integration API v1, over loopback HTTP, with content invented for the demonstration's story. The
 * control plane reads them through its own routes and Halcyonic's real clients, exactly as it reads
 * the products, so the data types and the flow are the real ones; only the content is made up.
 *
 * They observe a demonstration session through the control plane's journal of it, the mock
 * runtime's events, as the products observe a Claude Code session through its hooks and files, and
 * derive what the products would conclude and measure from them, with the story adding what the
 * mock's events do not carry: how many lines each edit changes, the agent's plan, and the
 * explanations a model would write. Nothing here is Salidium's or Seorak's: every answer the
 * stand-ins give is marked synthetic, the stand-in for Salidium reports the version "simulated"
 * and an instance id of zeros, and a client says "simulated" wherever it names them.
 */

type Json = Record<string, unknown>;

/** What an edit in a demonstration scenario changes, which the mock runtime's events do not say. */
export interface StoryEdit {
  readonly added: number;
  readonly removed: number;
  /** The edit creates the file. */
  readonly creates: boolean;
}

/** An explanation as the understanding source's model would write one once a turn has ended. */
export interface StoryExplanation {
  readonly what: { readonly summary: string; readonly currently: string | null };
  readonly why: { readonly summary: string; readonly chain: readonly string[] };
  readonly how: {
    readonly summary: string;
    readonly root: string | null;
    readonly steps: readonly string[];
  };
  readonly approachChange: {
    readonly from: string;
    readonly fromSteps: readonly string[];
    readonly why: string;
    readonly to: string;
    readonly toSteps: readonly string[];
  } | null;
}

/** What the stand-ins know about one scenario of the demonstration beyond the mock's events. */
export interface SourceStory {
  /** What each edit changes, by the scenario's tool call id. */
  readonly edits: Readonly<Record<string, StoryEdit>>;
  /** Steps in the agent's plan that it has not done, as the understanding source reads them. */
  readonly planned: readonly string[];
  /** Work the agent said remains, by the agent message that said so, in the agent's own words. */
  readonly reported: Readonly<Record<string, string>>;
  /** The explanation of a finished turn, by the agent message that ended it. */
  readonly explanations: Readonly<Record<string, StoryExplanation>>;
}

const SIGN_IN_BEFORE = 'Sign-in accepted any number of failed attempts.';
const SIGN_IN_WHY = {
  summary: 'Nothing counted failed attempts, so guessing a password cost nothing.',
  chain: ['Unlimited failed attempts', 'Passwords can be guessed'],
};

/** The demonstration's three scenarios, as the stand-ins know them. */
export const DEMONSTRATION_STORIES: Readonly<Record<string, SourceStory>> = {
  order_history_pagination: {
    edits: {
      'call-2': { added: 38, removed: 9, creates: false },
      'call-3': { added: 52, removed: 0, creates: false },
    },
    planned: ['Document the cursor parameter in the API reference'],
    reported: {},
    explanations: {
      'GET /orders now returns 50 orders per page with a cursor for the next, and all 31 order tests pass.':
        {
          what: {
            summary: 'GET /orders sent every order at once, so long histories loaded slowly.',
            currently: null,
          },
          why: {
            summary: 'The route read the whole order table for every request.',
            chain: ['Every order in one response', 'Slow pages for long histories'],
          },
          how: {
            summary:
              'Orders are read 50 at a time by creation time, with a cursor for the next page.',
            root: 'src/routes/orders.ts',
            steps: ['Sort by creation time', 'Read 50 after the cursor', 'Return the next cursor'],
          },
          approachChange: null,
        },
    },
  },
  order_confirmation_email: {
    edits: {
      'call-1': { added: 46, removed: 0, creates: true },
      'call-2': { added: 11, removed: 2, creates: false },
    },
    planned: [],
    reported: {},
    explanations: {
      'Checkout now queues an order confirmation email, and all 18 checkout tests pass.': {
        what: { summary: 'Customers heard nothing after a successful checkout.', currently: null },
        why: {
          summary: 'Checkout finished without handing anything to the mail queue.',
          chain: ['No message after checkout', 'Customers unsure their order went through'],
        },
        how: {
          summary: 'Checkout queues a confirmation from a new template once the order is placed.',
          root: 'src/checkout/complete.ts',
          steps: ['Render the order summary', 'Queue the email', 'Leave sending to the queue'],
        },
        approachChange: null,
      },
    },
  },
  sign_in_rate_limit: {
    edits: {
      'call-2': { added: 57, removed: 0, creates: true },
      'call-3': { added: 14, removed: 0, creates: true },
      'call-5': { added: 18, removed: 6, creates: false },
      'call-6': { added: 4, removed: 11, creates: false },
      'call-7': { added: 26, removed: 19, creates: false },
    },
    planned: [],
    reported: {
      'Understood, I did not run the migration. The limiter has no table to keep attempts in until it runs.':
        'The limiter has no table to keep attempts in until it runs.',
    },
    explanations: {
      'The migration ran and the limit works per address. One test fails: it expects failures from different addresses to lock the same account.':
        {
          what: {
            summary: SIGN_IN_BEFORE,
            currently: 'Limited per address; one test still expects a limit per account.',
          },
          why: SIGN_IN_WHY,
          how: {
            summary:
              'A new table counts failures, and the middleware answers 429 after five in fifteen minutes.',
            root: 'src/middleware/rate-limit.ts',
            steps: [
              'Count failures per address',
              'Refuse after five in fifteen minutes',
              'Keep the counts in a table',
            ],
          },
          approachChange: null,
        },
      'Understood, I did not run the migration. The limiter has no table to keep attempts in until it runs.':
        {
          what: {
            summary: SIGN_IN_BEFORE,
            currently: 'The limiter is written but waits for its table: the migration did not run.',
          },
          why: SIGN_IN_WHY,
          how: {
            summary: 'A limiter in front of sign-in, waiting for a table to count failures in.',
            root: 'src/middleware/rate-limit.ts',
            steps: ['Count failures per address', 'Refuse after five in fifteen minutes'],
          },
          approachChange: null,
        },
      'Failed attempts now count per account and per address, and all 24 sign-in tests pass.': {
        what: { summary: SIGN_IN_BEFORE, currently: null },
        why: SIGN_IN_WHY,
        how: {
          summary:
            'Failures count per address and per account in one table; either limit refuses sign-in for fifteen minutes.',
          root: 'src/middleware/rate-limit.ts',
          steps: [
            'Count failures per address and per account',
            'Refuse after five in fifteen minutes',
            'Keep the counts in a table',
          ],
        },
        approachChange: null,
      },
      'The lockout test now expects limits per address only, and all 24 sign-in tests pass.': {
        what: { summary: SIGN_IN_BEFORE, currently: null },
        why: SIGN_IN_WHY,
        how: {
          summary: 'Failures count per address only, and the lockout test now expects that.',
          root: 'src/middleware/rate-limit.ts',
          steps: ['Count failures per address', 'Refuse after five in fifteen minutes'],
        },
        approachChange: {
          from: 'Lock an account after failures from many addresses',
          fromSteps: ['Count per address', 'Count per account'],
          why: 'The instruction chose limits per address only and a changed test.',
          to: 'Limit per address only',
          toSteps: ['Count per address', 'Change the lockout test'],
        },
      },
      'Failed attempts are counted in memory until the migration runs, and all 24 sign-in tests pass.':
        {
          what: {
            summary: SIGN_IN_BEFORE,
            currently: 'The counts live in memory and reset when the service restarts.',
          },
          why: SIGN_IN_WHY,
          how: {
            summary: 'Failures count in memory per address and per account until the table exists.',
            root: 'src/middleware/rate-limit.ts',
            steps: ['Count failures in memory', 'Refuse after five in fifteen minutes'],
          },
          approachChange: null,
        },
    },
  },
};

/** The version the stand-in for Salidium reports, so no answer names a Salidium release. */
export const SIMULATED_VERSION = 'simulated';

/** The stand-in for Salidium's instance id: a real daemon's is random, never all zeros. */
export const SIMULATED_INSTANCE_ID = '0'.repeat(32);

/** List prices the stand-in for Seorak estimates the cost at, per million tokens. */
const PRICE_PER_MILLION = { input: 3, output: 15 } as const;

/** Tokens a step of a session uses, as the stand-in for Seorak counts them. */
const TOKENS = {
  turn: { input: 18_000, output: 0 },
  message: { input: 0, output: 600 },
  read: { input: 9_000, output: 0 },
  edit: { input: 11_000, output: 1_400 },
  otherTool: { input: 3_000, output: 0 },
  testRun: { input: 7_000, output: 0 },
  approval: { input: 1_000, output: 0 },
} as const;

/** How long the stand-in for Seorak calls its answers fresh, as Seorak's own examples do. */
const FRESH_MS = 5 * 60_000;

/** The stand-in for Seorak covers the 90 days that end on the day of the answer. */
const COVERAGE_DAYS = 90;

interface FileChange {
  readonly path: string;
  changeCount: number;
  added: number;
  removed: number;
  readonly kinds: ('add' | 'update')[];
  lastChangedAt: string;
  reason: Statement | null;
}

interface Statement {
  readonly text: string;
  readonly at: string;
}

interface TestRun {
  readonly id: string;
  readonly label: string;
  readonly outcome: 'passed' | 'failed' | 'errored';
  readonly summary: string | null;
  readonly at: string;
}

interface TurnEnd {
  readonly how: 'completed' | 'interrupted' | 'failed';
  readonly at: string;
  /** The agent message that ended the turn, if one came after it started. */
  readonly message: string | null;
  /** The evidence the stand-in had seen when the turn ended. */
  readonly sequence: number;
}

/** What the stand-ins have observed of one session, from its events in the control plane's journal. */
interface Observed {
  readonly startedAt: string;
  /** Pieces of evidence seen, as a source numbers them; it only grows. */
  sequence: number;
  /** When the newest evidence happened. */
  newest: string;
  running: boolean;
  turnStartedAt: string | null;
  readonly turnEnds: TurnEnd[];
  readonly statements: Statement[];
  readonly files: Map<string, FileChange>;
  commands: number;
  toolCalls: number;
  readonly runs: TestRun[];
  waiting: { readonly summary: string; readonly since: string } | null;
  approvalsAnswered: number;
  toolErrors: number;
  firstErrorAt: string | null;
  readonly tokens: { input: number; output: number };
}

function observe(events: readonly EventEnvelope[], story: SourceStory): Observed {
  const first = events[0];
  if (first === undefined) throw new Error('a stand-in observes a session only once it has events');
  const observed: Observed = {
    startedAt: first.occurred_at,
    sequence: 0,
    newest: first.occurred_at,
    running: false,
    turnStartedAt: null,
    turnEnds: [],
    statements: [],
    files: new Map(),
    commands: 0,
    toolCalls: 0,
    runs: [],
    waiting: null,
    approvalsAnswered: 0,
    toolErrors: 0,
    firstErrorAt: null,
    tokens: { input: 0, output: 0 },
  };
  const tools = new Map<string, { name: string; title: string | null }>();
  const spend = (use: { readonly input: number; readonly output: number }) => {
    observed.tokens.input += use.input;
    observed.tokens.output += use.output;
  };
  const evidence = (at: string) => {
    observed.sequence += 1;
    observed.newest = at;
  };
  for (const event of events) {
    const at = event.occurred_at;
    switch (event.event_type) {
      case 'runtime.turn.started':
        observed.running = true;
        observed.turnStartedAt = at;
        spend(TOKENS.turn);
        evidence(at);
        break;
      case 'runtime.agent_message':
        observed.statements.push({ text: event.payload.text, at });
        spend(TOKENS.message);
        evidence(at);
        break;
      case 'runtime.tool.started':
        tools.set(event.payload.tool_call_id, {
          name: event.payload.tool_name,
          title: event.payload.title,
        });
        break;
      case 'runtime.tool.completed': {
        const id = event.payload.tool_call_id;
        const tool = tools.get(id);
        if (tool === undefined) throw new Error(`tool call ${id} completed before it started`);
        observed.toolCalls += 1;
        if (event.payload.outcome === 'failed') {
          observed.toolErrors += 1;
          observed.firstErrorAt ??= at;
        }
        if (tool.name === 'edit' && tool.title !== null) {
          const edit = story.edits[id];
          if (edit === undefined) throw new Error(`the story says nothing about edit ${id}`);
          change(observed, tool.title, edit, at);
          spend(TOKENS.edit);
        } else if (tool.name === 'read') {
          spend(TOKENS.read);
        } else {
          if (tool.name === 'shell') observed.commands += 1;
          spend(TOKENS.otherTool);
        }
        evidence(at);
        break;
      }
      case 'runtime.test_run.completed':
        observed.runs.push({
          id: event.payload.test_run_id,
          label: testLabel(events, event.payload.test_run_id),
          outcome: event.payload.outcome,
          summary: event.payload.summary,
          at,
        });
        spend(TOKENS.testRun);
        evidence(at);
        break;
      case 'runtime.approval.requested':
        observed.waiting = { summary: event.payload.subject.summary, since: at };
        spend(TOKENS.approval);
        evidence(at);
        break;
      case 'runtime.approval.resolved':
        observed.waiting = null;
        observed.approvalsAnswered += 1;
        evidence(at);
        break;
      case 'runtime.turn.completed':
      case 'runtime.turn.interrupted':
      case 'runtime.turn.failed': {
        const how =
          event.event_type === 'runtime.turn.completed'
            ? 'completed'
            : event.event_type === 'runtime.turn.interrupted'
              ? 'interrupted'
              : 'failed';
        evidence(at);
        const last = observed.statements.at(-1);
        const said = last !== undefined && last.at >= (observed.turnStartedAt ?? '');
        observed.turnEnds.push({
          how,
          at,
          message: said ? last.text : null,
          sequence: observed.sequence,
        });
        observed.running = false;
        observed.waiting = null;
        break;
      }
      default:
        break;
    }
  }
  return observed;
}

function testLabel(events: readonly EventEnvelope[], id: string): string {
  for (const event of events) {
    if (event.event_type === 'runtime.test_run.started' && event.payload.test_run_id === id) {
      return event.payload.label ?? 'tests';
    }
  }
  return 'tests';
}

function change(observed: Observed, path: string, edit: StoryEdit, at: string): void {
  const reason = [...observed.statements].reverse().find((statement) => statement.at <= at);
  const existing = observed.files.get(path);
  const kind = edit.creates && existing === undefined ? 'add' : 'update';
  if (existing === undefined) {
    observed.files.set(path, {
      path,
      changeCount: 1,
      added: edit.added,
      removed: edit.removed,
      kinds: [kind],
      lastChangedAt: at,
      reason: reason ?? null,
    });
    return;
  }
  existing.changeCount += 1;
  existing.added += edit.added;
  existing.removed += edit.removed;
  if (!existing.kinds.includes(kind)) existing.kinds.push(kind);
  existing.lastChangedAt = at;
  existing.reason = reason ?? null;
}

const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`;

function counts(summary: string | null) {
  const number = (pattern: RegExp) => {
    const match = summary === null ? null : pattern.exec(summary);
    return match?.[1] === undefined ? null : Number(match[1]);
  };
  const passed = number(/(\d+) passed/);
  const failed = number(/(\d+) failed/);
  return {
    passed,
    failed,
    skipped: null,
    total: passed !== null && failed !== null ? passed + failed : null,
  };
}

/** The run's label in the understanding source's wording: its result, then what ran. */
const runLabel = (run: TestRun) => `${run.summary ?? run.outcome} (${run.label})`;

function statement(said: Statement) {
  return { text: said.text, provenance: 'reported', author: 'agent', at: said.at };
}

/** Salidium's session report, version 2, as the stand-in would serve it for these observations. */
function salidiumReport(nativeId: string, observed: Observed, story: SourceStory): Json {
  const files = [...observed.files.values()].sort(
    (a, b) => b.lastChangedAt.localeCompare(a.lastChangedAt) || a.path.localeCompare(b.path),
  );
  const latest = observed.runs.at(-1);
  const passing = observed.runs.filter((run) => run.outcome === 'passed').at(-1);
  const verified = (file: FileChange) => passing !== undefined && passing.at > file.lastChangedAt;
  const unverified = files.filter((file) => !verified(file)).map((file) => file.path);
  const stale = (run: TestRun) => files.some((file) => file.lastChangedAt > run.at);
  const failing = latest !== undefined && latest.outcome !== 'passed' && !stale(latest);
  const lastEnd = observed.turnEnds.at(-1);
  const added = files.reduce((sum, file) => sum + file.added, 0);
  const removed = files.reduce((sum, file) => sum + file.removed, 0);

  const verdict = (() => {
    if (observed.waiting !== null) {
      return {
        headline: 'Waiting for you',
        tone: 'attention',
        provenance: 'observed',
        because: observed.waiting.summary,
        at: observed.waiting.since,
      };
    }
    if (observed.running) {
      return {
        headline: 'Working',
        tone: 'working',
        provenance: 'observed',
        because: null,
        at: observed.turnStartedAt,
      };
    }
    if (lastEnd?.how === 'interrupted') {
      return {
        headline: 'Stopped',
        tone: 'neutral',
        provenance: 'observed',
        because:
          unverified.length === 0
            ? null
            : `${plural(unverified.length, 'file', 'files')} changed and not checked after.`,
        at: lastEnd.at,
      };
    }
    if (failing && latest !== undefined) {
      const failed = counts(latest.summary).failed;
      return {
        headline: failed === null ? 'Tests failing' : `${plural(failed, 'test', 'tests')} failing`,
        tone: 'fail',
        provenance: 'observed',
        because: runLabel(latest),
        at: latest.at,
      };
    }
    if (files.length === 0) {
      return {
        headline: 'No changes',
        tone: 'neutral',
        provenance: 'observed',
        because: null,
        at: lastEnd?.at ?? null,
      };
    }
    const changed = plural(files.length, 'file', 'files');
    if (unverified.length === 0 && passing !== undefined) {
      return {
        headline: `${changed} changed, verified`,
        tone: 'pass',
        provenance: 'inferred',
        because: `A passing check ran after the last change: ${runLabel(passing)}.`,
        at: passing.at,
      };
    }
    return {
      headline: `${changed} changed, unverified`,
      tone: 'attention',
      provenance: 'inferred',
      because:
        passing === undefined
          ? 'No check ran after the last change.'
          : `${plural(unverified.length, 'file', 'files')} changed after the last passing check (${runLabel(passing)}).`,
      at: lastEnd?.at ?? null,
    };
  })();

  const run = (each: TestRun) => ({
    id: each.id,
    at: each.at,
    label: runLabel(each),
    method: 'test',
    runner: 'make',
    outcome: each.outcome === 'passed' ? 'pass' : each.outcome === 'failed' ? 'fail' : 'unknown',
    counts: counts(each.summary),
    // A suite named on the command line: the source cannot tell whether it covers everything.
    scope: 'unknown',
    // The runtime reports the outcome but no exit code, so the code stays unknown.
    exit: {
      code: null,
      observation:
        each.outcome === 'passed'
          ? 'inferred-success'
          : each.outcome === 'failed'
            ? 'inferred-failure'
            : 'unknown',
    },
    provenance: 'observed',
    caveats: [],
    stale: stale(each),
    laterUnreadable: 0,
  });

  const groups: Json[] = [];
  if (observed.waiting !== null) {
    groups.push(
      group('waiting-permission', 'Waiting for your permission', 'high', observed.waiting.since, {
        label: 'Waiting for your permission',
        instance: observed.waiting.summary,
        provenance: 'observed',
      }),
    );
  }
  if (failing && latest !== undefined) {
    const label = `${counts(latest.summary).failed ?? 'Some'} failing (${latest.label})`;
    groups.push(
      group('verification-failed', label, 'high', latest.at, {
        label,
        instance: null,
        provenance: 'observed',
      }),
    );
  }
  if (!observed.running && unverified.length > 0 && lastEnd !== undefined) {
    const label = `${plural(unverified.length, 'file', 'files')} changed since the last passing check`;
    groups.push(
      group('changes-unverified', label, 'low', lastEnd.at, {
        label,
        instance: null,
        provenance: 'inferred',
      }),
    );
  }
  const answeredFailures = observed.runs.filter(
    (each, index) =>
      each.outcome !== 'passed' &&
      observed.runs.slice(index + 1).some((later) => later.outcome === 'passed'),
  ).length;

  const remaining: Json[] = [];
  if (observed.statements.length > 0) {
    story.planned.forEach((text, index) => {
      remaining.push({
        id: `plan:${index + 1}`,
        text,
        status: 'pending',
        provenance: 'planned',
        source: 'plan',
      });
    });
  }
  if (failing && latest !== undefined) {
    remaining.push({
      id: `verification:${latest.id}`,
      text: runLabel(latest),
      status: 'failing',
      provenance: 'observed',
      source: 'verification',
    });
  }
  for (const said of observed.statements) {
    const text = story.reported[said.text];
    if (text === undefined) continue;
    remaining.push({
      id: `agent:${said.at}`,
      text,
      status: 'reported',
      provenance: 'reported',
      source: 'agent',
    });
  }

  const explained = [...observed.turnEnds]
    .reverse()
    .find((end) => end.how === 'completed' && end.message !== null);
  const explanation = explained?.message ? story.explanations[explained.message] : undefined;

  return {
    format: 'salidium.session-report',
    version: 2,
    generatedAt: observed.newest,
    session: {
      id: `claude-code:${nativeId}`,
      native: { provider: 'claude-code', sessionId: nativeId },
      evidenceSeq: observed.sequence,
    },
    verdict,
    latestStatement:
      observed.statements.length === 0 ? null : statement(lastOf(observed.statements)),
    waiting:
      observed.waiting === null
        ? null
        : {
            kind: 'permission',
            summary: observed.waiting.summary,
            since: observed.waiting.since,
            provenance: 'observed',
          },
    changes: {
      glance:
        files.length === 0
          ? 'No files changed'
          : `${plural(files.length, 'file', 'files')} changed (+${added} −${removed})${
              observed.commands === 0
                ? ''
                : ` · ${plural(observed.commands, 'command', 'commands')}`
            }`,
      files: files.map((file) => ({
        path: file.path,
        changeCount: file.changeCount,
        linesAdded: file.added,
        linesRemoved: file.removed,
        kinds: [...file.kinds],
        lastChangedAt: file.lastChangedAt,
        coverage: {
          verifiedAfter: verified(file),
          by: verified(file) && passing !== undefined ? runLabel(passing) : null,
          provenance: 'inferred',
        },
        reason: file.reason === null ? null : statement(file.reason),
      })),
      commits: [],
    },
    verification: {
      glance:
        latest === undefined
          ? 'No checks ran'
          : `${runLabel(latest)}${stale(latest) ? ' (before latest changes)' : ''}`,
      runs: observed.runs.map(run),
      latestByMethod: latest === undefined ? [] : [run(latest)],
      unverifiedFiles: unverified,
      statements: observed.statements
        .filter((said) => /\btests?\b/i.test(said.text))
        .map(statement),
    },
    review: {
      glance:
        groups.length === 0
          ? 'Nothing needs attention'
          : `${plural(groups.length, 'item needs', 'items need')} attention`,
      open: groups.length,
      resolved: observed.approvalsAnswered + answeredFailures,
      groups,
    },
    remaining: {
      glance: remaining.length === 0 ? 'Nothing remaining' : `${remaining.length} remaining`,
      items: remaining,
    },
    explanation:
      explanation === undefined || explained === undefined
        ? {
            status: 'none',
            provenance: 'explained',
            current: false,
            basedOnSeq: null,
            generatedAt: null,
            model: null,
            content: null,
          }
        : {
            status: 'generated',
            provenance: 'explained',
            current: explained.sequence === observed.sequence,
            basedOnSeq: explained.sequence,
            generatedAt: explained.at,
            // Nothing wrote it but the demonstration's author, so no model is named.
            model: null,
            content: {
              what: explanation.what,
              why: {
                summary: explanation.why.summary,
                lanes: [],
                chain: [...explanation.why.chain],
              },
              how: { ...explanation.how, steps: [...explanation.how.steps] },
              approachChange: explanation.approachChange && {
                ...explanation.approachChange,
                fromSteps: [...explanation.approachChange.fromSteps],
                toSteps: [...explanation.approachChange.toSteps],
              },
            },
          },
  };
}

function group(
  rule: string,
  label: string,
  severity: string,
  at: string,
  item: { label: string; instance: string | null; provenance: string },
): Json {
  return {
    rule,
    label,
    severity,
    occurrences: 1,
    latestAt: at,
    items: [{ id: `${rule}:${at}`, ...item, createdAt: at, repeats: 1 }],
  };
}

function lastOf<T>(items: readonly T[]): T {
  const last = items.at(-1);
  if (last === undefined) throw new Error('empty');
  return last;
}

/** The three documents the stand-in for Seorak serves about a session: resolve, outcome and lens. */
function seorakDocuments(
  ref: string,
  observed: Observed,
): { resolve: Json; outcome: Json; lens: Json } {
  const generatedAt = observed.newest;
  const day = generatedAt.slice(0, 10);
  const from = new Date(Date.parse(`${day}T00:00:00.000Z`) - (COVERAGE_DAYS - 1) * 86_400_000)
    .toISOString()
    .slice(0, 10);
  const staleAt = new Date(Date.parse(generatedAt) + FRESH_MS).toISOString();
  const envelope = (computed: boolean) => ({
    apiVersion: 'v1',
    availability: computed
      ? { state: 'available', reason: null }
      : { state: 'unavailable', reason: 'not-yet-computed' },
    coverage: {
      requested: { from, through: day },
      observed: computed ? { from: observed.startedAt.slice(0, 10), through: day } : null,
      matchedSessionCount: 1,
      includedSessionCount: computed ? 1 : 0,
      complete: computed,
      omissions: computed ? [] : ['projection-pending'],
    },
    freshness: {
      state: computed ? 'fresh' : 'revalidating',
      generatedAt,
      dataThrough: computed ? generatedAt : null,
      staleAt,
    },
  });
  const cost =
    (observed.tokens.input * PRICE_PER_MILLION.input +
      observed.tokens.output * PRICE_PER_MILLION.output) /
    1_000_000;
  const passed = observed.runs.filter((run) => run.outcome === 'passed').length;
  const runs = observed.runs.length;
  // The outcome is computed once a turn has ended; until then Seorak says it is not yet computed.
  const settled = observed.turnEnds.length > 0;
  return {
    resolve: {
      ...envelope(true),
      session: {
        sessionRef: ref,
        agent: 'claude-code',
        status: 'active',
        startedAt: observed.startedAt,
        endedAt: null,
        toolCallCount: observed.toolCalls,
        promptCount: null,
        costUsd: Math.round(cost * 10_000) / 10_000,
        launcher: 'halcyonic',
      },
    },
    outcome: {
      ...envelope(settled),
      sessionRef: ref,
      outcome: settled
        ? {
            commitsLanded: 0,
            // Measured when the session ends, and it has not: unknown, never zero.
            uncommitted: null,
            // Measured three days after the lines were written.
            lineSurvival: null,
            errorCount: observed.toolErrors,
            firstErrorAt: observed.firstErrorAt,
            endReason: null,
          }
        : null,
    },
    lens: {
      ...envelope(true),
      result: {
        lens: 'verification',
        level: 'session',
        target: { kind: 'session', sessionRef: ref },
        headline: null,
        rows:
          runs === 0
            ? []
            : [
                {
                  label: 'test',
                  metrics: [
                    { key: 'runs', label: 'Measured runs', value: runs, unit: 'count' },
                    { key: 'passed', label: 'Passed runs', value: passed, unit: 'count' },
                    { key: 'passRate', label: 'Pass rate', value: passed / runs, unit: 'percent' },
                  ],
                },
              ],
        emptyReason: runs === 0 ? 'No verification result was captured.' : null,
        loadedSessionCount: 1,
        momentCount: observed.sequence,
        nextCursor: null,
      },
    },
  };
}

/** The demonstration's runtimes are the mock runtime under other names. */
const DEMONSTRATION_RUNTIME_KIND = 'mock';

/**
 * The kind of session the stand-ins treat a demonstration session as: the one both products observe
 * and Halcyonic's runtime kind `claude-agent` runs. Asking the real clients about that kind sends
 * the requests a Claude Code session's answer takes; nothing claims the session was Claude Code's,
 * because Halcyonic's contracts carry no provider and every answer is marked synthetic.
 */
const OBSERVED_AS = 'claude-agent';

/**
 * The stand-ins, running: a stand-in Salidium daemon and a stand-in Seorak plane on loopback, the
 * test doubles of Halcyonic's integrations, answering with documents derived from what they observe.
 */
export class DemonstrationSources {
  readonly #salidium: FakeSalidium;
  readonly #seorak: FakeSeorak;
  readonly #salidiumClient: SalidiumClient;
  readonly #seorakClient: SeorakClient;
  readonly #sessions = new Map<string, CapturedSession>();

  private constructor(salidium: FakeSalidium, seorak: FakeSeorak) {
    this.#salidium = salidium;
    this.#seorak = seorak;
    this.#salidiumClient = new SalidiumClient({ home: salidium.home, credential: salidium.token });
    // A stand-in has no request budget of its own to protect, so one evaluation per instant fits.
    this.#seorakClient = new SeorakClient({ port: seorak.port, requestsPerMinute: 1_000_000 });
  }

  static async start(): Promise<DemonstrationSources> {
    const salidium = await FakeSalidium.start();
    salidium.reports.clear();
    salidium.version = SIMULATED_VERSION;
    salidium.instanceId = SIMULATED_INSTANCE_ID;
    salidium.writeDiscovery();
    const seorak = await FakeSeorak.start();
    seorak.sessions.length = 0;
    return new DemonstrationSources(salidium, seorak);
  }

  /** Brings what the stand-ins say about an execution's session up to these events of it. */
  observe(execution: ExecutionView, story: SourceStory, events: readonly EventEnvelope[]): void {
    const nativeId = execution.native_id;
    if (nativeId === null) return;
    const observed = observe(events, story);
    const report = salidiumReport(nativeId, observed, story);
    this.#salidium.reports.set(`claude-code:${nativeId}`, report);
    let session = this.#sessions.get(nativeId);
    if (session === undefined) {
      session = this.#seorak.capture('claude-code', nativeId);
      this.#sessions.set(nativeId, session);
    }
    const documents = seorakDocuments(session.ref, observed);
    session.resolve = documents.resolve;
    session.outcome = documents.outcome;
    session.lens = documents.lens;
  }

  /** The control plane's sources: Halcyonic's real clients reading the stand-ins, every answer marked synthetic. */
  get sources(): { understanding: UnderstandingSource; evaluation: EvaluationSource } {
    return {
      understanding: {
        understand: async (runtimeKind, nativeId) =>
          simulatedUnderstanding(
            await this.#salidiumClient.understand(observedAs(runtimeKind), nativeId),
          ),
      },
      evaluation: {
        evaluate: async (runtimeKind, nativeId) =>
          simulatedEvaluation(
            await this.#seorakClient.evaluate(observedAs(runtimeKind), nativeId, {
              credential: this.#seorak.token,
            }),
          ),
      },
    };
  }

  async close(): Promise<void> {
    await Promise.all([this.#salidium.close(), this.#seorak.close()]);
  }
}

function observedAs(runtimeKind: string): string {
  if (runtimeKind !== DEMONSTRATION_RUNTIME_KIND) {
    throw new Error(
      `the demonstration's stand-ins observe only its own runtimes, not ${runtimeKind}`,
    );
  }
  return OBSERVED_AS;
}

/** An answer of the stand-in for Salidium, marked as not Salidium's. */
export function simulatedUnderstanding(result: UnderstandingResult): UnderstandingResult {
  if (result.availability !== 'available') return result;
  const { understanding } = result;
  return {
    ...result,
    understanding: { ...understanding, source: { ...understanding.source, synthetic: true } },
  };
}

/** An answer of the stand-in for Seorak, marked as not Seorak's. */
export function simulatedEvaluation(result: EvaluationResult): EvaluationResult {
  if (result.availability !== 'available') return result;
  const { evaluation } = result;
  return {
    ...result,
    evaluation: { ...evaluation, source: { ...evaluation.source, synthetic: true } },
  };
}
