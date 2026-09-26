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
 * enumerations and nullability: within major version 1 those never change, so a mismatch means the
 * producer is not speaking this contract.
 */

export const CONSUMER_BASE_PATH = '/consumer/v1';

/** The token scheme Salidium publishes so a consumer can recognize a credential without a request. */
export const CONSUMER_TOKEN_PATTERN = /^salidium_consumer_[0-9a-f]{12}_[0-9a-f]{64}$/;

/** A provider's own session id, as the lookup endpoint accepts it: 1 to 512 characters, no controls. */
// biome-ignore lint/suspicious/noControlCharactersInRegex: excluding control characters is the rule.
export const NATIVE_SESSION_ID_PATTERN = /^[^\u0000-\u001f\u007f]{1,512}$/;

const Count = Type.Integer({ minimum: 0 });
const Id = Type.String({ minLength: 1 });

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
 * `/consumer/v1/discovery`. `baseUrl` is held to the contract's loopback pattern, because it decides
 * where the credential is sent.
 */
export const WireDiscovery = Type.Object({
  format: Type.Literal('salidium.consumer-discovery'),
  version: Type.Literal(1),
  contract: Type.Object({
    name: Type.Literal('salidium.consumer'),
    major: Type.Literal(1),
    minor: Count,
  }),
  salidium: Type.Object({ version: Type.String() }),
  instanceId: Type.String({ pattern: '^[0-9a-f]{32}$' }),
  baseUrl: Type.String({ pattern: '^http://127\\.0\\.0\\.1:\\d{1,5}/consumer/v1$' }),
});
export type WireDiscovery = Static<typeof WireDiscovery>;

export const WireError = Type.Object({
  format: Type.Literal('salidium.consumer-error'),
  version: Type.Literal(1),
  error: Type.Enum([
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

const Statement = Type.Object({
  text: Type.String(),
  provenance: WireEpistemic,
  author: Nullable(Type.Enum(['agent', 'subagent', 'user'])),
  at: Nullable(Timestamp),
});

const VerificationRun = Type.Object({
  at: Timestamp,
  label: Type.String(),
  method: Type.Enum(['test', 'typecheck', 'lint', 'build', 'other']),
  runner: Nullable(Type.String()),
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
  caveats: Type.Array(Type.String()),
  stale: Type.Boolean(),
  laterUnreadable: Count,
});

const Steps = Type.Array(Type.String());

/** `salidium.session-report` version 2. Version 1 is the interface's download and not a contract. */
export const WireReport = Type.Object({
  format: Type.Literal('salidium.session-report'),
  version: Type.Literal(2),
  generatedAt: Timestamp,
  session: Type.Object({ id: Id, native: NativeIdentity, evidenceSeq: Count }),
  verdict: Type.Object({
    headline: Type.String(),
    tone: Type.Enum(['pass', 'fail', 'attention', 'working', 'neutral']),
    provenance: WireEpistemic,
    because: Nullable(Type.String()),
    at: Nullable(Timestamp),
  }),
  latestStatement: Nullable(Statement),
  changes: Type.Object({
    glance: Type.String(),
    files: Type.Array(
      Type.Object({
        path: Type.String(),
        changeCount: Count,
        linesAdded: Count,
        linesRemoved: Count,
        kinds: Type.Array(Type.Enum(['add', 'update', 'delete', 'move'])),
        lastChangedAt: Timestamp,
        coverage: Type.Object({
          verifiedAfter: Type.Boolean(),
          by: Nullable(Type.String()),
          provenance: Type.Literal('inferred'),
        }),
        reason: Nullable(Statement),
      }),
    ),
    commits: Type.Array(Type.Object({ sha: Type.String(), at: Timestamp })),
  }),
  verification: Type.Object({
    glance: Type.String(),
    latestByMethod: Type.Array(VerificationRun),
    unverifiedFiles: Type.Array(Type.String()),
    statements: Type.Array(Statement),
  }),
  review: Type.Object({
    glance: Type.String(),
    open: Count,
    resolved: Count,
    groups: Type.Array(
      Type.Object({
        rule: Type.String(),
        label: Type.String(),
        severity: Type.Enum(['info', 'low', 'medium', 'high']),
        occurrences: Count,
        latestAt: Timestamp,
        items: Type.Array(
          Type.Object({
            label: Type.String(),
            instance: Nullable(Type.String()),
            createdAt: Timestamp,
            provenance: WireEpistemic,
            repeats: Count,
          }),
        ),
      }),
    ),
  }),
  remaining: Type.Object({
    glance: Type.String(),
    items: Type.Array(
      Type.Object({
        text: Type.String(),
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
    model: Nullable(Type.String()),
    content: Nullable(
      Type.Object({
        what: Type.Object({ summary: Type.String(), currently: Nullable(Type.String()) }),
        why: Type.Object({
          summary: Type.String(),
          lanes: Type.Array(Type.Object({ title: Type.String(), steps: Steps })),
          chain: Steps,
        }),
        how: Type.Object({ summary: Type.String(), root: Nullable(Type.String()), steps: Steps }),
        approachChange: Nullable(
          Type.Object({
            from: Type.String(),
            fromSteps: Steps,
            why: Type.String(),
            to: Type.String(),
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
export const validateError = compileValidator(WireError);
export const validateLookup = compileValidator(WireLookup);
export const validateReport = compileValidator(WireReport);
