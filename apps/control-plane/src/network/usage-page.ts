import { readFileSync } from 'node:fs';
import type {
  ExecutionView,
  ProjectView,
  RuntimeDescriptor,
  RunUsage,
  SpendingCapView,
  UsagePrices,
  WorkstreamView,
} from '@halcyonic/contracts';
import type { PageRoutes } from './pairing-page.ts';

/**
 * The Mac's Usage page: for each project and each of its tasks, what its agent runs used as the agent apps
 * reported it, and the app's own estimate of the cost. Nothing is counted or priced here: a project's figures are
 * `ProjectView.usage` and a task's are the same sum over its runs (`Projection.workstreamUsage`), by the domain's one
 * rule. Unknown stays unknown: a figure that is not there says why, never 0, and a sum missing a part says so.
 */

/** One figure as the page draws it: its value, and the small lines under it that are owed. */
export interface UsageFigure {
  readonly value: string;
  readonly notes: readonly string[];
  /** False where the value is a word for why there is no number. */
  readonly known: boolean;
}

/** A task's row in its project's table. */
export interface UsageTaskRow {
  readonly workstream_id: string;
  readonly title: string;
  /** The agent app of its latest run, by its own name; null when it has never run. */
  readonly app: string | null;
  readonly tokens: UsageFigure;
  readonly cost: UsageFigure;
  /** Its spending limit, where it has one. */
  readonly limit: UsageFigure | null;
}

/** One part of the tokens, for the Breakdown table. */
export interface UsagePart {
  readonly label: string;
  readonly value: string;
  /** "At least this much" where a task left this part out. */
  readonly note: string | null;
}

/** A project's card. */
export interface UsageProjectCard {
  readonly project_id: string;
  readonly name: string;
  readonly tokens: UsageFigure;
  readonly cost: UsageFigure;
  /** Null where nothing was reported. */
  readonly breakdown: readonly UsagePart[] | null;
  readonly tasks: readonly UsageTaskRow[];
  /** Whether any task has a spending limit, so the table draws that column. */
  readonly limits: boolean;
  /** Under the task table, once: whose prices its estimates are by; null where none has one. */
  readonly estimates: string | null;
}

export interface UsageState {
  readonly projects: readonly UsageProjectCard[];
  /** The one line owed while the assistant runs on a hosted model: its requests are not in these figures. */
  readonly assistant: string | null;
}

/** What the page reads, each from the host's own records. */
export interface UsageSources {
  readonly projects: () => readonly ProjectView[];
  readonly tasks: (projectId: string) => readonly WorkstreamView[];
  /** A task's runs summed by the same rule as a project's; null where none reported. */
  readonly taskUsage: (workstreamId: string) => RunUsage | null;
  /** The runtime id of a task's latest run, and its spending limit, or nulls when it has none. */
  readonly latest: (task: WorkstreamView) => {
    readonly runtimeId: string | null;
    readonly limit: SpendingCapView | null;
  };
  readonly runtimes: () => readonly RuntimeDescriptor[];
  /** Whether the person chose a hosted model for the assistant (Use Model), as the host records it now. */
  readonly assistantHosted: () => boolean;
}

/** The page's words, in one place. */
export const USAGE_WORDS = {
  /** Nothing reported yet by an app that reports. */
  notReported: 'Not Reported Yet',
  /** A task that has not run at all. */
  noRuns: 'No Runs Yet',
  /** The app says nothing of what it used. */
  notCounted: 'Not Reported by This App',
  /** No estimate of the money. */
  notKnown: 'Not Known',
  /** One part the app left out. */
  notGiven: 'Not Reported',
  /** Estimates by different prices, which are never added. */
  mixed: 'Estimates use different prices',
  /** A sum that is less than all that was used, and why. */
  atLeast: 'At least this much',
  partsMissing: 'Some parts not reported',
  tasksMissing: 'Some tasks not reported',
  turnsMissing: (turns: number, ended: number) => `${turns} of ${ended} turns reported`,
  prices: {
    list: "The agent app's own estimate, by its list prices",
    managed: "The agent app's own estimate, by your organisation's rates",
    catalog: "The agent app's own estimate, by a public price list",
  } satisfies Record<UsagePrices, string>,
  /** Under a task table, once: whose prices its estimates are by. */
  estimates: (kinds: readonly UsagePrices[]) =>
    `Each estimate is the agent app's own, by ${kinds.map((kind) => PRICED_BY[kind]).join(', or by ')}.`,
  parts: {
    input: 'Input',
    cache_read: 'Read From Cache',
    cache_write: 'Written to Cache',
    output: 'Output',
    reasoning: 'Thinking (in Output)',
  },
  /**
   * The limit's state. Waiting is a task re-attached after a restart whose spend so far is not known: it waits for the
   * person to give a limit that counts from now (ADR 0062), so the word says what the person must do.
   */
  limit: {
    reached: 'At Its Limit',
    waiting: 'Needs a New Limit',
  },
  assistant: "Your requests to the assistant on a hosted model aren't counted here.",
} as const;

/** Whose prices, as the end of a sentence. */
const PRICED_BY: Record<UsagePrices, string> = {
  list: 'its list prices',
  managed: "your organisation's rates",
  catalog: 'a public price list',
};

const whole = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
const dollars = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const small = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  maximumSignificantDigits: 2,
});

const word = (value: string): UsageFigure => ({ value, notes: [], known: false });

/** Money as the page says it: a sum above zero but under a cent keeps its first figures, never $0.00. */
function money(usd: number): string {
  return usd > 0 && usd < 0.01 ? small.format(usd) : dollars.format(usd);
}

type PartName = keyof typeof USAGE_WORDS.parts;
const PART_NAMES = Object.keys(USAGE_WORDS.parts) as PartName[];
/** The parts that never overlap; `reasoning` is inside `output` and never added again. */
const ADDED: readonly PartName[] = ['input', 'cache_read', 'cache_write', 'output'];

/** A sum known to be at least `sum`; `partial` where some of what it adds did not give it. */
interface AtLeast {
  readonly sum: number | null;
  readonly partial: boolean;
}

/**
 * What several sums add up to, each part and the estimate on their own: a part or an estimate one of them left out
 * makes that sum "at least", never unknown, so a total never reads less than what it adds up (the page's one
 * arithmetic: whole numbers already counted by the domain's own rule, added).
 */
interface Combined {
  readonly parts: Readonly<Record<PartName, AtLeast>>;
  readonly tokens: AtLeast;
  readonly turns: number;
  readonly turnsEnded: number;
  /** Null where none gave one; `mixed` where they are by different prices, which are never added. */
  readonly cost: (AtLeast & { readonly prices: UsagePrices }) | 'mixed' | null;
  /** Some of what it should add has no figure at all: a task that ran and reported nothing. */
  readonly tasksMissing?: boolean;
}

function addUp(values: readonly (number | null)[]): AtLeast {
  const given = values.filter((value): value is number => value !== null);
  return {
    sum: given.length === 0 ? null : given.reduce((sum, value) => sum + value, 0),
    partial: given.length > 0 && given.length < values.length,
  };
}

function combine(usages: readonly RunUsage[]): Combined {
  const parts = Object.fromEntries(
    PART_NAMES.map((part) => [part, addUp(usages.map((usage) => usage.tokens[part]))]),
  ) as Record<PartName, AtLeast>;
  const added = ADDED.map((part) => parts[part]);
  const known = added.filter((part) => part.sum !== null);
  const costs = usages.map((usage) => usage.cost);
  const given = costs.filter((cost) => cost !== null);
  const kinds = new Set(given.map((cost) => cost.prices));
  const [prices] = kinds;
  return {
    parts,
    tokens: {
      sum: known.length === 0 ? null : known.reduce((sum, part) => sum + (part.sum ?? 0), 0),
      partial:
        known.length > 0 && (known.length < added.length || known.some((part) => part.partial)),
    },
    turns: usages.reduce((sum, usage) => sum + usage.turns, 0),
    turnsEnded: usages.reduce((sum, usage) => sum + usage.turns_ended, 0),
    cost:
      prices === undefined
        ? null
        : kinds.size > 1
          ? 'mixed'
          : {
              ...addUp(costs.map((cost) => cost?.estimated_usd ?? null)),
              prices,
            },
  };
}

/** The tokens figure: "At least this much" first where the sum is short of the whole, then each reason. */
function tokensFigure(sum: Combined): UsageFigure {
  const reasons: string[] = [];
  if (sum.tasksMissing === true) reasons.push(USAGE_WORDS.tasksMissing);
  if (sum.tokens.partial) reasons.push(USAGE_WORDS.partsMissing);
  if (sum.turns < sum.turnsEnded) reasons.push(USAGE_WORDS.turnsMissing(sum.turns, sum.turnsEnded));
  const notes = reasons.length === 0 ? [] : [USAGE_WORDS.atLeast, ...reasons];
  if (sum.tokens.sum === null) return { value: USAGE_WORDS.notKnown, notes: [], known: false };
  return { value: whole.format(sum.tokens.sum), notes, known: true };
}

/**
 * The cost figure: the app's own estimate, "At least this much" where it leaves out an estimate or a turn, whose
 * prices said under it where asked; Not Known where there is none, or where they are by different prices.
 */
function costFigure(sum: Combined, withPrices: boolean): UsageFigure {
  if (sum.cost === 'mixed') return { ...word(USAGE_WORDS.notKnown), notes: [USAGE_WORDS.mixed] };
  if (sum.cost === null || sum.cost.sum === null) return word(USAGE_WORDS.notKnown);
  const notes: string[] = [];
  if (sum.cost.partial || sum.turns < sum.turnsEnded || sum.tasksMissing === true)
    notes.push(USAGE_WORDS.atLeast);
  if (withPrices) notes.push(USAGE_WORDS.prices[sum.cost.prices]);
  return { value: money(sum.cost.sum), notes, known: true };
}

function limitFigure(cap: SpendingCapView): UsageFigure {
  return {
    value: money(cap.limit_usd),
    notes: cap.state === 'open' ? [] : [USAGE_WORDS.limit[cap.state]],
    known: true,
  };
}

/** Each kind of token; every one "At least this much" where turns went unreported, the left-out ones where not. */
function breakdown(sum: Combined): UsagePart[] {
  const short = sum.turns < sum.turnsEnded || sum.tasksMissing === true;
  return PART_NAMES.map((part) => {
    const { sum: count, partial } = sum.parts[part];
    return {
      label: USAGE_WORDS.parts[part],
      value: count === null ? USAGE_WORDS.notGiven : whole.format(count),
      note: count !== null && (partial || short) ? USAGE_WORDS.atLeast : null,
    };
  });
}

/** Both figures for some sums, or the one word for why there is none: not reported, or not counted by its apps. */
function figures(
  usages: readonly RunUsage[],
  counts: boolean,
  withPrices: boolean,
  sum: Combined = combine(usages),
): { tokens: UsageFigure; cost: UsageFigure } {
  if (usages.length === 0) {
    const why = word(counts ? USAGE_WORDS.notReported : USAGE_WORDS.notCounted);
    return { tokens: why, cost: why };
  }
  return { tokens: tokensFigure(sum), cost: costFigure(sum, withPrices) };
}

const newest = (tasks: readonly WorkstreamView[]) =>
  tasks.reduce((latest, task) => (task.updated_at > latest ? task.updated_at : latest), '');

/** The page's whole state, read fresh at each poll. */
export function usageState(sources: UsageSources): UsageState {
  const runtimes = new Map<string, RuntimeDescriptor>(
    sources.runtimes().map((runtime) => [runtime.runtime_id, runtime]),
  );
  const reports = (runtimeId: string | null) =>
    runtimeId !== null && runtimes.get(runtimeId)?.reports_usage === true;
  const noRuns = { tokens: word(USAGE_WORDS.noRuns), cost: word(USAGE_WORDS.noRuns) };
  const cards: { card: UsageProjectCard; newest: string }[] = [];
  for (const project of sources.projects()) {
    const tasks = [...sources.tasks(project.project_id)].sort((a, b) =>
      b.updated_at.localeCompare(a.updated_at),
    );
    if (tasks.length === 0) continue;
    const latest = tasks.map((task) => sources.latest(task));
    const taskUsage = tasks.map((task) => sources.taskUsage(task.workstream_id));
    const rows = tasks.map((task, index): UsageTaskRow => {
      const { runtimeId, limit } = latest[index] ?? { runtimeId: null, limit: null };
      const used = taskUsage[index] ?? null;
      return {
        workstream_id: task.workstream_id,
        title: task.title,
        app: runtimeId === null ? null : (runtimes.get(runtimeId)?.display_name ?? null),
        // Whose prices is said once, under the table, never on every row.
        ...(runtimeId === null
          ? noRuns
          : figures(used === null ? [] : [used], reports(runtimeId), false)),
        limit: limit === null ? null : limitFigure(limit),
      };
    });
    // The project's figures are its tasks' added up, so the card never reads less than its rows. Its own sum is
    // read only where no task's is (the domain's rule makes a part one task left out unknown for the whole project).
    const reported = taskUsage.filter((each): each is RunUsage => each !== null);
    const own = project.usage ?? null;
    const usages = reported.length > 0 ? reported : own === null ? [] : [own];
    const kinds = [
      ...new Set(reported.flatMap((each) => (each.cost === null ? [] : [each.cost.prices]))),
    ].sort();
    // A task that ran and has no figure is not in the sum, so the sum is "at least" and says why; the turns its runs
    // ended still count, from the domain's own count of every ended turn of the project's runs.
    const silent = taskUsage.some(
      (each, index) => each === null && (latest[index]?.runtimeId ?? null) !== null,
    );
    const added = combine(usages);
    const projectSum: Combined = {
      ...added,
      turnsEnded: Math.max(added.turnsEnded, own?.turns_ended ?? 0),
      tasksMissing: silent && reported.length > 0,
    };
    const shown = latest.every((each) => each.runtimeId === null)
      ? noRuns
      : figures(
          usages,
          latest.some((each) => reports(each.runtimeId)),
          true,
          projectSum,
        );
    cards.push({
      newest: newest(tasks),
      card: {
        project_id: project.project_id,
        name: project.name,
        ...shown,
        breakdown: usages.length === 0 ? null : breakdown(projectSum),
        tasks: rows,
        limits: rows.some((row) => row.limit !== null),
        // Whose prices is said once: under the project's cost where it has one, else under the table.
        estimates: kinds.length === 0 || shown.cost.known ? null : USAGE_WORDS.estimates(kinds),
      },
    });
  }
  cards.sort((a, b) => b.newest.localeCompare(a.newest));
  return {
    projects: cards.map((each) => each.card),
    assistant: sources.assistantHosted() ? USAGE_WORDS.assistant : null,
  };
}

/** What the page reads of the host's projection: only what a person may read, so a project taken off the list is not on it. */
export interface UsageProjection {
  listedProjects(): readonly ProjectView[];
  listedWorkstreams(projectId?: string): readonly WorkstreamView[];
  workstreamUsage(workstreamId: string): RunUsage | null;
  execution(executionId: string): ExecutionView | undefined;
  assistantModel(): unknown;
}

/** The page's reads, from the projection and the host's runtimes. */
export function projectionSources(
  projection: UsageProjection,
  runtimes: () => readonly RuntimeDescriptor[],
): UsageSources {
  return {
    projects: () => projection.listedProjects(),
    tasks: (projectId) => projection.listedWorkstreams(projectId),
    taskUsage: (workstreamId) => projection.workstreamUsage(workstreamId),
    latest: (task) => {
      const id = task.current_execution_id ?? task.execution_ids.at(-1) ?? null;
      const run = id === null ? undefined : projection.execution(id);
      return { runtimeId: run?.runtime.runtime_id ?? null, limit: run?.spending_cap ?? null };
    },
    runtimes,
    assistantHosted: () => projection.assistantModel() != null,
  };
}

/** The page's own files. */
function asset(name: string): Buffer {
  return readFileSync(new URL(`./usage-page/${name}`, import.meta.url));
}

/**
 * The page on the pairing page's server, beside setup and under its paths, behind the same session and origin gate.
 * It only reads: no POST, nothing it can change.
 */
export function mountUsage(sources: UsageSources): (routes: PageRoutes) => void {
  return (routes) => {
    routes.asset('/setup/usage', 'text/html; charset=utf-8', asset('usage.html'));
    routes.asset('/setup/usage.js', 'text/javascript; charset=utf-8', asset('usage.js'));
    routes.asset('/setup/usage.css', 'text/css; charset=utf-8', asset('usage.css'));
    routes.get('/setup/usage/state', () => usageState(sources));
  };
}

/** Several views mounted on the one page server, each adding its own paths. */
export function alongside(
  ...mounts: readonly ((routes: PageRoutes) => void)[]
): (routes: PageRoutes) => void {
  return (routes) => {
    for (const mount of mounts) mount(routes);
  };
}
