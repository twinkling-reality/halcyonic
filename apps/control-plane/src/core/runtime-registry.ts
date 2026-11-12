import { compileValidator, RuntimeDescriptor } from '@halcyonic/contracts';
import type { RuntimeCatalog } from '@halcyonic/domain';
import { capabilityProblems, type RuntimeAdapter } from '@halcyonic/runtime-core';
import type { Logger } from '../logger.ts';

const validateDescriptor = compileValidator(RuntimeDescriptor);

/** The runtime adapters this control plane hosts, keyed by runtime id. */
export class RuntimeRegistry implements RuntimeCatalog {
  readonly #adapters = new Map<string, RuntimeAdapter>();
  /**
   * Whether the host's instruction queue runs (ADR 0056): on the person's own journal only. Where it
   * does, a runtime that cannot steer but can be instructed at rest is described with
   * `holds_instructions`, the host's own declaration; an adapter's own is never taken.
   */
  holdsInstructions = false;

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
    const adapter = this.#adapters.get(runtimeId);
    return adapter === undefined ? undefined : this.#described(adapter.descriptor);
  }

  adapter(runtimeId: string): RuntimeAdapter | undefined {
    return this.#adapters.get(runtimeId);
  }

  /** Whether a runtime here reads `name` for itself, as its adapter declares (ADR 0057). */
  ownsEnvironmentName(name: string): boolean {
    return [...this.#adapters.values()].some(
      (adapter) => adapter.ownsEnvironmentName?.(name) === true,
    );
  }

  /**
   * The runtimes whose starts can run with no sandbox of Halcyonic's around them, as each adapter
   * declares, and those that do not say, which are taken as can (never as inside); none that is
   * synthetic, which touches no files.
   */
  outsideSandbox(): RuntimeDescriptor[] {
    return [...this.#adapters.values()]
      .filter(
        (adapter) => !adapter.descriptor.synthetic && adapter.runsOutsideSandbox?.() !== false,
      )
      .map((adapter) => this.#described(adapter.descriptor));
  }

  descriptors(): RuntimeDescriptor[] {
    return [...this.#adapters.values()].map((adapter) => this.#described(adapter.descriptor));
  }

  #described(descriptor: RuntimeDescriptor): RuntimeDescriptor {
    const { holds_instructions: _theirs, ...own } = descriptor;
    const { capabilities } = descriptor;
    return this.holdsInstructions &&
      !capabilities.instruct_while_running &&
      capabilities.instruct_at_rest
      ? { ...own, holds_instructions: true }
      : own;
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
