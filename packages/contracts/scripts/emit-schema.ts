import { writeFileSync } from 'node:fs';
import { renderSchemaDocument } from '../src/schema-document.ts';

const target = new URL('../schema/halcyonic-contracts.schema.json', import.meta.url);
writeFileSync(target, renderSchemaDocument());
process.stdout.write(`wrote ${target.pathname}\n`);
