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

/**
 * What a hosted model costs the person, in the host's words, read by the list's rows and the one review alike (the
 * lead's ruling on 1111-b123, 2026-11-13: a person who has never heard of an API key knows before the press that it
 * costs money). The provider is named by its declaration. The price is not known here, so none is guessed.
 */
export const HOSTED_COST = {
  detail: (provider: string) => `On ${provider}, Paid by You`,
  /** The line names the model where it fits its contract (200 characters); otherwise "this model", and the review names it. */
  whatHappens: (provider: string, model: string | null) =>
    `From now on the assistant answers on ${model ?? 'this model'}, and each request costs money on your own ${provider} account.`,
  // Short enough to read at a glance at the larger text size (the owner's report, 2026-11-13: the review was tall and
  // said the cost in one long paragraph). Who pays is said once, in the line; each warning is one short fact. What
  // leaves this computer comes first among them, so a person reading for the cost never misses it (AUDIT2, 2026-11-14).
  warnings: (provider: string) => [
    {
      code: 'sent_to_provider',
      line: `Your requests and what it reads in your projects leave this computer for ${provider}`,
    },
    { code: 'price_unknown', line: `${provider} sets each request's price; it is not shown here` },
    { code: 'free_here', line: "To stop paying, choose This Computer's Model, which is free" },
  ],
  /** This computer's own model, the one nobody charges for. */
  localWhatHappens:
    "From now on the assistant answers on this computer's model, which is free, and nothing it reads leaves this computer.",
} as const;

/** The most a review's line may hold (ActionReview's what_happens). */
const LINE_MOST = 200;

/** The hosted review's line and facts: the model named in the line where it fits, as a fact only where it does not. */
export function hostedReview(
  provider: string,
  model: string,
): { what_happens: string; facts: { kind: 'words'; value: string; theirs: true }[] } {
  const named = HOSTED_COST.whatHappens(provider, model);
  return [...named].length <= LINE_MOST
    ? { what_happens: named, facts: [] }
    : {
        what_happens: HOSTED_COST.whatHappens(provider, null),
        facts: [{ kind: 'words', value: model, theirs: true }],
      };
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
          detail: 'message' in key ? key.message : HOSTED_COST.detail(hosted.provider.name),
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
