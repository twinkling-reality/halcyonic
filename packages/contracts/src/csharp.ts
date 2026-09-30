import { indexDefinitions, NAMED_DEFINITIONS } from './definitions.ts';
import {
  COMMAND_SCHEMA_VERSION,
  EVENT_SCHEMA_VERSION,
  REALTIME_PROTOCOL_VERSION,
} from './versions.ts';

type Schema = Readonly<Record<string, unknown>>;

interface UnionNaming {
  readonly discriminator: string;
  /** Appended to the PascalCase discriminator value to name each variant's class. */
  readonly suffix: string;
}

/** Every discriminated union in the contracts, and how C# names its variants. */
const UNIONS: Readonly<Record<string, UnionNaming>> = {
  ServerMessage: { discriminator: 'type', suffix: 'Message' },
  ClientMessage: { discriminator: 'type', suffix: 'Message' },
  EventEnvelope: { discriminator: 'event_type', suffix: 'Event' },
  CommandEnvelope: { discriminator: 'command_type', suffix: 'Command' },
  EventSource: { discriminator: 'kind', suffix: 'Source' },
  Provenance: { discriminator: 'epistemic', suffix: 'Provenance' },
  AttentionReason: { discriminator: 'kind', suffix: 'Reason' },
  CommandResult: { discriminator: 'kind', suffix: 'Result' },
  ApprovalSubject: { discriminator: 'kind', suffix: 'Subject' },
  UnderstandingResult: { discriminator: 'availability', suffix: 'Understanding' },
  EvaluationResult: { discriminator: 'availability', suffix: 'Evaluation' },
  RuntimeModelsResult: { discriminator: 'availability', suffix: 'Models' },
};

/** The documents a C# client reads or writes. Everything they reference is generated too. */
const ROOTS: readonly string[] = [
  'ServerMessage',
  'ClientMessage',
  'Snapshot',
  'HealthResponse',
  'ProjectsResponse',
  'WorkstreamsResponse',
  'RuntimesResponse',
  'RuntimeModelsResponse',
  'EventsResponse',
  'CommandSubmissionResponse',
  'ErrorResponse',
  'UnderstandingResponse',
  'EvaluationResponse',
];

interface CsType {
  /** The C# type, without nullability. */
  readonly name: string;
  readonly valueType: boolean;
  readonly nullable: boolean;
  /** The schema admits only null. */
  readonly nullOnly: boolean;
}

interface Member {
  readonly wire: string;
  readonly name: string;
  readonly type: CsType;
  /** The value of a constant property, emitted as its initializer. */
  readonly constant: string | number | undefined;
}

interface Variant {
  readonly className: string;
  readonly union: string;
  readonly tag: string;
}

interface Parent {
  readonly union: string;
  readonly tag: string;
  /** Wire properties the base class declares. */
  readonly inherited: ReadonlySet<string>;
}

class CSharpGenerator {
  readonly #index = indexDefinitions();
  /**
   * Union variants by shape. Variants of different unions may share a shape, as the failures of
   * `UnderstandingResult` and `EvaluationResult` do: each is still its own class under its own
   * union. Such a shape maps to null, because only a union can say which variant it means.
   */
  readonly #variantByShape = new Map<string, Variant | null>();
  readonly #unionByVariant = new Map<string, string>();
  /** Declared C# type names, with the shape each was declared for. */
  readonly #declared = new Map<string, string>();
  readonly #lines: string[] = [];

  constructor() {
    for (const [union, naming] of Object.entries(UNIONS)) {
      for (const option of alternativesOf(definition(union), union)) {
        const tag = tagOf(option, naming.discriminator, union);
        const className = `${pascal(tag)}${naming.suffix}`;
        const published = this.#index.nameOf(option);
        if (published !== undefined && published !== className) {
          throw new Error(
            `${union} variant "${tag}" is published as ${published}, not ${className}`,
          );
        }
        const shape = this.#index.shapeOf(option);
        const shared = this.#variantByShape.has(shape);
        this.#variantByShape.set(shape, shared ? null : { className, union, tag });
        this.#unionByVariant.set(className, union);
      }
    }
  }

  render(): string {
    for (const root of ROOTS) this.#resolve(definition(root), root);
    return [...header(), ...this.#lines, '}', ''].join('\n');
  }

  #resolve(schema: Schema, context: string): CsType {
    const options = schema.anyOf as readonly Schema[] | undefined;
    if (options !== undefined) {
      const present = options.filter((option) => option.type !== 'null');
      if (present.length < options.length) {
        const [only] = present;
        if (only === undefined || present.length > 1) {
          throw new Error(`${context}: a nullable union needs exactly one other alternative`);
        }
        return { ...this.#resolve(only, context), nullable: true };
      }
    }
    if (schema.type === 'null') {
      return { name: 'object', valueType: false, nullable: true, nullOnly: true };
    }

    const variant = this.#variantByShape.get(this.#index.shapeOf(schema));
    if (variant === null) {
      throw new Error(`${context}: variants of several unions have this shape; refer to a union`);
    }
    if (variant !== undefined) {
      this.#resolve(definition(variant.union), variant.union);
      return reference(variant.className);
    }
    const name = this.#index.nameOf(schema) ?? context;
    if (options !== undefined) {
      if (options.every((option) => option.type === 'string' && typeof option.const === 'string')) {
        this.#declareEnum(
          name,
          schema,
          options.map((option) => option.const as string),
        );
        return value(name);
      }
      this.#declareUnion(name, schema, options);
      return reference(name);
    }

    switch (schema.type) {
      case 'string':
        return reference('string');
      case 'integer':
        return value('long');
      case 'number':
        return value(Number.isInteger(schema.const) ? 'long' : 'double');
      case 'boolean':
        return value('bool');
      case 'array': {
        const item = this.#resolve(schema.items as Schema, `${context}Item`);
        return reference(`List<${spell(item)}>`);
      }
      case 'object':
        if (schema.patternProperties !== undefined) return reference('Dictionary<string, JToken>');
        this.#declareClass(name, schema, null);
        return reference(name);
      case undefined:
        return reference('JToken');
      default:
        throw new Error(`${context}: unsupported schema type ${String(schema.type)}`);
    }
  }

  /** Reserves a C# type name. Returns false when the same shape already declared it. */
  #claim(name: string, schema: Schema): boolean {
    const shape = this.#index.shapeOf(schema);
    const existing = this.#declared.get(name);
    if (existing === shape) return false;
    if (existing !== undefined) throw new Error(`two different shapes want the C# name ${name}`);
    this.#declared.set(name, shape);
    return true;
  }

  #declareEnum(name: string, schema: Schema, values: readonly string[]): void {
    if (!this.#claim(name, schema)) return;
    const members = values.map((wire) => ({ wire, name: pascal(wire) }));
    if (new Set(members.map((member) => member.name)).size !== members.length) {
      throw new Error(`${name}: two values have the same C# name`);
    }
    this.#lines.push(
      '',
      '    [JsonConverter(typeof(StringEnumConverter))]',
      `    public enum ${name}`,
      '    {',
      ...members.map((member) => `        [EnumMember(Value = "${member.wire}")] ${member.name},`),
      '    }',
    );
  }

  #declareClass(name: string, schema: Schema, parent: Parent | null): void {
    if (!this.#claim(name, schema)) return;
    const stem = parent === null ? name : pascal(parent.tag);
    const members = this.#members(name, stem, schema).filter(
      (member) => parent === null || !parent.inherited.has(member.wire),
    );
    const body = members.map(renderMember);
    if (parent !== null) {
      body.unshift([`        protected override string Discriminator => "${parent.tag}";`]);
    }
    this.#lines.push(
      '',
      `    public sealed class ${name}${parent === null ? '' : ` : ${parent.union}`}`,
      '    {',
      ...joinBlocks(body),
      '    }',
    );
  }

  #declareUnion(name: string, schema: Schema, options: readonly Schema[]): void {
    const naming = UNIONS[name];
    if (naming === undefined) {
      throw new Error(`${name}: a union of objects needs its discriminator in UNIONS`);
    }
    if (!this.#claim(name, schema)) return;

    const variants = options.map((option) => {
      const tag = tagOf(option, naming.discriminator, name);
      const className = `${pascal(tag)}${naming.suffix}`;
      const members = new Map(
        this.#members(className, pascal(tag), option)
          .filter((member) => member.wire !== naming.discriminator)
          .map((member) => [member.wire, member]),
      );
      return { option, tag, className, members };
    });

    // A property every variant has, with one C# type and the same constant, moves to the base.
    const shared: Member[] = [];
    const [first, ...rest] = variants;
    if (first !== undefined && rest.length > 0) {
      for (const [wire, member] of first.members) {
        const merged = this.#share([member, ...rest.map((variant) => variant.members.get(wire))]);
        if (merged !== null) shared.push(merged);
      }
    }
    const discriminator = pascal(naming.discriminator);
    if (shared.some((member) => member.name === discriminator)) {
      throw new Error(`${name}: a shared property collides with ${discriminator}`);
    }

    const inherited = new Set([naming.discriminator, ...shared.map((member) => member.wire)]);
    this.#lines.push(
      '',
      `    [JsonConverter(typeof(${name}Converter))]`,
      `    public abstract class ${name}`,
      '    {',
      ...joinBlocks([
        [
          `        [JsonProperty("${naming.discriminator}", Order = -2)]`,
          `        public string ${discriminator} => Discriminator;`,
        ],
        ['        protected abstract string Discriminator { get; }'],
        ...shared.map(renderMember),
      ]),
      '    }',
      ...converter(name, naming.discriminator, variants),
    );
    for (const variant of variants) {
      this.#declareClass(variant.className, variant.option, {
        union: name,
        tag: variant.tag,
        inherited,
      });
    }
  }

  #members(owner: string, stem: string, schema: Schema): Member[] {
    const properties = (schema.properties ?? {}) as Readonly<Record<string, Schema>>;
    const required = new Set((schema.required ?? []) as readonly string[]);
    return Object.entries(properties).map(([wire, property]) => {
      if (!required.has(wire)) {
        throw new Error(`${owner}.${wire}: optional properties are not supported; use Nullable`);
      }
      const name = pascal(wire);
      if (name === owner)
        throw new Error(`${owner}.${wire}: a member cannot share its type's name`);
      const constant = property.const;
      return {
        wire,
        name,
        type: this.#resolve(property, `${stem}${name}`),
        constant:
          typeof constant === 'string' || typeof constant === 'number' ? constant : undefined,
      };
    });
  }

  /** The base class form of a property present in every variant, or null when it has none. */
  #share(members: readonly (Member | undefined)[]): Member | null {
    const present = members.filter((member) => member !== undefined);
    const [sample] = present;
    if (sample === undefined || present.length < members.length) return null;
    if (present.some((member) => member.constant !== sample.constant)) return null;
    const typed = present.filter((member) => !member.type.nullOnly);
    const [concrete] = typed;
    if (concrete === undefined) return null;
    const nullable = present.some((member) => member.type.nullable);
    if (typed.every((member) => member.type.name === concrete.type.name)) {
      return { ...sample, type: { ...concrete.type, nullable } };
    }
    const unions = new Set(typed.map((member) => this.#unionByVariant.get(member.type.name)));
    const [union] = unions;
    if (unions.size !== 1 || union === undefined) return null;
    return { ...sample, type: { name: union, valueType: false, nullable, nullOnly: false } };
  }
}

function converter(
  union: string,
  discriminator: string,
  variants: readonly { tag: string; className: string }[],
): string[] {
  return [
    '',
    `    public sealed class ${union}Converter : JsonConverter`,
    '    {',
    '        public override bool CanWrite => false;',
    '',
    `        public override bool CanConvert(Type objectType) => typeof(${union}).IsAssignableFrom(objectType);`,
    '',
    '        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)',
    '        {',
    '            if (reader.TokenType == JsonToken.Null) return null;',
    '            var item = JObject.Load(reader);',
    `            var token = item["${discriminator}"];`,
    '            var tag = token != null && token.Type == JTokenType.String ? (string?)token : null;',
    `            ${union} value = tag switch`,
    '            {',
    ...variants.map((variant) => `                "${variant.tag}" => new ${variant.className}(),`),
    '                _ => throw new JsonSerializationException(tag == null',
    `                    ? "${union} has no string ${discriminator}."`,
    `                    : "Unknown ${discriminator} \\"" + tag + "\\" for ${union}."),`,
    '            };',
    '            if (!objectType.IsInstanceOfType(value))',
    '            {',
    '                throw new JsonSerializationException(',
    `                    "Expected " + objectType.Name + " but ${discriminator} is \\"" + tag + "\\".");`,
    '            }',
    '            using (var itemReader = item.CreateReader())',
    '            {',
    '                serializer.Populate(itemReader, value);',
    '            }',
    '            return value;',
    '        }',
    '',
    '        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>',
    '            throw new NotSupportedException("Variants serialize as themselves.");',
    '    }',
  ];
}

function renderMember(member: Member): string[] {
  const required = member.type.nullable ? 'Required.AllowNull' : 'Required.Always';
  let initializer = '';
  if (member.constant !== undefined) {
    initializer = ` = ${JSON.stringify(member.constant)};`;
  } else if (!member.type.nullable && !member.type.valueType) {
    initializer = /^(List|Dictionary)</.test(member.type.name)
      ? ` = new ${member.type.name}();`
      : ' = default!;';
  }
  return [
    `        [JsonProperty("${member.wire}", Required = ${required})]`,
    `        public ${spell(member.type)} ${member.name} { get; set; }${initializer}`,
  ];
}

function joinBlocks(blocks: readonly (readonly string[])[]): string[] {
  return blocks.flatMap((block, index) => (index === 0 ? [...block] : ['', ...block]));
}

function spell(type: CsType): string {
  return type.nullable ? `${type.name}?` : type.name;
}

function reference(name: string): CsType {
  return { name, valueType: false, nullable: false, nullOnly: false };
}

function value(name: string): CsType {
  return { name, valueType: true, nullable: false, nullOnly: false };
}

function definition(name: string): Schema {
  const schema = NAMED_DEFINITIONS[name];
  if (schema === undefined) throw new Error(`${name} is not a named definition`);
  return schema as Schema;
}

function alternativesOf(schema: Schema, union: string): readonly Schema[] {
  const options = schema.anyOf as readonly Schema[] | undefined;
  if (options === undefined) throw new Error(`${union} is not a union`);
  return options;
}

function tagOf(option: Schema, discriminator: string, union: string): string {
  const properties = (option.properties ?? {}) as Readonly<Record<string, Schema>>;
  const tag = properties[discriminator]?.const;
  if (typeof tag !== 'string')
    throw new Error(`${union}: a variant has no constant ${discriminator}`);
  return tag;
}

function pascal(text: string): string {
  return text
    .split(/[^A-Za-z0-9]+/)
    .filter((part) => part.length > 0)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
}

function header(): string[] {
  return [
    '// <auto-generated>',
    '// Generated from packages/contracts/src by `pnpm contracts:emit`. Do not edit by hand.',
    '// </auto-generated>',
    '#nullable enable',
    '',
    'using System;',
    'using System.Collections.Generic;',
    'using System.Globalization;',
    'using System.Runtime.Serialization;',
    'using Newtonsoft.Json;',
    'using Newtonsoft.Json.Converters;',
    'using Newtonsoft.Json.Linq;',
    '',
    'namespace Halcyonic.Contracts',
    '{',
    '    public static class ContractVersions',
    '    {',
    `        public const int EventSchema = ${EVENT_SCHEMA_VERSION};`,
    `        public const int CommandSchema = ${COMMAND_SCHEMA_VERSION};`,
    `        public const int RealtimeProtocol = ${REALTIME_PROTOCOL_VERSION};`,
    '    }',
    '',
    '    /// <summary>',
    '    /// Serializer settings that match the wire contract. Timestamps stay strings, type metadata is',
    '    /// ignored, and nulls are written explicitly. Tolerant reading ignores unknown properties;',
    '    /// strict reading, for tests, rejects them.',
    '    /// </summary>',
    '    public static class HalcyonicJson',
    '    {',
    '        public static readonly JsonSerializerSettings Tolerant = CreateSettings(strict: false);',
    '        public static readonly JsonSerializerSettings Strict = CreateSettings(strict: true);',
    '',
    '        public static JsonSerializerSettings CreateSettings(bool strict) => new JsonSerializerSettings',
    '        {',
    '            DateParseHandling = DateParseHandling.None,',
    '            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,',
    '            MissingMemberHandling = strict ? MissingMemberHandling.Error : MissingMemberHandling.Ignore,',
    '            NullValueHandling = NullValueHandling.Include,',
    '            ObjectCreationHandling = ObjectCreationHandling.Replace,',
    '            TypeNameHandling = TypeNameHandling.None,',
    '        };',
    '',
    '        public static T Deserialize<T>(string json, bool strict = false) where T : class =>',
    '            JsonConvert.DeserializeObject<T>(json, strict ? Strict : Tolerant)',
    '            ?? throw new JsonSerializationException("Expected " + typeof(T).Name + " but the JSON is null.");',
    '',
    '        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Formatting.None, Tolerant);',
    '',
    '        /// <summary>The wire form of an instant: UTC with exactly three fractional digits.</summary>',
    '        public static string FormatTimestamp(DateTimeOffset instant) =>',
    "            instant.UtcDateTime.ToString(\"yyyy-MM-dd'T'HH:mm:ss.fff'Z'\", CultureInfo.InvariantCulture);",
    '    }',
  ];
}

/** C# bindings for the Unity client, generated from the contracts (ADR 0005). */
export function renderCSharpContracts(): string {
  return new CSharpGenerator().render();
}
