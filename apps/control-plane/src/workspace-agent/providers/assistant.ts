import { AssistantModel, type Model } from '../assistant-model.ts';
import { HostedWorkspaceModel } from './hosted-model.ts';
import { DECLARED_PROVIDERS, PROTOCOLS } from './index.ts';
import { declaredKeys } from './keys.ts';
import { type HostedProvider, upstreamProblem } from './provider.ts';

/**
 * The assistant's model as the plane runs it: the local model, unless a declared provider has its
 * key stored. `HALCYONIC_ASSISTANT_MODEL` names one of the stored provider's declared models;
 * `HALCYONIC_ASSISTANT_UPSTREAM` points every provider at this Mac (http or https on loopback only),
 * for proofs against a stand-in, so a key never goes to an address the provider did not declare.
 */
export function assistantModel(options: {
  local: Model;
  dataDir: string;
  environment: NodeJS.ProcessEnv;
  providers?: readonly HostedProvider[];
}): AssistantModel {
  const keys = declaredKeys(options.dataDir);
  const upstream = loopbackOnly(options.environment.HALCYONIC_ASSISTANT_UPSTREAM);
  const named = options.environment.HALCYONIC_ASSISTANT_MODEL || null;
  const hosted = (options.providers ?? DECLARED_PROVIDERS).flatMap((provider) => {
    const protocol = PROTOCOLS.get(provider.protocol);
    if (!protocol) return [];
    return [
      new HostedWorkspaceModel({
        provider,
        protocol,
        keys,
        model: named !== null && provider.models.includes(named) ? named : null,
        upstream,
      }),
    ];
  });
  return new AssistantModel({ local: options.local, hosted });
}

/** A stand-in address on this Mac, or the sentinel `upstreamProblem` refuses, so nothing is sent. */
function loopbackOnly(given: string | undefined): string | null {
  if (!given) return null;
  if (upstreamProblem(given) !== null) return 'refused:';
  const host = new URL(given).hostname;
  return ['127.0.0.1', '[::1]', 'localhost'].includes(host) ? given : 'refused:';
}
