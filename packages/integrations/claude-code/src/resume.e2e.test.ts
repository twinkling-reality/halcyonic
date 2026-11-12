import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import {
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';
import { readProcessRecords } from './process-record.ts';

/**
 * A session re-attached after a restart (ADR 0046), on a real Claude Code CLI: CLAUDE_CODE_BIN, the
 * one the agent SDK bundles (2.1.283 for 0.3.283). A fake Messages API on loopback answers every
 * request, the key is a dummy, HOME and CLAUDE_CONFIG_DIR are temporary, and every proxy variable
 * points at a trap that refuses and records, so nothing leaves the machine.
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
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-resume-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'project', 'tmp', 'halcyonic']) mkdirSync(join(ROOT, name));
const PROJECT = join(ROOT, 'project');
const CONFIG = join(ROOT, 'config');
// The adapter reads Claude Code's store in-process, where its CLAUDE_CONFIG_DIR is this process's.
process.env.CLAUDE_CONFIG_DIR = CONFIG;

/** A Messages API that streams a short answer, or streams slowly to a prompt containing SLOW. */
function fakeApi() {
  const bodies: string[] = [];
  const server = createServer((req, res) => {
    let raw = '';
    req.on('data', (chunk) => {
      raw += chunk;
    });
    req.on('end', async () => {
      if (req.method !== 'POST' || !req.url?.startsWith('/v1/messages')) {
        res.writeHead(404, { 'content-type': 'application/json' }).end('{}');
        return;
      }
      if (req.url.includes('count_tokens')) {
        res.writeHead(200, { 'content-type': 'application/json' }).end('{"input_tokens":10}');
        return;
      }
      bodies.push(raw);
      const body = JSON.parse(raw) as {
        stream?: boolean;
        model?: string;
        messages: { content: unknown }[];
      };
      const last = JSON.stringify(body.messages.at(-1)?.content ?? '');
      const slow = last.includes('SLOW');
      const text = slow ? 'slow ' : 'Done.';
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 1 },
      };
      if (body.stream !== true) {
        res.writeHead(200, { 'content-type': 'application/json' });
        res.end(
          JSON.stringify({
            ...message,
            content: [{ type: 'text', text }],
            stop_reason: 'end_turn',
          }),
        );
        return;
      }
      res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
      const send = (event: string, data: unknown) =>
        res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
      send('message_start', {
        type: 'message_start',
        message: { ...message, content: [], stop_reason: null },
      });
      send('content_block_start', {
        type: 'content_block_start',
        index: 0,
        content_block: { type: 'text', text: '' },
      });
      let closed = false;
      res.on('close', () => {
        closed = true;
      });
      for (let i = 0; i < (slow ? 600 : 1) && !closed; i += 1) {
        send('content_block_delta', {
          type: 'content_block_delta',
          index: 0,
          delta: { type: 'text_delta', text },
        });
        if (slow) await delay(200);
      }
      send('content_block_stop', { type: 'content_block_stop', index: 0 });
      send('message_delta', {
        type: 'message_delta',
        delta: { stop_reason: 'end_turn', stop_sequence: null },
        usage: { output_tokens: 1 },
      });
      send('message_stop', { type: 'message_stop' });
      res.end();
    });
  });
  return { server, bodies };
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

async function until(check: () => boolean, ms: number, what: string): Promise<void> {
  const deadline = Date.now() + ms;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await delay(50);
  }
}

function context(n: number): ExecutionContext {
  return {
    execution_id: `01920000-0000-7000-8000-0000000003${n}0` as ExecutionId,
    workstream_id: '01920000-0000-7000-8000-000000000302' as WorkstreamId,
    project_id: '01920000-0000-7000-8000-000000000301' as ProjectId,
  };
}

test('a session whose Claude Code process died mid-turn is re-attached by a new adapter, its cut turn ended, and carries on with its history', {
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
  const recordFile = join(ROOT, 'halcyonic', 'claude-agent-processes.json');
  const adapter = () =>
    new ClaudeAgentRuntimeAdapter({
      directoryPolicy: (path) => ({ ok: true, directory: path }),
      processRecordFile: recordFile,
      inheritedEnvironment: {
        PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
        HOME: join(ROOT, 'home'),
        CLAUDE_CONFIG_DIR: CONFIG,
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
      // This Mac's own managed Claude Code settings are not the test's: none.
      remainingSettings: async () => ({}),
    });

  // The first control plane: a turn that finishes, then one its Claude Code process dies in.
  const first = adapter();
  t.after(() => first.close());
  const before: RuntimeObservation[] = [];
  const { native_id } = await first.startExecution({
    execution: context(1),
    instruction: 'Hello first MARKER-ONE.',
    options: {},
    model_ref: null,
    directory: PROJECT,
    emit: (observation) => before.push(observation),
  });
  assert.ok(native_id !== null);
  await until(
    () => before.some((o) => o.type === 'runtime.turn.completed'),
    60_000,
    'the first turn',
  );
  await first.sendInstruction({
    execution: context(1),
    text: 'SLOW turn the restart cuts MARKER-TWO.',
  });
  await until(() => api.bodies.some((body) => body.includes('MARKER-TWO')), 60_000, 'the cut turn');
  const cut = before.filter((o) => o.type === 'runtime.turn.started').at(-1);
  const cutTurn = cut?.type === 'runtime.turn.started' ? cut.payload.turn_id : null;
  assert.ok(cutTurn !== null);
  const records = await readProcessRecords(recordFile);
  assert.equal(records.length, 1);
  process.kill((records[0] as { pid: number }).pid, 'SIGKILL');
  await until(() => before.some((o) => o.type === 'runtime.connection.lost'), 30_000, 'the loss');
  await first.close();

  // A new control plane: a new adapter re-attaches the session by its id.
  const second = adapter();
  t.after(() => second.close());
  const afterRestart: RuntimeObservation[] = [];
  await second.reattach({
    execution: context(1),
    native_id,
    options: {},
    model_ref: null,
    confined: false,
    directory: PROJECT,
    cut_turn: { turn_id: cutTurn },
    last_sequence: null,
    emit: (observation) => afterRestart.push(observation),
  });
  assert.deepEqual(
    afterRestart.map((o) => o.type),
    ['runtime.connection.restored', 'runtime.turn.interrupted'],
  );
  const sentBefore = api.bodies.length;
  await second.sendInstruction({
    execution: context(1),
    text: 'Hello after restart MARKER-THREE.',
  });
  await until(
    () => afterRestart.some((o) => o.type === 'runtime.turn.completed'),
    60_000,
    'the resumed turn',
  );
  assert.ok(!afterRestart.some((o) => o.type === 'runtime.execution.started'));
  const resumed = api.bodies.slice(sentBefore).find((body) => body.includes('MARKER-THREE'));
  assert.ok(resumed !== undefined);
  assert.ok(resumed.includes('MARKER-ONE'), 'the first turn is in the history');
  assert.ok(resumed.includes('MARKER-TWO'), 'the cut turn is in the history');
  // Its events never repeat an id the session's first run used, which the journal would drop.
  const firstIds = new Set(before.map((o) => o.native_event_id));
  assert.ok(afterRestart.every((o) => !firstIds.has(o.native_event_id)));

  // A session Claude Code does not have is not re-attached.
  await assert.rejects(
    second.reattach({
      execution: context(2),
      native_id: randomUUID(),
      options: {},
      model_ref: null,
      confined: false,
      directory: PROJECT,
      cut_turn: null,
      last_sequence: null,
      emit: () => undefined,
    }),
    (error: unknown) => error instanceof RuntimeActionError && error.code === 'session_not_found',
  );
  assert.deepEqual(refusing.egress, [], 'nothing tried to leave the machine');
});
