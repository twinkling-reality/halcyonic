import { execFile } from 'node:child_process';
import type { NetworkInterfaceInfo } from 'node:os';
import type { NetworkListener } from '@halcyonic/contracts';
import type { NetworkListenerConfig } from '../config.ts';
import { isLocalName } from './guard.ts';

/** Where macOS keeps the tool that reads the computer's Bonjour name. */
const SCUTIL = '/usr/sbin/scutil';

/**
 * The computer's `.local` name from its Bonjour host name (`scutil --get LocalHostName`), in lower
 * case, or null when there is none a device could type: a `.local` name stays the same when the
 * network hands the computer another address, so a headset that keeps it finds the computer again.
 */
export function localNameFrom(localHostName: string | null | undefined): string | null {
  const label = localHostName?.trim().toLowerCase();
  if (label === undefined || label === '') return null;
  const name = `${label}.local`;
  return isLocalName(name) ? name : null;
}

/**
 * Reads the computer's `.local` name, on macOS only, or null when it cannot be read in two
 * seconds. Nothing secret: the name is what the computer announces on its network.
 */
export function readLocalName(
  platform: NodeJS.Platform = process.platform,
): Promise<string | null> {
  if (platform !== 'darwin') return Promise.resolve(null);
  return new Promise((resolve) => {
    execFile(SCUTIL, ['--get', 'LocalHostName'], { timeout: 2_000 }, (error, stdout) => {
      resolve(error === null ? localNameFrom(stdout) : null);
    });
  });
}

/**
 * Where devices on the network can reach the listener now: its own address, or for a wildcard,
 * the computer's `.local` name first, when it has one, then every interface's external addresses
 * (IPv4 for `0.0.0.0`, both families for `::`). Read again on each call, since the network may have
 * handed the computer other addresses since the listener started.
 */
export function describeListener(
  config: NetworkListenerConfig,
  port: number,
  certificateSha256: string,
  localName: string | null,
  interfaces: NodeJS.Dict<NetworkInterfaceInfo[]>,
): NetworkListener {
  const wildcard = config.host === '0.0.0.0' || config.host === '::';
  const reachable = (entry: NetworkInterfaceInfo) =>
    !entry.internal &&
    (entry.family === 'IPv4' || (config.host === '::' && !entry.address.startsWith('fe80:')));
  const addresses = wildcard
    ? [
        ...(localName === null ? [] : [localName]),
        ...Object.values(interfaces)
          .flatMap((entries) => entries ?? [])
          .filter(reachable)
          .map((entry) => entry.address),
      ]
    : [config.host];
  return { host: config.host, port, addresses, certificate_sha256: certificateSha256 };
}
