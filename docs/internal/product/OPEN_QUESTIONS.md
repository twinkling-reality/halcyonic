# Open questions

Unresolved questions that must not silently become architecture. Resolving one means recording
the evidence (a validation record or an ADR) and removing it from this list in the same change.

## Product

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Are characters useful after repeated use, or decoration? | A kill condition | Hardware user sessions |
| What exactly belongs in the compressed state? | Defines the character presenter | XR shell prototyping |
| Does the characters' constant motion stay calm over a long session, in the corner of the eye while the person codes, or should working characters settle after a while? | Comfort and focus beside Virtual Display windows ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)) | Hardware user sessions |
| How much direct coding belongs in the expanded workspace? | Avoids rebuilding an IDE | Usage in the workbench |
| Does idea inception belong in the first release? | Scope | Product decision after the core mechanic works |
| When does guided learning help rather than distract? | Future policy design | Research after the base product works |
| What do competition judges run: a demonstration on the headset, a hosted control plane, or both? May they act in a demonstration, which cannot confirm a command? | Judges have no Mac and no `adb`, and the FAQ asks that an entry need no third-party device | The owner's decision on [ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md) |
| Would the competition accept an entry that depends on a service the entrant runs? The FAQ speaks only of devices. | Decides whether a hosted control plane is an option for judges at all | A written answer from the organizers |

## Integrations

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Claude Code: Agent SDK streaming input for hosted executions; how to attach to sessions a person started in a terminal? | Discovering existing work | Runtime smoke test; documented surfaces only |
| How does Halcyonic observe work it did not start (discovery and attach), per runtime? | The "existing project" entry path | Per-runtime validation |
| Should a project bind to one directory, and should parallel workstreams get their own git worktrees? Agents already work only under host-configured project roots, and clients name a directory per execution. | Starting real work from the XR client without typing paths | Product and security design |
| How are the pinned OpenCode and Codex binaries installed and updated for users, and should the control plane verify their checksums? Today each adapter checks the version its binary reports. | The OpenCode and Codex adapters run only a pinned binary ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md), [ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)) | Packaging decision |
| Codex: may a third-party product drive Codex with a person's ChatGPT sign-in, or only with an API key? No primary source says. The adapter uses whatever sign-in the developer's `CODEX_HOME` holds. | Halcyonic must not offer an authentication its vendor does not allow, as with claude.ai logins for Claude Code | A primary source or written confirmation from OpenAI |
| How are test runs identified in real runtimes? Only an inferred rule from commands is available. | `verifying` status and verification attention | Adapter design with an `inferred` provenance rule |
| Claude Code: can a background task raise an approval after its turn ended? The domain clears pending approvals when a turn ends. | Approvals must never vanish | A smoke test with a real model |
| Claude Code: `AskUserQuestion` arrives as an ordinary approval, and approving it gives no answers. How should questions reach the person? | Clarifying questions from agents | Adapter design after a real-model smoke test |
| Seorak: can a client prove that the process on the plane's port is Seorak before it sends the credential, as Salidium's discovery file allows? Seorak has no such mechanism yet and has recorded the request as deferred. | While Seorak is stopped, another local account could receive the credential ([SECURITY.md](../architecture/SECURITY.md)) | A discovery mechanism in Seorak |

## Platform and security

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Does a sustained WebSocket survive Quest focus changes, sleep and headset removal? | Multitasking is a kill condition | Hardware validation record |
| Does `System.Net.WebSockets.ClientWebSocket` work on Quest (Android, IL2CPP), including `wss://`? | The XR transport | First-week device test; keep a native fallback |
| Microphone behavior during multitasking and media playback | Voice interaction | Hardware validation |
| How do XR clients find the control plane on a LAN? | Local mode | Design plus hardware test |
| Device pairing and per-device identity | Required before serving beyond loopback | Security design and ADR |
| How does a headset receive a credential with no computer or phone involved? Today `adb` pushes the token. | A judge, or any user without `adb`, cannot connect otherwise; a credential shipped in the app is public | Security design, with device pairing |
| How are clients of one control plane kept from each other's work? Every client receives every event and holds the same credential. | Any control plane that more than one person reaches, such as a hosted one | Security design and ADR, before any hosted control plane |
| Remote relay provider and design | Remote mode | Later; not needed for the local slice |
| Scope of camera and environmental context | Privacy and permissions | Product and security design |
| Should the store build declare Meta VR Glasses (`stanley`) in `com.oculus.supportedDevices`? Meta's v207 default does; the store's manifest page lists only Quest identifiers, and Glasses are untested. | An upload could be refused, or claim a device never run on ([horizon-store-release.md](../validation/horizon-store-release.md)) | The owner, before the submission |

## Legal

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Name, trademark and domain clearance for "Halcyonic" | Branding | Legal review |
