import type { AssistantHostedModel, WorkspaceAgentStatus } from '@halcyonic/contracts';
import type { AssistantModelOption } from '../core/choices.ts';
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

const unavailable = (code: string, message: string): WorkspaceAgentStatus => ({
  availability: 'unavailable',
  reason: { code, message },
});

/**
 * The model the assistant runs on: where the person chose (ADR 0030, amendment of 2026-11-12; the lead's D1), read
 * at every status read and every turn. Until they choose, this computer's model. A stored key only makes its
 * provider's models offered; it never chooses. A chosen hosted model whose key is gone, unusable or refused stays
 * chosen and says so in words (D2): it never falls back unsaid, and a refused key is never sent again.
 */
export class AssistantModel implements Model {
  readonly #local: Model;
  readonly #hosted: ReadonlyMap<string, HostedWorkspaceModel>;
  readonly #chosen: () => AssistantHostedModel | null;
  constructor(options: {
    local: Model;
    hosted: readonly HostedWorkspaceModel[];
    chosen: () => AssistantHostedModel | null;
  }) {
    this.#local = options.local;
    this.#hosted = new Map(options.hosted.map((model) => [model.provider.id, model]));
    this.#chosen = options.chosen;
  }

  status(): Promise<WorkspaceAgentStatus> {
    const chosen = this.#chosen();
    if (chosen === null) return this.#local.status();
    const hosted = this.#offering(chosen);
    if (hosted === null)
      return Promise.resolve(
        unavailable('model_not_offered', 'The model you chose is no longer offered here.'),
      );
    return hosted.status(chosen.model);
  }

  async turn(
    messages: readonly ToolMessage[],
    tools: readonly ToolDefinition[],
    signal?: AbortSignal,
  ): Promise<ToolTurnResult> {
    const chosen = this.#chosen();
    if (chosen === null) return this.#local.turn(messages, tools, signal);
    const hosted = this.#offering(chosen);
    if (hosted === null)
      return {
        kind: 'failed',
        code: 'model_not_offered',
        message: 'The model you chose is no longer offered here.',
      };
    return hosted.turn(chosen.model, messages, tools, signal);
  }

  /**
   * Where the assistant may run, for the host's list `assistant_models`: this computer's model first, then each
   * declared model of a provider whose key is stored, in the declarations' order. A provider whose key is unusable or
   * refused shows its models with the reason and offers none. A provider with no key stored is not listed.
   */
  async options(): Promise<readonly AssistantModelOption[]> {
    const local = await this.#local.status().catch(() => null);
    const rows: AssistantModelOption[] = [
      {
        model: null,
        label: "This Computer's Model",
        detail: local?.availability === 'available' ? local.model : null,
        theirs: false,
        usable: true,
      },
    ];
    for (const hosted of this.#hosted.values()) {
      const key = hosted.key();
      if (key.kind === 'none') continue;
      for (const model of hosted.provider.models)
        rows.push({
          model: { provider_id: hosted.provider.id, model },
          label: model,
          detail: 'message' in key ? key.message : `On ${hosted.provider.name}`,
          theirs: true,
          usable: key.kind === 'stored',
        });
    }
    return rows;
  }

  #offering(chosen: AssistantHostedModel): HostedWorkspaceModel | null {
    const hosted = this.#hosted.get(chosen.provider_id);
    return hosted?.provider.models.includes(chosen.model) ? hosted : null;
  }
}
