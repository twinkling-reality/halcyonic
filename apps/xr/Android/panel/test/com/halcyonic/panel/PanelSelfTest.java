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
            case "wait_state": {
                PanelWait wait = new PanelWait();
                if (wait.waiting() || wait.moves(false, true, true)) throw new AssertionError("Idle wait moved");
                wait.start();
                if (!wait.moves(false, true, true) || wait.moves(true, true, true)
                    || wait.moves(false, false, true) || wait.moves(false, true, false)) throw new AssertionError("Wait motion ignored lifecycle/preferences");
                double early = PanelWait.lift(0.2, 0.3), late = PanelWait.lift(0.2, 0.7);
                if (early <= late || PanelWait.lift(0.5, 0.5) != PanelWait.DEPTH
                    || PanelWait.lift(0, 0) != 0 || PanelWait.lift(1, 1) != 0) throw new AssertionError("Wait band did not travel through text");
                wait.stop();
                if (wait.waiting() || wait.moves(false, true, true)) throw new AssertionError("Completed/canceled wait kept moving");
                wait.start(); wait.stop(); // A later replacement/error and a detach use the same stop path.
                if (wait.moves(false, true, true)) throw new AssertionError("Replacement kept loading");
                if (!PanelWait.still(null) || !PanelWait.still("corrupt") || !PanelWait.still("{\"version\":2,\"still\":false}")
                    || !PanelWait.still(PanelWait.motion(true)) || PanelWait.still(PanelWait.motion(false))) throw new AssertionError("Unknown preference animated");
                for (int x=0;x<=100;x++) for (int t=0;t<=100;t++) {
                    double lift=PanelWait.lift(x/100.0,t/100.0);
                    if (!Double.isFinite(lift) || lift<0 || lift>PanelWait.DEPTH) throw new AssertionError("Unbounded wait colour");
                }
                result="true";break;
            }
            case "voice_state": {
                PanelIdentity target=PanelIdentity.pairing(args[0]); Object editor=new Object();
                PanelUiState.Context context=new PanelUiState.Context(target.key,"journal","project","work","run");
                for(int scenario=0;scenario<17;scenario++) {
                    PanelUiState state=new PanelUiState();state.resumed(true);state.top(true);state.activityFocus(true);
                    PanelVoice.Lease lease=new PanelVoice.Lease(state,context,target,"instruction",editor,"Before",null,null,4);
                    if(!lease.current(state,context,target,"instruction",editor,"Before",null,null,4,true))throw new AssertionError("Current lease refused.");
                    PanelUiState.Context changed=context;
                    if(scenario<5)changed=new PanelUiState.Context(scenario==0?"other":target.key,scenario==1?"other":"journal",scenario==2?"other":"project",scenario==3?"other":"work",scenario==4?"other":"run");
                    if(scenario==11){state.activityFocus(false);state.activityFocus(true);}
                    if(scenario==12){state.top(false);state.top(true);}
                    if(scenario==13){state.resumed(false);state.resumed(true);}
                    if(scenario==14){long owner=state.open();state.close(owner);}
                    if(scenario==15)lease.revoke();
                    PanelIdentity identity=scenario==16?new PanelIdentity(target.host,target.port,target.pin,target.device,"hlcd_changed_fixture"):target;
                    boolean allowed=lease.current(state,changed,identity,scenario==5?"request":"instruction",scenario==6?new Object():editor,scenario==7?"Edited":"Before",scenario==8?"new-request":null,scenario==9?"pending":null,scenario==10?5:4,scenario!=15);
                    if(allowed)throw new AssertionError("Foreign or expired voice lease accepted: "+scenario);
                    if(lease.current(state,context,target,"instruction",editor,"Before",null,null,4,true))throw new AssertionError("Revoked voice lease revived.");
                }
                PanelVoice.Gesture gesture=new PanelVoice.Gesture();
                if(!gesture.down(7,100)||gesture.mature(7,399)||gesture.release(7))throw new AssertionError("Short gesture captured.");
                if(!gesture.down(7,100)||!gesture.mature(7,400)||gesture.mature(7,500)||!gesture.release(7)||gesture.release(7))throw new AssertionError("Held gesture ownership failed.");
                gesture.down(7,100);gesture.mature(7,500);if(gesture.down(8,500)||gesture.release(7))throw new AssertionError("Second pointer retained capture.");
                gesture.down(7,100);gesture.cancel();if(gesture.mature(7,500)||gesture.release(7))throw new AssertionError("Cancelled hold revived.");
                if(!PanelVoice.append("Before","after").equals("Before after")||!PanelVoice.append("Before",null).equals("Before")||!PanelVoice.append("Before"," ").equals("Before"))throw new AssertionError("Draft append changed prior words.");
                try {PanelVoice.append(String.join("",java.util.Collections.nCopies(32000,"x")),"overflow");throw new AssertionError("Draft was truncated or overflowed.");}catch(IllegalArgumentException expected){}
                for(int refusal=0;refusal<2;refusal++) {
                    byte[] rejected=PanelVoice.wav(new short[8000],8000);
                    if(PanelVoice.usableClip(rejected,refusal!=0,refusal==1))throw new AssertionError("Rejected clip survived delivery guard.");
                    for(byte value:rejected)if(value!=0)throw new AssertionError("Rejected clip was not disposed.");
                }
                byte[] accepted=PanelVoice.wav(new short[8000],8000);
                if(!PanelVoice.usableClip(accepted,true,false)||accepted[0]!='R')throw new AssertionError("Current clip was erased before upload.");
                PanelVoice.discard(accepted);for(byte value:accepted)if(value!=0)throw new AssertionError("Finished audio retained.");
                java.util.concurrent.ExecutorService queued=java.util.concurrent.Executors.newSingleThreadExecutor();
                java.util.concurrent.CountDownLatch running=new java.util.concurrent.CountDownLatch(1),hold=new java.util.concurrent.CountDownLatch(1);
                queued.execute(()->{running.countDown();try{hold.await();}catch(InterruptedException stopped){Thread.currentThread().interrupt();}});
                running.await();PanelVoice.Audio owned=new PanelVoice.Audio(PanelVoice.wav(new short[8000],8000));
                java.util.concurrent.atomic.AtomicBoolean uploaded=new java.util.concurrent.atomic.AtomicBoolean();
                queued.execute(()->{try{uploaded.set(true);}finally{owned.discard();}});
                owned.discard();List<Runnable> removed=queued.shutdownNow();hold.countDown();queued.awaitTermination(1,java.util.concurrent.TimeUnit.SECONDS);
                if(removed.size()!=1||uploaded.get())throw new AssertionError("Queued upload unexpectedly ran.");
                for(byte value:owned.bytes)if(value!=0)throw new AssertionError("Never-run queued upload bypassed Activity disposal.");
                result="true";break;
            }
            case "voice_wav": {
                int count=Integer.parseInt(args[0]);short[] samples=new short[Math.max(0,count)];if(count>0)samples[0]=(short)-32768;if(count>1)samples[1]=32767;
                byte[] wav=PanelVoice.wav(samples,count);
                result=wav==null?"null":Base64.getEncoder().encodeToString(wav);break;
            }
            case "voice_transcript":try{String heard=PanelVoice.transcript(args[0]);result=heard==null?"nothing_heard":heard;}catch(Exception error){result="refused";}break;
            case "voice_request":
            case "voice_request_usb": {
                Map<String,Object> usb=command.equals("voice_request_usb")?PanelJson.object(PanelJson.parse(args[0])):null;
                PanelIdentity target=usb==null?PanelIdentity.pairing(args[0]):PanelIdentity.developmentUsb((int)PanelJson.integer(usb.get("port"),1,65535),PanelJson.text(usb.get("token")));
                AtomicInteger reads=new AtomicInteger();
                PanelTransport transport=new PanelTransport(()->{if(args.length>2&&reads.incrementAndGet()>=Integer.parseInt(args[2]))return target.scheme.equals("ws")?PanelIdentity.developmentUsb(target.port,"rotated_fixture_token_01234567890123456789"):new PanelIdentity(target.host,target.port,target.pin,target.device,"hlcd_changed_fixture");return target;});
                int count=Integer.parseInt(args[1]);byte[] wav=PanelVoice.wav(new short[Math.max(0,count)],count);
                if(count>PanelVoice.MAX_SAMPLES)wav=new byte[PanelVoice.MAX_BYTES+1];
                if(args.length>3&&args[3].equals("cancel"))transport.cancel();
                try {PanelTransport.Result response=transport.transcribe(target,wav);result=PanelJson.encode(PanelJson.map("status",response.status,"body",response.body));}
                catch(PanelTransport.Refused error){result=PanelJson.encode(PanelJson.map("code",error.code,"unknown",error.unknownEffect));}break;
            }
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
            case "workspace_offer":try{result=PanelJson.encode(PanelWorkspaceRequest.offer(args[0]));}catch(Exception error){result="refused";}break;
            case "workspace_read":try{Map<String,Object> c=PanelJson.object(PanelJson.parse(args[2]));result=PanelJson.encode(PanelWorkspaceRequest.read(args[0],args[1],new PanelUiState.Context(PanelJson.text(c.get("connection")),PanelJson.text(c.get("journal")),PanelJson.text(c.get("project")),PanelJson.text(c.get("work")),PanelJson.text(c.get("execution")))));}catch(Exception error){result="refused";}break;
            case "workspace_command":try{Map<String,Object> c=PanelJson.object(PanelJson.parse(args[3]));PanelWorkspaceRequest.Recovery r=PanelWorkspaceRequest.command(args[0],args[1],args[2].equals("null")?null:args[2],new PanelUiState.Context(PanelJson.text(c.get("connection")),PanelJson.text(c.get("journal")),PanelJson.text(c.get("project")),PanelJson.text(c.get("work")),PanelJson.text(c.get("execution"))),args[4].equals("null")?null:args[4]);result=PanelJson.encode(PanelJson.map("settled",r.settled,"request_id",r.requestId,"message",r.message));}catch(Exception error){result="refused";}break;
            case "workspace_dispatch":{
                Map<String,Object> envelope=PanelJson.object(PanelJson.parse(args[0]));java.util.concurrent.atomic.AtomicBoolean saved=new java.util.concurrent.atomic.AtomicBoolean();
                java.util.concurrent.atomic.AtomicBoolean unknown=new java.util.concurrent.atomic.AtomicBoolean();
                PanelRequestDispatch.Prepared prepared=PanelRequestDispatch.prepare(envelope,new PanelRequestDispatch.Store(){public boolean persist(String id){if(!id.equals(envelope.get("command_id")))throw new AssertionError("Persisted wrong ID.");saved.set(!args[2].equals("fail"));return saved.get();}public boolean attempted(String id){if(!saved.get())throw new AssertionError("Unknown phase preceded pending persistence.");unknown.set(!args[2].equals("phasefail"));return unknown.get();}});
                if(prepared==null){result="not_sent";break;}if(!saved.get())throw new AssertionError("POST preceded persistence.");
                PanelIdentity target=PanelIdentity.pairing(args[1]);PanelTransport transport=new PanelTransport(()->target);PanelTransport.Result response;
                try{response=prepared.post(transport,target);}catch(PanelTransport.Refused error){if(args[2].equals("phasefail")&&!error.unknownEffect){result="not_sent";break;}throw error;}
                if(!unknown.get())throw new AssertionError("POST preceded durable unknown phase.");
                try{prepared.post(transport,target);throw new AssertionError("Command POST retried.");}catch(PanelTransport.Refused again){if(!again.unknownEffect)throw new AssertionError("Replay lost unknown effect.");}
                if(args.length>3){Map<String,Object> c=PanelJson.object(PanelJson.parse(args[3]));PanelUiState.Context scope=new PanelUiState.Context(target.key,PanelJson.text(c.get("journal")),PanelJson.text(c.get("project")),PanelJson.text(c.get("work")),PanelJson.text(c.get("execution")));PanelWorkspaceRequest.submission(response.body,prepared.id,null,scope,prepared.type);PanelWorkspaceRequest.Recovery recovery=prepared.recover(transport,target,null,scope);result=PanelJson.encode(PanelJson.map("settled",recovery!=null&&recovery.settled,"request_id",recovery==null?null:recovery.requestId));}
                else result=PanelJson.encode(PanelJson.map("status",response.status,"body",response.body,"persisted",saved.get()));break;
            }
            case "workspace_copy":try{result=PanelJson.encode(PanelWorkspaceRequest.copyWords(PanelJson.parse(args[0]),PanelJson.object(PanelJson.parse(args[1]))));}catch(Exception error){result="refused";}break;
            case "directory_sync":{
                for(int failure=0;failure<4;failure++){final int fail=failure;List<String> calls=new java.util.ArrayList<>();boolean returned=false;try{PanelAtomicWrite.commit(new PanelAtomicWrite.Operations(){public void writeTemporary()throws java.io.IOException{calls.add("write_and_file_sync");if(fail==0)throw new java.io.IOException();}public void rename()throws java.io.IOException{calls.add("rename");if(fail==1)throw new java.io.IOException();}public void syncDirectory()throws java.io.IOException{calls.add("directory_sync");if(fail==2)throw new java.io.IOException();}public void cleanup(){calls.add("cleanup");}});returned=true;}catch(java.io.IOException expected){}if(returned!=(failure==3))throw new AssertionError("Incomplete durability returned success.");List<String> wanted=failure==0?java.util.Arrays.asList("write_and_file_sync","cleanup"):failure==1?java.util.Arrays.asList("write_and_file_sync","rename","cleanup"):java.util.Arrays.asList("write_and_file_sync","rename","directory_sync","cleanup");if(!wanted.equals(calls))throw new AssertionError("Durability order changed.");}
                result="true";break;
            }
            case "workspace_phases":{
                Map<String,Object> v=PanelJson.object(PanelJson.parse(args[0]));PanelUiState.Context c=new PanelUiState.Context(PanelJson.text(v.get("connection_key")),PanelJson.text(v.get("journal_id")),PanelJson.text(v.get("project_id")),PanelJson.text(v.get("workstream_id")),PanelJson.text(v.get("execution_id")));
                String id="0192f1a0-0000-4000-8000-000000000010",other="0192f1a0-0000-4000-8000-000000000012";PanelRequestSends ledger=new PanelRequestSends();ledger.stage(id,c,null,"workspace.request.submit");String durable=ledger.json();
                for(int failure=0;failure<4;failure++){
                    final int failedWrite=failure;String[] stores=new String[3];
                    Map<String,Object> envelope=PanelModel.command("workspace.request.submit",PanelJson.map("project_id",c.project,"workstream_id",c.work,"execution_id",c.execution,"text","Synthetic request."));envelope.put("command_id",id);
                    PanelRequestDispatch.Prepared proof=PanelRequestDispatch.prepare(envelope,new PanelRequestDispatch.Store(){
                        public boolean persist(String command){PanelRequestSends saved=new PanelRequestSends();saved.stage(command,c,null,"workspace.request.submit");if(failedWrite==0)return false;stores[0]=saved.json();Map<String,Object> selection=new java.util.LinkedHashMap<>(v);selection.put("request",PanelWorkspaceRequest.draft(null,command,"Synthetic request.",false,0L));PanelContinuation pending=PanelContinuation.parse(PanelJson.encode(selection));PanelDrafts drafts=new PanelDrafts();drafts.put(pending);if(failedWrite==1)return false;stores[1]=drafts.json();if(failedWrite==2)return false;stores[2]=pending.json();return true;}
                        public boolean attempted(String command){PanelRequestSends saved=PanelRequestSends.parse(stores[0]);saved.attempted(command,c,null,"workspace.request.submit");if(failedWrite==3)return false;stores[0]=saved.json();return true;}
                    });
                    if(failure<3&&proof!=null)throw new AssertionError("Partial pending store authorized POST.");
                    if(failure==3){PanelIdentity target=PanelIdentity.pairing(args[1]);try{proof.post(new PanelTransport(()->target),target);throw new AssertionError("Failed phase transition sent POST.");}catch(PanelTransport.Refused expected){if(expected.unknownEffect)throw new AssertionError("Known failed phase became attempted.");}}
                    if(stores[0]!=null){PanelRequestSends recovered=PanelRequestSends.parse(stores[0]);if(!recovered.unsent(id,c,null))throw new AssertionError("Partial store lost unsent proof.");if(stores[1]!=null){PanelDrafts restored=PanelDrafts.parse(stores[1]);PanelContinuation selected=restored.find(c.connection,c.journal,c.work,c.execution);if(!id.equals(PanelJson.object(selected.value.get("request")).get("pending_command_id")))throw new AssertionError("Partial ordinary store lost original ID.");}
                        PanelUiState.Context foreign=new PanelUiState.Context(c.connection,c.journal,c.project,c.work,"0192f1a0-0000-7000-8000-000000000099");if(recovered.unsent(id,foreign,null)||recovered.unsent(id,c,"0192f1a0-0000-7000-8000-000000000008"))throw new AssertionError("Unsent cleanup crossed binding.");
                        recovered.removeUnsent(id,c,null);recovered.stage(other,c,null,"workspace.request.submit");if(!recovered.unsent(other,c,null))throw new AssertionError("Explicit new send unavailable after durable cleanup.");}
                }
                PanelRequestSends attempted=PanelRequestSends.parse(durable);attempted.attempted(id,c,null,"workspace.request.submit");PanelRequestSends restarted=PanelRequestSends.parse(attempted.json());if(restarted.unsent(id,c,null))throw new AssertionError("Unknown phase downgraded after restart.");try{restarted.removeUnsent(id,c,null);throw new AssertionError("Unknown request discarded.");}catch(IllegalArgumentException expected){}try{restarted.stage(id,c,null,"workspace.request.submit");throw new AssertionError("Duplicate pending identity replaced.");}catch(IllegalArgumentException expected){}
                if(new PanelRequestSends().unsent(id,c,null))throw new AssertionError("Missing phase granted cleanup.");result="true";break;
            }
            case "input_text": result=String.valueOf(PanelInputText.acceptedEnd(args[0],0,args[0].length(),args[1],Integer.parseInt(args[2]),Integer.parseInt(args[3]),32000)); break;
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
            case "request_copy": {
                PanelContinuation saved = PanelContinuation.parse(args[0]);
                result = PanelJson.encode(saved.requestFor(args[1], args[2], args[3], args[4], args[5])); break;
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
            case "monitor": {
                try { result = PanelJson.encode(new PanelModel(args[0]).monitor(args[1], args[2])); }
                catch (Exception error) { result = "refused"; } break;
            }
            case "connection_recovery": {
                PanelUiState state = new PanelUiState(); state.resumed(true); state.top(true); state.activityFocus(true);
                state.awaitingProjection();
                if (!state.connectionInteract(0) || state.interact(0) || state.commandsAllowed()) throw new AssertionError("Disconnected connection recovery unavailable or granted actions.");
                long owner = state.open(); state.activityFocus(false); state.dialogFocus(owner, true);
                if (!state.connectionInteract(owner) || state.connectionInteract(0) || state.interact(owner) || state.commandsAllowed()) throw new AssertionError("Connection dialog lost local authority or gained command authority.");
                state.dialogFocus(owner, false);
                if (state.connectionInteract(owner)) throw new AssertionError("Unfocused connection dialog acted.");
                state.dialogFocus(owner, true); state.top(false);
                if (state.connectionInteract(owner)) throw new AssertionError("Non-top connection dialog acted.");
                state.top(true); state.resumed(false);
                if (state.connectionInteract(owner)) throw new AssertionError("Paused connection dialog acted.");
                state.resumed(true); state.close(owner); long next = state.open(); state.dialogFocus(next, true);
                if (state.connectionInteract(owner) || !state.connectionInteract(next)) throw new AssertionError("Stale connection dialog retained authority.");
                state.compact(true);
                if (state.connectionInteract(next)) throw new AssertionError("Compact monitor gained connection actions.");
                result = "true"; break;
            }
            case "navigation_refresh": {
                PanelUiState state = new PanelUiState(); state.resumed(true); state.top(true); state.activityFocus(true);
                final int[] polls = {0}; Runnable actualContinuation = () -> polls[0]++;
                if (!state.navigated(actualContinuation) || polls[0] != 1 || state.controlsReady() || state.commandsAllowed()) throw new AssertionError("Selected navigation did not restart its bounded fresh read before actions.");
                state.projected();
                if (!state.commandsAllowed()) throw new AssertionError("Fresh selected projection did not offer controls.");
                state.changePresentation(true, true); state.top(false); state.activityFocus(false);
                if (!state.navigated(actualContinuation) || polls[0] != 2 || state.commandsAllowed()) throw new AssertionError("Compact non-top navigation lost read-only refresh or granted actions.");
                state.resumed(false);
                if (state.navigated(actualContinuation) || polls[0] != 2) throw new AssertionError("Paused navigation restarted a read.");
                result = "true"; break;
            }
            case "preview_resume": {
                PanelUiState state = new PanelUiState(); state.resumed(true); state.top(true); state.activityFocus(true);
                state.awaitingProjection();
                if (state.interact(0)) throw new AssertionError("Preview resumed before its immutable projection was offered.");
                state.projected();
                if (!state.interact(0)) throw new AssertionError("Selected expanded preview remained inert after immutable projection restore.");
                state.resumed(false); state.resumed(true); state.awaitingProjection(); state.projected();
                if (!state.interact(0)) throw new AssertionError("Same selected preview could not resume again.");
                result = "true"; break;
            }
            case "compact_ui": {
                PanelUiState state = new PanelUiState(); state.resumed(true); state.top(true); state.activityFocus(true);
                PanelUiState.Context scope = new PanelUiState.Context("key", "journal", "project", "work", "run");
                long owner = state.open(); state.dialogFocus(owner, true); PanelUiState.Review old = state.review(owner, scope); state.read(old);
                if (state.changePresentation(true, false) || !state.authorize(old, scope)) throw new AssertionError("Failed save changed the current mode or review.");
                state.close(owner);
                if (!state.changePresentation(true, true) || !state.compact() || state.commandsAllowed() || state.interact(0)) throw new AssertionError("Compact mode kept action authority.");
                if (state.read(old) || state.authorize(old, scope)) throw new AssertionError("Collapse preserved old review authority.");
                state.top(false); state.activityFocus(false);
                if (!state.monitor() || !state.readProjection() || state.navigate() || state.commandsAllowed()) throw new AssertionError("Non-top monitor gained input or lost its read-only eligibility.");
                state.resumed(false);
                if (state.monitor() || state.readProjection() || state.commandsAllowed()) throw new AssertionError("Paused monitor retained a read/command lease.");
                state.resumed(true); state.top(true); state.activityFocus(true);
                if (!state.navigate() || state.commandsAllowed()) throw new AssertionError("Compact resume granted controls.");
                if (state.changePresentation(false, false) || !state.compact()) throw new AssertionError("Failed expand save changed mode.");
                if (!state.changePresentation(false, true) || state.controlsReady() || state.commandsAllowed() || state.interact(0)) throw new AssertionError("Expand did not wait for a fresh projection.");
                state.projected();
                if (!state.controlsReady() || !state.commandsAllowed() || !state.interact(0) || state.authorize(old, scope)) throw new AssertionError("Fresh expand lacked new controls or revived the old review.");
                state.top(false);
                if (state.readProjection() || state.commandsAllowed()) throw new AssertionError("Expanded non-top state gained monitoring or command authority.");
                result = "true"; break;
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
