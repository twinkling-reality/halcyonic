// Starts one execution through the adapter, then ends this process in one of three ways, used by
// the adapter's process tests:
// - exit: process.exit without closing the adapter;
// - throw: an uncaught exception, the way a crashing control plane ends;
// - signal: like the control plane, wait for SIGTERM, close the adapter, and let the process end.
// Arguments: <executable> <cwd> <home> <record file> <exit | throw | signal>
import { ClaudeAgentRuntimeAdapter } from '../index.ts';

const [executable, cwd, home, record, mode] = process.argv.slice(2);

const adapter = new ClaudeAgentRuntimeAdapter({
  inheritedEnvironment: {
    PATH: '/usr/bin:/bin',
    HOME: home,
    ANTHROPIC_API_KEY: 'test-key-not-real',
  },
  environment: { FAKE_CLAUDE_RECORD: record, FAKE_CLAUDE_IGNORE_EOF: '1' },
  pathToClaudeCodeExecutable: executable,
});

await adapter.startExecution({
  execution: {
    execution_id: '01920000-0000-7000-8000-000000000103',
    workstream_id: '01920000-0000-7000-8000-000000000102',
    project_id: '01920000-0000-7000-8000-000000000101',
  },
  instruction: 'Keep working.',
  options: { cwd },
  emit: () => {},
});

if (mode === 'signal') {
  const keepAlive = setInterval(() => {}, 60_000);
  process.once('SIGTERM', () => {
    void adapter.close().then(() => clearInterval(keepAlive));
  });
  process.stdout.write('ready\n');
} else if (mode === 'throw') {
  throw new Error('simulated control plane crash');
} else {
  process.exit(0);
}
