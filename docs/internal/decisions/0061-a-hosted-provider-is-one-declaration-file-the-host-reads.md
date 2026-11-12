# ADR 0061: A hosted provider is one declaration file the host reads, never code

- Status: Accepted (the place and the host's list built; the readers move onto it in the 1111-b110 stack)
- Date: 2026-11-12

## Context

On 2026-11-12 hosted keys for two providers came into reach, and the product's direction is that nothing is built
for one vendor. On the tree of that day the same key was declared twice, in two places:
- the assistant's `HostedProvider` (LOOP, `workspace-agent/providers`);
- Add Key's `AgentKey` (LINK, runtime-core).

The key gate (VRIFY) and the held secrets (TASKS) would have needed a third and a fourth. A second provider meant
four edits, and a third meant four more.

## Decision

1. **A provider is data.** Each hosted provider is one `providers/<id>/provider.json` at the repository root, as a
   starter is `starters/<id>/starter.json`. It states:
   - `id` (the folder's name) and `name`;
   - `upstream`, https only, or http to this Mac for proofs;
   - `speaks`: the wire shapes it serves, of `chat_completions`, `messages` and `responses`, named by what they
     are, never by a vendor;
   - `key`: `file` (the private file in the data folder), `variable`, `field` (Add Key's words) and `header`
     (with its scheme, which the gate fills);
   - `models`, and an optional `tuning`.
2. **The shape is vendor-free**, in `packages/runtime-core/src/hosted-provider.ts`: `HostedProvider`,
   `ProviderKey`, `KeyState`, `WireShape`, `KeyReader`, `readerReaches` and `upstreamProblem`. LOOP owns the shape;
   CORE owns its place and the list.
3. **The host's one list** is `HostProviders` (`apps/control-plane/src/providers.ts`). It reads every declaration
   once, as the plane starts. A declaration that does not hold stops the start, saying which file and why, as a
   misconfigured runtime does. Two declarations of one key file are refused. It answers:
   - `list`, `byId`, and `serving(shapes)`;
   - `keyState(id)`: the private file only, never the environment;
   - `readersOf(id)`.
4. **A key's readers declare themselves** (`KeyReader`: an agent app's adapter, or the assistant), never the
   provider. A reader reaches a provider it names (or all) through a wire shape both speak.
5. **Every reader reads that one list:** the assistant, Add Key (one card per key file), the key gate (upstream,
   shape, header), the held secrets (every key file) and the model rows. On the rows, `RuntimeModel.provider` names
   the declaration a model is reached through, and a refused key counts per provider.
6. **The assistant** (LOOP, 2026-11-12) reads the list as the reader `ASSISTANT_READER`: every provider, through the
   wire shapes it speaks (`chat_completions`, `messages`), at each use, never turned on by a stored key. A provider's
   `tuning.effort` and `tuning.outputField` are mapped by each wire shape, which refuses a value it does not know.
   Its list order (the declarations' folder names) is the order Use Model offers the models in.
7. **The deciding test:** a third provider written in a test as one file reaches every reader with no other file
   changed. `tooling/architecture.test.ts` keeps vendors' names out of runtime-core and the core packages.

## Alternatives considered

- A TypeScript array of imported declarations (the assistant's `DECLARED_PROVIDERS`). It is code, and each provider
  needs an index line, so a third provider is not its own file alone.
- Keeping the key on the agent app (`AgentKey`, one per app). OpenCode can reach several providers, each with its
  own key, so a key belongs to a provider and an app declares which it reads.
- A schema in `packages/contracts` with generated C#. The headset never reads a declaration, so a TypeScript shape
  and the host's own check are enough; the contracts change only where a client reads something (`RuntimeModel`).

## Consequences

- A provider that speaks a known wire shape is one file and a stand-in for its test. A new wire shape is one
  protocol in the host, not the provider's.
- The vendor's words, its address and its key's names live only in its own folder.
- Every key file is a held secret by declaration. A reader that reads a key at its start says so, and Add Key then
  asks for a restart.
- Revisit when a provider needs something a declaration cannot state, such as a sign-in that is not a key.
