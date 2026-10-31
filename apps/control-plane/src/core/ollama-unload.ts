import { request as httpRequest, type IncomingMessage } from 'node:http';
import type { ExecutionId } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { EventPublisher } from './publisher.ts';

const MAX_PS_BYTES = 1024 * 1024;
const DEFAULT_TIMEOUT_MS = 2_000;

export interface OllamaUnloadOptions {
  /** Loopback URL of the local Ollama server, such as `http://127.0.0.1:11434`. */
  readonly url: string;
  /** Bounds each `/api/ps` read and `/api/generate` unload request. Defaults to 2,000 ms. */
  readonly timeoutMs?: number;
}

interface ModelTrack {
  /** The model name as the execution asked for it, without the `ollama/` prefix. */
  model: string;
  /** Whether `/api/ps` listed this model before Halcyonic started using it. */
  preloaded: 'yes' | 'no' | 'unknown';
  /** Executions currently using this model in an active turn or start. */
  readonly active: Set<ExecutionId>;
  /** In-flight `/api/ps` snapshot before the first active execution starts. */
  checking: Promise<void> | null;
}

/**
 * Extracts the Ollama model name (without the `ollama/` prefix) from a `model_ref` or runtime
 * start options, or null when the execution does not name an Ollama model.
 */
export function ollamaModelFrom(
  modelRef: string | null | undefined,
  options?: Readonly<Record<string, unknown>> | null,
): string | null {
  if (typeof modelRef === 'string' && modelRef.startsWith('ollama/')) {
    const name = modelRef.slice('ollama/'.length).trim();
    if (name.length > 0) return name;
  }
  if (options != null) {
    if (typeof options.model === 'string' && options.model.startsWith('ollama/')) {
      const name = options.model.slice('ollama/'.length).trim();
      if (name.length > 0) return name;
    }
    if (
      options.model_provider === 'ollama' &&
      typeof options.model === 'string' &&
      options.model.trim().length > 0
    ) {
      return options.model.trim();
    }
  }
  return null;
}

/** Normalizes an Ollama model tag so a name without `:` matches `:latest`, as Ollama itself does. */
export function normalizeOllamaTag(model: string): string {
  const trimmed = model.trim();
  return trimmed.includes(':') ? trimmed : `${trimmed}:latest`;
}

/** Whether two Ollama model names refer to the same tag (`foo` and `foo:latest`). */
export function sameOllamaModel(a: string, b: string): boolean {
  return a === b || normalizeOllamaTag(a) === normalizeOllamaTag(b);
}

/**
 * Unloads a local Ollama model that Halcyonic itself loaded once no active execution in Halcyonic
 * is using it (UL1). Before starting a turn on `ollama/<model>`, if no active execution is already
 * using that model, it reads `GET /api/ps`; when the execution's turn ends (or fails, is
 * interrupted, becomes unknown, or the control plane closes) and no other active execution uses
 * that model, it sends `POST /api/generate` with `{ model, keep_alive: 0 }` only when `/api/ps`
 * had answered and showed the model was not already loaded. Best-effort: an unreachable Ollama
 * never fails an execution or fabricates certainty.
 */
export class OllamaUnloader {
  readonly #target: { readonly hostname: string; readonly port: number };
  readonly #timeoutMs: number;
  readonly #executionModels = new Map<ExecutionId, string>();
  readonly #models = new Map<string, ModelTrack>();
  #projection: Projection | null = null;
  #closed = false;

  constructor(options: OllamaUnloadOptions) {
    const parsed = new URL(options.url);
    if (
      parsed.protocol !== 'http:' ||
      (parsed.hostname !== '127.0.0.1' && parsed.hostname !== '[::1]' && parsed.hostname !== '::1')
    ) {
      throw new Error(`OllamaUnloader requires a loopback http:// URL, not ${options.url}.`);
    }
    this.#target = {
      hostname: parsed.hostname.replace(/^\[|\]$/g, ''),
      port: Number(parsed.port || 80),
    };
    this.#timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  }

  /** The Ollama model name remembered for an execution, or null if none. */
  modelFor(executionId: ExecutionId): string | null {
    return this.#executionModels.get(executionId) ?? null;
  }

  /**
   * Called before an execution starts or sends an instruction on `ollama/<model>`. When no active
   * execution is already using `<model>`, snapshots `GET /api/ps` so Halcyonic knows whether the
   * model was already loaded before this run.
   */
  async beforeRun(executionId: ExecutionId, model: string): Promise<void> {
    if (this.#closed) return;
    this.#executionModels.set(executionId, model);
    const key = normalizeOllamaTag(model);
    let track = this.#models.get(key);
    if (track !== undefined) {
      track.active.add(executionId);
      if (track.checking !== null) await track.checking;
      return;
    }
    track = {
      model,
      preloaded: 'unknown',
      active: new Set([executionId]),
      checking: null,
    };
    this.#models.set(key, track);
    const current = track;
    const check = this.#isLoaded(model).then((loaded) => {
      current.preloaded = loaded;
    });
    current.checking = check;
    try {
      await check;
    } finally {
      if (current.checking === check) current.checking = null;
    }
  }

  /** Subscribes to runtime and command events so models are unloaded when their executions end. */
  attach(publisher: EventPublisher, projection: Projection): () => void {
    this.#projection = projection;
    return publisher.subscribe(({ event }) => {
      if (this.#closed) return;
      if (event.event_type === 'runtime.model.used') {
        const reported = ollamaModelFrom(event.payload.model_ref);
        if (reported !== null) {
          const previous = this.#executionModels.get(event.execution_id);
          this.#executionModels.set(event.execution_id, reported);
          if (previous !== undefined && !sameOllamaModel(previous, reported)) {
            void this.executionEnded(event.execution_id, normalizeOllamaTag(previous));
          }
        }
        return;
      }
      if (
        event.event_type === 'runtime.turn.completed' ||
        event.event_type === 'runtime.turn.failed' ||
        event.event_type === 'runtime.turn.interrupted' ||
        event.event_type === 'execution.state_unknown' ||
        event.event_type === 'runtime.connection.lost'
      ) {
        void this.executionEnded(event.execution_id);
        return;
      }
      if (
        event.event_type === 'command.failed' &&
        event.execution_id !== null &&
        (event.payload.command_type === 'execution.start' ||
          event.payload.command_type === 'execution.send_instruction')
      ) {
        const exec = projection.execution(event.execution_id);
        if (exec === undefined || !isActiveStatus(exec.status)) {
          void this.executionEnded(event.execution_id);
        }
      }
    });
  }

  /** Releases an execution's hold on its Ollama model and unloads the model if Halcyonic loaded it. */
  async executionEnded(executionId: ExecutionId, onlyKey?: string): Promise<void> {
    if (this.#closed) return;
    for (const [key, track] of [...this.#models.entries()]) {
      if (onlyKey !== undefined && key !== onlyKey) continue;
      if (!track.active.has(executionId)) continue;
      if (track.checking !== null) await track.checking;
      track.active.delete(executionId);
      if (track.active.size > 0 || this.#anyProjectionExecutionActive(key)) continue;
      this.#models.delete(key);
      if (track.preloaded === 'no' && !this.#closed) {
        await this.#unload(track.model);
      }
    }
  }

  /** Unloads any model Halcyonic loaded that is still tracked when the control plane closes. */
  async close(): Promise<void> {
    this.#closed = true;
    const tracks = [...this.#models.values()];
    this.#models.clear();
    await Promise.all(
      tracks.map(async (track) => {
        if (track.checking !== null) await track.checking.catch(() => undefined);
        if (track.preloaded === 'no') await this.#unload(track.model);
      }),
    );
  }

  #anyProjectionExecutionActive(key: string): boolean {
    const projection = this.#projection;
    if (projection === null) return false;
    for (const execution of projection.executions()) {
      if (!isActiveStatus(execution.status)) continue;
      const model =
        ollamaModelFrom(execution.model_ref) ??
        this.#executionModels.get(execution.execution_id) ??
        null;
      if (model !== null && normalizeOllamaTag(model) === key) return true;
    }
    return false;
  }

  #isLoaded(model: string): Promise<'yes' | 'no' | 'unknown'> {
    return new Promise((resolve) => {
      const req = httpRequest(
        {
          ...this.#target,
          path: '/api/ps',
          method: 'GET',
          timeout: this.#timeoutMs,
        },
        (res) => {
          if (res.statusCode !== 200) {
            res.resume();
            resolve('unknown');
            return;
          }
          readBounded(res, MAX_PS_BYTES).then(
            (raw) => {
              if (raw === null) {
                resolve('unknown');
                return;
              }
              try {
                const parsed = JSON.parse(raw.toString('utf8')) as { models?: unknown };
                if (!Array.isArray(parsed.models)) {
                  resolve('unknown');
                  return;
                }
                const found = parsed.models.some((entry) => {
                  if (typeof entry !== 'object' || entry === null) return false;
                  const record = entry as { model?: unknown; name?: unknown };
                  return (
                    (typeof record.model === 'string' && sameOllamaModel(record.model, model)) ||
                    (typeof record.name === 'string' && sameOllamaModel(record.name, model))
                  );
                });
                resolve(found ? 'yes' : 'no');
              } catch {
                resolve('unknown');
              }
            },
            () => resolve('unknown'),
          );
        },
      );
      req.once('timeout', () => {
        req.destroy();
        resolve('unknown');
      });
      req.once('error', () => resolve('unknown'));
      req.end();
    });
  }

  #unload(model: string): Promise<void> {
    return new Promise((resolve) => {
      const body = JSON.stringify({ model, keep_alive: 0 });
      const req = httpRequest(
        {
          ...this.#target,
          path: '/api/generate',
          method: 'POST',
          timeout: this.#timeoutMs,
          headers: {
            'content-type': 'application/json',
            'content-length': Buffer.byteLength(body),
          },
        },
        (res) => {
          res.resume();
          res.once('end', () => resolve());
          res.once('error', () => resolve());
        },
      );
      req.once('timeout', () => {
        req.destroy();
        resolve();
      });
      req.once('error', () => resolve());
      req.end(body);
    });
  }
}

function isActiveStatus(status: string): boolean {
  return (
    status === 'starting' ||
    status === 'running' ||
    status === 'verifying' ||
    status === 'waiting_for_human'
  );
}

function readBounded(stream: IncomingMessage, limit: number): Promise<Buffer | null> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let total = 0;
    let settled = false;
    stream.on('data', (chunk: Buffer) => {
      total += chunk.length;
      if (total > limit) {
        settled = true;
        stream.removeAllListeners('data');
        stream.resume();
        resolve(null);
        return;
      }
      chunks.push(chunk);
    });
    stream.once('end', () => {
      if (settled) return;
      settled = true;
      resolve(Buffer.concat(chunks));
    });
    stream.once('error', reject);
    stream.once('close', () => {
      if (!settled) resolve(null);
    });
  });
}
