package com.halcyonic.panel;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.cert.X509Certificate;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.Future;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.SynchronousQueue;
import java.util.concurrent.ThreadPoolExecutor;
import java.util.concurrent.TimeUnit;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

/** Independent pinned HTTPS client. Credentials leave only after the leaf certificate matched. */
final class PanelTransport {
    interface PairingSource { PanelIdentity read() throws IOException; }
    static final class Result {
        final int status; final String body; final Map<String, String> headers;
        Result(int status, String body) { this(status, body, new HashMap<>()); }
        Result(int status, String body, Map<String, String> headers) { this.status = status; this.body = body; this.headers = headers; }
    }
    static final class Refused extends IOException {
        final String code; final boolean unknownEffect;
        Refused(String code, boolean unknownEffect) { super(code); this.code = code; this.unknownEffect = unknownEffect; }
    }
    private static final ThreadPoolExecutor DNS = new ThreadPoolExecutor(0, 1, 10, TimeUnit.SECONDS,
        new SynchronousQueue<>(), runnable -> { Thread thread = new Thread(runnable, "Halcyonic panel address"); thread.setDaemon(true); return thread; });
    private final PairingSource source;
    private final java.util.function.Supplier<Socket> sockets;
    private static final ScheduledExecutorService WATCHDOG = Executors.newSingleThreadScheduledExecutor(runnable -> {
        Thread thread = new Thread(runnable, "Halcyonic panel deadline"); thread.setDaemon(true); return thread;
    });
    private static final class Lease {
        volatile Socket raw, wrapped;
        void close() {
            // Close the raw descriptor first: TLS close-notify must not wait for a blocked writer.
            Socket plain = raw, secure = wrapped;
            if (plain != null) try { plain.close(); } catch (IOException ignored) {}
            if (secure != null && secure != plain) try { secure.close(); } catch (IOException ignored) {}
        }
    }
    private volatile Lease active;
    private volatile boolean cancelled;

    PanelTransport(PairingSource source) { this(source, () -> new Socket(java.net.Proxy.NO_PROXY)); }
    PanelTransport(PairingSource source, java.util.function.Supplier<Socket> sockets) { this.source = source; this.sockets = sockets; }
    void cancel() { cancelled = true; Lease lease = active; if (lease != null) lease.close(); }

    Result request(PanelIdentity expected, String method, String path, String body) throws IOException {
        if (!(method.equals("GET") && (path.equals("/api/snapshot") || path.equals("/api/workspace/agent") || path.matches("/api/workspace/requests/" + PanelIdentity.ENTITY) || path.matches("/api/workspace/commands/" + PanelIdentity.UUID) || path.matches("/api/events\\?after=[0-9]{1,16}&limit=50&workstream_id=" + PanelIdentity.ENTITY)))
            && !(method.equals("POST") && path.equals("/api/commands") && body != null)) throw new Refused("unsupported_request", false);
        byte[] bytes = body == null ? new byte[0] : body.getBytes(StandardCharsets.UTF_8);
        if (bytes.length > 256 * 1024) throw new Refused("request_too_large", false);
        return perform(expected, method, path, bytes, body == null ? null : "application/json", 15);
    }

    Result transcribe(PanelIdentity expected, byte[] wav) throws IOException {
        PanelVoice.validate(wav);
        return perform(expected, "POST", "/api/transcriptions", wav, "audio/wav", 30);
    }

    private Result perform(PanelIdentity expected, String method, String path, byte[] bytes, String contentType, int seconds) throws IOException {
        boolean wrote = false;
        long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(seconds);
        Lease lease = new Lease(); active = lease;
        AtomicBoolean timedOut = new AtomicBoolean();
        ScheduledFuture<?> cutoff = WATCHDOG.schedule(() -> { timedOut.set(true); lease.close(); }, seconds, TimeUnit.SECONDS);
        try {
            current(expected);
            Future<InetAddress[]> lookup = DNS.submit(() -> InetAddress.getAllByName(expected.host));
            InetAddress[] addresses;
            try { addresses = lookup.get(3, TimeUnit.SECONDS); }
            finally { lookup.cancel(true); }
            if (addresses.length == 0) throw new Refused("address_unavailable", false);
            Socket plain = sockets.get();
            lease.raw = plain;
            current(expected);
            plain.connect(new InetSocketAddress(addresses[0], expected.port), 4000);
            plain.setSoTimeout(4000);
            Socket socket = plain;
            if (expected.scheme.equals("wss")) {
            SSLContext tls = SSLContext.getInstance("TLS");
            // Trust is the exact paired certificate, checked below before any HTTP byte. A CA or
            // hostname replacement is not an alternative authority for this local paired service.
            tls.init(null, new TrustManager[] {new X509TrustManager() {
                public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
                public void checkClientTrusted(X509Certificate[] chain, String kind) {}
                public void checkServerTrusted(X509Certificate[] chain, String kind) {}
            }}, null);
            SSLSocket secure = (SSLSocket) tls.getSocketFactory().createSocket(plain, expected.host, expected.port, true);
            lease.wrapped = secure;
            secure.setSoTimeout(4000);
            secure.startHandshake();
            String seen = PanelIdentity.hash(secure.getSession().getPeerCertificates()[0].getEncoded());
            if (!MessageDigest.isEqual(seen.getBytes(StandardCharsets.US_ASCII), expected.pin.getBytes(StandardCharsets.US_ASCII)))
                throw new Refused("certificate_changed", false);
            socket = secure;
            } else {
                // Explicit debuggable-build USB mode only. Prove the existing local owner token
                // on this exact socket before sending it, as the verified glance protocol does.
                String challenge = PanelProof.challenge();
                OutputStream proofRequest = socket.getOutputStream();
                proofRequest.write(("GET /api/health HTTP/1.1\r\nHost: 127.0.0.1:" + expected.port + "\r\nx-halcyonic-challenge: " + challenge + "\r\n\r\n").getBytes(StandardCharsets.US_ASCII)); proofRequest.flush();
                Result health = response(new DeadlineInput(socket, deadline), deadline);
                if (health.status != 200 || "close".equalsIgnoreCase(health.headers.get("connection"))
                    || !PanelProof.proves(health.headers.get("x-halcyonic-proof"), expected.credential, "127.0.0.1:" + expected.port, challenge)) throw new Refused("local_server_unproved", false);
            }
            current(expected);
            String host = expected.host.contains(":") ? "[" + expected.host + "]" : expected.host;
            String headers = method + " " + path + " HTTP/1.1\r\nHost: " + host + ":" + expected.port
                + "\r\nAuthorization: Bearer " + expected.credential + "\r\nAccept: application/json\r\nConnection: close\r\n"
                + (contentType == null ? "" : "Content-Type: " + contentType + "\r\nContent-Length: " + bytes.length + "\r\n") + "\r\n";
            OutputStream output = socket.getOutputStream();
            // A write can partially reach the host even when it throws. Never retry a command.
            wrote = method.equals("POST");
            output.write(headers.getBytes(StandardCharsets.US_ASCII)); output.write(bytes); output.flush();
            Result result = response(new DeadlineInput(socket, deadline, seconds == 30 ? 30000 : 4000), deadline, contentType != null && contentType.equals("audio/wav") ? 64 * 1024 : 8 * 1024 * 1024);
            current(expected);
            return result;
        } catch (Refused error) { throw new Refused(error.code, wrote || error.unknownEffect); }
        catch (Exception error) { throw new Refused(timedOut.get() ? "request_timeout" : cancelled ? "panel_closed" : "connection_unavailable", wrote); }
        finally { cutoff.cancel(false); lease.close(); if (active == lease) active = null; }
    }

    private void current(PanelIdentity expected) throws IOException {
        if (cancelled) throw new Refused("panel_closed", false);
        if (!expected.same(source.read())) throw new Refused("pairing_changed", false);
    }

    private static final class DeadlineInput extends java.io.FilterInputStream {
        private final Socket socket; private final long deadline; private final int idle;
        DeadlineInput(Socket socket, long deadline) throws IOException { this(socket, deadline, 4000); }
        DeadlineInput(Socket socket, long deadline, int idle) throws IOException { super(socket.getInputStream()); this.socket = socket; this.deadline = deadline; this.idle = idle; }
        private void bound() throws IOException {
            long remaining = TimeUnit.NANOSECONDS.toMillis(deadline - System.nanoTime());
            if (remaining <= 0) throw new Refused("response_timeout", false);
            socket.setSoTimeout((int) Math.min(idle, remaining));
        }
        @Override public int read() throws IOException { bound(); return super.read(); }
        @Override public int read(byte[] bytes, int offset, int count) throws IOException { bound(); return in.read(bytes, offset, count); }
    }

    private static Result response(InputStream input, long deadline) throws IOException { return response(input, deadline, 8 * 1024 * 1024); }
    private static Result response(InputStream input, long deadline, int maximum) throws IOException {
        int headerBytes = 0;
        String statusLine = line(input, deadline);
        headerBytes += statusLine.length();
        if (!statusLine.matches("HTTP/1\\.[01] [0-9]{3}( [\\x20-\\x7e]*)?")) throw new Refused("invalid_response", false);
        int status = Integer.parseInt(statusLine.substring(9, 12));
        Map<String, String> headers = new HashMap<>();
        for (String line = line(input, deadline); !line.isEmpty(); line = line(input, deadline)) {
            headerBytes += line.length() + 2;
            int colon = line.indexOf(':');
            if (headerBytes > 16384 || colon < 1) throw new Refused("invalid_response", false);
            String name = line.substring(0, colon).toLowerCase(Locale.ROOT);
            if (!name.matches("[a-z0-9-]+") || headers.put(name, line.substring(colon + 1).trim()) != null) throw new Refused("invalid_response", false);
        }
        if (status >= 300 && status < 400) throw new Refused("redirect_refused", false);
        if (status == 401 || status == 403) return new Result(status, "");
        String type = headers.getOrDefault("content-type", "").toLowerCase(Locale.ROOT);
        if (!type.matches("application/json(;\\s*charset=utf-8)?") || headers.containsKey("transfer-encoding") || headers.containsKey("content-encoding"))
            throw new Refused("unsupported_response", false);
        String length = headers.getOrDefault("content-length", "");
        if (!length.matches("[0-9]{1,8}")) throw new Refused("invalid_response", false);
        int count = Integer.parseInt(length);
        if (count > maximum) throw new Refused("response_too_large", false);
        byte[] bytes = new byte[count];
        for (int offset = 0; offset < count;) {
            if (System.nanoTime() > deadline) throw new Refused("response_timeout", false);
            int got = input.read(bytes, offset, count - offset);
            if (got < 0) throw new Refused("invalid_response", false);
            offset += got;
        }
        try { return new Result(status, StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString(), headers); }
        catch (java.nio.charset.CharacterCodingException error) { throw new Refused("invalid_response", false); }
    }

    private static String line(InputStream input, long deadline) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        int previous = -1;
        while (bytes.size() <= 8192) {
            if (System.nanoTime() > deadline) throw new Refused("response_timeout", false);
            int c = input.read();
            if (c < 0 || c > 127 || (c < 32 && c != 13 && c != 10 && c != 9)) throw new Refused("invalid_response", false);
            if (c == 10) {
                if (previous != 13) throw new Refused("invalid_response", false);
                byte[] all = bytes.toByteArray();
                return new String(all, 0, all.length - 1, StandardCharsets.US_ASCII);
            }
            bytes.write(c); previous = c;
        }
        throw new Refused("invalid_response", false);
    }
}
