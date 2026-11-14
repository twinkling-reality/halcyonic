/**
 * The host's facts as the model reads them, in plain sentences (the lead, 2026-11-12, after the 30B window at 04:40Z:
 * both answers were right and read like a printout, `type_check: passed`, `first_errors []`, a timestamp). One table
 * from the host's codes to words, so there is no field name or code for the model to repeat; the facts themselves
 * stay as they are for every other reader.
 */
import type { CheckFindings } from '@halcyonic/contracts';
import { OWN_LOOK_NOT_KNOWN, PARTLY_CHECKED } from '../preview/check.ts';

/**
 * A starter's check's state as the model reads it: `not_known` where everything seen passed and only whether the page
 * draws its own look could not be found out, never failed (VRIFY, 2026-11-13); else passed or failed by `passed`.
 */
export function checkStateOf(findings: CheckFindings): 'passed' | 'failed' | 'not_known' {
  if (findings.verdict === 'not_known') return 'not_known';
  return findings.passed ? 'passed' : 'failed';
}

/** A task's status in words: a turn that ended is not work that is done. */
export function statusWords(status: string): string {
  switch (status) {
    case 'starting':
      return 'starting';
    case 'running':
      return 'working now';
    case 'verifying':
      return 'working now, running its tests';
    case 'waiting_for_human':
      return 'waiting for the person';
    case 'completed':
    case 'turn_ended':
      return 'its last turn ended';
    case 'failed':
    case 'turn_failed':
      return 'its last turn failed';
    case 'interrupted':
    case 'turn_stopped':
      return 'its last turn was stopped';
    default:
      return 'not known';
  }
}

/** How one step of the computer's check ended, in words. */
function stepWords(state: string): string {
  return state === 'passed'
    ? 'passed'
    : state === 'failed'
      ? 'failed'
      : 'stopped at its time limit';
}

/** How long ago a time was, in words a person says, never the time itself. */
export function agoWords(at: string, now: Date): string {
  const minutes = Math.max(0, Math.round((now.getTime() - Date.parse(at)) / 60_000));
  if (!Number.isFinite(minutes)) return 'at a time not known';
  if (minutes < 1) return 'just now';
  if (minutes === 1) return 'a minute ago';
  if (minutes < 60) return `${minutes} minutes ago`;
  const hours = Math.round(minutes / 60);
  return hours === 1
    ? 'an hour ago'
    : hours < 48
      ? `${hours} hours ago`
      : `${Math.round(hours / 24)} days ago`;
}

const plural = (count: number, one: string, many = `${one}s`) =>
  `${count === 0 ? 'no' : count} ${count === 1 ? one : many}`;

interface CheckFacts {
  readonly state: string;
  readonly type_check?: string;
  readonly build?: string;
  readonly page?: {
    readonly errors: number;
    /** Null when it is not known (the starter's picture to compare it with could not be made). */
    readonly showed_its_own_picture: boolean | null;
  } | null;
  readonly unknown?: unknown;
  readonly files_since_check?: string;
  readonly recorded_at?: string;
}

/**
 * The computer's check in sentences: what passed, what failed, whether the files changed since, and when. A check whose
 * own look is not known is Partly Checked, its line and the finding's sentence under it, never failed.
 */
export function checkWords(check: CheckFacts, now: Date): string {
  if (check.state === 'not_checked') return 'The computer has not checked these files.';
  const notKnown = check.state === 'not_known';
  const parts = [
    check.type_check === undefined ? null : `the type check ${stepWords(check.type_check)}`,
    check.build === undefined ? null : `the build ${stepWords(check.build)}`,
    check.page === undefined || check.page === null
      ? 'the page was not opened'
      : check.page.errors === 0
        ? `the page opened with no errors${check.page.showed_its_own_picture === true ? ' and drew the scene' : check.page.showed_its_own_picture === false ? ' but drew nothing' : ''}`
        : `the page showed ${plural(check.page.errors, 'error')}${check.page.showed_its_own_picture === false ? ' and drew nothing' : ''}`,
  ].filter((part): part is string => part !== null);
  const head = notKnown
    ? `The computer's check: ${PARTLY_CHECKED.label}. ${PARTLY_CHECKED.line} ${OWN_LOOK_NOT_KNOWN}${parts.length ? ` In it ${parts.join(', ')}.` : ''}`
    : `The computer's check ${check.state === 'passed' ? 'passed' : 'failed'}${parts.length ? `: ${parts.join(', ')}` : ''}.`;
  const since =
    check.files_since_check === 'current'
      ? ' The files have not changed since.'
      : check.files_since_check === 'changed'
        ? ' The files changed after it, so it may be out of date.'
        : check.files_since_check === undefined
          ? ''
          : ' Whether the files changed since is not known.';
  const when =
    check.recorded_at === undefined ? '' : ` It ran ${agoWords(check.recorded_at, now)}.`;
  const unknown =
    check.unknown && !notKnown ? ' Part of it could not tell; its words are quoted below.' : '';
  return head + since + when + unknown;
}

interface AutoFixFacts {
  readonly state: string;
  readonly attempt: number;
  readonly cap: number;
  readonly reason: string | null;
}

/** The failed check the computer sends back on its own, in words. */
export function autoFixWords(fix: AutoFixFacts | null): string | null {
  if (fix === null) return null;
  if (fix.state === 'fixing')
    return `The computer sent its failed check back to the agent on its own: try ${fix.attempt} of ${fix.cap}.`;
  if (fix.reason === 'passed')
    return `After ${plural(fix.attempt, 'try', 'tries')} sent back on its own, the check passed.`;
  if (fix.reason === 'cap_reached')
    return `The computer stopped sending the failed check back after ${plural(fix.attempt, 'try', 'tries')}.`;
  return `The computer stopped sending the failed check back (${fix.attempt} of ${fix.cap} tries).`;
}

interface TaskFacts {
  readonly task: {
    readonly status: string;
    readonly turns: number;
    readonly model_reported: string | null;
  };
  readonly changes: {
    readonly turns_observed: number;
    readonly all_turns_read: boolean;
    readonly some_turn_read_incompletely: boolean;
    readonly created: number;
    readonly changed: number;
    readonly deleted: number;
    readonly latest_turn: {
      readonly created: number;
      readonly changed: number;
      readonly deleted: number;
    } | null;
    readonly more_listed: number;
  };
  readonly check: CheckFacts;
  readonly sent_back_on_its_own: AutoFixFacts | null;
}

/** What the Mac observed of a task, as sentences: its state, the files its turns changed, the check, any retry. */
export function taskWords(facts: TaskFacts, now: Date): string[] {
  const { task, changes } = facts;
  const said = [
    `The task is ${statusWords(task.status)}; it has taken ${plural(task.turns, 'turn')}.`,
    changes.turns_observed === 0
      ? 'The Mac has not seen any turn change a file yet.'
      : `The Mac saw ${plural(changes.turns_observed, 'turn')} change files: ${changes.created} created, ${changes.changed} changed and ${changes.deleted} deleted in all.`,
    changes.latest_turn === null || changes.turns_observed < 2
      ? null
      : `The latest turn created ${changes.latest_turn.created}, changed ${changes.latest_turn.changed} and deleted ${changes.latest_turn.deleted}.`,
    changes.some_turn_read_incompletely
      ? 'Some turn was not read whole, so a file may be missing here.'
      : null,
    changes.all_turns_read ? null : 'Only the newest turns were read.',
    changes.more_listed > 0 ? `${changes.more_listed} more file changes are not listed.` : null,
    checkWords(facts.check, now),
    autoFixWords(facts.sent_back_on_its_own),
  ];
  return said.filter((line): line is string => line !== null);
}

/**
 * Whether an answer reads like the computer's records rather than a person talking (the lead, 2026-11-12): an
 * identifier joined by underscores that is not in the words or names the request holds, a bracketed list, or an ISO
 * time. A path or file name (it holds a slash or a dot) is never one. The kind found, or null.
 */
export function looksLikeRecords(
  text: string,
  allowed: readonly string[] = [],
): 'identifier' | 'bracketed list' | 'time' | null {
  if (/\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(text)) return 'time';
  if (/\[[^\]\n]*\]/.test(text)) return 'bracketed list';
  const held = allowed.join('\n').toLowerCase();
  for (const token of text.split(/[\s,;:()'"`*]+/)) {
    const word = token.replace(/[.!?]+$/, '');
    if (word.includes('/') || word.includes('.')) continue;
    if (/^[a-z][a-z0-9]*(?:_[a-z0-9]+)+$/i.test(word) && !held.includes(word.toLowerCase()))
      return 'identifier';
  }
  return null;
}
