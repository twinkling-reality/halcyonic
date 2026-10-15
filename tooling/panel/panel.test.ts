import assert from 'node:assert/strict';
import { execFile, execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createServer as createPlainServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { createServer, type TLSSocket } from 'node:tls';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { DEMO_WORKSTREAMS } from '../../apps/control-plane/src/demo-plan.ts';
import { loopbackProof } from '../../apps/control-plane/src/http/security.ts';
import { createNetworkIdentity } from '../../apps/control-plane/src/network/certificate.ts';
import { credentialFromBytes } from '../../apps/control-plane/src/network/pairing-protocol.ts';
import { writeWav } from '../../apps/control-plane/src/speech/wav.ts';
import type { SpeechEngine } from '../../apps/control-plane/src/speech/whisper.ts';
import { startTestServer } from '../../apps/control-plane/src/testing/harness.ts';
import {
  compileValidator,
  parseCommandEnvelope,
  Snapshot,
  WorkspaceAgentStatus,
  WorkspaceRequestResponse,
} from '../../packages/contracts/src/index.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const PANEL = join(ROOT, 'apps/xr/Android/panel');
const SOURCES = join(PANEL, 'src/com/halcyonic/panel');
const ANDROID = '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer';
const BIN = process.env.JAVA_HOME
  ? join(process.env.JAVA_HOME, 'bin')
  : join(ANDROID, 'OpenJDK/bin');
const JAR = process.env.ANDROID_JAR ?? join(ANDROID, 'SDK/platforms/android-36/android.jar');
const PLAIN = [
  'PanelJson.java',
  'PanelWait.java',
  'PanelIdentity.java',
  'PanelTransport.java',
  'PanelContinuation.java',
  'PanelModel.java',
  'PanelText.java',
  'PanelInputText.java',
  'PanelWorkspaceRequest.java',
  'PanelRequestDispatch.java',
  'PanelRequestSends.java',
  'PanelAtomicWrite.java',
  'PanelDrafts.java',
  'PanelProof.java',
  'PanelUiState.java',
  'PanelVoice.java',
  'PanelRestore.java',
  'PanelViewStore.java',
  'PanelProjectOutput.java',
  'PanelToolResults.java',
];
const ANDROID_ONLY = [
  'PanelFiles.java',
  'PanelLoadingText.java',
  'PanelBridge.java',
  'AgentPanelActivity.java',
  'PanelVoiceCapture.java',
  'PanelAppLauncher.java',
];
const DEVELOPMENT = join(PANEL, 'development/com/halcyonic/panel');
const DEVELOPMENT_ONLY = ['PanelPreviewActivity.java'];
const classes = mkdtempSync(join(tmpdir(), 'halcyonic-panel-'));
const fixtures = JSON.parse(
  readFileSync(join(ROOT, 'fixtures/workspace/continuation.json'), 'utf8'),
) as {
  cases: { name: string; valid: boolean; value: unknown }[];
  identities: { name: string; connection_key: string; [key: string]: unknown }[];
  text_limits: { symbol: string; count: number; valid: boolean }[];
};

function args(command: string, values: readonly string[]) {
  return [
    '-cp',
    classes,
    'com.halcyonic.panel.PanelSelfTest',
    command,
    ...values.map((v) => Buffer.from(v, 'utf16le').toString('base64')),
  ];
}
function run(command: string, ...values: string[]) {
  return Buffer.from(
    execFileSync(join(BIN, 'java'), args(command, values), {
      encoding: 'utf8',
      maxBuffer: 4 * 1024 * 1024,
    }).trim(),
    'base64',
  ).toString();
}
async function runAsync(command: string, ...values: string[]) {
  const { stdout } = await promisify(execFile)(join(BIN, 'java'), args(command, values), {
    encoding: 'utf8',
  });
  return JSON.parse(Buffer.from(stdout.trim(), 'base64').toString()) as {
    status?: number;
    body?: string;
    code?: string;
    unknown?: boolean;
    persisted?: boolean;
    settled?: boolean;
    request_id?: string | null;
  };
}
const device = '0192f1a0-0000-7000-8000-000000000009';
const fakeCredential = 'hlcd_fixture_not_a_secret';
function pairing(port: number, pin: string, credential = fakeCredential, deviceId = device) {
  return JSON.stringify({
    host: '127.0.0.1',
    port,
    certificate_sha256: pin,
    device_id: deviceId,
    credential,
  });
}
async function listener(answer: (text: string, socket: TLSSocket) => void) {
  const identity = createNetworkIdentity(new Date());
  const sockets = new Set<TLSSocket>();
  const heard: string[] = [];
  const server = createServer({ key: identity.key, cert: identity.certificate }, (socket) => {
    sockets.add(socket);
    socket.on('error', () => {});
    socket.on('close', () => sockets.delete(socket));
    let request = '';
    let answered = false;
    socket.on('data', (data) => {
      request += data.toString();
      if (!answered && request.includes('\r\n\r\n')) {
        answered = true;
        heard.push(request);
        answer(request, socket);
      }
    });
  });
  server.on('tlsClientError', () => {});
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  assert.ok(address && typeof address !== 'string');
  return {
    port: address.port,
    pin: identity.certificateSha256,
    heard,
    async close() {
      for (const socket of sockets) socket.destroy();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
  };
}
function response(body: string, status = 200) {
  return `HTTP/1.1 ${status} OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: ${Buffer.byteLength(body)}\r\n\r\n${body}`;
}

const projectId = '0192f1a0-0000-7000-8000-000000000002';
const workId = '0192f1a0-0000-7000-8000-000000000003';
const executionId = '0192f1a0-0000-7000-8000-000000000004';
const requestId = '0192f1a0-0000-7000-8000-000000000008';
const commandId = '0192f1a0-0000-4000-8000-000000000010';
const nativeContext = {
  connection: 'synthetic-key',
  journal: '0192f1a0-0000-7000-8000-000000000001',
  project: projectId,
  work: workId,
  execution: executionId,
};
function workspaceRequest() {
  return {
    request: {
      request_id: requestId,
      journal_id: nativeContext.journal,
      project_id: projectId,
      workstream_id: workId,
      execution_id: executionId,
      original_text: 'Investigate this synthetic task.',
      status: 'responded',
      reply: {
        text: 'Reported result, not software verification.',
        epistemic: 'reported',
        model: 'synthetic-model',
        served: 'this_mac',
      },
      steps: [
        {
          step_id: '0192f1a0-0000-7000-8000-000000000011',
          tool_name: 'continue_task',
          status: 'confirmed',
          command_id: commandId,
          command_status: 'completed',
          observation_position: 1,
          active_turn_id: 'synthetic-turn',
          had_active_turn: true,
          observation_after: 2,
          observed_turn_id: 'synthetic-turn',
          runtime_status: 'completed',
          summary: 'Instruction delivered; subsequent native turn ended.',
        },
      ],
      earlier_steps: 0,
      created_at: '2026-10-12T10:00:00.000Z',
      updated_at: '2026-10-12T10:01:00.000Z',
    },
  };
}
function workspaceCommand() {
  return {
    journal_id: nativeContext.journal,
    command: {
      command_id: commandId,
      command_type: 'workspace.request.submit',
      status: 'completed',
      project_id: projectId,
      workstream_id: workId,
      execution_id: executionId,
      issued_at: '2026-10-12T10:00:00.000Z',
      updated_at: '2026-10-12T10:01:00.000Z',
      rejection: null,
      failure: null,
      result: { kind: 'workspace_request_created', request_id: requestId },
    },
  };
}
function model() {
  return {
    journal: { journal_id: '0192f1a0-0000-7000-8000-000000000001', origin: 'live' },
    position: 1,
    projects: [{ project_id: projectId, name: 'Fixture' }],
    workstreams: [
      { workstream_id: workId, project_id: projectId, current_execution_id: executionId },
    ],
    executions: [
      {
        execution_id: executionId,
        workstream_id: workId,
        project_id: projectId,
        runtime: { runtime_id: 'mock' },
        native_id: 'fixture',
        status: 'running',
        pending_approvals: [] as unknown[],
        pending_questions: [] as unknown[],
      },
    ],
    runtimes: [
      {
        runtime_id: 'mock',
        capabilities: {
          instruct_at_rest: true,
          instruct_while_running: true,
          interrupt: true,
          respond_to_approval: true,
          answer_question: true,
        },
      },
    ],
    commands: [],
  };
}

describe('production native agent panel', () => {
  before(() => {
    assert.ok(
      existsSync(join(BIN, 'javac')) && existsSync(JAR),
      'JDK and Android platform jar are required',
    );
    execFileSync(join(BIN, 'javac'), [
      '--release',
      '11',
      '-d',
      classes,
      ...PLAIN.map((p) => join(SOURCES, p)),
      join(PANEL, 'test/com/halcyonic/panel/PanelSelfTest.java'),
    ]);
  });
  after(() => rmSync(classes, { recursive: true, force: true }));
  test('native voice leases and held gestures permanently reject stale authority without changing existing drafts', () => {
    assert.equal(run('voice_state', pairing(1, '0'.repeat(64))), 'true');
  });
  test('native voice creates canonical bounded PCM16 WAV and strictly reads untrusted transcripts', () => {
    for (const count of [7999, 480001]) assert.equal(run('voice_wav', String(count)), 'null');
    for (const count of [8000, 16000, 480000]) {
      const wav = Buffer.from(run('voice_wav', String(count)), 'base64');
      const samples = Buffer.alloc(count * 2);
      samples.writeInt16LE(-32768, 0);
      samples.writeInt16LE(32767, 2);
      assert.deepEqual(wav, writeWav(samples));
    }
    const engine = { name: 'synthetic-engine', version: 'test' };
    const heard = { outcome: 'heard', text: 'Add a bounded regression.', language: 'en', engine };
    assert.equal(run('voice_transcript', JSON.stringify(heard)), heard.text);
    assert.equal(
      run('voice_transcript', JSON.stringify({ outcome: 'nothing_heard', engine })),
      'nothing_heard',
    );
    for (const changed of [
      { ...heard, outcome: 'unknown' },
      { ...heard, text: 'x'.repeat(4097) },
      { ...heard, language: 'en invalid' },
      { ...heard, other: true },
      { ...heard, engine: { name: 'x'.repeat(65), version: 'test' } },
      { outcome: 'nothing_heard', text: 'invented', engine },
    ])
      assert.equal(run('voice_transcript', JSON.stringify(changed)), 'refused');
  });
  test('native voice uses the real local transcription route and its host bounds without journaling or commands', async () => {
    const received: Buffer[] = [];
    let words = 'Synthetic transcript for a local draft.';
    const speech: SpeechEngine = {
      engine: { name: 'synthetic-engine', version: 'test' },
      async transcribe(wav) {
        received.push(wav);
        return { kind: 'text', text: words };
      },
      async warmUp() {
        return 0;
      },
    };
    const server = await startTestServer({ speech });
    try {
      const target = JSON.stringify({ port: server.port, token: server.token });
      const head = server.controlPlane.journal.head();
      for (const count of [8000, 480000]) {
        const answer = await runAsync('voice_request_usb', target, String(count));
        assert.equal(answer.status, 200);
        assert.deepEqual(received.at(-1), writeWav(Buffer.alloc(count * 2)));
        assert.equal(run('voice_transcript', answer.body ?? ''), words);
      }
      words = '';
      const empty = await runAsync('voice_request_usb', target, '16000');
      assert.equal(empty.status, 200);
      assert.equal(run('voice_transcript', empty.body ?? ''), 'nothing_heard');
      const before = received.length;
      for (const count of ['7999', '480001']) {
        const refused = await runAsync('voice_request_usb', target, count);
        assert.equal(refused.code, 'invalid_audio');
        assert.equal(refused.unknown, false);
      }
      const cancelled = await runAsync('voice_request_usb', target, '16000', '999', 'cancel');
      assert.equal(cancelled.code, 'panel_closed');
      assert.equal(received.length, before);
      assert.equal(server.controlPlane.journal.head(), head);
    } finally {
      await server.stop();
    }
  });
  test('native audio never sends credentials or audio to an unpinned or replaced pairing and retains JSON route restrictions', async () => {
    const server = await listener((_text, socket) => socket.end(response('{}')));
    try {
      const wrong = await runAsync('voice_request', pairing(server.port, '0'.repeat(64)), '16000');
      assert.equal(wrong.code, 'certificate_changed');
      const changed = await runAsync(
        'voice_request',
        pairing(server.port, server.pin),
        '16000',
        '3',
      );
      assert.equal(changed.code, 'pairing_changed');
      assert.equal(server.heard.length, 0);
      const generic = await runAsync(
        'request',
        pairing(server.port, server.pin),
        'POST',
        '/api/transcriptions',
        '{}',
      );
      assert.equal(generic.code, 'unsupported_request');
      const oversized = await runAsync('voice_request', pairing(server.port, server.pin), '480001');
      assert.equal(oversized.code, 'invalid_audio');
      assert.equal(server.heard.length, 0);
    } finally {
      await server.close();
    }
  });
  test('native audio waits for a bounded transcription longer than the command idle timeout and refuses redirects without retry', async () => {
    for (const redirect of [false, true]) {
      const server = await listener((_text, socket) => {
        if (redirect)
          socket.end(
            'HTTP/1.1 302 Found\r\nLocation: https://elsewhere.invalid/\r\nContent-Length: 0\r\n\r\n',
          );
        else
          setTimeout(
            () =>
              socket.end(
                response(
                  JSON.stringify({
                    outcome: 'nothing_heard',
                    engine: { name: 'synthetic', version: 'test' },
                  }),
                ),
              ),
            4200,
          );
      });
      try {
        const result = await runAsync('voice_request', pairing(server.port, server.pin), '8000');
        if (redirect) assert.equal(result.code, 'redirect_refused');
        else assert.equal(result.status, 200);
        assert.equal(server.heard.length, 1);
      } finally {
        await server.close();
      }
    }
  });
  test('native request receipts distinguish observed turn outcome, read evidence and human direction', () => {
    const finished = {
      status: 'confirmed',
      command_id: 'synthetic-command',
      runtime_status: 'completed',
      tool_name: 'continue_task',
    };
    assert.equal(
      run('workspace_receipt', JSON.stringify(finished)),
      'Turn finished\nSoftware changes remain unverified.',
    );
    assert.equal(
      run(
        'workspace_receipt',
        JSON.stringify({
          ...finished,
          command_id: null,
          runtime_status: null,
          tool_name: 'read_results',
        }),
      ),
      'Read confirmed',
    );
    assert.equal(
      run(
        'workspace_receipt',
        JSON.stringify({ ...finished, runtime_status: null, tool_name: 'person_response' }),
      ),
      'Direction recorded',
    );
    assert.equal(
      run('workspace_receipt', JSON.stringify({ ...finished, status: 'failed' })),
      'failed',
    );
  });
  test('native workspace offer and exact request consumer accept canonical producer fields and reject foreign or malformed records', () => {
    const offer = { availability: 'available', model: 'synthetic-model', served: 'this_mac' };
    assert.equal(compileValidator(WorkspaceAgentStatus)(offer).ok, true);
    assert.deepEqual(JSON.parse(run('workspace_offer', JSON.stringify(offer))), offer);
    assert.equal(run('workspace_offer', JSON.stringify({ ...offer, served: 'remote' })), 'refused');
    const response = workspaceRequest();
    assert.equal(compileValidator(WorkspaceRequestResponse)(response).ok, true);
    assert.deepEqual(
      JSON.parse(
        run('workspace_read', JSON.stringify(response), requestId, JSON.stringify(nativeContext)),
      ),
      response.request,
    );
    for (const field of [
      'journal_id',
      'project_id',
      'workstream_id',
      'execution_id',
      'request_id',
    ] as const) {
      const foreign = structuredClone(response);
      foreign.request[field] = '0192f1a0-0000-7000-8000-000000000099';
      assert.equal(
        run('workspace_read', JSON.stringify(foreign), requestId, JSON.stringify(nativeContext)),
        'refused',
      );
    }
    const malformed = structuredClone(response);
    malformed.request.reply.epistemic = 'observed';
    assert.equal(
      run('workspace_read', JSON.stringify(malformed), requestId, JSON.stringify(nativeContext)),
      'refused',
    );
    assert.equal(
      run(
        'workspace_read',
        JSON.stringify({ ...response, unexpected: true }),
        requestId,
        JSON.stringify(nativeContext),
      ),
      'refused',
    );
  });
  test('native exact command recovery distinguishes creation, refusal, unknown effect and wrong identity without success fallback', () => {
    const value = workspaceCommand();
    const read = (body: unknown) =>
      run(
        'workspace_command',
        JSON.stringify(body),
        commandId,
        'null',
        JSON.stringify(nativeContext),
        'workspace.request.submit',
      );
    const created = JSON.parse(read(value));
    assert.equal(created.settled, true);
    assert.equal(created.request_id, requestId);
    assert.match(created.message, /not software completion/);
    for (const field of ['command_id', 'project_id', 'workstream_id', 'execution_id'] as const) {
      const foreign = structuredClone(value);
      foreign.command[field] = '0192f1a0-0000-7000-8000-000000000099';
      assert.equal(read(foreign), 'refused');
    }
    assert.equal(read({ ...value, journal_id: '0192f1a0-0000-7000-8000-000000000099' }), 'refused');
    assert.equal(
      read({ ...value, command: { ...value.command, command_type: 'execution.send_instruction' } }),
      'refused',
    );
    assert.equal(read({ ...value, command: { ...value.command, result: null } }), 'refused');
    for (const effect of ['none', 'unknown']) {
      const failed = {
        ...value,
        command: {
          ...value.command,
          status: 'failed',
          result: null,
          failure: { code: 'synthetic_failure', message: 'Synthetic failure.', effect },
        },
      };
      assert.equal(JSON.parse(read(failed)).settled, effect === 'none');
    }
    assert.equal(
      read({ ...value, command: { ...value.command, status: 'not_a_status' } }),
      'refused',
    );
  });
  test('native workspace POST proof requires persisted original identity and permits only one attempt', async () => {
    const server = await listener((_text, socket) =>
      socket.end(response(JSON.stringify({ synthetic: true }))),
    );
    const envelope = JSON.parse(
      run(
        'command',
        'workspace.request.submit',
        JSON.stringify({
          project_id: projectId,
          workstream_id: workId,
          execution_id: executionId,
          text: 'Synthetic authorized request.',
        }),
      ),
    );
    assert.equal(parseCommandEnvelope(envelope).ok, true);
    try {
      const target = pairing(server.port, server.pin);
      assert.equal(run('workspace_dispatch', JSON.stringify(envelope), target, 'fail'), 'not_sent');
      assert.equal(server.heard.length, 0);
      assert.equal(
        run('workspace_dispatch', JSON.stringify(envelope), target, 'phasefail'),
        'not_sent',
      );
      assert.equal(server.heard.length, 0);
      const sent = await runAsync('workspace_dispatch', JSON.stringify(envelope), target, 'save');
      assert.equal(sent.persisted, true);
      assert.equal(sent.status, 200);
      assert.equal(server.heard.length, 1);
      assert.match(server.heard[0] ?? '', /^POST \/api\/commands /);
    } finally {
      await server.close();
    }
  });
  test('native duplicate completed ACK never imports a reference when exact original-owner command GET is unavailable', async () => {
    const receipt = workspaceCommand();
    const server = await listener((request, socket) =>
      socket.end(
        request.startsWith('POST ')
          ? response(JSON.stringify({ disposition: 'duplicate', command: receipt.command }))
          : response(
              JSON.stringify({
                code: 'not_found',
                message: 'Unavailable for this original sender.',
              }),
              404,
            ),
      ),
    );
    try {
      const envelope = JSON.parse(
        run(
          'command',
          'workspace.request.submit',
          JSON.stringify({
            project_id: projectId,
            workstream_id: workId,
            execution_id: executionId,
            text: 'Synthetic request.',
          }),
        ),
      );
      envelope.command_id = commandId;
      const result = await runAsync(
        'workspace_dispatch',
        JSON.stringify(envelope),
        pairing(server.port, server.pin),
        'save',
        JSON.stringify(nativeContext),
      );
      assert.equal(result.settled, false);
      assert.equal(result.request_id, null);
      assert.equal(server.heard.filter((value) => value.startsWith('POST ')).length, 1);
      assert.equal(
        server.heard.filter((value) =>
          value.startsWith(`GET /api/workspace/commands/${commandId} `),
        ).length,
        1,
      );
    } finally {
      await server.close();
    }
  });
  test('native send phases preserve partial-store identities, permit only exact unsent cleanup and never downgrade unknown after restart', () => {
    assert.equal(
      run('workspace_phases', JSON.stringify(fixtures.cases[0]?.value), pairing(1, 'a'.repeat(64))),
      'true',
    );
  });
  test('native private write refuses success before record sync rename and containing directory sync', () => {
    assert.equal(run('directory_sync'), 'true');
    const source = readFileSync(join(SOURCES, 'PanelFiles.java'), 'utf8');
    assert.match(source, /PanelAtomicWrite\.commit/);
    assert.match(source, /Os\.fsync\(parent\)/);
    assert.match(source, /O_RDONLY\|OsConstants\.O_NOFOLLOW\|OsConstants\.O_NONBLOCK/);
    assert.match(source, /S_ISDIR\(held\.st_mode\)/);
  });
  test('native earlier-request word copy cannot replace an unresolved current send', () => {
    const current = {
      request_id: requestId,
      pending_command_id: commandId,
      text: 'Current words.',
      spoken: false,
      part: 0,
    };
    const earlier = {
      request_id: '0192f1a0-0000-7000-8000-000000000009',
      pending_command_id: null,
      text: 'Earlier words.',
      spoken: true,
      part: 4,
    };
    assert.equal(
      run('workspace_copy', JSON.stringify(current), JSON.stringify(earlier)),
      'refused',
    );
    assert.equal(current.pending_command_id, commandId);
    assert.deepEqual(
      JSON.parse(
        run(
          'workspace_copy',
          JSON.stringify({ ...current, pending_command_id: null }),
          JSON.stringify(earlier),
        ),
      ),
      { request_id: null, pending_command_id: null, text: 'Earlier words.', spoken: true, part: 0 },
    );
  });

  test('native wait text stops on completion and respects still, visibility and system motion', () => {
    assert.equal(run('wait_state'), 'true');
  });

  test('all production sources compile against Android API and are explicitly covered', () => {
    const output = mkdtempSync(join(tmpdir(), 'halcyonic-panel-api-'));
    try {
      for (const development of [false, true])
        execFileSync(join(BIN, 'javac'), [
          '--release',
          '11',
          '-cp',
          JAR,
          '-d',
          output,
          ...[...PLAIN, ...ANDROID_ONLY].map((p) => join(SOURCES, p)),
          ...(development ? DEVELOPMENT_ONLY.map((p) => join(DEVELOPMENT, p)) : []),
        ]);
    } finally {
      rmSync(output, { recursive: true, force: true });
    }
    assert.deepEqual(readdirSync(SOURCES).sort(), [...PLAIN, ...ANDROID_ONLY].sort());
    assert.deepEqual(readdirSync(DEVELOPMENT).sort(), DEVELOPMENT_ONLY);
  });
  for (const fixture of fixtures.cases)
    test(`shared continuation: ${fixture.name}`, () =>
      assert.equal(run('continuation', JSON.stringify(fixture.value)), String(fixture.valid)));
  test('shared Unicode limits count codepoints on both independent draft editors', () => {
    const sample = fixtures.cases[0]?.value as Record<string, unknown>;
    for (const vector of fixtures.text_limits) {
      for (const field of ['instruction', 'request']) {
        const draft = { text: vector.symbol.repeat(vector.count), spoken: false, part: 0 };
        const value = {
          ...sample,
          [field]:
            field === 'request' ? { ...draft, request_id: null, pending_command_id: null } : draft,
        };
        assert.equal(run('continuation', JSON.stringify(value)), String(vector.valid));
        if (vector.valid) {
          const stored = run(
            'drafts',
            JSON.stringify({ version: 1, drafts: [] }),
            JSON.stringify([value]),
          );
          const retained = JSON.parse(stored).drafts[0];
          assert.deepEqual(retained, value);
          assert.ok(Buffer.byteLength(JSON.stringify(retained), 'utf8') < 256 * 1024);
          assert.equal(run('continuation', JSON.stringify(retained)), 'true');
        }
      }
    }
  });
  for (const fixture of fixtures.identities)
    test(`shared connection identity: ${fixture.name}`, () =>
      assert.equal(run('identity', JSON.stringify(fixture)), fixture.connection_key));
  test('strict continuation rejects duplicates, trailing data, deep data and excess bytes', () => {
    const value = JSON.stringify(fixtures.cases[0]?.value);
    for (const invalid of [
      value.replace('"version":1', '"version":1,"version":1'),
      `${value}{}`,
      `${'['.repeat(34)}0${']'.repeat(34)}`,
      `${' '.repeat(256 * 1024 + 1)}${value}`,
    ])
      assert.equal(run('continuation', invalid), 'false');
  });
  test('Unity view storage consumes shared continuation parity without adding a record-count cap', () => {
    for (const fixture of fixtures.cases) {
      const record = fixture.value as { handoff_id: string };
      assert.equal(
        run(
          'viewstore',
          JSON.stringify({ version: 1, selected: record.handoff_id, views: [record] }),
        ),
        String(fixture.valid),
        fixture.name,
      );
    }
    const template = fixtures.cases[0]?.value as Record<string, unknown>;
    const views = Array.from({ length: 24 }, (_, i) => ({
      ...template,
      handoff_id: `0192f1a0-0000-4000-8000-${i.toString(16).padStart(12, '0')}`,
    }));
    assert.equal(
      run('viewstore', JSON.stringify({ version: 1, selected: views[0]?.handoff_id, views })),
      'true',
    );
  });
  test('Unity view storage rejects duplicate records, absent selection, extra fields and syntax coercions', () => {
    const view = fixtures.cases[0]?.value as Record<string, unknown>;
    for (const value of [
      { version: 1, selected: view.handoff_id, views: [view, view] },
      { version: 1, selected: '0192f1a0-0000-4000-8000-000000000099', views: [view] },
      { version: 1, selected: null, views: [], confirmation: true },
      { version: 1.0, selected: null, views: '[]' },
    ])
      assert.equal(run('viewstore', JSON.stringify(value)), 'false');
    const empty = '{"version":1,"selected":null,"views":[]}';
    for (const value of [
      empty.replace('"version":1', '"version":1,"version":1'),
      empty.replace('"version":1', '"version":1.0'),
      `${empty}[]`,
    ])
      assert.equal(run('viewstore', value), 'false');
  });
  test('Unity view storage enforces the aggregate UTF-8 byte boundary', () => {
    assert.equal(run('viewstore_bound', JSON.stringify(fixtures.cases[0]?.value)), 'true');
  });
  test('Unity JNI storage uses one fixed private record and distinguishes actual absent from failed IO', () => {
    const files = readFileSync(join(SOURCES, 'PanelFiles.java'), 'utf8');
    const bridge = readFileSync(join(SOURCES, 'PanelBridge.java'), 'utf8');
    assert.match(files, /UNITY_VIEWS = "halcyonic-unity-workspace-views.json"/);
    assert.match(files, /error.errno == OsConstants.ENOENT/);
    assert.match(files, /readLocked\(UNITY_VIEWS, PanelViewStore.MAXIMUM_BYTES\)/);
    assert.match(files, /catch \(RecordAbsent missing\) \{ return null; \}/);
    assert.match(files, /name.equals\(UNITY_VIEWS\)\) PanelViewStore.validate\(json\)/);
    assert.match(files, /O_NOFOLLOW \| OsConstants.O_NONBLOCK/);
    assert.match(files, /O_EXCL \| OsConstants.O_NOFOLLOW/);
    assert.match(files, /stat.st_size > maximum/);
    assert.match(files, /byte\[maximum \+ 1\]/);
    assert.match(bridge, /readWorkspaceViews\(Activity activity\) throws IOException/);
    assert.match(bridge, /write\(PanelFiles.UNITY_VIEWS, json\)/);
  });
  test('identity rejects unsafe endpoint material, including non-ASCII and local host alternatives', () => {
    for (const host of [
      'computer.local/path',
      'bad@host',
      '[::1]extra',
      'é.local',
      'host\nname',
      'a'.repeat(254),
    ])
      assert.equal(run('identity', JSON.stringify({ ...fixtures.identities[1], host })), 'refused');
    assert.equal(
      run('identity', JSON.stringify({ ...fixtures.identities[0], host: 'localhost' })),
      'refused',
    );
  });
  test('actual native editor insertion counts canonical codepoints and never splits a supplementary pair', () => {
    assert.equal(run('input_text', '😀'.repeat(16001), '', '0', '0'), String(32002));
    assert.equal(run('input_text', '😀'.repeat(32001), '', '0', '0'), String(64000));
    assert.equal(run('input_text', '😀🙂', 'x'.repeat(31999), '0', '0'), '2');
    assert.equal(run('input_text', '😀🙂', 'x'.repeat(32000), '0', '1'), '2');
  });
  test('outside text renders invisibles literally without markup interpretation', () => {
    assert.equal(run('text', 'x\u202Ey <b>yes</b>'), 'x‹U+202E›y <b>yes</b>');
    assert.equal(run('text', 'lone \uD800'), 'lone ‹U+D800›');
  });
  test('actual Activity authority state refuses retarget, dismissed callbacks and multi-resume/focus revival', () => {
    assert.equal(run('ui_state'), 'true');
  });
  test('consumed compact authority preserves failed transitions and expires hidden action tickets', () => {
    assert.equal(run('compact_ui'), 'true');
    const activity = readFileSync(join(SOURCES, 'AgentPanelActivity.java'), 'utf8');
    assert.match(activity, /authority\.changePresentation\(compact, saveState\(\)\)/);
    assert.match(activity, /authority\.commandsAllowed\(\)/);
    assert.match(activity, /authority\.readProjection\(\)/);
    assert.match(activity, /generation == offeredGeneration/);
    assert.match(activity, /putBoolean\("halcyonic\.panel\.compact", compactPanel\)/);
  });
  test('disconnected connection recovery retains local focus authority without enabling commands', () => {
    assert.equal(run('connection_recovery'), 'true');
    const activity = readFileSync(join(SOURCES, 'AgentPanelActivity.java'), 'utf8');
    assert.match(activity, /!authority\.connectionInteract\(0\) \|\| !saveState\(\)/);
    for (const label of [
      'Use paired computer',
      'Use USB connection (development)',
      'Preview synthetic panel (screenshots allowed)',
    ]) {
      assert.ok(activity.includes(`connectionButton(choices, "${label}"`));
    }
    assert.match(activity, /localConnection \? !busy && authority\.connectionInteract\(id\)/);
  });
  test('Activity-used navigation continuation restarts fresh reads and pauses them without granting actions', () => {
    assert.equal(run('navigation_refresh'), 'true');
    const activity = readFileSync(join(SOURCES, 'AgentPanelActivity.java'), 'utf8');
    assert.match(activity, /authority\.navigated\(\(\) -> poll\(true\)\)/);
    assert.match(activity, /continuation = null; selectionChanged\(\);/);
    assert.match(activity, /restoreDraft\(\); selectionChanged\(\);/);
  });
  test('the same selected expanded synthetic preview resumes with its immutable projection ready', () => {
    assert.equal(run('preview_resume'), 'true');
    const activity = readFileSync(join(SOURCES, 'AgentPanelActivity.java'), 'utf8');
    assert.match(
      activity,
      /if \(preview\) \{ if \(snapshot != null && content != null\) \{ authority\.projected\(\); show\(\); \} return; \}/,
    );
  });
  test('compact task projection uses the selected tuple and separates live, recorded and simulated provenance', () => {
    const base = model();
    const selected = {
      ...base,
      workstreams: base.workstreams.map((work) => ({
        ...work,
        title: 'Review complete task identity',
        status: 'running',
      })),
      executions: base.executions.map((run) => ({
        ...run,
        runtime: { ...run.runtime, synthetic: true },
      })),
    };
    const read = (snapshot: unknown, project = projectId, work = workId) =>
      run('monitor', JSON.stringify(snapshot), project, work);
    assert.deepEqual(JSON.parse(read(selected)), {
      project: 'Fixture',
      task: 'Review complete task identity',
      state: 'Working',
      source: 'Live host state · simulated agent',
      permissions: 0,
      questions: 0,
      has_agent: true,
    });
    const recorded = { ...selected, journal: { ...selected.journal, origin: 'fixture' } };
    assert.equal(JSON.parse(read(recorded)).source, 'Recorded state · simulated agent');
    const pending = {
      ...selected,
      executions: selected.executions.map((execution) => ({
        ...execution,
        status: 'waiting_for_human',
        runtime: { ...execution.runtime, synthetic: false },
        pending_approvals: [{ approval_id: 'permission1' }, { approval_id: 'permission2' }],
        pending_questions: [{ question_id: 'question1', prompts: [{ key: 'a' }, { key: 'b' }] }],
      })),
    };
    assert.deepEqual(JSON.parse(read(pending)), {
      project: 'Fixture',
      task: 'Review complete task identity',
      state: 'Needs your decision',
      source: 'Live host state',
      permissions: 2,
      questions: 1,
      has_agent: true,
    });
    const unknown = {
      ...selected,
      executions: selected.executions.map((execution) => ({ ...execution, status: 'unknown' })),
    };
    assert.equal(JSON.parse(read(unknown)).state, "Can't tell yet");
    const finished = {
      ...selected,
      executions: selected.executions.map((execution) => ({ ...execution, status: 'completed' })),
    };
    assert.equal(JSON.parse(read(finished)).state, 'Completed');
    const noAgent = {
      ...selected,
      workstreams: selected.workstreams.map((work) => ({
        ...work,
        status: 'created',
        current_execution_id: null,
      })),
      executions: [],
    };
    assert.equal(JSON.parse(read(noAgent)).has_agent, false);
    assert.equal(JSON.parse(read(noAgent)).state, 'Not started');
    assert.equal(read(selected, '0192f1a0-0000-7000-8000-000000000099'), 'refused');
    assert.equal(read(selected, projectId, '0192f1a0-0000-7000-8000-000000000099'), 'refused');
    assert.equal(read({ ...selected, projects: [] }), 'refused');
    assert.equal(
      read({
        ...selected,
        workstreams: selected.workstreams.map((work) => ({
          ...work,
          project_id: '0192f1a0-0000-7000-8000-000000000099',
        })),
      }),
      'refused',
    );
  });
  test('compact transitions preserve both independent drafts and the exact pending command reference', () => {
    const first = fixtures.cases[0]?.value as Record<string, unknown>;
    const value = {
      ...first,
      instruction: { text: 'Keep this ordinary instruction.', spoken: true, part: 2 },
      surface: 'request',
      request: {
        request_id: '0192f1a0-0000-7000-8000-000000000008',
        pending_command_id: '550e8400-e29b-41d4-a716-446655440000',
        text: 'Keep this independent workspace direction.',
        spoken: false,
        part: 3,
      },
    };
    const saved = JSON.parse(
      run('drafts', JSON.stringify({ version: 1, drafts: [] }), JSON.stringify([value])),
    );
    assert.equal(run('compact_ui'), 'true');
    const after = JSON.parse(run('drafts', JSON.stringify(saved), JSON.stringify(saved.drafts)));
    assert.deepEqual(after.drafts, [value]);
    const activity = readFileSync(join(SOURCES, 'AgentPanelActivity.java'), 'utf8');
    const compact = activity.slice(
      activity.indexOf('private void changePresentation'),
      activity.indexOf('private String monitorFreshness'),
    );
    assert.doesNotMatch(
      compact,
      /requestPending\s*=|requestId\s*=|requestText\s*=|draft\s*=|sendWorkspace\(|POST/,
    );
    const monitor = activity.slice(
      activity.indexOf('private void monitorTask'),
      activity.indexOf('private void cancelNetwork'),
    );
    assert.doesNotMatch(monitor, /EditText|voiceEditor|gatedButton|sendWorkspace|\bsend\(/);
  });
  test('failed pause saves retain volatile ordinary drafts and incoming context waits for its fresh projection', () => {
    const paired = fixtures.identities[1];
    assert.ok(paired);
    const target = JSON.stringify({
      host: paired.host,
      port: paired.port,
      certificate_sha256: paired.certificate_sha256,
      device_id: paired.device_id,
      credential: paired.credential,
    });
    const record = {
      ...(fixtures.cases[0]?.value as Record<string, unknown>),
      connection_key: paired.connection_key,
    };
    assert.equal(
      run('restore_state', JSON.stringify(record), target, JSON.stringify(model())),
      'true',
    );
  });
  test('real panel never clears secure window flags and preview is a separate internal development Activity', () => {
    const activity = readFileSync(join(SOURCES, 'AgentPanelActivity.java'), 'utf8');
    assert.doesNotMatch(activity, /clearFlags/);
    const fixedInitialization = activity.slice(
      activity.indexOf('if (syntheticSurface()) {'),
      activity.indexOf('// The real panel'),
    );
    assert.match(fixedInitialization, /if \(!developmentAllowed\)/);
    assert.match(
      fixedInitialization,
      /getAssets\(\).open\("halcyonic-native-panel-preview.json"\)/,
    );
    assert.doesNotMatch(
      fixedInitialization,
      /new PanelFiles|files\.|loadSaved|PanelTransport|PanelIdentity/,
    );
    const preview = readFileSync(join(DEVELOPMENT, 'PanelPreviewActivity.java'), 'utf8');
    assert.match(preview, /extends AgentPanelActivity/);
    assert.doesNotMatch(preview, /PanelFiles|PanelTransport|loadSaved|connection\(/);
    const packaging = readFileSync(
      join(ROOT, 'apps/xr/Assets/Halcyonic/Editor/PanelInBuilds.cs'),
      'utf8',
    );
    assert.match(packaging, /PreviewManifest[\s\S]*GlanceInDevelopmentBuilds.Include/);
    assert.match(packaging, /SetAttribute\("exported", android, "false"\)/);
    assert.doesNotMatch(
      readFileSync(join(ROOT, 'apps/xr/Assets/Plugins/Android/AndroidManifest.xml'), 'utf8'),
      /PanelPreviewActivity/,
    );
  });
  test('offline visual fixture conforms to Snapshot and contains only synthetic executions', () => {
    const snapshot = compileValidator(Snapshot)(
      JSON.parse(readFileSync(join(ROOT, 'fixtures/workspace/native-panel.json'), 'utf8')),
    );
    assert.equal(snapshot.ok, true);
    if (!snapshot.ok) return;
    assert.equal(snapshot.value.journal.origin, 'fixture');
    assert.ok(snapshot.value.executions.length >= 3);
    assert.ok(snapshot.value.executions.every((run) => run.runtime.synthetic));
    assert.ok(snapshot.value.executions.some((run) => run.pending_approvals.length > 0));
    assert.ok(snapshot.value.executions.some((run) => run.pending_questions.length > 0));
  });
  test('failed or malformed acknowledgements never fall through to acceptance', () => {
    assert.match(run('status', JSON.stringify({ status: 'accepted', failure: null })), /accepted/);
    assert.match(
      run('status', JSON.stringify({ status: 'completed', failure: null })),
      /confirmed/,
    );
    for (const command of [
      { status: 'failed', failure: { effect: 'unknown' } },
      { status: 'failed', failure: null },
    ]) {
      const text = run('status', JSON.stringify(command));
      assert.match(text, /failed.*unknown/);
      assert.doesNotMatch(text, /accepted|confirmed/);
    }
    assert.equal(
      run('status', JSON.stringify({ status: 'failed', failure: { effect: 'none' } })),
      'The request failed.',
    );
    for (const status of ['unexpected', null, 42])
      assert.doesNotMatch(
        run('status', JSON.stringify({ status, failure: null })),
        /accepted|confirmed/,
      );
  });
  test('legacy native continuation migrates only request and surface and preserves ordinary words', () => {
    const first = fixtures.cases[0]?.value as Record<string, unknown>;
    const legacy = { ...first };
    delete legacy.request;
    delete legacy.surface;
    const saved = JSON.parse(
      run('drafts', JSON.stringify({ version: 1, drafts: [] }), JSON.stringify([legacy])),
    );
    assert.deepEqual(saved.drafts[0], { ...legacy, request: null, surface: 'task' });
    assert.equal(run('continuation', JSON.stringify({ ...legacy, unexpected: true })), 'false');
    const missing = { ...legacy };
    delete missing.instruction;
    assert.equal(run('continuation', JSON.stringify(missing)), 'false');
  });
  test('native request-only drafts preserve exact host reference, spoken origin and part independently of instruction', () => {
    const first = fixtures.cases[0]?.value as Record<string, unknown>;
    const value = {
      ...first,
      instruction: null,
      request: {
        request_id: '0192f1a0-0000-7000-8000-000000000008',
        pending_command_id: null,
        text: 'Investigate this synthetic task.',
        spoken: true,
        part: 2,
      },
    };
    const saved = JSON.parse(
      run('drafts', JSON.stringify({ version: 1, drafts: [] }), JSON.stringify([value])),
    );
    assert.deepEqual(saved.drafts, [value]);
    assert.equal(
      run(
        'continuation',
        JSON.stringify({ ...value, request: { ...value.request, request_id: 'not-a-request' } }),
      ),
      'false',
    );
    assert.equal(
      run('continuation', JSON.stringify({ ...value, request: { ...value.request, armed: true } })),
      'false',
    );
  });
  test('native pending-only command references survive storage and narrowly migrate older request drafts', () => {
    const first = fixtures.cases[0]?.value as Record<string, unknown>;
    const value = {
      ...first,
      instruction: null,
      request: {
        request_id: null,
        pending_command_id: '550e8400-e29b-41d4-a716-446655440000',
        text: '',
        spoken: false,
        part: 0,
      },
    };
    const saved = JSON.parse(
      run('drafts', JSON.stringify({ version: 1, drafts: [] }), JSON.stringify([value])),
    );
    assert.deepEqual(saved.drafts, [value]);
    const { pending_command_id: omitted, ...legacyRequest } = value.request;
    assert.ok(omitted);
    const legacy = { ...value, request: { ...legacyRequest, text: 'Retained words' } };
    const migrated = JSON.parse(
      run('drafts', JSON.stringify({ version: 1, drafts: [] }), JSON.stringify([legacy])),
    );
    assert.deepEqual(migrated.drafts[0].request, { ...legacy.request, pending_command_id: null });
  });
  test('native request reference copies only across the original complete identity tuple', () => {
    const first = fixtures.cases[0]?.value as Record<string, unknown>;
    const value = {
      ...first,
      request: {
        request_id: '0192f1a0-0000-7000-8000-000000000008',
        pending_command_id: null,
        text: 'Synthetic direction',
        spoken: false,
        part: 0,
      },
    };
    const tuple = [
      'connection_key',
      'journal_id',
      'project_id',
      'workstream_id',
      'execution_id',
    ].map((key) => String(value[key as keyof typeof value]));
    assert.deepEqual(
      JSON.parse(run('request_copy', JSON.stringify(value), ...tuple)),
      value.request,
    );
    for (let i = 0; i < tuple.length; i++) {
      const changed = [...tuple];
      changed[i] = 'different';
      assert.equal(run('request_copy', JSON.stringify(value), ...changed), 'null');
    }
  });
  test('native draft navigation retains separate tasks and earlier runs without implicit retargeting', () => {
    const first = fixtures.cases[0]?.value as Record<string, unknown>;
    const second = {
      ...first,
      execution_id: '0192f1a0-0000-7000-8000-000000000005',
      instruction: { text: 'A separate run', spoken: false, part: 0 },
    };
    const anotherTask = {
      ...first,
      workstream_id: '0192f1a0-0000-7000-8000-000000000006',
      instruction: { text: 'Another task draft', spoken: false, part: 0 },
    };
    const saved = run(
      'drafts',
      JSON.stringify({ version: 1, drafts: [] }),
      JSON.stringify([first, second, anotherTask]),
    );
    assert.equal(JSON.parse(saved).drafts.length, 3);
    const earlier = JSON.parse(
      run(
        'draft_earlier',
        saved,
        String(first.connection_key),
        String(first.journal_id),
        String(first.workstream_id),
        String(second.execution_id),
      ),
    );
    assert.deepEqual(earlier, [first]);
    assert.deepEqual(
      JSON.parse(
        run(
          'draft_earlier',
          saved,
          'b'.repeat(64),
          String(first.journal_id),
          String(first.workstream_id),
          String(second.execution_id),
        ),
      ),
      [],
    );
    assert.deepEqual(
      JSON.parse(
        run(
          'draft_earlier',
          saved,
          String(first.connection_key),
          '0192f1a0-0000-7000-8000-000000000007',
          String(first.workstream_id),
          String(second.execution_id),
        ),
      ),
      [],
    );
    const updated = run(
      'drafts',
      saved,
      JSON.stringify([{ ...second, instruction: { text: 'Edited', spoken: false, part: 0 } }]),
    );
    assert.equal(JSON.parse(updated).drafts.length, 3);
    assert.deepEqual(JSON.parse(updated).drafts[0], first);
    assert.deepEqual(JSON.parse(updated).drafts[1], anotherTask);
    const cleared = run(
      'drafts',
      updated,
      JSON.stringify([{ ...second, instruction: { text: '', spoken: false, part: 0 } }]),
    );
    assert.deepEqual(JSON.parse(cleared).drafts, [first, anotherTask]);
    const many = Array.from({ length: 20 }, (_, i) => ({
      ...first,
      execution_id: `0192f1a0-0000-7000-8000-${(i + 10).toString(16).padStart(12, '0')}`,
    }));
    assert.equal(
      JSON.parse(run('drafts', JSON.stringify({ version: 1, drafts: [] }), JSON.stringify(many)))
        .drafts.length,
      20,
    );
  });
  test('continuation never restores instruction to a stale, unrelated or rotated target', () => {
    const value = model();
    const paired = fixtures.identities[1];
    assert.ok(paired);
    const target = JSON.stringify({
      host: paired.host,
      port: paired.port,
      certificate_sha256: paired.certificate_sha256,
      device_id: paired.device_id,
      credential: paired.credential,
    });
    const first = fixtures.cases[0]?.value as Record<string, unknown>;
    const continuation = { ...first, connection_key: paired.connection_key };
    assert.equal(
      run('matches', JSON.stringify(continuation), target, JSON.stringify(value)),
      'true',
    );
    assert.equal(
      run(
        'matches',
        JSON.stringify({ ...continuation, execution_id: projectId }),
        target,
        JSON.stringify(value),
      ),
      'false',
    );
    assert.equal(
      run(
        'matches',
        JSON.stringify({ ...continuation, project_id: executionId }),
        target,
        JSON.stringify(value),
      ),
      'false',
    );
    assert.equal(
      run(
        'matches',
        JSON.stringify({ ...continuation, connection_key: 'a'.repeat(64) }),
        target,
        JSON.stringify(value),
      ),
      'false',
    );
  });
  test('aggregate draft byte boundary refuses new data while preserving every saved draft', () => {
    assert.equal(run('draft_bound', JSON.stringify(fixtures.cases[0]?.value)), 'true');
  });
  test('capabilities and native current execution gate instruction and stop', () => {
    const value = model();
    assert.ok(value.runtimes[0]);
    assert.ok(value.executions[0]);
    assert.equal(run('model', JSON.stringify(value), workId, 'instruction'), 'true');
    value.runtimes[0].capabilities.instruct_while_running = false;
    assert.equal(run('model', JSON.stringify(value), workId, 'instruction'), 'false');
    value.executions[0].status = 'completed';
    assert.equal(run('model', JSON.stringify(value), workId, 'instruction'), 'true');
    assert.equal(run('model', JSON.stringify(value), workId, 'interrupt'), 'false');
    value.executions[0].workstream_id = projectId;
    assert.equal(run('model', JSON.stringify(value), workId, 'instruction'), 'false');
  });
  test('shortened approval permits deny only, changed request loses approval authority', () => {
    const value = model();
    assert.ok(value.runtimes[0]);
    assert.ok(value.executions[0]);
    const request = {
      approval_id: 'fixture',
      approvable: false,
      subject: { kind: 'tool_use', tool_name: 'shell', summary: 'Read every word.' },
    };
    value.executions[0].pending_approvals.push(request);
    assert.equal(
      run('model', JSON.stringify(value), workId, 'approval', JSON.stringify(request), 'approve'),
      'false',
    );
    assert.equal(
      run('model', JSON.stringify(value), workId, 'approval', JSON.stringify(request), 'deny'),
      'true',
    );
    assert.equal(run('model', JSON.stringify(value), workId, 'instruction'), 'false');
    assert.equal(
      run(
        'model',
        JSON.stringify(value),
        workId,
        'approval',
        JSON.stringify({ ...request, approvable: true }),
        'approve',
      ),
      'false',
    );
  });
  test('complete questions, choices, free text, secret refusal and stale request all use actual model', () => {
    const value = model();
    assert.ok(value.runtimes[0]);
    assert.ok(value.executions[0]);
    const prompt = {
      key: 'first',
      header: null,
      text: 'Choose one',
      options: [
        { label: 'A', description: 'First' },
        { label: 'B', description: null },
      ],
      multiple: false,
      free_text: true,
      secret: false,
    };
    const question = { question_id: 'fixture', answerable: true, prompts: [prompt] };
    value.executions[0].pending_questions.push(question);
    const answer = [{ key: 'first', selected: ['A'], text: null }];
    const ask = (q = question, a: unknown = answer) =>
      run('model', JSON.stringify(value), workId, 'answer', JSON.stringify(q), JSON.stringify(a));
    assert.equal(ask(), 'true');
    assert.equal(ask(question, [{ key: 'first', selected: ['A', 'B'], text: null }]), 'false');
    assert.equal(ask(question, [{ key: 'first', selected: ['not offered'], text: null }]), 'false');
    assert.equal(ask(question, []), 'false');
    assert.equal(ask(question, [{ key: 'first', selected: [], text: 'My own answer' }]), 'true');
    prompt.secret = true;
    assert.equal(ask(), 'false');
    prompt.secret = false;
    question.answerable = false;
    assert.equal(ask(), 'false');
    question.answerable = true;
    assert.equal(ask({ ...question, question_id: 'changed' }), 'false');
    prompt.options.push({ label: 'A', description: 'Ambiguous' });
    assert.equal(ask(), 'false');
  });
  test('all native command envelopes validate against the actual TypeBox contract', () => {
    for (const [type, payload] of [
      ['execution.send_instruction', { execution_id: executionId, text: 'Explicit instruction' }],
      ['execution.interrupt', { execution_id: executionId }],
      [
        'execution.respond_to_approval',
        { execution_id: executionId, approval_id: 'fixture', decision: 'deny', message: null },
      ],
      [
        'execution.answer_question',
        {
          execution_id: executionId,
          question_id: 'fixture',
          answers: [{ key: 'first', selected: ['A'], text: null }],
        },
      ],
    ] as const)
      assert.equal(
        parseCommandEnvelope(JSON.parse(run('command', type, JSON.stringify(payload)))).ok,
        true,
      );
  });
  test('pin mismatch and pairing replacement send no HTTP headers or credential', async () => {
    const server = await listener((_text, socket) => socket.end(response('{}')));
    try {
      const wrong = await runAsync(
        'request',
        pairing(server.port, 'a'.repeat(64)),
        'GET',
        '/api/snapshot',
        'null',
      );
      assert.equal(wrong.code, 'certificate_changed');
      assert.equal(wrong.unknown, false);
      assert.equal(server.heard.length, 0);
      const rotated = await runAsync(
        'request',
        pairing(server.port, server.pin),
        'GET',
        '/api/snapshot',
        'null',
        '3',
      );
      assert.equal(rotated.code, 'pairing_changed');
      assert.equal(server.heard.length, 0);
    } finally {
      await server.close();
    }
  });
  test('redirects, malformed lengths, chunking and partial command replies are refused without retry', async () => {
    for (const answer of [
      'HTTP/1.1 302 Found\r\nLocation: https://elsewhere.invalid/\r\nContent-Length: 0\r\n\r\n',
      'HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nContent-Length: 2\r\n\r\n{}',
      'HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n',
      response('{}').slice(0, -1),
    ]) {
      const server = await listener((_text, socket) => socket.end(answer));
      try {
        const result = await runAsync(
          'request',
          pairing(server.port, server.pin),
          'POST',
          '/api/commands',
          '{}',
        );
        assert.ok(result.code);
        assert.equal(result.unknown, true);
        assert.equal(server.heard.length, 1);
      } finally {
        await server.close();
      }
    }
  });
  test('explicit development USB proves the real server before snapshot/events/commands, with no fallback', async () => {
    const server = await startTestServer();
    try {
      const target = JSON.stringify({ port: server.port, token: server.token });
      const snapshot = await runAsync('request_usb', target, 'GET', '/api/snapshot', 'null');
      assert.equal(snapshot.status, 200);
      const events = await runAsync(
        'request_usb',
        target,
        'GET',
        `/api/events?after=0&limit=50&workstream_id=${workId}`,
        'null',
      );
      assert.equal(events.status, 200);
      const command = run(
        'command',
        'execution.interrupt',
        JSON.stringify({ execution_id: executionId }),
      );
      const refused = await runAsync('request_usb', target, 'POST', '/api/commands', command);
      assert.equal(refused.status, 422);
      const bad = await runAsync(
        'request_usb',
        JSON.stringify({ port: server.port, token: 'wrong_fixture_token_01234567890123456789' }),
        'GET',
        '/api/snapshot',
        'null',
      );
      assert.equal(bad.code, 'local_server_unproved');
    } finally {
      await server.stop();
    }
  });
  test('USB impostor and proof for another address or token never receive bearer', async () => {
    const token = 'fixture_owner_token_01234567890123456789';
    for (const kind of ['missing', 'other_port', 'other_token']) {
      let heard = '';
      const server = createPlainServer((socket) => {
        socket.on('error', () => {});
        socket.on('data', (bytes) => {
          heard += bytes.toString();
          const challenge = /x-halcyonic-challenge: ([0-9a-f]+)/i.exec(heard)?.[1] ?? '';
          const address = server.address();
          assert.ok(address && typeof address !== 'string');
          const proof =
            kind === 'missing'
              ? null
              : loopbackProof(
                  kind === 'other_token' ? 'different_fixture_token_0123456789012' : token,
                  `127.0.0.1:${address.port + (kind === 'other_port' ? 1 : 0)}`,
                  challenge,
                );
          socket.end(
            `HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n${proof === null ? '' : `x-halcyonic-proof: ${proof}\r\n`}\r\n{}`,
          );
        });
      });
      await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
      const address = server.address();
      assert.ok(address && typeof address !== 'string');
      try {
        const result = await runAsync(
          'request_usb',
          JSON.stringify({ port: address.port, token }),
          'GET',
          '/api/snapshot',
          'null',
        );
        assert.equal(result.code, 'local_server_unproved');
        assert.equal(result.unknown, false);
        assert.doesNotMatch(heard, /Authorization|Bearer|fixture_owner_token/);
      } finally {
        await new Promise<void>((resolve) => server.close(() => resolve()));
      }
    }
  });
  test('total deadline interrupts a blocked write after real TLS authentication, reports unknown and never retries', async () => {
    const directory = mkdtempSync(join(tmpdir(), 'halcyonic-panel-stalled-'));
    const identity = createNetworkIdentity(new Date());
    writeFileSync(join(directory, 'certificate.pem'), identity.certificate);
    writeFileSync(join(directory, 'key.pem'), identity.key, { mode: 0o600 });
    const script = `import socket,ssl,time,sys
server=socket.socket()
server.setsockopt(socket.SOL_SOCKET,socket.SO_RCVBUF,1024)
server.bind(('127.0.0.1',0)); server.listen(1)
print(server.getsockname()[1],flush=True)
context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(sys.argv[1],sys.argv[2])
connection,_=server.accept()
secure=context.wrap_socket(connection,server_side=True)
head=b''
while not head.endswith(b'\\r\\n\\r\\n'): head+=secure.recv(1)
print('head',flush=True)
time.sleep(25)
`;
    const peer = spawn(
      'python3',
      ['-u', '-c', script, join(directory, 'certificate.pem'), join(directory, 'key.pem')],
      { stdio: ['ignore', 'pipe', 'pipe'] },
    );
    let output = '';
    peer.stdout.on('data', (bytes) => {
      output += bytes.toString();
    });
    const port = await new Promise<number>((resolve, reject) => {
      peer.stdout.once('data', (bytes) => resolve(Number(bytes.toString().trim())));
      peer.once('error', reject);
      peer.once('exit', (code) => reject(new Error(`stalled peer exited ${code}`)));
    });
    try {
      assert.ok(Number.isInteger(port) && port > 0);
      const start = Date.now();
      const result = await runAsync(
        'request_stall',
        pairing(port, identity.certificateSha256),
        'POST',
        '/api/commands',
        JSON.stringify({ text: 'x'.repeat(250000) }),
      );
      const elapsed = Date.now() - start;
      assert.equal(result.code, 'request_timeout');
      assert.equal(result.unknown, true);
      assert.ok(elapsed >= 13000 && elapsed < 19000, `deadline ${elapsed}ms`);
      assert.equal(output.split('head').length - 1, 1, 'one request, no retry');
    } finally {
      peer.kill('SIGTERM');
      rmSync(directory, { recursive: true, force: true });
    }
  });
  test('snapshot, task events and commands work against the actual paired control plane; revocation applies', async () => {
    const server = await startTestServer({ network: {} });
    const feature = DEMO_WORKSTREAMS[0];
    assert.ok(feature);
    try {
      assert.ok(server.network);
      const paired = server.devices.pair(
        'Synthetic native panel test',
        server.network.identity.certificateSha256,
      );
      const target = pairing(
        server.network.target.port,
        server.network.identity.certificateSha256,
        credentialFromBytes(paired.credential),
        paired.deviceId,
      );
      const first = await runAsync('request', target, 'GET', '/api/snapshot', 'null');
      assert.equal(first.status, 200);
      const snapshot = JSON.parse(first.body as string);
      assert.ok(snapshot.journal.journal_id);
      const missing = JSON.parse(
        run('command', 'execution.interrupt', JSON.stringify({ execution_id: executionId })),
      );
      const refused = await runAsync(
        'request',
        target,
        'POST',
        '/api/commands',
        JSON.stringify(missing),
      );
      assert.equal(refused.status, 422);
      assert.equal(JSON.parse(refused.body as string).disposition, 'rejected');
      const create = await server.controlPlane.commands.submit(
        server.commands.createProject('Synthetic project'),
        'internal',
      );
      assert.ok(create.command?.result?.kind === 'project_created');
      const work = await server.controlPlane.commands.submit(
        server.commands.createWorkstream(create.command.result.project_id, feature),
        'internal',
      );
      assert.ok(work.command?.result?.kind === 'workstream_created');
      const events = await runAsync(
        'request',
        target,
        'GET',
        `/api/events?after=0&limit=50&workstream_id=${work.command.result.workstream_id}`,
        'null',
      );
      assert.equal(events.status, 200);
      assert.ok(JSON.parse(events.body as string).events[0].event.event_type);
      assert.equal(server.devices.revoke(paired.deviceId, { kind: 'local' }), 'revoked');
      const revoked = await runAsync('request', target, 'GET', '/api/snapshot', 'null');
      assert.equal(revoked.status, 401);
    } finally {
      await server.stop();
    }
  });
  test('native workspace availability and exact pending outcome consume the actual paired producer without assuming a model', async () => {
    const server = await startTestServer({ network: {} });
    const feature = DEMO_WORKSTREAMS[0];
    assert.ok(feature);
    try {
      assert.ok(server.network);
      const paired = server.devices.pair(
        'Synthetic workspace client',
        server.network.identity.certificateSha256,
      );
      const target = pairing(
        server.network.target.port,
        server.network.identity.certificateSha256,
        credentialFromBytes(paired.credential),
        paired.deviceId,
      );
      const status = await runAsync('request', target, 'GET', '/api/workspace/agent', 'null');
      assert.equal(status.status, 200);
      const offer = JSON.parse(run('workspace_offer', status.body as string));
      assert.equal(offer.availability, 'unavailable');
      const project = server.controlPlane.commands.submit(
        server.commands.createProject('Synthetic workspace request'),
        'internal',
      );
      assert.ok(project.command?.result?.kind === 'project_created');
      const work = server.controlPlane.commands.submit(
        server.commands.createWorkstream(project.command.result.project_id, feature),
        'internal',
      );
      assert.ok(work.command?.result?.kind === 'workstream_created');
      server.controlPlane.commands.submit(
        server.commands.startExecution(work.command.result.workstream_id, feature),
        'internal',
      );
      await server.time.runUntilIdle();
      const execution = server.controlPlane.projection.workstream(
        work.command.result.workstream_id,
      )?.current_execution_id;
      assert.ok(execution);
      const envelope = JSON.parse(
        run(
          'command',
          'workspace.request.submit',
          JSON.stringify({
            project_id: project.command.result.project_id,
            workstream_id: work.command.result.workstream_id,
            execution_id: execution,
            text: 'Synthetic authorized request.',
          }),
        ),
      );
      const sent = await runAsync('workspace_dispatch', JSON.stringify(envelope), target, 'save');
      assert.equal(sent.persisted, true);
      assert.equal(sent.status, 422);
      await server.time.advance(0);
      const read = await runAsync(
        'request',
        target,
        'GET',
        `/api/workspace/commands/${envelope.command_id}`,
        'null',
      );
      assert.equal(read.status, 200);
      const scope = {
        connection: 'synthetic-key',
        journal: server.controlPlane.journal.info.journal_id,
        project: project.command.result.project_id,
        work: work.command.result.workstream_id,
        execution,
      };
      const resolved = run(
        'workspace_command',
        read.body as string,
        envelope.command_id,
        'null',
        JSON.stringify(scope),
        'workspace.request.submit',
      );
      // A coordinator-unavailable rejection has no admitted target scope. It cannot settle
      // this task-bound pending reference merely because the sender can read the receipt.
      assert.equal(resolved, 'refused');
      assert.equal(server.devices.revoke(paired.deviceId, { kind: 'local' }), 'revoked');
      const refused = await runAsync(
        'request',
        target,
        'GET',
        `/api/workspace/commands/${envelope.command_id}`,
        'null',
      );
      assert.equal(refused.status, 401);
    } finally {
      await server.stop();
    }
  });
  test('all four native actions are accepted and runtime-confirmed through the actual paired producer', async () => {
    const server = await startTestServer({ network: {} });
    const feature = DEMO_WORKSTREAMS[0],
      approving = DEMO_WORKSTREAMS[2];
    assert.ok(feature && approving);
    try {
      assert.ok(server.network);
      const paired = server.devices.pair(
        'Synthetic native action test',
        server.network.identity.certificateSha256,
      );
      const target = pairing(
        server.network.target.port,
        server.network.identity.certificateSha256,
        credentialFromBytes(paired.credential),
        paired.deviceId,
      );
      const project = server.controlPlane.commands.submit(
        server.commands.createProject('Synthetic native actions'),
        'internal',
      );
      assert.ok(project.command?.result?.kind === 'project_created');
      const selectedProject = project.command.result.project_id;
      const start = async (scenario: typeof feature) => {
        const work = server.controlPlane.commands.submit(
          server.commands.createWorkstream(selectedProject, scenario),
          'internal',
        );
        assert.ok(work.command?.result?.kind === 'workstream_created');
        server.controlPlane.commands.submit(
          server.commands.startExecution(work.command.result.workstream_id, scenario),
          'internal',
        );
        await server.time.runUntilIdle();
        const execution = server.controlPlane.projection.workstream(
          work.command.result.workstream_id,
        )?.current_execution_id;
        assert.ok(execution);
        return execution;
      };
      const send = async (type: string, payload: unknown) => {
        const envelope = JSON.parse(run('command', type, JSON.stringify(payload)));
        const reply = await runAsync(
          'request',
          target,
          'POST',
          '/api/commands',
          JSON.stringify(envelope),
        );
        assert.equal(reply.status, 202);
        assert.equal(JSON.parse(reply.body as string).disposition, 'accepted');
        await server.time.advance(0);
        assert.equal(
          server.controlPlane.projection.command(envelope.command_id)?.status,
          'completed',
          'runtime confirmed, not just HTTP accepted',
        );
      };
      const execution = await start(feature);
      await send('execution.send_instruction', {
        execution_id: execution,
        text: 'Synthetic explicit instruction',
      });
      await send('execution.interrupt', { execution_id: execution });
      const permissionExecution = await start(approving);
      const approval =
        server.controlPlane.projection.execution(permissionExecution)?.pending_approvals[0];
      assert.ok(approval);
      await send('execution.respond_to_approval', {
        execution_id: permissionExecution,
        approval_id: approval.approval_id,
        decision: 'deny',
        message: null,
      });
      const questionExecution = await start({ ...feature, scenario: 'question_asked' });
      const question =
        server.controlPlane.projection.execution(questionExecution)?.pending_questions[0];
      assert.ok(question);
      await send('execution.answer_question', {
        execution_id: questionExecution,
        question_id: question.question_id,
        answers: [
          { key: 'q0', selected: ['Dark'], text: null },
          { key: 'q1', selected: ['Orders', 'Settings'], text: null },
        ],
      });
    } finally {
      await server.stop();
    }
  });
});
