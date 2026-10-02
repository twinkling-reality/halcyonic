# Salidium consumer contract fixtures

`v1/` holds copies of the retained fixtures of Salidium's consumer contract v1, from
`packages/consumer-contract/fixtures/v1/` in the Salidium repository
(<https://github.com/twinkling-reality/salidium>), release candidate `1.0.0-rc.0` as revised on
2026-09-26, copied that day before the version was published. They equal, value for value, the
fixtures of `@salidium/consumer-contract@1.0.0-rc.0` as published on npm on 2026-09-27 (SLSA
provenance, commit `0e9269a`). Salidium recorded them from a real daemon serving invented sessions
on a fixed clock: every name, path and id in them is made up, and they hold no credentials. The
redaction marker in one statement (`ghp_[GITHUB_TOKEN#1]`) is Salidium's proof that it redacts, not
a token.

Twelve of Salidium's thirteen are copied:

- `consumer-discovery.json`
- `consumer-error-session-not-observed.json`, `consumer-error-unauthorized.json`
- `session-lookup.json`
- `session-report-verified.json`, `session-report-failing.json`, `session-report-working.json`
- `session-feed-resync.json`, `session-feed-session-changed.json`,
  `session-feed-session-removed.json`, `session-feed-heartbeat.json`, `session-feed-closing.json`

`session-list.json` is not copied because this client does not read the list. The values are
unchanged; whitespace in the reports follows this repository's formatter.

`v1/1.1/` holds the same twelve of contract 1.1 (Salidium 0.7.0), from
`packages/consumer-contract/fixtures/v1/1.1/` at Salidium commit `5b53b77` on main, copied on
2026-10-02 while `@salidium/consumer-contract@1.1.0` waited for publishing. They add the revision
anchors, each changed file's repository and `linesRemovedExact`, and the providers in discovery;
every path and id in them is invented too.

They are used under the MIT License:

```text
Copyright (c) 2026 Glendon Chin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
