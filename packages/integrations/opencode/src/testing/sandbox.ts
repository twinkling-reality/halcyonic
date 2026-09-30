import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { type FakeProvider, type FakeProviderOptions, startFakeProvider } from './fake-provider.ts';

/**
 * Everything an end to end test gives a real OpenCode server, so it never touches the user's
 * OpenCode configuration or data and never leaves the machine: private HOME, XDG and TMPDIR
 * directories, a configuration whose only provider is the fake one, shell commands that ask for
 * approval, and proxy variables pointing at a trap that records and refuses every request.
 */
export interface OpenCodeSandbox {
  readonly root: string;
  /** A directory for sessions to work in. */
  readonly project: string;
  readonly recordFile: string;
  /** Environment additions for the adapter. */
  readonly env: Readonly<Record<string, string>>;
  readonly provider: FakeProvider;
  /** Requests OpenCode tried to send off the machine through the proxy variables. */
  readonly egress: readonly string[];
  cleanup(): Promise<void>;
}

export async function createSandbox(options: FakeProviderOptions = {}): Promise<OpenCodeSandbox> {
  const root = await mkdtemp(join(tmpdir(), 'halcyonic-opencode-e2e-'));
  const path = (name: string) => join(root, name);
  for (const name of [
    'home',
    'config/opencode',
    'data',
    'state',
    'cache',
    'run',
    'tmp',
    'project',
  ]) {
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
  await new Promise<void>((resolve) => trap.listen(0, '127.0.0.1', () => resolve()));
  const proxy = `http://127.0.0.1:${(trap.address() as AddressInfo).port}`;
  await writeFile(
    path('config/opencode/opencode.json'),
    JSON.stringify(
      {
        update: 'disable',
        share: 'disabled',
        model: 'fake/fake-model',
        providers: {
          fake: {
            name: 'Fake provider (Halcyonic tests)',
            package: '@opencode/ai/providers/openai-compatible',
            settings: { baseURL: provider.baseUrl },
            models: {
              'fake-model': { name: 'Fake model' },
              'fake-model-2': { name: 'Fake model 2' },
            },
          },
        },
        permissions: [{ action: 'shell', resource: '*', effect: 'ask' }],
      },
      null,
      2,
    ),
  );
  const loopback = '127.0.0.1,localhost,::1';
  return {
    root,
    // A real path, as the host binds a project's folder; macOS's temporary directory is a link.
    project: await realpath(path('project')),
    recordFile: path('halcyonic/opencode-server.json'),
    env: {
      HOME: path('home'),
      XDG_CONFIG_HOME: path('config'),
      XDG_DATA_HOME: path('data'),
      XDG_STATE_HOME: path('state'),
      XDG_CACHE_HOME: path('cache'),
      XDG_RUNTIME_DIR: path('run'),
      TMPDIR: `${path('tmp')}/`,
      SHELL: '/bin/bash',
      HTTP_PROXY: proxy,
      HTTPS_PROXY: proxy,
      http_proxy: proxy,
      https_proxy: proxy,
      NO_PROXY: loopback,
      no_proxy: loopback,
      OPENCODE_DISABLE_MODELS_FETCH: 'true',
    },
    provider,
    egress,
    cleanup: async () => {
      await provider.close();
      await close(trap);
      await rm(root, { recursive: true, force: true });
    },
  };
}

function close(server: Server): Promise<void> {
  server.closeAllConnections();
  return new Promise((resolve) => server.close(() => resolve()));
}
