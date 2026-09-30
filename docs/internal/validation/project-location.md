# Project location and a real start

- **Question:** With each project bound to one folder the host approves
  ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)), can a client that
  never names a path create a project, bind it to a new or existing folder, and start real work
  there through the control plane, with the runtime confirming the start; and does the control
  plane refuse, and report unknown effects, truthfully along the way?
- **Date:** 2026-09-30.
- **Environment:** an Apple M5 Max with macOS 26.7; Node.js 24.15.0; .NET SDK 10.0.401; Ollama 0.34.4
  on 127.0.0.1:11434 (`OLLAMA_CONTEXT_LENGTH=65536`, `OLLAMA_MAX_LOADED_MODELS=1`,
  `OLLAMA_NO_CLOUD=1`); OpenCode 2.0.18 and Codex 0.157.0, the pinned binaries; model
  `qwen3.6:35b-a3b-nvfp4`; Halcyonic on top of `3cbe884`.
- **Method:** a scratch control plane on 127.0.0.1:47851 started from a clean environment (`env -i`)
  with its own data directory, HOME, XDG directories and `CODEX_HOME`, one project root in a
  temporary directory (below, `<root>`), and both runtimes registered. Codex's `config.toml` named
  the `ollama` provider and the model, turned plugins and analytics off, and held no sign-in;
  OpenCode's `opencode.json` named the Ollama model, asked before shell commands and denied
  `webfetch` and `websearch`. A small client drove it over REST with the access token, as the
  headset would with its own credential, and read back the snapshot and the workstream's events.
  No hosted model was called, and nothing outside the temporary directory was written.
- **Status:** Runtime verified for OpenCode and Codex on a local model, for the flows below. Claude
  Code was not run, since every run spends Anthropic credit; its adapter change is covered by unit
  tests. The XR client's Create flow now chooses a folder from this listing, checked by its tests,
  a live control plane test with a project root and editor renders
  ([XR_CLIENT.md](../architecture/XR_CLIENT.md)); not checked on a headset.

## Real starts in a folder the host made

Each run: `project.create` with `{kind: 'new_folder', root: <root>, folder_name}`, then
`workstream.create`, then `execution.start` with `model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4'` and no
folder, asking the agent to write `hello.txt` with one line. The client approved any approval,
after reading it in full; none was raised.

| | OpenCode 2.0.18 | Codex 0.157.0 |
| --- | --- | --- |
| The folder | Made by the host, empty, not a git repository; the project's location `{name: 'greeting-opencode', created: true}` | Same, `greeting-codex`; no git repository and no trust entry needed |
| Start options | none | `context_window: 65536`, `auto_compact_token_limit: 52000` |
| `execution.start` | Accepted, then completed at 0.3 s, after `runtime.execution.started` | Accepted, then completed at 0.3 s, after `runtime.execution.started` and `runtime.model.used` |
| The turn | `runtime.turn.started`, the model reported, a `write` tool, the agent's message, `runtime.turn.completed` at 14.6 s | `runtime.turn.started`, two `commandExecution` tools, the agent's message, `runtime.turn.completed` at 7.9 s |
| `execution.created.directory` and the execution's `directory` | `<root>/greeting-opencode` | `<root>/greeting-codex` |
| The folder afterwards | `hello.txt` only, containing `hello from halcyonic.` | `hello.txt` only, containing `hello from halcyonic` |

The journal for each workstream read, in order: `workstream.created`, `command.completed`
(workstream), `command.accepted` (start), `execution.created` with the folder,
`runtime.execution.started`, `command.completed` (start), `runtime.turn.started` and the turn's
events. So the project, the workstream, the command's completion and the runtime's confirmation of
the start are separate facts, and `command.completed` followed the runtime's confirmation.

Codex ran its two commands without asking: under the adapter's default `workspace-write` sandbox
with `on-request`, Codex runs commands it can confine to the folder. The headset should not expect
an approval for such work.

## Refusals

Against the same control plane, after the starts. Each refusal was journaled as
`command.rejected` with the code, created nothing, and made no folder.

| Command | Code | Message, in part |
| --- | --- | --- |
| `project.create`, new folder named like an existing one | `location_exists` | "Something named greeting-opencode is already in <root>. Choose it as an existing folder, or give the new folder another name." |
| `project.create`, existing folder, a root the host does not have | `location_not_allowed` | "... is not one of the folders this computer lets agents work in." |
| `project.create`, existing folder that is not there | `location_missing` | "<root>/not-here is not there." |
| `project.create`, existing folder `.git`, and a new folder `../escape` | none: 400 `invalid_command` | The names do not match the contract; nothing is journaled |
| `execution.start` on OpenCode in a project created with no folder | `location_required` | "Runtime opencode works in the project's folder, and the project has none. Choose a folder for the project first." |
| The same, with the old `directory` option | `location_required` | Admission refuses before the adapter reads options; in a project with a folder, the option is refused as `invalid_runtime_options` (unit tests) |

**A folder moved on the Mac.** A project bound to `moved-app`, then the folder renamed to
`moved-app-renamed` outside Halcyonic: the next `execution.start` was refused with
`location_missing` ("<root>/moved-app does not exist."). `project.set_location` with
`{kind: 'existing_folder', root: <root>, folder_name: 'moved-app-renamed'}` completed, and the
project's location became `{name: 'moved-app-renamed', created: false}`. A folder replaced by a
symbolic link, and a folder removed between admission and the runtime's start, are covered by the
control plane's tests (the latter fails the start with `location_missing` and effect `none`, and
the execution reads `failed`, not `unknown`).

## An unknown effect

The control plane was restarted on the same data directory with `HALCYONIC_COMMAND_TIMEOUT_MS=100`,
shorter than a start takes, and a project made in a new folder, `unknown-effect`, on OpenCode:

1. `execution.start` was accepted. At 0.3 s the command had failed with `timeout`, "The runtime did
   not confirm the action within 100 ms. It may still take effect.", effect `unknown`, and the
   execution read `unknown` with the reason `start_outcome_unknown`.
2. The same command sent again, with the same command id, was answered `duplicate` with that
   failure: no second execution. The workstream had one execution throughout.
3. The runtime's own reports arrived next: `runtime.execution.started`, `runtime.turn.started`, the
   model, a `write` tool, the agent's message and `runtime.turn.completed` at 5.8 s. The first
   observation cleared `unknown`; the execution read `completed`, and `hello.txt` existed. The
   control plane logged a warning that the runtime answered after the command timed out.

So the command's record keeps its unknown effect while the execution shows what the runtime then
reported. A client that must not start the same work twice keeps the command id and resends it,
rather than sending a new start.

## What the control plane logged

Identifiers, codes and the warning above. No instruction or agent text. The only paths are the
configured project roots in the existing startup line ("control plane ready").

## End to end suites

With the adapters changed to take the folder from the project, the pinned binaries' own end to end
suites passed against their fake providers: OpenCode 19 of 19 and Codex 18 of 18. The OpenCode
suite's sandbox passed `OPENCODE_DISABLE_MODELS_FETCH` itself, which the adapter refuses as an
override since it sets the variable, so every OpenCode end to end test failed when its adapter was
constructed (observed before the fix; the sandbox and the refusal are the same on `3cbe884`, where
the suite was not run). The sandbox no longer passes it.

## Consequences

- A person can start real work from a client that knows no path on the Mac: choose a listed
  folder, or name a new one, and start. The folder needs no git repository for OpenCode or Codex.
- A refusal names the location problem and its remedy; a moved folder is recovered by binding the
  project again, not by starting over.
- An unknown effect is recorded as such and later resolved by the runtime's own reports; resending
  the command, never a new start, is the safe retry.
- Not verified: Claude Code in a new folder; two executions at once in one folder; a folder on a
  network or removable volume; a root with more than 200 folders in the headset.
