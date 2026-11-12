import { randomBytes, timingSafeEqual } from 'node:crypto';
import {
  createServer,
  request as httpRequest,
  type IncomingHttpHeaders,
  type Server,
} from 'node:http';
import { request as httpsRequest } from 'node:https';
import type { AddressInfo, Socket } from 'node:net';

/**
 * The one way out of Halcyonic's closed sandbox for a confined agent app (ADR 0048, BOX-4), and the
 * only holder of its key (VRIFY, 2026-11-12): a gateway on loopback that the profile lets the app
 * reach. The app is pointed at the gate as its API's base address and holds, in place of the key, a
 * credential the gate made for that one run: random, checked on every request, worthless anywhere
 * else and revoked when the run ends (AUDIT3, 2026-11-12). The gate takes a request only when its
 * Host is the gate's own loopback address and it carries a live run credential in the declared
 * header; it then drops every credential the app sent, adds the person's own key in the header the
 * app's adapter declares (`KeyGateDeclaration`), and sends it to the declared upstream, streaming the
 * answer back unbuffered. Anything else (no credential or a wrong, revoked or another gate's one, a
 * foreign Host, a proxied request for another host, a tunnel) is refused with 403 before the key is
 * touched, and a refusal says nothing of it. Nothing of a request's headers or body is kept. One mechanism for
 * every agent app that takes a key: what differs between apps is their declaration alone.
 */
export interface ApiGate {
  /** The gate's own address: the app's API base address, and its `HTTP_PROXY` and `HTTPS_PROXY`. */
  readonly url: string;
  /** Its port on loopback, the only one the profile lets the app connect to. */
  readonly port: number;
  /** A new run's credential: random, live until revoked or the gate closes. */
  issue(): string;
  /** Ends a run's credential: every later request with it is refused. */
  revoke(credential: string): void;
  close(): Promise<void>;
}

/** The credential the gate adds to each request: the header the adapter declares, and its whole value. */
export interface GateCredential {
  readonly header: string;
  readonly value: string;
}

/** The start of every run credential the gate makes, so one reads as the gate's, never as a key. */
export const RUN_CREDENTIAL_PREFIX = 'halcyonic-gate-run-';

/** How the app presents its run credential: in the declared header, after `Bearer ` or as it is. */
export interface PresentedCredential {
  readonly header: string;
  readonly bearer: boolean;
}

/**
 * How an agent app takes a key, as its adapter declares it (R2): which variable it reads its API's
 * base address from and which its key, any other variable it would take a credential from, the
 * header its API takes the key in (written after `Bearer ` or as it is), and the API's address when
 * the plane names none. The gate and the agent's environment are made from it alone.
 */
export interface KeyGateDeclaration {
  readonly baseVariable: string;
  readonly keyVariable: string;
  readonly otherCredentials: readonly string[];
  readonly header: string;
  readonly bearer: boolean;
  readonly upstream: string;
}

/**
 * A hosted model's provider as an agent app reaches it through the gate (VRIFY, 2026-11-12): what the
 * host declares of it (its upstream, the header and scheme its key travels in, its models) and the
 * person's stored key, which only the gate ever holds. The app is pointed at the gate and given a run
 * credential in the key's place.
 */
export interface GatedProvider {
  /** Opaque, stable, lowercase: the provider's id, as the app names its models (`<id>/<model>`). */
  readonly id: string;
  /** The provider's name, as the host declares it. */
  readonly name: string;
  /** The provider's API address, a path of its own included (for example `https://host/v1`). */
  readonly upstream: string;
  /** The header its key travels in, and whether it is written after `Bearer `. */
  readonly header: string;
  readonly bearer: boolean;
  /** The person's key: given to the gate alone. */
  readonly key: string;
  /** The model ids it offers. */
  readonly models: readonly string[];
}

/**
 * Opens the gate for a hosted provider: its key added in the provider's declared header, a run credential taken the
 * way the app presents a key (`presented`, the app's own: an OpenAI-compatible client sends `Authorization: Bearer`
 * whatever the provider declares), so a provider's header and an app's way of sending are never confused.
 */
export function startProviderGate(
  provider: GatedProvider,
  presented: PresentedCredential,
): Promise<ApiGate> {
  return startApiGate(
    new URL(provider.upstream),
    { header: provider.header, value: provider.bearer ? `Bearer ${provider.key}` : provider.key },
    presented,
  );
}

/** The person's key for the app, from the plane's environment where its declaration says; null for none. */
export function declaredKey(
  declaration: KeyGateDeclaration,
  environment: Readonly<Record<string, string | undefined>>,
): string | null {
  const key = (environment[declaration.keyVariable] ?? '').trim();
  return key === '' ? null : key;
}

/** The API the gate sends to: the plane's own base address for the app where set, else the declared one. */
export function declaredUpstream(
  declaration: KeyGateDeclaration,
  environment: Readonly<Record<string, string | undefined>>,
): URL {
  const base = (environment[declaration.baseVariable] ?? '').trim();
  return new URL(base === '' ? declaration.upstream : base);
}

/** The credential the gate adds for `key`, as the declaration writes it. */
export function declaredCredential(declaration: KeyGateDeclaration, key: string): GateCredential {
  return { header: declaration.header, value: declaration.bearer ? `Bearer ${key}` : key };
}

/**
 * The confined app's environment as the gate wants it: its base address the gate's, its key the run's
 * own credential from the gate, every other credential variable it declares gone.
 */
export function gatedEnvironment(
  declaration: KeyGateDeclaration,
  gateUrl: string,
  runCredential: string,
  environment: Record<string, string | undefined>,
): Record<string, string | undefined> {
  const out: Record<string, string | undefined> = {
    ...environment,
    [declaration.baseVariable]: gateUrl,
    [declaration.keyVariable]: runCredential,
  };
  for (const name of declaration.otherCredentials) delete out[name];
  return out;
}

/** Opens the gate for an app's declaration and the plane's environment; refused where no key is declared there. */
export function startKeyGate(
  declaration: KeyGateDeclaration,
  environment: Readonly<Record<string, string | undefined>>,
): Promise<ApiGate> {
  const key = declaredKey(declaration, environment);
  if (key === null) return Promise.reject(new Error('there is no key for the gate to add'));
  return startApiGate(
    declaredUpstream(declaration, environment),
    declaredCredential(declaration, key),
    { header: declaration.header, bearer: declaration.bearer },
  );
}

/** Headers the app sends that never pass the gate: its credentials, the hop's own, and the proxy's. */
const DROPPED = new Set([
  'authorization',
  'x-api-key',
  'proxy-authorization',
  'proxy-connection',
  'connection',
  'keep-alive',
  'host',
  'te',
  'trailer',
  'upgrade',
]);

/** The host and port an address names: its port, else 443 or 80. */
export function apiAuthority(target: URL): string {
  const port = target.port !== '' ? target.port : target.protocol === 'https:' ? '443' : '80';
  return `${target.hostname}:${port}`;
}

/**
 * The path the upstream is asked for: the upstream's own path (a gateway's prefix) before the
 * request's, which the app made against the gate's root; null for a request not to the gate.
 */
export function upstreamPath(
  requestUrl: string,
  gateAuthority: string,
  upstream: URL,
): string | null {
  let path: string;
  if (requestUrl.startsWith('/')) path = requestUrl;
  else {
    let url: URL;
    try {
      url = new URL(requestUrl);
    } catch {
      return null;
    }
    // A proxied request passes only when it is for the gate's own address.
    if (url.protocol !== 'http:' || apiAuthority(url) !== gateAuthority) return null;
    path = `${url.pathname}${url.search}`;
  }
  const prefix = upstream.pathname.replace(/\/+$/, '');
  return `${prefix}${path}`;
}

/** The request's headers as they go upstream: no credential of the app's, the upstream's host, and the declared one. */
export function upstreamHeaders(
  headers: IncomingHttpHeaders,
  upstream: URL,
  credential: GateCredential,
): Record<string, string | string[]> {
  const out: Record<string, string | string[]> = {};
  for (const [name, value] of Object.entries(headers)) {
    if (value === undefined || DROPPED.has(name.toLowerCase())) continue;
    out[name] = value;
  }
  out.host = upstream.host;
  out[credential.header.toLowerCase()] = credential.value;
  return out;
}

/** The run credential a request presents in `presented`'s header, or null for none. */
function presentedCredential(
  headers: IncomingHttpHeaders,
  presented: PresentedCredential,
): string | null {
  const raw = headers[presented.header.toLowerCase()];
  const value = Array.isArray(raw) ? null : (raw ?? null);
  if (value === null) return null;
  if (!presented.bearer) return value;
  return value.startsWith('Bearer ') ? value.slice('Bearer '.length) : null;
}

/** Whether `candidate` is one of `live`, each compared in constant time. */
function isLive(candidate: string, live: ReadonlySet<string>): boolean {
  const given = Buffer.from(candidate);
  let found = false;
  for (const each of live) {
    const known = Buffer.from(each);
    if (known.length === given.length && timingSafeEqual(known, given)) found = true;
  }
  return found;
}

export async function startApiGate(
  upstream: URL,
  credential: GateCredential,
  presented: PresentedCredential,
): Promise<ApiGate> {
  if (upstream.protocol !== 'https:' && upstream.protocol !== 'http:') {
    throw new Error(`the API's address must be http or https, not ${upstream.protocol}`);
  }
  if (!/^[a-z0-9-]{1,64}$/i.test(credential.header) || credential.value === '') {
    throw new Error('the gate needs a credential header and its value');
  }
  if (!/^[a-z0-9-]{1,64}$/i.test(presented.header))
    throw new Error('the gate needs a header to check');
  const send = upstream.protocol === 'https:' ? httpsRequest : httpRequest;
  const sockets = new Set<Socket>();
  const live = new Set<string>();
  let authority = '';
  const server: Server = createServer((req, res) => {
    // Refused before the key is touched: a foreign Host (a page's rebinding), no live run credential,
    // or a request not to the gate itself.
    const path = upstreamPath(req.url ?? '', authority, upstream);
    const given = presentedCredential(req.headers, presented);
    if (req.headers.host !== authority || path === null || given === null || !isLive(given, live)) {
      req.resume();
      res.writeHead(403).end();
      return;
    }
    const out = send(
      {
        protocol: upstream.protocol,
        hostname: upstream.hostname.replace(/^\[|\]$/g, ''),
        port: upstream.port === '' ? undefined : Number(upstream.port),
        method: req.method,
        path,
        headers: upstreamHeaders(req.headers, upstream, credential),
      },
      (answer) => {
        res.writeHead(answer.statusCode ?? 502, answer.headers);
        // A streamed answer goes on as it comes (the gateway protocol: never buffered).
        res.flushHeaders();
        answer.pipe(res);
      },
    );
    out.on('error', () => {
      if (!res.headersSent) res.writeHead(502);
      res.end();
    });
    req.pipe(out);
  });
  server.on('connection', (socket) => {
    sockets.add(socket);
    socket.once('close', () => sockets.delete(socket));
  });
  // A tunnel would carry the app's own TLS past the gate, with whatever credential it holds: never.
  server.on('connect', (_req, socket: Socket) => {
    socket.on('error', () => socket.destroy());
    socket.end('HTTP/1.1 403 Forbidden\r\n\r\n');
  });
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve());
  });
  const listening = (server.address() as AddressInfo).port;
  authority = `127.0.0.1:${listening}`;
  return {
    url: `http://127.0.0.1:${listening}`,
    port: listening,
    issue: () => {
      const made = `${RUN_CREDENTIAL_PREFIX}${randomBytes(32).toString('hex')}`;
      live.add(made);
      return made;
    },
    revoke: (made) => {
      live.delete(made);
    },
    close: () =>
      new Promise<void>((resolve) => {
        live.clear();
        for (const socket of sockets) socket.destroy();
        server.close(() => resolve());
      }),
  };
}
