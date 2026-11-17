// A stand-in ACP agent app that replays a trace recorded from a real one (fixtures/*.jsonl), for the
// adapter's unit tests. Each fixture line is one step, in the order the real app took it:
//   {"expect": method}            wait for the client's request of that method
//   {"answer": method, result|error}  answer the client's latest request of that method
//   {"send": message}             write the app's own message as recorded
//   {"expectResponse": id}        wait for the client's answer to the app's request with that id
//   {"expectNotification": method}  wait for the client's notification of that method
//   {"exit": code}                stop at once, as an app that crashed (a test cuts a recorded trace there)
// The client's messages may come in another order than recorded (an interrupt answers the open
// request at once): a step takes the first waiting message it matches. `$CWD` stands for the folder
// the app was started in. Every message the client sends is appended to FAKE_ACP_LOG as one JSON line.
// FAKE_ACP_VERSION and FAKE_ACP_PROTOCOL, when set, replace the version and the protocol version the app reports. Usage: node fake-acp-agent.mjs <fixture>
import { appendFileSync, readFileSync } from 'node:fs';

const steps = readFileSync(process.argv[2], 'utf8')
  .split('\n')
  .filter((line) => line.trim() !== '')
  .map((line) => JSON.parse(line.replaceAll('$CWD', process.cwd())));
const log = process.env.FAKE_ACP_LOG;
const received = [];
const lastRequestId = new Map();
let waiting = null;
let buffered = '';

const write = (message) => process.stdout.write(`${JSON.stringify(message)}\n`);

function matches(step, message) {
  if (step.expect !== undefined) return message.method === step.expect && message.id !== undefined;
  if (step.expectNotification !== undefined)
    return message.method === step.expectNotification && message.id === undefined;
  if (step.expectResponse !== undefined)
    return message.method === undefined && String(message.id) === String(step.expectResponse);
  return false;
}

function take(step) {
  const index = received.findIndex((message) => matches(step, message));
  if (index < 0) return null;
  const [message] = received.splice(index, 1);
  if (message.method !== undefined && message.id !== undefined)
    lastRequestId.set(message.method, message.id);
  return message;
}

async function run() {
  for (const step of steps) {
    if (step.exit !== undefined) process.exit(step.exit);
    if (step.send !== undefined) write(step.send);
    else if (step.answer !== undefined) {
      const id = lastRequestId.get(step.answer);
      let result = step.result;
      if (step.answer === 'initialize' && process.env.FAKE_ACP_VERSION && result?.agentInfo) {
        result = {
          ...result,
          agentInfo: { ...result.agentInfo, version: process.env.FAKE_ACP_VERSION },
        };
      }
      if (step.answer === 'initialize' && process.env.FAKE_ACP_PROTOCOL) {
        result = { ...result, protocolVersion: Number(process.env.FAKE_ACP_PROTOCOL) };
      }
      write(
        step.error === undefined
          ? { jsonrpc: '2.0', id, result }
          : { jsonrpc: '2.0', id, error: step.error },
      );
    } else {
      while (take(step) === null) {
        await new Promise((resolve) => {
          waiting = resolve;
        });
      }
    }
  }
}

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  buffered += chunk;
  for (let newline = buffered.indexOf('\n'); newline >= 0; newline = buffered.indexOf('\n')) {
    const line = buffered.slice(0, newline);
    buffered = buffered.slice(newline + 1);
    if (line.trim() === '') continue;
    if (log) appendFileSync(log, `${line}\n`);
    received.push(JSON.parse(line));
    waiting?.();
  }
});
// As the real app does, it ends at the end of its input.
process.stdin.on('end', () => process.exit(0));
run().then(() => undefined);
