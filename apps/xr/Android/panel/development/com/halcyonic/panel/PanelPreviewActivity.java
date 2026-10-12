package com.halcyonic.panel;

/** Internal development surface: a new window that has never held private work or credentials. */
public final class PanelPreviewActivity extends AgentPanelActivity {
    @Override protected boolean syntheticSurface() { return true; }
}
