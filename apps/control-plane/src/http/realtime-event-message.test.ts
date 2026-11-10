import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { EntityChanges, EventEnvelope } from '@halcyonic/contracts';
import type { PublishedEvent } from '../core/publisher.ts';
import { eventMessage } from './realtime.ts';

test('an event is serialized once for every connection that receives it (core audit 2026-11-03)', () => {
  const published: PublishedEvent = {
    position: 7,
    event: { event_type: 'project.created', payload: { name: 'P' } } as unknown as EventEnvelope,
    changes: { projects: ['p'] } as unknown as EntityChanges,
  };
  const stringify = JSON.stringify;
  let calls = 0;
  JSON.stringify = ((...args: Parameters<typeof stringify>) => {
    calls += 1;
    return stringify(...args);
  }) as typeof JSON.stringify;
  let first: string;
  let second: string;
  try {
    first = eventMessage(published);
    second = eventMessage(published);
  } finally {
    JSON.stringify = stringify;
  }
  assert.equal(calls, 1);
  assert.equal(second, first);
  assert.deepEqual(JSON.parse(first), {
    type: 'event',
    position: 7,
    event: published.event,
    changes: published.changes,
  });
});
