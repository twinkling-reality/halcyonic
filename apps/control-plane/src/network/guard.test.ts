import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { checkNetworkRequest, type NetworkRequestFacts } from './guard.ts';
import { WindowCounter } from './limits.ts';

const facts: NetworkRequestFacts = {
  host: '192.168.1.23:47801',
  origin: undefined,
  secFetchSite: undefined,
  localPort: 47801,
};
const codeOf = (decision: ReturnType<typeof checkNetworkRequest>) =>
  decision.allowed ? null : decision.code;

describe('the network listener guard', () => {
  test('accepts an IP address or a .local name with the listener port as Host', () => {
    for (const host of [
      '192.168.1.23:47801',
      '10.0.0.5:47801',
      '[fe80::1c2d:3e4f:5a6b:7c8d]:47801',
      'Glendons-MacBook-Pro.local:47801',
      'mac.lan.local:47801',
    ]) {
      assert.equal(codeOf(checkNetworkRequest({ ...facts, host })), null, host);
    }
  });

  test('refuses any other Host, which is how DNS rebinding arrives', () => {
    for (const host of [
      undefined,
      'evil.example:47801',
      'mac.local.evil.example:47801',
      '192.168.1.23:47800',
      '192.168.1.23',
      '[not-an-address]:47801',
      '192.168.1.300:47801',
      '-mac.local:47801',
      '.local:47801',
    ]) {
      assert.equal(codeOf(checkNetworkRequest({ ...facts, host })), 'host_not_allowed', host);
    }
  });

  test('refuses browsers as loopback does', () => {
    assert.equal(
      codeOf(checkNetworkRequest({ ...facts, origin: 'https://evil.example' })),
      'origin_not_allowed',
    );
    assert.equal(codeOf(checkNetworkRequest({ ...facts, origin: 'null' })), 'origin_not_allowed');
    assert.equal(
      codeOf(checkNetworkRequest({ ...facts, secFetchSite: 'cross-site' })),
      'cross_site_request',
    );
  });
});

describe('the per-address window counter', () => {
  test('allows the limit in a window, then refuses until the window ends', async () => {
    const time = createVirtualTime(new Date('2026-09-29T12:00:00.000Z'));
    const counter = new WindowCounter(time, 2, 60_000);
    assert.equal(counter.take('a'), true);
    assert.equal(counter.exhausted('a'), false);
    assert.equal(counter.take('a'), true);
    assert.equal(counter.exhausted('a'), true);
    assert.equal(counter.take('a'), false);
    assert.equal(counter.take('b'), true, 'each address has a window of its own');
    await time.advance(60_000);
    assert.equal(counter.exhausted('a'), false);
    assert.equal(counter.take('a'), true);
  });
});
