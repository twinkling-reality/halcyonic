import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { parseModels, sameModel } from './models.ts';

/** Two entries as OpenCode 2.0.18 lists them: an Ollama model it discovered, and a hosted one. */
const LISTED = [
  {
    id: 'qwen3.6:35b-a3b-nvfp4',
    modelID: 'qwen3.6:35b-a3b-nvfp4',
    providerID: 'ollama',
    family: 'qwen3_5_moe',
    name: 'qwen3.6:35b-a3b-nvfp4',
    package: '@opencode/ai/providers/openai-compatible',
    settings: { apiKey: '', baseURL: 'http://127.0.0.1:11434/v1', provider: 'ollama' },
    capabilities: { tools: true, input: ['text', 'image'], output: ['text'] },
    variants: [],
    time: { released: 0 },
    cost: [],
    status: 'active',
    enabled: true,
    limit: { context: 65536, output: 16384 },
  },
  {
    id: 'big-pickle',
    modelID: 'big-pickle',
    providerID: 'opencode',
    name: 'Big Pickle',
    settings: { apiKey: 'key-that-must-not-travel', baseURL: 'https://opencode.ai/zen/v1' },
    headers: { authorization: 'Bearer key-that-must-not-travel' },
    capabilities: { tools: true, input: ['text'], output: ['text'] },
    enabled: true,
    limit: { context: 200000, input: 160000, output: 32000 },
  },
];

describe('OpenCode model listing', () => {
  test('keeps what the adapter reads, field by field, and nothing else', () => {
    const models = parseModels(LISTED);
    assert.deepEqual(models, [
      {
        providerID: 'ollama',
        id: 'qwen3.6:35b-a3b-nvfp4',
        name: 'qwen3.6:35b-a3b-nvfp4',
        tools: true,
        contextTokens: 65536,
        baseUrl: 'http://127.0.0.1:11434/v1',
      },
      {
        providerID: 'opencode',
        id: 'big-pickle',
        name: 'Big Pickle',
        tools: true,
        contextTokens: 200000,
        baseUrl: 'https://opencode.ai/zen/v1',
      },
    ]);
    assert.equal(JSON.stringify(models).includes('key-that-must-not-travel'), false);
  });

  test('drops disabled and malformed entries, and says unknown for what is missing', () => {
    const models = parseModels([
      { ...LISTED[0], enabled: false },
      { ...LISTED[0], providerID: '' },
      { ...LISTED[0], id: 42 },
      'not a model',
      null,
      {
        providerID: 'lmstudio',
        id: 'local-model',
        name: ' ',
        capabilities: {},
        limit: { context: 0 },
      },
      { providerID: 'vllm', id: 'served', limit: { context: 1.5 }, settings: { baseURL: 7 } },
    ]);
    assert.deepEqual(models, [
      {
        providerID: 'lmstudio',
        id: 'local-model',
        name: 'local-model',
        tools: null,
        contextTokens: null,
        baseUrl: null,
      },
      {
        providerID: 'vllm',
        id: 'served',
        name: 'served',
        tools: null,
        contextTokens: null,
        baseUrl: null,
      },
    ]);
  });

  test('matches a model by provider and id, never by name', () => {
    const [local] = parseModels(LISTED);
    assert.ok(local !== undefined);
    assert.equal(sameModel(local, { providerID: 'ollama', id: 'qwen3.6:35b-a3b-nvfp4' }), true);
    assert.equal(sameModel(local, { providerID: 'opencode', id: 'qwen3.6:35b-a3b-nvfp4' }), false);
    assert.equal(sameModel(local, { providerID: 'ollama', id: 'qwen3.6' }), false);
  });
});
