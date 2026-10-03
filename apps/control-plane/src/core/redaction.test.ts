import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  fits,
  fitted,
  JOURNALED_TEXT,
  looksLikeCredential,
  REDACTED,
  redaction,
  redactSecrets,
  TRUNCATED,
  withinLimit,
} from './redaction.ts';

describe('error text before it is journaled', () => {
  test("a gateway's 401 that echoes the key loses the key and keeps the rest", () => {
    const text =
      'unexpected status 401 Unauthorized: {"error":{"message":"Incorrect API key provided: sk-proj-AbCdEf0123456789xyzQRS. You can find your API key at https://platform.openai.com/account/api-keys."}}';
    const out = redactSecrets(text, []);
    assert.ok(!out.includes('sk-proj'), out);
    assert.ok(
      out.includes(
        `Incorrect API key provided: ${REDACTED}. You can find your API key at https://platform.openai.com/account/api-keys.`,
      ),
      out,
    );
  });

  test('a validation error that echoes an instruction keeps it word for word', () => {
    const text =
      'Invalid request: "text" was "Add a login page to the settings screen, and test it on 2 devices"';
    assert.equal(redactSecrets(text, [{ what: 'GATEWAY_KEY', value: 'a-secret-value' }]), text);
  });

  test('every value Halcyonic holds or passes to a runtime goes, wherever it appears, named', () => {
    const agentValue = 'my-gateway-secret-42';
    const token = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_-Z';
    const out = redactSecrets(
      `proxy said ${agentValue}; header was ${token}; again ${agentValue}`,
      [
        { what: 'access token', value: token },
        { what: 'GATEWAY_KEY', value: agentValue },
        { what: 'SHORT', value: 'short' },
        // The same value under a second name reads as the first.
        { what: 'OTHER_KEY', value: agentValue },
      ],
    );
    assert.equal(
      out,
      'proxy said [redacted: GATEWAY_KEY]; header was [redacted: access token]; again [redacted: GATEWAY_KEY]',
    );
    assert.equal(
      redactSecrets('a short word stays', [{ what: 'SHORT', value: 'short' }]),
      'a short word stays',
      'too short to be replaced everywhere',
    );
  });

  test('credential shapes go, ids and hashes stay', () => {
    const cases: [string, string][] = [
      ['Authorization: Bearer abc.DEF-123_xyz', `Authorization: Bearer ${REDACTED}`],
      ['Basic dXNlcjpwYXNzd29yZA==', `Basic ${REDACTED}`],
      [
        'GET https://user:hunter22@gateway.example/v1 failed',
        `GET https://${REDACTED}@gateway.example/v1 failed`,
      ],
      ['key AKIAIOSFODNN7EXAMPLE is not valid', `key ${REDACTED} is not valid`],
      ['token ghp_0123456789abcdefghijABCDEFGHIJ was revoked', `token ${REDACTED} was revoked`],
      ['xoxb-1234567890-abcdefghij leaked', `${REDACTED} leaked`],
      ['glpat-AbCdEfGhIjKlMnOpQrSt was refused', `${REDACTED} was refused`],
      ['credential srkx_9f8e7d6c5b4a was refused', `credential ${REDACTED} was refused`],
      ['device hlcd_AbCdEfGh0123456789IjKl is unknown', `device ${REDACTED} is unknown`],
      ['jwt eyJhbGciOiJIUzI1.eyJzdWIiOiIxMjM0.SflKxwRJSMeKKF2QT4fwpM ok', `jwt ${REDACTED} ok`],
      ['random Zx9Yw8Vu7Ts6Rq5Po4Nm3Lk2Ji1Hg0Fe9Dc8 end', `random ${REDACTED} end`],
    ];
    for (const [text, expected] of cases) assert.equal(redactSecrets(text, []), expected, text);
    const kept = [
      'execution 0199a6b2-7c1d-7e3f-8a9b-0c1d2e3f4a5b failed',
      'sha256 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08',
      'the server answered 503 for /api/info',
      'Bearer token missing',
      'Basic authentication failed for the gateway',
    ];
    for (const text of kept) assert.equal(redactSecrets(text, []), text, text);
  });

  test('names built of words stay, however long, and random tokens of the same length go', () => {
    const names = [
      'SignInRateLimit2FactorAuthHandlerV2',
      'RejectedExecutionExceptionHandler2024',
      'feature-AddLoginPage-2026-10-03-final',
      'MyComponentTest01UserProfileSettingsPage',
      'HttpClientHandlerTests_SendAsync_Returns200',
      'test_upgrade_on_the_proven_connection_v2_ok',
    ];
    for (const name of names) {
      const text = `branch ${name} failed to build`;
      assert.equal(redactSecrets(text, []), text, name);
    }
    const tokens = [
      'q7Xk2pLm9vRt4wZb8nHc3jYd6fGs1aKe5uNo0iVx',
      'Ab3dE7gH1jK5mN9pQ2sT6vW0yZ4bC8eF',
      'x9Q_2mZ-7Lk4Pw8Rt1Vn5Hj3Bc6Gf0Ds-eYa',
    ];
    for (const token of tokens) {
      assert.equal(redactSecrets(`key ${token} refused`, []), `key ${REDACTED} refused`, token);
    }
  });

  test("a URL's user and password go up to the last @ before its host, and nothing past it", () => {
    const cases: [string, string][] = [
      // A raw @ in a password Halcyonic doesn't hold.
      [
        'GET https://deploy:p@ss-W0rd@gateway.example/v1 failed',
        `GET https://${REDACTED}@gateway.example/v1 failed`,
      ],
      // Two URLs in one message: each loses only its own.
      [
        'tried https://a:pw1@one.example/x and ftp://b:p@w2@two.example then gave up',
        `tried https://${REDACTED}@one.example/x and ftp://${REDACTED}@two.example then gave up`,
      ],
      // An email address after the URL keeps its @, with or without a space between.
      [
        'https://u:pw@host.example failed; write to admin@example.com',
        `https://${REDACTED}@host.example failed; write to admin@example.com`,
      ],
      [
        'mirrors: https://u:pw@host.example, admin@example.com',
        `mirrors: https://${REDACTED}@host.example, admin@example.com`,
      ],
      // Glued by a comma alone, the host goes with it: the smaller harm than sparing a password.
      [
        'mirrors: https://u:pw@host.example,admin@example.com',
        `mirrors: https://${REDACTED}@example.com`,
      ],
      // A comma or an apostrophe in a password, as RFC 3986 allows.
      [
        'https://u:pa,ss12345@db.example.test refused',
        `https://${REDACTED}@db.example.test refused`,
      ],
      [
        "https://u:pa'ss12345@db.example.test refused",
        `https://${REDACTED}@db.example.test refused`,
      ],
    ];
    for (const [text, expected] of cases) assert.equal(redactSecrets(text, []), expected, text);
    // An @ in a path is no user's.
    const scoped = 'npm could not fetch https://registry.example/@scope/pkg';
    assert.equal(redactSecrets(scoped, []), scoped);
  });

  test('a scheme word before a path keeps the path', () => {
    const kept = [
      'could not read token /Users/me/.config/gh/hosts.yml',
      'Token ~/.netrc2 is not readable',
      'bearer ./secrets/key1.txt not found',
      'invalid token src/config/settings.json',
    ];
    for (const text of kept) assert.equal(redactSecrets(text, []), text, text);
    assert.equal(redactSecrets('token abc123def456', []), `token ${REDACTED}`);
    // A full stop that ends a sentence is no path's, and stays outside what is replaced.
    assert.equal(redactSecrets('Basic dXNlcjpwYXNz/d29yZA==.', []), `Basic ${REDACTED}.`);
    assert.equal(
      redactSecrets('Bearer ab/cd+ef12GH34ij56==. Next', []),
      `Bearer ${REDACTED}. Next`,
    );
    assert.equal(redactSecrets('Bearer abc.DEF-123_xyz.', []), `Bearer ${REDACTED}.`);
    // Padding or a `+` is base64's, never a path's, whatever follows.
    assert.equal(
      redactSecrets('Basic dXNlcjpwYXNz/d29yZA==.Retry later', []),
      `Basic ${REDACTED}.Retry later`,
    );
    assert.equal(
      redactSecrets('Token abc123.def/GHI456+jkl== was refused', []),
      `Token ${REDACTED} was refused`,
    );
    // A name before the credential, as Rails' `Token token=…` has, stays; the credential goes.
    assert.equal(
      redactSecrets('Authorization: Token token=abc123def456ghi789jkl', []),
      `Authorization: Token token=${REDACTED}`,
    );
    assert.equal(
      redactSecrets('Authorization: Token token="abc123def456ghi789jkl", nonce="x"', []),
      `Authorization: Token token="${REDACTED}", nonce="x"`,
    );
    assert.equal(
      redactSecrets("Authorization: Token token='abc123def456ghi789jkl'", []),
      `Authorization: Token token='${REDACTED}'`,
    );
    // A credential with = inside it is no parameter's name, and goes whole.
    assert.equal(
      redactSecrets('Bearer abcdefgh=ijklmnopqrstuv123 refused', []),
      `Bearer ${REDACTED} refused`,
    );
    // Base64 has no `.` and a JSON Web Token no `/`, so either alone is still a credential.
    assert.equal(redactSecrets('Bearer ab/cd+ef12==', []), `Bearer ${REDACTED}`);
    assert.equal(redactSecrets('Bearer abc.DEF-123_xyz', []), `Bearer ${REDACTED}`);
  });

  test('reads only the first 4096 characters, so a runtime line of any length takes no time', () => {
    // A long run joined by - . +, which a scheme pattern could restart at every boundary.
    const line = 'a-'.repeat(100_000);
    const started = performance.now();
    const out = redactSecrets(line, []);
    assert.ok(performance.now() - started < 500, 'linear, and only 4096 characters read');
    assert.equal(out.length, 4096);
  });

  test('a text is cut to the journal limit after redaction, never splitting a pair', () => {
    assert.equal(withinLimit('short'), 'short');
    const long = withinLimit('x'.repeat(5000));
    assert.equal(long.length, JOURNALED_TEXT);
    assert.ok(long.endsWith('…'));
    const pair = withinLimit(`${'x'.repeat(JOURNALED_TEXT - 2)}😀${'y'.repeat(10)}`);
    assert.ok(
      Array.from(pair).length <= JOURNALED_TEXT && !/[\uD800-\uDBFF]…$/.test(pair),
      'no half of a pair before the ellipsis',
    );
  });

  test('length is measured and cut in code points, as the contract counts it', () => {
    // 2000 code points in 4000 UTF-16 units: it fits, so it is not cut.
    const emoji = '😀'.repeat(JOURNALED_TEXT);
    assert.equal(withinLimit(emoji), emoji);
    assert.equal(fitted(emoji, JOURNALED_TEXT), emoji);
    const over = `${emoji}x`;
    assert.equal(withinLimit(over), `${'😀'.repeat(JOURNALED_TEXT - 1)}…`);
    assert.equal(
      fitted(over, JOURNALED_TEXT),
      `${'😀'.repeat(JOURNALED_TEXT - TRUNCATED.length)}${TRUNCATED}`,
    );
    assert.ok(fits(emoji, JOURNALED_TEXT) && !fits(over, JOURNALED_TEXT));
    // What is read is never cut inside a pair either.
    const read = redactSecrets(`${'x'.repeat(4095)}😀`, []);
    assert.ok(!/[\uD800-\uDBFF]$/.test(read), 'no half of a pair at the end of what is read');
  });

  test('a value reads as a credential by itself when it is random, hex or a known key shape', () => {
    const credentials = [
      'q7Xk2pLm9vRt4wZb8nHc',
      'sk-ant-api03-AbCdEf0123456789',
      '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b',
      'glpat-AbCdEfGhIjKlMnOpQrSt',
    ];
    for (const value of credentials) assert.ok(looksLikeCredential(value), value);
    const plain = [
      'https://gateway.example/v1',
      'ap-southeast-1',
      'claude-sonnet-4-5-20250929',
      '/Users/someone/.ssh/agent.sock',
      'true',
      'GatewayForTheTeamInSingapore2',
    ];
    for (const value of plain) assert.ok(!looksLikeCredential(value), value);
  });

  test('text a person reads as given loses only what Halcyonic holds; error text loses shapes too', () => {
    const { held, errorText } = redaction(() => [
      { what: 'Anthropic key', value: 'held-value-123' },
    ]);
    const text = 'curl -H "x-api-key: held-value-123" -H "Authorization: Bearer abc123def456"';
    assert.equal(
      held(text),
      'curl -H "x-api-key: [redacted: Anthropic key]" -H "Authorization: Bearer abc123def456"',
    );
    assert.equal(
      errorText(text),
      `curl -H "x-api-key: [redacted: Anthropic key]" -H "Authorization: Bearer ${REDACTED}"`,
    );
  });
});
