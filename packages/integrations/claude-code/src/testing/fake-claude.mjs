#!/usr/bin/env node
// A stand-in for the Claude Code CLI, used by the adapter's process tests. It is not Claude Code
// and contacts nothing. It speaks just enough of the stream-json protocol for the Agent SDK: it
// answers the initialize request, and answers each user message with system/init and a successful
// result. It records its arguments, working directory, the names of its environment variables,
// what kind of file each of its standard streams is, every line it receives, and any stopping
// signal in `<pid>.json` in the directory named by FAKE_CLAUDE_RECORD_DIR, so that several
// processes can share one environment. Values are recorded only for HOME, CLAUDE_CONFIG_DIR and
// PATH. With FAKE_CLAUDE_PROCESS_RECORD naming the adapter's process record file, it also records
// whether that file already listed this process when its first input arrived. With
// FAKE_CLAUDE_IGNORE_EOF=1 it keeps running after its input closes, like a CLI still finishing a
// turn, until a signal stops it.
import { randomUUID } from 'node:crypto';
import { fstatSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { createInterface } from 'node:readline';

const args = process.argv.slice(2);
const sessionFlag = args.find((arg) => arg.startsWith('--session-id='));
const sessionId =
  sessionFlag === undefined ? randomUUID() : sessionFlag.slice('--session-id='.length);

function kind(fd) {
  const stat = fstatSync(fd);
  if (stat.isFIFO()) return 'fifo';
  if (stat.isSocket()) return 'socket';
  if (stat.isCharacterDevice()) return 'character device';
  return stat.isFile() ? 'file' : 'other';
}

const record = {
  pid: process.pid,
  args,
  cwd: process.cwd(),
  env_names: Object.keys(process.env).sort(),
  home: process.env.HOME ?? null,
  config_dir: process.env.CLAUDE_CONFIG_DIR ?? null,
  path: process.env.PATH ?? null,
  stdio: [0, 1, 2].map(kind),
  listed_before_input: null,
  received: [],
  input_closed: false,
  signal: null,
};

function listsThisProcess(file) {
  try {
    const { processes } = JSON.parse(readFileSync(file, 'utf8'));
    return processes.some((entry) => entry.pid === process.pid);
  } catch {
    return false;
  }
}

const recordFile = process.env.FAKE_CLAUDE_RECORD_DIR
  ? join(process.env.FAKE_CLAUDE_RECORD_DIR, `${process.pid}.json`)
  : undefined;

// Replaced atomically, so a test never reads half a record.
function save() {
  if (!recordFile) return;
  writeFileSync(`${recordFile}.tmp`, JSON.stringify(record, null, 2));
  renameSync(`${recordFile}.tmp`, recordFile);
}

function send(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

const usage = {
  cache_creation: { ephemeral_1h_input_tokens: 0, ephemeral_5m_input_tokens: 0 },
  cache_creation_input_tokens: 0,
  cache_read_input_tokens: 0,
  input_tokens: 0,
  output_tokens: 0,
  server_tool_use: { web_fetch_requests: 0, web_search_requests: 0 },
  service_tier: 'standard',
};

function answer() {
  send({
    type: 'system',
    subtype: 'init',
    apiKeySource: 'ANTHROPIC_API_KEY',
    claude_code_version: 'fake',
    cwd: process.cwd(),
    tools: [],
    mcp_servers: [],
    model: 'fake',
    permissionMode: 'default',
    slash_commands: [],
    output_style: 'default',
    skills: [],
    plugins: [],
    uuid: randomUUID(),
    session_id: sessionId,
  });
  send({
    type: 'result',
    subtype: 'success',
    duration_ms: 0,
    duration_api_ms: 0,
    is_error: false,
    num_turns: 1,
    result: 'Done.',
    stop_reason: 'end_turn',
    total_cost_usd: 0,
    usage,
    modelUsage: {},
    permission_denials: [],
    uuid: randomUUID(),
    session_id: sessionId,
  });
}

save();

const input = createInterface({ input: process.stdin });
input.on('line', (line) => {
  if (record.listed_before_input === null && process.env.FAKE_CLAUDE_PROCESS_RECORD) {
    record.listed_before_input = listsThisProcess(process.env.FAKE_CLAUDE_PROCESS_RECORD);
  }
  const message = JSON.parse(line);
  record.received.push(message);
  save();
  if (message.type === 'control_request' && message.request?.subtype === 'initialize') {
    send({
      type: 'control_response',
      response: {
        subtype: 'success',
        request_id: message.request_id,
        response: {
          commands: [],
          agents: [],
          output_style: 'default',
          available_output_styles: ['default'],
          models: [],
          account: {},
        },
      },
    });
  } else if (message.type === 'user') {
    answer();
  }
});
input.on('close', () => {
  record.input_closed = true;
  save();
  if (process.env.FAKE_CLAUDE_IGNORE_EOF === '1') setInterval(() => {}, 60_000);
  else process.exit(0);
});

for (const [signal, code] of [
  ['SIGTERM', 143],
  ['SIGINT', 130],
]) {
  process.on(signal, () => {
    record.signal = signal;
    save();
    process.exit(code);
  });
}
