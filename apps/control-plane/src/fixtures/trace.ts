import { type EventEnvelope, parseEventEnvelope } from '@halcyonic/contracts';
import type { Scheduler } from '@halcyonic/runtime-core';
import type { Recorder } from '../core/recorder.ts';

/**
 * A trace is a recorded journal in JSON Lines form: one event envelope per line, in journal
 * order. Traces are sanitized development fixtures and must never contain credentials or
 * private repository content.
 */
export function serializeTrace(events: readonly EventEnvelope[]): string {
  return events.map((event) => `${JSON.stringify(event)}\n`).join('');
}

export class TraceError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'TraceError';
  }
}

export function parseTrace(text: string, source: string): EventEnvelope[] {
  const events: EventEnvelope[] = [];
  const lines = text.split('\n');
  for (const [index, line] of lines.entries()) {
    if (line.trim() === '') continue;
    let value: unknown;
    try {
      value = JSON.parse(line);
    } catch {
      throw new TraceError(`${source}:${index + 1}: not valid JSON`);
    }
    const parsed = parseEventEnvelope(value);
    if (!parsed.ok) {
      const details = parsed.issues.map((issue) => `${issue.path} ${issue.message}`).join('; ');
      throw new TraceError(`${source}:${index + 1}: ${details}`);
    }
    events.push(parsed.value);
  }
  return events;
}

export interface ReplayOptions {
  /** `recorded` waits out the recorded gaps between events, capped at `maxGapMs`. */
  readonly pace: 'instant' | 'recorded';
  readonly maxGapMs: number;
  readonly scheduler: Scheduler;
  readonly signal?: AbortSignal;
}

/**
 * Feeds a trace through the normal write path, so replayed events are validated, journaled,
 * projected and streamed exactly like live ones. Events keep their recorded identity, which
 * makes replaying the same trace twice a no-op.
 */
export async function replayTrace(
  recorder: Recorder,
  events: readonly EventEnvelope[],
  options: ReplayOptions,
): Promise<{ recorded: number; duplicates: number }> {
  let recorded = 0;
  let duplicates = 0;
  let previous: number | null = null;
  for (const event of events) {
    const at = Date.parse(event.ingested_at);
    if (options.pace === 'recorded' && previous !== null && at > previous) {
      await options.scheduler.sleep(Math.min(at - previous, options.maxGapMs), options.signal);
    }
    previous = at;
    if (recorder.import(event).status === 'recorded') recorded += 1;
    else duplicates += 1;
  }
  return { recorded, duplicates };
}
