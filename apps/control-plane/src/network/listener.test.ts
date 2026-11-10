import assert from 'node:assert/strict';
import type { NetworkInterfaceInfo } from 'node:os';
import { describe, test } from 'node:test';
import { describeListener, localNameFrom } from './listener.ts';

const PIN = 'a'.repeat(64);

function entry(address: string, family: 'IPv4' | 'IPv6', internal = false): NetworkInterfaceInfo {
  const common = { address, netmask: '', mac: '00:00:00:00:00:00', internal, cidr: null };
  return family === 'IPv4' ? { ...common, family } : { ...common, family, scopeid: 0 };
}

describe('where devices reach the network listener', () => {
  test("the computer's .local name is its Bonjour name in lower case, or none", () => {
    assert.equal(localNameFrom('Studio-Mac\n'), 'studio-mac.local');
    assert.equal(localNameFrom(''), null);
    assert.equal(localNameFrom(null), null);
    assert.equal(localNameFrom('not a name'), null);
    assert.equal(localNameFrom('-leading-hyphen'), null);
  });

  test('a wildcard listener names the computer first, then its addresses as they are now', () => {
    const interfaces = {
      lo0: [entry('127.0.0.1', 'IPv4', true)],
      en0: [entry('192.168.1.23', 'IPv4'), entry('fe80::1', 'IPv6')],
    };
    const now = describeListener(
      { host: '0.0.0.0', port: 47801 },
      47801,
      PIN,
      'studio-mac.local',
      interfaces,
    );
    assert.deepEqual(now.addresses, ['studio-mac.local', '192.168.1.23']);
    const moved = describeListener({ host: '0.0.0.0', port: 47801 }, 47801, PIN, null, {
      en0: [entry('10.0.0.5', 'IPv4')],
    });
    assert.deepEqual(moved.addresses, ['10.0.0.5']);
  });

  test('a listener bound to one address names only that one', () => {
    const one = describeListener(
      { host: '192.168.1.23', port: 47801 },
      47801,
      PIN,
      'studio-mac.local',
      { en0: [entry('192.168.1.23', 'IPv4'), entry('10.0.0.5', 'IPv4')] },
    );
    assert.deepEqual(one, {
      host: '192.168.1.23',
      port: 47801,
      addresses: ['192.168.1.23'],
      certificate_sha256: PIN,
    });
  });
});
