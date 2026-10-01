import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { Principal } from '@halcyonic/contracts';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { CLIPS_PER_MINUTE, type TranscriptionAnswer, Transcriptions } from './transcriptions.ts';
import { writeWav } from './wav.ts';
import type { EngineResult, SpeechEngine } from './whisper.ts';

const ENGINE = { name: 'whisper.cpp', version: '1.9.4-test' };
const LOCAL: Principal = { kind: 'local' };
const HEADSET: Principal = {
  kind: 'device',
  device_id: '01990000-0000-7000-8000-000000000001' as never,
};
const OTHER: Principal = {
  kind: 'device',
  device_id: '01990000-0000-7000-8000-000000000002' as never,
};
const clip = writeWav(Buffer.alloc(32_000));

/** An engine that answers each clip with the next scripted result, holding it until released. */
class ScriptedEngine implements SpeechEngine {
  readonly engine = ENGINE;
  readonly received: Buffer[] = [];
  #held: Array<() => void> = [];
  hold = false;
  readonly #results: EngineResult[];

  constructor(results: EngineResult[]) {
    this.#results = results;
  }

  async transcribe(wav: Buffer): Promise<EngineResult> {
    this.received.push(wav);
    if (this.hold) await new Promise<void>((resolve) => this.#held.push(resolve));
    return this.#results.shift() ?? { kind: 'text', text: 'again' };
  }

  async warmUp(): Promise<number> {
    if (this.hold) await new Promise<void>((resolve) => this.#held.push(resolve));
    return 5;
  }

  release(): void {
    for (const resolve of this.#held.splice(0)) resolve();
  }
}

function setUp(results: EngineResult[] = []) {
  const time = createVirtualTime(new Date('2026-10-01T09:00:00.000Z'));
  const engine = new ScriptedEngine(results);
  return { time, engine, transcriptions: new Transcriptions({ engine, clock: time }) };
}

const refusal = (answer: TranscriptionAnswer) =>
  answer.kind === 'refused' ? { status: answer.status, code: answer.code } : null;

describe('turning a clip into a draft', () => {
  test('without an engine, voice is unavailable', async () => {
    const transcriptions = new Transcriptions({
      engine: null,
      clock: createVirtualTime(new Date()),
    });
    assert.deepEqual(refusal(await transcriptions.transcribe(LOCAL, clip)), {
      status: 503,
      code: 'transcription_unavailable',
    });
    assert.equal(await transcriptions.warmUp(), null);
  });

  test('text is a draft heard in English; no text is nothing heard', async () => {
    const { transcriptions, engine } = setUp([
      { kind: 'text', text: 'Add a contact form.' },
      { kind: 'text', text: '' },
    ]);
    const heard = await transcriptions.transcribe(HEADSET, clip);
    assert.deepEqual(heard, {
      kind: 'answered',
      body: { outcome: 'heard', text: 'Add a contact form.', language: 'en', engine: ENGINE },
      seconds: 1,
    });
    assert.deepEqual(engine.received[0], clip);
    assert.deepEqual(await transcriptions.transcribe(HEADSET, clip), {
      kind: 'answered',
      body: { outcome: 'nothing_heard', engine: ENGINE },
      seconds: 1,
    });
  });

  test("the engine's failure, or a transcript too long for a draft, is a failed transcription", async () => {
    const { transcriptions } = setUp([
      { kind: 'failed', message: 'The engine took longer than 15 s.' },
      { kind: 'text', text: 'word '.repeat(1000).trim() },
    ]);
    const failed = await transcriptions.transcribe(LOCAL, clip);
    assert.deepEqual(failed, {
      kind: 'refused',
      status: 502,
      code: 'transcription_failed',
      message: 'The engine took longer than 15 s.',
    });
    assert.deepEqual(refusal(await transcriptions.transcribe(LOCAL, clip)), {
      status: 502,
      code: 'transcription_failed',
    });
  });

  test('a body that is not a clip never reaches the engine', async () => {
    const { transcriptions, engine } = setUp();
    assert.deepEqual(refusal(await transcriptions.transcribe(LOCAL, undefined)), {
      status: 400,
      code: 'invalid_audio',
    });
    assert.deepEqual(refusal(await transcriptions.transcribe(LOCAL, { text: 'hello' })), {
      status: 400,
      code: 'invalid_audio',
    });
    assert.deepEqual(
      refusal(await transcriptions.transcribe(LOCAL, writeWav(Buffer.alloc(8_000)))),
      {
        status: 400,
        code: 'audio_too_short',
      },
    );
    assert.equal(engine.received.length, 0);
  });

  test('one clip at a time for each principal, and one at a time on the Mac', async () => {
    const { transcriptions, engine } = setUp();
    engine.hold = true;
    const first = transcriptions.transcribe(HEADSET, clip);
    await Promise.resolve();
    assert.deepEqual(refusal(await transcriptions.transcribe(HEADSET, clip)), {
      status: 429,
      code: 'transcription_busy',
    });
    assert.deepEqual(refusal(await transcriptions.transcribe(OTHER, clip)), {
      status: 503,
      code: 'transcription_busy_on_mac',
    });
    engine.release();
    assert.equal((await first).kind, 'answered');
    engine.hold = false;
    assert.equal((await transcriptions.transcribe(OTHER, clip)).kind, 'answered');
    assert.equal(engine.received.length, 2);
  });

  test('six clips a minute reach the engine for each principal, counted apart', async () => {
    const { transcriptions, time } = setUp();
    for (let index = 0; index < CLIPS_PER_MINUTE; index++) {
      assert.equal((await transcriptions.transcribe(HEADSET, clip)).kind, 'answered');
    }
    assert.deepEqual(refusal(await transcriptions.transcribe(HEADSET, clip)), {
      status: 429,
      code: 'rate_limited',
    });
    assert.equal((await transcriptions.transcribe(OTHER, clip)).kind, 'answered');
    await time.advance(60_000);
    assert.equal((await transcriptions.transcribe(HEADSET, clip)).kind, 'answered');
  });

  test('the warm-up holds the Mac, and is skipped while a clip is transcribed', async () => {
    const { transcriptions, engine } = setUp();
    engine.hold = true;
    const warming = transcriptions.warmUp();
    await Promise.resolve();
    assert.deepEqual(refusal(await transcriptions.transcribe(LOCAL, clip)), {
      status: 503,
      code: 'transcription_busy_on_mac',
    });
    engine.release();
    assert.equal(await warming, 5);
    const transcribing = transcriptions.transcribe(LOCAL, clip);
    await Promise.resolve();
    assert.equal(await transcriptions.warmUp(), null);
    engine.release();
    await transcribing;
  });
});
