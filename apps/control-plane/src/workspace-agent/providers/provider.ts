import type { HostedProvider, KeyState, ProviderKey } from '@halcyonic/runtime-core';
import type { ToolDefinition, ToolMessage } from '../ollama.ts';

export { upstreamProblem } from '@halcyonic/runtime-core';

/**
 * How the assistant reads a declared provider's key now (`declaredKeys`): the file only, never the environment.
 * Its value leaves only inside `stored`.
 */
export type KeyLookup = (key: ProviderKey) => KeyState;

/** One model turn as a protocol hands it back, before the host's own checks of every call. */
export type ProtocolReply =
  | {
      readonly kind: 'reply';
      readonly content: string;
      readonly calls: readonly {
        readonly id: string | null;
        readonly name: string;
        readonly arguments: unknown;
      }[];
      /** The model the response says served it, which may differ from the one asked for. */
      readonly model: string;
      /** The tokens the provider says the turn read and wrote, as its reply gives them; null where it gives none. */
      readonly usage: TurnUsage | null;
    }
  | {
      readonly kind: 'failed';
      readonly code: string;
      readonly message: string;
      /** The provider's counts where its reply came and gave them, since a failed turn is billed too. */
      readonly usage?: TurnUsage | null;
    };

/** A provider's own count of one turn's tokens: what was read (cached reads among them, where it says) and written. */
export interface TurnUsage {
  readonly input_tokens: number;
  readonly output_tokens: number;
  readonly cached_input_tokens: number | null;
}

/** A count only if it is one: a whole number, not negative; null otherwise, never a guess. */
export const count = (value: unknown): number | null =>
  Number.isSafeInteger(value) && (value as number) >= 0 ? (value as number) : null;

export interface ProtocolCall {
  readonly provider: HostedProvider;
  readonly upstream: URL;
  readonly key: string;
  readonly model: string;
  readonly outputTokens: number;
  readonly timeoutMs: number;
  readonly signal: AbortSignal;
}

/** A wire shape the host speaks to hosted providers. */
export interface Protocol {
  /** A metadata read only: whether the key is taken and the model offered. Never inference. */
  check(call: ProtocolCall): Promise<{ readonly ok: true } | ProtocolFailure>;
  turn(
    call: ProtocolCall,
    messages: readonly ToolMessage[],
    tools: readonly ToolDefinition[],
  ): Promise<ProtocolReply>;
}
export type ProtocolFailure = {
  readonly ok: false;
  readonly code: string;
  readonly message: string;
};

/** The words every protocol says for a failure it cannot name more closely. */
export const KEY_REFUSED = {
  code: 'key_refused',
  message: 'The provider refused the stored key.',
} as const;
