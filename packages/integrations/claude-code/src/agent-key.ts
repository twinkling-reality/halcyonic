import type { AgentKey } from '@halcyonic/runtime-core';

/**
 * This agent app runs on the person's own key (AgentKey, R2): the host keeps it in this file and hands it to the app's
 * processes as this variable; the Mac's page draws a field with these words.
 */
export const CLAUDE_AGENT_KEY: AgentKey = {
  runtimeId: 'claude-agent',
  app: 'Claude Agent',
  variable: 'ANTHROPIC_API_KEY',
  file: 'anthropic-api-key',
  field: 'Anthropic API Key',
};
