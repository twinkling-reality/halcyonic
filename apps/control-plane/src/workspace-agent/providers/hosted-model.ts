import type { WorkspaceAgentStatus } from '@halcyonic/contracts';
import {
  TOOL_MODEL_BOUNDS,
  type ToolCall,
  type ToolDefinition,
  type ToolMessage,
  type ToolModelBounds,
  type ToolTurnResult,
} from '../ollama.ts';
import {
  type HostedProvider,
  type KeyReader,
  type Protocol,
  type ProtocolCall,
  upstreamProblem,
} from './provider.ts';

/**
 * A hosted model's bounds: the same input the host packs for the local model and the same reply and
 * call limits, with longer waits for a network round trip and output room for the thinking a hosted
 * model may do before it answers (its thinking tokens count toward the output it is allowed).
 */
export const HOSTED_MODEL_BOUNDS: ToolModelBounds = {
  ...TOOL_MODEL_BOUNDS,
  metadataMs: 5_000,
  totalMs: 90_000,
  outputTokens: 8_000,
};

const failure = (code: string, message: string): Extract<ToolTurnResult, { kind: 'failed' }> => ({
  kind: 'failed',
  code,
  message,
});
const unavailable = (code: string, message: string): WorkspaceAgentStatus => ({
  availability: 'unavailable',
  reason: { code, message },
});
const object = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

/**
 * The assistant on a declared hosted provider, through one of the host's protocols. Every call the
 * model returns is checked here, the same for every protocol: offered, distinct, an object of
 * arguments, within the call and reply limits. A refused key or a refusal says so; it never falls
 * back to another model.
 */
export class HostedWorkspaceModel {
  readonly provider: HostedProvider;
  readonly #protocol: Protocol;
  readonly #keys: KeyReader;
  readonly #model: string;
  readonly #upstream: string;
  readonly #bounds: ToolModelBounds;

  constructor(options: {
    provider: HostedProvider;
    protocol: Protocol;
    keys: KeyReader;
    /** One of the provider's declared models; its first when null. */
    model?: string | null;
    /** For proofs on this Mac only: `upstreamProblem` still refuses anything but https or loopback. */
    upstream?: string | null;
    bounds?: Partial<ToolModelBounds>;
  }) {
    this.provider = options.provider;
    this.#protocol = options.protocol;
    this.#keys = options.keys;
    this.#model = options.model ?? options.provider.models[0] ?? '';
    this.#upstream = options.upstream ?? options.provider.upstream;
    this.#bounds = { ...HOSTED_MODEL_BOUNDS, ...options.bounds };
  }

  /** Whether a key is stored for this provider, read now; the value never leaves this check. */
  keyStored(): boolean {
    return this.#keys(this.provider.key).kind !== 'none';
  }

  async status(): Promise<WorkspaceAgentStatus> {
    const call = this.#call(AbortSignal.timeout(this.#bounds.metadataMs), this.#bounds.metadataMs);
    if (!('upstream' in call)) return unavailable(call.code, call.message);
    try {
      const checked = await this.#protocol.check(call);
      if (!checked.ok) return unavailable(checked.code, checked.message);
    } catch {
      return unavailable('provider_unreachable', `${this.provider.name} could not be reached.`);
    }
    return {
      availability: 'available',
      model: this.#model,
      served: 'remote',
      provider: this.provider.name,
    };
  }

  async turn(
    messages: readonly ToolMessage[],
    tools: readonly ToolDefinition[],
    signal?: AbortSignal,
  ): Promise<ToolTurnResult> {
    const controller = new AbortController();
    const onCancel = () => controller.abort();
    signal?.addEventListener('abort', onCancel, { once: true });
    if (signal?.aborted) controller.abort();
    const timer = setTimeout(() => controller.abort(), this.#bounds.totalMs);
    const ended = () =>
      failure(
        signal?.aborted ? 'cancelled' : 'timeout',
        'The model request ended before it could continue.',
      );
    try {
      if (controller.signal.aborted)
        return failure('cancelled', 'The request stopped before the model replied.');
      const body = JSON.stringify({ messages, tools });
      if (Buffer.byteLength(body) > this.#bounds.maxInputBytes)
        return failure('context_limit', 'This request needs less context before it can continue.');
      // Snapshot before the first await, as the local transport does.
      const captured = JSON.parse(body) as { messages: ToolMessage[]; tools: ToolDefinition[] };
      // The key is read again at every turn, so a key removed since the last turn is not used.
      const call = this.#call(controller.signal, this.#bounds.totalMs);
      if (!('upstream' in call)) return failure(call.code, call.message);
      const reply = await this.#protocol.turn(call, captured.messages, captured.tools);
      if (controller.signal.aborted) return ended();
      if (reply.kind === 'failed') return failure(reply.code, reply.message);
      return this.#checked(reply.content, reply.calls, reply.model, captured.tools);
    } catch {
      if (controller.signal.aborted) return ended();
      // Never relay provider or transport error text: it can repeat inputs and the key.
      return failure(
        'model_response',
        `${this.provider.name} did not return a complete valid response.`,
      );
    } finally {
      clearTimeout(timer);
      signal?.removeEventListener('abort', onCancel);
      controller.abort();
    }
  }

  #call(
    signal: AbortSignal,
    timeoutMs: number,
  ): ProtocolCall | { readonly code: string; readonly message: string } {
    if (!this.provider.models.includes(this.#model))
      return {
        code: 'model_not_offered',
        message: `${this.provider.name} does not offer that model.`,
      };
    const problem = upstreamProblem(this.#upstream);
    if (problem !== null) return { code: 'provider_address', message: problem };
    const key = this.#keys(this.provider.key);
    if (key.kind === 'none')
      return { code: 'key_not_stored', message: `No key is stored for ${this.provider.name}.` };
    if (key.kind === 'unusable') return { code: key.code, message: key.message };
    return {
      provider: this.provider,
      upstream: new URL(this.#upstream),
      key: key.value,
      model: this.#model,
      outputTokens: this.#bounds.outputTokens,
      timeoutMs,
      signal,
    };
  }

  #checked(
    content: string,
    calls: readonly { id: string | null; name: string; arguments: unknown }[],
    model: string,
    tools: readonly ToolDefinition[],
  ): ToolTurnResult {
    const invalid = () =>
      failure('model_response', `${this.provider.name} did not return a complete valid response.`);
    if ([...content].length > this.#bounds.maxReplyCharacters) return invalid();
    if (calls.length > this.#bounds.maxToolCalls) return invalid();
    if (typeof model !== 'string' || model === '' || model.length > 200) return invalid();
    const offered = new Set(tools.map((tool) => tool.function.name));
    const toolCalls: ToolCall[] = [];
    const ids = new Set<string>();
    const identities = new Set<string>();
    for (const call of calls) {
      if (!offered.has(call.name) || !object(call.arguments)) return invalid();
      if (call.id !== null) {
        if (call.id.length === 0 || call.id.length > 256 || ids.has(call.id)) return invalid();
        ids.add(call.id);
      }
      const identity = canonical({ name: call.name, arguments: call.arguments });
      if (identities.has(identity)) return invalid();
      identities.add(identity);
      toolCalls.push({
        ...(call.id === null ? {} : { id: call.id }),
        function: { name: call.name, arguments: call.arguments },
      });
    }
    if (toolCalls.length === 0 && content.trim() === '') return invalid();
    return {
      kind: 'answered',
      content,
      toolCalls,
      model,
      served: 'remote',
      provider: this.provider.name,
    };
  }
}

function canonical(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (object(value))
    return `{${Object.keys(value)
      .sort()
      .map((key) => `${JSON.stringify(key)}:${canonical(value[key])}`)
      .join(',')}}`;
  return JSON.stringify(value);
}
