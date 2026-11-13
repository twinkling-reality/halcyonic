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

/**
 * Claude Code on a folder the person connected, OUTSIDE the closed profile (VRIFY, 2026-11-13; the lead's 18:21Z step
 * 3), on the real CLI the agent SDK bundles (CLAUDE_CODE_BIN): it reaches its API only through the key gate, holding a
 * run credential in place of the person's key, and runs under the light profile, so neither it nor any command it
 * runs reads or writes the host's own folders (a scratch data folder and a `.halcyonic-proving` home), while its own
 * folder, the person's home and the network stay as they are. A fake Messages API on loopback; a fake key.
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
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-open-')));
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
const DATA = join(ROOT, 'halcyonic');
const PROVING = join(ROOT, 'home', '.halcyonic-proving');
mkdirSync(PROVING);
writeFileSync(join(DATA, 'anthropic-api-key'), `${KEY}\n`, { mode: 0o600 });
writeFileSync(join(PROVING, 'marker.txt'), 'proving-marker-6e3b\n');
writeFileSync(join(ROOT, 'home', 'marker.txt'), 'home-marker-9a2e\n');
const COMMAND = [
  // The host's own folders: refused, read, listed or written.
  `cat ${DATA}/anthropic-api-key >> ${RESULTS} 2>&1; echo "data=$?" >> ${RESULTS}`,
  `ls ${DATA} > /dev/null 2>&1; echo "data-list=$?" >> ${RESULTS}`,
  `echo planted > ${DATA}/planted 2>/dev/null; echo "data-write=$?" >> ${RESULTS}`,
  `cat ${PROVING}/marker.txt >> ${RESULTS} 2>&1; echo "proving=$?" >> ${RESULTS}`,
  // The person's own home and folder: as before.
  `cat ${ROOT}/home/marker.txt >> ${RESULTS} 2>&1; echo "home=$?" >> ${RESULTS}`,
  `echo inside > ${PROJECT}/inside.txt`,
  // The key the command holds: the gate's run credential, never the person's.
  `echo "key=\${ANTHROPIC_API_KEY:-none} token=\${ANTHROPIC_AUTH_TOKEN:-none}" >> ${RESULTS}`,
  // The gate, asked without this run's credential: refused.
  `/usr/bin/curl -s --max-time 5 -o /dev/null -w 'bare=%{http_code}' -X POST "\${ANTHROPIC_BASE_URL}/v1/messages" >> ${RESULTS}; echo " bare-curl=$?" >> ${RESULTS}`,
].join('; ');

/** A Messages API that asks for one Bash call for the prompt marked TOOL, then answers Done. */
function fakeApi() {
  const bodies: string[] = [];
  let direct = 0;
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
      // A request with no body is no Claude Code turn (the test's own bare request, reaching the API itself where
      // nothing stands between): answered, and counted as one that came without the gate.
      if (raw.trim() === '') {
        direct += 1;
        res.writeHead(400, { 'content-type': 'application/json' }).end('{}');
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
  return {
    server,
    bodies,
    keys,
    get direct() {
      return direct;
    },
  };
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
    execution_id: `01920000-0000-7000-8000-0000000006${n}0` as ExecutionId,
    workstream_id: '01920000-0000-7000-8000-000000000602' as WorkstreamId,
    project_id: '01920000-0000-7000-8000-000000000601' as ProjectId,
  };
}

test("Claude Code on a connected folder holds no key and reaches none of the host's folders, its own folder and home as before", {
  skip: SKIP,
  timeout: 240_000,
}, async (t) => {
  const api = fakeApi();
  const base = await listen(api.server);
  t.after(() => {
    api.server.closeAllConnections();
    api.server.close();
  });
  const adapter = new ClaudeAgentRuntimeAdapter({
    directoryPolicy: (path) => ({ ok: true, directory: path }),
    processRecordFile: join(DATA, 'claude-agent-processes.json'),
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
    },
    pathToClaudeCodeExecutable: BINARY,
    // This Mac's own managed Claude Code settings are not the test's: none.
    remainingSettings: async () => ({}),
    confinement: {
      dataFolder: DATA,
      toolFolders: toolFoldersFor(process.execPath),
      hidden: [DATA],
      // A connected folder: never confined.
      forDirectory: () => null,
      hostFolders: [DATA],
    },
  });
  t.after(() => adapter.close());
  const observed: RuntimeObservation[] = [];
  const execution = context(1);
  await adapter.startExecution({
    execution,
    instruction: 'TOOL first MARKER-OPEN.',
    options: {},
    model_ref: null,
    directory: PROJECT,
    emit: (observation) => observed.push(observation),
  });
  await until(
    () => observed.some((o) => o.type === 'runtime.approval.requested'),
    60_000,
    () => `the command to ask; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  const asked = observed.find((o) => o.type === 'runtime.approval.requested');
  assert.ok(asked?.type === 'runtime.approval.requested');
  await adapter.respondToApproval({
    execution,
    approval_id: asked.payload.approval_id,
    decision: 'approve',
    message: null,
  });
  await until(
    () => observed.some((o) => o.type === 'runtime.turn.completed'),
    90_000,
    () => `the turn to finish; observed ${observed.map((o) => o.type).join(', ')}`,
  );
  // Outside the closed profile, and recorded so: the light profile makes no run confined.
  const started = observed.find((o) => o.type === 'runtime.execution.started');
  assert.equal((started?.payload as { confined?: boolean } | undefined)?.confined, false);
  const said = readFileSync(RESULTS, 'utf8');
  t.diagnostic(said);
  // Its own folder and the person's home as before.
  assert.equal(readFileSync(join(PROJECT, 'inside.txt'), 'utf8'), 'inside\n');
  assert.match(said, /home-marker-9a2e/);
  assert.match(said, /home=0/);
  // None of the host's folders: not read, listed or written.
  assert.match(said, /data=1/);
  assert.match(said, /data-list=1/);
  assert.match(said, /data-write=1/);
  assert.equal(existsSync(join(DATA, 'planted')), false);
  assert.match(said, /proving=1/);
  assert.doesNotMatch(said, /proving-marker-6e3b/);
  // It holds this run's credential from the gate, never the key; the gate refuses a request without it.
  assert.match(said, /key=halcyonic-gate-run-[0-9a-f]{64} token=none/);
  assert.match(said, /bare=403/);
  assert.equal(api.direct, 0, 'a request reached the API without the gate');
  assert.ok(!said.includes(KEY), "the person's key reached the command");
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
  // No file of its folder holds the key.
  assert.deepEqual(filesHolding(PROJECT, KEY), []);
});
