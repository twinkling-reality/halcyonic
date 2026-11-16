import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { MEDIA_FOLDER_BYTES } from '@halcyonic/contracts';
import { box, clip, ftyp, png, StandInTool } from '../testing/media-stand-in.ts';
import { MEDIA_NAME, MediaPlacer, placeFile } from './media.ts';
import { checkMedia, mediaKind } from './media-kind.ts';
import { KEPT_FILES, KEPT_MS, MediaStore } from './media-store.ts';
import type { MediaTool } from './media-tool.ts';

const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-media-')));
after(() => rmSync(root, { recursive: true, force: true }));
let made = 0;
const fresh = (name: string) => {
  made += 1;
  const path = join(root, `${name}-${made}`);
  mkdirSync(path, { recursive: true });
  return path;
};

async function* chunks(...parts: Buffer[]): AsyncIterable<Uint8Array> {
  for (const part of parts) yield part;
}

test('a photo or video is known by its bytes and a walk of its boxes to the end, never its name (ADR 0066)', async () => {
  const folder = fresh('kind');
  const write = (name: string, bytes: Buffer) => {
    const path = join(folder, name);
    writeFileSync(path, bytes);
    return path;
  };
  assert.equal(mediaKind(clip()), 'video/quicktime');
  assert.equal(mediaKind(clip('isom')), 'video/mp4');
  assert.equal(
    (await checkMedia(write('a.jpg', clip())))?.type,
    'video/quicktime',
    'its name says nothing',
  );
  assert.equal((await checkMedia(write('b.mp4', clip('mp42'))))?.type, 'video/mp4');
  assert.equal(
    (
      await checkMedia(
        write('c.heic', Buffer.concat([ftyp('heic'), box('meta', Buffer.alloc(16))])),
      )
    )?.type,
    'image/heic',
  );
  assert.equal((await checkMedia(write('d.png', png([1, 2, 3]))))?.type, 'image/png');
  // Cut short, padded, with no moov, or with a box past the end: none of them.
  assert.equal(await checkMedia(write('cut.mov', clip().subarray(0, 70))), null);
  assert.equal(await checkMedia(write('pad.mov', Buffer.concat([clip(), Buffer.alloc(3)]))), null);
  assert.equal(
    await checkMedia(write('nomoov.mov', Buffer.concat([ftyp('qt  '), box('mdat')]))),
    null,
  );
  assert.equal(
    await checkMedia(write('heicnometa.heic', Buffer.concat([ftyp('heic'), box('mdat')]))),
    null,
  );
  assert.equal(await checkMedia(write('brand.mov', clip('abcd'))), null);
  assert.equal(
    await checkMedia(write('text.mov', Buffer.from('not a video at all, only words'))),
    null,
  );
});

test('the store keeps a file private by its digest, refuses one too large or not whole, one at a time, four at most, two hours (ADR 0066)', async () => {
  const folder = join(fresh('store'), 'media');
  let now = 1_000_000;
  const store = new MediaStore(folder, () => now);
  const first = await store.receive(
    chunks(clip().subarray(0, 20), clip().subarray(20)),
    clip().length,
  );
  assert.ok(first.ok);
  assert.equal(first.media_type, 'video/quicktime');
  assert.equal((statSync(folder).mode & 0o777).toString(8), '700');
  assert.equal((statSync(join(folder, first.id)).mode & 0o777).toString(8), '600');
  assert.equal(await store.path(first.id), join(folder, first.id));
  assert.equal(await store.path('../x'), null, 'never a path');

  // Too large by its declared length: refused before a byte is read.
  let read = false;
  const never = (async function* () {
    read = true;
    yield Buffer.alloc(1);
  })();
  assert.deepEqual(await store.receive(never, 80_000_001), { ok: false, reason: 'too_large' });
  assert.equal(read, false);
  // Not a whole medium: refused and nothing left behind.
  assert.deepEqual(await store.receive(chunks(Buffer.from('words')), 5), {
    ok: false,
    reason: 'not_a_media',
  });
  assert.deepEqual(readdirSync(folder).sort(), [first.id]);

  // One at a time: a second while the first still arrives is refused.
  let release: () => void = () => undefined;
  const slow = (async function* () {
    yield clip().subarray(0, 10);
    await new Promise<void>((resolve) => {
      release = resolve;
    });
    yield clip().subarray(10);
  })();
  const arriving = store.receive(slow, clip().length);
  await new Promise((resolve) => setTimeout(resolve, 20));
  assert.deepEqual(await store.receive(chunks(clip('isom')), clip('isom').length), {
    ok: false,
    reason: 'busy',
  });
  release();
  assert.ok((await arriving).ok, 'the same file again is the same kept file');

  for (let extra = 1; readdirSync(folder).length < KEPT_FILES; extra += 1)
    assert.ok((await store.receive(chunks(clip('qt  ', extra)), clip('qt  ', extra).length)).ok);
  assert.deepEqual(await store.receive(chunks(clip('isom', 99)), clip('isom', 99).length), {
    ok: false,
    reason: 'full',
  });

  // Past two hours, gone.
  now += KEPT_MS + 1_000;
  assert.equal(await store.path(first.id), null);
  await store.sweep();
  assert.deepEqual(readdirSync(folder), []);
});

test('a medium is written only under the host own names, never through a link or over a file (ADR 0066)', async () => {
  const project = fresh('place');
  const source = join(fresh('source'), 'copy.mov');
  writeFileSync(source, clip());
  const at = 'media/video-0123456789ab.mov';
  assert.equal(await placeFile(project, at, source, []), 'placed');
  assert.deepEqual(readFileSync(join(project, at)), clip());
  // The same name again: refused, never overwritten.
  writeFileSync(source, clip('isom'));
  assert.equal(await placeFile(project, at, source, []), 'media_present');
  assert.deepEqual(readFileSync(join(project, at)), clip());
  // Not one of the host's names, or a name that may hold secrets.
  assert.equal(await placeFile(project, 'src/video.mov', source, []), 'folder_refused');
  assert.equal(await placeFile(project, 'media/../x.mov', source, []), 'folder_refused');
  // media/ as a link to a folder outside: nothing lands there.
  const other = fresh('linked');
  const outside = fresh('outside');
  symlinkSync(outside, join(other, 'media'));
  assert.equal(await placeFile(other, at, source, []), 'folder_refused');
  assert.deepEqual(readdirSync(outside), []);
  // A stills folder that is a link, below a real media/: nothing lands there either.
  const third = fresh('stills-link');
  mkdirSync(join(third, 'media'));
  symlinkSync(outside, join(third, 'media', 'video-0123456789ab-stills'));
  assert.equal(
    await placeFile(third, 'media/video-0123456789ab-stills/1.jpg', source, []),
    'folder_refused',
  );
  assert.deepEqual(readdirSync(outside), []);
  // A folder of the host's own is never written in.
  assert.equal(
    await placeFile(project, 'media/video-111111111111.mov', source, [project]),
    'folder_refused',
  );
  assert.ok(MEDIA_NAME.test('media/photo-0123456789ab-stills/1.jpg'));
  assert.ok(!MEDIA_NAME.test('media/photo-0123456789ab-stills/5.jpg'));
});

async function kept(store: MediaStore, bytes: Buffer): Promise<string> {
  const received = await store.receive(chunks(bytes), bytes.length);
  assert.ok(received.ok);
  return received.id;
}

test('an added video is placed, then its four stills and a play copy, each recorded, the upload removed (ADR 0066)', async () => {
  const data = fresh('data');
  const project = fresh('project');
  const store = new MediaStore(join(data, 'media'));
  const tool = new StandInTool(join(data, 'media-work'));
  const placer = new MediaPlacer({
    store,
    tool,
    notes: join(data, 'media-placing'),
    protectedFolders: [data],
  });
  const id = await kept(store, clip());
  const added: unknown[] = [];
  const outcome = await placer.add({
    projectId: 'p1',
    commandId: 'c1',
    folder: project,
    id,
    bytes: 0,
    added: (file) => added.push(file),
  });
  assert.ok(outcome.ok);
  const { prepared } = outcome;
  assert.match(prepared.path, /^media\/video-[0-9a-f]{12}\.mov$/);
  assert.equal(added.length, 1, 'added once, before anything is made');
  assert.deepEqual(tool.calls, [
    'strip',
    'still 656',
    'still 2297',
    'still 3939',
    'still 5580',
    'play',
  ]);
  assert.equal(prepared.stills.length, 4);
  assert.deepEqual(
    prepared.stills.map((still) => still.path),
    [1, 2, 3, 4].map((i) => `${prepared.path.slice(0, -4)}-stills/${i}.jpg`),
  );
  assert.equal(prepared.play?.path, `${prepared.path.slice(0, -4)}-play.mp4`);
  assert.equal(prepared.stills_not_made, null);
  assert.equal(prepared.play_not_made, null);
  assert.equal(prepared.duration_ms, 6_565);
  for (const file of [
    prepared.path,
    prepared.play?.path ?? '',
    ...prepared.stills.map((s) => s.path),
  ])
    assert.ok(existsSync(join(project, file)), file);
  assert.equal(await store.path(id), null, 'the upload is gone once placed');
  assert.deepEqual(readdirSync(join(data, 'media-work')), [], 'the tool folder is gone');
  assert.ok(
    !existsSync(join(data, 'media-placing')) ||
      readdirSync(join(data, 'media-placing')).length === 0,
  );

  // The same file again is already there.
  const again = await kept(store, clip());
  const second = await placer.add({
    projectId: 'p1',
    commandId: 'c2',
    folder: project,
    id: again,
    bytes: 0,
    added: () => undefined,
  });
  assert.deepEqual(second, { ok: false, refusal: 'media_present' });
});

test('a photo gets one still of its own, never itself; no tool, a changed tool or a full folder adds nothing (ADR 0066)', async () => {
  const data = fresh('data');
  const project = fresh('project');
  const store = new MediaStore(join(data, 'media'));
  const placer = (tool: MediaTool) =>
    new MediaPlacer({ store, tool, notes: join(data, 'media-placing'), protectedFolders: [data] });
  const photo = await kept(store, png([9, 9, 9]));
  const outcome = await placer(new StandInTool(join(data, 'work'))).add({
    projectId: 'p',
    commandId: 'c',
    folder: project,
    id: photo,
    bytes: 0,
    added: () => undefined,
  });
  assert.ok(outcome.ok);
  assert.match(outcome.prepared.path, /^media\/photo-[0-9a-f]{12}\.jpg$/);
  assert.equal(outcome.prepared.stills.length, 1);
  assert.notEqual(outcome.prepared.stills[0]?.path, outcome.prepared.path);
  assert.equal(outcome.prepared.play, null);
  assert.equal(outcome.prepared.play_not_made, null);

  for (const [ready, refusal] of [
    [{ ok: false, reason: 'tool_missing' }, 'tool_missing'],
    [{ ok: false, reason: 'tool_changed' }, 'tool_changed'],
  ] as const) {
    const id = await kept(store, clip('qt  ', 7));
    const refused = await placer(new StandInTool(join(data, 'work'), { ready })).add({
      projectId: 'p',
      commandId: `c-${refusal}`,
      folder: project,
      id,
      bytes: 0,
      added: () => assert.fail('nothing is added'),
    });
    assert.deepEqual(refused, { ok: false, refusal });
  }
  const id = await kept(store, clip('qt  ', 8));
  const full = await placer(new StandInTool(join(data, 'work'))).add({
    projectId: 'p',
    commandId: 'c-full',
    folder: project,
    id,
    bytes: MEDIA_FOLDER_BYTES - 10,
    added: () => assert.fail('nothing is added'),
  });
  assert.deepEqual(full, { ok: false, refusal: 'media_full' });
  assert.ok(
    !readdirSync(join(project, 'media')).some(
      (name) => name.includes('stills') && name.startsWith('video'),
    ),
  );
});

test('what is made stops at the folder bound and says no_room, and a video too long gets none (ADR 0066)', async () => {
  const data = fresh('data');
  const project = fresh('project');
  const store = new MediaStore(join(data, 'media'));
  const placer = (tool: MediaTool) =>
    new MediaPlacer({ store, tool, notes: join(data, 'media-placing'), protectedFolders: [data] });
  const id = await kept(store, clip());
  // Room for the copy and its stills, not for a play copy of 4 KB more.
  const room = MEDIA_FOLDER_BYTES - clip().length - 4 * png([1, 0, 0]).length - 1_000;
  const outcome = await placer(new StandInTool(join(data, 'work'), { playBytes: 4_000 })).add({
    projectId: 'p',
    commandId: 'c',
    folder: project,
    id,
    bytes: room,
    added: () => undefined,
  });
  assert.ok(outcome.ok);
  assert.equal(outcome.prepared.stills.length, 4);
  assert.equal(outcome.prepared.play, null);
  assert.equal(outcome.prepared.play_not_made, 'no_room');

  const long = await kept(store, clip('qt  ', 3));
  const tooLong = await placer(
    new StandInTool(join(data, 'work'), { facts: { width: 10, height: 10, durationMs: 180_001 } }),
  ).add({
    projectId: 'p',
    commandId: 'c2',
    folder: fresh('project-long'),
    id: long,
    bytes: 0,
    added: () => undefined,
  });
  assert.ok(tooLong.ok);
  assert.deepEqual(tooLong.prepared.stills, []);
  assert.equal(tooLong.prepared.stills_not_made, 'too_long');
  assert.equal(tooLong.prepared.play_not_made, 'too_long');
});

test('at the next start only a cut-off write of the host own name is removed, never a recorded medium, a link, a larger file or one outside media (ADR 0066)', async () => {
  const data = fresh('data');
  const project = fresh('project');
  const notes = join(data, 'media-placing');
  mkdirSync(notes, { recursive: true });
  mkdirSync(join(project, 'media', 'video-0123456789ab-stills'), { recursive: true });
  const write = (path: string, bytes: number) =>
    writeFileSync(join(project, path), Buffer.alloc(bytes));
  write('media/video-0123456789ab.mov', 10); // cut off: smaller than noted
  write('media/video-0123456789ab-stills/1.jpg', 50); // larger than noted: left
  write('media/photo-aaaaaaaaaaaa.jpg', 5); // recorded as added: never removed
  write('notes.txt', 1); // outside media/: left
  const outside = fresh('outside');
  writeFileSync(join(outside, 'secret.txt'), 'x');
  symlinkSync(join(outside, 'secret.txt'), join(project, 'media', 'video-bbbbbbbbbbbb.mov')); // a link: left
  writeFileSync(
    join(notes, 'a.json'),
    JSON.stringify({
      projectId: 'p',
      folder: project,
      entries: [
        { path: 'media/video-0123456789ab.mov', size: 100, sha256: 'x', medium: true },
        { path: 'media/video-0123456789ab-stills/1.jpg', size: 20, sha256: 'x', medium: false },
        { path: 'media/photo-aaaaaaaaaaaa.jpg', size: 100, sha256: 'x', medium: true },
        { path: 'notes.txt', size: 100, sha256: 'x', medium: false },
        { path: 'media/video-bbbbbbbbbbbb.mov', size: 100, sha256: 'x', medium: true },
      ],
    }),
  );
  const placer = new MediaPlacer({
    store: new MediaStore(join(data, 'media')),
    tool: new StandInTool(join(data, 'work')),
    notes,
    protectedFolders: [data],
  });
  const swept = await placer.reconcile(
    (projectId, path) => projectId === 'p' && path === 'media/photo-aaaaaaaaaaaa.jpg',
  );
  assert.deepEqual(swept, { removed: 1, left: 3 });
  assert.ok(!existsSync(join(project, 'media/video-0123456789ab.mov')));
  assert.ok(existsSync(join(project, 'media/video-0123456789ab-stills/1.jpg')));
  assert.ok(existsSync(join(project, 'media/photo-aaaaaaaaaaaa.jpg')));
  assert.ok(existsSync(join(project, 'notes.txt')));
  assert.ok(existsSync(join(outside, 'secret.txt')));
  assert.deepEqual(readdirSync(notes), [], 'the note goes once read');
});

test('an add stopped by a failure after the medium is in removes what it had made at once, never the medium (ADR 0066)', async () => {
  const data = fresh('data');
  const project = fresh('project');
  const store = new MediaStore(join(data, 'media'));
  class Breaking extends StandInTool {
    override async play(): Promise<never> {
      throw new Error('the tool broke');
    }
  }
  const placer = new MediaPlacer({
    store,
    tool: new Breaking(join(data, 'work')),
    notes: join(data, 'media-placing'),
    protectedFolders: [data],
  });
  const id = await kept(store, clip());
  let added = '';
  await assert.rejects(
    placer.add({
      projectId: 'p',
      commandId: 'c',
      folder: project,
      id,
      bytes: 0,
      added: (file) => {
        added = file.path;
      },
    }),
    /the tool broke/,
  );
  assert.ok(existsSync(join(project, added)), 'the medium stays');
  assert.deepEqual(
    readdirSync(join(project, `${added.slice(0, -4)}-stills`)),
    [],
    'its stills are gone',
  );
  assert.deepEqual(readdirSync(join(data, 'media-placing')), [], 'and its note');
});
