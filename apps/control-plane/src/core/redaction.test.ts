import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { JOURNALED_TEXT, REDACTED, redactSecrets, withinLimit } from './redaction.ts';

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
    assert.equal(redactSecrets(text, ['a-secret-value']), text);
  });

  test('every value Halcyonic holds or passes to a runtime goes, wherever it appears', () => {
    const agentValue = 'my-gateway-secret-42';
    const token = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_-Z';
    const out = redactSecrets(
      `proxy said ${agentValue}; header was ${token}; again ${agentValue}`,
      [agentValue, token, 'short'],
    );
    assert.equal(out, `proxy said ${REDACTED}; header was ${REDACTED}; again ${REDACTED}`);
    assert.equal(
      redactSecrets('a short word stays', ['short']),
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

  test('a scheme word before a path keeps the path', () => {
    const kept = [
      'could not read token /Users/me/.config/gh/hosts.yml',
      'Token ~/.netrc2 is not readable',
      'bearer ./secrets/key1.txt not found',
      'invalid token src/config/settings.json',
    ];
    for (const text of kept) assert.equal(redactSecrets(text, []), text, text);
    assert.equal(redactSecrets('token abc123def456', []), `token ${REDACTED}`);
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
      pair.length <= JOURNALED_TEXT && !/[\uD800-\uDBFF]…$/.test(pair),
      'no half of a pair before the ellipsis',
    );
  });
});
