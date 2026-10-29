import { type ChildProcess, spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import type { Readable, Writable } from 'node:stream';
import { chromeProfile, SANDBOX_EXEC } from './profiles.ts';

type Message = {
  id?: number;
  method?: string;
  params?: Record<string, unknown>;
  sessionId?: string;
} & Record<string, unknown>;

/**
 * The DevTools protocol over Chrome's `--remote-debugging-pipe`: JSON messages, each ended by a NUL
 * byte, written to Chrome's fd 3 and read from its fd 4. Nothing listens on a port, so no other
 * process on the Mac can drive this Chrome (AUDIT M18A-S1).
 */
export class DevToolsPipe {
  #next = 1;
  #buffer = '';
  readonly #waiting = new Map<number, (message: Message) => void>();
  readonly #listeners = new Set<(message: Message) => void>();
  readonly #input: Writable;

  constructor(input: Writable, output: Readable) {
    this.#input = input;
    output.setEncoding('utf8');
    output.on('data', (chunk: string) => {
      this.#buffer += chunk;
      for (let end = this.#buffer.indexOf('\0'); end !== -1; end = this.#buffer.indexOf('\0')) {
        const text = this.#buffer.slice(0, end);
        this.#buffer = this.#buffer.slice(end + 1);
        let message: Message;
        try {
          message = JSON.parse(text) as Message;
        } catch {
          continue;
        }
        if (typeof message.id === 'number' && this.#waiting.has(message.id)) {
          this.#waiting.get(message.id)?.(message);
          this.#waiting.delete(message.id);
        } else {
          for (const listener of this.#listeners) listener(message);
        }
      }
    });
  }

  /** Sends one command and waits for its answer; an answer with an error rejects. */
  send(
    method: string,
    params: Record<string, unknown> = {},
    sessionId?: string,
    timeoutMs = 20_000,
  ) {
    const id = this.#next++;
    return new Promise<Record<string, unknown>>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.#waiting.delete(id);
        reject(new Error(`Chrome did not answer ${method}`));
      }, timeoutMs);
      this.#waiting.set(id, (message) => {
        clearTimeout(timer);
        if (message.error !== undefined)
          reject(new Error(`${method}: ${JSON.stringify(message.error)}`));
        else resolve((message.result as Record<string, unknown>) ?? {});
      });
      this.#input.write(
        `${JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) })}\0`,
      );
    });
  }

  /** Calls the listener with every event Chrome sends; returns a function that stops it. */
  onEvent(listener: (message: Message) => void): () => void {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }
}

export interface ChromeRun {
  readonly pid: number;
  readonly devtools: DevToolsPipe;
  /** The page's session, attached with flat sessions. */
  readonly session: string;
  /** Stops this Chrome by its own pid, then by force; resolves when it has exited. */
  stop(): Promise<void>;
}

/** The switches the check's Chrome runs with: headless, no network but the preview, nothing remembered. */
export function chromeArguments(folder: string, port: number): string[] {
  return [
    '--headless=new',
    // Chrome's own sandbox cannot start inside Seatbelt; the outer profile is the boundary.
    '--no-sandbox',
    `--user-data-dir=${join(folder, 'profile')}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--use-angle=metal',
    '--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1',
    '--proxy-server=http://127.0.0.1:9',
    `--proxy-bypass-list=127.0.0.1:${port}`,
    '--force-webrtc-ip-handling-policy=disable_non_proxied_udp',
    '--disable-background-networking',
    '--disable-component-update',
    '--disable-domain-reliability',
    '--disable-sync',
    '--no-pings',
    '--disable-features=AsyncDns,DnsOverHttps,MediaRouter,WebRtcHideLocalIpsWithMdns',
    // The login keychain is outside the profile's reads; Chrome would otherwise wait on it.
    '--use-mock-keychain',
    '--window-size=1280,800',
    '--remote-debugging-pipe',
    'about:blank',
  ];
}

/**
 * Starts headless Chrome inside its profile, with its HOME and temporary folders in its own folder,
 * and attaches to its page. Rejects, with Chrome stopped, if it does not answer within the limit.
 */
export async function launchChrome(options: {
  readonly chrome: string;
  readonly home: string;
  readonly folder: string;
  readonly port: number;
  readonly timeoutMs?: number;
}): Promise<ChromeRun> {
  const { chrome, home, folder, port } = options;
  for (const sub of ['home', 'tmp'])
    await mkdir(join(folder, sub), { recursive: true, mode: 0o700 });
  const profile = join(folder, 'chrome.sb');
  await writeFile(profile, chromeProfile({ home, folder, port }), { mode: 0o600 });
  const child: ChildProcess = spawn(
    SANDBOX_EXEC,
    ['-f', profile, chrome, ...chromeArguments(folder, port)],
    {
      env: {
        HOME: join(folder, 'home'),
        TMPDIR: join(folder, 'tmp'),
        // Moves Chrome's process singleton socket folder, which ignores TMPDIR.
        MAC_CHROMIUM_TMPDIR: join(folder, 'tmp'),
        PATH: '/usr/bin:/bin',
      },
      stdio: ['ignore', 'ignore', 'ignore', 'pipe', 'pipe'],
    },
  );
  const pid = child.pid;
  const exited = new Promise<void>((resolve) => child.once('exit', () => resolve()));
  const stop = async () => {
    if (child.exitCode !== null || child.signalCode !== null) return;
    child.kill('SIGTERM');
    const forced = setTimeout(() => child.kill('SIGKILL'), 3000);
    await exited;
    clearTimeout(forced);
  };
  if (pid === undefined) throw new Error('Chrome did not start');
  try {
    const devtools = new DevToolsPipe(child.stdio[3] as Writable, child.stdio[4] as Readable);
    const timeoutMs = options.timeoutMs ?? 30_000;
    const { targetInfos } = (await devtools.send(
      'Target.getTargets',
      {},
      undefined,
      timeoutMs,
    )) as {
      targetInfos: { targetId: string; type: string }[];
    };
    const page = targetInfos.find((target) => target.type === 'page');
    if (page === undefined) throw new Error('Chrome opened no page');
    const { sessionId } = (await devtools.send('Target.attachToTarget', {
      targetId: page.targetId,
      flatten: true,
    })) as {
      sessionId: string;
    };
    return { pid, devtools, session: sessionId, stop };
  } catch (error) {
    await stop();
    throw error;
  }
}
