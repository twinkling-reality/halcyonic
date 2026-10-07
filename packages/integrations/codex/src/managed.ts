import { lstat } from 'node:fs/promises';
import { userInfo } from 'node:os';
import { join } from 'node:path';

/**
 * Where Codex 0.157.0 reads configuration on a Mac whatever its home: the system files under
 * `/etc/codex` (codex-rs/config/src/loader/mod.rs at rust-v0.157.0), `config.toml` below the
 * launch's settings, which may still add MCP servers or providers, and `managed_config.toml` and
 * `requirements.toml`, which outrank them; and the `com.openai.codex` managed preferences a device
 * profile forces (codex-rs/config/src/loader/macos.rs: `config_toml_base64` and
 * `requirements_toml_base64`), which macOS keeps under `/Library/Managed Preferences`.
 */
export function systemConfigurationPaths(): string[] {
  const managed = '/Library/Managed Preferences';
  return [
    '/etc/codex/config.toml',
    '/etc/codex/managed_config.toml',
    '/etc/codex/requirements.toml',
    join(managed, 'com.openai.codex.plist'),
    join(managed, userInfo().username, 'com.openai.codex.plist'),
  ];
}

/**
 * The paths of those that exist, as a link or anything else. Only their existence is read, never
 * their contents. A path that cannot be checked counts as present: what it holds can't be told.
 */
export async function presentSystemConfiguration(paths: readonly string[]): Promise<string[]> {
  const present: string[] = [];
  for (const path of paths) {
    try {
      await lstat(path);
      present.push(path);
    } catch (error) {
      const absent =
        error instanceof Error &&
        'code' in error &&
        (error.code === 'ENOENT' || error.code === 'ENOTDIR');
      if (!absent) present.push(path);
    }
  }
  return present;
}
