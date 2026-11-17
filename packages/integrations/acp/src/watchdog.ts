/**
 * Stops the ACP agent app processes an adapter launched when the adapter can no longer do so: its
 * process killed (SIGKILL, out of memory) runs no exit handler, and an app need not end at the end
 * of its input (AUDIT3 AS1). The adapter runs this small detached process beside them and holds
 * its stdin open; stdin ends when the adapter's process exits, however it exits, or when the
 * adapter closes it.
 *
 * Each stdin line is the complete list of processes to watch, as in the record file; the last one
 * received counts. On end of input, or on SIGTERM, SIGINT or SIGHUP, each listed process is stopped
 * if the process with its pid is still that process.
 */
import { type ProcessRecord, parseProcessRecords, stopRecordedProcess } from './process-record.ts';

let buffered = '';
let watched: readonly ProcessRecord[] = [];
let stopping = false;

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk: string) => {
  buffered += chunk;
  for (let newline = buffered.indexOf('\n'); newline >= 0; newline = buffered.indexOf('\n')) {
    watched = parseProcessRecords(buffered.slice(0, newline)) ?? watched;
    buffered = buffered.slice(newline + 1);
  }
});
process.stdin.on('end', stop);
process.stdin.on('error', stop);
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
process.on('SIGHUP', stop);

function stop(): void {
  if (stopping) return;
  stopping = true;
  void Promise.allSettled(watched.map((record) => stopRecordedProcess(record))).then((results) =>
    process.exit(results.every((result) => result.status === 'fulfilled') ? 0 : 1),
  );
}
