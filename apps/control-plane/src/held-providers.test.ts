import assert from 'node:assert/strict';
import {
  chmodSync,
  mkdirSync,
  mkdtempSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { ProjectId } from '@halcyonic/contracts';
import { loadConfig } from './config.ts';
import { heldForms, heldIn } from './core/held-in-tree.ts';
import { HostProviders } from './providers.ts';
import { heldSecrets } from './runtimes.ts';
import { GitHubSender } from './sending/github.ts';
import { providersFolder } from './testing/declared-providers.ts';
import { standInGitHub } from './testing/github-stand-in.ts';
import { readKeyFile } from './workspace-agent/providers/keys.ts';

// The deciding test's held-secrets part (1111-b110, AUDIT3 M1 and M2): a third provider written as ONE
// provider.json, nothing else changed, has its key held, found in a tree, and a tree holding it refused by Send;
// and the one way a key file is read refuses a link, so nothing is used that is not held. Made-up keys only.

const HOST = { PATH: '/usr/bin:/bin', HOME: '/Users/test' };

test('a third provider declared as one file has its key held, found in a tree, and refused by Send, with nothing else changed', {
  skip: process.platform !== 'darwin',
}, async (t) => {
  const declared = providersFolder(t);
  declared.declare('thirdprovider', 'Thirdprovider');
  const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-held-providers-')));
  t.after(() => rmSync(base, { recursive: true, force: true }));
  const dataDir = join(base, 'data');
  mkdirSync(dataDir, { recursive: true, mode: 0o700 });
  const providers = new HostProviders({ dir: declared.dir, dataDir });
  assert.deepEqual(
    providers.list().map((provider) => provider.id),
    ['anthropic', 'thirdprovider'],
  );
  const config = loadConfig({ ...HOST, HALCYONIC_DATA_DIR: dataDir });
  const held = heldSecrets(
    config,
    { environment: { ...HOST }, dataDir },
    'token-value-0123456789',
    [],
    providers,
  );
  assert.equal(
    held().some((secret) => secret.what === 'Thirdprovider key'),
    false,
    'none stored yet',
  );
  // Stored by the page as its declaration says: held by its declared name.
  writeFileSync(join(dataDir, 'thirdprovider-api-key'), 'third-provider-key-value-0123\n', {
    mode: 0o600,
  });
  assert.deepEqual(
    held().filter((secret) => secret.what === 'Thirdprovider key'),
    [{ what: 'Thirdprovider key', value: 'third-provider-key-value-0123' }],
  );
  // Refused for use when others can read it, yet still held.
  chmodSync(join(dataDir, 'thirdprovider-api-key'), 0o644);
  assert.equal(readKeyFile(dataDir, 'thirdprovider-api-key').kind, 'unusable');
  assert.ok(held().some((secret) => secret.value === 'third-provider-key-value-0123'));
  // Its declared variable in the plane's environment is held too.
  const withVariable = heldSecrets(
    config,
    { environment: { ...HOST, THIRDPROVIDER_API_KEY: 'third-provider-env-value-45' }, dataDir },
    'token-value-0123456789',
    [],
    providers,
  )();
  assert.ok(withVariable.some((secret) => secret.value === 'third-provider-env-value-45'));
  // Found in a tree's file, and a tree holding it is never sent.
  assert.equal(
    heldIn(Buffer.from('k="third-provider-key-value-0123"'), heldForms(held())),
    'Thirdprovider key',
  );
  const stand = standInGitHub(join(base, 'stand'));
  writeFileSync(
    join(stand.folder, 'config.js'),
    'export const k = "third-provider-key-value-0123";\n',
  );
  const sender = new GitHubSender({
    git: '/usr/bin/git',
    gh: stand.ghPath,
    dataDir: join(base, 'stand', 'data'),
    env: { HOME: stand.home },
    held,
  });
  const reviewed = await sender.review(
    '01a1d000-0000-7000-8000-00000000b110' as ProjectId,
    stand.folder,
    [],
    'Third',
  );
  assert.equal(reviewed.ok ? null : reviewed.code, 'holds_secret');
  assert.match(
    reviewed.ok ? '' : reviewed.message,
    /^config\.js holds a secret this Mac keeps \(Thirdprovider key\)/,
  );
});

test('the one way a key file is read refuses a link, so no key is used that the held secrets do not hold (M2)', (t) => {
  const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-key-link-')));
  t.after(() => rmSync(base, { recursive: true, force: true }));
  const elsewhere = join(base, 'elsewhere');
  writeFileSync(elsewhere, 'linked-key-value-0123456789\n', { mode: 0o600 });
  const dataDir = join(base, 'data');
  mkdirSync(dataDir, { mode: 0o700 });
  symlinkSync(elsewhere, join(dataDir, 'anthropic-api-key'));
  const state = readKeyFile(dataDir, 'anthropic-api-key');
  assert.equal(state.kind, 'unusable');
  assert.equal(state.kind === 'unusable' ? state.code : null, 'key_file_not_regular');
  const providers = new HostProviders({ dataDir });
  assert.equal(
    providers.keyState('anthropic').kind,
    'unusable',
    'the host list reads it the same way',
  );
  const config = loadConfig({ ...HOST, HALCYONIC_DATA_DIR: dataDir });
  const held = heldSecrets(
    config,
    { environment: { ...HOST }, dataDir },
    'token-value-0123456789',
    [],
    providers,
  )();
  assert.equal(
    held.some((secret) => secret.value.startsWith('linked-key')),
    false,
    'a link is never read',
  );
});
