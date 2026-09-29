import { execFile } from 'node:child_process';
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { type FakeProvider, type FakeProviderOptions, startFakeProvider } from './fake-provider.ts';

/**
 * Everything an end to end test gives a real Codex app-server, so it never touches the user's
 * Codex home or configuration and never leaves the machine: private HOME, CODEX_HOME, XDG and
 * TMPDIR directories; a configuration whose only model provider is the fake one; the switches that
 * stop Codex's own traffic; proxy variables pointing at a trap that records and refuses every
 * connection; and a monitor of the sockets of every process running the binary under test.
 *
 * The switches, each verified against Codex 0.157.0 (source at rust-v0.157.0, and runs without
 * credentials that attributed every connection attempt):
 * - `features.plugins = false`: plugin startup sync otherwise contacts chatgpt.com and GitHub;
 * - `analytics.enabled = false`: turns off the analytics events client, which is on unless this
 *   is false (codex-rs/core/src/session/session.rs), and the metrics exporter, which sends to
 *   ab.chatgpt.com when analytics is on (codex-rs/core/src/otel_init.rs);
 * - remote control is disabled by the adapter itself, with
 *   `CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED=1`.
 */
export interface CodexSandbox {
  readonly root: string;
  /** A directory for threads to work in, as a real path. */
  readonly project: string;
  readonly recordFile: string;
  /** Environment additions for the adapter. */
  readonly env: Readonly<Record<string, string>>;
  readonly provider: FakeProvider;
  /** Connections Codex tried to open through the proxy variables. */
  readonly egress: readonly string[];
  /** Sockets to anything but loopback that a process of the binary under test held. */
  readonly sockets: readonly string[];
  cleanup(): Promise<void>;
}

export async function createSandbox(
  binaryPath: string,
  options: FakeProviderOptions = {},
): Promise<CodexSandbox> {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'halcyonic-codex-e2e-')));
  const path = (name: string) => join(root, name);
  for (const name of ['home', 'codex-home', 'config', 'data', 'state', 'cache', 'tmp', 'project']) {
    await mkdir(path(name), { recursive: true, mode: 0o700 });
  }
  const provider = await startFakeProvider(options);
  const egress: string[] = [];
  const trap = createServer((req, res) => {
    egress.push(`${req.method} ${req.url}`);
    res.writeHead(403).end();
  });
  trap.on('connect', (req, socket) => {
    egress.push(`CONNECT ${req.url}`);
    socket.end('HTTP/1.1 403 Forbidden\r\n\r\n');
  });
  trap.on('upgrade', (req, socket) => {
    egress.push(`UPGRADE ${req.url}`);
    socket.destroy();
  });
  await new Promise<void>((resolve) => trap.listen(0, '127.0.0.1', () => resolve()));
  const proxy = `http://127.0.0.1:${(trap.address() as AddressInfo).port}`;
  await writeFile(
    path('codex-home/config.toml'),
    [
      '# Halcyonic end to end tests: the fake provider on loopback, no credentials.',
      'model = "gpt-5.5"',
      'model_provider = "fake"',
      '',
      '[model_providers.fake]',
      'name = "Fake provider (Halcyonic tests)"',
      `base_url = "${provider.baseUrl}"`,
      'wire_api = "responses"',
      'request_max_retries = 0',
      'stream_max_retries = 0',
      '',
      '[features]',
      'plugins = false',
      '',
      '[analytics]',
      'enabled = false',
      '',
    ].join('\n'),
  );
  const monitor = watchSockets(binaryPath);
  const loopback = '127.0.0.1,localhost,::1';
  return {
    root,
    project: path('project'),
    recordFile: path('halcyonic/codex-server.json'),
    env: {
      HOME: path('home'),
      CODEX_HOME: path('codex-home'),
      XDG_CONFIG_HOME: path('config'),
      XDG_DATA_HOME: path('data'),
      XDG_STATE_HOME: path('state'),
      XDG_CACHE_HOME: path('cache'),
      TMPDIR: `${path('tmp')}/`,
      SHELL: '/bin/zsh',
      HTTP_PROXY: proxy,
      HTTPS_PROXY: proxy,
      ALL_PROXY: proxy,
      http_proxy: proxy,
      https_proxy: proxy,
      all_proxy: proxy,
      NO_PROXY: loopback,
      no_proxy: loopback,
    },
    provider,
    egress,
    sockets: monitor.findings,
    cleanup: async () => {
      await monitor.stop();
      await provider.close();
      await close(trap);
      await rm(root, { recursive: true, force: true });
    },
  };
}

const LOOPBACK = /^(?:127\.0\.0\.1|\[::1\]):\d+(?:->(?:127\.0\.0\.1|\[::1\]):\d+)?(?: \(\w+\))?$/;
const PS_ENV = { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', LC_ALL: 'C' };

/** An extended regular expression, the kind pgrep takes, that matches `text` literally. */
export function literalPattern(text: string): string {
  return text.replace(/[\\^$.|?*+()[\]{}]/g, '\\$&');
}

/**
 * Every 200 ms, lists the internet sockets of every process running `binaryPath` and of their
 * descendants, and records any that is not between loopback addresses. It finds a connection that
 * ignores the proxy variables, as long as it lasts longer than a sample.
 */
function watchSockets(binaryPath: string): { findings: string[]; stop(): Promise<void> } {
  const findings: string[] = [];
  let running = true;
  const loop = (async () => {
    while (running) {
      try {
        const pids = await processTree(binaryPath);
        if (pids.length > 0) {
          const output = await run('lsof', ['-nP', '-a', '-p', pids.join(','), '-i']);
          for (const line of output.split('\n').slice(1)) {
            const fields = line.trim().split(/\s+/);
            const name = fields.slice(8).join(' ');
            if (name !== '' && !LOOPBACK.test(name))
              findings.push(`${fields[0]} ${fields[1]} ${name}`);
          }
        }
      } catch (error) {
        findings.push(
          `the socket monitor failed: ${error instanceof Error ? error.message : String(error)}`,
        );
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
  })();
  return {
    findings,
    stop: async () => {
      running = false;
      await loop;
    },
  };
}

/**
 * The processes running `binaryPath` and their descendants. pgrep matches every command line
 * without printing them, and `ps` lists each process with its parent only, so what is read stays
 * small however long the command lines on the machine are.
 */
async function processTree(binaryPath: string): Promise<number[]> {
  const found = new Set(
    (await run('pgrep', ['-f', `^${literalPattern(binaryPath)} `]))
      .split('\n')
      .filter((line) => line.trim() !== '')
      .map(Number),
  );
  const rows = (await run('ps', ['-A', '-o', 'pid=,ppid=']))
    .split('\n')
    .map((line) => line.trim().split(/\s+/))
    .map((fields) => ({ pid: Number(fields[0]), ppid: Number(fields[1]) }));
  for (let grew = true; grew; ) {
    grew = false;
    for (const row of rows) {
      if (found.has(row.ppid) && !found.has(row.pid)) {
        found.add(row.pid);
        grew = true;
      }
    }
  }
  return [...found];
}

/**
 * Runs a command and resolves with its output. Exit status 1 is not a failure: lsof exits 1 when
 * one of the processes it was asked about has already exited, and pgrep when no process matches.
 */
function run(command: string, args: readonly string[]): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile(command, args, { env: PS_ENV, timeout: 5000 }, (error, stdout) => {
      if (error === null || error.code === 1) resolve(stdout);
      else reject(new Error(`${command} failed: ${error.message}`));
    });
  });
}

function close(server: Server): Promise<void> {
  server.closeAllConnections();
  return new Promise((resolve) => server.close(() => resolve()));
}
