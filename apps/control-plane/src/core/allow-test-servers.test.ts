import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { type TestContext, test } from 'node:test';
import type { CommandEnvelope, CommandId, ProjectId } from '@halcyonic/contracts';
import {
  ALLOW_TEST_SERVERS,
  ASSISTANT_CLIENT,
  COMMAND_FIELDS,
  NEVER_PROPOSED,
} from '@halcyonic/domain';
import { confinedProfile, OWN_LOOPBACK_ALWAYS_DENIED } from '@halcyonic/seatbelt';
import { createHostLocations } from '../locations.ts';
import { commandSha256, managersOn } from '../own-check/own-check.ts';
import { createTestControlPlane, TEST_CLIENT } from '../testing/harness.ts';
import { buildReview } from './reviews.ts';

/** A connected folder with one script, and the plane that sees it as a project. */
function connected(t: TestContext) {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-test-servers-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, 'site'));
  writeFileSync(
    join(root, 'site', 'package.json'),
    JSON.stringify({ name: 'site', scripts: { test: 'node -e ""' } }),
  );
  const { controlPlane, commands } = createTestControlPlane({
    locations: createHostLocations([root]),
    // The listeners read as none, so a run of the check here opens no port.
    ownChecks: {
      dataDir: join(root, 'data'),
      home: '/Users/someone',
      node: process.execPath,
      managers: managersOn(process.env.PATH),
      listening: async () => null,
    },
  });
  t.after(() => controlPlane.close());
  const created = controlPlane.commands.submit(
    commands.createProject('Site', { kind: 'existing_folder', root, folder_name: 'site' }),
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  let n = 0;
  const choose = (client: CommandEnvelope['client'], openPorts?: boolean): CommandEnvelope => {
    n += 1;
    return {
      schema_version: 1,
      command_id: `00000000-0000-4000-9000-2${String(n).padStart(11, '0')}` as CommandId,
      issued_at: '2026-11-16T16:30:00.000Z',
      client,
      command_type: 'project.own_check.choose',
      payload: {
        project_id: projectId,
        scripts: [{ name: 'test', command_sha256: commandSha256('node -e ""') }],
        ...(openPorts === undefined ? {} : { open_ports: openPorts }),
      },
    } as CommandEnvelope;
  };
  return { controlPlane, choose };
}

const ASSISTANT = { name: ASSISTANT_CLIENT, version: null, device_label: null };

test("test servers are the person's own yes: refused from the assistant and from the host itself, admitted from the person's own client (AUDIT3 A1, CORE 16:22Z)", async (t) => {
  /** One choice on a fresh plane, so an earlier accepted choice's run never makes the project busy for the next. */
  const once = (
    client: CommandEnvelope['client'],
    via: 'internal' | 'http' | 'websocket',
    openPorts?: boolean,
  ) => {
    const { controlPlane, choose } = connected(t);
    return controlPlane.commands.submit(choose(client, openPorts), via);
  };
  const refusal = /Allow Test Servers is chosen by you on your own screen/;
  // From the assistant, with the yes: refused, in words that say whose choice it is.
  const proposed = once(ASSISTANT, 'internal', true);
  assert.equal(proposed.disposition, 'rejected');
  assert.match(proposed.command?.rejection?.message ?? '', refusal);
  // Submitted by the host itself, whatever name its client declares (a confirmed proposal, a replay): refused (AUDIT3 A1).
  for (const client of [TEST_CLIENT, { name: 'halcyonic-xr', version: null, device_label: null }]) {
    const internal = once(client, 'internal', true);
    assert.equal(internal.disposition, 'rejected', client.name);
    assert.match(internal.command?.rejection?.message ?? '', refusal, client.name);
  }
  // From the assistant without it, as today: admitted.
  assert.equal(once(ASSISTANT, 'internal', false).disposition, 'accepted');
  // From the person's own client, over either transport a client uses, with the yes: admitted.
  for (const via of ['websocket', 'http'] as const) {
    const own = once(TEST_CLIENT, via, true);
    assert.equal(own.disposition, 'accepted', `${via}: ${JSON.stringify(own.command?.rejection)}`);
  }
  // The model is never offered the field, and the assistant's list names it as never proposed.
  assert.deepEqual(NEVER_PROPOSED['project.own_check.choose'], ['open_ports']);
  assert.equal(COMMAND_FIELDS['project.own_check.choose'].open_ports?.from, 'person');
});

test('the review card holds the yes in its digest and says both gaps beside it; without the yes, nothing is said', async (t) => {
  const { controlPlane, choose } = connected(t);
  const withYes = await buildReview(controlPlane, choose(TEST_CLIENT, true));
  const withoutYes = await buildReview(controlPlane, choose(TEST_CLIENT));
  assert.ok(withYes.ok && withoutYes.ok);
  assert.deepEqual(withYes.review.warnings, [
    { code: 'test_servers', line: ALLOW_TEST_SERVERS.line },
  ]);
  assert.deepEqual(withoutYes.review.warnings, []);
  assert.notEqual(
    withYes.review.digest,
    withoutYes.review.digest,
    'the yes is part of what is pressed',
  );
});

test("the line says exactly the rule's two gaps: pinned to the check's profile (AUDIT2, 16:24Z)", (t) => {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-test-servers-pin-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  for (const folder of ['project', 'home', 'temp', 'runtime']) mkdirSync(join(root, folder));
  const profile = confinedProfile({
    project: join(root, 'project'),
    readable: [],
    home: join(root, 'home'),
    temp: join(root, 'temp'),
    runtimeFolder: join(root, 'runtime'),
    toolFolders: [],
    loopbackPorts: [],
    ownLoopback: { denied: [] },
  });
  // Gap 1: the listen is "localhost", which Seatbelt does not hold to loopback, so devices on the network can reach it.
  assert.ok(profile.includes('(allow network-bind network-inbound (local tcp "localhost:*"))'));
  assert.match(ALLOW_TEST_SERVERS.line, /devices on your network can reach them/);
  // Gap 2: connects go to any loopback port but those listening at the start and the fixed set, so only a service that
  // starts meanwhile is reachable; the line claims no more.
  assert.ok(profile.includes('(allow network-outbound (remote tcp "localhost:*"))'));
  for (const port of OWN_LOOPBACK_ALWAYS_DENIED)
    assert.ok(profile.includes(`(deny network-outbound (remote ip "localhost:${port}"))`));
  assert.match(ALLOW_TEST_SERVERS.line, /any service that starts on this computer meanwhile/);
  assert.doesNotMatch(ALLOW_TEST_SERVERS.line, /only this computer|other services/);
  assert.equal(ALLOW_TEST_SERVERS.label, 'Allow Test Servers');
});
