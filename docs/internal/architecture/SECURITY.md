# Security

## Scope today

The control plane runs on the developer's own machine and serves loopback by default. When the
owner turns it on, a second listener serves devices paired with it over the local network, TLS
only ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md), "Devices on the
network" below). The threats it defends against now are:

- web pages in a browser on the same machine, or on another machine on the network (cross-site
  requests, DNS rebinding, cross-site WebSocket hijacking);
- other local user accounts;
- other devices on the same network, including one that intercepts traffic;
- a paired device that is lost or stolen, once the owner revokes it;
- malformed or malicious input on every interface;
- leaking secrets or work content into logs.

It is not a sandbox against other processes running as the same user; those can read the token,
the network listener's key and the journal, as with Salidium.

When the Claude Agent runtime is enabled, the Anthropic API key (from the environment, or else
from `<data dir>/anthropic-api-key`, refused when other users can read it) or cloud provider
credentials reach each launched Claude Code process through an explicitly built environment: an allowlist of variables, plus the names listed in
`HALCYONIC_AGENT_ENV`. Nothing else in the control plane's environment reaches an agent, and a
claude.ai login is never used. Launched Claude Code sessions carry `SEORAK_LAUNCHER=halcyonic`, so
Seorak attributes them to Halcyonic. `ANTHROPIC_BASE_URL` is not inherited, because it decides where the
API key is sent and a tool that launches the control plane may set it for its own endpoint; a
gateway must be passed on purpose.

When the Codex runtime is enabled, its app-server also gets an explicitly built environment: an
allowlist (`PATH`, `HOME`, `USER`, `LOGNAME`, `SHELL`, the locale variables, `TMPDIR`, `TZ`,
`CODEX_HOME` and the XDG directories), plus the names listed in `HALCYONIC_AGENT_ENV`. Configuration
may not set `SALIDIUM_INTERNAL`, `CODEX_INTERNAL_ORIGINATOR_OVERRIDE`, which would replace
Halcyonic's identity on its threads, or `CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED`, which
the adapter always sets so the server never enables remote control, a second control channel
through chatgpt.com. Codex runs with the developer's own `CODEX_HOME`, so the developer's
`config.toml` applies: sign-in and keys, model providers, the MCP servers configured there, plugins
and their startup sync (chatgpt.com, github.com and api.github.com, observed without credentials), the
analytics events client, which runs unless `analytics.enabled = false`, metrics sent to
ab.chatgpt.com when `analytics.enabled = true`, and saved rules that let matching commands run
without asking. Halcyonic sets only what keeps the person in control: the working directory, the
sandbox mode, an approval policy that asks (`on-request` or `untrusted`), and approvals routed to
the person rather than to a reviewer agent; it refuses a thread for which Codex reports other
settings. When a start names a model provider or a model, a thread Codex reports running on
another is refused too, so work meant for a local model never reaches a hosted one. Every request Codex sends to the model provider carries the originator `halcyonic`, a
user agent with the Codex version and the operating system, and turn metadata with the
installation id, the thread and session ids, the sandbox mode, whether analytics is on and, for a
workspace that is a git repository, its path, latest commit hash and whether it has uncommitted
changes; the working directory also reaches the provider in the conversation's environment
context. Codex writes each thread's rollout to the developer's `CODEX_HOME`, tagged `halcyonic`. The
adapter writes no logs. The Codex and OpenCode adapters drain their server's error output without
keeping it, as the Claude Code adapter does: it may hold secrets, such as a key that verbose
logging or a configuration error prints, and a start failure is journaled and shown on every
device. A start failure says the exit status and how to see the output: run the binary in a
terminal on the Mac.

When the OpenCode runtime is enabled, its server gets the same kind of allowlist, plus the names in
`HALCYONIC_AGENT_ENV`, and uses the developer's own OpenCode configuration and providers. What
OpenCode 2.0.18 itself sends off the Mac, whatever model runs ([local-models.md](../validation/local-models.md)):
its model catalog, fetched from `models.opencode.ai` at launch and every five minutes unless
`OPENCODE_DISABLE_MODELS_FETCH=true` reaches it through `HALCYONIC_AGENT_ENV`; and ripgrep,
downloaded from GitHub the first time an agent searches files when no `rg` is on the PATH it
inherits. Its configuration decides the rest, and its defaults do not keep work local: without a
configured model it uses a free hosted model of its own service (OpenCode Zen) even when local
models are listed, and it runs every tool without asking, `webfetch` and `websearch` included,
unless its permissions say otherwise. Halcyonic does not yet impose permission rules on OpenCode
sessions ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). When `HALCYONIC_OPENCODE_CONFIG_HOME`
is set, as `pnpm mac-setup local-model` sets it, OpenCode alone gets that directory as its
`XDG_CONFIG_HOME`: Halcyonic's own OpenCode settings, which ask before every shell command, refuse
`webfetch` and `websearch`, and name a model the Mac serves through Ollama as the default and as the
small model. The control plane refuses to start unless they are held to the settings file's
standard: the directory and its `opencode` folder are real folders owned by the user and closed to
others (mode 0700), and the `opencode` folder holds nothing but `opencode.json`, a regular file of mode 0600 that
holds only `model`, `small_model`, `permissions` and Ollama's context limits, with both models
served on this Mac. Other folders may sit beside `opencode`, since tools an agent runs inherit the
same configuration home. Their permissions are the person's to change; `pnpm mac-setup` reads them and
says what they allow. They are not rules imposed on each session: a project's own `opencode.json`
may still change them for its folder (seen for model settings,
[local-models.md](../validation/local-models.md); assumed for permissions).

When Create's companion is set up (`HALCYONIC_COMPANION_MODEL`, [ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)), the control plane asks
Ollama on loopback for one reply at a time. Ollama's API has no authentication, so any process of
the same user can ask the same model, as the agents' runtimes do, and while Ollama is stopped
another local account could listen on its port and receive the exchange, as with Seorak's port
below. The address must be `http://` on a loopback address with a port, and the control plane
refuses to start the companion while Node's environment proxy would send that address through a
proxy (`NODE_USE_ENV_PROXY` or `--use-env-proxy` with `HTTP_PROXY`, unless `NO_PROXY` names the
address). Before every turn it reads Ollama's model list and refuses a model that is not listed,
that Ollama would pass to another host (`remote_host`, `remote_model`), or whose name has a
`cloud` tag; the `remote_host` check has not been tried against a real remote model. It never pulls,
creates or deletes a model. The model gets no tools and no image, sees only the person's own words
and the companion's earlier replies under a fixed prompt (never a folder, a project, a runtime or
other work), and its reply is checked against the contract, refused if a field holds a control,
bidirectional control or mark, zero-width space, line or paragraph separator, byte order mark, tag
character or half a surrogate pair, and shown as the companion's reported words. Nothing it says
reaches a command: its proposal fills the headset's recap, and the ordinary commands are sent only
after the person reads the whole first task in the review and confirms it. An idea or spoken words
can carry instructions to the model: every angle bracket in what the client sends is written as an
entity, so the person's words cannot close their tags or spell a chat template's turn markers,
Halcyonic's own notes go only as system messages, with the rule repeated after the exchange, and no
model tried kept injected text out of everything it said: the review is the boundary
([companion-model.md](../validation/companion-model.md)). So the review counts a line as read only
once the headset has drawn it, holds Yes until every line of the layout showing has been, and reads
again any item drawn only in part when the text is laid out anew; a quick second press never
passes a part. The exchange's order and its four
questions are checked, but the client sends the companion's earlier replies back, so a client can
forge them; that shapes only its own reply. An exchange whose UTF-8 size could pass the model's
context (two bytes a token, so the engine never cuts its start) is refused before the model is
asked. One principal can keep the companion answering it back to back within its twelve turns a
minute, so another principal meanwhile hears it is busy; one person at a time is the companion's
case. The exchange is kept only on the headset, in the app's
private files, with the rest of a Create draft, for 7 days without a change; the control plane keeps
no session and journals, stores and logs none of it. Ollama keeps the prompt in memory while the
model is loaded and, at its default log level, logs counts and times but no text; with
`OLLAMA_DEBUG` set it may log more (not verified).

A runtime's list of models (`GET /api/runtimes/:runtime_id/models`,
[ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)) is read from
the runtime at each request and never cached or journaled; a failure is logged with the runtime id
and its code only. Adapters read each model field by field, so a provider's settings, keys and
headers never reach a client: OpenCode's `GET /api/model` carries each provider's settings, API
key included, of which only the model's own fields are kept, and `/api/provider` is never read.
Where a model is served is decided from the address the runtime sends its requests to, never from
the model's name, because a local model may carry a hosted model's name (this Mac's Ollama serves
`llama3.2:1b` as `gpt-4o:latest`), and each name says what serves the model. Listing launches the
OpenCode or Codex server when none runs. Listing Claude Code's models starts a short-lived Claude
Code process with an execution's environment, key included, in a temporary directory, so it may
reach Anthropic; the tests never list against the real CLI. A chosen model is checked against a
fresh list before anything runs, so a start never falls back to another model.

The control plane also holds one credential for each product whose conclusions it reads through.
Each is read from its file on every request, refused when other users can read the file, and never
logged or passed to launched agents:

- Salidium's consumer credential, in `<data dir>/salidium-credential`, reads Salidium's reports
  and nothing else. It is sent only after Salidium's discovery file and endpoint prove the same
  instance on loopback.
- Seorak's integration credential, in `<data dir>/seorak-credential`, reads Seorak's content-free
  measurements with the `sessions:read` and `replay:read` scopes, and the provider usage limits
  Seorak last observed, account wide, with `limits:read`, for the audience
  `http://127.0.0.1:4317/api/v1`, until it expires or the owner revokes it. It is sent only if it
  has Seorak's `srkx_` form, only to 127.0.0.1 on Seorak's port, and never along a redirect.
  Seorak publishes no way to prove that the process on that port is Seorak (the request is open
  with Seorak), so while Seorak is stopped another local account could listen there and receive
  the credential. With it, that account could read the same measurements through Seorak's loopback
  plane until the credential expires or is revoked. Seorak's unauthenticated `GET /data-plane`
  proves nothing, since any listener could answer it, and is not read.

The access token meets the same risk on loopback: while the control plane is stopped, another local
account could listen on its port and receive the token from a client that sends it, and the token
never expires. `pnpm devices`, `pnpm pair`, `pnpm demo` and `pnpm mac-setup --with-token` therefore
send it only after the server proves, again before every request that carries it, that it holds
it, follow no redirect (which would carry the token on without a proof), and stop at the first
request that can't be proved (`fetchWithProof`), so a control plane
restarted while `pnpm pair` polls, and whatever took its port, gets no token; what remains is the
moment between a proof and the request it precedes. For each proof the client sends a fresh
32-byte challenge to the public `GET /api/health` in `x-halcyonic-challenge`, and only the loopback listener answers, in
`x-halcyonic-proof`, with an HMAC-SHA256 under the token of a fixed label, the local address and
port the connection reached, and the challenge, which reveals nothing about the token. The clients
dial a literal address, 127.0.0.1 or [::1] (`localhost` is tried as each), and check the proof
against the address and port they dialled, so a listener on another port or address that relays the
challenge to the real control plane gets a proof for the control plane's address, not its own, and
no token. The headset over USB (`adb reverse`) and the editor ask for the same proof, dial only
`ws://` or `http://` at 127.0.0.1 or [::1] (never `localhost` or another scheme, which ends the
session), through no proxy, and follow no
redirect (`LoopbackProof` and `LoopbackProofHandler` in the client core). Each REST request opens
a connection of its own, never from a pool, asks the proof on it and sends the token on that same
connection, so only what just proved itself receives it, and a connection another program kept
open while the control plane was stopped is never used again. The realtime upgrade does the same:
it asks the proof on a connection of its own and upgrades that connection
(`LoopbackWebSocketTransport`). Each reads its answers whole and refuses one framed more than one
way. What answers without the proof ends the session, which says the headset
didn't send its access code; nothing answering is tried again. The proof stops an app on the
headset that listens on 127.0.0.1:47800 there while `adb reverse` isn't in place, and another
account on the Mac that listens on 47800 while the control plane is stopped; the limits below hold
all the same.

The glance (a spike in development builds only, [XR_CLIENT.md](XR_CLIENT.md)) is a Java client on the
headset that asks for the proof on one plain socket and sends the token only on that same connection,
once the answer proves the listener holds it, so no retry or pooled connection can carry it anywhere
else (`GlancePoll`, run whole against a real control plane and misbehaving listeners by
`tooling/glance`). It goes through no proxy, follows no redirect, reads bounded heads and bodies
within a 10 second deadline, and refuses a token file that is a link, not its own, or readable or
writable by anyone else.

These limits hold for the access token on the headset, the app's (`files/access-token`) and the
glance's (`files/glance-access-token`) alike:

- **The access token lives on the headset.** It is the owner's token, which never expires and is
  not a revocable device credential; anyone with adb on the unlocked headset can read it with
  `run-as`, since development builds are debuggable. It is for the owner's own headset only, and is
  removed with this, whole: `.tmp` is what a write left before its move, `.off` a token a session
  set aside, and `.new` where the app leaves one if it is stopped mid-move.
  ```bash
  adb shell run-as com.halcyonic.xr rm -f files/access-token files/access-token.off files/access-token.tmp files/access-token.new files/glance-access-token files/glance-access-token.tmp
  ```
- **The Mac's adb server answers every local account.** While the headset is attached, the adb
  server on the Mac's 127.0.0.1:5037 takes commands from any process on the Mac without
  authenticating it, so another local account, the threat the proof exists for, can read the token
  with `run-as` or add a mapping of its own. After a session, remove the token and stop the server
  (`adb kill-server`).
- **The proof's address binding does not reach across `adb reverse`.** The control plane names the
  address and port its own socket reached, which on the Mac is always its listener whatever port the
  headset dialled. So anything that routes to the Mac's 47800 lets whatever listens on the headset's
  127.0.0.1:47800 relay a challenge and receive the token: a second reverse mapping, and equally an
  `ssh -L`, `socat` or a proxy on the Mac. Keep one mapping only (`adb reverse --list`), 47800 to
  47800, and nothing else forwarding to 47800.
- **A stale token and an impostor read the same.** The proof can't tell a control plane holding
  another token from another program, so the app's line names both: the code doesn't match, or
  something else is answering in its place. Either way the token was not sent.

## Controls

| Control | Implementation |
| --- | --- |
| Loopback only | `HALCYONIC_HOST` must be `127.0.0.1`, `::1` or `localhost`; anything else is refused at startup |
| DNS rebinding | Every request's `Host` must name this server's loopback address and port, otherwise 403; on the network listener, an IP address or a `.local` name with the listener's port |
| Browser requests | Any `Origin` header or `Sec-Fetch-Site: cross-site` is refused with 403, which also blocks browser WebSocket upgrades, on both listeners |
| Authentication | Loopback: a bearer token on every request and WebSocket upgrade except `/api/health`; compared in constant time. Network listener: a paired device's credential, checked by its SHA-256 against the device registry, on everything except `/api/health` and `/pair`; the access token is never accepted there |
| Token storage | 32 random bytes in `<data dir>/access-token`, mode 0600; created once; never logged; `authorization` headers are redacted from logs. On a headset (a development build reaching the computer over USB) it lives in the app's private storage (`Context.getFilesDir()`), written there with `run-as` straight from the computer's file, whole and made mode 0600 before it takes the old one's place, never on shared storage. A development build that finds a token earlier builds kept in `/sdcard/Android/data/com.halcyonic.xr/files` moves it in at startup, before it reads a pairing; a release build removes it unread. Only a regular file with one name, of at most 64 bytes, holding a token in its own form (43 characters of base64url) is taken: it is looked at with `lstat`, opened with `O_NOFOLLOW` and `O_NONBLOCK` and checked again with `fstat` before a byte is read, so no link is followed in place of the file and no pipe holds the app, and nothing in a folder that is a link is read or removed. That holds for the file, not its folder: the folder is checked once, with `lstat`, before the file is opened, so a folder swapped for a link in that moment would be followed. A system call that fails other than with `ENOENT` or `ENOTDIR` (nothing there), `ELOOP` from the open (a link) or `EACCES`, `EPERM` or `EROFS` from the removal (refused) stops the move and is logged with the call and its errno, so a failure is never taken for nothing there, not a token or removed; a `chmod` that fails is logged as a mode that could not be set, and the token still moves. The new file is made mode 0600 where it can be while still empty, before the token is written (private storage alone already keeps other apps out). A copy the app can't remove, as one `adb push` left owned by `shell`, is logged with the command that removes it, and the owner replaces the token once after every headset moved it, since it sat readable over `adb` and USB file access and never expires (XR_DEVELOPMENT.md); none of this is yet checked on a headset |
| Logs and error text | The `authorization`, `cookie`, `x-halcyonic-proof` and `x-halcyonic-challenge` headers are redacted wherever a logged object carries headers; an error is logged as its type, code and stack frames, never its message, which can quote what it read, unless it is one of Halcyonic's own errors in fixed words (`OwnWordsError`: a setting's problem, a contract's issue paths, the speech engine's state), nor its other fields, such as the head of a malformed request with its Authorization header in Node's `rawPacket`. A runtime's or a provider's error text, which every device sees once journaled or read through (a turn's failure, a refused action, a lost connection, Seorak's reason text and labels, a model listing's failure), loses every exact copy of a secret Halcyonic holds or passes to a runtime, in its place which one it was, as "[redacted: Anthropic key]": the access token, the Anthropic key, OpenCode's server password, Salidium's and Seorak's credentials, and the `HALCYONIC_AGENT_ENV` values whose names have a secret's word in them (KEY, TOKEN, SECRET, PASSWORD, PASS, PASSPHRASE, PAT, AUTH, CREDENTIAL, COOKIE, SESSION, HEADER and the like, matched as whole words) or that read as a credential by themselves, each part of a value that does, as a header's value in `Name: value`, the password and `user:password` of any URL in one, up to the last `@` before the host, any value given to a secret's key in one (password, pwd, passwd, passphrase, secret, token, api_key, access_key, credentials, an Azure SAS's `sig`), as in `password=…;`, `?password=…` or `"api_key": "…"`, escaped quotes and commas in it included, and any value given to a secret's command-line option, as in `--password …`, however they look, all under their variable's name; a part of a value that is an absolute path is not held for looking random alone. It then loses every credential-shaped run as "[redacted]" (a scheme's credential, never a path, though one with `+` or `=` padding is base64's, never a path's; a URL's user and password, up to the last `@` before its host; well-known key shapes; a long random run, never a name built of words) (`core/redaction.ts`). The rest is kept as given, but the shapes can also take what only looks like a credential: a URL's user name, a word with a digit or `+`, `/` or `=` after Bearer, Basic or Token that is not a path, or a run of 32 or more characters mixing capitals, small letters and digits with few words in it, such as a random id. Only the first 4096 characters are read, and the result is cut to its field's limit. What a person reads to decide, a tool's title, what an approval asks for, a question's prompt, a test run's label and summary, loses only the exact copies, named, so the command a person approves is never guessed at; a credential Halcyonic doesn't hold stays in it, and a question's options stay as given, since an answer names them. The adapters report a title, a summary, a label, a question's texts and error text whole, never cut, since a held secret cut in two is no longer an exact copy; the control plane takes credentials out, then cuts each to its limit, counted in code points as the contract counts, with "[truncated]", a question with `fitQuestion` (each text to its field's limit, then a question past 16,000 characters shortened, prompts to 1000), once. A question with anything cut can no longer be answered, and a fitted one stays within the 16,000 whatever the markers add. An agent can type the marker itself (open, [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). What redaction knowingly misses is listed below the table. `tooling/log-calls.test.ts` fails on a log call that names something that can be private unless it was reviewed ([logging-audit.md](../validation/logging-audit.md)) |
| Network listener | Off unless `HALCYONIC_NETWORK_HOST` names an IP address; TLS only (1.2 or later), with a self-signed ECDSA P-256 certificate generated once into `<data dir>/network-key.pem` and `network-certificate.pem`, mode 0600; the key is never logged; 5 seconds for the TLS handshake, 10 for a whole request, 60 for a connection on which nothing moves, 5 between requests; 32 connections at once, 8 from one address, and 4 realtime connections per device; a WebSocket it closes waits a second for the client's answer |
| Pairing | Opened only from loopback with the access token (`pnpm pair`), one window at a time, for five minutes, closed by the first device that pairs, by three failed proofs, or by the owner; an eight-digit code, never logged or journaled; SRP-6a (RFC 5054, 3072-bit group, SHA-256) with the TLS certificate bound into both proofs; the salt and verifier derived once per window, so no exchange's work depends on the code; `/pair` refused outside a window before any cryptography; four exchanges at once, one per address, six connections per address a minute, 30 seconds each; what it turns away or cuts short without checking a code is counted by address and shown by `pnpm pair` |
| Device credentials | 32 random bytes per device (`hlcd_` and base64url), sent once, encrypted under the SRP session key inside TLS; the journal keeps only their SHA-256; revoked from loopback (`pnpm devices revoke`) or by the device itself, at once and for what is already open: its realtime connections handle nothing more and are cut off, its commands are rejected where they would act (`device_revoked`), and any answer to a request it opened before is replaced by 401 |
| Failed credentials | 30 refused credentials from one address in a minute, and it gets 429 for the rest of the minute |
| Content types | JSON only; `text/plain` and form bodies are refused with 415. The one exception, `POST /api/transcriptions`, takes `audio/wav` only, through a parser registered in a scope of its own so no other route gains it, and refuses JSON |
| Input validation | Every command, client message and query is validated against the contracts |
| Size limits | 1 MiB request bodies; 256 KiB for a companion request, whose exchange holds at most 20 messages and 24,000 characters; 960,044 bytes for a clip of speech, refused with 413 from its declared length before the body is read, or as soon as a chunked body passes it; 256 KiB WebSocket messages; slow WebSocket clients are disconnected |
| Data at rest | Data directory mode 0700; journal, WAL and SHM files mode 0600 |
| Host settings | `<data dir>/settings.json`, written by `pnpm mac-setup`, fills in the `HALCYONIC_` variables the environment leaves unset ([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)). Startup is refused unless it is a regular file opened without following a link, owned by the user running the control plane, mode 0600, at most 64 KiB, in a data directory that user owns and that is closed to others (mode 0700), with `"format": 1` and only known settings; anything but a regular file, such as a named pipe, is refused before it is opened. It may hold the project roots, the pinned OpenCode and Codex binaries, Halcyonic's own OpenCode settings, the voice files, the companion's model and its loopback Ollama, and the network listener's address, never `HALCYONIC_CLAUDE_AGENT`, `HALCYONIC_CLAUDE_EXECUTABLE`, `HALCYONIC_AGENT_ENV` or a model that can run on a remote service (a companion model with a `cloud` tag, or Ollama anywhere but loopback, stops startup), so a hand edit cannot start paid model use or pass a variable to agents. Its values are checked like the environment's, and the ready log names the settings taken from it and each OpenCode and Codex binary's path with whether its SHA-256 is the pinned one (a warning when it is not) |
| Project roots | Every root, from the environment or the settings file, must be an existing folder that may hold projects, or the control plane does not start (`folder-safety.ts`): never `/`, `/Users`, `/Volumes` or a whole drive in it, `/private`, `/var`, `/tmp` or `/opt`; nothing in `/System`, `/Library`, `/Applications`, `/usr`, `/bin`, `/sbin`, `/etc` (`/private/etc`), `/dev`, `/opt/homebrew` or `/Users/Shared`; nothing in `/private/var` but a folder made inside a user's own temporary folder (`/private/var/folders/xx/yyy/T`); not another person's home, the home folder (the account's own, not `$HOME`, which a launcher may change) or a folder holding it, Halcyonic's data or a folder holding it or in it, a hidden folder or Library in the home folder, a folder another user owns, or a folder any user can change |
| Logging | Log context carries identifiers only, never tokens, pairing codes, device credentials or their hashes, keys, instructions, agent text, clips of speech or their transcripts, or a companion's exchange or reply (only its outcome, view, times and token counts) |
| Agent working directories | Every real execution runs in its project's folder, and a client never sends a path ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). A project is bound to a folder a client chooses by naming one of the host's project roots (`HALCYONIC_PROJECT_ROOTS`) and a folder directly inside it: the host composes the path, refuses a symbolic link, a hidden name or `..`, and records the real path the file system itself gives (`realpath(3)`), so one folder has one recorded spelling whatever case or Unicode form the client used. Before each start the control plane, then the adapter before it launches anything, and the adapter again right before it hands the folder to the runtime (OpenCode's before each model read that sends the folder, and Codex's before it resumes a thread after a relaunch) ask the directory policy again: only real paths under a root, so `..` and symbolic links cannot escape it, and a path that now resolves elsewhere is refused. With no roots configured, no real runtime can start |
| Project folders | The host makes a new folder only directly inside a root, with a one-segment name of letters, digits, `.`, `_` and `-` that does not start with `.`, and a non-recursive `mkdir` that fails rather than follow or reuse anything already at the path. Right before it binds or makes anything, it checks that the root is still the folder it found at startup (same real path, no symbolic link along it, same device and inode); a root replaced since, by a link or another folder, is refused until the control plane restarts. A folder it made that then fails the policy is left in place and reported, with effect `unknown` and the folder's real path. One race remains: a root swapped for a link between that check and the `mkdir`, which takes a path, gets the new, empty folder made where the link leads; the failure then names it. It deletes no folder |
| Agent permissions | Runtime permission modes that take decisions away from the supervising person (`bypassPermissions`, `auto`) are refused as start options, and so are Codex's approval policy `never`, its granular policies and `danger-full-access` with `on-request`, under which Codex runs every command it does not flag as dangerous without asking |
| Agent processes | Stopped on close and when the control plane exits, including on a second signal during shutdown. Every Claude Code process and the OpenCode and Codex servers are recorded before they receive work and watched by a small process that stops them if the control plane dies, even by SIGKILL; the next start stops anything recorded that survived. Identity is checked before any signal. Codex starts each command in a session of its own, beyond the reach of a signal to its server's process group: ending the server's input makes Codex stop them, and a server that has to be killed is killed with all its descendants. A Codex server killed by anything else leaves its running commands behind |
| OpenCode server | Launched from the configured binary only, never from PATH; bound to 127.0.0.1 on a free port with a password generated per launch and kept in memory; refused unless it reports version 2.0.18 and the process id Halcyonic started; recorded (without the password, mode 0600) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies |
| Speech engine | Off unless the owner sets `HALCYONIC_WHISPER_BIN`, `HALCYONIC_WHISPER_MODEL` and `HALCYONIC_WHISPER_VAD_MODEL`, all absolute paths to existing files ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)). whisper.cpp's `whisper-cli` is launched from that binary only, never from PATH, once per clip, with no environment and nothing from the client in its arguments, and startup is refused unless it reports version 1.9.4. That it opens no port and reaches no network is `whisper-cli`'s own behaviour, observed with `lsof` (voice-transcription.md); nothing sandboxes it, and it runs with the owner's file and network access. A clip is read by Halcyonic's own WAV parser and its samples alone are written into a new WAV, mode 0600, in a fresh temporary directory of mode 0700. That directory is removed when the engine exits, whatever the outcome, or as the control plane exits if it does first; one left by a control plane that was killed is removed at the next start, once it is three minutes old. The engine runs in a process group of its own, which is killed whole after 15 s (two minutes for the warm-up at startup) or after 64 KiB of output, so nothing it started holds the Mac. One clip at a time per principal, six reaching the engine in each fixed minute, and one transcription at a time on the Mac; a transcript longer than a draft (4,096 characters) is refused. While one principal's clip is transcribed, another's is refused as `transcription_busy_on_mac`, which tells a paired device that someone else is speaking at that moment. Voice activity detection means a clip with no speech yields no text rather than invented words |
| Companion | Off unless the owner names its model (`HALCYONIC_COMPANION_MODEL`); Ollama only at a loopback `http://` address with a port and nothing after it, never through Node's environment proxy, and a model name with a `cloud` tag refused at startup. Before every turn the model must be in Ollama's list without `remote_host` or `remote_model`. `POST /api/chat` only, streamed, thinking off, no tools, `format` the reply's schema where the engine keeps to one, `num_ctx` 16,384, `num_predict` 512; no pull, create, delete or `keep_alive`. 30 s to the first token and 45 s for a turn, one retry within them (and one more request without the schema when the engine cannot keep to one, so at most three requests a turn); past either the request is closed, which stops the model. At most 4,096 characters of reply read. One turn at a time per principal and one on the computer, 12 a minute per principal. A request carries at most 20 messages and 24,000 characters (2,000 for a message of the person's) and must fit the context at two bytes a token; its order is checked for consistency, since a client can forge the companion's earlier turns; every angle bracket the client sends is written as an entity, and written back in the reply. Whether it can be asked is read at most once in 2 s, however often it is asked. The reply is model text: checked against the contract, never cut to fit, shown only as the companion's reported words, and never a command |
| Codex server | Launched from the configured native binary only, never from PATH, in its own process group, speaking JSON-RPC over its stdin and stdout, so it listens on no port; refused unless both `codex --version` and its answer to `initialize` report 0.157.0 and `ps` shows the launched binary; remote control switched off; a thread Codex reports working in another folder than the project's is refused; only methods on the stable API surface, never the experimental opt-in, with one under-development feature switched on per thread so the agent can ask the person questions (`default_mode_request_user_input`, guarded by an end to end test and withdrawn by the adapter option `answerQuestions: false`, [ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)); a question marked secret is shown but never answerable through Halcyonic; requests Halcyonic does not show the person (permission grants, MCP elicitations) are refused, which Codex takes as a denial or an empty answer; recorded (mode 0600, no secrets) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies |

### What redaction misses

Known, and accepted when the redaction work closed (2026-10-03). Leaks, where a credential stays:
- A credential Halcyonic doesn't hold, in error text, in a shape no pattern knows: shorter than 32
  characters, after no scheme word and without a known prefix, as a 20-character password a
  provider's 401 echoes; `Basic user:pass` that isn't base64; or a random token that reads as a
  name, 1.3 percent of random 32-character ones. In what a person reads to decide, any credential
  Halcyonic doesn't hold stays, by design, so the command they approve is never guessed at.
- A held value shorter than 8 characters, too likely an ordinary word to replace wherever it
  appears, and a held value that reaches the text in another form, split across lines or encoded
  otherwise than as written or percent-encoded.
- An agent value with no secret's word in its name that holds a credential in no form read here:
  not random-looking, as a password made of words, and not in a URL, a `key=value` pair under a
  secret's key, a header or a long command-line option; a short option such as `-p value` is not
  read. A part of a value that is an absolute path is not held for looking random alone.
- Error text past its first 4096 characters, which is never read and never journaled.
- A credential in the agent's own messages, a question's options or a question's header, reported
  as given (agent messages are an open question), and Salidium's understanding text, the agent's
  statements and steps, which devices read as Salidium gives it.
- Events journaled before 2026-10-02, when redaction began, keep what they held: the journal is
  append-only and replayed as written.
- The marker, which an agent can type itself, and a question or approval the sink or the recorder
  drops, which leaves the agent waiting while the person sees nothing (both open,
  [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

Over-redaction, where something that is no credential is taken out, never a leak:
- A value given to a secret's key or option that is no secret: `"credentials": "same-origin"`
  holds `same-origin`, and `mysql --no-password --insecure` holds `--insecure`.
- A path held by its key or name: `PWD=/Users/me/project` in an agent value holds the project's
  folder, and `SSH_AUTH_SOCK` holds its socket's path, since a value held by its name, key, option
  or URL is held even when it starts with `/`, as a base64 secret does about once in 64.
- A URL glued to an email address by a comma without a space loses its host with its password.
- What only looks like a credential, in error text: a URL's user name, a word with a digit after
  Bearer, Basic or Token, a long run that reads as random.

## Project folders and clients

What a client sees and can do about folders on the host
([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)):

- **Paths reach every authenticated client**, paired devices included: the listing of roots and
  the folders directly in them (`GET /api/locations`), each project's `location` and each
  execution's `directory`. Folder names are the file system's, so clients treat them as untrusted
  text, as they do agent text. A device could already start agents that read everything under the
  roots, so the names give it nothing it could not reach; they do show the Mac's folder layout and
  user name to anyone holding a device credential.
- **A device can make empty folders** directly inside a root, one per `project.create` or
  `project.set_location`, and bind projects to any folder directly inside a root. It cannot make
  one elsewhere, name a deeper folder, follow a link, or delete anything. Nothing limits how many
  it makes: there is no per-principal or per-device limit on folder creation, so a paired device
  can fill a root with empty folders until the owner revokes it.
- **Listing reads a bounded part of each root**: at most 10,000 entries, in the order the file
  system returns them, then sorts the folders among them and keeps 200; a root with more entries is
  marked truncated, and its folders past the first 10,000 entries are not offered.
- **A root's label** (`label`, beside its folder's own `name`) is that name, or, where another root
  reads the same, the name with folders above that tell them apart, nearest first and written in
  path order ("Projects (Work)" for `/Volumes/Work/Projects` beside "Projects (person)", or
  "Projects (Personal, Work)" where one folder above is not enough), or a number where nothing does
  (`rootLabels`). No two labels read alike once case and compatibility forms are folded; only labels
  still alike reach further up, a root named by its own folder alone keeps that name, and none holds a
  path. Startup refuses `/`, `/Users`, `/Volumes` and a whole drive as roots (`assessFolder`). A label
  can change when the roots change, so it is shown, never kept as a choice's identity: a choice sends
  the root's path. `ProjectFolder.Current` reads a kept choice's label again from the latest listing,
  saying a place is gone rather than showing an old label; New project calls it once lane C's wiring
  lands, and until then a restored draft can show a stale label beside the right path. `label` is
  optional in the contract, so a headset reads an older host that sends none, showing `name`. Labels
  come from folder names, so they may hold control, bidi or private-use characters; the host leaves
  them as they are and the headset shows them by `LabelText`'s rule, as it does every name. Projects
  are named, and look-alikes compared, by `name`.
- **What the listing tells about a folder** comes from the folder's own entry and from one name
  inside it, never from a file's contents: whether a `.git` folder or file sits directly inside it
  (`repository`), the newer modification time of the folder and of that `.git` entry
  (`changed_at`), and the projects bound to it (`used_by`): a project counts while its recorded
  path is still its own real path, the rule every start applies, matched to the folder by device
  and inode. Each folder costs at most three `lstat` calls, each leaving a link at the end of its
  path unfollowed; each bound path directly inside a root is read once, and no other is read. Facts
  are kept only when the folder is the same directory after the reads as before, and a root found
  replaced after its folders were read lists as missing with none. A folder swapped for a link and
  back between two reads can still have one fact read elsewhere, which only someone who can already
  read the file system can arrange. So a paired device
  learns which folders are repositories and roughly when each last changed at its top level, which
  a device that can start an agent there could learn anyway; project ids it already sees in the
  snapshot. Connect a folder in the client core (`FolderConnect`, not yet drawn on the headset)
  offers only folders whose `used_by` is empty, and connecting one is an ordinary `project.create`
  with an `existing_folder` choice, checked like any other; the host does
  not refuse a folder another project already uses (ADR 0020 lets projects share one), so two
  devices, or a listing gone stale, can still bind one folder twice. Facts are a moment's: a
  folder replaced by another between the listing and Connect is bound with no word of the change.
  Cost: one `lstat` per distinct bound path inside the roots, so a device that makes many projects
  in one folder adds one read, not one per project.
- **Folder names are someone else's words.** Anything that can write in a root (an agent working
  there, for one) can name a folder to look like another or to read as Halcyonic's sentence. Connect
  a folder's words (`ConnectText`) show names by `LabelText`'s rule, quote them inside Halcyonic's
  sentences, and mark a free folder whose shown name looks like another free folder's, a folder in
  use or a project's (spacing, case, compatibility forms and invisible characters aside) with "Look-alike
  name", keeping its place and change time on the row to tell them apart;
  Projects marks a project whose name looks like another project's or a free folder's the same way, so
  a decoy folder named after a project, and the project it imitates, both carry the mark. A name that
  shows as nothing (only white space, or characters `LabelText` drops) shows each character's code
  point instead (`LabelText.Name`), so it can neither vanish nor stop the menu from building. Letters
  of other scripts that look alike are not caught, and the newest folders come first, so a decoy made
  a moment ago stands above the folder it imitates with only its facts and that mark to tell them
  apart. A row's key holds the root's absolute path and the folder's raw name: it is never shown and
  kept out of every log. Every press in Projects goes through `ProjectsScreens.Allows`, which acts
  only on a prompt or row the frame built from the same state shows as available, so a stale press,
  a second Connect, or Add a task in the demonstration does nothing.
- **A folder that changes after it was bound** is checked again at every start: gone or no longer
  a folder is `location_missing`, now leading elsewhere through a symbolic link is
  `location_missing` too, and outside the roots is `location_not_allowed`. Work already running
  keeps its folder.
- **Two executions can share a folder**: parallel workstreams of one project, or two projects bound
  to one folder. Nothing isolates them from each other's edits
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

## Setting up the Mac

`pnpm mac-setup` checks the Mac's setup and writes the settings file
([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md),
[mac-host-setup.md](../validation/mac-host-setup.md)). What it touches:

- **Credentials.** It never opens the Seorak, Salidium or Anthropic credential files: it checks only
  that each exists and that other users cannot read it. A credential still moves only as a file
  with mode 600, never through the clipboard, a prompt or an AI. Whether Seorak accepts its
  credential it learns from the running control plane (`GET /api/usage-limits`).
- **The access token.** By default it reads none, and asks the running control plane only its
  public health check. With `--with-token` it reads the token and sends it only after the server
  proves it holds it (above), then asks on loopback which folders, agent apps and paired devices it
  has, as `pnpm devices` does.
- **The person's own files.** With Codex set up, it reads the top-level `model_provider` line of
  `$CODEX_HOME/config.toml` (`~/.codex` by default) and prints only a provider id that matches
  `[A-Za-z0-9._-]{1,64}`; no other part of the file is parsed or shown. It never reads the person's
  own OpenCode settings, which may hold provider keys.
- **Binaries and models.** It records the pinned OpenCode and Codex binaries and the voice models only
  when their SHA-256 matches the pins Halcyonic was checked with on Apple silicon; elsewhere it says
  it cannot check them. It downloads nothing: what needs a download it shows as a command.
- **Folders.** Allowing a folder says what it allows and asks first. It refuses what the control
  plane refuses as a root (Controls, "Project roots"), and says a personal folder such as Documents
  holds much more than projects before it asks.
- **Pairing.** It turns the network listener on only on `pnpm mac-setup pairing on`, after saying
  what it opens (an encrypted listener for every device on the network, the macOS firewall prompt,
  that anyone on the network can try to pair while `pnpm pair` runs) and asking. Nothing else turns
  it on.
- **What it writes.** The settings file, and Halcyonic's own OpenCode settings in
  `<data dir>/opencode-config`, each mode 0600 in directories of mode 0700, through a new file
  renamed into place.

## Authorization

There are two kinds of principal: `local`, whoever holds the access token, which only loopback
accepts, and `device`, a paired device, which only the network listener accepts. Every command
records the principal that sent it, as the control plane authenticated it, beside the client's
self-declared identity, which is recorded for audit and never trusted. What differs between them
today is device management: opening a pairing window, listing devices and revoking one are served
on loopback only, and a device can revoke no credential but its own. Commands are admitted alike
for both: any authenticated principal, a paired device included, can rebind any project to another
folder with `project.set_location`, which changes where that project's later work runs, as it can
start work in any project. Every accepted command records its policy category (`low_consequence`,
`review_required`, `high_consequence`); categories do not yet restrict anyone. No
`high_consequence` command exists; merge, deploy, delete and destructive commands must not be added
until explicit human confirmation and a policy that tells principals apart exist.

A conversational model must never turn vague speech into permission for an irreversible action.

## Audit

The journal is the audit log. For each command it records the full command, when it was received,
through which transport and from which principal, its policy category, the admission decision,
and the runtime confirmed outcome or failure (including whether the effect is unknown). Commands
journaled before principals were recorded read with a null principal. Pairing and revoking
devices are journaled too (`device.paired`, `device.revoked`, [EVENTS.md](EVENTS.md)); pairing
windows and failed attempts are logged, without the code. Instructions and answers to an agent's
questions are work content and are journaled locally; they are never logged. No question asking
for a secret can be answered through Halcyonic: admission refuses an answer to any prompt marked
secret, whatever the adapter said, and a refused answer is journaled with its keys only, so what a
client sent with it never reaches the journal or the other clients. Nor can another client learn it
by resending guesses under its command id: only the principal that sent it is told whether a
resend matches, at most six times a minute (DOMAIN_MODEL.md). A question that asks for a
secret in plain words, without the runtime marking it secret, cannot be told apart and is
answerable like any other.

## Devices on the network

What each party can do with the network listener on
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md),
[network-pairing.md](../validation/network-pairing.md)):

- **A hostile device on the same network** sees that a TLS service listens on the port, and its
  self-signed certificate. Without a credential it gets `/api/health` and nothing else. It cannot
  pair outside a window the owner opened; inside one it gets three guesses at an eight-digit code
  in all, so one chance in 33 million, and SRP gives it nothing to test offline, even when it sits
  in the middle and relays every message: the device binds the certificate it saw into its proof,
  and the relay's is not the control plane's. It cannot read or change a paired device's traffic,
  because the device refuses any certificate but the one it pinned before sending anything. It can
  deny service: flood the port, use up a window's three attempts (the owner sees the window close
  after three failures), hold the four exchange slots open without guessing (`pnpm pair` shows
  each connection turned away or cut short, with its address), hold the 8 connections one
  address may, each for at most 5 seconds without finishing TLS and 10 without a request, fill all
  32 from several addresses, or block traffic. Its timing tells it nothing about the code: an
  exchange's work depends only on a secret it draws itself.
- **A stolen or lost headset** holds its credential in app-internal storage and can do whatever
  its owner could over that network, until the owner revokes it with `pnpm devices revoke`, which
  refuses the credential and ends what it has open at once: a realtime connection handles nothing
  more, even if the headset ignores the close frame, a command in a request opened earlier is
  rejected and journaled as such, and no answer reaches it. What the headset started before the
  revocation keeps running: revoking stops the device, not work it already set going, which the
  owner can interrupt from loopback. Paired devices cannot see one another: no device reads device
  events. It reaches the control plane only
  where the listener is reachable, the owner's network. A development build is debuggable, so
  anyone with `adb` access to the unlocked headset can read the credential, or an access token put
  there for USB, with `run-as`; an access token, unlike a device credential, works until the owner
  replaces it on the computer; the
  headset's own lock is the first defense. It holds no provider key, repository secret or SSH
  key.
- **A web page**, on this machine or another, cannot drive either listener: both refuse any
  `Origin` and cross-site fetches, the network listener refuses `Host` names other than addresses
  and `.local` names, and browsers refuse its self-signed certificate.
- **Another account on this machine** can reach the network listener as a device on the network
  can. It cannot read the data directory (mode 0700), so neither the access token nor the key.
- **Someone who sees the code** during the five minutes it is valid, and is on the network, could
  pair first. `pnpm pair` names the device that paired; revoke it and pair again.

Remaining risks: a device that pairs again gets a second credential, and the first stays valid
until revoked; credentials do not expire; the device's label is self-declared; a paired device can
hold the 8 connections its address may with slow requests, and devices on several addresses can
fill the listener's 32, a connection that sends no request for at most 10 seconds at a time.

## Untrusted text in the client

The XR client shows text Halcyonic did not write: workstream titles and objectives, which come
from commands; anything an agent or a tool wrote, such as messages, activity, approval requests
naming a shell command or a file path, and questions with their options; refusals and failures; setup problems carrying exception
text; what Salidium and Seorak say; names from runtimes; and, while pairing, a refusal in the words
of whatever answers at the typed address. TextMeshPro interprets text even with
rich text off: a backslash with u and four hex digits becomes that character whatever its
settings, a backslash with n, r, t or v a control character while escape parsing is on, the end of
text character U+0003, typed or escaped, ends a label there without an ellipsis, a carriage return
draws what follows over the start of the line, and zero width and bidirectional control characters
draw nothing or a mark over their neighbors ([workspace-interaction.md](../validation/workspace-interaction.md)).
An agent could hide the end of its own approval request that way, or make two commands read alike.
So ([XR_CLIENT.md](XR_CLIENT.md), "Words"):

- Every label that can show such text gets it through one rule in the client core (`LabelText`):
  no markup, in TextMeshPro and in Unity's `TextMesh` alike; every backslash doubled for
  TextMeshPro's escape parsing, so a backslash sequence shows as written; line breaks and tabs as
  spaces; and every control, format or default ignorable character, and half a surrogate pair,
  shown as its code point, as ‹U+202E›, so nothing is hidden or reordered. Every Private Use Area
  character shows as its code point too, as ‹U+E769›: Halcyonic's icons are drawn from those code
  points in their own font, so text from outside cannot draw a state's icon among its words.
- Nothing is cut short silently: a label cut short ends in an ellipsis, and no label uses
  TextMeshPro's italics or bold, which lose it. An approval is confirmed only once the whole
  request it answers has shown, in parts when it is long.
- The editor's render check puts hostile text on every workspace label and on a character's, and
  fails if one interprets any of it or cuts it short without an ellipsis. The line above the stage
  and the pairing line go through the same code but are not rendered by it.

Not covered: characters that only look alike, such as a Cyrillic letter for a Latin one or a
no-break space for a space, show as they look. The client shows an approval summary as the control
plane recorded it, which the contract limits to 2000 characters. The Codex, OpenCode and Claude
Code adapters report a summary whole, and the control plane cuts it to that limit with "[truncated]" after taking credentials out of it. A summary may select one field
from the runtime's request and omit others without a truncation mark
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

## Not yet built

- **Remote relay**, where the developer's machine connects outward and nothing is exposed
  unauthenticated. Pairing is for the local network only.
- **Policy per principal**, for example a `high_consequence` command that only the local principal,
  or a confirmation on the Mac, may send.
- **Rotation**: a device credential rotates by revoking it and pairing again, and the listener's
  certificate by deleting its key and certificate, after which every device pairs again; the
  access token still rotates only by deleting its file. Nothing expires.
- **Keystore protection** of the credential on the headset, and discovery without typing the
  address (mDNS).

Device pairing, encrypted transport for devices on the network, and per-device identity exist
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)); serving anything to the
internet still needs the relay and more.

Provider credentials (Anthropic, OpenAI and others) must stay with the runtime on the machine that
needs them. XR clients must never receive provider keys, repository secrets or SSH keys.
