import { compileValidator, RuntimeDescriptor } from '@halcyonic/contracts';
import type { RuntimeCatalog } from '@halcyonic/domain';
import { capabilityProblems, type RuntimeAdapter } from '@halcyonic/runtime-core';
import type { Logger } from '../logger.ts';

const validateDescriptor = compileValidator(RuntimeDescriptor);

/** The runtime adapters this control plane hosts, keyed by runtime id. */
export class RuntimeRegistry implements RuntimeCatalog {
  readonly #adapters = new Map<string, RuntimeAdapter>();

  /** Refuses an adapter whose descriptor is malformed or claims capabilities it does not implement. */
  register(adapter: RuntimeAdapter): void {
    const valid = validateDescriptor(adapter.descriptor);
    if (!valid.ok) {
      throw new Error(
        `runtime descriptor is invalid: ${valid.issues.map((i) => `${i.path} ${i.message}`).join('; ')}`,
      );
    }
    const runtimeId = adapter.descriptor.runtime_id;
    if (this.#adapters.has(runtimeId)) {
      throw new Error(`runtime ${runtimeId} is already registered`);
    }
    const problems = capabilityProblems(adapter);
    if (problems.length > 0) {
      throw new Error(`runtime ${runtimeId} is inconsistent: ${problems.join('; ')}`);
    }
    this.#adapters.set(runtimeId, adapter);
  }

  get(runtimeId: string): RuntimeDescriptor | undefined {
    return this.#adapters.get(runtimeId)?.descriptor;
  }

  adapter(runtimeId: string): RuntimeAdapter | undefined {
    return this.#adapters.get(runtimeId);
  }

  descriptors(): RuntimeDescriptor[] {
    return [...this.#adapters.values()].map((adapter) => adapter.descriptor);
  }

  async closeAll(logger: Logger): Promise<void> {
    for (const [runtimeId, adapter] of this.#adapters) {
      try {
        await adapter.close();
      } catch (error) {
        logger.error({ err: error, runtime_id: runtimeId }, 'runtime adapter failed to close');
      }
    }
  }
}
