package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;

/** Runs the actual plain Java consumers, not a test-only copy of the model or transport. */
public final class PanelSelfTest {
    public static void main(String[] encoded) throws Exception {
        String command = encoded[0]; String[] args = new String[encoded.length - 1];
        for (int i = 1; i < encoded.length; i++) {
            byte[] bytes = Base64.getDecoder().decode(encoded[i]); StringBuilder decoded = new StringBuilder();
            for (int j = 0; j < bytes.length; j += 2) decoded.append((char) ((bytes[j] & 255) | ((bytes[j + 1] & 255) << 8)));
            args[i - 1] = decoded.toString();
        }
        String result;
        switch (command) {
            case "continuation":
                try { PanelContinuation.parse(args[0]); result = "true"; } catch (Exception error) { result = "false"; } break;
            case "identity": {
                Map<String, Object> v = PanelJson.object(PanelJson.parse(args[0]));
                try { result = PanelIdentity.key(PanelJson.text(v.get("scheme")), PanelJson.text(v.get("host")), (int) PanelJson.integer(v.get("port"), 1, 65535), PanelJson.nullableText(v.get("device_id")), PanelJson.nullableText(v.get("certificate_sha256")), PanelJson.text(v.get("credential"))); }
                catch (Exception error) { result = "refused"; } break;
            }
            case "text": result = PanelText.plain(args[0]); break;
            case "command": result = PanelJson.encode(PanelModel.command(args[0], PanelJson.object(PanelJson.parse(args[1])))); break;
            case "model": {
                try {
                    PanelModel model = new PanelModel(args[0]); Map<String, Object> run = model.execution(args[1]);
                    String action = args[2];
                    result = String.valueOf(action.equals("instruction") ? model.instruct(run) : action.equals("interrupt") ? model.interrupt(run)
                        : action.equals("approval") ? model.decision(run, PanelJson.object(PanelJson.parse(args[3])), args[4])
                        : model.answer(run, PanelJson.object(PanelJson.parse(args[3])), PanelJson.array(PanelJson.parse(args[4]))));
                } catch (Exception error) { result = "false"; } break;
            }
            case "drafts": {
                PanelDrafts drafts = PanelDrafts.parse(args[0]);
                try { for (Object item : PanelJson.array(PanelJson.parse(args[1]))) drafts.put(PanelContinuation.parse(PanelJson.encode(item))); result = drafts.json(); }
                catch (Exception error) { result = "refused"; } break;
            }
            case "draft_earlier": {
                PanelDrafts drafts = PanelDrafts.parse(args[0]); List<Object> earlier = new java.util.ArrayList<>();
                for (PanelContinuation entry : drafts.earlier(args[1], args[2], args[3], args[4])) earlier.add(entry.value);
                result = PanelJson.encode(earlier); break;
            }
            case "draft_bound": {
                PanelDrafts drafts = new PanelDrafts(); Map<String, Object> template = PanelJson.object(PanelJson.parse(args[0]));
                String text = String.join("", java.util.Collections.nCopies(32000, "x"));
                int kept = 0;
                for (int i = 0; i < 200; i++) {
                    Map<String, Object> entry = new java.util.LinkedHashMap<>(template);
                    entry.put("execution_id", String.format(java.util.Locale.ROOT, "0192f1a0-0000-7000-8000-%012x", i + 100));
                    entry.put("instruction", PanelJson.map("text", text, "spoken", false, "part", 0L));
                    try { drafts.put(PanelContinuation.parse(PanelJson.encode(entry))); kept++; }
                    catch (IllegalStateException full) {
                        int parsed = PanelJson.array(PanelJson.object(PanelJson.parse(drafts.json())).get("drafts")).size();
                        result = String.valueOf(kept > 16 && parsed == kept && drafts.json().getBytes(StandardCharsets.UTF_8).length <= PanelDrafts.MAXIMUM_BYTES);
                        System.out.println(Base64.getEncoder().encodeToString(result.getBytes(StandardCharsets.UTF_8))); return;
                    }
                }
                throw new AssertionError("The byte boundary was not reached.");
            }
            case "matches": {
                try { result = String.valueOf(PanelContinuation.parse(args[0]).matches(PanelIdentity.pairing(args[1]), new PanelModel(args[2]))); }
                catch (Exception error) { result = "false"; } break;
            }
            case "request_usb":
            case "request": {
                Map<String, Object> local = command.equals("request_usb") ? PanelJson.object(PanelJson.parse(args[0])) : null;
                PanelIdentity target = local == null ? PanelIdentity.pairing(args[0]) : PanelIdentity.developmentUsb((int) PanelJson.integer(local.get("port"), 1, 65535), PanelJson.text(local.get("token"))); AtomicInteger reads = new AtomicInteger();
                PanelTransport transport = new PanelTransport(() -> {
                    if (args.length > 4 && reads.incrementAndGet() >= Integer.parseInt(args[4]))
                        return target.scheme.equals("ws") ? PanelIdentity.developmentUsb(target.port, "rotated_fixture_token_01234567890123456789") : new PanelIdentity(target.host, target.port, target.pin, target.device, "hlcd_rotated_fixture");
                    return target;
                });
                try {
                    PanelTransport.Result response = transport.request(target, args[1], args[2], args[3].equals("null") ? null : args[3]);
                    result = PanelJson.encode(PanelJson.map("status", response.status, "body", response.body));
                } catch (PanelTransport.Refused error) { result = PanelJson.encode(PanelJson.map("code", error.code, "unknown", error.unknownEffect)); }
                break;
            }
            default: throw new IllegalArgumentException("Unknown test.");
        }
        System.out.println(Base64.getEncoder().encodeToString(result.getBytes(StandardCharsets.UTF_8)));
    }
}
