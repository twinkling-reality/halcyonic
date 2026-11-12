import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { CREDENTIAL_REFUSED, type RuntimeObservation } from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';

/**
 * A key the Messages API refuses, on the real CLI the agent SDK bundles (CLAUDE_CODE_BIN): a fake
 * Messages API on loopback answers every request 401 with the API's own authentication error, and
 * the turn fails with the one code a host reads as "the key was refused". The key is a dummy.
 */
const BINARY = process.env.CLAUDE_CODE_BIN ?? '';
const SKIP =
  BINARY === ''
    ? 'CLAUDE_CODE_BIN is not set; point it at the Claude Code binary the agent SDK bundles to run this test'
    : existsSync(BINARY)
      ? false
      : `CLAUDE_CODE_BIN does not exist: ${BINARY}`;

for (const name of ['DEBUG', 'DEBUG_SDK', 'DEBUG_CLAUDE_AGENT_SDK', 'CLAUDE_CODE_DEBUG_LOGS_DIR']) {
  delete process.env[name];
}
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-credential-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'tmp', 'halcyonic', 'project']) mkdirSync(join(ROOT, name));

/** A Messages API that refuses every key, as api.anthropic.com answers a key it does not know. */
function refusingApi() {
  let requests = 0;
  const server = createServer((req, res) => {
    req.resume();
    req.on('end', () => {
      requests += 1;
      res.writeHead(401, { 'content-type': 'application/json' }).end(
        JSON.stringify({
          type: 'error',
          error: { type: 'authentication_error', message: 'invalid x-api-key' },
        }),
      );
    });
  });
  return { server, requests: () => requests };
}

async function listen(server: Server): Promise<string> {
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  return `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
}

async function until(check: () => boolean, ms: number, what: () => string): Promise<void> {
  const deadline = Date.now() + ms;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what()}`);
    await delay(50);
  }
}

test('a key the API refuses fails the turn as credential_refused, never as a turn that ran', {
  skip: SKIP,
  timeout: 260_000,
}, async (t) => {
  const api = refusingApi();
  const base = await listen(api.server);
  t.after(() => {
    api.server.closeAllConnections();
    api.server.close();
  });
  const adapter = new ClaudeAgentRuntimeAdapter({
    directoryPolicy: (path) => ({ ok: true, directory: path }),
    processRecordFile: join(ROOT, 'halcyonic', 'claude-agent-processes.json'),
    inheritedEnvironment: {
      PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
      HOME: join(ROOT, 'home'),
      CLAUDE_CONFIG_DIR: join(ROOT, 'config'),
      TMPDIR: `${join(ROOT, 'tmp')}/`,
      ANTHROPIC_API_KEY: 'sk-ant-dummy-halcyonic-refused',
    },
    environment: {
      ANTHROPIC_BASE_URL: base,
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1',
      DISABLE_AUTOUPDATER: '1',
      DISABLE_TELEMETRY: '1',
      DISABLE_ERROR_REPORTING: '1',
    },
    pathToClaudeCodeExecutable: BINARY,
    // This Mac's own managed Claude Code settings are not the test's: none.
    remainingSettings: async () => ({}),
  });
  t.after(() => adapter.close());
  const observed: RuntimeObservation[] = [];
  const started = Date.now();
  await adapter.startExecution({
    execution: {
      execution_id: '01920000-0000-7000-8000-000000000710' as ExecutionId,
      workstream_id: '01920000-0000-7000-8000-000000000702' as WorkstreamId,
      project_id: '01920000-0000-7000-8000-000000000701' as ProjectId,
    },
    instruction: 'Say hello.',
    options: {},
    model_ref: null,
    directory: join(ROOT, 'project'),
    emit: (observation) => observed.push(observation),
  });
  const ended = new Set([
    'runtime.turn.completed',
    'runtime.turn.failed',
    'runtime.turn.interrupted',
  ]);
  await until(
    () => observed.some((o) => ended.has(o.type)),
    200_000,
    () =>
      `the turn to end; ${api.requests()} requests; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  assert.ok(api.requests() > 0, 'the CLI asked the API');
  // The CLI would retry a refused key for about three minutes; the turn ends at its first retry.
  assert.ok(Date.now() - started < 60_000, `the turn ended in ${Date.now() - started} ms`);
  const end = observed.find((o) => ended.has(o.type));
  assert.equal(end?.type, 'runtime.turn.failed');
  assert.equal(
    (end?.payload as { error?: { code: string } } | undefined)?.error?.code,
    CREDENTIAL_REFUSED,
  );
  assert.ok(
    !observed.some((o) => o.type === 'runtime.agent_message'),
    'the API error is no agent speaking',
  );
  assert.ok(
    !JSON.stringify(observed).includes('sk-ant-dummy-halcyonic-refused'),
    'the key never appears in what the runtime reports',
  );
});
