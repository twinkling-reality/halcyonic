import assert from 'node:assert/strict';
import {
  chmodSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { compileValidator, RuntimeDescriptor } from '@halcyonic/contracts';
import {
  capabilityProblems,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { ACP_CAPABILITIES, AcpRuntimeAdapter, UNCONFINED_REASON } from './acp-runtime.ts';
import { approvalRequest } from './approvals.ts';
import { type AcpProfile, ANTIGRAVITY, KIRO } from './profiles.ts';
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
    `#!/bin/sh\nif [ "$1" = "--version" ]; then echo '${options.versionLine ?? 'kiro-cli 3.4.0'}'; exit 0; fi\nenv | cut -d= -f1 > '${variables}'\nprintf '%s\\n' "$@" > '${join(root, 'args.txt')}'\nprintf '%s' "$KIRO_HOME" > '${join(root, 'kiro-home.txt')}'\nexec '${process.execPath}' '${FAKE}' '${replay}'\n`,
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

function start(run: Run, instruction = 'Make the file.') {
  return run.adapter.startExecution({
    execution: TEST_EXECUTION,
    instruction,
    options: {},
    model_ref: null,
    directory: run.folder,
    emit: run.emit,
  });
}

function types(run: Run): string[] {
  return run.observations.map((observation) => observation.type);
}

describe('ACP adapter descriptor', () => {
  test('declares only what a runtime test proved, consistently with its methods', () => {
    for (const profile of [ANTIGRAVITY, KIRO]) {
      const adapter = new AcpRuntimeAdapter({
        profile,
        binaryPath: '/nonexistent',
        directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
        env: {},
        home: '/nonexistent-home',
        hostFolders: null,
      });
      assert.deepEqual(capabilityProblems(adapter), []);
      assert.ok(compileValidator(RuntimeDescriptor)(adapter.descriptor));
      assert.deepEqual(adapter.descriptor.capabilities, ACP_CAPABILITIES);
      assert.equal(adapter.descriptor.capabilities.instruct_while_running, false);
      assert.equal(adapter.descriptor.capabilities.answer_question, false);
      assert.equal(adapter.descriptor.model_choice, 'none');
      assert.equal(adapter.descriptor.offers_never_ask, undefined);
      assert.equal(adapter.descriptor.reports_tool_activity, false);
      assert.equal(adapter.runsOutsideSandbox(), true);
    }
  });

  test('refuses start options, a model and never asking', () => {
    const adapter = new AcpRuntimeAdapter({
      profile: ANTIGRAVITY,
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: '/nonexistent-home',
      hostFolders: null,
    });
    assert.equal(adapter.validateStartOptions({}, null).ok, true);
    assert.equal(adapter.validateStartOptions({ model: 'x' }, null).ok, false);
    assert.equal(adapter.validateStartOptions({}, 'gemini/x').ok, false);
    assert.equal(adapter.validateStartOptions({}, null, 'never_ask').ok, false);
  });

  test('names the app’s own sign-in and endpoint variables as its own', () => {
    const adapter = new AcpRuntimeAdapter({
      profile: ANTIGRAVITY,
      binaryPath: '/nonexistent',
      directoryPolicy: () => ({ ok: false, code: 'location_not_allowed', message: 'no' }),
      env: {},
      home: '/nonexistent-home',
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
    assert.deepEqual(asked.payload.subject, {
      kind: 'tool_use',
      tool_name: 'run_command',
      summary: "[in its shell, which started in the task's folder] echo halcyonic-probe > made.txt",
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
    // A settings home of Halora's own, so the person's own settings for the app never apply.
    assert.deepEqual(run.args(), [
      'acp',
      '--disable-telemetry',
      '--gemini-dir',
      join(run.root, '.gemini', 'halora'),
    ]);
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

describe('ACP adapter on Kiro 3.4.0 traces', () => {
  test('signed in: each request shows the input Kiro reported for its call; commands whole, writes only denied', async (t) => {
    const run = setUp(t, 'kiro-signed-in-deny.jsonl', { profile: KIRO });
    await start(run);
    const asked: { summary: string; complete: boolean; kind: string }[] = [];
    for (let index = 0; index < 6; index++) {
      const request = await run.next('runtime.approval.requested', index);
      if (request.type !== 'runtime.approval.requested') return;
      asked.push({
        summary: request.payload.subject.summary,
        complete: request.payload.complete,
        kind: request.payload.subject.tool_name,
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
    assert.deepEqual(
      commands.map((request) => [JSON.parse(request.summary).command, request.complete]),
      [
        ['echo "hello" > hello.txt', true],
        ['ls -la', true],
        ['true', true],
      ],
    );
    const writes = asked.filter((request) => request.kind === 'edit');
    assert.equal(writes.length, 3);
    for (const write of writes) {
      assert.equal(write.complete, false);
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
    assert.equal(run.kiroHome(), join(run.root, '.kiro', 'halora'));
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

  test('a request without its input is shown by its title and never complete', () => {
    const asked = approvalRequest(
      { toolCallId: 'a', kind: 'execute', title: 'Run tests' },
      KIRO,
      folder,
    );
    assert.equal(asked.subject.summary, 'Run tests');
    assert.equal(asked.complete, false);
  });

  test('a command beside an input that acts unknown is shown whole but not complete', () => {
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
    assert.equal(asked.complete, false);
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
    assert.equal(
      asked.subject.summary,
      "[in its shell, which started in the task's folder, in the background] npm start",
    );
    assert.equal(asked.complete, true);
  });

  test('any other tool is shown as its whole input', () => {
    const asked = approvalRequest(
      { toolCallId: 'a', kind: 'fetch', title: 'Fetch', rawInput: { url: 'https://example.com' } },
      KIRO,
      folder,
    );
    assert.equal(asked.subject.summary, '{"url":"https://example.com"}');
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
