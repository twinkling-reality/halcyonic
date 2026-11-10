# Approval levels: what each pinned runtime offers for working without asking (2026-11-10)

## Question

Which pinned runtime can let its agent work without asking the person, and what does it still refuse or keep
bounded when it does ([ADR 0043](../decisions/0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md))?

## Sources

Read on 2026-11-10 from each pinned binary or package on this Mac, no vendor web page:

- Codex 0.157.0: `codex app-server generate-json-schema` and `codex --help`.
- OpenCode 2.0.18: `opencode run --help` and the agent defaults in the binary's own code.
- Claude Agent SDK 0.3.283: `sdk.d.ts`.

## Findings

### Codex 0.157.0

- **The schema's policies:**
  - `AskForApproval` is `untrusted`, `on-request`, `never`, or a granular object (`sandbox_approval`, `rules`,
    `mcp_elicitations`, `request_permissions`, `skill_approval`).
  - `SandboxMode` is `read-only`, `workspace-write` or `danger-full-access`. The turn's `SandboxPolicy` adds
    `externalSandbox`. `ApprovalsReviewer` is `user` or `auto_review`.
  - `TurnStartParams` also takes `approvalPolicy` and `model`, per turn. The adapter uses neither.
- **The help:** it describes `never` as "Never ask for user approval Execution failures are immediately returned to
  the model". It lists `--dangerously-bypass-approvals-and-sandbox` as "EXTREMELY DANGEROUS".
- **What a workspace-write thread reports:** `{"type":"workspaceWrite","writableRoots":[],"networkAccess":false,
  "excludeTmpdirEnvVar":false,"excludeSlashTmp":false}`, so it writes the thread's folder, `$TMPDIR` and `/tmp`. A home
  `config.toml` with `[sandbox_workspace_write] network_access = true` or extra `writable_roots` widened it (AUDIT's
  probe, 2026-11-10); every thread now pins both above the home, and the adapter refuses a wider report.
- **End to end on the pinned binary** (fake provider, `codex-runtime.e2e.test.ts`, "never asking" and the two
  "AUDIT F1" tests), with the policy `never`, `workspace-write` and reviewer `user`:
  - An escalated command (`require_escalated`, a `touch` outside the workspace):
    - no approval request and no command ran;
    - the file outside was not written;
    - Codex handed the model its own refusal: "approval policy is Never; reject command ... you cannot ask for
      escalated permissions if the approval policy is Never".
  - A plain `rm -rf` of a folder outside the workspace, which Codex asks before under `on-request`: no request, and the
    folder's file is still there.
  - A command in the workspace: it ran without a request.
  - With the home widened as above, a never-asking `nc -z` to a loopback listener of the test's own reached it 0 times,
    and a `touch` into the extra folder wrote nothing (both failed on the code before the pin).
  - Not shown under `/tmp`: the test skips there, as `workspace-write` writes `/tmp` (ADR 0041, amendment of
    2026-11-04).

- **What `untrusted` asks before** (BOX-2, 2026-11-10, scripted provider, `exec_command`): every command tried. In a
  confined folder, 37 of 37, the read-only ones (`ls`, `pwd`, `cat`, `head`, `tail`, `wc`, `grep`, `rg`, `find`,
  `sed -n`, `nl`, `which`, `git status`, `git log`, `git diff`, `git show`, `git branch`, `date`, `node -v`, `sort`,
  `diff`, `echo`) included. On the open server, `ls`, `cat`, `git status` and `touch`. So an asking run under
  `untrusted` asks before every command.

### OpenCode 2.0.18

- **Rules:** each permission rule is `{action, resource, effect}`, with `effect` `allow`, `ask` or `deny`. The last
  matching rule wins, and no match asks ([opencode-permissions.md](opencode-permissions.md)).
- **The agent's defaults, read from the binary's code:**

  ```
  [{*,*,allow},{external_directory,*,ask},{read,*.env,ask},{read,*.env.*,ask},{read,*.env.example,allow}]
  ```

- **The help:** `opencode run --help` lists `--auto`, "Auto-approve permissions that are not explicitly denied".
- **End to end on the pinned binary,** inside Halcyonic's sandbox (fake provider, a test since withdrawn with
  OpenCode's offer), with session rules for never asking: the four denied tools, `shell * allow`, `external_directory *
  deny`, `read *.env deny`, `read *.env.* deny`, `read *.env.example allow`, `edit /* deny`, then every hidden-path and
  configuration edit denial:
  - One shell call writing outside the project and inside it:
    - no request;
    - the inside file was written, and the outside write failed (`write=1`, the sandbox's refusal);
    - OpenCode reported the session with exactly the rules it was given.
  - The write tool outside the folder: no request, nothing written.
  - The read tool on the project's `.env`: no request, and its text never reached the model. The control read of
    another file did reach it.

- **What the sandbox writes** (AUDIT's probe of the same rules, 2026-11-10): Halcyonic's sandbox makes every host
  project root writable, so one never-asking shell call created and overwrote files in another project under the same
  root, and nothing asked. So OpenCode does not offer `never_ask` until a task's sandbox writes only its own project.

### Claude Agent SDK 0.3.283

`PermissionMode` is one of:

| Mode | What the SDK says it does |
| --- | --- |
| `default` | prompts for dangerous operations |
| `acceptEdits` | accepts edits |
| `plan` | runs no tools |
| `dontAsk` | "Don't prompt for permissions, deny if not pre-approved" |
| `bypassPermissions` | "Bypass all permission checks"; needs `allowDangerouslySkipPermissions` |
| `auto` | a model classifier decides |

The SDK's `init` message reports the mode in effect, and `setPermissionMode` changes it mid-session. Halcyonic gives
Claude Code no sandbox of its own, so nothing bounds a command it runs without asking. Not run here: it is a hosted
runtime.

## Conclusions

| Runtime | Offers `never_ask` | What it is |
| --- | --- | --- |
| Codex | yes | policy `never` with `workspace-write`, reviewer `user` |
| OpenCode | not yet | its one server's sandbox writes every project under the host's roots; the rules above wait for a per-task sandbox |
| Claude Code | no | it needs a sandbox Halcyonic controls first ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |

## Not checked

- **OpenCode with the person's own configuration:** a configuration of the person's or the project's that asks, for an
  action Halcyonic's rules do not name, was not tried. By the rule order it still asks.
- **A runtime upgrade:** these findings must be verified again on each pinned upgrade. Codex's end to end suite pins
  its own; OpenCode's never-asking test left with its offer, and returns with a sandbox bounded to one task's project.
