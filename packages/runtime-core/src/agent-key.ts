/**
 * An agent app that runs on the person's own key (ADR 0053, amendment of 2026-11-12): what it reads the key from,
 * where the host keeps it, and the words the Mac's page draws for it. The app's own package declares it, so the host
 * lists it whether or not the app is on, and a page for keys draws whatever is declared, naming no app of its own.
 */
export interface AgentKey {
  /** The runtime the key is for, as its adapter's descriptor names it. */
  readonly runtimeId: string;
  /** The agent app's name, as the page says it. */
  readonly app: string;
  /** The environment variable the app reads the key from. */
  readonly variable: string;
  /** The private file in the host's data folder the key is kept in (mode 600, the folder 700). */
  readonly file: string;
  /** The words for the field the person types or pastes the key into. */
  readonly field: string;
}
