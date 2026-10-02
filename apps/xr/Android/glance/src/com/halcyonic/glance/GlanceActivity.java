package com.halcyonic.glance;

import android.Manifest;
import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.SystemClock;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * The glance: a small 2D window of Halcyonic's that the person opens from the Library or the
 * Navigator while a game or another immersive app runs, as the OVERLAY_LAUNCHER of a hybrid app. It
 * shows what waits for them and what is working, in the redesign's pocket Tasks style, and offers
 * one thing: Open Halcyonic. It acts on no work; answering and approving stay in Halcyonic, where the
 * whole request is shown. While it lives, visible or minimised, it reads the control plane and, when
 * a task starts waiting, raises a notification without the task's title. A spike for development
 * builds only, to learn on a Quest whether this reaches someone inside a game.
 */
public final class GlanceActivity extends Activity {
    private static final String TAG = "Halcyonic";
    private static final String CHANNEL = "halcyonic-waiting";
    private static final long VISIBLE_MS = 10_000;
    private static final long HIDDEN_MS = 30_000;
    private static final int MOST_ROWS = 8;

    private static final int GROUND = Color.rgb(0x1B, 0x22, 0x2D);
    private static final int RAISED = Color.rgb(0x24, 0x2D, 0x3A);
    private static final int INK = Color.rgb(0xEE, 0xF2, 0xF6);
    private static final int INK_2 = Color.rgb(0xAA, 0xB5, 0xC2);
    private static final int ATTENTION = Color.rgb(0xF7, 0xCF, 0x70);
    private static final int ACCENT = Color.rgb(0xA3, 0xC3, 0xFF);

    private volatile boolean visible;
    private volatile boolean running;
    private volatile boolean polled;
    private Thread poller;
    private GlanceClient client;
    private TextView lead;
    private LinearLayout rows;
    private Set<String> waitingBefore;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        client = new GlanceClient(this);
        setContentView(layout());
        lead.setText("Waiting for your computer.");
        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[] {Manifest.permission.POST_NOTIFICATIONS}, 1);
        }
        NotificationChannel channel = new NotificationChannel(CHANNEL, "Waiting for you", NotificationManager.IMPORTANCE_HIGH);
        getSystemService(NotificationManager.class).createNotificationChannel(channel);
        running = true;
        poller = new Thread(this::pollLoop, "halcyonic-glance");
        poller.start();
        Log.i(TAG, "glance started");
    }

    @Override
    protected void onStart() {
        super.onStart();
        boolean wasHidden = !visible && poller != null && polled;
        visible = true;
        Log.i(TAG, "glance visible");
        // Shown again after being minimised: read now rather than at the hidden pace.
        if (wasHidden) poller.interrupt();
    }

    @Override
    protected void onStop() {
        visible = false;
        Log.i(TAG, "glance hidden");
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        running = false;
        if (poller != null) poller.interrupt();
        Log.i(TAG, "glance stopped");
        super.onDestroy();
    }

    private void pollLoop() {
        while (running) {
            long started = SystemClock.elapsedRealtime();
            GlanceClient.Poll poll = client.poll();
            long took = SystemClock.elapsedRealtime() - started;
            polled = true;
            int waiting = 0;
            int working = 0;
            if (poll.tasks != null) {
                for (GlanceClient.Task task : poll.tasks) {
                    if (task.waiting) waiting++;
                    else if (working(task.status)) working++;
                }
            }
            // Codes and numbers only: never a title, the token or an address.
            Log.i(TAG, String.format(Locale.ROOT, "glance polled %s in %d ms, %d waiting, %d working, visible %d",
                poll.code, took, waiting, working, visible ? 1 : 0));
            final GlanceClient.Poll shown = poll;
            runOnUiThread(() -> show(shown));
            notifyNewlyWaiting(poll.tasks);
            try {
                Thread.sleep(visible ? VISIBLE_MS : HIDDEN_MS);
            } catch (InterruptedException woken) {
                // Shown again, or stopping: poll now, or end.
            }
        }
    }

    /** A notification when a task starts waiting since the last poll; none for what already waited. */
    private void notifyNewlyWaiting(List<GlanceClient.Task> tasks) {
        if (tasks == null) return;
        Set<String> waiting = new HashSet<>();
        for (GlanceClient.Task task : tasks) if (task.waiting) waiting.add(task.id);
        Set<String> before = waitingBefore;
        waitingBefore = waiting;
        if (before == null) return;
        Set<String> started = new HashSet<>(waiting);
        started.removeAll(before);
        if (started.isEmpty()) return;
        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            Log.i(TAG, "glance notification not allowed");
            return;
        }
        PendingIntent open = PendingIntent.getActivity(this, 0, openHalcyonic(), PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        // Halcyonic's own words only: the OS keeps notifications in its feed, so no title or agent text.
        Notification notification = new Notification.Builder(this, CHANNEL)
            .setSmallIcon(getApplicationInfo().icon)
            .setContentTitle("A task is waiting for you")
            .setContentText("Open Halcyonic to see it.")
            .setContentIntent(open)
            .addAction(new Notification.Action.Builder(null, "Open Halcyonic", open).build())
            .setAutoCancel(true)
            .build();
        getSystemService(NotificationManager.class).notify(1, notification);
        Log.i(TAG, String.format(Locale.ROOT, "glance notified, %d newly waiting", started.size()));
    }

    private Intent openHalcyonic() {
        Intent intent = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (intent == null) intent = new Intent(Intent.ACTION_MAIN).setPackage(getPackageName());
        return intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
    }

    private void show(GlanceClient.Poll poll) {
        rows.removeAllViews();
        if (poll.tasks == null) {
            lead.setText(problem(poll.code));
            lead.setTextColor(INK_2);
            return;
        }
        int waiting = 0;
        int working = 0;
        for (GlanceClient.Task task : poll.tasks) {
            if (task.waiting) waiting++;
            else if (working(task.status)) working++;
        }
        if (waiting > 0) {
            lead.setText(waiting == 1 ? "1 task is waiting for you." : waiting + " tasks are waiting for you.");
            lead.setTextColor(ATTENTION);
        } else {
            lead.setText("Nothing is waiting for you." + (working == 0 ? "" : working == 1 ? " 1 task running." : " " + working + " tasks running."));
            lead.setTextColor(INK);
        }
        int shown = 0;
        for (GlanceClient.Task task : poll.tasks) {
            if (shown == MOST_ROWS) break;
            if (task.waiting) {
                rows.addView(row(task, "Waiting for you", true));
                shown++;
            }
        }
        for (GlanceClient.Task task : poll.tasks) {
            if (shown == MOST_ROWS) break;
            if (!task.waiting && working(task.status)) {
                rows.addView(row(task, word(task.status), false));
                shown++;
            }
        }
    }

    private static boolean working(String status) {
        return "running".equals(status) || "verifying".equals(status) || "starting".equals(status);
    }

    /** The state words of the client core's StateLanguage for the states the list shows. */
    private static String word(String status) {
        if ("verifying".equals(status)) return "Checking its work";
        if ("starting".equals(status)) return "Starting";
        return "Working";
    }

    private static String problem(String code) {
        switch (code) {
            case "no_token":
            case "token_readable_by_others":
                return "Not set up on this headset yet.";
            default:
                return "Can't reach your computer. Trying again.";
        }
    }

    private View layout() {
        LinearLayout page = new LinearLayout(this);
        page.setOrientation(LinearLayout.VERTICAL);
        page.setBackgroundColor(GROUND);
        int pad = dp(24);
        page.setPadding(pad, pad, pad, pad);

        TextView title = text("Halcyonic", 24, INK);
        page.addView(title);
        lead = text("", 18, INK);
        lead.setPadding(0, dp(8), 0, dp(16));
        page.addView(lead);

        rows = new LinearLayout(this);
        rows.setOrientation(LinearLayout.VERTICAL);
        ScrollView scroll = new ScrollView(this);
        scroll.addView(rows);
        page.addView(scroll, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout footer = new LinearLayout(this);
        footer.setGravity(Gravity.END);
        footer.setPadding(0, dp(16), 0, 0);
        Button open = new Button(this);
        open.setText("Open Halcyonic");
        open.setAllCaps(false);
        open.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
        open.setTextColor(GROUND);
        GradientDrawable fill = new GradientDrawable();
        fill.setColor(ACCENT);
        fill.setCornerRadius(dp(12));
        open.setBackground(fill);
        open.setMinimumHeight(dp(60));
        open.setPadding(dp(24), 0, dp(24), 0);
        open.setOnClickListener(pressed -> startActivity(openHalcyonic()));
        footer.addView(open);
        page.addView(footer);
        return page;
    }

    private View row(GlanceClient.Task task, String state, boolean waiting) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable fill = new GradientDrawable();
        fill.setColor(RAISED);
        fill.setCornerRadius(dp(12));
        if (waiting) fill.setStroke(dp(2), ATTENTION);
        row.setBackground(fill);
        row.setPadding(dp(16), dp(12), dp(16), dp(12));
        LinearLayout.LayoutParams spacing = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        spacing.bottomMargin = dp(8);
        row.setLayoutParams(spacing);
        // Titles and project names come from outside: one line of exactly what they say, cut short.
        row.addView(text(GlanceText.cut(task.title, 80), 18, INK));
        String project = GlanceText.cut(task.project, 40);
        TextView detail = text(project.isEmpty() ? state : project + " · " + state, 15, waiting ? ATTENTION : INK_2);
        row.addView(detail);
        return row;
    }

    private TextView text(String words, int sp, int color) {
        TextView view = new TextView(this);
        view.setText(words);
        view.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        view.setTextColor(color);
        view.setSingleLine(false);
        return view;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
