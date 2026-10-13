package com.halcyonic.panel;

import android.app.Activity;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/** User-requested launchers only. No all-package inventory, app data, custom extras or automatic launch. */
final class PanelAppLauncher {
    static final class Catalog {
        final List<PanelProjectOutput.App> apps;
        final boolean truncated;
        Catalog(List<PanelProjectOutput.App> apps, boolean truncated) { this.apps = apps; this.truncated = truncated; }
        PanelProjectOutput.App find(PanelProjectOutput.Component component) {
            for (PanelProjectOutput.App app : apps) if (app.component.same(component)) return app;
            return null;
        }
    }
    static Catalog list(Activity activity) {
        PackageManager manager = activity.getPackageManager();
        Intent query = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        List<PanelProjectOutput.App> apps = new ArrayList<>(); boolean truncated = false;
        for (ResolveInfo result : manager.queryIntentActivities(query, 0)) {
            ActivityInfo info = result.activityInfo;
            if (!available(activity, info)) continue;
            PanelProjectOutput.Component component;
            try { component = new PanelProjectOutput.Component(info.packageName, info.name); }
            catch (IllegalArgumentException invalid) { continue; }
            if (apps.stream().anyMatch(app -> app.component.same(component))) continue;
            if (apps.size() == PanelProjectOutput.MAXIMUM_APPS) { truncated = true; continue; }
            CharSequence name = result.loadLabel(manager);
            String label = name == null ? info.packageName : name.toString();
            if (label.codePointCount(0, label.length()) > 160) label = label.substring(0, label.offsetByCodePoints(0, 160)) + "…";
            apps.add(new PanelProjectOutput.App(component, PanelText.plain(label)));
        }
        apps.sort(Comparator.comparing((PanelProjectOutput.App app) -> app.label)
            .thenComparing(app -> app.component.packageName).thenComparing(app -> app.component.activity));
        return new Catalog(apps, truncated);
    }
    /** Re-query only this saved package; never substitute its new default or another component. */
    static PanelProjectOutput.App current(Activity activity, PanelProjectOutput.Component component) {
        PackageManager manager = activity.getPackageManager();
        Intent query = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).setPackage(component.packageName);
        for (ResolveInfo result : manager.queryIntentActivities(query, 0)) {
            ActivityInfo info = result.activityInfo;
            if (available(activity, info) && component.packageName.equals(info.packageName) && component.activity.equals(info.name)) {
                CharSequence label = result.loadLabel(manager);
                String name = label == null ? info.packageName : label.toString();
                if (name.codePointCount(0, name.length()) > 160) name = name.substring(0, name.offsetByCodePoints(0, 160)) + "…";
                return new PanelProjectOutput.App(component, PanelText.plain(name));
            }
        }
        return null;
    }
    private static boolean available(Activity activity, ActivityInfo info) {
        return info != null && info.applicationInfo != null && info.enabled && info.applicationInfo.enabled && info.exported
            && !activity.getPackageName().equals(info.packageName)
            && (info.permission == null || activity.checkSelfPermission(info.permission) == PackageManager.PERMISSION_GRANTED);
    }
    static Intent intent(PanelProjectOutput.Component component) {
        return Intent.makeMainActivity(new ComponentName(component.packageName, component.activity)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
    }
    /** Called only after the Activity revalidates its lease and preserves its exact local drafts. */
    static void open(Activity activity, PanelProjectOutput.Component component) { activity.startActivity(intent(component)); }
}
