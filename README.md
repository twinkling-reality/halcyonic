<p align="center">
  <img src="docs/internal/product/brand/halora-mark.svg" width="84" alt="Halora">
</p>

<h1 align="center">Halora</h1>

<p align="center"><strong>The best way to build with AI agents is in VR.</strong></p>

<p align="center">
  <a href="https://youtu.be/Onu9mu5tyI4">Watch the film</a> ·
  <a href="#getting-started">Getting started</a> ·
  <a href="docs/internal/README.md">Documentation</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache_2.0-4c715d" alt="License: Apache 2.0"></a>
  <a href="package.json"><img src="https://img.shields.io/badge/Node.js-24.15%2B-5f7846" alt="Node.js 24.15 or newer"></a>
  <a href="docs/internal/runbooks/XR_DEVELOPMENT.md"><img src="https://img.shields.io/badge/VR-Meta_Quest_3-1c1e21" alt="Meta Quest 3"></a>
</p>

Halora is a VR-native AI harness that turns the usual agentic development workflow into something you can see and understand interactively or ambiently, whether you're building an app for VR or not.

Each agent is a character, so running many of them at once is easy to follow, and when one needs you, it calls for you. [Salidium](https://github.com/twinkling-reality/salidium) and [Seorak](https://github.com/twinkling-reality/seorak) are built into every run: Salidium explains what the agent did and why, and Seorak shows how well it did, measured on the real run. So whatever your technical level, you aren't building blind.

And because you're in VR, what you build can be VR too: say what you want out loud and agents build it, or turn an app, photo or video you already have into VR.

<p align="center">
  <a href="https://youtu.be/Onu9mu5tyI4"><img src="docs/images/gallery/halora-film.jpg" width="100%" alt="The Halora film: the Halora logo surrounded by twelve scenes from inside the headset."></a>
</p>

<p align="center">
  <img src="docs/images/gallery/agents-room.jpg" width="32%" alt="Three agent characters, each above its task card.">
  <img src="docs/images/gallery/three-agents-one-scene.jpg" width="32%" alt="A night island with a lake, glowing flowers and lanterns, built by three agents at once.">
  <img src="docs/images/gallery/piano-of-light.jpg" width="32%" alt="A piano of falling light.">
  <img src="docs/images/gallery/jellyfish-bloom.jpg" width="32%" alt="Glowing jellyfish in deep blue water.">
  <img src="docs/images/gallery/camel-ride.jpg" width="32%" alt="A camel ride across the dunes, made from a video.">
  <img src="docs/images/gallery/sky-tower.jpg" width="32%" alt="A tower-stacking game on a floating island.">
  <img src="docs/images/gallery/equipped-in-vr.jpg" width="32%" alt="Equipped, a web app, in VR.">
  <img src="docs/images/gallery/albedian-in-vr.jpg" width="32%" alt="Albedian, a web app, in VR.">
  <img src="docs/images/gallery/siterra-in-vr.jpg" width="32%" alt="Siterra, a web app, in VR.">
</p>

## How it works

Halora has two halves.

- **On your Mac**, a control plane (`apps/control-plane`, TypeScript on Node) runs the agents, keeps every change as an event in a local journal, builds and checks each task's copy, and talks to the headset.
- **On the headset**, a Unity app for Meta Quest 3 (`apps/xr`) shows your projects, tasks and agents in the room. It only ever talks to the control plane.

Agents plug in through one adapter interface, so the work stays the same whichever agent does it. Salidium and Seorak are read through the control plane, never by the headset directly. Supported today: Claude Code, Codex, OpenCode, Cursor, Kiro and Antigravity. Each runs confined to its task's folder on macOS.

The deeper design lives in the docs: [system](docs/internal/architecture/SYSTEM.md), [domain model](docs/internal/architecture/DOMAIN_MODEL.md), [integrations](docs/internal/architecture/INTEGRATIONS.md), [security](docs/internal/architecture/SECURITY.md) and [XR client](docs/internal/architecture/XR_CLIENT.md).

## Getting started

You need a Mac with **Node.js 24.15 or newer** and **pnpm**. For the headset you also need a **Meta Quest 3** (or 3S); building the app from source uses **Unity 6000.3.25f1** with Android Build Support and the **.NET 10 SDK**.

```bash
pnpm install
pnpm dev          # start the control plane on 127.0.0.1:47800
pnpm mac-setup    # connect agents, voice and the headset
```

`pnpm check` runs the type check, lint and every test. To connect real agents and a headset, follow [local development](docs/internal/runbooks/LOCAL_DEVELOPMENT.md), [XR development](docs/internal/runbooks/XR_DEVELOPMENT.md) and [headset session](docs/internal/runbooks/HEADSET_SESSION.md). Contributors and coding agents start with [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md).

## License

[Apache-2.0](LICENSE). Third-party fonts, SDK components and starter assets carry their own terms; see [NOTICE](NOTICE).
