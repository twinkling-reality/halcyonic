/**
 * The two calls Create's companion makes to Ollama's HTTP API on this computer (ADR 0025), as
 * Ollama documents them (read 2026-10-02, docs/internal/validation/companion-model.md): the model
 * list, to check that the companion's model is here and runs here, and one chat reply, streamed so
 * the control plane can stop it as soon as it runs out of time or length; closing the request stops
 * Ollama generating. Nothing else: no pull, create, delete or `keep_alive`, so asking the companion
 * never downloads a model or holds one in memory longer than Ollama would.
 */

/** A model name with a `cloud` tag, such as `gemma4:cloud` or `gpt-oss:120b-cloud`: Ollama runs it on its own service. */
export function isCloudName(model: string): boolean {
  const tag = model.includes(':') ? model.slice(model.lastIndexOf(':') + 1) : '';
  return tag === 'cloud' || tag.endsWith('-cloud');
}

export type ModelCheck =
  | { readonly kind: 'local' }
  | { readonly kind: 'not_running' }
  | { readonly kind: 'missing' }
  /** Listed, but Ollama would pass the request to another host: a cloud model or a remote one. */
  | { readonly kind: 'remote' }
  | { readonly kind: 'failed'; readonly message: string };

/** The largest model list read: Ollama lists every model it holds, a few hundred bytes each. */
const MAX_LIST_BYTES = 1024 * 1024;

/**
 * Whether `model` is in Ollama's list (`GET /api/tags`) as a model that runs here: listed, and
 * carrying neither `remote_host` nor `remote_model`, which Ollama gives a model that runs on
 * another host. A name without a tag matches its `:latest`, as Ollama resolves it.
 */
export async function checkModel(base: URL, model: string, timeoutMs: number): Promise<ModelCheck> {
  if (isCloudName(model)) return { kind: 'remote' };
  let response: Response;
  try {
    response = await fetch(new URL('/api/tags', base), {
      redirect: 'error',
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch (error) {
    return unreachable(error);
  }
  if (!response.ok) {
    await response.body?.cancel();
    return { kind: 'failed', message: `The model list answered HTTP ${response.status}.` };
  }
  const text = await readBounded(response, MAX_LIST_BYTES);
  if (text === null) return { kind: 'failed', message: 'The model list is too large.' };
  let models: unknown;
  try {
    models = (JSON.parse(text) as { models?: unknown }).models;
  } catch {
    return { kind: 'failed', message: 'The model list is not JSON.' };
  }
  if (!Array.isArray(models)) return { kind: 'failed', message: 'The model list has no models.' };
  const wanted = model.includes(':') ? [model] : [model, `${model}:latest`];
  const listed = models.find(
    (entry): entry is Record<string, unknown> =>
      typeof entry === 'object' &&
      entry !== null &&
      (wanted.includes((entry as { name?: unknown }).name as string) ||
        wanted.includes((entry as { model?: unknown }).model as string)),
  );
  if (listed === undefined) return { kind: 'missing' };
  if (present(listed.remote_host) || present(listed.remote_model)) return { kind: 'remote' };
  return { kind: 'local' };
}

export interface ChatMessage {
  readonly role: 'system' | 'user' | 'assistant';
  readonly content: string;
}

export interface ChatRequest {
  readonly base: URL;
  readonly model: string;
  readonly messages: readonly ChatMessage[];
  /** Ollama's `num_ctx`: the context the model is given for this request. */
  readonly contextTokens: number;
  /** Ollama's `num_predict`: the most tokens it writes. */
  readonly outputTokens: number;
  readonly temperature: number;
  /** How long until the first line of the reply: waiting behind other requests, loading, reading the prompt. */
  readonly firstTokenMs: number;
  /** How long for the whole reply. */
  readonly totalMs: number;
  /** The most characters of reply read; a longer one is stopped and refused. */
  readonly maxCharacters: number;
  /** Ends the request early, as when the person's headset goes away. */
  readonly signal?: AbortSignal;
}

export type ChatResult =
  | {
      readonly kind: 'answered';
      readonly content: string;
      readonly firstTokenMs: number;
      readonly promptTokens: number | null;
      readonly outputTokens: number | null;
    }
  | {
      readonly kind: 'failed';
      readonly reason: 'not_running' | 'missing' | 'too_slow' | 'too_long' | 'cancelled' | 'error';
      readonly message: string;
      readonly firstTokenMs: number | null;
    };

/**
 * One reply from `POST /api/chat`, thinking off, no tools, streamed: the request is closed the
 * moment the first line is late, the whole reply is late, the reply passes `maxCharacters`, or the
 * caller's signal ends it.
 */
export async function chat(request: ChatRequest): Promise<ChatResult> {
  const started = performance.now();
  const controller = new AbortController();
  let stopped: 'too_slow' | 'too_long' | 'cancelled' | null = null;
  const stop = (why: 'too_slow' | 'too_long' | 'cancelled') => {
    stopped ??= why;
    controller.abort();
  };
  const firstTimer = setTimeout(() => stop('too_slow'), request.firstTokenMs);
  const totalTimer = setTimeout(() => stop('too_slow'), request.totalMs);
  const onCancel = () => stop('cancelled');
  if (request.signal?.aborted === true) stop('cancelled');
  request.signal?.addEventListener('abort', onCancel, { once: true });
  let firstTokenMs: number | null = null;
  const failed = (
    reason: Exclude<ChatResult, { kind: 'answered' }>['reason'],
    message: string,
  ): ChatResult => ({ kind: 'failed', reason, message, firstTokenMs });
  try {
    let response: Response;
    try {
      response = await fetch(new URL('/api/chat', request.base), {
        method: 'POST',
        redirect: 'error',
        signal: controller.signal,
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          model: request.model,
          messages: request.messages,
          stream: true,
          think: false,
          options: {
            num_ctx: request.contextTokens,
            num_predict: request.outputTokens,
            temperature: request.temperature,
          },
        }),
      });
    } catch (error) {
      if (stopped !== null) return failed(stopped, stoppedMessage(stopped));
      const check = unreachable(error);
      return check.kind === 'not_running'
        ? failed('not_running', 'Ollama is not running.')
        : failed('error', (check as { message: string }).message);
    }
    if (response.status === 404) {
      await response.body?.cancel();
      return failed('missing', 'Ollama does not have the model.');
    }
    if (!response.ok || response.body === null) {
      await response.body?.cancel();
      return failed('error', `Ollama answered HTTP ${response.status}.`);
    }
    let content = '';
    let promptTokens: number | null = null;
    let outputTokens: number | null = null;
    let pending = '';
    const decoder = new TextDecoder();
    try {
      for await (const chunk of response.body) {
        if (firstTokenMs === null) {
          firstTokenMs = Math.round(performance.now() - started);
          clearTimeout(firstTimer);
        }
        pending += decoder.decode(chunk as Uint8Array, { stream: true });
        let newline = pending.indexOf('\n');
        while (newline >= 0) {
          const line = pending.slice(0, newline).trim();
          pending = pending.slice(newline + 1);
          newline = pending.indexOf('\n');
          if (line === '') continue;
          let parsed: {
            error?: unknown;
            message?: { content?: unknown };
            done?: unknown;
            prompt_eval_count?: unknown;
            eval_count?: unknown;
          };
          try {
            parsed = JSON.parse(line) as typeof parsed;
          } catch {
            return failed('error', 'Ollama sent a line that is not JSON.');
          }
          if (parsed.error !== undefined) return failed('error', 'Ollama stopped with an error.');
          if (typeof parsed.message?.content === 'string') content += parsed.message.content;
          if (content.length > request.maxCharacters) {
            stop('too_long');
            return failed('too_long', 'The reply is longer than a reply may be.');
          }
          if (parsed.done === true) {
            promptTokens =
              typeof parsed.prompt_eval_count === 'number' ? parsed.prompt_eval_count : null;
            outputTokens = typeof parsed.eval_count === 'number' ? parsed.eval_count : null;
          }
        }
        if (pending.length > request.maxCharacters * 8) {
          stop('too_long');
          return failed('too_long', 'Ollama sent a line longer than a reply may be.');
        }
      }
    } catch {
      if (stopped !== null) return failed(stopped, stoppedMessage(stopped));
      return failed('error', 'The reply from Ollama was cut off.');
    }
    if (stopped !== null) return failed(stopped, stoppedMessage(stopped));
    return {
      kind: 'answered',
      content,
      firstTokenMs: firstTokenMs ?? Math.round(performance.now() - started),
      promptTokens,
      outputTokens,
    };
  } finally {
    clearTimeout(firstTimer);
    clearTimeout(totalTimer);
    request.signal?.removeEventListener('abort', onCancel);
    controller.abort();
  }
}

function stoppedMessage(why: 'too_slow' | 'too_long' | 'cancelled'): string {
  if (why === 'too_slow') return 'The model took too long.';
  if (why === 'too_long') return 'The reply is longer than a reply may be.';
  return 'The request ended before the reply.';
}

function present(value: unknown): boolean {
  return value !== undefined && value !== null && value !== '';
}

function unreachable(error: unknown): ModelCheck {
  const cause = (error as { cause?: { code?: unknown } } | null)?.cause;
  if (cause?.code === 'ECONNREFUSED' || cause?.code === 'ECONNRESET')
    return { kind: 'not_running' };
  if ((error as { name?: unknown } | null)?.name === 'TimeoutError')
    return { kind: 'failed', message: 'Ollama did not answer in time.' };
  return { kind: 'failed', message: 'Ollama could not be reached.' };
}

/** The body as text, or null once it passes `limit` bytes, when the rest is not read. */
async function readBounded(response: Response, limit: number): Promise<string | null> {
  if (response.body === null) return '';
  const chunks: Uint8Array[] = [];
  let size = 0;
  for await (const chunk of response.body) {
    size += (chunk as Uint8Array).byteLength;
    if (size > limit) {
      await response.body.cancel().catch(() => undefined);
      return null;
    }
    chunks.push(chunk as Uint8Array);
  }
  return Buffer.concat(chunks).toString('utf8');
}
