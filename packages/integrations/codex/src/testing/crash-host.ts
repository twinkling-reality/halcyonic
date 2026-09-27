/**
 * Plays a control plane that is about to crash: it runs one Codex execution whose turn runs a slow
 * command, and prints `{"serverPid": n}` once the command runs. End to end tests kill this process
 * to check that neither the server it launched nor the command outlives it. Usage:
 * `node crash-host.ts <json options>` with `binaryPath`, `recordFile`, `env`, `directory` and
 * `instruction`.
 */
import { CodexRuntimeAdapter } from '../codex-runtime.ts';
import { allowOnly } from './directory-policy.ts';
import { TEST_EXECUTION } from './observations.ts';

const options = JSON.parse(process.argv[2] ?? '{}') as {
  binaryPath: string;
  recordFile: string;
  env: Record<string, string>;
  directory: string;
  instruction: string;
};
const runtime = new CodexRuntimeAdapter({
  binaryPath: options.binaryPath,
  serverRecordFile: options.recordFile,
  directoryPolicy: allowOnly(options.directory),
  env: options.env,
});
await runtime.startExecution({
  execution: TEST_EXECUTION,
  instruction: options.instruction,
  options: { cwd: options.directory },
  emit: (observation) => {
    if (observation.type === 'runtime.tool.started') {
      process.stdout.write(`${JSON.stringify({ serverPid: runtime.serverPid })}\n`);
    }
  },
});
setInterval(() => undefined, 1000);
