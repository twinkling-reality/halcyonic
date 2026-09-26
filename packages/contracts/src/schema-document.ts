import type { TSchema } from 'typebox';
import {
  CommandSubmissionResponse,
  ErrorResponse,
  EventsResponse,
  HealthResponse,
  ProjectsResponse,
  RuntimesResponse,
  ValidationIssueSchema,
  WorkstreamsResponse,
} from './api.ts';
import { CommandEnvelope, CommandFailure, CommandRejection, CommandResult } from './commands.ts';
import { EventEnvelope, EventSource, Provenance, StoredEvent } from './events.ts';
import { ClientInfo, ErrorInfo } from './primitives.ts';
import { ClientMessage, ServerMessage } from './realtime.ts';
import { RuntimeCapabilities, RuntimeDescriptor, RuntimeRef } from './runtime.ts';
import {
  COMMAND_SCHEMA_VERSION,
  EVENT_SCHEMA_VERSION,
  REALTIME_PROTOCOL_VERSION,
} from './versions.ts';
import {
  ApprovalView,
  Attention,
  CommandView,
  EntityChanges,
  ExecutionView,
  JournalInfo,
  ProjectView,
  Snapshot,
  TestRunResultView,
  TestRunView,
  ToolActivityView,
  WorkstreamView,
} from './views.ts';

/**
 * Definitions published by name. Anywhere one of them appears inside another, the document
 * references it instead of repeating it, so generated bindings get one type per concept.
 */
const NAMED_DEFINITIONS: Readonly<Record<string, TSchema>> = {
  EventEnvelope,
  StoredEvent,
  EventSource,
  Provenance,
  CommandEnvelope,
  CommandRejection,
  CommandFailure,
  CommandResult,
  ClientInfo,
  ErrorInfo,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeRef,
  JournalInfo,
  Attention,
  ApprovalView,
  ToolActivityView,
  TestRunView,
  TestRunResultView,
  ProjectView,
  WorkstreamView,
  ExecutionView,
  CommandView,
  EntityChanges,
  Snapshot,
  ClientMessage,
  ServerMessage,
  ValidationIssue: ValidationIssueSchema,
  HealthResponse,
  ProjectsResponse,
  WorkstreamsResponse,
  RuntimesResponse,
  EventsResponse,
  CommandSubmissionResponse,
  ErrorResponse,
};

/**
 * The language-neutral form of the contracts for consumers that are not TypeScript (the Unity
 * client, generated bindings, external tools). TypeBox schemas are already JSON Schema, so this
 * is a serialization of the source of truth, not a second definition of it.
 */
export function buildSchemaDocument(): Record<string, unknown> {
  const canonical = createCanonicalizer();
  const nameByShape = new Map<string, string>();
  for (const [name, schema] of Object.entries(NAMED_DEFINITIONS)) {
    const shape = canonical(schema);
    const existing = nameByShape.get(shape);
    if (existing !== undefined) {
      throw new Error(`${name} and ${existing} have the same shape; one name must be dropped`);
    }
    nameByShape.set(shape, name);
  }

  const hoist = (node: unknown, isDefinitionRoot: boolean): unknown => {
    if (Array.isArray(node)) return node.map((item) => hoist(item, false));
    if (node === null || typeof node !== 'object') return node;
    if (!isDefinitionRoot) {
      const name = nameByShape.get(canonical(node));
      if (name !== undefined) return { $ref: `#/$defs/${name}` };
    }
    return Object.fromEntries(
      Object.entries(node).map(([key, value]) => [key, hoist(value, false)]),
    );
  };

  return {
    $schema: 'https://json-schema.org/draft/2020-12/schema',
    $id: 'urn:halcyonic:contracts',
    title: 'Halcyonic contracts',
    description:
      'Generated from packages/contracts/src by `pnpm contracts:emit`. Do not edit by hand.',
    'x-halcyonic-versions': {
      event_schema: EVENT_SCHEMA_VERSION,
      command_schema: COMMAND_SCHEMA_VERSION,
      realtime_protocol: REALTIME_PROTOCOL_VERSION,
    },
    $defs: Object.fromEntries(
      Object.entries(NAMED_DEFINITIONS).map(([name, schema]) => [name, hoist(schema, true)]),
    ),
  };
}

export function renderSchemaDocument(): string {
  return `${JSON.stringify(buildSchemaDocument(), null, 2)}\n`;
}

/** Canonical JSON (sorted keys) of a schema node, memoized by object identity. */
function createCanonicalizer(): (node: object) => string {
  const memo = new WeakMap<object, string>();
  const canonical = (node: unknown): string => {
    if (node === null || typeof node !== 'object') return JSON.stringify(node);
    const cached = memo.get(node);
    if (cached !== undefined) return cached;
    const text = Array.isArray(node)
      ? `[${node.map(canonical).join(',')}]`
      : `{${Object.keys(node)
          .sort()
          .map(
            (key) => `${JSON.stringify(key)}:${canonical((node as Record<string, unknown>)[key])}`,
          )
          .join(',')}}`;
    memo.set(node, text);
    return text;
  };
  return canonical;
}
