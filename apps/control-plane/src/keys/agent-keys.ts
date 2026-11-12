import { randomBytes } from 'node:crypto';
import { lstatSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import type { AgentKey } from '@halcyonic/runtime-core';

/** The longest key kept: room for any provider's key, no more. */
export const LONGEST_KEY = 4096;

/** What the Mac's page may know of a declared key: whether one is stored, and since when; never the key. */
export interface KeyView {
  readonly runtime_id: string;
  readonly app: string;
  readonly field: string;
  readonly stored: boolean;
  /** When the stored key was written, or null where none is stored. */
  readonly stored_at: string | null;
  /**
   * A file is where the key would be, but the app would refuse it: other users can read it, or it is empty, as a file
   * placed by hand may be. It is not counted as stored, and Add Key replaces it.
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
 * The person's own keys for the agent apps that declare one (AgentKey, R2), each kept as its private file in the data
 * folder, mode 600, written by a temporary file renamed into place, so a key is whole or absent. Nothing here logs,
 * returns or records a key: what leaves is whether one is stored and when. A vendor's name appears only in what the
 * declarations themselves say.
 */
export class AgentKeys {
  readonly #dataDir: string;
  readonly #declared: readonly AgentKey[];

  constructor(dataDir: string, declared: readonly AgentKey[]) {
    this.#dataDir = dataDir;
    this.#declared = declared;
  }

  /** Every declared key, in the declarations' order. */
  list(): KeyView[] {
    return this.#declared.map((key) => {
      const file = keyFile(this.#dataDir, key);
      return {
        runtime_id: key.runtimeId,
        app: key.app,
        field: key.field,
        stored: file.state === 'stored',
        stored_at: file.state === 'stored' ? file.at : null,
        unusable: file.state === 'unusable',
      };
    });
  }

  /** Keeps `value` as the key of `runtimeId`'s agent app, over any kept before; the refusal's words, or null. */
  store(runtimeId: unknown, value: unknown): string | null {
    const key = this.#declared.find((each) => each.runtimeId === runtimeId);
    if (key === undefined) return 'That agent app takes no key here.';
    const problem = keyProblem(value);
    if (problem !== null) return problem;
    const path = join(this.#dataDir, key.file);
    const temporary = join(this.#dataDir, `.${key.file}.${randomBytes(6).toString('hex')}`);
    try {
      writeFileSync(temporary, `${(value as string).trim()}\n`, { mode: 0o600, flag: 'wx' });
      renameSync(temporary, path);
    } catch {
      rmSync(temporary, { force: true });
      return "The key couldn't be kept on this computer. Try again.";
    }
    return null;
  }

  /** Removes `runtimeId`'s kept key; the refusal's words, or null. */
  remove(runtimeId: unknown): string | null {
    const key = this.#declared.find((each) => each.runtimeId === runtimeId);
    if (key === undefined) return 'That agent app takes no key here.';
    if (keyFile(this.#dataDir, key).state === 'none') return 'No key is stored for it.';
    try {
      rmSync(join(this.#dataDir, key.file));
    } catch {
      return "The key couldn't be removed. Try again.";
    }
    return null;
  }
}

/**
 * `key`'s file as the app would take it: `stored` where it is a file of its own that only the person can read and holds
 * a key, `unusable` where it is a file the app would refuse (other users can read it, or it is empty), else `none`.
 * A file larger than any key the page keeps is counted as one without being read. The key read here never leaves
 * this function.
 */
export function keyFile(
  dataDir: string,
  key: AgentKey,
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

/** When `key`'s file was written, where it holds a key the app would take; null where none is kept or it is unusable. */
export function storedAt(dataDir: string, key: AgentKey): string | null {
  const file = keyFile(dataDir, key);
  return file.state === 'stored' ? file.at : null;
}
