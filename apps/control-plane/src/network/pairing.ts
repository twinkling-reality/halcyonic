import { randomBytes } from 'node:crypto';
import {
  type DeviceId,
  type DeviceView,
  MAX_PAIRING_REFUSAL_TALLIES,
  type PairingClientMessage,
  type PairingRefusalReason,
  type PairingRefusalTally,
  type PairingServerMessage,
  type PairingState,
  type PairingStatus,
} from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import type { Logger } from '../logger.ts';
import type { DeviceAccess } from './devices.ts';
import { WindowCounter } from './limits.ts';
import {
  clientProof,
  codePassword,
  createPairingCode,
  pairingTranscript,
  sameMac,
  sealCredential,
  serverProof,
} from './pairing-protocol.ts';
import { fromBytes, PAIRING_GROUP, PAIRING_IDENTITY, pad, SrpServer, verifier } from './srp.ts';

export interface PairingLimits {
  /** How long a window stays open. */
  readonly windowMs: number;
  /** Failed proofs that close a window. */
  readonly maxFailedAttempts: number;
  /** How long one exchange may take, from the upgrade to the answer. */
  readonly attemptTimeoutMs: number;
  /** Exchanges in progress at once, from all addresses. */
  readonly maxConcurrentAttempts: number;
  /** Pairing connections one address may open in a minute. */
  readonly attemptsPerAddressPerMinute: number;
}

/** ADR 0017: five minutes, three failures, 30 seconds an exchange, four at once, six a minute. */
export const DEFAULT_PAIRING_LIMITS: PairingLimits = {
  windowMs: 5 * 60_000,
  maxFailedAttempts: 3,
  attemptTimeoutMs: 30_000,
  maxConcurrentAttempts: 4,
  attemptsPerAddressPerMinute: 6,
};

export interface PairingRefusal {
  /** The HTTP status when the refusal comes before the WebSocket upgrade. */
  readonly status: 403 | 429;
  readonly code: string;
  readonly message: string;
}

interface Window {
  readonly expiresAt: number;
  /**
   * The salt and SRP verifier every attempt in the window uses, derived from the code once when
   * the window opens, so an attempt's work depends only on its own random secret and never on the
   * code. The code itself is not kept.
   */
  readonly salt: Buffer;
  readonly verifier: bigint;
  state: PairingState;
  failures: number;
  deviceId: DeviceId | null;
  /** Connections refused or cut short without a code checked, by reason and address, oldest first. */
  readonly refusals: Map<string, PairingRefusalTally>;
}

/** What an exchange answers a message with, and whether that ends it. */
export interface PairingAnswer {
  readonly reply: PairingServerMessage;
  readonly done: boolean;
}

/**
 * The pairing window the owner opens from loopback (ADR 0017). It holds the code, which is never
 * logged or journaled, and admits one exchange per connection on the network listener's `/pair`.
 * Every check and change of the window happens synchronously, so concurrent attempts cannot get
 * more guesses than the window allows.
 */
export class Pairing {
  readonly #clock: Clock;
  readonly #devices: DeviceAccess;
  readonly #describe: (deviceId: DeviceId) => DeviceView | undefined;
  readonly #certificateSha256: string;
  readonly #logger: Logger;
  readonly limits: PairingLimits;
  readonly #perAddress: WindowCounter;
  readonly #active = new Map<string, number>();
  #window: Window | null = null;

  constructor(options: {
    clock: Clock;
    devices: DeviceAccess;
    describe: (deviceId: DeviceId) => DeviceView | undefined;
    /** The network listener's certificate, which devices must have seen for their proof to hold. */
    certificateSha256: string;
    logger: Logger;
    limits?: PairingLimits;
  }) {
    this.#clock = options.clock;
    this.#devices = options.devices;
    this.#describe = options.describe;
    this.#certificateSha256 = options.certificateSha256;
    this.#logger = options.logger;
    this.limits = options.limits ?? DEFAULT_PAIRING_LIMITS;
    this.#perAddress = new WindowCounter(
      options.clock,
      this.limits.attemptsPerAddressPerMinute,
      60_000,
    );
  }

  /** Opens a window with a new code, closing any window still open. */
  open(): { code: string; status: PairingStatus } {
    if (this.#current()?.state === 'open') this.#end('closed', 'another window replaced it');
    const code = createPairingCode();
    const salt = randomBytes(16);
    const window: Window = {
      expiresAt: this.#now() + this.limits.windowMs,
      salt,
      verifier: verifier(PAIRING_GROUP, PAIRING_IDENTITY, codePassword(code), salt),
      state: 'open',
      failures: 0,
      deviceId: null,
      refusals: new Map(),
    };
    this.#window = window;
    this.#logger.info(
      { expires_at: new Date(window.expiresAt).toISOString() },
      'pairing window opened',
    );
    return { code, status: this.status() };
  }

  /** Closes the window, if it is open. */
  close(): PairingStatus {
    if (this.#current()?.state === 'open') this.#end('closed', 'the owner closed it');
    return this.status();
  }

  status(): PairingStatus {
    const window = this.#current();
    const device = window?.deviceId == null ? undefined : this.#describe(window.deviceId);
    return {
      state: window?.state ?? 'closed',
      expires_at: window === null ? null : new Date(window.expiresAt).toISOString(),
      failed_attempts: window?.failures ?? 0,
      max_failed_attempts: this.limits.maxFailedAttempts,
      device: device ?? null,
      refusals: window === null ? [] : [...window.refusals.values()],
    };
  }

  /** Before a `/pair` upgrade: refuses cheaply when no window is open or an address floods. */
  admit(address: string): PairingRefusal | null {
    if (this.#current()?.state !== 'open') {
      return {
        status: 403,
        code: 'pairing_closed',
        message: 'No pairing is open. Run pnpm pair on the control plane, then try again.',
      };
    }
    if (!this.#perAddress.take(address)) {
      this.tally(this.#window, address, 'too_many_requests');
      return {
        status: 429,
        code: 'too_many_requests',
        message: 'Too many pairing attempts from this address; wait a minute.',
      };
    }
    return null;
  }

  /** After the upgrade: one exchange for this connection, or why there is none. */
  begin(address: string): PairingAttempt | PairingRefusal {
    const window = this.#current();
    if (window === null || window.state !== 'open') return closedRefusal();
    let total = 0;
    for (const count of this.#active.values()) total += count;
    if (total >= this.limits.maxConcurrentAttempts || (this.#active.get(address) ?? 0) > 0) {
      this.tally(window, address, 'busy');
      return {
        status: 429,
        code: 'busy',
        message: 'Another pairing attempt is in progress; try again in a moment.',
      };
    }
    this.#active.set(address, 1);
    let ended = false;
    return new PairingAttempt(this, window, address, () => {
      if (ended) return;
      ended = true;
      this.#active.delete(address);
    });
  }

  /**
   * Counts a connection `window` refused or cut short without checking a code, so the owner can
   * see something hold pairing up. Only an open window counts; the first of each reason from an
   * address is logged.
   */
  tally(window: Window | null, address: string, reason: PairingRefusalReason): void {
    if (window === null || !this.isOpen(window)) return;
    const from = address === '' ? 'unknown' : address.slice(0, 64);
    const key = `${reason} ${from}`;
    const previous = window.refusals.get(key);
    window.refusals.delete(key);
    window.refusals.set(key, {
      address: from,
      reason,
      count: (previous?.count ?? 0) + 1,
      last_at: this.#clock.now().toISOString(),
    });
    for (const oldest of window.refusals.keys()) {
      if (window.refusals.size <= MAX_PAIRING_REFUSAL_TALLIES) break;
      window.refusals.delete(oldest);
    }
    if (previous === undefined) {
      this.#logger.warn({ address: from, reason }, 'pairing connection refused');
    }
  }

  /** Answers a device's proof for `window`: records the device, or counts a failure. */
  settle(
    window: Window,
    verified: boolean,
    label: string,
  ): { ok: true; deviceId: DeviceId; credential: Buffer } | { ok: false; attemptsLeft: number } {
    if (!verified) {
      window.failures += 1;
      const attemptsLeft = Math.max(0, this.limits.maxFailedAttempts - window.failures);
      this.#logger.warn(
        { failed_attempts: window.failures, attempts_left: attemptsLeft },
        'pairing attempt failed',
      );
      if (attemptsLeft === 0) this.#end('locked', 'too many failed attempts');
      return { ok: false, attemptsLeft };
    }
    const paired = this.#devices.pair(label, this.#certificateSha256);
    window.deviceId = paired.deviceId;
    this.#end('paired', 'a device paired');
    return { ok: true, ...paired };
  }

  /** Whether a window is still the open one, so an attempt from a closed window stops. */
  isOpen(window: Window): boolean {
    return this.#current() === window && window.state === 'open';
  }

  get certificateSha256(): string {
    return this.#certificateSha256;
  }

  /** The window, marked expired once its time is up. */
  #current(): Window | null {
    const window = this.#window;
    if (window?.state === 'open' && this.#now() >= window.expiresAt) {
      this.#end('expired', 'its time ran out');
    }
    return this.#window;
  }

  #end(state: Exclude<PairingState, 'open'>, reason: string): void {
    const window = this.#window;
    if (window === null || window.state !== 'open') return;
    window.state = state;
    this.#logger.info({ state, failed_attempts: window.failures, reason }, 'pairing window closed');
  }

  #now(): number {
    return this.#clock.now().getTime();
  }
}

/**
 * One exchange on one `/pair` connection: `pair_request`, then `pair_proof`. The window's salt and
 * verifier with a fresh SRP secret for every attempt make a recorded exchange useless to replay.
 */
export class PairingAttempt {
  readonly #pairing: Pairing;
  readonly #window: Window;
  readonly #address: string;
  readonly #release: () => void;
  #phase: 'request' | 'proof' | 'done' = 'request';
  #label = '';
  #server: SrpServer | null = null;

  constructor(pairing: Pairing, window: Window, address: string, release: () => void) {
    this.#pairing = pairing;
    this.#window = window;
    this.#address = address;
    this.#release = release;
  }

  /** The answer to one message, and whether the exchange is over. */
  receive(message: PairingClientMessage): PairingAnswer {
    if (this.#phase === 'done') return this.#after();
    if (!this.#pairing.isOpen(this.#window)) return this.#finish(refused(closedRefusal()));
    if (message.type === 'pair_request' && this.#phase === 'request') {
      this.#label = message.device_label;
      this.#server = SrpServer.forVerifier(PAIRING_GROUP, this.#window.verifier);
      this.#phase = 'proof';
      return {
        reply: {
          type: 'pair_challenge',
          salt: this.#window.salt.toString('base64'),
          server_public: pad(PAIRING_GROUP, this.#server.B).toString('base64'),
        },
        done: false,
      };
    }
    if (message.type === 'pair_proof' && this.#phase === 'proof' && this.#server !== null) {
      return this.#finish(this.#prove(this.#server, message));
    }
    return this.reject('invalid_message', 'Send pair_request, then pair_proof, once each.');
  }

  /** Refuses a message outside the protocol, and counts it against the address. */
  reject(reason: 'invalid_message' | 'unsupported_protocol', message: string): PairingAnswer {
    if (this.#phase === 'done') return this.#after();
    return this.#finish(refused({ code: reason, message }), reason);
  }

  /** Ends an exchange whose time ran out, and counts it against the address. */
  timeOut(): PairingAnswer {
    if (this.#phase === 'done') return this.#after();
    return this.#finish(
      refused({ code: 'timeout', message: 'Pairing took too long; start again on the device.' }),
      'timeout',
    );
  }

  /** Ends an exchange the control plane failed; the window stays as it was. */
  fail(): PairingAnswer {
    if (this.#phase === 'done') return this.#after();
    return this.#finish(
      refused({ code: 'internal_error', message: 'The control plane failed to pair.' }),
    );
  }

  /** The connection closed: ends the exchange, counting it when it closed before its answer. */
  closed(): void {
    if (this.#phase === 'done') return;
    this.#phase = 'done';
    this.#release();
    this.#pairing.tally(this.#window, this.#address, 'abandoned');
  }

  #prove(
    server: SrpServer,
    message: Extract<PairingClientMessage, { type: 'pair_proof' }>,
  ): PairingServerMessage {
    const A = fromBytes(Buffer.from(message.client_public, 'base64'));
    const key = server.sessionKey(A);
    const certificate = Buffer.from(this.#pairing.certificateSha256, 'hex');
    const transcript = pairingTranscript(this.#label, this.#window.salt, A, server.B, certificate);
    const proof = Buffer.from(message.proof, 'base64');
    // An invalid A is an attack, not a typo; it counts as a failed attempt all the same.
    const verified = key !== null && sameMac(clientProof(key, transcript), proof);
    const outcome = this.#pairing.settle(this.#window, verified, this.#label);
    if (!outcome.ok || key === null) {
      return {
        type: 'pair_refused',
        error: {
          code: 'wrong_code',
          message:
            outcome.ok || outcome.attemptsLeft > 0
              ? 'The code was not accepted: it was wrong, or something between this device and the control plane intercepted the connection.'
              : 'The code was not accepted, and pairing closed after too many failed attempts. Run pnpm pair again for a new code.',
        },
        attempts_left: outcome.ok ? null : outcome.attemptsLeft,
      };
    }
    const sealed = sealCredential(outcome.credential, key, transcript);
    return {
      type: 'pair_accepted',
      device_id: outcome.deviceId,
      credential: sealed.toString('base64'),
      proof: serverProof(key, transcript, proof, outcome.deviceId, sealed).toString('base64'),
    };
  }

  #finish(reply: PairingServerMessage, counted?: PairingRefusalReason): PairingAnswer {
    this.#phase = 'done';
    this.#release();
    if (counted !== undefined) this.#pairing.tally(this.#window, this.#address, counted);
    return { reply, done: true };
  }

  /** A message after the exchange ended, which only the closing connection can still carry. */
  #after(): PairingAnswer {
    return {
      reply: refused({ code: 'invalid_message', message: 'This pairing exchange has ended.' }),
      done: true,
    };
  }
}

export function refused(
  error: { code: string; message: string },
  attemptsLeft: number | null = null,
): Extract<PairingServerMessage, { type: 'pair_refused' }> {
  return {
    type: 'pair_refused',
    error: { code: error.code, message: error.message },
    attempts_left: attemptsLeft,
  };
}

function closedRefusal(): PairingRefusal {
  return {
    status: 403,
    code: 'pairing_closed',
    message: 'Pairing is closed. Run pnpm pair on the control plane, then try again.',
  };
}
