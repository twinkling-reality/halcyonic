import assert from 'node:assert/strict';
import type { ChildProcess } from 'node:child_process';
import { EventEmitter } from 'node:events';
import { describe, test } from 'node:test';
import { inspect } from 'node:util';
import { OpenCodeClient } from './client.ts';
import { OpenCodeServer } from './server.ts';

describe("an OpenCode server's password", () => {
  test('is read only through its accessor, never with the object logged or copied', () => {
    const password = 'opencode-server-password-1';
    // A stand-in process that never exits, so nothing is signalled.
    const child = new EventEmitter() as unknown as ChildProcess;
    const server = new OpenCodeServer(
      child,
      2_147_483_000,
      new OpenCodeClient('http://127.0.0.1:4096', password),
      '/nonexistent/record.json',
      password,
    );
    assert.equal(server.secret, password);
    assert.ok(!Object.keys(server).includes('secret'));
    assert.ok(!JSON.stringify(server).includes(password));
    assert.ok(!inspect(server, { depth: 5 }).includes(password));
    assert.ok(!('secret' in { ...server }));
  });
});
