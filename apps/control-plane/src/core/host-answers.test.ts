import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  ExecutionId,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import type { RuntimeAdapter } from '@halcyonic/runtime-core';
import { createTestControlPlane } from '../testing/harness.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import { judgeRequest } from './host-answers.ts';

const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-host-answers-')));
after(() => rmSync(root, { recursive: true, force: true }));
const copy = join(root, 'tidewatch-vr');
const original = join(root, 'tidewatch');
const shared = join(root, 'starters', 'iwsdk-vr');
const outside = join(root, 'elsewhere');
for (const folder of [copy, join(copy, 'src'), original, shared, outside]) {
  mkdirSync(folder, { recursive: true });
}
// A link inside the copy that leads out of it.
symlinkSync(outside, join(copy, 'out'));
const reads = { readable: [shared], originals: [original] };

const claude = (toolName: string, paths?: { path: string; access: 'read' | 'write' }[]) => ({
  runtimeKind: 'claude-agent',
  toolName,
  paths,
  complete: true,
});

describe("the host's answer in a confined app (judgeRequest)", () => {
  test('work inside its own copy goes ahead, relative or absolute, read or write', () => {
    for (const request of [
      claude('Write', [{ path: join(copy, 'src/index.ts'), access: 'write' }]),
      claude('Edit', [{ path: 'src/index.ts', access: 'write' }]),
      claude('NotebookEdit', [{ path: 'notes.ipynb', access: 'write' }]),
      claude('Read', [{ path: join(copy, 'package.json'), access: 'read' }]),
      claude('Glob', [{ path: '.', access: 'read' }]),
      claude('Grep', [{ path: join(copy, 'src'), access: 'read' }]),
    ]) {
      assert.equal(judgeRequest(request, copy, reads)?.because, 'inside_copy', request.toolName);
    }
  });

  test('a read of the declared original or the starter goes ahead; a write there asks', () => {
    assert.equal(
      judgeRequest(
        claude('Read', [{ path: join(original, 'src/app.ts'), access: 'read' }]),
        copy,
        reads,
      )?.because,
      'reads_original',
    );
    assert.equal(
      judgeRequest(claude('Grep', [{ path: shared, access: 'read' }]), copy, reads)?.because,
      'reads_original',
    );
    assert.equal(
      judgeRequest(
        claude('Write', [{ path: join(original, 'x.ts'), access: 'write' }]),
        copy,
        reads,
      ),
      null,
    );
    // Its version control asks, in any case (AUDIT3 S2).
    for (const path of ['.git/config', '.GIT/HEAD', 'src/../.git/hooks/x'])
      assert.equal(
        judgeRequest(claude('Read', [{ path: join(original, path), access: 'read' }]), copy, reads),
        null,
        path,
      );
  });

  test('anything outside, through a link out, climbing out, or running later asks', () => {
    for (const request of [
      claude('Read', [{ path: join(outside, 'secret.txt'), access: 'read' }]),
      claude('Read', [{ path: 'out/secret.txt', access: 'read' }]),
      claude('Write', [{ path: '../elsewhere/x', access: 'write' }]),
      claude('Read', [{ path: join(root, '..', 'etc', 'hosts'), access: 'read' }]),
      claude('Write', [{ path: '.git/config', access: 'write' }]),
      claude('Write', [{ path: '.envrc', access: 'write' }]),
      claude('Edit', [{ path: '.vscode/tasks.json', access: 'write' }]),
      // The Mac's disk folds case (AUDIT3 S1).
      claude('Write', [{ path: '.GIT/config', access: 'write' }]),
      claude('Write', [{ path: '.Git/hooks/pre-commit', access: 'write' }]),
      claude('Write', [{ path: '.ENVRC', access: 'write' }]),
      // One place inside and one outside asks.
      claude('Read', [
        { path: 'src/index.ts', access: 'read' },
        { path: join(outside, 'x'), access: 'read' },
      ]),
    ]) {
      assert.equal(judgeRequest(request, copy, reads), null, JSON.stringify(request.paths));
    }
  });

  test('a file tool with no places, an incomplete request, an unknown tool or runtime asks', () => {
    assert.equal(judgeRequest(claude('Read'), copy, reads), null);
    assert.equal(judgeRequest(claude('Read', []), copy, reads), null);
    assert.equal(
      judgeRequest(
        { ...claude('Write', [{ path: 'a.ts', access: 'write' }]), complete: false },
        copy,
        reads,
      ),
      null,
    );
    for (const tool of ['WebFetch', 'WebSearch', 'mcp__files__write', 'Task', 'shell']) {
      assert.equal(
        judgeRequest(claude(tool, [{ path: 'a.ts', access: 'write' }]), copy, reads),
        null,
        tool,
      );
    }
    for (const kind of ['codex', 'mock', 'constructor', 'toString']) {
      assert.equal(judgeRequest({ ...claude('Bash'), runtimeKind: kind }, copy, reads), null, kind);
    }
  });

  test("a shell command is the closed profile's to hold, whole, on each runtime's own tool", () => {
    assert.deepEqual(judgeRequest(claude('Bash'), copy, reads), {
      because: 'confined_command',
      paths: [],
    });
    assert.equal(
      judgeRequest({ ...claude('shell'), runtimeKind: 'opencode' }, copy, reads)?.because,
      'confined_command',
    );
    // OpenCode's file actions are its own session's rules, never the host's.
    assert.equal(
      judgeRequest(
        { ...claude('edit', [{ path: 'a.ts', access: 'write' }]), runtimeKind: 'opencode' },
        copy,
        reads,
      ),
      null,
    );
  });

  test("a read inside the run's own temporary folder goes ahead; anything wider, or a write there, asks (AUDIT3 T1 to T5)", () => {
    const data = join(root, 'data');
    const sandbox = join(data, 'claude-sandbox-0123456789abcdef');
    const temp = join(sandbox, 'tmp');
    const otherTemp = join(data, 'claude-sandbox-fedcba9876543210', 'tmp');
    for (const folder of [temp, join(sandbox, 'home'), otherTemp])
      mkdirSync(folder, { recursive: true });
    // A link in the run's temporary folder that leads to the data folder.
    symlinkSync(data, join(temp, 'up'));
    const withTemp = { ...reads, runTemp: { 'claude-agent': temp } };
    const read = (path: string) => claude('Read', [{ path, access: 'read' }]);
    assert.equal(
      judgeRequest(read(join(temp, 'frame.jpg')), copy, withTemp)?.because,
      'run_temp_read',
    );
    for (const [path, why] of [
      [join(temp, 'up', 'access-token'), 'through a link to the data folder'],
      [join(sandbox, 'home', '.claude', 'settings.json'), 'a sibling of the temporary folder'],
      [join(otherTemp, 'frame.jpg'), "another folder's run"],
      [temp, 'the folder itself, not inside it'],
    ] as const) {
      assert.equal(judgeRequest(read(path), copy, withTemp), null, why);
    }
    assert.equal(
      judgeRequest(
        claude('Write', [{ path: join(temp, 'frame.jpg'), access: 'write' }]),
        copy,
        withTemp,
      ),
      null,
      'a write there follows the usual rule',
    );
    assert.equal(
      judgeRequest(read(join(temp, 'frame.jpg')), copy, reads),
      null,
      'no recorded folder, no answer',
    );
    // The run removes its own temporary folder and puts a link to the data folder in its place (LOOP, 04:39Z).
    const swapped = join(data, 'claude-sandbox-00000000aaaaaaaa', 'tmp');
    mkdirSync(join(swapped, '..'), { recursive: true });
    symlinkSync(data, swapped);
    assert.equal(
      judgeRequest(read(join(swapped, 'access-token')), copy, {
        ...reads,
        runTemp: { 'claude-agent': swapped },
      }),
      null,
      'a temporary folder that is now a link answers nothing',
    );
    assert.equal(
      judgeRequest({ ...read(join(temp, 'frame.jpg')), runtimeKind: 'opencode' }, copy, withTemp),
      null,
      "another runtime's request is not this run's temporary folder",
    );
    assert.equal(
      judgeRequest(
        claude('Read', [
          { path: join(temp, 'frame.jpg'), access: 'read' },
          { path: join(copy, 'package.json'), access: 'read' },
        ]),
        copy,
        withTemp,
      ),
      null,
      'one reason a request: the temporary folder with the copy stays asked',
    );
  });

  test('the record holds the places as resolved', () => {
    assert.deepEqual(
      judgeRequest(claude('Edit', [{ path: 'src/../src/index.ts', access: 'write' }]), copy, reads)
        ?.paths,
      [{ path: join(copy, 'src', 'index.ts'), access: 'write' }],
    );
  });
});

describe("a task's copy of a folder the person connected (AUDIT3 H1 to H5, the lead's 15:17Z ruling)", () => {
  const connected = { readable: [], connectedCopy: true } as const;
  const codex = (command?: unknown) => ({
    runtimeKind: 'codex',
    toolName: 'commandExecution',
    paths: undefined,
    complete: true,
    command: command as never,
  });
  const inCopy = {
    text: 'npx tsc',
    runs_in: { kind: 'folder', path: copy },
    background: false,
    input_to_running: false,
  };

  test("under Claude Code's copy profile every request still asks; under the closed profile a command goes ahead", () => {
    assert.equal(judgeRequest(claude('Bash'), copy, connected), null);
    assert.equal(
      judgeRequest(claude('Write', [{ path: 'src/a.ts', access: 'write' }]), copy, connected),
      null,
    );
    assert.equal(
      judgeRequest({ ...claude('shell'), runtimeKind: 'opencode' }, copy, connected)?.because,
      'confined_command',
    );
    assert.equal(judgeRequest(codex(inCopy), copy, connected)?.because, 'confined_command');
  });

  test("Codex's command goes ahead only from its parts: no network asked, run in a folder inside the copy", () => {
    assert.equal(judgeRequest(codex(), copy, connected), null);
    assert.equal(
      judgeRequest(
        codex({ ...inCopy, network: { host: 'example.com', protocol: 'https' } }),
        copy,
        connected,
      ),
      null,
    );
    assert.equal(
      judgeRequest(codex({ ...inCopy, runs_in: { kind: 'unknown' } }), copy, connected),
      null,
    );
    assert.equal(
      judgeRequest(
        codex({ ...inCopy, runs_in: { kind: 'folder', path: outside } }),
        copy,
        connected,
      ),
      null,
    );
    // Through a link that leads out of the copy.
    assert.equal(
      judgeRequest(
        codex({ ...inCopy, runs_in: { kind: 'folder', path: join(copy, 'out') } }),
        copy,
        connected,
      ),
      null,
    );
    assert.equal(
      judgeRequest(codex({ ...inCopy, runs_in: { kind: 'folder', path: 'src' } }), copy, connected)
        ?.because,
      'confined_command',
    );
  });
});

describe("the host's answers in the pipeline", () => {
  /** An agent app that can be sent decisions, as Claude Agent can. */
  function answeringRuntime(respondToApproval = true) {
    const descriptor: RuntimeDescriptor = {
      runtime_id: 'claude-agent' as RuntimeId,
      kind: 'claude-agent',
      display_name: 'Claude Agent',
      synthetic: true,
      capabilities: {
        start_execution: true,
        instruct_at_rest: false,
        instruct_while_running: false,
        respond_to_approval: respondToApproval,
        answer_question: false,
        interrupt: false,
      },
      model_choice: 'none',
      uses_project_location: false,
      reports_tool_activity: false,
    };
    const adapter: RuntimeAdapter = {
      descriptor,
      validateStartOptions: () => ({ ok: true }),
      startExecution: async () => ({ native_id: 'n' }),
      respondToApproval: async () => {},
      close: async () => {},
    };
    return adapter;
  }

  type Extra = { approval_id: string; length: number; write?: string };
  async function confinedRun(confined: boolean, respondToApproval = true, extra: Extra[] = []) {
    const harness = createTestControlPlane({
      adapters: () => [answeringRuntime(respondToApproval)],
      confinedReads: (directory) => (directory === copy ? reads : null),
    });
    const { controlPlane, commands, time } = harness;
    const project = controlPlane.commands.submit(
      commands.createProject('Tidewatch VR'),
      'internal',
    );
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const projectId = project.command.result.project_id as ProjectId;
    const now = '2026-09-26T10:00:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000b9' as WorkstreamId;
    const executionId = '01a19000-0000-7000-8000-0000000000c9' as ExecutionId;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { ...scope, execution_id: null },
        { title: 'Make VR Version', objective: null },
        now,
        NO_CAUSE,
      ),
    );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.created',
        scope,
        {
          runtime: {
            runtime_id: 'claude-agent',
            kind: 'claude-agent',
            display_name: 'Claude Agent',
            synthetic: true,
          },
          instruction: 'Make it.',
          directory: copy,
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    const requests = [
      { approval_id: 'a1', tool: 'Write', paths: [{ path: 'src/index.ts', access: 'write' }] },
      {
        approval_id: 'a2',
        tool: 'Read',
        paths: [{ path: join(original, 'a.ts'), access: 'read' }],
      },
      {
        approval_id: 'a3',
        tool: 'Read',
        paths: [{ path: join(outside, 'id_rsa'), access: 'read' }],
      },
      { approval_id: 'a4', tool: 'Bash', paths: undefined },
      // Commands too long for a person to approve, as the control plane marks them (AUDIT3 M1).
      ...extra.map((each) => ({
        approval_id: each.approval_id,
        tool: each.write === undefined ? 'Bash' : 'Write',
        paths:
          each.write === undefined ? undefined : [{ path: each.write, access: 'write' as const }],
        tooLong: each.length,
      })),
    ];
    const observations: [string, unknown][] = [
      ['runtime.execution.started', { native_id: 'n', confined }],
      ['runtime.turn.started', { turn_id: 't1' }],
      ...requests.map(
        (each) =>
          [
            'runtime.approval.requested',
            {
              approval_id: each.approval_id,
              subject: {
                kind: 'tool_use',
                tool_name: each.tool,
                summary:
                  each.tool === 'Bash'
                    ? "[in its shell, which started in the task's folder] npx tsc"
                    : each.tool,
                ...(each.paths !== undefined && { paths: each.paths }),
              },
              complete: !('tooLong' in each),
              ...('tooLong' in each && {
                too_long: { length: each.tooLong, reported_complete: true },
              }),
            },
          ] as [string, unknown],
      ),
    ];
    let sequence = 0;
    for (const [eventType, payload] of observations) {
      sequence += 1;
      controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'claude-agent' },
        source_native_id: `n:${sequence}`,
        sequence,
        payload,
      } as never);
    }
    await time.runUntilIdle();
    const events = [...controlPlane.journal.readAll()].map((stored) => stored.event);
    const answered = events.filter(
      (event) => event.event_type === 'execution.approval.answered_by_host',
    );
    // The decisions the host sent, by the same command a press sends, in the order recorded.
    const sent = events.flatMap((event) => {
      if (event.event_type !== 'command.accepted') return [];
      const { command } = event.payload;
      if (command.command_type !== 'execution.respond_to_approval') return [];
      const { approval_id, decision } = command.payload;
      return [[approval_id, decision, events.indexOf(event)] as const];
    });
    const view = controlPlane.projection.execution(executionId);
    await controlPlane.close();
    return { events, answered, sent, view };
  }

  test('in a confined run, work in the copy, a read of the original and a command go ahead, recorded first; a read outside still asks', async () => {
    const { events, answered, sent, view } = await confinedRun(true);
    assert.deepEqual(
      answered.map((event) => [
        (event.payload as { approval_id: string }).approval_id,
        (event.payload as { because: string }).because,
      ]),
      [
        ['a1', 'inside_copy'],
        ['a2', 'reads_original'],
        ['a4', 'confined_command'],
      ],
    );
    const first = answered[0];
    assert.ok(first?.event_type === 'execution.approval.answered_by_host');
    assert.deepEqual(first.payload.paths, [
      { path: join(copy, 'src', 'index.ts'), access: 'write' },
    ]);
    assert.deepEqual(
      sent.map(([approvalId, decision]) => [approvalId, decision]),
      [
        ['a1', 'approve'],
        ['a2', 'approve'],
        ['a4', 'approve'],
      ],
    );
    // Each answer is recorded before its decision is sent.
    for (const [index, event] of answered.entries()) {
      assert.ok(events.indexOf(event) < (sent[index]?.[2] ?? -1));
    }
    // Only the read outside is a card; the counts say three went ahead and one asked.
    assert.deepEqual(
      view?.pending_approvals.map((each) => each.approval_id),
      ['a3'],
    );
    assert.deepEqual(view?.approvals, { went_ahead: 3, asked: 1 });
  });

  test('an answer the runtime cannot be sent is withdrawn, and the request is a card after all (AUDIT3 S3)', async () => {
    const { events, answered, sent, view } = await confinedRun(true, false);
    assert.equal(answered.length, 3);
    assert.equal(sent.length, 0);
    assert.deepEqual(
      events
        .filter((event) => event.event_type === 'execution.approval.host_answer_withdrawn')
        .map((event) => (event.payload as { approval_id: string }).approval_id),
      ['a1', 'a2', 'a4'],
    );
    assert.deepEqual(
      view?.pending_approvals.map((each) => each.approval_id),
      ['a1', 'a2', 'a3', 'a4'],
    );
    assert.deepEqual(view?.approvals, { went_ahead: 0, asked: 4 });
  });

  test('a confined command too long for a person is answered by the host, whole, the cut said beyond the record (AUDIT3 M1)', async () => {
    const { events, answered, sent } = await confinedRun(true, true, [
      { approval_id: 'a5', length: 5000 },
      { approval_id: 'a6', length: 40_000 },
    ]);
    const byId = (id: string) =>
      answered.find(
        (event) =>
          event.event_type === 'execution.approval.answered_by_host' &&
          event.payload.approval_id === id,
      );
    const five = byId('a5');
    const forty = byId('a6');
    assert.ok(five?.event_type === 'execution.approval.answered_by_host');
    assert.equal(five.payload.because, 'confined_command');
    assert.equal(five.payload.cut, undefined, 'kept whole in the record');
    assert.ok(forty?.event_type === 'execution.approval.answered_by_host');
    assert.deepEqual(forty.payload.cut, { length: 40_000 });
    // Approved by the same command a press sends, and never denied for length.
    assert.ok(sent.some(([id, decision]) => id === 'a5' && decision === 'approve'));
    assert.ok(sent.some(([id, decision]) => id === 'a6' && decision === 'approve'));
    assert.equal(
      events.filter((event) => event.event_type === 'execution.approval.denied_by_host').length,
      0,
    );
  });

  test('a confined Write of 10000 characters, its content now carried, is answered by the host, never denied (AUDIT3 M1)', async () => {
    const { events, answered, sent } = await confinedRun(true, true, [
      { approval_id: 'w1', length: 10_000, write: 'src/scene.ts' },
    ]);
    const written = answered.find(
      (event) =>
        event.event_type === 'execution.approval.answered_by_host' &&
        event.payload.approval_id === 'w1',
    );
    assert.ok(written?.event_type === 'execution.approval.answered_by_host');
    assert.equal(written.payload.because, 'inside_copy');
    assert.ok(sent.some(([id, decision]) => id === 'w1' && decision === 'approve'));
    assert.equal(
      events.filter((event) => event.event_type === 'execution.approval.denied_by_host').length,
      0,
    );
  });

  test('the same run not recorded confined asks for all four', async () => {
    const { answered, sent, view } = await confinedRun(false);
    assert.equal(answered.length, 0);
    assert.equal(sent.length, 0);
    assert.deepEqual(
      view?.pending_approvals.map((each) => each.approval_id),
      ['a1', 'a2', 'a3', 'a4'],
    );
    assert.deepEqual(view?.approvals, { went_ahead: 0, asked: 4 });
  });
});

describe("the host's answers in a connected folder's copy, in the pipeline (AUDIT3 H1, H4)", () => {
  async function connectedRun(ownCopy: boolean) {
    const descriptor: RuntimeDescriptor = {
      runtime_id: 'opencode' as RuntimeId,
      kind: 'opencode',
      display_name: 'OpenCode',
      synthetic: true,
      capabilities: {
        start_execution: true,
        instruct_at_rest: false,
        instruct_while_running: false,
        respond_to_approval: true,
        answer_question: false,
        interrupt: false,
      },
      model_choice: 'none',
      uses_project_location: false,
      reports_tool_activity: false,
    };
    const adapter: RuntimeAdapter = {
      descriptor,
      validateStartOptions: () => ({ ok: true }),
      startExecution: async () => ({ native_id: 'n' }),
      respondToApproval: async () => {},
      close: async () => {},
    };
    const harness = createTestControlPlane({
      adapters: () => [adapter],
      confinedReads: (directory) =>
        directory === copy ? { readable: [], connectedCopy: true } : null,
    });
    const { controlPlane, commands, time } = harness;
    const project = controlPlane.commands.submit(commands.createProject('Tidewatch'), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const projectId = project.command.result.project_id as ProjectId;
    const now = '2026-09-26T10:00:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000d9' as WorkstreamId;
    const executionId = '01a19000-0000-7000-8000-0000000000e9' as ExecutionId;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    const where = { ...scope, execution_id: null };
    controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        where,
        { title: 'Fix it', objective: null },
        now,
        NO_CAUSE,
      ),
    );
    if (ownCopy)
      controlPlane.recorder.record(
        controlPlaneDraft(
          'workstream.copy_made',
          where,
          { directory: copy, base_tree: 'a'.repeat(64), files: 1 } as never,
          now,
          NO_CAUSE,
        ),
      );
    controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.created',
        scope,
        {
          runtime: {
            runtime_id: 'opencode',
            kind: 'opencode',
            display_name: 'OpenCode',
            synthetic: true,
          },
          instruction: 'Fix it.',
          directory: copy,
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    const observations: [string, unknown][] = [
      ['runtime.execution.started', { native_id: 'n', confined: true }],
      ['runtime.turn.started', { turn_id: 't1' }],
      [
        'runtime.approval.requested',
        {
          approval_id: 'b1',
          subject: {
            kind: 'tool_use',
            tool_name: 'shell',
            summary: "[in the task's folder] git status",
          },
          complete: true,
        },
      ],
    ];
    let sequence = 0;
    for (const [eventType, payload] of observations) {
      sequence += 1;
      controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'opencode' },
        source_native_id: `n:${sequence}`,
        sequence,
        payload,
      } as never);
    }
    await time.runUntilIdle();
    const answered = [...controlPlane.journal.readAll()]
      .map((stored) => stored.event)
      .filter((event) => event.event_type === 'execution.approval.answered_by_host');
    const pending = controlPlane.projection
      .execution(executionId)
      ?.pending_approvals.map((each) => each.approval_id);
    await controlPlane.close();
    return { answered, pending };
  }

  test("a command in the task's own copy under the closed profile goes ahead, recorded; with no copy of its own it asks", async () => {
    const own = await connectedRun(true);
    assert.deepEqual(
      own.answered.map((event) => (event.payload as { because: string }).because),
      ['confined_command'],
    );
    assert.deepEqual(own.pending, []);
    const none = await connectedRun(false);
    assert.equal(none.answered.length, 0);
    assert.deepEqual(none.pending, ['b1']);
  });
});
