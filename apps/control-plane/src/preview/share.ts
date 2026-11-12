import { type ChildProcess, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { promises as dns } from 'node:dns';
import { mkdir, readFile } from 'node:fs/promises';
import {
  createServer,
  request as httpRequest,
  type IncomingHttpHeaders,
  type OutgoingHttpHeaders,
  type Server,
} from 'node:http';
import { request } from 'node:https';
import type { LookupFunction } from 'node:net';
import { type NetworkInterfaceInfo, networkInterfaces } from 'node:os';
import { dirname } from 'node:path';
import type {
  CheckId,
  ProjectId,
  ShareStopReason,
  ShareWay,
  ShareWayState,
  Timestamp,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { WIFI_SHARE_PORT_FIRST, WIFI_SHARE_PORT_LAST } from '../config.ts';
import { type Cause, controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { heldInFolder, heldMessage } from '../core/held-in-tree.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import type { HeldSecret } from '../core/redaction.ts';
import type { Logger } from '../logger.ts';
import { SANDBOX_EXEC, subpaths } from '../seatbelt.ts';

/**
 * Sharing a checked game (keeping and sharing what was made, F1, ADR 0054), only ever what the
 * project's running preview serves: the checked build, from inside its own sandbox.
 * - A link: a temporary public tunnel, Cloudflare's quick tunnel by its pinned binary, run with a
 *   home of its own and allowed to write nothing else; it needs no account. Its address lives only
 *   while it runs, and is recorded only once a verified `https://` request gets the game.
 * - On the Wi-Fi: a listener on the Mac's own address on its local network that passes only reads
 *   (`GET`, `HEAD`) to the preview. A plain `http://` page is not a secure page, so a game opened
 *   from it cannot enter VR.
 */

/** The binary a link runs, by its SHA-256, and the release it came from (validation/sharing-a-link.md). */
export const TUNNEL_PIN = {
  version: '2026.10.7',
  sha256: '2136431d5eb467bf447d2ee514b713e192719b143aec1d9e09bf7dc290d5bb12',
} as const;

/** The ports a share on the Wi-Fi listens on beside 47800 (`portFamily` moves them with a plane). */
export { WIFI_SHARE_PORT_FIRST, WIFI_SHARE_PORT_LAST };

const ADDRESS = /https:\/\/[a-z0-9-]+\.trycloudflare\.com/;

/** A share that cannot start here, with words for the person and a code for the command. */
export class ShareRefused extends Error {
  readonly code: 'unavailable' | 'no_network' | 'holds_secret';
  constructor(code: 'unavailable' | 'no_network' | 'holds_secret', message: string) {
    super(message);
    this.code = code;
  }
}

/** Whether `binary` is the pinned tunnel, by its own bytes. */
export async function isPinnedTunnel(binary: string): Promise<boolean> {
  try {
    return (
      createHash('sha256')
        .update(await readFile(binary))
        .digest('hex') === TUNNEL_PIN.sha256
    );
  } catch {
    return false;
  }
}

export interface ProjectSharesDeps {
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly clock: Clock;
  readonly logger: Logger;
  /** The pinned tunnel binary, checked against `TUNNEL_PIN` before this is built; null, no links. */
  readonly tunnel: string | null;
  /** A folder of Halcyonic's own the tunnel uses as its home, and the only one it may write. */
  readonly home: string;
  /**
   * Halcyonic's data folder, with its journal and access token, which the tunnel may not read but
   * for its own home and the folder its binary is in.
   */
  readonly dataDir?: string;
  /** For tests: starts the tunnel to `port`. */
  readonly spawnTunnel?: (port: number) => ChildProcess;
  /** For tests: whether `url` answers with the game, waiting as long as its name takes to resolve. */
  readonly answers?: (url: string) => Promise<boolean>;
  /** How long a share may take to give its address and answer; 90 s by default. */
  readonly startTimeoutMs?: number;
  /** For tests: the Mac's address on its local network, or null when it is on none. */
  readonly lanAddress?: () => string | null;
  /** For tests: the ports a share on the Wi-Fi may listen on. */
  readonly wifiPorts?: readonly [number, number];
  /** The check's built copy the preview serves, by project and check, or null where it is not on disk. */
  readonly builtCopy?: (projectId: ProjectId, checkId: CheckId) => string | null;
  /** Every secret the host holds: a built copy holding one is never shared (the lead, 2026-11-12). */
  readonly held?: () => Iterable<HeldSecret>;
}

interface Share {
  readonly checkId: CheckId;
  /** Ends it; resolves once nothing of it listens or runs. */
  readonly end: () => Promise<void>;
  stopping: boolean;
}

interface Started {
  readonly share: Share;
  readonly url: string;
  /** Calls `then` once, when the tunnel or the listener ends. */
  readonly onEnd: (then: () => void) => void;
}

export class ProjectShares {
  readonly #deps: ProjectSharesDeps;
  readonly #shares = new Map<ProjectId, Share>();
  readonly #starting = new Set<ProjectId>();
  #closed = false;

  constructor(deps: ProjectSharesDeps) {
    this.#deps = deps;
  }

  /**
   * Ends any share a restart left recorded as live (no tunnel or listener survives one), then stops
   * a project's share whenever its preview stops.
   */
  attach(publisher: EventPublisher): () => void {
    for (const project of this.#deps.projection.projects()) {
      const share = this.#deps.projection.share(project.project_id);
      if (share !== undefined)
        this.#recordStopped(project.project_id, share.check_id, 'host_stopped', NO_CAUSE);
    }
    return publisher.subscribe(({ event }) => {
      if (event.event_type === 'project.preview.stopped')
        void this.stop(event.project_id, 'preview_stopped', NO_CAUSE);
    });
  }

  /** Shares the checked build the project's preview serves now; resolves once its address answers. */
  async start(
    projectId: ProjectId,
    way: ShareWay,
    cause: Cause,
  ): Promise<{ check_id: CheckId; url: string; way: ShareWay }> {
    if (this.#closed) throw new Error('the host is closing');
    if (this.#shares.has(projectId) || this.#starting.has(projectId))
      throw new Error('the game is already shared');
    const preview = this.#deps.projection.preview(projectId);
    if (preview === undefined) throw new Error('no preview to share');
    if (way === 'link' && this.#deps.tunnel === null && this.#deps.spawnTunnel === undefined)
      throw new ShareRefused('unavailable', 'This computer has no program for a shared link.');
    // Marked as starting before anything is awaited, so a second press waits for none of it (critic, 18:05Z).
    this.#starting.add(projectId);
    try {
      // What anyone with the address can read holds nothing the host keeps as a secret, as Send and Publish.
      const copy = this.#deps.builtCopy?.(projectId, preview.check_id) ?? null;
      if (copy !== null && this.#deps.held !== undefined) {
        let hit: Awaited<ReturnType<typeof heldInFolder>>;
        try {
          hit = await heldInFolder(copy, this.#deps.held());
        } catch {
          throw new ShareRefused(
            'unavailable',
            "The game's built files could not be read, so nothing was shared. Check the game again.",
          );
        }
        if (hit !== null) throw new ShareRefused('holds_secret', heldMessage(hit, 'shared'));
      }
      const { share, url, onEnd } =
        way === 'link'
          ? await this.#startLink(preview.port, preview.check_id)
          : await this.#startWifi(preview.port, preview.check_id);
      if (this.#closed || this.#deps.projection.preview(projectId)?.check_id !== preview.check_id) {
        share.stopping = true;
        await share.end();
        throw new Error('the preview or the host stopped');
      }
      this.#shares.set(projectId, share);
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.share.started',
          { project_id: projectId, workstream_id: null, execution_id: null },
          { check_id: preview.check_id, url, way },
          this.#now(),
          cause,
        ),
      );
      // A tunnel or listener that ends on its own stops the share.
      onEnd(() => {
        if (this.#shares.get(projectId) === share && !share.stopping) {
          this.#shares.delete(projectId);
          this.#recordStopped(projectId, share.checkId, 'ended', NO_CAUSE);
        }
      });
      return { check_id: preview.check_id, url, way };
    } finally {
      this.#starting.delete(projectId);
    }
  }

  /** Stops the project's share, if one runs; the address stops answering at once. */
  async stop(projectId: ProjectId, reason: ShareStopReason, cause: Cause): Promise<boolean> {
    const share = this.#shares.get(projectId);
    if (share === undefined) return false;
    share.stopping = true;
    this.#shares.delete(projectId);
    await share.end();
    this.#recordStopped(projectId, share.checkId, reason, cause);
    return true;
  }

  /**
   * Which ways of sharing work on this Mac now, as far as it can tell before a start: a link where
   * the pinned tunnel program is set up, the Wi-Fi where the Mac has a private network address.
   * Never a promise: a start that fails still says why.
   */
  ways(): { link: ShareWayState; wifi: ShareWayState } {
    const link: ShareWayState =
      this.#deps.tunnel !== null || this.#deps.spawnTunnel !== undefined ? 'ready' : 'not_set_up';
    let wifi: ShareWayState;
    try {
      wifi = (this.#deps.lanAddress ?? lanAddress)() === null ? 'no_network' : 'ready';
    } catch {
      wifi = 'unknown';
    }
    return { link, wifi };
  }

  /** Stops every share as the control plane closes. */
  async close(): Promise<void> {
    this.#closed = true;
    for (const projectId of [...this.#shares.keys()])
      await this.stop(projectId, 'host_stopped', NO_CAUSE);
  }

  async #startLink(port: number, checkId: CheckId): Promise<Started> {
    await mkdir(this.#deps.home, { recursive: true, mode: 0o700 });
    const child = (this.#deps.spawnTunnel ?? ((p) => this.#spawn(p)))(port);
    const share: Share = { checkId, end: () => stopChild(child), stopping: false };
    try {
      const url = await this.#address(child);
      const answers = this.#deps.answers ?? answersOverHttps;
      if (!(await withTimeout(answers(url), this.#timeout())))
        throw new Error('the shared address did not answer with the game');
      if (child.exitCode !== null || child.signalCode !== null)
        throw new Error('the tunnel stopped');
      return { share, url, onEnd: (then) => void child.once('exit', then) };
    } catch (error) {
      share.stopping = true;
      await share.end();
      throw error;
    }
  }

  async #startWifi(port: number, checkId: CheckId): Promise<Started> {
    const address = (this.#deps.lanAddress ?? lanAddress)();
    if (address === null)
      throw new ShareRefused('no_network', "This Mac isn't on a Wi-Fi network.");
    const [first, last] = this.#deps.wifiPorts ?? [WIFI_SHARE_PORT_FIRST, WIFI_SHARE_PORT_LAST];
    const server = await listenOnFirstFree(readsOnlyTo(port), address, first, last);
    const share: Share = { checkId, end: () => closeServer(server), stopping: false };
    try {
      const { port: listening } = server.address() as { port: number };
      const url = `http://${address}:${listening}`;
      if (!(await withTimeout(answersOverHttp(url), this.#timeout())))
        throw new Error('the address on the Wi-Fi did not answer with the game');
      return { share, url, onEnd: (then) => void server.once('close', then) };
    } catch (error) {
      share.stopping = true;
      await share.end();
      throw error;
    }
  }

  #recordStopped(projectId: ProjectId, checkId: CheckId, reason: ShareStopReason, cause: Cause) {
    if (this.#deps.projection.share(projectId)?.check_id !== checkId) return;
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.share.stopped',
        { project_id: projectId, workstream_id: null, execution_id: null },
        { check_id: checkId, reason },
        this.#now(),
        cause,
      ),
    );
  }

  /**
   * The tunnel to the preview on 127.0.0.1, its Host header rewritten to the preview's own so the
   * preview serves it as it serves this Mac; no update of itself, a home of its own, and, on macOS,
   * writes only there.
   */
  #spawn(port: number): ChildProcess {
    const tunnel = this.#deps.tunnel as string;
    const origin = `127.0.0.1:${port}`;
    const args = [
      'tunnel',
      '--no-autoupdate',
      '--url',
      `http://${origin}`,
      '--http-host-header',
      origin,
    ];
    const env = { PATH: '/usr/bin:/bin', HOME: this.#deps.home, LC_ALL: 'C' };
    if (process.platform !== 'darwin')
      return spawn(tunnel, args, { env, stdio: ['ignore', 'ignore', 'pipe'] });
    const profile = tunnelProfile(this.#deps.home, tunnel, this.#deps.dataDir);
    return spawn(SANDBOX_EXEC, ['-p', profile, tunnel, ...args], {
      env,
      stdio: ['ignore', 'ignore', 'pipe'],
    });
  }

  /** The address the tunnel prints, or a failure once it exits or the time is up. */
  #address(child: ChildProcess): Promise<string> {
    return new Promise((resolve, reject) => {
      let seen = '';
      const timer = setTimeout(() => {
        reject(new Error('the tunnel gave no address'));
      }, this.#timeout());
      child.stderr?.on('data', (chunk: Buffer) => {
        seen = (seen + chunk.toString()).slice(-8192);
        const found = ADDRESS.exec(seen)?.[0];
        if (found !== undefined) {
          clearTimeout(timer);
          resolve(found);
        }
      });
      child.once('exit', () => {
        clearTimeout(timer);
        reject(new Error('the tunnel exited'));
      });
      child.once('error', (error) => {
        clearTimeout(timer);
        reject(error);
      });
    });
  }

  #timeout(): number {
    return this.#deps.startTimeoutMs ?? 90_000;
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString() as Timestamp;
  }
}

/**
 * The places the tunnel looks for a configuration besides its home (its own log names them), none
 * of which a share may read: a configuration there could add ingress rules or another origin to a
 * share (AUDIT2 S1). `/etc` is `/private/etc` to the sandbox.
 */
export const TUNNEL_CONFIG_FOLDERS = [
  '/private/etc/cloudflared',
  '/usr/local/etc/cloudflared',
  '/opt/homebrew/etc/cloudflared',
] as const;

/**
 * The tunnel's Seatbelt profile: it writes only its own home and `/dev/null`, reads no configuration
 * but its home's (empty), and reads nothing of Halcyonic's data folder but that home and the folder
 * its binary is in.
 */
export function tunnelProfile(
  home: string,
  tunnel: string,
  dataDir?: string,
  configFolders: readonly string[] = TUNNEL_CONFIG_FOLDERS,
): string {
  return [
    '(version 1)',
    '(allow default)',
    '(deny file-write*)',
    `(allow file-write* ${subpaths([home])} (literal "/dev/null"))`,
    `(deny file-read* ${configFolders.map((folder) => `(subpath "${folder}")`).join(' ')})`,
    ...(dataDir === undefined
      ? []
      : [
          `(deny file-read* ${subpaths([dataDir])})`,
          `(allow file-read* ${subpaths([home, dirname(tunnel)])})`,
        ]),
    '',
  ].join('\n');
}

/**
 * The Mac's IPv4 address on a private local network (10/8, 172.16/12, 192.168/16), its Wi-Fi first
 * (`en0` on a Mac), or null when it is on none.
 */
export function lanAddress(
  interfaces: NodeJS.Dict<NetworkInterfaceInfo[]> = networkInterfaces(),
): string | null {
  const names = Object.keys(interfaces).sort((a, b) =>
    a === 'en0' ? -1 : b === 'en0' ? 1 : a.localeCompare(b),
  );
  for (const name of names)
    for (const entry of interfaces[name] ?? [])
      if (entry.family === 'IPv4' && !entry.internal && isPrivateIpv4(entry.address))
        return entry.address;
  return null;
}

function isPrivateIpv4(address: string): boolean {
  const [a = -1, b = -1] = address.split('.').map(Number);
  return a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168);
}

/** The request headers passed to the preview: what a page load reads with, nothing else. */
const PASSED_HEADERS = [
  'accept',
  'accept-encoding',
  'accept-language',
  'if-modified-since',
  'if-none-match',
  'range',
  'user-agent',
];

/** The response headers never passed back: they belong to one connection. */
const HOP_HEADERS = new Set(['connection', 'keep-alive', 'proxy-connection', 'upgrade']);

/**
 * A listener that passes only reads to the preview on 127.0.0.1:`port`, under the preview's own
 * Host, with no body and only `PASSED_HEADERS`; anything else is refused with 405.
 */
export function readsOnlyTo(port: number): Server {
  const server = createServer((req, res) => {
    if (req.method !== 'GET' && req.method !== 'HEAD') {
      res.writeHead(405, { allow: 'GET, HEAD' }).end();
      return;
    }
    const path = req.url ?? '/';
    if (!path.startsWith('/')) {
      res.writeHead(400).end();
      return;
    }
    const headers: OutgoingHttpHeaders = { host: `127.0.0.1:${port}` };
    for (const name of PASSED_HEADERS) {
      const value = req.headers[name];
      if (value !== undefined) headers[name] = value;
    }
    const upstream = httpRequest(
      { host: '127.0.0.1', port, method: req.method, path, headers },
      (answer) => {
        res.writeHead(answer.statusCode ?? 502, withoutHopHeaders(answer.headers));
        answer.pipe(res);
      },
    );
    upstream.on('error', () => {
      if (!res.headersSent) res.writeHead(502);
      res.end();
    });
    res.on('close', () => upstream.destroy());
    upstream.end();
  });
  // As the network listener bounds its own (AUDIT2 S2): anyone on the network may connect.
  server.requestTimeout = 30_000;
  server.headersTimeout = 10_000;
  server.keepAliveTimeout = 5_000;
  server.maxConnections = 32;
  return server;
}

function withoutHopHeaders(headers: IncomingHttpHeaders): OutgoingHttpHeaders {
  const kept: OutgoingHttpHeaders = {};
  for (const [name, value] of Object.entries(headers))
    if (value !== undefined && !HOP_HEADERS.has(name)) kept[name] = value;
  return kept;
}

/** Listens on `host` at the first of `first` to `last` the system lets it. */
async function listenOnFirstFree(
  server: Server,
  host: string,
  first: number,
  last: number,
): Promise<Server> {
  for (let port = first; port <= last; port += 1) {
    const listening = await new Promise<boolean>((resolve) => {
      const failed = () => resolve(false);
      server.once('error', failed);
      server.listen(port, host, () => {
        server.off('error', failed);
        resolve(true);
      });
    });
    if (listening) return server;
  }
  throw new Error('no port for the share on the Wi-Fi');
}

function closeServer(server: Server): Promise<void> {
  if (!server.listening) return Promise.resolve();
  return new Promise((resolve) => {
    server.close(() => resolve());
    server.closeAllConnections();
  });
}

/** Whether `url` answers 200 over plain `http://`, as a device on the Wi-Fi asks it. */
function answersOverHttp(url: string): Promise<boolean> {
  return new Promise((resolve) => {
    const req = httpRequest(`${url}/`, { method: 'GET', timeout: 10_000 }, (res) => {
      res.resume();
      resolve(res.statusCode === 200);
    });
    req.on('timeout', () => req.destroy());
    req.on('error', () => resolve(false));
    req.end();
  });
}

/**
 * Whether `url` answers 200 over a verified `https://`, its name resolved by Cloudflare's own public
 * resolver, which knows a new tunnel's name within seconds, where this Mac's may not yet.
 */
async function answersOverHttps(url: string): Promise<boolean> {
  const host = new URL(url).hostname;
  const resolver = new dns.Resolver();
  resolver.setServers(['1.1.1.1', '1.0.0.1']);
  for (let tries = 0; tries < 30; tries += 1) {
    const address = await resolver.resolve4(host).then(
      (found) => found[0],
      () => undefined,
    );
    if (address !== undefined && (await getsGame(url, address))) return true;
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  return false;
}

function getsGame(url: string, address: string): Promise<boolean> {
  return new Promise((resolve) => {
    const req = request(
      url,
      { method: 'GET', timeout: 10_000, lookup: pinnedLookup(address) },
      (res) => {
        res.resume();
        resolve(res.statusCode === 200);
      },
    );
    req.on('timeout', () => req.destroy());
    req.on('error', () => resolve(false));
    req.end();
  });
}

/**
 * A lookup that answers every name with `address`. Node asks for a list (`all`) where it may try
 * several addresses, and refuses a lone address then, so both forms are answered.
 */
export function pinnedLookup(address: string): LookupFunction {
  return (_host, options, callback) => {
    if (options.all) callback(null, [{ address, family: 4 }]);
    else callback(null, address, 4);
  };
}

function withTimeout(promise: Promise<boolean>, ms: number): Promise<boolean> {
  return new Promise((resolve) => {
    const timer = setTimeout(() => resolve(false), ms);
    promise.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      () => {
        clearTimeout(timer);
        resolve(false);
      },
    );
  });
}

function stopChild(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return Promise.resolve();
  return new Promise((resolve) => {
    const timer = setTimeout(() => child.kill('SIGKILL'), 5000);
    child.once('exit', () => {
      clearTimeout(timer);
      resolve();
    });
    child.kill('SIGTERM');
  });
}
