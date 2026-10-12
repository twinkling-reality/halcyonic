package com.halcyonic.panel;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Date;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TimeZone;
import java.util.UUID;

/** Snapshot projection and capability checks, read directly from the public control-plane contract. */
final class PanelModel {
    final String journal;
    final long position;
    final Map<String, Map<String, Object>> projects, workstreams, executions, runtimes;
    final List<Object> commands;

    PanelModel(String json) {
        Map<String, Object> v = PanelJson.object(PanelJson.parse(json));
        Map<String, Object> j = PanelJson.object(v.get("journal"));
        journal = PanelJson.text(j.get("journal_id")); PanelContinuation.matches(journal, PanelIdentity.ENTITY);
        if (!Arrays.asList("live", "fixture").contains(PanelJson.text(j.get("origin")))) throw PanelJson.invalid();
        position = PanelJson.integer(v.get("position"), 0, Long.MAX_VALUE);
        projects = index(v.get("projects"), "project_id", PanelIdentity.ENTITY);
        workstreams = index(v.get("workstreams"), "workstream_id", PanelIdentity.ENTITY);
        executions = index(v.get("executions"), "execution_id", PanelIdentity.ENTITY);
        runtimes = index(v.get("runtimes"), "runtime_id", "[a-z][a-z0-9-]{0,62}");
        commands = PanelJson.array(v.get("commands"));
    }
    private static Map<String, Map<String, Object>> index(Object array, String key, String pattern) {
        Map<String, Map<String, Object>> result = new LinkedHashMap<>();
        for (Object item : PanelJson.array(array)) {
            Map<String, Object> value = PanelJson.object(item);
            String id = PanelJson.text(value.get(key)); PanelContinuation.matches(id, pattern);
            if (result.put(id, value) != null) throw PanelJson.invalid();
        }
        return result;
    }
    Map<String, Object> execution(String workId) {
        Map<String, Object> work = workstreams.get(workId);
        if (work == null || work.get("current_execution_id") == null) return null;
        Map<String, Object> run = executions.get(PanelJson.text(work.get("current_execution_id")));
        if (run == null || !workId.equals(run.get("workstream_id")) || !work.get("project_id").equals(run.get("project_id"))) throw PanelJson.invalid();
        return run;
    }
    String context(String workId) {
        Map<String, Object> run = execution(workId);
        if (run == null) return "none";
        Map<String, Object> descriptor = runtimes.get(PanelJson.text(PanelJson.object(run.get("runtime")).get("runtime_id")));
        return requestFingerprint(PanelJson.map("execution_id", run.get("execution_id"), "status", run.get("status"), "native_id", run.get("native_id"),
            "runtime", run.get("runtime"), "capabilities", descriptor == null ? null : descriptor.get("capabilities"), "approvals", run.get("pending_approvals"), "questions", run.get("pending_questions")));
    }
    boolean capability(Map<String, Object> run, String name) {
        if (run == null || run.get("native_id") == null) return false;
        Map<String, Object> descriptor = runtimes.get(PanelJson.text(PanelJson.object(run.get("runtime")).get("runtime_id")));
        return descriptor != null && PanelJson.bool(PanelJson.object(descriptor.get("capabilities")).get(name));
    }
    static boolean running(Map<String, Object> run) { return run != null && Arrays.asList("running", "verifying", "waiting_for_human").contains(PanelJson.text(run.get("status"))); }
    boolean instruct(Map<String, Object> run) {
        if (run == null || !PanelJson.array(run.get("pending_approvals")).isEmpty() || !PanelJson.array(run.get("pending_questions")).isEmpty()) return false;
        return running(run) ? capability(run, "instruct_while_running")
            : Arrays.asList("completed", "failed", "interrupted").contains(PanelJson.text(run.get("status"))) && capability(run, "instruct_at_rest");
    }
    boolean interrupt(Map<String, Object> run) { return running(run) && capability(run, "interrupt"); }

    static String requestFingerprint(Map<String, Object> request) { return PanelIdentity.hash(PanelJson.encode(request).getBytes(java.nio.charset.StandardCharsets.UTF_8)); }
    boolean decision(Map<String, Object> run, Map<String, Object> expected, String decision) {
        if (!running(run) || !capability(run, "respond_to_approval") || !(decision.equals("approve") || decision.equals("deny"))) return false;
        String id = PanelJson.text(expected.get("approval_id"));
        for (Object item : PanelJson.array(run.get("pending_approvals"))) {
            Map<String, Object> request = PanelJson.object(item);
            if (id.equals(request.get("approval_id")) && requestFingerprint(request).equals(requestFingerprint(expected)))
                return decision.equals("deny") || PanelJson.bool(request.get("approvable"));
        }
        return false;
    }
    static boolean questionSupported(Map<String, Object> question) {
        if (!PanelJson.bool(question.get("answerable"))) return false;
        List<Object> prompts = PanelJson.array(question.get("prompts"));
        if (prompts.isEmpty() || prompts.size() > 10) return false;
        Set<String> keys = new HashSet<>();
        for (Object item : prompts) {
            Map<String, Object> p = PanelJson.object(item);
            if (PanelJson.bool(p.get("secret")) || !keys.add(PanelJson.text(p.get("key")))) return false;
            List<Object> options = PanelJson.array(p.get("options"));
            if (options.size() > 20 || (options.isEmpty() && !PanelJson.bool(p.get("free_text")))) return false;
            Set<String> labels = new HashSet<>();
            for (Object option : options) if (!labels.add(PanelText.plain(PanelJson.text(PanelJson.object(option).get("label"))))) return false;
        }
        return true;
    }
    boolean answer(Map<String, Object> run, Map<String, Object> expected, List<Object> answers) {
        if (!running(run) || !capability(run, "answer_question") || !questionSupported(expected)) return false;
        boolean current = false;
        for (Object item : PanelJson.array(run.get("pending_questions"))) {
            Map<String, Object> question = PanelJson.object(item);
            if (expected.get("question_id").equals(question.get("question_id")) && requestFingerprint(question).equals(requestFingerprint(expected))) current = true;
        }
        List<Object> prompts = PanelJson.array(expected.get("prompts"));
        if (!current || answers.size() != prompts.size()) return false;
        for (int i = 0; i < prompts.size(); i++) {
            Map<String, Object> p = PanelJson.object(prompts.get(i)), a = PanelJson.object(answers.get(i));
            if (!p.get("key").equals(a.get("key"))) return false;
            List<Object> selected = PanelJson.array(a.get("selected"));
            if ((!PanelJson.bool(p.get("multiple")) && selected.size() > 1) || new HashSet<>(selected).size() != selected.size()) return false;
            Set<String> offered = new HashSet<>();
            for (Object option : PanelJson.array(p.get("options"))) offered.add(PanelJson.text(PanelJson.object(option).get("label")));
            for (Object label : selected) if (!offered.contains(PanelJson.text(label))) return false;
            String text = PanelJson.nullableText(a.get("text"));
            if (text != null && (!PanelJson.bool(p.get("free_text")) || text.length() > 4000 || text.trim().isEmpty())) return false;
            if (selected.isEmpty() && text == null) return false;
        }
        return true;
    }

    static Map<String, Object> command(String type, Map<String, Object> payload) {
        SimpleDateFormat time = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT); time.setTimeZone(TimeZone.getTimeZone("UTC"));
        return PanelJson.map("schema_version", 1L, "command_id", UUID.randomUUID().toString(), "issued_at", time.format(new Date()),
            "client", PanelJson.map("name", "halcyonic-native-panel", "version", "1", "device_label", null), "command_type", type, "payload", payload);
    }
}
