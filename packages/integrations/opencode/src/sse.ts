/** One server-sent event: the joined `data` lines, or a comment line such as a heartbeat. */
export type SseMessage =
  | { readonly kind: 'data'; readonly data: string }
  | { readonly kind: 'comment'; readonly text: string };

/**
 * Incremental parser for `text/event-stream`. Chunks may split lines and multi-byte characters
 * anywhere. The `event`, `id` and `retry` fields are ignored: OpenCode 2.0.18 never sends them
 * and carries its own event id inside the data.
 */
export class SseParser {
  readonly #decoder = new TextDecoder();
  #buffer = '';
  #data: string[] = [];

  push(chunk: Uint8Array): SseMessage[] {
    return this.#consume(this.#decoder.decode(chunk, { stream: true }), false);
  }

  /** Flushes the input. An event without its terminating blank line is discarded, as the format requires. */
  end(): SseMessage[] {
    const messages = this.#consume(this.#decoder.decode(), true);
    this.#data = [];
    return messages;
  }

  #consume(text: string, final: boolean): SseMessage[] {
    this.#buffer += text;
    const messages: SseMessage[] = [];
    let start = 0;
    for (let index = 0; index < this.#buffer.length; index += 1) {
      const code = this.#buffer.charCodeAt(index);
      if (code !== 10 && code !== 13) continue;
      // A carriage return at the end of the input may be the first half of a CRLF.
      if (code === 13 && index + 1 === this.#buffer.length && !final) break;
      const line = this.#buffer.slice(start, index);
      if (code === 13 && this.#buffer.charCodeAt(index + 1) === 10) index += 1;
      start = index + 1;
      this.#line(line, messages);
    }
    this.#buffer = this.#buffer.slice(start);
    return messages;
  }

  #line(line: string, messages: SseMessage[]): void {
    if (line === '') {
      if (this.#data.length > 0) messages.push({ kind: 'data', data: this.#data.join('\n') });
      this.#data = [];
      return;
    }
    if (line.startsWith(':')) {
      messages.push({ kind: 'comment', text: line.slice(1).trim() });
      return;
    }
    const colon = line.indexOf(':');
    const field = colon < 0 ? line : line.slice(0, colon);
    if (field !== 'data') return;
    const value = colon < 0 ? '' : line.slice(colon + 1);
    this.#data.push(value.startsWith(' ') ? value.slice(1) : value);
  }
}
