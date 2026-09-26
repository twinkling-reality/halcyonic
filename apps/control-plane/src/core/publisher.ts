import type { EntityChanges, EventEnvelope } from '@halcyonic/contracts';
import type { Logger } from '../logger.ts';

export interface PublishedEvent {
  readonly position: number;
  readonly event: EventEnvelope;
  readonly changes: EntityChanges;
}

export type Subscriber = (published: PublishedEvent) => void;

/** Fans newly journaled events out to live subscribers, such as realtime connections. */
export class EventPublisher {
  readonly #subscribers = new Set<Subscriber>();
  readonly #logger: Logger;

  constructor(logger: Logger) {
    this.#logger = logger;
  }

  subscribe(subscriber: Subscriber): () => void {
    this.#subscribers.add(subscriber);
    return () => {
      this.#subscribers.delete(subscriber);
    };
  }

  publish(published: PublishedEvent): void {
    for (const subscriber of [...this.#subscribers]) {
      try {
        subscriber(published);
      } catch (error) {
        // One failing subscriber must not stop delivery to the others or fail the write.
        this.#logger.error({ err: error, position: published.position }, 'event subscriber failed');
      }
    }
  }
}
