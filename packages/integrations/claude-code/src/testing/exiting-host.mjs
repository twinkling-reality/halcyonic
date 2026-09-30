// Starts executions through the adapter, prints `{"watchdog": <pid>}` once they have started, then
// ends this process in one of four ways, used by the adapter's process tests:
// - exit: process.exit without closing the adapter;
// - throw: an uncaught exception, the way a crashing control plane ends;
// - signal: like the control plane, wait for SIGTERM, close the adapter, and let the process end;
// - wait: keep running until the test kills it, for example with SIGKILL.
// In every mode, if its stdin ends first, it exits as `exit` does, so it does not outlive its test
// either.
// Argument: JSON with executable, cwd, home, processRecordFile, environment (variables added to
// the agents' environment), mode, and count (executions to start, default 1).
import { ClaudeAgentRuntimeAdapter } from '../index.ts';

// The test holds stdin open and never writes to it, so stdin ends when the test's process exits,
// however it exits.
const exit = () => process.exit(0);
process.stdin.on('end', exit);
process.stdin.on('error', exit);
process.stdin.resume();

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
    model_ref: null,
    emit: () => {},
  });
}

// Listening before the ready line below, because the test sends SIGTERM as soon as it reads that
// line, and a SIGTERM with no listener ends the process before the adapter is closed.
if (mode === 'signal') {
  const keepAlive = setInterval(() => {}, 60_000);
  process.once('SIGTERM', () => {
    // Reading stdin would keep this process alive after the close. Destroying it emits no `end`,
    // so the close runs to its end.
    process.stdin.destroy();
    void adapter.close().then(() => clearInterval(keepAlive));
  });
}

// Written before ending, since pipes to a parent are asynchronous on macOS.
// In signal mode the interval above keeps it alive until SIGTERM closes the adapter.
process.stdout.write(`${JSON.stringify({ watchdog: adapter.watchdogPid })}\n`, () => {
  if (mode === 'throw') {
    throw new Error('simulated control plane crash');
  } else if (mode === 'exit') {
    process.exit(0);
  } else if (mode === 'wait') {
    setInterval(() => {}, 60_000);
  }
});
