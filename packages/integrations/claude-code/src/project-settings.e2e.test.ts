import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';

/**
 * What a project's own files make Claude Code do by themselves, on the real CLI the agent SDK
 * bundles (CLAUDE_CODE_BIN): nothing. A project's CLAUDE.md, its AGENTS.md where it has no CLAUDE.md,
 * and the hooks in its `.claude/settings.json` and `.claude/settings.local.json` are not loaded; the
 * person's own settings and hooks are, so Salidium and Seorak still observe the session. A fake
 * Messages API on loopback answers and records every request; the key is a dummy; each hook only
 * creates a marker file of the test's own, so a hook that ran shows as a file that exists.
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
const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-project-settings-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
const MARKS = join(ROOT, 'marks');
for (const name of ['home', 'config', 'tmp', 'halcyonic', 'marks', 'rules', 'agents']) {
  mkdirSync(join(ROOT, name));
}

/** Settings whose hooks each create one marker file when Claude Code runs them. */
function hooks(prefix: string, more: Record<string, unknown> = {}): string {
  const touch = (event: string) => [
    {
      hooks: [{ type: 'command', command: `/usr/bin/touch ${join(MARKS, `${prefix}-${event}`)}` }],
    },
  ];
  return JSON.stringify({
    ...more,
    hooks: { SessionStart: touch('session-start'), UserPromptSubmit: touch('prompt') },
  });
}

// The person's own Claude Code configuration: their instructions and a hook, which still load.
writeFileSync(join(ROOT, 'config', 'settings.json'), hooks('user'));
writeFileSync(join(ROOT, 'config', 'CLAUDE.md'), 'The person says: user-rules-5b0e.\n');

// A project with its own CLAUDE.md, CLAUDE.local.md, rules folder and skill, hooks in both of its
// settings files, and an MCP server its settings approve, which creates a marker when started.
const RULES = join(ROOT, 'rules');
mkdirSync(join(RULES, '.claude', 'rules'), { recursive: true });
mkdirSync(join(RULES, '.claude', 'skills', 'house-style'), { recursive: true });
writeFileSync(join(RULES, 'CLAUDE.md'), 'The project says: claude-md-rules-71c4.\n');
writeFileSync(join(RULES, 'CLAUDE.local.md'), 'The project says: claude-local-rules-2a9f.\n');
writeFileSync(
  join(RULES, '.claude', 'rules', 'style.md'),
  'The project says: rules-folder-c3d1.\n',
);
writeFileSync(
  join(RULES, '.claude', 'skills', 'house-style', 'SKILL.md'),
  '---\nname: house-style\ndescription: The house style, skill-rules-90ab.\n---\nWrite in the house style.\n',
);
writeFileSync(
  join(RULES, '.claude', 'settings.json'),
  hooks('project', { enableAllProjectMcpServers: true }),
);
writeFileSync(
  join(RULES, '.claude', 'settings.local.json'),
  hooks('local', { enableAllProjectMcpServers: true }),
);
writeFileSync(
  join(RULES, '.mcp.json'),
  JSON.stringify({
    mcpServers: {
      planted: { command: '/usr/bin/touch', args: [join(MARKS, 'project-mcp-server')] },
    },
  }),
);

// A project with an AGENTS.md and no CLAUDE.md, which the bundled CLI reads in its place by default.
const AGENTS = join(ROOT, 'agents');
writeFileSync(join(AGENTS, 'AGENTS.md'), 'The project says: agents-md-rules-e8d2.\n');

/** A Messages API that answers Done to everything and keeps each request's body. */
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
      const body = JSON.parse(raw) as { stream?: boolean; model?: string };
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 1 },
      };
      if (body.stream !== true) {
        res.writeHead(200, { 'content-type': 'application/json' }).end(
          JSON.stringify({
            ...message,
            content: [{ type: 'text', text: 'Done.' }],
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
      send('content_block_delta', {
        type: 'content_block_delta',
        index: 0,
        delta: { type: 'text_delta', text: 'Done.' },
      });
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

const marked = (name: string) => existsSync(join(MARKS, name));

test("Claude Code loads the person's own settings and instructions, and none of a project's own", {
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
    },
    pathToClaudeCodeExecutable: BINARY,
  });
  t.after(() => adapter.close());

  const run = async (directory: string, n: number, marker: string): Promise<string> => {
    const observed: RuntimeObservation[] = [];
    const before = api.bodies.length;
    await adapter.startExecution({
      execution: {
        execution_id: `01920000-0000-7000-8000-0000000005${n}0` as ExecutionId,
        workstream_id: '01920000-0000-7000-8000-000000000502' as WorkstreamId,
        project_id: '01920000-0000-7000-8000-000000000501' as ProjectId,
      },
      instruction: `Say done ${marker}.`,
      options: {},
      model_ref: null,
      directory,
      emit: (observation) => observed.push(observation),
    });
    await until(
      () => observed.some((o) => o.type === 'runtime.turn.completed'),
      90_000,
      () => `the turn to finish; observed ${observed.map((o) => o.type).join(', ')}`,
    );
    const sent = api.bodies.slice(before).filter((body) => body.includes(marker));
    assert.ok(sent.length >= 1, 'the instruction reached the API');
    return sent.join('\n');
  };

  const withClaudeMd = await run(RULES, 1, 'MARKER-RULES');
  // The person's own instructions and hooks load: the observers' hooks keep running.
  assert.match(withClaudeMd, /user-rules-5b0e/, "the person's own CLAUDE.md");
  assert.ok(marked('user-prompt'), "the person's own prompt hook ran");
  // None of the project's own: its CLAUDE.md, the hooks of either of its settings files.
  assert.doesNotMatch(withClaudeMd, /claude-md-rules-71c4/, "the project's CLAUDE.md was sent");
  assert.doesNotMatch(
    withClaudeMd,
    /claude-local-rules-2a9f/,
    "the project's CLAUDE.local.md was sent",
  );
  assert.doesNotMatch(withClaudeMd, /rules-folder-c3d1/, "the project's .claude/rules was sent");
  assert.doesNotMatch(withClaudeMd, /skill-rules-90ab/, "the project's skill was offered");
  for (const name of [
    'project-session-start',
    'project-prompt',
    'local-session-start',
    'local-prompt',
    'project-mcp-server',
  ]) {
    assert.equal(marked(name), false, `the project's hook ran: ${name}`);
  }

  const withAgentsMd = await run(AGENTS, 2, 'MARKER-AGENTS');
  assert.match(withAgentsMd, /user-rules-5b0e/, "the person's own CLAUDE.md");
  assert.doesNotMatch(withAgentsMd, /agents-md-rules-e8d2/, "the project's AGENTS.md was sent");
});
