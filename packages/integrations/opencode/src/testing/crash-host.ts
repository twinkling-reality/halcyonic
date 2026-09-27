/**
 * Plays a control plane that is about to crash: it runs one OpenCode execution with a slow turn
 * and prints `{"serverPid": n}` once the turn runs. End to end tests kill this process to check
 * that the server it launched does not outlive it. Usage: `node crash-host.ts <json options>`
 * with `binaryPath`, `recordFile`, `env` and `directory`.
 */
import { OpenCodeRuntimeAdapter } from '../opencode-runtime.ts';
import { allowOnly } from './directory-policy.ts';
import { TEST_EXECUTION } from './observations.ts';

const options = JSON.parse(process.argv[2] ?? '{}') as {
  binaryPath: string;
  recordFile: string;
  env: Record<string, string>;
  directory: string;
};
const runtime = new OpenCodeRuntimeAdapter({
  binaryPath: options.binaryPath,
  serverRecordFile: options.recordFile,
  directoryPolicy: allowOnly(options.directory),
  env: options.env,
});
await runtime.startExecution({
  execution: TEST_EXECUTION,
  instruction: 'SLOW turn in a process that is about to crash.',
  options: { directory: options.directory },
  emit: (observation) => {
    if (observation.type === 'runtime.turn.started') {
      process.stdout.write(`${JSON.stringify({ serverPid: runtime.serverPid })}\n`);
    }
  },
});
setInterval(() => undefined, 1000);
