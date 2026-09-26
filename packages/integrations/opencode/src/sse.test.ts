import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { type SseMessage, SseParser } from './sse.ts';

const encoder = new TextEncoder();
const FIXTURE = readFileSync(new URL('../fixtures/approvals.sse', import.meta.url));

function parseChunks(chunks: readonly Uint8Array[]): SseMessage[] {
  const parser = new SseParser();
  return [...chunks.flatMap((chunk) => parser.push(chunk)), ...parser.end()];
}

function parseText(text: string): SseMessage[] {
  return parseChunks([encoder.encode(text)]);
}

describe('SSE parser', () => {
  test('reads data events and comments as OpenCode frames them', () => {
    assert.deepEqual(parseText('data: {"a":1}\n\n: heartbeat\n\ndata: {"b":2}\n\n'), [
      { kind: 'data', data: '{"a":1}' },
      { kind: 'comment', text: 'heartbeat' },
      { kind: 'data', data: '{"b":2}' },
    ]);
  });

  test('gives the same events whatever the chunk boundaries', () => {
    const whole = parseChunks([FIXTURE]);
    assert.ok(whole.filter((message) => message.kind === 'data').length > 60);
    const bytes = [...FIXTURE].map((byte) => Uint8Array.of(byte));
    assert.deepEqual(parseChunks(bytes), whole);
    const uneven: Uint8Array[] = [];
    let size = 1;
    for (let start = 0; start < FIXTURE.length; start += size) {
      size = ((size * 7 + 3) % 101) + 1;
      uneven.push(FIXTURE.subarray(start, start + size));
    }
    assert.deepEqual(parseChunks(uneven), whole);
  });

  test('accepts CRLF and CR line endings, including a CRLF split across chunks', () => {
    const expected = [
      { kind: 'data', data: 'one' },
      { kind: 'data', data: 'two' },
    ];
    assert.deepEqual(parseText('data: one\r\n\r\ndata: two\r\n\r\n'), expected);
    assert.deepEqual(parseText('data: one\r\rdata: two\r\r'), expected);
    assert.deepEqual(
      parseChunks([encoder.encode('data: one\r'), encoder.encode('\n\r'), encoder.encode('\n')]),
      [{ kind: 'data', data: 'one' }],
    );
  });

  test('joins multi-line data, keeps split multi-byte characters and ignores other fields', () => {
    const bytes = encoder.encode('event: x\nid: 7\nretry: 5\ndata: café\ndata:second\n\n');
    const split = bytes.indexOf(0xc3) + 1;
    assert.deepEqual(parseChunks([bytes.subarray(0, split), bytes.subarray(split)]), [
      { kind: 'data', data: 'café\nsecond' },
    ]);
  });

  test('discards an event whose terminating blank line never arrived', () => {
    assert.deepEqual(parseText('data: complete\n\ndata: cut off'), [
      { kind: 'data', data: 'complete' },
    ]);
  });
});
