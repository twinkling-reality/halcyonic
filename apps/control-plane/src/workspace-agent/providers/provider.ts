import type { ToolDefinition, ToolMessage } from '../ollama.ts';

/**
 * A hosted model the assistant may run on, as its provider declares it: which protocol its requests
 * speak, where they go, which key they carry and how, and which models it offers. Nothing outside a
 * provider's own folder names a vendor; a second provider is a second declaration, not new code.
 */
export interface HostedProvider {
  /** Opaque, stable, lowercase: the provider's id in settings and evidence. */
  readonly id: string;
  /** The name the person sees for where the assistant runs, as the provider declares it. */
  readonly name: string;
  /** A protocol the host implements (see PROTOCOLS); the provider never brings code of its own. */
  readonly protocol: string;
  /** Where requests go: https, or loopback for proofs only (`upstreamProblem`). */
  readonly upstream: string;
  readonly key: ProviderKey;
  /** The model ids the provider offers; the first is used unless the person names another. */
  readonly models: readonly string[];
}

/**
 * The key a provider's requests carry, declared the way an agent app declares its key (LINK's Add
 * Key): the environment variable an agent app may read (never the assistant: `declaredKeys`), the
 * private file in the data folder it is kept in, and the request header it travels in.
 */
export interface ProviderKey {
  readonly variable: string;
  readonly file: string;
  readonly header: { readonly name: string; readonly scheme: string | null };
}

/** What the host can see of a declared key: never its value outside `stored`. */
export type KeyState =
  | { readonly kind: 'stored'; readonly value: string }
  | { readonly kind: 'none' }
  | { readonly kind: 'unusable'; readonly code: string; readonly message: string };
export type KeyReader = (key: ProviderKey) => KeyState;

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

/** Why `upstream` may not carry a key, or null when it may: https anywhere, http only to this Mac. */
export function upstreamProblem(upstream: string): string | null {
  let url: URL;
  try {
    url = new URL(upstream);
  } catch {
    return 'The provider address is not a web address.';
  }
  if (url.username || url.password || url.search || url.hash)
    return 'The provider address carries more than a place.';
  if (url.protocol === 'https:') return null;
  const loopback = ['127.0.0.1', '[::1]', 'localhost'].includes(url.hostname);
  if (url.protocol === 'http:' && loopback) return null;
  return 'A key travels only over https, or to this Mac.';
}

/** The words every protocol says for a failure it cannot name more closely. */
export const KEY_REFUSED = {
  code: 'key_refused',
  message: 'The provider refused the stored key.',
} as const;
