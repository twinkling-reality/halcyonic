package com.halcyonic.panel;

/** Actual Activity/Dialog authority state, independent of Android rendering for ordered race tests. */
final class PanelUiState {
    static final class Context {
        final String connection, journal, project, work, execution;
        Context(String connection, String journal, String project, String work, String execution) {
            this.connection = connection; this.journal = journal; this.project = project; this.work = work; this.execution = execution;
        }
        boolean same(Context other) {
            return other != null && equal(connection, other.connection) && equal(journal, other.journal) && equal(project, other.project)
                && equal(work, other.work) && equal(execution, other.execution);
        }
        private static boolean equal(String a, String b) { return a == null ? b == null : a.equals(b); }
    }
    static final class Review {
        final long owner, epoch; final Context context;
        Review(long owner, long epoch, Context context) { this.owner = owner; this.epoch = epoch; this.context = context; }
    }
    private boolean resumed, top, activityFocus, dialogFocus, read, compact, projectionReady = true;
    private long epoch, owner, owners;
    void compact(boolean value) { if (compact == value) return; compact = value; projectionReady = false; expire(); }
    boolean changePresentation(boolean value, boolean persisted) { if (!persisted || compact == value) return false; compact(value); return true; }
    boolean navigated(Runnable freshRead) {
        awaitingProjection();
        if (!readProjection()) return false;
        freshRead.run();
        return true;
    }
    boolean readProjection() { return resumed && (compact || top); }
    void awaitingProjection() { projectionReady = false; expire(); }
    void projected() { projectionReady = true; }
    boolean compact() { return compact; }
    boolean controlsReady() { return !compact && projectionReady; }
    boolean monitor() { return resumed && compact; }
    boolean navigate() { return resumed && top && owner == 0 && activityFocus; }
    boolean commandsAllowed() { return controlsReady() && resumed && top && (owner == 0 ? activityFocus : dialogFocus); }
    void resumed(boolean value) { resumed = value; expire(); }
    void top(boolean value) { top = value; if (!value) expire(); }
    void activityFocus(boolean value) {
        activityFocus = value;
        // Creating our own dialog transfers focus; its window reports whether it owns focus.
        if (!value && owner == 0) expire();
    }
    void dialogFocus(long id, boolean value) { if (id != owner) return; boolean lost = dialogFocus && !value; dialogFocus = value; if (lost) expire(); }
    long open() { expire(); owner = ++owners; dialogFocus = false; return owner; }
    void close(long id) { if (owner == id) { owner = 0; dialogFocus = false; expire(); } }
    private void expire() { epoch++; read = false; }
    boolean interact(long id) { return controlsReady() && resumed && top && (id == 0 ? owner == 0 && activityFocus : owner == id && dialogFocus); }
    boolean interact(long id, boolean busy, boolean escape) { return interact(id) && (!busy || escape); }
    Review review(long id, Context context) { return new Review(id, epoch, context); }
    boolean read(Review ticket) { if (!valid(ticket)) return false; read = true; return true; }
    boolean authorize(Review ticket, Context current) { return read && valid(ticket) && (ticket.context == null ? current == null : ticket.context.same(current)); }
    boolean current(Review ticket, Context current) { return valid(ticket) && ticket.context != null && ticket.context.same(current); }
    private boolean valid(Review ticket) { return ticket != null && ticket.owner == owner && ticket.epoch == epoch && interact(ticket.owner); }
}
