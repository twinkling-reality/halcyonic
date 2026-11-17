import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { Principal } from '@halcyonic/contracts';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { spokenNames } from './tidy.ts';
import {
  CLIPS_PER_MINUTE,
  CLIPS_WAITING,
  type TranscriptionAnswer,
  Transcriptions,
  TURN_WAIT_MS,
} from './transcriptions.ts';
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

/** A headset of its own, numbered. */
const device = (n: number): Principal => ({
  kind: 'device',
  device_id: `01990000-0000-7000-8000-0000000001${String(n).padStart(2, '0')}` as never,
});

/** Lets every promise already resolved run its continuations. */
const settle = () => new Promise<void>((resolve) => setImmediate(resolve));

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

function setUp(results: EngineResult[] = [], names?: () => readonly string[]) {
  const time = createVirtualTime(new Date('2026-10-01T09:00:00.000Z'));
  const engine = new ScriptedEngine(results);
  return {
    time,
    engine,
    transcriptions: new Transcriptions({
      engine,
      clock: time,
      scheduler: time,
      ...(names === undefined ? {} : { names }),
    }),
  };
}

const textOf = (answer: TranscriptionAnswer) =>
  answer.kind === 'answered' && answer.body.outcome === 'heard' ? answer.body.text : answer;

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

  test('a draft is tidied with the names known when it is heard; nothing heard stays nothing heard', async () => {
    let projects = ['Space Garden'];
    const { transcriptions } = setUp(
      [
        { kind: 'text', text: 'Um, open space garden.' },
        { kind: 'text', text: 'Uh, open pond with codex.' },
        { kind: 'text', text: '' },
      ],
      () => spokenNames(projects),
    );
    assert.equal(textOf(await transcriptions.transcribe(LOCAL, clip)), 'Open Space Garden.');
    projects = ['Pond'];
    assert.equal(textOf(await transcriptions.transcribe(LOCAL, clip)), 'Open Pond with Codex.');
    assert.deepEqual(await transcriptions.transcribe(LOCAL, clip), {
      kind: 'answered',
      body: { outcome: 'nothing_heard', engine: ENGINE },
      seconds: 1,
    });
  });

  test("without a names source, Halcyonic's own names are still spelled as written", async () => {
    const { transcriptions } = setUp([{ kind: 'text', text: 'Um, ask opencode.' }]);
    assert.equal(textOf(await transcriptions.transcribe(LOCAL, clip)), 'Ask OpenCode.');
  });

  test('the words are shown as heard when tidying fails, leaves nothing or outgrows a draft', async () => {
    const longest = 'claudecode '.repeat(372).trim();
    assert.ok(longest.length <= 4096);
    const failing = setUp([{ kind: 'text', text: 'um, open space garden' }], () => {
      throw new Error('the projection is closed');
    });
    assert.equal(
      textOf(await failing.transcriptions.transcribe(LOCAL, clip)),
      'um, open space garden',
    );
    const { transcriptions } = setUp([
      { kind: 'text', text: 'Um.' },
      { kind: 'text', text: longest },
    ]);
    assert.equal(textOf(await transcriptions.transcribe(LOCAL, clip)), 'Um.');
    assert.equal(textOf(await transcriptions.transcribe(LOCAL, clip)), longest);
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

  test('one clip at a time for each principal, and the next clip waits its turn on the Mac', async () => {
    const { transcriptions, engine } = setUp();
    engine.hold = true;
    const first = transcriptions.transcribe(HEADSET, clip);
    await Promise.resolve();
    assert.deepEqual(refusal(await transcriptions.transcribe(HEADSET, clip)), {
      status: 429,
      code: 'transcription_busy',
    });
    const second = transcriptions.transcribe(OTHER, clip);
    await settle();
    assert.equal(
      engine.received.length,
      1,
      'the second clip waits; the Mac transcribes one at a time',
    );
    engine.release();
    assert.equal((await first).kind, 'answered');
    await settle();
    assert.equal(engine.received.length, 2, 'the waiting clip is transcribed next');
    engine.release();
    assert.equal((await second).kind, 'answered');
  });

  test('three clips wait behind the one on the Mac; a fourth is refused in words naming them', async () => {
    const { transcriptions, engine } = setUp();
    engine.hold = true;
    const running = transcriptions.transcribe(device(0), clip);
    await Promise.resolve();
    const waiting = [1, 2, 3].map((n) => transcriptions.transcribe(device(n), clip));
    await settle();
    const full = await transcriptions.transcribe(device(4), clip);
    assert.deepEqual(refusal(full), { status: 503, code: 'transcription_busy_on_mac' });
    assert.equal(
      full.kind === 'refused' ? full.message : '',
      'The Mac is transcribing a clip and 3 more are waiting; try again in a moment.',
    );
    for (let index = 0; index < 4; index++) {
      engine.release();
      await settle();
    }
    assert.equal((await running).kind, 'answered');
    for (const each of waiting) assert.equal((await each).kind, 'answered');
    assert.equal(engine.received.length, 4, 'the refused clip never reached the engine');
    assert.equal(CLIPS_WAITING, 3);
  });

  test('a clip that waits longer than its turn allows is refused in words, and the queue moves on', async () => {
    const { transcriptions, engine, time } = setUp();
    engine.hold = true;
    const running = transcriptions.transcribe(HEADSET, clip);
    await Promise.resolve();
    const late = transcriptions.transcribe(OTHER, clip);
    await settle();
    await time.advance(TURN_WAIT_MS);
    const answer = await late;
    assert.deepEqual(refusal(answer), { status: 503, code: 'transcription_busy_on_mac' });
    assert.equal(
      answer.kind === 'refused' ? answer.message : '',
      'The Mac was still transcribing the clip ahead of yours after 4 s; try again in a moment.',
    );
    engine.release();
    assert.equal((await running).kind, 'answered');
    engine.hold = false;
    assert.equal((await transcriptions.transcribe(OTHER, clip)).kind, 'answered');
    assert.equal(engine.received.length, 2, 'the clip that gave up never reached the engine');
  });

  test('thirty clips a minute reach the engine for each principal, counted apart', async () => {
    const { transcriptions, time } = setUp();
    assert.equal(CLIPS_PER_MINUTE, 30);
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

  test('the warm-up holds the Mac, a clip waits for it, and it is skipped while a clip is transcribed', async () => {
    const { transcriptions, engine } = setUp();
    engine.hold = true;
    const warming = transcriptions.warmUp();
    await Promise.resolve();
    const waited = transcriptions.transcribe(LOCAL, clip);
    await settle();
    assert.equal(engine.received.length, 0, 'the clip waits for the warm-up');
    engine.release();
    assert.equal(await warming, 5);
    await settle();
    assert.equal(engine.received.length, 1);
    assert.equal(await transcriptions.warmUp(), null);
    engine.release();
    assert.equal((await waited).kind, 'answered');
  });
});
