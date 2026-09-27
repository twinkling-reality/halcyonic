#!/usr/bin/env node
/**
 * A stand-in for the Codex 0.157.0 binary in unit tests, for behavior the real app-server cannot
 * be made to show on demand. It answers `--version` and speaks the part of the app-server protocol
 * the adapter uses, shaped like the messages the smoke test captured. FAKE_CODEX_MODE, a
 * comma-separated list, chooses its behavior:
 *
 * - `version=<v>`: the version `--version` prints (default 0.157.0);
 * - `agent=<v>`: the version in the user agent `initialize` answers (default 0.157.0);
 * - `never`: `thread/start` answers that the thread's approval policy is `never`;
 * - `silent-interrupt`: `turn/interrupt` is never answered;
 * - `writer-held`: `thread/resume` fails as when another Codex process holds the thread;
 * - `ask`: every turn raises an `item/tool/requestUserInput` request.
 *
 * A turn runs until it is interrupted, unless its text contains COMPLETE. Every message received
 * is appended to FAKE_CODEX_LOG when it is set.
 */
import { appendFileSync } from 'node:fs';
import { createInterface } from 'node:readline';

const flags = new Set((process.env.FAKE_CODEX_MODE ?? '').split(',').filter(Boolean));
const option = (name, fallback) =>
  [...flags].find((flag) => flag.startsWith(`${name}=`))?.slice(name.length + 1) ?? fallback;

if (process.argv[2] === '--version') {
  process.stdout.write(`codex-cli ${option('version', '0.157.0')}\n`);
  process.exit(0);
}
if (process.argv[2] !== 'app-server') process.exit(2);

const SANDBOX_TYPES = {
  'read-only': 'readOnly',
  'workspace-write': 'workspaceWrite',
  'danger-full-access': 'dangerFullAccess',
};
let counter = 0;
let serverRequests = 0;
const send = (message) => process.stdout.write(`${JSON.stringify(message)}\n`);
const notify = (method, params) => send({ method, params, emittedAtMs: Date.now() });
const turn = (id, status) => ({ id, items: [], status, error: null });

createInterface({ input: process.stdin }).on('line', (line) => {
  if (process.env.FAKE_CODEX_LOG) appendFileSync(process.env.FAKE_CODEX_LOG, `${line}\n`);
  const message = JSON.parse(line);
  const { id, method, params } = message;
  if (method === undefined) return;
  const respond = (result) => send({ id, result });
  const refuse = (text) => send({ id, error: { code: -32600, message: text } });
  switch (method) {
    case 'initialize':
      respond({
        userAgent: `${params.clientInfo.name}/${option('agent', '0.157.0')} (fake)`,
        codexHome: '/fake/codex-home',
        platformFamily: 'unix',
        platformOs: 'fake',
      });
      return;
    case 'initialized':
      return;
    case 'thread/start':
    case 'thread/resume':
      if (method === 'thread/resume' && flags.has('writer-held')) {
        refuse(`thread ${params.threadId} already has an active writer`);
        return;
      }
      counter += 1;
      respond({
        thread: { id: params.threadId ?? `thread-${process.pid}-${counter}` },
        approvalPolicy: flags.has('never') ? 'never' : params.approvalPolicy,
        approvalsReviewer: params.approvalsReviewer,
        sandbox: { type: SANDBOX_TYPES[params.sandbox] },
      });
      return;
    case 'thread/turns/list':
      respond({ data: [], nextCursor: null, backwardsCursor: null });
      return;
    case 'turn/start': {
      counter += 1;
      const turnId = `turn-${process.pid}-${counter}`;
      respond({ turn: turn(turnId, 'inProgress') });
      notify('turn/started', { threadId: params.threadId, turn: turn(turnId, 'inProgress') });
      if (flags.has('ask')) {
        send({
          id: serverRequests++,
          method: 'item/tool/requestUserInput',
          params: { threadId: params.threadId, turnId, itemId: 'call-1', questions: [] },
        });
      }
      if (params.input.some((input) => input.text.includes('COMPLETE'))) {
        notify('turn/completed', { threadId: params.threadId, turn: turn(turnId, 'completed') });
      }
      return;
    }
    case 'turn/steer':
      respond({ turnId: params.expectedTurnId });
      return;
    case 'turn/interrupt':
      if (flags.has('silent-interrupt')) return;
      respond({});
      notify('turn/completed', {
        threadId: params.threadId,
        turn: turn(params.turnId, 'interrupted'),
      });
      return;
    default:
      refuse(`unknown method ${method}`);
  }
});
process.stdin.on('end', () => process.exit(0));
