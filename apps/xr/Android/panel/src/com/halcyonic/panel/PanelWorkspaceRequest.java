package com.halcyonic.panel;

import java.util.Arrays;
import java.util.Map;

/** Strict consumer of requests.ts. Local references do not authorize a request for another tuple. */
final class PanelWorkspaceRequest {
    static String receiptWords(Map<String,Object> step) {
        String status=PanelJson.text(step.get("status"));
        if(!"confirmed".equals(status))return PanelText.plain(status);
        if("person_response".equals(step.get("tool_name")))return "Direction recorded";
        return step.get("command_id")!=null&&step.get("runtime_status")!=null?"Turn finished\nSoftware changes remain unverified.":"Read confirmed";
    }
    static final String[] STATES = {"running", "waiting_for_human", "waiting_for_runtime", "responded", "stopped", "interrupted", "failed"};
    static Map<String, Object> offer(String json) {
        Map<String, Object> value = PanelJson.object(PanelJson.parse(json));
        if ("available".equals(value.get("availability"))) {
            PanelJson.keys(value, "availability", "model", "served"); text(value.get("model"), 200, false);
            if (!"this_mac".equals(value.get("served"))) throw PanelJson.invalid();
        } else {
            PanelJson.keys(value, "availability", "reason"); if (!"unavailable".equals(value.get("availability"))) throw PanelJson.invalid();
            Map<String, Object> reason = PanelJson.object(value.get("reason")); PanelJson.keys(reason, "code", "message"); code(reason.get("code")); text(reason.get("message"), 2000, false);
        }
        return value;
    }
    static Map<String, Object> read(String json, String id, PanelUiState.Context context) {
        Map<String, Object> response = PanelJson.object(PanelJson.parse(json)); PanelJson.keys(response, "request");
        Map<String, Object> value = PanelJson.object(response.get("request"));
        PanelJson.keys(value, "request_id", "journal_id", "project_id", "workstream_id", "execution_id", "original_text", "status", "reply", "steps", "earlier_steps", "created_at", "updated_at");
        for (String field : new String[] {"request_id", "journal_id", "project_id", "workstream_id", "execution_id"}) PanelContinuation.matches(value.get(field), PanelIdentity.ENTITY);
        if (!id.equals(value.get("request_id")) || !context.journal.equals(value.get("journal_id")) || !context.project.equals(value.get("project_id")) || !context.work.equals(value.get("workstream_id")) || !context.execution.equals(value.get("execution_id"))) throw PanelJson.invalid();
        text(value.get("original_text"), 32000, false); if (!Arrays.asList(STATES).contains(PanelJson.text(value.get("status")))) throw PanelJson.invalid();
        PanelJson.integer(value.get("earlier_steps"), 0, Long.MAX_VALUE); timestamp(value.get("created_at")); timestamp(value.get("updated_at"));
        if (value.get("reply") != null) {
            Map<String, Object> reply = PanelJson.object(value.get("reply")); PanelJson.keys(reply, "text", "epistemic", "model", "served"); text(reply.get("text"), 4000, false); text(reply.get("model"), 200, false);
            if (!"reported".equals(reply.get("epistemic")) || !"this_mac".equals(reply.get("served"))) throw PanelJson.invalid();
        }
        java.util.List<Object> steps = PanelJson.array(value.get("steps")); if (steps.size() > 64) throw PanelJson.invalid();
        for (Object item : steps) {
            Map<String, Object> step = PanelJson.object(item); PanelJson.keys(step, "step_id", "tool_name", "status", "command_id", "command_status", "observation_position", "active_turn_id", "had_active_turn", "observation_after", "observed_turn_id", "runtime_status", "summary");
            PanelContinuation.matches(step.get("step_id"), PanelIdentity.ENTITY); text(step.get("tool_name"), 64, false); text(step.get("summary"), 2000, false);
            if (!Arrays.asList("prepared", "running", "waiting", "confirmed", "rejected", "failed", "unknown").contains(step.get("status"))) throw PanelJson.invalid();
            if (step.get("command_id") != null) PanelContinuation.matches(step.get("command_id"), PanelIdentity.UUID);
            if (step.get("command_status") != null && !Arrays.asList("accepted", "rejected", "completed", "failed").contains(step.get("command_status"))) throw PanelJson.invalid();
            for (String field : new String[] {"active_turn_id", "observed_turn_id"}) if (step.get(field) != null) text(step.get(field), 512, false);
            PanelJson.bool(step.get("had_active_turn")); PanelJson.integer(step.get("observation_position"), 0, Long.MAX_VALUE); PanelJson.integer(step.get("observation_after"), 0, Long.MAX_VALUE);
            if (step.get("runtime_status") != null && !Arrays.asList("starting", "running", "verifying", "waiting_for_human", "completed", "failed", "interrupted", "unknown").contains(step.get("runtime_status"))) throw PanelJson.invalid();
        }
        return value;
    }
    static String text(Object value, int maximum, boolean empty) {
        String text = PanelJson.text(value); if (text.codePointCount(0, text.length()) > maximum || (!empty && text.trim().isEmpty())) throw PanelJson.invalid(); return text;
    }
    static void timestamp(Object value) { String text=PanelJson.text(value);if (!text.matches("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$")) throw PanelJson.invalid();try{java.time.Instant.parse(text);}catch(Exception error){throw PanelJson.invalid();} }
    static void code(Object value) { if (!PanelJson.text(value).matches("^[a-z][a-z0-9_]{0,63}$")) throw PanelJson.invalid(); }
    static final class Recovery {
        final boolean settled; final String requestId, message;
        Recovery(boolean settled, String requestId, String message) { this.settled=settled; this.requestId=requestId; this.message=message; }
    }
    static Recovery command(String json, String pendingId, String currentRequest, PanelUiState.Context context, String expectedType) {
        Map<String,Object> response=PanelJson.object(PanelJson.parse(json)); PanelJson.keys(response,"journal_id","command");
        if(!context.journal.equals(response.get("journal_id")))throw PanelJson.invalid();
        return commandView(PanelJson.object(response.get("command")),pendingId,currentRequest,context,expectedType);
    }
    static Recovery submission(String json,String pendingId,String currentRequest,PanelUiState.Context context,String expectedType){Map<String,Object> response=PanelJson.object(PanelJson.parse(json));PanelJson.keys(response,"disposition","command");if(!Arrays.asList("accepted","rejected","duplicate").contains(response.get("disposition")))throw PanelJson.invalid();return commandView(PanelJson.object(response.get("command")),pendingId,currentRequest,context,expectedType);}
    static Recovery commandView(Map<String,Object> value,String pendingId,String currentRequest,PanelUiState.Context context,String expectedType) {
        PanelJson.keys(value,"command_id","command_type","status","project_id","workstream_id","execution_id","issued_at","updated_at","rejection","failure","result");
        PanelContinuation.matches(value.get("command_id"),PanelIdentity.UUID);
        String type=PanelJson.text(value.get("command_type"));
        if(!pendingId.equals(value.get("command_id")) || !context.project.equals(value.get("project_id")) || !context.work.equals(value.get("workstream_id")) || !context.execution.equals(value.get("execution_id")))throw PanelJson.invalid();
        if(!Arrays.asList("workspace.request.submit","workspace.request.resume","workspace.request.cancel").contains(type) || (expectedType!=null&&!expectedType.equals(type)) || (currentRequest==null&&!type.equals("workspace.request.submit")) || (currentRequest!=null&&type.equals("workspace.request.submit")))throw PanelJson.invalid();
        timestamp(value.get("issued_at"));timestamp(value.get("updated_at"));
        String state=PanelJson.text(value.get("status"));
        if(!Arrays.asList("accepted","completed","rejected","failed").contains(state))throw PanelJson.invalid();
        for(String field:new String[]{"rejection","failure"})if(value.get(field)!=null){Map<String,Object> detail=PanelJson.object(value.get(field));if(field.equals("failure")){PanelJson.keys(detail,"code","message","effect");if(!Arrays.asList("none","unknown").contains(detail.get("effect")))throw PanelJson.invalid();}else PanelJson.keys(detail,"code","message");code(detail.get("code"));text(detail.get("message"),2000,false);}
        if(state.equals("completed")){
            if(value.get("failure")!=null||value.get("rejection")!=null)throw PanelJson.invalid();
            if(type.equals("workspace.request.submit")){Map<String,Object> result=PanelJson.object(value.get("result"));PanelJson.keys(result,"kind","request_id");if(!"workspace_request_created".equals(result.get("kind")))throw PanelJson.invalid();PanelContinuation.matches(result.get("request_id"),PanelIdentity.ENTITY);return new Recovery(true,PanelJson.text(result.get("request_id")),"Your workspace request was recorded. This is not software completion.");}
            if(value.get("result")!=null)throw PanelJson.invalid();return new Recovery(true,currentRequest,type.equals("workspace.request.cancel")?"Request planning stopped. The agent’s work was not stopped.":"Your continuation was recorded. The request may still need work.");
        }
        if(value.get("result")!=null)throw PanelJson.invalid();
        if(state.equals("rejected")){if(value.get("rejection")==null||value.get("failure")!=null)throw PanelJson.invalid();return new Recovery(true,currentRequest,"The workspace command was refused. Your words are retained.");}
        if(state.equals("failed")){Map<String,Object> failure=PanelJson.object(value.get("failure"));if(value.get("rejection")!=null)throw PanelJson.invalid();boolean none="none".equals(failure.get("effect"));return new Recovery(none,currentRequest,none?"The workspace command failed without an effect. Your words are retained.":"The command failed with an unknown effect. Its original ID is retained; no retry is sent.");}
        if(value.get("failure")!=null||value.get("rejection")!=null)throw PanelJson.invalid();return new Recovery(false,currentRequest,"The command was accepted. Waiting for its recorded outcome; no duplicate is sent.");
    }
    static Map<String, Object> draft(String id, String pending, String text, boolean spoken, long part) {
        return PanelJson.map("request_id", id, "pending_command_id", pending, "text", text, "spoken", spoken, "part", part);
    }
    static Map<String,Object> copyWords(Object current,Map<String,Object> source){if(current!=null&&PanelJson.object(current).get("pending_command_id")!=null)throw PanelJson.invalid();return draft(null,null,text(source.get("text"),32000,true),PanelJson.bool(source.get("spoken")),0L);}
}
