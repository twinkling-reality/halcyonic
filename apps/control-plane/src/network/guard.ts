import { isIPv4, isIPv6 } from 'node:net';

export interface NetworkRequestFacts {
  readonly host: string | undefined;
  readonly origin: string | undefined;
  readonly secFetchSite: string | undefined;
  readonly localPort: number;
}

export type NetworkDecision =
  | { readonly allowed: true }
  | { readonly allowed: false; readonly code: string; readonly message: string };

/** A `.local` name: labels of letters, digits and hyphens, as mDNS publishes them. */
const LOCAL_NAME = /^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+local$/;

/**
 * The network listener's checks before any credential (ADR 0017), all refused with 403:
 * 1. `Host` must be an IP address or a `.local` name, with this listener's port. A web page
 *    rebinding its own DNS name to this address cannot pass it, and neither can one that is not on
 *    this network, since `.local` names resolve only here.
 * 2. Browser requests are refused, as on loopback: any `Origin`, or a cross-site fetch. Browsers
 *    send `Origin` with every WebSocket upgrade, and they refuse the self-signed certificate anyway.
 */
export function checkNetworkRequest(facts: NetworkRequestFacts): NetworkDecision {
  if (!hostAllowed(facts.host, facts.localPort)) {
    return deny(
      'host_not_allowed',
      'The Host header must name this listener by IP address or .local name, with its port.',
    );
  }
  if (facts.origin !== undefined) {
    return deny('origin_not_allowed', 'Requests from browser origins are not accepted.');
  }
  if (facts.secFetchSite === 'cross-site') {
    return deny('cross_site_request', 'Cross-site requests are not accepted.');
  }
  return { allowed: true };
}

function hostAllowed(host: string | undefined, port: number): boolean {
  if (host === undefined) return false;
  const suffix = `:${port}`;
  const lower = host.toLowerCase();
  if (!lower.endsWith(suffix)) return false;
  const name = lower.slice(0, -suffix.length);
  if (name.startsWith('[') && name.endsWith(']')) return isIPv6(name.slice(1, -1));
  return isIPv4(name) || (name.length <= 253 && LOCAL_NAME.test(name));
}

function deny(code: string, message: string): NetworkDecision {
  return { allowed: false, code, message };
}
