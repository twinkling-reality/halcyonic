import assert from 'node:assert/strict';
import { type ChildProcess, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PassThrough } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  ExecutionId,
  ProjectId,
  ProjectsResponse,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import { MOCK_MODELS, MockRuntimeAdapter, type Scenario } from '@halcyonic/integration-mock';
import {
  type RuntimeAdapter,
  type StartExecutionRequest,
  systemClock,
  systemScheduler,
} from '@halcyonic/runtime-core';
import { RealtimeClient } from './client/realtime-client.ts';
import { controlPlaneDraft, NO_CAUSE } from './core/drafts.ts';
import { fetchWithProof } from './http/security.ts';
import { createHostLocations } from './locations.ts';
import type { CheckResult } from './preview/check.ts';
import { type ChromeRun, DevToolsPipe } from './preview/chrome.ts';
import type { ServeArgs } from './preview/preview.ts';
import { writeWav } from './speech/wav.ts';
import type { EngineResult, SpeechEngine } from './speech/whisper.ts';
import { HostStarters } from './starters.ts';
import { startTestServer } from './testing/harness.ts';

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
    },
  ],
]);

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
    let holdStart = true;
    let releaseStart: () => void = () => {};
    const startGate = new Promise<void>((resolve) => {
      releaseStart = resolve;
    });

    const adapter: RuntimeAdapter = {
      descriptor: innerMock.descriptor,
      ...(innerMock.listModels === undefined ? {} : { listModels: innerMock.listModels }),
      validateStartOptions: (options, modelRef) =>
        innerMock.validateStartOptions(options, modelRef),
      async startExecution(request: StartExecutionRequest) {
        startedInstructions.push(request.instruction);
        if (holdStart) await startGate;
        return innerMock.startExecution(request);
      },
      async sendInstruction(request) {
        sentInstructions.push(request.text);
        return innerMock.sendInstruction(request);
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
        runCheck: async (options) => {
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
    const client = await RealtimeClient.connect(server.wsUrl, server.token);
    t.after(async () => {
      holdStart = false;
      releaseStart();
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

    // 4a. Check 1 fails tsc -> preview is refused -> voice instruction carries FX1 once, journal keeps only person's words.
    const check1Cmd = makeCommand('project.check', { project_id: projectId });
    client.command(check1Cmd);
    const check1Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === check1Cmd.command_id,
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
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.execution_id === executionId &&
        m.event.event_type === 'runtime.turn.completed' &&
        m.position > (check1Done.type === 'event' ? check1Done.position : 0),
    );
    assert.ok(sentInstructions[0]?.startsWith("Make the moles blue.\n\n[The Mac's own check"));
    assert.ok(sentInstructions[0]?.includes('| ./src/index.ts(4,7): error TS2322'));

    // FX1 is given only once per failed check: a second instruction before a new check does not repeat it.
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

    // 4b & 5. Check 2 passes -> preview serves built copy -> Play on my laptop is refused because mock is not confined.
    const check2Cmd = makeCommand('project.check', { project_id: projectId });
    client.command(check2Cmd);
    const check2Done = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === check2Cmd.command_id,
    );
    const check2Pos = check2Done.type === 'event' ? check2Done.position : 0;
    const check2Id =
      check2Done.type === 'event' &&
      check2Done.event.event_type === 'command.completed' &&
      check2Done.event.payload.result?.kind === 'project_checked'
        ? check2Done.event.payload.result.check_id
        : '';
    assert.ok(check2Id !== '');

    const previewStart = makeCommand('project.preview.start', { project_id: projectId });
    client.command(previewStart);
    const previewStartDone = await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === previewStart.command_id,
    );
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

    // 7 & 8. Voice change turn after passing check -> old check and preview remain until a new check stops the preview.
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

    const staleView = await readProjectView();
    assert.equal(staleView?.check?.check_id, check2Id);
    assert.equal(staleView?.preview?.check_id, check2Id);

    const check3Cmd = makeCommand('project.check', { project_id: projectId });
    client.command(check3Cmd);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === check3Cmd.command_id,
    );
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

    const checkCmd = makeCommand('project.check', { project_id: projectId });
    client.command(checkCmd);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === checkCmd.command_id,
    );

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
    const previewStart = makeCommand('project.preview.start', { project_id: projectId });
    client.command(previewStart);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === previewStart.command_id,
    );

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
    const check2Cmd = makeCommand('project.check', { project_id: projectId });
    client.command(check2Cmd);
    await client.waitFor(
      (m) =>
        m.type === 'event' &&
        m.event.event_type === 'command.completed' &&
        m.event.payload.command_id === check2Cmd.command_id,
    );
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
});
