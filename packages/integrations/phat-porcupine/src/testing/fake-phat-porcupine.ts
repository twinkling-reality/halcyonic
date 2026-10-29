import { createHmac, randomBytes } from 'node:crypto';
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/** One of the fixtures Halcyonic wrote from Phat Porcupine's catalog contract v1, parsed. */
export function fixture(name: string): unknown {
  return JSON.parse(readFileSync(new URL(`../../fixtures/v1/${name}`, import.meta.url), 'utf8'));
}

/** A consumer credential in Phat Porcupine's form: `ppc_` and 32 random bytes in base64url. */
export function consumerToken(): string {
  return `ppc_${randomBytes(32).toString('base64url')}`;
}

export interface SeenRequest {
  readonly path: string;
  readonly authorization: string | undefined;
}

/**
 * A stand-in for Phat Porcupine's Node server speaking catalog contract v1 on loopback, for tests:
 * it publishes its discovery file in a temporary home of its own (directory 0700, file 0600), proves
 * the instance with an HMAC of each challenge, and serves a catalog to its one credential. Each
 * field below changes what it answers next, so a test can make it refuse, wait, or pose as another.
 */
export class FakePhatPorcupine {
  readonly home: string;
  readonly token = consumerToken();
  readonly instanceId = randomBytes(16).toString('hex');
  readonly secret = randomBytes(32);
  /** Every request, as received, for tests that check what was sent before the proof. */
  readonly requests: SeenRequest[] = [];
  /** The catalog it serves; the hand-written small fixture until a test sets another. */
  catalog: unknown = fixture('catalog-small.json');
  /** Answer the catalog with this status and fixture instead, as 503 before the first sync. */
  refuse: { status: number; body: unknown; headers?: Record<string, string> } | null = null;
  /** Answer discovery as another instance would: with a wrong proof, or another instance's id. */
  impersonate: 'proof' | 'instance' | null = null;
  /** The ETag of the catalog served; a matching If-None-Match gets 304. */
  etag = '"catalog-1"';
  sample = false;
  testGitHub = false;
  readonly #server: Server;

  private constructor(home: string, server: Server) {
    this.home = home;
    this.#server = server;
  }

  static async start(): Promise<FakePhatPorcupine> {
    const home = mkdtempSync(join(tmpdir(), 'halcyonic-phat-porcupine-'));
    chmodSync(home, 0o700);
    let fake: FakePhatPorcupine | null = null;
    const server = createServer((request, response) => fake?.handle(request, response));
    fake = new FakePhatPorcupine(home, server);
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    fake.publish();
    return fake;
  }

  get port(): number {
    return (this.#server.address() as AddressInfo).port;
  }

  get discoveryPath(): string {
    return join(this.home, 'consumer.json');
  }

  /** Writes the discovery file as the real server does at every start. */
  publish(change: (document: Record<string, unknown>) => void = () => {}): void {
    const document: Record<string, unknown> = {
      instanceId: this.instanceId,
      secret: this.secret.toString('base64url'),
      sample: this.sample,
      testGitHub: this.testGitHub,
      contracts: [
        {
          name: 'phat-porcupine.catalog',
          major: 1,
          minor: 0,
          baseUrl: `http://127.0.0.1:${this.port}/consumer/v1`,
        },
      ],
    };
    change(document);
    writeFileSync(this.discoveryPath, `${JSON.stringify(document)}\n`, { mode: 0o600 });
    chmodSync(this.discoveryPath, 0o600);
  }

  async close(): Promise<void> {
    await new Promise<void>((resolve) => this.#server.close(() => resolve()));
    rmSync(this.home, { recursive: true, force: true });
  }

  private handle(request: IncomingMessage, response: ServerResponse): void {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1');
    this.requests.push({ path: url.pathname, authorization: request.headers.authorization });
    const send = (status: number, body: unknown, headers: Record<string, string> = {}) => {
      response.writeHead(status, { 'content-type': 'application/json', ...headers });
      response.end(body === undefined ? undefined : JSON.stringify(body));
    };
    const error = (status: number, code: string) =>
      send(status, { error: { code, message: code } });
    if (request.headers.origin !== undefined || request.headers['sec-fetch-site'] !== undefined) {
      error(403, 'origin_not_allowed');
      return;
    }
    if (request.method !== 'GET') {
      error(405, 'method_not_allowed');
      return;
    }
    if (url.pathname === '/consumer/v1/discovery') {
      const challenge = Buffer.from(url.searchParams.get('challenge') ?? '', 'base64url');
      if (
        challenge.length !== 32 ||
        challenge.toString('base64url') !== url.searchParams.get('challenge')
      ) {
        error(400, 'bad_request');
        return;
      }
      const proof = createHmac(
        'sha256',
        this.impersonate === 'proof' ? randomBytes(32) : this.secret,
      )
        .update(challenge)
        .digest('base64url');
      send(200, {
        instanceId:
          this.impersonate === 'instance' ? randomBytes(16).toString('hex') : this.instanceId,
        sample: this.sample,
        testGitHub: this.testGitHub,
        contracts: [
          {
            name: 'phat-porcupine.catalog',
            major: 1,
            minor: 0,
            baseUrl: `http://127.0.0.1:${this.port}/consumer/v1`,
          },
        ],
        proof,
      });
      return;
    }
    if (url.pathname === '/consumer/v1/catalog') {
      if (request.headers.authorization !== `Bearer ${this.token}`) {
        send(401, fixture('error-unauthorized-401.json'));
        return;
      }
      if (this.refuse !== null) {
        send(this.refuse.status, this.refuse.body, this.refuse.headers);
        return;
      }
      const match = (request.headers['if-none-match'] ?? '')
        .split(',')
        .map((tag) => tag.trim().replace(/^W\//, ''));
      if (match.includes(this.etag) || match.includes('*')) {
        send(304, undefined, { etag: this.etag });
        return;
      }
      send(200, this.catalog, { etag: this.etag });
      return;
    }
    error(404, 'not_found');
  }
}
