package com.halcyonic.panel;

/** Ordinary context recovery only. It never stores view/input/read/confirmation authority. */
final class PanelRestore {
    private PanelContinuation unsaved, pending;
    void paused(PanelContinuation current, boolean persisted) {
        if (persisted) unsaved = null;
        else if (current != null) unsaved = current;
    }
    void stage(PanelContinuation disk) { pending = unsaved == null ? disk : unsaved; }
    PanelContinuation pending() { return pending; }
    PanelContinuation fresh(PanelIdentity identity, PanelModel model) {
        return pending != null && pending.matches(identity, model) ? pending : null;
    }
    void applied() { pending = null; }
    void persisted() { unsaved = null; }
}
