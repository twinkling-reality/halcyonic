import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { compileValidator, RuntimeModel } from '@halcyonic/contracts';
import { modelsFromCodex, servedBy } from './models.ts';

/** Two models of the catalog built into Codex 0.157.0, as `model/list` answers them. */
const BUILT_IN_CATALOG = [
  { id: 'gpt-6-astra', model: 'gpt-6-astra', displayName: 'GPT-6-Astra', hidden: false },
  { id: 'gpt-5.4', model: 'gpt-5.4', displayName: 'GPT-5.4', hidden: true },
  { id: 'gpt-5.5', model: 'gpt-5.5', displayName: 'GPT-5.5', hidden: false },
];

const valid = compileValidator(RuntimeModel);

describe('Codex model list', () => {
  test('the built-in catalog is OpenAI models, served remotely, and only for the openai provider', () => {
    const models = modelsFromCodex({ model: 'gpt-5.5' }, BUILT_IN_CATALOG, {});
    assert.deepEqual(models, [
      {
        model_ref: 'openai/gpt-6-astra',
        display_name: 'GPT-6-Astra (OpenAI)',
        served: 'remote',
        tool_calling: 'unknown',
        context_tokens: null,
      },
      {
        model_ref: 'openai/gpt-5.5',
        display_name: 'GPT-5.5 (OpenAI)',
        served: 'remote',
        tool_calling: 'unknown',
        context_tokens: null,
      },
    ]);
    assert.ok(models.every((model) => valid(model).ok));
  });

  test('a local provider lists its configured model, not the OpenAI catalog', () => {
    // This Mac's Ollama serves llama3.2:1b's weights under a hosted model's name.
    const models = modelsFromCodex(
      { model_provider: 'ollama', model: 'gpt-4o:latest', model_context_window: 65536n },
      BUILT_IN_CATALOG,
      {},
    );
    assert.deepEqual(models, [
      {
        model_ref: 'ollama/gpt-4o:latest',
        display_name: 'gpt-4o:latest (Ollama)',
        served: 'this_mac',
        tool_calling: 'unknown',
        context_tokens: 65536,
      },
    ]);
    assert.deepEqual(modelsFromCodex({ model_provider: 'lmstudio' }, BUILT_IN_CATALOG, {}), []);
  });

  test('a configured catalog belongs to the configured provider', () => {
    const models = modelsFromCodex(
      {
        model_provider: 'gateway',
        model_catalog_json: '/Users/you/.codex/models.json',
        model_providers: {
          gateway: { name: 'Team gateway', base_url: 'http://127.0.0.1:8080/v1' },
        },
      },
      [{ id: 'local-coder', model: 'local-coder', displayName: 'Local Coder', hidden: false }],
      {},
    );
    assert.deepEqual(
      models.map((model) => [model.model_ref, model.display_name, model.served]),
      [['gateway/local-coder', 'Local Coder (Team gateway)', 'this_mac']],
    );
  });

  test('where a provider serves follows its address, never a model name', () => {
    assert.equal(servedBy({}, 'openai', {}), 'remote');
    assert.equal(
      servedBy({ openai_base_url: 'http://localhost:4000/v1' }, 'openai', {}),
      'this_mac',
    );
    assert.equal(servedBy({}, 'ollama', {}), 'this_mac');
    assert.equal(
      servedBy({}, 'ollama', { CODEX_OSS_BASE_URL: 'http://192.168.1.20:11434/v1' }),
      'remote',
    );
    assert.equal(
      servedBy({}, 'lmstudio', { CODEX_OSS_BASE_URL: 'http://[::1]:1234/v1' }),
      'this_mac',
    );
    assert.equal(servedBy({}, 'amazon-bedrock', {}), 'remote');
    assert.equal(servedBy({}, 'unknown-provider', {}), 'unknown');
    const defined = { model_providers: { remote: { base_url: 'https://llm.example.com/v1' } } };
    assert.equal(servedBy(defined, 'remote', {}), 'remote');
    assert.equal(servedBy({ model_providers: { quiet: {} } }, 'quiet', {}), 'unknown');
    // Codex ignores an entry under a built-in provider's id, except Bedrock's, so the entry says
    // nothing: the built-in provider's own address decides.
    const overridden = { model_providers: { ollama: { base_url: 'http://gpu-box:11434/v1' } } };
    assert.equal(servedBy(overridden, 'ollama', {}), 'this_mac');
    const openai = {
      model_provider: 'openai',
      model_providers: { openai: { base_url: 'http://127.0.0.1:8080/v1' } },
    };
    assert.equal(servedBy(openai, 'openai', {}), 'remote');
    assert.equal(
      servedBy({ ...openai, openai_base_url: 'http://127.0.0.1:8080/v1' }, 'openai', {}),
      'this_mac',
    );
    const bedrock = {
      model_providers: { 'amazon-bedrock': { base_url: 'http://127.0.0.1:9000' } },
    };
    assert.equal(servedBy(bedrock, 'amazon-bedrock', {}), 'this_mac');
    assert.equal(servedBy({}, 'amazon-bedrock-runtime', {}), 'remote');
  });

  test('names that cannot travel as a model_ref are left out, and duplicates are listed once', () => {
    const models = modelsFromCodex(
      { model: 'gpt-5.5' },
      [...BUILT_IN_CATALOG, { model: 'has space', displayName: 'Spaced', hidden: false }, 'junk'],
      {},
    );
    assert.deepEqual(
      models.map((model) => model.model_ref),
      ['openai/gpt-6-astra', 'openai/gpt-5.5'],
    );
  });
});
