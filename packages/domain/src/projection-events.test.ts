// Every shape of contract-valid event, in random orders and scopes, folds without stopping the projection (#11).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { EVENT_VARIANTS, type EventEnvelope, parseEventEnvelope } from '@halcyonic/contracts';
import { Projection } from './projection.ts';

type Schema = { readonly [key: string]: unknown };
type Random = () => number;

/** mulberry32: small and seeded, so a failure names the seed that brings it back. */
function seeded(seed: number): Random {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const pick = <T>(random: Random, items: readonly T[]): T => {
  const item = items[Math.floor(random() * items.length)];
  if (item === undefined) throw new Error('nothing to pick from');
  return item;
};
const between = (random: Random, low: number, high: number) =>
  low + Math.floor(random() * (high - low + 1));
const hex = (random: Random, count: number) =>
  Array.from({ length: count }, () => '0123456789abcdef'[between(random, 0, 15)]).join('');
const v7 = (random: Random) =>
  `${hex(random, 8)}-${hex(random, 4)}-7${hex(random, 3)}-${pick(random, ['8', '9', 'a', 'b'])}${hex(random, 3)}-${hex(random, 12)}`;

/** A few tasks, so events meet what earlier ones made far more often than chance would. */
function makePools(random: Random) {
  const projects = Array.from({ length: 2 }, () => v7(random));
  const workstreams = Array.from({ length: 3 }, (_, index) => ({
    id: v7(random),
    project: projects[index % projects.length] as string,
  }));
  const executions = Array.from({ length: 5 }, (_, index) => {
    const workstream = workstreams[index % workstreams.length] as { id: string; project: string };
    return { id: v7(random), workstream: workstream.id, project: workstream.project };
  });
  return {
    projects,
    workstreams,
    executions,
    commands: Array.from({ length: 6 }, () => uuid(random)),
  };
}
type Pools = ReturnType<typeof makePools>;
const uuid = (random: Random) =>
  `${hex(random, 8)}-${hex(random, 4)}-${hex(random, 4)}-${hex(random, 4)}-${hex(random, 12)}`;

const WORDS = [
  'Fix',
  'the',
  'waves',
  'tests',
  'pass',
  'npm test',
  'src/index.ts',
  'Ünïcode',
  '日本',
  'a/b',
  '..',
  '$HOME',
];

/** Strings for the patterns the contracts use; each is checked against its pattern before use. */
function forPattern(random: Random, pattern: string, pools: Pools): string | undefined {
  const candidates: (() => string)[] = [
    () =>
      pick(random, [
        ...pools.projects,
        ...pools.workstreams.map((workstream) => workstream.id),
        ...pools.executions.map((execution) => execution.id),
      ]),
    () => v7(random),
    () => uuid(random),
    () => pick(random, pools.commands),
    () =>
      new Date(
        Date.UTC(
          2026,
          10,
          3,
          12,
          between(random, 0, 59),
          between(random, 0, 59),
          between(random, 0, 999),
        ),
      ).toISOString(),
    () => pick(random, ['mock', 'opencode', 'codex', 'claude-code', 'ollama-local']),
    () =>
      pick(random, ['not_found', 'invalid_state', 'runtime_error', 'control_plane_restarted', 'x']),
    () => hex(random, 64),
    () => hex(random, 40),
    () => pick(random, ['catalog-1', 'abc', 'A-1']),
    () => String(between(random, 10_000_000, 99_999_999)),
    () => 'QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE=',
    () => pick(random, ['src/index.ts', 'notes.md', 'a/b/c.txt']),
    () => pick(random, WORDS),
  ];
  const expression = new RegExp(pattern, 'u');
  for (let tries = 0; tries < 40; tries += 1) {
    const candidate = pick(random, candidates)();
    if (expression.test(candidate)) return candidate;
  }
  for (const make of candidates) {
    const candidate = make();
    if (expression.test(candidate)) return candidate;
  }
  return undefined;
}

class Unsupported extends Error {}

function value(random: Random, schema: Schema, pools: Pools, depth: number): unknown {
  if (depth > 12) throw new Unsupported('too deep');
  if ('const' in schema) return schema.const;
  if (Array.isArray(schema.anyOf))
    return value(random, pick(random, schema.anyOf as Schema[]), pools, depth + 1);
  if (Array.isArray(schema.oneOf))
    return value(random, pick(random, schema.oneOf as Schema[]), pools, depth + 1);
  if (Array.isArray(schema.enum)) return pick(random, schema.enum as unknown[]);
  switch (schema.type) {
    case 'null':
      return null;
    case 'boolean':
      return random() < 0.5;
    case 'integer':
    case 'number': {
      const low = typeof schema.minimum === 'number' ? schema.minimum : 0;
      const high = typeof schema.maximum === 'number' ? schema.maximum : low + 1000;
      return between(random, low, Math.min(high, low + 1_000_000));
    }
    case 'string': {
      const min = typeof schema.minLength === 'number' ? schema.minLength : 0;
      const max = typeof schema.maxLength === 'number' ? schema.maxLength : 40;
      if (typeof schema.pattern === 'string' && schema.pattern !== '\\S') {
        const made = forPattern(random, schema.pattern, pools);
        if (made === undefined) throw new Unsupported(`pattern ${schema.pattern}`);
        return made;
      }
      // Text: words, sometimes long, always something other than space.
      const length = random() < 0.1 ? max : between(random, Math.max(min, 1), Math.min(max, 60));
      let text = '';
      while (text.length < length) text += `${pick(random, WORDS)} `;
      text = text.slice(0, length).trimEnd();
      return text.length >= Math.max(min, 1) ? text : 'x'.repeat(Math.max(min, 1));
    }
    case 'array': {
      const min = typeof schema.minItems === 'number' ? schema.minItems : 0;
      const max = typeof schema.maxItems === 'number' ? schema.maxItems : 3;
      const count = between(random, min, Math.min(max, min + 3));
      return Array.from({ length: count }, () =>
        value(random, (schema.items ?? {}) as Schema, pools, depth + 1),
      );
    }
    case 'object': {
      const properties = (schema.properties ?? {}) as Record<string, Schema>;
      const required = new Set((schema.required ?? []) as string[]);
      const result: Record<string, unknown> = {};
      for (const [key, property] of Object.entries(properties)) {
        if (!required.has(key) && random() < 0.5) continue;
        result[key] = value(random, property, pools, depth + 1);
      }
      return result;
    }
    default:
      // Any JSON (a start's options, say): a small object.
      return { option: pick(random, WORDS) };
  }
}

/** A contract-valid event of the variant, its scope drawn from the pools, or null if none came after many tries. */
function eventOf(random: Random, variant: Schema, pools: Pools): EventEnvelope | null {
  for (let tries = 0; tries < 60; tries += 1) {
    let candidate: Record<string, unknown>;
    try {
      candidate = value(random, variant, pools, 0) as Record<string, unknown>;
    } catch (error) {
      if (error instanceof Unsupported) return null;
      throw error;
    }
    // Scopes that hang together most of the time, so events meet what earlier ones made.
    const execution = pick(random, pools.executions);
    if (
      candidate.execution_id !== null &&
      candidate.execution_id !== undefined &&
      random() < 0.85
    ) {
      candidate.execution_id = execution.id;
      candidate.workstream_id = execution.workstream;
      candidate.project_id = execution.project;
    } else if (
      candidate.workstream_id !== null &&
      candidate.workstream_id !== undefined &&
      random() < 0.85
    ) {
      const workstream = pick(random, pools.workstreams);
      candidate.workstream_id = workstream.id;
      candidate.project_id = workstream.project;
    }
    const parsed = parseEventEnvelope(candidate);
    if (parsed.ok) return parsed.value;
  }
  return null;
}

/** An event of `type` for the given scope, or null if none came. */
function scoped(
  random: Random,
  type: string,
  scope: { project_id: string | null; workstream_id: string | null; execution_id: string | null },
  shared: Pools,
): EventEnvelope | null {
  const variant = (EVENT_VARIANTS as unknown as Schema[]).find(
    (schema) => (schema.properties as Record<string, Schema>).event_type?.const === type,
  );
  if (variant === undefined) throw new Error(`no variant ${type}`);
  for (let tries = 0; tries < 60; tries += 1) {
    const candidate = value(random, variant, shared, 0) as Record<string, unknown>;
    Object.assign(candidate, scope);
    const parsed = parseEventEnvelope(candidate);
    if (parsed.ok) return parsed.value;
  }
  return null;
}

/** What a client could be told: every view the projection gives, as JSON. */
function views(projection: Projection): string {
  return JSON.stringify({
    projects: projection.projects(),
    workstreams: projection.workstreams(),
    executions: projection.executions(),
    commands: projection.commands(50),
    requests: projection.workspaceRequests(),
    devices: projection.devices(),
  });
}

test('no order of contract-valid events stops the projection, and the same events give the same views (#11)', () => {
  const generated = new Set<string>();
  let applied = 0;
  let quiet = 0;
  for (let seed = 1; seed <= 30; seed += 1) {
    const random = seeded(seed);
    const shared = makePools(random);
    const events: EventEnvelope[] = [];
    // First the projects, tasks and executions the rest refers to, then anything in any order.
    for (const project of shared.projects)
      events.push(
        scoped(
          random,
          'project.created',
          { project_id: project, workstream_id: null, execution_id: null },
          shared,
        ) as EventEnvelope,
      );
    for (const workstream of shared.workstreams)
      events.push(
        scoped(
          random,
          'workstream.created',
          { project_id: workstream.project, workstream_id: workstream.id, execution_id: null },
          shared,
        ) as EventEnvelope,
      );
    for (const execution of shared.executions)
      events.push(
        scoped(
          random,
          'execution.created',
          {
            project_id: execution.project,
            workstream_id: execution.workstream,
            execution_id: execution.id,
          },
          shared,
        ) as EventEnvelope,
      );
    const variants = EVENT_VARIANTS as unknown as Schema[];
    while (events.length < 600) {
      const event = eventOf(random, pick(random, variants), shared);
      if (event !== null) events.push(event);
    }
    const projection = new Projection();
    events.forEach((event, index) => {
      generated.add(event.event_type);
      let result: ReturnType<Projection['apply']>;
      try {
        result = projection.apply({ position: index + 1, event });
      } catch (error) {
        assert.fail(
          `seed ${seed}, position ${index + 1}, ${event.event_type}: ${(error as Error).stack}`,
        );
      }
      applied += 1;
      if (result.notes.length === 0) quiet += 1;
    });
    // A pure fold: the same journal folded again gives every view the same.
    const again = new Projection();
    for (const [index, event] of events.entries()) again.apply({ position: index + 1, event });
    assert.equal(views(again), views(projection), `seed ${seed}`);
  }
  assert.equal(generated.size, EVENT_VARIANTS.length, 'every event type was generated');
  // Many events meet what earlier ones made, so the fold is tried beyond its notes of the unknown.
  assert.ok(quiet * 3 >= applied, `${quiet} of ${applied} events applied without a note`);
});
