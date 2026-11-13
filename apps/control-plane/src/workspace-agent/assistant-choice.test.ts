import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  ActionReview,
  type CommandId,
  compileValidator,
  type HereScope,
  type WorkspaceAgentStatus,
  WorkspaceRequestProposal,
} from '@halcyonic/contracts';
import { Projection } from '@halcyonic/domain';
import type { HostedProvider } from '@halcyonic/runtime-core';
import { createUuidV7Generator } from '../ids.ts';
import { HostProviders } from '../providers.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import { HOSTED_COST, hostedReview, type Model } from './assistant-model.ts';
import type { ToolTurnResult } from './ollama.ts';
import { assistantModel } from './providers/assistant.ts';
import { KEY_REFUSED, type Protocol } from './providers/provider.ts';
import { reviewWords, WorkspaceRequests } from './requests.ts';

// The person's own choice of where the assistant runs, end to end in one plane (the lead's D1 and D2, 2026-11-12).
// A test provider and an in-process protocol: nothing leaves this process, and the key is a fake value.

const FAKE = 'fake-key-choice-0001';
const ALL: HereScope = { project_id: null, workstream_id: null, execution_id: null };
const provider: HostedProvider = {
  id: 'test-hosted',
  name: 'Test Hosted',
  upstream: 'https://hosted.invalid/v1',
  speaks: ['chat_completions'],
  key: {
    variable: 'TEST_HOSTED_KEY',
    file: 'test-hosted-key',
    field: 'Test Hosted Key',
    header: { name: 'authorization', scheme: 'Bearer' },
  },
  models: ['test-model-1'],
};
const settle = async () => {
  for (let i = 0; i < 24; i++) await new Promise<void>((resolve) => setImmediate(resolve));
};

function plane(t: { after: (fn: () => unknown) => void }, refuse: boolean) {
  const dir = mkdtempSync(join(tmpdir(), 'assistant-choice-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  writeFileSync(join(dir, provider.key.file), `${FAKE}\n`);
  chmodSync(join(dir, provider.key.file), 0o600);
  // The test provider as one declaration file, read into the host's one list as the plane reads its own (ADR 0061).
  const declarations = mkdtempSync(join(tmpdir(), 'assistant-choice-providers-'));
  t.after(() => rmSync(declarations, { recursive: true, force: true }));
  mkdirSync(join(declarations, provider.id));
  writeFileSync(
    join(declarations, provider.id, 'provider.json'),
    JSON.stringify({ format: 1, ...provider }),
  );
  const host = new HostProviders({ dir: declarations, dataDir: dir });
  let hostedTurns = 0;
  let hostedChecks = 0;
  let localTurns = 0;
  const protocol: Protocol = {
    check: async () => {
      hostedChecks++;
      return refuse ? { ok: false, ...KEY_REFUSED } : { ok: true };
    },
    turn: async () => {
      hostedTurns++;
      return refuse
        ? { kind: 'failed', ...KEY_REFUSED }
        : {
            kind: 'reply',
            content: 'Answered on the hosted model.',
            calls: [],
            model: 'test-model-1',
            usage: null,
          };
    },
  };
  const local: Model = {
    status: async (): Promise<WorkspaceAgentStatus> => ({
      availability: 'available',
      model: 'local-model:tag',
      served: 'this_mac',
    }),
    turn: async (): Promise<ToolTurnResult> => {
      localTurns++;
      return {
        kind: 'answered',
        content: 'Answered on this computer.',
        toolCalls: [],
        model: 'local-model:tag',
        served: 'this_mac',
      };
    },
  };
  const h = createTestControlPlane({ providers: host });
  const ids = createUuidV7Generator({ now: () => h.time.now().getTime() });
  const assistant = assistantModel({
    local,
    dataDir: dir,
    environment: {},
    providers: [provider],
    protocols: new Map([['chat_completions', protocol]]),
    chosen: () => h.controlPlane.projection.assistantModel(),
  });
  h.controlPlane.commands.attachAssistantModels(() => assistant.options());
  const unavailable = { availability: 'unavailable' as const, reason: { code: 'x', message: 'x' } };
  const coordinator = new WorkspaceRequests({
    controlPlane: h.controlPlane,
    ids,
    model: assistant,
    understanding: { understand: async () => unavailable },
    evaluation: { evaluate: async () => unavailable },
    scheduler: h.time,
  });
  t.after(async () => {
    coordinator.close();
    await h.controlPlane.close();
  });
  const envelope = (command_type: string, payload: object) =>
    ({
      schema_version: 1,
      command_id: ids.next() as CommandId,
      issued_at: h.time.now().toISOString(),
      client: { name: 'test', version: null, device_label: null },
      command_type,
      payload,
    }) as never;
  const project = h.controlPlane.commands.submit(h.commands.createProject('Garden'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const projectId = project.command.result.project_id;
  const ask = (text: string) => {
    const sent = h.controlPlane.commands.submit(
      envelope('workspace.request.submit', {
        project_id: projectId,
        workstream_id: null,
        execution_id: null,
        text,
      }),
      'http',
      { kind: 'local' },
    );
    if (sent.command?.result?.kind !== 'workspace_request_created') throw new Error('no request');
    return sent.command.result.request_id;
  };
  return {
    h,
    assistant,
    coordinator,
    envelope,
    ask,
    counts: () => ({ hosted: hostedTurns, local: localTurns }),
    /** Every request that carried the key to the provider: its turns and its status checks. */
    sent: () => hostedTurns + hostedChecks,
  };
}

test('a stored key offers its models and moves nothing until the person presses Use Model, remembered', async (t) => {
  const p = plane(t, false);
  // Offered everywhere, with this computer's model first and the stored key's model after it.
  const here = await p.h.controlPlane.commands.here(ALL);
  assert.ok(
    here.some(
      (entry) => entry.kind === 'command' && entry.command_type === 'assistant.model.choose',
    ),
  );
  const list = await p.h.controlPlane.commands.choices(ALL, 'assistant_models');
  assert.deepEqual(
    list?.rows.map((row) => [row.label, row.detail, row.actions]),
    [
      ["This Computer's Model", 'local-model:tag', ['assistant.model.choose']],
      ['test-model-1', 'On Test Hosted, Paid by You', ['assistant.model.choose']],
    ],
  );
  // Nothing chosen: the request runs on this computer, and the stored key spends nothing.
  const first = p.ask('What is in the garden?');
  await settle();
  assert.equal(p.coordinator.read(first, { kind: 'local' })?.reply?.served, 'this_mac');
  assert.deepEqual(p.counts(), { hosted: 0, local: 1 });
  // The person's one press, prepared as any entry's (a row by its index and the list's digest).
  const prepared = await p.h.controlPlane.commands.prepare({
    scope: ALL,
    command_type: 'assistant.model.choose',
    values: { model: 1 },
    read: { assistant_models: list?.digest ?? '' },
    command_id: '0192f1a0-0000-7000-8000-00000000c001' as CommandId,
    issued_at: p.h.time.now().toISOString(),
    client: { name: 'test', version: null, device_label: null },
  });
  assert.ok(prepared.ok);
  if (!prepared.ok) return;
  p.h.controlPlane.commands.submit(prepared.command, 'http', { kind: 'local' });
  await settle();
  assert.deepEqual(p.h.controlPlane.projection.assistantModel(), {
    provider_id: 'test-hosted',
    model: 'test-model-1',
  });
  const status = await p.assistant.status();
  assert.equal(status.availability === 'available' && status.provider, 'Test Hosted');
  const second = p.ask('And now?');
  await settle();
  const reply = p.coordinator.read(second, { kind: 'local' })?.reply;
  assert.equal(reply?.served, 'remote');
  assert.equal(reply?.provider, 'Test Hosted');
  assert.deepEqual(p.counts(), { hosted: 1, local: 1 });
  // Remembered: a projection restored from the exported state, and one rebuilt from the journal, choose the same.
  const restored = new Projection();
  restored.restoreState(p.h.controlPlane.projection.exportState());
  assert.deepEqual(restored.assistantModel(), {
    provider_id: 'test-hosted',
    model: 'test-model-1',
  });
  const rebuilt = new Projection();
  for (const stored of p.h.controlPlane.journal.readAll()) rebuilt.apply(stored);
  assert.deepEqual(rebuilt.assistantModel(), { provider_id: 'test-hosted', model: 'test-model-1' });
});

test('a model the list does not offer now is refused in words, and the choice stays', async (t) => {
  const p = plane(t, false);
  const sent = p.h.controlPlane.commands.submit(
    p.envelope('assistant.model.choose', {
      model: { provider_id: 'test-hosted', model: 'not-declared' },
    }),
    'http',
    { kind: 'local' },
  );
  await settle();
  const command = p.h.controlPlane.projection.command(sent.command?.command_id as CommandId);
  assert.equal(command?.status, 'failed');
  assert.equal(p.h.controlPlane.projection.assistantModel(), null);
});

test('a refused key is said in words, never sent again, and this computer is one press away', async (t) => {
  const p = plane(t, true);
  p.h.controlPlane.commands.submit(
    p.envelope('assistant.model.choose', {
      model: { provider_id: 'test-hosted', model: 'test-model-1' },
    }),
    'http',
    { kind: 'local' },
  );
  await settle();
  const id = p.ask('What is in the garden?');
  await settle();
  let request = p.coordinator.read(id, { kind: 'local' });
  assert.equal(
    request?.status,
    'waiting_for_human',
    'no silent fall back and no failure left alone',
  );
  const failed = request?.steps.find((step) => step.tool_name === 'model');
  assert.match(failed?.summary ?? '', /refused the stored key/);
  const offer = request?.steps.find((step) => step.tool_name === 'use_this_computer');
  assert.equal(offer?.status, 'waiting');
  assert.equal(offer?.proposal?.command_type, 'assistant.model.choose');
  // The host's own offer (not the model's step), of this computer's model only, one press with no review (AUDIT2),
  // with the host's own words for its card (XR, 2026-11-13).
  assert.equal(offer?.proposal?.confirm, 'none');
  assert.equal(offer?.proposal?.confirm_words, "Use This Computer's Model");
  assert.match(offer?.proposal?.what_happens ?? '', /nothing it reads leaves this computer/);
  const held = p.h.controlPlane.projection.workspaceProposal(id, offer?.step_id as never);
  assert.deepEqual(held?.command.payload, { model: null });
  assert.equal(p.counts().hosted, 1);
  // The refused key is not sent again: a status read and a new request spend nothing more.
  const sent = p.sent();
  const status = await p.assistant.status();
  assert.equal(status.availability === 'unavailable' && status.reason.code, 'key_refused');
  const again = p.ask('And now?');
  await settle();
  assert.equal(p.coordinator.read(again, { kind: 'local' })?.status, 'waiting_for_human');
  assert.equal(p.sent(), sent, 'neither the status read nor the new request sent the refused key');
  // The one press: the request goes on, on this computer's model.
  p.h.controlPlane.commands.submit(
    p.envelope('workspace.request.resume', {
      request_id: id,
      text: null,
      confirm: { step_id: offer?.step_id, review: offer?.proposal?.review },
    }),
    'http',
    { kind: 'local' },
  );
  await settle();
  assert.equal(p.h.controlPlane.projection.assistantModel(), null);
  request = p.coordinator.read(id, { kind: 'local' });
  assert.equal(request?.status, 'responded');
  assert.equal(request?.reply?.served, 'this_mac');
  assert.deepEqual(p.counts(), { hosted: 1, local: 1 });
});

test('the last press decides: a hosted choice still reading its options never lands after a later one', async (t) => {
  const p = plane(t, false);
  const hosted = p.h.controlPlane.commands.submit(
    p.envelope('assistant.model.choose', {
      model: { provider_id: 'test-hosted', model: 'test-model-1' },
    }),
    'http',
    { kind: 'local' },
  );
  // Pressed before the hosted choice has read its options: this computer's model, recorded at once.
  p.h.controlPlane.commands.submit(p.envelope('assistant.model.choose', { model: null }), 'http', {
    kind: 'local',
  });
  await settle();
  assert.equal(p.h.controlPlane.projection.assistantModel(), null);
  assert.equal(
    p.h.controlPlane.projection.command(hosted.command?.command_id as CommandId)?.status,
    'failed',
  );
});

test("a press on a hosted row waits on the host's own card: its button, the model and provider, the warning", async (t) => {
  const p = plane(t, false);
  const list = await p.h.controlPlane.commands.choices(ALL, 'assistant_models');
  const sent = p.h.controlPlane.commands.submit(
    p.envelope('workspace.request.submit', {
      project_id: null,
      workstream_id: null,
      execution_id: null,
      text: 'Use Model',
      act: {
        command_type: 'assistant.model.choose',
        values: { model: 1 },
        lists: { assistant_models: list?.digest },
      },
    }),
    'http',
    { kind: 'local' },
  );
  const id =
    sent.command?.result?.kind === 'workspace_request_created'
      ? sent.command.result.request_id
      : null;
  assert.ok(id);
  await settle();
  const step = p.coordinator
    .read(id as string, { kind: 'local' })
    ?.steps.find((each) => each.proposal && each.status === 'waiting');
  assert.equal(step?.proposal?.confirm, 'review');
  assert.equal(step?.proposal?.confirm_words, 'Use This Model');
  // The model named in the line itself, so no unlabelled box stands beside it (FIELD's reader on 1111-b123).
  assert.deepEqual(step?.proposal?.facts, []);
  // The money first, in plain words, before the press: on whose account, that the price is not known, what is sent,
  // and what stays free (the lead's ruling on 1111-b123), short (the reader on b130).
  assert.equal(
    step?.proposal?.what_happens,
    'From now on the assistant answers on test-model-1, and Test Hosted charges you money for each request.',
  );
  assert.deepEqual(step?.proposal?.warnings, [
    {
      code: 'costs_money',
      line: 'Each request is charged to your own Test Hosted account, through the access key you added',
    },
    {
      code: 'price_unknown',
      line: 'Halcyonic does not know the price, and cannot show a total yet',
    },
    {
      code: 'sent_to_provider',
      line: 'Your requests, and what the assistant reads to answer them, are sent to Test Hosted',
    },
    { code: 'free_here', line: "This Computer's Model is free" },
  ]);
});

test("the cost words fit the review's and the list's own contracts for the longest name a declaration may give", () => {
  const longest = 'W'.repeat(80);
  const longestModel = 'm'.repeat(128);
  const review = compileValidator(ActionReview);
  const words: ActionReview = {
    command_type: 'assistant.model.choose',
    digest: 'a'.repeat(64),
    confirm_words: 'Use This Model',
    ...hostedReview(longest, longestModel),
    items: [],
    items_total: 0,
    warnings: HOSTED_COST.warnings(longest),
    said: [],
  };
  assert.ok(review(words).ok);
  // Too long to name in the line: "this model" there, and the model as the review's one fact.
  assert.deepEqual(words.facts, [{ kind: 'words', value: longestModel, theirs: true }]);
  assert.match(words.what_happens, /answers on this model/);
  assert.deepEqual(hostedReview('Meta', 'muse-wonder-1.0').facts, []);
  assert.ok(review({ ...words, what_happens: HOSTED_COST.localWhatHappens, warnings: [] }).ok);
  assert.ok([...HOSTED_COST.detail(longest)].length <= 400);
  // Unknown stays unknown: no price, estimate or reassurance about size is ever said.
  const said = [
    HOSTED_COST.whatHappens('P', 'm'),
    ...HOSTED_COST.warnings('P').map((each) => each.line),
  ].join(' ');
  assert.doesNotMatch(said, /\$|\d|small|cheap|estimate|about/i);
  assert.match(said, /charges you money/);
});

test("a review holding more than a proposal may carry is fitted to the proposal's own contract", () => {
  const review: ActionReview = {
    command_type: 'workstream.add_to_project',
    digest: 'a'.repeat(64),
    confirm_words: 'Add Twelve Files To The Project Folder Now Please',
    what_happens: 'Twelve files are added.',
    facts: Array.from({ length: 6 }, (_, at) => ({
      kind: 'folder' as const,
      value: `/${'deep/'.repeat(100)}${at}`,
      theirs: true,
    })),
    items: [],
    items_total: 0,
    warnings: Array.from({ length: 11 }, (_, at) => ({
      code: 'moves_a_key',
      line: `providers/p${at}/provider.json changes where a key goes`,
    })),
    said: [],
  };
  const words = reviewWords(review);
  assert.equal(words.facts?.length, 4);
  assert.ok(words.facts?.every((fact) => [...fact.value].length <= 400));
  assert.equal(words.warnings?.length, 8);
  assert.deepEqual(words.warnings?.at(-1), {
    code: 'more_warnings',
    line: 'And 4 more to read on this computer',
  });
  assert.ok([...(words.confirm_words ?? '')].length <= 40);
  const check = compileValidator(WorkspaceRequestProposal);
  assert.ok(
    check({
      command_type: 'workstream.add_to_project',
      confirm: 'review',
      what_happens: 'x',
      review: 'a'.repeat(64),
      said: [],
      ...words,
    }).ok,
  );
});
