package com.halcyonic.panel;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.content.pm.ResolveInfo;

/** Executes the real API-compiled adapter against a test-only recording Android surface. */
public final class PanelLauncherSelfTest {
    private static void require(boolean value) { if (!value) throw new AssertionError("Launcher boundary failed."); }
    private static ResolveInfo app(String pkg, String name, String label) {
        ResolveInfo result = new ResolveInfo(); result.activityInfo = new ActivityInfo();
        result.activityInfo.packageName = pkg; result.activityInfo.name = name; result.label = label; return result;
    }
    private static void query(Intent value, String pkg) {
        require(Intent.ACTION_MAIN.equals(value.getAction()) && value.getCategories().size() == 1
            && value.getCategories().contains(Intent.CATEGORY_LAUNCHER) && java.util.Objects.equals(pkg, value.getPackage())
            && value.getComponent() == null && value.getFlags() == 0);
    }
    public static void main(String[] args) {
        Activity activity = new Activity();
        ResolveInfo first = app("example.one", "example.one.Entry", "Same name"), second = app("example.two", "example.two.Entry", "Same name");
        activity.manager.results.add(first); activity.manager.results.add(second); activity.manager.results.add(first);
        activity.manager.results.add(app("com.halcyonic.panel", "com.halcyonic.panel.Entry", "Self"));
        ResolveInfo disabled = app("example.disabled", "example.disabled.Entry", "Disabled"); disabled.activityInfo.enabled = false; activity.manager.results.add(disabled);
        ResolveInfo appDisabled = app("example.appdisabled", "example.appdisabled.Entry", "Disabled app"); appDisabled.activityInfo.applicationInfo.enabled = false; activity.manager.results.add(appDisabled);
        ResolveInfo privateApp = app("example.private", "example.private.Entry", "Private"); privateApp.activityInfo.exported = false; activity.manager.results.add(privateApp);
        ResolveInfo protectedApp = app("example.protected", "example.protected.Entry", "Protected"); protectedApp.activityInfo.permission = "example.permission"; activity.manager.results.add(protectedApp);
        activity.manager.results.add(app("invalid/package", "example.invalid.Entry", "Invalid"));
        ResolveInfo absent = new ResolveInfo(); activity.manager.results.add(absent);
        PanelAppLauncher.Catalog catalog = PanelAppLauncher.list(activity);
        require(catalog.apps.size() == 2 && !catalog.truncated && catalog.apps.get(0).component.packageName.equals("example.one"));
        query(activity.manager.queries.get(0), null);
        PanelProjectOutput.Component chosen = catalog.apps.get(1).component;
        require(catalog.find(chosen) == catalog.apps.get(1));
        require(PanelAppLauncher.current(activity, chosen) != null); query(activity.manager.queries.get(1), "example.two");
        second.activityInfo.name = "example.two.Replacement";
        require(PanelAppLauncher.current(activity, chosen) == null); // No substitution after alias/update change.
        activity.allowedPermission = "example.permission";
        require(PanelAppLauncher.current(activity, new PanelProjectOutput.Component("example.protected", "example.protected.Entry")) != null);
        activity.manager.results.clear();
        activity.manager.results.add(app("example.label", "example.label.Entry", "\u202e".repeat(200)));
        for (int i = 0; i < 300; i++) activity.manager.results.add(app("example.app" + i, "example.app" + i + ".Entry", "App"));
        catalog = PanelAppLauncher.list(activity);
        require(catalog.apps.size() == 256 && catalog.truncated);
        PanelProjectOutput.App label = catalog.find(new PanelProjectOutput.Component("example.label", "example.label.Entry"));
        require(label != null && !label.label.contains("\u202e") && label.label.endsWith("…") && label.label.length() < 1600);
        PanelAppLauncher.open(activity, chosen);
        Intent launch = activity.starts.get(0);
        require(launch.getComponent().getPackageName().equals("example.two") && launch.getComponent().getClassName().equals("example.two.Entry")
            && launch.getPackage() == null && Intent.ACTION_MAIN.equals(launch.getAction()) && launch.getCategories().size() == 1
            && launch.getCategories().contains(Intent.CATEGORY_LAUNCHER) && launch.getFlags() == Intent.FLAG_ACTIVITY_NEW_TASK);
        // The recording Intent exposes only the consumed narrow operations. Adding extras/data/grants
        // to production would fail to link here rather than silently accepting them in this witness.
        activity.refuse = true;
        try { PanelAppLauncher.open(activity, chosen); throw new AssertionError("Refusal ignored."); }
        catch (SecurityException expected) { require(activity.starts.size() == 2); }
        System.out.println("passed");
    }
}
