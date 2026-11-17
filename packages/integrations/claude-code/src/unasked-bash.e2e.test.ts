import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { query } from '@anthropic-ai/claude-agent-sdk';
import { OPEN_RUN_ASKS } from './claude-agent-runtime.ts';

/**
 * Which Bash commands the real Claude Code CLI the agent SDK bundles (CLAUDE_CODE_BIN) runs without asking, as a run
 * outside the closed profile launches it: default permission mode, no settings sources, OPEN_RUN_ASKS in its flag
 * settings (AUDIT3, 2026-11-17; validation/claude-code-unasked.md). A fake Messages API on loopback asks for one Bash
 * call per case; the host's callback records whether it was asked and denies. HOME is a scratch folder and every name
 * that could touch a credential or the network (gh, security, curl, git, ssh) is a shim on PATH that does nothing, so
 * a command Claude Code runs unasked reads nothing real. It fails when a new CLI changes the set.
 */
const BINARY = process.env.CLAUDE_CODE_BIN ?? '';
const SKIP =
  process.platform !== 'darwin'
    ? 'Claude Code is tested on macOS here'
    : BINARY === ''
      ? 'CLAUDE_CODE_BIN is not set; point it at the Claude Code binary the agent SDK bundles to run this test'
      : existsSync(BINARY)
        ? false
        : `CLAUDE_CODE_BIN does not exist: ${BINARY}`;

const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-unasked-bash-')));
after(() => rmSync(ROOT, { recursive: true, force: true }));
const HOME = join(ROOT, 'home');
const PROJECT = join(ROOT, 'project');
const SHIMS = join(ROOT, 'shims');
const CONFIG = join(ROOT, 'config');
for (const folder of [HOME, PROJECT, SHIMS, CONFIG]) mkdirSync(folder);
writeFileSync(join(PROJECT, 'README.md'), 'readme\n');
writeFileSync(join(HOME, 'marker.txt'), 'outside-marker\n');
for (const name of ['gh', 'security', 'curl', 'git', 'ssh', 'wget', 'nc'])
  writeFileSync(join(SHIMS, name), '#!/bin/sh\nexit 0\n', { mode: 0o755 });

/** Every tool name the CLI offered the model, from its requests. */
const OFFERED = new Set<string>();

/** One tool call the fake API asks for: a Bash command as text, or any tool by name and input. */
type Call = string | { readonly name: string; readonly input: Record<string, unknown> };

/** A Messages API that asks for the one tool call `call` for the prompt, then answers Done. */
function fakeApi(call: Call) {
  const name = typeof call === 'string' ? 'Bash' : call.name;
  const input = typeof call === 'string' ? { command: call, description: 'Probe' } : call.input;
  return createServer((req, res) => {
    let raw = '';
    req.on('data', (chunk) => {
      raw += chunk;
    });
    req.on('end', () => {
      if (req.method !== 'POST' || !req.url?.startsWith('/v1/messages')) {
        res.writeHead(404).end('{}');
        return;
      }
      if (req.url.includes('count_tokens')) {
        res.writeHead(200, { 'content-type': 'application/json' }).end('{"input_tokens":10}');
        return;
      }
      const body = (raw === '' ? {} : JSON.parse(raw)) as {
        stream?: boolean;
        model?: string;
        tools?: { name?: string }[];
      };
      for (const offered of body.tools ?? []) if (offered.name) OFFERED.add(offered.name);
      const tool = raw.includes('TOOL first') && !raw.includes('tool_result');
      const message = {
        id: `msg_${randomUUID()}`,
        type: 'message',
        role: 'assistant',
        model: body.model,
        stop_sequence: null,
        usage: { input_tokens: 10, output_tokens: 1 },
      };
      const block = tool
        ? {
            type: 'tool_use',
            id: `toolu_${randomUUID().slice(0, 8)}`,
            name,
            input,
          }
        : { type: 'text', text: 'Done.' };
      const stop = tool ? 'tool_use' : 'end_turn';
      if (body.stream !== true) {
        res
          .writeHead(200, { 'content-type': 'application/json' })
          .end(JSON.stringify({ ...message, content: [block], stop_reason: stop }));
        return;
      }
      res.writeHead(200, { 'content-type': 'text/event-stream' });
      const send = (event: string, data: unknown) =>
        res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
      send('message_start', {
        type: 'message_start',
        message: { ...message, content: [], stop_reason: null },
      });
      send('content_block_start', {
        type: 'content_block_start',
        index: 0,
        content_block: tool ? { ...block, input: {} } : { type: 'text', text: '' },
      });
      send('content_block_delta', {
        type: 'content_block_delta',
        index: 0,
        delta: tool
          ? {
              type: 'input_json_delta',
              partial_json: JSON.stringify(input),
            }
          : { type: 'text_delta', text: 'Done.' },
      });
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
}

/** Whether Claude Code asked before making `call`, launched as a run outside the closed profile is. */
async function asks(call: Call): Promise<boolean> {
  const server = fakeApi(call);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()));
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  let asked = false;
  try {
    const run = query({
      prompt: 'TOOL first',
      options: {
        cwd: PROJECT,
        pathToClaudeCodeExecutable: BINARY,
        settingSources: [],
        settings: { permissions: { ask: [...OPEN_RUN_ASKS] } },
        env: {
          HOME,
          PATH: `${SHIMS}:/usr/bin:/bin`,
          ANTHROPIC_BASE_URL: url,
          ANTHROPIC_API_KEY: 'sk-ant-dummy-halcyonic-test',
          CLAUDE_CONFIG_DIR: CONFIG,
          TMPDIR: `${ROOT}/`,
        },
        canUseTool: async () => {
          asked = true;
          return { behavior: 'deny', message: 'Denied by the test.' };
        },
      },
    });
    for await (const message of run) if (message.type === 'result') break;
  } finally {
    server.close();
  }
  return asked;
}

test('outside the closed profile, every git command that reads or reaches a remote asks, as do credentials, the network and every read or search outside the folder (AUDIT3, 2026-11-17)', {
  skip: SKIP,
  timeout: 600_000,
}, async () => {
  const asking = [
    'git remote -v',
    'git config --get remote.origin.url',
    'git config --get credential.helper',
    'git ls-remote',
    'git fetch',
    'git push',
    'env',
    'printenv',
    'curl -s https://example.com',
    'gh auth status',
    'gh auth token',
    'security find-generic-password -s gh:github.com -w',
    `cat ${HOME}/marker.txt`,
    `grep -r marker ${HOME}`,
    'cat README.md > copy.txt',
    // AUDIT3 M1: forms that hide a git command from a prefix rule.
    'git -C . remote -v',
    'git --no-pager remote -v',
    'git -c core.pager=cat config --get remote.origin.url',
    'GIT_DIR=.git git remote -v',
    'cd . && git remote -v',
    'ls && git remote -v',
    'git remote -v | cat',
    'command git remote -v',
    "sh -c 'git remote -v'",
    "bash -c 'git config --get remote.origin.url'",
  ];
  // AUDIT3 M2: the other tools that read or reach the network.
  const askingTools = [
    { name: 'Read', input: { file_path: join(HOME, 'marker.txt') } },
    { name: 'WebFetch', input: { url: 'http://127.0.0.1:9/page', prompt: 'Say what it says.' } },
    { name: 'WebSearch', input: { query: 'halora probe' } },
  ];
  const unaskedTools = [{ name: 'Read', input: { file_path: join(PROJECT, 'README.md') } }];
  // Claude Code's own read-only set inside the folder, as the person's own Claude Code runs it at the desk.
  const unasked = [
    'ls',
    'cat README.md',
    'grep -n readme README.md',
    'git status',
    'git log --oneline',
  ];
  // This CLI offers no Grep or Glob tool (it answers a call to either "No such tool available"), so a search runs
  // only through Bash, which the rules above hold. A CLI that offers them again fails here, to be probed first.
  const key = (call: Call) =>
    typeof call === 'string' ? call : `${call.name} ${JSON.stringify(call.input)}`;
  const seen: Record<string, boolean> = {};
  for (const call of [...asking, ...askingTools, ...unasked, ...unaskedTools])
    seen[key(call)] = await asks(call);
  assert.deepEqual(
    seen,
    Object.fromEntries([
      ...[...asking, ...askingTools].map((call) => [key(call), true]),
      ...[...unasked, ...unaskedTools].map((call) => [key(call), false]),
    ]),
  );
  assert.ok(OFFERED.has('Bash') && OFFERED.has('Read'), [...OFFERED].join(', '));
  assert.deepEqual(
    [...OFFERED].filter((name) => name === 'Grep' || name === 'Glob'),
    [],
    'Claude Code offers a search tool again: probe it outside the folder before this CLI is used',
  );
});
