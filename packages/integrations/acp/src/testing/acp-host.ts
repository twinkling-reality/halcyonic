// A control plane stand-in for the process sweep's tests: it starts one execution on the fake app and
// prints the app's pid, then waits to be killed. Usage: node acp-host.ts <fake binary> <folder> <record file|none>
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { AcpRuntimeAdapter } from '../acp-runtime.ts';
import { ANTIGRAVITY } from '../profiles.ts';
import { allowOnly } from './directory-policy.ts';
import { TEST_EXECUTION } from './observations.ts';

const [binary, folder, record] = process.argv.slice(2) as [string, string, string];
const adapter = new AcpRuntimeAdapter({
  profile: ANTIGRAVITY,
  binaryPath: binary,
  directoryPolicy: allowOnly(folder),
  env: { PATH: process.env.PATH ?? '/usr/bin:/bin', FAKE_ACP_STAY: '1' },
  home: folder,
  processRecordFile: record === 'none' ? null : record,
  hostFolders: null,
});
await adapter.startExecution({
  execution: TEST_EXECUTION,
  instruction: 'Hold on.',
  options: {},
  model_ref: null,
  directory: join(folder, 'project'),
  emit: () => undefined,
});
process.stdout.write(`${readFileSync(join(folder, 'app.pid'), 'utf8').trim()}\n`);
setInterval(() => undefined, 1000);
