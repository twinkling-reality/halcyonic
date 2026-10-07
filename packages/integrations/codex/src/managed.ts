import { lstat } from 'node:fs/promises';
import { userInfo } from 'node:os';
import { join } from 'node:path';

/**
 * Where Codex 0.157.0 reads configuration on a Mac whatever its home: `/etc/codex`
 * (codex-rs/config/src/loader/mod.rs at rust-v0.157.0), with `config.toml` below the launch's
 * settings, which may still add MCP servers or providers, `managed_config.toml` and
 * `requirements.toml`, which outrank them, and managed hooks and skills; and the `com.openai.codex` managed preferences a device
 * profile forces (codex-rs/config/src/loader/macos.rs: `config_toml_base64` and
 * `requirements_toml_base64`), which macOS keeps under `/Library/Managed Preferences`, machine-wide and per user.
 */
export function systemConfigurationPaths(): string[] {
  const managed = '/Library/Managed Preferences';
  return [
    // The folder itself: besides its configuration files, Codex reads managed hooks (`hooks.json`,
    // hooks/src/engine/discovery.rs) and skills (`skills`, ext/skills/src/host_roots.rs) there.
    '/etc/codex',
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
