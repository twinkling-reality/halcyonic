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
 * - `other-model`: `thread/start` and `thread/resume` answer that the thread runs `gpt-5.5` from
 *   `openai`, whatever was asked (otherwise they answer the model and provider asked for, or those
 *   defaults);
 * - `other-cwd`: `thread/start` and `thread/resume` answer that the thread works in
 *   `/somewhere/else` (otherwise in the `cwd` asked for);
 * - `silent-interrupt`: `turn/interrupt` is never answered;
 * - `writer-held`: `thread/resume` fails as when another Codex process holds the thread;
 * - `ask`: every turn raises an `item/tool/requestUserInput` request with one question, red or blue,
 *   and `ask-secret` one whose question is secret; an answer to it is confirmed with
 *   `serverRequest/resolved`, as Codex does, except the first with `deaf-once`, which is ignored
 *   as Codex ignores a message it cannot read;
 * - `elicit`: every turn raises an `mcpServer/elicitation/request`;
 * - `managed`: a managed layer that outranks the launch's overrides turns plugins back on;
 * - `requirements`: `configRequirements/read` answers managed requirements that pin plugins on,
 *   which `config/read` does not show (otherwise null, none configured);
 * - `version-fails` and `startup-fails`: `--version`, or `app-server` before it answers anything,
 *   prints FAKE_CODEX_SECRET to its error output, as a configuration error can print a key, and
 *   exits with 3 or 1.
 *
 * `config/read` answers the configuration in FAKE_CODEX_CONFIG (JSON, empty by default), and
 * `model/list` the catalog in FAKE_CODEX_CATALOG (a JSON array), one model a page. A thread that
 * asks for no model or provider runs on the configuration's, as Codex's do, or `gpt-5.5` from
 * `openai`. Each launch of `app-server` appends its arguments and CODEX_HOME to
 * FAKE_CODEX_LAUNCHES when it is set.
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
  if (flags.has('version-fails')) {
    process.stderr.write(
      `error: experimental_bearer_token = "${process.env.FAKE_CODEX_SECRET ?? ''}"\n`,
    );
    process.exit(3);
  }
  process.stdout.write(`codex-cli ${option('version', '0.157.0')}\n`);
  process.exit(0);
}
if (process.argv[2] !== 'app-server') process.exit(2);
if (process.env.FAKE_CODEX_LAUNCHES) {
  appendFileSync(
    process.env.FAKE_CODEX_LAUNCHES,
    `${JSON.stringify({ argv: process.argv.slice(2), codexHome: process.env.CODEX_HOME ?? null })}\n`,
  );
}
const configured = JSON.parse(process.env.FAKE_CODEX_CONFIG ?? '{}');
// The launch's `-c key=value` overrides apply above the configuration, as Codex applies them,
// unless `managed` plays a managed layer that outranks them and turns plugins back on.
for (let index = 3; index < process.argv.length - 1; index += 1) {
  if (process.argv[index] !== '-c') continue;
  const setting = process.argv[index + 1] ?? '';
  const equals = setting.indexOf('=');
  const parts = setting.slice(0, equals).split('.');
  let target = configured;
  for (const part of parts.slice(0, -1)) {
    target[part] ??= {};
    target = target[part];
  }
  target[parts.at(-1)] = JSON.parse(setting.slice(equals + 1));
}
if (flags.has('managed')) {
  configured.features = { ...configured.features, plugins: true };
}
if (flags.has('startup-fails')) {
  process.stderr.write(
    `ERROR config: experimental_bearer_token = "${process.env.FAKE_CODEX_SECRET ?? ''}"\n`,
  );
  process.exit(1);
}

const SANDBOX_TYPES = {
  'read-only': 'readOnly',
  'workspace-write': 'workspaceWrite',
  'danger-full-access': 'dangerFullAccess',
};
let counter = 0;
let serverRequests = 0;
let answersIgnored = 0;
/** Threads of server requests still waiting for an answer, by request id. */
const pendingRequests = new Map();
const send = (message) => process.stdout.write(`${JSON.stringify(message)}\n`);
const notify = (method, params) => send({ method, params, emittedAtMs: Date.now() });
const turn = (id, status) => ({ id, items: [], status, error: null });

createInterface({ input: process.stdin }).on('line', (line) => {
  if (process.env.FAKE_CODEX_LOG) appendFileSync(process.env.FAKE_CODEX_LOG, `${line}\n`);
  const message = JSON.parse(line);
  const { id, method, params } = message;
  if (method === undefined) {
    // An answer to a server request: Codex confirms that it took it.
    const threadId = pendingRequests.get(id);
    if (flags.has('deaf-once') && answersIgnored === 0) {
      answersIgnored += 1;
      return;
    }
    if (threadId !== undefined) {
      pendingRequests.delete(id);
      notify('serverRequest/resolved', { threadId, requestId: id });
    }
    return;
  }
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
        model: flags.has('other-model')
          ? 'gpt-5.5'
          : (params.model ?? configured.model ?? 'gpt-5.5'),
        modelProvider: flags.has('other-model')
          ? 'openai'
          : (params.modelProvider ?? configured.model_provider ?? 'openai'),
        cwd: flags.has('other-cwd') ? '/somewhere/else' : params.cwd,
        approvalPolicy: flags.has('never') ? 'never' : params.approvalPolicy,
        approvalsReviewer: params.approvalsReviewer,
        sandbox: { type: SANDBOX_TYPES[params.sandbox] },
      });
      return;
    case 'configRequirements/read':
      respond({
        requirements: flags.has('requirements') ? { featureRequirements: { plugins: true } } : null,
      });
      return;
    case 'config/read':
      respond({
        config: configured,
        origins: {},
        layers: null,
      });
      return;
    case 'model/list': {
      // Pages of one model, to exercise the adapter's paging.
      const catalog = JSON.parse(process.env.FAKE_CODEX_CATALOG ?? '[]');
      const index = params.cursor === null ? 0 : Number(params.cursor);
      respond({
        data: catalog.slice(index, index + 1),
        nextCursor: index + 1 < catalog.length ? String(index + 1) : null,
      });
      return;
    }
    case 'thread/turns/list':
      respond({ data: [], nextCursor: null, backwardsCursor: null });
      return;
    case 'turn/start': {
      counter += 1;
      const turnId = `turn-${process.pid}-${counter}`;
      respond({ turn: turn(turnId, 'inProgress') });
      notify('turn/started', { threadId: params.threadId, turn: turn(turnId, 'inProgress') });
      if (flags.has('ask') || flags.has('ask-secret')) {
        const requestId = serverRequests++;
        pendingRequests.set(requestId, params.threadId);
        send({
          id: requestId,
          method: 'item/tool/requestUserInput',
          params: {
            threadId: params.threadId,
            turnId,
            itemId: 'call-1',
            questions: [
              {
                id: 'colour',
                header: 'Colour',
                question: 'Which colour should the file mention?',
                isOther: true,
                isSecret: flags.has('ask-secret'),
                options: [
                  { label: 'red', description: 'The warm one' },
                  { label: 'blue', description: 'The calm one' },
                ],
              },
            ],
            isBlocking: false,
            autoResolutionMs: null,
          },
        });
      }
      if (flags.has('elicit')) {
        send({
          id: serverRequests++,
          method: 'mcpServer/elicitation/request',
          params: {
            threadId: params.threadId,
            turnId,
            serverName: 'asker',
            mode: 'form',
            message: 'Which file?',
            requestedSchema: { type: 'object', properties: {} },
          },
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
      // As Codex does, the interrupted turn's pending requests are resolved after it ends.
      for (const [requestId, threadId] of pendingRequests) {
        if (threadId !== params.threadId) continue;
        pendingRequests.delete(requestId);
        notify('serverRequest/resolved', { threadId, requestId });
      }
      return;
    default:
      refuse(`unknown method ${method}`);
  }
});
process.stdin.on('end', () => process.exit(0));
