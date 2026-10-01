import assert from 'node:assert/strict';
import { request as httpRequest } from 'node:http';
import { after, before, describe, test } from 'node:test';
import {
  compileValidator,
  type ServerMessage,
  Snapshot,
  type WorkstreamStatus,
} from '@halcyonic/contracts';
import { RealtimeClient } from '../client/realtime-client.ts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { startTestServer, TEST_CLIENT } from '../testing/harness.ts';

const validateSnapshot = compileValidator(Snapshot);
const APPROVAL = DEMO_WORKSTREAMS[2] as (typeof DEMO_WORKSTREAMS)[number];

let server: Awaited<ReturnType<typeof startTestServer>>;
before(async () => {
  server = await startTestServer();
});
after(async () => {
  await server.stop();
});

/** node:http, because fetch does not let a caller set Host. */
function raw(
  path: string,
  headers: Record<string, string>,
  method = 'GET',
  body?: string,
): Promise<{ status: number; body: string }> {
  return new Promise((resolve, reject) => {
    const req = httpRequest(
      { host: '127.0.0.1', port: server.port, path, method, headers },
      (res) => {
        let text = '';
        res.on('data', (chunk: Buffer) => {
          text += chunk.toString('utf8');
        });
        res.on('end', () => resolve({ status: res.statusCode ?? 0, body: text }));
      },
    );
    req.on('error', reject);
    if (body !== undefined) req.write(body);
    req.end();
  });
}

const auth = () => ({ authorization: `Bearer ${server.token}` });
const json = () => ({ ...auth(), 'content-type': 'application/json' });

async function post(body: unknown, headers: Record<string, string> = json()) {
  const response = await fetch(`${server.baseUrl}/api/commands`, {
    method: 'POST',
    headers,
    body: typeof body === 'string' ? body : JSON.stringify(body),
  });
  return { status: response.status, body: (await response.json()) as Record<string, unknown> };
}

describe('REST', () => {
  test('health is public; everything else needs the token', async () => {
    assert.equal((await fetch(`${server.baseUrl}/api/health`)).status, 200);
    const anonymous = await fetch(`${server.baseUrl}/api/snapshot`);
    assert.equal(anonymous.status, 401);
    assert.equal(anonymous.headers.get('www-authenticate'), 'Bearer');
    const wrong = await fetch(`${server.baseUrl}/api/snapshot`, {
      headers: { authorization: 'Bearer nope' },
    });
    assert.equal(wrong.status, 401);
    const snapshot = await fetch(`${server.baseUrl}/api/snapshot`, { headers: auth() });
    assert.equal(snapshot.status, 200);
    assert.ok(validateSnapshot(await snapshot.json()).ok);
  });

  test('foreign Hosts and browser origins are refused even with the token', async () => {
    const rebinding = await raw('/api/snapshot', { ...auth(), host: 'evil.example' });
    assert.equal(rebinding.status, 403);
    const browser = await raw('/api/snapshot', { ...auth(), origin: 'https://evil.example' });
    assert.equal(browser.status, 403);
    assert.match(browser.body, /origin_not_allowed/);
  });

  test('commands are validated, admitted, and answered with honest status codes', async () => {
    assert.equal((await post('name=x', { ...auth(), 'content-type': 'text/plain' })).status, 415);
    const invalid = await post({ command_type: 'project.create' });
    assert.equal(invalid.status, 400);
    assert.ok(Array.isArray((invalid.body.error as { issues: unknown[] }).issues));

    const command = server.commands.createProject('REST project');
    const accepted = await post(command);
    assert.equal(accepted.status, 202);
    assert.equal(accepted.body.disposition, 'accepted');
    assert.equal((await post(command)).status, 200);
    assert.equal(
      (await post({ ...command, payload: { name: 'Other', location: null } })).status,
      409,
    );

    const rejected = await post(
      server.commands.createWorkstream('01920000-0000-7000-8000-00000000ffff' as never, APPROVAL),
    );
    assert.equal(rejected.status, 422);
    assert.equal(
      (rejected.body.command as { rejection: { code: string } }).rejection.code,
      'project_not_found',
    );
  });

  test('journal history pages by position', async () => {
    assert.equal((await post(server.commands.createProject('History'))).status, 202);
    const page = await fetch(`${server.baseUrl}/api/events?after=0&limit=2`, { headers: auth() });
    const body = (await page.json()) as { events: { position: number }[]; head: number };
    assert.deepEqual(
      body.events.map((stored) => stored.position),
      [1, 2],
    );
    assert.ok(body.head >= 2);
    const bad = await fetch(`${server.baseUrl}/api/events?limit=5000`, { headers: auth() });
    assert.equal(bad.status, 400);
  });
});

describe('realtime protocol', () => {
  test('an upgrade without the token, or from a browser origin, is refused', async () => {
    await assert.rejects(RealtimeClient.connect(server.wsUrl, 'wrong'));
    await assert.rejects(
      RealtimeClient.connect(server.wsUrl, server.token, { origin: 'https://evil.example' }),
    );
  });

  test('messages before hello, and unsupported protocols, close the connection', async () => {
    const early = await RealtimeClient.connect(server.wsUrl, server.token);
    early.sendRaw({ type: 'ping', nonce: null });
    early.sendRaw({ type: 'command', command: server.commands.createProject('Too early') });
    const closed = await early.closed;
    assert.equal(closed.code, 1008);

    const future = await RealtimeClient.connect(server.wsUrl, server.token);
    future.sendRaw({ type: 'hello', protocol: 99, client: TEST_CLIENT, resume: null });
    const error = await future.waitFor((message) => message.type === 'error');
    assert.equal(error.type === 'error' && error.error.code, 'unsupported_protocol');
    assert.equal((await future.closed).code, 1008);
  });

  test('bad JSON after hello is reported without dropping the connection', async () => {
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    client.hello(TEST_CLIENT);
    await client.waitFor((message) => message.type === 'snapshot');
    client.sendRaw('not json');
    await client.waitFor((message) => message.type === 'error');
    client.sendRaw({ type: 'ping', nonce: 'still-here' });
    const pong = await client.waitFor((message) => message.type === 'pong');
    assert.equal(pong.type === 'pong' && pong.nonce, 'still-here');
    await client.close();
  });

  test('the welcome tells the client which commands need a deliberate confirmation', async () => {
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    client.hello(TEST_CLIENT);
    const welcome = await client.waitFor((message) => message.type === 'welcome');
    await client.close();
    assert.equal(welcome.type, 'welcome');
    if (welcome.type !== 'welcome') return;
    const policies = new Map(
      welcome.command_policies.map((entry) => [entry.command_type, entry.policy]),
    );
    assert.equal(policies.size, 8);
    assert.equal(policies.get('execution.answer_question'), 'low_consequence');
    assert.equal(policies.get('project.set_location'), 'low_consequence');
    assert.equal(policies.get('execution.respond_to_approval'), 'review_required');
    assert.equal(policies.get('execution.interrupt'), 'review_required');
    assert.equal(policies.get('execution.send_instruction'), 'low_consequence');
  });

  test('a client with a current cursor resumes without a snapshot; a stale one gets a snapshot', async () => {
    assert.equal((await post(server.commands.createProject('Resume'))).status, 202);
    const first = await RealtimeClient.connect(server.wsUrl, server.token);
    first.hello(TEST_CLIENT);
    const welcome = await first.waitFor((message) => message.type === 'welcome');
    await first.close();
    assert.equal(welcome.type, 'welcome');
    if (welcome.type !== 'welcome') return;

    const current = await RealtimeClient.connect(server.wsUrl, server.token);
    current.hello(TEST_CLIENT, { journal_id: welcome.journal.journal_id, position: welcome.head });
    const resumed = await current.waitFor((message) => message.type === 'welcome');
    assert.equal(resumed.type === 'welcome' && resumed.resumed, true);
    current.sendRaw({ type: 'ping', nonce: 'after-welcome' });
    await current.waitFor((message) => message.type === 'pong');
    assert.ok(!current.messages.some((message) => message.type === 'snapshot'));
    await current.close();

    assert.ok(welcome.head > 0);
    const behind = await RealtimeClient.connect(server.wsUrl, server.token);
    behind.hello(TEST_CLIENT, {
      journal_id: welcome.journal.journal_id,
      position: welcome.head - 1,
    });
    await behind.waitFor((message) => message.type === 'snapshot');
    await behind.close();

    const otherJournal = await RealtimeClient.connect(server.wsUrl, server.token);
    otherJournal.hello(TEST_CLIENT, {
      journal_id: '01920000-0000-7000-8000-000000000000' as never,
      position: welcome.head,
    });
    await otherJournal.waitFor((message) => message.type === 'snapshot');
    await otherJournal.close();
  });

  test('milestone: mock runtime to journal to a realtime client, including an approval round trip', async () => {
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    client.hello(TEST_CLIENT);
    await client.waitFor((message) => message.type === 'snapshot');

    const ack = async (command: Parameters<RealtimeClient['command']>[0]) => {
      client.command(command);
      const message = await client.waitFor(
        (m) => m.type === 'command_ack' && m.command_id === command.command_id,
      );
      assert.equal(message.type === 'command_ack' && message.disposition, 'accepted');
      return message.type === 'command_ack' ? message.command : null;
    };
    const project = await ack(server.commands.createProject('Milestone'));
    if (project?.result?.kind !== 'project_created') throw new Error('no project');
    const workstream = await ack(
      server.commands.createWorkstream(project.result.project_id, APPROVAL),
    );
    if (workstream?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const workstreamId = workstream.result.workstream_id;
    await ack(server.commands.startExecution(workstreamId, APPROVAL));

    const statuses: WorkstreamStatus[] = [];
    client.onMessage((message: ServerMessage) => {
      if (message.type !== 'event') return;
      for (const view of message.changes.workstreams) {
        if (view.workstream_id === workstreamId && statuses.at(-1) !== view.status) {
          statuses.push(view.status);
        }
      }
    });

    await server.time.runUntilIdle();
    const waiting = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.changes.workstreams.some(
          (ws) => ws.workstream_id === workstreamId && ws.attention.level === 'action_required',
        ),
    );
    if (waiting.type !== 'event') throw new Error('unexpected message');
    const execution = waiting.changes.executions[0];
    const approval = execution?.pending_approvals[0];
    assert.ok(execution && approval, 'the client can see what needs approving');

    await ack(server.commands.approve(execution.execution_id, approval.approval_id));
    await server.time.runUntilIdle();
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.changes.workstreams.some(
          (ws) => ws.workstream_id === workstreamId && ws.status === 'completed',
        ),
    );

    assert.deepEqual(statuses, [
      'running',
      'waiting_for_human',
      'running',
      'verifying',
      'running',
      'completed',
    ]);
    assert.deepEqual(client.invalid, [], 'every server message matched the contract');

    const snapshot = await fetch(`${server.baseUrl}/api/snapshot`, { headers: auth() });
    const persisted = (await snapshot.json()) as {
      workstreams: { workstream_id: string; status: string }[];
    };
    assert.equal(
      persisted.workstreams.find((ws) => ws.workstream_id === workstreamId)?.status,
      'completed',
      'REST bootstrap agrees with what was streamed',
    );
    await client.close();
  });
});
