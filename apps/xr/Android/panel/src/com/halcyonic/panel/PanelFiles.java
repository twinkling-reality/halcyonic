package com.halcyonic.panel;

import android.content.Context;
import android.content.pm.ApplicationInfo;
import java.util.Map;
import android.system.ErrnoException;
import android.system.Os;
import android.system.OsConstants;
import android.system.StructStat;
import java.io.File;
import java.io.FileDescriptor;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

/** All records are app-private, bounded, regular files, checked on the descriptor actually read. */
final class PanelFiles {
    static final String PAIRING = "halcyonic-pairing.json";
    static final String STATE = "halcyonic-workspace-panel.json";
    static final String INCOMING = "halcyonic-workspace-to-panel.json";
    static final String RETURN = "halcyonic-workspace-return.json";
    static final String MODE = "halcyonic-panel-mode.json";
    static final String DRAFTS = "halcyonic-workspace-drafts.json";
    static final String UNITY_VIEWS = "halcyonic-unity-workspace-views.json";
    private static final Object LOCK = new Object();
    private final File directory;
    final boolean developmentAllowed;
    PanelFiles(Context context) { directory = context.getFilesDir(); developmentAllowed = (context.getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0; }

    boolean exists(String name) { return new File(directory, name).exists(); }
    PanelIdentity pairing() throws IOException { return PanelIdentity.pairing(read(PAIRING, 16384)); }
    boolean developmentUsb() throws IOException {
        if (!developmentAllowed || !exists(MODE)) return false;
        Map<String, Object> value = PanelJson.object(PanelJson.parse(read(MODE, 1024)));
        PanelJson.keys(value, "version", "development_usb"); PanelJson.integer(value.get("version"), 1, 1);
        return PanelJson.bool(value.get("development_usb"));
    }
    void useDevelopmentUsb(boolean enabled) throws IOException {
        if (!developmentAllowed) throw new IOException("Development connection unavailable.");
        write(MODE, PanelJson.encode(PanelJson.map("version", 1L, "development_usb", enabled)));
    }
    PanelIdentity connection() throws IOException {
        if (!developmentUsb()) return pairing();
        String token = read("access-token", 4095).trim();
        return PanelIdentity.developmentUsb(47800, token);
    }
    String read(String name, int maximum) throws IOException {
        synchronized (LOCK) { return readLocked(name, maximum); }
    }
    private String readLocked(String name, int maximum) throws IOException {
        FileDescriptor descriptor;
        try {
            descriptor = Os.open(new File(directory, name).getPath(), OsConstants.O_RDONLY | OsConstants.O_NOFOLLOW | OsConstants.O_NONBLOCK | OsConstants.O_CLOEXEC, 0);
        } catch (ErrnoException error) {
            if (error.errno == OsConstants.ENOENT) throw new RecordAbsent();
            throw new IOException("Private record unavailable.");
        }
        try (FileInputStream input = new FileInputStream(descriptor)) {
            StructStat stat = Os.fstat(descriptor);
            if (!OsConstants.S_ISREG(stat.st_mode) || stat.st_uid != Os.getuid() || (stat.st_mode & 0077) != 0
                || stat.st_size < 0 || stat.st_size > maximum) throw new IOException("Private record unavailable.");
            byte[] bytes = new byte[maximum + 1];
            int count = 0, got;
            while (count < bytes.length && (got = input.read(bytes, count, bytes.length - count)) >= 0) count += got;
            if (count > maximum) throw new IOException("Private record unavailable.");
            return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes, 0, count)).toString();
        } catch (ErrnoException error) { throw new IOException("Private record unavailable."); }
    }
    void write(String name, String json) throws IOException {
        synchronized (LOCK) { writeLocked(name, json); }
    }
    private void writeLocked(String name, String json) throws IOException {
        if (name.equals(MODE)) { Map<String, Object> v = PanelJson.object(PanelJson.parse(json)); PanelJson.keys(v, "version", "development_usb"); PanelJson.integer(v.get("version"), 1, 1); PanelJson.bool(v.get("development_usb")); }
        else if (name.equals(DRAFTS)) PanelDrafts.parse(json);
        else if (name.equals(UNITY_VIEWS)) PanelViewStore.validate(json);
        else PanelContinuation.parse(json);
        String temporary = new File(directory, name + "." + UUID.randomUUID() + ".new").getPath();
        try {
            FileDescriptor descriptor = Os.open(temporary, OsConstants.O_WRONLY | OsConstants.O_CREAT | OsConstants.O_EXCL | OsConstants.O_NOFOLLOW | OsConstants.O_CLOEXEC, 0600);
            try (FileOutputStream output = new FileOutputStream(descriptor)) {
                output.write(json.getBytes(StandardCharsets.UTF_8)); output.flush(); output.getFD().sync();
            }
            Os.rename(temporary, new File(directory, name).getPath());
        } catch (ErrnoException error) { throw new IOException("Private record unavailable."); }
        finally { new File(temporary).delete(); }
    }
    /** Only an actual ENOENT from opening the fixed record means absent. */
    String readWorkspaceViews() throws IOException {
        synchronized (LOCK) {
            try { return readLocked(UNITY_VIEWS, PanelViewStore.MAXIMUM_BYTES); }
            catch (RecordAbsent missing) { return null; }
        }
    }
    private static final class RecordAbsent extends IOException { }
    void remove(String name) { synchronized (LOCK) { new File(directory, name).delete(); } }
    String peekReturn() {
        synchronized (LOCK) {
            try { return PanelContinuation.parse(readLocked(RETURN, PanelContinuation.MAXIMUM_BYTES)).json(); }
            catch (Exception error) { return null; }
        }
    }
    boolean acknowledgeReturn(String id) {
        return acknowledge(RETURN, id);
    }
    boolean acknowledge(String name, String id) {
        synchronized (LOCK) {
            try {
                String json = readLocked(name, PanelContinuation.MAXIMUM_BYTES);
                return PanelContinuation.parse(json).string("handoff_id").equals(id) && new File(directory, name).delete();
            } catch (Exception error) { return false; }
        }
    }
}
