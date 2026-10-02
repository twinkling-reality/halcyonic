package com.halcyonic.glance;

import java.io.IOException;
import java.io.InputStream;
import java.net.InetSocketAddress;
import java.net.Proxy;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;

/**
 * Asks the control plane's public health check for its proof (GlanceProof) before the glance sends
 * the token anywhere. Whatever listens on the port has proved nothing yet, so this reads at most
 * MOST_BYTES of status line and headers, none of the body, through no proxy, within a deadline, and
 * takes a 200 with exactly one proof header or nothing. Plain Java, so the Mac's tests run it against
 * a real control plane and against listeners that misbehave.
 */
final class GlanceHealth {
    /** The most of a health answer read: its status line and headers. */
    static final int MOST_BYTES = 8192;

    /** The longest it may take, start to end, however slowly whatever answers sends. */
    static final long DEADLINE_MS = 10_000;

    private static final ScheduledExecutorService WATCHDOG = Executors.newSingleThreadScheduledExecutor(runnable -> {
        Thread thread = new Thread(runnable, "halcyonic-glance-health");
        thread.setDaemon(true);
        return thread;
    });

    private GlanceHealth() {}

    /** The proof header answered for <code>challenge</code>, or null when the answer is not a 200 with exactly one. */
    static String proof(String host, int port, String challenge) throws IOException {
        Socket socket = new Socket(Proxy.NO_PROXY);
        ScheduledFuture<?> deadline = WATCHDOG.schedule(() -> {
            try {
                socket.close();
            } catch (IOException ignored) {
                // Closing ends the read either way.
            }
        }, DEADLINE_MS, TimeUnit.MILLISECONDS);
        try {
            socket.connect(new InetSocketAddress(host, port), 3000);
            socket.setSoTimeout(5000);
            String request = "GET /api/health HTTP/1.1\r\nHost: " + host + ":" + port
                + "\r\nx-halcyonic-challenge: " + challenge + "\r\nConnection: close\r\n\r\n";
            socket.getOutputStream().write(request.getBytes(StandardCharsets.US_ASCII));
            socket.getOutputStream().flush();
            InputStream in = socket.getInputStream();
            byte[] head = new byte[MOST_BYTES];
            int length = 0;
            while (length < head.length && !endOfHead(head, length)) {
                int count = in.read(head, length, head.length - length);
                if (count < 0) break;
                length += count;
            }
            if (!endOfHead(head, length)) return null;
            String[] lines = new String(head, 0, length, StandardCharsets.ISO_8859_1).split("\r\n");
            if (lines.length == 0 || !lines[0].matches("HTTP/1\\.[01] 200( .*)?")) return null;
            String proof = null;
            for (int index = 1; index < lines.length; index++) {
                int colon = lines[index].indexOf(':');
                if (colon <= 0) continue;
                if (!lines[index].substring(0, colon).trim().equalsIgnoreCase("x-halcyonic-proof")) continue;
                // Two proof headers prove nothing.
                if (proof != null) return null;
                proof = lines[index].substring(colon + 1).trim();
            }
            return proof;
        } finally {
            deadline.cancel(false);
            socket.close();
        }
    }

    /** Whether the bytes read so far hold the blank line that ends an HTTP head. */
    private static boolean endOfHead(byte[] head, int length) {
        for (int index = 3; index < length; index++) {
            if (head[index - 3] == '\r' && head[index - 2] == '\n' && head[index - 1] == '\r' && head[index] == '\n') return true;
        }
        return false;
    }
}
