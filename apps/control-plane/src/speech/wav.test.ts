import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { readClip, writeWav } from './wav.ts';

/** A WAV with these format fields and sample bytes, and any chunks before the data. */
function wav(
  samples: Buffer,
  format: { tag?: number; channels?: number; rate?: number; bits?: number } = {},
  before: Buffer[] = [],
): Buffer {
  const fmt = Buffer.alloc(24);
  fmt.write('fmt ', 0, 'latin1');
  fmt.writeUInt32LE(16, 4);
  fmt.writeUInt16LE(format.tag ?? 1, 8);
  fmt.writeUInt16LE(format.channels ?? 1, 10);
  fmt.writeUInt32LE(format.rate ?? 16_000, 12);
  fmt.writeUInt32LE((format.rate ?? 16_000) * 2, 16);
  fmt.writeUInt16LE(2, 20);
  fmt.writeUInt16LE(format.bits ?? 16, 22);
  const data = chunk('data', samples);
  const body = Buffer.concat([Buffer.from('WAVE', 'latin1'), fmt, ...before, data]);
  const riff = Buffer.alloc(8);
  riff.write('RIFF', 0, 'latin1');
  riff.writeUInt32LE(body.length, 4);
  return Buffer.concat([riff, body]);
}

function chunk(id: string, content: Buffer): Buffer {
  const head = Buffer.alloc(8);
  head.write(id, 0, 'latin1');
  head.writeUInt32LE(content.length, 4);
  return Buffer.concat([head, content, Buffer.alloc(content.length % 2)]);
}

/** Seconds of 16 kHz samples counting up, so a copy can be told from a reordering. */
function samples(seconds: number): Buffer {
  const buffer = Buffer.alloc(Math.round(seconds * 16_000) * 2);
  for (let index = 0; index < buffer.length / 2; index++)
    buffer.writeInt16LE(index % 32_768, index * 2);
  return buffer;
}

describe('a clip sent for transcription', () => {
  test('a 16 kHz mono PCM16 clip is read, and only its samples reach the WAV the engine reads', () => {
    const sound = samples(1.5);
    const reading = readClip(
      wav(sound, {}, [chunk('LIST', Buffer.from('INFOISFT\u0003\u0000\u0000\u0000abc', 'latin1'))]),
    );
    assert.ok(reading.ok);
    assert.equal(reading.seconds, 1.5);
    assert.deepEqual(reading.wav, writeWav(sound));
    assert.equal(reading.wav.length, 44 + sound.length);
    assert.equal(reading.wav.subarray(44).equals(sound), true);
  });

  test('only 16-bit mono PCM at 16 kHz is taken', () => {
    for (const format of [
      { tag: 3 },
      { channels: 2 },
      { rate: 44_100 },
      { rate: 8_000 },
      { bits: 8 },
      { bits: 24 },
    ]) {
      const reading = readClip(wav(samples(1), format));
      assert.equal(reading.ok, false, JSON.stringify(format));
      assert.equal(!reading.ok && reading.code, 'invalid_audio');
    }
  });

  test('what is not a whole RIFF WAVE file is invalid audio', () => {
    const good = wav(samples(1));
    const cases: Buffer[] = [
      Buffer.alloc(0),
      Buffer.from('{"text":"hello"}'),
      Buffer.concat([Buffer.from('RIFX'), good.subarray(4)]),
      Buffer.concat([good.subarray(0, 8), Buffer.from('AVI '), good.subarray(12)]),
      // The data chunk claims more than was sent.
      good.subarray(0, good.length - 2),
      // No data chunk.
      good.subarray(0, 36),
      // A second data chunk after the first.
      Buffer.concat([good, chunk('data', samples(0.6))]),
      // Half a sample.
      wav(Buffer.alloc(16_001)),
    ];
    for (const [index, body] of cases.entries()) {
      const reading = readClip(body);
      assert.equal(reading.ok, false, `case ${index}`);
      assert.equal(!reading.ok && reading.code, 'invalid_audio', `case ${index}`);
    }
  });

  test('a clip from 0.5 s to 30 s is taken; shorter or longer is refused by name', () => {
    assert.equal(readClip(wav(samples(0.5))).ok, true);
    assert.equal(readClip(wav(samples(30))).ok, true);
    const short = readClip(wav(samples(0.49)));
    assert.equal(!short.ok && short.code, 'audio_too_short');
    const long = readClip(wav(samples(30.01)));
    assert.equal(!long.ok && long.code, 'audio_too_long');
  });
});
