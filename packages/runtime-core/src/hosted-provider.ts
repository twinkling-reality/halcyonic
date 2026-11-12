/**
 * A hosted model provider as its own declaration states it (ADR 0061): where requests go, which wire shapes it
 * serves there, which key they carry and how, and the words a person sees. Data, never code: each provider is one
 * `providers/<id>/provider.json` the host reads as it starts, and every reader (the assistant, Add Key, the model
 * rows, the key gate, the held secrets) reads that one list. Nothing here names a vendor.
 */
export interface HostedProvider {
  /** Opaque, stable, lowercase: the declaration's folder name, the provider's id in settings and evidence. */
  readonly id: string;
  /** What the person sees for the provider, as it declares it. */
  readonly name: string;
  /** The https base address the wire shapes' paths join; http only to this Mac, for proofs (`upstreamProblem`). */
  readonly upstream: string;
  /** The wire shapes the upstream serves, in the provider's order of preference. */
  readonly speaks: readonly WireShape[];
  readonly key: ProviderKey;
  /** The model ids the provider offers; the first is the default where none is chosen. */
  readonly models: readonly string[];
  /** How a host reader asks, where the shape lets it vary; absent means each shape's defaults. */
  readonly tuning?: {
    /** In the provider's own word ('low'); each wire shape maps it. */
    readonly effort?: string;
    /** The request's output limit field ('max_completion_tokens'); a wire shape refuses one it does not know. */
    readonly outputField?: string;
  };
}

/** A wire shape by what it is, never by a vendor: the host implements each once. */
export type WireShape = 'chat_completions' | 'messages' | 'responses';

/** Every wire shape a declaration may name. */
export const WIRE_SHAPES: readonly WireShape[] = ['chat_completions', 'messages', 'responses'];

/** The person's key for a provider: one file, one field, one way it travels. */
export interface ProviderKey {
  /** The environment variable an agent app that reads a key itself reads it from (the assistant never reads one). */
  readonly variable: string;
  /** The private file in the host's data folder (mode 600 in a 700 folder): the one place it is kept. */
  readonly file: string;
  /** The caption over Add Key's field. */
  readonly field: string;
  /** The request header it travels in, with its scheme (`Bearer`), or null for the key as it is: what the gate fills. */
  readonly header: { readonly name: string; readonly scheme: string | null };
}

/** What the host can see of a declared key: never its value outside `stored`. */
export type KeyState =
  | { readonly kind: 'stored'; readonly value: string }
  | { readonly kind: 'none' }
  | { readonly kind: 'unusable'; readonly code: string; readonly message: string };

/** One reader of providers' keys, declared by the reader itself (an adapter, or the assistant), never by a provider. */
export interface KeyReader {
  /** What the person sees for the reader. */
  readonly name: string;
  /** The provider ids it reads the key of, or every declared provider sharing a shape it speaks. */
  readonly providers: 'all' | readonly string[];
  /** The wire shapes it can speak; it reaches a provider that speaks one of them. */
  readonly needs: readonly WireShape[];
  /** When it reads the key: at the host's start (a change waits for a restart) or at each use. */
  readonly reads: 'at_start' | 'at_each_use';
  /** Whether a stored key turns this reader on. */
  readonly turnsOn: boolean;
}

/** Whether `reader` reaches `provider`: named by it (or all), and sharing a wire shape. */
export function readerReaches(reader: KeyReader, provider: HostedProvider): boolean {
  const named = reader.providers === 'all' || reader.providers.includes(provider.id);
  return named && reader.needs.some((shape) => provider.speaks.includes(shape));
}

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
