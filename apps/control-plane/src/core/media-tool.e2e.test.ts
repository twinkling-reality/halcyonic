import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { SANDBOX_EXEC } from '../seatbelt.ts';
import { checkMedia } from './media-kind.ts';
import { MediaTool, mediaToolProfile, toolEnvironment } from './media-tool.ts';
import { wholePicture } from './pictures.ts';

/**
 * The real tool on this Mac (ADR 0066), opt-in: HALCYONIC_FFMPEG names an ffmpeg (ffprobe beside it). Every
 * fixture is made here by the tool itself with made-up place and device tags, never anyone's footage.
 */
const ffmpeg = process.env.HALCYONIC_FFMPEG ?? '';
const skip = ffmpeg === '' ? 'set HALCYONIC_FFMPEG to run the real tool' : false;
const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-media-tool-')));
after(() => rmSync(root, { recursive: true, force: true }));

const PLACE = '+31.0001-004.0002/';
const MAKE = 'MarkerMakeQ7';
const MODEL = 'MarkerModelZ3';
const TRACK = 'TrackMarkerK5';
const TAKEN = '2026:01:02 03:04:05';

function tool(): MediaTool {
  const real = realpathSync(ffmpeg);
  const sha256 = createHash('sha256').update(readFileSync(real)).digest('hex');
  return new MediaTool({ ffmpeg, sha256 }, join(root, 'work'));
}

function run(args: readonly string[]): void {
  const result = spawnSync(ffmpeg, ['-nostdin', '-hide_banner', '-v', 'error', '-y', ...args]);
  assert.equal(result.status, 0, 'the fixture was made');
}

function probe(path: string, entries: string): string {
  return spawnSync(join(realpathSync(ffmpeg), '..', 'ffprobe'), [
    '-v',
    'error',
    '-show_entries',
    entries,
    '-of',
    'json',
    path,
  ]).stdout.toString('utf8');
}

/** The made-up place and device are in the fixture, so a clean copy is not clean by accident. */
function holdsPlace(path: string): void {
  const printed = probe(path, 'format_tags:stream_tags');
  for (const marker of [MAKE, MODEL, '31.0001'])
    assert.ok(printed.includes(marker), `${marker} is in the fixture`);
}

/** Every tag ffprobe prints of a file, and its bytes, hold none of the made-up place or device. */
function holdsNoPlace(path: string): void {
  const printed = probe(path, 'format_tags:stream_tags');
  for (const marker of ['location', 'creation_time', MAKE, MODEL, '31.0001'])
    assert.ok(!printed.includes(marker), `${marker} is not in its tags`);
  const bytes = readFileSync(path).toString('latin1');
  for (const marker of [MAKE, MODEL, '31.0001', 'Exif', TRACK, TAKEN])
    assert.ok(!bytes.includes(marker), `${marker} is not in its bytes`);
}

test('a video is stripped to its streams, its stills and play copy made, none holding a place or a device (ADR 0066, D4)', {
  skip,
}, async () => {
  // A clip with a timed text track holding a marker, as phones keep other tracks beside the picture.
  const words = join(root, 'words.srt');
  writeFileSync(words, `1\n00:00:00,000 --> 00:00:03,000\n${TRACK}\n`);
  const plain = join(root, 'plain.mov');
  run([
    ...['-f', 'lavfi', '-i', 'testsrc=duration=3:size=320x240:rate=10'],
    ...['-f', 'lavfi', '-i', 'sine=duration=3', '-i', words],
    ...['-map', '0', '-map', '1', '-map', '2'],
    ...['-c:v', 'libx264', '-c:a', 'aac', '-c:s', 'mov_text', '-shortest', plain],
  ]);
  // Then the place, the device, the time and which way up, in a stream copy as a phone stores them.
  const input = join(root, 'clip.mov');
  run([
    ...['-display_rotation', '90', '-i', plain, '-map', '0', '-c', 'copy'],
    ...[
      '-metadata',
      `location=${PLACE}`,
      '-metadata',
      `com.apple.quicktime.location.ISO6709=${PLACE}`,
    ],
    ...[
      '-metadata',
      `com.apple.quicktime.make=${MAKE}`,
      '-metadata',
      `com.apple.quicktime.model=${MODEL}`,
    ],
    ...['-metadata', 'creation_time=2026-01-02T03:04:05Z', '-metadata:s:v', `handler_name=${MAKE}`],
    ...['-movflags', 'use_metadata_tags', input],
  ]);
  holdsPlace(input);
  assert.ok(
    readFileSync(input).toString('latin1').includes(TRACK),
    'the text track is in the fixture',
  );
  const media = tool();
  const out = await media.folder();
  try {
    const stripped = await media.strip(input, 'video/quicktime', out.path);
    assert.ok(stripped.ok);
    assert.equal((await checkMedia(stripped.value.path))?.type, 'video/quicktime');
    assert.ok(Math.abs((stripped.value.facts.durationMs ?? 0) - 3000) < 200);
    assert.equal(stripped.value.facts.width, 320);
    holdsNoPlace(stripped.value.path);
    // The one thing of the file kept besides its streams: which way up it is drawn.
    assert.match(probe(stripped.value.path, 'stream_side_data'), /rotation/);
    // The same upload makes the same copy, so its name is the content's.
    const first = createHash('sha256').update(readFileSync(stripped.value.path)).digest('hex');
    const second = await media.folder();
    try {
      const again = await media.strip(input, 'video/quicktime', second.path);
      assert.ok(again.ok);
      assert.equal(
        createHash('sha256').update(readFileSync(again.value.path)).digest('hex'),
        first,
      );
    } finally {
      await second.done();
    }
    const still = await media.still(stripped.value.path, 1500, out.path, 1);
    assert.ok(still.ok);
    assert.ok(wholePicture(readFileSync(still.value)));
    holdsNoPlace(still.value);
    const play = await media.play(stripped.value.path, out.path);
    assert.ok(play.ok);
    assert.equal((await checkMedia(play.value))?.type, 'video/mp4');
    holdsNoPlace(play.value);
  } finally {
    await out.done();
  }
});

test('a photo is re-encoded as a JPEG with no Exif, whatever it held (ADR 0066, C5)', {
  skip,
}, async () => {
  const plain = join(root, 'plain.jpg');
  run(['-f', 'lavfi', '-i', 'testsrc=size=64x48', '-frames:v', '1', plain]);
  // A made-up Exif segment, its place and maker as plain text, put after the JPEG's start.
  const exif = Buffer.from(`Exif\0\0GPS ${PLACE} ${MAKE} ${MODEL} ${TAKEN}`, 'latin1');
  const segment = Buffer.concat([Buffer.from([0xff, 0xe1, 0, exif.length + 2]), exif]);
  const original = readFileSync(plain);
  const input = join(root, 'tagged.jpg');
  writeFileSync(input, Buffer.concat([original.subarray(0, 2), segment, original.subarray(2)]));
  const media = tool();
  const out = await media.folder();
  try {
    const stripped = await media.strip(input, 'image/jpeg', out.path);
    assert.ok(stripped.ok);
    assert.ok(wholePicture(readFileSync(stripped.value.path)));
    holdsNoPlace(stripped.value.path);
  } finally {
    await out.done();
  }
});

test('the tool profile reads its one input and denies a file beside it in the data folder (ADR 0066, D1)', {
  skip,
}, () => {
  const data = join(root, 'data');
  mkdirSync(join(data, 'out'), { recursive: true });
  const input = join(data, 'in.jpg');
  run(['-f', 'lavfi', '-i', 'testsrc=size=32x32', '-frames:v', '1', input]);
  const marker = join(data, 'beside.jpg');
  writeFileSync(marker, readFileSync(input));
  const real = realpathSync(ffmpeg);
  const profile = join(data, 'tool.sb');
  writeFileSync(profile, mediaToolProfile({ tool: real, input, out: join(data, 'out') }));
  const read = (from: string, to: string) =>
    spawnSync(
      SANDBOX_EXEC,
      ['-f', profile, real, '-nostdin', '-v', 'error', '-y', '-i', from, join(data, 'out', to)],
      {
        env: toolEnvironment(join(data, 'out'), join(data, 'out')),
      },
    ).status;
  assert.equal(read(input, 'a.png'), 0, 'its own input');
  assert.notEqual(read(marker, 'b.png'), 0, 'not a file beside it');
});

test('the tool starts with four variables and none of the host own (ADR 0066, D2)', () => {
  process.env.HALCYONIC_MEDIA_MARKER = 'never-passed';
  try {
    const env = toolEnvironment('/h', '/t');
    assert.deepEqual(Object.keys(env).sort(), ['HOME', 'LANG', 'PATH', 'TMPDIR']);
    assert.ok(!Object.values(env).includes('never-passed'));
  } finally {
    delete process.env.HALCYONIC_MEDIA_MARKER;
  }
});

test('a HEIC photo from a phone becomes a whole JPEG, its still a picture a runtime reads (ADR 0066)', {
  skip,
}, async () => {
  const jpeg = join(root, 'grid.jpg');
  run(['-f', 'lavfi', '-i', 'testsrc=size=1024x768', '-frames:v', '1', jpeg]);
  // The fixture's HEIC is made by macOS's own sips, outside any profile, only to have one: the host never runs sips.
  const heic = join(root, 'grid.heic');
  assert.equal(spawnSync('/usr/bin/sips', ['-s', 'format', 'heic', jpeg, '--out', heic]).status, 0);
  assert.equal((await checkMedia(heic))?.type, 'image/heic');
  const media = tool();
  const out = await media.folder();
  try {
    const stripped = await media.strip(heic, 'image/heic', out.path);
    assert.ok(stripped.ok);
    assert.equal((await checkMedia(stripped.value.path))?.type, 'image/jpeg');
    const still = await media.still(stripped.value.path, 0, out.path, 1);
    assert.ok(still.ok);
    assert.ok(wholePicture(readFileSync(still.value)));
  } finally {
    await out.done();
  }
});
