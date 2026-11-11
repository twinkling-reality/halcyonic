import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { toolFoldersFor } from '@halcyonic/seatbelt';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';
import { confinedFolder } from './confinement.ts';

/**
 * A new app's Claude Code inside Halcyonic's closed sandbox (ADR 0048, BOX-4), on the real CLI the
 * agent SDK bundles (CLAUDE_CODE_BIN). A fake Messages API on loopback answers, reached only through
 * the adapter's gate; the key is a dummy; every proxy variable the test sets points at a trap that
 * refuses and records, so a request that bypassed the gate would show there.
 */
const BINARY = process.env.CLAUDE_CODE_BIN ?? '';
const SKIP =
  process.platform !== 'darwin'
    ? 'Seatbelt is macOS only'
    : BINARY === ''
      ? 'CLAUDE_CODE_BIN is not set; point it at the Claude Code binary the agent SDK bundles to run this test'
      : existsSync(BINARY)
        ? false
        : `CLAUDE_CODE_BIN does not exist: ${BINARY}`;

for (const name of ['DEBUG', 'DEBUG_SDK', 'DEBUG_CLAUDE_AGENT_SDK', 'CLAUDE_CODE_DEBUG_LOGS_DIR']) {
  delete process.env[name];
}
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-confined-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'project', 'tmp', 'halcyonic', 'outside']) {
  mkdirSync(join(ROOT, name));
}
const PROJECT = join(ROOT, 'project');
const RESULTS = join(PROJECT, 'results.txt');
writeFileSync(join(ROOT, 'outside', 'marker.txt'), 'outside-marker-4c1d\n');
writeFileSync(join(ROOT, 'home', 'marker.txt'), 'home-marker-9a2e\n');
const COMMAND = [
  `cat ${ROOT}/outside/marker.txt >> ${RESULTS} 2>&1; echo "outside=$?" >> ${RESULTS}`,
  `cat ${ROOT}/home/marker.txt >> ${RESULTS} 2>&1; echo "home=$?" >> ${RESULTS}`,
  `ls /Users > /dev/null 2>&1; echo "users=$?" >> ${RESULTS}`,
  `echo "own-home=$([ "$HOME" != ${ROOT}/home ] && echo yes)" >> ${RESULTS}`,
  `/usr/bin/curl --noproxy '*' -s --max-time 5 -o /dev/null -w 'net=%{http_code}' http://1.1.1.1/ >> ${RESULTS}; echo " curl=$?" >> ${RESULTS}`,
  `echo inside > ${PROJECT}/inside.txt`,
  // AUDIT BOX-4 F1: the sign-in key is in the command's environment.
  `echo "key=\${ANTHROPIC_API_KEY:-none}" >> ${RESULTS}`,
].join('; ');

/** A Messages API that asks for one Bash call for the prompt marked TOOL, then answers Done. */
function fakeApi() {
  const bodies: string[] = [];
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
      bodies.push(raw);
      const body = JSON.parse(raw) as { stream?: boolean; model?: string; messages: unknown[] };
      // The whole request: Claude Code may send the prompt in a block of its own before reminders.
      const tool = raw.includes('TOOL first') && !raw.includes('tool_result');
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 1 },
      };
      if (body.stream !== true) {
        const content = tool
          ? [
              {
                type: 'tool_use',
                id: `toolu_${randomUUID().slice(0, 8)}`,
                name: 'Bash',
                input: { command: COMMAND, description: 'Try the sandbox' },
              },
            ]
          : [{ type: 'text', text: 'Done.' }];
        res
          .writeHead(200, { 'content-type': 'application/json' })
          .end(
            JSON.stringify({ ...message, content, stop_reason: tool ? 'tool_use' : 'end_turn' }),
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
      if (tool) {
        send('content_block_start', {
          type: 'content_block_start',
          index: 0,
          content_block: {
            type: 'tool_use',
            id: `toolu_${randomUUID().slice(0, 8)}`,
            name: 'Bash',
            input: {},
          },
        });
        send('content_block_delta', {
          type: 'content_block_delta',
          index: 0,
          delta: {
            type: 'input_json_delta',
            partial_json: JSON.stringify({ command: COMMAND, description: 'Try the sandbox' }),
          },
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
        delta: { stop_reason: tool ? 'tool_use' : 'end_turn', stop_sequence: null },
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

async function until(
  check: () => boolean,
  ms: number,
  what: string | (() => string),
): Promise<void> {
  const deadline = Date.now() + ms;
  while (!check()) {
    if (Date.now() > deadline)
      throw new Error(`timed out waiting for ${typeof what === 'string' ? what : what()}`);
    await delay(50);
  }
}

function context(n: number): ExecutionContext {
  return {
    execution_id: `01920000-0000-7000-8000-0000000004${n}0` as ExecutionId,
    workstream_id: '01920000-0000-7000-8000-000000000402' as WorkstreamId,
    project_id: '01920000-0000-7000-8000-000000000401' as ProjectId,
  };
}

test("a new app's confined Claude Code runs a command that reaches nothing outside its folder, only through the gate, and is re-attached from its folder's own store", {
  skip: SKIP,
  timeout: 240_000,
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
  const data = join(ROOT, 'halcyonic');
  const adapter = () =>
    new ClaudeAgentRuntimeAdapter({
      directoryPolicy: (path) => ({ ok: true, directory: path }),
      processRecordFile: join(data, 'claude-agent-processes.json'),
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
      confinement: {
        dataFolder: data,
        toolFolders: toolFoldersFor(process.execPath),
        hidden: [data],
        forDirectory: () => ({ readable: [] }),
      },
    });

  // What an agent inside could have written into the folder's own configuration on an earlier turn: a
  // hook that marks the project, and an allow rule. Neither may load (AUDIT2 S1).
  const own = confinedFolder(
    { dataFolder: data, toolFolders: [], forDirectory: () => null },
    PROJECT,
    { readable: [] },
  );
  mkdirSync(own.config, { recursive: true, mode: 0o700 });
  writeFileSync(
    join(own.config, 'settings.json'),
    JSON.stringify({
      permissions: { allow: ['Bash(*)'] },
      hooks: {
        UserPromptSubmit: [
          {
            hooks: [
              { type: 'command', command: `/usr/bin/touch ${join(PROJECT, 'planted-hook-ran')}` },
            ],
          },
        ],
      },
    }),
  );

  // A first control plane: a turn with one approved command.
  const first = adapter();
  const observed: RuntimeObservation[] = [];
  const execution = context(1);
  const { native_id: sessionId } = await first.startExecution({
    execution,
    instruction: 'TOOL first MARKER-ONE.',
    options: {},
    model_ref: null,
    directory: PROJECT,
    emit: (observation) => observed.push(observation),
  });
  await until(
    () => observed.some((o) => o.type === 'runtime.approval.requested'),
    60_000,
    () =>
      `the command to ask; observed ${observed.map((o) => o.type).join(', ')}; api ${api.bodies.length}`,
  );
  const asked = observed.find((o) => o.type === 'runtime.approval.requested');
  // The planted allow rule did not let the command run unasked, and the planted hook did not run.
  assert.equal(existsSync(join(PROJECT, 'planted-hook-ran')), false, 'a planted hook ran');
  assert.ok(asked?.type === 'runtime.approval.requested');
  await first.respondToApproval({
    execution,
    approval_id: asked.payload.approval_id,
    decision: 'approve',
    message: null,
  });
  await until(
    () => observed.some((o) => o.type === 'runtime.turn.completed'),
    90_000,
    () => `the first turn to finish; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  const started = observed.find((o) => o.type === 'runtime.execution.started');
  // Inside the sandbox, but not counted confined: its key is inside with it (AUDIT BOX-4 F1).
  const payload = started?.payload as { confined?: boolean; unconfined_reason?: string };
  assert.equal(payload.confined, false);
  assert.match(payload.unconfined_reason ?? '', /sign-in key is inside with it/);
  const said = readFileSync(RESULTS, 'utf8');
  t.diagnostic(said);
  // The controls: the command ran, inside its folder, with a home of its own.
  assert.equal(readFileSync(join(PROJECT, 'inside.txt'), 'utf8'), 'inside\n');
  assert.match(said, /own-home=yes/);
  assert.doesNotMatch(said, /outside-marker-4c1d/, 'read a file outside its folder');
  assert.doesNotMatch(said, /home-marker-9a2e/, "read the person's home");
  assert.match(said, /outside=1/);
  assert.match(said, /home=1/);
  assert.match(said, /users=1/);
  assert.match(said, /net=000/, 'reached beyond loopback');
  // The fact the record follows: the command could read the test's dummy key.
  assert.match(said, /key=sk-ant-dummy-halcyonic-test/);
  // Every request went through the gate to the API: none bypassed it to the test's proxies.
  assert.ok(api.bodies.length >= 2);
  assert.deepEqual(refusing.egress, []);
  // The person's Claude Code configuration was never used: the session is in its folder's own store.
  assert.equal(existsSync(join(ROOT, 'config', 'projects')), false);
  await first.close();

  // A second control plane re-attaches the session from the folder's own store.
  const second = adapter();
  t.after(() => second.close());
  const again: RuntimeObservation[] = [];
  const next = context(2);
  await second.reattach({
    execution: next,
    native_id: sessionId as string,
    options: {},
    model_ref: null,
    confined: false,
    directory: PROJECT,
    cut_turn: null,
    last_sequence: null,
    emit: (observation) => again.push(observation),
  });
  assert.deepEqual(
    again.map((o) => o.type),
    ['runtime.connection.restored'],
  );
  const before = api.bodies.length;
  await second.sendInstruction({ execution: next, text: 'Hello after restart MARKER-TWO.' });
  await until(
    () => again.some((o) => o.type === 'runtime.turn.completed'),
    90_000,
    'the resumed turn to finish',
  );
  const resumed = api.bodies.slice(before).find((body) => body.includes('MARKER-TWO'));
  assert.ok(resumed?.includes('MARKER-ONE'), 'the first turn is in the history');
  assert.deepEqual(refusing.egress, []);
});
