package com.halcyonic.glance;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Proxy;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * One read of what waits, over one connection to the control plane and no other: it asks the public
 * health check for the proof (GlanceProof), and only once the answer proves that what listens holds
 * the token does it ask for the snapshot, with the token, on that same connection. So the token
 * never reaches a listener that has not proved itself on the connection it is sent on, and nothing
 * can retry it elsewhere. Through no proxy, following no redirect, reading at most MOST_HEAD of each
 * head and stopping at its blank line, a body only by its Content-Length and at most MOST_SNAPSHOT
 * of the snapshot, all within DEADLINE_MS however slowly whatever answers sends. Plain Java, so the
 * Mac's tests run it whole against a real control plane and against listeners that misbehave; the
 * token comes from a TokenSource, which on the headset reads app-private storage (GlanceClient).
 */
final class GlancePoll {
    /** The most of a status line and headers read. */
    static final int MOST_HEAD = 8192;

    /** The most of the health check's body read, to reach the snapshot's answer after it. */
    static final int MOST_HEALTH_BODY = 8192;

    /** The most of a snapshot read. */
    static final int MOST_SNAPSHOT = 1024 * 1024;

    /** The longest a poll may take, start to end. */
    static final long DEADLINE_MS = 10_000;

    private static final int CONNECT_MS = 3000;
    private static final int READ_MS = 5000;

    /** The shortest token the control plane makes or accepts (security.ts). */
    static final int SHORTEST_TOKEN = 32;

    /** The most of a token file read; a longer file is not a token. */
    static final int MOST_TOKEN_BYTES = 4096;

    private static final ScheduledExecutorService WATCHDOG = Executors.newSingleThreadScheduledExecutor(runnable -> {
        Thread thread = new Thread(runnable, "halcyonic-glance-watchdog");
        thread.setDaemon(true);
        return thread;
    });

    private GlancePoll() {}

    /** Where the token comes from. */
    interface TokenSource {
        String read() throws Refused;
    }

    /** No token to send, for the reason its code says. */
    static final class Refused extends Exception {
        final String code;

        Refused(String code) {
            super(code, null, false, false);
            this.code = code;
        }
    }

    /**
     * What a poll found: a code for the log and, when it is ok, the snapshot's JSON. Codes: ok,
     * no_token, token_not_private, token_malformed, token_unreadable, unreachable, unproved,
     * refused_NNN, unreadable, too_large, too_slow.
     */
    static final class Result {
        final String code;
        final String snapshot;
        /** For unreachable and too_slow, the class of the exception that ended the poll, so a refused socket differs from no listener. */
        final String cause;

        Result(String code, String snapshot) {
            this(code, snapshot, null);
        }

        Result(String code, String snapshot, String cause) {
            this.code = code;
            this.snapshot = snapshot;
            this.cause = cause;
        }
    }

    /**
     * The token in a token file's first bytes, if the file is a regular file of this app's own that
     * no one else can read or write (mode 077 clear), holding one token of the control plane's form:
     * base64url, at least SHORTEST_TOKEN characters, and nothing past MOST_TOKEN_BYTES.
     */
    static String token(boolean regular, boolean own, int mode, byte[] bytes, int length) throws Refused {
        if (!regular || !own || (mode & 077) != 0) throw new Refused("token_not_private");
        if (length >= MOST_TOKEN_BYTES) throw new Refused("token_malformed");
        String token = new String(bytes, 0, Math.max(length, 0), StandardCharsets.US_ASCII).trim();
        if (token.length() < SHORTEST_TOKEN || !token.matches("[A-Za-z0-9_-]+")) throw new Refused("token_malformed");
        return token;
    }

    static Result run(String host, int port, TokenSource tokens) {
        String token;
        try {
            token = tokens.read();
        } catch (Refused refused) {
            return new Result(refused.code, null);
        }
        String address = host + ":" + port;
        String challenge = GlanceProof.challenge();
        Socket socket = new Socket(Proxy.NO_PROXY);
        AtomicBoolean late = new AtomicBoolean();
        ScheduledFuture<?> deadline = WATCHDOG.schedule(() -> {
            late.set(true);
            close(socket);
        }, DEADLINE_MS, TimeUnit.MILLISECONDS);
        try {
            socket.connect(new InetSocketAddress(host, port), CONNECT_MS);
            socket.setSoTimeout(READ_MS);
            OutputStream out = socket.getOutputStream();
            InputStream in = new BufferedInputStream(socket.getInputStream());
            send(out, "GET /api/health HTTP/1.1\r\nHost: " + address + "\r\nx-halcyonic-challenge: " + challenge + "\r\n\r\n");
            Head health = Head.read(in);
            if (health == null || health.status != 200 || !health.keptOpen()) return new Result("unproved", null);
            String proof = health.only("x-halcyonic-proof");
            if (proof == null || !GlanceProof.proves(proof, token, address, challenge)) return new Result("unproved", null);
            if (body(in, health, MOST_HEALTH_BODY) == null) return new Result("unproved", null);
            // Proved on this connection: the token goes on it, and nowhere else.
            send(out, "GET /api/snapshot HTTP/1.1\r\nHost: " + address + "\r\nAuthorization: Bearer " + token
                + "\r\nConnection: close\r\n\r\n");
            Head answer = Head.read(in);
            if (answer == null) return new Result("unreadable", null);
            if (answer.status != 200) return new Result(String.format(Locale.ROOT, "refused_%03d", answer.status), null);
            byte[] snapshot = body(in, answer, MOST_SNAPSHOT);
            if (snapshot == null) return new Result(answer.length() > MOST_SNAPSHOT ? "too_large" : "unreadable", null);
            return new Result("ok", new String(snapshot, StandardCharsets.UTF_8));
        } catch (IOException error) {
            return new Result(late.get() ? "too_slow" : "unreachable", null, error.getClass().getSimpleName());
        } finally {
            deadline.cancel(false);
            close(socket);
        }
    }

    private static void send(OutputStream out, String request) throws IOException {
        out.write(request.getBytes(StandardCharsets.ISO_8859_1));
        out.flush();
    }

    /** A body of exactly its Content-Length, at most <code>most</code> bytes; null when it has no length of its own or a longer one. */
    private static byte[] body(InputStream in, Head head, int most) throws IOException {
        long length = head.length();
        if (length < 0 || length > most || head.count("transfer-encoding") > 0) return null;
        byte[] body = new byte[(int) length];
        int read = 0;
        while (read < body.length) {
            int count = in.read(body, read, body.length - read);
            if (count < 0) throw new IOException("ended inside a body");
            read += count;
        }
        return body;
    }

    private static void close(Socket socket) {
        try {
            socket.close();
        } catch (IOException ignored) {
            // Closing ends any read either way.
        }
    }

    /** An HTTP answer's status and headers, up to the first empty line and never past it. */
    static final class Head {
        private static final Pattern STATUS_LINE = Pattern.compile("HTTP/1\\.([01]) ([0-9]{3})( .*)?");

        final int status;
        final boolean http11;
        final List<String[]> headers;

        private Head(int status, boolean http11, List<String[]> headers) {
            this.status = status;
            this.http11 = http11;
            this.headers = headers;
        }

        /**
         * Reads a head, byte by byte so nothing after its blank line is taken; null when it is not one
         * within MOST_HEAD. A connection closed before any byte answered nothing: as adbd does when
         * nothing listens behind its reverse mapping, which is unreachable, not unproved.
         */
        static Head read(InputStream in) throws IOException {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            int matched = 0;
            while (matched < 4) {
                if (bytes.size() >= MOST_HEAD) return null;
                int next = in.read();
                if (next < 0 && bytes.size() == 0) throw new EOFException("closed before answering");
                if (next < 0) return null;
                bytes.write(next);
                matched = next == (matched % 2 == 0 ? '\r' : '\n') ? matched + 1 : next == '\r' ? 1 : 0;
            }
            String text = new String(bytes.toByteArray(), 0, bytes.size() - 4, StandardCharsets.ISO_8859_1);
            String[] lines = text.split("\r\n", -1);
            Matcher status = STATUS_LINE.matcher(lines[0]);
            if (!status.matches()) return null;
            List<String[]> headers = new ArrayList<>();
            for (int index = 1; index < lines.length; index++) {
                int colon = lines[index].indexOf(':');
                if (colon <= 0) return null;
                headers.add(new String[] {lines[index].substring(0, colon).trim().toLowerCase(Locale.ROOT), lines[index].substring(colon + 1).trim()});
            }
            return new Head(Integer.parseInt(status.group(2)), "1".equals(status.group(1)), headers);
        }

        int count(String name) {
            int count = 0;
            for (String[] header : headers) if (header[0].equals(name)) count++;
            return count;
        }

        /** The one value of a header; null when it is absent or given more than once. */
        String only(String name) {
            if (count(name) != 1) return null;
            for (String[] header : headers) if (header[0].equals(name)) return header[1];
            return null;
        }

        /** Its Content-Length, or -1 when it has none, more than one, or one that is not a number. */
        long length() {
            String value = only("content-length");
            if (value == null || !value.matches("[0-9]{1,10}")) return -1;
            return Long.parseLong(value);
        }

        /** The connection stays open for another request: HTTP/1.1, and not told to close. */
        boolean keptOpen() {
            String connection = only("connection");
            return http11 && count("connection") <= 1 && (connection == null || !connection.equalsIgnoreCase("close"));
        }
    }
}
