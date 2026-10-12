package com.halcyonic.panel;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Looper;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

/** JNI entry points. Extras never contain credentials, selections, drafts or agent text. */
public final class PanelBridge {
    private PanelBridge() {}

    /** The result is the actual startActivity outcome, not a queued promise of a launch. */
    public static String open(Activity activity, String json, boolean inHome) {
        if (activity == null) return "Workspace is not available.";
        try { PanelContinuation.parse(json); } catch (Exception error) { return "Workspace context could not be saved."; }
        AtomicReference<String> result = new AtomicReference<>("The panel did not open.");
        AtomicBoolean expired = new AtomicBoolean();
        CountDownLatch completed = new CountDownLatch(1);
        Runnable launch = () -> {
            try {
                if (expired.get()) return;
                PanelFiles files = new PanelFiles(activity);
                files.remove(PanelFiles.RETURN);
                files.write(PanelFiles.STATE, json);
                if (expired.get()) return;
                Intent panel = new Intent(activity, AgentPanelActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                if (inHome) {
                    PendingIntent pending = PendingIntent.getActivity(activity, 48169, panel, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
                    Intent home = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    home.putExtra("extra_launch_in_home_pending_intent", pending);
                    activity.startActivity(home);
                } else activity.startActivity(panel);
                result.set("");
            } catch (Exception error) { result.set("The panel could not open. Your work is still in Workspace."); }
            finally { completed.countDown(); }
        };
        if (Looper.myLooper() == Looper.getMainLooper()) launch.run();
        else {
            activity.runOnUiThread(launch);
            try { if (!completed.await(3, TimeUnit.SECONDS)) { expired.set(true); return "The panel launch timed out. Check whether it opened."; } }
            catch (InterruptedException error) { expired.set(true); Thread.currentThread().interrupt(); return "The panel launch was interrupted."; }
        }
        return result.get();
    }

    public static String takePendingReturn(Activity activity) {
        return activity == null ? null : new PanelFiles(activity).takeReturn();
    }
    public static String peekPendingReturn(Activity activity) {
        return activity == null ? null : new PanelFiles(activity).peekReturn();
    }
    public static boolean acknowledgePendingReturn(Activity activity, String id) {
        return activity != null && id != null && id.matches(PanelIdentity.UUID) && new PanelFiles(activity).acknowledgeReturn(id);
    }
}
