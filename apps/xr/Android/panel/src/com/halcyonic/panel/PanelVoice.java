package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.Objects;

/** Consumed dictation lease, gesture and clip bounds; never command authority. */
final class PanelVoice {
    static final int RATE = 16000, MIN_SAMPLES = 8000, MAX_SAMPLES = 480000, MAX_BYTES = 960044;
    static final long HOLD_MS = 300, MAX_MS = 30000;
    static final class Lease {
        final PanelUiState.Review ticket;
        final PanelIdentity identity;
        final String route, words, request, pending;
        final Object editor;
        final int generation;
        private boolean revoked;
        Lease(PanelUiState authority, PanelUiState.Context context, PanelIdentity identity, String route,
              Object editor, String words, String request, String pending, int generation) {
            this.ticket = authority.review(0, context); this.identity = identity; this.route = route;
            this.editor = editor; this.words = words; this.request = request; this.pending = pending; this.generation = generation;
        }
        void revoke() { revoked = true; }
        boolean current(PanelUiState authority, PanelUiState.Context context, PanelIdentity target, String route,
                        Object editor, String words, String request, String pending, int generation, boolean eligible) {
            boolean current = !revoked && eligible && authority.current(ticket, context) && identity.same(target)
                && this.generation == generation && this.route.equals(route) && this.editor == editor && this.words.equals(words)
                && Objects.equals(this.request, request) && Objects.equals(this.pending, pending);
            if (!current) revoke();
            return current;
        }
    }
    static final class Gesture {
        private int pointer = -1;
        private long began;
        private boolean mature;
        boolean down(int id, long now) { if (pointer != -1) { cancel(); return false; } pointer = id; began = now; mature = false; return true; }
        boolean mature(int id, long now) { if (pointer != id || mature || now - began < HOLD_MS) return false; mature = true; return true; }
        boolean release(int id) { boolean send = pointer == id && mature; cancel(); return send; }
        boolean owns(int id) { return pointer == id; }
        void cancel() { pointer = -1; mature = false; }
    }
    static byte[] wav(short[] samples, int count) {
        if (count < MIN_SAMPLES || count > MAX_SAMPLES || count > samples.length) return null;
        byte[] bytes = new byte[44 + count * 2];
        tag(bytes, 0, "RIFF"); put(bytes, 4, 36 + count * 2, 4); tag(bytes, 8, "WAVE"); tag(bytes, 12, "fmt ");
        put(bytes, 16, 16, 4); put(bytes, 20, 1, 2); put(bytes, 22, 1, 2); put(bytes, 24, RATE, 4);
        put(bytes, 28, RATE * 2, 4); put(bytes, 32, 2, 2); put(bytes, 34, 16, 2); tag(bytes, 36, "data"); put(bytes, 40, count * 2, 4);
        for (int i = 0; i < count; i++) put(bytes, 44 + i * 2, samples[i], 2);
        return bytes;
    }
    static void discard(byte[] wav) { if (wav != null) java.util.Arrays.fill(wav, (byte)0); }
    /** The Activity owns disposal even if an executor drops a queued upload without running it. */
    static final class Audio {
        final byte[] bytes;
        Audio(byte[] bytes) { this.bytes = bytes; }
        void discard() { PanelVoice.discard(bytes); }
    }
    static boolean usableClip(byte[] wav, boolean current, boolean cancelled) {
        if (!current || cancelled || wav == null) { discard(wav); return false; }
        return true;
    }
    private static void tag(byte[] bytes, int offset, String value) { System.arraycopy(value.getBytes(StandardCharsets.US_ASCII), 0, bytes, offset, 4); }
    private static void put(byte[] bytes, int offset, int value, int count) { for (int i = 0; i < count; i++) bytes[offset + i] = (byte)(value >> (8 * i)); }
    static void validate(byte[] bytes) throws PanelTransport.Refused {
        if (bytes == null || bytes.length > MAX_BYTES || bytes.length < 44 + MIN_SAMPLES * 2 || (bytes.length - 44) % 2 != 0)
            throw new PanelTransport.Refused("invalid_audio", false);
        // Only our exact canonical header is transmitted, not arbitrary caller-controlled chunks.
        byte[] header = wav(new short[(bytes.length - 44) / 2], (bytes.length - 44) / 2);
        for (int i = 0; i < 44; i++) if (header[i] != bytes[i]) throw new PanelTransport.Refused("invalid_audio", false);
    }
    static String transcript(String json) {
        Map<String,Object> value = PanelJson.object(PanelJson.parse(json));
        boolean heard = "heard".equals(value.get("outcome"));
        PanelJson.keys(value, heard ? new String[]{"outcome", "text", "language", "engine"} : new String[]{"outcome", "engine"});
        Map<String,Object> engine = PanelJson.object(value.get("engine")); PanelJson.keys(engine, "name", "version");
        bounded(PanelJson.text(engine.get("name")), 64); bounded(PanelJson.text(engine.get("version")), 64);
        if (!heard) { if (!"nothing_heard".equals(value.get("outcome"))) throw PanelJson.invalid(); return null; }
        if (!PanelJson.text(value.get("language")).matches("[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,4}")) throw PanelJson.invalid();
        return bounded(PanelJson.text(value.get("text")), 4096);
    }
    private static String bounded(String value, int max) { if (value.trim().isEmpty() || value.codePointCount(0, value.length()) > max) throw PanelJson.invalid(); return value; }
    static String append(String before, String heard) {
        if (heard == null || heard.trim().isEmpty()) return before;
        String result = before + (before.isEmpty() || Character.isWhitespace(before.charAt(before.length()-1)) ? "" : " ") + heard;
        if (result.codePointCount(0, result.length()) > 32000) throw PanelJson.invalid();
        return result;
    }
}
