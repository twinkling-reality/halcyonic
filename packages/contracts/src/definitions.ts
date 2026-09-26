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
import {
  ApprovalDecision,
  CommandEnvelope,
  CommandFailure,
  CommandRejection,
  CommandResult,
  CommandType,
  PolicyCategory,
  ReceivedVia,
  RejectionCode,
} from './commands.ts';
import {
  ApprovalSubject,
  ControlPlaneSource,
  EventEnvelope,
  EventSource,
  ExecutionStateUnknown,
  Provenance,
  RUNTIME_EVENT_PAYLOADS,
  RuntimeSource,
  StoredEvent,
} from './events.ts';
import { ClientInfo, ErrorInfo } from './primitives.ts';
import {
  ClientMessage,
  CommandAckMessage,
  CommandPolicy,
  ResumeCursor,
  ServerMessage,
} from './realtime.ts';
import { RuntimeCapabilities, RuntimeDescriptor, RuntimeRef } from './runtime.ts';
import {
  ApprovalView,
  Attention,
  AttentionLevel,
  AttentionReason,
  CommandStatus,
  CommandView,
  EntityChanges,
  ExecutionStatus,
  ExecutionView,
  JournalInfo,
  ProjectView,
  Snapshot,
  TestRunResultView,
  TestRunView,
  ToolActivityView,
  WorkstreamStatus,
  WorkstreamView,
} from './views.ts';

/**
 * Definitions published by name. Wherever a schema with the same shape appears, the schema
 * document references the definition instead of repeating it, and generated bindings use one type
 * for it, so every language calls each concept by the same name.
 */
export const NAMED_DEFINITIONS: Readonly<Record<string, TSchema>> = {
  EventEnvelope,
  StoredEvent,
  EventSource,
  ControlPlaneSource,
  RuntimeSource,
  Provenance,
  ApprovalSubject,
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
  AttentionReason,
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
  ResumeCursor,
  CommandPolicy,
  ClientMessage,
  ServerMessage,
  ValidationIssue: ValidationIssueSchema,
  ErrorBody: ErrorResponse.properties.error,
  HealthResponse,
  ProjectsResponse,
  WorkstreamsResponse,
  RuntimesResponse,
  EventsResponse,
  CommandSubmissionResponse,
  ErrorResponse,
  ExecutionStatus,
  WorkstreamStatus,
  AttentionLevel,
  CommandStatus,
  CommandType,
  PolicyCategory,
  RejectionCode,
  ReceivedVia,
  ApprovalDecision,
  JournalOrigin: JournalInfo.properties.origin,
  TestOutcome: TestRunResultView.properties.outcome,
  ToolOutcome: RUNTIME_EVENT_PAYLOADS['runtime.tool.completed'].properties.outcome,
  ApprovalResolution: RUNTIME_EVENT_PAYLOADS['runtime.approval.resolved'].properties.decision,
  FailureEffect: CommandFailure.properties.effect,
  StateUnknownCode: ExecutionStateUnknown.properties.payload.properties.code,
  CommandAckDisposition: CommandAckMessage.properties.disposition,
  SubmissionDisposition: CommandSubmissionResponse.properties.disposition,
};

export interface DefinitionIndex {
  /** Canonical JSON (sorted keys) of a schema node, memoized by object identity. */
  readonly shapeOf: (node: object) => string;
  /** The published name of the definition with this node's shape, if there is one. */
  readonly nameOf: (node: object) => string | undefined;
}

export function indexDefinitions(): DefinitionIndex {
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

  const nameByShape = new Map<string, string>();
  for (const [name, schema] of Object.entries(NAMED_DEFINITIONS)) {
    const shape = canonical(schema);
    const existing = nameByShape.get(shape);
    if (existing !== undefined) {
      throw new Error(`${name} and ${existing} have the same shape; one name must be dropped`);
    }
    nameByShape.set(shape, name);
  }
  return { shapeOf: canonical, nameOf: (node) => nameByShape.get(canonical(node)) };
}
