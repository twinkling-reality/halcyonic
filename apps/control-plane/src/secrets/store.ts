import { randomUUID } from 'node:crypto';
import {
  chmodSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  renameSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { join } from 'node:path';
import {
  compileValidator,
  ProjectId,
  type RuntimeDescriptor,
  SecretName,
} from '@halcyonic/contracts';
import { type Projection, reservedSecretName } from '@halcyonic/domain';
import type { HeldSecret } from '../core/redaction.ts';

/**
 * Project secrets (ADR 0057): each value in a file of its own, `<data dir>/secrets/<project>/<name>`,
 * readable by the user only, and nowhere else. The journal, the projection and every view know a
 * secret by its name only. A value is never logged, returned by a read, drawn, written into a
 * project's folder, a copy, a saved copy, a check, a preview or a share, or given to anything but
 * the agent's commands at a start, where the runtime declares `secret_environment`.
 *
 * The setup page stages a value under the id of the command it is about to submit; the command's
 * dispatch commits it, so only a set the person submitted on this Mac, and admission accepted, keeps
 * one. Every value held is in `held()` from the moment it is staged, so redaction knows it before
 * any agent can.
 */

/** The folder of all projects' secrets in the data folder. */
export const SECRETS_FOLDER = 'secrets';

/** The shortest value kept: a shorter one is too likely an ordinary word for redaction to take out. */
export const SHORTEST_VALUE = 8;
/** The longest value kept, in UTF-8 bytes: room for a private key in PEM. */
export const LONGEST_VALUE = 8192;

const STAGED = '.staged-';
const isProjectId = compileValidator(ProjectId);
const isSecretName = compileValidator(SecretName);

/** Why `value` cannot be a secret's value, in the product's words, or null when it can. */
export function valueProblem(value: unknown): string | null {
  if (typeof value !== 'string') return 'Type the secret.';
  if (value.trim().length < SHORTEST_VALUE)
    return `A secret is at least ${SHORTEST_VALUE} characters, so it can be kept out of what agents report.`;
  if (Buffer.byteLength(value, 'utf8') > LONGEST_VALUE) return 'That secret is too long to keep.';
  if (value.includes('\0')) return "A secret can't hold a null character.";
  return null;
}

/** Why `name` cannot be a secret's name, in the product's words, or null when it can. */
export function nameProblem(name: unknown): string | null {
  if (typeof name !== 'string' || !isSecretName(name).ok)
    return 'A name is capital letters, digits and _, starting with a letter, as API_KEY.';
  return reservedSecretName(name);
}

interface Staged {
  readonly projectId: ProjectId;
  readonly name: SecretName;
  readonly value: string;
  readonly path: string;
}

export class SecretStore {
  readonly #root: string;
  readonly #values = new Map<ProjectId, Map<SecretName, string>>();
  readonly #staged = new Map<string, Staged>();
  /**
   * Values replaced or removed while this control plane runs, kept in memory only, never on disk: a
   * task started before still holds its value and can print it (AUDIT3), so redaction keeps knowing
   * it for the control plane's life.
   */
  readonly #retired: HeldSecret[] = [];

  constructor(dataDir: string) {
    this.#root = join(dataDir, SECRETS_FOLDER);
  }

  /**
   * Reads what the store holds, once the journal is folded. `listed` is each project's names as the
   * journal has them: a value whose set was never recorded, and a staged value a press left behind,
   * are deleted.
   */
  open(listed: (projectId: ProjectId) => readonly string[]): void {
    mkdirSync(this.#root, { recursive: true, mode: 0o700 });
    chmodSync(this.#root, 0o700);
    this.#values.clear();
    for (const project of readdirSync(this.#root)) {
      const folder = join(this.#root, project);
      const id = isProjectId(project);
      if (!id.ok || !lstatSync(folder).isDirectory()) {
        rmSync(folder, { recursive: true, force: true });
        continue;
      }
      const names = new Set(listed(id.value));
      const values = new Map<SecretName, string>();
      for (const file of readdirSync(folder)) {
        const path = join(folder, file);
        if (!names.has(file) || !lstatSync(path).isFile() || nameProblem(file) !== null) {
          rmSync(path, { force: true });
          continue;
        }
        chmodSync(path, 0o600);
        values.set(file, readFileSync(path, 'utf8'));
      }
      if (values.size > 0) this.#values.set(id.value, values);
    }
  }

  /**
   * Keeps `value` aside for the set `commandId` is about to submit, never in place of a kept one
   * until the command commits it. Throws when it cannot be written.
   */
  stage(commandId: string, projectId: ProjectId, name: SecretName, value: string): void {
    const folder = join(this.#root, projectId);
    mkdirSync(folder, { recursive: true, mode: 0o700 });
    const path = join(folder, `${STAGED}${randomUUID()}`);
    writeFileSync(path, value, { mode: 0o600, flag: 'wx' });
    this.#staged.set(commandId, { projectId, name, value, path });
  }

  /** Drops a staged value whose command was not accepted. */
  discard(commandId: string): void {
    const staged = this.#staged.get(commandId);
    if (staged === undefined) return;
    this.#staged.delete(commandId);
    rmSync(staged.path, { force: true });
  }

  /**
   * Puts the value staged for `commandId` in place, for its project and name: null when none was
   * staged for exactly that, as for a set submitted anywhere but this Mac's page.
   */
  commit(
    commandId: string,
    projectId: ProjectId,
    name: SecretName,
  ): { readonly replaced: boolean } | null {
    const staged = this.#staged.get(commandId);
    if (staged === undefined || staged.projectId !== projectId || staged.name !== name) return null;
    this.#staged.delete(commandId);
    renameSync(staged.path, join(this.#root, projectId, name));
    let values = this.#values.get(projectId);
    if (values === undefined) {
      values = new Map();
      this.#values.set(projectId, values);
    }
    const replaced = values.has(name);
    const before = values.get(name);
    if (before !== undefined && before !== staged.value)
      this.#retired.push({ what: name, value: before });
    values.set(name, staged.value);
    return { replaced };
  }

  /** Deletes a secret's value. */
  remove(projectId: ProjectId, name: SecretName): void {
    rmSync(join(this.#root, projectId, name), { force: true });
    const values = this.#values.get(projectId);
    const before = values?.get(name);
    if (before !== undefined) this.#retired.push({ what: name, value: before });
    values?.delete(name);
    if (values?.size === 0) this.#values.delete(projectId);
  }

  /**
   * The project's secrets as variables, for an agent's commands at a start: only those `names`
   * lists, as the project's view has them.
   */
  environment(projectId: ProjectId, names: readonly string[]): Record<string, string> {
    const values = this.#values.get(projectId);
    const environment: Record<string, string> = {};
    if (values === undefined) return environment;
    for (const name of names) {
      const value = values.get(name);
      if (value !== undefined) environment[name] = value;
    }
    return environment;
  }

  /** Every value kept, staged, or replaced or removed since this start, each under its name, for redaction. */
  held(): HeldSecret[] {
    const held: HeldSecret[] = [];
    for (const values of this.#values.values()) {
      for (const [name, value] of values) held.push({ what: name, value });
    }
    for (const staged of this.#staged.values())
      held.push({ what: staged.name, value: staged.value });
    held.push(...this.#retired);
    return held;
  }
}

/**
 * What a start, or a re-attach after a restart, gives the agent's commands (ADR 0057): the project's
 * secrets as they are now, for a runtime that declares `secret_environment`, and nothing otherwise.
 */
export function givenSecrets(
  store: SecretStore | null | undefined,
  projection: Projection,
  projectId: ProjectId,
  runtime: RuntimeDescriptor,
): Record<string, string> {
  if (store === null || store === undefined || runtime.secret_environment !== true) return {};
  return store.environment(projectId, projection.project(projectId)?.secrets ?? []);
}
