import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { describe, test } from 'node:test';
import { chromeArguments, DevToolsPipe } from './chrome.ts';

/** A pipe pair standing for Chrome's fd 3 (commands in) and fd 4 (answers and events out). */
function pair() {
  const toChrome = new PassThrough();
  const fromChrome = new PassThrough();
  const sent: Record<string, unknown>[] = [];
  let pending = '';
  toChrome.setEncoding('utf8');
  toChrome.on('data', (chunk: string) => {
    pending += chunk;
    for (let end = pending.indexOf('\0'); end !== -1; end = pending.indexOf('\0')) {
      sent.push(JSON.parse(pending.slice(0, end)) as Record<string, unknown>);
      pending = pending.slice(end + 1);
    }
  });
  return { pipe: new DevToolsPipe(toChrome, fromChrome), fromChrome, sent };
}

describe("the check's DevTools pipe (AUDIT M18A-S1)", () => {
  test('writes each command as JSON ended by a NUL byte, with its session, and resolves its answer', async () => {
    const { pipe, fromChrome, sent } = pair();
    const answer = pipe.send('Page.navigate', { url: 'http://127.0.0.1:1/' }, 'S1');
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(sent, [
      { id: 1, method: 'Page.navigate', params: { url: 'http://127.0.0.1:1/' }, sessionId: 'S1' },
    ]);
    // An answer split across chunks, with an event before it.
    fromChrome.write(
      '{"method":"Runtime.exceptionThrown","sessionId":"S1","params":{}}\0{"id":1,"res',
    );
    fromChrome.write('ult":{"frameId":"F"}}\0');
    assert.deepEqual(await answer, { frameId: 'F' });
  });

  test('passes events to listeners and rejects an answer with an error', async () => {
    const { pipe, fromChrome } = pair();
    const events: unknown[] = [];
    pipe.onEvent((message) => events.push(message.method));
    const failing = pipe.send('Target.attachToTarget', { targetId: 'nope' });
    fromChrome.write(
      '{"method":"Target.targetCreated","params":{}}\0{"id":1,"error":{"message":"No target"}}\0',
    );
    await assert.rejects(failing, /No target/);
    assert.deepEqual(events, ['Target.targetCreated']);
  });

  test('gives up on a command Chrome never answers', async () => {
    const { pipe } = pair();
    await assert.rejects(pipe.send('Page.enable', {}, undefined, 20), /did not answer Page.enable/);
  });

  test('starts Chrome headless with the pipe, never a DevTools port, and the network only to the preview', () => {
    const args = chromeArguments('/scratch/chrome', 47882);
    assert.ok(args.includes('--remote-debugging-pipe'));
    assert.ok(!args.some((arg) => arg.startsWith('--remote-debugging-port')));
    assert.ok(args.includes('--headless=new'));
    assert.ok(args.includes('--proxy-bypass-list=127.0.0.1:47882'));
    assert.ok(args.includes('--user-data-dir=/scratch/chrome/profile'));
  });
});
