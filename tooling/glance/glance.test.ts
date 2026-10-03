/**
 * The glance (apps/xr/Android/glance) is a second client of the control plane, written in Java for
 * Android. These tests run its plain-Java parts on the Mac: its whole poll (GlancePoll) against a
 * real control plane and against listeners that misbehave, its token checks, its loopback proof
 * against security.ts's, and its text rule against the client core's LabelText (the same cases are
 * checked in C# by GlanceParityTests). They compile every source, the Android ones against Android's
 * API, and hold every snapshot field the glance reads to the generated JSON Schema, so a contract
 * change cannot silently break it. They need a JDK and Android's platform jar: Unity's, or
 * `JAVA_HOME` and `ANDROID_JAR`; without them they fail rather than pass unseen.
 */
import assert from 'node:assert/strict';
import { execFile, execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { createServer, type Server, type Socket } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { loopbackProof } from '../../apps/control-plane/src/http/security.ts';
import { startTestServer } from '../../apps/control-plane/src/testing/harness.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const GLANCE = join(ROOT, 'apps/xr/Android/glance');
const SOURCES = join(GLANCE, 'src/com/halcyonic/glance');
const UNITY_ANDROID = '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer';

function jdk(): string | null {
  const candidates = [
    process.env.JAVA_HOME ? join(process.env.JAVA_HOME, 'bin') : null,
    join(UNITY_ANDROID, 'OpenJDK/bin'),
  ];
  for (const bin of candidates) {
    if (bin !== null && existsSync(join(bin, 'javac')) && existsSync(join(bin, 'java'))) return bin;
  }
  return null;
}

function androidJar(): string | null {
  const candidates = [
    process.env.ANDROID_JAR,
    join(UNITY_ANDROID, 'SDK/platforms/android-36/android.jar'),
  ];
  return candidates.find((jar) => jar !== undefined && existsSync(jar)) ?? null;
}

/** The plain-Java sources: everything in the glance that does not touch Android. */
const PLAIN = ['GlancePoll.java', 'GlanceProof.java', 'GlanceText.java'];

/** The sources that use Android's API, compiled against it but run only on a headset. */
const ANDROID_ONLY = ['GlanceActivity.java', 'GlanceClient.java'];

/** The same table GlanceParityTests checks against LabelText.Plain in C#. */
export const TEXT_CASES: readonly (readonly [string, string])[] = [
  ['  a\tb  c\n', 'a b  c'],
  ['x\u202Ey', 'x‹U+202E›y'],
  ['a\u200Bb', 'a‹U+200B›b'],
  ['\uE000 icon', '‹U+E000› icon'],
  ['lone \uD800 half', 'lone ‹U+D800› half'],
  ['smile \u{1F600}', 'smile \u{1F600}'],
  ['tag \u{E0041}', 'tag ‹U+E0041›'],
  ['line\u2028break', 'line break'],
  ['no\u00A0break', 'no break'],
  ['', ''],
];

/** A token of the control plane's form, for listeners that hold one. */
const TOKEN = 'k3Hq9vZbN0aR_x-7cLmW2pYtE5sDfG8u';

const bin = jdk();
const jar = androidJar();
const classes = mkdtempSync(join(tmpdir(), 'halcyonic-glance-'));

function args(command: string, rest: readonly string[], flags: readonly string[] = []): string[] {
  const encoded = rest.map((value) => Buffer.from(value, 'utf16le').toString('base64'));
  return [...flags, '-cp', classes, 'com.halcyonic.glance.GlanceSelfTest', command, ...encoded];
}

function run(command: string, ...rest: string[]): string {
  const output = execFileSync(join(bin as string, 'java'), args(command, rest), {
    encoding: 'utf8',
  });
  return Buffer.from(output.trim(), 'base64').toString('utf8');
}

/** As run, without blocking the event loop, for a server in this process to answer. */
async function runAsync(command: string, rest: readonly string[], flags: readonly string[] = []) {
  const { stdout } = await promisify(execFile)(
    join(bin as string, 'java'),
    args(command, rest, flags),
    {
      encoding: 'utf8',
    },
  );
  return Buffer.from(stdout.trim(), 'base64').toString('utf8');
}

/** A poll of 127.0.0.1:port with `token`: its code, the snapshot it read, and how long it took. */
async function poll(port: number, token: string, flags: readonly string[] = []) {
  const started = Date.now();
  const output = await runAsync('poll', ['127.0.0.1', String(port), token], flags);
  const newline = output.indexOf('\n');
  const [code, cause] = output.slice(0, newline).split(' ');
  return {
    code: code as string,
    cause,
    snapshot: output.slice(newline + 1),
    ms: Date.now() - started,
  };
}

/** What a listener received: each connection's bytes, as text. */
interface Heard {
  readonly connections: string[];
}

/**
 * A listener on loopback that answers each complete request (a head ending in a blank line) with
 * whatever `answer` writes for it, given its index on its connection, and records every byte.
 */
async function listener(
  answer: (request: string, index: number, socket: Socket, port: number) => void,
): Promise<{ server: Server; port: number; heard: Heard; close: () => Promise<void> }> {
  const heard: Heard = { connections: [] };
  const sockets = new Set<Socket>();
  let port = 0;
  const server = createServer((socket) => {
    sockets.add(socket);
    const slot = heard.connections.push('') - 1;
    let pending = '';
    let index = 0;
    socket.on('error', () => {});
    socket.on('close', () => sockets.delete(socket));
    socket.on('data', (data) => {
      const text = data.toString('latin1');
      heard.connections[slot] += text;
      pending += text;
      for (let end = pending.indexOf('\r\n\r\n'); end >= 0; end = pending.indexOf('\r\n\r\n')) {
        const request = pending.slice(0, end);
        pending = pending.slice(end + 4);
        answer(request, index, socket, port);
        index += 1;
      }
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  if (address === null || typeof address === 'string') throw new Error('no port');
  port = address.port;
  return {
    server,
    port,
    heard,
    close: async () => {
      for (const socket of sockets) socket.destroy();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
  };
}

/** The challenge in a health request. */
function challengeOf(request: string): string {
  return /x-halcyonic-challenge: ([0-9a-f]+)/i.exec(request)?.[1] ?? '';
}

/** A health answer that proves holding TOKEN, as the control plane's would, kept open. */
function proved(request: string, port: number): string {
  const proof = loopbackProof(TOKEN, `127.0.0.1:${port}`, challengeOf(request));
  return `HTTP/1.1 200 OK\r\nx-halcyonic-proof: ${proof}\r\ncontent-length: 15\r\n\r\n{"status":"ok"}`;
}

/** A 200 with no proof in its head and `body` after it. */
function withBody(body: string): string {
  return `HTTP/1.1 200 OK\r\ncontent-length: ${body.length}\r\n\r\n${body}`;
}

/** Sends `text` one byte a second until the socket closes. */
function drip(socket: Socket, text: string): void {
  let sent = 0;
  const timer = setInterval(() => {
    if (socket.destroyed || sent >= text.length) {
      clearInterval(timer);
      return;
    }
    socket.write(text[sent] as string);
    sent += 1;
  }, 1000);
  socket.on('close', () => clearInterval(timer));
}

describe('the glance', () => {
  before(() => {
    assert.ok(bin, 'no JDK: install Unity 6000.3.25f1 with Android, or set JAVA_HOME');
    assert.ok(
      jar,
      'no Android platform jar: install Unity 6000.3.25f1 with Android, or set ANDROID_JAR',
    );
    const plain = PLAIN.map((name) => join(SOURCES, name));
    plain.push(join(GLANCE, 'test/com/halcyonic/glance/GlanceSelfTest.java'));
    execFileSync(join(bin, 'javac'), ['--release', '11', '-d', classes, ...plain]);
  });

  after(() => rmSync(classes, { recursive: true, force: true }));

  test('every source compiles against Android’s API', () => {
    const android = mkdtempSync(join(tmpdir(), 'halcyonic-glance-android-'));
    try {
      const all = [...PLAIN, ...ANDROID_ONLY].map((name) => join(SOURCES, name));
      execFileSync(join(bin as string, 'javac'), [
        '--release',
        '11',
        '-cp',
        jar as string,
        '-d',
        android,
        ...all,
      ]);
    } finally {
      rmSync(android, { recursive: true, force: true });
    }
  });

  test('its loopback proof is the control plane’s, and only that proves', () => {
    const token = 'a6Zr1N0n-example-token_9xk';
    const address = '127.0.0.1:47800';
    const challenge = 'ab'.repeat(32);
    const proof = loopbackProof(token, address, challenge);
    assert.equal(run('proof', token, address, challenge), proof);
    assert.equal(run('proves', proof, token, address, challenge), 'true');
    assert.equal(
      run('proves', proof, token, '127.0.0.1:47801', challenge),
      'false',
      'another port',
    );
    assert.equal(
      run('proves', proof, 'another-token', address, challenge),
      'false',
      'another token',
    );
    assert.equal(
      run('proves', proof.toUpperCase(), token, address, challenge),
      'false',
      'only lowercase hex',
    );
    assert.equal(run('proves', '', token, address, challenge), 'false');
    const challengeMade = run('challenge');
    assert.match(challengeMade, /^[0-9a-f]{64}$/);
    assert.notEqual(run('challenge'), challengeMade);
  });

  test('it takes a token only from a private regular file of its own, in the control plane’s form', () => {
    assert.equal(
      run('token', '1', '1', '600', `${TOKEN}\n`),
      TOKEN,
      'a trailing line break is fine',
    );
    assert.equal(run('token', '1', '1', '400', TOKEN), TOKEN);
    assert.equal(run('token', '0', '1', '600', TOKEN), 'token_not_private', 'not a regular file');
    assert.equal(run('token', '1', '0', '600', TOKEN), 'token_not_private', 'another user’s');
    assert.equal(
      run('token', '1', '1', '640', TOKEN),
      'token_not_private',
      'its group can read it',
    );
    assert.equal(run('token', '1', '1', '602', TOKEN), 'token_not_private', 'anyone can write it');
    assert.equal(run('token', '1', '1', '600', TOKEN.slice(0, 31)), 'token_malformed', 'too short');
    assert.equal(
      run('token', '1', '1', '600', `${TOKEN} ${TOKEN}`),
      'token_malformed',
      'two words',
    );
    assert.equal(run('token', '1', '1', '600', `${TOKEN}=`), 'token_malformed', 'not base64url');
    assert.equal(
      run('token', '1', '1', '600', 'a'.repeat(4096)),
      'token_malformed',
      'longer than a token file',
    );
  });

  test('it reads a real control plane’s snapshot, proved on the connection the token goes on', async () => {
    const server = await startTestServer();
    try {
      const { code, snapshot } = await poll(server.port, server.token);
      assert.equal(code, 'ok');
      const read = JSON.parse(snapshot) as { projects: unknown[]; workstreams: unknown[] };
      assert.ok(Array.isArray(read.projects) && Array.isArray(read.workstreams));
      assert.equal(
        (await poll(server.port, TOKEN)).code,
        'unproved',
        'another token is not proved',
      );
    } finally {
      await server.stop();
    }
  });

  test('it sends the token only after the proof, on the same connection, and follows no redirect', async () => {
    const holder = await listener((request, index, socket, port) => {
      if (index === 0) socket.write(proved(request, port));
      else
        socket.end(
          'HTTP/1.1 302 Found\r\nlocation: http://127.0.0.1:1/api/snapshot\r\ncontent-length: 0\r\n\r\n',
        );
    });
    try {
      assert.equal((await poll(holder.port, TOKEN)).code, 'refused_302');
      assert.equal(
        holder.heard.connections.length,
        1,
        'one connection, and nothing followed the redirect',
      );
      const [health, snapshot, ...more] = (holder.heard.connections[0] as string).split('\r\n\r\n');
      assert.match(health as string, /^GET \/api\/health HTTP\/1\.1\r\n/);
      assert.doesNotMatch(health as string, /authorization/i, 'no token before the proof');
      assert.match(snapshot as string, /^GET \/api\/snapshot HTTP\/1\.1\r\n/);
      assert.match(snapshot as string, new RegExp(`\r\nAuthorization: Bearer ${TOKEN}\r\n`));
      assert.deepEqual(more, [''], 'no third request');
    } finally {
      await holder.close();
    }
  });

  test('a listener that cannot prove it holds the token never receives it', async () => {
    const answers = [
      [
        'a wrong proof',
        `HTTP/1.1 200 OK\r\nx-halcyonic-proof: ${'a'.repeat(64)}\r\ncontent-length: 0\r\n\r\n`,
      ],
      ['no proof', 'HTTP/1.1 200 OK\r\ncontent-length: 0\r\n\r\n'],
      ['a proof in the body', withBody(`x-halcyonic-proof: ${'a'.repeat(64)}\r\n\r\n`)],
      ['a head too long', `HTTP/1.1 200 OK\r\nx-filler: ${'x'.repeat(20_000)}\r\n\r\n`],
      ['not a 200', `HTTP/1.1 403 Forbidden\r\nx-halcyonic-proof: ${'a'.repeat(64)}\r\n\r\n`],
    ] as const;
    for (const [what, answer] of answers) {
      const squatter = await listener((_request, _index, socket) => socket.write(answer));
      try {
        const { code, ms } = await poll(squatter.port, TOKEN);
        assert.equal(code, 'unproved', what);
        assert.ok(ms < 5000, `${what} answered at once`);
        assert.doesNotMatch(
          squatter.heard.connections.join(''),
          /authorization|k3Hq9vZbN0aR/i,
          what,
        );
      } finally {
        await squatter.close();
      }
    }
  });

  test('a proof that cannot carry the token on its own connection is not taken', async () => {
    const answers = [
      [
        'two proofs',
        (proof: string) => `HTTP/1.1 200 OK\r\n${proof}\r\n${proof}\r\ncontent-length: 0\r\n\r\n`,
      ],
      [
        'closing the connection',
        (proof: string) =>
          `HTTP/1.1 200 OK\r\n${proof}\r\nconnection: close\r\ncontent-length: 0\r\n\r\n`,
      ],
      ['HTTP/1.0', (proof: string) => `HTTP/1.0 200 OK\r\n${proof}\r\ncontent-length: 0\r\n\r\n`],
      ['no length', (proof: string) => `HTTP/1.1 200 OK\r\n${proof}\r\n\r\n`],
    ] as const;
    for (const [what, answer] of answers) {
      const holder = await listener((request, _index, socket, port) =>
        socket.write(
          answer(
            `x-halcyonic-proof: ${loopbackProof(TOKEN, `127.0.0.1:${port}`, challengeOf(request))}`,
          ),
        ),
      );
      try {
        assert.equal((await poll(holder.port, TOKEN)).code, 'unproved', what);
        assert.doesNotMatch(holder.heard.connections.join(''), /authorization/i, what);
      } finally {
        await holder.close();
      }
    }
  });

  test('a snapshot is read by its length, at most a mebibyte', async () => {
    const answers = [
      ['too large', 'HTTP/1.1 200 OK\r\ncontent-length: 1048577\r\n\r\n{', 'too_large'],
      [
        'chunked',
        'HTTP/1.1 200 OK\r\ntransfer-encoding: chunked\r\n\r\n2\r\n{}\r\n0\r\n\r\n',
        'unreadable',
      ],
      ['no length', 'HTTP/1.1 200 OK\r\n\r\n{}', 'unreadable'],
      ['refused', 'HTTP/1.1 401 Unauthorized\r\ncontent-length: 0\r\n\r\n', 'refused_401'],
    ] as const;
    for (const [what, answer, expected] of answers) {
      const holder = await listener((request, index, socket, port) => {
        if (index === 0) socket.write(proved(request, port));
        else socket.write(answer);
      });
      try {
        const { code, ms } = await poll(holder.port, TOKEN);
        assert.equal(code, expected, what);
        assert.ok(ms < 5000, `${what} answered at once`);
      } finally {
        await holder.close();
      }
    }
  });

  test('a poll ends at its deadline however slowly whatever answers sends', async () => {
    const slowHealth = await listener((_request, _index, socket) =>
      drip(socket, `HTTP/1.1 200 OK\r\nx-filler: ${'x'.repeat(100)}\r\n\r\n`),
    );
    const slowSnapshot = await listener((request, index, socket, port) => {
      if (index === 0) socket.write(proved(request, port));
      else drip(socket, `HTTP/1.1 200 OK\r\ncontent-length: 100\r\n\r\n${'x'.repeat(100)}`);
    });
    try {
      const [health, snapshot] = await Promise.all([
        poll(slowHealth.port, TOKEN),
        poll(slowSnapshot.port, TOKEN),
      ]);
      for (const [what, { code, ms }] of [
        ['health', health],
        ['snapshot', snapshot],
      ] as const) {
        assert.equal(code, 'too_slow', what);
        assert.ok(ms >= 9500 && ms < 13_000, `${what} ended after ${ms} ms`);
      }
    } finally {
      await slowHealth.close();
      await slowSnapshot.close();
    }
  });

  test('it goes through no proxy, and finds no one where no one listens', async () => {
    const proxy = await listener((_request, _index, socket) => socket.destroy());
    const server = await startTestServer();
    try {
      const flags = [
        `-DsocksProxyHost=127.0.0.1`,
        `-DsocksProxyPort=${proxy.port}`,
        `-Dhttp.proxyHost=127.0.0.1`,
        `-Dhttp.proxyPort=${proxy.port}`,
      ];
      assert.equal((await poll(server.port, server.token, flags)).code, 'ok');
      assert.equal(proxy.heard.connections.length, 0, 'the proxy was never asked');
    } finally {
      await server.stop();
      await proxy.close();
    }
    const nobody = await listener(() => {});
    const port = nobody.port;
    await nobody.close();
    const refused = await poll(port, TOKEN);
    assert.deepEqual([refused.code, refused.cause], ['unreachable', 'ConnectException']);
    // As adbd does when nothing listens behind its reverse mapping: it accepts, then closes.
    const closing = await listener(() => {});
    closing.server.on('connection', (socket) => socket.end());
    try {
      const closed = await poll(closing.port, TOKEN);
      assert.deepEqual([closed.code, closed.cause], ['unreachable', 'EOFException']);
    } finally {
      await closing.close();
    }
  });

  test('it shows text from outside as LabelText does', () => {
    for (const [input, expected] of TEXT_CASES) {
      assert.equal(run('plain', input), expected, JSON.stringify(input));
    }
    assert.equal(run('cut', 'A long title that goes on', '10'), 'A long ti…');
    assert.equal(run('cut', 'Short', '10'), 'Short');
    assert.equal(run('cut', 'abcdefgh\u{1F600}xyz', '10'), 'abcdefgh…', 'never half a pair');
    assert.equal(
      run('cut', 'ab\u202Ecdefghijkl', '5'),
      'ab…',
      'a code that would pass the limit is left out whole',
    );
    assert.equal(run('cut', 'ab\u202Ecdefghijkl', '12'), 'ab‹U+202E›c…', 'never half a code shown');
    assert.ok(run('cut', '\u200B'.repeat(79), '40').length <= 40, 'at most the limit as shown');
  });
});

describe('the snapshot fields the glance reads', () => {
  type Schema = Record<string, unknown>;
  const defs = (
    JSON.parse(
      readFileSync(join(ROOT, 'packages/contracts/schema/halcyonic-contracts.schema.json'), 'utf8'),
    ) as {
      $defs: Record<string, Schema>;
    }
  ).$defs;
  const resolve = (schema: Schema): Schema =>
    typeof schema.$ref === 'string'
      ? resolve(defs[schema.$ref.replace('#/$defs/', '')] as Schema)
      : schema;
  const property = (owner: string, name: string): Schema => {
    const definition = defs[owner] as { properties: Record<string, Schema>; required: string[] };
    assert.ok(definition.required.includes(name), `${owner}.${name} is required`);
    return resolve(definition.properties[name] as Schema);
  };
  const constants = (schema: Schema): string[] =>
    ((schema.anyOf ?? []) as Schema[]).map((option) => {
      assert.equal(option.type, 'string');
      return option.const as string;
    });

  test('exist, are required and have the types it reads them as', () => {
    for (const [owner, name, item] of [
      ['Snapshot', 'projects', 'ProjectView'],
      ['Snapshot', 'workstreams', 'WorkstreamView'],
    ] as const) {
      const list = property(owner, name);
      assert.equal(list.type, 'array', `${owner}.${name} is a list`);
      assert.equal((list.items as Schema).$ref, `#/$defs/${item}`);
    }
    // Read with getString: each must be a string.
    for (const [owner, name] of [
      ['ProjectView', 'project_id'],
      ['ProjectView', 'name'],
      ['WorkstreamView', 'workstream_id'],
      ['WorkstreamView', 'project_id'],
      ['WorkstreamView', 'title'],
    ] as const) {
      assert.equal(property(owner, name).type, 'string', `${owner}.${name}`);
    }
    assert.equal(property('WorkstreamView', 'attention').type, 'object');
    assert.ok(constants(property('Attention', 'level')).includes('action_required'));
    const statuses = constants(property('WorkstreamView', 'status'));
    for (const status of ['running', 'verifying', 'starting'])
      assert.ok(statuses.includes(status), status);
  });

  test('its text rule has exactly LabelText’s ranges', () => {
    const java = readFileSync(join(SOURCES, 'GlanceText.java'), 'utf8');
    const csharp = readFileSync(
      join(ROOT, 'apps/xr/Packages/com.halcyonic.client/Runtime/LabelText.cs'),
      'utf8',
    );
    const ranges = (source: string, name: string) => {
      const match = new RegExp(`${name}\\s*=\\s*(?:new int\\[\\]\\s*)?\\{([^}]*)\\}`).exec(source);
      assert.ok(match?.[1], name);
      return [...match[1].matchAll(/0x([0-9A-Fa-f]+)/g)].map((hex) =>
        Number.parseInt(hex[1] as string, 16),
      );
    };
    for (const [javaName, csharpName] of [
      ['WHITE_SPACE', 'WhiteSpace'],
      ['SHOWN_BY_CODE', 'ShownByCode'],
      ['PRIVATE_USE', 'PrivateUse'],
    ] as const) {
      const fromJava = ranges(java, javaName);
      assert.ok(fromJava.length > 0);
      assert.deepEqual(fromJava, ranges(csharp, csharpName), javaName);
    }
  });

  test('no source of the glance, nor this test, holds an invisible or reordering character literally', () => {
    // Written literally, such a character can hide or reorder what a reviewer reads; escapes show it.
    const hidden = /[\u00A0\u061C\u200B-\u200F\u2028-\u202E\u2060-\u2069\uFEFF\uE000-\uF8FF]/u;
    const files = [
      ...readdirSync(SOURCES).map((name) => join(SOURCES, name)),
      join(GLANCE, 'test/com/halcyonic/glance/GlanceSelfTest.java'),
      fileURLToPath(import.meta.url),
    ];
    for (const file of files) {
      for (const [index, line] of readFileSync(file, 'utf8').split('\n').entries()) {
        assert.doesNotMatch(line, hidden, `${file}:${index + 1}`);
      }
    }
  });

  test('cover every source file of the glance', () => {
    // A new Java file must be named in one of the lists, so nothing slips past review.
    assert.deepEqual(readdirSync(SOURCES).sort(), [...ANDROID_ONLY, ...PLAIN].sort());
  });
});
