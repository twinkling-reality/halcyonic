package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;

/** Runs the actual plain Java consumers, not a test-only copy of the model or transport. */
public final class PanelSelfTest {
    /** A deterministic blocked-write fault after real TLS pin authentication, released by close. */
    private static final class BlockedSocket extends java.net.Socket {
        volatile boolean block;
        final java.util.concurrent.CountDownLatch closed = new java.util.concurrent.CountDownLatch(1);
        BlockedSocket() { super(java.net.Proxy.NO_PROXY); }
        @Override public java.io.OutputStream getOutputStream() throws java.io.IOException {
            java.io.OutputStream real = super.getOutputStream();
            return new java.io.OutputStream() {
                public void write(int value) throws java.io.IOException { write(new byte[] {(byte) value}); }
                public void write(byte[] bytes, int offset, int count) throws java.io.IOException {
                    real.write(bytes, offset, count);
                    if (block) {
                        block = false;
                        try { closed.await(); } catch (InterruptedException error) { Thread.currentThread().interrupt(); }
                        throw new java.net.SocketException("Injected blocked writer closed.");
                    }
                }
                public void flush() throws java.io.IOException { real.flush(); }
            };
        }
        @Override public void close() throws java.io.IOException { try { super.close(); } finally { closed.countDown(); } }
    }
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
            case "viewstore":
                try { PanelViewStore.validate(args[0]); result = "true"; } catch (Exception error) { result = "false"; } break;
            case "viewstore_bound": {
                Map<String, Object> value = PanelJson.object(PanelJson.parse(args[0]));
                List<Object> views = new java.util.ArrayList<>();
                for (int i = 0; i < 150; i++) {
                    Map<String, Object> view = new java.util.LinkedHashMap<>(value);
                    view.put("handoff_id", String.format(java.util.Locale.ROOT, "0192f1a0-0000-4000-8000-%012x", i));
                    view.put("instruction", PanelJson.map("text", String.join("", java.util.Collections.nCopies(32000, "x")), "spoken", false, "part", 0L));
                    views.add(view);
                }
                try { PanelViewStore.validate(PanelJson.encode(PanelJson.map("version", 1L, "selected", null, "views", views))); result = "false"; }
                catch (IllegalArgumentException error) { result = "true"; }
                break;
            }
            case "identity": {
                Map<String, Object> v = PanelJson.object(PanelJson.parse(args[0]));
                try { result = PanelIdentity.key(PanelJson.text(v.get("scheme")), PanelJson.text(v.get("host")), (int) PanelJson.integer(v.get("port"), 1, 65535), PanelJson.nullableText(v.get("device_id")), PanelJson.nullableText(v.get("certificate_sha256")), PanelJson.text(v.get("credential"))); }
                catch (Exception error) { result = "refused"; } break;
            }
            case "status": result = PanelModel.commandStatus(PanelJson.object(PanelJson.parse(args[0]))); break;
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
            case "ui_state": {
                PanelUiState state = new PanelUiState(); state.resumed(true); state.top(true); state.activityFocus(true);
                PanelUiState.Context original = new PanelUiState.Context("key", "journal", "project", "work", "old-execution");
                long owner = state.open(); state.activityFocus(false); state.dialogFocus(owner, true); PanelUiState.Review ticket = state.review(owner, original);
                if (state.authorize(ticket, original)) throw new AssertionError("Unread confirmation authorized.");
                if (!state.read(ticket) || !state.authorize(ticket, original)) throw new AssertionError("Current complete review refused.");
                if (!state.interact(owner, true, true) || state.interact(owner, true, false)) throw new AssertionError("Busy state blocked safe Back or permitted a command.");
                PanelUiState.Context changed = new PanelUiState.Context("key", "journal", "project", "work", "new-execution");
                if (state.authorize(ticket, changed)) throw new AssertionError("Instruction/stop retargeted.");
                state.dialogFocus(owner, false);
                if (state.authorize(ticket, original)) throw new AssertionError("Unfocused dialog authorized.");
                state.dialogFocus(owner, true);
                if (state.authorize(ticket, original) || state.read(ticket)) throw new AssertionError("Old review revived after keyboard/window focus loss.");
                state.close(owner); long next = state.open(); state.dialogFocus(next, true); PanelUiState.Review renewed = state.review(next, original);
                state.read(renewed); state.top(false); state.top(true);
                if (state.authorize(renewed, original)) throw new AssertionError("Multi-resume revived confirmation.");
                state.close(next); long again = state.open(); state.dialogFocus(again, true); PanelUiState.Review finalReview = state.review(again, original); state.read(finalReview);
                if (state.authorize(ticket, original)) throw new AssertionError("Dismissed callback authorized against new modal.");
                state.resumed(false); state.resumed(true);
                if (state.authorize(finalReview, original)) throw new AssertionError("Paused callback revived.");
                result = "true"; break;
            }
            case "restore_state": {
                PanelContinuation disk = PanelContinuation.parse(args[0]); Map<String, Object> edited = new java.util.LinkedHashMap<>(disk.value);
                edited.put("instruction", PanelJson.map("text", "Unsaved live text", "spoken", true, "part", 2L)); PanelContinuation live = PanelContinuation.parse(PanelJson.encode(edited));
                PanelRestore restore = new PanelRestore(); restore.paused(live, false); restore.stage(disk);
                if (!restore.pending().draft().equals("Unsaved live text")) throw new AssertionError("Failed save lost live draft on resume.");
                PanelIdentity identity = PanelIdentity.pairing(args[1]); PanelModel fresh = new PanelModel(args[2]);
                if (restore.fresh(identity, fresh) == null) throw new AssertionError("Unsaved exact context refused by fresh state.");
                restore.paused(live, true); restore.stage(disk);
                if (!restore.pending().draft().equals(disk.draft())) throw new AssertionError("Persisted context not restored.");
                Map<String, Object> newSelection = new java.util.LinkedHashMap<>(disk.value); newSelection.put("execution_id", "0192f1a0-0000-7000-8000-000000000005");
                PanelContinuation incoming = PanelContinuation.parse(PanelJson.encode(newSelection)); restore.stage(incoming);
                if (restore.fresh(identity, fresh) != null) throw new AssertionError("Incoming selection used old cached execution.");
                Map<String, Object> freshJson = PanelJson.object(PanelJson.parse(args[2]));
                PanelJson.object(PanelJson.array(freshJson.get("workstreams")).get(0)).put("current_execution_id", incoming.string("execution_id"));
                PanelJson.object(PanelJson.array(freshJson.get("executions")).get(0)).put("execution_id", incoming.string("execution_id"));
                if (restore.fresh(identity, new PanelModel(PanelJson.encode(freshJson))) == null) throw new AssertionError("New singleTask selection never adopted fresh projection.");
                result = "true"; break;
            }
            case "request_stall":
            case "request_usb":
            case "request": {
                Map<String, Object> local = command.equals("request_usb") ? PanelJson.object(PanelJson.parse(args[0])) : null;
                PanelIdentity target = local == null ? PanelIdentity.pairing(args[0]) : PanelIdentity.developmentUsb((int) PanelJson.integer(local.get("port"), 1, 65535), PanelJson.text(local.get("token"))); AtomicInteger reads = new AtomicInteger();
                BlockedSocket stalled = command.equals("request_stall") ? new BlockedSocket() : null;
                PanelTransport.PairingSource pairingSource = () -> {
                    int count = reads.incrementAndGet();
                    if (stalled != null && count == 3) stalled.block = true;
                    if (args.length > 4 && count >= Integer.parseInt(args[4]))
                        return target.scheme.equals("ws") ? PanelIdentity.developmentUsb(target.port, "rotated_fixture_token_01234567890123456789") : new PanelIdentity(target.host, target.port, target.pin, target.device, "hlcd_rotated_fixture");
                    return target;
                };
                PanelTransport transport = stalled == null ? new PanelTransport(pairingSource) : new PanelTransport(pairingSource, () -> stalled);
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
