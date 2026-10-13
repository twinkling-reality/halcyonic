package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/** Read-only recorded evidence, never a build/test certificate or current-file reader. */
final class PanelToolResults {
    static final class ListView {
        final List<Map<String,Object>> summaries; final long total, earlier; final String availability;
        ListView(List<Map<String,Object>> summaries,long total,long earlier,String availability){this.summaries=summaries;this.total=total;this.earlier=earlier;this.availability=availability;}
    }
    static final class Lease {
        final PanelUiState.Context context;
        final PanelIdentity identity;
        final int generation;
        private final PanelUiState.Review ticket;
        private final Object editor;
        private final String words;
        private boolean revoked;
        Lease(PanelUiState authority, long owner, PanelUiState.Context context, PanelIdentity identity, int generation, Object editor, String words) {
            this.context=context;this.identity=identity;this.generation=generation;this.editor=editor;this.words=words;
            ticket=authority.review(owner,context);
        }
        void revoke(){revoked=true;}
        boolean current(PanelUiState authority, PanelUiState.Context context, PanelIdentity identity, int generation, Object editor, String words, boolean eligible) {
            boolean valid=!revoked && eligible && this.identity.same(identity) && this.generation==generation && this.editor==editor && Objects.equals(this.words,words) && authority.current(ticket,context);
            if(!valid)revoke();return valid;
        }
    }
    static String path(PanelUiState.Context scope, String event) {
        for(String id:new String[]{scope.journal,scope.project,scope.work,scope.execution})PanelContinuation.matches(id,PanelIdentity.ENTITY);
        if(event!=null)PanelContinuation.matches(event,PanelIdentity.ENTITY);
        return "/api/executions/"+scope.execution+"/results"+(event==null?"":"/"+event)+"?journal_id="+scope.journal+"&project_id="+scope.project+"&workstream_id="+scope.work;
    }
    static void scope(Map<String,Object> value, PanelUiState.Context scope) {
        if(!scope.journal.equals(value.get("journal_id")) || !scope.project.equals(value.get("project_id")) || !scope.work.equals(value.get("workstream_id")) || !scope.execution.equals(value.get("execution_id")))throw PanelJson.invalid();
    }
    static ListView list(String json,PanelUiState.Context scope,String runtime) {
        Map<String,Object> value=PanelJson.object(PanelJson.parse(json));
        PanelJson.keys(value,"journal_id","project_id","workstream_id","execution_id","availability","total","earlier","results");scope(value,scope);
        choice(value.get("availability"),"available","unsupported","unknown","unavailable");
        long total=PanelJson.integer(value.get("total"),0,9007199254740991L),earlier=PanelJson.integer(value.get("earlier"),0,total);
        List<Object> raw=PanelJson.array(value.get("results"));if(raw.size()>16 || total-earlier!=raw.size())throw PanelJson.invalid();
        List<Map<String,Object>> results=new ArrayList<>();long prior=Long.MAX_VALUE;
        for(Object item:raw){Map<String,Object> summary=summary(item);if(!runtime.equals(summary.get("runtime_id")))throw PanelJson.invalid();long position=PanelJson.integer(summary.get("position"),1,9007199254740991L);if(position>=prior)throw PanelJson.invalid();prior=position;for(Map<String,Object> previous:results)if(previous.get("event_id").equals(summary.get("event_id")))throw PanelJson.invalid();results.add(summary);}
        return new ListView(results,total,earlier,PanelJson.text(value.get("availability")));
    }
    static Map<String,Object> summary(Object raw) {
        Map<String,Object> value=PanelJson.object(raw);
        PanelJson.keys(value,"event_id","position","occurred_at","runtime_id","epistemic","native_type","kind","completion","title","exit_code","files_total","body_available","retained_complete");
        PanelContinuation.matches(PanelJson.text(value.get("event_id")),PanelIdentity.ENTITY);PanelJson.integer(value.get("position"),1,9007199254740991L);
        bounded(value.get("occurred_at"),32,false);PanelContinuation.matches(PanelJson.text(value.get("occurred_at")),"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z");PanelContinuation.matches(PanelJson.text(value.get("runtime_id")),"[a-z][a-z0-9-]{0,62}");bounded(value.get("native_type"),128,true);bounded(value.get("title"),180,true);
        choice(value.get("epistemic"),"observed");choice(value.get("kind"),"command","file_changes");completion(value.get("completion"));
        number(value.get("exit_code"),-2147483648L,2147483647L);number(value.get("files_total"),0,9007199254740991L);PanelJson.bool(value.get("body_available"));PanelJson.bool(value.get("retained_complete"));
        if("command".equals(value.get("kind"))?value.get("files_total")!=null:value.get("exit_code")!=null)throw PanelJson.invalid();return value;
    }
    static Map<String,Object> detail(String json,PanelUiState.Context scope,Map<String,Object> expected) {
        Map<String,Object> value=PanelJson.object(PanelJson.parse(json));PanelJson.keys(value,"journal_id","project_id","workstream_id","execution_id","summary","result");scope(value,scope);
        Map<String,Object> summary=summary(value.get("summary"));if(!Objects.equals(summary,expected))throw PanelJson.invalid();
        Map<String,Object> result=PanelJson.object(value.get("result"));
        boolean command="command".equals(result.get("kind"));
        if(command)PanelJson.keys(result,"kind","tool_call_id","turn_id","completion","redaction","exit_code","output");else PanelJson.keys(result,"kind","tool_call_id","turn_id","completion","redaction","files","files_total","list_complete");
        if(!summary.get("kind").equals(result.get("kind")) || !summary.get("completion").equals(result.get("completion")))throw PanelJson.invalid();
        bounded(result.get("tool_call_id"),512,false);bounded(result.get("turn_id"),512,true);completion(result.get("completion"));
        Map<String,Object> redaction=PanelJson.object(result.get("redaction"));PanelJson.keys(redaction,"policy","replacements");choice(redaction.get("policy"),"known_held_values");PanelJson.integer(redaction.get("replacements"),0,9007199254740991L);
        boolean available=false,complete=true;
        if(command){number(result.get("exit_code"),-2147483648L,2147483647L);if(!Objects.equals(summary.get("exit_code"),result.get("exit_code")))throw PanelJson.invalid();Map<String,Object> body=body(result.get("output"));available="available".equals(body.get("availability"));complete=PanelJson.bool(body.get("complete"));}
        else {
            choice(result.get("kind"),"file_changes");List<Object> files=PanelJson.array(result.get("files"));long count=PanelJson.integer(result.get("files_total"),files.size(),9007199254740991L);complete=PanelJson.bool(result.get("list_complete"));if(files.size()>32 || complete && count!=files.size() || !Objects.equals(summary.get("files_total"),result.get("files_total")))throw PanelJson.invalid();
            for(Object item:files){Map<String,Object> file=PanelJson.object(item);PanelJson.keys(file,"kind","path","path_scope","path_complete","move_path","move_path_scope","move_path_complete","patch");choice(file.get("kind"),"add","update","delete");pathMetadata(file,false);pathMetadata(file,true);Map<String,Object> body=body(file.get("patch"));if(!"project_relative".equals(file.get("path_scope")) || file.get("move_path_scope")!=null && !"project_relative".equals(file.get("move_path_scope")))if(body.get("text")!=null)throw PanelJson.invalid();available|="available".equals(body.get("availability"));complete&=PanelJson.bool(body.get("complete")) && PanelJson.bool(file.get("path_complete")) && (file.get("move_path_complete")==null || PanelJson.bool(file.get("move_path_complete")));}
        }
        if(available!=PanelJson.bool(summary.get("body_available")) || complete!=PanelJson.bool(summary.get("retained_complete")) || PanelJson.encode(result).getBytes(StandardCharsets.UTF_8).length>65536)throw PanelJson.invalid();
        return result;
    }
    private static void pathMetadata(Map<String,Object> file,boolean move){String prefix=move?"move_path":"path";Object scope=file.get(prefix+"_scope"),value=file.get(prefix),complete=file.get(prefix+"_complete");bounded(value,1024,true);if(move && scope==null){if(value!=null || complete!=null)throw PanelJson.invalid();return;}choice(scope,"project_relative","outside_project","unknown");PanelJson.bool(complete);if("project_relative".equals(scope)!=(value!=null))throw PanelJson.invalid();}
    static Map<String,Object> body(Object raw){Map<String,Object> body=PanelJson.object(raw);PanelJson.keys(body,"text","availability","complete","truncation");bounded(body.get("text"),4096,true);choice(body.get("availability"),"available","absent","source_over_limit","omitted_scope","malformed");choice(body.get("truncation"),"none","body_bound","aggregate_bound","source_limit","scope","absent","malformed");boolean complete=PanelJson.bool(body.get("complete"));if("available".equals(body.get("availability"))!=(body.get("text")!=null) || complete && (body.get("text")==null || !"none".equals(body.get("truncation"))) || body.get("text")!=null && PanelJson.text(body.get("text")).getBytes(StandardCharsets.UTF_8).length>16384)throw PanelJson.invalid();return body;}
    private static void completion(Object value){choice(value,"completed","failed","declined","interrupted","unknown");}
    private static void number(Object value,long min,long max){if(value!=null)PanelJson.integer(value,min,max);}
    private static void bounded(Object value,int maximum,boolean nullable){if(value==null){if(!nullable)throw PanelJson.invalid();return;}String text=PanelJson.text(value);if(text.codePointCount(0,text.length())>maximum || !nullable && text.isEmpty())throw PanelJson.invalid();}
    private static void choice(Object value,String... choices){for(String choice:choices)if(choice.equals(value))return;throw PanelJson.invalid();}
    static String label(Map<String,Object> summary){return ("command".equals(summary.get("kind"))?"Command":"File changes")+" · "+PanelJson.text(summary.get("completion"))+(summary.get("exit_code")==null?"":" · exit "+summary.get("exit_code"))+(summary.get("files_total")==null?"":" · "+summary.get("files_total")+" files")+(PanelJson.bool(summary.get("retained_complete"))?"":" · content missing or shortened")+"\n"+PanelJson.text(summary.get("occurred_at"));}
    static String note(Map<String,Object> body){String reason=PanelJson.text(body.get("truncation"));if(reason.equals("aggregate_bound"))return "Content omitted because this recorded result reached its size limit.";if(reason.equals("body_bound"))return "Recorded content was shortened.";if(reason.equals("source_limit"))return "Content omitted because the output exceeded the recording limit.";if(reason.equals("scope"))return "Patch omitted because its path could not be placed within the project.";if(reason.equals("malformed"))return "This content could not be read.";if(body.get("text")==null)return "No content was recorded.";if(!PanelJson.bool(body.get("complete")))return "Whole source content was not confirmed.";return "Recorded content; current files are not verified.";}
    static List<String> pages(String raw){List<String> pages=new ArrayList<>();for(int start=0;start<raw.length();){int count=Math.min(1000,raw.codePointCount(start,raw.length()));int end=raw.offsetByCodePoints(start,count);pages.add(PanelText.recorded(raw.substring(start,end)));start=end;}if(pages.isEmpty())pages.add("");return pages;}
}
