import { writeFileSync } from 'node:fs';
import { renderCSharpContracts } from '../src/csharp.ts';
import { renderSchemaDocument } from '../src/schema-document.ts';

const outputs: readonly [URL, string][] = [
  [new URL('../schema/halcyonic-contracts.schema.json', import.meta.url), renderSchemaDocument()],
  [new URL('../csharp/Runtime/HalcyonicContracts.g.cs', import.meta.url), renderCSharpContracts()],
];
for (const [target, content] of outputs) {
  writeFileSync(target, content);
  process.stdout.write(`wrote ${target.pathname}\n`);
}
