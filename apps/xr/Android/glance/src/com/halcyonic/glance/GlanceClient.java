package com.halcyonic.glance;

import android.content.Context;
import android.system.ErrnoException;
import android.system.Os;
import android.system.OsConstants;
import android.system.StructStat;
import java.io.File;
import java.io.FileDescriptor;
import java.io.FileInputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/**
 * Reads what waits from the control plane, the only place the glance talks to: the snapshot the
 * control plane computed, never a runtime, Salidium or Seorak, and it sends no command. The request
 * itself, with the proof before the token, is GlancePoll's; this reads the token from app-private
 * storage and the tasks from the snapshot. Development builds only; a release build never carries
 * this.
 */
final class GlanceClient {
    /** Where the headset reaches the control plane over USB, through adb reverse. */
    static final String HOST = "127.0.0.1";

    static final int PORT = 47800;

    /** The token's file in the app's private storage, written there by the owner with run-as. */
    static final String TOKEN_FILE = "glance-access-token";

    /** What a poll found: a code for the log, and the tasks when it read them. */
    static final class Poll {
        final String code;
        final List<Task> tasks;
        /** GlancePoll's cause, an exception's class name, or null. */
        final String cause;

        Poll(String code, List<Task> tasks, String cause) {
            this.code = code;
            this.tasks = tasks;
            this.cause = cause;
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

    /** One poll, with GlancePoll's codes. */
    Poll poll() {
        GlancePoll.Result result = GlancePoll.run(HOST, PORT, this::readToken);
        if (result.snapshot == null) return new Poll(result.code, null, result.cause);
        try {
            return new Poll(result.code, tasks(new JSONObject(result.snapshot)), null);
        } catch (JSONException error) {
            return new Poll("unreadable", null, null);
        }
    }

    /**
     * The token (GlancePoll.token), checked on the open descriptor, so the file checked is the file
     * read. Opened without following a link (a link is ELOOP, not private) and without blocking, so a
     * pipe put in its place cannot hold the poll past its deadline: it fails the regular-file check
     * before anything is read.
     */
    private String readToken() throws GlancePoll.Refused {
        FileDescriptor descriptor;
        try {
            descriptor = Os.open(tokenFile.getPath(),
                OsConstants.O_RDONLY | OsConstants.O_NOFOLLOW | OsConstants.O_NONBLOCK | OsConstants.O_CLOEXEC, 0);
        } catch (ErrnoException error) {
            if (error.errno == OsConstants.ENOENT) throw new GlancePoll.Refused("no_token");
            if (error.errno == OsConstants.ELOOP) throw new GlancePoll.Refused("token_not_private");
            throw new GlancePoll.Refused("token_unreadable");
        }
        // Android's FileInputStream does not own a descriptor it is given: close it ourselves.
        try (FileInputStream in = new FileInputStream(descriptor)) {
            StructStat stat = Os.fstat(descriptor);
            boolean regular = OsConstants.S_ISREG(stat.st_mode);
            boolean own = stat.st_uid == Os.getuid();
            byte[] bytes = new byte[GlancePoll.MOST_TOKEN_BYTES];
            int length = 0;
            if (regular && own && (stat.st_mode & 077) == 0) {
                int count;
                while (length < bytes.length && (count = in.read(bytes, length, bytes.length - length)) > 0) length += count;
            }
            return GlancePoll.token(regular, own, stat.st_mode, bytes, length);
        } catch (ErrnoException | IOException error) {
            throw new GlancePoll.Refused("token_unreadable");
        } finally {
            try {
                Os.close(descriptor);
            } catch (ErrnoException ignored) {
                // Already closed: nothing more to release.
            }
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
