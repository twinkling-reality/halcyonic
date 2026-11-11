package com.halcyonic.relay;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;

/**
 * Keeps Halcyonic's process running while a preview is relayed to the headset's browser (ADR 0052).
 * The relay itself is the client core's PreviewRelay, on threads of the app's own process; when the
 * browser comes to the front, Halcyonic's activity stops and Android may freeze or end a process with
 * nothing in the foreground, which would stop the relay mid-page. A foreground service, held only while
 * a relay is open, keeps the process in the foreground set. It does nothing else: no network, no state.
 */
public final class PreviewRelayService extends Service {
    private static final String CHANNEL = "halcyonic.preview";
    private static final int NOTIFICATION = 52;

    /** Holds the process in the foreground while a relay is open; called from the Unity layer. */
    public static void hold(Context context, boolean relaying) {
        Intent intent = new Intent(context, PreviewRelayService.class);
        if (relaying) {
            context.startForegroundService(intent);
        } else {
            context.stopService(intent);
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        // The project's minimum is Android 12 (API 32), so a channel is always there to post in.
        getSystemService(NotificationManager.class).createNotificationChannel(
            new NotificationChannel(CHANNEL, "Preview", NotificationManager.IMPORTANCE_LOW));
        Notification notification = new Notification.Builder(this, CHANNEL)
            .setContentTitle("Showing Your Preview")
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setOngoing(true)
            .build();
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTIFICATION, notification);
        }
        // Not restarted if Android ends it: a relay that is gone needs no process held for it.
        return START_NOT_STICKY;
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
