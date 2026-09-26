import assert from 'node:assert/strict';
import { accessSync, constants } from 'node:fs';
import { delimiter, dirname, join } from 'node:path';
import { describe, test } from 'node:test';
import { buildEnvironment, EnvironmentError, INHERITED_VARIABLES } from './environment.ts';

const NODE_DIRECTORY = '/opt/node/bin';

const BASE = {
  PATH: '/usr/bin:/bin',
  HOME: '/home/tester',
  USER: 'tester',
  ANTHROPIC_API_KEY: 'test-key-not-real',
};

describe('launched agent environment', () => {
  test('only allowlisted variables are inherited', () => {
    const environment = buildEnvironment(
      {
        ...BASE,
        LANG: 'en_US.UTF-8',
        CLAUDE_CONFIG_DIR: '/home/tester/.claude-work',
        AWS_REGION: 'us-east-1',
        GITHUB_TOKEN: 'not for agents',
        NODE_OPTIONS: '--inspect',
        CLAUDE_CODE_OAUTH_TOKEN: 'a claude.ai login',
        ANTHROPIC_AUTH_TOKEN: 'a gateway token',
      },
      {},
      NODE_DIRECTORY,
    );
    assert.deepEqual(environment, {
      PATH: `/usr/bin:/bin${delimiter}${NODE_DIRECTORY}`,
      HOME: '/home/tester',
      USER: 'tester',
      LANG: 'en_US.UTF-8',
      CLAUDE_CONFIG_DIR: '/home/tester/.claude-work',
      ANTHROPIC_API_KEY: 'test-key-not-real',
      AWS_REGION: 'us-east-1',
    });
  });

  test('SALIDIUM_INTERNAL never reaches an agent, whether inherited or configured', () => {
    const environment = buildEnvironment({ ...BASE, SALIDIUM_INTERNAL: '1' }, {}, NODE_DIRECTORY);
    assert.equal(Object.hasOwn(environment, 'SALIDIUM_INTERNAL'), false);
    assert.equal(INHERITED_VARIABLES.includes('SALIDIUM_INTERNAL'), false);
    assert.throws(
      () => buildEnvironment(BASE, { SALIDIUM_INTERNAL: '1' }, NODE_DIRECTORY),
      EnvironmentError,
    );
  });

  test('configured additions are added, and may not change HOME or CLAUDE_CONFIG_DIR or add a claude.ai login', () => {
    const environment = buildEnvironment(
      BASE,
      { LAUNCHER_LABEL: 'halcyonic', ANTHROPIC_BASE_URL: 'https://gateway.internal' },
      NODE_DIRECTORY,
    );
    assert.equal(environment.LAUNCHER_LABEL, 'halcyonic');
    assert.equal(environment.ANTHROPIC_BASE_URL, 'https://gateway.internal');
    for (const name of ['HOME', 'CLAUDE_CONFIG_DIR', 'CLAUDE_CODE_OAUTH_TOKEN']) {
      assert.throws(
        () => buildEnvironment(BASE, { [name]: '/somewhere/else' }, NODE_DIRECTORY),
        EnvironmentError,
        name,
      );
    }
  });

  test('HOME is passed through unchanged and is required', () => {
    assert.equal(buildEnvironment(BASE, {}, NODE_DIRECTORY).HOME, '/home/tester');
    const { HOME: _home, ...withoutHome } = BASE;
    assert.throws(() => buildEnvironment(withoutHome, {}, NODE_DIRECTORY), EnvironmentError);
  });

  test('PATH keeps its order and gains the directory of the running node when it lacks it', () => {
    assert.equal(
      buildEnvironment(BASE, {}, NODE_DIRECTORY).PATH,
      ['/usr/bin', '/bin', NODE_DIRECTORY].join(delimiter),
    );
    const withNode = { ...BASE, PATH: [NODE_DIRECTORY, '/usr/bin'].join(delimiter) };
    assert.equal(buildEnvironment(withNode, {}, NODE_DIRECTORY).PATH, withNode.PATH);
    const { PATH: _path, ...withoutPath } = BASE;
    assert.equal(buildEnvironment(withoutPath, {}, NODE_DIRECTORY).PATH, NODE_DIRECTORY);
  });

  test('by default PATH can find node, which the observers hooks run with', () => {
    const { PATH: _path, ...withoutPath } = BASE;
    const environment = buildEnvironment({ ...withoutPath, PATH: '/nonexistent' }, {});
    const found = (environment.PATH ?? '').split(delimiter).some((directory) => {
      try {
        accessSync(join(directory, 'node'), constants.X_OK);
        return true;
      } catch {
        return false;
      }
    });
    assert.ok(found, environment.PATH);
    assert.ok((environment.PATH ?? '').split(delimiter).includes(dirname(process.execPath)));
  });

  test('an API key or a cloud provider is required, and claude.ai login is not accepted', () => {
    const { ANTHROPIC_API_KEY: _key, ...withoutKey } = BASE;
    assert.throws(() => buildEnvironment(withoutKey, {}, NODE_DIRECTORY), EnvironmentError);
    assert.throws(
      () => buildEnvironment({ ...withoutKey, ANTHROPIC_API_KEY: '  ' }, {}, NODE_DIRECTORY),
      EnvironmentError,
    );
    assert.throws(
      () =>
        buildEnvironment({ ...withoutKey, CLAUDE_CODE_OAUTH_TOKEN: 'a login' }, {}, NODE_DIRECTORY),
      EnvironmentError,
    );
    assert.throws(
      () => buildEnvironment({ ...withoutKey, CLAUDE_CODE_USE_BEDROCK: '0' }, {}, NODE_DIRECTORY),
      EnvironmentError,
    );
    for (const provider of [
      'CLAUDE_CODE_USE_BEDROCK',
      'CLAUDE_CODE_USE_ANTHROPIC_AWS',
      'CLAUDE_CODE_USE_VERTEX',
      'CLAUDE_CODE_USE_FOUNDRY',
    ]) {
      const environment = buildEnvironment(
        { ...withoutKey, [provider]: 'true' },
        {},
        NODE_DIRECTORY,
      );
      assert.equal(environment[provider], 'true');
    }
    assert.equal(
      buildEnvironment(withoutKey, { ANTHROPIC_API_KEY: 'test-key-not-real' }, NODE_DIRECTORY)
        .ANTHROPIC_API_KEY,
      'test-key-not-real',
    );
  });

  test('errors name variables but never their values', () => {
    try {
      buildEnvironment(BASE, { CLAUDE_CODE_OAUTH_TOKEN: 'secret-value-123' }, NODE_DIRECTORY);
      assert.fail('expected an error');
    } catch (error) {
      assert.ok(error instanceof EnvironmentError);
      assert.ok(!error.message.includes('secret-value-123'));
    }
  });
});
