package com.halcyonic.panel;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/** Ordinary local editor storage, apart from selected handoff. No implicit eviction or retargeting. */
final class PanelDrafts {
    static final int MAXIMUM_BYTES = 4 * 1024 * 1024;
    private final List<PanelContinuation> entries = new ArrayList<>();
    static PanelDrafts parse(String json) {
        Map<String, Object> value = PanelJson.object(PanelJson.parse(json));
        PanelJson.keys(value, "version", "drafts"); PanelJson.integer(value.get("version"), 1, 1);
        List<Object> records = PanelJson.array(value.get("drafts"));
        if (json.getBytes(java.nio.charset.StandardCharsets.UTF_8).length > MAXIMUM_BYTES) throw PanelJson.invalid();
        PanelDrafts result = new PanelDrafts();
        for (Object item : records) {
            PanelContinuation draft = PanelContinuation.parse(PanelJson.encode(item));
            if ((draft.value.get("instruction") == null && draft.value.get("request") == null) || draft.string("journal_id") == null || draft.string("workstream_id") == null || draft.string("execution_id") == null) throw PanelJson.invalid();
            if (result.find(draft.string("connection_key"), draft.string("journal_id"), draft.string("workstream_id"), draft.string("execution_id")) != null) throw PanelJson.invalid();
            result.entries.add(draft);
        }
        return result;
    }
    PanelContinuation find(String key, String journal, String work, String execution) {
        for (PanelContinuation entry : entries) if (key.equals(entry.string("connection_key")) && journal.equals(entry.string("journal_id")) && work.equals(entry.string("workstream_id")) && execution.equals(entry.string("execution_id"))) return entry;
        return null;
    }
    List<PanelContinuation> earlier(String key, String journal, String work, String execution) {
        List<PanelContinuation> result = new ArrayList<>();
        for (PanelContinuation entry : entries) if (key.equals(entry.string("connection_key")) && journal.equals(entry.string("journal_id")) && work.equals(entry.string("workstream_id")) && !execution.equals(entry.string("execution_id"))) result.add(entry);
        return result;
    }
    void put(PanelContinuation draft) {
        if (draft.value.get("instruction") == null && draft.value.get("request") == null) return;
        PanelContinuation existing = find(draft.string("connection_key"), draft.string("journal_id"), draft.string("workstream_id"), draft.string("execution_id"));
        if (!draft.hasContent()) { entries.remove(existing); return; }
        List<PanelContinuation> previous = new ArrayList<>(entries);
        entries.remove(existing); entries.add(draft);
        if (json().getBytes(java.nio.charset.StandardCharsets.UTF_8).length > MAXIMUM_BYTES) {
            entries.clear(); entries.addAll(previous); throw new IllegalStateException("Draft storage is full.");
        }
    }
    void remove(PanelContinuation entry) { entries.remove(entry); }
    String json() {
        List<Object> all = new ArrayList<>(); for (PanelContinuation entry : entries) all.add(entry.value);
        return PanelJson.encode(PanelJson.map("version", 1L, "drafts", all));
    }
}
