import { compileValidator, Nullable, Timestamp, type Validated } from '@halcyonic/contracts';
import Type, { type Static } from 'typebox';

/**
 * Salidium's consumer contract, major version 1, as this client reads it.
 *
 * Written from the release candidate of `@salidium/consumer-contract` (1.0.0-rc.0): its schemas,
 * README, ADR 0005 and retained fixtures. Halcyonic depends on the wire contract only, never on
 * Salidium's packages, so these schemas are Halcyonic's own.
 *
 * They are tolerant readers. Each requires only the properties this package uses and leaves every
 * object open, because a later minor version may add properties and feed message types, and a
 * consumer must ignore them. What they do require is checked as the contract states it, including
 * enumerations, nullability and maximum lengths: within major version 1 those never change, and a
 * bound may shrink but never grow, so a mismatch means the producer is not speaking this contract.
 */

export const CONSUMER_BASE_PATH = '/consumer/v1';

/** The token scheme Salidium publishes so a consumer can recognize a credential without a request. */
export const CONSUMER_TOKEN_PATTERN = /^salidium_consumer_[0-9a-f]{12}_[0-9a-f]{64}$/;

/** A provider's own session id, as the lookup endpoint accepts it: 1 to 512 characters, no controls. */
// biome-ignore lint/suspicious/noControlCharactersInRegex: excluding control characters is the rule.
export const NATIVE_SESSION_ID_PATTERN = /^[^\u0000-\u001f\u007f]{1,512}$/;

const Count = Type.Integer({ minimum: 0 });
const Id = Type.String({ minLength: 1, maxLength: 1100 });
/** Salidium's text, bounded as the contract bounds it. */
const Text = (maxLength: number) => Type.String({ maxLength });

export const WireEpistemic = Type.Enum([
  'observed',
  'reported',
  'inferred',
  'planned',
  'explained',
]);
export type WireEpistemic = Static<typeof WireEpistemic>;

/**
 * Written to `$SALIDIUM_HOME/consumer.json` while the daemon runs and served without a credential at
 * `/consumer/v1/discovery`. `contracts` lists every major version the daemon serves; entries for
 * other majors are ignored here, so the array is read loosely and the major 1 entry is checked on
 * its own with `WireContractEntry`.
 */
export const WireDiscovery = Type.Object({
  format: Type.Literal('salidium.consumer-discovery'),
  version: Type.Literal(1),
  contracts: Type.Array(Type.Unknown(), { minItems: 1 }),
  salidium: Type.Object({ version: Text(64) }),
  instanceId: Type.String({ pattern: '^[0-9a-f]{32}$' }),
});
export type WireDiscovery = Static<typeof WireDiscovery>;

/** The discovery entry for major version 1. `baseUrl` decides where the credential is sent. */
export const WireContractEntry = Type.Object({
  name: Type.Literal('salidium.consumer'),
  major: Type.Literal(1),
  minor: Count,
  baseUrl: Type.String({ pattern: '^http://127\\.0\\.0\\.1:\\d{1,5}/consumer/v1$' }),
});
export type WireContractEntry = Static<typeof WireContractEntry>;

/** A provider id as lookup takes it: Salidium's own two, or a namespaced `owner/name`. */
export const PROVIDER_ID_PATTERN =
  '^(?:claude-code|codex|[a-z][a-z0-9-]{0,62}\\/[a-z][a-z0-9-]{0,62})$';

/**
 * The providers a daemon instance observes, from contract 1.1 on: a top-level `providers` list on
 * the discovery document, sorted, unique, at most 32, every one it observes now, Salidium's own
 * two included. It is fixed for the instance's life. Read only beside a major 1 entry of minor 1 or
 * later; each entry is open for facts a later minor adds.
 */
export const WireProviders = Type.Array(
  Type.Object({ id: Type.String({ pattern: PROVIDER_ID_PATTERN }) }),
  { maxItems: 32 },
);
export type WireProviders = Static<typeof WireProviders>;

/** Whether a discovery entry names the contract and major version this client implements. */
export function isMajorOneEntry(entry: unknown): boolean {
  const { name, major } = (entry ?? {}) as { name?: unknown; major?: unknown };
  return name === 'salidium.consumer' && major === 1;
}

export const WireError = Type.Object({
  format: Type.Literal('salidium.consumer-error'),
  version: Type.Literal(1),
  error: Type.Enum([
    'host-not-allowed',
    'origin-not-allowed',
    'unauthorized',
    'not-found',
    'session-not-observed',
    'method-not-allowed',
    'bad-request',
    'internal',
  ]),
});
export type WireError = Static<typeof WireError>;

const NativeIdentity = Type.Object({ provider: Id, sessionId: Id });

export const WireLookup = Type.Object({
  format: Type.Literal('salidium.session-lookup'),
  version: Type.Literal(1),
  session: Type.Object({ id: Id }),
});
export type WireLookup = Static<typeof WireLookup>;

/** Only what the agent or a subagent said crosses; nothing the user wrote does. */
const Statement = Type.Object({
  text: Text(600),
  provenance: WireEpistemic,
  author: Nullable(Type.Enum(['agent', 'subagent'])),
  at: Nullable(Timestamp),
});

const VerificationRun = Type.Object({
  at: Timestamp,
  label: Text(300),
  method: Type.Enum(['test', 'typecheck', 'lint', 'build', 'other']),
  runner: Nullable(Text(120)),
  outcome: Type.Enum(['pass', 'fail', 'partial', 'unknown']),
  counts: Nullable(
    Type.Object({
      passed: Nullable(Count),
      failed: Nullable(Count),
      skipped: Nullable(Count),
      total: Nullable(Count),
    }),
  ),
  scope: Type.Enum(['full', 'partial', 'unknown']),
  exit: Type.Object({
    code: Nullable(Type.Integer()),
    observation: Type.Enum(['explicit', 'inferred-success', 'inferred-failure', 'unknown']),
  }),
  provenance: WireEpistemic,
  caveats: Type.Array(Text(120)),
  stale: Type.Boolean(),
  laterUnreadable: Count,
});

const Step = Text(200);
const Steps = Type.Array(Step);

/** `salidium.session-report` version 2. Version 1 is the interface's download and not a contract. */
export const WireReport = Type.Object({
  format: Type.Literal('salidium.session-report'),
  version: Type.Literal(2),
  generatedAt: Timestamp,
  session: Type.Object({ id: Id, native: NativeIdentity, evidenceSeq: Count }),
  verdict: Type.Object({
    headline: Text(300),
    tone: Type.Enum(['pass', 'fail', 'attention', 'working', 'neutral']),
    provenance: WireEpistemic,
    because: Nullable(Text(600)),
    at: Nullable(Timestamp),
  }),
  latestStatement: Nullable(Statement),
  waiting: Nullable(
    Type.Object({
      kind: Type.Enum(['permission', 'question', 'input']),
      summary: Text(300),
      since: Timestamp,
      provenance: WireEpistemic,
    }),
  ),
  changes: Type.Object({
    glance: Text(300),
    files: Type.Array(
      Type.Object({
        path: Text(4096),
        changeCount: Count,
        linesAdded: Count,
        linesRemoved: Count,
        kinds: Type.Array(Type.Enum(['add', 'update', 'delete', 'move'])),
        lastChangedAt: Timestamp,
        coverage: Type.Object({
          verifiedAfter: Type.Boolean(),
          by: Nullable(Text(300)),
          provenance: Type.Literal('inferred'),
        }),
        reason: Nullable(Statement),
      }),
    ),
    commits: Type.Array(Type.Object({ sha: Text(64), at: Timestamp })),
  }),
  verification: Type.Object({
    glance: Text(300),
    latestByMethod: Type.Array(VerificationRun),
    unverifiedFiles: Type.Array(Text(4096)),
    statements: Type.Array(Statement),
  }),
  review: Type.Object({
    glance: Text(300),
    open: Count,
    resolved: Count,
    groups: Type.Array(
      Type.Object({
        rule: Text(120),
        label: Text(300),
        severity: Type.Enum(['info', 'low', 'medium', 'high']),
        occurrences: Count,
        latestAt: Timestamp,
        items: Type.Array(
          Type.Object({
            label: Text(300),
            instance: Nullable(Text(200)),
            createdAt: Timestamp,
            provenance: WireEpistemic,
            repeats: Count,
          }),
        ),
      }),
    ),
  }),
  remaining: Type.Object({
    glance: Text(300),
    items: Type.Array(
      Type.Object({
        text: Text(600),
        status: Type.Enum(['pending', 'in_progress', 'failing', 'reported']),
        provenance: WireEpistemic,
        source: Type.Enum(['plan', 'verification', 'agent']),
      }),
    ),
  }),
  explanation: Type.Object({
    status: Type.Enum(['generated', 'generating', 'disabled', 'unavailable', 'failed', 'none']),
    provenance: Type.Literal('explained'),
    current: Type.Boolean(),
    basedOnSeq: Nullable(Count),
    generatedAt: Nullable(Timestamp),
    model: Nullable(Text(120)),
    content: Nullable(
      Type.Object({
        what: Type.Object({ summary: Text(600), currently: Nullable(Text(600)) }),
        why: Type.Object({
          summary: Text(600),
          lanes: Type.Array(Type.Object({ title: Text(100), steps: Steps })),
          chain: Steps,
        }),
        how: Type.Object({ summary: Text(600), root: Nullable(Step), steps: Steps }),
        approachChange: Nullable(
          Type.Object({
            from: Step,
            fromSteps: Steps,
            why: Text(600),
            to: Step,
            toSteps: Steps,
          }),
        ),
      }),
    ),
  }),
});
export type WireReport = Static<typeof WireReport>;

const FeedBase = { format: Type.Literal('salidium.session-feed'), version: Type.Literal(1) };

/** The feed messages this version knows. The contract says consumers ignore any other type. */
const FEED_MESSAGES = {
  resync: Type.Object({ ...FeedBase, type: Type.Literal('resync') }),
  'session.changed': Type.Object({
    ...FeedBase,
    type: Type.Literal('session.changed'),
    sessionId: Id,
    native: NativeIdentity,
    evidenceSeq: Count,
  }),
  'session.removed': Type.Object({
    ...FeedBase,
    type: Type.Literal('session.removed'),
    sessionId: Id,
    native: Nullable(NativeIdentity),
  }),
  heartbeat: Type.Object({ ...FeedBase, type: Type.Literal('heartbeat') }),
  closing: Type.Object({
    ...FeedBase,
    type: Type.Literal('closing'),
    reason: Type.Enum(['credential-revoked', 'shutting-down']),
  }),
} as const;

export type WireFeedMessage = {
  [K in keyof typeof FEED_MESSAGES]: Static<(typeof FEED_MESSAGES)[K]>;
}[keyof typeof FEED_MESSAGES];

const FeedEnvelope = Type.Object({ ...FeedBase, type: Type.String() });
const validateFeedEnvelope = compileValidator(FeedEnvelope);
const feedValidators = new Map(
  Object.entries(FEED_MESSAGES).map(([type, schema]) => [type, compileValidator(schema)]),
);

/**
 * Reads one feed message. A message of a type this version does not know returns `null`, to be
 * skipped as the compatibility rules require; anything that is not a version 1 feed message, or a
 * known type that does not validate, is invalid.
 */
export function readFeedMessage(value: unknown): Validated<WireFeedMessage> | null {
  const envelope = validateFeedEnvelope(value);
  if (!envelope.ok) return envelope;
  const validate = feedValidators.get(envelope.value.type);
  if (validate === undefined) return null;
  return validate(value) as Validated<WireFeedMessage>;
}

export const validateDiscovery = compileValidator(WireDiscovery);
export const validateContractEntry = compileValidator(WireContractEntry);
export const validateProviders = compileValidator(WireProviders);
export const validateError = compileValidator(WireError);
export const validateLookup = compileValidator(WireLookup);
export const validateReport = compileValidator(WireReport);
