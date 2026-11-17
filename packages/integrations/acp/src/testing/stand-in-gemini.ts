import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';

/** One scripted model reply: Gemini `parts`, a function call or text. */
export type StandInReply = readonly Record<string, unknown>[];

/**
 * A stand-in for the Gemini API on loopback, for the end to end suite: Antigravity reaches it
 * through `GOOGLE_GEMINI_BASE_URL` with a dummy key, so whole turns run with no sign-in and no
 * spend. A request that offers tools takes the next scripted reply (the last one repeats); one
 * without tools, Antigravity's title request, is answered with a title. It keeps each request's
 * body in memory, for a test to ask whether a marker reached the model (`heard`).
 */
export class StandInGemini {
  readonly #server: Server;
  readonly #replies: StandInReply[];
  #next = 0;
  readonly models: string[] = [];
  readonly #bodies: string[] = [];

  private constructor(replies: StandInReply[]) {
    this.#replies = replies;
    this.#server = createServer((request, response) => {
      let body = '';
      request.setEncoding('utf8');
      request.on('data', (chunk: string) => {
        body += chunk;
      });
      request.on('end', () => {
        const url = request.url ?? '';
        this.#bodies.push(body);
        const model = /models\/([^:/?]+)/.exec(url)?.[1];
        if (model !== undefined) this.models.push(model);
        let tools = false;
        try {
          tools = Array.isArray((JSON.parse(body) as { tools?: unknown }).tools);
        } catch {
          tools = false;
        }
        const parts = tools
          ? (this.#replies[Math.min(this.#next++, this.#replies.length - 1)] ?? [{ text: 'Done.' }])
          : [{ text: 'Stand-in title' }];
        const reply = {
          candidates: [{ content: { role: 'model', parts }, finishReason: 'STOP', index: 0 }],
          // 40 of the 100 input tokens read from the cache, 5 of the output spent thinking (Gemini counts them apart).
          usageMetadata: {
            promptTokenCount: 100,
            cachedContentTokenCount: 40,
            candidatesTokenCount: 10,
            thoughtsTokenCount: 5,
            totalTokenCount: 115,
          },
          modelVersion: 'stand-in',
        };
        if (/streamgeneratecontent/i.test(url)) {
          response.writeHead(200, { 'content-type': 'text/event-stream' });
          response.end(`data: ${JSON.stringify(reply)}\r\n\r\n`);
        } else if (/generatecontent/i.test(url)) {
          response.writeHead(200, { 'content-type': 'application/json' });
          response.end(JSON.stringify(reply));
        } else {
          response.writeHead(404, { 'content-type': 'application/json' });
          response.end(
            JSON.stringify({
              error: { code: 404, message: 'Not served by the stand-in.', status: 'NOT_FOUND' },
            }),
          );
        }
      });
    });
  }

  static async start(replies: StandInReply[]): Promise<StandInGemini> {
    const standIn = new StandInGemini(replies);
    await new Promise<void>((resolve) => standIn.#server.listen(0, '127.0.0.1', resolve));
    return standIn;
  }

  get url(): string {
    return `http://127.0.0.1:${(this.#server.address() as AddressInfo).port}`;
  }

  /** Whether any request the app sent the model carried `marker`. */
  heard(marker: string): boolean {
    return this.#bodies.some((body) => body.includes(marker));
  }

  /** How many scripted replies were taken. */
  get taken(): number {
    return this.#next;
  }

  close(): Promise<void> {
    return new Promise((resolve) => this.#server.close(() => resolve()));
  }
}
