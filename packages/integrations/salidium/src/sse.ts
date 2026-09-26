const LF = 0x0a;
const CR = 0x0d;

/** The stream broke the rules or a limit, so nothing more from it can be trusted. */
export class EventStreamError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'EventStreamError';
  }
}

/**
 * Incremental parser for a server-sent event stream, following the "interpreting an event stream"
 * rules of the WHATWG HTML standard for what this client needs: lines end in CRLF, LF or CR (also
 * when a chunk boundary splits a CRLF), lines starting with a colon are comments, `data` lines
 * accumulate and are joined with LF, and a blank line dispatches the event. An event without data
 * dispatches nothing, and an event the stream ends in the middle of is never dispatched.
 *
 * `event`, `id` and `retry` are valid fields that Salidium's feed never sends: messages carry their
 * type in their JSON, and there is no replay to resume from. They are ignored.
 *
 * Text is pushed already decoded; decoding UTF-8 with `TextDecoder` also drops a leading byte order
 * mark, as the standard requires.
 */
export class SseParser {
  readonly #maxEventLength: number;
  #partial = '';
  #data: string[] = [];
  #dataLength = 0;
  #skipLf = false;

  /** `maxEventLength` bounds one line and one event's data, so a stream cannot grow memory freely. */
  constructor(maxEventLength: number) {
    this.#maxEventLength = maxEventLength;
  }

  /** Consumes decoded text and returns the data of every event it completed, in order. */
  push(text: string): string[] {
    const events: string[] = [];
    let start = 0;
    if (this.#skipLf && text.length > 0) {
      this.#skipLf = false;
      if (text.charCodeAt(0) === LF) start = 1;
    }
    for (let index = start; index < text.length; index++) {
      const code = text.charCodeAt(index);
      if (code !== LF && code !== CR) continue;
      this.#line(this.#partial + text.slice(start, index), events);
      this.#partial = '';
      if (code === CR) {
        if (index + 1 === text.length) this.#skipLf = true;
        else if (text.charCodeAt(index + 1) === LF) index++;
      }
      start = index + 1;
    }
    this.#partial += text.slice(start);
    if (this.#partial.length > this.#maxEventLength)
      throw new EventStreamError(`a line exceeded ${this.#maxEventLength} characters`);
    return events;
  }

  #line(line: string, events: string[]): void {
    if (line === '') {
      if (this.#data.length > 0) events.push(this.#data.join('\n'));
      this.#data = [];
      this.#dataLength = 0;
      return;
    }
    if (line.charCodeAt(0) === 0x3a) return;
    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    if (field !== 'data') return;
    let value = colon === -1 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    this.#dataLength += value.length + 1;
    if (this.#dataLength > this.#maxEventLength)
      throw new EventStreamError(`an event exceeded ${this.#maxEventLength} characters`);
    this.#data.push(value);
  }
}
