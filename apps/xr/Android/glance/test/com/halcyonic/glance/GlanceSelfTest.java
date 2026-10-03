package com.halcyonic.glance;

import java.nio.charset.StandardCharsets;
import java.util.Base64;

/**
 * Prints what the glance's plain-Java parts make of the inputs tooling/glance/glance.test.ts gives
 * it, one result a line, so the test can hold them equal to security.ts's proof and the client
 * core's LabelText. Arguments come as base64 of UTF-16, so any code unit, an unpaired surrogate included, passes through intact.
 * Usage: proof TOKEN ADDRESS CHALLENGE | proves ANSWER TOKEN ADDRESS CHALLENGE | plain TEXT | cut TEXT MOST |
 * token REGULAR OWN MODE CONTENT (REGULAR and OWN 1 or 0, MODE octal) | poll HOST PORT TOKEN, which
 * prints its code, its cause after a space when it has one, a line break and the snapshot it read
 */
public final class GlanceSelfTest {
    public static void main(String[] args) {
        String[] decoded = new String[args.length];
        for (int index = 1; index < args.length; index++) {
            // By hand, unit by unit: a decoder would replace an unpaired surrogate, which is a case.
            byte[] bytes = Base64.getDecoder().decode(args[index]);
            char[] units = new char[bytes.length / 2];
            for (int unit = 0; unit < units.length; unit++) {
                units[unit] = (char) ((bytes[2 * unit] & 0xff) | (bytes[2 * unit + 1] & 0xff) << 8);
            }
            decoded[index] = new String(units);
        }
        String result;
        switch (args[0]) {
            case "proof":
                result = GlanceProof.expected(decoded[1], decoded[2], decoded[3]);
                break;
            case "proves":
                result = String.valueOf(GlanceProof.proves(decoded[1], decoded[2], decoded[3], decoded[4]));
                break;
            case "plain":
                result = GlanceText.plain(decoded[1]);
                break;
            case "cut":
                result = GlanceText.cut(decoded[1], Integer.parseInt(decoded[2]));
                break;
            case "challenge":
                result = GlanceProof.challenge();
                break;
            case "token":
                byte[] content = decoded[4].getBytes(StandardCharsets.ISO_8859_1);
                try {
                    result = GlancePoll.token("1".equals(decoded[1]), "1".equals(decoded[2]), Integer.parseInt(decoded[3], 8), content, content.length);
                } catch (GlancePoll.Refused refused) {
                    result = refused.code;
                }
                break;
            case "poll":
                String token = decoded[3];
                GlancePoll.Result polled = GlancePoll.run(decoded[1], Integer.parseInt(decoded[2]), () -> token);
                result = polled.code + (polled.cause == null ? "" : " " + polled.cause) + "\n" + (polled.snapshot == null ? "" : polled.snapshot);
                break;
            default:
                throw new IllegalArgumentException("unknown " + args[0]);
        }
        System.out.println(Base64.getEncoder().encodeToString(result.getBytes(StandardCharsets.UTF_8)));
    }
}
