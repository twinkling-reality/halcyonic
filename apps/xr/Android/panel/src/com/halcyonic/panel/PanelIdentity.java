package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Locale;
import java.util.Map;

/** Identity is private comparison material, never a request, display string or log entry. */
final class PanelIdentity {
    static final String UUID = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";
    static final String ENTITY = "[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}";
    final String scheme, host, pin, device, credential, key;
    final int port;

    PanelIdentity(String host, int port, String pin, String device, String credential) {
        this.scheme = "wss"; this.host = host(host);
        if (port < 1 || port > 65535 || !pin.matches("[0-9a-f]{64}") || !device.matches(ENTITY)
            || !credential.matches("hlcd_[A-Za-z0-9_-]{1,4096}")) throw PanelJson.invalid();
        this.port = port; this.pin = pin; this.device = device; this.credential = credential;
        key = key("wss", this.host, port, device, pin, credential);
    }

    private PanelIdentity(int port, String token) {
        if (port < 1 || port > 65535 || !token.matches("[A-Za-z0-9_-]{32,4095}")) throw PanelJson.invalid();
        scheme = "ws"; host = "127.0.0.1"; this.port = port; pin = device = null; credential = token;
        key = key(scheme, host, port, null, null, credential);
    }
    static PanelIdentity developmentUsb(int port, String token) { return new PanelIdentity(port, token); }

    static PanelIdentity pairing(String json) {
        Map<String, Object> value = PanelJson.object(PanelJson.parse(json));
        PanelJson.keys(value, "host", "port", "certificate_sha256", "device_id", "credential");
        return new PanelIdentity(PanelJson.text(value.get("host")), (int) PanelJson.integer(value.get("port"), 1, 65535),
            PanelJson.text(value.get("certificate_sha256")), PanelJson.text(value.get("device_id")), PanelJson.text(value.get("credential")));
    }

    static String host(String value) {
        if (value == null) throw PanelJson.invalid();
        if (value.startsWith("[") && value.endsWith("]")) value = value.substring(1, value.length() - 1);
        if (value.isEmpty() || value.length() > 253) throw PanelJson.invalid();
        for (char c : value.toCharArray()) if (c <= 32 || c >= 127 || "/\\?#@%[]".indexOf(c) >= 0) throw PanelJson.invalid();
        return value.toLowerCase(Locale.ROOT);
    }

    static String key(String scheme, String host, int port, String device, String pin, String credential) {
        if (!scheme.equals("ws") && !scheme.equals("wss")) throw PanelJson.invalid();
        String canonical = host(host);
        if (port < 1 || port > 65535 || (scheme.equals("ws") && (!canonical.equals("127.0.0.1") || device != null || pin != null))) throw PanelJson.invalid();
        return hash(PanelJson.encode(Arrays.asList("halcyonic-workspace-v1", scheme, canonical, port, device, pin, credential)).getBytes(StandardCharsets.UTF_8));
    }

    static String hash(byte[] bytes) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256").digest(bytes);
            StringBuilder hex = new StringBuilder(64);
            for (byte b : digest) hex.append(String.format(Locale.ROOT, "%02x", b & 255));
            return hex.toString();
        } catch (java.security.NoSuchAlgorithmException impossible) { throw new IllegalStateException(); }
    }
    boolean same(PanelIdentity other) { return other != null && MessageDigest.isEqual(key.getBytes(StandardCharsets.US_ASCII), other.key.getBytes(StandardCharsets.US_ASCII)); }
}
