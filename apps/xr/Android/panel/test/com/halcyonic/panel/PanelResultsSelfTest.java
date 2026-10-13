package com.halcyonic.panel;
import java.util.List;
import java.util.Map;

public final class PanelResultsSelfTest {
    private static String id(int n){return String.format(java.util.Locale.ROOT,"00000000-0000-7000-8000-%012x",n);}
    private static void check(boolean value){if(!value)throw new AssertionError("Result boundary failed");}
    private static void invalid(Runnable action){try{action.run();throw new AssertionError("Invalid result accepted");}catch(IllegalArgumentException expected){}}
    private static Map<String,Object> summary(){return PanelJson.map("event_id",id(5),"position",10L,"occurred_at","2026-10-13T00:00:00.000Z","runtime_id","codex-local","epistemic","observed","native_type",null,"kind","command","completion","failed","title","synthetic check","exit_code",2L,"files_total",null,"body_available",true,"retained_complete",true);}
    public static void main(String[] args) throws Exception {
        if(args[0].equals("transport")){
            Map<String,Object> input=PanelJson.object(PanelJson.parse(args[1]));
            PanelIdentity target=PanelIdentity.developmentUsb((int)PanelJson.integer(input.get("port"),1,65535),PanelJson.text(input.get("token")));
            PanelUiState.Context selection=new PanelUiState.Context(target.key,PanelJson.text(input.get("journal_id")),PanelJson.text(input.get("project_id")),PanelJson.text(input.get("workstream_id")),PanelJson.text(input.get("execution_id")));
            PanelTransport transport=new PanelTransport(()->target);
            PanelTransport.Result listResponse=transport.request(target,"GET",PanelToolResults.path(selection,null),null);check(listResponse.status==200);
            PanelToolResults.ListView list=PanelToolResults.list(listResponse.body,selection,PanelJson.text(input.get("runtime_id")));check(list.summaries.size()==1);
            Map<String,Object> chosen=list.summaries.get(0);PanelTransport.Result detailResponse=transport.request(target,"GET",PanelToolResults.path(selection,PanelJson.text(chosen.get("event_id"))),null);check(detailResponse.status==200);
            Map<String,Object> read=PanelToolResults.detail(detailResponse.body,selection,chosen);check(PanelJson.text(PanelJson.object(read.get("output")).get("text")).equals("native producer output\n"));
            System.out.print("passed");return;
        }
        PanelIdentity identity=PanelIdentity.developmentUsb(47831,"a".repeat(64));
        PanelUiState.Context scope=new PanelUiState.Context(identity.key,id(1),id(2),id(3),id(4));
        Map<String,Object> summary=summary();
        Map<String,Object> list=PanelJson.map("journal_id",scope.journal,"project_id",scope.project,"workstream_id",scope.work,"execution_id",scope.execution,"availability","available","total",1L,"earlier",0L,"results",java.util.Arrays.asList(summary));
        Map<String,Object> result=PanelJson.map("kind","command","tool_call_id","tool","turn_id","turn","completion","failed","redaction",PanelJson.map("policy","known_held_values","replacements",0L),"exit_code",2L,"output",PanelJson.map("text","native bytes","availability","available","complete",true,"truncation","none"));
        Map<String,Object> detail=PanelJson.map("journal_id",scope.journal,"project_id",scope.project,"workstream_id",scope.work,"execution_id",scope.execution,"summary",summary,"result",result);
        if(args[0].equals("parser")){
            check(PanelToolResults.list(PanelJson.encode(list),scope,"codex-local").summaries.size()==1);
            check(PanelToolResults.detail(PanelJson.encode(detail),scope,summary).get("exit_code").equals(2L));
            Map<String,Object> reordered=new java.util.LinkedHashMap<>();List<String> keys=new java.util.ArrayList<>(summary.keySet());java.util.Collections.reverse(keys);for(String key:keys)reordered.put(key,summary.get(key));detail.put("summary",reordered);
            check(PanelToolResults.detail(PanelJson.encode(detail),scope,summary).get("exit_code").equals(2L));
            reordered.put("position",11L);invalid(()->PanelToolResults.detail(PanelJson.encode(detail),scope,summary));detail.put("summary",summary);
            invalid(()->PanelToolResults.list(PanelJson.encode(list),scope,"foreign-runtime"));
            invalid(()->PanelToolResults.list(PanelJson.encode(list),new PanelUiState.Context(identity.key,id(9),id(2),id(3),id(4)),"codex-local"));
            list.put("earlier",1L);invalid(()->PanelToolResults.list(PanelJson.encode(list),scope,"codex-local"));list.put("earlier",0L);
            summary.put("epistemic","reported");invalid(()->PanelToolResults.list(PanelJson.encode(list),scope,"codex-local"));summary.put("epistemic","observed");
            summary.put("event_id","00000000-0000-4000-8000-000000000005");invalid(()->PanelToolResults.list(PanelJson.encode(list),scope,"codex-local"));summary.put("event_id",id(5));
            summary.put("runtime_id","invalid/runtime");invalid(()->PanelToolResults.list(PanelJson.encode(list),scope,"codex-local"));summary.put("runtime_id","codex-local");
            result.put("exit_code",0L);invalid(()->PanelToolResults.detail(PanelJson.encode(detail),scope,summary));result.put("exit_code",2L);
            PanelJson.object(result.get("output")).put("complete",false);invalid(()->PanelToolResults.detail(PanelJson.encode(detail),scope,summary));
            check(PanelToolResults.path(scope,id(5)).contains("/results/"+id(5)+"?journal_id="+id(1)));
        }else if(args[0].equals("text")){
            String raw="\n\n  +line  \r\n\t\\n <b>\u001b[31m\u202e😀\n\n";
            String shown="\n\n  +line  ‹U+000D›\n‹U+0009›\\n <b>‹U+001B›[31m‹U+202E›😀\n\n";
            check(String.join("",PanelToolResults.pages(raw)).equals(shown));
            String longLine=" ".repeat(999)+"😀"+"\n\n"+"\\".repeat(1100)+"  ";
            List<String> pages=PanelToolResults.pages(longLine);check(pages.size()==3);check(String.join("",pages).equals(longLine));
            check(PanelToolResults.pages("").size()==1);
        }else if(args[0].equals("lease")){
            for(int scenario=0;scenario<11;scenario++){
                PanelUiState state=new PanelUiState();state.resumed(true);state.top(true);state.activityFocus(true);long owner=state.open();state.dialogFocus(owner,true);
                Object editor=new Object();PanelToolResults.Lease lease=new PanelToolResults.Lease(state,owner,scope,identity,4,editor,"both drafts and pending reference");
                check(lease.current(state,scope,identity,4,editor,"both drafts and pending reference",true));
                PanelUiState.Context changed=new PanelUiState.Context(identity.key,scenario==0?id(8):id(1),scenario==1?id(8):id(2),scenario==2?id(8):id(3),scenario==3?id(8):id(4));
                if(scenario==4){state.dialogFocus(owner,false);state.dialogFocus(owner,true);}if(scenario==5){state.top(false);state.top(true);}if(scenario==6){state.compact(true);state.compact(false);state.projected();}if(scenario==7){state.close(owner);}
                check(!lease.current(state,changed,identity,scenario==8?5:4,scenario==9?new Object():editor,scenario==10?"changed pending":"both drafts and pending reference",true));
                check(!lease.current(state,scope,identity,4,editor,"both drafts and pending reference",true));
            }
        }else throw new AssertionError();
        System.out.print("passed");
    }
}
