import type { AssistantHostedModel } from '@halcyonic/contracts';
import { type HostedProvider, upstreamProblem, type WireShape } from '@halcyonic/runtime-core';
import { AssistantModel, type Model } from '../assistant-model.ts';
import { HostedWorkspaceModel } from './hosted-model.ts';
import { PROTOCOLS } from './index.ts';
import { declaredKeys } from './keys.ts';
import type { Protocol } from './provider.ts';

/**
 * The assistant's model as the plane runs it: where the person chose from the host's list (`chosen`, the
 * projection's `assistantModel`), else this computer's model. `HALCYONIC_ASSISTANT_UPSTREAM` points every provider
 * at this Mac (http or https on loopback only), for proofs against a stand-in, so a key never goes to an address the
 * provider did not declare.
 */
export function assistantModel(options: {
  local: Model;
  dataDir: string;
  environment: NodeJS.ProcessEnv;
  chosen: () => AssistantHostedModel | null;
  /** The host's one list of declared providers (`HostProviders.list()`, ADR 0061), in its order. */
  providers: readonly HostedProvider[];
  /** The wire shapes the assistant speaks; a test may give its own. */
  protocols?: ReadonlyMap<WireShape, Protocol>;
}): AssistantModel {
  const keys = declaredKeys(options.dataDir);
  const upstream = loopbackOnly(options.environment.HALCYONIC_ASSISTANT_UPSTREAM);
  const protocols = options.protocols ?? PROTOCOLS;
  const hosted = options.providers.flatMap((provider) => {
    // The first shape the provider serves, in its own order, that the assistant speaks; none, and it is not offered.
    const shape = provider.speaks.find((each) => protocols.has(each));
    const protocol = shape === undefined ? undefined : protocols.get(shape);
    if (!protocol) return [];
    return [
      new HostedWorkspaceModel({
        provider,
        protocol,
        keys,
        upstream,
      }),
    ];
  });
  return new AssistantModel({ local: options.local, hosted, chosen: options.chosen });
}

/** A stand-in address on this Mac, or the sentinel `upstreamProblem` refuses, so nothing is sent. */
function loopbackOnly(given: string | undefined): string | null {
  if (!given) return null;
  if (upstreamProblem(given) !== null) return 'refused:';
  const host = new URL(given).hostname;
  return ['127.0.0.1', '[::1]', 'localhost'].includes(host) ? given : 'refused:';
}
