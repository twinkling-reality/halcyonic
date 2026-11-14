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
import {
  type ExecutionId,
  type ProjectId,
  parseEventEnvelope,
  type RuntimeId,
  type WorkstreamId,
} from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';

/**
 * An agent deleting text behind an approval, on the real CLI the agent SDK bundles (CLAUDE_CODE_BIN): an Edit whose
 * replacement is empty, and one whose replaced text is only white space. Each change's before and after are content,
 * so an empty or blank one is recorded as it is (ADR 0065), the person can answer it, and the turn ends. A fake
 * Messages API on loopback asks for one Edit per prompt; the key is a dummy; every proxy variable points at a trap.
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
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-deletion-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
for (const name of ['home', 'config', 'project', 'tmp', 'halcyonic']) mkdirSync(join(ROOT, name));
const PROJECT = join(ROOT, 'project');
process.env.CLAUDE_CONFIG_DIR = join(ROOT, 'config');

/** The Edit each prompt marker asks for: the file, the text replaced and its replacement. */
const EDITS: Readonly<Record<string, { file: string; old_string: string; new_string: string }>> = {
  'EDIT-empty': { file: 'notes.txt', old_string: 'drop me\n', new_string: '' },
  'EDIT-blank': { file: 'spaced.txt', old_string: ' \n', new_string: '' },
};

/** A Messages API that asks for the Edit of the latest prompt's marker, then answers Done. */
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
      const markers = [...raw.matchAll(/EDIT-(empty|blank)/g)];
      const last = markers.at(-1);
      const answered = last !== undefined && raw.lastIndexOf('tool_result') > (last.index ?? 0);
      const edit = last === undefined || answered ? null : EDITS[last[0]];
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 1 },
      };
      const block =
        edit === null || edit === undefined
          ? { type: 'text', text: 'Done.' }
          : {
              type: 'tool_use',
              id: `toolu_${randomUUID().slice(0, 8)}`,
              name: 'Edit',
              input: {
                file_path: join(PROJECT, edit.file),
                old_string: edit.old_string,
                new_string: edit.new_string,
              },
            };
      const stop = block.type === 'tool_use' ? 'tool_use' : 'end_turn';
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
          delta: {
            type: 'input_json_delta',
            partial_json: JSON.stringify((block as { input: unknown }).input),
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
  execution_id: '01920000-0000-7000-8000-000000000610' as ExecutionId,
  workstream_id: '01920000-0000-7000-8000-000000000602' as WorkstreamId,
  project_id: '01920000-0000-7000-8000-000000000601' as ProjectId,
};

/** Each observation forms a contract-valid event, the way the control plane journals one. */
function assertJournalable(observations: readonly RuntimeObservation[]): void {
  for (const [index, observation] of observations.entries()) {
    const parsed = parseEventEnvelope({
      schema_version: 1,
      event_id: `01920000-0000-7000-8000-${String(index + 600).padStart(12, '0')}`,
      event_type: observation.type,
      ...execution,
      source: { kind: 'runtime', runtime_id: 'claude-agent' as RuntimeId },
      source_native_id: observation.native_event_id,
      sequence: observation.sequence,
      occurred_at: observation.occurred_at,
      ingested_at: observation.occurred_at,
      correlation_id: null,
      causation_id: null,
      provenance: observation.provenance,
      payload: observation.payload,
    });
    assert.ok(
      parsed.ok,
      `${observation.type}: ${JSON.stringify(parsed.ok ? null : parsed.issues)}`,
    );
  }
}

test('an Edit that deletes text, and one that removes a blank line, are asked with their empty and blank text as content, approved, and done, and each turn ends', {
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
  writeFileSync(join(PROJECT, 'notes.txt'), 'keep\ndrop me\n');
  writeFileSync(join(PROJECT, 'spaced.txt'), 'a\n \nb\n');
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
    // This Mac's own managed Claude Code settings are not the test's: none.
    remainingSettings: async () => ({}),
  });
  t.after(() => adapter.close());
  const observed: RuntimeObservation[] = [];
  const types = () => observed.map((o) => o.type).join(', ');
  const approve = async (count: number) => {
    await until(
      () => observed.filter((o) => o.type === 'runtime.approval.requested').length >= count,
      60_000,
      () => `the Edit to ask; observed ${types()}`,
    );
    const asked = observed.filter((o) => o.type === 'runtime.approval.requested')[count - 1];
    assert.ok(asked?.type === 'runtime.approval.requested');
    await adapter.respondToApproval({
      execution,
      approval_id: asked.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    return asked.payload.subject;
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
  const change = (file: string, before: string, after: string) => ({
    path: join(PROJECT, file),
    real_path: null,
    to_path: null,
    kind: 'update',
    shape: 'replace',
    before,
    after,
    every: false,
    cell: null,
  });

  // The replacement is empty: the change's after is "", the text it removes is its before.
  await adapter.startExecution({
    execution,
    instruction: 'EDIT-empty, take the line out.',
    options: {},
    model_ref: null,
    directory: PROJECT,
    emit: (observation) => observed.push(observation),
  });
  const first = await approve(1);
  assert.equal(first.kind, 'tool_use');
  assert.deepEqual(first.kind === 'tool_use' ? first.changes : null, [
    change('notes.txt', 'drop me\n', ''),
  ]);
  await finished(1);
  assert.equal(readFileSync(join(PROJECT, 'notes.txt'), 'utf8'), 'keep\n');

  // The replaced text is only white space, and the replacement empty: both are content.
  await adapter.sendInstruction({ execution, text: 'EDIT-blank, take the blank line out.' });
  const second = await approve(2);
  assert.deepEqual(second.kind === 'tool_use' ? second.changes : null, [
    change('spaced.txt', ' \n', ''),
  ]);
  await finished(2);
  assert.equal(readFileSync(join(PROJECT, 'spaced.txt'), 'utf8'), 'a\nb\n');

  assert.equal(
    observed.filter((o) => o.type === 'runtime.turn.completed').length,
    2,
    `both turns end; observed ${types()}`,
  );
  assertJournalable(observed);
  assert.deepEqual(refusing.egress, []);
});
