import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import { ClaudeAgentRuntimeAdapter, EnvironmentError } from '@halcyonic/integration-claude-code';
import { CodexRuntimeAdapter } from '@halcyonic/integration-codex';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { RuntimeActionError } from '@halcyonic/runtime-core';
import { ConfigError, loadConfig } from './config.ts';
import { looksLikeCredential, redactSecrets } from './core/redaction.ts';
import { createDirectoryPolicy } from './directory-policy.ts';
import {
  ANTHROPIC_KEY_FILE,
  CLAUDE_AGENT_PROCESS_RECORD,
  CODEX_SERVER_RECORD,
  claudeAgentEnvironment,
  codexEnvironment,
  createRuntimeAdapters,
  heldSecrets,
  openCodeEnvironment,
  secretName,
  stopStaleRuntimeServers,
} from './runtimes.ts';
import { SCENARIOS } from './testing/harness.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-runtimes-'));
after(() => rmSync(base, { recursive: true, force: true }));

function adapters(
  configEnv: NodeJS.ProcessEnv,
  environment: NodeJS.ProcessEnv,
  dataDir = join(base, 'empty'),
) {
  return createRuntimeAdapters(loadConfig(configEnv), {
    mock: new MockRuntimeAdapter({ scenarios: SCENARIOS }),
    directoryPolicy: createDirectoryPolicy([]),
    environment,
    dataDir,
  });
}

const HOST = { HOME: '/home/someone', PATH: '/usr/bin' };

describe('runtime composition', () => {
  test('only the synthetic mock runtime is hosted unless a real runtime is enabled', () => {
    assert.deepEqual(
      adapters({}, {}).map((adapter) => adapter.descriptor.runtime_id),
      ['mock'],
    );
  });

  test("OpenCode alone gets Halcyonic's own OpenCode settings as its configuration home", () => {
    const additions = { HTTPS_PROXY: 'http://127.0.0.1:3128' };
    assert.deepEqual(openCodeEnvironment({ opencodeConfigHome: null }, additions), additions);
    assert.deepEqual(
      openCodeEnvironment(
        { opencodeConfigHome: '/Users/someone/.halcyonic/opencode-config' },
        {
          ...additions,
          XDG_CONFIG_HOME: '/elsewhere',
        },
      ),
      { ...additions, XDG_CONFIG_HOME: '/Users/someone/.halcyonic/opencode-config' },
    );
  });

  test('the Claude Agent runtime is registered when enabled and credentials exist', () => {
    const hosted = adapters(
      { HALCYONIC_CLAUDE_AGENT: '1' },
      { HOME: '/home/someone', PATH: '/usr/bin', ANTHROPIC_API_KEY: 'not-a-real-key' },
    );
    const claude = hosted.find((adapter) => adapter.descriptor.kind === 'claude-agent');
    assert.ok(claude);
    assert.equal(claude.descriptor.display_name, 'Claude Agent');
    assert.equal(claude.descriptor.synthetic, false);
  });

  test('the API key may come from a private file in the data directory instead of the environment', () => {
    const dataDir = mkdtempSync(join(base, 'key-'));
    writeFileSync(join(dataDir, ANTHROPIC_KEY_FILE), 'not-a-real-key\n', { mode: 0o600 });
    const hosted = adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, HOST, dataDir);
    assert.ok(hosted.some((adapter) => adapter.descriptor.kind === 'claude-agent'));
  });

  test('a key file other users can read, or an empty one, stops startup', () => {
    const exposed = mkdtempSync(join(base, 'exposed-'));
    writeFileSync(join(exposed, ANTHROPIC_KEY_FILE), 'not-a-real-key\n', { mode: 0o644 });
    assert.throws(() => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, HOST, exposed), ConfigError);
    const empty = mkdtempSync(join(base, 'empty-key-'));
    writeFileSync(join(empty, ANTHROPIC_KEY_FILE), '\n', { mode: 0o600 });
    assert.throws(() => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, HOST, empty), ConfigError);
  });

  test('the OpenCode runtime is registered when its binary is configured, without launching it', async () => {
    const dataDir = mkdtempSync(join(base, 'opencode-'));
    const binary = join(dataDir, 'opencode');
    writeFileSync(binary, '#!/bin/sh\nexit 1\n', { mode: 0o755 });
    const hosted = adapters({ HALCYONIC_OPENCODE_BIN: binary }, HOST, dataDir);
    const opencode = hosted.find((adapter) => adapter.descriptor.kind === 'opencode');
    assert.ok(opencode);
    assert.equal(opencode.descriptor.synthetic, false);
    assert.deepEqual(
      await stopStaleRuntimeServers(hosted),
      [],
      'nothing was recorded, so nothing is stopped',
    );
    await Promise.all(hosted.map((adapter) => adapter.close()));
  });

  test('the Codex runtime is registered when its binary is configured, without launching it', async () => {
    const dataDir = mkdtempSync(join(base, 'codex-'));
    const binary = join(dataDir, 'codex');
    writeFileSync(binary, '#!/bin/sh\nexit 1\n', { mode: 0o755 });
    const hosted = adapters({ HALCYONIC_CODEX_BIN: binary }, HOST, dataDir);
    const codex = hosted.find((adapter) => adapter.descriptor.kind === 'codex');
    assert.ok(codex instanceof CodexRuntimeAdapter);
    assert.equal(codex.descriptor.runtime_id, 'codex');
    assert.equal(codex.descriptor.display_name, 'Codex 0.157.0');
    assert.equal(codex.descriptor.synthetic, false);
    // The control plane's directory policy decides before anything is launched: with no project
    // roots, no folder is allowed.
    await assert.rejects(
      codex.startExecution({
        execution: {
          execution_id: '01920000-0000-7000-8000-000000000103' as ExecutionId,
          workstream_id: '01920000-0000-7000-8000-000000000102' as WorkstreamId,
          project_id: '01920000-0000-7000-8000-000000000101' as ProjectId,
        },
        instruction: 'Do the work.',
        options: {},
        model_ref: null,
        directory: realpathSync(dataDir),
        emit: () => undefined,
      }),
      (error: unknown) =>
        error instanceof RuntimeActionError &&
        error.code === 'location_not_allowed' &&
        error.message ===
          'No project roots are configured on the control plane (HALCYONIC_PROJECT_ROOTS).',
    );
    assert.deepEqual(
      await stopStaleRuntimeServers(hosted),
      [],
      'nothing was recorded, so nothing is stopped',
    );
    assert.equal(codex.serverPid, null, 'nothing was launched');
    await Promise.all(hosted.map((adapter) => adapter.close()));
  });

  test('a variable the Codex adapter owns cannot reach it through the agent environment', () => {
    const dataDir = mkdtempSync(join(base, 'codex-reserved-'));
    const binary = join(dataDir, 'codex');
    writeFileSync(binary, '#!/bin/sh\nexit 1\n', { mode: 0o755 });
    assert.throws(
      () =>
        adapters(
          { HALCYONIC_CODEX_BIN: binary, HALCYONIC_AGENT_ENV: 'SALIDIUM_INTERNAL' },
          { ...HOST, SALIDIUM_INTERNAL: '1' },
          dataDir,
        ),
      /SALIDIUM_INTERNAL/,
    );
  });

  test('a key passed through for another runtime never reaches Codex, which is never signed in', () => {
    const dataDir = mkdtempSync(join(base, 'codex-sign-in-'));
    const binary = join(dataDir, 'codex');
    writeFileSync(binary, '#!/bin/sh\nexit 1\n', { mode: 0o755 });
    const hosted = adapters(
      {
        HALCYONIC_CODEX_BIN: binary,
        HALCYONIC_AGENT_ENV: 'OPENAI_API_KEY,CODEX_API_KEY,GIT_AUTHOR_NAME',
      },
      {
        ...HOST,
        OPENAI_API_KEY: 'sk-example',
        CODEX_API_KEY: 'ck-example',
        GIT_AUTHOR_NAME: 'Someone',
      },
      dataDir,
    );
    assert.ok(hosted.some((adapter) => adapter instanceof CodexRuntimeAdapter));
    assert.deepEqual(
      codexEnvironment({
        OPENAI_API_KEY: 'sk-example',
        CODEX_ACCESS_TOKEN: 'token',
        CODEX_GITHUB_PERSONAL_ACCESS_TOKEN: 'ghp-example',
        GIT_AUTHOR_NAME: 'Someone',
      }),
      { GIT_AUTHOR_NAME: 'Someone' },
    );
  });

  test('a Codex server recorded by an earlier run is stopped at startup, without launching anything', async (t) => {
    const dataDir = mkdtempSync(join(base, 'codex-stale-'));
    // Plays a Codex app-server that outlived a crashed control plane, in its own process group.
    const binary = join(dataDir, 'codex.mjs');
    writeFileSync(binary, 'setInterval(() => {}, 1000);\n');
    const orphan = spawn(process.execPath, [binary, 'app-server'], {
      detached: true,
      stdio: 'ignore',
    });
    const stoppedBy = new Promise((resolve) =>
      orphan.once('exit', (_code, signal) => resolve(signal)),
    );
    t.after(() => orphan.kill('SIGKILL'));
    const pid = orphan.pid;
    assert.ok(pid !== undefined);
    // Recorded as the adapter records a launch: the start time and command line `ps` reports.
    let fields: string[] = [];
    for (let attempt = 0; attempt < 50 && !fields.includes('app-server'); attempt += 1) {
      fields = execFileSync('ps', ['-ww', '-p', String(pid), '-o', 'lstart=,args='], {
        env: { PATH: '/bin:/usr/bin', TZ: 'UTC', LC_ALL: 'C' },
        encoding: 'utf8',
      })
        .trim()
        .split(/\s+/);
    }
    const record = join(dataDir, CODEX_SERVER_RECORD);
    writeFileSync(
      record,
      JSON.stringify({
        pid,
        binaryPath: binary,
        command: fields.slice(5).join(' '),
        startedAt: fields.slice(0, 5).join(' '),
      }),
      { mode: 0o600 },
    );

    const hosted = adapters({ HALCYONIC_CODEX_BIN: binary }, HOST, dataDir);
    assert.deepEqual(await stopStaleRuntimeServers(hosted), [
      { runtimeId: 'codex', outcome: 'stopped', pid },
    ]);
    assert.equal(await stoppedBy, 'SIGTERM');
    assert.equal(existsSync(record), false);
    const codex = hosted.find((adapter) => adapter instanceof CodexRuntimeAdapter);
    assert.ok(codex instanceof CodexRuntimeAdapter);
    assert.equal(codex.serverPid, null, 'nothing was launched');
    await Promise.all(hosted.map((adapter) => adapter.close()));
  });

  test('a Claude Code process recorded by an earlier run is stopped at startup, without launching anything', async (t) => {
    const dataDir = mkdtempSync(join(base, 'claude-stale-'));
    // Plays a Claude Code process that outlived a crashed control plane.
    const sessionId = randomUUID();
    const orphan = spawn(
      process.execPath,
      ['-e', 'setInterval(() => {}, 1000)', '--', `--session-id=${sessionId}`],
      { stdio: 'ignore' },
    );
    const stoppedBy = new Promise((resolve) =>
      orphan.once('exit', (_code, signal) => resolve(signal)),
    );
    t.after(() => orphan.kill('SIGKILL'));
    const pid = orphan.pid;
    assert.ok(pid !== undefined);
    // Recorded as the adapter records a launch: the start time and command line `ps` reports.
    const fields = execFileSync('ps', ['-ww', '-p', String(pid), '-o', 'lstart=,args='], {
      env: { PATH: '/bin:/usr/bin', TZ: 'UTC', LC_ALL: 'C' },
      encoding: 'utf8',
    })
      .trim()
      .split(/\s+/);
    const record = join(dataDir, CLAUDE_AGENT_PROCESS_RECORD);
    const startedAt = fields.slice(0, 5).join(' ');
    const command = fields.slice(5).join(' ');
    writeFileSync(record, JSON.stringify({ processes: [{ pid, startedAt, command, sessionId }] }), {
      mode: 0o600,
    });

    const hosted = adapters(
      { HALCYONIC_CLAUDE_AGENT: '1' },
      { ...HOST, ANTHROPIC_API_KEY: 'not-a-real-key' },
      dataDir,
    );
    assert.deepEqual(await stopStaleRuntimeServers(hosted), [
      { runtimeId: 'claude-agent', outcome: 'stopped', pid },
    ]);
    assert.equal(await stoppedBy, 'SIGTERM');
    assert.equal(existsSync(record), false);
    const claude = hosted.find((adapter) => adapter instanceof ClaudeAgentRuntimeAdapter);
    assert.ok(claude instanceof ClaudeAgentRuntimeAdapter);
    assert.equal(claude.watchdogPid, null, 'nothing was launched');
    await Promise.all(hosted.map((adapter) => adapter.close()));
  });

  test('launched Claude Code sessions carry the Halcyonic launcher label, which pass-through cannot override', () => {
    assert.deepEqual(
      claudeAgentEnvironment({ SSH_AUTH_SOCK: '/tmp/agent', SEORAK_LAUNCHER: 'other' }),
      {
        SSH_AUTH_SOCK: '/tmp/agent',
        SEORAK_LAUNCHER: 'halcyonic',
      },
    );
  });

  test('an enabled Claude Agent runtime without credentials stops startup', () => {
    assert.throws(
      () => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, { HOME: '/home/someone', PATH: '/usr/bin' }),
      EnvironmentError,
    );
  });
});

describe("the secrets taken out of a runtime's text", () => {
  test('are the token, the Anthropic key, the credentials and the secret agent values, named and read when asked', () => {
    const dataDir = join(base, 'held');
    mkdirSync(dataDir, { recursive: true, mode: 0o700 });
    const environment = {
      ...HOST,
      GATEWAY_KEY: 'gateway-value-1',
      PROXY_AUTH: 'proxy-value-2',
      GITLAB_PAT: 'gitlab-value-3',
      SESSION_COOKIE: 'cookie-value-4',
      // A credential under a name that does not say so, held because it reads as one.
      GATEWAY_HEADER: 'q7Xk2pLm9vRt4wZb8nHc3jYd',
      ANTHROPIC_BASE_URL: 'https://gateway.example/v1',
      AWS_REGION: 'ap-southeast-1',
      GIT_AUTHOR_NAME: 'Someone Who Commits',
      ANTHROPIC_API_KEY: 'sk-ant-env-key',
    };
    const config = loadConfig({
      ...HOST,
      HALCYONIC_DATA_DIR: dataDir,
      HALCYONIC_AGENT_ENV:
        'GATEWAY_KEY,ANTHROPIC_BASE_URL,PROXY_AUTH,AWS_REGION,GITLAB_PAT,SESSION_COOKIE,GIT_AUTHOR_NAME,GATEWAY_HEADER',
    });
    const secrets = heldSecrets(config, { environment, dataDir }, 'the-access-token-value', []);
    // An address, a region and an author's name are passed to agents but stay in the text a person reads.
    const agentValues = [
      { what: 'GATEWAY_KEY', value: 'gateway-value-1' },
      { what: 'PROXY_AUTH', value: 'proxy-value-2' },
      { what: 'GITLAB_PAT', value: 'gitlab-value-3' },
      { what: 'SESSION_COOKIE', value: 'cookie-value-4' },
      { what: 'GATEWAY_HEADER', value: 'q7Xk2pLm9vRt4wZb8nHc3jYd' },
    ];
    assert.deepEqual(secrets(), [
      { what: 'access token', value: 'the-access-token-value' },
      { what: 'Anthropic key', value: 'sk-ant-env-key' },
      ...agentValues,
    ]);
    // A credential put in place later is read the next time.
    for (const [name, value] of [
      ['salidium-credential', 'salidium-value'],
      ['seorak-credential', 'srkx_seorak-value'],
    ] as const) {
      writeFileSync(join(dataDir, name), `${value}\n`, { mode: 0o600 });
      chmodSync(join(dataDir, name), 0o600);
    }
    assert.deepEqual(secrets().slice(2), [
      { what: 'Salidium credential', value: 'salidium-value' },
      { what: 'Seorak credential', value: 'srkx_seorak-value' },
      ...agentValues,
    ]);
    // One others can read is not read, and gives nothing.
    chmodSync(join(dataDir, 'seorak-credential'), 0o644);
    assert.deepEqual(secrets().slice(2, 4), [
      { what: 'Salidium credential', value: 'salidium-value' },
      { what: 'GATEWAY_KEY', value: 'gateway-value-1' },
    ]);
  });

  test("names read as a secret's by their whole words", () => {
    const secret = [
      'ANTHROPIC_API_KEY',
      'GITHUB_TOKEN',
      'GITLAB_PAT',
      'SMTP_PASS',
      'SSH_KEY_PASSPHRASE',
      'SESSION_COOKIE',
      'AWS_SESSION_TOKEN',
      'OPENAI_APIKEY',
      'proxy_auth',
    ];
    for (const name of [...secret, 'ANTHROPIC_CUSTOM_HEADERS', 'EXTRA_HEADER']) {
      assert.ok(secretName(name), name);
    }
    const plain = [
      'GIT_AUTHOR_NAME',
      'ANTHROPIC_BASE_URL',
      'AWS_REGION',
      'KEYBOARD_LAYOUT',
      'PATH',
    ];
    for (const name of plain) assert.ok(!secretName(name), name);
  });

  test('hold a header variable whole, and each part of any agent value that reads as a credential', () => {
    const token = 'q7Xk2pLm9vRt4wZb8nHc3jYd';
    const environment = {
      ...HOST,
      ANTHROPIC_CUSTOM_HEADERS: `Authorization: Bearer ${token}`,
      GATEWAY_OPTIONS: `region=us-east-1; key=${token}x, retries=3`,
    };
    const config = loadConfig({
      ...HOST,
      HALCYONIC_AGENT_ENV: 'ANTHROPIC_CUSTOM_HEADERS,GATEWAY_OPTIONS',
    });
    const dataDir = join(base, 'parts');
    mkdirSync(dataDir, { recursive: true, mode: 0o700 });
    const held = heldSecrets(config, { environment, dataDir }, 'the-access-token-value', [])();
    // Ordinary words such as "Authorization", "Bearer" and a region are never held.
    assert.deepEqual(held.slice(1), [
      { what: 'ANTHROPIC_CUSTOM_HEADERS', value: `Authorization: Bearer ${token}` },
      { what: 'ANTHROPIC_CUSTOM_HEADERS', value: token },
      { what: 'GATEWAY_OPTIONS', value: `${token}x` },
    ]);
  });

  test("hold the password in a URL in an agent value, however it looks, under the variable's name", () => {
    const environment = {
      ...HOST,
      DATABASE_URL: 'postgres://app:pa55W0rdXYZ123abc@db.internal:5432/app',
      CACHE_URL: 'redis://:Zx9Yw8Vu7Ts6Rq5Po4@cache:6379',
      MIRROR_URL: 'https://deploy:p%40ss-word-1@mirror.example/repo, https://plain.example/',
      DOCS_URL: 'https://docs.example/start',
    };
    const config = loadConfig({
      ...HOST,
      HALCYONIC_AGENT_ENV: 'DATABASE_URL,CACHE_URL,MIRROR_URL,DOCS_URL',
    });
    const dataDir = join(base, 'urls');
    mkdirSync(dataDir, { recursive: true, mode: 0o700 });
    const held = heldSecrets(config, { environment, dataDir }, 'the-access-token-value', [])();
    assert.deepEqual(held.slice(1), [
      { what: 'DATABASE_URL', value: 'app:pa55W0rdXYZ123abc' },
      { what: 'DATABASE_URL', value: 'pa55W0rdXYZ123abc' },
      { what: 'CACHE_URL', value: ':Zx9Yw8Vu7Ts6Rq5Po4' },
      { what: 'CACHE_URL', value: 'Zx9Yw8Vu7Ts6Rq5Po4' },
      { what: 'MIRROR_URL', value: 'deploy:p%40ss-word-1' },
      { what: 'MIRROR_URL', value: 'p%40ss-word-1' },
      { what: 'MIRROR_URL', value: 'p@ss-word-1' },
    ]);
    // A connection string an agent prints loses the password, and keeps where it connects.
    assert.equal(
      redactSecrets(
        'could not connect to postgres://app:pa55W0rdXYZ123abc@db.internal:5432/app',
        held,
      ),
      'could not connect to postgres://[redacted: DATABASE_URL]@db.internal:5432/app',
    );
  });

  test('hold a password with a raw @ in it, and one given to a password, secret or token key, however it looks', () => {
    const environment = {
      ...HOST,
      DATABASE_URL: 'postgres://app:p@ssw0rd123@db:5432/app',
      JDBC_URL: 'jdbc:postgresql://db/app?user=app&password=Secret123',
      DB_CONNECTION: 'Server=db;User Id=app;Password=Secret123x;',
      SERVICE_JSON: '{"token": "tok-value-123", "user": "someone"}',
      OAUTH_QUERY: 'client_id=halcyonic&client_secret=cs-value-456&scope=read',
      TOOL_OPTIONS: 'tokenizer=fast-tokenizer-v2; pwd_dir=/tmp/work; region=us-east-1',
    };
    const config = loadConfig({
      ...HOST,
      HALCYONIC_AGENT_ENV:
        'DATABASE_URL,JDBC_URL,DB_CONNECTION,SERVICE_JSON,OAUTH_QUERY,TOOL_OPTIONS',
    });
    const dataDir = join(base, 'keyed');
    mkdirSync(dataDir, { recursive: true, mode: 0o700 });
    const held = heldSecrets(config, { environment, dataDir }, 'the-access-token-value', [])();
    // A tokenizer, a working folder and a region are no secrets, whatever their keys contain.
    assert.deepEqual(held.slice(1), [
      { what: 'DATABASE_URL', value: 'app:p@ssw0rd123' },
      { what: 'DATABASE_URL', value: 'p@ssw0rd123' },
      { what: 'JDBC_URL', value: 'Secret123' },
      { what: 'DB_CONNECTION', value: 'Secret123x' },
      { what: 'SERVICE_JSON', value: 'tok-value-123' },
      { what: 'OAUTH_QUERY', value: 'cs-value-456' },
    ]);
    assert.equal(
      redactSecrets('connect postgres://app:p@ssw0rd123@db:5432/app failed', held),
      'connect postgres://[redacted: DATABASE_URL]@db:5432/app failed',
    );
  });

  test('hold escaped, comma-holding, command-line and more-keyed secrets, and an absolute path only as a value held by name or key', () => {
    const random = 'q7Xk2pLm9vRt4wZb8nHc';
    const environment = {
      ...HOST,
      SERVICE_JSON: '{"password": "pa\\"ss12345word", "user": "app"}',
      DB_OPTIONS: 'password=pa,ss12345word;user=app,role=admin',
      SERVICE_KEYS:
        'api_key=api-value-111 apikey=api-value-222 access_key=acc-value-333 passphrase=phrase-value-444 credentials=cred-value-555',
      BLOB_URL: 'https://acct.blob.core.windows.net/c?sv=2022-11-02&sp=r&sig=abc%2Bdef123456',
      SERVICE_CONFIG: `{"api_key": "${random}"}`,
      CLIENT_CONFIG: `{"client": "${random}x"}`,
      TOOL_ARGS: "mysql --host db --password hunter2pass --api-key 'k3y-value-123' --user app",
      ENV_FILE: 'PWD=/Users/me/project; HOME=/Users/me',
      SSH_AUTH_SOCK: '/private/tmp/com.apple.launchd.abc/Listeners',
      // A base64 secret starts with / about once in 64: held by its name all the same.
      AWS_SECRET_ACCESS_KEY: '/k7XpQ2aLm9vRt4wZb8nHc3jYd6fGs1aKe5uNo0iV',
      DB_PASSWORD: '/aB3+xY9qLm2Zt7Rk4Wd8Hc1Vn',
      // A path that reads as random, among a value's parts, is a place: not held.
      CACHE_OPTIONS: 'cache at /Q7xK2pLm9vRt4wZb8nHc3jYd',
    };
    const config = loadConfig({
      ...HOST,
      HALCYONIC_AGENT_ENV: Object.keys(environment).slice(2).join(','),
    });
    const dataDir = join(base, 'more-keyed');
    mkdirSync(dataDir, { recursive: true, mode: 0o700 });
    const held = heldSecrets(config, { environment, dataDir }, 'the-access-token-value', [])();
    assert.ok(looksLikeCredential('/Q7xK2pLm9vRt4wZb8nHc3jYd'), 'the path part reads as random');
    assert.deepEqual(held.slice(1), [
      { what: 'SERVICE_JSON', value: 'pa\\"ss12345word' },
      { what: 'SERVICE_JSON', value: 'pa"ss12345word' },
      { what: 'DB_OPTIONS', value: 'pa,ss12345word' },
      { what: 'SERVICE_KEYS', value: 'api-value-111' },
      { what: 'SERVICE_KEYS', value: 'api-value-222' },
      { what: 'SERVICE_KEYS', value: 'acc-value-333' },
      { what: 'SERVICE_KEYS', value: 'phrase-value-444' },
      { what: 'SERVICE_KEYS', value: 'cred-value-555' },
      { what: 'BLOB_URL', value: 'abc%2Bdef123456' },
      { what: 'BLOB_URL', value: 'abc+def123456' },
      { what: 'SERVICE_CONFIG', value: random },
      { what: 'CLIENT_CONFIG', value: `${random}x` },
      { what: 'TOOL_ARGS', value: 'hunter2pass' },
      { what: 'TOOL_ARGS', value: 'k3y-value-123' },
      // Held by a pwd key and by a secret's word in the name: over-redaction, never a leak.
      { what: 'ENV_FILE', value: '/Users/me/project' },
      { what: 'SSH_AUTH_SOCK', value: '/private/tmp/com.apple.launchd.abc/Listeners' },
      { what: 'AWS_SECRET_ACCESS_KEY', value: '/k7XpQ2aLm9vRt4wZb8nHc3jYd6fGs1aKe5uNo0iV' },
      { what: 'DB_PASSWORD', value: '/aB3+xY9qLm2Zt7Rk4Wd8Hc1Vn' },
    ]);
  });
});
