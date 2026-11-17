import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { type ApprovalCommand, compileValidator, RuntimeDescriptor } from '@halcyonic/contracts';
import {
  capabilityProblems,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import {
  ACP_CAPABILITIES,
  AcpRuntimeAdapter,
  sweepSettingsHomes,
  UNCONFINED_REASON,
} from './acp-runtime.ts';
import { approvalRequest } from './approvals.ts';
import { readProcessIdentity, writeProcessRecords } from './process-record.ts';
import { type AcpProfile, ANTIGRAVITY, CURSOR, KIRO } from './profiles.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { assertValidObservations, TEST_EXECUTION } from './testing/observations.ts';

const FAKE = fileURLToPath(new URL('./testing/fake-acp-agent.mjs', import.meta.url));
const FIXTURES = fileURLToPath(new URL('../fixtures/', import.meta.url));

interface Run {
  readonly adapter: AcpRuntimeAdapter;
  readonly folder: string;
  readonly observations: RuntimeObservation[];
  readonly emit: (observation: RuntimeObservation) => void;
  /** Every message the adapter sent the app, in order. */
  sent(): Record<string, unknown>[];
  /** The names of the variables the app was launched with. */
  variables(): string[];
  /** The arguments the app was launched with, and the settings home Kiro was given. */
  args(): string[];
  kiroHome(): string;
  readonly root: string;
  /** Resolves with the first observation of `type` (after `after` of them), waiting for it. */
  next(type: RuntimeObservation['type'], after?: number): Promise<RuntimeObservation>;
}

function temporary(t: TestContext): string {
  const directory = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-acp-unit-')));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  return directory;
}

/**
 * An adapter whose app is the fake replaying `fixture`, recorded from the real app. `fixtureText`
 * replaces the file's text where a test cuts a recorded trace.
 */
function setUp(
  t: TestContext,
  fixture: string,
  options: {
    profile?: AcpProfile;
    version?: string;
    fixtureText?: string;
    versionLine?: string;
    sessionOptions?: Record<string, string>;
    processRecordFile?: string;
    env?: Record<string, string>;
  } = {},
): Run {
  const root = temporary(t);
  const folder = join(root, 'project');
  const log = join(root, 'sent.jsonl');
  const variables = join(root, 'variables.txt');
  let replay = join(FIXTURES, fixture);
  if (options.fixtureText !== undefined) {
    replay = join(root, 'cut.jsonl');
    writeFileSync(replay, options.fixtureText);
  }
  const binary = join(root, 'app');
  writeFileSync(
    binary,
    `#!/bin/sh\nif [ "$1" = "--version" ]; then echo '${options.versionLine ?? 'kiro-cli 3.4.0'}'; exit 0; fi\nif [ "$1" = "chat" ]; then printf '%s\\n' "$@" > '${join(root, 'list-args.txt')}'; cat '${join(FIXTURES, 'kiro-list-models.json')}'; exit 0; fi\nenv | cut -d= -f1 > '${variables}'\nprintf '%s\\n' "$@" > '${join(root, 'args.txt')}'\nprintf '%s' "$KIRO_HOME" > '${join(root, 'kiro-home.txt')}'\nexec '${process.execPath}' '${FAKE}' '${replay}'\n`,
  );
  chmodSync(binary, 0o755);
  writeFileSync(log, '');
  mkdirSync(folder);
  const adapter = new AcpRuntimeAdapter({
    profile: options.profile ?? ANTIGRAVITY,
    binaryPath: binary,
    directoryPolicy: allowOnly(root),
    env: {
      PATH: process.env.PATH ?? '/usr/bin:/bin',
      FAKE_ACP_LOG: log,
      ...(options.version === undefined ? {} : { FAKE_ACP_VERSION: options.version }),
      ...options.env,
    },
    home: root,
    processRecordFile: options.processRecordFile ?? null,
    ...(options.sessionOptions === undefined ? {} : { sessionOptions: options.sessionOptions }),
    hostFolders: null,
  });
  t.after(() => adapter.close());
  const observations: RuntimeObservation[] = [];
  const waiters: (() => void)[] = [];
  const emit = (observation: RuntimeObservation) => {
    observations.push(observation);
    for (const wake of waiters.splice(0)) wake();
  };
  return {
    adapter,
    folder,
    observations,
    emit,
    sent: () =>
      readFileSync(log, 'utf8')
        .split('\n')
        .filter((line) => line !== '')
        .map((line) => JSON.parse(line)),
    variables: () =>
      readFileSync(variables, 'utf8')
        .split('\n')
        .filter((line) => line !== ''),
    args: () =>
      readFileSync(join(root, 'args.txt'), 'utf8')
        .split('\n')
        .filter((line) => line !== ''),
    kiroHome: () => readFileSync(join(root, 'kiro-home.txt'), 'utf8'),
    root,
    async next(type, after = 0) {
      for (let waited = 0; waited < 200; waited++) {
        const found = observations.filter((observation) => observation.type === type)[after];
        if (found !== undefined) return found;
        await Promise.race([new Promise<void>((resolve) => waiters.push(resolve)), delay(50)]);
      }
      assert.fail(`no ${type} after ${after}: ${observations.map((o) => o.type).join(', ')}`);
    },
  };
}

function start(run: Run, instruction = 'Make the file.', modelRef: string | null = null) {
  return run.adapter.startExecution({
    execution: TEST_EXECUTION,
    instruction,
    options: {},
    model_ref: modelRef,
    directory: run.folder,
    emit: run.emit,
  });
}

function types(run: Run): string[] {
  return run.observations.map((observation) => observation.type);
}

describe('ACP adapter descriptor', () => {
  test('declares only what a runtime test proved, consistently with its methods', () => {
    for (const profile of [ANTIGRAVITY, KIRO, CURSOR]) {
      const adapter = new AcpRuntimeAdapter({
        profile,
        binaryPath: '/nonexistent',
        directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
        env: {},
        home: '/nonexistent-home',
        processRecordFile: null,
        hostFolders: null,
      });
      assert.deepEqual(capabilityProblems(adapter), []);
      assert.ok(compileValidator(RuntimeDescriptor)(adapter.descriptor));
      assert.deepEqual(adapter.descriptor.capabilities, ACP_CAPABILITIES);
      assert.equal(adapter.descriptor.capabilities.instruct_while_running, false);
      assert.equal(adapter.descriptor.capabilities.answer_question, false);
      // A model choice only from a list the app was seen to give: each of the three was seen signed in.
      assert.equal(adapter.descriptor.model_choice, 'listed');
      assert.equal(typeof adapter.listModels, 'function');
      assert.equal(adapter.descriptor.offers_never_ask, undefined);
      assert.equal(adapter.descriptor.reports_tool_activity, false);
      assert.equal(adapter.runsOutsideSandbox(), true);
    }
  });

  test("refuses start options and never asking, and a model where the app's list was never seen", () => {
    const adapter = new AcpRuntimeAdapter({
      profile: ANTIGRAVITY,
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: '/nonexistent-home',
      processRecordFile: null,
      hostFolders: null,
    });
    assert.equal(adapter.validateStartOptions({}, null).ok, true);
    assert.equal(adapter.validateStartOptions({ model: 'x' }, null).ok, false);
    // A model from its list is taken; the app itself is asked to use it at the start.
    assert.equal(adapter.validateStartOptions({}, 'gemini-4-carbon').ok, true);
    assert.equal(adapter.validateStartOptions({}, null, 'never_ask').ok, false);
    // A profile whose app's list was never seen offers none.
    const unlisted = new AcpRuntimeAdapter({
      profile: { ...CURSOR, models: null },
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: '/nonexistent-home',
      processRecordFile: null,
      hostFolders: null,
    });
    assert.equal(unlisted.validateStartOptions({}, 'x').ok, false);
    assert.equal(unlisted.descriptor.model_choice, 'none');
    assert.equal(unlisted.listModels, undefined);
  });

  test("held to a task's own copy only where the host names its folders and its copies (ADR 0045)", () => {
    const made = (extra: Partial<ConstructorParameters<typeof AcpRuntimeAdapter>[0]>) =>
      new AcpRuntimeAdapter({
        profile: KIRO,
        binaryPath: '/nonexistent',
        directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
        env: {},
        home: '/nonexistent-home',
        processRecordFile: null,
        hostFolders: null,
        ...extra,
      });
    const hostFolders = {
      scope: {
        folders: ['/nonexistent-host/data', '/nonexistent-host/tasks'],
        home: '/nonexistent-home',
      },
      profileFolder: '/nonexistent-host/data/acp-profiles',
    };
    const copy = '/nonexistent-host/tasks/p/a/one';
    const copyOriginOf = (directory: string) =>
      directory === copy ? '/nonexistent-work/one' : null;
    // Off macOS (no host folders), or with no copies: never held, so a project takes one task at a time.
    assert.equal(made({}).holdsFolder('/nonexistent-work/one'), false);
    assert.equal(made({ copyOriginOf }).holdsFolder(copy), false);
    assert.equal(made({ hostFolders }).holdsFolder('/nonexistent-work/one'), false);
    const held = made({ hostFolders, copyOriginOf });
    // The person's folder, asked before its copy is made, and the recorded copy itself.
    assert.equal(held.holdsFolder('/nonexistent-work/one'), true);
    assert.equal(held.holdsFolder(copy), true);
    // A folder of the host's that is no recorded copy: never.
    assert.equal(held.holdsFolder('/nonexistent-host/tasks/p/b/one'), false);
    assert.equal(held.holdsFolder('/nonexistent-host/data'), false);
    assert.equal(held.holdsFolder('/nonexistent-home/.halcyonic/x'), false);
  });

  test('names the app’s own sign-in and endpoint variables as its own', () => {
    const adapter = new AcpRuntimeAdapter({
      profile: ANTIGRAVITY,
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: '/nonexistent-home',
      processRecordFile: null,
      hostFolders: null,
    });
    assert.equal(adapter.ownsEnvironmentName('GEMINI_API_KEY'), true);
    assert.equal(adapter.ownsEnvironmentName('GOOGLE_GEMINI_BASE_URL'), true);
    assert.equal(adapter.ownsEnvironmentName('DATABASE_URL'), false);
  });
});

describe('ACP adapter on Antigravity 1.5.10 traces', () => {
  test('an approved command: asked whole, answered once, run, the turn completed', async (t) => {
    const run = setUp(t, 'antigravity-command-allow.jsonl', {
      env: { GEMINI_API_KEY: 'from-the-host' },
    });
    const started = await start(run);
    assert.equal(started.native_id, '2d0f9c23-ed68-4e08-83b9-17bc23854404');
    const asked = await run.next('runtime.approval.requested');
    assert.equal(asked.type, 'runtime.approval.requested');
    if (asked.type !== 'runtime.approval.requested') return;
    // The command in its parts, from the call's own input, and the summary from those parts alone (AUDIT3 CP1, CP2).
    assert.deepEqual(asked.payload.subject, {
      kind: 'tool_use',
      tool_name: 'run_command',
      summary: "[in the task's folder] echo halcyonic-probe > made.txt",
      command: {
        text: 'echo halcyonic-probe > made.txt',
        runs_in: { kind: 'task_folder' },
        background: false,
        input_to_running: false,
      },
    });
    assert.equal(asked.payload.complete, true);
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: asked.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    await run.next('runtime.turn.completed');
    assert.deepEqual(types(run), [
      'runtime.execution.started',
      'runtime.turn.started',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.usage.reported',
      'runtime.turn.completed',
    ]);
    const first = run.observations[0];
    assert.deepEqual(first?.payload, {
      native_id: '2d0f9c23-ed68-4e08-83b9-17bc23854404',
      confined: false,
      unconfined_reason: UNCONFINED_REASON,
    });
    const resolved = run.observations[4];
    assert.deepEqual(resolved?.payload, {
      approval_id: asked.payload.approval_id,
      decision: 'approved',
    });
    assert.deepEqual(run.observations[5]?.payload, {
      tool_call_id: '86208734-15d5-4c88-b65a-09d3f4bb0db8',
      turn_id: (run.observations[1]?.payload as { turn_id: string } | undefined)?.turn_id,
      outcome: 'succeeded',
    });
    assert.deepEqual(run.observations[6]?.payload, { text: 'Done.' });
    assert.equal(run.observations[6]?.provenance.epistemic, 'reported');
    assertValidObservations(run.observations);
    const answer = run.sent().find((message) => message.id === '1' && message.method === undefined);
    assert.deepEqual(answer?.result, { outcome: { outcome: 'selected', optionId: 'allow' } });
    // The app signs in only its own way: no key from the host reaches it.
    assert.equal(run.variables().includes('GEMINI_API_KEY'), false);
    assert.equal(run.variables().includes('AGY_CLI_DISABLE_AUTO_UPDATE'), true);
    // A settings home of Halora's own, so the person's own settings for the app never apply, fresh for this launch
    // and removed at its end, so nothing written there reaches a later one.
    const launched = run.args();
    assert.deepEqual(launched.slice(0, 3), ['acp', '--disable-telemetry', '--gemini-dir']);
    const own = launched[3] ?? '';
    assert.equal(dirname(own), join(run.root, '.gemini', 'halora'));
    assert.match(basename(own), new RegExp(`^${process.pid}-[0-9a-f-]{36}$`));
    await run.adapter.close();
    assert.equal(existsSync(own), false);
    const created = run.sent().find((message) => message.method === 'session/new');
    assert.deepEqual(created?.params, { cwd: run.folder, mcpServers: [] });
  });

  test('a denied command: refused once, settled failed, the turn completed', async (t) => {
    const run = setUp(t, 'antigravity-command-deny.jsonl');
    await start(run);
    const asked = await run.next('runtime.approval.requested');
    if (asked.type !== 'runtime.approval.requested') return;
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: asked.payload.approval_id,
      decision: 'deny',
      message: 'Not now.',
    });
    await run.next('runtime.turn.completed');
    const resolved = run.observations.find((o) => o.type === 'runtime.approval.resolved');
    assert.deepEqual(resolved?.payload, {
      approval_id: asked.payload.approval_id,
      decision: 'denied',
    });
    const ended = run.observations.find((o) => o.type === 'runtime.tool.completed');
    assert.equal((ended?.payload as { outcome: string } | undefined)?.outcome, 'failed');
    const answer = run.sent().find((message) => message.id === '1' && message.method === undefined);
    assert.deepEqual(answer?.result, { outcome: { outcome: 'selected', optionId: 'deny' } });
    assertValidObservations(run.observations);
  });

  test('a denial counts as taken only once the app settles the call failed, not when it moves on', async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'antigravity-command-deny.jsonl'), 'utf8').split(
      '\n',
    );
    const answered = recorded.findIndex((line) => line.includes('"expectResponse"'));
    // Antigravity reports the refused call in progress before it settles it failed: cut the trace between the two.
    const cut = [...recorded.slice(0, answered + 2), '{"exit": 1}', ''].join('\n');
    assert.ok(cut.includes('"in_progress"') && !recorded[answered + 1]?.includes('"failed"'));
    const run = setUp(t, 'cut', { fixtureText: cut });
    await start(run);
    const asked = await run.next('runtime.approval.requested');
    if (asked.type !== 'runtime.approval.requested') return;
    await assert.rejects(
      run.adapter.respondToApproval({
        execution: TEST_EXECUTION,
        approval_id: asked.payload.approval_id,
        decision: 'deny',
        message: null,
      }),
      (error: unknown) => error instanceof RuntimeActionError && error.effect === 'unknown',
    );
    assert.equal(types(run).includes('runtime.approval.resolved'), false);
  });

  test('an approval counts as taken when the call fails in running, with no in progress before it', async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'antigravity-command-deny.jsonl'), 'utf8').split(
      '\n',
    );
    const answered = recorded.findIndex((line) => line.includes('"expectResponse"'));
    // The recorded refusal, with its in-progress report taken out: the call goes from asked straight to failed.
    assert.ok(recorded[answered + 1]?.includes('"in_progress"'));
    const cut = [...recorded.slice(0, answered + 1), ...recorded.slice(answered + 2)].join('\n');
    const run = setUp(t, 'cut', { fixtureText: cut });
    await start(run);
    const asked = await run.next('runtime.approval.requested');
    if (asked.type !== 'runtime.approval.requested') return;
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: asked.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    const resolved = run.observations.find((o) => o.type === 'runtime.approval.resolved');
    assert.deepEqual(resolved?.payload, {
      approval_id: asked.payload.approval_id,
      decision: 'approved',
    });
    await run.next('runtime.turn.completed');
  });

  test('an ask whose input changes before the person answers is refused, recorded why, and their answer finds it gone', async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'antigravity-command-deny.jsonl'), 'utf8').split(
      '\n',
    );
    const asking = recorded.findIndex((line) => line.includes('session/request_permission'));
    const request = JSON.parse(recorded[asking] ?? '{}');
    const call = request.send.params.toolCall;
    // The same call, reported with another command after it was asked about (AUDIT3 KW1).
    const changed = JSON.stringify({
      send: {
        jsonrpc: '2.0',
        method: 'session/update',
        params: {
          sessionId: request.send.params.sessionId,
          update: {
            ...call,
            sessionUpdate: 'tool_call_update',
            rawInput: { ...call.rawInput, CommandLine: 'rm -rf ./*' },
          },
        },
      },
    });
    const run = setUp(t, 'cut', {
      fixtureText: [...recorded.slice(0, asking + 1), changed, ...recorded.slice(asking + 1)].join(
        '\n',
      ),
    });
    await start(run);
    const asked = await run.next('runtime.approval.requested');
    if (asked.type !== 'runtime.approval.requested') return;
    const resolved = await run.next('runtime.approval.resolved');
    assert.deepEqual(resolved.payload, {
      approval_id: asked.payload.approval_id,
      decision: 'denied',
    });
    assert.match(resolved.provenance.native_type ?? '', /^withdrawn\.input_changed\./);
    const answer = run
      .sent()
      .find((message) => message.method === undefined && 'result' in message);
    assert.deepEqual((answer?.result as { outcome: unknown } | undefined)?.outcome, {
      outcome: 'selected',
      optionId: 'deny',
    });
    await assert.rejects(
      run.adapter.respondToApproval({
        execution: TEST_EXECUTION,
        approval_id: asked.payload.approval_id,
        decision: 'approve',
        message: null,
      }),
      (error: unknown) => error instanceof RuntimeActionError,
    );
    await run.next('runtime.turn.completed');
  });

  test('Kiro: an ask whose command alone changes before the person answers is refused, recorded why (AUDIT3 CP4)', async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'kiro-signed-in-deny.jsonl'), 'utf8').split('\n');
    const target = 'echo "hello" > hello.txt';
    const callOf = (line: string) => {
      const update = JSON.parse(line || '{}').send?.params?.update;
      return update?.rawInput?.command === target ? update : null;
    };
    const call = recorded.map(callOf).find((update) => update !== null);
    assert.ok(call !== undefined, 'the recorded call');
    const asking = recorded.findIndex(
      (line) =>
        line.includes('session/request_permission') &&
        JSON.parse(line).send?.params?.toolCall?.toolCallId === call.toolCallId,
    );
    const sessionId = JSON.parse(recorded[asking] ?? '{}').send.params.sessionId;
    // The same call, reported again with only its command changed after it was asked about.
    const changed = JSON.stringify({
      send: {
        jsonrpc: '2.0',
        method: 'session/update',
        params: {
          sessionId,
          update: {
            toolCallId: call.toolCallId,
            sessionUpdate: 'tool_call_update',
            rawInput: { ...call.rawInput, command: 'rm -rf ./*' },
          },
        },
      },
    });
    const run = setUp(t, 'cut', {
      profile: KIRO,
      fixtureText: [...recorded.slice(0, asking + 1), changed, ...recorded.slice(asking + 1)].join(
        '\n',
      ),
    });
    await start(run);
    let withdrawn: string | null = null;
    for (let index = 0; index < 6; index++) {
      const request = await run.next('runtime.approval.requested', index);
      if (request.type !== 'runtime.approval.requested') return;
      if (request.payload.subject.command?.text === target) {
        withdrawn = request.payload.approval_id;
        continue;
      }
      await run.adapter.respondToApproval({
        execution: TEST_EXECUTION,
        approval_id: request.payload.approval_id,
        decision: 'deny',
        message: null,
      });
    }
    await run.next('runtime.turn.completed');
    assert.ok(withdrawn !== null, 'the command was asked about');
    const resolved = run.observations.find(
      (o) => o.type === 'runtime.approval.resolved' && o.payload.approval_id === withdrawn,
    );
    assert.ok(resolved !== undefined && resolved.type === 'runtime.approval.resolved');
    assert.equal(resolved.payload.decision, 'denied');
    assert.match(resolved.provenance.native_type ?? '', /^withdrawn\.input_changed\./);
  });

  test('stopping during an approval cancels the turn and withdraws the request', async (t) => {
    const run = setUp(t, 'antigravity-command-cancel.jsonl');
    await start(run);
    await run.next('runtime.approval.requested');
    await run.adapter.interrupt({ execution: TEST_EXECUTION });
    assert.equal(types(run).at(-1), 'runtime.turn.interrupted');
    assert.equal(types(run).includes('runtime.turn.completed'), false);
    const sent = run.sent();
    const cancel = sent.findIndex((message) => message.method === 'session/cancel');
    const withdrawn = sent.findIndex(
      (message) => message.id === '1' && message.method === undefined,
    );
    assert.ok(cancel >= 0 && withdrawn > cancel);
    assert.deepEqual(sent[withdrawn]?.result, { outcome: { outcome: 'cancelled' } });
    assertValidObservations(run.observations);
  });

  test('a second instruction at rest starts a second turn; one while a turn runs is refused', async (t) => {
    const run = setUp(t, 'antigravity-two.jsonl');
    await start(run);
    await assert.rejects(
      run.adapter.sendInstruction({ execution: TEST_EXECUTION, text: 'Too soon.' }),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'turn_running' &&
        error.effect === 'none',
    );
    await run.next('runtime.turn.completed');
    await run.adapter.sendInstruction({ execution: TEST_EXECUTION, text: 'And again.' });
    await run.next('runtime.turn.completed', 1);
    const messages = run.observations
      .filter((o) => o.type === 'runtime.agent_message')
      .map((o) => o.payload);
    assert.deepEqual(messages, [{ text: 'First answer.' }, { text: 'Second answer.' }]);
    const turns = run.observations.filter((o) => o.type === 'runtime.turn.started');
    assert.equal(turns.length, 2);
    assertValidObservations(run.observations);
  });

  test('a new file and an edit carry their changes whole, the edit at every occurrence', async (t) => {
    const run = setUp(t, 'antigravity-write-allow.jsonl');
    await start(run);
    const write = await run.next('runtime.approval.requested');
    if (write.type !== 'runtime.approval.requested') return;
    assert.equal(write.payload.complete, true);
    assert.deepEqual(write.payload.subject.changes, [
      {
        path: join(run.folder, 'notes.md'),
        real_path: null,
        to_path: null,
        kind: 'add',
        shape: 'content',
        before: null,
        after: '# Notes\nfrom the stand-in\n',
        every: false,
        cell: null,
      },
    ]);
    assert.deepEqual(write.payload.subject.paths, [
      { path: join(run.folder, 'notes.md'), access: 'write' },
    ]);
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: write.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    const edit = await run.next('runtime.approval.requested', 1);
    if (edit.type !== 'runtime.approval.requested') return;
    assert.equal(edit.payload.complete, true);
    assert.deepEqual(edit.payload.subject.changes?.[0], {
      path: join(run.folder, 'notes.md'),
      real_path: null,
      to_path: null,
      kind: 'update',
      shape: 'replace',
      before: 'from the stand-in',
      after: 'edited by the stand-in',
      every: true,
      cell: null,
    });
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: edit.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    await run.next('runtime.turn.completed');
    assertValidObservations(run.observations);
  });

  test('an app that stops mid-turn is reported lost, and its approval can no longer be answered', async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'antigravity-command-allow.jsonl'), 'utf8').split(
      '\n',
    );
    const asking = recorded.findIndex((line) => line.includes('session/request_permission'));
    const run = setUp(t, 'cut', {
      fixtureText: [...recorded.slice(0, asking + 1), '{"exit": 1}', ''].join('\n'),
    });
    await start(run);
    const asked = await run.next('runtime.approval.requested');
    await run.next('runtime.connection.lost');
    if (asked.type !== 'runtime.approval.requested') return;
    await assert.rejects(
      run.adapter.respondToApproval({
        execution: TEST_EXECUTION,
        approval_id: asked.payload.approval_id,
        decision: 'approve',
        message: null,
      }),
      (error: unknown) =>
        error instanceof RuntimeActionError && error.code === 'execution_not_found',
    );
    assertValidObservations(run.observations);
  });

  test('the same version on another protocol is refused, saying to reinstall it', async (t) => {
    const run = setUp(t, 'antigravity-two.jsonl', { env: { FAKE_ACP_PROTOCOL: '2' } });
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'runtime_version_unsupported' &&
        error.effect === 'none' &&
        error.message ===
          'Antigravity 1.5.10 on this computer answers in a way Halora does not know, so it could not start. Reinstall Antigravity 1.5.10, then start again.',
    );
    assert.deepEqual(types(run), []);
  });

  test('a copy that names no version is refused in words', async (t) => {
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', { profile: KIRO, versionLine: 'kiro-cli' });
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.message ===
          'Halora works with Kiro 3.4.0 only, and the copy on this computer does not say its version. Install Kiro 3.4.0, then start again.',
    );
  });

  test('another version of the app is refused before a session opens', async (t) => {
    const run = setUp(t, 'antigravity-two.jsonl', { version: '1.6.0' });
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'runtime_version_unsupported' &&
        error.effect === 'none',
    );
    assert.deepEqual(types(run), []);
    assert.equal(
      run.sent().some((message) => message.method === 'session/new'),
      false,
    );
  });
});

describe('ACP adapter: the mode that asks', () => {
  /** The recorded two-turn trace, its session started in `mode` with only `available` offered. */
  function startedIn(mode: string, available: readonly string[]): string {
    return readFileSync(join(FIXTURES, 'antigravity-two.jsonl'), 'utf8')
      .split('\n')
      .map((line) => {
        if (!line.includes('"answer": "session/new"')) return line;
        const step = JSON.parse(line);
        step.result.modes = {
          currentModeId: mode,
          availableModes: available.map((id) => ({ id, name: id })),
        };
        return [
          JSON.stringify(step),
          '{"expect": "session/set_mode"}',
          '{"answer": "session/set_mode", "result": {}}',
        ].join('\n');
      })
      .join('\n');
  }

  test('a session the app starts in a mode that allows by itself is switched to the asking mode first', async (t) => {
    const run = setUp(t, 'cut', {
      fixtureText: startedIn('auto', ['default', 'accept-edits', 'auto']),
    });
    await start(run);
    await run.next('runtime.turn.completed');
    const sent = run.sent().map((message) => message.method);
    assert.deepEqual(sent.slice(0, 4), [
      'initialize',
      'session/new',
      'session/set_mode',
      'session/prompt',
    ]);
    const switched = run.sent().find((message) => message.method === 'session/set_mode');
    assert.deepEqual(switched?.params, {
      sessionId: '11579b02-6764-4e01-932b-629ccfeb8eef',
      modeId: 'default',
    });
  });

  test("a run's own session options are set before its first prompt", async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'antigravity-two.jsonl'), 'utf8')
      .split('\n')
      .flatMap((line) =>
        line.includes('"answer": "session/new"')
          ? [
              line,
              '{"expect": "session/set_config_option"}',
              '{"answer": "session/set_config_option", "result": {}}',
            ]
          : [line],
      )
      .join('\n');
    const run = setUp(t, 'cut', {
      fixtureText: recorded,
      sessionOptions: { model: 'gemini-3.8-flash' },
    });
    await start(run);
    await run.next('runtime.turn.completed');
    const sent = run.sent().map((message) => message.method);
    assert.deepEqual(sent.slice(0, 4), [
      'initialize',
      'session/new',
      'session/set_config_option',
      'session/prompt',
    ]);
    const set = run.sent().find((message) => message.method === 'session/set_config_option');
    assert.deepEqual(set?.params, {
      sessionId: '11579b02-6764-4e01-932b-629ccfeb8eef',
      configId: 'model',
      value: 'gemini-3.8-flash',
    });
  });

  test('the app leaving the asking mode during a turn stops the turn in words, and the mode is set back first', async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'antigravity-two.jsonl'), 'utf8').split('\n');
    const firstPrompt = recorded.findIndex((line) => line.includes('"expect": "session/prompt"'));
    const sessionId = '11579b02-6764-4e01-932b-629ccfeb8eef';
    const said = (mode: string) =>
      JSON.stringify({
        send: {
          jsonrpc: '2.0',
          method: 'session/update',
          params: {
            sessionId,
            update: { sessionUpdate: 'current_mode_update', currentModeId: mode },
          },
        },
      });
    // The recorded session, with the app announcing `auto` in its first turn; it answers the cancel as ACP says.
    const cut = [
      ...recorded.slice(0, firstPrompt + 1),
      said('auto'),
      '{"expectNotification": "session/cancel"}',
      '{"answer": "session/prompt", "result": {"stopReason": "cancelled"}}',
      '{"expect": "session/set_mode"}',
      said('default'),
      '{"answer": "session/set_mode", "result": {}}',
      ...recorded.slice(
        recorded.findIndex(
          (line, index) => index > firstPrompt && line.includes('"expect": "session/prompt"'),
        ),
      ),
    ].join('\n');
    const run = setUp(t, 'cut', { fixtureText: cut });
    await start(run);
    const failed = await run.next('runtime.turn.failed');
    assert.deepEqual((failed.payload as { error: unknown }).error, {
      code: 'mode_left',
      message:
        'Antigravity 1.5.10 switched to a mode where it does not ask you first, so Halora stopped the turn.',
    });
    await run.adapter.sendInstruction({ execution: TEST_EXECUTION, text: 'And again.' });
    await run.next('runtime.turn.completed');
    const sent = run.sent().map((message) => message.method);
    assert.deepEqual(sent.slice(3), ['session/cancel', 'session/set_mode', 'session/prompt']);
    assertValidObservations(run.observations);
  });

  test('a session that cannot be put in the asking mode is not started', async (t) => {
    const run = setUp(t, 'cut', { fixtureText: startedIn('auto', ['auto', 'accept-edits']) });
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'mode_unavailable' &&
        error.effect === 'none',
    );
    assert.deepEqual(types(run), []);
    assert.equal(
      run.sent().some((message) => message.method === 'session/prompt'),
      false,
    );
  });
});

describe('ACP adapter on Cursor 2026.11.13 traces, signed in', () => {
  test('a denied command: asked whole, denied, taken when Cursor ends the call completed, and never said to succeed', async (t) => {
    const run = setUp(t, 'cursor-signed-in-deny.jsonl', {
      profile: CURSOR,
      versionLine: '2026.11.13-eacade1',
    });
    await start(
      run,
      'Run exactly this one shell command in the current folder, and nothing else: echo hello > hello.txt',
      'gpt-5.4-nano[reasoning=medium]',
    );
    const asked = await run.next('runtime.approval.requested');
    if (asked.type !== 'runtime.approval.requested') return;
    assert.equal(asked.payload.subject.command?.text, 'echo hello > hello.txt');
    assert.equal(asked.payload.complete, true);
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: asked.payload.approval_id,
      decision: 'deny',
      message: null,
    });
    const resolved = await run.next('runtime.approval.resolved');
    assert.deepEqual(resolved.payload, {
      approval_id: asked.payload.approval_id,
      decision: 'denied',
    });
    await run.next('runtime.turn.completed');
    const ended = run.observations.find((o) => o.type === 'runtime.tool.completed');
    assert.equal((ended?.payload as { outcome?: string } | undefined)?.outcome, 'failed');
    const answer = run
      .sent()
      .find((message) => message.method === undefined && 'result' in message);
    assert.deepEqual((answer?.result as { outcome: unknown } | undefined)?.outcome, {
      outcome: 'selected',
      optionId: 'reject-once',
    });
    assertValidObservations(run.observations);
  });
});

describe("what a turn used, from the app's own report (2026-11-17)", () => {
  test("Antigravity's answer to the prompt gives the turn's tokens: reported, as it counts them, never money", async (t) => {
    const run = setUp(t, 'antigravity-model.jsonl');
    assert.equal(run.adapter.descriptor.reports_usage, true);
    await start(run, 'Make the file.', 'gemini-4-carbon');
    await run.next('runtime.turn.completed');
    const used = run.observations.filter((o) => o.type === 'runtime.usage.reported');
    assert.equal(used.length, 1);
    const turn = run.observations.find((o) => o.type === 'runtime.turn.started');
    assert.ok(turn !== undefined && turn.type === 'runtime.turn.started');
    assert.deepEqual(used[0]?.payload, {
      turn_id: turn.payload.turn_id,
      tokens: { input: 100, cache_read: 0, cache_write: 0, output: 10, reasoning: 0 },
      cost: null,
      counted: 'given',
    });
    assert.equal(used[0]?.provenance.epistemic, 'reported');
    assertValidObservations(run.observations);
  });

  test('Kiro, whose credits the contract does not carry, and Cursor, which reports nothing, report no usage', async (t) => {
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', { profile: KIRO });
    assert.equal(run.adapter.descriptor.reports_usage, undefined);
    await start(run);
    for (let index = 0; index < 6; index++) {
      const asked = await run.next('runtime.approval.requested', index);
      if (asked.type !== 'runtime.approval.requested') return;
      await run.adapter.respondToApproval({
        execution: TEST_EXECUTION,
        approval_id: asked.payload.approval_id,
        decision: 'deny',
        message: null,
      });
    }
    await run.next('runtime.turn.completed');
    assert.ok(!run.observations.some((o) => o.type === 'runtime.usage.reported'));
    const cursor = new AcpRuntimeAdapter({
      profile: CURSOR,
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: '/nonexistent-home',
      processRecordFile: null,
      hostFolders: null,
    });
    assert.equal(cursor.descriptor.reports_usage, undefined);
  });
});

describe("each app's own models (2026-11-17)", () => {
  test('Antigravity lists its models from its session answer, the one it has now first; nothing is prompted', async (t) => {
    const run = setUp(t, 'antigravity-model.jsonl');
    const listed = (await run.adapter.listModels?.()) ?? [];
    // Its `model` option's choices, as the run recorded them on the stand-in model.
    assert.deepEqual(
      listed.map((model) => [model.model_ref, model.display_name]),
      [
        ['gemini-4-carbon', 'Gemini 4 Carbon (Antigravity)'],
        ['gemini-4-hydrogen', 'Gemini 4 Hydrogen (Antigravity)'],
      ],
    );
    for (const model of listed) {
      assert.match(model.display_name, / \(Antigravity\)$/);
      assert.equal(model.served, 'remote');
      assert.equal(model.tool_calling, 'unknown');
      assert.equal(model.context_tokens, null);
    }
    assert.ok(!run.sent().some((message) => message.method === 'session/prompt'));
    assert.deepEqual(run.observations, []);
  });

  test('a start with a model from the list sets it and goes on once Antigravity reads it back', async (t) => {
    const run = setUp(t, 'antigravity-model.jsonl');
    await start(run, 'Make the file.', 'gemini-4-carbon');
    await run.next('runtime.turn.completed');
    const set = run.sent().find((message) => message.method === 'session/set_config_option');
    assert.deepEqual(set?.params, {
      sessionId: '490946d2-fcef-4254-a12f-3319a5d52d7b',
      configId: 'model',
      value: 'gemini-4-carbon',
    });
  });

  test('a model the app does not read back refuses the start in words, and nothing runs', async (t) => {
    const run = setUp(t, 'antigravity-model.jsonl');
    await assert.rejects(
      start(run, 'Make the file.', 'gemini-0-imagined'),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'model_unavailable' &&
        /would not use the model you chose/.test(error.message),
    );
    assert.ok(!run.sent().some((message) => message.method === 'session/prompt'));
  });

  test("Kiro lists its models from its own listing command's JSON, with each one's context", async (t) => {
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', { profile: KIRO });
    const listed = (await run.adapter.listModels?.()) ?? [];
    assert.equal(listed.length, 23);
    assert.deepEqual(listed[0], {
      model_ref: 'auto',
      display_name: 'auto (Kiro)',
      served: 'remote',
      tool_calling: 'unknown',
      context_tokens: 1000000,
    });
    assert.ok(listed.some((model) => model.model_ref === 'qwen3-coder-next'));
    assert.deepEqual(
      readFileSync(join(run.folder, '..', 'list-args.txt'), 'utf8')
        .trim()
        .split('\n'),
      ['chat', '--list-models', '--format', 'json'],
    );
    // No session, so nothing was sent over ACP, and the listing's settings home is gone.
    assert.deepEqual(run.sent(), []);
    assert.deepEqual(readdirSync(join(run.folder, '..', '.kiro', 'halora')), []);
  });

  test('Cursor lists its models from its session answer, Auto first, as its signed-in run recorded them', async (t) => {
    const run = setUp(t, 'cursor-signed-in-deny.jsonl', {
      profile: CURSOR,
      versionLine: '2026.11.13-eacade1',
    });
    const listed = (await run.adapter.listModels?.()) ?? [];
    // The recorded list, shortened to two in the fixture (48 on the run).
    assert.equal(listed.length, 2);
    assert.deepEqual(listed[0], {
      model_ref: 'default[]',
      display_name: 'Auto (Cursor)',
      served: 'remote',
      tool_calling: 'unknown',
      context_tokens: null,
    });
  });
});

describe('a chosen model before the held options (AUDIT3 MC1)', () => {
  /** Kiro's signed-in trace with a model set answered as Kiro answered it (19:39Z), before the held options' sets. */
  function withModel(change: (answer: string) => string = (answer) => answer): string {
    const lines = readFileSync(join(FIXTURES, 'kiro-signed-in-deny.jsonl'), 'utf8').split('\n');
    const first = lines.findIndex((line) => line.includes('"expect": "session/set_config_option"'));
    const answer = readFileSync(join(FIXTURES, 'kiro-model-set.json'), 'utf8').trim();
    const steps = [
      JSON.stringify({ expect: 'session/set_config_option' }),
      JSON.stringify({ answer: 'session/set_config_option', result: JSON.parse(change(answer)) }),
    ];
    return [...lines.slice(0, first), ...steps, ...lines.slice(first)].join('\n');
  }

  test('the model is set first, then autopilot and content collection, each read back last', async (t) => {
    const run = setUp(t, 'cut', { profile: KIRO, fixtureText: withModel() });
    await start(run, 'Make the file.', 'claude-haiku-5.5');
    const sets = run
      .sent()
      .filter((message) => message.method === 'session/set_config_option')
      .map((message) => {
        const { configId, value } = message.params as { configId: string; value: string };
        return [configId, value];
      });
    assert.deepEqual(sets, [
      ['model', 'claude-haiku-5.5'],
      ['autopilot', 'off'],
      ['contentCollection', 'disabled'],
    ]);
  });

  test("a model change that turns Kiro's autopilot on and sharing back on is set back before the task runs", async (t) => {
    const fixture = withModel((answer) =>
      answer
        .replace(/("id": "autopilot"[^}]*"currentValue": )"off"/, '$1"on"')
        .replace(/("id": "contentCollection"[^}]*"currentValue": )"disabled"/, '$1"enabled"'),
    );
    assert.match(fixture, /"currentValue": "enabled"/);
    const run = setUp(t, 'cut', { profile: KIRO, fixtureText: fixture });
    await start(run, 'Make the file.', 'claude-haiku-5.5');
    // Each held option was set after the model and read back at its held value, so the task started.
    await run.next('runtime.approval.requested');
  });
});

describe("Kiro's content collection held off (the lead's ruling, 2026-11-17)", () => {
  test('every Kiro session sets content collection disabled and starts only once Kiro reads it back', async (t) => {
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', { profile: KIRO });
    await start(run);
    const sets = run
      .sent()
      .filter((message) => message.method === 'session/set_config_option')
      .map((message) => message.params as { configId: string; value: string });
    assert.deepEqual(
      sets.map(({ configId, value }) => [configId, value]),
      [
        ['autopilot', 'off'],
        ['contentCollection', 'disabled'],
      ],
    );
  });

  test('a Kiro that keeps it enabled is not started, in words', async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'kiro-signed-in-deny.jsonl'), 'utf8').split('\n');
    // Kiro's answer to the set, as if it kept sharing on.
    const answers = recorded
      .map((line, index) => ({ line, index }))
      .filter(({ line }) => line.includes('"answer": "session/set_config_option"'));
    const second = answers[1];
    assert.ok(second !== undefined);
    recorded[second.index] = second.line.replace(
      /("id": "contentCollection"[^}]*"currentValue": )"disabled"/,
      '$1"enabled"',
    );
    const run = setUp(t, 'cut', { profile: KIRO, fixtureText: recorded.join('\n') });
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'option_refused' &&
        /would not stop sharing this task's work/.test(error.message),
    );
    assert.ok(!run.sent().some((message) => message.method === 'session/prompt'));
  });

  test('Kiro reporting it enabled again during a turn stops the turn in words', async (t) => {
    const lines = readFileSync(join(FIXTURES, 'kiro-signed-in-edit.jsonl'), 'utf8').split('\n');
    const prompt = lines.findIndex((line) => line.includes('"expect": "session/prompt"'));
    const recorded = lines
      .map((line, index) =>
        index > prompt && line.includes('"config_option_update"')
          ? line.replace(
              /("id": "contentCollection"[^}]*"currentValue": )"disabled"/,
              '$1"enabled"',
            )
          : line,
      )
      .join('\n');
    assert.ok(/"id": "contentCollection"[^}]*"currentValue": "enabled"/.test(recorded));
    const run = setUp(t, 'cut', { profile: KIRO, fixtureText: recorded });
    await start(run);
    const failed = await run.next('runtime.turn.failed');
    assert.deepEqual((failed.payload as { error: unknown }).error, {
      code: 'sharing_on',
      message:
        'Kiro 3.4.0 turned back on sharing your work to improve its service, so Halora stopped the turn.',
    });
    assert.ok(run.sent().some((message) => message.method === 'session/cancel'));
  });
});

describe('ACP adapter on Kiro 3.4.0 traces', () => {
  test('signed in: each request shows the input Kiro reported for its call; commands whole, new files whole', async (t) => {
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', { profile: KIRO });
    await start(run);
    const asked: {
      summary: string;
      complete: boolean;
      kind: string;
      command: ApprovalCommand | undefined;
    }[] = [];
    for (let index = 0; index < 6; index++) {
      const request = await run.next('runtime.approval.requested', index);
      if (request.type !== 'runtime.approval.requested') return;
      asked.push({
        summary: request.payload.subject.summary,
        complete: request.payload.complete,
        kind: request.payload.subject.tool_name,
        command: request.payload.subject.command,
      });
      await run.adapter.respondToApproval({
        execution: TEST_EXECUTION,
        approval_id: request.payload.approval_id,
        decision: 'deny',
        message: null,
      });
    }
    await run.next('runtime.turn.completed');
    const commands = asked.filter((request) => request.kind === 'execute');
    // Each command whole in its parts, from the input Kiro reported for the call, where it runs said unknown: no run
    // showed it (AUDIT3 CP1 to CP3). The summary is the command alone, as its parts say.
    assert.deepEqual(
      commands.map((request) => [request.summary, request.command, request.complete]),
      ['echo "hello" > hello.txt', 'ls -la', 'true'].map((text) => [
        text,
        { text, runs_in: { kind: 'unknown' }, background: false, input_to_running: false },
        true,
      ]),
    );
    const writes = asked.filter((request) => request.kind === 'edit');
    assert.equal(writes.length, 3);
    for (const write of writes) assert.equal(write.command, undefined);
    // Each a new file's whole text, `{path, text}` where nothing is yet: carried, so it can be approved.
    for (const write of writes) {
      assert.equal(write.complete, true);
      assert.match(write.summary, /hello\.txt/);
    }
    // `pwd` ran without a request, by Kiro's own rules; it is reported as a tool all the same.
    const tools = run.observations.filter((o) => o.type === 'runtime.tool.started').length;
    assert.equal(tools, 7);
    for (const answer of run
      .sent()
      .filter((message) => message.method === undefined && 'result' in message)) {
      assert.deepEqual((answer.result as { outcome: unknown }).outcome, {
        outcome: 'selected',
        optionId: 'reject',
      });
    }
    assertValidObservations(run.observations);
  });

  test('signed in: an edit asks with its replacement carried whole, and approved it runs', async (t) => {
    const run = setUp(t, 'kiro-signed-in-edit.jsonl', { profile: KIRO });
    await start(run);
    const asked = await run.next('runtime.approval.requested');
    if (asked.type !== 'runtime.approval.requested') return;
    assert.equal(asked.payload.complete, true);
    assert.deepEqual(asked.payload.subject.changes, [
      {
        path: join(run.folder, 'notes.txt'),
        real_path: null,
        to_path: null,
        kind: 'update',
        shape: 'replace',
        before: 'line two',
        after: 'line 2',
        every: false,
        cell: null,
      },
    ]);
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: asked.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    await run.next('runtime.turn.completed');
    const answer = run
      .sent()
      .find((message) => message.method === undefined && 'result' in message);
    assert.deepEqual((answer?.result as { outcome: unknown } | undefined)?.outcome, {
      outcome: 'selected',
      optionId: 'accept',
    });
    assertValidObservations(run.observations);
  });

  test("Supervised, Kiro's review of its staged changes asks with each change as it was asked, and approved it goes on", async (t) => {
    const recorded = readFileSync(join(FIXTURES, 'kiro-signed-in-edit.jsonl'), 'utf8').split('\n');
    const answered = recorded.findIndex((line) => line.includes('"expectResponse"'));
    const edit = 'tooluse_MVsTxhc6rCMnfecKH2lciL';
    const sessionId = JSON.parse(
      recorded.find((line) => line.includes('"answer": "session/new"')) ?? '{}',
    ).result.sessionId;
    // The review as Kiro asked it under Supervised (seen 18:46Z), here for the recorded edit, then its resolution.
    const review = 'turn_approval_1_1_test';
    const steps = [
      JSON.stringify({
        send: {
          jsonrpc: '2.0',
          id: 99,
          method: 'session/request_permission',
          params: {
            sessionId,
            toolCall: { toolCallId: review, status: 'pending', title: 'Review changes' },
            options: [
              { optionId: 'accept', name: 'Accept', kind: 'allow_once' },
              { optionId: 'reject', name: 'Reject', kind: 'reject_once' },
            ],
            _meta: {
              kiro: {
                type: 'turn_approval',
                files: [{ path: '$CWD/notes.txt', toolCallId: edit }],
              },
            },
          },
        },
      }),
      JSON.stringify({ expectResponse: 99 }),
      JSON.stringify({
        send: {
          jsonrpc: '2.0',
          method: 'session/update',
          params: {
            sessionId,
            update: {
              sessionUpdate: 'session_info_update',
              _meta: {
                kiro: {
                  kind: 'interaction_resolved',
                  toolCallId: review,
                  outcome: 'selected',
                  selectedOption: 'accept',
                },
              },
            },
          },
        },
      }),
    ];
    // After Kiro's own report that it took the edit's answer, as it reports each answer.
    const took = recorded.findIndex(
      (line, index) => index > answered && line.includes('"interaction_resolved"'),
    );
    assert.ok(took > answered);
    const run = setUp(t, 'cut', {
      profile: KIRO,
      fixtureText: [...recorded.slice(0, took + 1), ...steps, ...recorded.slice(took + 1)].join(
        '\n',
      ),
    });
    await start(run);
    const first = await run.next('runtime.approval.requested');
    if (first.type !== 'runtime.approval.requested') return;
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: first.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    const second = await run.next('runtime.approval.requested', 1);
    if (second.type !== 'runtime.approval.requested') return;
    assert.equal(second.payload.subject.tool_name, 'review');
    assert.equal(
      second.payload.subject.summary,
      `Review changes: ${join(run.folder, 'notes.txt')}`,
    );
    assert.deepEqual(second.payload.subject.changes, first.payload.subject.changes);
    assert.equal(second.payload.complete, true);
    await run.adapter.respondToApproval({
      execution: TEST_EXECUTION,
      approval_id: second.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    await run.next('runtime.turn.completed');
    assertValidObservations(run.observations);
  });

  test('a review listing a call whose change was never carried can only be denied', () => {
    const asked = approvalRequest(
      { toolCallId: 'r', title: 'Review changes' },
      KIRO,
      '/nonexistent-project',
      {
        meta: {
          kiro: { type: 'turn_approval', files: [{ path: '/p/x', toolCallId: 'never-asked' }] },
        },
        changesOf: () => undefined,
      },
    );
    assert.equal(asked.subject.tool_name, 'review');
    assert.equal(asked.subject.changes, undefined);
    assert.equal(asked.complete, false);
  });

  test("Kiro's autopilot is set off even where session/new does not report it, and a start where Kiro reads it back on is refused", async (t) => {
    // The deny trace's session/new names no configOptions: the set is sent all the same (AUDIT3 AK1).
    const reported = readFileSync(join(FIXTURES, 'kiro-signed-in-deny.jsonl'), 'utf8');
    assert.equal(
      reported.split('"expect": "session/set_config_option"')[0]?.includes('"configOptions"'),
      false,
    );
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', { profile: KIRO });
    await start(run);
    const set = run.sent().find((message) => message.method === 'session/set_config_option');
    assert.deepEqual(
      (set?.params as { configId?: string; value?: string } | undefined)?.value,
      'off',
    );
    // The same set, answered with autopilot still on and no report since: refused before any prompt (AK2).
    const still = reported
      .split('\n')
      .filter(
        (line) =>
          !(
            line.includes('"config_option_update"') &&
            line.includes('"send"') &&
            reported.indexOf(line) < reported.indexOf('"answer": "session/set_config_option"')
          ),
      )
      .map((line) =>
        line.includes('"answer": "session/set_config_option"')
          ? line.replace(/("id": "autopilot"[^}]*"currentValue": )"off"/, '$1"on"')
          : line,
      )
      .join('\n');
    const refused = setUp(t, 'cut', { profile: KIRO, fixtureText: still });
    await assert.rejects(
      start(refused),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'mode_unavailable' &&
        error.effect === 'none',
    );
    assert.equal(
      refused.sent().some((message) => message.method === 'session/prompt'),
      false,
    );
  });

  test('signed in: Kiro reporting autopilot back on during a turn stops the turn in words', async (t) => {
    // The recorded edit run, as Kiro reported it before Halora set autopilot off: on, in the middle of the turn.
    const recorded = readFileSync(join(FIXTURES, 'kiro-signed-in-edit.jsonl'), 'utf8')
      .split('\n')
      .map((line, index, lines) =>
        index >
          lines.findIndex((entry) => entry.includes('"answer": "session/set_config_option"')) &&
        line.includes('"config_option_update"')
          ? line
              .replace(
                '"id": "autopilot", "name": "Autopilot"',
                '"id": "autopilot", "name": "Autopilot"',
              )
              .replace(/("id": "autopilot"[^}]*"currentValue": )"off"/, '$1"on"')
          : line,
      )
      .join('\n');
    assert.ok(
      /"id": "autopilot"[^}]*"currentValue": "on"/.test(
        recorded.split('"answer": "session/set_config_option"').slice(1).join(''),
      ),
    );
    const run = setUp(t, 'cut', { profile: KIRO, fixtureText: recorded });
    await start(run);
    const failed = await run.next('runtime.turn.failed');
    assert.equal((failed.payload as { error: { code: string } }).error.code, 'mode_left');
    assert.ok(run.sent().some((message) => message.method === 'session/cancel'));
    const set = run.sent().find((message) => message.method === 'session/set_config_option');
    assert.deepEqual(
      (set?.params as { configId?: string; value?: string } | undefined)?.value,
      'off',
    );
  });

  test('signed in, Kiro names no version in initialize: its own version line does, and another one is refused', async (t) => {
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', {
      profile: KIRO,
      versionLine: 'kiro-cli 3.5.0',
    });
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'runtime_version_unsupported' &&
        error.effect === 'none' &&
        error.message.includes('this computer has 3.5.0'),
    );
    assert.deepEqual(types(run), []);
  });

  test('a Kiro that is not signed in fails the start in words, with nothing started', async (t) => {
    const run = setUp(t, 'kiro-signed-out.jsonl', { profile: KIRO });
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'sign_in_required' &&
        error.effect === 'none' &&
        error.message.includes('not signed in'),
    );
    assert.deepEqual(types(run), []);
    assert.deepEqual(run.args(), ['acp', '--agent-engine', 'v3']);
    assert.equal(dirname(run.kiroHome()), join(run.root, '.kiro', 'halora'));
    assert.equal(existsSync(run.kiroHome()), false, 'removed once the app ended');
  });
});

describe('ACP settings homes left behind', () => {
  test('a launch removes the homes whose control plane is gone, and leaves every other entry', (t) => {
    const root = temporary(t);
    const dead = 999_999;
    assert.throws(
      () => process.kill(dead, 0),
      'no process has the id the test takes for a gone one',
    );
    const id = '0f0f0f0f-0f0f-4f0f-8f0f-0f0f0f0f0f0f';
    for (const name of [`${dead}-${id}`, `${process.pid}-${id}`, `1-${id}`, 'not-a-home']) {
      mkdirSync(join(root, name, 'antigravity-cli'), { recursive: true });
    }
    sweepSettingsHomes(root);
    assert.deepEqual(
      readdirSync(root).sort(),
      [`${process.pid}-${id}`, `1-${id}`, 'not-a-home'].sort(),
    );
  });
});

describe('ACP apps never outlive the control plane (AUDIT3 AS1)', () => {
  const HOST = fileURLToPath(new URL('./testing/acp-host.ts', import.meta.url));

  /** A fake app that ignores the end of its input, as an app may, and says its pid. */
  function stayingApp(t: TestContext): { root: string; binary: string } {
    const root = temporary(t);
    mkdirSync(join(root, 'project'));
    const binary = join(root, 'app');
    writeFileSync(
      binary,
      `#!/bin/sh\necho $$ > '${join(root, 'app.pid')}'\nexec '${process.execPath}' '${FAKE}' '${join(FIXTURES, 'antigravity-two.jsonl')}'\n`,
    );
    chmodSync(binary, 0o755);
    return { root, binary };
  }

  function alive(pid: number): boolean {
    try {
      process.kill(pid, 0);
      return true;
    } catch {
      return false;
    }
  }

  async function killedHost(t: TestContext, record: boolean, waitMs: number): Promise<number> {
    const { root, binary } = stayingApp(t);
    const host = spawn(
      process.execPath,
      [HOST, binary, root, record ? join(root, 'processes.json') : 'none'],
      {
        stdio: ['ignore', 'pipe', 'ignore'],
      },
    );
    const pid = await new Promise<number>((resolve, reject) => {
      host.stdout.once('data', (chunk: Buffer) => resolve(Number(String(chunk).trim())));
      host.once('exit', () => reject(new Error('the host ended before it started the app')));
    });
    t.after(() => {
      // Only the app this test launched, by its own pid.
      if (alive(pid)) process.kill(pid, 'SIGKILL');
    });
    assert.ok(alive(pid));
    host.kill('SIGKILL');
    for (let waited = 0; waited < waitMs / 100 && alive(pid); waited++) await delay(100);
    return pid;
  }

  test('a control plane killed hard: its watchdog stops the app it was running', async (t) => {
    const pid = await killedHost(t, true, 10_000);
    assert.equal(alive(pid), false);
  });

  test('without a record the same app outlives it, so the test above can fail', async (t) => {
    const pid = await killedHost(t, false, 2_000);
    assert.equal(alive(pid), true);
  });

  test('the sweep stops a recorded app that leads its process group, with the commands it started', async (t) => {
    const root = temporary(t);
    const file = join(root, 'processes.json');
    const leader = spawn(
      process.execPath,
      [
        '-e',
        "const c = require('node:child_process').spawn(process.execPath, ['-e', 'setInterval(() => undefined, 1000)'], { stdio: 'ignore' }); console.log(c.pid); setInterval(() => undefined, 1000);",
      ],
      { detached: true, stdio: ['ignore', 'pipe', 'ignore'] },
    );
    const child = await new Promise<number>((resolve) =>
      leader.stdout.once('data', (chunk: Buffer) => resolve(Number(String(chunk).trim()))),
    );
    t.after(() => {
      for (const pid of [child, leader.pid ?? 0]) if (alive(pid)) process.kill(pid, 'SIGKILL');
    });
    const identity = await readProcessIdentity(leader.pid ?? 0);
    assert.ok(identity !== null && alive(child));
    await writeProcessRecords(file, [{ pid: leader.pid ?? 0, ...identity }]);
    const adapter = new AcpRuntimeAdapter({
      profile: ANTIGRAVITY,
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: root,
      processRecordFile: file,
      hostFolders: null,
    });
    t.after(() => adapter.close());
    assert.deepEqual(await adapter.stopStaleProcesses(), [{ pid: leader.pid, outcome: 'stopped' }]);
    for (let waited = 0; waited < 50 && alive(child); waited++) await delay(100);
    assert.equal(alive(child), false, 'the command the app started is gone with it');
  });

  test('the next start stops a recorded app that outlived both, and never a process that only took its pid', async (t) => {
    const root = temporary(t);
    const file = join(root, 'processes.json');
    const marker = `acp-sweep-${process.pid}-${Date.now()}`;
    const stray = spawn(process.execPath, ['-e', 'setInterval(() => undefined, 1000)', marker], {
      stdio: 'ignore',
    });
    const other = spawn(
      process.execPath,
      ['-e', 'setInterval(() => undefined, 1000)', `${marker}-other`],
      { stdio: 'ignore' },
    );
    t.after(() => {
      for (const child of [stray, other]) if (child.exitCode === null) child.kill('SIGKILL');
    });
    await delay(300);
    const strayIdentity = await readProcessIdentity(stray.pid ?? 0);
    const otherIdentity = await readProcessIdentity(other.pid ?? 0);
    assert.ok(strayIdentity !== null && otherIdentity !== null);
    await writeProcessRecords(file, [
      { pid: stray.pid ?? 0, ...strayIdentity },
      // A record whose pid a later process took: another command line.
      {
        pid: other.pid ?? 0,
        startedAt: otherIdentity.startedAt,
        command: `${otherIdentity.command} changed`,
      },
    ]);
    const adapter = new AcpRuntimeAdapter({
      profile: ANTIGRAVITY,
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: root,
      processRecordFile: file,
      hostFolders: null,
    });
    t.after(() => adapter.close());
    const outcomes = await adapter.stopStaleProcesses();
    assert.deepEqual(
      [...outcomes].sort((a, b) => a.pid - b.pid),
      [
        { pid: stray.pid, outcome: 'stopped' },
        { pid: other.pid, outcome: 'not_ours' },
      ].sort((a, b) => (a.pid ?? 0) - (b.pid ?? 0)),
    );
    assert.equal(alive(other.pid ?? 0), true);
    assert.equal(existsSync(file), false);
  });
});

describe('ACP adapter on Cursor 2026.11.13 traces', () => {
  test("a Cursor not signed in fails the start in words, its settings home holding Halora's own asking setup", async (t) => {
    const run = setUp(t, 'cursor-signed-out.jsonl', {
      profile: CURSOR,
      versionLine: '2026.11.13-eacade1',
    });
    let settings = '';
    const watch = setInterval(() => {
      try {
        const home = readdirSync(join(run.root, '.cursor', 'halora'))[0];
        if (home !== undefined)
          settings = readFileSync(
            join(run.root, '.cursor', 'halora', home, 'cli-config.json'),
            'utf8',
          );
      } catch {}
    }, 20);
    t.after(() => clearInterval(watch));
    await assert.rejects(
      start(run),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'sign_in_required' &&
        error.effect === 'none',
    );
    assert.deepEqual(types(run), []);
    assert.deepEqual(JSON.parse(settings), {
      version: 1,
      permissions: { allow: [], deny: [] },
      approvalMode: 'allowlist',
      sandbox: { mode: 'disabled' },
    });
    // Its own sign-in is asked for only once the session was refused as not signed in.
    assert.deepEqual(
      run.sent().map((message) => message.method),
      ['initialize', 'session/new', 'authenticate'],
    );
    assert.equal(run.variables().includes('CURSOR_API_KEY'), false);
  });
});

describe('ACP approval requests', () => {
  const folder = '/nonexistent-project';

  test('an edit whose reach the profile does not know is carried as nothing, so only denied', () => {
    const asked = approvalRequest(
      {
        toolCallId: 'a',
        kind: 'edit',
        title: 'Edit x',
        rawInput: { path: 'x', old: 'a', new: 'b' },
        content: [{ type: 'diff', path: '/nonexistent-project/x', oldText: 'a', newText: 'b' }],
      },
      KIRO,
      folder,
    );
    assert.equal(asked.subject.changes, undefined);
    assert.equal(asked.complete, false);
  });

  test("Kiro's write over a file already there is not taken for a new one, so it can only be denied", (t) => {
    const folder = temporary(t);
    writeFileSync(join(folder, 'here.txt'), 'old\n');
    const asked = approvalRequest(
      {
        toolCallId: 'a',
        kind: 'edit',
        rawInput: { path: join(folder, 'here.txt'), text: 'new\n' },
      },
      KIRO,
      folder,
    );
    assert.equal(asked.subject.changes, undefined);
    assert.equal(asked.complete, false);
  });

  test('a request without its input is shown by its title and never complete', () => {
    const asked = approvalRequest(
      { toolCallId: 'a', kind: 'execute', title: 'Run tests' },
      KIRO,
      folder,
    );
    assert.equal(asked.subject.summary, 'Run tests');
    assert.equal(asked.complete, false);
  });

  test("a command in a folder of the app's own naming says that folder, in quotes, resolved against the task's (CP3)", () => {
    const asked = approvalRequest(
      {
        toolCallId: 'a',
        name: 'run_command',
        kind: 'execute',
        rawInput: { CommandLine: 'ls', Cwd: '/elsewhere' },
      },
      ANTIGRAVITY,
      folder,
    );
    assert.deepEqual(asked.subject.command, {
      text: 'ls',
      runs_in: { kind: 'folder', path: '/elsewhere' },
      background: false,
      input_to_running: false,
    });
    assert.equal(asked.subject.summary, '[in "/elsewhere"] ls');
    assert.equal(asked.complete, true);
    const relative = approvalRequest(
      {
        toolCallId: 'b',
        name: 'run_command',
        kind: 'execute',
        rawInput: { CommandLine: 'ls', Cwd: 'sub' },
      },
      ANTIGRAVITY,
      folder,
    );
    assert.deepEqual(relative.subject.command?.runs_in, {
      kind: 'folder',
      path: join(folder, 'sub'),
    });
  });

  test('a command beside an input that acts unknown is shown in words and each input, and is not complete', () => {
    const asked = approvalRequest(
      {
        toolCallId: 'a',
        name: 'run_command',
        kind: 'execute',
        rawInput: { CommandLine: 'ls', Blocking: true },
      },
      ANTIGRAVITY,
      folder,
    );
    assert.equal(asked.complete, false);
    // No command in parts where any other input may change what runs (AUDIT3 CP1); never JSON (AUDIT2).
    assert.equal(asked.subject.command, undefined);
    assert.equal(asked.subject.summary, 'Use run_command\nCommandLine: ls\nBlocking: true');
  });

  test("Cursor's recorded command call carries the command in its parts, where it runs unknown", () => {
    const recorded = readFileSync(join(FIXTURES, 'cursor-signed-in-deny.jsonl'), 'utf8')
      .split('\n')
      .filter((line) => line.trim() !== '')
      .map((line) => JSON.parse(line))
      .map((step) => step.send?.params?.update)
      .find((update) => update?.sessionUpdate === 'tool_call');
    assert.ok(recorded !== undefined, 'the recorded call');
    const asked = approvalRequest(recorded, CURSOR, folder);
    assert.deepEqual(asked.subject.command, {
      text: 'echo hello > hello.txt',
      runs_in: { kind: 'unknown' },
      background: false,
      input_to_running: false,
    });
    assert.equal(asked.subject.summary, 'echo hello > hello.txt');
    assert.equal(asked.complete, true);
  });

  test("Kiro's command field alone makes a command; its other known inputs never reach it", () => {
    const asked = approvalRequest(
      {
        toolCallId: 'a',
        kind: 'execute',
        rawInput: { command: 'npm test', description: 'Run the tests', run_in_background: true },
      },
      KIRO,
      folder,
    );
    assert.deepEqual(asked.subject.command, {
      text: 'npm test',
      runs_in: { kind: 'unknown' },
      background: true,
      input_to_running: false,
    });
    assert.equal(asked.subject.summary, '[in the background] npm test');
    // A command of many lines is one command line to its shell, kept whole, never cut (CP5).
    const long = 'echo start\n'.repeat(3000);
    const whole = approvalRequest(
      { toolCallId: 'b', kind: 'execute', rawInput: { command: long } },
      KIRO,
      folder,
    );
    assert.equal(whole.subject.command?.text, long);
  });

  test('a command in the background says so', () => {
    const asked = approvalRequest(
      {
        toolCallId: 'a',
        name: 'run_command',
        kind: 'execute',
        rawInput: { CommandLine: 'npm start', IsDaemon: true },
      },
      ANTIGRAVITY,
      folder,
    );
    assert.equal(asked.subject.summary, "[in the task's folder, in the background] npm start");
    assert.equal(asked.subject.command?.background, true);
    assert.equal(asked.complete, true);
  });

  test('any other tool is shown in words and then each of its inputs, never as JSON (AUDIT2)', () => {
    const asked = approvalRequest(
      { toolCallId: 'a', kind: 'fetch', title: 'Fetch', rawInput: { url: 'https://example.com' } },
      KIRO,
      folder,
    );
    assert.equal(asked.subject.summary, 'Use fetch\nurl: https://example.com');
    // A value or a name that holds a line break, a control character or an edge space is one quoted value, and adds
    // no line the person could read as another input (AUDIT3 IS1).
    const forged = approvalRequest(
      {
        toolCallId: 'b',
        kind: 'fetch',
        rawInput: { path: 'a.txt\ncontent: (empty)', 'x\ny': 1, pad: ' a', bell: 'a\u0007' },
      },
      KIRO,
      folder,
    );
    assert.equal(
      forged.subject.summary,
      'Use fetch\npath: "a.txt\\ncontent: (empty)"\n"x\\ny": 1\npad: " a"\nbell: "a\\u0007"',
    );
    assert.equal(forged.subject.summary.split('\n').length, 5);
    const named = approvalRequest(
      { toolCallId: 'c', name: 'fetch\nurl: elsewhere', rawInput: { url: 'x' } },
      KIRO,
      folder,
    );
    assert.equal(named.subject.summary.split('\n')[0], 'Use "fetch\\nurl: elsewhere"');
    assert.equal(asked.subject.tool_name, 'fetch');
    assert.equal(asked.complete, true);
  });

  test('a delete, with no change carried, is never complete', () => {
    const asked = approvalRequest(
      { toolCallId: 'a', kind: 'delete', rawInput: { path: 'x' }, locations: [{ path: '/p/x' }] },
      KIRO,
      folder,
    );
    assert.deepEqual(asked.subject.paths, [{ path: '/p/x', access: 'write' }]);
    assert.equal(asked.complete, false);
  });
});
