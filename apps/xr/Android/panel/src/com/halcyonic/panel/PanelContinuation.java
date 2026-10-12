package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Map;
import java.util.UUID;

/** Consumer of contracts/workspace.ts. No confirmation, answer or reading authority is stored. */
final class PanelContinuation {
    static final int MAXIMUM_BYTES = 256 * 1024;
    final Map<String, Object> value;
    PanelContinuation(Map<String, Object> value) { this.value = value; }

    static PanelContinuation parse(String json) {
        if (json == null || json.length() > MAXIMUM_BYTES || json.getBytes(StandardCharsets.UTF_8).length > MAXIMUM_BYTES) throw PanelJson.invalid();
        Map<String, Object> v = PanelJson.object(PanelJson.parse(json));
        if (!v.containsKey("request")) v.put("request", null); // Only the legacy v1 omission is migrated.
        if (!v.containsKey("surface")) v.put("surface", "task");
        PanelJson.keys(v, "version", "handoff_id", "connection_key", "journal_id", "project_id", "workstream_id", "execution_id", "surface", "section", "page", "instruction", "request");
        if (!java.util.Arrays.asList("task", "request").contains(PanelJson.text(v.get("surface")))) throw PanelJson.invalid();
        PanelJson.integer(v.get("version"), 1, 1);
        matches(v.get("handoff_id"), PanelIdentity.UUID);
        matches(v.get("connection_key"), "[0-9a-f]{64}");
        for (String name : new String[] {"journal_id", "project_id", "workstream_id", "execution_id"}) if (v.get(name) != null) matches(v.get(name), PanelIdentity.ENTITY);
        if (!Arrays.asList("waiting", "activity", "changes", "checks").contains(PanelJson.text(v.get("section")))) throw PanelJson.invalid();
        PanelJson.integer(v.get("page"), 0, 100000);
        if (v.get("instruction") != null) {
            Map<String, Object> draft = PanelJson.object(v.get("instruction"));
            PanelJson.keys(draft, "text", "spoken", "part");
            if (PanelJson.text(draft.get("text")).codePointCount(0, PanelJson.text(draft.get("text")).length()) > 32000) throw PanelJson.invalid();
            PanelJson.bool(draft.get("spoken")); PanelJson.integer(draft.get("part"), 0, 100000);
        }
        if (v.get("request") != null) {
            Map<String, Object> request = PanelJson.object(v.get("request"));
            if (!request.containsKey("pending_command_id")) request.put("pending_command_id", null);
            PanelJson.keys(request, "request_id", "pending_command_id", "text", "spoken", "part");
            if (request.get("request_id") != null) matches(request.get("request_id"), PanelIdentity.ENTITY);
            if (request.get("pending_command_id") != null) matches(request.get("pending_command_id"), PanelIdentity.UUID);
            String text = PanelJson.text(request.get("text"));
            if (text.codePointCount(0, text.length()) > 32000) throw PanelJson.invalid();
            PanelJson.bool(request.get("spoken")); PanelJson.integer(request.get("part"), 0, 100000);
        }
        return new PanelContinuation(v);
    }
    static void matches(Object value, String pattern) { if (!PanelJson.text(value).matches(pattern)) throw PanelJson.invalid(); }
    String string(String key) { return PanelJson.nullableText(value.get(key)); }
    String json() { return PanelJson.encode(value); }
    String draft() { return value.get("instruction") == null ? "" : PanelJson.text(PanelJson.object(value.get("instruction")).get("text")); }
    boolean spoken() { return value.get("instruction") != null && PanelJson.bool(PanelJson.object(value.get("instruction")).get("spoken")); }

    boolean matches(PanelIdentity identity, PanelModel snapshot) {
        if (!string("connection_key").equals(identity.key) || !snapshot.journal.equals(string("journal_id"))) return false;
        String project = string("project_id"), work = string("workstream_id"), execution = string("execution_id");
        if ("request".equals(string("surface")) && execution == null) return false;
        if (project == null) return work == null && execution == null && value.get("instruction") == null && value.get("request") == null;
        if (!snapshot.projects.containsKey(project)) return false;
        if (work == null) return execution == null && value.get("instruction") == null && value.get("request") == null;
        Map<String, Object> task = snapshot.workstreams.get(work);
        if (task == null || !project.equals(task.get("project_id"))) return false;
        if (execution == null) return value.get("instruction") == null && value.get("request") == null;
        Map<String, Object> run = snapshot.executions.get(execution);
        return run != null && execution.equals(task.get("current_execution_id")) && project.equals(run.get("project_id")) && work.equals(run.get("workstream_id"));
    }

    boolean hasContent() {
        if (!draft().isEmpty()) return true;
        if (value.get("request") == null) return false;
        Map<String, Object> request = PanelJson.object(value.get("request"));
        return request.get("request_id") != null || request.get("pending_command_id") != null || !PanelJson.text(request.get("text")).isEmpty();
    }
    Object requestFor(String key, String journal, String project, String work, String execution) {
        return java.util.Objects.equals(key, string("connection_key")) && java.util.Objects.equals(journal, string("journal_id")) &&
            java.util.Objects.equals(project, string("project_id")) && java.util.Objects.equals(work, string("workstream_id")) &&
            java.util.Objects.equals(execution, string("execution_id")) ? value.get("request") : null;
    }
    static PanelContinuation selection(PanelIdentity identity, PanelModel snapshot, String project, String work, String execution, String draft, boolean spoken, PanelContinuation previous) {
        Map<String, Object> v = PanelJson.map("version", 1L, "handoff_id", UUID.randomUUID().toString(), "connection_key", identity.key,
            "journal_id", snapshot.journal, "project_id", project, "workstream_id", work, "execution_id", execution,
            "section", previous == null ? "activity" : previous.value.get("section"), "page", previous == null ? 0L : previous.value.get("page"),
            "request", previous == null || execution == null ? null : previous.requestFor(identity.key, snapshot.journal, project, work, execution),
            "instruction", execution == null ? null : PanelJson.map("text", draft, "spoken", spoken, "part", previous != null && execution.equals(previous.string("execution_id")) && draft.equals(previous.draft()) && previous.value.get("instruction") != null ? PanelJson.object(previous.value.get("instruction")).get("part") : 0L));
        PanelContinuation result = parse(PanelJson.encode(v));
        if (!result.matches(identity, snapshot)) throw PanelJson.invalid();
        return result;
    }
}
