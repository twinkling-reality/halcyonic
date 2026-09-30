import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { parseModels, sameModel, servedBy, toRuntimeModel } from './models.ts';

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

  test('a local model named after a hosted one reads as local, and says what serves it', () => {
    const [alias, hosted] = parseModels([
      // What this Mac's Ollama serves under a hosted model's name: llama3.2:1b's weights.
      {
        ...LISTED[0],
        id: 'gpt-4o:latest',
        modelID: 'gpt-4o:latest',
        name: 'gpt-4o:latest',
        family: 'llama',
        capabilities: { tools: true, input: ['text'], output: ['text'] },
        limit: { context: 131072, output: 32000 },
      },
      {
        providerID: 'openai',
        id: 'gpt-4o',
        name: 'GPT-4o',
        settings: { baseURL: 'https://api.openai.com/v1' },
        capabilities: { tools: true },
        enabled: true,
        limit: { context: 128000, output: 16384 },
      },
    ]);
    assert.ok(alias !== undefined && hosted !== undefined);
    assert.deepEqual(toRuntimeModel(alias), {
      model_ref: 'ollama/gpt-4o:latest',
      display_name: 'gpt-4o:latest (Ollama)',
      served: 'this_mac',
      tool_calling: 'declared',
      context_tokens: 131072,
    });
    assert.deepEqual(toRuntimeModel(hosted), {
      model_ref: 'openai/gpt-4o',
      display_name: 'GPT-4o (openai)',
      served: 'remote',
      tool_calling: 'declared',
      context_tokens: 128000,
    });
  });

  test('where a model runs follows the address OpenCode reaches it at, not its name', () => {
    const served = (providerID: string, id: string, baseUrl: string | null) =>
      servedBy({ providerID, id, name: id, tools: null, contextTokens: null, baseUrl });
    assert.equal(
      served('ollama', 'qwen3.6:35b-a3b-nvfp4', 'http://127.0.0.1:11434/v1'),
      'this_mac',
    );
    assert.equal(served('lmstudio', 'local', 'http://localhost:1234/v1'), 'this_mac');
    assert.equal(served('vllm', 'local', 'http://[::1]:8000/v1'), 'this_mac');
    assert.equal(served('ollama', 'qwen3.6:35b', 'http://192.168.1.20:11434/v1'), 'remote');
    assert.equal(served('opencode', 'big-pickle', 'https://opencode.ai/zen/v1'), 'remote');
    // Ollama serves its cloud tags from its hosted service, whatever address reaches Ollama.
    assert.equal(served('ollama', 'gpt-oss:120b-cloud', 'http://127.0.0.1:11434/v1'), 'remote');
    assert.equal(served('ollama', 'kimi-k2:cloud', 'http://127.0.0.1:11434/v1'), 'remote');
    assert.equal(served('anthropic', 'claude', null), 'unknown');
    assert.equal(served('custom', 'model', 'not a url'), 'unknown');
    // A missing tools flag stays unknown rather than becoming a claim either way.
    const quiet = toRuntimeModel({
      providerID: 'custom',
      id: 'model',
      name: 'Model',
      tools: null,
      contextTokens: null,
      baseUrl: null,
    });
    assert.equal(quiet.tool_calling, 'unknown');
    assert.equal(quiet.served, 'unknown');
  });

  test('matches a model by provider and id, never by name', () => {
    const [local] = parseModels(LISTED);
    assert.ok(local !== undefined);
    assert.equal(sameModel(local, { providerID: 'ollama', id: 'qwen3.6:35b-a3b-nvfp4' }), true);
    assert.equal(sameModel(local, { providerID: 'opencode', id: 'qwen3.6:35b-a3b-nvfp4' }), false);
    assert.equal(sameModel(local, { providerID: 'ollama', id: 'qwen3.6' }), false);
  });
});
