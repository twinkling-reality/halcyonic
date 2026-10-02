import {
  type CompanionExchangeTurn,
  CompanionExchangeTurn as CompanionExchangeTurnSchema,
  type CompanionReply,
  compileValidator,
} from '@halcyonic/contracts';
import type { Companion } from '../companion/companion.ts';
import { exchangeProblem } from '../companion/companion.ts';

/**
 * The companion's part of the recorded demonstration (ADR 0012, ADR 0025): one exchange recorded
 * once from the real companion on the owner's computer, played on the headset with no computer and
 * no model, and labelled as recorded. It is its own file, apart from the demonstration's journal
 * recording, because a model's words cannot be recorded again to the same bytes.
 */
export const COMPANION_DEMONSTRATION_FILE = new URL(
  '../../../xr/Assets/Halcyonic/Resources/HalcyonicCompanionDemonstration.json',
  import.meta.url,
);

export const COMPANION_DEMONSTRATION_VERSION = 1;

/** The idea the recording starts from: plain, and naming nothing a judge may not read. */
export const COMPANION_DEMONSTRATION_IDEA =
  "A page where my running club keeps everyone's race times";

/** At most this many questions are answered before the recap is asked for. */
export const COMPANION_DEMONSTRATION_QUESTIONS = 2;

/**
 * Names a judge must not read (the competition's rules forbid brand names), as the competition
 * build checks every word of the demonstration; a recording with one is refused.
 */
export const JUDGE_BRANDS = [
  'Claude',
  'Anthropic',
  'OpenAI',
  'Codex',
  'OpenCode',
  'GPT',
  'Gemini',
  'Llama',
  'Qwen',
  'Ollama',
  'Mistral',
  'Meta',
  'Quest',
  'Oculus',
  'Horizon',
  'Apple',
  'Mac',
  'macOS',
  'iPhone',
  'Android',
  'Google',
  'YouTube',
  'GitHub',
  'Microsoft',
  'Windows',
  'Unity',
  'Salidium',
  'Seorak',
  'Stripe',
  'Postgres',
  'PostgreSQL',
  'Redis',
  'Docker',
  'AWS',
  'npm',
  'React',
] as const;

export interface CompanionDemonstration {
  readonly version: number;
  /** Where the words come from, for a person reading the file; never shown. */
  readonly source: string;
  /** The model that answered, for the record; never shown on the headset. */
  readonly model: string;
  readonly recorded_at: string;
  readonly start: 'idea';
  readonly turns: CompanionExchangeTurn[];
}

const validateTurn = compileValidator(CompanionExchangeTurnSchema);

/**
 * What is wrong with a recording, or the empty list: every turn in the contract's shape and the
 * exchange's order, every answer of the person's one of the choices just offered (so the headset
 * can play it with a press), a proposal at the end and nowhere else, and no brand a judge may not read.
 */
export function recordingProblems(recording: CompanionDemonstration): string[] {
  const problems: string[] = [];
  if (recording.version !== COMPANION_DEMONSTRATION_VERSION) problems.push('unknown version');
  if (recording.start !== 'idea') problems.push('the recording starts from an idea');
  const turns = recording.turns ?? [];
  turns.forEach((turn, index) => {
    if (!validateTurn(turn).ok) problems.push(`turn ${index} is outside the contract`);
  });
  if (problems.length > 0) return problems;
  const order = exchangeProblem('idea', 'proposal', turns.slice(0, -1));
  if (order !== null) problems.push(order);
  if (turns[0]?.from !== 'person' || turns[0].text !== COMPANION_DEMONSTRATION_IDEA) {
    problems.push('the recording starts from its idea');
  }
  const last = turns.at(-1);
  if (last?.from !== 'companion' || last.reply.next !== 'propose')
    problems.push('the recording ends with a proposal');
  turns.slice(0, -1).forEach((turn, index) => {
    if (turn.from === 'companion' && turn.reply.next === 'propose')
      problems.push(`turn ${index} proposes before the end`);
    const before = turns[index - 1];
    if (turn.from === 'person' && index > 0) {
      if (before?.from !== 'companion' || before.reply.next !== 'ask')
        problems.push(`turn ${index} answers no question`);
      else if (!before.reply.question.choices.includes(turn.text))
        problems.push(`turn ${index} is not one of the choices offered`);
    }
  });
  for (const words of recordingWords(turns)) {
    for (const brand of JUDGE_BRANDS) {
      if (new RegExp(`\\b${brand}\\b`).test(words)) problems.push(`it names ${brand}`);
    }
  }
  return problems;
}

/** Every word of a recording a judge may read. */
function recordingWords(turns: readonly CompanionExchangeTurn[]): string[] {
  return turns.flatMap((turn) => (turn.from === 'person' ? [turn.text] : replyWords(turn.reply)));
}

function replyWords(reply: CompanionReply): string[] {
  return reply.next === 'ask'
    ? [reply.line, reply.question.text, ...reply.question.choices]
    : [reply.line, reply.proposal.project_name, reply.proposal.first_task];
}

/**
 * Records the exchange: the idea, then the first choice of each question, up to
 * {@link COMPANION_DEMONSTRATION_QUESTIONS}, then the recap. Every reply comes from the companion as
 * the control plane asks it, under its bounds; a reply that is refused stops the recording.
 */
export async function recordCompanionDemonstration(
  companion: Companion,
  model: string,
  now: Date,
): Promise<CompanionDemonstration> {
  const turns: CompanionExchangeTurn[] = [{ from: 'person', text: COMPANION_DEMONSTRATION_IDEA }];
  for (let asked = 0; ; asked++) {
    const last = turns.at(-1);
    // The recap once enough was answered, or after a question with no choice to press.
    const want =
      asked >= COMPANION_DEMONSTRATION_QUESTIONS || last?.from === 'companion'
        ? 'proposal'
        : 'next';
    const answer = await companion.reply(null, { start: 'idea', want, messages: turns });
    if (answer.kind === 'refused') throw new Error(`the companion refused: ${answer.code}`);
    const reply = answer.body.reply;
    turns.push({ from: 'companion', reply });
    if (reply.next === 'propose') break;
    const choice = reply.question.choices[0];
    if (choice !== undefined) turns.push({ from: 'person', text: choice });
  }
  return {
    version: COMPANION_DEMONSTRATION_VERSION,
    source:
      'Recorded once from the companion on the owner computer; replayed on the headset, never asked.',
    model,
    recorded_at: now.toISOString(),
    start: 'idea',
    turns,
  };
}
