package com.halcyonic.panel;

import android.app.Activity;
import android.content.pm.ApplicationInfo;
import android.util.Log;

/**
 * Whether the native panel's window may be captured by the headset's own recording (ADR 0029 amendment, 2026-11-16).
 * The real panel is secure (FLAG_SECURE) in every build. Only when three things hold may it be captured: the build is
 * debuggable, it is a development build (its development-only class is packaged, a build-time gate a release APK never
 * has), and the immersive app, in this process, allowed it because the person launched it with the capture extra.
 * Nothing the panel's own intent carries changes this, and the answer dies with the process.
 */
public final class PanelCapture {
    private static volatile boolean allowedByApp;

    private PanelCapture() {}

    /** Called in-process by the immersive app when the person's launch carried the capture extra. A no-op in a release build. */
    public static boolean allow(Activity activity) {
        if (!eligible(activity)) return false;
        allowedByApp = true;
        Log.i("Halcyonic", "panel capture: allowed by the launch extra");
        return true;
    }

    /** Whether a panel created now may leave its window capturable. */
    public static boolean allowed(Activity activity) {
        return allowedByApp && eligible(activity);
    }

    private static boolean eligible(Activity activity) {
        if (activity == null || (activity.getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) == 0) return false;
        try {
            Class.forName("com.halcyonic.panel.PanelCaptureDevelopment");
            return true;
        } catch (ClassNotFoundException releaseBuild) {
            return false;
        }
    }
}
