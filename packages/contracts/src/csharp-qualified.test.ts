import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const GENERATED = readFileSync(
  new URL('../csharp/Runtime/HalcyonicContracts.g.cs', import.meta.url),
  'utf8',
);

test('the generated C# names every library type it writes on a member in full, so no member can shadow one', () => {
  // `HereNeed.required` once shadowed Newtonsoft's `Required` inside its own class (1111-b81): written in
  // full, a contract member of any name can never take an attribute's or a member type's meaning.
  const bare = [
    /\[(JsonProperty|JsonConverter|EnumMember)\(/,
    /Required = Required\./,
    /NullValueHandling = NullValueHandling\.Ignore/,
    /typeof\(StringEnumConverter\)/,
    /public (List|Dictionary)</,
    /public JToken\b/,
  ];
  for (const pattern of bare)
    assert.doesNotMatch(GENERATED, pattern, `written in full: ${pattern.source}`);
});
