import {
  type CommandPolicy,
  type CommandType,
  REALTIME_PROTOCOL_VERSION,
  type ServerMessage,
} from '@halcyonic/contracts';
import { COMMAND_POLICY } from '@halcyonic/domain';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { createSeededRandom, createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { silentLogger } from '../logger.ts';
import { parseTrace, replayTrace } from './trace.ts';

type WelcomeMessage = Extract<ServerMessage, { type: 'welcome' }>;
type SnapshotMessage = Extract<ServerMessage, { type: 'snapshot' }>;
type EventMessage = Extract<ServerMessage, { type: 'event' }>;

/**
 * What the XR client plays when no control plane is configured or reachable: exactly the realtime
 * messages a client receives from `pnpm replay` of a trace, and when. Its journal is a `fixture`,
 * so every surface labels it as recorded, and it registers no runtime, so no action is offered.
 */
export interface Demonstration {
  readonly version: 1;
  /** The trace it was recorded from, relative to the repository root. */
  readonly source: string;
  readonly welcome: WelcomeMessage;
  readonly snapshot: SnapshotMessage;
  /** Every event message, with its time from the start of the demonstration. */
  readonly events: readonly { readonly at_ms: number; readonly message: EventMessage }[];
}

const ROOT = new URL('../../../../', import.meta.url);

/** The trace the XR client's demonstration plays. */
export const DEMONSTRATION_SOURCE = 'fixtures/traces/multiple_workstreams.jsonl';
export const DEMONSTRATION_TRACE = new URL(DEMONSTRATION_SOURCE, ROOT);

/** Where the Unity project bundles the demonstration, as a text asset under Resources. */
export const DEMONSTRATION_FILE = new URL(
  'apps/xr/Assets/Halcyonic/Resources/HalcyonicDemonstration.json',
  ROOT,
);

/** `pnpm replay` waits out a trace's recorded gaps, capped at this. */
export const REPLAY_MAX_GAP_MS = 3000;

/** Seeds the journal identity, so the same trace always records the same demonstration. */
const SEED = 12;

/**
 * Records a demonstration by replaying a trace through the real control plane under virtual time:
 * its journal, projection and publisher compute every snapshot and change, so the client that
 * plays it derives nothing. The same trace always produces the same text.
 */
export async function recordDemonstration(traceText: string, source: string): Promise<string> {
  const events = parseTrace(traceText, source);
  const first = events[0];
  if (first === undefined) throw new Error(`${source} holds no events`);

  const time = createVirtualTime(new Date(first.ingested_at));
  const start = time.now().getTime();
  const ids = createUuidV7Generator({
    now: () => time.now().getTime(),
    random: createSeededRandom(SEED),
  });
  const controlPlane = new ControlPlane({
    journal: openSqliteJournal({ path: ':memory:', originIfNew: 'fixture', ids }),
    adapters: [],
    ids,
    clock: time,
    scheduler: time,
    logger: silentLogger,
    commandTimeoutMs: 30_000,
  });
  try {
    // A client connecting before the replay starts gets this welcome and snapshot.
    const snapshot = controlPlane.snapshot();
    const welcome: WelcomeMessage = {
      type: 'welcome',
      protocol: REALTIME_PROTOCOL_VERSION,
      journal: controlPlane.journal.info,
      head: snapshot.position,
      resumed: false,
      server_time: time.now().toISOString(),
      command_policies: commandPolicies(),
    };
    const played: { at_ms: number; message: EventMessage }[] = [];
    controlPlane.publisher.subscribe(({ position, event, changes }) => {
      played.push({
        at_ms: time.now().getTime() - start,
        message: { type: 'event', position, event, changes },
      });
    });
    const replay = replayTrace(controlPlane.recorder, events, {
      pace: 'recorded',
      maxGapMs: REPLAY_MAX_GAP_MS,
      scheduler: time,
    });
    await time.runUntilIdle();
    const result = await replay;
    if (result.duplicates > 0) {
      throw new Error(`${source} repeats ${result.duplicates} events, which a replay drops`);
    }
    return serializeDemonstration({
      version: 1,
      source,
      welcome,
      snapshot: { type: 'snapshot', snapshot },
      events: played,
    });
  } finally {
    await controlPlane.close();
  }
}

/** The command policies every realtime client is welcomed with (http/realtime.ts). */
function commandPolicies(): CommandPolicy[] {
  return Object.entries(COMMAND_POLICY).map(([commandType, policy]) => ({
    command_type: commandType as CommandType,
    policy,
  }));
}

/** JSON with one event per line, so a change to the recording reads like a change to a trace. */
function serializeDemonstration(demonstration: Demonstration): string {
  const events = demonstration.events.map((event) => `    ${JSON.stringify(event)}`);
  return [
    '{',
    `  "version": ${JSON.stringify(demonstration.version)},`,
    `  "source": ${JSON.stringify(demonstration.source)},`,
    `  "welcome": ${JSON.stringify(demonstration.welcome)},`,
    `  "snapshot": ${JSON.stringify(demonstration.snapshot)},`,
    '  "events": [',
    events.join(',\n'),
    '  ]',
    '}',
    '',
  ].join('\n');
}
