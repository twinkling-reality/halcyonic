import { readdirSync, readFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import {
  ApprovalSubject,
  compileValidator,
  ErrorInfo,
  NativeId,
  RUNTIME_EVENT_PAYLOADS,
  Text,
} from '@halcyonic/contracts';
import Type, { type Static } from 'typebox';

const strict = { additionalProperties: false } as const;
const Delay = Type.Integer({ minimum: 0, maximum: 600_000 });

function emission<const T extends keyof typeof RUNTIME_EVENT_PAYLOADS>(type: T) {
  return Type.Object({ type: Type.Literal(type), payload: RUNTIME_EVENT_PAYLOADS[type] }, strict);
}

/**
 * What a scenario may describe happening inside a turn. The mock itself emits the execution
 * start, each turn start, approval requests and resolutions, and connection loss, so scenarios
 * cannot contradict the mock's own lifecycle. Turn endings omit `turn_id`; the mock fills it in.
 */
const Emission = Type.Union([
  Type.Object(
    { type: Type.Literal('runtime.turn.completed'), payload: Type.Object({}, strict) },
    strict,
  ),
  Type.Object(
    {
      type: Type.Literal('runtime.turn.failed'),
      payload: Type.Object({ error: ErrorInfo }, strict),
    },
    strict,
  ),
  emission('runtime.agent_message'),
  emission('runtime.tool.started'),
  emission('runtime.tool.completed'),
  emission('runtime.test_run.started'),
  emission('runtime.test_run.completed'),
]);

const EmitStep = Type.Object({ after_ms: Delay, emit: Emission }, strict);
const DisconnectStep = Type.Object(
  { after_ms: Delay, disconnect: Type.Object({ reason: Text(2000) }, strict) },
  strict,
);
const BranchStep = Type.Union([EmitStep, DisconnectStep]);
const ApprovalStep = Type.Object(
  {
    after_ms: Delay,
    await_approval: Type.Object(
      {
        approval_id: NativeId,
        subject: ApprovalSubject,
        if_approved: Type.Array(BranchStep),
        if_denied: Type.Array(BranchStep),
      },
      strict,
    ),
  },
  strict,
);

export const ScenarioStep = Type.Union([EmitStep, DisconnectStep, ApprovalStep]);
export type ScenarioStep = Static<typeof ScenarioStep>;
export type BranchStep = Static<typeof BranchStep>;

/** A scripted first turn for the mock runtime, stored as JSON under fixtures/scenarios. */
export const Scenario = Type.Object(
  {
    format: Type.Literal(1),
    id: Type.String({ pattern: '^[a-z][a-z0-9_]{0,63}$' }),
    description: Text(500),
    steps: Type.Array(ScenarioStep, { minItems: 1 }),
  },
  strict,
);
export type Scenario = Static<typeof Scenario>;

const validateScenario = compileValidator(Scenario);

export class ScenarioError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ScenarioError';
  }
}

export function parseScenario(value: unknown, source: string): Scenario {
  const result = validateScenario(value);
  if (!result.ok) {
    const details = result.issues.map((issue) => `${issue.path} ${issue.message}`).join('; ');
    throw new ScenarioError(`${source}: invalid scenario: ${details}`);
  }
  return result.value;
}

/** Loads every `*.json` scenario in a directory. A scenario's id must match its file name. */
export function loadScenarios(directory: string): Map<string, Scenario> {
  const scenarios = new Map<string, Scenario>();
  const files = readdirSync(directory)
    .filter((name) => name.endsWith('.json'))
    .sort();
  for (const file of files) {
    const path = join(directory, file);
    let raw: unknown;
    try {
      raw = JSON.parse(readFileSync(path, 'utf8'));
    } catch (error) {
      throw new ScenarioError(`${path}: not valid JSON: ${(error as Error).message}`);
    }
    const scenario = parseScenario(raw, path);
    if (scenario.id !== basename(file, '.json')) {
      throw new ScenarioError(`${path}: id "${scenario.id}" must match the file name`);
    }
    scenarios.set(scenario.id, scenario);
  }
  return scenarios;
}
