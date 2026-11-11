import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';

/**
 * Claude Code's own plan mode (PLAN 4b) and carrying a plan out (4a), on the real CLI the agent SDK
 * bundles (CLAUDE_CODE_BIN). A fake Messages API on loopback asks for one Write per prompt; the key
 * is a dummy; every proxy variable points at a trap that refuses and records.
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
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-plan-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'project', 'tmp', 'halcyonic']) mkdirSync(join(ROOT, name));
const PROJECT = join(ROOT, 'project');
process.env.CLAUDE_CONFIG_DIR = join(ROOT, 'config');

/** A Messages API that asks for a Write of `<word>.txt` for each prompt marked WRITE-<word>, then answers Done. */
function fakeApi() {
  const server = createServer((req, res) => {
    let raw = '';
    req.on('data', (chunk) => {
      raw += chunk;
    });
    req.on('end', () => {
      if (req.method !== 'POST' || !req.url?.startsWith('/v1/messages')) {
        res.writeHead(404, { 'content-type': 'application/json' }).end('{}');
        return;
      }
      if (req.url.includes('count_tokens')) {
        res.writeHead(200, { 'content-type': 'application/json' }).end('{"input_tokens":10}');
        return;
      }
      const body = JSON.parse(raw) as { model?: string; stream?: boolean };
      // The latest prompt's word, unless a tool result came after it.
      const words = [...raw.matchAll(/WRITE-([a-z]+)/g)];
      const last = words.at(-1);
      const answered = last !== undefined && raw.lastIndexOf('tool_result') > (last.index ?? 0);
      const word = last === undefined || answered ? null : last[1];
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 1 },
      };
      const block =
        word === null
          ? { type: 'text', text: 'Done.' }
          : {
              type: 'tool_use',
              id: `toolu_${randomUUID().slice(0, 8)}`,
              name: 'Write',
              input: { file_path: join(PROJECT, `${word}.txt`), content: `${word}\n` },
            };
      const stop = word === null ? 'end_turn' : 'tool_use';
      if (body.stream !== true) {
        res
          .writeHead(200, { 'content-type': 'application/json' })
          .end(JSON.stringify({ ...message, content: [block], stop_reason: stop }));
        return;
      }
      res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
      const send = (event: string, data: unknown) =>
        res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
      send('message_start', {
        type: 'message_start',
        message: { ...message, content: [], stop_reason: null },
      });
      if (block.type === 'tool_use') {
        send('content_block_start', {
          type: 'content_block_start',
          index: 0,
          content_block: { ...block, input: {} },
        });
        send('content_block_delta', {
          type: 'content_block_delta',
          index: 0,
          delta: { type: 'input_json_delta', partial_json: JSON.stringify(block.input) },
        });
      } else {
        send('content_block_start', {
          type: 'content_block_start',
          index: 0,
          content_block: { type: 'text', text: '' },
        });
        send('content_block_delta', {
          type: 'content_block_delta',
          index: 0,
          delta: { type: 'text_delta', text: 'Done.' },
        });
      }
      send('content_block_stop', { type: 'content_block_stop', index: 0 });
      send('message_delta', {
        type: 'message_delta',
        delta: { stop_reason: stop, stop_sequence: null },
        usage: { output_tokens: 1 },
      });
      send('message_stop', { type: 'message_stop' });
      res.end();
    });
  });
  return { server };
}

function trap() {
  const egress: string[] = [];
  const server = createServer((req, res) => {
    egress.push(`${req.method} ${req.url}`);
    res.writeHead(403).end();
  });
  server.on('connect', (req, socket) => {
    egress.push(`CONNECT ${req.url}`);
    socket.end('HTTP/1.1 403 Forbidden\r\n\r\n');
  });
  return { server, egress };
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

const execution: ExecutionContext = {
  execution_id: '01920000-0000-7000-8000-000000000510' as ExecutionId,
  workstream_id: '01920000-0000-7000-8000-000000000502' as WorkstreamId,
  project_id: '01920000-0000-7000-8000-000000000501' as ProjectId,
};

test('a Claude Code plan runs in its plan mode, recorded a plan, its write asked and refused, and carrying it out leaves plan mode in the same session before the write goes ahead', {
  skip: SKIP,
  timeout: 180_000,
}, async (t) => {
  const api = fakeApi();
  const refusing = trap();
  const base = await listen(api.server);
  const proxy = await listen(refusing.server);
  t.after(() => {
    for (const server of [api.server, refusing.server]) {
      server.closeAllConnections();
      server.close();
    }
  });
  const adapter = new ClaudeAgentRuntimeAdapter({
    directoryPolicy: (path) => ({ ok: true, directory: path }),
    processRecordFile: join(ROOT, 'halcyonic', 'claude-agent-processes.json'),
    inheritedEnvironment: {
      PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
      HOME: join(ROOT, 'home'),
      CLAUDE_CONFIG_DIR: join(ROOT, 'config'),
      TMPDIR: `${join(ROOT, 'tmp')}/`,
      ANTHROPIC_API_KEY: 'sk-ant-dummy-halcyonic-test',
    },
    environment: {
      ANTHROPIC_BASE_URL: base,
      CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1',
      DISABLE_AUTOUPDATER: '1',
      DISABLE_TELEMETRY: '1',
      DISABLE_ERROR_REPORTING: '1',
      HTTP_PROXY: proxy,
      HTTPS_PROXY: proxy,
      NO_PROXY: '127.0.0.1,localhost,::1',
    },
    pathToClaudeCodeExecutable: BINARY,
  });
  t.after(() => adapter.close());
  assert.equal(adapter.descriptor.offers_plan, true);
  const observed: RuntimeObservation[] = [];
  const types = () => observed.map((o) => o.type).join(', ');
  const answer = async (decision: 'approve' | 'deny', count: number) => {
    await until(
      () => observed.filter((o) => o.type === 'runtime.approval.requested').length >= count,
      60_000,
      () => `the Write to ask; observed ${types()}`,
    );
    const asked = observed.filter((o) => o.type === 'runtime.approval.requested')[count - 1];
    assert.ok(asked?.type === 'runtime.approval.requested');
    await adapter.respondToApproval({
      execution,
      approval_id: asked.payload.approval_id,
      decision,
      message: null,
    });
  };
  const finished = (count: number) =>
    until(
      () =>
        observed.filter(
          (o) => o.type === 'runtime.turn.completed' || o.type === 'runtime.turn.interrupted',
        ).length >= count,
      90_000,
      () => `turn ${count} to end; observed ${types()}`,
    );

  await adapter.startExecution({
    execution,
    instruction: 'WRITE-planned, plan it.',
    options: {},
    model_ref: null,
    directory: PROJECT,
    plan: true,
    emit: (observation) => observed.push(observation),
  });
  const started = observed.find((o) => o.type === 'runtime.execution.started');
  assert.equal((started?.payload as { plan?: boolean } | undefined)?.plan, true);
  // In plan mode the Write asks, as every Claude Code write does; refused, nothing is written.
  await answer('deny', 1);
  await finished(1);
  assert.equal(existsSync(join(PROJECT, 'planned.txt')), false);

  // Carrying the plan out: the same session leaves plan mode, confirmed, before the instruction goes.
  await adapter.sendInstruction({
    execution,
    text: 'WRITE-carried, carry it out.',
    leave_plan: true,
  });
  assert.ok(
    observed.some((o) => o.type === 'runtime.plan.left'),
    `not left; ${types()}`,
  );
  await answer('approve', 2);
  await finished(2);
  assert.equal(readFileSync(join(PROJECT, 'carried.txt'), 'utf8'), 'carried\n');
  const left = observed.findIndex((o) => o.type === 'runtime.plan.left');
  const secondTurn = observed.findLastIndex((o) => o.type === 'runtime.turn.started');
  assert.ok(
    left > -1 && left < secondTurn,
    'the plan was left before the turn that carries it out',
  );
  assert.deepEqual(refusing.egress, []);
});
