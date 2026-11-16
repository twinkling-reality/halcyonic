package com.halcyonic.panel;

/** Internal development surface: the synthetic preview opened compact on its task that waits on an approval (take 10's gate frame). */
public final class PanelPreviewNeedsYouActivity extends AgentPanelActivity {
    @Override protected boolean syntheticSurface() { return true; }
    @Override protected String previewTask() { return "Move sessions to their own table"; }
}
