package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;

/** Consumer of the generated WorkspaceViewStore envelope. Ordinary recovery data only. */
final class PanelViewStore {
    static final int MAXIMUM_BYTES = 4 * 1024 * 1024;
    private PanelViewStore() {}
    static void validate(String json) {
        if (json == null || json.length() > MAXIMUM_BYTES || json.getBytes(StandardCharsets.UTF_8).length > MAXIMUM_BYTES) throw PanelJson.invalid();
        Map<String, Object> value = PanelJson.object(PanelJson.parse(json));
        PanelJson.keys(value, "version", "selected", "views");
        PanelJson.integer(value.get("version"), 1, 1);
        String selected = PanelJson.nullableText(value.get("selected"));
        if (selected != null) PanelContinuation.matches(selected, PanelIdentity.UUID);
        Set<String> ids = new HashSet<>();
        for (Object item : PanelJson.array(value.get("views"))) {
            PanelContinuation view = PanelContinuation.parse(PanelJson.encode(item));
            if (!ids.add(view.string("handoff_id"))) throw PanelJson.invalid();
        }
        if (selected != null && !ids.contains(selected)) throw PanelJson.invalid();
    }
}
