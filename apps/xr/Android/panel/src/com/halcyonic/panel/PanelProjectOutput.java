package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/** Headset-local presentation links. They never identify a build, runtime or verified software output. */
final class PanelProjectOutput {
    static final int MAXIMUM_BYTES = 65536, MAXIMUM_LINKS = 128, MAXIMUM_APPS = 256;
    static final class Scope {
        final String connection, journal, project;
        Scope(String connection, String journal, String project) {
            PanelContinuation.matches(connection, "[0-9a-f]{64}");
            PanelContinuation.matches(journal, PanelIdentity.ENTITY);
            PanelContinuation.matches(project, PanelIdentity.ENTITY);
            this.connection = connection; this.journal = journal; this.project = project;
        }
        boolean same(Scope other) { return other != null && connection.equals(other.connection) && journal.equals(other.journal) && project.equals(other.project); }
        boolean matches(PanelUiState.Context context) {
            return context != null && connection.equals(context.connection) && journal.equals(context.journal) && project.equals(context.project);
        }
    }
    static final class Component {
        final String packageName, activity;
        Component(String packageName, String activity) {
            this.packageName = identifier(packageName); this.activity = identifier(activity);
        }
        boolean same(Component other) { return other != null && packageName.equals(other.packageName) && activity.equals(other.activity); }
        private static String identifier(String value) {
            if (value == null || value.length() > 512 || value.isEmpty()) throw PanelJson.invalid();
            boolean first = true;
            for (int at = 0; at < value.length();) {
                int code = value.codePointAt(at); at += Character.charCount(code);
                if (code == '.') { if (first) throw PanelJson.invalid(); first = true; continue; }
                if (first ? !Character.isJavaIdentifierStart(code) : !Character.isJavaIdentifierPart(code)) throw PanelJson.invalid();
                if (Character.isIdentifierIgnorable(code)) throw PanelJson.invalid();
                first = false;
            }
            if (first) throw PanelJson.invalid();
            return value;
        }
    }
    static final class App {
        final Component component;
        final String label;
        App(Component component, String label) { this.component = component; this.label = label; }
    }
    static final class Link {
        final Scope scope;
        final Component component;
        Link(Scope scope, Component component) { this.scope = scope; this.component = component; }
        Map<String,Object> value() { return PanelJson.map("connection_key", scope.connection, "journal_id", scope.journal, "project_id", scope.project,
            "package", component.packageName, "activity", component.activity); }
    }
    private final List<Link> links;
    PanelProjectOutput() { links = Collections.emptyList(); }
    private PanelProjectOutput(List<Link> links) { this.links = Collections.unmodifiableList(new ArrayList<>(links)); }
    static PanelProjectOutput parse(String json) {
        if (json == null || json.getBytes(StandardCharsets.UTF_8).length > MAXIMUM_BYTES) throw PanelJson.invalid();
        Map<String,Object> value = PanelJson.object(PanelJson.parse(json));
        PanelJson.keys(value, "version", "links"); PanelJson.integer(value.get("version"), 1, 1);
        List<Object> values = PanelJson.array(value.get("links")); if (values.size() > MAXIMUM_LINKS) throw PanelJson.invalid();
        List<Link> links = new ArrayList<>();
        for (Object item : values) {
            Map<String,Object> entry = PanelJson.object(item);
            PanelJson.keys(entry, "connection_key", "journal_id", "project_id", "package", "activity");
            Scope scope = new Scope(PanelJson.text(entry.get("connection_key")), PanelJson.text(entry.get("journal_id")), PanelJson.text(entry.get("project_id")));
            if (links.stream().anyMatch(link -> link.scope.same(scope))) throw PanelJson.invalid();
            links.add(new Link(scope, new Component(PanelJson.text(entry.get("package")), PanelJson.text(entry.get("activity")))));
        }
        return new PanelProjectOutput(links);
    }
    Link find(Scope scope) { return links.stream().filter(link -> link.scope.same(scope)).findFirst().orElse(null); }
    /** Produces a bounded candidate; a failed write never changes the caller's previous store. */
    PanelProjectOutput change(Scope scope, Component expected, Component replacement) {
        Link previous = find(scope);
        if (!same(previous == null ? null : previous.component, expected)) throw PanelJson.invalid();
        List<Link> next = new ArrayList<>();
        for (Link link : links) if (!link.scope.same(scope)) next.add(link);
        if (replacement != null) next.add(new Link(scope, replacement));
        if (next.size() > MAXIMUM_LINKS) throw PanelJson.invalid();
        return parse(new PanelProjectOutput(next).json());
    }
    String json() {
        List<Object> values = new ArrayList<>(); for (Link link : links) values.add(link.value());
        String json = PanelJson.encode(PanelJson.map("version", 1L, "links", values));
        if (json.getBytes(StandardCharsets.UTF_8).length > MAXIMUM_BYTES) throw PanelJson.invalid();
        return json;
    }
    static boolean same(Component a, Component b) { return a == null ? b == null : a.same(b); }

    static boolean selected(PanelUiState.Context scope, PanelIdentity target, PanelModel model) {
        if (scope == null || target == null || model == null || !scope.connection.equals(target.key)
            || !scope.journal.equals(model.journal) || !model.projects.containsKey(scope.project)) return false;
        if (scope.work == null) return scope.execution == null;
        Map<String,Object> work = model.workstreams.get(scope.work);
        if (work == null || !scope.project.equals(work.get("project_id"))) return false;
        Map<String,Object> run = model.execution(scope.work);
        return run == null ? scope.execution == null : Objects.equals(scope.execution, run.get("execution_id"));
    }

    /** The Activity consumes this ordering; a synthetic launcher can prove failed saves never leave it. */
    interface OpenOperations {
        boolean current() throws Exception;
        boolean available() throws Exception;
        boolean preserveDrafts() throws Exception;
        void start() throws Exception;
    }
    enum OpenResult { REQUESTED, CHANGED, UNAVAILABLE, UNSAVED, FAILED }
    static OpenResult open(OpenOperations io) {
        try {
            if (!io.current()) return OpenResult.CHANGED;
            if (!io.available()) return OpenResult.UNAVAILABLE;
            if (!io.current()) return OpenResult.CHANGED;
            if (!io.preserveDrafts()) return OpenResult.UNSAVED;
            if (!io.current()) return OpenResult.CHANGED;
            io.start();
            return OpenResult.REQUESTED;
        } catch (Exception failure) { return OpenResult.FAILED; }
    }

    /** An in-flight choice belongs to its original full selection, even though a saved link is project-wide. */
    static final class Lease {
        private final PanelUiState.Review ticket;
        final Scope scope;
        final Component previous;
        final int generation;
        private final Object editor;
        private final String words;
        private boolean revoked;
        Lease(PanelUiState authority, long owner, PanelUiState.Context context, Scope scope, Component previous,
              int generation, Object editor, String words) {
            if (!scope.matches(context)) throw PanelJson.invalid();
            ticket = authority.review(owner, context); this.scope = scope; this.previous = previous;
            this.generation = generation; this.editor = editor; this.words = words;
        }
        void revoke() { revoked = true; }
        boolean current(PanelUiState authority, PanelUiState.Context context, Scope scope, Component stored,
                        int generation, Object editor, String words, boolean eligible) {
            boolean valid = !revoked && eligible && authority.current(ticket, context) && this.scope.same(scope)
                && this.scope.matches(context) && same(previous, stored) && this.generation == generation
                && this.editor == editor && Objects.equals(this.words, words);
            if (!valid) revoke();
            return valid;
        }
    }
}
