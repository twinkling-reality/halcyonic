import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { gatedArguments } from './codex-runtime.ts';
import { CODEX_KEY_READER, gatedCredentialVariable, gatedEntriesProblem } from './key-reader.ts';

describe('Codex on a hosted provider through the key gate (VRIFY, 2026-11-13)', () => {
  test('reads every declared provider serving Responses, at start, and never turns Codex on', () => {
    assert.deepEqual(CODEX_KEY_READER.needs, ['responses']);
    assert.equal(CODEX_KEY_READER.providers, 'all');
    assert.equal(CODEX_KEY_READER.turnsOn, false);
    assert.equal(gatedCredentialVariable('meta'), 'HALCYONIC_GATE_META_TOKEN');
    assert.equal(gatedCredentialVariable('my-host'), 'HALCYONIC_GATE_MY_HOST_TOKEN');
  });

  test("the server's overrides name each gate, the credential's variable and the Responses wire, never a credential, the credential's value never", () => {
    const args = gatedArguments([
      {
        codexId: 'halcyonic_meta',
        name: 'Meta',
        url: 'http://127.0.0.1:5555',
        variable: 'HALCYONIC_GATE_META_TOKEN',
      },
    ]);
    assert.deepEqual(args, [
      '-c',
      'model_providers.halcyonic_meta.name="Meta"',
      '-c',
      'model_providers.halcyonic_meta.base_url="http://127.0.0.1:5555"',
      '-c',
      'model_providers.halcyonic_meta.env_key="HALCYONIC_GATE_META_TOKEN"',
      '-c',
      'model_providers.halcyonic_meta.wire_api="responses"',
    ]);
    assert.ok(!args.some((arg) => arg.includes('halcyonic-gate-run-')));
    assert.deepEqual(gatedArguments([]), []);
  });

  test("Codex's configuration must hold the gated providers exactly as passed (AUDIT3's read, A)", () => {
    const gated = new Map([['halcyonic_meta', 'http://127.0.0.1:5555']]);
    const entry = {
      name: 'Meta',
      base_url: 'http://127.0.0.1:5555',
      env_key: 'HALCYONIC_GATE_META_TOKEN',
      wire_api: 'responses',
    };
    const with_ = (providers: Record<string, unknown>) => ({ model_providers: providers });
    assert.equal(gatedEntriesProblem(with_({ halcyonic_meta: entry }), gated), null);
    assert.equal(gatedEntriesProblem({}, new Map()), null);
    // Each way it can be other than passed, refused by name.
    const refused: [Record<string, unknown>, RegExp][] = [
      [
        { halcyonic_meta: entry, halcyonic_other: entry },
        /halcyonic_other is not one Halcyonic made/,
      ],
      [
        { halcyonic_meta: { ...entry, base_url: 'https://elsewhere.example' } },
        /does not point at its key gate/,
      ],
      [{ halcyonic_meta: { ...entry, env_key: 'OTHER_KEY' } }, /names another credential/],
      [{ halcyonic_meta: { ...entry, wire_api: 'chat' } }, /does not speak Responses/],
      [{ halcyonic_meta: { ...entry, http_headers: { 'x-a': 'b' } } }, /adds http_headers/],
      [{ halcyonic_meta: { ...entry, env_http_headers: { 'x-a': 'B' } } }, /adds env_http_headers/],
      [{ halcyonic_meta: { ...entry, query_params: { k: 'v' } } }, /adds query_params/],
      [
        { halcyonic_meta: { ...entry, experimental_bearer_token: 't' } },
        /adds experimental_bearer_token/,
      ],
      [
        { halcyonic_meta: entry, local: { env_key: 'HALCYONIC_GATE_META_TOKEN' } },
        /local names HALCYONIC_GATE_META_TOKEN/,
      ],
      [{}, /did not apply the provider halcyonic_meta/],
    ];
    for (const [providers, words] of refused)
      assert.match(gatedEntriesProblem(with_(providers), gated) ?? 'none', words);
    // Empty adding fields are as none.
    assert.equal(
      gatedEntriesProblem(with_({ halcyonic_meta: { ...entry, http_headers: {} } }), gated),
      null,
    );
    // An open server, with no gates, refuses any halcyonic_ entry in the home's configuration.
    assert.match(
      gatedEntriesProblem(with_({ halcyonic_meta: entry }), new Map()) ?? 'none',
      /not one Halcyonic made/,
    );
  });
});
