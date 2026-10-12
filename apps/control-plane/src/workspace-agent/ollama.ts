import type { WorkspaceAgentStatus } from '@halcyonic/contracts';
import { checkModel } from '../companion/ollama.ts';
import { type CompanionConfig, companionOllamaAddress } from '../config.ts';

export interface ToolCall {
  /** Ollama's opaque call reference, retained only in the transient model conversation. */
  readonly id?: string;
  readonly function: { readonly name: string; readonly arguments: Record<string, unknown> };
}
export interface ToolDefinition {
  readonly type: 'function';
  readonly function: {
    readonly name: string;
    readonly description: string;
    readonly parameters: object;
  };
}
export type ToolMessage =
  | { readonly role: 'system' | 'user'; readonly content: string }
  | {
      readonly role: 'assistant';
      readonly content: string;
      readonly tool_calls?: readonly ToolCall[];
    }
  | { readonly role: 'tool'; readonly content: string; readonly tool_name: string };
export type ToolTurnResult =
  | {
      readonly kind: 'answered';
      readonly content: string;
      readonly toolCalls: ToolCall[];
      readonly model: string;
      readonly served: 'this_mac';
    }
  | { readonly kind: 'failed'; readonly code: string; readonly message: string };
export interface ToolModelBounds {
  readonly metadataMs: number;
  readonly totalMs: number;
  readonly contextTokens: number;
  readonly outputTokens: number;
  readonly maxInputBytes: number;
  readonly maxReplyBytes: number;
  readonly maxReplyCharacters: number;
  readonly maxToolCalls: number;
}
export const TOOL_MODEL_BOUNDS: ToolModelBounds = {
  metadataMs: 1_000,
  totalMs: 45_000,
  contextTokens: 8_192,
  outputTokens: 1_024,
  maxInputBytes: 12_288,
  maxReplyBytes: 128 * 1_024,
  maxReplyCharacters: 4_000,
  maxToolCalls: 8,
};
const METADATA_BYTES = 1024 * 1024;
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

/** Native Ollama tools, not a text/phrase parser. The coordinator owns validation and dispatch. */
export class OllamaWorkspaceModel {
  readonly #config: CompanionConfig | null;
  readonly #bounds: ToolModelBounds;
  constructor(options: { config: CompanionConfig | null; bounds?: Partial<ToolModelBounds> }) {
    this.#config =
      options.config === null
        ? null
        : {
            model: options.config.model,
            ollama: new URL(options.config.ollama.href),
          };
    this.#bounds = { ...TOOL_MODEL_BOUNDS, ...options.bounds };
  }

  /** Only bounded metadata reads; this never invokes inference or downloads a model. */
  async status(): Promise<WorkspaceAgentStatus> {
    return this.#status();
  }

  async #status(cancel?: AbortSignal): Promise<WorkspaceAgentStatus> {
    const config = this.#config;
    if (config === null)
      return unavailable('not_configured', 'A local model has not been configured.');
    try {
      companionOllamaAddress(config.ollama.href);
    } catch {
      return unavailable('local_model_required', 'The model must run on this computer.');
    }
    const checked = await checkModel(config.ollama, config.model, this.#bounds.metadataMs, cancel);
    if (checked.kind !== 'local')
      return unavailable(
        `model_${checked.kind}`,
        checked.kind === 'remote'
          ? 'The configured model runs on another host.'
          : 'The configured local model is unavailable.',
      );
    const signal =
      cancel === undefined
        ? AbortSignal.timeout(this.#bounds.metadataMs)
        : AbortSignal.any([cancel, AbortSignal.timeout(this.#bounds.metadataMs)]);
    try {
      const shown = await requestJson(
        config.ollama,
        '/api/show',
        { model: config.model },
        signal,
        METADATA_BYTES,
      );
      if (!object(shown) || shown.error !== undefined)
        return unavailable('model_metadata', 'The local model metadata is unavailable.');
      if (present(shown.remote_host) || present(shown.remote_model))
        return unavailable('model_remote', 'The configured model runs on another host.');
      if (!Array.isArray(shown.capabilities) || !shown.capabilities.includes('tools'))
        return unavailable(
          'tools_unsupported',
          'The configured local model does not declare tool support.',
        );
      return { availability: 'available', model: config.model, served: 'this_mac' };
    } catch {
      return unavailable('model_metadata', 'The local model metadata could not be read.');
    }
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
    try {
      if (controller.signal.aborted)
        return failure('cancelled', 'The request stopped before the model replied.');
      const body = JSON.stringify({ messages, tools });
      if (Buffer.byteLength(body) > this.#bounds.maxInputBytes)
        return failure('context_limit', 'This request needs less context before it can continue.');
      // Snapshot before the first await; caller mutation cannot bypass the measured input bound.
      const captured = JSON.parse(body) as { messages: ToolMessage[]; tools: ToolDefinition[] };
      // Recheck before every inference; an earlier status is not continuing locality authority.
      const status = await this.#status(controller.signal);
      if (controller.signal.aborted)
        return failure(
          signal?.aborted ? 'cancelled' : 'timeout',
          'The model request ended before it could continue.',
        );
      if (status.availability !== 'available')
        return failure(status.reason.code, status.reason.message);
      const config = this.#config;
      if (config === null)
        return failure('not_configured', 'A local model has not been configured.');
      const reply = await requestJson(
        config.ollama,
        '/api/chat',
        {
          model: config.model,
          messages: captured.messages,
          tools: captured.tools,
          stream: false,
          think: false,
          options: {
            num_ctx: this.#bounds.contextTokens,
            num_predict: this.#bounds.outputTokens,
            temperature: 0,
          },
        },
        controller.signal,
        this.#bounds.maxReplyBytes,
      );
      if (controller.signal.aborted) throw new Error('aborted');
      return this.#readReply(reply, config.model, captured.tools);
    } catch (error) {
      if (controller.signal.aborted)
        return failure(
          signal?.aborted ? 'cancelled' : 'timeout',
          'The model request ended before it could continue.',
        );
      if (error instanceof ResponseLimit)
        return failure('response_limit', 'The model response exceeded its size limit.');
      // Never relay transport or model error text: it can repeat inputs and credentials.
      return failure('model_response', 'The local model did not return a complete valid response.');
    } finally {
      clearTimeout(timer);
      signal?.removeEventListener('abort', onCancel);
      controller.abort();
    }
  }

  #readReply(reply: unknown, model: string, tools: readonly ToolDefinition[]): ToolTurnResult {
    const invalid = () =>
      failure('model_response', 'The local model did not return a complete valid response.');
    if (
      !object(reply) ||
      reply.error !== undefined ||
      reply.done !== true ||
      reply.done_reason !== 'stop' ||
      !sameModel(reply.model, model)
    )
      return invalid();
    if (
      !Number.isSafeInteger(reply.prompt_eval_count) ||
      (reply.prompt_eval_count as number) < 0 ||
      !Number.isSafeInteger(reply.eval_count) ||
      (reply.eval_count as number) < 0
    )
      return invalid();
    // A full context or exhausted generation budget cannot authorize a possibly truncated call.
    if (
      (reply.prompt_eval_count as number) >=
        this.#bounds.contextTokens - this.#bounds.outputTokens ||
      (reply.eval_count as number) >= this.#bounds.outputTokens
    )
      return failure('context_limit', 'The model reached its context or response limit.');
    const message = reply.message;
    if (
      !object(message) ||
      message.role !== 'assistant' ||
      typeof message.content !== 'string' ||
      [...message.content].length > this.#bounds.maxReplyCharacters
    )
      return invalid();
    const calls = message.tool_calls ?? [];
    if (!Array.isArray(calls) || calls.length > this.#bounds.maxToolCalls) return invalid();
    const offered = new Set(tools.map((tool) => tool.function.name));
    const toolCalls: ToolCall[] = [];
    const indices = new Set<number>();
    const ids = new Set<string>();
    const identities = new Set<string>();
    for (const call of calls) {
      if (
        !object(call) ||
        Object.keys(call).some((key) => key !== 'type' && key !== 'function' && key !== 'id') ||
        (call.type !== undefined && call.type !== 'function') ||
        !object(call.function)
      )
        return invalid();
      if (call.id !== undefined) {
        if (
          typeof call.id !== 'string' ||
          call.id.length === 0 ||
          call.id.length > 256 ||
          ids.has(call.id)
        )
          return invalid();
        ids.add(call.id);
      }
      const fn = call.function;
      if (
        Object.keys(fn).some((key) => !['name', 'arguments', 'index'].includes(key)) ||
        typeof fn.name !== 'string' ||
        !offered.has(fn.name) ||
        !object(fn.arguments)
      )
        return invalid();
      if (fn.index !== undefined) {
        if (
          !Number.isSafeInteger(fn.index) ||
          (fn.index as number) < 0 ||
          indices.has(fn.index as number)
        )
          return invalid();
        indices.add(fn.index as number);
      }
      const identity = canonical({ name: fn.name, arguments: fn.arguments });
      if (identities.has(identity)) return invalid();
      identities.add(identity);
      toolCalls.push({
        ...(call.id === undefined ? {} : { id: call.id as string }),
        function: { name: fn.name, arguments: fn.arguments },
      });
    }
    if (toolCalls.length === 0 && message.content.trim() === '') return invalid();
    return { kind: 'answered', content: message.content, toolCalls, model, served: 'this_mac' };
  }
}

const present = (value: unknown) => value !== undefined && value !== null && value !== '';
const sameModel = (given: unknown, wanted: string) =>
  typeof given === 'string' &&
  (given === wanted || (!wanted.includes(':') && given === `${wanted}:latest`));
function canonical(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (object(value))
    return `{${Object.keys(value)
      .sort()
      .map((key) => `${JSON.stringify(key)}:${canonical(value[key])}`)
      .join(',')}}`;
  return JSON.stringify(value);
}
class ResponseLimit extends Error {}
async function requestJson(
  base: URL,
  path: '/api/show' | '/api/chat',
  body: object,
  signal: AbortSignal,
  maximum: number,
): Promise<unknown> {
  const response = await fetch(new URL(path, base), {
    method: 'POST',
    redirect: 'error',
    signal,
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!response.ok || response.body === null) {
    await response.body?.cancel();
    throw new Error('model HTTP response refused');
  }
  const reader = response.body.getReader();
  let bytes = 0;
  const chunks: Uint8Array[] = [];
  try {
    for (;;) {
      const part = await reader.read();
      if (part.done) break;
      bytes += part.value.byteLength;
      if (bytes > maximum) throw new ResponseLimit();
      chunks.push(part.value);
    }
    return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
  } finally {
    await reader.cancel().catch(() => undefined);
    reader.releaseLock();
  }
}
