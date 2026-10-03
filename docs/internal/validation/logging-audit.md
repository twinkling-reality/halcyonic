# What leaves the process: logs, errors and the journal

- **Question:** Does anything private leave Halcyonic's processes through a log, an error message,
  an HTTP error response, the journal's stored error text, a printed line or a file written?
  Private means tokens, credentials and loopback proofs; instruction text, agent messages,
  companion words and a person's typed or spoken answers; folder paths where words promise none; and
  error messages that echo a request's or a response's body. AGENTS.md forbids logging secrets, the
  access token, or instruction and agent message text.
- **Date:** 2026-10-02.
- **Versions:** Halcyonic branch lane-g-logging-audit from main 0b3e92c; Node.js 24.15; .NET 10;
  Unity 6000.3.25f1's Android class libraries for the headset's code.
- **Method:** Four read-only sweeps, one an area: the control plane; the runtime adapters, core
  packages and the Salidium and Seorak readers; the command-line tools and tooling; the C# client
  core, the Unity scripts and the glance's Java. Every log call was found by search and read, and
  every error built from text from outside. Each suspected leak was reproduced, with test servers,
  a trace-level logger, fake binaries and stub adapters, never with real credentials or
  `~/.halcyonic`. Each fix has a test that fails without it, run against main's code.
- **Status:** Verified in code and tests. Nothing ran on a headset.

## Fixed

Each fix is its own commit; the test named fails on main and passes with the fix.

| Leak or risk | Where | Fix | Guard |
| --- | --- | --- | --- |
| A key in Codex's or OpenCode's error output reached every device: a failed start put the last 2000 characters in its message, which is journaled; `codex --version`'s failure added the command's error output too | `packages/integrations/codex/src/server.ts`, `packages/integrations/opencode/src/server.ts` | Error output is drained and never kept, as the Claude Code adapter already did; a failure says the exit status or signal and how to see the output | `codex-runtime.test.ts` and `opencode-runtime.test.ts`, "never carry the server's error output": a stand-in binary prints a key and exits |
| `pnpm mac-setup` printed `HALCYONIC_COMPANION_OLLAMA_URL` as given, credentials included, and asked it before any check, also when it named another computer | `apps/control-plane/src/cli/mac-setup.ts` | The address is checked first with the control plane's own rule (`companionOllamaAddress` in config.ts): http, loopback, a port, nothing else; only its origin is printed or asked | `mac-setup.test.ts`, "an Ollama address with credentials in it is never printed or asked" |
| An adapter's unexpected error was journaled word for word: a parse error quotes what it read, which can be an instruction | `apps/control-plane/src/core/command-service.ts` (`toFailure`) | Journaled as "The runtime adapter failed unexpectedly (its type)." | `control-plane.test.ts`, "an adapter's unexpected error is journaled as its type" |
| Branches that can't be reached would have put a whole command, instruction included, into an error | `command-service.ts`, `runtime-models.ts`, `http/realtime.ts`, `packages/domain/src/admission.ts`, `devices.ts`, `projection.ts` | They name only the type | The type checker; the scan below |
| Log redaction covered one path, `req.headers.authorization`, which Fastify never logs; any other line carrying headers showed the token or the proof | `apps/control-plane/src/http/server.ts` | `authorization`, `cookie`, `x-halcyonic-proof` and `x-halcyonic-challenge` are redacted in `headers`, `req.headers` and any object's `headers` | `server.test.ts`, "never shows the token, a cookie or the proof" |
| The headset logged agent text: a realtime message of another shape ended the connection with Newtonsoft's message, which quotes the value, and the status line logs it on every retry | `apps/xr/Packages/com.halcyonic.client/Runtime/RealtimeSession.cs` | A message the app can't read ends the connection with fixed words and the exception's type | `RealtimeSessionTests`, "a message it cannot read never puts its text in the status" |
| What answers at a typed pairing address chose the refusal code that is logged, and could write a line of its own, such as a forged `connection Live` | `PairingClient.cs` (`PairingException`) | Only a code's own shape is kept; anything else reads `refused` | `PinnedTransportTests`, "a pairing code only its own shape is kept" |
| `pnpm demo` printed the titles of every project's tasks, the person's own work included | `apps/control-plane/src/cli/demo.ts` | Only the demo's own project is printed (`workstreamLine` in demo-scenario.ts) | `demo-scenario.test.ts`, "only the demo's own project" |
| `pnpm devices` and `pnpm demo` failed with a parse error that quotes part of a body | `cli/devices.ts`, `demo-scenario.ts` | `readJson` fails in fixed words | `demo-scenario.test.ts`, "an answer that is not JSON" |
| A failed `adb` call in `pnpm quest:session` and `quest:cold-start` was printed whole by Node: everything adb printed, which names the headset | `tooling/quest/adb.ts` | The error names the subcommand and its exit status only | `tooling/quest/adb.test.ts` |
| A runtime's or provider's error text was journaled as given: a gateway's 401 that echoes the key, Codex's error answer at start, OpenCode's | `core/redaction.ts`, used by `command-service.ts` (failures) and `drafts.ts` (a turn's failure, a lost or restored connection) | Every secret Halcyonic holds or passes to a runtime is replaced, exactly, by which one it was ("[redacted: Anthropic key]"); of the agent environment, the values whose names have a secret's word in them (KEY, TOKEN, PASS, PAT, AUTH, COOKIE, SESSION, HEADER and the like, matched whole, so GIT_AUTHOR_NAME is not one) or that read as a credential by themselves, each part of a value that does (`Name: value`), and the password of any URL in one (`postgres://app:password@db`), so an address or a region stays. Then every credential shape, never a path after a scheme word nor a name built of words; the rest stays as given, but for what only looks like a credential (SECURITY.md) (the coordinator's decision). The adapters pass error text whole, since a held value cut in two at 2000 left its first part; the control plane reads at most 4096 characters and cuts after redacting | `redaction.test.ts`; `control-plane.test.ts`, "a runtime's refusal loses what Halcyonic holds"; `drafts.test.ts`; `runtimes.test.ts`, "the secrets taken out of a runtime's text"; `drafts.test.ts`, "a runtime's long error loses a held value across the journal's limit"; the adapters' tests that a long error comes whole |
| Runtime text a person reads as given was journaled or answered as given: a tool's title (Codex's is the whole command line), what an approval asks for, a question's prompt, a test run's label and summary; and over REST, Seorak's reason text and labels and a model listing's failure. The adapters cut titles and summaries before the control plane redacted them, so part of a held value cut in two survived | `drafts.ts`; `http/routes.ts` (evaluation, usage limits); `core/runtime-models.ts`; the Claude Code, Codex and OpenCode adapters | A tool's title, an approval's summary, a question's prompt and a test run's label and summary lose only exact copies of what Halcyonic holds, named, so the command a person approves is never guessed at; the adapters pass them whole and the observation sink is the one place they are cut, after redaction, in code points as the contract counts, with "[truncated]", a question once with `fitQuestion`, which leaves one with anything cut unanswerable. A question's options stay as given, since an answer names them. Seorak's reason text and labels and a model listing's failure are error text, which can echo a key Halcyonic never held, and lose credential shapes too (the coordinator's decision, 2026-10-03) | `drafts.test.ts`, "lose only exact copies of what Halcyonic holds from what a person reads to decide", "are cut to their field's limit after redaction", "are measured and cut in code points" and "a question is fitted once, here, after redaction"; the adapters' tests that titles and summaries come whole; `models-route.test.ts`, "says in words why"; `evaluation-route.test.ts`, "the provider's own words lose what Halcyonic holds" |
| An error logged with `{ err }` showed its message, which can quote an instruction, and Node's `rawPacket` for a malformed request: its head, Authorization header and all, as a byte array | `http/server.ts` | Errors are logged by `errorForLog`: type, code and stack frames only, and the message of Halcyonic's own errors in fixed words (`OwnWordsError`: `ConfigError`, `JournalError`, `InvalidEventError`, `TraceError`, `SpeechEngineError`), so a warm-up failure keeps its why and a refused event its issue paths | `server.test.ts`, "logs an error's type, code and frames": a malformed request carrying a token |
| An adapter's bug, journaled as its type since the audit, left no trace for the owner | `command-service.ts` | It is logged on the Mac with its type and frames | `control-plane.test.ts`, "an adapter's bug is logged" |

A scan guards what the sweeps reviewed: `tooling/log-calls.test.ts` reads every log call in the code
that ships (the control plane and packages through pino, the headset's `Debug.Log` and `Log`
helpers outside `Editor/`, the glance's `Log`) and fails on one whose arguments, outside their
literals, name something that can be private, unless it is listed as reviewed with why it is safe.
It also reads `console.*`, `process.stderr.write`, the headset's
`LogErrorFormat`, `LogWarningFormat`, `LogAssertion` and `unityLogger`, and the glance's `Log.wtf`,
and flags a call that passes an error or a spread. Sixty-two are listed; a new or changed one fails
until a person reviews it, and a listed one that went fails too. It reads names, not values, so it
narrows what a review must look at and proves nothing about a call it passes.

## Sites checked

OK means nothing private can reach the site. Paths are under `apps/control-plane/src` unless named.

**The control plane.**
- The logger: Fastify's request lines log method, URL, host, remote address and port, never
  headers or bodies; the response line logs the status only, so the proof header is never logged
  (both shown at trace level). The 5xx handler logs the error locally and answers in fixed words;
  4xx answers carry only Fastify's fixed parser messages or the sender's own URL; validation issues
  carry no values (TypeBox), except `unknown command_type "<tag>"`, sent back to its sender. OK.
- Every log call (`main.ts`, `core/`, `http/`, `network/`): ids, codes, counts, times, enums,
  folders and binary paths on the ready line, as SECURITY.md says. The list is in the scan's
  reviews. OK.
- The realtime socket: messages, closes and commands are never logged. OK.
- Pairing: the code goes only in the loopback answer to `pnpm pair`; SRP values and protocol errors
  carry no values; the device's label and its credential's hash are journaled, not logged. OK
  (`network.test.ts` guards it at trace level).
- The companion: refusals are fixed words; Ollama's answer is never echoed; neither the person's
  words nor the reply are logged. OK (`companion-route.test.ts`).
- Speech: the transcript appears only in its answer; neither it nor the audio is logged. OK
  (`transcription-route.test.ts`).
- Salidium and Seorak read-through: credentials are never logged or echoed. A reason names the
  credential file's path, which names the home folder, and reaches paired devices; minor, since
  devices already see project paths. Seorak's own reason text, the verification lens's note, and
  each failure's reason lose credentials before a device reads them: fixed above.
- The journal: commands are journaled whole by contract, instruction included; a refused answer
  is stripped (`journaledRejection`); folder paths in rejections match paths already journaled.
  Adapter errors: fixed above for unexpected ones; runtime error text is open, below.

**The adapters, core packages and readers.**
- No log, console or debug call in production code in any adapter or core package.
- Claude Code: its error output is drained unread (`process-guard.ts`); environment errors name
  variables, never values; a gateway's name is its host and port only. OK.
- Every adapter builds the agent's environment from an allowlist; arguments carry no instruction
  or secret (Claude Code's prompt goes on standard input; OpenCode's password in its environment);
  record files are mode 600 with no secrets. OK.
- Codex and OpenCode start failures: fixed above.
- Turn failures and refused actions repeat the runtime's or provider's own error text into
  `runtime.turn.failed` and `command.failed`: open, below.
- Salidium: the credential travels only in the Authorization header, with no redirect, to an
  instance that proved itself; failures are fixed words; repository roots are dropped. OK.
- Seorak: the credential is header-only, the native id travels in a POST body, and messages are
  fixed. OK; its reason text loses credentials (fixed above).
- The domain's admission never repeats an answer's text. OK.

**The command-line tools and tooling.**
- `mac-setup`: credential files are only looked at, never opened; the token is read only with
  `--with-token`, never printed, and sent only after a proof; settings are written mode 600 through
  a temporary file. OK, but for the Ollama address (fixed).
- `devices`: the pairing code is printed where it is meant to be, and nowhere else; labels are made
  printable; unknown fetch errors become fixed words. OK, but for parse errors (fixed).
- `demo`: fixed above. Its WebSocket opens right after the proof, leaving the moment SECURITY.md
  admits for the command-line clients. OK.
- The fixture recorders, replay and emit print paths, counts and TypeBox issues without values.
  The committed fixtures hold placeholders (`/Users/dev`, `/home/developer`), RFC 5054 vectors and
  Salidium's redaction marker only. OK. A person's own scenarios through
  `HALCYONIC_MOCK_SCENARIOS_DIR` would reach a recorded trace; `--check` shows the change.
- `tooling/quest`: `pnpm quest:check` prints fixed words, modes, sizes, errno names and glance
  codes only (its own tests); `quest:session` writes numbers only, under the ignored `.private/`.
  OK, but for adb's errors (fixed).

**The headset: the client core, Unity scripts and the glance.**
- The client core logs nothing; messages that echo a response's body go to the screen only.
- Every `Debug.Log` outside `Editor/`: fixed words, numbers, enums, workstream ids, exception type
  names, the typed or paired address, and the connection status. The status's detail is fixed
  words or a socket's error, and now never a parser's quote (fixed). The pairing store's inner
  exception, which could quote the pairing file, is never logged. Voice and companion failures are
  codes only. OK.
- No type that holds a secret or private text overrides `ToString`, so logging one prints only its
  type's name; there are no C# records. OK.
- The glance's Java logs fixed words, codes from its fixed set, exception class names and counts;
  titles appear on its screen only. OK.
- `Editor/` code doesn't ship; it logs fixture titles and local paths. OK.

## Open

- **Runtime error text in the journal.** Credentials are now taken out (above). The text is
  otherwise the runtime's or provider's own, so a validation error that repeats the instruction it
  rejected still repeats it, which is work content where an error is expected; whether to curate
  error text further is an open question ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). A
  credential in a shape the patterns don't know, and held nowhere Halcyonic can see (in Codex's own
  configuration, for one), still passes.
- **Tool input in titles and summaries.** Codex's tool title is the whole command line, and Claude
  Code's approval summary falls back to a tool's whole input as JSON, which for an MCP tool could
  hold a credential argument. What Halcyonic holds is now taken out (above); a credential it doesn't
  hold stays, since a person needs the command as given to decide on it (SECURITY.md). EVENTS.md
  doesn't say these fields carry tool input.
- **The marker can be forged.** An agent can type "[redacted: access token]" itself. Carrying
  replaced spans as structured data, drawn as a chip, would make it unforgeable: a contract and
  headset change, open ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **Seorak's response** is read with no size limit (not a privacy problem).
- **Host paths** in failure messages: binary paths and the data folder's in some adapter errors,
  credential file paths in Salidium's and Seorak's reasons. Devices already see project paths.

## Not verified

- The pinned Codex 0.157.0 and OpenCode 2.0.18 were not run; their error output was shown with
  stand-in binaries and Homebrew Codex 0.151 against a scratch home. Whether they repeat request
  values in their error answers is inferred.
- Nothing ran on a headset; logcat's handling of a line with a line break in it is assumed.
