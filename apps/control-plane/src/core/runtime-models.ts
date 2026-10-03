import { compileValidator, RuntimeModelsResult } from '@halcyonic/contracts';
import { RuntimeActionError, type RuntimeAdapter } from '@halcyonic/runtime-core';

const validateResult = compileValidator(RuntimeModelsResult);

/** How long a runtime has to list its models. Listing may first launch the runtime's server. */
export const MODEL_LIST_TIMEOUT_MS = 30_000;

const CODE_PATTERN = /^[a-z][a-z0-9_]{0,63}$/;

/**
 * Reads the models a runtime can use now, straight from its adapter, and states why when it
 * cannot (ADR 0016). Nothing is cached or journaled. A list that does not match the contract is
 * not passed on.
 */
export async function readRuntimeModels(
  adapter: RuntimeAdapter,
  timeoutMs: number = MODEL_LIST_TIMEOUT_MS,
): Promise<RuntimeModelsResult> {
  const list = adapter.listModels;
  if (list === undefined) {
    return unavailable('models_not_listed', 'This runtime does not list its models.');
  }
  let timer: NodeJS.Timeout | undefined;
  const timeout = new Promise<{ readonly kind: 'timeout' }>((resolve) => {
    timer = setTimeout(() => resolve({ kind: 'timeout' }), timeoutMs);
  });
  try {
    const outcome = await Promise.race([
      Promise.resolve()
        .then(() => list.call(adapter))
        .then(
          (models) => ({ kind: 'listed', models }) as const,
          (error: unknown) => ({ kind: 'failed', error }) as const,
        ),
      timeout,
    ]);
    switch (outcome.kind) {
      case 'timeout':
        return unavailable(
          'timeout',
          `The runtime did not list its models within ${timeoutMs} ms.`,
        );
      case 'failed': {
        const { error } = outcome;
        if (error instanceof RuntimeActionError) {
          return unavailable(
            CODE_PATTERN.test(error.code) ? error.code : 'runtime_error',
            clip(error.message) ?? 'The runtime could not list its models.',
          );
        }
        return unavailable('adapter_error', 'The runtime adapter failed to list its models.');
      }
      case 'listed': {
        const result: RuntimeModelsResult = {
          availability: 'available',
          models: [...outcome.models],
        };
        return validateResult(result).ok
          ? result
          : unavailable('invalid_models', 'The runtime listed models outside the contract.');
      }
      default: {
        const unhandled: never = outcome;
        throw new Error(`unhandled outcome ${String((unhandled as { kind?: unknown }).kind)}`);
      }
    }
  } finally {
    clearTimeout(timer);
  }
}

function unavailable(code: string, message: string): RuntimeModelsResult {
  return { availability: 'unavailable', reason: { code, message } };
}

function clip(text: string): string | null {
  const trimmed = text.trim().slice(0, 2000);
  return trimmed === '' ? null : trimmed;
}
