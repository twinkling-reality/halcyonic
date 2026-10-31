import { type ChildProcess, spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import type { Readable, Writable } from 'node:stream';
import { chromeProfile, SANDBOX_EXEC } from './profiles.ts';

/** The longest message the check reads from Chrome; its 1280 by 800 pictures are a few megabytes. */
export const MAX_MESSAGE_CHARS = 16 * 1024 * 1024;

type Message = {
  id?: number;
  method?: string;
  params?: Record<string, unknown>;
  sessionId?: string;
} & Record<string, unknown>;

/**
 * The DevTools protocol over Chrome's `--remote-debugging-pipe`: JSON messages, each ended by a NUL
 * byte, written to Chrome's fd 3 and read from its fd 4. Nothing listens on a port, so no other
 * process on the Mac can drive this Chrome (AUDIT M18A-S1). A message longer than `maxMessage`
 * characters, which a page can cause (a huge console argument), ends the pipe: every command waiting
 * and every later one fails, and the overflow listeners run (AUDIT M18B-M2).
 */
export class DevToolsPipe {
  #next = 1;
  /** The message being received, in pieces, and its length so far. */
  #parts: string[] = [];
  #pending = 0;
  #overflowed = false;
  readonly #maxMessage: number;
  readonly #waiting = new Map<number, (message: Message) => void>();
  readonly #listeners = new Set<(message: Message) => void>();
  readonly #overflowListeners = new Set<() => void>();
  readonly #input: Writable;

  constructor(input: Writable, output: Readable, maxMessage = MAX_MESSAGE_CHARS) {
    this.#input = input;
    this.#maxMessage = maxMessage;
    output.setEncoding('utf8');
    output.on('data', (chunk: string) => {
      if (this.#overflowed) return;
      let start = 0;
      for (let end = chunk.indexOf('\0'); end !== -1; end = chunk.indexOf('\0', start)) {
        const piece = chunk.slice(start, end);
        if (this.#pending + piece.length > this.#maxMessage) return this.#overflow();
        const text = this.#parts.join('') + piece;
        this.#parts = [];
        this.#pending = 0;
        start = end + 1;
        this.#deliver(text);
      }
      const rest = chunk.slice(start);
      if (this.#pending + rest.length > this.#maxMessage) return this.#overflow();
      if (rest !== '') {
        this.#parts.push(rest);
        this.#pending += rest.length;
      }
    });
  }

  /** Whether Chrome sent a message longer than the pipe reads, which ended it. */
  get overflowed(): boolean {
    return this.#overflowed;
  }

  /** Calls the listener once if the pipe ends on a message too long to read. */
  onOverflow(listener: () => void): void {
    this.#overflowListeners.add(listener);
  }

  #deliver(text: string) {
    let message: Message;
    try {
      message = JSON.parse(text) as Message;
    } catch {
      return;
    }
    if (typeof message.id === 'number' && this.#waiting.has(message.id)) {
      this.#waiting.get(message.id)?.(message);
      this.#waiting.delete(message.id);
    } else {
      for (const listener of this.#listeners) listener(message);
    }
  }

  #overflow() {
    this.#overflowed = true;
    this.#parts = [];
    this.#pending = 0;
    const tooLong = { error: { message: 'Chrome sent more than the check reads' } };
    for (const answer of this.#waiting.values()) answer(tooLong);
    this.#waiting.clear();
    for (const listener of this.#overflowListeners) listener();
    this.#overflowListeners.clear();
  }

  /** Sends one command and waits for its answer; an answer with an error rejects. */
  send(
    method: string,
    params: Record<string, unknown> = {},
    sessionId?: string,
    timeoutMs = 20_000,
  ) {
    const id = this.#next++;
    if (this.#overflowed)
      return Promise.reject(new Error(`${method}: Chrome sent more than the check reads`));
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
  /** The profile and switches to run with instead of the check's (Play on my laptop's, P-A). */
  readonly profileText?: string;
  readonly args?: readonly string[];
  /**
   * The launcher that starts Seatbelt, and so the browser, as its own responsible process, without
   * the app's macOS permissions (S1); null or absent starts it directly, as before.
   */
  readonly launcher?: string | null;
}): Promise<ChromeRun> {
  const { chrome, home, folder, port } = options;
  const launcher = options.launcher ?? null;
  for (const sub of ['home', 'tmp'])
    await mkdir(join(folder, sub), { recursive: true, mode: 0o700 });
  const profile = join(folder, 'chrome.sb');
  await writeFile(
    profile,
    options.profileText ?? chromeProfile({ home, folder, port, browser: dirname(chrome) }),
    { mode: 0o600 },
  );
  const command = [
    SANDBOX_EXEC,
    '-f',
    profile,
    chrome,
    ...(options.args ?? chromeArguments(folder, port)),
  ];
  const child: ChildProcess = spawn(
    launcher ?? SANDBOX_EXEC,
    launcher === null ? command.slice(1) : command,
    {
      // Through the launcher, the browser is a grandchild: its own process group lets a forced stop
      // reach it as well.
      detached: launcher !== null,
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
    const forced = setTimeout(() => {
      if (launcher === null || child.pid === undefined) child.kill('SIGKILL');
      else {
        try {
          process.kill(-child.pid, 'SIGKILL');
        } catch {
          child.kill('SIGKILL');
        }
      }
    }, 3000);
    await exited;
    clearTimeout(forced);
  };
  if (pid === undefined) throw new Error('Chrome did not start');
  try {
    const devtools = new DevToolsPipe(child.stdio[3] as Writable, child.stdio[4] as Readable);
    // A page that makes Chrome send more than the check reads ends the run.
    devtools.onOverflow(() => void stop());
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
