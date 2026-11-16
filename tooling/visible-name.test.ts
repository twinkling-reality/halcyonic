/**
 * The product's name where a person reads it (the owner, 2026-11-16: "Use Halora"). The code keeps its root
 * name, halcyonic: package names, identifiers, data folders, environment variables and log tags stay, and
 * only shown words changed (the start file is Start Halora.command). This holds the shown words of every
 * listed source to the new name, so the old one is never shown anywhere new. A part that shows the name
 * adds its source here in the commit that renames it (the headset's words with the film batch).
 */
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

/** Files whose strings and page text a person reads, hears or is shown: the Mac's pages, its words and the host's. */
const SOURCES: readonly string[] = [
  'apps/control-plane/src/checkpoints/store.ts',
  'apps/control-plane/src/checkpoints/task-copies.ts',
  'apps/control-plane/src/cli/login-agent.ts',
  'apps/control-plane/src/cli/mac-setup.ts',
  'apps/control-plane/src/config.ts',
  'apps/control-plane/src/core/command-service.ts',
  'apps/control-plane/src/folder-safety.ts',
  'apps/control-plane/src/http/routes.ts',
  'apps/control-plane/src/intelligence/catalog.ts',
  'apps/control-plane/src/intelligence/evaluation.ts',
  'apps/control-plane/src/intelligence/understanding.ts',
  'apps/control-plane/src/locations.ts',
  'apps/control-plane/src/main.ts',
  'apps/control-plane/src/network/pairing-page/index.html',
  'apps/control-plane/src/network/pairing-page/page.js',
  'apps/control-plane/src/network/setup-page.ts',
  'apps/control-plane/src/network/setup-page/setup.html',
  'apps/control-plane/src/network/setup-page/setup.js',
  'apps/control-plane/src/observer/file-observer.ts',
  'apps/control-plane/src/preview/check-output.ts',
  'apps/control-plane/src/preview/play-viewer.ts',
  'apps/control-plane/src/sending/github.ts',
  'apps/control-plane/src/speech/whisper.ts',
  'apps/control-plane/src/starters.ts',
  'apps/control-plane/src/workspace-agent/requests.ts',
  'apps/xr/Android/glance/src/com/halcyonic/glance/GlanceActivity.java',
  'apps/xr/Android/panel/src/com/halcyonic/panel/AgentPanelActivity.java',
  'apps/xr/Assets/Plugins/Android/AndroidManifest.xml',
  'apps/xr/ProjectSettings/ProjectSettings.asset',
  'packages/domain/src/admission.ts',
  'packages/domain/src/policy.ts',
  'packages/integrations/claude-code/src/claude-agent-runtime.ts',
  'packages/integrations/claude-code/src/personal-settings.ts',
  'packages/integrations/codex/src/codex-runtime.ts',
  'packages/integrations/codex/src/events.ts',
  'packages/integrations/codex/src/home.ts',
  'packages/integrations/codex/src/key-reader.ts',
  'packages/integrations/omnorium/src/connection.ts',
  'packages/integrations/omnorium/src/wire.ts',
  'packages/integrations/opencode/src/ollama-gate.ts',
  'packages/integrations/opencode/src/opencode-runtime.ts',
  'packages/integrations/opencode/src/reconcile.ts',
  'packages/integrations/salidium/src/connection.ts',
  'packages/integrations/seorak/src/client.ts',
  'packages/seatbelt/src/profile.ts',
];

/** What names the code, never the product to a person: dropped before a line is read for the old name. */
const NOT_SHOWN: readonly RegExp[] = [
  /@halcyonic\b/g, // package names
  /HALCYONIC_\w*/g, // environment variables
  /\.halcyonic\w*/g, // data folders
  /halcyonic[_-]\w*/g, // cookie, key and folder names
  /refs\/halcyonic\S*/g, // git refs
  /\w+Halcyonic\w*|Halcyonic\w+/g, // identifiers and file names
  /Halcyonic(?=\/)|\/Halcyonic\b/g, // paths
  /`Halcyonic execution /g, // the session title an agent app keeps for the host: seen only in that app's own list
  /"; Halcyonic.s confinement/g, // a comment inside the sandbox profile
  /TAG = "Halcyonic"/g, // the headset glance's log tag
  /companyName: Halcyonic/g, // Unity's company name: the Editor's data folder, never shown on the Quest (productName is)
];

/** The lines of `text` that show the old name: none of a comment, after what names the code is dropped. */
function shownOldName(text: string): number[] {
  const found: number[] = [];
  text.split('\n').forEach((line, index) => {
    if (/^\s*(\/\/|\*|\/\*|<!--|#)/.test(line)) return;
    let rest = line.replace(/\s\/\/\s.*$/, '');
    for (const pattern of NOT_SHOWN) rest = rest.replace(pattern, '');
    if (/\bHalcyonic\b/.test(rest)) found.push(index + 1);
  });
  return found;
}

describe("the product's shown name", () => {
  test('finds the old name in a shown sentence, and not where the code names itself', () => {
    assert.deepEqual(
      shownOldName("throw new Error('Halcyonic could not prepare the send.');"),
      [1],
    );
    assert.deepEqual(shownOldName("`Halcyonic's own data`"), [1]);
    for (const line of [
      "import { x } from '@halcyonic/contracts';",
      'const dir = env.HALCYONIC_DATA_DIR ?? `${home}/.halcyonic`;',
      "await inHalcyonicsCodexHome('x');",
      "'Halora was not started from the file Start Halora.command, so it cannot start itself again.'",
      '// Halcyonic keeps this in its own folder.',
      "const path = 'apps/xr/Assets/Halcyonic/Resources/run.json';",
    ])
      assert.deepEqual(shownOldName(line), [], line);
  });

  test('no listed source shows the old name', () => {
    const shown = SOURCES.flatMap((source) =>
      shownOldName(readFileSync(join(ROOT, source), 'utf8')).map((line) => `${source}:${line}`),
    );
    assert.deepEqual(shown, []);
  });
});
