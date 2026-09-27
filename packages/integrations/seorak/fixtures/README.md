# Seorak integration API fixtures

The documents in `v1/` are the examples in Seorak's public ADR 007, "Resolving a known session by
its native identity, and launcher labels" (`docs/adr/007-native-session-resolve-and-launcher-labels.md`
in <https://github.com/twinkling-reality/seorak>). The ADR calls their values illustrative: every
id, date and amount in them is made up, and they hold no credential.

- `resolve-hit.json`: the answer to `POST /api/v1/sessions/resolve` in section 1, at commit
  `aa2d57e`, unchanged at `8199865`. Its text is the ADR's.
- `outcome.json` and `lens-verification.json`: the outcome and verification lens for the same
  session in section 2, at commit `8199865`. The ADR abbreviates the lens's `coverage` and
  `freshness` as "as above", so they are copied from the outcome example. Their values are the
  ADR's; whitespace follows this repository's formatter.

The other documents the tests serve (a resolve miss, and outcomes and lenses of other sessions)
are built in `src/testing/fake-seorak.ts` from the published `@seorak/types` 0.2.0 declarations
and the ADR's text. They are Halcyonic's construction, not recordings of Seorak.

The ADR is used under the Apache License, Version 2.0, the license of those commits. Its notice:

```text
Seorak
Copyright 2026 Twinkling Reality
```
