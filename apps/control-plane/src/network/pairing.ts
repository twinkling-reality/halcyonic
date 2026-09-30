import { randomBytes } from 'node:crypto';
import type {
  DeviceId,
  DeviceView,
  PairingClientMessage,
  PairingServerMessage,
  PairingState,
  PairingStatus,
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
import { fromBytes, PAIRING_GROUP, PAIRING_IDENTITY, pad, SrpServer } from './srp.ts';

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
  readonly code: string;
  readonly expiresAt: number;
  state: PairingState;
  failures: number;
  deviceId: DeviceId | null;
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
    const window: Window = {
      code: createPairingCode(),
      expiresAt: this.#now() + this.limits.windowMs,
      state: 'open',
      failures: 0,
      deviceId: null,
    };
    this.#window = window;
    this.#logger.info(
      { expires_at: new Date(window.expiresAt).toISOString() },
      'pairing window opened',
    );
    return { code: window.code, status: this.status() };
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
      return {
        status: 429,
        code: 'busy',
        message: 'Another pairing attempt is in progress; try again in a moment.',
      };
    }
    this.#active.set(address, 1);
    let ended = false;
    return new PairingAttempt(this, window, () => {
      if (ended) return;
      ended = true;
      this.#active.delete(address);
    });
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
 * One exchange on one `/pair` connection: `pair_request`, then `pair_proof`. A fresh salt and SRP
 * secret for every attempt make a recorded exchange useless to replay.
 */
export class PairingAttempt {
  readonly #pairing: Pairing;
  readonly #window: Window;
  readonly #release: () => void;
  #phase: 'request' | 'proof' | 'done' = 'request';
  #label = '';
  #salt: Buffer = Buffer.alloc(0);
  #server: SrpServer | null = null;

  constructor(pairing: Pairing, window: Window, release: () => void) {
    this.#pairing = pairing;
    this.#window = window;
    this.#release = release;
  }

  /** The answer to one message, and whether the exchange is over. */
  receive(message: PairingClientMessage): { reply: PairingServerMessage; done: boolean } {
    if (!this.#pairing.isOpen(this.#window)) return this.#finish(refused(closedRefusal()));
    if (message.type === 'pair_request' && this.#phase === 'request') {
      this.#label = message.device_label;
      this.#salt = randomBytes(16);
      this.#server = SrpServer.create(
        PAIRING_GROUP,
        PAIRING_IDENTITY,
        codePassword(this.#window.code),
        this.#salt,
      );
      this.#phase = 'proof';
      return {
        reply: {
          type: 'pair_challenge',
          salt: this.#salt.toString('base64'),
          server_public: pad(PAIRING_GROUP, this.#server.B).toString('base64'),
        },
        done: false,
      };
    }
    if (message.type === 'pair_proof' && this.#phase === 'proof' && this.#server !== null) {
      return this.#finish(this.#prove(this.#server, message));
    }
    return this.#finish(
      refused({
        code: 'invalid_message',
        message: 'Send pair_request, then pair_proof, once each.',
      }),
    );
  }

  /** Ends the exchange, for example when its connection closes. */
  end(): void {
    this.#phase = 'done';
    this.#release();
  }

  #prove(
    server: SrpServer,
    message: Extract<PairingClientMessage, { type: 'pair_proof' }>,
  ): PairingServerMessage {
    const A = fromBytes(Buffer.from(message.client_public, 'base64'));
    const key = server.sessionKey(A);
    const certificate = Buffer.from(this.#pairing.certificateSha256, 'hex');
    const transcript = pairingTranscript(this.#label, this.#salt, A, server.B, certificate);
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

  #finish(reply: PairingServerMessage): { reply: PairingServerMessage; done: boolean } {
    this.end();
    return { reply, done: true };
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
