import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { crc32, deflateSync } from 'node:zlib';
import type { CommandId, ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import type { InstructionRequest, StartExecutionRequest } from '@halcyonic/runtime-core';
import { createHostLocations } from '../locations.ts';
import { createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { listPictures, pictureKind, readPictures, wholePicture } from './pictures.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-pictures-')));
after(() => rmSync(base, { recursive: true, force: true }));

/** A 2 by 2 PNG of one colour, whole: each chunk's CRC, and image data of the size its header names. */
function solidPng([r, g, b]: readonly [number, number, number]): Buffer {
  const chunk = (type: string, data: Buffer) => {
    const body = Buffer.concat([Buffer.from(type, 'latin1'), data]);
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(2, 0);
  header.writeUInt32BE(2, 4);
  header.set([8, 2, 0, 0, 0], 8);
  const row = Buffer.from([0, r, g, b, r, g, b]);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(Buffer.concat([row, row]))),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}
const PNG = solidPng([200, 40, 40]);
/** A JPEG by its structure: a frame, a scan with data, and its end marker. */
const JPEG = Buffer.from([
  0xff, 0xd8, 0xff, 0xc0, 0x00, 0x11, 0x08, 0x00, 0x02, 0x00, 0x02, 0x03, 0x01, 0x11, 0x00, 0x02,
  0x11, 0x01, 0x03, 0x11, 0x01, 0xff, 0xda, 0x00, 0x0c, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11,
  0x00, 0x3f, 0x00, 0x00, 0xff, 0xd9,
]);
/** A WebP by its structure: its RIFF size, then a lossless chunk. */
const WEBP = Buffer.concat([
  Buffer.from('RIFF', 'latin1'),
  Buffer.from([0x12, 0, 0, 0]),
  Buffer.from('WEBPVP8L', 'latin1'),
  Buffer.from([0x06, 0, 0, 0, 0x2f, 0x01, 0x40, 0x00, 0x00, 0x00]),
]);
const sha = (bytes: Uint8Array) => createHash('sha256').update(bytes).digest('hex');

/** Until `done`, letting the host's real file reads finish between virtual-time steps. */
async function settle(time: { runUntilIdle(): Promise<void> }, done: () => boolean) {
  for (let i = 0; i < 200 && !done(); i += 1) {
    await time.runUntilIdle();
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  await time.runUntilIdle();
}

/** A project's folder with pictures, a text file named as one, a link out, and folders kept out. */
function folder(name: string) {
  const root = join(base, name);
  const app = join(root, 'app');
  const own = join(app, 'halcyonic-data');
  for (const dir of [
    join(app, 'art', 'deep'),
    join(app, 'node_modules', 'pkg'),
    join(app, '.cache'),
    own,
    join(root, 'outside'),
  ])
    mkdirSync(dir, { recursive: true });
  writeFileSync(join(app, 'art', 'tree.png'), PNG);
  writeFileSync(join(app, 'art', 'deep', 'sky.jpg'), JPEG);
  writeFileSync(join(app, 'cover.webp'), WEBP);
  writeFileSync(join(app, 'fake.png'), 'not a picture\n');
  writeFileSync(join(app, 'node_modules', 'pkg', 'icon.png'), PNG);
  writeFileSync(join(app, '.cache', 'thumb.png'), PNG);
  writeFileSync(join(own, 'shot.png'), PNG);
  writeFileSync(join(app, 'id_rsa.png'), PNG);
  writeFileSync(join(root, 'outside', 'private.png'), PNG);
  symlinkSync(join(root, 'outside', 'private.png'), join(app, 'linked.png'));
  symlinkSync(join(root, 'outside'), join(app, 'linked-folder'));
  return { root, app, own };
}

describe("an instruction's pictures (A4)", () => {
  test('a picture is what its first bytes say, never its name', () => {
    assert.equal(pictureKind(PNG), 'image/png');
    assert.equal(pictureKind(JPEG), 'image/jpeg');
    assert.equal(pictureKind(WEBP), 'image/webp');
    assert.equal(pictureKind(Buffer.from('RIFF\x10\x00\x00\x00WAVE', 'latin1')), null);
    assert.equal(pictureKind(Buffer.from('<svg/>')), null);
    assert.equal(pictureKind(new Uint8Array()), null);
  });

  test('a picture is whole only where its structure says so, so no runtime is handed one it cannot decode', () => {
    for (const whole of [PNG, JPEG, WEBP]) assert.equal(wholePicture(whole), true);
    const broken = Buffer.from(PNG);
    broken.writeUInt8((broken.readUInt8(broken.length - 20) ^ 0xff) & 0xff, broken.length - 20);
    for (const [what, bytes] of [
      ['a PNG cut short', PNG.subarray(0, PNG.length - 12)],
      ['a PNG with a bad CRC', broken],
      ['a PNG with only its signature', PNG.subarray(0, 8)],
      ['a PNG with bytes after its end', Buffer.concat([PNG, Buffer.from([0])])],
      ['a JPEG without its end marker', JPEG.subarray(0, JPEG.length - 2)],
      ['a JPEG with no scan', Buffer.from([0xff, 0xd8, 0xff, 0xd9])],
      ['a WebP of another size', Buffer.concat([WEBP, Buffer.from([0])])],
      ['text', Buffer.from('hello')],
    ] as const)
      assert.equal(wholePicture(bytes), false, what);
  });

  test("the host reads each picture once, from inside the project's folder, with its digest", async () => {
    const { app, own } = folder('read');
    const read = await readPictures(
      app,
      [
        { path: 'art/tree.png', kind: 'image' },
        { path: 'art/deep/sky.jpg', kind: 'image' },
      ],
      [own],
    );
    assert.ok(read.ok);
    assert.deepEqual(
      read.pictures.map((p) => [p.path, p.media_type, Buffer.from(p.bytes).equals(PNG), p.sha256]),
      [
        ['art/tree.png', 'image/png', true, sha(PNG)],
        ['art/deep/sky.jpg', 'image/jpeg', false, sha(JPEG)],
      ],
    );
  });

  test('anything else refuses them all, and says which picture and why', async () => {
    const { app, own } = folder('refused');
    const refusal = async (path: string) => {
      const read = await readPictures(
        app,
        [
          { path: 'art/tree.png', kind: 'image' },
          { path, kind: 'image' },
        ],
        [own],
      );
      assert.ok(!read.ok, path);
      return [read.code, read.message];
    };
    assert.deepEqual(await refusal('fake.png'), [
      'picture_not_a_picture',
      "The picture fake.png isn't a whole PNG, JPEG or WebP picture, so the instruction was not sent.",
    ]);
    writeFileSync(join(app, 'cut.png'), PNG.subarray(0, PNG.length - 12));
    assert.equal((await refusal('cut.png'))[0], 'picture_not_a_picture');
    assert.deepEqual(await refusal('missing.png'), [
      'picture_not_found',
      "The picture missing.png isn't a file in the project's folder, so the instruction was not sent.",
    ]);
    // Never through a link, to a file or a folder, and never a folder of the host's own.
    assert.equal((await refusal('linked.png'))[0], 'picture_not_found');
    assert.equal((await refusal('linked-folder/private.png'))[0], 'picture_not_found');
    assert.equal((await refusal('halcyonic-data/shot.png'))[0], 'picture_not_found');
    assert.equal((await refusal('../outside/private.png'))[0], 'picture_not_found');
    assert.equal((await refusal('art'))[0], 'picture_not_found');
    assert.equal((await refusal('id_rsa.png'))[0], 'picture_withheld');
    writeFileSync(join(app, 'huge.png'), Buffer.concat([PNG, Buffer.alloc(5_242_880)]));
    assert.deepEqual(await refusal('huge.png'), [
      'picture_too_large',
      'The picture huge.png is larger than 5 MB, so the instruction was not sent.',
    ]);
  });

  test("the project's list names its pictures, sorted, and nothing it would not read", async () => {
    const { app, own } = folder('list');
    const { pictures, total } = await listPictures(app, [own]);
    assert.equal(total, 3);
    assert.deepEqual(
      pictures.map((p) => [p.path, p.media_type, p.size_bytes]),
      [
        ['art/deep/sky.jpg', 'image/jpeg', JPEG.length],
        ['art/tree.png', 'image/png', PNG.length],
        ['cover.webp', 'image/webp', WEBP.length],
      ],
    );
  });
});

describe('pictures through the control plane (A4)', () => {
  function plane(name: string, takesPictures: boolean) {
    const { root, app } = folder(name);
    const starts: StartExecutionRequest[] = [];
    const sends: InstructionRequest[] = [];
    const harness = createTestControlPlane({
      locations: createHostLocations([root]),
      adapters: (time) => {
        const mock = new MockRuntimeAdapter({
          scenarios: SCENARIOS,
          clock: time,
          scheduler: time,
          takesPictures,
        });
        const start = mock.startExecution.bind(mock);
        const send = mock.sendInstruction.bind(mock);
        mock.startExecution = (request: StartExecutionRequest) => {
          starts.push(request);
          return start(request);
        };
        mock.sendInstruction = (request: InstructionRequest) => {
          sends.push(request);
          return send(request);
        };
        return [mock];
      },
    });
    const { controlPlane, commands, time } = harness;
    const project = controlPlane.commands.submit(
      commands.createProject('Garden', { kind: 'existing_folder', root, folder_name: 'app' }),
      'internal',
    );
    const projectId = (
      project.command?.result?.kind === 'project_created' ? project.command.result.project_id : ''
    ) as ProjectId;
    const task = controlPlane.commands.submit(
      commands.createWorkstream(projectId, {
        title: 'Paint',
        objective: 'Paint the trees.',
        instruction: '',
        scenario: '',
      } as never),
      'internal',
    );
    const workstreamId = (
      task.command?.result?.kind === 'workstream_created' ? task.command.result.workstream_id : ''
    ) as WorkstreamId;
    const start = (attachments: { path: string; kind: 'image' }[]) => {
      const command = commands.startExecution(workstreamId, {
        title: 'Paint',
        objective: 'Paint the trees.',
        instruction: 'Paint the trees like this picture.',
        scenario: MockRuntimeAdapter.DEFAULT_SCENARIO,
      } as never);
      return {
        command,
        ack: controlPlane.commands.submit(
          { ...command, payload: { ...command.payload, attachments } } as typeof command,
          'internal',
        ),
      };
    };
    const events = (type: string) =>
      [...controlPlane.journal.readAll()]
        .map((stored) => stored.event)
        .filter((event) => event.event_type === type);
    return { controlPlane, commands, time, app, starts, sends, start, events };
  }

  test("a start hands the runtime the bytes the host read, and the journal keeps each picture's digest", async (t) => {
    const { controlPlane, commands, time, starts, sends, start, events } = plane(
      'start-sent',
      true,
    );
    t.after(() => controlPlane.close());
    const { command } = start([{ path: 'art/tree.png', kind: 'image' }]);
    await settle(time, () => starts.length > 0);
    assert.equal(starts.length, 1);
    assert.deepEqual(
      starts[0]?.pictures?.map((p) => [p.path, p.media_type, Buffer.from(p.bytes).equals(PNG)]),
      [['art/tree.png', 'image/png', true]],
    );
    const [read] = events('execution.instruction.pictures_read');
    assert.deepEqual(read?.payload, {
      command_id: command.command_id,
      pictures: [
        { path: 'art/tree.png', media_type: 'image/png', size_bytes: PNG.length, sha256: sha(PNG) },
      ],
    });
    assert.equal(
      JSON.stringify([...controlPlane.journal.readAll()]).includes(PNG.toString('base64')),
      false,
    );

    // An instruction to the resting execution carries its pictures the same way.
    const executionId = read?.execution_id as ExecutionId;
    await settle(
      time,
      () => controlPlane.projection.execution(executionId)?.status === 'completed',
    );
    const instruct = commands.instruct(executionId, 'Now the sky.');
    controlPlane.commands.submit(
      {
        ...instruct,
        payload: {
          ...instruct.payload,
          attachments: [{ path: 'art/deep/sky.jpg', kind: 'image' }],
        },
      },
      'internal',
    );
    await settle(time, () => sends.length > 0);
    assert.deepEqual(
      sends.map((s) => [s.text, s.pictures?.map((p) => p.media_type)]),
      [['Now the sky.', ['image/jpeg']]],
    );
    assert.equal(events('execution.instruction.pictures_read').length, 2);
  });

  test('a picture that cannot be read starts nothing, and the command fails having done nothing', async (t) => {
    const { controlPlane, time, starts, start, events } = plane('start-refused', true);
    t.after(() => controlPlane.close());
    const { command } = start([
      { path: 'art/tree.png', kind: 'image' },
      { path: 'fake.png', kind: 'image' },
    ]);
    await settle(time, () => events('command.failed').length > 0);
    assert.equal(starts.length, 0);
    assert.equal(events('execution.created').length, 0);
    assert.equal(events('execution.instruction.pictures_read').length, 0);
    const failed = events('command.failed').find(
      (event) => (event.payload as { command_id: CommandId }).command_id === command.command_id,
    );
    assert.deepEqual((failed?.payload as { failure: unknown } | undefined)?.failure, {
      code: 'picture_not_a_picture',
      message:
        "The picture fake.png isn't a whole PNG, JPEG or WebP picture, so the instruction was not sent.",
      effect: 'none',
    });
  });

  test('a runtime that does not take pictures is refused at admission, never sent the words alone', async (t) => {
    const { controlPlane, time, starts, start } = plane('start-admission', false);
    t.after(() => controlPlane.close());
    const { ack } = start([{ path: 'art/tree.png', kind: 'image' }]);
    await time.runUntilIdle();
    assert.equal(ack.disposition, 'rejected');
    assert.equal(ack.command?.rejection?.code, 'capability_unsupported');
    assert.match(
      ack.command?.rejection?.message ?? '',
      /does not take pictures with an instruction/,
    );
    assert.equal(starts.length, 0);
  });
});
