import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  type CommandEnvelope,
  compileValidator,
  type EntityChanges,
  type EventEnvelope,
  type RuntimeDescriptor,
  ServerMessage,
} from '@halcyonic/contracts';
import { admitCommand, Projection } from '@halcyonic/domain';
import { loadScenarios } from '@halcyonic/integration-mock';
import { RealtimeClient } from '../client/realtime-client.ts';
import { startTestServer, TEST_CLIENT } from '../testing/harness.ts';
import {
  AT_REST_HOLD_MS,
  DEMONSTRATION_FILE,
  DEMONSTRATION_PLAN,
  DEMONSTRATION_SCENARIOS,
  type Demonstration,
  type DemonstrationAnswer,
  type DemonstrationNode,
  DIRECTED_RUNTIME_ID,
  DIRECTED_RUNTIME_NAME,
  demonstrationPrologue,
  END_HOLD_MS,
  recordDemonstration,
  WATCHED_RUNTIME_ID,
  WATCHED_RUNTIME_NAME,
} from './demonstration.ts';

const SCENARIOS = loadScenarios(fileURLToPath(DEMONSTRATION_SCENARIOS));
const COMMITTED = readFileSync(DEMONSTRATION_FILE, 'utf8');
const DEMONSTRATION = JSON.parse(COMMITTED) as Demonstration;
const PROLOGUE = await demonstrationPrologue(SCENARIOS);
const validateServerMessage = compileValidator(ServerMessage);

/** One way through the tree: the nodes from the beginning, and the events played on the way. */
interface Path {
  readonly nodes: readonly number[];
  readonly events: readonly {
    readonly position: number;
    readonly event: EventEnvelope;
    readonly changes: EntityChanges;
  }[];
}

/** Every path from the beginning to each node, with the events of the nodes before it up to its answer. */
function paths(): Path[] {
  const found: Path[] = [];
  const walk = (index: number, nodes: number[], before: Path['events']) => {
    const node = DEMONSTRATION.nodes[index] as DemonstrationNode;
    const events = [...before, ...node.events.map(({ message }) => message)];
    found.push({ nodes: [...nodes, index], events });
    for (const { after, node: child } of node.answers) {
      walk(
        child,
        [...nodes, index],
        [...before, ...node.events.slice(0, after).map(({ message }) => message)],
      );
    }
  };
  walk(0, [], []);
  return found;
}

/** The entities a client holds, keyed by id, once it applied a snapshot and then each change. */
function held(sources: readonly EntityChanges[]) {
  const state = {
    projects: new Map<string, unknown>(),
    workstreams: new Map<string, unknown>(),
    executions: new Map<string, unknown>(),
    commands: new Map<string, unknown>(),
  };
  for (const source of sources) {
    for (const project of source.projects) state.projects.set(project.project_id, project);
    for (const workstream of source.workstreams) {
      state.workstreams.set(workstream.workstream_id, workstream);
    }
    for (const execution of source.executions) {
      state.executions.set(execution.execution_id, execution);
    }
    for (const command of source.commands) state.commands.set(command.command_id, command);
  }
  return state;
}

/**
 * What a workspace would offer after the events so far, asked of the control plane's own admission
 * with only the runtimes the recording lists, as a client sees them. Instructions are one action,
 * whatever their text.
 */
function offered(projection: Projection, runtimes: readonly RuntimeDescriptor[]): string[] {
  const catalog = { get: (id: string) => runtimes.find((runtime) => runtime.runtime_id === id) };
  const probe = (command_type: string, payload: object) =>
    admitCommand(
      {
        schema_version: 1,
        command_id: '00000000-0000-4000-8000-000000000000',
        issued_at: '2026-09-29T09:00:00.000Z',
        client: TEST_CLIENT,
        command_type,
        payload,
      } as CommandEnvelope,
      projection,
      catalog,
    ).admitted;
  const keys: string[] = [];
  for (const execution of projection.executions()) {
    const execution_id = execution.execution_id;
    for (const { approval_id } of execution.pending_approvals) {
      for (const decision of ['approve', 'deny']) {
        const payload = { execution_id, approval_id, decision, message: null };
        if (probe('execution.respond_to_approval', payload)) {
          keys.push(`${decision} ${execution_id} ${approval_id}`);
        }
      }
    }
    if (probe('execution.interrupt', { execution_id })) keys.push(`interrupt ${execution_id}`);
    if (probe('execution.send_instruction', { execution_id, text: 'Anything.' })) {
      keys.push(`instruct ${execution_id}`);
    }
  }
  return keys.sort();
}

function keyOf(answer: DemonstrationAnswer): string {
  return answer.kind === 'approve' || answer.kind === 'deny'
    ? `${answer.kind} ${answer.execution_id} ${answer.approval_id}`
    : `${answer.kind} ${answer.execution_id}`;
}

describe('the XR client demonstration', () => {
  test('the committed demonstration is exactly what the current code records', async () => {
    assert.equal(await recordDemonstration(SCENARIOS), COMMITTED, 'run pnpm demonstration:record');
  });

  test('every message it holds matches the realtime contract', () => {
    const messages = [
      DEMONSTRATION.welcome,
      DEMONSTRATION.snapshot,
      ...DEMONSTRATION.nodes.flatMap((node) => [
        ...node.events.map(({ message }) => message),
        ...(node.ending.snapshot === null ? [] : [node.ending.snapshot]),
      ]),
    ];
    for (const message of messages) {
      const parsed = validateServerMessage(message);
      assert.ok(parsed.ok, parsed.ok ? '' : JSON.stringify(parsed.issues));
    }
  });

  test('it is recorded and simulated, and only the directed work has a runtime that takes actions', () => {
    const { welcome, snapshot } = DEMONSTRATION;
    assert.equal(welcome.journal.origin, 'fixture');
    assert.deepEqual(snapshot.snapshot.journal, welcome.journal);
    assert.equal(welcome.resumed, false);
    assert.equal(welcome.head, snapshot.snapshot.position);
    const runtimes = snapshot.snapshot.runtimes;
    assert.deepEqual(
      runtimes.map(({ runtime_id, display_name, synthetic }) => ({
        runtime_id,
        display_name,
        synthetic,
      })),
      [
        { runtime_id: DIRECTED_RUNTIME_ID, display_name: DIRECTED_RUNTIME_NAME, synthetic: true },
        { runtime_id: WATCHED_RUNTIME_ID, display_name: WATCHED_RUNTIME_NAME, synthetic: true },
      ],
    );
    const watchOnly = runtimes.find(({ runtime_id }) => runtime_id === WATCHED_RUNTIME_ID);
    assert.deepEqual(watchOnly?.capabilities, {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      interrupt: false,
    });
    const executions = DEMONSTRATION.nodes.flatMap((node) =>
      node.events.flatMap(({ message }) => message.changes.executions),
    );
    assert.ok(executions.length > 0);
    assert.ok(executions.every((execution) => execution.runtime.synthetic));
    // Before anything starts, the workstreams already exist, so playing it again keeps them.
    assert.deepEqual(
      snapshot.snapshot.workstreams.map(({ title, status }) => ({ title, status })),
      DEMONSTRATION_PLAN.workstreams.map(({ title }) => ({ title, status: 'created' })),
    );
  });

  test('along every path, each change continues the journal and reaches the state the journal holds', () => {
    for (const path of paths()) {
      let position = DEMONSTRATION.snapshot.snapshot.position;
      for (const { position: next } of path.events) {
        assert.equal(next, position + 1, `path ${path.nodes.join(' > ')}`);
        position = next;
      }
      // A projection of the path's journal holds what the client holds after the beginning's
      // snapshot and every change on the way.
      const replayed = replayPath(path);
      assert.deepEqual(
        held([DEMONSTRATION.snapshot.snapshot, ...path.events.map(({ changes }) => changes)]),
        held([replayed]),
        `path ${path.nodes.join(' > ')}`,
      );
      const last = DEMONSTRATION.nodes[path.nodes.at(-1) ?? 0] as DemonstrationNode;
      const ending = last.ending.snapshot?.snapshot;
      if (ending !== undefined) {
        assert.equal(ending.position, position);
        assert.deepEqual(ending.journal, DEMONSTRATION.welcome.journal);
        assert.deepEqual(ending.runtimes, [], 'a finished recording offers no runtime');
        assert.deepEqual(held([ending]), held([replayed]));
      }
    }
  });

  test('it offers an answer exactly where a live workspace would offer that action, and nowhere else', () => {
    let boundaries = 0;
    let answers = 0;
    for (const path of paths()) {
      const index = path.nodes.at(-1) ?? 0;
      const node = DEMONSTRATION.nodes[index] as DemonstrationNode;
      const before = path.events.length - node.events.length;
      const projection = new Projection();
      for (const stored of asJournal(path.events.slice(0, before))) projection.apply(stored);
      let runtimes = DEMONSTRATION.snapshot.snapshot.runtimes;
      node.events.forEach(({ at_ms, message }, played) => {
        projection.apply({ position: message.position, event: message.event });
        const next = node.events[played + 1];
        if (next !== undefined && next.at_ms === at_ms) return;
        const after = played + 1;
        const last = after === node.events.length;
        if (last && node.ending.snapshot !== null)
          runtimes = node.ending.snapshot.snapshot.runtimes;
        const here = node.answers.filter((answer) => answer.after === after);
        const recorded = [...new Set(here.map(({ answer }) => keyOf(answer)))].sort();
        assert.deepEqual(recorded, offered(projection, runtimes), `node ${index}, after ${after}`);
        boundaries += 1;
        answers += here.length;
      });
    }
    const total = DEMONSTRATION.nodes.reduce((sum, node) => sum + node.answers.length, 0);
    assert.equal(answers, total, 'every recorded answer was checked where it is offered');
    assert.ok(boundaries > total, 'boundaries without answers were checked too');
  });

  test('answers come only where an instant of the recording ends', () => {
    DEMONSTRATION.nodes.forEach((node, index) => {
      node.events.forEach(({ at_ms }, played) => {
        const previous = node.events[played - 1];
        assert.ok(
          previous === undefined || previous.at_ms <= at_ms,
          `node ${index} goes back in time`,
        );
      });
      for (const { after, node: child } of node.answers) {
        assert.ok(after >= 1 && after <= node.events.length, `node ${index}`);
        const next = node.events[after];
        assert.ok(next === undefined || next.at_ms > (node.events[after - 1]?.at_ms ?? 0));
        assert.ok(child > index, 'a later node');
      }
    });
    const children = DEMONSTRATION.nodes.flatMap((node) =>
      node.answers.map(({ node: child }) => child),
    );
    assert.deepEqual(
      [...children].sort((a, b) => a - b),
      DEMONSTRATION.nodes.map((_, index) => index).slice(1),
      'a tree: every node but the first is exactly one answer',
    );
  });

  test('it holds for a person at the approval, offers its instructions for a while, and ends otherwise', () => {
    const beginning = DEMONSTRATION.nodes[0] as DemonstrationNode;
    assert.deepEqual(beginning.ending, { snapshot: null, hold_ms: null });
    const atApproval = beginning.answers.filter(({ after }) => after === beginning.events.length);
    assert.deepEqual(
      atApproval.map(({ answer }) => answer.kind),
      ['approve', 'deny', 'interrupt'],
    );
    const node = (kind: string) =>
      DEMONSTRATION.nodes[
        atApproval.find(({ answer }) => answer.kind === kind)?.node ?? -1
      ] as DemonstrationNode;
    const instructions = (of: DemonstrationNode) =>
      of.answers
        .filter(({ after }) => after === of.events.length)
        .map(({ answer: { label, text } }) => ({ label, text }));
    assert.deepEqual(instructions(node('approve')), DEMONSTRATION_PLAN.instructions.approve);
    assert.deepEqual(instructions(node('deny')), DEMONSTRATION_PLAN.instructions.deny);
    for (const kind of ['approve', 'deny']) {
      assert.deepEqual(node(kind).ending, { snapshot: null, hold_ms: AT_REST_HOLD_MS });
    }
    for (const [index, each] of DEMONSTRATION.nodes.entries()) {
      if (each.ending.hold_ms === null || each.ending.hold_ms === AT_REST_HOLD_MS) continue;
      assert.equal(each.ending.hold_ms, END_HOLD_MS, `node ${index}`);
      assert.notEqual(
        each.ending.snapshot,
        null,
        `node ${index} ends with its control plane's state`,
      );
    }
    // The approved path's tests fail first, and pass after the first recorded instruction.
    const approved = node('approve');
    const passing = DEMONSTRATION.nodes[
      approved.answers.find(
        ({ answer }) => answer.text === DEMONSTRATION_PLAN.instructions.approve[0]?.text,
      )?.node ?? -1
    ] as DemonstrationNode;
    const outcomes = (of: DemonstrationNode) =>
      of.events.flatMap(({ message: { event } }) =>
        event.event_type === 'runtime.test_run.completed' ? [event.payload.outcome] : [],
      );
    assert.deepEqual(outcomes(approved), ['failed']);
    assert.deepEqual(outcomes(passing), ['passed']);
  });

  test('the watched work has finished before the directed work starts', () => {
    const events = (DEMONSTRATION.nodes[0] as DemonstrationNode).events.map(
      ({ message }) => message.event,
    );
    const directedStart = events.findIndex(
      (event) =>
        event.event_type === 'execution.created' &&
        event.payload.runtime.runtime_id === DIRECTED_RUNTIME_ID,
    );
    assert.ok(directedStart > 0);
    const watchedAfter = events
      .slice(directedStart)
      .filter(
        (event) =>
          event.source.kind === 'runtime' && event.source.runtime_id === WATCHED_RUNTIME_ID,
      );
    assert.deepEqual(watchedAfter, []);
  });

  test('its welcome carries what a live realtime connection welcomes a client with', async () => {
    const server = await startTestServer();
    try {
      const client = await RealtimeClient.connect(server.wsUrl, server.token);
      client.hello(TEST_CLIENT);
      const welcome = await client.waitFor((message) => message.type === 'welcome');
      await client.close();
      assert.equal(welcome.type, 'welcome');
      if (welcome.type !== 'welcome') return;
      assert.equal(DEMONSTRATION.welcome.protocol, welcome.protocol);
      assert.deepEqual(DEMONSTRATION.welcome.command_policies, welcome.command_policies);
    } finally {
      await server.stop();
    }
  });

  test('a plan that offers an instruction its scenario does not script records nothing', async () => {
    await assert.rejects(
      recordDemonstration(SCENARIOS, {
        ...DEMONSTRATION_PLAN,
        instructions: {
          approve: [{ label: 'Anything', text: 'Something no scenario scripts.' }],
          deny: [],
        },
      }),
      /scripts no turn for an instruction the plan offers/,
    );
  });
});

/**
 * A path's events as a journal: the recording starts from a snapshot, so the events that made the
 * beginning come first, as the recorder journaled them.
 */
function asJournal(events: Path['events']): { position: number; event: EventEnvelope }[] {
  return [...PROLOGUE, ...events.map(({ position, event }) => ({ position, event }))];
}

/** The entities a projection of the path's journal holds. */
function replayPath(path: Path): EntityChanges {
  const projection = new Projection();
  for (const stored of asJournal(path.events)) projection.apply(stored);
  return {
    projects: projection.projects(),
    workstreams: projection.workstreams(),
    executions: projection.executions(),
    commands: projection.commands(50),
  };
}
