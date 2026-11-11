import { createServer, request as httpRequest, type Server } from 'node:http';
import { type AddressInfo, connect, type Socket } from 'node:net';

/**
 * The one way out of Halcyonic's sandbox for a confined Claude Code (ADR 0048, BOX-4): a proxy on
 * loopback that the profile lets Claude Code reach, passing only what goes to its API host. An
 * `https` host is reached through a CONNECT tunnel to exactly that host and port, so the TLS
 * session is Claude Code's own, end to end; a plain `http` host, as a gateway or a test's fake one
 * on this Mac, through absolute-form requests to exactly that host and port. Everything else is
 * refused with 403. Nothing of a request's headers or body is kept.
 */
export interface ApiGate {
  /** The proxy's address, for `HTTP_PROXY` and `HTTPS_PROXY`. */
  readonly url: string;
  /** Its port on loopback, the only one the profile lets Claude Code connect to. */
  readonly port: number;
  close(): Promise<void>;
}

/** The host and port the gate passes, from the API's address: its port, else 443 or 80. */
export function apiAuthority(target: URL): string {
  const port = target.port !== '' ? target.port : target.protocol === 'https:' ? '443' : '80';
  return `${target.hostname}:${port}`;
}

export async function startApiGate(target: URL): Promise<ApiGate> {
  if (target.protocol !== 'https:' && target.protocol !== 'http:') {
    throw new Error(`the API's address must be http or https, not ${target.protocol}`);
  }
  const authority = apiAuthority(target);
  const [host, port] = [
    target.hostname.replace(/^\[|\]$/g, ''),
    Number(authority.split(':').at(-1)),
  ];
  const sockets = new Set<Socket>();
  const server: Server = createServer((req, res) => {
    let url: URL | null = null;
    try {
      url = new URL(req.url ?? '');
    } catch {
      url = null;
    }
    // Plain HTTP only where the API itself is plain HTTP; an https API is reached by CONNECT alone.
    if (
      target.protocol !== 'http:' ||
      url === null ||
      url.protocol !== 'http:' ||
      apiAuthority(url) !== authority
    ) {
      res.writeHead(403).end();
      return;
    }
    const headers = { ...req.headers };
    delete headers['proxy-connection'];
    delete headers['proxy-authorization'];
    const upstream = httpRequest(
      { host, port, method: req.method, path: `${url.pathname}${url.search}`, headers },
      (answer) => {
        res.writeHead(answer.statusCode ?? 502, answer.headers);
        answer.pipe(res);
      },
    );
    upstream.on('error', () => {
      if (!res.headersSent) res.writeHead(502);
      res.end();
    });
    req.pipe(upstream);
  });
  server.on('connection', (socket) => {
    sockets.add(socket);
    socket.once('close', () => sockets.delete(socket));
  });
  server.on('connect', (req, socket: Socket) => {
    socket.on('error', () => socket.destroy());
    if (target.protocol !== 'https:' || req.url !== authority) {
      socket.end('HTTP/1.1 403 Forbidden\r\n\r\n');
      return;
    }
    const upstream = connect(port, host, () => {
      socket.write('HTTP/1.1 200 Connection Established\r\n\r\n');
      upstream.pipe(socket);
      socket.pipe(upstream);
    });
    sockets.add(upstream);
    upstream.once('close', () => sockets.delete(upstream));
    upstream.on('error', () => socket.destroy());
    socket.once('close', () => upstream.destroy());
  });
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve());
  });
  const listening = (server.address() as AddressInfo).port;
  return {
    url: `http://127.0.0.1:${listening}`,
    port: listening,
    close: () =>
      new Promise<void>((resolve) => {
        for (const socket of sockets) socket.destroy();
        server.close(() => resolve());
      }),
  };
}
