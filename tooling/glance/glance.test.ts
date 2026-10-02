/**
 * The glance (apps/xr/Android/glance) is a second client of the control plane, written in Java for
 * Android. These tests hold its plain-Java parts equal to the originals: its loopback proof to
 * security.ts's, and its text rule to the client core's LabelText (the same cases are checked in C#
 * by GlanceParityTests). They also hold every snapshot field the glance reads to the generated JSON
 * Schema, so a contract change cannot silently break it. They compile the Java with a JDK: Unity's
 * bundled OpenJDK, or `JAVA_HOME`.
 */
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { loopbackProof } from '../../apps/control-plane/src/http/security.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const GLANCE = join(ROOT, 'apps/xr/Android/glance');
const UNITY_JDK =
  '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/OpenJDK/bin';

function jdk(): string | null {
  const candidates = [process.env.JAVA_HOME ? join(process.env.JAVA_HOME, 'bin') : null, UNITY_JDK];
  for (const bin of candidates) {
    if (bin !== null && existsSync(join(bin, 'javac')) && existsSync(join(bin, 'java'))) return bin;
  }
  return null;
}

/** The plain-Java sources: everything in the glance that does not touch Android. */
const PLAIN = ['GlanceProof.java', 'GlanceText.java'];

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

const bin = jdk();
const classes = mkdtempSync(join(tmpdir(), 'halcyonic-glance-'));

function run(...args: string[]): string {
  const [command, ...rest] = args;
  const encoded = rest.map((value) => Buffer.from(value, 'utf16le').toString('base64'));
  const output = execFileSync(
    join(bin as string, 'java'),
    ['-cp', classes, 'com.halcyonic.glance.GlanceSelfTest', command as string, ...encoded],
    {
      encoding: 'utf8',
    },
  );
  return Buffer.from(output.trim(), 'base64').toString('utf8');
}

describe('the glance', {
  skip: bin === null ? 'no JDK: install Unity 6000.3.25f1 with Android, or set JAVA_HOME' : false,
}, () => {
  before(() => {
    const sources = PLAIN.map((name) => join(GLANCE, 'src/com/halcyonic/glance', name));
    sources.push(join(GLANCE, 'test/com/halcyonic/glance/GlanceSelfTest.java'));
    execFileSync(join(bin as string, 'javac'), ['--release', '11', '-d', classes, ...sources]);
  });

  after(() => rmSync(classes, { recursive: true, force: true }));

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

  test('it shows text from outside as LabelText does', () => {
    for (const [input, expected] of TEXT_CASES) {
      assert.equal(run('plain', input), expected, JSON.stringify(input));
    }
    assert.equal(run('cut', 'A long title that goes on', '10'), 'A long ti…');
    assert.equal(run('cut', 'Short', '10'), 'Short');
    assert.equal(
      run('cut', 'abcdefgh\u{1F600}xyz', '10'),
      'abcdefgh\u{1F600}…',
      'counts code points',
    );
    assert.equal(run('cut', 'ab\u202Ecdefghijkl', '5'), 'ab‹U+202E›c…', 'never half a code shown');
  });
});

describe('the snapshot fields the glance reads', () => {
  const schema = JSON.parse(
    readFileSync(join(ROOT, 'packages/contracts/schema/halcyonic-contracts.schema.json'), 'utf8'),
  ) as {
    $defs: Record<
      string,
      { properties: Record<string, Record<string, unknown>>; required: string[] }
    >;
  };
  const defs = schema.$defs;
  const reference = (property: Record<string, unknown>): string | undefined =>
    typeof property.$ref === 'string' ? property.$ref.replace('#/$defs/', '') : undefined;

  test('exist, are required and have the types it reads them as', () => {
    const snapshot = defs.Snapshot;
    assert.ok(snapshot?.required.includes('projects') && snapshot.required.includes('workstreams'));
    const items = (name: string) =>
      reference((snapshot?.properties[name]?.items ?? {}) as Record<string, unknown>);
    assert.equal(items('projects'), 'ProjectView');
    assert.equal(items('workstreams'), 'WorkstreamView');
    const project = defs.ProjectView;
    for (const field of ['project_id', 'name']) assert.ok(project?.required.includes(field), field);
    const workstream = defs.WorkstreamView;
    for (const field of ['workstream_id', 'project_id', 'title', 'status', 'attention']) {
      assert.ok(workstream?.required.includes(field), field);
    }
    assert.equal(reference(workstream?.properties.attention ?? {}), 'Attention');
    assert.ok(defs.Attention?.required.includes('level'));
    const text = JSON.stringify(schema);
    for (const literal of ['action_required', 'running', 'verifying', 'starting']) {
      assert.ok(text.includes(`"const":"${literal}"`), literal);
    }
  });

  test('its text rule has exactly LabelText’s ranges', () => {
    const java = readFileSync(join(GLANCE, 'src/com/halcyonic/glance/GlanceText.java'), 'utf8');
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

  test('cover every source file of the glance', () => {
    // A new Java file must be named here or in the Android-only list, so nothing slips past review.
    const androidOnly = ['GlanceActivity.java', 'GlanceClient.java'];
    const files = readdirSync(join(GLANCE, 'src/com/halcyonic/glance')).sort();
    assert.deepEqual(files, [...androidOnly, ...PLAIN].sort());
  });
});
