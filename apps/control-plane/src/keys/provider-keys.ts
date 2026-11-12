import { randomBytes } from 'node:crypto';
import { lstatSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import type { HostedProvider, ProviderKey } from '@halcyonic/runtime-core';
import type { HostProviders } from '../providers.ts';

/** The longest key kept: room for any provider's key, no more. */
export const LONGEST_KEY = 4096;

/**
 * What the Mac's page may know of a declared provider's key (ADR 0061): whose it is, its field's words, what reads it
 * and when, whether one is stored and since when; never the key.
 */
export interface KeyView {
  /** The provider's id, as its declaration names it. */
  readonly id: string;
  /** The provider's name, as its declaration says it. */
  readonly name: string;
  /** The caption over the field, as the declaration says it. */
  readonly field: string;
  /** What reads this key, each by the name and the timing its own declaration says. */
  readonly readers: readonly {
    readonly name: string;
    readonly reads: 'at_start' | 'at_each_use';
  }[];
  /** Whether a change waits for the next start (a reader reads it at its start) or takes effect at once. */
  readonly applies: 'next_start' | 'at_once';
  readonly stored: boolean;
  /** When the stored key was written, or null where none is stored. */
  readonly stored_at: string | null;
  /**
   * A file is where the key would be, but its readers would refuse it: other users can read it, or it is empty, as a
   * file placed by hand may be. It is not counted as stored, and Add Key replaces it.
   */
  readonly unusable: boolean;
}

/** Why `value` cannot be kept as a key, in the page's words, or null. Its words never repeat the value. */
export function keyProblem(value: unknown): string | null {
  if (typeof value !== 'string' || value.trim() === '') return 'Paste the key first.';
  const key = value.trim();
  if (key.length > LONGEST_KEY) return 'That is longer than any key. Paste the key alone.';
  // A key is one word of printable characters: a space or a line inside is a paste that took more than the key.
  if (!/^[\x21-\x7e]+$/.test(key))
    return 'That has spaces or line breaks in it. Paste the key alone.';
  return null;
}

/**
 * The person's own keys for the declared hosted providers that something here reads (ADR 0061), each kept as the
 * private file its declaration names in the data folder, mode 600, written by a temporary file renamed into place, so
 * a key is whole or absent. Nothing here logs, returns or records a key: what leaves is whether one is stored and when.
 * Every name and caption comes from the declarations; a provider nothing reads is not offered, since its key would do
 * nothing.
 */
export class ProviderKeys {
  readonly #dataDir: string;
  readonly #providers: HostProviders;

  constructor(dataDir: string, providers: HostProviders) {
    this.#dataDir = dataDir;
    this.#providers = providers;
  }

  /** Every declared provider something here reads, in the host list's order. */
  list(): KeyView[] {
    return this.#offered().map((provider) => {
      const readers = this.#providers.readersOf(provider.id);
      const file = keyFile(this.#dataDir, provider.key);
      return {
        id: provider.id,
        name: provider.name,
        field: provider.key.field,
        readers: readers.map((reader) => ({ name: reader.name, reads: reader.reads })),
        applies: readers.some((reader) => reader.reads === 'at_start') ? 'next_start' : 'at_once',
        stored: file.state === 'stored',
        stored_at: file.state === 'stored' ? file.at : null,
        unusable: file.state === 'unusable',
      };
    });
  }

  /** Keeps `value` as provider `id`'s key, over any kept before; the refusal's words, or null. */
  store(id: unknown, value: unknown): string | null {
    const provider = this.#offered().find((each) => each.id === id);
    if (provider === undefined) return 'Nothing here takes that key.';
    const problem = keyProblem(value);
    if (problem !== null) return problem;
    const { file } = provider.key;
    const path = join(this.#dataDir, file);
    const temporary = join(this.#dataDir, `.${file}.${randomBytes(6).toString('hex')}`);
    try {
      writeFileSync(temporary, `${(value as string).trim()}\n`, { mode: 0o600, flag: 'wx' });
      renameSync(temporary, path);
    } catch {
      rmSync(temporary, { force: true });
      return "The key couldn't be kept on this computer. Try again.";
    }
    return null;
  }

  /** Removes provider `id`'s kept key; the refusal's words, or null. */
  remove(id: unknown): string | null {
    const provider = this.#offered().find((each) => each.id === id);
    if (provider === undefined) return 'Nothing here takes that key.';
    if (keyFile(this.#dataDir, provider.key).state === 'none') return 'No key is stored for it.';
    try {
      rmSync(join(this.#dataDir, provider.key.file));
    } catch {
      return "The key couldn't be removed. Try again.";
    }
    return null;
  }

  #offered(): readonly HostedProvider[] {
    return this.#providers
      .list()
      .filter((provider) => this.#providers.readersOf(provider.id).length > 0);
  }
}

/**
 * The declared keys a reader reads at its start, as `id=<when written>` lines, never their values: what a start used,
 * so a key kept, replaced or removed since can ask for Restart to Apply.
 */
export function keysReadAtStart(dataDir: string, providers: HostProviders): string {
  return providers
    .list()
    .filter((provider) =>
      providers.readersOf(provider.id).some((reader) => reader.reads === 'at_start'),
    )
    .map((provider) => `${provider.id}=${storedAt(dataDir, provider.key) ?? ''}`)
    .join('\n');
}

/**
 * `key`'s file as its readers would take it: `stored` where it is a file of its own that only the person can read and
 * holds a key, `unusable` where it is a file they would refuse (other users can read it, or it is empty), else `none`.
 * A file larger than four times the longest key the page keeps (LONGEST_KEY) is counted as holding one, unread. The
 * key read here never leaves this function.
 */
export function keyFile(
  dataDir: string,
  key: Pick<ProviderKey, 'file'>,
): { readonly state: 'stored' | 'unusable'; readonly at: string } | { readonly state: 'none' } {
  const path = join(dataDir, key.file);
  let stat: ReturnType<typeof lstatSync>;
  try {
    stat = lstatSync(path);
  } catch {
    return { state: 'none' };
  }
  if (!stat.isFile()) return { state: 'none' };
  const at = stat.mtime.toISOString();
  if ((stat.mode & 0o077) !== 0) return { state: 'unusable', at };
  if (stat.size === 0) return { state: 'unusable', at };
  // Larger than any key the page keeps: counted as a key without reading it at every listing.
  if (stat.size > LONGEST_KEY * 4) return { state: 'stored', at };
  try {
    return readFileSync(path, 'utf8').trim() === ''
      ? { state: 'unusable', at }
      : { state: 'stored', at };
  } catch {
    return { state: 'unusable', at };
  }
}

/** When `key`'s file was written, where it holds a key its readers would take; null where none is kept or unusable. */
export function storedAt(dataDir: string, key: Pick<ProviderKey, 'file'>): string | null {
  const file = keyFile(dataDir, key);
  return file.state === 'stored' ? file.at : null;
}
