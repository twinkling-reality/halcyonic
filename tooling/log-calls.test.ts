/**
 * Every log call in the code that ships names no private text: AGENTS.md forbids logging secrets,
 * the access token, or instruction and agent message text, and the headset's log is readable over
 * adb ([logging-audit.md](../docs/internal/validation/logging-audit.md)). A call whose arguments,
 * outside their string literals and comments, name something that can be private must be one a
 * person reviewed and listed below with why it is safe; a new or changed one fails until it is.
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

/** What a log call may not name outside a literal, unless reviewed. */
const PRIVATE =
  /\b(message|getmessage|getbaseexception|detail|credential|accesstoken|token|titles?|text|instructions?|answers?|transcript|transcription|reply|replies|summary|body|content|prompt|words|label|code|path|address|host|password|authorization|cookie|proof|challenge|snapshot|json)\b/i;

type Kind = 'ts' | 'cs' | 'java';

interface Source {
  readonly kind: Kind;
  readonly roots: readonly string[];
  readonly include: (path: string) => boolean;
  readonly call: RegExp;
}

const SOURCES: readonly Source[] = [
  {
    kind: 'ts',
    roots: ['apps/control-plane/src', 'packages'],
    include: (path) =>
      path.endsWith('.ts') &&
      !path.endsWith('.test.ts') &&
      !path.includes('/testing/') &&
      !path.includes('/node_modules/'),
    call: /\b(?:log|logger)\.(?:trace|debug|info|warn|error|fatal)\s*\(/g,
  },
  {
    kind: 'cs',
    roots: ['apps/xr/Assets/Halcyonic', 'apps/xr/Packages/com.halcyonic.client/Runtime'],
    include: (path) => path.endsWith('.cs') && !path.includes('/Editor/'),
    call: /(?:\bDebug\.Log(?:Warning|Error|Format|Exception)?|(?<![\w.])Log(?:Warning|Error)?)\s*\(/g,
  },
  {
    kind: 'java',
    roots: ['apps/xr/Android'],
    include: (path) => path.endsWith('.java') && !path.includes('/test/'),
    call: /\bLog\.[viwde]\s*\(/g,
  },
];

/**
 * Reviewed calls: the file, the call's arguments as the scan reads them (literals blanked, spaces
 * collapsed), and why what it names is safe.
 */
const REVIEWED: readonly { readonly file: string; readonly args: string; readonly why: string }[] =
  [
    {
      file: 'apps/control-plane/src/core/command-service.ts',
      args: '{ command_id: command.command_id, command_type: command.command_type, rejection: admission.rejection.code, }, "" ,',
      why: 'ids, the command type and the rejection code',
    },
    {
      file: 'apps/control-plane/src/core/command-service.ts',
      args: '{ command_id: command.command_id, command_type: command.command_type, failure: failure.code }, "" ,',
      why: 'ids, the command type and the failure code',
    },
    {
      file: 'apps/control-plane/src/core/control-plane.ts',
      args: '{ note: note.code, event_id: note.event_id, position: stored.position, detail: note.message, }, "" ,',
      why: "a projection note's code and ids; its message is ids and numbers only (projection.ts, note())",
    },
    {
      file: 'apps/control-plane/src/core/recorder.ts',
      args: '{ note: note.code, event_id: note.event_id, position: appended.position, detail: note.message, }, "" ,',
      why: "a projection note's code and ids; its message is ids and numbers only (projection.ts, note())",
    },
    {
      file: 'apps/control-plane/src/http/routes.ts',
      args: '{ runtime_id: runtimeId, reason: result.reason.code }, "" ,',
      why: "a runtime id and the models answer's reason code",
    },
    {
      file: 'apps/control-plane/src/http/routes.ts',
      args: '{ code: answer.code, ms }, ""',
      why: "the transcription's refusal code and a time, never the transcript",
    },
    {
      file: 'apps/control-plane/src/http/routes.ts',
      args: '{ outcome: answer.body.outcome, audio_seconds: answer.seconds, ms }, "" ,',
      why: "the transcription's outcome and its audio length, never the transcript",
    },
    {
      file: 'apps/control-plane/src/http/routes.ts',
      args: '{ code: status.reason.code }, ""',
      why: "the companion's availability code",
    },
    {
      file: 'apps/control-plane/src/http/routes.ts',
      args: '{ code: answer.code, ms, ...answer.log }, ""',
      why: "the companion's refusal code, a time and CompanionLog's counts",
    },
    {
      file: 'apps/control-plane/src/http/routes.ts',
      args: '{ ms, ...answer.log }, ""',
      why: "a time and CompanionLog's counts",
    },
    {
      file: 'apps/control-plane/src/http/routes.ts',
      args: '{ next: answer.body.reply.next, view: answer.body.reply.view, ms, ...answer.log }, "" ,',
      why: "the companion reply's next and view, which are enums, a time and counts, never its words",
    },
    {
      file: 'apps/control-plane/src/main.ts',
      args: '{ addresses: app.addresses(), network: listener, data_dir: config.dataDir, token_file: access.path, journal: controlPlane.journal.info, runtimes: controlPlane.registry.descriptors().map((runtime) => runtime.runtime_id), project_roots: config.projectRoots, speech: speech?.engine ?? null, settings: { file: join(config.dataDir, SETTINGS_FILE), used: settingsInUse(process.env, settings), }, agent_binaries: agentBinaries, companion: config.companion?.model ?? null, }, "" ,',
      why: "the ready line: addresses, folders, the token file's path (never the token), setting names and binary paths, as SECURITY.md says",
    },
    {
      file: 'apps/control-plane/src/network/pairing.ts',
      args: '{ address: from, reason }, ""',
      why: "a refused pairing's remote address and a fixed reason, never the code",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Pairing/PairingPanel.cs',
      args: '"" + Address(host, port)',
      why: 'the address the person typed',
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Pairing/PairingPanel.cs',
      args: '"" + outcome.Code + (outcome.AttemptsLeft == null ? "" : "" + outcome.AttemptsLeft + "" )',
      why: "a refusal code kept to a code's own shape (PairingException) and a count",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Pairing/PairingPanel.cs',
      args: 'LogType.Log, LogOption.NoStacktrace, this, "" , message',
      why: 'the Log helper passing on its own message; its callers are scanned',
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Room/RoomPlacement.cs',
      args: '"" + read.Scan + "" + read.Detail',
      why: "the room scan's state and its fixed detail words",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Room/RoomPlacement.cs',
      args: '"" + detail',
      why: "fixed words about the room's anchor, from RoomPlacement itself",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Room/RoomPlacement.cs',
      args: '"" + detail',
      why: "fixed words about the room's anchor, from RoomPlacement itself",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Room/RoomPlacement.cs',
      args: '"" + detail',
      why: "fixed words about the room's anchor, from RoomPlacement itself",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Room/RoomPlacement.cs',
      args: 'LogType.Log, LogOption.NoStacktrace, this, "" , message',
      why: 'the Log helper passing on its own message; its callers are scanned',
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Scripts/ControlPlaneConnection.cs',
      args: '"" + target.Pairing.Address',
      why: "the paired control plane's address",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Scripts/ControlPlaneConnection.cs',
      args: '"" + text.Length / 1024 + "" + loaded + "" + parsing.ElapsedMilliseconds + ""',
      why: "the bundled demonstration's size and times",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Scripts/ControlPlaneConnection.cs',
      args: '"" + failed.Exception?.GetBaseException().Message',
      why: "a parse error in the demonstration bundled in the app, never the person's work",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Scripts/ControlPlaneConnection.cs',
      args: 'session + "" + (logged.Detail == null ? logged.Phase.ToString() : logged.Phase + "" + logged.Detail)',
      why: "the connection's phase and detail: fixed words, a socket's error, or a message it can't read named by type only (RealtimeSession)",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Scripts/ControlPlaneConnection.cs',
      args: 'LogType.Log, LogOption.NoStacktrace, this, "" , message',
      why: 'the Log helper passing on its own message; its callers are scanned',
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Scripts/ControlPlaneSettings.cs',
      args: '"" + error.Message',
      why: "the pairing store's fixed outer message, never the inner one that could quote the file",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Scripts/DeviceMeasures.cs',
      args: 'LogType.Log, LogOption.NoStacktrace, this, "" , message',
      why: 'the Log helper passing on its own message; its callers are scanned',
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Sound/StageSound.cs',
      args: '"" + rendering.Exception?.GetBaseException().Message',
      why: "an error rendering the app's own sound cues",
    },
    {
      file: 'apps/xr/Assets/Halcyonic/Sound/StageSound.cs',
      args: 'LogType.Log, LogOption.NoStacktrace, this, "" , message',
      why: 'the Log helper passing on its own message; its callers are scanned',
    },
    {
      file: 'apps/xr/Android/glance/src/com/halcyonic/glance/GlanceActivity.java',
      args: 'TAG, String.format(Locale.ROOT, "" , poll.code, poll.cause == null ? "" : "" + poll.cause + "" , took, waiting, working, visible ? 1 : 0)',
      why: "a poll code from GlancePoll's fixed set, the exception's class name and counts",
    },
  ];

interface Call {
  readonly file: string;
  readonly line: number;
  readonly args: string;
}

/**
 * The arguments of the call whose opening parenthesis is at `open`, with string, character and
 * template literals blanked (an interpolation's expression kept) and comments removed, up to the
 * parenthesis that closes it.
 */
export function argumentsAt(source: string, open: number, kind: Kind): string {
  let out = '';
  let depth = 1;
  let i = open + 1;
  while (i < source.length) {
    const c = source[i] as string;
    const next = source[i + 1];
    if (c === '/' && next === '/') {
      while (i < source.length && source[i] !== '\n') i++;
      continue;
    }
    if (c === '/' && next === '*') {
      const end = source.indexOf('*/', i + 2);
      i = end < 0 ? source.length : end + 2;
      continue;
    }
    const interpolated =
      kind === 'cs' &&
      (source.startsWith('$"', i) || source.startsWith('$@"', i) || source.startsWith('@$"', i));
    if (interpolated || (kind === 'ts' && c === '`')) {
      i = source.indexOf(kind === 'ts' ? '`' : '"', i) + 1;
      out += ' "';
      while (i < source.length) {
        const d = source[i] as string;
        if (kind === 'ts' ? d === '`' : d === '"') break;
        if (d === '\\') {
          i += 2;
          continue;
        }
        const opens =
          kind === 'ts' ? source.startsWith('${', i) : d === '{' && source[i + 1] !== '{';
        if (opens) {
          i += kind === 'ts' ? 2 : 1;
          let braces = 1;
          out += ' ';
          while (i < source.length && braces > 0) {
            const e = source[i] as string;
            if (e === '{') braces++;
            if (e === '}') braces--;
            if (braces > 0) out += e;
            i++;
          }
          out += ' ';
          continue;
        }
        if (kind === 'cs' && d === '{') i++;
        i++;
      }
      out += '" ';
      i++;
      continue;
    }
    if (c === '"' || c === "'" || (kind === 'cs' && c === '@' && next === '"')) {
      const verbatim = c === '@';
      const quote = verbatim ? '"' : c;
      i += verbatim ? 2 : 1;
      while (i < source.length) {
        if (!verbatim && source[i] === '\\') {
          i += 2;
          continue;
        }
        if (source[i] === quote) {
          if (verbatim && source[i + 1] === '"') {
            i += 2;
            continue;
          }
          break;
        }
        i++;
      }
      out += ' "" ';
      i++;
      continue;
    }
    if (c === '(') depth++;
    if (c === ')') {
      depth--;
      if (depth === 0) break;
    }
    out += c;
    i++;
  }
  return out.replace(/\s+/g, ' ').trim();
}

function files(directory: string, include: (path: string) => boolean): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return entry.name === 'node_modules' ? [] : files(path, include);
    return include(path) ? [path] : [];
  });
}

/** Every log call that names something private, in the code that ships. */
function flagged(): { calls: number; hits: Call[] } {
  let calls = 0;
  const hits: Call[] = [];
  for (const source of SOURCES) {
    for (const root of source.roots) {
      for (const path of files(join(ROOT, root), source.include)) {
        const text = readFileSync(path, 'utf8');
        for (const match of text.matchAll(source.call)) {
          const before = text.slice(Math.max(0, match.index - 40), match.index);
          // A method's declaration, not a call.
          if (source.kind === 'cs' && /\b(void|string|static|bool)\s+$/.test(before)) continue;
          calls++;
          const args = argumentsAt(text, match.index + match[0].length - 1, source.kind);
          if (!PRIVATE.test(args)) continue;
          hits.push({
            file: relative(ROOT, path),
            line: text.slice(0, match.index).split('\n').length,
            args,
          });
        }
      }
    }
  }
  return { calls, hits };
}

describe('log calls', () => {
  test('the scan blanks literals and keeps what a call names', () => {
    assert.equal(argumentsAt("log.info({ id }, 'the token was not sent')", 8, 'ts'), '{ id }, ""');
    assert.match(
      argumentsAt('log.info({ instruction: payload.instruction }, "x")', 8, 'ts'),
      /instruction/,
    );
    assert.equal(
      argumentsAt(`x(\`sent ${'$'}{count} of ${'$'}{total}\`)`, 1, 'ts'),
      '" count total "',
    );
    assert.equal(
      argumentsAt('Log($"moved {bytes} bytes" + "the token")', 3, 'cs'),
      '" bytes " + ""',
    );
    assert.equal(argumentsAt('Log("a (b" + /* the token */ c)', 3, 'cs'), '"" + c');
    assert.equal(PRIVATE.test(argumentsAt('Log(draft.Text)', 3, 'cs')), true);
  });

  test('every call that names something private was reviewed, and every review still applies', () => {
    const { calls, hits } = flagged();
    assert.ok(calls > 100, `only ${calls} log calls were found; the scan has lost its way`);
    const unreviewed = hits.filter(
      (hit) => !REVIEWED.some((entry) => entry.file === hit.file && entry.args === hit.args),
    );
    assert.deepEqual(
      unreviewed.map((hit) => `${hit.file}:${hit.line} ${hit.args}`),
      [],
      'review these, then list them in REVIEWED with why they are safe',
    );
    const stale = REVIEWED.filter(
      (entry) => !hits.some((hit) => hit.file === entry.file && hit.args === entry.args),
    );
    assert.deepEqual(
      stale.map((entry) => `${entry.file} ${entry.args}`),
      [],
      'remove reviews of calls that changed or went',
    );
  });
});
