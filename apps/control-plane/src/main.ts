import { execFile } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { chmod, mkdir } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadScenarios, MOCK_MODELS, MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { OpenCodeRuntimeAdapter } from '@halcyonic/integration-opencode';
import { systemClock, systemScheduler } from '@halcyonic/runtime-core';
import { CheckpointStore } from './checkpoints/store.ts';
import { localMacSetupIo } from './cli/mac-setup.ts';
import { Companion } from './companion/companion.ts';
import {
  type ControlPlaneConfig,
  defaultDataDir,
  downloadsDir,
  loadConfig,
  parseNetwork,
  portFamily,
} from './config.ts';
import { ControlPlane } from './core/control-plane.ts';
import { OllamaUnloader } from './core/ollama-unload.ts';
import { admittingAlso } from './directory-policy.ts';
import { registerDeviceRoutes } from './http/device-routes.ts';
import { registerRealtime } from './http/realtime.ts';
import { registerRoutes } from './http/routes.ts';
import { loadOrCreateAccessToken } from './http/security.ts';
import { createHttpServer } from './http/server.ts';
import { createUuidV7Generator } from './ids.ts';
import { omnoriumCatalogFor } from './intelligence/catalog.ts';
import { seorakEvaluationFor } from './intelligence/evaluation.ts';
import { salidiumUnderstandingFor } from './intelligence/understanding.ts';
import { openSqliteJournal } from './journal/sqlite-journal.ts';
import { createHostLocations } from './locations.ts';
import { DeviceAccess } from './network/devices.ts';
import { OPEN_LINK_KEEP_MS, openLinkPrivately, sweepOpenLinks } from './network/open-link.ts';
import { PairingPage, pairingControls } from './network/pairing-page.ts';
import { confinedPreviewTarget } from './network/preview-relay.ts';
import {
  mountSetup,
  pageSecrets,
  pageStopShare,
  RESTART_EXIT_CODE,
  runCommand,
  SetupView,
  settingsForRestart,
  setupDownloads,
} from './network/setup-page.ts';
import { NetworkSwitch } from './network/switch.ts';
import { managersOn } from './own-check/own-check.ts';
import { matchesPin, pinsForThisMac } from './pins.ts';
import { audioOutputFor, outputInTree } from './preview/audio-output.ts';
import { launcherFor } from './preview/launcher.ts';
import { installBrowser } from './preview/play.ts';
import { isPinnedTunnel, TUNNEL_PIN } from './preview/share.ts';
import {
  createRuntimeAdapters,
  heldSecrets,
  heldToFolder,
  OLLAMA_PORT,
  ripgrepOnPath,
  stopStaleRuntimeServers,
} from './runtimes.ts';
import {
  readHostSettings,
  rememberNetworkListener,
  SETTINGS_FILE,
  settingsInUse,
  withSettings,
} from './settings.ts';
import { spokenNames } from './speech/tidy.ts';
import { Transcriptions } from './speech/transcriptions.ts';
import { WhisperEngine } from './speech/whisper.ts';
import { HostStarters, originalFolderOf, originalRefusal } from './starters.ts';
import { OllamaWorkspaceModel } from './workspace-agent/ollama.ts';
import { WorkspaceRequests } from './workspace-agent/requests.ts';

async function main(): Promise<void> {
  // What `pnpm mac-setup` wrote fills in what the environment leaves unset (ADR 0024).
  const settings = readHostSettings(defaultDataDir(process.env));
  const config = loadConfig(withSettings(process.env, settings));
  // Its side ports follow its own (ADR 0047, amended): a second plane never takes 47800's.
  const family = portFamily(config.port);
  // A saved copy goes in the account's own Downloads from its own plane, beside the data folder from any
  // other (AUDIT2 S4), made here for the latter so a press finds it.
  const downloads = downloadsDir(config.dataDir);
  if (downloads !== join(homedir(), 'Downloads'))
    mkdirSync(downloads, { recursive: true, mode: 0o700 });
  await mkdir(config.dataDir, { recursive: true, mode: 0o700 });
  await chmod(config.dataDir, 0o700);
  const access = await loadOrCreateAccessToken(config.dataDir);
  const locations = createHostLocations(config.projectRoots);
  // Every task's own copy (ADR 0045) lives beside the data folder; only the runtimes may work there,
  // and only where copies are on.
  if (config.taskCopies) {
    await mkdir(config.tasksDir, { recursive: true, mode: 0o700 });
    await chmod(config.tasksDir, 0o700);
  }
  // Which starter a folder was made from, read from the projection once the control plane is built.
  let starterOf: (directory: string) => string | null = () => null;
  let originalOf: (directory: string) => string | null = () => null;
  // Every bound project's folder, read at each start, so no run reaches another project (BOX-3).
  let projectFolders: () => readonly string[] = () => [];
  // Built first, so a misconfigured runtime stops startup before anything else opens.
  const adapters = createRuntimeAdapters(config, {
    mock: new MockRuntimeAdapter({
      scenarios: loadScenarios(config.scenariosDir),
      models: MOCK_MODELS,
    }),
    directoryPolicy: config.taskCopies
      ? admittingAlso(locations.policy, config.tasksDir)
      : locations.policy,
    environment: process.env,
    dataDir: config.dataDir,
    starterOf: (directory) => starterOf(directory),
    originalOf: (directory) => originalOf(directory),
    projectFolders: () => projectFolders(),
  });
  // Opened before anything listens, so a speech engine that cannot report its version stops startup.
  const speech = config.speech === null ? null : await WhisperEngine.open(config.speech);

  const app = await createHttpServer({ logLevel: config.logLevel, token: access.token });
  // The browser checks and plays run (ADR 0037, S1): its copy in the data folder, or none.
  let browser: { folder: string; sha256: string } | null = null;
  if (config.browser !== null && process.platform === 'darwin') {
    try {
      const folder = await installBrowser(
        config.browser.folder,
        config.browser.sha256,
        config.dataDir,
      );
      browser = { folder, sha256: config.browser.sha256 };
    } catch {
      // The reason names the browser's path, so it is not logged.
      app.log.warn(
        'checks cannot pass and games are not played: the browser is missing or not the one set up',
      );
    }
  }
  if (browser !== null && launcherFor(config.dataDir) === null)
    app.log.warn(
      "checks run without their launcher, so with this app's macOS permissions, and games are not played: run pnpm mac-setup launcher",
    );
  // Whether each agent binary is the copy Halcyonic was checked with, whoever named it (ADR 0024).
  const agentBinaries = await checkAgentBinaries(config);
  for (const adapter of adapters) {
    if (adapter instanceof OpenCodeRuntimeAdapter && !adapter.sandboxed) {
      app.log.warn(
        { platform: process.platform },
        "OpenCode runs unsandboxed here: Halcyonic's sandbox is macOS only (ADR 0028)",
      );
    }
    if (
      adapter instanceof OpenCodeRuntimeAdapter &&
      adapter.sandboxed &&
      !ripgrepOnPath(process.env.PATH)
    ) {
      app.log.warn(
        "OpenCode can't search files: ripgrep isn't on this PATH, and its sandbox refuses the download (brew install ripgrep)",
      );
    }
  }
  for (const binary of agentBinaries.filter((entry) => entry.pinned === 'differs')) {
    app.log.warn(binary, 'an agent binary is not the copy Halcyonic was checked with');
  }
  for (const stale of await stopStaleRuntimeServers(adapters)) {
    app.log.warn(stale, 'a runtime server from an earlier run was still recorded');
  }
  const ids = createUuidV7Generator();
  const journal = openSqliteJournal({
    path: join(config.dataDir, 'control-plane.db'),
    originIfNew: 'live',
    ids,
    onMigrated: (from, to) =>
      app.log.info({ from, to }, `journal migrated from version ${from} to ${to}`),
  });
  if (journal.info.origin !== 'live') {
    journal.close();
    throw new Error(
      `${config.dataDir} holds a fixture journal; the live control plane refuses to serve it.`,
    );
  }

  // The tunnel that shares a game (F1) runs only as the pinned binary; any other leaves sharing off.
  const sharingTunnel =
    config.tunnelBinary !== null && (await isPinnedTunnel(config.tunnelBinary))
      ? config.tunnelBinary
      : null;
  if (config.tunnelBinary !== null && sharingTunnel === null)
    app.log.warn(`the tunnel binary is not the pinned ${TUNNEL_PIN.version}: sharing is off`);
  // The person's catalog (ADR 0039): its last answer checks links, so it exists before the control plane.
  const catalog = omnoriumCatalogFor(config.dataDir);
  const controlPlane = new ControlPlane({
    journal,
    adapters,
    ids,
    clock: systemClock,
    scheduler: systemScheduler,
    logger: app.log,
    commandTimeoutMs: config.commandTimeoutMs,
    locations,
    ...(config.gitBinary === null
      ? {}
      : {
          checkpoints: {
            store: new CheckpointStore({
              git: config.gitBinary,
              directory: join(config.dataDir, 'checkpoints'),
              // A project folder holding the journal and token is never checkpointed.
              protect: [config.dataDir],
            }),
            // A saved copy goes in the account's own Downloads folder (F3).
            keeping: { downloads: downloads },
            ...(config.taskCopies
              ? {
                  copies: {
                    directory: config.tasksDir,
                    protect: [config.dataDir],
                    heldToFolder: heldToFolder(adapters),
                  },
                }
              : {}),
          },
        }),
    // A project folder holding the journal and token is never read, as it is never checkpointed.
    observeFiles: { protect: [config.dataDir] },
    catalogLinks: catalog,
    starters: new HostStarters({
      trackedDir: fileURLToPath(new URL('../../../starters', import.meta.url)),
      installedDir: join(config.dataDir, 'starters'),
      dataDir: config.dataDir,
    }),
    // Checks and previews of starter projects (ADR 0037): Seatbelt, so macOS only.
    ...(process.platform === 'darwin'
      ? {
          previews: {
            dataDir: config.dataDir,
            home: homedir(),
            node: process.execPath,
            browser,
            // Read before every launch: one missing or changed since it was built is not used.
            launcher: () => launcherFor(config.dataDir),
            starters: {
              trackedDir: fileURLToPath(new URL('../../../starters', import.meta.url)),
              installedDir: join(config.dataDir, 'starters'),
            },
            // Codex confines a starter project's agent here (ADR 0041), when Codex is set up.
            confinesCodex: config.codexBinary !== null,
            ports: family.previews,
            play: {
              viewerPorts: family.viewers,
              // The game's sound, as CoreAudio records it for the browser's own processes (S1).
              audible: (pid) => outputInTree(audioOutputFor(config.dataDir), pid),
              // The viewer's one-time link opens in the Mac's default browser: the page there is
              // Halcyonic's viewer only; the game itself runs in the contained browser.
              openViewer: (link) => {
                execFile('/usr/bin/open', [link], (error) => {
                  if (error !== null) app.log.warn('the viewer of a play could not be opened');
                });
              },
            },
          },
        }
      : {}),
    // A project's own check (ADR 0055), in the agents' closed Seatbelt profile: macOS only.
    ...(process.platform === 'darwin'
      ? {
          ownChecks: {
            dataDir: config.dataDir,
            home: homedir(),
            node: process.execPath,
            managers: managersOn(process.env.PATH),
          },
        }
      : {}),
    // Send to GitHub (ADR 0039, amended): the person's own git and gh, for their reviewed command only.
    ...(config.gitBinary === null
      ? {}
      : {
          github: {
            git: config.gitBinary,
            gh: config.ghBinary,
            dataDir: config.dataDir,
            env: process.env,
          },
        }),
    // Sharing (F1, ADR 0054): on the Wi-Fi always; a link only by the pinned tunnel, its home in the
    // data folder it may not otherwise read.
    ...(process.platform === 'darwin'
      ? {
          share: {
            wifiPorts: family.shares,
            tunnel: sharingTunnel,
            home: join(config.dataDir, 'share-home'),
            dataDir: config.dataDir,
          },
        }
      : {}),
    // The person's project secrets (ADR 0057), set on this Mac's setup page only.
    projectSecrets: { dataDir: config.dataDir },
    secrets: heldSecrets(
      config,
      { environment: process.env, dataDir: config.dataDir },
      access.token,
      adapters,
    ),
    ollamaUnload: new OllamaUnloader({ url: `http://127.0.0.1:${OLLAMA_PORT}` }),
  });
  projectFolders = () =>
    controlPlane.projection
      .projects()
      .flatMap((project) => (project.location === null ? [] : [project.location.path]));
  // A task's own copy is its project's folder for what the project was made from (ADR 0045).
  const projectFolderOf = (directory: string): string => {
    const projection = controlPlane.projection;
    for (const workstream of projection.workstreams()) {
      const copy = workstream.copy;
      if (copy?.state === 'own' && copy.directory === directory)
        return projection.project(workstream.project_id)?.location?.path ?? directory;
    }
    return directory;
  };
  starterOf = (directory) => {
    const folder = projectFolderOf(directory);
    return (
      controlPlane.projection
        .projects()
        .find((project) => project.starter !== null && project.location?.path === folder)
        ?.starter ?? null
    );
  };
  // The original a VR version was made from, read from the journal at each start (M1.8c-1).
  // Bounded again here as at the command (AUDIT M18C1-M1, M18C1-S2).
  originalOf = (directory) => {
    const projection = controlPlane.projection;
    const projects = projection.projects().map((project) => ({
      id: project.project_id,
      folder: project.location?.path ?? null,
      starter: project.starter,
      madeFrom: projection.madeFrom(project.project_id),
    }));
    const folders = projects.flatMap((project) =>
      project.folder === null ? [] : [project.folder],
    );
    return originalFolderOf(projectFolderOf(directory), projects, (folder) =>
      originalRefusal(folder, {
        roots: locations.list().roots.map((root) => root.path),
        home: homedir(),
        dataDir: config.dataDir,
        projects: folders,
      }),
    );
  };
  controlPlane.reconcile();
  // Before anything can start a task (ADR 0045, amended): each task whose copy is gone says so, and
  // what the copies' folder holds that this journal does not is never removed, only counted.
  const copies = await controlPlane.noteCopies();
  if (copies !== null && copies.missing > 0)
    app.log.warn(
      { missing: copies.missing },
      'task copies this journal holds are gone from this Mac',
    );
  if (copies !== null && (copies.unheld > 0 || copies.other > 0))
    app.log.warn(
      { unheld: copies.unheld, other: copies.other },
      "task copies this journal does not hold are left where they are: another control plane's, or work an older journal does not name",
    );
  const transcriptions = new Transcriptions({
    engine: speech,
    clock: systemClock,
    names: () => spokenNames(controlPlane.projection.projects().map((project) => project.name)),
  });
  const companion = new Companion({ config: config.companion, clock: systemClock });
  const understanding = salidiumUnderstandingFor(config.dataDir),
    evaluation = seorakEvaluationFor(config.dataDir);
  const workspaceRequests = new WorkspaceRequests({
    controlPlane,
    ids,
    model: new OllamaWorkspaceModel({ config: config.assistant }),
    understanding,
    evaluation,
  });
  const sources = {
    understanding,
    evaluation,
    catalog,
    transcriptions,
    companion,
    workspaceRequests,
  };
  const devices = new DeviceAccess({ controlPlane, ids, logger: app.log });

  // Paired devices reach the control plane only through the network listener, which is off unless
  // the owner configured it (ADR 0017) or turns it on from the pairing page (ADR 0047).
  const network = new NetworkSwitch({
    dataDir: config.dataDir,
    controlPlane,
    sources,
    devices,
    clock: systemClock,
    logger: app.log,
    // A preview reaches a paired headset only when every agent's reads were confined (ADR 0052).
    previewTarget: (projectId) =>
      confinedPreviewTarget(controlPlane.projection, config.dataDir, projectId, family.previews),
  });
  registerRoutes(app, controlPlane, sources);
  registerRealtime(app, controlPlane);
  let page: PairingPage | null = null;
  registerDeviceRoutes(app, {
    controlPlane,
    devices,
    pairing: () => network.pairing,
    listener: () => network.listener(),
    page: () => page,
    sweepLinks: () => sweepOpenLinks(config.dataDir),
  });
  // Hand-over files of links whose code has run out go within a minute, whatever opened them.
  setInterval(() => sweepOpenLinks(config.dataDir), OPEN_LINK_KEEP_MS).unref();

  try {
    await app.listen({ host: config.host, port: config.port });
    if (config.network !== null) await network.start(config.network);
  } catch (error) {
    await network.stop();
    await app.close();
    workspaceRequests.close();
    await controlPlane.close();
    throw error;
  }
  // The pairing page (ADR 0047): it serves from the start, and opens nothing and turns nothing on
  // until the person presses there. The setting is written only once the listener has turned on or
  // off, so a listener that fails to start never stops the next start.
  try {
    page = await PairingPage.open(
      pairingControls({
        network,
        controlPlane,
        devices,
        listenOn: () => {
          const on = parseNetwork(
            {
              ...process.env,
              HALCYONIC_NETWORK_HOST: process.env.HALCYONIC_NETWORK_HOST || '0.0.0.0',
            },
            config.port,
          );
          if (on === null) throw new Error('the network listener has no address');
          return on;
        },
        remember: (on) => rememberNetworkListener(config.dataDir, on),
      }),
      {
        ports: family.pairingPage,
        // The setup view beside pairing (ADR 0053): every step `pnpm mac-setup` checks, with its presses.
        mount: mountSetup(
          new SetupView(
            {
              dataDir: config.dataDir,
              home: homedir(),
              io: (print) => localMacSetupIo(print),
              run: runCommand,
              appliedAtStart: settingsForRestart(settings),
              appliedNow: () => {
                try {
                  return settingsForRestart(readHostSettings(config.dataDir));
                } catch {
                  return 'unreadable';
                }
              },
              canRestart: process.argv.includes('--restartable'),
              // What is shared now, for Copy and Stop beside the steps (ADR 0054).
              shared: () =>
                controlPlane.projection.projects().flatMap((project) => {
                  const share = controlPlane.projection.share(project.project_id);
                  return share === undefined
                    ? []
                    : [{ project_id: project.project_id, name: project.name, ...share }];
                }),
              // Each project's secrets by name, with Set and Remove (ADR 0057).
              ...(controlPlane.projectSecrets === null
                ? {}
                : {
                    secrets: pageSecrets({
                      submit: (command, via, principal) =>
                        controlPlane.commands.submit(command, via, principal),
                      store: controlPlane.projectSecrets,
                      projects: () =>
                        controlPlane.projection.projects().map((project) => ({
                          project_id: project.project_id,
                          name: project.name,
                          secrets: project.secrets ?? [],
                        })),
                      // Each agent by its own name, as the page's steps name OpenCode and Codex.
                      without: () =>
                        controlPlane.registry
                          .descriptors()
                          .filter(
                            (descriptor) =>
                              !descriptor.synthetic && descriptor.secret_environment !== true,
                          )
                          .map((descriptor) => descriptor.display_name),
                      // Claude Code runs outside Halcyonic's sandbox on a folder the person connected.
                      outside: () =>
                        controlPlane.registry
                          .descriptors()
                          .filter((descriptor) => descriptor.kind === 'claude-agent')
                          .map((descriptor) => descriptor.display_name),
                      nextId: () => ids.next(),
                      now: () => systemClock.now(),
                    }),
                  }),
              stopShare: pageStopShare({
                submit: (command, via, principal) =>
                  controlPlane.commands.submit(command, via, principal),
                isShared: (projectId) => controlPlane.projection.share(projectId) !== undefined,
                nextId: () => ids.next(),
                now: () => systemClock.now(),
              }),
              tasksThatPause: () =>
                controlPlane.projection
                  .executions()
                  .filter((execution) =>
                    ['starting', 'running', 'verifying', 'waiting_for_human'].includes(
                      execution.status,
                    ),
                  )
                  .map(
                    (execution) =>
                      controlPlane.projection.workstream(execution.workstream_id)?.title ??
                      'a task',
                  ),
              // Closed cleanly first (the journal's checkpoint, the runtimes stopped), then the
              // launcher starts Halcyonic again on this exit code; tasks come back through re-attach.
              restart: () => {
                // A close that fails exits 1, so the launcher stops rather than start a half-closed one again.
                void shutdown({ reason: 'restart to apply settings' }).then(
                  () => process.exit(RESTART_EXIT_CODE),
                  (error: unknown) => {
                    app.log.error({ err: error }, 'closing for the restart failed');
                    process.exit(1);
                  },
                );
              },
            },
            setupDownloads(
              config.dataDir,
              localMacSetupIo(() => undefined).which,
              localMacSetupIo(() => undefined).pins,
            ),
          ),
        ),
      },
    );
  } catch (error) {
    app.log.warn({ err: error }, 'the pairing page could not be served');
  }
  if (page !== null && process.argv.includes('--open-pairing-page')) {
    openLinkPrivately(config.dataDir, page.link());
  }

  app.log.info(
    {
      addresses: app.addresses(),
      pairing_page: page === null ? null : `http://127.0.0.1:${page.port}/`,
      network: network.listener(),
      data_dir: config.dataDir,
      token_file: access.path,
      journal: controlPlane.journal.info,
      runtimes: controlPlane.registry.descriptors().map((runtime) => runtime.runtime_id),
      project_roots: config.projectRoots,
      speech: speech?.engine ?? null,
      settings: {
        file: join(config.dataDir, SETTINGS_FILE),
        used: settingsInUse(process.env, settings),
      },
      agent_binaries: agentBinaries,
      companion: config.companion?.model ?? null,
    },
    'control plane ready',
  );
  // The first transcription after whisper.cpp is built or updated compiles its GPU shaders, which
  // can take longer than a clip may; done here, it never keeps a person waiting.
  void transcriptions.warmUp().then(
    (ms) => {
      if (ms !== null) app.log.info({ ms }, 'speech engine warmed up');
    },
    (error: unknown) => app.log.warn({ err: error }, 'speech engine warm-up failed'),
  );

  let stopping = false;
  let signalled = false;
  // Runs once; later calls do nothing. Causes repeat and coincide: stdin ends and then closes, and
  // a launcher ended by the same Ctrl-C as this process closes stdin during the shutdown the
  // signal started.
  const shutdown = async (cause: { signal: NodeJS.Signals } | { reason: string }) => {
    if (stopping) return;
    stopping = true;
    app.log.info(cause, 'shutting down');
    // A stdin still being read would keep the process alive after a signal.
    if (config.exitOnStdinEnd) process.stdin.destroy();
    await page?.close();
    await network.stop();
    await app.close();
    workspaceRequests.close();
    await controlPlane.close();
  };
  const onSignal = (signal: NodeJS.Signals) => {
    if (signalled) {
      // A second signal exits at once; process exit handlers still stop launched agent processes.
      app.log.warn({ signal }, 'second signal; exiting without waiting');
      process.exit(1);
    }
    signalled = true;
    void shutdown({ signal });
  };
  process.on('SIGINT', onSignal);
  process.on('SIGTERM', onSignal);
  if (config.exitOnStdinEnd) {
    // The launcher holds stdin open and never writes to it, so stdin ends when the launcher exits,
    // however it exits. Anything written is discarded. The end of stdin, or a failure to read it,
    // stops the control plane as SIGTERM does, but never counts as a second signal.
    const stdinEnded = () => void shutdown({ reason: 'stdin ended' });
    process.stdin.on('end', stdinEnded);
    process.stdin.on('close', stdinEnded);
    process.stdin.on('error', stdinEnded);
    process.stdin.resume();
  }
}

/**
 * Each configured OpenCode and Codex binary's path, and whether its SHA-256 is the pinned one:
 * `matches`, `differs`, or `no_pin` where Halcyonic has no pin for this processor.
 */
async function checkAgentBinaries(
  config: ControlPlaneConfig,
): Promise<{ app: string; path: string; pinned: 'matches' | 'differs' | 'no_pin' }[]> {
  const pins = pinsForThisMac();
  const binaries = [
    ...(config.opencodeBinary === null
      ? []
      : [{ app: 'opencode', path: config.opencodeBinary, pin: pins?.opencode ?? null }]),
    ...(config.codexBinary === null
      ? []
      : [{ app: 'codex', path: config.codexBinary, pin: pins?.codex ?? null }]),
  ];
  return Promise.all(
    binaries.map(async ({ app, path, pin }) => {
      const matches = await matchesPin(path, pin);
      return {
        app,
        path,
        pinned:
          matches === null
            ? ('no_pin' as const)
            : matches
              ? ('matches' as const)
              : ('differs' as const),
      };
    }),
  );
}

main().catch((error: unknown) => {
  process.stderr.write(
    `control plane failed to start: ${error instanceof Error ? error.message : String(error)}\n`,
  );
  process.exitCode = 1;
});
