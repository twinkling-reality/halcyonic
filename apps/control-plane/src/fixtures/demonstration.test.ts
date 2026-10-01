import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  type CommandEnvelope,
  compileValidator,
  type EntityChanges,
  ESTIMATED_COST_NOTE,
  EvaluationResponse,
  type EventEnvelope,
  type RuntimeDescriptor,
  ServerMessage,
  UnderstandingResponse,
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
  type RecordedAnswers,
  recordDemonstration,
  WATCHED_RUNTIME_ID,
  WATCHED_RUNTIME_NAME,
} from './demonstration.ts';
import { SIMULATED_INSTANCE_ID, SIMULATED_VERSION } from './demonstration-sources.ts';

const SCENARIOS = loadScenarios(fileURLToPath(DEMONSTRATION_SCENARIOS));
const COMMITTED = readFileSync(DEMONSTRATION_FILE, 'utf8');
const DEMONSTRATION = JSON.parse(COMMITTED) as Demonstration;
const PROLOGUE = await demonstrationPrologue(SCENARIOS);
const validateServerMessage = compileValidator(ServerMessage);
const validateUnderstanding = compileValidator(UnderstandingResponse);
const validateEvaluation = compileValidator(EvaluationResponse);

/** Where the playback stands: the nodes on the way from the beginning, and how far each played. */
type Point = readonly { readonly node: number; readonly played: number }[];

/** Every point where an instant of the recording ends, on every path through the tree. */
function points(): Point[] {
  const found: Point[] = [];
  const walk = (index: number, way: Point) => {
    const node = DEMONSTRATION.nodes[index] as DemonstrationNode;
    node.events.forEach(({ at_ms }, played) => {
      const next = node.events[played + 1];
      if (next === undefined || next.at_ms !== at_ms) {
        found.push([...way, { node: index, played: played + 1 }]);
      }
    });
    for (const { after, node: child } of node.answers) {
      walk(child, [...way, { node: index, played: after }]);
    }
  };
  walk(0, []);
  return found;
}

/** The answer in force for an execution at a point: the latest recorded on the way there. */
function inForce<T>(answers: RecordedAnswers<T>, executionId: string, point: Point): T | undefined {
  for (let step = point.length - 1; step >= 0; step -= 1) {
    const { node, played } = point[step] as Point[number];
    const found = (answers[executionId] ?? []).filter(
      (answer) => answer.node === node && answer.after <= played,
    );
    const latest = found.at(-1);
    if (latest !== undefined) return latest.response;
  }
  return undefined;
}

/** The executions a client holds at a point, from the changes played on the way there. */
function executionsAt(point: Point): string[] {
  const ids = new Set<string>();
  for (const { node, played } of point) {
    const events = (DEMONSTRATION.nodes[node] as DemonstrationNode).events.slice(0, played);
    for (const { message } of events) {
      for (const execution of message.changes.executions) ids.add(execution.execution_id);
    }
  }
  return [...ids];
}

/** The directed work's execution, which every path shares. */
function directedExecution(): string {
  const executions = DEMONSTRATION.nodes.flatMap((node) =>
    node.events.flatMap(({ message }) => message.changes.executions),
  );
  const directed = executions.find(({ runtime }) => runtime.runtime_id === DIRECTED_RUNTIME_ID);
  if (directed === undefined) throw new Error('no directed execution');
  return directed.execution_id;
}

/** The understanding and evaluation in force for the directed work once a node has played. */
function directedAt(way: Point) {
  const executionId = directedExecution();
  const understanding = inForce<UnderstandingResponse>(
    DEMONSTRATION.understanding,
    executionId,
    way,
  );
  const evaluation = inForce<EvaluationResponse>(DEMONSTRATION.evaluation, executionId, way);
  assert.equal(understanding?.result.availability, 'available');
  assert.equal(evaluation?.result.availability, 'available');
  if (understanding?.result.availability !== 'available') throw new Error('no understanding');
  if (evaluation?.result.availability !== 'available') throw new Error('no evaluation');
  return {
    understanding: understanding.result.understanding,
    evaluation: evaluation.result.evaluation,
  };
}

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
      answer_question: false,
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

  test('its understanding and evaluation answers are the stand-ins’, marked synthetic, as the REST contract says', () => {
    const executions = new Set(points().flatMap(executionsAt));
    assert.deepEqual(Object.keys(DEMONSTRATION.understanding).sort(), [...executions].sort());
    assert.deepEqual(Object.keys(DEMONSTRATION.evaluation).sort(), [...executions].sort());
    for (const [executionId, answers] of Object.entries(DEMONSTRATION.understanding)) {
      for (const { response, read_at } of answers) {
        assert.ok(validateUnderstanding(response).ok, executionId);
        assert.equal(response.execution_id, executionId);
        assert.ok(Date.parse(read_at) > 0);
        if (response.result.availability !== 'available') assert.fail('only available answers');
        const { source } = response.result.understanding;
        assert.equal(source.synthetic, true, 'never taken for a real Salidium');
        assert.equal(source.version, SIMULATED_VERSION);
        assert.equal(source.instance_id, SIMULATED_INSTANCE_ID);
      }
    }
    for (const [executionId, answers] of Object.entries(DEMONSTRATION.evaluation)) {
      for (const { response } of answers) {
        assert.ok(validateEvaluation(response).ok, executionId);
        assert.equal(response.execution_id, executionId);
        if (response.result.availability !== 'available') assert.fail('only available answers');
        const { source, cost } = response.result.evaluation;
        assert.deepEqual(source, { system: 'seorak', synthetic: true, api_version: 'v1' });
        assert.equal(cost.note, ESTIMATED_COST_NOTE);
      }
    }
  });

  test('wherever the playback stands, each execution it shows has the answer the control plane gave there', () => {
    let checked = 0;
    for (const point of points()) {
      for (const executionId of executionsAt(point)) {
        const where = point.map(({ node, played }) => `${node}@${played}`).join(' > ');
        assert.ok(inForce(DEMONSTRATION.understanding, executionId, point), where);
        assert.ok(inForce(DEMONSTRATION.evaluation, executionId, point), where);
        checked += 1;
      }
    }
    assert.ok(checked > 100);
    // An answer is recorded where it changed, never twice in a row on one path.
    for (const answers of [DEMONSTRATION.understanding, DEMONSTRATION.evaluation]) {
      for (const recorded of Object.values(answers)) {
        for (const [index, answer] of recorded.entries()) {
          const node = DEMONSTRATION.nodes[answer.node] as DemonstrationNode;
          assert.ok(answer.after >= 1 && answer.after <= node.events.length);
          const previous = recorded[index - 1];
          if (previous?.node === answer.node) {
            assert.ok(previous.after < answer.after);
            assert.notDeepEqual(previous.response, answer.response);
          }
        }
      }
    }
  });

  test('the directed work reads as the story goes: waiting, then failing tests, then verified', () => {
    const beginning = DEMONSTRATION.nodes[0] as DemonstrationNode;
    const answer = (of: DemonstrationNode, kind: string) =>
      of.answers.find(({ answer: { kind: candidate } }) => candidate === kind);
    const approve = answer(beginning, 'approve');
    if (approve === undefined) throw new Error('no approval');
    const approved = DEMONSTRATION.nodes[approve.node] as DemonstrationNode;
    const instruct = approved.answers.find(
      ({ answer: { text } }) => text === DEMONSTRATION_PLAN.instructions.approve[0]?.text,
    );
    if (instruct === undefined) throw new Error('no instruction');
    const instructed = DEMONSTRATION.nodes[instruct.node] as DemonstrationNode;

    const atApproval = directedAt([{ node: 0, played: beginning.events.length }]);
    assert.equal(atApproval.understanding.verdict.headline, 'Waiting for you');
    assert.equal(atApproval.understanding.verdict.epistemic, 'observed');
    assert.match(atApproval.understanding.waiting?.summary ?? '', /^Run make migrate/);
    assert.equal(atApproval.understanding.latest_statement?.epistemic, 'reported');
    assert.deepEqual(atApproval.understanding.verification.latest_by_method, []);
    assert.equal(atApproval.evaluation.outcome.availability.state, 'unavailable');
    assert.equal(atApproval.evaluation.outcome.availability.reason, 'not_yet_computed');
    assert.equal(atApproval.evaluation.outcome.measure, null);
    assert.deepEqual(atApproval.evaluation.verification.lens?.by_kind, []);
    assert.ok(atApproval.evaluation.verification.lens?.empty_reason);

    const failing = directedAt([
      { node: 0, played: approve.after },
      { node: approve.node, played: approved.events.length },
    ]);
    assert.equal(failing.understanding.verdict.headline, '1 test failing');
    assert.equal(failing.understanding.verdict.tone, 'fail');
    const run = failing.understanding.verification.latest_by_method[0];
    assert.equal(run?.outcome, 'fail');
    assert.deepEqual(
      run?.exit,
      { code: null, observation: 'inferred_failure' },
      'no exit code was observed',
    );
    assert.equal(failing.understanding.explanation.status, 'generated');
    assert.equal(failing.understanding.explanation.epistemic, 'explained');
    const measure = failing.evaluation.outcome.measure;
    assert.equal(measure?.uncommitted, null, 'unknown until the session ends, never zero');
    assert.equal(measure?.line_survival, null, 'pending for three days');
    assert.equal(measure?.error_count, 0, 'a measured zero');
    assert.deepEqual(failing.evaluation.verification.lens?.by_kind, [
      { label: 'test', runs: 1, passed: 0, pass_rate: 0 },
    ]);

    const verified = directedAt([
      { node: 0, played: approve.after },
      { node: approve.node, played: instruct.after },
      { node: instruct.node, played: instructed.events.length },
    ]);
    assert.equal(verified.understanding.verdict.headline, '2 files changed, verified');
    assert.equal(verified.understanding.verdict.epistemic, 'inferred');
    assert.ok(
      verified.understanding.changes.files.every(({ coverage }) => coverage.verified_after),
    );
    assert.deepEqual(verified.evaluation.verification.lens?.by_kind, [
      { label: 'test', runs: 2, passed: 1, pass_rate: 0.5 },
    ]);
    assert.ok(
      (verified.evaluation.cost.estimated_usd ?? 0) > (failing.evaluation.cost.estimated_usd ?? 0),
      'the estimate grows with the tokens the session used',
    );
  });

  test('its answers show every epistemic class the understanding source uses', () => {
    const classes = new Set<string>();
    const collect = (value: unknown): void => {
      if (Array.isArray(value)) value.forEach(collect);
      else if (value !== null && typeof value === 'object') {
        for (const [key, inner] of Object.entries(value)) {
          if (key === 'epistemic' && typeof inner === 'string') classes.add(inner);
          else collect(inner);
        }
      }
    };
    for (const answers of Object.values(DEMONSTRATION.understanding)) {
      for (const { response } of answers) {
        if (response.result.availability !== 'available') continue;
        const { explanation, ...claims } = response.result.understanding;
        collect(claims);
        if (explanation.status === 'generated') classes.add(explanation.epistemic);
      }
    }
    assert.deepEqual([...classes].sort(), [
      'explained',
      'inferred',
      'observed',
      'planned',
      'reported',
    ]);
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
