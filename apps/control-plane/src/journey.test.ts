import assert from 'node:assert/strict';
import { type ChildProcess, execFile, spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
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
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PassThrough } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  type CatalogResponse,
  type CommandEnvelope,
  type CommandId,
  type EvaluationResponse,
  type ExecutionId,
  PREVIEW_PORT_LAST,
  type ProjectId,
  type ProjectsResponse,
  type RuntimeId,
  type UnderstandingResponse,
  type UsageLimitsResponse,
  type WorkstreamId,
} from '@halcyonic/contracts';
import { MOCK_MODELS, MockRuntimeAdapter, type Scenario } from '@halcyonic/integration-mock';
import { FakeOmnorium } from '@halcyonic/integration-omnorium/testing';
import { FakeSalidium } from '@halcyonic/integration-salidium/testing';
import { FakeSeorak } from '@halcyonic/integration-seorak/testing';
import {
  createVirtualTime,
  type RuntimeAdapter,
  type StartExecutionRequest,
  systemClock,
  systemScheduler,
} from '@halcyonic/runtime-core';
import { CheckpointStore } from './checkpoints/store.ts';
import { RealtimeClient } from './client/realtime-client.ts';
import { controlPlaneDraft, NO_CAUSE } from './core/drafts.ts';
import { registerRealtime } from './http/realtime.ts';
import { registerRoutes } from './http/routes.ts';
import { fetchWithProof, loadOrCreateAccessToken } from './http/security.ts';
import { createHttpServer } from './http/server.ts';
import { OMNORIUM_CREDENTIAL_FILE, OmnoriumCatalog } from './intelligence/catalog.ts';
import { seorakEvaluation } from './intelligence/evaluation.ts';
import { salidiumUnderstanding } from './intelligence/understanding.ts';
import { createHostLocations } from './locations.ts';
import { FileObserver } from './observer/file-observer.ts';
import type { CheckResult } from './preview/check.ts';
import { type ChromeRun, DevToolsPipe } from './preview/chrome.ts';
import type { ServeArgs } from './preview/preview.ts';
import { writeWav } from './speech/wav.ts';
import type { EngineResult, SpeechEngine } from './speech/whisper.ts';
import { HostStarters, vrVersionInstruction } from './starters.ts';
import { capturingLogger, createTestControlPlane, startTestServer } from './testing/harness.ts';

/** Synthetic scenarios with short step delays for fast realtime WebSocket tests. */
const FAST_SCENARIOS = new Map<string, Scenario>([
  [
    'simulated_start',
    {
      format: 1,
      id: 'simulated_start',
      description: 'Synthetic two-step turn for the journey test.',
      steps: [
        {
          after_ms: 5,
          emit: {
            type: 'runtime.agent_message',
            payload: { text: 'Synthetic mock agent: starting work.' },
          },
        },
        {
          after_ms: 5,
          emit: {
            type: 'runtime.tool.started',
            payload: { tool_call_id: 'call-1', tool_name: 'edit', title: 'Edit src/index.ts' },
          },
        },
        {
          after_ms: 5,
          emit: {
            type: 'runtime.tool.completed',
            payload: { tool_call_id: 'call-1', outcome: 'succeeded' },
          },
        },
        { after_ms: 5, emit: { type: 'runtime.turn.completed', payload: {} } },
      ],
      instructions: [
        {
          text: 'fast_continue',
          steps: [
            {
              after_ms: 5,
              emit: {
                type: 'runtime.agent_message',
                payload: { text: 'Synthetic mock agent: continuing work.' },
              },
            },
            { after_ms: 5, emit: { type: 'runtime.turn.completed', payload: {} } },
          ],
        },
      ],
    },
  ],
]);

function psLines(): Promise<string[]> {
  return new Promise((resolve) =>
    execFile('/bin/ps', ['-axo', 'command='], (error, stdout) =>
      resolve(error ? [] : stdout.split('\n')),
    ),
  );
}

/**
 * Waits while a sibling test worker is running `preview.test.ts` (which binds all 20
 * loopback preview ports `47930..47949` and tests port collisions) and until `PREVIEW_PORT_LAST`
 * is free on 127.0.0.1.
 */
async function waitForPreviewPort(initialSettle = false): Promise<void> {
  if (initialSettle) {
    const initial = await psLines();
    const workers = initial.filter((line) => line.includes('test-isolation=process'));
    if (workers.length > 1) {
      await new Promise((resolve) => setTimeout(resolve, 400));
    }
  }
  for (let attempt = 0; attempt < 240; attempt += 1) {
    const lines = await psLines();
    const previewTestRunning = lines.some(
      (line) => line.includes('test-isolation=process') && line.includes('preview/preview.test.ts'),
    );
    if (!previewTestRunning) {
      const free = await new Promise<boolean>((resolve) => {
        const probe = createServer();
        probe.once('error', () => resolve(false));
        probe.listen({ port: PREVIEW_PORT_LAST, host: '127.0.0.1', exclusive: true }, () =>
          probe.close(() => resolve(true)),
        );
      });
      if (free) return;
    }
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
}

async function runPreviewCommand(
  client: RealtimeClient,
  makeCommand: (command_type: CommandEnvelope['command_type'], payload: unknown) => CommandEnvelope,
  command_type: 'project.preview.start' | 'project.check',
  payload: unknown,
) {
  for (let attempt = 0; ; attempt += 1) {
    await waitForPreviewPort();
    const cmd = makeCommand(command_type, payload);
    client.command(cmd);
    const outcome = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        (m.event.event_type === 'command.completed' || m.event.event_type === 'command.failed') &&
        m.event.payload.command_id === cmd.command_id,
    );
    if (outcome.type === 'event' && outcome.event.event_type === 'command.failed' && attempt < 5) {
      await new Promise((resolve) => setTimeout(resolve, 50));
      continue;
    }
    assert.equal(outcome.type === 'event' && outcome.event.event_type, 'command.completed');
    return outcome;
  }
}

const PASSED_CHECK: CheckResult = {
  tsc: { exit: 0, seconds: 0.4 },
  build: { exit: 0, seconds: 1.2 },
  page: {
    exceptions: [],
    exception_count: 0,
    console_errors: 0,
    canvases: 1,
    edges: { at_3s: 3200, at_10s: 3200 },
    drawn: true,
    pictures: [{ at_ms: 10_000, file: '/nowhere/picture-10s.png', sha256: 'a'.repeat(64) }],
  },
  unknown: null,
  passed: true,
  laptop: {
    layer: 'starter',
    targets: 1,
    mouse: 'reached',
    key: 'reached',
    screen_changed: true,
    exceptions_during: 0,
    ready: true,
  },
  vr: { offered: 'offered', how: 'offer_session', exceptions: 0, ready: true },
  notes: { scanned: 'full', items: [] },
  ready: true,
};

const FAILED_TSC_CHECK: CheckResult = {
  ...PASSED_CHECK,
  tsc: { exit: 2, seconds: 0.3 },
  page: null,
  passed: false,
  laptop: null,
  vr: null,
  ready: false,
};

/** Stand-in for `vite preview`: serves the check's built `index.html` on `127.0.0.1:<port>`. */
const SERVER_SCRIPT = `const [dist, port] = process.argv.slice(1);
require('node:http').createServer((_, res) => res.end(require('node:fs').readFileSync(dist + '/index.html')))
  .listen(Number(port), '127.0.0.1');`;

const serveCopy = (args: ServeArgs): ChildProcess =>
  spawn(process.execPath, ['-e', SERVER_SCRIPT, args.dist, args.env.HALCYONIC_PREVIEW_PORT ?? ''], {
    stdio: 'ignore',
  });

const CLIENT_INFO = { name: 'journey-test-client', version: null, device_label: null };
const WAV_CLIP = writeWav(Buffer.alloc(32_000));

function setUpStarterFolders(baseDir: string): {
  root: string;
  trackedDir: string;
  installedDir: string;
} {
  const root = join(baseDir, 'projects');
  const trackedDir = join(baseDir, 'starters-tracked');
  const installedDir = join(baseDir, 'starters-installed');
  mkdirSync(root, { recursive: true });
  mkdirSync(join(trackedDir, 'iwsdk-vr', 'template', 'src'), { recursive: true });
  mkdirSync(join(trackedDir, 'iwsdk-vr', 'host'), { recursive: true });
  mkdirSync(join(installedDir, 'iwsdk-vr', 'node_modules'), { recursive: true });

  writeFileSync(
    join(trackedDir, 'iwsdk-vr', 'starter.json'),
    JSON.stringify({ format: 1, title: 'Immersive Web SDK (3D and VR in the browser)' }),
  );
  const lockBytes = Buffer.from('{"name":"iwsdk-vr-template","lockfileVersion":3}\n');
  writeFileSync(join(trackedDir, 'iwsdk-vr', 'package-lock.json'), lockBytes);
  writeFileSync(join(trackedDir, 'iwsdk-vr', 'template', 'src', 'index.ts'), '// starter\n');
  const lockHash = createHash('sha256').update(lockBytes).digest('hex');
  writeFileSync(
    join(installedDir, 'iwsdk-vr', 'installed.json'),
    JSON.stringify({ format: 1, lockfile: lockHash }),
  );
  return { root, trackedDir, installedDir };
}

describe('the end-to-end journey over the realtime protocol (synthetic)', () => {
  test('drives say, build from starter, watch, failed check + FX1 once, passed check, preview, unconfined play refusal, voice change, and stale check replacement', async (t: TestContext) => {
    await waitForPreviewPort(true);
    const tempDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-journey-')));
    t.after(() => rmSync(tempDir, { recursive: true, force: true }));
    const { root, trackedDir, installedDir } = setUpStarterFolders(tempDir);

    const transcripts = [
      'Um, make a game where I bonk the moles.',
      'Make the moles blue.',
      'Keep the moles blue.',
      'Add a scoreboard.',
    ];
    const speech: SpeechEngine = {
      engine: { name: 'whisper.cpp', version: '1.9.4-test' },
      async transcribe(): Promise<EngineResult> {
        return { kind: 'text', text: transcripts.shift() ?? 'More.' };
      },
      async warmUp(): Promise<number> {
        return 1;
      },
    };

    const innerMock = new MockRuntimeAdapter({
      scenarios: FAST_SCENARIOS,
      models: MOCK_MODELS,
      clock: systemClock,
      scheduler: systemScheduler,
    });
    const startedInstructions: string[] = [];
    const sentInstructions: string[] = [];
    let projectDir: string | null = null;
    let holdStart = true;
    let releaseStart: () => void = () => {};
    const startGate = new Promise<void>((resolve) => {
      releaseStart = resolve;
    });
    let holdCheck3 = false;
    let releaseCheck3: () => void = () => {};
    const check3Gate = new Promise<void>((resolve) => {
      releaseCheck3 = resolve;
    });
    let notifyCheck3Entered: () => void = () => {};
    const check3Entered = new Promise<void>((resolve) => {
      notifyCheck3Entered = resolve;
    });

    const fileObserver = new FileObserver({ protect: [trackedDir, installedDir] });

    const adapter: RuntimeAdapter = {
      descriptor: { ...innerMock.descriptor, uses_project_location: true },
      ...(innerMock.listModels === undefined ? {} : { listModels: innerMock.listModels }),
      validateStartOptions: (options, modelRef) =>
        innerMock.validateStartOptions(options, modelRef),
      async startExecution(request: StartExecutionRequest) {
        startedInstructions.push(request.instruction);
        projectDir = request.directory;
        if (request.directory !== null) {
          await fileObserver.baseline(request.execution.execution_id, request.directory);
        }
        if (holdStart) await startGate;
        if (request.directory !== null) {
          writeFileSync(join(request.directory, 'src', 'index.ts'), '// turn 1 mole game\n');
        }
        await waitForPreviewPort();
        return innerMock.startExecution(request);
      },
      async sendInstruction(request) {
        sentInstructions.push(request.text);
        if (projectDir !== null && sentInstructions.length === 2) {
          writeFileSync(join(projectDir, 'src', 'index.ts'), '// turn 3 fixed mole game\n');
        } else if (projectDir !== null && sentInstructions.length === 3) {
          writeFileSync(join(projectDir, 'src', 'index.ts'), '// turn 4 scoreboard\n');
        }
        await waitForPreviewPort();
        return innerMock.sendInstruction({ ...request, text: 'fast_continue' });
      },
      ...(innerMock.respondToApproval === undefined
        ? {}
        : { respondToApproval: (req) => innerMock.respondToApproval?.(req) ?? Promise.resolve() }),
      ...(innerMock.answerQuestion === undefined
        ? {}
        : { answerQuestion: (req) => innerMock.answerQuestion?.(req) ?? Promise.resolve() }),
      ...(innerMock.interrupt === undefined
        ? {}
        : { interrupt: (req) => innerMock.interrupt?.(req) ?? Promise.resolve() }),
      close: () => innerMock.close(),
    };

    const checkResults: CheckResult[] = [FAILED_TSC_CHECK, PASSED_CHECK, PASSED_CHECK];
    let runCheckCalls = 0;
    const children: ChildProcess[] = [];
    const server = await startTestServer({
      adapters: () => [adapter],
      locations: createHostLocations([root]),
      starters: new HostStarters({ trackedDir, installedDir, dataDir: tempDir }),
      speech,
      previews: {
        dataDir: tempDir,
        home: '/Users/someone',
        node: process.execPath,
        browser: { folder: '/fake/chrome-headless-shell', sha256: 'a'.repeat(64) },
        browserProblem: () => null,
        launcher: () => '/fake/bin/halcyonic-disclaim',
        starters: { trackedDir, installedDir },
        confinesCodex: true,
        play: {
          viewerPorts: [47950, 47959],
        },
        runCheck: async (options) => {
          runCheckCalls += 1;
          if (runCheckCalls === 3 && holdCheck3) {
            notifyCheck3Entered();
            await check3Gate;
          }
          const next = checkResults.shift() ?? PASSED_CHECK;
          mkdirSync(join(options.cache, 'dist'), { recursive: true });
          writeFileSync(
            join(options.cache, 'dist', 'index.html'),
            `<title>${options.cache}</title>`,
          );
          if (next.tsc.exit !== 0) {
            writeFileSync(
              join(options.cache, 'tsc.log'),
              `${join(options.project, 'src/index.ts')}(4,7): error TS2322: Type 'string' is not assignable to type 'number'.\n`,
            );
          }
          return next;
        },
        serve: (args) => {
          const child = serveCopy(args);
          children.push(child);
          return child;
        },
      },
    });
    fileObserver.attach({
      publisher: server.controlPlane.publisher,
      recorder: server.controlPlane.recorder,
      projection: server.controlPlane.projection,
      clock: server.controlPlane.clock,
      logger: capturingLogger().logger,
    });
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    t.after(async () => {
      holdStart = false;
      releaseStart();
      holdCheck3 = false;
      releaseCheck3();
      client.close();
      await server.stop();
      for (const child of children) child.kill('SIGKILL');
    });
    client.hello(CLIENT_INFO);
    const welcome = await client.waitFor((m) => m.type === 'welcome');
    assert.equal(welcome.type === 'welcome' && welcome.journal.origin, 'live');
    const initialSnapshot = await client.waitFor((m) => m.type === 'snapshot');
    assert.equal(initialSnapshot.type, 'snapshot');

    let seq = 0;
    const makeCommand = (command_type: CommandEnvelope['command_type'], payload: unknown) => {
      seq += 1;
      return {
        schema_version: 1 as const,
        command_id: `00000000-0000-4000-8000-${String(seq).padStart(12, '0')}` as CommandId,
        issued_at: '2026-10-31T22:00:00.000Z',
        client: CLIENT_INFO,
        command_type,
        payload,
      } as CommandEnvelope;
    };

    const transcribe = async (): Promise<string> => {
      const response = await fetchWithProof(
        fetch,
        server.baseUrl,
        server.token,
        '/api/transcriptions',
        {
          method: 'POST',
          headers: { 'content-type': 'audio/wav' },
          body: WAV_CLIP,
        },
      );
      assert.equal(response.status, 200);
      const body = (await response.json()) as { outcome: string; text?: string };
      assert.equal(body.outcome, 'heard');
      assert.ok(typeof body.text === 'string' && body.text.length > 0);
      return body.text;
    };

    // 1. Say an idea: tidied draft returned, nothing journaled until an explicit command is sent.
    const eventsBeforeSpeech = client.messages.filter((m) => m.type === 'event').length;
    const heardIdea = await transcribe();
    assert.equal(heardIdea, 'Make a game where I bonk the moles.');
    assert.equal(
      client.messages.filter((m) => m.type === 'event').length,
      eventsBeforeSpeech,
      'speech transcription must not journal any event',
    );

    // 2. Build from starter: create project -> check is null (absent, not false) before any check.
    const createProj = makeCommand('project.create', {
      name: 'Bonk the Moles',
      location: {
        kind: 'new_from_starter',
        root: realpathSync.native(root),
        folder_name: 'bonk-moles',
        starter_id: 'iwsdk-vr',
      },
    });
    client.command(createProj);
    const createProjDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === createProj.command_id,
    );
    const projectId = (
      createProjDone.type === 'event' &&
      createProjDone.event.event_type === 'command.completed' &&
      createProjDone.event.payload.result?.kind === 'project_created'
        ? createProjDone.event.payload.result.project_id
        : null
    ) as ProjectId;
    assert.ok(projectId !== null);

    const readProjectView = async () => {
      const res = await fetchWithProof(fetch, server.baseUrl, server.token, '/api/projects');
      const body = (await res.json()) as ProjectsResponse;
      return body.projects.find((p) => p.project_id === projectId) ?? null;
    };
    const beforeCheckView = await readProjectView();
    assert.equal(beforeCheckView?.starter, 'iwsdk-vr');
    assert.equal(
      beforeCheckView?.check,
      undefined,
      'an unchecked project omits check on ProjectView, never reporting false',
    );

    const createWs = makeCommand('workstream.create', {
      project_id: projectId,
      title: 'Main task',
      objective: null,
    });
    client.command(createWs);
    const createWsDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === createWs.command_id,
    );
    const workstreamId = (
      createWsDone.type === 'event' &&
      createWsDone.event.event_type === 'command.completed' &&
      createWsDone.event.payload.result?.kind === 'workstream_created'
        ? createWsDone.event.payload.result.workstream_id
        : null
    ) as WorkstreamId;
    assert.ok(workstreamId !== null);

    // Accepted is not completed: while adapter.startExecution is held, command_ack is accepted and
    // command.completed has not occurred.
    const startExec = makeCommand('execution.start', {
      workstream_id: workstreamId,
      runtime_id: 'mock' as RuntimeId,
      instruction: heardIdea,
      options: { scenario: 'simulated_start' },
      model_ref: 'mock/fast',
    });
    client.command(startExec);
    const startAck = await client.waitFor(
      (m) => m.type === 'command_ack' && m.command_id === startExec.command_id,
    );
    assert.equal(startAck.type === 'command_ack' && startAck.disposition, 'accepted');
    assert.equal(startAck.type === 'command_ack' && startAck.command?.status, 'accepted');
    assert.equal(
      client.messages.some(
        (m) =>
          m.type === 'event' &&
          m.event.event_type === 'command.completed' &&
          m.event.payload.command_id === startExec.command_id,
      ),
      false,
      'command.completed must not be recorded before the runtime adapter confirms startExecution',
    );
    holdStart = false;
    releaseStart();

    const startDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === startExec.command_id,
    );
    const executionId = (
      startDone.type === 'event' &&
      startDone.event.event_type === 'command.completed' &&
      startDone.event.payload.result?.kind === 'execution_created'
        ? startDone.event.payload.result.execution_id
        : null
    ) as ExecutionId;
    assert.ok(executionId !== null);
    assert.equal(startedInstructions[0], heardIdea);

    // 3. Watch Turn 1: agent_message is reported, tool events are observed.
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === executionId &&
        m.event.event_type === 'runtime.turn.completed',
    );
    const turn1Events = client.messages.flatMap((m) =>
      m.type === 'event' && m.event.execution_id === executionId ? [m.event] : [],
    );
    const agentMsg = turn1Events.find((e) => e.event_type === 'runtime.agent_message');
    assert.equal(
      agentMsg?.event_type === 'runtime.agent_message' && agentMsg.provenance.epistemic,
      'reported',
    );
    const toolStarted = turn1Events.find((e) => e.event_type === 'runtime.tool.started');
    assert.equal(
      toolStarted?.event_type === 'runtime.tool.started' && toolStarted.provenance.epistemic,
      'observed',
    );

    // 4a. Turn 1 changed src/index.ts -> execution.files.observed triggers automatic internal check 1 (A1),
    // which fails tsc -> preview is refused -> voice instruction carries FX1 once.
    const check1Accepted = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.accepted' &&
        m.event.payload.command.command_type === 'project.check' &&
        m.event.payload.received_via === 'internal',
    );
    const check1CmdId =
      check1Accepted.type === 'event' && check1Accepted.event.event_type === 'command.accepted'
        ? check1Accepted.event.payload.command.command_id
        : '';
    assert.ok(check1CmdId !== '');
    const check1Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === check1CmdId,
    );
    assert.equal(
      check1Done.type === 'event' &&
        check1Done.event.event_type === 'command.completed' &&
        check1Done.event.payload.result?.kind === 'project_checked' &&
        check1Done.event.payload.result.passed,
      false,
    );
    const check1Id =
      check1Done.type === 'event' &&
      check1Done.event.event_type === 'command.completed' &&
      check1Done.event.payload.result?.kind === 'project_checked'
        ? check1Done.event.payload.result.check_id
        : '';

    const previewRefused = makeCommand('project.preview.start', { project_id: projectId });
    client.command(previewRefused);
    const previewRefusedAck = await client.waitFor(
      (m) => m.type === 'command_ack' && m.command_id === previewRefused.command_id,
    );
    assert.equal(
      previewRefusedAck.type === 'command_ack' && previewRefusedAck.disposition,
      'rejected',
    );

    const heardChange1 = await transcribe();
    assert.equal(heardChange1, 'Make the moles blue.');
    assert.equal(sentInstructions.length, 0, 'nothing sent before explicit send_instruction');

    const instruct1 = makeCommand('execution.send_instruction', {
      execution_id: executionId,
      text: heardChange1,
    });
    client.command(instruct1);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === instruct1.command_id,
    );
    const turn2Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === executionId &&
        m.event.event_type === 'runtime.turn.completed' &&
        m.position > (check1Done.type === 'event' ? check1Done.position : 0),
    );
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === executionId &&
        m.event.event_type === 'execution.files.observed' &&
        m.position > (turn2Done.type === 'event' ? turn2Done.position : 0),
    );
    assert.ok(sentInstructions[0]?.startsWith("Make the moles blue.\n\n[The Mac's own check"));
    assert.ok(sentInstructions[0]?.includes('| ./src/index.ts(4,7): error TS2322'));
    assert.equal(
      runCheckCalls,
      1,
      'a turn with no file changes must not start an automatic check (A1)',
    );

    // FX1 is given only once per failed check: a second instruction before a new check does not repeat it.
    // Turn 3 changes src/index.ts -> triggers automatic internal check 2 (A1), which passes.
    const heardChange2 = await transcribe();
    const instruct2 = makeCommand('execution.send_instruction', {
      execution_id: executionId,
      text: heardChange2,
    });
    client.command(instruct2);
    const instruct2Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === instruct2.command_id,
    );
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === executionId &&
        m.event.event_type === 'runtime.turn.completed' &&
        m.position > (instruct2Done.type === 'event' ? instruct2Done.position : 0),
    );
    assert.equal(sentInstructions[1], 'Keep the moles blue.');
    const fx1Events = client.messages.flatMap((m) =>
      m.type === 'event' && m.event.event_type === 'execution.check_output_given' ? [m.event] : [],
    );
    assert.equal(fx1Events.length, 1, 'FX1 must be recorded once for check 1');
    assert.equal(fx1Events[0]?.payload.check_id, check1Id);

    // 4b & 5. Automatic Check 2 (A1) passes -> preview serves built copy -> Play on my laptop is refused because mock is not confined.
    const check2Accepted = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.accepted' &&
        m.event.payload.command.command_type === 'project.check' &&
        m.event.payload.received_via === 'internal' &&
        m.event.payload.command.command_id !== check1CmdId,
    );
    const check2CmdId =
      check2Accepted.type === 'event' && check2Accepted.event.event_type === 'command.accepted'
        ? check2Accepted.event.payload.command.command_id
        : '';
    assert.ok(check2CmdId !== '');
    const check2Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === check2CmdId,
    );
    const check2Pos = check2Done.type === 'event' ? check2Done.position : 0;
    const check2Id =
      check2Done.type === 'event' &&
      check2Done.event.event_type === 'command.completed' &&
      check2Done.event.payload.result?.kind === 'project_checked'
        ? check2Done.event.payload.result.check_id
        : '';
    assert.ok(check2Id !== '');
    const currentView = await readProjectView();
    assert.equal(currentView?.check?.check_id, check2Id);
    assert.equal(currentView?.check?.files, 'current');

    const previewStartDone = await runPreviewCommand(client, makeCommand, 'project.preview.start', {
      project_id: projectId,
    });
    const previewPort =
      previewStartDone.type === 'event' &&
      previewStartDone.event.event_type === 'command.completed' &&
      previewStartDone.event.payload.result?.kind === 'preview_started'
        ? previewStartDone.event.payload.result.port
        : 0;
    assert.ok(previewPort > 0);
    const previewRes = await fetch(`http://127.0.0.1:${String(previewPort)}/`);
    assert.equal(previewRes.status, 200);
    const previewHtml = await previewRes.text();
    assert.ok(
      previewHtml.includes(check2Id),
      'preview must serve the built copy of check 2, not the live project folder',
    );

    const openRefused = makeCommand('project.open_on_mac', { project_id: projectId });
    client.command(openRefused);
    const openRefusedAck = await client.waitFor(
      (m) => m.type === 'command_ack' && m.command_id === openRefused.command_id,
    );
    assert.equal(openRefusedAck.type === 'command_ack' && openRefusedAck.disposition, 'rejected');
    assert.match(
      openRefusedAck.type === 'command_ack'
        ? (openRefusedAck.command?.rejection?.message ?? '')
        : '',
      /Not every agent run in this project was confined/,
    );

    // 7 & 8. Voice change turn modifies src/index.ts -> check 2 becomes files: 'changed' (T2), refusing
    // preview.start and open_on_mac while automatic check 3 (A1) is in flight, and stopping the old preview.
    holdCheck3 = true;
    const heardChange3 = await transcribe();
    assert.equal(heardChange3, 'Add a scoreboard.');
    const instruct3 = makeCommand('execution.send_instruction', {
      execution_id: executionId,
      text: heardChange3,
    });
    client.command(instruct3);
    const instruct3Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === instruct3.command_id,
    );
    const turn4Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === executionId &&
        m.event.event_type === 'runtime.turn.completed' &&
        m.position > (instruct3Done.type === 'event' ? instruct3Done.position : 0),
    );
    assert.ok((turn4Done.type === 'event' ? turn4Done.position : 0) > check2Pos);

    await check3Entered;
    const staleView = await readProjectView();
    assert.equal(staleView?.check?.check_id, check2Id);
    assert.equal(
      staleView?.check?.files,
      'changed',
      'a check whose project files changed in a later turn must report files: changed (T2)',
    );

    const stalePreviewCmd = makeCommand('project.preview.start', { project_id: projectId });
    client.command(stalePreviewCmd);
    const stalePreviewAck = await client.waitFor(
      (m) => m.type === 'command_ack' && m.command_id === stalePreviewCmd.command_id,
    );
    assert.equal(stalePreviewAck.type === 'command_ack' && stalePreviewAck.disposition, 'rejected');
    assert.match(
      stalePreviewAck.type === 'command_ack'
        ? (stalePreviewAck.command?.rejection?.message ?? '')
        : '',
      /files changed since its last check/,
    );

    const stalePlayCmd = makeCommand('project.open_on_mac', { project_id: projectId });
    client.command(stalePlayCmd);
    const stalePlayAck = await client.waitFor(
      (m) => m.type === 'command_ack' && m.command_id === stalePlayCmd.command_id,
    );
    assert.equal(stalePlayAck.type === 'command_ack' && stalePlayAck.disposition, 'rejected');
    assert.match(
      stalePlayAck.type === 'command_ack' ? (stalePlayAck.command?.rejection?.message ?? '') : '',
      /files changed since its last check/,
    );

    const check3Accepted = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.accepted' &&
        m.event.payload.command.command_type === 'project.check' &&
        m.event.payload.received_via === 'internal' &&
        m.event.payload.command.command_id !== check1CmdId &&
        m.event.payload.command.command_id !== check2CmdId,
    );
    const check3CmdId =
      check3Accepted.type === 'event' && check3Accepted.event.event_type === 'command.accepted'
        ? check3Accepted.event.payload.command.command_id
        : '';
    assert.ok(check3CmdId !== '');

    holdCheck3 = false;
    releaseCheck3();
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === check3CmdId,
    );
    const freshView = await readProjectView();
    assert.notEqual(freshView?.check?.check_id, check2Id);
    assert.equal(freshView?.check?.files, 'current');

    const previewStopped = client.messages.find(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'project.preview.stopped' &&
        m.event.payload.check_id === check2Id,
    );
    assert.equal(
      previewStopped?.type === 'event' &&
        previewStopped.event.event_type === 'project.preview.stopped' &&
        previewStopped.event.payload.reason,
      'new_check',
    );
    assert.equal(client.invalid.length, 0);
  });

  test('admits Play on my laptop only when every execution recorded confined, the check passed and is laptop-ready, and the preview serves that check', async (t: TestContext) => {
    const tempDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-journey-play-')));
    t.after(() => rmSync(tempDir, { recursive: true, force: true }));
    const { root, trackedDir, installedDir } = setUpStarterFolders(tempDir);

    const playRuns = { started: 0, stopped: 0, links: [] as string[] };
    const children: ChildProcess[] = [];
    const server = await startTestServer({
      locations: createHostLocations([root]),
      starters: new HostStarters({ trackedDir, installedDir, dataDir: tempDir }),
      previews: {
        dataDir: tempDir,
        home: '/Users/someone',
        node: process.execPath,
        browser: { folder: '/fake/chrome-headless-shell', sha256: 'a'.repeat(64) },
        browserProblem: () => null,
        launcher: () => '/fake/bin/halcyonic-disclaim',
        starters: { trackedDir, installedDir },
        confinesCodex: true,
        play: {
          launch: async (): Promise<ChromeRun> => {
            playRuns.started += 1;
            const toChrome = new PassThrough();
            const fromChrome = new PassThrough();
            toChrome.setEncoding('utf8');
            toChrome.on('data', (chunk: string) => {
              for (const text of chunk.split('\0').filter((part) => part !== ''))
                fromChrome.write(
                  `${JSON.stringify({ id: (JSON.parse(text) as { id: number }).id, result: {} })}\0`,
                );
            });
            return {
              pid: process.pid,
              devtools: new DevToolsPipe(toChrome, fromChrome),
              session: 'page',
              stop: async () => {
                playRuns.stopped += 1;
              },
            };
          },
          openViewer: (link) => {
            playRuns.links.push(link);
          },
          viewerWaitMs: 60_000,
          viewerGraceMs: 50,
          viewerPorts: [47960, 47969],
        },
        runCheck: async (options) => {
          mkdirSync(join(options.cache, 'dist'), { recursive: true });
          writeFileSync(
            join(options.cache, 'dist', 'index.html'),
            `<title>${options.cache}</title>`,
          );
          return PASSED_CHECK;
        },
        serve: (args) => {
          const child = serveCopy(args);
          children.push(child);
          return child;
        },
      },
    });
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    t.after(async () => {
      client.close();
      await server.stop();
      for (const child of children) child.kill('SIGKILL');
    });
    client.hello(CLIENT_INFO);
    await client.waitFor((m) => m.type === 'snapshot');

    let seq = 0;
    const makeCommand = (command_type: CommandEnvelope['command_type'], payload: unknown) => {
      seq += 1;
      return {
        schema_version: 1 as const,
        command_id: `00000000-0000-4000-9000-${String(seq).padStart(12, '0')}` as CommandId,
        issued_at: '2026-10-31T22:00:00.000Z',
        client: CLIENT_INFO,
        command_type,
        payload,
      } as CommandEnvelope;
    };

    const createProj = makeCommand('project.create', {
      name: 'Confined Game',
      location: {
        kind: 'new_from_starter',
        root: realpathSync.native(root),
        folder_name: 'confined-game',
        starter_id: 'iwsdk-vr',
      },
    });
    client.command(createProj);
    const createProjDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === createProj.command_id,
    );
    const projectId = (
      createProjDone.type === 'event' &&
      createProjDone.event.event_type === 'command.completed' &&
      createProjDone.event.payload.result?.kind === 'project_created'
        ? createProjDone.event.payload.result.project_id
        : null
    ) as ProjectId;
    assert.ok(projectId !== null);

    // Record a confined Codex execution in the journal (synthetic test record).
    const now = '2026-10-31T22:01:00.000Z' as never;
    const workstreamId = '01a19000-0000-7000-8000-0000000000b1' as WorkstreamId;
    const executionId = '01a19000-0000-7000-8000-0000000000c1' as ExecutionId;
    const scope = { project_id: projectId, workstream_id: workstreamId, execution_id: executionId };
    server.controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.created',
        { ...scope, execution_id: null },
        { title: 'Build game', objective: null },
        now,
        NO_CAUSE,
      ),
    );
    server.controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.created',
        scope,
        {
          runtime: { runtime_id: 'codex', kind: 'codex', display_name: 'Codex', synthetic: false },
          instruction: 'Build it.',
          directory: join(root, 'confined-game'),
        } as never,
        now,
        NO_CAUSE,
      ),
    );
    for (const [sequence, eventType, payload] of [
      [1, 'runtime.execution.started', { native_id: 'confined-1', confined: true }],
      [2, 'runtime.turn.started', { turn_id: 'turn-1' }],
      [3, 'runtime.turn.completed', { turn_id: 'turn-1' }],
    ] as const) {
      server.controlPlane.recorder.record({
        ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
        event_type: eventType,
        source: { kind: 'runtime', runtime_id: 'codex' },
        source_native_id: `confined-1:${sequence}`,
        sequence,
        payload,
      } as never);
    }

    await runPreviewCommand(client, makeCommand, 'project.check', { project_id: projectId });

    // Without a running preview of the passed check, Play on my laptop is refused.
    const openWithoutPreview = makeCommand('project.open_on_mac', { project_id: projectId });
    client.command(openWithoutPreview);
    const withoutPreviewAck = await client.waitFor(
      (m) => m.type === 'command_ack' && m.command_id === openWithoutPreview.command_id,
    );
    assert.equal(
      withoutPreviewAck.type === 'command_ack' && withoutPreviewAck.disposition,
      'rejected',
    );
    assert.match(
      withoutPreviewAck.type === 'command_ack'
        ? (withoutPreviewAck.command?.rejection?.message ?? '')
        : '',
      /Start the preview of the latest check first/,
    );

    // Once the preview serves the latest passed check, Play on my laptop is admitted and starts.
    await runPreviewCommand(client, makeCommand, 'project.preview.start', {
      project_id: projectId,
    });

    const openPlay = makeCommand('project.open_on_mac', { project_id: projectId });
    client.command(openPlay);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === openPlay.command_id,
    );
    assert.equal(playRuns.started, 1);
    assert.equal(playRuns.links.length, 1);

    // A new check stops the running play (newer_check) and preview (new_check).
    await runPreviewCommand(client, makeCommand, 'project.check', { project_id: projectId });
    assert.equal(playRuns.stopped, 1);
    const macPlayStopped = client.messages.find(
      (m) => m.type === 'event' && m.event.event_type === 'project.mac_play.stopped',
    );
    assert.equal(
      macPlayStopped?.type === 'event' &&
        macPlayStopped.event.event_type === 'project.mac_play.stopped' &&
        macPlayStopped.event.payload.reason,
      'newer_check',
    );
    assert.equal(client.invalid.length, 0);
  });

  test('proves multi-project Omnorium catalog linking, VR version creation from an existing non-VR project, and concurrent workstreams with Salidium and Seorak intelligence', async (t: TestContext) => {
    await waitForPreviewPort(true);
    const tempDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-journey-multi-')));
    t.after(() => rmSync(tempDir, { recursive: true, force: true }));

    const root = join(tempDir, 'projects');
    const dataDir = join(tempDir, 'data');
    const installedDir = join(tempDir, 'starters-installed');
    mkdirSync(root, { recursive: true });
    mkdirSync(dataDir, { recursive: true });

    // Point HostStarters at the repository's real starters directory so make_vr_version copies all real iwsdk-vr files.
    const realStartersDir = fileURLToPath(new URL('../../../starters', import.meta.url));
    const realLockBytes = readFileSync(join(realStartersDir, 'iwsdk-vr', 'package-lock.json'));
    const realLockHash = createHash('sha256').update(realLockBytes).digest('hex');
    mkdirSync(join(installedDir, 'iwsdk-vr', 'node_modules'), { recursive: true });
    writeFileSync(
      join(installedDir, 'iwsdk-vr', 'installed.json'),
      JSON.stringify({ format: 1, lockfile: realLockHash }),
    );

    // Pre-create an existing non-VR web project folder and a git repository folder matching a catalog entry.
    const webFolder = join(root, 'wind-chimes-web');
    mkdirSync(join(webFolder, 'src'), { recursive: true });
    const originalPackageJson = '{"name":"wind-chimes-web","version":"1.0.0"}\n';
    const originalAppJs = 'export const ring = () => "chime";\n';
    writeFileSync(join(webFolder, 'package.json'), originalPackageJson);
    writeFileSync(join(webFolder, 'src', 'app.js'), originalAppJs);

    const pinwheelFolder = join(root, 'pinwheel-clone');
    mkdirSync(join(pinwheelFolder, '.git'), { recursive: true });
    writeFileSync(
      join(pinwheelFolder, '.git', 'config'),
      '[remote "origin"]\n\turl = https://github.com/fixture-person/pinwheel.git\n',
    );

    // Start stand-in Omnorium, Salidium, and Seorak servers.
    const omnorium = await FakeOmnorium.start({
      contracts: ['omnorium.catalog'],
      tokenPrefix: 'omc',
    });
    t.after(() => omnorium.close());
    const salidium = await FakeSalidium.start();
    t.after(() => salidium.close());
    const seorak = await FakeSeorak.start();
    t.after(() => seorak.close());
    seorak.scopes.add('limits:read');

    const claudeNativeId = '6f1c2a90-3b7e-4d15-9a2c-0e8b5d7f4c11';
    seorak.capture('claude-code', claudeNativeId);
    const confinedCodexNativeId = '019a0000-0000-7000-8000-000000000077';

    const saveCred = (fileName: string, token: string) => {
      const path = join(dataDir, fileName);
      writeFileSync(path, `${token}\n`, { mode: 0o600 });
      chmodSync(path, 0o600);
      return path;
    };
    const catalogCredPath = saveCred(OMNORIUM_CREDENTIAL_FILE, omnorium.token);
    const salidiumCredPath = saveCred('salidium-credential', salidium.token);
    const seorakCredPath = saveCred('seorak-credential', seorak.token);

    const codexHome = join(dataDir, 'codex-home');
    const confinedCodexHomes = join(dataDir, 'codex-confined');
    const catalog = new OmnoriumCatalog({
      homes: [omnorium.home],
      credentialPath: catalogCredPath,
      protect: [dataDir],
      timeoutMs: 2000,
    });
    const understanding = salidiumUnderstanding({
      home: salidium.home,
      credentialPath: salidiumCredPath,
      codexHome,
      confinedCodexHomes,
    });
    const evaluation = seorakEvaluation({
      credentialPath: seorakCredPath,
      port: seorak.port,
      codexHome,
      confinedCodexHomes,
    });

    const fileObserver = new FileObserver({ protect: [realStartersDir, installedDir, dataDir] });

    let releaseConcurrentTurns: () => void = () => {};
    const concurrentTurnGate = new Promise<void>((resolve) => {
      releaseConcurrentTurns = resolve;
    });

    const claudeAdapter: RuntimeAdapter = {
      descriptor: {
        runtime_id: 'claude-agent' as RuntimeId,
        kind: 'claude-agent',
        display_name: 'Claude Agent (synthetic journey adapter)',
        synthetic: true,
        capabilities: {
          start_execution: true,
          instruct_at_rest: true,
          instruct_while_running: false,
          respond_to_approval: false,
          answer_question: false,
          interrupt: true,
        },
        model_choice: 'none',
        uses_project_location: true,
        reports_tool_activity: true,
        reports_tool_results: false,
      },
      validateStartOptions: () => ({ ok: true }),
      async startExecution(request: StartExecutionRequest) {
        if (request.directory !== null) {
          await fileObserver.baseline(request.execution.execution_id, request.directory);
        }
        let seq = 0;
        const emit = (type: string, payload: unknown) => {
          seq += 1;
          request.emit({
            native_event_id: `${claudeNativeId}:${String(seq)}`,
            sequence: seq,
            occurred_at: '2026-09-26T10:00:01.000Z' as never,
            type: type as never,
            payload: payload as never,
            provenance: {
              epistemic: type === 'runtime.agent_message' ? 'reported' : 'observed',
              native_type: `claude/${type}`,
            },
          });
        };
        emit('runtime.execution.started', { native_id: claudeNativeId });
        emit('runtime.turn.started', { turn_id: 'claude-turn-1' });
        emit('runtime.agent_message', {
          text: 'Synthetic Claude agent: reviewing Wind Chimes Web.',
        });
        void concurrentTurnGate.then(() => {
          emit('runtime.turn.completed', { turn_id: 'claude-turn-1' });
        });
        return { native_id: claudeNativeId };
      },
      async sendInstruction() {},
      async interrupt() {},
      close: async () => {},
    };

    const vrStartedInstructions: string[] = [];
    const confinedCodexAdapter: RuntimeAdapter = {
      descriptor: {
        runtime_id: 'codex' as RuntimeId,
        kind: 'codex',
        display_name: 'Codex (synthetic confined journey adapter)',
        synthetic: true,
        capabilities: {
          start_execution: true,
          instruct_at_rest: true,
          instruct_while_running: false,
          respond_to_approval: false,
          answer_question: false,
          interrupt: true,
        },
        model_choice: 'none',
        uses_project_location: true,
        reports_tool_activity: true,
        reports_tool_results: false,
      },
      validateStartOptions: () => ({ ok: true }),
      async startExecution(request: StartExecutionRequest) {
        vrStartedInstructions.push(request.instruction);
        if (request.directory !== null) {
          await fileObserver.baseline(request.execution.execution_id, request.directory);
        }
        // Write a confined Codex rollout in the execution's local date folder so Salidium and Seorak
        // recognize the session as belonging to Halcyonic's own confined Codex home.
        const startedDay = new Date('2026-09-26T10:00:00.000Z');
        const rolloutFolder = join(
          confinedCodexHomes,
          '0123456789abcdef',
          'sessions',
          String(startedDay.getFullYear()).padStart(4, '0'),
          String(startedDay.getMonth() + 1).padStart(2, '0'),
          String(startedDay.getDate()).padStart(2, '0'),
        );
        mkdirSync(rolloutFolder, { recursive: true });
        writeFileSync(
          join(rolloutFolder, `rollout-2026-09-26T10-00-00-${confinedCodexNativeId}.jsonl`),
          '{}\n',
        );

        let seq = 0;
        const emit = (type: string, payload: unknown) => {
          seq += 1;
          request.emit({
            native_event_id: `${confinedCodexNativeId}:${String(seq)}`,
            sequence: seq,
            occurred_at: '2026-09-26T10:00:01.000Z' as never,
            type: type as never,
            payload: payload as never,
            provenance: {
              epistemic: type === 'runtime.agent_message' ? 'reported' : 'observed',
              native_type: `codex/${type}`,
            },
          });
        };
        emit('runtime.execution.started', { native_id: confinedCodexNativeId, confined: true });
        emit('runtime.turn.started', { turn_id: 'codex-turn-1' });
        emit('runtime.agent_message', {
          text: 'Synthetic confined Codex: building VR wind chimes.',
        });
        void concurrentTurnGate.then(async () => {
          if (request.directory !== null) {
            writeFileSync(
              join(request.directory, 'src', 'index.ts'),
              '// VR wind chimes scene built from Wind Chimes Web\n',
            );
          }
          await waitForPreviewPort();
          emit('runtime.turn.completed', { turn_id: 'codex-turn-1' });
        });
        return { native_id: confinedCodexNativeId };
      },
      async sendInstruction() {},
      async interrupt() {},
      close: async () => {},
    };

    const playRuns = { started: 0, stopped: 0, links: [] as string[] };
    const children: ChildProcess[] = [];
    const { token } = await loadOrCreateAccessToken(dataDir);
    const app = await createHttpServer({ logLevel: 'silent', token });
    const harness = createTestControlPlane({
      adapters: () => [claudeAdapter, confinedCodexAdapter],
      locations: createHostLocations([root]),
      starters: new HostStarters({
        trackedDir: realStartersDir,
        installedDir,
        dataDir,
      }),
      catalogLinks: catalog,
      previews: {
        dataDir,
        home: '/Users/someone',
        node: process.execPath,
        browser: { folder: '/fake/chrome-headless-shell', sha256: 'a'.repeat(64) },
        browserProblem: () => null,
        launcher: () => '/fake/bin/halcyonic-disclaim',
        starters: { trackedDir: realStartersDir, installedDir },
        confinesCodex: true,
        play: {
          launch: async (): Promise<ChromeRun> => {
            playRuns.started += 1;
            const toChrome = new PassThrough();
            const fromChrome = new PassThrough();
            toChrome.setEncoding('utf8');
            toChrome.on('data', (chunk: string) => {
              for (const text of chunk.split('\0').filter((part) => part !== ''))
                fromChrome.write(
                  `${JSON.stringify({ id: (JSON.parse(text) as { id: number }).id, result: {} })}\0`,
                );
            });
            return {
              pid: process.pid,
              devtools: new DevToolsPipe(toChrome, fromChrome),
              session: 'page',
              stop: async () => {
                playRuns.stopped += 1;
              },
            };
          },
          openViewer: (link) => {
            playRuns.links.push(link);
          },
          viewerWaitMs: 60_000,
          viewerGraceMs: 50,
          viewerPorts: [47970, 47979],
        },
        runCheck: async (options) => {
          mkdirSync(join(options.cache, 'dist'), { recursive: true });
          writeFileSync(
            join(options.cache, 'dist', 'index.html'),
            `<title>${options.cache}</title>`,
          );
          return PASSED_CHECK;
        },
        serve: (args) => {
          const child = serveCopy(args);
          children.push(child);
          return child;
        },
      },
    });
    fileObserver.attach({
      publisher: harness.controlPlane.publisher,
      recorder: harness.controlPlane.recorder,
      projection: harness.controlPlane.projection,
      clock: harness.controlPlane.clock,
      logger: capturingLogger().logger,
    });
    registerRoutes(app, harness.controlPlane, {
      understanding,
      evaluation,
      catalog,
    });
    registerRealtime(app, harness.controlPlane);
    await app.listen({ host: '127.0.0.1', port: 0 });
    const address = app.server.address();
    if (address === null || typeof address === 'string') throw new Error('server has no port');
    const baseUrl = `http://127.0.0.1:${String(address.port)}`;
    const wsUrl = `ws://127.0.0.1:${String(address.port)}/realtime`;
    t.after(async () => {
      releaseConcurrentTurns();
      await app.close();
      await harness.controlPlane.close();
      for (const child of children) child.kill('SIGKILL');
    });

    const client = await RealtimeClient.connect(wsUrl, token);
    t.after(() => client.close());
    client.hello(CLIENT_INFO);
    await client.waitFor((m) => m.type === 'snapshot');

    let seq = 0;
    const makeCommand = (command_type: CommandEnvelope['command_type'], payload: unknown) => {
      seq += 1;
      return {
        schema_version: 1 as const,
        command_id: `00000000-0000-4000-a000-${String(seq).padStart(12, '0')}` as CommandId,
        issued_at: '2026-10-31T22:00:00.000Z',
        client: CLIENT_INFO,
        command_type,
        payload,
      } as CommandEnvelope;
    };

    // 1. Multi-project creation + Omnorium catalog discovery, linking, and unlinking.
    const createWebProj = makeCommand('project.create', {
      name: 'Wind Chimes Web',
      location: {
        kind: 'existing_folder',
        root: realpathSync.native(root),
        folder_name: 'wind-chimes-web',
      },
    });
    client.command(createWebProj);
    const createWebDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === createWebProj.command_id,
    );
    const webProjectId = (
      createWebDone.type === 'event' &&
      createWebDone.event.event_type === 'command.completed' &&
      createWebDone.event.payload.result?.kind === 'project_created'
        ? createWebDone.event.payload.result.project_id
        : null
    ) as ProjectId;
    assert.ok(webProjectId !== null);
    assert.equal(harness.controlPlane.projection.project(webProjectId)?.starter, null);

    const createNotesProj = makeCommand('project.create', {
      name: 'Studio Notes',
      location: {
        kind: 'new_folder',
        root: realpathSync.native(root),
        folder_name: 'studio-notes',
      },
    });
    client.command(createNotesProj);
    const createNotesDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === createNotesProj.command_id,
    );
    const notesProjectId = (
      createNotesDone.type === 'event' &&
      createNotesDone.event.event_type === 'command.completed' &&
      createNotesDone.event.payload.result?.kind === 'project_created'
        ? createNotesDone.event.payload.result.project_id
        : null
    ) as ProjectId;
    assert.ok(notesProjectId !== null);

    // Before GET /api/catalog has been read, project.link_catalog is rejected.
    const catalogRef = {
      source: 'omnorium' as const,
      repository_id: 8100001,
      full_name: 'lantern-lab/wind-chimes',
    };
    const linkBeforeRead = makeCommand('project.link_catalog', {
      project_id: webProjectId,
      catalog: catalogRef,
    });
    client.command(linkBeforeRead);
    const linkBeforeReadAck = await client.waitFor(
      (m) => m.type === 'command_ack' && m.command_id === linkBeforeRead.command_id,
    );
    assert.equal(
      linkBeforeReadAck.type === 'command_ack' && linkBeforeReadAck.disposition,
      'rejected',
    );
    assert.match(
      linkBeforeReadAck.type === 'command_ack'
        ? (linkBeforeReadAck.command?.rejection?.message ?? '')
        : '',
      /No catalog has been read yet/,
    );

    // Discover Omnorium catalog via GET /api/catalog.
    const catalogRes1 = await fetchWithProof(fetch, baseUrl, token, '/api/catalog');
    assert.equal(catalogRes1.status, 200);
    const catalogBody1 = (await catalogRes1.json()) as CatalogResponse;
    assert.equal(catalogBody1.availability, 'available');
    assert.equal(catalogBody1.provenance, 'reported');
    assert.equal(catalogBody1.projects.length, 3);
    const windChimesEntry1 = catalogBody1.projects.find((p) => p.name === 'Wind chimes');
    const pinwheelEntry1 = catalogBody1.projects.find((p) => p.name === 'pinwheel');
    assert.deepEqual(windChimesEntry1?.local, { project_ids: [], suggestions: [] });
    assert.deepEqual(pinwheelEntry1?.local, {
      project_ids: [],
      suggestions: [
        {
          root: realpathSync.native(root),
          folder_name: 'pinwheel-clone',
          rule: 'github_origin_full_name',
        },
      ],
    });

    // Link Wind Chimes Web to the Omnorium catalog entry over the realtime WebSocket.
    const linkCmd = makeCommand('project.link_catalog', {
      project_id: webProjectId,
      catalog: catalogRef,
    });
    client.command(linkCmd);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === linkCmd.command_id,
    );
    const linkedEvent = client.messages.find(
      (m) =>
        m.type === 'event' &&
        m.event.project_id === webProjectId &&
        m.event.event_type === 'project.catalog_linked',
    );
    assert.deepEqual(
      linkedEvent?.type === 'event' &&
        linkedEvent.event.event_type === 'project.catalog_linked' &&
        linkedEvent.event.payload,
      { catalog: catalogRef, previous: null },
    );
    assert.deepEqual(harness.controlPlane.projection.catalogLink(webProjectId), catalogRef);

    const catalogRes2 = await fetchWithProof(fetch, baseUrl, token, '/api/catalog');
    const catalogBody2 = (await catalogRes2.json()) as CatalogResponse;
    assert.equal(catalogBody2.availability, 'available');
    assert.deepEqual(catalogBody2.projects.find((p) => p.name === 'Wind chimes')?.local, {
      project_ids: [webProjectId],
      suggestions: [],
    });

    // Unlink Wind Chimes Web from the Omnorium catalog entry over the realtime WebSocket.
    const unlinkCmd = makeCommand('project.unlink_catalog', { project_id: webProjectId });
    client.command(unlinkCmd);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === unlinkCmd.command_id,
    );
    const unlinkedEvent = client.messages.find(
      (m) =>
        m.type === 'event' &&
        m.event.project_id === webProjectId &&
        m.event.event_type === 'project.catalog_unlinked',
    );
    assert.deepEqual(
      unlinkedEvent?.type === 'event' &&
        unlinkedEvent.event.event_type === 'project.catalog_unlinked' &&
        unlinkedEvent.event.payload,
      { previous: catalogRef },
    );
    assert.equal(harness.controlPlane.projection.catalogLink(webProjectId), null);

    const catalogRes3 = await fetchWithProof(fetch, baseUrl, token, '/api/catalog');
    const catalogBody3 = (await catalogRes3.json()) as CatalogResponse;
    assert.equal(catalogBody3.availability, 'available');
    assert.deepEqual(catalogBody3.projects.find((p) => p.name === 'Wind chimes')?.local, {
      project_ids: [],
      suggestions: [],
    });

    // 2. VR-ify the existing non-VR project via project.make_vr_version over the realtime WebSocket.
    const makeVrCmd = makeCommand('project.make_vr_version', {
      original_project_id: webProjectId,
      root: realpathSync.native(root),
      folder_name: 'wind-chimes-vr',
    });
    client.command(makeVrCmd);
    const makeVrDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === makeVrCmd.command_id,
    );
    const vrProjectId = (
      makeVrDone.type === 'event' &&
      makeVrDone.event.event_type === 'command.completed' &&
      makeVrDone.event.payload.result?.kind === 'project_created'
        ? makeVrDone.event.payload.result.project_id
        : null
    ) as ProjectId;
    assert.ok(vrProjectId !== null);
    assert.notEqual(vrProjectId, webProjectId);

    // Original non-VR project folder is completely untouched.
    assert.deepEqual(readdirSync(webFolder).sort(), ['package.json', 'src']);
    assert.deepEqual(readdirSync(join(webFolder, 'src')).sort(), ['app.js']);
    assert.equal(readFileSync(join(webFolder, 'package.json'), 'utf8'), originalPackageJson);
    assert.equal(readFileSync(join(webFolder, 'src', 'app.js'), 'utf8'), originalAppJs);

    // New sibling VR project has starter: 'iwsdk-vr', madeFrom: webProjectId, and all ART7/GQ1b starter files.
    const vrFolder = join(realpathSync.native(root), 'wind-chimes-vr');
    for (const relPath of [
      'src/index.ts',
      'src/palette.ts',
      'src/look.ts',
      'src/decor.ts',
      'src/motion.ts',
      'src/play-input.ts',
      'public/scenes/main.iwsdk.scene.json',
      'iwsdk.config.json',
      'AGENTS.md',
    ]) {
      assert.equal(
        existsSync(join(vrFolder, relPath)),
        true,
        `expected starter file ${relPath} in VR project folder`,
      );
    }
    assert.equal(harness.controlPlane.projection.project(vrProjectId)?.starter, 'iwsdk-vr');
    assert.equal(harness.controlPlane.projection.madeFrom(vrProjectId), webProjectId);
    const madeFromEvent = client.messages.find(
      (m) =>
        m.type === 'event' &&
        m.event.project_id === vrProjectId &&
        m.event.event_type === 'project.made_from',
    );
    const expectedVrInstruction = vrVersionInstruction(
      join(realpathSync.native(root), 'wind-chimes-web'),
    );
    assert.deepEqual(
      madeFromEvent?.type === 'event' &&
        madeFromEvent.event.event_type === 'project.made_from' &&
        madeFromEvent.event.payload,
      {
        original_project_id: webProjectId,
        instruction: expectedVrInstruction,
      },
    );

    // 3. Concurrent multi-project workstreams + Salidium (understanding) & Seorak (evaluation + usage-limits).
    const createWebWs = makeCommand('workstream.create', {
      project_id: webProjectId,
      title: 'Review web chimes',
      objective: null,
    });
    client.command(createWebWs);
    const createWebWsDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === createWebWs.command_id,
    );
    const webWsId = (
      createWebWsDone.type === 'event' &&
      createWebWsDone.event.event_type === 'command.completed' &&
      createWebWsDone.event.payload.result?.kind === 'workstream_created'
        ? createWebWsDone.event.payload.result.workstream_id
        : null
    ) as WorkstreamId;
    assert.ok(webWsId !== null);

    const createVrWs = makeCommand('workstream.create', {
      project_id: vrProjectId,
      title: 'Build VR chimes',
      objective: null,
    });
    client.command(createVrWs);
    const createVrWsDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === createVrWs.command_id,
    );
    const vrWsId = (
      createVrWsDone.type === 'event' &&
      createVrWsDone.event.event_type === 'command.completed' &&
      createVrWsDone.event.payload.result?.kind === 'workstream_created'
        ? createVrWsDone.event.payload.result.workstream_id
        : null
    ) as WorkstreamId;
    assert.ok(vrWsId !== null);

    const startWebExec = makeCommand('execution.start', {
      workstream_id: webWsId,
      runtime_id: 'claude-agent' as RuntimeId,
      instruction: 'Inspect Wind Chimes Web.',
      options: {},
      model_ref: null,
    });
    const startVrExec = makeCommand('execution.start', {
      workstream_id: vrWsId,
      runtime_id: 'codex' as RuntimeId,
      instruction: expectedVrInstruction,
      options: {},
      model_ref: null,
    });
    client.command(startWebExec);
    client.command(startVrExec);

    const startWebDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === startWebExec.command_id,
    );
    const webExecId = (
      startWebDone.type === 'event' &&
      startWebDone.event.event_type === 'command.completed' &&
      startWebDone.event.payload.result?.kind === 'execution_created'
        ? startWebDone.event.payload.result.execution_id
        : null
    ) as ExecutionId;
    assert.ok(webExecId !== null);

    const startVrDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === startVrExec.command_id,
    );
    const vrExecId = (
      startVrDone.type === 'event' &&
      startVrDone.event.event_type === 'command.completed' &&
      startVrDone.event.payload.result?.kind === 'execution_created'
        ? startVrDone.event.payload.result.execution_id
        : null
    ) as ExecutionId;
    assert.ok(vrExecId !== null);
    assert.equal(vrStartedInstructions[0], expectedVrInstruction);

    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === webExecId &&
        m.event.event_type === 'runtime.turn.started',
    );
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === vrExecId &&
        m.event.event_type === 'runtime.turn.started',
    );

    // Both workstreams across the two projects are running concurrently.
    assert.equal(harness.controlPlane.projection.workstream(webWsId)?.status, 'running');
    assert.equal(harness.controlPlane.projection.workstream(vrWsId)?.status, 'running');

    // Query Salidium understanding and Seorak evaluation + usage-limits while both executions run concurrently.
    const webUnderstandingRes = await fetchWithProof(
      fetch,
      baseUrl,
      token,
      `/api/executions/${webExecId}/understanding`,
    );
    assert.equal(webUnderstandingRes.status, 200);
    const webUnderstandingBody = (await webUnderstandingRes.json()) as UnderstandingResponse;
    assert.equal(webUnderstandingBody.execution_id, webExecId);
    assert.equal(webUnderstandingBody.result.availability, 'available');
    assert.equal(
      webUnderstandingBody.result.understanding.verdict.headline,
      '4 files changed, unverified',
    );
    assert.equal(webUnderstandingBody.result.understanding.verdict.epistemic, 'observed');
    assert.equal(webUnderstandingBody.result.understanding.latest_statement?.epistemic, 'reported');

    const webEvaluationRes = await fetchWithProof(
      fetch,
      baseUrl,
      token,
      `/api/executions/${webExecId}/evaluation`,
    );
    assert.equal(webEvaluationRes.status, 200);
    const webEvaluationBody = (await webEvaluationRes.json()) as EvaluationResponse;
    assert.equal(webEvaluationBody.execution_id, webExecId);
    assert.equal(webEvaluationBody.result.availability, 'available');
    assert.equal(webEvaluationBody.result.evaluation.outcome.measure?.commits_landed, 2);
    assert.equal(webEvaluationBody.result.evaluation.verification.lens?.by_kind.length, 2);

    const vrUnderstandingRes = await fetchWithProof(
      fetch,
      baseUrl,
      token,
      `/api/executions/${vrExecId}/understanding`,
    );
    assert.equal(vrUnderstandingRes.status, 200);
    const vrUnderstandingBody = (await vrUnderstandingRes.json()) as UnderstandingResponse;
    assert.equal(vrUnderstandingBody.execution_id, vrExecId);
    assert.equal(vrUnderstandingBody.result.availability, 'unavailable');
    assert.equal(vrUnderstandingBody.result.reason.code, 'runtime_not_observed');
    assert.match(vrUnderstandingBody.result.reason.message, /Halcyonic's own Codex home/);

    const vrEvaluationRes = await fetchWithProof(
      fetch,
      baseUrl,
      token,
      `/api/executions/${vrExecId}/evaluation`,
    );
    assert.equal(vrEvaluationRes.status, 200);
    const vrEvaluationBody = (await vrEvaluationRes.json()) as EvaluationResponse;
    assert.equal(vrEvaluationBody.execution_id, vrExecId);
    assert.equal(vrEvaluationBody.result.availability, 'unavailable');
    assert.equal(vrEvaluationBody.result.reason.code, 'runtime_not_observed');
    assert.match(vrEvaluationBody.result.reason.message, /Halcyonic's own Codex home/);

    const usageLimitsRes = await fetchWithProof(fetch, baseUrl, token, '/api/usage-limits');
    assert.equal(usageLimitsRes.status, 200);
    const usageLimits = (await usageLimitsRes.json()) as UsageLimitsResponse;
    assert.equal(usageLimits.availability, 'available');
    assert.equal(usageLimits.readings.length, 1);
    assert.equal(usageLimits.readings[0]?.agent, 'codex');
    assert.equal(usageLimits.readings[0]?.window, 'weekly');
    assert.equal(usageLimits.readings[0]?.used_percent, 62);

    // Release both turns: vrExecId modifies wind-chimes-vr/src/index.ts, triggering FileObserver ->
    // automatic internal project.check (A1) -> preview.start -> open_on_mac.
    releaseConcurrentTurns();

    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === webExecId &&
        m.event.event_type === 'runtime.turn.completed',
    );
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === vrExecId &&
        m.event.event_type === 'runtime.turn.completed',
    );

    const vrAutoCheckAccepted = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.project_id === vrProjectId &&
        m.event.event_type === 'command.accepted' &&
        m.event.payload.command.command_type === 'project.check' &&
        m.event.payload.received_via === 'internal',
    );
    const vrCheckCmdId =
      vrAutoCheckAccepted.type === 'event' &&
      vrAutoCheckAccepted.event.event_type === 'command.accepted'
        ? vrAutoCheckAccepted.event.payload.command.command_id
        : '';
    assert.ok(vrCheckCmdId !== '');

    const vrCheckDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === vrCheckCmdId,
    );
    assert.equal(
      vrCheckDone.type === 'event' &&
        vrCheckDone.event.event_type === 'command.completed' &&
        vrCheckDone.event.payload.result?.kind === 'project_checked' &&
        vrCheckDone.event.payload.result.passed &&
        vrCheckDone.event.payload.result.ready,
      true,
    );

    await runPreviewCommand(client, makeCommand, 'project.preview.start', {
      project_id: vrProjectId,
    });
    const openVrPlay = makeCommand('project.open_on_mac', { project_id: vrProjectId });
    client.command(openVrPlay);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === openVrPlay.command_id,
    );
    assert.equal(playRuns.started, 1);

    // Original non-VR project folder remains completely untouched after all executions and checks.
    assert.deepEqual(readdirSync(webFolder).sort(), ['package.json', 'src']);
    assert.equal(readFileSync(join(webFolder, 'package.json'), 'utf8'), originalPackageJson);
    assert.equal(readFileSync(join(webFolder, 'src', 'app.js'), 'utf8'), originalAppJs);
    assert.equal(client.invalid.length, 0);
  });

  test("drives a real task's path without a model: an OpenCode task's approvals, the Mac's reading of its files, Understand and Checks for Halcyonic's own store, and an undo byte for byte", async (t: TestContext) => {
    // As the real run on 2026-11-03 went (local-models.md), with a synthetic stand-in for OpenCode.
    const tempDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-journey-real-')));
    t.after(() => rmSync(tempDir, { recursive: true, force: true }));
    const root = join(tempDir, 'projects');
    const folder = join(root, 'adder');
    mkdirSync(folder, { recursive: true });
    const original = {
      'math.js': 'export function add(a, b) {\n  return a + b;\n}\n',
      'math.test.js':
        "import assert from 'node:assert/strict';\nimport { add } from './math.js';\n",
    };
    for (const [name, text] of Object.entries(original)) writeFileSync(join(folder, name), text);
    const git = (...args: string[]) =>
      new Promise<string>((resolve, reject) =>
        execFile('/usr/bin/git', ['-C', folder, ...args], (error, stdout) =>
          error ? reject(error) : resolve(stdout.trim()),
        ),
      );
    await git('init', '-q', '-b', 'main');
    await git('add', '-A');
    await git(
      '-c',
      'user.name=Journey',
      '-c',
      'user.email=journey@example.invalid',
      'commit',
      '-qm',
      'Start',
    );
    const head = await git('rev-parse', 'HEAD');
    const hashes = () =>
      Object.fromEntries(
        Object.keys(original).map((name) => [
          name,
          createHash('sha256')
            .update(readFileSync(join(folder, name)))
            .digest('hex'),
        ]),
      );
    const before = hashes();

    // Halcyonic's sandboxed OpenCode store, made at the server's launch, before the task starts.
    const dataDir = join(tempDir, 'data');
    mkdirSync(join(dataDir, 'opencode-sandbox', 'data', 'opencode'), { recursive: true });
    writeFileSync(join(dataDir, 'opencode-sandbox', 'data', 'opencode', 'opencode.db'), '');
    const time = createVirtualTime(new Date(Date.now() + 1_000));

    // A Salidium that observes OpenCode, and a Seorak: neither may be asked about this task.
    const salidium = await FakeSalidium.start();
    t.after(() => salidium.close());
    salidium.minor = 1;
    salidium.providers = ['claude-code', 'codex', 'salidium/opencode'];
    salidium.writeDiscovery();
    const seorak = await FakeSeorak.start();
    t.after(() => seorak.close());
    const credential = (name: string, token: string) => {
      const path = join(dataDir, name);
      writeFileSync(path, `${token}\n`, { mode: 0o600 });
      return path;
    };

    const fileObserver = new FileObserver({ protect: [dataDir] });
    const answers = new Map<string, (decision: 'approve' | 'deny') => void>();
    const nativeId = 'ses_1a2b3c4d5e6fJourneyRealRun0';
    const openCode: RuntimeAdapter = {
      descriptor: {
        runtime_id: 'opencode' as RuntimeId,
        kind: 'opencode',
        display_name: 'OpenCode (synthetic journey adapter)',
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
        uses_project_location: true,
        reports_tool_activity: true,
        reports_tool_results: false,
        approval_may_leave_sandbox: false,
      },
      validateStartOptions: () => ({ ok: true }),
      async startExecution(request: StartExecutionRequest) {
        const directory = request.directory as string;
        await fileObserver.baseline(request.execution.execution_id, directory);
        let seq = 0;
        const send = (type: string, payload: unknown) => {
          seq += 1;
          request.emit({
            native_event_id: `${nativeId}:${String(seq)}`,
            sequence: seq,
            occurred_at: time.now().toISOString() as never,
            type: type as never,
            payload: payload as never,
            provenance: { epistemic: 'observed', native_type: `opencode/${type}` },
          });
        };
        const ask = (approvalId: string, summary: string) =>
          new Promise<'approve' | 'deny'>((resolve) => {
            answers.set(approvalId, resolve);
            send('runtime.approval.requested', {
              approval_id: approvalId,
              subject: { kind: 'tool_use', tool_name: 'shell', summary },
              complete: true,
            });
          });
        send('runtime.execution.started', { native_id: nativeId });
        void (async () => {
          send('runtime.turn.started', { turn_id: 'turn-1' });
          send('runtime.tool.started', { tool_call_id: 'edit', tool_name: 'edit', title: null });
          writeFileSync(
            join(directory, 'math.js'),
            `${original['math.js']}\nexport function multiply(a, b) {\n  return a * b;\n}\n`,
          );
          writeFileSync(
            join(directory, 'math.test.js'),
            `${original['math.test.js']}// multiply\n`,
          );
          send('runtime.tool.completed', { tool_call_id: 'edit', outcome: 'succeeded' });
          for (const [call, approvalId, summary] of [
            ['test', 'per_test', "[in the task's folder] node --test"],
            ['touch', 'per_touch', "[in the task's folder] touch ../outside.txt"],
          ] as const) {
            send('runtime.tool.started', { tool_call_id: call, tool_name: 'shell', title: null });
            const decision = await ask(approvalId, summary);
            send('runtime.approval.resolved', {
              approval_id: approvalId,
              decision: decision === 'approve' ? 'approved' : 'denied',
            });
            send('runtime.tool.completed', {
              tool_call_id: call,
              outcome: decision === 'approve' ? 'succeeded' : 'failed',
            });
            // A denial without a message ends OpenCode's turn.
            if (decision === 'deny') send('runtime.turn.interrupted', { turn_id: 'turn-1' });
          }
        })();
        return { native_id: nativeId };
      },
      async respondToApproval(request) {
        answers.get(request.approval_id)?.(request.decision);
      },
      close: async () => {},
    };

    const server = await startTestServer({
      time,
      adapters: () => [openCode],
      locations: createHostLocations([root]),
      checkpoints: {
        store: new CheckpointStore({
          git: '/usr/bin/git',
          directory: join(dataDir, 'checkpoints'),
        }),
      },
      understanding: salidiumUnderstanding({
        home: salidium.home,
        credentialPath: credential('salidium-credential', salidium.token),
        codexHome: join(dataDir, 'codex-home'),
      }),
      evaluation: seorakEvaluation({
        credentialPath: credential('seorak-credential', seorak.token),
        port: seorak.port,
        codexHome: join(dataDir, 'codex-home'),
      }),
    });
    t.after(() => server.stop());
    fileObserver.attach({
      publisher: server.controlPlane.publisher,
      recorder: server.controlPlane.recorder,
      projection: server.controlPlane.projection,
      clock: server.controlPlane.clock,
      logger: capturingLogger().logger,
    });

    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    t.after(() => client.close());
    client.hello(CLIENT_INFO);
    await client.waitFor((m) => m.type === 'snapshot');
    let seq = 0;
    const submit = async (command_type: CommandEnvelope['command_type'], payload: unknown) => {
      seq += 1;
      const command = {
        schema_version: 1 as const,
        command_id: `00000000-0000-4000-b000-${String(seq).padStart(12, '0')}` as CommandId,
        issued_at: '2026-11-03T18:00:00.000Z',
        client: CLIENT_INFO,
        command_type,
        payload,
      } as CommandEnvelope;
      client.command(command);
      const ack = await client.waitFor(
        (m) => m.type === 'command_ack' && m.command_id === command.command_id,
      );
      assert.ok(ack.type === 'command_ack' && ack.disposition === 'accepted', command_type);
      return ack.command;
    };
    const event = (type: string) =>
      client.waitFor((m) => m.type === 'event' && m.event.event_type === type, 30_000);

    await submit('project.create', {
      name: 'Adder',
      location: { kind: 'existing_folder', root: realpathSync.native(root), folder_name: 'adder' },
    });
    const projectId = server.controlPlane.projection
      .projects()
      .find((project) => project.name === 'Adder')?.project_id as ProjectId;
    await submit('workstream.create', {
      project_id: projectId,
      title: 'Add multiply',
      objective: null,
    });
    const workstreamId = server.controlPlane.projection
      .workstreams()
      .find((workstream) => workstream.project_id === projectId)?.workstream_id as WorkstreamId;
    await submit('execution.start', {
      workstream_id: workstreamId,
      runtime_id: 'opencode',
      instruction: 'Add multiply and its test, run the tests, then touch ../outside.txt.',
      options: {},
      model_ref: null,
    });
    const executionId = server.controlPlane.projection.workstream(workstreamId)
      ?.current_execution_id as ExecutionId;

    // The person approves the test run and denies the write outside the folder.
    for (const [approvalId, decision] of [
      ['per_test', 'approve'],
      ['per_touch', 'deny'],
    ] as const) {
      await client.waitFor(
        (m) =>
          m.type === 'event' &&
          m.event.event_type === 'runtime.approval.requested' &&
          m.event.payload.approval_id === approvalId,
      );
      await submit('execution.respond_to_approval', {
        execution_id: executionId,
        approval_id: approvalId,
        decision,
        message: null,
      });
    }
    const observed = await event('execution.files.observed');
    assert.ok(
      observed.type === 'event' && observed.event.event_type === 'execution.files.observed',
    );
    assert.deepEqual(
      observed.event.payload.changed.files.map((file) => [
        file.path,
        file.lines_before,
        file.lines_after,
      ]),
      [
        ['math.js', 3, 7],
        ['math.test.js', 2, 3],
      ],
    );
    const execution = server.controlPlane.projection.execution(executionId);
    assert.equal(execution?.status, 'interrupted');
    assert.deepEqual(execution?.pending_approvals, []);
    const decisions = [...server.journal.readAll()]
      .map((stored) => stored.event)
      .filter((stored) => stored.event_type === 'runtime.approval.resolved')
      .map((stored) => (stored.payload as { decision: string }).decision);
    assert.deepEqual(decisions, ['approved', 'denied']);

    // Understand and Checks say plainly that neither product reads this task, and ask nothing.
    const understanding = (await (
      await fetchWithProof(
        fetch,
        server.baseUrl,
        server.token,
        `/api/executions/${executionId}/understanding`,
      )
    ).json()) as UnderstandingResponse;
    assert.equal(understanding.result.availability, 'unavailable');
    assert.equal(
      understanding.result.availability === 'unavailable' && understanding.result.reason.code,
      'runtime_not_observed',
    );
    assert.match(
      understanding.result.availability === 'unavailable'
        ? understanding.result.reason.message
        : '',
      /Halcyonic's own OpenCode store/,
    );
    const evaluation = (await (
      await fetchWithProof(
        fetch,
        server.baseUrl,
        server.token,
        `/api/executions/${executionId}/evaluation`,
      )
    ).json()) as EvaluationResponse;
    assert.equal(
      evaluation.result.availability === 'unavailable' && evaluation.result.reason.code,
      'runtime_not_observed',
    );
    assert.equal(salidium.requests.length, 0, 'Salidium was asked about a task it cannot read');
    assert.equal(seorak.requests.length, 0, 'Seorak was asked about a task it cannot read');

    // Undo: the checkpoint before the task, its preview, then every file back as it was.
    const checkpoints = (await (
      await fetchWithProof(
        fetch,
        server.baseUrl,
        server.token,
        `/api/projects/${projectId}/checkpoints`,
      )
    ).json()) as {
      result: { availability: string; checkpoints: { checkpoint_id: string; reason: string }[] };
    };
    const beforeTask = checkpoints.result.checkpoints.find((item) => item.reason === 'before_task');
    assert.ok(beforeTask !== undefined, 'a checkpoint before the task');
    const preview = (await (
      await fetchWithProof(
        fetch,
        server.baseUrl,
        server.token,
        `/api/projects/${projectId}/checkpoints/${beforeTask.checkpoint_id}/restore-preview`,
      )
    ).json()) as {
      result: {
        preview: { changed_back: { paths: string[] }; project_head_moved: boolean | null };
      };
    };
    assert.deepEqual(preview.result.preview.changed_back.paths, ['math.js', 'math.test.js']);
    assert.equal(preview.result.preview.project_head_moved, false);
    await submit('project.restore', {
      project_id: projectId,
      checkpoint_id: beforeTask.checkpoint_id,
    });
    await event('project.restored');
    assert.deepEqual(hashes(), before, 'every file byte for byte as before the task');
    assert.equal(await git('rev-parse', 'HEAD'), head);
    assert.equal(client.invalid.length, 0);
  });

  test('switches a task to another agent and model: a new execution at rest, refused while one is active (ADMIT), each read and undone as its own task', async (t: TestContext) => {
    // What XR's switch builds on (SWITCH-PATH, 2026-11-03), with synthetic stand-ins for OpenCode and Codex.
    const tempDir = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-journey-switch-')));
    t.after(() => rmSync(tempDir, { recursive: true, force: true }));
    const root = join(tempDir, 'projects');
    const folder = join(root, 'garden');
    mkdirSync(folder, { recursive: true });
    const original = { 'notes.md': '# Garden\n', 'math.js': 'export const one = 1;\n' };
    for (const [name, text] of Object.entries(original)) writeFileSync(join(folder, name), text);
    const read = () =>
      Object.fromEntries(
        Object.keys(original).map((name) => [name, readFileSync(join(folder, name), 'utf8')]),
      );

    // Halcyonic's own sandboxed OpenCode store and Codex home, where these runtimes keep sessions.
    const dataDir = join(tempDir, 'data');
    mkdirSync(join(dataDir, 'opencode-sandbox', 'data', 'opencode'), { recursive: true });
    writeFileSync(join(dataDir, 'opencode-sandbox', 'data', 'opencode', 'opencode.db'), '');
    const codexHome = join(dataDir, 'codex-home');
    const time = createVirtualTime(new Date(Date.now() + 1_000));

    const salidium = await FakeSalidium.start();
    t.after(() => salidium.close());
    salidium.minor = 1;
    salidium.providers = ['claude-code', 'codex', 'salidium/opencode'];
    salidium.writeDiscovery();
    const seorak = await FakeSeorak.start();
    t.after(() => seorak.close());
    const credential = (name: string, token: string) => {
      const path = join(dataDir, name);
      writeFileSync(path, `${token}\n`, { mode: 0o600 });
      return path;
    };

    const fileObserver = new FileObserver({ protect: [dataDir] });
    const answers = new Map<string, (decision: 'approve' | 'deny') => void>();
    const MODEL = 'ollama/stand-in:1b';
    /**
     * A synthetic stand-in: each start's instruction names what its one turn does to the folder,
     * `notes`, `math` or nothing, and `ask` makes it wait on an approval first.
     */
    const standIn = (kind: 'opencode' | 'codex'): RuntimeAdapter => ({
      descriptor: {
        runtime_id: kind as RuntimeId,
        kind,
        display_name: `${kind === 'opencode' ? 'OpenCode' : 'Codex'} (synthetic journey adapter)`,
        synthetic: true,
        capabilities: {
          start_execution: true,
          instruct_at_rest: false,
          instruct_while_running: false,
          respond_to_approval: true,
          answer_question: false,
          interrupt: false,
        },
        model_choice: kind === 'codex' ? 'listed' : 'none',
        uses_project_location: true,
        reports_tool_activity: true,
        reports_tool_results: false,
        // As each declares: OpenCode's approved commands stay in Halcyonic's sandbox, Codex's may not.
        approval_may_leave_sandbox: kind === 'codex',
      },
      validateStartOptions: () => ({ ok: true }),
      ...(kind === 'codex' && {
        listModels: async () => [
          {
            model_ref: MODEL,
            display_name: 'stand-in:1b (Ollama)',
            served: 'this_mac' as const,
            tool_calling: 'unknown' as const,
            context_tokens: 65_536,
          },
        ],
      }),
      async startExecution(request: StartExecutionRequest) {
        const directory = request.directory as string;
        await fileObserver.baseline(request.execution.execution_id, directory);
        const nativeId =
          kind === 'codex' ? randomUUID() : `ses_${randomUUID().replaceAll('-', '')}`;
        if (kind === 'codex') {
          // Codex writes its rollout in Halcyonic's own Codex home, by the start's local date.
          const day = time.now();
          const dated = join(
            codexHome,
            'sessions',
            String(day.getFullYear()),
            String(day.getMonth() + 1).padStart(2, '0'),
            String(day.getDate()).padStart(2, '0'),
          );
          mkdirSync(dated, { recursive: true });
          writeFileSync(join(dated, `rollout-2026-11-03T20-00-00-${nativeId}.jsonl`), '{}\n');
        }
        let seq = 0;
        const send = (type: string, payload: unknown) => {
          seq += 1;
          request.emit({
            native_event_id: `${nativeId}:${String(seq)}`,
            sequence: seq,
            occurred_at: time.now().toISOString() as never,
            type: type as never,
            payload: payload as never,
            provenance: { epistemic: 'observed', native_type: `${kind}/${type}` },
          });
        };
        send('runtime.execution.started', { native_id: nativeId });
        if (request.model_ref !== null)
          send('runtime.model.used', { model_ref: request.model_ref });
        void (async () => {
          send('runtime.turn.started', { turn_id: 'turn-1' });
          if (request.instruction.includes('ask')) {
            const approvalId = `per_${nativeId.slice(-8)}`;
            const decision = await new Promise<'approve' | 'deny'>((resolve) => {
              answers.set(approvalId, resolve);
              send('runtime.tool.started', {
                tool_call_id: 'shell',
                tool_name: 'shell',
                title: null,
              });
              send('runtime.approval.requested', {
                approval_id: approvalId,
                subject: {
                  kind: 'tool_use',
                  tool_name: 'shell',
                  summary: "[in the task's folder] ls",
                },
                complete: true,
              });
            });
            send('runtime.approval.resolved', {
              approval_id: approvalId,
              decision: decision === 'approve' ? 'approved' : 'denied',
            });
            send('runtime.tool.completed', { tool_call_id: 'shell', outcome: 'failed' });
            send('runtime.turn.interrupted', { turn_id: 'turn-1' });
            return;
          }
          for (const name of ['notes', 'math'] as const) {
            if (!request.instruction.includes(name)) continue;
            const file = name === 'notes' ? 'notes.md' : 'math.js';
            writeFileSync(
              join(directory, file),
              `${readFileSync(join(directory, file), 'utf8')}// ${kind}\n`,
            );
          }
          send('runtime.turn.completed', { turn_id: 'turn-1' });
        })();
        return { native_id: nativeId };
      },
      async respondToApproval(request) {
        answers.get(request.approval_id)?.(request.decision);
      },
      close: async () => {},
    });

    const server = await startTestServer({
      time,
      adapters: () => [standIn('opencode'), standIn('codex')],
      locations: createHostLocations([root]),
      checkpoints: {
        store: new CheckpointStore({
          git: '/usr/bin/git',
          directory: join(dataDir, 'checkpoints'),
        }),
      },
      understanding: salidiumUnderstanding({
        home: salidium.home,
        credentialPath: credential('salidium-credential', salidium.token),
        codexHome,
      }),
      evaluation: seorakEvaluation({
        credentialPath: credential('seorak-credential', seorak.token),
        port: seorak.port,
        codexHome,
      }),
    });
    t.after(() => server.stop());
    fileObserver.attach({
      publisher: server.controlPlane.publisher,
      recorder: server.controlPlane.recorder,
      projection: server.controlPlane.projection,
      clock: server.controlPlane.clock,
      logger: capturingLogger().logger,
    });

    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    t.after(() => client.close());
    client.hello(CLIENT_INFO);
    await client.waitFor((m) => m.type === 'snapshot');
    let seq = 0;
    const submit = async (command_type: CommandEnvelope['command_type'], payload: unknown) => {
      seq += 1;
      const command = {
        schema_version: 1 as const,
        command_id: `00000000-0000-4000-c000-${String(seq).padStart(12, '0')}` as CommandId,
        issued_at: '2026-11-03T20:00:00.000Z',
        client: CLIENT_INFO,
        command_type,
        payload,
      } as CommandEnvelope;
      client.command(command);
      const ack = await client.waitFor(
        (m) => m.type === 'command_ack' && m.command_id === command.command_id,
      );
      assert.ok(ack.type === 'command_ack', command_type);
      return ack;
    };
    const projection = server.controlPlane.projection;
    const atRest = (executionId: ExecutionId) =>
      client.waitFor(
        () =>
          ['completed', 'failed', 'interrupted'].includes(
            projection.execution(executionId)?.status ?? '',
          ),
        30_000,
      );
    const filesOf = async (executionId: ExecutionId) => {
      const observed = await client.waitFor(
        (m) =>
          m.type === 'event' &&
          m.event.event_type === 'execution.files.observed' &&
          m.event.execution_id === executionId,
        30_000,
      );
      assert.ok(
        observed.type === 'event' && observed.event.event_type === 'execution.files.observed',
      );
      return observed.event.payload.changed.files.map((file) => file.path);
    };
    const reads = async (executionId: ExecutionId) => {
      const answer = async (path: string) =>
        (await (
          await fetchWithProof(
            fetch,
            server.baseUrl,
            server.token,
            `/api/executions/${executionId}/${path}`,
          )
        ).json()) as UnderstandingResponse | EvaluationResponse;
      const said = (result: UnderstandingResponse['result'] | EvaluationResponse['result']) =>
        result.availability === 'available'
          ? 'available'
          : `${result.availability} ${result.reason.code} ${result.reason.message}`;
      return {
        understanding: said((await answer('understanding')).result),
        evaluation: said((await answer('evaluation')).result),
      };
    };

    await submit('project.create', {
      name: 'Garden',
      location: { kind: 'existing_folder', root: realpathSync.native(root), folder_name: 'garden' },
    });
    const projectId = projection.projects().find((project) => project.name === 'Garden')
      ?.project_id as ProjectId;
    await submit('workstream.create', {
      project_id: projectId,
      title: 'Tend the garden',
      objective: null,
    });
    const workstreamId = projection.workstreams().find((w) => w.project_id === projectId)
      ?.workstream_id as WorkstreamId;
    const start = async (runtime: 'opencode' | 'codex', instruction: string) => {
      const ack = await submit('execution.start', {
        workstream_id: workstreamId,
        runtime_id: runtime,
        instruction,
        options: {},
        model_ref: runtime === 'codex' ? MODEL : null,
      });
      assert.ok(ack.type === 'command_ack');
      assert.equal(
        ack.disposition,
        'accepted',
        `${runtime}: ${JSON.stringify(ack.command?.rejection)}`,
      );
      return projection.workstream(workstreamId)?.current_execution_id as ExecutionId;
    };

    // 1. At rest: OpenCode's task ends, then the same task goes on with Codex and another model.
    const first = await start('opencode', 'Write in notes.');
    await atRest(first);
    assert.deepEqual(await filesOf(first), ['notes.md']);
    const second = await start('codex', 'Change math.');
    assert.notEqual(second, first, 'a new execution');
    await atRest(second);
    // The Mac's reading of the second turn starts from the second start: only its own change.
    assert.deepEqual(await filesOf(second), ['math.js']);
    const workstream = projection.workstream(workstreamId);
    assert.deepEqual(workstream?.execution_ids, [first, second]);
    assert.equal(workstream?.current_execution_id, second);
    assert.equal(workstream?.status, 'completed');
    assert.deepEqual(
      [first, second].map((id) => {
        const execution = projection.execution(id);
        return [execution?.runtime.runtime_id, execution?.model_ref, execution?.status];
      }),
      [
        ['opencode', null, 'completed'],
        ['codex', MODEL, 'completed'],
      ],
    );
    // Understand and Checks follow each execution's own runtime, never the task's latest.
    const firstReads = await reads(first);
    const secondReads = await reads(second);
    assert.match(
      firstReads.understanding,
      /^unavailable runtime_not_observed .*own OpenCode store/,
    );
    assert.match(firstReads.evaluation, /^unavailable runtime_not_observed .*opencode runtime/);
    assert.match(secondReads.understanding, /^unavailable runtime_not_observed .*own Codex home/);
    assert.match(secondReads.evaluation, /^unavailable runtime_not_observed .*own Codex home/);

    // 2. While one is active: a start is refused (ADMIT), so two agents never work in the one folder.
    const waiting = await start('opencode', 'ask before anything');
    await client.waitFor(
      () => projection.execution(waiting)?.status === 'waiting_for_human',
      30_000,
    );
    const beside = await submit('execution.start', {
      workstream_id: workstreamId,
      runtime_id: 'codex',
      instruction: 'Look, change nothing.',
      options: {},
      model_ref: MODEL,
    });
    assert.ok(beside.type === 'command_ack');
    assert.equal(beside.disposition, 'rejected');
    assert.deepEqual(beside.command?.rejection, {
      code: 'invalid_state',
      message: 'This task is waiting_for_human. Stop it first, or start another task.',
    });
    const during = projection.workstream(workstreamId);
    assert.deepEqual(during?.execution_ids, [first, second, waiting], 'no execution beside it');
    assert.equal(
      during?.current_execution_id,
      waiting,
      'the waiting execution stays the current one',
    );
    assert.equal(
      projection.execution(waiting)?.status,
      'waiting_for_human',
      'the first still waits',
    );
    assert.deepEqual(
      during?.attention.reasons.map((reason) => [reason.kind, reason.execution_id]),
      [['approval_pending', waiting]],
      "the waiting execution's approval is in the task's attention",
    );
    const pending = projection.execution(waiting)?.pending_approvals[0];
    assert.ok(pending !== undefined);
    const denied = await submit('execution.respond_to_approval', {
      execution_id: waiting,
      approval_id: pending.approval_id,
      decision: 'deny',
      message: null,
    });
    assert.ok(denied.type === 'command_ack' && denied.disposition === 'accepted', 'answered');
    await atRest(waiting);

    // 3. Undo: each start took a checkpoint, so one execution's changes come back apart from another's.
    const checkpoints = (await (
      await fetchWithProof(
        fetch,
        server.baseUrl,
        server.token,
        `/api/projects/${projectId}/checkpoints`,
      )
    ).json()) as {
      result: {
        checkpoints: { checkpoint_id: string; reason: string; execution_id: string | null }[];
      };
    };
    const beforeTask = (executionId: ExecutionId) =>
      checkpoints.result.checkpoints.find(
        (item) => item.reason === 'before_task' && item.execution_id === executionId,
      )?.checkpoint_id;
    assert.deepEqual(
      [first, second, waiting].map((id) => beforeTask(id) !== undefined),
      [true, true, true],
      'a checkpoint before every start',
    );
    const restore = async (checkpointId: string | undefined) => {
      assert.ok(checkpointId !== undefined);
      await submit('project.restore', { project_id: projectId, checkpoint_id: checkpointId });
      await client.waitFor(
        (m) =>
          m.type === 'event' &&
          (m.event.event_type === 'project.restored' ||
            m.event.event_type === 'project.restore_incomplete') &&
          m.event.payload.checkpoint_id === checkpointId,
        30_000,
      );
    };
    await restore(beforeTask(second));
    assert.deepEqual(read(), {
      'notes.md': `${original['notes.md']}// opencode\n`,
      'math.js': original['math.js'],
    });
    await restore(beforeTask(first));
    assert.deepEqual(read(), original, 'both executions undone, byte for byte');
    assert.equal(client.invalid.length, 0);
  });
});
