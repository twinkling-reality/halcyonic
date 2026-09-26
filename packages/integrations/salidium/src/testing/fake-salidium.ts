import { randomBytes } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import {
  createServer,
  type IncomingHttpHeaders,
  type IncomingMessage,
  type ServerResponse,
} from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const FIXTURES = new URL('../../fixtures/v1/', import.meta.url);

type Json = Record<string, unknown>;

/** A retained fixture of Salidium's consumer contract. Every call returns a fresh copy. */
export function fixture(name: string): Json {
  return JSON.parse(readFileSync(new URL(`${name}.json`, FIXTURES), 'utf8')) as Json;
}

/** A credential in the shape Salidium issues, invented for one test. */
export function consumerToken(): string {
  return `salidium_consumer_${randomBytes(6).toString('hex')}_${randomBytes(32).toString('hex')}`;
}

export interface RecordedRequest {
  readonly method: string;
  readonly path: string;
  readonly query: URLSearchParams;
  readonly headers: IncomingHttpHeaders;
}

/**
 * A stand-in for Salidium's daemon that speaks consumer contract v1 over real loopback HTTP, the way
 * Salidium's release candidate does (`packages/daemon/src/server/httpServer.ts`, `consumer/routes.ts`
 * and `server/sse.ts`): Host, Origin and cross-site checks first, GET only, discovery without a
 * credential, the bearer credential on everything else, a feed that opens with a padding comment
 * and `resync`, and the retained fixtures as documents.
 */
export class FakeSalidium {
  readonly home = mkdtempSync(join(tmpdir(), 'halcyonic-salidium-'));
  /** The one credential this daemon accepts. Replacing it revokes the old one. */
  token = consumerToken();
  readonly requests: RecordedRequest[] = [];
  /** Reports served, by Salidium's session id. */
  readonly reports = new Map<string, Json>();
  /** Replaces the answer to one path. */
  readonly overrides = new Map<string, (response: ServerResponse) => void>();
  instanceId = randomBytes(16).toString('hex');
  /** Whether a new feed connection starts with `resync`, as the contract requires. */
  resyncOnConnect = true;
  feedConnections = 0;
  readonly #feeds = new Set<ServerResponse>();
  readonly #server = createServer((request, response) => this.#handle(request, response));
  #port = 0;

  static async start(): Promise<FakeSalidium> {
    const fake = new FakeSalidium();
    for (const name of ['session-report-verified', 'session-report-failing']) {
      const report = fixture(name);
      fake.reports.set((report.session as Json).id as string, report);
    }
    await new Promise<void>((resolve) => fake.#server.listen(0, '127.0.0.1', resolve));
    fake.#port = (fake.#server.address() as AddressInfo).port;
    fake.writeDiscovery();
    return fake;
  }

  get port(): number {
    return this.#port;
  }

  /** The discovery document this daemon serves and writes. */
  discovery(): Json {
    return {
      ...fixture('consumer-discovery'),
      instanceId: this.instanceId,
      baseUrl: `http://127.0.0.1:${this.#port}/consumer/v1`,
    };
  }

  writeDiscovery(document: unknown = this.discovery()): void {
    const text = typeof document === 'string' ? document : JSON.stringify(document);
    writeFileSync(join(this.home, 'consumer.json'), text, { mode: 0o600 });
  }

  removeDiscovery(): void {
    rmSync(join(this.home, 'consumer.json'), { force: true });
  }

  /** Writes one feed message to every open feed connection. */
  send(message: unknown): void {
    this.sendRaw(`data: ${JSON.stringify(message)}\n\n`);
  }

  sendRaw(text: string): void {
    for (const feed of this.#feeds) feed.write(text);
  }

  endFeeds(): void {
    for (const feed of this.#feeds) feed.end();
  }

  get openFeeds(): number {
    return this.#feeds.size;
  }

  authorizedRequests(): RecordedRequest[] {
    return this.requests.filter((request) => request.headers.authorization !== undefined);
  }

  async close(): Promise<void> {
    const closed = new Promise<void>((resolve) => this.#server.close(() => resolve()));
    // fetch keeps connections alive; close them rather than wait for their idle timeout.
    this.#server.closeAllConnections();
    await closed;
    rmSync(this.home, { recursive: true, force: true });
  }

  #handle(request: IncomingMessage, response: ServerResponse): undefined {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1');
    this.requests.push({
      method: request.method ?? '',
      path: url.pathname,
      query: url.searchParams,
      headers: request.headers,
    });
    const hosts = new Set([
      `127.0.0.1:${this.#port}`,
      `localhost:${this.#port}`,
      `[::1]:${this.#port}`,
    ]);
    if (!hosts.has(request.headers.host ?? ''))
      return json(response, 421, { error: 'unexpected host' });
    const origin = request.headers.origin;
    if (origin && !hosts.has(origin.replace(/^https?:\/\//, '')))
      return json(response, 403, { error: 'origin not allowed' });
    if (request.headers['sec-fetch-site'] === 'cross-site')
      return json(response, 403, { error: 'cross-site request' });

    const override = this.overrides.get(url.pathname);
    if (override) {
      override(response);
      return;
    }
    if (request.method !== 'GET') {
      response.setHeader('Allow', 'GET');
      return error(response, 405, 'method-not-allowed', 'the consumer contract is read-only');
    }
    if (url.pathname === '/consumer/v1/discovery') return json(response, 200, this.discovery());
    if (request.headers.authorization !== `Bearer ${this.token}`) {
      response.setHeader('WWW-Authenticate', 'Bearer realm="salidium-consumer"');
      return json(response, 401, fixture('consumer-error-unauthorized'));
    }
    if (url.pathname === '/consumer/v1/sessions/lookup') return this.#lookup(response, url);
    if (url.pathname === '/consumer/v1/feed') return this.#feed(response);
    const report = /^\/consumer\/v1\/sessions\/([^/]+)\/report$/.exec(url.pathname);
    if (report?.[1]) {
      const found = this.reports.get(decodeURIComponent(report[1]));
      return found
        ? json(response, 200, found)
        : error(response, 404, 'not-found', 'no such session');
    }
    return error(response, 404, 'not-found', 'no such consumer endpoint');
  }

  #lookup(response: ServerResponse, url: URL): undefined {
    const provider = url.searchParams.get('provider');
    const sessionId = url.searchParams.get('sessionId');
    if (!provider || !sessionId)
      return error(response, 400, 'bad-request', 'provider and sessionId are required');
    for (const report of this.reports.values()) {
      const session = report.session as Json;
      const native = session.native as Json;
      if (native.provider === provider && native.sessionId === sessionId)
        return json(response, 200, {
          format: 'salidium.session-lookup',
          version: 1,
          generatedAt: report.generatedAt,
          session,
        });
    }
    return json(response, 404, fixture('consumer-error-session-not-observed'));
  }

  #feed(response: ServerResponse): undefined {
    response.writeHead(200, {
      'Content-Type': 'text/event-stream; charset=utf-8',
      'Cache-Control': 'no-store',
      Connection: 'keep-alive',
    });
    response.flushHeaders();
    response.write(`: salidium ${' '.repeat(2048)}\n\n`);
    this.feedConnections++;
    this.#feeds.add(response);
    response.on('close', () => this.#feeds.delete(response));
    if (this.resyncOnConnect)
      response.write(`data: ${JSON.stringify(fixture('session-feed-resync'))}\n\n`);
  }
}

function json(response: ServerResponse, status: number, body: unknown): undefined {
  response.statusCode = status;
  response.setHeader('Content-Type', 'application/json; charset=utf-8');
  response.end(JSON.stringify(body));
}

function error(response: ServerResponse, status: number, code: string, message: string): undefined {
  return json(response, status, {
    format: 'salidium.consumer-error',
    version: 1,
    error: code,
    message,
  });
}
