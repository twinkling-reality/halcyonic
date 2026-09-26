# ADR 0006: License Halcyonic under Apache-2.0

- Status: Accepted
- Date: 2026-09-26

## Context

Halcyonic had no license, which meant all rights reserved. Its neighbours differ: Salidium is MIT;
Seorak's public core is Apache-2.0, chosen in Seorak's ADR 005 because Apache section 3 grants an
express patent license from every contributor, with defensive termination, and MIT grants none.
Halcyonic is expected to accept outside contributions and may later sit beside privately operated
services, such as a remote relay.

## Decision

- The repository is licensed under Apache-2.0. `LICENSE` is the unmodified text published by the
  Apache Software Foundation, and `NOTICE` names the copyright holder: Twinkling Reality.
- Every package declares `"license": "Apache-2.0"`.
- Contributions are accepted under the same license (Apache-2.0 section 5).

## Alternatives considered

- **MIT**: shorter and matches Salidium, but has no patent grant.
- **AGPL-3.0**: rejected for the same reasons Seorak recorded; it deters adoption of reusable
  parts and reaches only modified hosted copies.
- **No license yet**: blocks outside use and contribution while giving no benefit.

## Consequences

- Dependencies must be license-compatible with Apache-2.0. Current third-party dependencies
  (Fastify, `@fastify/websocket`, TypeBox) are MIT.
- The license grants no trademark rights (section 6), so the name remains separate from the code
  once it is cleared.
- Relicensing later requires every contributor's agreement, so this decision should be treated as
  permanent.
