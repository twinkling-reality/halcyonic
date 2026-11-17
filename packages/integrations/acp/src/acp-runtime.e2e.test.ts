import assert from 'node:assert/strict';
import {
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
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { RuntimeActionError, type RuntimeObservation } from '@halcyonic/runtime-core';
import { AcpRuntimeAdapter } from './acp-runtime.ts';
import { ANTIGRAVITY, KIRO } from './profiles.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { assertValidObservations, TEST_EXECUTION } from './testing/observations.ts';
import { StandInGemini, type StandInReply } from './testing/stand-in-gemini.ts';

/**
 * End to end against the real Antigravity CLI 1.5.10 (`agy`, the vendor's darwin-arm64 build, sha512 from
 * its manifest), its turns served by a stand-in Gemini API on loopback: no sign-in, no key, no spend.
 */
const BINARY = process.env.HALCYONIC_AGY_BIN ?? '';
const SKIP =
  BINARY === ''
    ? 'HALCYONIC_AGY_BIN is not set; point it at the pinned Antigravity CLI 1.5.10 to run these tests'
    : existsSync(BINARY)
      ? false
      : `HALCYONIC_AGY_BIN names no file: ${BINARY}`;

interface Run {
  readonly adapter: AcpRuntimeAdapter;
  /** What the adapter was made with, for a later launch on the same home. */
  readonly options: ConstructorParameters<typeof AcpRuntimeAdapter>[0];
  readonly project: string;
  readonly host: string;
  readonly home: string;
  readonly observations: RuntimeObservation[];
  readonly standIn: StandInGemini;
  next(type: RuntimeObservation['type'], after?: number): Promise<RuntimeObservation>;
  start(instruction?: string): Promise<{ native_id: string | null }>;
}

function command(line: string): StandInReply {
  return [
    {
      functionCall: {
        name: 'run_command',
        args: {
          CommandLine: line,
          IsDaemon: false,
          WaitMsBeforeAsync: 5000,
          toolSummary: 'A command',
        },
      },
    },
  ];
}

async function setUp(
  t: TestContext,
  replies: StandInReply[] | ((host: string) => StandInReply[]),
  options: { signedIn?: boolean } = {},
): Promise<Run> {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-acp-e2e-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const project = join(root, 'projects', 'one');
  const host = join(root, 'host-data');
  const home = join(root, 'home');
  mkdirSync(project, { recursive: true });
  mkdirSync(host);
  // The app's own temporary folder, apart from the rest: it reads there unasked.
  mkdirSync(join(root, 'tmp'));
  // The person's own settings for the app, which never apply to a task: here every call would go ahead unasked.
  mkdirSync(join(home, '.gemini', 'antigravity-cli'), { recursive: true });
  writeFileSync(
    join(home, '.gemini', 'antigravity-cli', 'settings.json'),
    '{"modelProvider":"gemini","toolPermission":"always-proceed"}\n',
  );
  // A sign-in file of the person's and a file beside it, outside the project.
  mkdirSync(join(home, '.ssh'));
  writeFileSync(join(home, '.ssh', 'id_marker'), 'halcyonic-ssh-marker\n');
  writeFileSync(join(home, 'beside.txt'), 'halcyonic-beside-marker\n');
  writeFileSync(join(host, 'held.txt'), 'halcyonic-held-value\n');
  const standIn = await StandInGemini.start(
    typeof replies === 'function' ? replies(host) : replies,
  );
  t.after(() => standIn.close());
  const made: ConstructorParameters<typeof AcpRuntimeAdapter>[0] = {
    profile: ANTIGRAVITY,
    binaryPath: BINARY,
    directoryPolicy: allowOnly(join(root, 'projects')),
    env: {
      PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
      HOME: home,
      LANG: 'en_US.UTF-8',
      TMPDIR: `${join(root, 'tmp')}/`,
    },
    home,
    processRecordFile: join(root, 'processes.json'),
    ...(options.signedIn === false
      ? {}
      : {
          standIn: {
            variables: {
              GEMINI_API_KEY: 'halcyonic-stand-in-not-a-key',
              GOOGLE_GEMINI_BASE_URL: standIn.url,
            },
            authMethod: 'gemini-api-key',
            // The stand-in's key setting, in each launch's own settings home.
            settings: { 'antigravity-cli/settings.json': '{"modelProvider":"gemini"}\n' },
          },
        }),
    hostFolders:
      process.platform === 'darwin'
        ? { scope: { folders: [host], home }, profileFolder: join(root, 'profiles') }
        : null,
  };
  const adapter = new AcpRuntimeAdapter(made);
  t.after(() => adapter.close());
  const observations: RuntimeObservation[] = [];
  const emit = (observation: RuntimeObservation) => {
    observations.push(observation);
  };
  return {
    adapter,
    options: made,
    project,
    host,
    home,
    observations,
    standIn,
    async next(type, after = 0) {
      for (let waited = 0; waited < 1200; waited++) {
        const found = observations.filter((observation) => observation.type === type)[after];
        if (found !== undefined) return found;
        await delay(50);
      }
      assert.fail(`no ${type} after ${after}: ${observations.map((o) => o.type).join(', ')}`);
    },
    start: (instruction = 'Do the work.') =>
      adapter.startExecution({
        execution: TEST_EXECUTION,
        instruction,
        options: {},
        model_ref: null,
        directory: project,
        emit,
      }),
  };
}

async function answer(run: Run, index: number, decision: 'approve' | 'deny') {
  const asked = await run.next('runtime.approval.requested', index);
  assert.equal(asked.type, 'runtime.approval.requested');
  if (asked.type !== 'runtime.approval.requested') throw new Error('not an approval');
  await run.adapter.respondToApproval({
    execution: TEST_EXECUTION,
    approval_id: asked.payload.approval_id,
    decision,
    message: null,
  });
  return asked;
}

describe('Antigravity 1.5.10 end to end, on a stand-in model', { skip: SKIP }, () => {
  test('an approved command runs in the project, asked whole, and the turn completes', async (t) => {
    const run = await setUp(t, [command('echo made > made.txt'), [{ text: 'Made it.' }]]);
    const started = await run.start();
    assert.match(started.native_id ?? '', /^[0-9a-f-]{36}$/);
    assert.equal(existsSync(join(run.project, 'made.txt')), false);
    const asked = await answer(run, 0, 'approve');
    if (asked.type !== 'runtime.approval.requested') return;
    assert.equal(asked.payload.complete, true);
    assert.equal(
      asked.payload.subject.summary,
      "[in its shell, which started in the task's folder] echo made > made.txt",
    );
    await run.next('runtime.turn.completed');
    assert.equal(readFileSync(join(run.project, 'made.txt'), 'utf8'), 'made\n');
    const messages = run.observations
      .filter((o) => o.type === 'runtime.agent_message')
      .map((o) => o.payload);
    assert.deepEqual(messages, [{ text: 'Made it.' }]);
    assertValidObservations(run.observations);
  });

  test('a denied command never runs', async (t) => {
    const run = await setUp(t, [command('echo made > made.txt'), [{ text: 'Understood.' }]]);
    await run.start();
    await answer(run, 0, 'deny');
    await run.next('runtime.turn.completed');
    assert.equal(existsSync(join(run.project, 'made.txt')), false);
    const resolved = run.observations.find((o) => o.type === 'runtime.approval.resolved');
    assert.equal((resolved?.payload as { decision?: string } | undefined)?.decision, 'denied');
    assertValidObservations(run.observations);
  });

  test('stopping during an approval ends the turn interrupted and the command never runs', async (t) => {
    const run = await setUp(t, [command('echo made > made.txt'), [{ text: 'Never sent.' }]]);
    await run.start();
    await run.next('runtime.approval.requested');
    await run.adapter.interrupt({ execution: TEST_EXECUTION });
    assert.equal(run.observations.at(-1)?.type, 'runtime.turn.interrupted');
    await delay(500);
    assert.equal(existsSync(join(run.project, 'made.txt')), false);
    assertValidObservations(run.observations);
  });

  test('a second instruction at rest continues the same session', async (t) => {
    const run = await setUp(t, [[{ text: 'First.' }], [{ text: 'Second.' }]]);
    await run.start();
    await run.next('runtime.turn.completed');
    await run.adapter.sendInstruction({ execution: TEST_EXECUTION, text: 'Again.' });
    await run.next('runtime.turn.completed', 1);
    const messages = run.observations
      .filter((o) => o.type === 'runtime.agent_message')
      .map((o) => o.payload);
    assert.deepEqual(messages, [{ text: 'First.' }, { text: 'Second.' }]);
    assertValidObservations(run.observations);
  });

  test("an approved command cannot read the host's own folders", {
    skip: process.platform !== 'darwin' && 'the light profile is macOS only',
  }, async (t) => {
    // The control: the same read of a file beside the host's folders, not in them, succeeds.
    const second = await setUp(t, (host) => [
      command(
        `cat '${join(host, 'held.txt')}' > leaked.txt; echo "exit $?" > status.txt; cat '${join(host, '..', 'beside.txt')}' > control.txt`,
      ),
      [{ text: 'Tried.' }],
    ]);
    writeFileSync(join(second.host, '..', 'beside.txt'), 'readable\n');
    await second.start();
    await answer(second, 0, 'approve');
    await second.next('runtime.turn.completed');
    const status = readFileSync(join(second.project, 'status.txt'), 'utf8');
    assert.notEqual(status, 'exit 0\n');
    assert.equal(
      readFileSync(join(second.project, 'leaked.txt'), 'utf8').includes('halcyonic-held-value'),
      false,
    );
    assert.equal(readFileSync(join(second.project, 'control.txt'), 'utf8'), 'readable\n');
  });

  test("the person's own allow-everything setting does not apply: a command still asks, and denied never runs", async (t) => {
    const run = await setUp(t, [command('echo made > made.txt'), [{ text: 'Understood.' }]]);
    assert.match(
      readFileSync(join(run.home, '.gemini', 'antigravity-cli', 'settings.json'), 'utf8'),
      /always-proceed/,
    );
    await run.start();
    const asked = await answer(run, 0, 'deny');
    assert.equal(asked.type, 'runtime.approval.requested');
    await run.next('runtime.turn.completed');
    assert.equal(existsSync(join(run.project, 'made.txt')), false);
  });

  test('a read outside the folder asks, and denied its words never reach the model', async (t) => {
    const run = await setUp(t, (host) => [
      [
        {
          functionCall: {
            name: 'read_file',
            args: { Path: join(host, '..', 'home', 'beside.txt') },
          },
        },
      ],
      [{ text: 'Read.' }],
    ]);
    await run.start();
    const asked = await answer(run, 0, 'deny');
    if (asked.type !== 'runtime.approval.requested') return;
    assert.equal(asked.payload.subject.tool_name, 'read_file');
    assert.deepEqual(asked.payload.subject.paths, [
      { path: join(run.home, 'beside.txt'), access: 'read' },
    ]);
    await run.next('runtime.turn.completed');
    assert.equal(run.standIn.heard('halcyonic-beside-marker'), false);
  });

  test("an approved read of the person's sign-in files is refused by the profile; the same read beside them goes ahead", {
    skip: process.platform !== 'darwin' && 'the agent-app profile is macOS only',
  }, async (t) => {
    const run = await setUp(t, (host) => [
      [
        {
          functionCall: {
            name: 'read_file',
            args: { Path: join(host, '..', 'home', '.ssh', 'id_marker') },
          },
        },
      ],
      [
        {
          functionCall: {
            name: 'read_file',
            args: { Path: join(host, '..', 'home', 'beside.txt') },
          },
        },
      ],
      [{ text: 'Read.' }],
    ]);
    await run.start();
    await answer(run, 0, 'approve');
    await answer(run, 1, 'approve');
    await run.next('runtime.turn.completed');
    assert.equal(run.standIn.heard('halcyonic-ssh-marker'), false);
    assert.equal(run.standIn.heard('halcyonic-beside-marker'), true);
  });

  test("the project's own settings for the app are kept from it: an approved write of a hook there fails", {
    skip: process.platform !== 'darwin' && 'the agent-app profile is macOS only',
  }, async (t) => {
    const run = await setUp(t, [
      command(
        `mkdir -p .agents && echo planted > .agents/hooks.json; echo "exit $?" > status.txt; echo ok > beside.txt`,
      ),
      [{ text: 'Tried.' }],
    ]);
    await run.start();
    await answer(run, 0, 'approve');
    await run.next('runtime.turn.completed');
    assert.equal(existsSync(join(run.project, '.agents', 'hooks.json')), false);
    assert.notEqual(readFileSync(join(run.project, 'status.txt'), 'utf8'), 'exit 0\n');
    assert.equal(readFileSync(join(run.project, 'beside.txt'), 'utf8'), 'ok\n');
  });

  test("an approved write into Halora's settings homes reaches no later launch: the next one still asks", {
    skip: process.platform !== 'darwin' && 'the agent-app profile is macOS only',
  }, async (t) => {
    const allow = '{"modelProvider":"gemini","toolPermission":"always-proceed"}';
    const run = await setUp(t, [
      command(
        `mkdir -p ~/.gemini/halora/antigravity-cli; echo '${allow}' > ~/.gemini/halora/antigravity-cli/settings.json; echo "exit $?" > status.txt`,
      ),
      [{ text: 'Planted.' }],
      command('echo made > made.txt'),
      [{ text: 'Understood.' }],
    ]);
    await run.start();
    await answer(run, 0, 'approve');
    await run.next('runtime.turn.completed');
    // The folder that holds every launch's home was refused; this launch's own took the write, and goes at its end.
    assert.notEqual(readFileSync(join(run.project, 'status.txt'), 'utf8'), 'exit 0\n');
    assert.equal(existsSync(join(run.home, '.gemini', 'halora', 'antigravity-cli')), false);
    await run.adapter.close();
    assert.deepEqual(readdirSync(join(run.home, '.gemini', 'halora')), []);
    // A later launch, on a fresh home: its command still asks, and denied it never runs.
    const later = new AcpRuntimeAdapter(run.options);
    t.after(() => later.close());
    const observations: RuntimeObservation[] = [];
    await later.startExecution({
      execution: {
        ...TEST_EXECUTION,
        execution_id: '01920000-0000-7000-8000-000000000013' as never,
      },
      instruction: 'Do the work.',
      options: {},
      model_ref: null,
      directory: run.project,
      emit: (observation) => observations.push(observation),
    });
    for (
      let waited = 0;
      waited < 600 && !observations.some((o) => o.type === 'runtime.approval.requested');
      waited++
    )
      await delay(50);
    const asked = observations.find((o) => o.type === 'runtime.approval.requested');
    assert.ok(
      asked !== undefined && asked.type === 'runtime.approval.requested',
      'the later launch asked',
    );
    await later.respondToApproval({
      execution: {
        ...TEST_EXECUTION,
        execution_id: '01920000-0000-7000-8000-000000000013' as never,
      },
      approval_id: asked.payload.approval_id,
      decision: 'deny',
      message: null,
    });
    assert.equal(existsSync(join(run.project, 'made.txt')), false);
  });

  test('signed out, the start fails in words and nothing starts', async (t) => {
    const run = await setUp(t, [[{ text: 'Never.' }]], { signedIn: false });
    await assert.rejects(
      run.start(),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'sign_in_required' &&
        error.effect === 'none',
    );
    assert.deepEqual(run.observations, []);
    assert.equal(run.standIn.taken, 0);
  });
});

/** End to end against the real Kiro CLI 3.4.0 (the vendor's universal-darwin build), signed out. */
const KIRO_BINARY = process.env.HALCYONIC_KIRO_BIN ?? '';
const KIRO_SKIP =
  KIRO_BINARY === ''
    ? 'HALCYONIC_KIRO_BIN is not set; point it at the pinned Kiro CLI 3.4.0 to run these tests'
    : existsSync(KIRO_BINARY)
      ? false
      : `HALCYONIC_KIRO_BIN names no file: ${KIRO_BINARY}`;

describe('Kiro 3.4.0 end to end, signed out', { skip: KIRO_SKIP }, () => {
  test('launched as the control plane launches it, the start fails in words and nothing starts', async (t) => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-acp-kiro-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const project = join(root, 'projects', 'one');
    const home = join(root, 'home');
    mkdirSync(project, { recursive: true });
    mkdirSync(home);
    const adapter = new AcpRuntimeAdapter({
      profile: KIRO,
      binaryPath: KIRO_BINARY,
      directoryPolicy: allowOnly(join(root, 'projects')),
      // A key from the host is never passed: the start still reads as not signed in.
      env: {
        PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
        HOME: home,
        LANG: 'en_US.UTF-8',
        TMPDIR: `${root}/`,
        KIRO_API_KEY: 'halcyonic-stand-in-not-a-key',
      },
      home,
      processRecordFile: join(root, 'processes.json'),
      hostFolders:
        process.platform === 'darwin'
          ? {
              scope: { folders: [join(root, 'host-data')], home },
              profileFolder: join(root, 'profiles'),
            }
          : null,
    });
    t.after(() => adapter.close());
    const observations: RuntimeObservation[] = [];
    await assert.rejects(
      adapter.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: {},
        model_ref: null,
        directory: project,
        emit: (observation) => observations.push(observation),
      }),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'sign_in_required' &&
        error.effect === 'none',
    );
    assert.deepEqual(observations, []);
  });
});
