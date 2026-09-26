export interface HttpResponse {
  readonly status: number;
  /** The parsed JSON body, or null when there was none. */
  readonly body: unknown;
}

/** No HTTP response arrived. `refused` means the request cannot have reached the server. */
export class TransportError extends Error {
  readonly refused: boolean;

  constructor(message: string, refused: boolean) {
    super(message);
    this.name = 'TransportError';
    this.refused = refused;
  }
}

/** HTTP access to one OpenCode server, authenticating every request with Basic auth. */
export class OpenCodeClient {
  readonly baseUrl: string;
  readonly #authorization: string;

  constructor(baseUrl: string, password: string) {
    this.baseUrl = baseUrl;
    this.#authorization = `Basic ${Buffer.from(`opencode:${password}`).toString('base64')}`;
  }

  async request(
    method: string,
    path: string,
    options: { readonly body?: unknown; readonly timeoutMs?: number } = {},
  ): Promise<HttpResponse> {
    const init: RequestInit = { method, headers: { authorization: this.#authorization } };
    if (options.body !== undefined) {
      init.headers = { authorization: this.#authorization, 'content-type': 'application/json' };
      init.body = JSON.stringify(options.body);
    }
    if (options.timeoutMs !== undefined) init.signal = AbortSignal.timeout(options.timeoutMs);
    let status: number;
    let text: string;
    try {
      const response = await fetch(this.baseUrl + path, init);
      status = response.status;
      text = await response.text();
    } catch (error) {
      throw new TransportError(`${method} ${path} failed: ${describe(error)}`, isRefused(error));
    }
    let body: unknown = null;
    if (text !== '') {
      try {
        body = JSON.parse(text);
      } catch {
        body = null;
      }
    }
    return { status, body };
  }

  /** Opens `GET /api/event`, which carries every event of the server. */
  async events(signal: AbortSignal): Promise<ReadableStream<Uint8Array>> {
    let response: Response;
    try {
      response = await fetch(`${this.baseUrl}/api/event`, {
        headers: { authorization: this.#authorization, accept: 'text/event-stream' },
        signal,
      });
    } catch (error) {
      throw new TransportError(`GET /api/event failed: ${describe(error)}`, isRefused(error));
    }
    if (response.status !== 200 || response.body === null) {
      await response.body?.cancel();
      throw new TransportError(`GET /api/event answered ${response.status}`, false);
    }
    return response.body;
  }
}

function isRefused(error: unknown): boolean {
  return (error as { cause?: { code?: unknown } }).cause?.code === 'ECONNREFUSED';
}

function describe(error: unknown): string {
  if (!(error instanceof Error)) return String(error);
  const cause = (error as { cause?: unknown }).cause;
  return cause instanceof Error ? `${error.message} (${cause.message})` : error.message;
}
