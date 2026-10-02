package com.halcyonic.glance;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * The loopback proof, as the control plane's security.ts makes it: before any request that carries
 * the access token, the glance sends a fresh 32-byte challenge to the public health check, and the
 * control plane answers with an HMAC-SHA256 under the token of a fixed label, the address and port
 * the connection reached, and the challenge. Only something that holds the token can answer, and a
 * listener on another address or port that relays the challenge gets a proof for the wrong address.
 * Plain Java, so a test on the Mac holds it equal to the TypeScript one.
 */
public final class GlanceProof {
    /** The label security.ts proves under; any change there must change here. */
    static final String LABEL = "halcyonic loopback proof v2\n";

    private static final SecureRandom RANDOM = new SecureRandom();

    private GlanceProof() {}

    /** A fresh challenge: 32 random bytes as 64 lowercase hex digits. */
    public static String challenge() {
        byte[] bytes = new byte[32];
        RANDOM.nextBytes(bytes);
        return hex(bytes);
    }

    /** The proof a control plane holding <code>token</code> gives for <code>address</code> and <code>challenge</code>. */
    public static String expected(String token, String address, String challenge) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(token.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            mac.update(LABEL.getBytes(StandardCharsets.UTF_8));
            mac.update((address + "\n").getBytes(StandardCharsets.UTF_8));
            mac.update(challenge.getBytes(StandardCharsets.UTF_8));
            return hex(mac.doFinal());
        } catch (java.security.GeneralSecurityException error) {
            throw new IllegalStateException("HmacSHA256 is unavailable", error);
        }
    }

    /**
     * Whether <code>answered</code>, the control plane's proof header, proves it holds the token: 64
     * lowercase hex digits equal to the expected proof, compared in constant time.
     */
    public static boolean proves(String answered, String token, String address, String challenge) {
        if (answered == null || !answered.matches("[0-9a-f]{64}")) return false;
        byte[] expected = expected(token, address, challenge).getBytes(StandardCharsets.US_ASCII);
        return MessageDigest.isEqual(answered.getBytes(StandardCharsets.US_ASCII), expected);
    }

    private static String hex(byte[] bytes) {
        StringBuilder text = new StringBuilder(bytes.length * 2);
        for (byte each : bytes) text.append(String.format(java.util.Locale.ROOT, "%02x", each & 0xff));
        return text.toString();
    }
}
