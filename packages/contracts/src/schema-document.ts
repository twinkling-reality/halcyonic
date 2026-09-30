import { indexDefinitions, NAMED_DEFINITIONS } from './definitions.ts';
import {
  COMMAND_SCHEMA_VERSION,
  EVENT_SCHEMA_VERSION,
  PAIRING_PROTOCOL_VERSION,
  REALTIME_PROTOCOL_VERSION,
} from './versions.ts';

/**
 * The language-neutral form of the contracts for consumers that are not TypeScript (the Unity
 * client, generated bindings, external tools). TypeBox schemas are already JSON Schema, so this
 * is a serialization of the source of truth, not a second definition of it.
 */
export function buildSchemaDocument(): Record<string, unknown> {
  const { nameOf } = indexDefinitions();

  const hoist = (node: unknown, isDefinitionRoot: boolean): unknown => {
    if (Array.isArray(node)) return node.map((item) => hoist(item, false));
    if (node === null || typeof node !== 'object') return node;
    if (!isDefinitionRoot) {
      const name = nameOf(node);
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
      pairing_protocol: PAIRING_PROTOCOL_VERSION,
    },
    $defs: Object.fromEntries(
      Object.entries(NAMED_DEFINITIONS).map(([name, schema]) => [name, hoist(schema, true)]),
    ),
  };
}

export function renderSchemaDocument(): string {
  return `${JSON.stringify(buildSchemaDocument(), null, 2)}\n`;
}
