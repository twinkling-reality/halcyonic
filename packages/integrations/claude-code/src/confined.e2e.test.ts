import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
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
/** A fake key for the fake API: what the person's key would be, never a real one. */
const KEY = 'sk-ant-dummy-halcyonic-test';

/** Every file under `folder` whose bytes hold `text`, relative; links passed over. */
function filesHolding(folder: string, text: string): string[] {
  const found: string[] = [];
  const walk = (at: string) => {
    for (const name of readdirSync(at)) {
      const path = join(at, name);
      const stat = lstatSync(path);
      if (stat.isSymbolicLink()) continue;
      if (stat.isDirectory()) walk(path);
      else if (stat.isFile() && readFileSync(path).includes(text))
        found.push(path.slice(folder.length + 1));
    }
  };
  walk(folder);
  return found;
}
const RESULTS = join(PROJECT, 'results.txt');
// The project's own git data, which the agent may read and never write.
mkdirSync(join(PROJECT, '.git'));
writeFileSync(join(PROJECT, '.git', 'config'), '[kept]\n');
writeFileSync(join(ROOT, 'outside', 'marker.txt'), 'outside-marker-4c1d\n');
writeFileSync(join(ROOT, 'home', 'marker.txt'), 'home-marker-9a2e\n');
const COMMAND = [
  `cat ${ROOT}/outside/marker.txt >> ${RESULTS} 2>&1; echo "outside=$?" >> ${RESULTS}`,
  `cat ${ROOT}/home/marker.txt >> ${RESULTS} 2>&1; echo "home=$?" >> ${RESULTS}`,
  `ls /Users > /dev/null 2>&1; echo "users=$?" >> ${RESULTS}`,
  // What in the project runs later outside the sandbox, never written: its git data and an open-time file (AUDIT3).
  `echo '[core]' >> ${PROJECT}/.git/config 2>/dev/null; echo "gitconfig=$?" >> ${RESULTS}`,
  `echo 'export X=1' > ${PROJECT}/.envrc 2>/dev/null; echo "envrc=$?" >> ${RESULTS}`,
  `echo "own-home=$([ "$HOME" != ${ROOT}/home ] && echo yes)" >> ${RESULTS}`,
  `/usr/bin/curl --noproxy '*' -s --max-time 5 -o /dev/null -w 'net=%{http_code}' http://1.1.1.1/ >> ${RESULTS}; echo " curl=$?" >> ${RESULTS}`,
  `echo inside > ${PROJECT}/inside.txt`,
  // The key the command's environment holds: the gate's placeholder, never the person's (VRIFY, 2026-11-12).
  `echo "key=\${ANTHROPIC_API_KEY:-none} token=\${ANTHROPIC_AUTH_TOKEN:-none}" >> ${RESULTS}`,
  // The plane's own environment, as ps would show it from inside: refused or holding no key.
  `/bin/ps eww -p ${process.pid} >> ${RESULTS} 2>&1; echo "ps=$?" >> ${RESULTS}`,
  // The gate itself, asked without this run's credential: refused (AUDIT3).
  `/usr/bin/curl -s --max-time 5 --noproxy '*' -o /dev/null -w 'bare=%{http_code}' -X POST "\${ANTHROPIC_BASE_URL}/v1/messages" >> ${RESULTS}; echo " bare-curl=$?" >> ${RESULTS}`,
  // A request for another host through the gate as a proxy: refused there.
  `/usr/bin/curl -s --max-time 5 -o /dev/null -w 'via=%{http_code}' http://example.test/ >> ${RESULTS}; echo " via-curl=$?" >> ${RESULTS}`,
].join('; ');

/** A Messages API that asks for one Bash call for the prompt marked TOOL, then answers Done. */
function fakeApi() {
  const bodies: string[] = [];
  /** The credential headers each request reached the API with. */
  const keys: { key: string | null; authorization: string | null }[] = [];
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
      keys.push({
        key: (req.headers['x-api-key'] as string | undefined) ?? null,
        authorization: req.headers.authorization ?? null,
      });
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
  return { server, bodies, keys };
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
  // As a plane started with the key in its environment holds it: so ps from inside has something to find.
  const keyBefore = process.env.ANTHROPIC_API_KEY;
  process.env.ANTHROPIC_API_KEY = KEY;
  t.after(() => {
    if (keyBefore === undefined) delete process.env.ANTHROPIC_API_KEY;
    else process.env.ANTHROPIC_API_KEY = keyBefore;
  });
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
        ANTHROPIC_API_KEY: KEY,
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
  // Inside the sandbox and counted confined: the gate holds its key, it holds none (AUDIT BOX-4 F1 closed).
  const payload = started?.payload as { confined?: boolean; unconfined_reason?: string };
  assert.equal(payload.confined, true);
  assert.equal(payload.unconfined_reason, undefined);
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
  // The shell's writes to the project's git data and to an open-time file are refused by the profile.
  assert.match(said, /gitconfig=1/);
  assert.match(said, /envrc=1/);
  assert.equal(readFileSync(join(PROJECT, '.git', 'config'), 'utf8'), '[kept]\n');
  assert.ok(!existsSync(join(PROJECT, '.envrc')));
  assert.match(said, /net=000/, 'reached beyond loopback');
  // The command holds only this run's credential from the gate; the gate refuses a request without it;
  // ps from inside shows no key of the plane's; another host is refused.
  assert.match(said, /key=halcyonic-gate-run-[0-9a-f]{64} token=none/);
  assert.match(said, /bare=403/);
  assert.ok(!said.includes(KEY), 'the person key reached the command');
  assert.match(said, /via=403/);
  // Every request reached the API through the gate, with the person's key alone in the declared header.
  assert.ok(api.bodies.length >= 2);
  assert.ok(
    api.keys.every((each) => each.key === KEY && each.authorization === null),
    JSON.stringify(
      api.keys.map((each) => ({
        key: each.key === KEY,
        authorization: each.authorization !== null,
      })),
    ),
  );
  assert.deepEqual(refusing.egress, []);
  // No file of the agent's own place holds the key.
  assert.deepEqual(filesHolding(own.root, KEY), []);
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
    confined: true,
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
