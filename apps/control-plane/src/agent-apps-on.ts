import { readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The agent apps over ACP the person turned on from the setup page (Kiro, Antigravity): each spends the person's own
 * plan, so it runs only from the person's own press, never from a settings file or for them (ADR 0067). The file lives
 * in the data folder, which no agent can reach, and takes effect at the next start, as a stored key does.
 */
export const AGENT_APPS_ON_FILE = 'agent-apps-on.json';

/** The ids of the apps the person turned on; none where the file is missing or not understood. */
export function agentAppsOn(dataDir: string): readonly string[] {
  let value: unknown;
  try {
    value = JSON.parse(readFileSync(join(dataDir, AGENT_APPS_ON_FILE), 'utf8'));
  } catch {
    return [];
  }
  const on = typeof value === 'object' && value !== null ? Reflect.get(value, 'on') : undefined;
  if (!Array.isArray(on)) return [];
  return [
    ...new Set(
      on.filter((id): id is string => typeof id === 'string' && /^[a-z][a-z0-9-]{0,62}$/.test(id)),
    ),
  ].sort();
}

/** Turns one app on or off, writing the file whole and readable by its owner only. */
export function setAgentAppOn(dataDir: string, id: string, on: boolean): readonly string[] {
  const now = new Set(agentAppsOn(dataDir));
  if (on) now.add(id);
  else now.delete(id);
  const file = join(dataDir, AGENT_APPS_ON_FILE);
  const list = [...now].sort();
  if (list.length === 0) {
    rmSync(file, { force: true });
    return list;
  }
  const draft = `${file}.${process.pid}.tmp`;
  writeFileSync(draft, `${JSON.stringify({ on: list })}\n`, { mode: 0o600 });
  renameSync(draft, file);
  return list;
}
