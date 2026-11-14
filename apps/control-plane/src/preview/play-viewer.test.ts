import assert from 'node:assert/strict';
import { request } from 'node:http';
import { createServer } from 'node:net';
import { describe, test } from 'node:test';
import { PlayViewer, type ViewerPlay } from './play-viewer.ts';

/** A free port, and the one after it, for a viewer of the test's own. */
async function freePorts(): Promise<[number, number]> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as { port: number };
  await new Promise((resolve) => server.close(resolve));
  return [port, port];
}

interface Answer {
  readonly status: number;
  readonly headers: Record<string, string | string[] | undefined>;
  readonly body: string;
}

/** A GET with exactly these headers; a browser's navigation by default. */
function get(port: number, path: string, headers: Record<string, string> = {}): Promise<Answer> {
  return new Promise((resolve, reject) => {
    const req = request(
      {
        host: '127.0.0.1',
        port,
        path,
        headers: { host: `127.0.0.1:${port}`, 'sec-fetch-site': 'none', ...headers },
      },
      (res) => {
        let body = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => {
          body += chunk;
        });
        res.on('end', () => resolve({ status: res.statusCode ?? 0, headers: res.headers, body }));
      },
    );
    req.on('error', reject);
    req.end();
  });
}

/** A play standing in: frames sent on demand, inputs kept, watchers counted. */
function standIn() {
  const watchers = new Set<(frame: string) => void>();
  const inputs: unknown[] = [];
  const play: ViewerPlay = {
    watch(listener) {
      watchers.add(listener);
      return () => watchers.delete(listener);
    },
    async input(message) {
      inputs.push(message);
      return true;
    },
  };
  const frame = (bytes: string) => {
    for (const watcher of watchers) watcher(Buffer.from(bytes).toString('base64'));
  };
  return { play, watchers, inputs, frame };
}

/** Opens the viewer's link as the person's browser would, and returns its cookie. */
async function signIn(viewer: PlayViewer): Promise<string> {
  const link = new URL(viewer.link);
  const answer = await get(viewer.port, `${link.pathname}${link.search}`);
  assert.equal(answer.status, 200);
  assert.match(
    answer.body,
    /http-equiv="refresh" content="0;url=\/"/,
    'a page that goes on by itself, never a redirect',
  );
  const cookie = String(answer.headers['set-cookie']).split(';')[0] ?? '';
  return cookie;
}

function connect(port: number, headers: Record<string, string>): Promise<WebSocket> {
  const socket = new WebSocket(`ws://127.0.0.1:${port}/stream`, { headers } as never);
  socket.binaryType = 'arraybuffer';
  return new Promise((resolve, reject) => {
    socket.addEventListener('open', () => resolve(socket), { once: true });
    socket.addEventListener('error', () => reject(new Error('refused')), { once: true });
  });
}

const until = async (check: () => boolean, what: string) => {
  const end = Date.now() + 5000;
  while (!check()) {
    if (Date.now() > end) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
};

describe('the viewer of a play on the Mac: a server of its own, its one play only (P-A)', () => {
  test("the link's code works once and in time, and becomes this port's own HttpOnly, SameSite=Strict cookie", async () => {
    let now = 1_000;
    const viewer = await PlayViewer.open(standIn().play, {
      ports: await freePorts(),
      now: () => now,
      codeMs: 60_000,
    });
    try {
      const link = new URL(viewer.link);
      assert.equal(link.origin, `http://127.0.0.1:${viewer.port}`);
      // A wrong code is refused and does not use up the right one.
      assert.equal((await get(viewer.port, '/?code=AAAA')).status, 403);
      assert.equal((await get(viewer.port, '/')).status, 403, 'no page before the code');
      now += 60_000;
      const first = await get(viewer.port, `/${link.search}`);
      // A page that goes on to '/' by itself, never a redirect (a redirect would keep a hand-over's cross-site navigation).
      assert.equal(first.status, 200);
      assert.equal(first.headers.location, undefined);
      assert.match(first.body, /http-equiv="refresh" content="0;url=\/"/);
      const cookie = String(first.headers['set-cookie']);
      assert.match(cookie, new RegExp(`^halcyonic_play_${viewer.port}=[0-9a-f]{64}; `));
      assert.match(cookie, /; HttpOnly/);
      assert.match(cookie, /; SameSite=Strict/);
      assert.match(cookie, /; Path=\//);
      const again = await get(viewer.port, `/${link.search}`);
      assert.equal(again.status, 403, 'a code works once');
      const page = await get(viewer.port, '/', {
        cookie: cookie.split(';')[0] ?? '',
        'sec-fetch-site': 'same-origin',
      });
      assert.equal(page.status, 200);
      assert.equal(page.headers['cache-control'], 'no-store');
      assert.match(String(page.headers['content-security-policy']), /default-src 'none'/);
      assert.match(
        String(page.headers['content-security-policy']),
        new RegExp(`connect-src ws://127\\.0\\.0\\.1:${viewer.port};`),
      );
      assert.equal(
        (await get(viewer.port, '/', { cookie: `halcyonic_play_${viewer.port}=${'0'.repeat(64)}` }))
          .status,
        403,
        'only its own session',
      );
    } finally {
      await viewer.close();
    }
  });

  test('a code past its time is refused', async () => {
    let now = 1_000;
    const viewer = await PlayViewer.open(standIn().play, {
      ports: await freePorts(),
      now: () => now,
      codeMs: 60_000,
    });
    try {
      now += 60_001;
      const link = new URL(viewer.link);
      assert.equal((await get(viewer.port, `/${link.search}`)).status, 403);
    } finally {
      await viewer.close();
    }
  });

  test('a cookie of its name another port planted ahead of its own keeps no one out, and lets no one in (AUDIT S2)', async () => {
    const viewer = await PlayViewer.open(standIn().play, { ports: await freePorts() });
    try {
      const own = await signIn(viewer);
      // A page on another 127.0.0.1 port can set the same name for a longer path, which the browser sends first.
      const planted = `halcyonic_play_${viewer.port}=${'0'.repeat(64)}`;
      const page = await get(viewer.port, '/', {
        cookie: `${planted}; ${own}`,
        'sec-fetch-site': 'same-origin',
      });
      assert.equal(
        page.status,
        200,
        'its own session among the cookies of its name lets the person in',
      );
      const alone = await get(viewer.port, '/', {
        cookie: `${planted}; ${planted.replace(/0$/, '1')}`,
        'sec-fetch-site': 'same-origin',
      });
      assert.equal(alone.status, 403, 'planted cookies alone are refused');
    } finally {
      await viewer.close();
    }
  });

  test("the link's code comes from the hand-over file as another site's top-level navigation, and nothing else may", async () => {
    // CARBON A1-1: the code is handed over in a file (open-link.ts), never in open's arguments, and a file's origin
    // counts as another site. Only the trade of a code, as a page the person navigates to, may come so.
    const viewer = await PlayViewer.open(standIn().play, { ports: await freePorts() });
    try {
      const link = new URL(viewer.link);
      const navigate = {
        'sec-fetch-site': 'cross-site',
        'sec-fetch-mode': 'navigate',
        'sec-fetch-dest': 'document',
      };
      for (const headers of [
        { ...navigate, 'sec-fetch-mode': 'cors' },
        { ...navigate, 'sec-fetch-dest': 'iframe' },
      ]) {
        const answer = await get(viewer.port, `${link.pathname}${link.search}`, headers);
        assert.equal(answer.status, 403, JSON.stringify(headers));
      }
      assert.equal(
        (await get(viewer.port, '/', navigate)).status,
        403,
        'no code, nothing opens from another site',
      );
      const traded = await get(viewer.port, `${link.pathname}${link.search}`, navigate);
      assert.equal(traded.status, 200, 'the code, navigated to, is traded once');
      // Followed as a browser does: the page's own refresh to '/' is same-origin and carries the cookie. A redirect
      // would have kept the hand-over's cross-site navigation, and '/' would be refused (AUDIT3's B1).
      assert.match(traded.body, /http-equiv="refresh" content="0;url=\/"/);
      const cookie = String(traded.headers['set-cookie']).split(';')[0] ?? '';
      const refresh = {
        cookie,
        'sec-fetch-site': 'same-origin',
        'sec-fetch-mode': 'navigate',
        'sec-fetch-dest': 'document',
      };
      assert.equal((await get(viewer.port, '/', refresh)).status, 200);
      assert.equal(
        (await get(viewer.port, '/', { ...refresh, 'sec-fetch-site': 'cross-site' })).status,
        403,
        'with the cookie but from another site, still refused',
      );
    } finally {
      await viewer.close();
    }
  });

  test('another name for it, a page of another port, or a request a page started elsewhere are refused, with the cookie', async () => {
    const viewer = await PlayViewer.open(standIn().play, { ports: await freePorts() });
    try {
      const cookie = await signIn(viewer);
      const own = { cookie, 'sec-fetch-site': 'same-origin' };
      assert.equal((await get(viewer.port, '/', own)).status, 200);
      for (const headers of [
        { ...own, host: `localhost:${viewer.port}` },
        { ...own, origin: 'http://127.0.0.1:47930' },
        { ...own, origin: `http://localhost:${viewer.port}` },
        { ...own, 'sec-fetch-site': 'same-site' },
        { ...own, 'sec-fetch-site': 'cross-site' },
      ]) {
        assert.equal((await get(viewer.port, '/', headers)).status, 403, JSON.stringify(headers));
      }
      const missing = await new Promise<number>((resolve, reject) => {
        const req = request(
          {
            host: '127.0.0.1',
            port: viewer.port,
            path: '/',
            headers: { host: `127.0.0.1:${viewer.port}`, cookie },
          },
          (res) => {
            res.resume();
            resolve(res.statusCode ?? 0);
          },
        );
        req.on('error', reject);
        req.end();
      });
      assert.equal(missing, 403, 'a request that does not say where it came from');
    } finally {
      await viewer.close();
    }
  });

  test('the stream: frames out, input in, only from its own origin with its cookie; the watch ends with the socket', async () => {
    const { play, watchers, inputs, frame } = standIn();
    const viewer = await PlayViewer.open(play, { ports: await freePorts() });
    try {
      const cookie = await signIn(viewer);
      const origin = `http://127.0.0.1:${viewer.port}`;
      for (const headers of [
        { cookie, origin: 'http://127.0.0.1:47930' },
        { cookie, origin: `http://localhost:${viewer.port}` },
        { cookie },
        { origin },
        { cookie: `halcyonic_play_${viewer.port}=${'0'.repeat(64)}`, origin },
      ]) {
        await assert.rejects(connect(viewer.port, headers), /refused/, JSON.stringify(headers));
      }
      assert.equal(watchers.size, 0);
      const socket = await connect(viewer.port, { cookie, origin });
      const received: string[] = [];
      socket.addEventListener('message', (event) => {
        received.push(Buffer.from(event.data as ArrayBuffer).toString());
      });
      await until(() => watchers.size === 1, 'the stream to watch the play');
      frame('jpeg one');
      await until(() => received.length === 1, 'a frame');
      assert.deepEqual(received, ['jpeg one']);
      const press = { type: 'key', action: 'down', key: 'a', code: 'KeyA' };
      socket.send(JSON.stringify(press));
      socket.send('not json');
      await until(() => inputs.length === 1, 'the input');
      assert.deepEqual(inputs, [press]);
      socket.close();
      await until(() => watchers.size === 0, 'the watch to end with the socket');
    } finally {
      await viewer.close();
    }
  });

  test('closed, it answers nothing and ends its stream', async () => {
    const { play, watchers } = standIn();
    const viewer = await PlayViewer.open(play, { ports: await freePorts() });
    const cookie = await signIn(viewer);
    const socket = await connect(viewer.port, {
      cookie,
      origin: `http://127.0.0.1:${viewer.port}`,
    });
    const closed = new Promise((resolve) => socket.addEventListener('close', resolve));
    await until(() => watchers.size === 1, 'the stream to watch the play');
    await viewer.close();
    await closed;
    await until(() => watchers.size === 0, 'the watch to end');
    await assert.rejects(get(viewer.port, '/'));
  });

  test("its page, script and styles are Halcyonic's own, served only to its session, with their types", async () => {
    const viewer = await PlayViewer.open(standIn().play, { ports: await freePorts() });
    try {
      for (const path of ['/', '/viewer.js', '/viewer.css'])
        assert.equal(
          (await get(viewer.port, path, { 'sec-fetch-site': 'same-origin' })).status,
          403,
          path,
        );
      const cookie = await signIn(viewer);
      const own = { cookie, 'sec-fetch-site': 'same-origin' };
      const page = await get(viewer.port, '/', own);
      assert.match(String(page.headers['content-type']), /^text\/html/);
      assert.deepEqual(
        [...page.body.matchAll(/(?:src|href)="([^"]+)"/g)].map((match) => match[1]),
        ['/viewer.css', '/viewer.js'],
        'the page loads nothing but its own two files',
      );
      const script = await get(viewer.port, '/viewer.js', own);
      assert.equal(script.status, 200);
      assert.match(String(script.headers['content-type']), /^text\/javascript/);
      assert.match(script.body, /new WebSocket\(`ws:\/\/\$\{location\.host\}\/stream`\)/);
      const styles = await get(viewer.port, '/viewer.css', own);
      assert.equal(styles.status, 200);
      assert.match(String(styles.headers['content-type']), /^text\/css/);
      assert.equal((await get(viewer.port, '/other.js', own)).status, 404);
    } finally {
      await viewer.close();
    }
  });

  test('what it announces reaches every stream, and the latest of each kind greets a stream that comes back', async () => {
    const viewer = await PlayViewer.open(standIn().play, { ports: await freePorts() });
    try {
      const cookie = await signIn(viewer);
      const origin = `http://127.0.0.1:${viewer.port}`;
      const texts = (socket: WebSocket) => {
        const seen: unknown[] = [];
        socket.addEventListener('message', (event) => {
          if (typeof event.data === 'string') seen.push(JSON.parse(event.data));
        });
        return seen;
      };
      const first = await connect(viewer.port, { cookie, origin });
      const firstSeen = texts(first);
      viewer.announce({ type: 'sound', state: 'none' });
      viewer.announce({ type: 'sound', state: 'playing' });
      await until(() => firstSeen.length === 2, 'both announcements');
      first.close();
      const again = await connect(viewer.port, { cookie, origin });
      const againSeen = texts(again);
      await until(() => againSeen.length === 1, 'the latest sound state');
      assert.deepEqual(againSeen, [{ type: 'sound', state: 'playing' }]);
      again.close();
    } finally {
      await viewer.close();
    }
  });

  test('ending, it tells every stream why, then closes', async () => {
    const viewer = await PlayViewer.open(standIn().play, { ports: await freePorts() });
    const cookie = await signIn(viewer);
    const socket = await connect(viewer.port, {
      cookie,
      origin: `http://127.0.0.1:${viewer.port}`,
    });
    const seen: unknown[] = [];
    socket.addEventListener('message', (event) => {
      if (typeof event.data === 'string') seen.push(JSON.parse(event.data));
    });
    const closed = new Promise((resolve) => socket.addEventListener('close', resolve));
    try {
      await viewer.end({ type: 'ended', reason: 'newer_check' });
      await closed;
      assert.deepEqual(seen, [{ type: 'ended', reason: 'newer_check' }]);
      await assert.rejects(get(viewer.port, '/'));
    } finally {
      await viewer.close();
    }
  });

  test('it takes the next port of its range when one is in use, and refuses when none is free', async () => {
    // A port held here, and the next one held too until just before the viewer opens, so no other
    // test's connection can take it in between.
    const listen = (server: ReturnType<typeof createServer>, at: number) =>
      new Promise<boolean>((resolve) => {
        server.once('error', () => resolve(false));
        server.listen(at, '127.0.0.1', () => resolve(true));
      });
    const taken = createServer();
    const next = createServer();
    // Below the system's temporary ports (49152 and up), which other tests' connections take at any time.
    let port = 0;
    for (let attempt = 0; attempt < 50 && port === 0; attempt += 1) {
      const held = 41_000 + Math.floor(Math.random() * 6000);
      if (!(await listen(taken, held))) continue;
      if (await listen(next, held + 1)) port = held;
      else await new Promise((resolve) => taken.close(resolve));
    }
    assert.notEqual(port, 0, 'two free ports in a row');
    try {
      await assert.rejects(
        PlayViewer.open(standIn().play, { ports: [port, port] }),
        /every viewer port is in use/,
      );
      await new Promise((resolve) => next.close(resolve));
      const viewer = await PlayViewer.open(standIn().play, { ports: [port, port + 1] });
      assert.equal(viewer.port, port + 1);
      await viewer.close();
    } finally {
      await new Promise((resolve) => taken.close(resolve));
    }
  });
});
