import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { EventStreamError, SseParser } from './sse.ts';

const parse = (...chunks: string[]) => {
  const parser = new SseParser(1024);
  return chunks.flatMap((chunk) => parser.push(chunk));
};

describe('server-sent event parsing', () => {
  test('dispatches data on a blank line and joins data lines with a line feed', () => {
    assert.deepEqual(parse('data: {"a":1}\n\ndata: first\ndata: second\n\n'), [
      '{"a":1}',
      'first\nsecond',
    ]);
  });

  test('accepts LF, CRLF and CR line endings, and a CRLF split across chunks', () => {
    assert.deepEqual(parse('data: a\r\n\r\ndata: b\r\rdata: c\n\n'), ['a', 'b', 'c']);
    assert.deepEqual(parse('data: a\r', '\n\r', '\ndata: b\r', '', '\n\n'), ['a', 'b']);
  });

  test('dispatches an event ended by CR at a chunk boundary without waiting for the next chunk', () => {
    const parser = new SseParser(1024);
    assert.deepEqual(parser.push('data: a\r\r'), ['a']);
    assert.deepEqual(parser.push('\ndata: b\n\n'), ['b']);
  });

  test('ignores comments, including the padding Salidium sends before the first message', () => {
    assert.deepEqual(parse(`: salidium ${' '.repeat(2048)}\n\n:ping\ndata: x\n\n`), ['x']);
  });

  test('strips one leading space from a value and treats a field without a colon as empty', () => {
    assert.deepEqual(parse('data:  two spaces\n\ndata:none\n\ndata\ndata: z\n\n'), [
      ' two spaces',
      'none',
      '\nz',
    ]);
  });

  test('ignores event, id, retry and unknown fields', () => {
    assert.deepEqual(parse('event: update\nid: 7\nretry: 10\nfoo: bar\ndata: x\n\n'), ['x']);
  });

  test('dispatches nothing for an event without data', () => {
    assert.deepEqual(parse('\n\nevent: nothing\n\n: comment\n\n'), []);
  });

  test('never dispatches an event the stream ends in the middle of', () => {
    assert.deepEqual(parse('data: complete\n\ndata: cut off\n'), ['complete']);
  });

  test('gives the same events however the text is split', () => {
    const stream = `: pad\r\ndata: {"type":"resync"}\r\n\r\ndata: a\rdata: b\r\r:c\ndata: é\n\n`;
    const whole = parse(stream);
    assert.deepEqual(whole, ['{"type":"resync"}', 'a\nb', 'é']);
    for (let cut = 0; cut <= stream.length; cut++)
      assert.deepEqual(parse(stream.slice(0, cut), stream.slice(cut)), whole, `cut at ${cut}`);
  });

  test('bounds a line and an event, so a stream cannot grow memory freely', () => {
    assert.throws(() => new SseParser(16).push('x'.repeat(17)), EventStreamError);
    assert.throws(
      () => new SseParser(16).push('data: 12345678\ndata: 12345678\n'),
      EventStreamError,
    );
    assert.deepEqual(new SseParser(16).push('data: 1234567\ndata: 1234567\n\n'), [
      '1234567\n1234567',
    ]);
  });

  test('receives text decoded as UTF-8, which drops a byte order mark and joins split characters', () => {
    const bytes = new TextEncoder().encode(`${String.fromCharCode(0xfeff)}data: é\n\n`);
    const decoder = new TextDecoder();
    const parser = new SseParser(1024);
    // Three bytes of byte order mark and six of "data: ", then a cut inside the two bytes of "é".
    const events = [bytes.subarray(0, 10), bytes.subarray(10)].flatMap((chunk) =>
      parser.push(decoder.decode(chunk, { stream: true })),
    );
    assert.deepEqual(events, ['é']);
  });
});
