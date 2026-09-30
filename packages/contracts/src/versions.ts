/**
 * Versions of the external contracts. Bump a version only for an incompatible change, and migrate
 * fixtures and persisted data in the same change (see docs/internal/architecture/EVENTS.md).
 */
export const EVENT_SCHEMA_VERSION = 1;
export const COMMAND_SCHEMA_VERSION = 1;
export const REALTIME_PROTOCOL_VERSION = 1;
/** The pairing exchange on the network listener's `/pair` WebSocket (ADR 0017). */
export const PAIRING_PROTOCOL_VERSION = 1;
