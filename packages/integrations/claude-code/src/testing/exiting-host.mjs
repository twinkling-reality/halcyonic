// Starts executions through the adapter, prints `{"watchdog": <pid>}` once they have started, then
// ends this process in one of four ways, used by the adapter's process tests:
// - exit: process.exit without closing the adapter;
// - throw: an uncaught exception, the way a crashing control plane ends;
// - signal: like the control plane, wait for SIGTERM, close the adapter, and let the process end;
// - wait: keep running until the test kills it, for example with SIGKILL.
// Argument: JSON with executable, cwd, home, processRecordFile, environment (variables added to
// the agents' environment), mode, and count (executions to start, default 1).
import { ClaudeAgentRuntimeAdapter } from '../index.ts';

const {
  executable,
  cwd,
  home,
  processRecordFile,
  environment,
  mode,
  count = 1,
} = JSON.parse(process.argv[2]);

const adapter = new ClaudeAgentRuntimeAdapter({
  directoryPolicy: (path) => ({ ok: true, directory: path }),
  processRecordFile,
  inheritedEnvironment: {
    PATH: '/usr/bin:/bin',
    HOME: home,
    ANTHROPIC_API_KEY: 'test-key-not-real',
  },
  environment,
  pathToClaudeCodeExecutable: executable,
});

for (let index = 0; index < count; index += 1) {
  await adapter.startExecution({
    execution: {
      execution_id: `01920000-0000-7000-8000-${String(103 + index).padStart(12, '0')}`,
      workstream_id: '01920000-0000-7000-8000-000000000102',
      project_id: '01920000-0000-7000-8000-000000000101',
    },
    instruction: 'Keep working.',
    options: { cwd },
    emit: () => {},
  });
}

// Written before ending, since pipes to a parent are asynchronous on macOS.
process.stdout.write(`${JSON.stringify({ watchdog: adapter.watchdogPid })}\n`, () => {
  if (mode === 'signal') {
    const keepAlive = setInterval(() => {}, 60_000);
    process.once('SIGTERM', () => {
      void adapter.close().then(() => clearInterval(keepAlive));
    });
  } else if (mode === 'throw') {
    throw new Error('simulated control plane crash');
  } else if (mode === 'exit') {
    process.exit(0);
  } else {
    setInterval(() => {}, 60_000);
  }
});
