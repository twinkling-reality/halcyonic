package com.halcyonic.glance;

import android.content.Context;
import android.system.ErrnoException;
import android.system.Os;
import android.system.OsConstants;
import android.system.StructStat;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileDescriptor;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.Proxy;
import java.net.URL;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/**
 * Reads what waits from the control plane, the only place the glance talks to: the snapshot the
 * control plane computed, never a runtime, Salidium or Seorak, and it sends no command. Before each
 * request that carries the access token, the control plane proves it holds it (GlanceProof); no
 * redirect is followed. The token is read only from app-private storage, and only if no one else can
 * read the file (mode 600). Development builds only; a release build never carries this.
 */
final class GlanceClient {
    /** Where the headset reaches the control plane over USB, through adb reverse. */
    static final String HOST = "127.0.0.1";

    static final int PORT = 47800;

    /** The token's file in the app's private storage, written there by the owner with run-as. */
    static final String TOKEN_FILE = "glance-access-token";

    /** The most of a snapshot the glance reads. */
    private static final int MOST_BYTES = 1024 * 1024;

    /** The longest a request may take, start to end, however slowly whatever answers sends. */
    private static final long DEADLINE_MS = 10_000;

    /** The shortest token the control plane makes or accepts (security.ts). */
    private static final int SHORTEST_TOKEN = 32;

    /** Ends a request that runs past its deadline, by closing its connection. */
    private static final ScheduledExecutorService WATCHDOG = Executors.newSingleThreadScheduledExecutor(runnable -> {
        Thread thread = new Thread(runnable, "halcyonic-glance-watchdog");
        thread.setDaemon(true);
        return thread;
    });

    /** What a poll found: a code for the log, and the tasks when it read them. */
    static final class Poll {
        final String code;
        final List<Task> tasks;

        Poll(String code, List<Task> tasks) {
            this.code = code;
            this.tasks = tasks;
        }
    }

    /** One task as the control plane reports it; the title and project are text from outside. */
    static final class Task {
        final String id;
        final String title;
        final String project;
        final boolean waiting;
        final String status;

        Task(String id, String title, String project, boolean waiting, String status) {
            this.id = id;
            this.title = title;
            this.project = project;
            this.waiting = waiting;
            this.status = status;
        }
    }

    private final File tokenFile;

    GlanceClient(Context context) {
        tokenFile = new File(context.getFilesDir(), TOKEN_FILE);
    }

    /**
     * One poll. Codes: ok, no_token, token_not_private, token_malformed, unreachable, unproved,
     * refused_NNN, unreadable.
     */
    Poll poll() {
        String token;
        try {
            token = readToken();
        } catch (SecurityException refused) {
            return new Poll(refused.getMessage(), null);
        } catch (IOException missing) {
            return new Poll("no_token", null);
        }
        String address = HOST + ":" + PORT;
        String base = "http://" + address;
        String challenge = GlanceProof.challenge();
        String proof;
        HttpURLConnection health = null;
        ScheduledFuture<?> healthDeadline = null;
        try {
            health = open(base + "/api/health");
            healthDeadline = deadline(health);
            health.setRequestProperty("x-halcyonic-challenge", challenge);
            int status = health.getResponseCode();
            proof = health.getHeaderField("x-halcyonic-proof");
            // Nothing is read from what has not proved itself yet, not even the body.
            if (status != 200) return new Poll("unproved", null);
        } catch (IOException error) {
            return new Poll("unreachable", null);
        } finally {
            if (healthDeadline != null) healthDeadline.cancel(false);
            if (health != null) health.disconnect();
        }
        if (!GlanceProof.proves(proof, token, address, challenge)) return new Poll("unproved", null);
        HttpURLConnection snapshot = null;
        ScheduledFuture<?> snapshotDeadline = null;
        try {
            snapshot = open(base + "/api/snapshot");
            snapshotDeadline = deadline(snapshot);
            snapshot.setRequestProperty("Authorization", "Bearer " + token);
            int status = snapshot.getResponseCode();
            if (status != 200) return new Poll("refused_" + status, null);
            String body = read(snapshot);
            return new Poll("ok", tasks(new JSONObject(body)));
        } catch (IOException error) {
            return new Poll("unreachable", null);
        } catch (JSONException error) {
            return new Poll("unreadable", null);
        } finally {
            if (snapshotDeadline != null) snapshotDeadline.cancel(false);
            if (snapshot != null) snapshot.disconnect();
        }
    }

    /** Closes the connection if the request is still running at its deadline. */
    private static ScheduledFuture<?> deadline(HttpURLConnection connection) {
        return WATCHDOG.schedule(connection::disconnect, DEADLINE_MS, TimeUnit.MILLISECONDS);
    }

    /**
     * The token, if its file is a regular file of the app's own, not a link, that no one else can
     * read or write, holding a token of the control plane's form. Checked on the open descriptor, so
     * the file checked is the file read.
     */
    private String readToken() throws IOException {
        FileDescriptor descriptor;
        try {
            descriptor = Os.open(tokenFile.getPath(), OsConstants.O_RDONLY | OsConstants.O_NOFOLLOW | OsConstants.O_CLOEXEC, 0);
        } catch (ErrnoException missing) {
            throw new IOException("no token file");
        }
        try (FileInputStream in = new FileInputStream(descriptor)) {
            StructStat stat;
            try {
                stat = Os.fstat(descriptor);
            } catch (ErrnoException error) {
                throw new IOException("token file unreadable");
            }
            if (!OsConstants.S_ISREG(stat.st_mode) || stat.st_uid != Os.getuid() || (stat.st_mode & 077) != 0) {
                throw new SecurityException("token_not_private");
            }
            byte[] bytes = new byte[4096];
            int read = in.read(bytes);
            String token = new String(bytes, 0, Math.max(read, 0), StandardCharsets.US_ASCII).trim();
            // The control plane's tokens are base64url, at least 32 characters (security.ts).
            if (token.length() < SHORTEST_TOKEN || !token.matches("[A-Za-z0-9_-]+")) throw new SecurityException("token_malformed");
            return token;
        }
    }

    private static HttpURLConnection open(String url) throws IOException {
        // Never through a proxy: the proof must come from the control plane itself.
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection(Proxy.NO_PROXY);
        connection.setInstanceFollowRedirects(false);
        connection.setUseCaches(false);
        connection.setConnectTimeout(3000);
        connection.setReadTimeout(5000);
        return connection;
    }

    private static String read(HttpURLConnection connection) throws IOException {
        try (InputStream in = connection.getInputStream()) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[16384];
            int count;
            while ((count = in.read(buffer)) > 0) {
                if (out.size() + count > MOST_BYTES) throw new IOException("snapshot too large");
                out.write(buffer, 0, count);
            }
            return new String(out.toByteArray(), StandardCharsets.UTF_8);
        } finally {
            connection.disconnect();
        }
    }

    /**
     * The tasks in a snapshot, from fields the control plane computed (tooling/glance's test holds
     * these paths to the generated JSON Schema): each workstream's id, title, project and status, and
     * whether its attention level is action_required.
     */
    static List<Task> tasks(JSONObject snapshot) throws JSONException {
        Map<String, String> projects = new HashMap<>();
        JSONArray projectList = snapshot.getJSONArray("projects");
        for (int index = 0; index < projectList.length(); index++) {
            JSONObject project = projectList.getJSONObject(index);
            projects.put(project.getString("project_id"), project.getString("name"));
        }
        List<Task> tasks = new ArrayList<>();
        JSONArray workstreams = snapshot.getJSONArray("workstreams");
        for (int index = 0; index < workstreams.length(); index++) {
            JSONObject workstream = workstreams.getJSONObject(index);
            String project = projects.get(workstream.getString("project_id"));
            boolean waiting = "action_required".equals(workstream.getJSONObject("attention").getString("level"));
            tasks.add(new Task(
                workstream.getString("workstream_id"),
                workstream.getString("title"),
                project == null ? "" : project,
                waiting,
                workstream.getString("status")));
        }
        return tasks;
    }
}
