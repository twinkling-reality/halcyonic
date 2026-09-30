# Open questions

Unresolved questions that must not silently become architecture. Resolving one means recording
the evidence (a validation record or an ADR) and removing it from this list in the same change.

## Product

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Are characters useful after repeated use, or decoration? | A kill condition | Hardware user sessions |
| What exactly belongs in the compressed state? | Defines the character presenter | XR shell prototyping |
| Does the characters' constant motion stay calm over a long session, in the corner of the eye while the person codes, or should working characters settle after a while? | Comfort and focus beside Virtual Display windows ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)) | Hardware user sessions |
| Do system windows, Virtual Display's above all, cover the characters standing on the desk within a meter of the person, where the stage's default placement keeps beyond two meters? | The desk is where the real room puts the characters ([ADR 0015](../decisions/0015-the-stage-stands-on-the-persons-desk.md)), and working beside the Mac is the point of multitasking | A headset check with Virtual Display open over the app |
| How much direct coding belongs in the expanded workspace? | Avoids rebuilding an IDE | Usage in the workbench |
| What form makes the shared vision visible and easy to revise while the person and companion create a project together? | A text-only setup form would miss the intended co-creation experience; a crowded spatial board could be just as hard to use | Interaction prototypes and user sessions |
| How present should the companion be during creation, and how does it relate to the characters that represent Workstreams? | The companion helps form a project; Workstream characters must still represent work, never a vendor session | Product design and headset sessions |
| What is the smallest credible co-creation loop for the first release, and when is the person ready to start building? | The target includes creating on the spot, but the first release scope and the safe defaults for real work are undecided | Product decision and an end-to-end prototype |
| What can Connect projects discover and show in its first release, and how does a person choose what is visible? | The entry path must be understandable without exposing runtime sessions as the primary objects | Per-runtime discovery validation and interaction design |
| When does guided learning help rather than distract? | Future policy design | Research after the base product works |
| The demonstration holds at its approval until someone answers. Should it continue on its own after a while, with an answer the recording names as its own, for a judge who never opens the character? | A judge who does not find the workspace sees the story stop at "Needs you" until the headset sleeps | The owner, after watching someone new to the app use it on a headset |
| Do judges read the demonstration's watch-only characters as a limit of the recording rather than of the product? Only one of its three workstreams can be directed ([ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)). | A judge who tries a watched character first finds no action | Headset sessions with people new to the app |
| Would the competition accept an entry that depends on a service the entrant runs? The FAQ speaks only of devices. | Decides whether a hosted control plane is an option for judges at all | A written answer from the organizers |
| Should the characters' cues play while a system window, Virtual Display's above all, has input focus? No cue starts without focus today, and in the second headset session Halcyonic stayed unfocused while Virtual Display was in use, so the characters are silent exactly while the person works on their Mac beside them. Meta's focus requirement asks apps to hide hands and ignore their input, not to mute ([meta-xr-platform.md](../validation/meta-xr-platform.md)). | Hearing "Needs you" at the side while coding is what the sound is for ([XR_CLIENT.md](../architecture/XR_CLIENT.md), "Sound") | The owner, after a headset session with Virtual Display: whether cues then help or intrude |
| Should "Needs you" repeat once, softer, when nobody has looked at the character for two minutes, as the soundbook proposed? It plays once today. | An approval nobody noticed stalls the work | Hardware user sessions |

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
| OpenCode: should Halcyonic give each session permission rules that ask before shell commands and refuse `webfetch` and `websearch`, as the Codex adapter imposes an asking approval policy? OpenCode 2.0.18 allows every action by default, so with a default configuration nobody is asked and an agent may fetch from the network. Session rules win over the person's own, so they would also turn a rule the person set to deny into one that asks. | Approvals are Halcyonic's defining interaction, and local models are chosen to keep work on the Mac ([local-models.md](../validation/local-models.md)) | Security design |

## Platform and security

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Does a sustained WebSocket survive Quest focus changes, sleep and headset removal? | Multitasking is a kill condition | Hardware validation record |
| On a desk, does the wider head gaze with a visible reticle make look and pinch reliable, and does the 20 degree palm exclusion let a seated person point without showing rays while typing? The placement and thresholds are tested in code, not yet on a headset. | The third Quest session found both interactions hard to use ([quest-3-device.md](../validation/quest-3-device.md)) | Repeat the seated desk interaction checks on the Quest 3 |
| Does a recenter with the palm gesture come with an input focus change? The stage in front of the person comes to the person after a recenter only when no focus change comes with it, since the reference space changes that come with focus changes are ignored ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md)). | A person who recenters to find the stage could find it stays where it was | The recenter check in the milestone 3 runbook |
| With system windows open over the app, can a person return input focus to Halcyonic with hands by pinching at its content, as Meta's requirements describe, also after minimizing the windows? And do the windows, drawn over the app, hide the workspace where they overlap it? | In the second headset session Halcyonic stayed unfocused until `adb` brought it to the front | The Virtual Display checks in the milestone 3 runbook |
| Do the pinned transports (`SslStream` with the pin in its own callback, the WebSocket upgrade and `WebSocket.CreateFromStream`) work under IL2CPP on a Quest 3, over Wi-Fi? `ClientWebSocket` works over `ws://`, cannot pin on the headset, and the pinned transports work in the editor's Mono with UnityTLS ([network-pairing.md](../validation/network-pairing.md)). | Every paired connection | The pairing checks in the XR runbook |
| Microphone behavior during multitasking and media playback | Voice interaction | Hardware validation |
| Should the headset find the Mac by mDNS rather than a typed address, and does Horizon OS resolve `.local` names? Pairing takes a typed address ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)). | Typing an address, and pairing again when it changes | A headset test of `.local` resolution, then a decision on NSD |
| Should release builds offer pairing? Only development builds do, so judges never see a control they cannot use ([ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)). | The owner's own release builds could not pair | The owner |
| Should the credential on the headset be wrapped with an Android Keystore key? It sits in app-internal storage, which a debuggable build exposes to `adb run-as`. | A credential copied off a headset works until revoked | A headset test of the Keystore through JNI |
| Should pairing again replace the device's earlier credential, and should credentials expire? Today each pairing adds a device, and nothing expires. | Forgotten credentials stay valid until the owner revokes them | Security design, after use |
| How are clients of one control plane kept from each other's work? Every client receives every event and holds the same credential. | Any control plane that more than one person reaches, such as a hosted one | Security design and ADR, before any hosted control plane |
| Remote relay provider and design | Remote mode | Later; not needed for the local slice |
| Scope of camera and environmental context | Privacy and permissions | Product and security design |
| Should the store build declare Meta VR Glasses (`stanley`) in `com.oculus.supportedDevices`? Meta's v207 default does; the store's manifest page lists only Quest identifiers, and Glasses are untested. | An upload could be refused, or claim a device never run on ([horizon-store-release.md](../validation/horizon-store-release.md)) | The owner, before the submission |
| How should a person inspect the full runtime approval request when an adapter's summary selects one field and omits others? When the displayed summary exceeds the contract's 2000 characters, should it be cut with "[truncated]" as all three adapters do today, kept whole under a higher limit, or refused? | The workspace shows the recorded summary before an approval ([SECURITY.md](../architecture/SECURITY.md), "Untrusted text in the client"), which may not contain the full request | Security design, then the adapters |

## Legal

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Name, trademark and domain clearance for "Halcyonic" | Branding | Legal review |
