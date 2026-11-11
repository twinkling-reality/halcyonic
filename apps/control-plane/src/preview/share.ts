import { type ChildProcess, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { promises as dns } from 'node:dns';
import { mkdir, readFile } from 'node:fs/promises';
import { request } from 'node:https';
import type { LookupFunction } from 'node:net';
import { dirname } from 'node:path';
import type { CheckId, ProjectId, ShareStopReason, Timestamp } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { type Cause, controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import type { Logger } from '../logger.ts';
import { SANDBOX_EXEC, subpaths } from '../seatbelt.ts';

/**
 * Sharing a checked game at an address anyone can open (keeping and sharing what was made, F1):
 * a temporary public tunnel to the project's running preview, which serves only the checked build
 * from inside its own sandbox. The tunnel is Cloudflare's quick tunnel, by its pinned binary, run
 * with a home of its own and allowed to write nothing else; it needs no account. Its address lives
 * only while it runs, and is recorded only once a verified `https://` request gets the game.
 */

/** The binary a share runs, by its SHA-256, and the release it came from (validation/sharing-a-link.md). */
export const TUNNEL_PIN = {
  version: '2026.10.7',
  sha256: '2136431d5eb467bf447d2ee514b713e192719b143aec1d9e09bf7dc290d5bb12',
} as const;

const ADDRESS = /https:\/\/[a-z0-9-]+\.trycloudflare\.com/;

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
  /** The pinned tunnel binary, checked against `TUNNEL_PIN` before this is built. */
  readonly tunnel: string;
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
  /** How long the tunnel may take to give its address and answer; 90 s by default. */
  readonly startTimeoutMs?: number;
}

interface Share {
  readonly child: ChildProcess;
  readonly checkId: CheckId;
  stopping: boolean;
}

export class ProjectShares {
  readonly #deps: ProjectSharesDeps;
  readonly #shares = new Map<ProjectId, Share>();
  #closed = false;

  constructor(deps: ProjectSharesDeps) {
    this.#deps = deps;
  }

  /**
   * Ends any share a restart left recorded as live (no tunnel survives one), then stops a project's
   * share whenever its preview stops.
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
  async start(projectId: ProjectId, cause: Cause): Promise<{ check_id: CheckId; url: string }> {
    if (this.#closed) throw new Error('the host is closing');
    if (this.#shares.has(projectId)) throw new Error('the game is already shared');
    const preview = this.#deps.projection.preview(projectId);
    if (preview === undefined) throw new Error('no preview to share');
    await mkdir(this.#deps.home, { recursive: true, mode: 0o700 });
    const child = (this.#deps.spawnTunnel ?? ((port) => this.#spawn(port)))(preview.port);
    const share: Share = { child, checkId: preview.check_id, stopping: false };
    this.#shares.set(projectId, share);
    try {
      const url = await this.#address(child);
      const answers = this.#deps.answers ?? answersOverHttps;
      if (!(await withTimeout(answers(url), this.#timeout())))
        throw new Error('the shared address did not answer with the game');
      if (child.exitCode !== null || share.stopping) throw new Error('the tunnel stopped');
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.share.started',
          { project_id: projectId, workstream_id: null, execution_id: null },
          { check_id: preview.check_id, url },
          this.#now(),
          cause,
        ),
      );
      // A tunnel that ends on its own stops the share.
      child.once('exit', () => {
        if (this.#shares.get(projectId) === share && !share.stopping) {
          this.#shares.delete(projectId);
          this.#recordStopped(projectId, share.checkId, 'tunnel_exited', NO_CAUSE);
        }
      });
      return { check_id: preview.check_id, url };
    } catch (error) {
      share.stopping = true;
      this.#shares.delete(projectId);
      child.kill('SIGTERM');
      throw error;
    }
  }

  /** Stops the project's share, if one runs; the address stops answering at once. */
  async stop(projectId: ProjectId, reason: ShareStopReason, cause: Cause): Promise<boolean> {
    const share = this.#shares.get(projectId);
    if (share === undefined) return false;
    share.stopping = true;
    this.#shares.delete(projectId);
    await stopChild(share.child);
    this.#recordStopped(projectId, share.checkId, reason, cause);
    return true;
  }

  /** Stops every share as the control plane closes. */
  async close(): Promise<void> {
    this.#closed = true;
    for (const projectId of [...this.#shares.keys()])
      await this.stop(projectId, 'host_stopped', NO_CAUSE);
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
      return spawn(this.#deps.tunnel, args, { env, stdio: ['ignore', 'ignore', 'pipe'] });
    const own = [this.#deps.home, dirname(this.#deps.tunnel)];
    const profile = [
      '(version 1)',
      '(allow default)',
      '(deny file-write*)',
      `(allow file-write* ${subpaths([this.#deps.home])} (literal "/dev/null"))`,
      ...(this.#deps.dataDir === undefined
        ? []
        : [
            `(deny file-read* ${subpaths([this.#deps.dataDir])})`,
            `(allow file-read* ${subpaths(own)})`,
          ]),
      '',
    ].join('\n');
    return spawn(SANDBOX_EXEC, ['-p', profile, this.#deps.tunnel, ...args], {
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
      {
        method: 'GET',
        timeout: 10_000,
        lookup: pinnedLookup(address),
      },
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
