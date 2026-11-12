import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { RuntimeActionError } from '@halcyonic/runtime-core';
import {
  credentialSettings,
  isSalidiumHook,
  isSeorakHook,
  observationHooks,
  personalConfigFolder,
  personalSettings,
  refuseOtherCredentials,
} from './personal-settings.ts';

const ROOT = mkdtempSync(join(tmpdir(), 'halcyonic-personal-settings-'));
after(() => rmSync(ROOT, { recursive: true, force: true }));

const seorak = (script: string) =>
  `'/usr/local/bin/node' '/Users/p/.seorak/runtime/1/dist/${script}' --seorak-hook-path=${Buffer.from(`file:///Users/p/.seorak/runtime/1/dist/${script}`).toString('base64url')}`;
const salidium = (event: string) =>
  `SALIDIUM_HOOK=1 '/Users/p/.salidium/hooks/relay.sh' claude-code ${event} retain`;

describe("the person's own Claude Code set-up, as an unconfined run takes it (VRIFY, 2026-11-12)", () => {
  test("Salidium's and Seorak's hooks are recognised by each project's own rule, and near misses are not", () => {
    assert.equal(isSalidiumHook(salidium('Stop')), true);
    assert.equal(isSalidiumHook("SALIDIUM_HOOK=1 '/x/hooks/relay.sh'"), true);
    assert.equal(isSalidiumHook("'/x/hooks/relay.sh' claude-code Stop"), false);
    assert.equal(isSalidiumHook("SALIDIUM_HOOK=1 '/x/hooks/relay.sh.evil' Stop"), false);
    assert.equal(isSalidiumHook('SALIDIUM_HOOK=1 /x/hooks/relay.sh Stop'), false);
    assert.equal(isSeorakHook(seorak('hook-user-prompt.mjs'), 'UserPromptSubmit'), true);
    assert.equal(isSeorakHook(seorak('hook-turn-end.mjs'), 'StopFailure'), true);
    // Another event's script, an event Seorak does not observe, no marker, a marker that is no file URL.
    assert.equal(isSeorakHook(seorak('hook-user-prompt.mjs'), 'Stop'), false);
    assert.equal(isSeorakHook(seorak('hook-tool-use.mjs'), 'PreToolUse'), false);
    assert.equal(
      isSeorakHook("'/usr/local/bin/node' '/x/hook-user-prompt.mjs'", 'UserPromptSubmit'),
      false,
    );
    assert.equal(
      isSeorakHook(
        `node x --seorak-hook-path=${Buffer.from('relative/hook-user-prompt.mjs').toString('base64url')}`,
        'UserPromptSubmit',
      ),
      false,
    );
  });

  test("only the observers' hooks pass, each with its four fields, and nothing else of the settings", () => {
    const hooks = observationHooks({
      env: { ANTHROPIC_AUTH_TOKEN: 'fake' },
      apiKeyHelper: '/bin/echo fake',
      permissions: { allow: ['Bash(*)'] },
      hooks: {
        UserPromptSubmit: [
          {
            hooks: [
              {
                type: 'command',
                command: salidium('UserPromptSubmit'),
                async: true,
                timeout: 5,
                extra: 1,
              },
              { type: 'command', command: '/usr/bin/say hello' },
            ],
          },
          { hooks: [{ type: 'command', command: seorak('hook-user-prompt.mjs') }] },
        ],
        // A matcher names tools: neither project writes one.
        PreToolUse: [
          { matcher: 'Bash', hooks: [{ type: 'command', command: salidium('PreToolUse') }] },
        ],
        Stop: [{ hooks: [{ type: 'command', command: '/usr/bin/true' }] }],
      },
    });
    assert.deepEqual(hooks, {
      UserPromptSubmit: [
        {
          hooks: [
            { type: 'command', command: salidium('UserPromptSubmit'), async: true, timeout: 5 },
          ],
        },
        { hooks: [{ type: 'command', command: seorak('hook-user-prompt.mjs') }] },
      ],
    });
    assert.deepEqual(observationHooks(null), {});
    assert.deepEqual(observationHooks({ hooks: [] }), {});
  });

  test('the settings and CLAUDE.md are read where Claude Code reads them; a file that is not JSON refuses the run', () => {
    assert.equal(personalConfigFolder({ HOME: '/h', CLAUDE_CONFIG_DIR: '/c' }), '/c');
    assert.equal(personalConfigFolder({ HOME: '/h' }), '/h/.claude');
    const config = join(ROOT, 'config');
    mkdirSync(config);
    assert.deepEqual(personalSettings({ HOME: ROOT, CLAUDE_CONFIG_DIR: config }), {
      hooks: {},
      instructions: null,
    });
    writeFileSync(
      join(config, 'settings.json'),
      JSON.stringify({
        hooks: { Stop: [{ hooks: [{ type: 'command', command: salidium('Stop') }] }] },
      }),
    );
    writeFileSync(join(config, 'CLAUDE.md'), 'Write tests first.\n');
    assert.deepEqual(personalSettings({ HOME: ROOT, CLAUDE_CONFIG_DIR: config }), {
      hooks: { Stop: [{ hooks: [{ type: 'command', command: salidium('Stop') }] }] },
      instructions: 'Write tests first.\n',
    });
    writeFileSync(join(config, 'settings.json'), '{ not json');
    assert.throws(
      () => personalSettings({ HOME: ROOT, CLAUDE_CONFIG_DIR: config }),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'runtime_start_failed' &&
        /is not JSON/.test(error.message),
    );
  });

  test('settings left that name another credential refuse the run, by name and never by value', () => {
    assert.deepEqual(credentialSettings({}), []);
    assert.deepEqual(credentialSettings({ env: { HTTPS_PROXY: 'x', NODE_OPTIONS: 'y' } }), []);
    assert.deepEqual(
      credentialSettings({
        env: {
          ANTHROPIC_AUTH_TOKEN: 'fake-secret-value',
          CLAUDE_CODE_USE_BEDROCK: '1',
          AWS_PROFILE: 'p',
          CLAUDE_CODE_OAUTH_TOKEN: 't',
          GOOGLE_APPLICATION_CREDENTIALS: '/g',
        },
        apiKeyHelper: '/bin/echo k',
        awsAuthRefresh: 'aws sso login',
      }),
      [
        'ANTHROPIC_AUTH_TOKEN',
        'AWS_PROFILE',
        'CLAUDE_CODE_OAUTH_TOKEN',
        'CLAUDE_CODE_USE_BEDROCK',
        'GOOGLE_APPLICATION_CREDENTIALS',
        'apiKeyHelper',
        'awsAuthRefresh',
      ],
    );
    assert.doesNotThrow(() => refuseOtherCredentials({ model: 'x' }));
    assert.throws(
      () => refuseOtherCredentials({ env: { ANTHROPIC_AUTH_TOKEN: 'fake-secret-value' } }),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        /ANTHROPIC_AUTH_TOKEN/.test(error.message) &&
        !error.message.includes('fake-secret-value'),
    );
  });
});
