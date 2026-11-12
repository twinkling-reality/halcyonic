import type { ToolDefinition, ToolMessage } from '../../ollama.ts';
import {
  KEY_REFUSED,
  type Protocol,
  type ProtocolCall,
  type ProtocolFailure,
  type ProtocolReply,
} from '../provider.ts';

const MAX_RESPONSE_BYTES = 256 * 1024;

/**
 * The OpenAI-compatible Chat Completions shape (`POST <upstream>/chat/completions`, `GET
 * <upstream>/models`) that many hosted endpoints and local servers speak. The key travels in the
 * header the provider declares, with its scheme ("Bearer" for most). Earlier tool calls are replayed
 * natively: each `tool` message answers the earlier call it follows, paired by order, since this
 * shape keeps no reasoning that must come back unchanged.
 */
export const openAiChat: Protocol = {
  // The one model's own record (`GET <upstream>/models/{model}`, a 404 when not served), not the list, whose shape and
  // paging Meta's pages do not show (review of a1e824c84).
  async check(call) {
    const response = await request(call, `models/${encodeURIComponent(call.model)}`, null);
    if (!response.ok) return response;
    return (response.body as { id?: unknown }).id === call.model ? { ok: true } : invalid(call);
  },

  async turn(call, messages, tools): Promise<ProtocolReply> {
    const declared = declarationProblem(call);
    if (declared !== null)
      return { kind: 'failed', code: 'provider_declaration', message: declared };
    const wire = asChat(messages);
    if (wire === null) return { kind: 'failed', ...invalidInput };
    const response = await request(call, 'chat/completions', {
      model: call.model,
      messages: wire,
      [call.provider.tuning?.outputField ?? 'max_tokens']: call.outputTokens,
      ...(call.provider.tuning?.effort === undefined
        ? {}
        : { reasoning_effort: call.provider.tuning.effort }),
      ...(tools.length === 0 ? {} : { tools: tools as readonly ToolDefinition[] }),
    });
    if (!response.ok) return { kind: 'failed', code: response.code, message: response.message };
    const body = response.body as {
      model?: unknown;
      choices?: { finish_reason?: unknown; message?: Record<string, unknown> }[];
    };
    const choice =
      Array.isArray(body.choices) && body.choices.length === 1 ? body.choices[0] : null;
    const message = choice?.message;
    if (!choice || !message || message.role !== 'assistant' || typeof body.model !== 'string')
      return { kind: 'failed', ...invalid(call) };
    // A refusal may come as its own finish reason or as the message's `refusal` text, never relayed.
    const refused = typeof message.refusal === 'string' && message.refusal !== '';
    if (choice.finish_reason === 'content_filter' || refused)
      return {
        kind: 'failed',
        code: 'model_refused',
        message: `${call.provider.name} declined this request.`,
      };
    if (choice.finish_reason === 'length')
      return {
        kind: 'failed',
        code: 'context_limit',
        message: 'The model reached its context or response limit.',
      };
    if (choice.finish_reason !== 'stop' && choice.finish_reason !== 'tool_calls')
      return { kind: 'failed', ...invalid(call) };
    const content = message.content ?? '';
    if (typeof content !== 'string') return { kind: 'failed', ...invalid(call) };
    const calls: { id: string | null; name: string; arguments: unknown }[] = [];
    for (const entry of (message.tool_calls as unknown[] | undefined) ?? []) {
      const tool = entry as {
        id?: unknown;
        type?: unknown;
        function?: { name?: unknown; arguments?: unknown };
      };
      if (tool.type !== 'function' || typeof tool.function?.name !== 'string')
        return { kind: 'failed', ...invalid(call) };
      let args: unknown;
      try {
        args =
          typeof tool.function.arguments === 'string' ? JSON.parse(tool.function.arguments) : null;
      } catch {
        return { kind: 'failed', ...invalid(call) };
      }
      calls.push({
        id: typeof tool.id === 'string' ? tool.id : null,
        name: tool.function.name,
        arguments: args,
      });
    }
    return { kind: 'reply', content: content.trim(), calls, model: body.model };
  },
};

/** The effort words and output fields this shape documents (OpenAI's and Meta's pages, 2026-11-12); `none` is a 400. */
const EFFORTS = new Set(['minimal', 'low', 'medium', 'high', 'xhigh', 'max']);
const OUTPUT_FIELDS = new Set(['max_tokens', 'max_completion_tokens']);
function declarationProblem(call: ProtocolCall): string | null {
  const { effort, outputField } = call.provider.tuning ?? {};
  if (effort !== undefined && !EFFORTS.has(effort))
    return 'This protocol does not know that effort.';
  if (outputField !== undefined && !OUTPUT_FIELDS.has(outputField))
    return 'This protocol does not know that output field.';
  return null;
}

const invalidInput = {
  code: 'model_input',
  message: 'The request so far could not be put in this shape.',
};
const invalid = (call: ProtocolCall): ProtocolFailure => ({
  ok: false,
  code: 'model_response',
  message: `${call.provider.name} did not return a complete valid response.`,
});

async function request(
  call: ProtocolCall,
  path: string,
  body: object | null,
): Promise<{ ok: true; body: unknown } | ProtocolFailure> {
  const { name, scheme } = call.provider.key.header;
  const base = call.upstream.href.endsWith('/') ? call.upstream.href : `${call.upstream.href}/`;
  let response: Response;
  try {
    response = await fetch(new URL(path, base), {
      method: body === null ? 'GET' : 'POST',
      redirect: 'error',
      signal: call.signal,
      headers: {
        [name]: scheme === null ? call.key : `${scheme} ${call.key}`,
        ...(body === null ? {} : { 'content-type': 'application/json' }),
      },
      ...(body === null ? {} : { body: JSON.stringify(body) }),
    });
  } catch {
    if (call.signal.aborted) throw new Error('aborted');
    return {
      ok: false,
      code: 'provider_unreachable',
      message: 'The provider could not be reached.',
    };
  }
  if (response.status === 401 || response.status === 403) {
    await response.body?.cancel();
    return { ok: false, ...KEY_REFUSED };
  }
  if (response.status === 404) {
    await response.body?.cancel();
    return {
      ok: false,
      code: 'model_not_found',
      message: 'The provider does not serve that model.',
    };
  }
  if (response.status === 429) {
    await response.body?.cancel();
    return {
      ok: false,
      code: 'rate_limited',
      message: 'The provider asked to wait before the next request.',
    };
  }
  const text = await response.text();
  if (!response.ok || Buffer.byteLength(text) > MAX_RESPONSE_BYTES) return invalid(call);
  try {
    const parsed: unknown = JSON.parse(text);
    return parsed !== null && typeof parsed === 'object'
      ? { ok: true, body: parsed }
      : invalid(call);
  } catch {
    return invalid(call);
  }
}

type ChatMessage =
  | { role: 'system' | 'user'; content: string }
  | {
      role: 'assistant';
      content: string;
      tool_calls?: {
        id: string;
        type: 'function';
        function: { name: string; arguments: string };
      }[];
    }
  | { role: 'tool'; tool_call_id: string; content: string };

/** The host's conversation in this shape, each tool result paired by order with the call it answers. */
export function asChat(messages: readonly ToolMessage[]): ChatMessage[] | null {
  // The host's own lines go first, as one system message: whether a provider takes a system message after the
  // person's words is not documented for every endpoint of this shape (Meta's, 2026-11-12).
  const system = messages.flatMap((message) =>
    message.role === 'system' ? [message.content] : [],
  );
  const wire: ChatMessage[] = system.length
    ? [{ role: 'system', content: system.join('\n\n') }]
    : [];
  let waiting: { id: string; name: string }[] = [];
  let made = 0;
  for (const message of messages) {
    if (message.role === 'system') continue;
    if (message.role === 'tool') {
      const answered = waiting.shift();
      if (!answered || answered.name !== message.tool_name) return null;
      wire.push({ role: 'tool', tool_call_id: answered.id, content: message.content });
      continue;
    }
    if (waiting.length > 0) return null;
    if (message.role === 'assistant') {
      const calls = (message.tool_calls ?? []).map((call) => ({
        id: call.id ?? `call_${++made}`,
        type: 'function' as const,
        function: { name: call.function.name, arguments: JSON.stringify(call.function.arguments) },
      }));
      waiting = calls.map((call) => ({ id: call.id, name: call.function.name }));
      wire.push({
        role: 'assistant',
        content: message.content,
        ...(calls.length ? { tool_calls: calls } : {}),
      });
    } else if (message.role === 'user') wire.push({ role: 'user', content: message.content });
  }
  return waiting.length > 0 ? null : wire;
}
