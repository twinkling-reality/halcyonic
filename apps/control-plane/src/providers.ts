import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, realpathSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  type HostedProvider,
  type KeyReader,
  type KeyState,
  readerReaches,
  upstreamProblem,
  WIRE_SHAPES,
  type WireShape,
} from '@halcyonic/runtime-core';
import { ConfigError } from './config.ts';
import { declaredKeys } from './workspace-agent/providers/keys.ts';
import { KEY_REFUSED } from './workspace-agent/providers/provider.ts';

/** The repository's own declarations: `providers/<id>/provider.json` (ADR 0061). */
export const PROVIDERS_DIR = fileURLToPath(new URL('../../../providers', import.meta.url));

const ID = /^[a-z][a-z0-9_-]{0,31}$/;
const NAME = /^[A-Za-z0-9._-]{1,64}$/;
const VARIABLE = /^[A-Z][A-Z0-9_]{0,63}$/;
const HEADER = /^[A-Za-z][A-Za-z0-9-]{0,63}$/;
const SCHEME = /^[A-Za-z]{1,16}$/;
const WORDS = /^\S(?:.{0,78}\S)?$/;
const MODEL = /^\S{1,128}$/;

/**
 * The host's one list of declared hosted providers, read once as it starts from each `<dir>/<id>/provider.json`.
 * A declaration that does not hold stops the start, as a misconfigured runtime does, rather than vanishing; a folder
 * without a `provider.json` is not one. The list names no vendor: a third provider is a third file.
 */
export class HostProviders {
  readonly #providers: readonly HostedProvider[];
  readonly #readers: readonly KeyReader[];
  readonly #keys: (key: HostedProvider['key']) => KeyState;

  /**
   * `readers` are the keys' readers as each declares itself (an agent app's adapter, the assistant); `dir` is where
   * the declarations are, the repository's own unless a test hands its own.
   */
  /**
   * `standIn`, where set, is one address on this Mac every provider is reached at instead of its own: the stand-in a
   * proof or a suite runs, or a closed port, so no fake key ever reaches a real provider (rule 29). Every reader reads
   * the list, so the assistant, the key check and the gate are all redirected by this one setting. An address that is
   * not this Mac's stops the start.
   */
  constructor(options: {
    readonly dir?: string;
    readonly dataDir: string;
    readonly readers?: readonly KeyReader[];
    readonly standIn?: string | null;
    /**
     * The data folders that keep a person's own keys (the default one, the proving plane's): a plane on one refuses
     * the stand-in, so one variable left in a shell never sends a person's keys to a lane's listener (AUDIT3).
     */
    readonly ownDataFolders?: readonly string[];
  }) {
    const declared = readDeclarations(options.dir ?? PROVIDERS_DIR);
    const standIn = options.standIn ?? null;
    if (standIn !== null && !onThisMac(standIn))
      throw new ConfigError(
        `HALCYONIC_ASSISTANT_UPSTREAM must be an address on this Mac (http or https on 127.0.0.1, [::1] or localhost), not ${standIn}.`,
      );
    if (
      standIn !== null &&
      (options.ownDataFolders ?? []).some(
        (own) => realOrResolved(own) === realOrResolved(options.dataDir),
      )
    )
      throw new ConfigError(
        "HALCYONIC_ASSISTANT_UPSTREAM is for test planes; this plane keeps a person's keys. Start it without it.",
      );
    this.#providers =
      standIn === null
        ? declared
        : declared.map((provider) => ({ ...provider, upstream: standIn }));
    this.#readers = options.readers ?? [];
    this.#keys = declaredKeys(options.dataDir);
  }

  /** The declared readers that reach provider `id`: those that name it (or all) and speak a shape it serves. */
  readersOf(id: string): readonly KeyReader[] {
    const provider = this.byId(id);
    return provider === undefined
      ? []
      : this.#readers.filter((reader) => readerReaches(reader, provider));
  }

  /** Every declared provider, by its id's order. */
  list(): readonly HostedProvider[] {
    return this.#providers;
  }

  byId(id: string): HostedProvider | undefined {
    return this.#providers.find((provider) => provider.id === id);
  }

  /** The providers that serve one of `needs`, for an agent app or the assistant that speaks them. */
  serving(needs: readonly WireShape[]): readonly HostedProvider[] {
    return this.#providers.filter((provider) =>
      provider.speaks.some((shape) => needs.includes(shape)),
    );
  }

  /** What the host sees of a provider's key now, read at each ask (stored, none, unusable); never logged. */
  keyState(id: string): KeyState {
    const provider = this.byId(id);
    return provider === undefined ? { kind: 'none' } : this.#keys(provider.key);
  }
}

/** A folder's real path, links resolved, or its resolved path where it does not exist yet. */
function realOrResolved(path: string): string {
  try {
    return realpathSync(path);
  } catch {
    return resolve(path);
  }
}

/** Whether `address` is a plain address on this Mac's loopback, the only place a stand-in may be. */
export function onThisMac(address: string): boolean {
  if (upstreamProblem(address) !== null) return false;
  return ['127.0.0.1', '[::1]', 'localhost'].includes(new URL(address).hostname);
}

function readDeclarations(dir: string): HostedProvider[] {
  let ids: string[];
  try {
    ids = readdirSync(dir).sort();
  } catch {
    return [];
  }
  const providers: HostedProvider[] = [];
  for (const id of ids) {
    const path = join(dir, id, 'provider.json');
    try {
      if (!statSync(path).isFile()) continue;
    } catch {
      continue;
    }
    let raw: unknown;
    try {
      raw = JSON.parse(readFileSync(path, 'utf8'));
    } catch {
      throw new ConfigError(`${path} is not JSON.`);
    }
    const problem = declarationProblem(id, raw);
    if (problem !== null) throw new ConfigError(`${path}: ${problem}`);
    providers.push(declared(raw as Declaration));
  }
  const files = new Map<string, string>();
  for (const provider of providers) {
    const other = files.get(provider.key.file);
    if (other !== undefined)
      throw new ConfigError(
        `Providers ${other} and ${provider.id} declare the same key file; one key, one declaration.`,
      );
    files.set(provider.key.file, provider.id);
  }
  return providers;
}

interface Declaration {
  readonly format: 1;
  readonly id: string;
  readonly name: string;
  readonly speaks: readonly WireShape[];
  readonly upstream: string;
  readonly key: {
    readonly file: string;
    readonly variable: string;
    readonly header: { readonly name: string; readonly scheme: string | null };
    readonly field: string;
  };
  readonly models: readonly string[];
  readonly tuning?: { readonly effort?: string; readonly outputField?: string };
}

const KEYS = ['format', 'id', 'name', 'speaks', 'upstream', 'key', 'models', 'tuning'];
const KEY_KEYS = ['file', 'variable', 'header', 'field'];

/** Why a declaration in folder `id` does not hold, or null: every field present, of its shape, and nothing else. */
export function declarationProblem(id: string, raw: unknown): string | null {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return 'not an object.';
  const value = raw as Record<string, unknown>;
  const extra = Object.keys(value).filter((name) => !KEYS.includes(name));
  if (extra.length > 0) return `unknown fields: ${extra.join(', ')}.`;
  if (value.format !== 1) return 'format must be 1.';
  if (value.id !== id || !ID.test(id)) return 'id must be the folder name, lowercase.';
  if (typeof value.name !== 'string' || !WORDS.test(value.name)) return 'name must be words.';
  const speaks = value.speaks;
  if (
    !Array.isArray(speaks) ||
    speaks.length === 0 ||
    new Set(speaks).size !== speaks.length ||
    !speaks.every((shape) => WIRE_SHAPES.includes(shape as WireShape))
  )
    return `speaks must name one or more of ${WIRE_SHAPES.join(', ')}, each once.`;
  if (typeof value.upstream !== 'string') return 'upstream must be an address.';
  const upstream = upstreamProblem(value.upstream);
  if (upstream !== null) return upstream;
  const key = value.key as Record<string, unknown> | null | undefined;
  if (typeof key !== 'object' || key === null || Array.isArray(key))
    return 'key must be an object.';
  const extraKey = Object.keys(key).filter((name) => !KEY_KEYS.includes(name));
  if (extraKey.length > 0) return `unknown key fields: ${extraKey.join(', ')}.`;
  if (typeof key.file !== 'string' || !NAME.test(key.file))
    return 'key.file must be a plain file name.';
  if (typeof key.variable !== 'string' || !VARIABLE.test(key.variable))
    return 'key.variable must be an environment variable name.';
  const header = key.header as Record<string, unknown> | null | undefined;
  if (
    typeof header !== 'object' ||
    header === null ||
    typeof header.name !== 'string' ||
    !HEADER.test(header.name) ||
    !(
      header.scheme === null ||
      (typeof header.scheme === 'string' && SCHEME.test(header.scheme))
    ) ||
    Object.keys(header).some((name) => name !== 'name' && name !== 'scheme')
  )
    return 'key.header must be {name, scheme}.';
  if (typeof key.field !== 'string' || !WORDS.test(key.field)) return 'key.field must be words.';
  const models = value.models;
  if (
    !Array.isArray(models) ||
    models.length === 0 ||
    !models.every((model) => typeof model === 'string' && MODEL.test(model))
  )
    return 'models must be one or more model ids.';
  if (value.tuning !== undefined) {
    const tuning = value.tuning as Record<string, unknown> | null;
    if (
      typeof tuning !== 'object' ||
      tuning === null ||
      Array.isArray(tuning) ||
      Object.keys(tuning).some((name) => name !== 'effort' && name !== 'outputField') ||
      (tuning.effort !== undefined &&
        (typeof tuning.effort !== 'string' || !ID.test(tuning.effort))) ||
      (tuning.outputField !== undefined &&
        (typeof tuning.outputField !== 'string' || !ID.test(tuning.outputField)))
    )
      return 'tuning may name only effort and outputField, each a lowercase word.';
  }
  return null;
}

function declared(value: Declaration): HostedProvider {
  return {
    id: value.id,
    name: value.name,
    speaks: [...value.speaks],
    upstream: value.upstream,
    key: {
      file: value.key.file,
      variable: value.key.variable,
      header: { name: value.key.header.name, scheme: value.key.header.scheme },
      field: value.key.field,
    },
    models: [...value.models],
    ...(value.tuning === undefined
      ? {}
      : {
          tuning: {
            ...(value.tuning.effort === undefined ? {} : { effort: value.tuning.effort }),
            ...(value.tuning.outputField === undefined
              ? {}
              : { outputField: value.tuning.outputField }),
          },
        }),
  };
}

/** What a provider's no-spend check says of its stored key now (ADR 0061): taken, refused, none stored, or unknown. */
export type KeyCheck = 'taken' | 'refused' | 'none' | 'unknown';

/** A protocol's metadata read, as the host's wire shapes implement it: whether the key is taken, never inference. */
export interface KeyCheckProtocol {
  check(call: {
    readonly provider: HostedProvider;
    readonly upstream: URL;
    readonly key: string;
    readonly model: string;
    readonly outputTokens: number;
    readonly timeoutMs: number;
    readonly signal: AbortSignal;
  }): Promise<{ readonly ok: true } | { readonly ok: false; readonly code: string }>;
}

/**
 * Tells, before any start, whether a declared provider takes its stored key: the same metadata read the assistant
 * makes (each wire shape's `check`, which spends nothing), for the provider's first model, through the first shape it
 * speaks that the host implements. A taken or refused answer is kept for that key only (by its digest, never its
 * value) for ten minutes; a key changed since is read again. An answer that is no refusal (unreachable, a model not
 * offered) is `unknown` and kept for none.
 */
export class KeyChecks {
  readonly #providers: HostProviders;
  readonly #protocols: ReadonlyMap<WireShape, KeyCheckProtocol>;
  readonly #timeoutMs: number;
  readonly #now: () => number;
  readonly #kept = new Map<string, { digest: string; check: KeyCheck; at: number }>();

  constructor(options: {
    readonly providers: HostProviders;
    readonly protocols: ReadonlyMap<WireShape, KeyCheckProtocol>;
    readonly timeoutMs?: number;
    readonly now?: () => number;
  }) {
    this.#providers = options.providers;
    this.#protocols = options.protocols;
    this.#timeoutMs = options.timeoutMs ?? 8000;
    this.#now = options.now ?? Date.now;
  }

  async state(id: string): Promise<KeyCheck> {
    const provider = this.#providers.byId(id);
    if (provider === undefined) return 'unknown';
    const key = this.#providers.keyState(id);
    if (key.kind === 'none') return 'none';
    if (key.kind === 'unusable') return 'unknown';
    const digest = createHash('sha256').update(key.value).digest('hex');
    const kept = this.#kept.get(id);
    if (kept !== undefined && kept.digest === digest && this.#now() - kept.at < KEPT_MS)
      return kept.check;
    const shape = provider.speaks.find((each) => this.#protocols.has(each));
    const protocol = shape === undefined ? undefined : this.#protocols.get(shape);
    const model = provider.models[0];
    if (
      protocol === undefined ||
      model === undefined ||
      upstreamProblem(provider.upstream) !== null
    )
      return 'unknown';
    let check: KeyCheck;
    try {
      const answer = await protocol.check({
        provider,
        upstream: new URL(provider.upstream),
        key: key.value,
        model,
        outputTokens: 1,
        timeoutMs: this.#timeoutMs,
        signal: AbortSignal.timeout(this.#timeoutMs),
      });
      check = answer.ok ? 'taken' : answer.code === KEY_REFUSED.code ? 'refused' : 'unknown';
    } catch {
      check = 'unknown';
    }
    if (check === 'unknown') this.#kept.delete(id);
    else this.#kept.set(id, { digest, check, at: this.#now() });
    return check;
  }
}

const KEPT_MS = 10 * 60_000;
