/**
 * Versions of the external contracts. Bump a version only for an incompatible change, and migrate
 * fixtures and persisted data in the same change (see docs/internal/architecture/EVENTS.md).
 *
 * A contract change an older client cannot read raises REALTIME_PROTOCOL_VERSION in the same batch:
 * a field made optional or nullable that the older client requires, a new value of a closed set it
 * reads, a new event or message type it cannot place. Then the welcome turns that client away in words
 * ("update the headset's app or the control plane") instead of leaving it to fail on every message as
 * unreadable. 2 (2026-11-14): a check's page drawn null (not known), and the day's earlier ones it
 * covers: a tool result's source_cut, a host denial's not_recorded with no length, a refused question's
 * event and a denial's tool name. 3 (2026-11-16): photos and videos in a project (ADR 0066): the events
 * project.media.added, project.media.prepared and execution.instruction.media_given, the media of a
 * project's and a run's views, the media attachment and need, and the media rejection codes; and Watch It
 * Work's execution.files.writing and the run's view of it; a task's view's usage; and the host's answer
 * reason run_temp_read.
 */
export const EVENT_SCHEMA_VERSION = 1;
export const COMMAND_SCHEMA_VERSION = 1;
export const REALTIME_PROTOCOL_VERSION = 3;
/** The pairing exchange on the network listener's `/pair` WebSocket (ADR 0017). */
export const PAIRING_PROTOCOL_VERSION = 1;
