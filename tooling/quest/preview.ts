/**
 * Which preview the device tool may open on the headset (M1.6a): only the host's own server of a
 * project's latest check, when that check passed and recorded that every agent in the project ran
 * with its reads confined to the project's folder (`reads_confined`, ADR 0041). The journal says
 * which check the preview serves; the Mac says the server on its port is the host's and serves that
 * copy. The headset then opens it by IP, `http://127.0.0.1:<port>/`, never by `localhost`, which
 * another listener could also answer to.
 *
 * The journal is read through the control plane's API with the access token, sent only to a
 * server that proves just before each request that it holds it (SECURITY.md). The token is never
 * printed.
 */
import { execFile } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { defaultDataDir, loopbackListener } from '../../apps/control-plane/src/config.ts';
import {
  ACCESS_TOKEN_FILE,
  fetchWithProof,
  loopbackBases,
  provenBase,
  TokenNotSent,
} from '../../apps/control-plane/src/http/security.ts';
import {
  type EventsResponse,
  PREVIEW_PORT_FIRST,
  PREVIEW_PORT_LAST,
  type StoredEvent,
} from '../../packages/contracts/src/index.ts';
import { Projection } from '../../packages/domain/src/index.ts';

export type Gate =
  | { readonly ok: true; readonly port: number; readonly checkId: string; readonly url: string }
  | { readonly ok: false; readonly reason: string };

const PROJECT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

const refuse = (reason: string): Gate => ({ ok: false, reason });

/** The ports a preview may use: the contract's range unless a test names its own. */
export interface PreviewPorts {
  readonly first: number;
  readonly last: number;
}

const CONTRACT_PORTS: PreviewPorts = { first: PREVIEW_PORT_FIRST, last: PREVIEW_PORT_LAST };

/**
 * Whether the project's preview may open on the headset, from the journal's `events` and the host's
 * files in `dataDir`: its preview record and the check's built copy. `ports` is the contract's range;
 * only tests name another, so that their stand-in servers never hold a port the host hands out.
 */
export async function confinedPreview(
  projectId: string,
  events: readonly StoredEvent[],
  dataDir: string,
  ports: PreviewPorts = CONTRACT_PORTS,
): Promise<Gate> {
  // The id names files below the data directory: nothing but an id gets that far.
  if (!PROJECT_ID.test(projectId)) return refuse(`${projectId} is not a project id.`);
  const projection = new Projection();
  for (const stored of events) projection.apply(stored);
  const preview = projection.preview(projectId);
  if (preview === undefined) return refuse('no preview runs for this project.');
  const check = projection.latestCheck(projectId);
  if (check === undefined || check.check_id !== preview.check_id)
    return refuse('the preview serves a copy that is not the latest check.');
  if (!check.findings.passed) return refuse('the check it serves did not pass.');
  if (check.findings.reads_confined !== true)
    return refuse("the agents' reads were not confined to the project's folder.");
  const port = preview.port;
  if (!Number.isInteger(port) || port < ports.first || port > ports.last)
    return refuse(`port ${port} is outside ${ports.first} to ${ports.last}.`);

  // The host writes this record before its server serves, and removes it when the server stops.
  let record: { pid?: unknown; port?: unknown; check_id?: unknown };
  try {
    record = JSON.parse(await readFile(join(dataDir, 'previews', `${projectId}.json`), 'utf8'));
  } catch {
    return refuse("the host has no record of this preview's server.");
  }
  if (record.check_id !== check.check_id || record.port !== port || typeof record.pid !== 'number')
    return refuse("the host's record names another preview.");

  let expected: Buffer;
  try {
    expected = await readFile(
      join(dataDir, 'checks', projectId, check.check_id, 'dist', 'index.html'),
    );
  } catch {
    return refuse('the checked copy is gone.');
  }
  const before = await listenersOf(port);
  if (before.length === 0) return refuse(`nothing listens on 127.0.0.1:${port}.`);
  if (before.some((pid) => pid !== record.pid))
    return refuse(`what listens on 127.0.0.1:${port} is not the host's preview server.`);
  const page = await indexOf(port);
  if (page === null || !page.body.equals(expected))
    return refuse(`127.0.0.1:${port} does not serve the checked copy.`);
  // The headset's cache keeps one origin's pages across projects on the same port.
  if (!noStore(page.cacheControl))
    return refuse(
      `127.0.0.1:${port} does not send Cache-Control: no-store, so the headset could show an earlier check's copy from its cache.`,
    );
  // Every preview of every project that later gets this port shares the headset browser's origin
  // http://127.0.0.1:<port>: a service worker one registered could answer the next one's loads with
  // bytes this gate never checked, so the server must refuse to serve a service worker script.
  // At / and at a path no starter has: the server must refuse the script wherever it is asked for.
  const refusals = await Promise.all(WORKER_PROBES.map((path) => refusesWorkerScript(port, path)));
  if (refusals.some((refused) => !refused))
    return refuse(
      `127.0.0.1:${port} does not refuse a service worker script, which could answer later previews on this address.`,
    );
  // Still the host's once it answered: nothing took the port in between.
  const after = await listenersOf(port);
  if (after.length === 0 || after.some((pid) => pid !== record.pid))
    return refuse(`what listens on 127.0.0.1:${port} changed while it was checked.`);
  return { ok: true, port, checkId: check.check_id, url: previewUrl(port) };
}

/** Where the headset opens a preview: by IP, never `localhost`. */
export function previewUrl(port: number): string {
  return `http://127.0.0.1:${port}/`;
}

/** Every event of the journal, a page at a time, each page from a server that proved it holds the token. */
export async function journalEvents(
  fetchImpl: typeof fetch,
  base: string,
  token: string,
  pageSize = 200,
): Promise<StoredEvent[]> {
  const events: StoredEvent[] = [];
  let after = 0;
  for (;;) {
    const response = await fetchWithProof(
      fetchImpl,
      base,
      token,
      `/api/events?after=${after}&limit=${pageSize}`,
    );
    if (!response.ok)
      throw new Error(`The control plane answered ${response.status} for its journal.`);
    const page = (await response.json()) as EventsResponse;
    for (const stored of page.events) events.push(stored);
    const last = page.events.at(-1);
    if (last === undefined || last.position >= page.head) return events;
    after = last.position;
  }
}

/**
 * The gate against the control plane this Mac runs: its data directory and port as the control
 * plane reads them (`HALCYONIC_DATA_DIR`, `HALCYONIC_HOST`, `HALCYONIC_PORT`).
 */
export async function controlPlaneGate(
  projectId: string,
  env: NodeJS.ProcessEnv = process.env,
): Promise<Gate> {
  if (!PROJECT_ID.test(projectId)) return refuse(`${projectId} is not a project id.`);
  const dataDir = defaultDataDir(env);
  const { host, port } = loopbackListener(env);
  let token: string;
  try {
    token = (await readFile(join(dataDir, ACCESS_TOKEN_FILE), 'utf8')).trim();
  } catch {
    return refuse(`there is no access token in ${dataDir}.`);
  }
  const proven = await provenBase(fetch, loopbackBases(host, port), token);
  if (typeof proven !== 'object') return refuse(new TokenNotSent(proven, `port ${port}`).message);
  return confinedPreview(projectId, await journalEvents(fetch, proven.base, token), dataDir);
}

/**
 * Whether something on the headset listens on `port`, from its /proc/net/tcp and tcp6: a local
 * address ending in the port in hex, in state 0A (LISTEN), any uid.
 */
export function listensOn(procNet: string, port: number): boolean {
  const hex = port.toString(16).toUpperCase().padStart(4, '0');
  return procNet.split(/\r?\n/).some((line) => {
    const fields = line.trim().split(/\s+/);
    return /^\d+:$/.test(fields[0] ?? '') && fields[1]?.endsWith(`:${hex}`) && fields[3] === '0A';
  });
}

/** The pids listening on 127.0.0.1:port, by lsof; none when nothing listens. */
function listenersOf(port: number): Promise<number[]> {
  return new Promise((resolve) => {
    execFile(
      '/usr/sbin/lsof',
      ['-nP', `-iTCP@127.0.0.1:${port}`, '-sTCP:LISTEN', '-Fp'],
      (_error, stdout) =>
        resolve([...String(stdout).matchAll(/^p(\d+)$/gm)].map((match) => Number(match[1]))),
    );
  });
}

/** Where the gate asks for a service worker script: the page, and a path no starter file has. */
const WORKER_PROBES = ['/', '/halcyonic-sw-probe.js'] as const;

/** Whether a Cache-Control value carries the no-store directive. */
function noStore(cacheControl: string | null): boolean {
  return (cacheControl ?? '')
    .split(',')
    .some((directive) => directive.trim().toLowerCase() === 'no-store');
}

/**
 * Whether 127.0.0.1:port refuses (4xx) at `path` a request that a browser sends when it fetches a
 * service worker script (`Service-Worker: script`); anything else, an error included, counts as
 * not refused.
 */
async function refusesWorkerScript(port: number, path: string): Promise<boolean> {
  try {
    const response = await fetch(`http://127.0.0.1:${port}${path}`, {
      headers: { 'service-worker': 'script' },
      redirect: 'error',
      signal: AbortSignal.timeout(3000),
    });
    await response.arrayBuffer();
    return response.status >= 400 && response.status < 500;
  } catch {
    return false;
  }
}

/** What 127.0.0.1:port answers for `/`: its bytes and its Cache-Control, or null. */
async function indexOf(
  port: number,
): Promise<{ body: Buffer; cacheControl: string | null } | null> {
  try {
    const response = await fetch(previewUrl(port), {
      redirect: 'error',
      signal: AbortSignal.timeout(3000),
    });
    if (!response.ok) return null;
    const body = Buffer.from(await response.arrayBuffer());
    return { body, cacheControl: response.headers.get('cache-control') };
  } catch {
    return null;
  }
}
