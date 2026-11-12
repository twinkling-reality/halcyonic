import type { WorkspaceAgentStatus } from '@halcyonic/contracts';
import type { ToolDefinition, ToolMessage, ToolTurnResult } from './ollama.ts';
import type { HostedWorkspaceModel } from './providers/hosted-model.ts';

/** What the assistant needs of a model: requests.ts's own `Model`. */
export interface Model {
  status(): Promise<WorkspaceAgentStatus>;
  turn(
    messages: readonly ToolMessage[],
    tools: readonly ToolDefinition[],
    signal?: AbortSignal,
  ): Promise<ToolTurnResult>;
}

/**
 * The model the assistant runs on, chosen again at every status read and every turn: the first
 * declared hosted provider with a key stored, else the local model, which stays the default. A key
 * that is stored but refused or unusable keeps that provider chosen and says why; it never falls
 * back to the local model unsaid. Removing the key brings the local model back at the next turn.
 */
export class AssistantModel implements Model {
  readonly #local: Model;
  readonly #hosted: readonly HostedWorkspaceModel[];
  constructor(options: { local: Model; hosted: readonly HostedWorkspaceModel[] }) {
    this.#local = options.local;
    this.#hosted = options.hosted;
  }

  /** The model chosen now. */
  chosen(): Model {
    return this.#hosted.find((model) => model.keyStored()) ?? this.#local;
  }

  status(): Promise<WorkspaceAgentStatus> {
    return this.chosen().status();
  }

  turn(
    messages: readonly ToolMessage[],
    tools: readonly ToolDefinition[],
    signal?: AbortSignal,
  ): Promise<ToolTurnResult> {
    return this.chosen().turn(messages, tools, signal);
  }
}
