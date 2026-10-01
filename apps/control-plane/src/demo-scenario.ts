import type {
  CommandEnvelope,
  CommandView,
  RuntimeDescriptor,
  RuntimeModel,
  RuntimeModelsResponse,
} from '@halcyonic/contracts';
import type { RealtimeClient } from './client/realtime-client.ts';
import { type createCommandFactory, type DemoWorkstream, MOCK_RUNTIME_ID } from './demo-plan.ts';

/** A scenario's name as the mock runtime's fixtures are named: `question_asked`, `approval_required`. */
const SCENARIO_NAME = /^[a-z][a-z0-9_]{0,63}$/;

export type DemoArguments = { readonly scenario: string | null } | { readonly error: string };

/** `pnpm demo` plays the demo plan; `pnpm demo --scenario <name>` starts one scenario and leaves it waiting. */
export function parseDemoArguments(argv: readonly string[]): DemoArguments {
  if (argv.length === 0) return { scenario: null };
  const [flag, name, ...rest] = argv;
  if (flag !== '--scenario' || rest.length > 0)
    return { error: 'usage: pnpm demo [--scenario <name>]' };
  if (name === undefined || !SCENARIO_NAME.test(name)) {
    return { error: 'a scenario is named like question_asked or approval_required' };
  }
  return { scenario: name };
}

/** One workstream that plays a scenario, titled so the headset says what it is for. */
export function scenarioWorkstream(name: string): DemoWorkstream {
  return {
    title: `Try the ${name.replaceAll('_', ' ')} scenario`,
    objective: `A device check: the mock runtime plays its ${name} scenario.`,
    instruction: `Play the ${name} scenario.`,
    scenario: name,
  };
}

/**
 * The model to start the mock with when it lists models, as the control plane requires
 * (`model_required`): one on this Mac that declares tool calling, else any on this Mac, else the
 * first listed. Null for a runtime that chooses its own.
 */
export function chooseModel(
  runtime: RuntimeDescriptor,
  models: readonly RuntimeModel[],
): string | null {
  if (runtime.model_choice !== 'listed') return null;
  const local = models.filter((model) => model.served === 'this_mac');
  return (
    (local.find((model) => model.tool_calling === 'declared') ?? local[0] ?? models[0])
      ?.model_ref ?? null
  );
}

/** Reads the models a listing runtime offers, through the control plane's REST API. */
export async function listModels(
  baseUrl: string,
  token: string,
  runtimeId: string,
): Promise<RuntimeModel[]> {
  const response = await fetch(`${baseUrl}/api/runtimes/${encodeURIComponent(runtimeId)}/models`, {
    headers: { authorization: `Bearer ${token}` },
  });
  if (!response.ok)
    throw new Error(`listing ${runtimeId}'s models answered HTTP ${response.status}`);
  const body = (await response.json()) as RuntimeModelsResponse;
  if (body.result.availability !== 'available') throw new Error(`${runtimeId} lists no models now`);
  return body.result.models;
}

/** Sends a command and waits for its acknowledgement; throws, in words, unless it was accepted. */
export async function submit(
  client: RealtimeClient,
  command: CommandEnvelope,
): Promise<CommandView> {
  client.command(command);
  const ack = await client.waitFor(
    (message) => message.type === 'command_ack' && message.command_id === command.command_id,
  );
  if (ack.type !== 'command_ack' || ack.disposition !== 'accepted' || ack.command === null) {
    const reason =
      ack.type === 'command_ack'
        ? (ack.command?.rejection?.message ?? ack.disposition)
        : 'no acknowledgement';
    throw new Error(`${command.command_type} was not accepted: ${reason}`);
  }
  return ack.command;
}

/**
 * Starts one scenario on the mock runtime in a project of its own, and returns once the start is
 * accepted. Nothing answers for the person: an approval or a question the scenario asks waits for
 * them, as on the headset.
 */
export async function startScenario(options: {
  readonly client: RealtimeClient;
  readonly commands: ReturnType<typeof createCommandFactory>;
  readonly name: string;
  readonly mock: RuntimeDescriptor;
  readonly models: readonly RuntimeModel[];
}): Promise<{ readonly workstreamId: string }> {
  const { client, commands, name, mock, models } = options;
  const workstream = scenarioWorkstream(name);
  const project = await submit(client, commands.createProject(`Scenario: ${name}`));
  if (project.result?.kind !== 'project_created') throw new Error('the project was not created');
  const created = await submit(
    client,
    commands.createWorkstream(project.result.project_id, workstream),
  );
  if (created.result?.kind !== 'workstream_created')
    throw new Error('the workstream was not created');
  const start = commands.startExecution(created.result.workstream_id, workstream, MOCK_RUNTIME_ID);
  await submit(client, {
    ...start,
    payload: { ...start.payload, model_ref: chooseModel(mock, models) },
  });
  return { workstreamId: created.result.workstream_id };
}
