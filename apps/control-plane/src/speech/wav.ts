import {
  TRANSCRIPTION_MAX_SECONDS,
  TRANSCRIPTION_MIN_SECONDS,
  TRANSCRIPTION_SAMPLE_RATE,
} from '@halcyonic/contracts';

export type ClipReading =
  | {
      readonly ok: true;
      /** The clip rewritten as a plain 44-byte-header WAV holding only its samples. */
      readonly wav: Buffer;
      readonly seconds: number;
    }
  | {
      readonly ok: false;
      readonly code: 'invalid_audio' | 'audio_too_short' | 'audio_too_long';
      readonly message: string;
    };

const FORMAT = 'The clip must be a WAV file of 16-bit mono PCM at 16 kHz.';

/**
 * Reads a clip a client sent: RIFF WAVE with one `fmt ` chunk for 16-bit mono PCM at 16 kHz and
 * one `data` chunk, other chunks skipped. Its samples are written into a WAV of Halcyonic's own
 * making, so the engine never reads the client's header or any other chunk it sent.
 */
export function readClip(body: Buffer): ClipReading {
  if (
    body.length < 12 ||
    body.toString('latin1', 0, 4) !== 'RIFF' ||
    body.toString('latin1', 8, 12) !== 'WAVE'
  ) {
    return invalid(FORMAT);
  }
  let format: { channels: number; rate: number; bits: number; tag: number } | null = null;
  let data: Buffer | null = null;
  let offset = 12;
  while (offset + 8 <= body.length) {
    const id = body.toString('latin1', offset, offset + 4);
    const size = body.readUInt32LE(offset + 4);
    const start = offset + 8;
    if (size > body.length - start) return invalid('The clip ends inside one of its chunks.');
    if (id === 'fmt ') {
      if (format !== null || size < 16) return invalid(FORMAT);
      format = {
        tag: body.readUInt16LE(start),
        channels: body.readUInt16LE(start + 2),
        rate: body.readUInt32LE(start + 4),
        bits: body.readUInt16LE(start + 14),
      };
    } else if (id === 'data') {
      if (data !== null) return invalid('The clip has more than one data chunk.');
      data = body.subarray(start, start + size);
    }
    // Chunks are padded to an even size.
    offset = start + size + (size % 2);
  }
  if (format === null || data === null) return invalid(FORMAT);
  if (
    format.tag !== 1 ||
    format.channels !== 1 ||
    format.rate !== TRANSCRIPTION_SAMPLE_RATE ||
    format.bits !== 16
  ) {
    return invalid(FORMAT);
  }
  if (data.length % 2 !== 0) return invalid('The clip holds half a sample.');
  const seconds = data.length / 2 / TRANSCRIPTION_SAMPLE_RATE;
  if (seconds < TRANSCRIPTION_MIN_SECONDS) {
    return {
      ok: false,
      code: 'audio_too_short',
      message: `The clip is shorter than ${TRANSCRIPTION_MIN_SECONDS} s.`,
    };
  }
  if (seconds > TRANSCRIPTION_MAX_SECONDS) {
    return {
      ok: false,
      code: 'audio_too_long',
      message: `The clip is longer than ${TRANSCRIPTION_MAX_SECONDS} s.`,
    };
  }
  return { ok: true, wav: writeWav(data), seconds };
}

/** A WAV file of 16-bit mono PCM at 16 kHz holding these sample bytes. */
export function writeWav(samples: Buffer): Buffer {
  const header = Buffer.alloc(44);
  header.write('RIFF', 0, 'latin1');
  header.writeUInt32LE(36 + samples.length, 4);
  header.write('WAVE', 8, 'latin1');
  header.write('fmt ', 12, 'latin1');
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(TRANSCRIPTION_SAMPLE_RATE, 24);
  header.writeUInt32LE(TRANSCRIPTION_SAMPLE_RATE * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write('data', 36, 'latin1');
  header.writeUInt32LE(samples.length, 40);
  return Buffer.concat([header, samples]);
}

function invalid(message: string): ClipReading {
  return { ok: false, code: 'invalid_audio', message };
}
