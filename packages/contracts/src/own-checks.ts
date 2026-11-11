import Type, { type Static } from 'typebox';
import { Sha256Hex } from './devices.ts';
import { Nullable, Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/**
 * A project's own check (ADR 0055): scripts of the project's own `package.json`, chosen by the
 * person, run by the host inside the same closed sandbox as an agent. Never a command the model or
 * an agent wrote.
 */

/** The package manager a project's own check runs its scripts with, from the project's lockfile; go for a Go module. */
export const OwnCheckManager = Type.Union([
  Type.Literal('npm'),
  Type.Literal('pnpm'),
  Type.Literal('yarn'),
  /** A Go module with no package.json: Halcyonic's own `go test ./...` and `go vet ./...`. */
  Type.Literal('go'),
]);
export type OwnCheckManager = Static<typeof OwnCheckManager>;

/** The only script names a project's own check offers, in the order it offers them. */
export const OWN_CHECK_SCRIPT_NAMES = ['test', 'check', 'typecheck', 'lint', 'build'] as const;

export const OwnCheckScriptName = Type.Union([
  Type.Literal('test'),
  Type.Literal('check'),
  Type.Literal('typecheck'),
  Type.Literal('lint'),
  Type.Literal('build'),
]);
export type OwnCheckScriptName = Static<typeof OwnCheckScriptName>;

/** The longest script command the host offers; a longer one is not offered. */
export const OWN_CHECK_COMMAND_MAX = 2000;

/**
 * A script the person chose, by name and the SHA-256 of the command they read (AUDIT3 S1): a command changed since it
 * was read is refused, so the choice is bound to what the person saw.
 */
export const OwnCheckChoice = Type.Object(
  { name: OwnCheckScriptName, command_sha256: Sha256Hex },
  strict,
);
export type OwnCheckChoice = Static<typeof OwnCheckChoice>;

/** A chosen script, with the SHA-256 of its command as chosen, so a changed command is not run. */
export const OwnCheckScript = Type.Object(
  {
    name: OwnCheckScriptName,
    command: Text(OWN_CHECK_COMMAND_MAX),
    command_sha256: Sha256Hex,
  },
  strict,
);
export type OwnCheckScript = Static<typeof OwnCheckScript>;

/**
 * Why no script can be offered: `no_manifest` no `package.json` at the folder's top; `manifest_unreadable`
 * it is not a file the host reads (a link, too large, not JSON, no `scripts` object); `no_scripts` none of
 * the offered names; `manager_missing` its lockfile's package manager is not found on this Mac.
 */
export const OwnCheckNotFound = Type.Union([
  Type.Literal('no_manifest'),
  Type.Literal('manifest_unreadable'),
  Type.Literal('no_scripts'),
  Type.Literal('manager_missing'),
]);
export type OwnCheckNotFound = Static<typeof OwnCheckNotFound>;

/**
 * `GET /api/projects/:project_id/own-check`: the scripts the host found in the project's folder now, to
 * show before the person chooses, each with its command's SHA-256 for the choice to carry; `reason` null
 * when some are offered.
 */
export const OwnCheckFoundResponse = Type.Object(
  {
    manager: Nullable(OwnCheckManager),
    scripts: Type.Array(OwnCheckScript, { maxItems: OWN_CHECK_SCRIPT_NAMES.length }),
    reason: Nullable(OwnCheckNotFound),
  },
  strict,
);
export type OwnCheckFoundResponse = Static<typeof OwnCheckFoundResponse>;

/**
 * One chosen script's run: its exit code, or null when it was stopped at its limit (`timed_out`) or
 * could not start; `refused` when a failed script's last lines name a refusal of the sandbox (`EPERM`), so a
 * need for the network or a port never reads as the agent's fault; `left_running` when something it started
 * outlived it: a pipe still held after it exited, or the folder written after the run, so what the check
 * observed is not whole.
 */
export const OwnCheckRun = Type.Object(
  {
    name: OwnCheckScriptName,
    exit: Nullable(Type.Integer()),
    seconds: Type.Number({ minimum: 0 }),
    timed_out: Type.Boolean(),
    refused: Type.Boolean(),
    left_running: Type.Boolean(),
  },
  strict,
);
export type OwnCheckRun = Static<typeof OwnCheckRun>;

/** The most paths of the check's own writes kept. */
export const OWN_CHECK_WRITTEN_LISTED = 20;

/**
 * What the check itself changed in the folder, read before and after it as the Mac reads a turn (ADR
 * 0038): the check's, never an agent's. Counts, and the first paths in path order.
 */
export const OwnCheckWritten = Type.Object(
  {
    created: Type.Integer({ minimum: 0 }),
    changed: Type.Integer({ minimum: 0 }),
    deleted: Type.Integer({ minimum: 0 }),
    paths: Type.Array(Type.String({ minLength: 1, maxLength: 1024 }), {
      maxItems: OWN_CHECK_WRITTEN_LISTED,
    }),
  },
  strict,
);
export type OwnCheckWritten = Static<typeof OwnCheckWritten>;

/**
 * What the host observed of a project's own check: `ran` the chosen scripts ran, in order, until one
 * failed; `command_changed` a chosen script's command is no longer the one chosen (or is gone), so
 * nothing ran; `manager_missing` the package manager is not found; `manifest_unreadable` the
 * `package.json` could not be read. `passed` is every chosen script exiting 0, and nothing more.
 * `written` is null when the folder could not be read before or after.
 */
export const OwnCheckFindings = Type.Object(
  {
    outcome: Type.Union([
      Type.Literal('ran'),
      Type.Literal('command_changed'),
      Type.Literal('manager_missing'),
      Type.Literal('manifest_unreadable'),
    ]),
    manager: OwnCheckManager,
    runs: Type.Array(OwnCheckRun, { maxItems: OWN_CHECK_SCRIPT_NAMES.length }),
    passed: Type.Boolean(),
    written: Nullable(OwnCheckWritten),
  },
  strict,
);
export type OwnCheckFindings = Static<typeof OwnCheckFindings>;
