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
    }
  | { readonly kind: 'failed'; readonly code: string; readonly message: string };

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
