import assert from 'node:assert/strict';
import {
  cpSync,
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
import { dirname, join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { RuntimeActionError, type RuntimeObservation } from '@halcyonic/runtime-core';
import { AcpRuntimeAdapter } from './acp-runtime.ts';
import { ANTIGRAVITY, CURSOR, KIRO } from './profiles.ts';
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
  /** Where the start runs: the project, or with `inCopy` a task's own copy of it. */
  readonly directory: string;
  /** Another task's copy of the same project, with `inCopy`. */
  readonly sibling: string;
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
  options: {
    signedIn?: boolean;
    inCopy?: boolean;
    /** With `inCopy`: the other task's copy is recorded too, so both tasks may run at once. */
    bothCopies?: boolean;
  } = {},
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
  // As the host makes them (ADR 0045): each task's own copy of the project, in the task copies' folder.
  const tasks = join(root, 'tasks');
  const copy = join(tasks, 'p', 'a', 'one');
  const sibling = join(tasks, 'p', 'b', 'one');
  if (options.inCopy === true) {
    writeFileSync(join(project, 'marker.txt'), 'halcyonic-original-marker\n');
    mkdirSync(dirname(copy), { recursive: true });
    cpSync(project, copy, { recursive: true });
    mkdirSync(dirname(sibling), { recursive: true });
    cpSync(project, sibling, { recursive: true });
    writeFileSync(join(sibling, 'marker.txt'), 'halcyonic-sibling-marker\n');
  }
  const directory = options.inCopy === true ? copy : project;
  const standIn = await StandInGemini.start(
    typeof replies === 'function' ? replies(host) : replies,
  );
  t.after(() => standIn.close());
  const made: ConstructorParameters<typeof AcpRuntimeAdapter>[0] = {
    profile: ANTIGRAVITY,
    binaryPath: BINARY,
    directoryPolicy: allowOnly(options.inCopy === true ? root : join(root, 'projects')),
    env: {
      PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
      HOME: home,
      LANG: 'en_US.UTF-8',
      TMPDIR: `${join(root, 'tmp')}/`,
      // As a pass-through variable could bring it: the SSH agent's address, never handed on in a copy.
      ...(options.inCopy === true ? { SSH_AUTH_SOCK: join(root, 'agent.sock') } : {}),
    },
    home,
    processRecordFile: join(root, 'processes.json'),
    ...(options.inCopy === true
      ? {
          copyOriginOf: (folder: string) =>
            folder === copy || (options.bothCopies === true && folder === sibling) ? project : null,
        }
      : {}),
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
        ? {
            scope: {
              folders: [host, ...(options.inCopy === true ? [tasks] : [])],
              home,
            },
            profileFolder: join(root, 'profiles'),
          }
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
    directory,
    sibling,
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
        directory,
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
    assert.equal(asked.payload.subject.summary, "[in the task's folder] echo made > made.txt");
    assert.deepEqual(asked.payload.subject.command, {
      text: 'echo made > made.txt',
      runs_in: { kind: 'task_folder' },
      background: false,
      input_to_running: false,
    });
    await run.next('runtime.turn.completed');
    assert.equal(readFileSync(join(run.project, 'made.txt'), 'utf8'), 'made\n');
    const messages = run.observations
      .filter((o) => o.type === 'runtime.agent_message')
      .map((o) => o.payload);
    assert.deepEqual(messages, [{ text: 'Made it.' }]);
    assertValidObservations(run.observations);
  });

  test("each command starts in the task's folder: a cd in one reaches no later one, so its asks say the task's folder", async (t) => {
    const run = await setUp(t, [
      command('mkdir -p sub && cd sub && pwd -P > first.txt'),
      command('pwd -P > where.txt'),
      [{ text: 'Done.' }],
    ]);
    await run.start();
    await answer(run, 0, 'approve');
    const second = await answer(run, 1, 'approve');
    if (second.type !== 'runtime.approval.requested') return;
    assert.deepEqual(second.payload.subject.command?.runs_in, { kind: 'task_folder' });
    await run.next('runtime.turn.completed');
    assert.equal(
      readFileSync(join(run.project, 'sub', 'first.txt'), 'utf8'),
      `${join(run.project, 'sub')}\n`,
    );
    assert.equal(readFileSync(join(run.project, 'where.txt'), 'utf8'), `${run.project}\n`);
  });

  test('lists its models from its own session answer, and a start on one of them runs its turn on it', async (t) => {
    const run = await setUp(t, [[{ text: 'Done.' }]]);
    const listed = (await run.adapter.listModels?.()) ?? [];
    assert.ok(listed.length > 0, 'it lists models');
    for (const model of listed) {
      assert.match(model.display_name, / \(Antigravity\)$/);
      // The stand-in serves them on this Mac's loopback.
      assert.equal(model.served, 'this_mac');
    }
    assert.equal(run.standIn.models.length, 0, 'listing asks the model nothing');
    const chosen = listed.at(-1);
    assert.ok(chosen !== undefined);
    await run.adapter.startExecution({
      execution: TEST_EXECUTION,
      instruction: 'Say done.',
      options: {},
      model_ref: chosen.model_ref,
      directory: run.project,
      emit: (observation) => run.observations.push(observation),
    });
    await run.next('runtime.turn.completed');
    // One model call: 100 input, 40 of it from the cache, 10 written and 5 more thinking, as the stand-in served it.
    const used = run.observations.find((o) => o.type === 'runtime.usage.reported');
    assert.deepEqual((used?.payload as { tokens?: unknown } | undefined)?.tokens, {
      input: 60,
      cache_read: 40,
      cache_write: 0,
      output: 15,
      reasoning: 5,
    });
    assert.ok(
      run.standIn.models.some((model) => model.startsWith(chosen.model_ref)),
      `the turn asked ${chosen.model_ref}: ${run.standIn.models.join(', ')}`,
    );
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

  test("in a task's own copy: it writes the copy alone, reads the project, and never another task's copy (ADR 0045)", {
    skip: process.platform !== 'darwin' && 'the agent-app profile is macOS only',
  }, async (t) => {
    const run = await setUp(
      t,
      (_host) => [
        command(
          [
            'echo made > made.txt',
            'cat ../../../../projects/one/marker.txt > read-original.txt',
            'echo planted > ../../../../projects/one/planted.txt; echo "exit $?" > status-original.txt',
            'echo planted > ../../b/one/planted.txt; echo "exit $?" > status-sibling.txt',
            'cat ../../b/one/marker.txt > read-sibling.txt; echo "exit $?" > status-sibling-read.txt',
            'mkdir -p .agents && echo planted > .agents/hooks.json; echo "exit $?" > status-settings.txt',
            'printenv SSH_AUTH_SOCK > sock.txt || echo none > sock.txt',
          ].join('; '),
        ),
        [{ text: 'Done.' }],
      ],
      { inCopy: true },
    );
    assert.equal(run.adapter.holdsFolder(run.directory), true);
    assert.equal(run.adapter.holdsFolder(run.project), true);
    assert.equal(run.adapter.holdsFolder(run.sibling), false);
    await run.start();
    await answer(run, 0, 'approve');
    await run.next('runtime.turn.completed');
    const inCopy = (name: string) => readFileSync(join(run.directory, name), 'utf8');
    // The copy is written, and the project it was made from read.
    assert.equal(inCopy('made.txt'), 'made\n');
    assert.equal(inCopy('read-original.txt'), 'halcyonic-original-marker\n');
    // The project is never written, another task's copy neither read nor written.
    assert.notEqual(inCopy('status-original.txt'), 'exit 0\n');
    assert.equal(existsSync(join(run.project, 'planted.txt')), false);
    assert.equal(existsSync(join(run.project, 'made.txt')), false);
    assert.notEqual(inCopy('status-sibling.txt'), 'exit 0\n');
    assert.equal(existsSync(join(run.sibling, 'planted.txt')), false);
    assert.notEqual(inCopy('status-sibling-read.txt'), 'exit 0\n');
    assert.equal(inCopy('read-sibling.txt'), '');
    // The app's own settings stay refused inside the copy, and the SSH agent's address is not handed on.
    assert.notEqual(inCopy('status-settings.txt'), 'exit 0\n');
    assert.equal(existsSync(join(run.directory, '.agents', 'hooks.json')), false);
    assert.equal(inCopy('sock.txt'), 'none\n');
  });

  test('two tasks of one project at once, each in its own copy: each writes its own, neither reaches the other (AUDIT3 C6b)', {
    skip: process.platform !== 'darwin' && 'the agent-app profile is macOS only',
  }, async (t) => {
    const each = command(
      [
        'pwd -P > who.txt',
        'case "$(pwd -P)" in */a/one) o=../../b/one ;; *) o=../../a/one ;; esac',
        'echo planted > "$o/planted.txt"; echo "exit $?" > status-other.txt',
        'cat "$o/marker.txt" > read-other.txt',
      ].join('; '),
    );
    // Served in order: the first task's command, then the second's, then each one's last word.
    const run = await setUp(t, [each, each, [{ text: 'Done.' }], [{ text: 'Done.' }]], {
      inCopy: true,
      bothCopies: true,
    });
    assert.equal(run.adapter.holdsFolder(run.directory), true);
    assert.equal(run.adapter.holdsFolder(run.sibling), true);
    await run.start();
    const first = await run.next('runtime.approval.requested');
    // The second task starts while the first waits on its ask: both run at once.
    const second = {
      ...TEST_EXECUTION,
      execution_id: '01920000-0000-7000-8000-000000000021' as never,
    };
    const observations: RuntimeObservation[] = [];
    await run.adapter.startExecution({
      execution: second,
      instruction: 'Do the work.',
      options: {},
      model_ref: null,
      directory: run.sibling,
      emit: (observation) => observations.push(observation),
    });
    const seen = async (type: RuntimeObservation['type']) => {
      for (let waited = 0; waited < 1200; waited++) {
        const found = observations.find((observation) => observation.type === type);
        if (found !== undefined) return found;
        await delay(50);
      }
      assert.fail(`no ${type} for the second task`);
    };
    const asked = await seen('runtime.approval.requested');
    assert.ok(
      first.type === 'runtime.approval.requested' && asked.type === 'runtime.approval.requested',
    );
    for (const [execution, approval] of [
      [TEST_EXECUTION, first],
      [second, asked],
    ] as const) {
      if (approval.type !== 'runtime.approval.requested') throw new Error('not an approval');
      await run.adapter.respondToApproval({
        execution,
        approval_id: approval.payload.approval_id,
        decision: 'approve',
        message: null,
      });
    }
    await run.next('runtime.turn.completed');
    await seen('runtime.turn.completed');
    for (const folder of [run.directory, run.sibling]) {
      const inside = (name: string) => readFileSync(join(folder, name), 'utf8');
      assert.equal(inside('who.txt'), `${folder}\n`, 'each command ran in its own copy');
      assert.notEqual(inside('status-other.txt'), 'exit 0\n');
      assert.equal(inside('read-other.txt'), '');
      assert.equal(existsSync(join(folder, 'planted.txt')), false);
    }
    assert.equal(existsSync(join(run.project, 'who.txt')), false, 'the project waits');
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

/** End to end against the real Cursor agent CLI 2026.11.13-eacade1 (the vendor's darwin-arm64 build), signed out. */
const CURSOR_BINARY = process.env.HALCYONIC_CURSOR_BIN ?? '';
const CURSOR_SKIP =
  CURSOR_BINARY === ''
    ? 'HALCYONIC_CURSOR_BIN is not set; point it at the pinned Cursor agent CLI 2026.11.13-eacade1 to run these tests'
    : existsSync(CURSOR_BINARY)
      ? false
      : `HALCYONIC_CURSOR_BIN names no file: ${CURSOR_BINARY}`;

describe('Cursor 2026.11.13 end to end, signed out', { skip: CURSOR_SKIP }, () => {
  test('launched as the control plane launches it, the start fails in words and nothing starts', async (t) => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-acp-cursor-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const project = join(root, 'projects', 'one');
    const home = join(root, 'home');
    mkdirSync(project, { recursive: true });
    mkdirSync(home);
    const adapter = new AcpRuntimeAdapter({
      profile: CURSOR,
      binaryPath: CURSOR_BINARY,
      directoryPolicy: allowOnly(join(root, 'projects')),
      // A key from the host is never passed: the start still reads as not signed in.
      env: {
        PATH: '/usr/bin:/bin:/usr/sbin:/sbin',
        HOME: home,
        LANG: 'en_US.UTF-8',
        TMPDIR: `${root}/`,
        CURSOR_API_KEY: 'halcyonic-stand-in-not-a-key',
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
