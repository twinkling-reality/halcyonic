# Seorak integration API fixtures

`v1/resolve-hit.json` is the example answer to `POST /api/v1/sessions/resolve` in Seorak's
public ADR 007, "Resolving a known session by its native identity, and launcher labels", at commit
`aa2d57e` of <https://github.com/twinkling-reality/seorak>
(`docs/adr/007-native-session-resolve-and-launcher-labels.md`). The ADR calls its values
illustrative: every id, date and amount in it is made up, and it holds no credential. The values
are unchanged.

It is the only example document Seorak has published. The other documents the tests serve (the
resolve miss, the outcome and the verification lens) are built in `src/testing/fake-seorak.ts`
from the published `@seorak/types` 0.2.0 declarations and the ADR's description of a miss. They
are Halcyonic's construction, not recordings of Seorak.

The ADR is used under the Apache License, Version 2.0, the license of that commit. Its notice:

```text
Seorak
Copyright 2026 Twinkling Reality
```
