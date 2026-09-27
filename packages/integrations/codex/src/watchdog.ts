/**
 * Stops the Codex app-server when the adapter that launched it can no longer do so. When the
 * adapter's process dies, however it dies, the server's input ends and Codex 0.157.0 shuts down by
 * itself, stopping the commands it runs; this small process covers a server that does not. The
 * adapter's process holds this process's stdin open; stdin ends when that process exits, or when
 * the adapter closes it after the server has exited.
 *
 * The first stdin line is the server record. On end of input, or on SIGTERM, SIGINT or SIGHUP, the
 * server is stopped if the process with the recorded pid is still that server.
 */
import { parseServerRecord, type ServerRecord, stopRecordedProcess } from './server-record.ts';

let buffered = '';
let record: ServerRecord | null = null;
let stopping = false;

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk: string) => {
  if (record !== null) return;
  buffered += chunk;
  const newline = buffered.indexOf('\n');
  if (newline >= 0) record = parseServerRecord(buffered.slice(0, newline));
});
process.stdin.on('end', stop);
process.stdin.on('error', stop);
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
process.on('SIGHUP', stop);

function stop(): void {
  if (stopping) return;
  stopping = true;
  if (record === null) process.exit(0);
  stopRecordedProcess(record).then(
    () => process.exit(0),
    () => process.exit(1),
  );
}
