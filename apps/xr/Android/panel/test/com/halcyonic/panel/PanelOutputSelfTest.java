package com.halcyonic.panel;

import java.util.ArrayList;
import java.util.List;

/** Synthetic JVM witnesses for the helpers consumed by the native project output controls. */
public final class PanelOutputSelfTest {
    private static final String KEY = String.join("", java.util.Collections.nCopies(64, "a"));
    private static String id(int number) { return String.format(java.util.Locale.ROOT, "00000000-0000-7000-8000-%012x", number); }
    private static void require(boolean test) { if (!test) throw new AssertionError("Project output boundary failed."); }
    private static void invalid(Runnable action) { try { action.run(); throw new AssertionError("Invalid record accepted."); } catch (IllegalArgumentException expected) { } }
    public static void main(String[] args) {
        if (args.length != 1) throw new AssertionError();
        PanelProjectOutput.Scope scope = new PanelProjectOutput.Scope(KEY, id(1), id(2));
        PanelProjectOutput.Component app = new PanelProjectOutput.Component("example.synthetic", "example.synthetic.Launcher");
        PanelProjectOutput.Component second = new PanelProjectOutput.Component("example.other", "example.other.Launcher");
        switch (args[0]) {
            case "store": {
                PanelProjectOutput empty = new PanelProjectOutput(), one = empty.change(scope, null, app);
                require(empty.find(scope) == null && one.find(scope).component.same(app));
                require(PanelProjectOutput.parse(one.json()).find(scope).component.same(app));
                for (PanelProjectOutput.Scope other : new PanelProjectOutput.Scope[] {
                    new PanelProjectOutput.Scope(KEY.replace('a','b'), id(1), id(2)),
                    new PanelProjectOutput.Scope(KEY, id(3), id(2)), new PanelProjectOutput.Scope(KEY,id(1),id(4)) }) require(one.find(other) == null);
                PanelProjectOutput.Scope other = new PanelProjectOutput.Scope(KEY,id(1),id(5));
                PanelProjectOutput two = one.change(other,null,second), removed = two.change(scope,app,null);
                require(removed.find(scope) == null && removed.find(other).component.same(second));
                invalid(() -> two.change(scope,second,null)); require(two.find(scope).component.same(app));
                String entry = PanelJson.encode(one.find(scope).value());
                invalid(() -> PanelProjectOutput.parse("{\"version\":1,\"links\":["+entry+","+entry+"]}"));
                invalid(() -> PanelProjectOutput.parse(one.json().replace("\"activity\":", "\"token\":\"fixture\",\"activity\":")));
                invalid(() -> PanelProjectOutput.parse("{\"version\":2,\"links\":[]}"));
                invalid(() -> PanelProjectOutput.parse("{\"version\":1,\"links\":null}"));
                invalid(() -> new PanelProjectOutput.Component("example.invalid/secret", "example.Valid"));
                invalid(() -> new PanelProjectOutput.Component("example.valid", "example.\u200cInvalid"));
                PanelProjectOutput full = empty;
                for(int i=0;i<PanelProjectOutput.MAXIMUM_LINKS;i++)full=full.change(new PanelProjectOutput.Scope(KEY,id(1),id(10+i)),null,app);
                final PanelProjectOutput before=full;
                invalid(() -> before.change(new PanelProjectOutput.Scope(KEY,id(1),id(1000)),null,app));
                require(before.find(new PanelProjectOutput.Scope(KEY,id(1),id(10))) != null);
                PanelProjectOutput.Component longName=new PanelProjectOutput.Component("a".repeat(500),"b".repeat(500));
                List<Object> longEntries=new ArrayList<>();for(int i=0;i<100;i++)longEntries.add(new PanelProjectOutput.Link(new PanelProjectOutput.Scope(KEY,id(1),id(100+i)),longName).value());
                invalid(() -> PanelProjectOutput.parse(PanelJson.encode(PanelJson.map("version",1L,"links",longEntries))));
                break;
            }
            case "lease": {
                for(int scenario=0;scenario<15;scenario++) {
                    PanelUiState state=new PanelUiState();state.resumed(true);state.top(true);state.activityFocus(true);
                    Object editor=new Object();PanelUiState.Context original=new PanelUiState.Context(KEY,id(1),id(2),id(6),id(7));
                    PanelProjectOutput.Lease lease=new PanelProjectOutput.Lease(state,0,original,scope,app,4,editor,"Existing draft");
                    require(lease.current(state,original,scope,app,4,editor,"Existing draft",true));
                    PanelUiState.Context changed=new PanelUiState.Context(scenario==0?KEY.replace('a','b'):KEY,scenario==1?id(8):id(1),scenario==2?id(8):id(2),scenario==3?id(8):id(6),scenario==4?id(8):id(7));
                    if(scenario==5){state.activityFocus(false);state.activityFocus(true);}
                    if(scenario==6){state.top(false);state.top(true);}
                    if(scenario==7){long owner=state.open();state.close(owner);}
                    if(scenario==8){state.resumed(false);state.resumed(true);}
                    if(scenario==13){state.compact(true);state.compact(false);state.projected();}
                    if(scenario==14){state.awaitingProjection();state.projected();}
                    require(!lease.current(state,changed,scope,scenario==9?second:app,scenario==10?5:4,scenario==11?new Object():editor,scenario==12?"Edited": "Existing draft",true));
                    require(!lease.current(state,original,scope,app,4,editor,"Existing draft",true));
                }
                PanelUiState state=new PanelUiState();state.resumed(true);state.top(true);state.activityFocus(true);
                PanelUiState.Context projectOnly=new PanelUiState.Context(KEY,id(1),id(2),null,null);
                PanelProjectOutput.Lease lease=new PanelProjectOutput.Lease(state,0,projectOnly,scope,null,1,null,null);
                require(lease.current(state,projectOnly,scope,null,1,null,null,true));
                require(!lease.current(state,projectOnly,scope,null,1,null,null,false));
                require(!lease.current(state,projectOnly,scope,null,1,null,null,true));
                break;
            }
            case "selection": {
                PanelIdentity target = PanelIdentity.developmentUsb(8710, "t".repeat(32));
                java.util.Map<String,Object> value = PanelJson.map("journal", PanelJson.map("journal_id", id(1), "origin", "fixture"), "position", 1L,
                    "projects", java.util.Arrays.asList(PanelJson.map("project_id", id(2))),
                    "workstreams", java.util.Arrays.asList(PanelJson.map("workstream_id", id(6), "project_id", id(2), "current_execution_id", id(7))),
                    "executions", java.util.Arrays.asList(PanelJson.map("execution_id", id(7), "workstream_id", id(6), "project_id", id(2))),
                    "runtimes", new ArrayList<>(), "commands", new ArrayList<>());
                PanelModel model = new PanelModel(PanelJson.encode(value));
                PanelUiState.Context original = new PanelUiState.Context(target.key, id(1), id(2), id(6), id(7));
                require(PanelProjectOutput.selected(original,target,model));
                require(PanelProjectOutput.selected(new PanelUiState.Context(target.key,id(1),id(2),null,null),target,model));
                for (int field=0;field<5;field++) require(!PanelProjectOutput.selected(new PanelUiState.Context(field==0?KEY:target.key,field==1?id(8):id(1),field==2?id(8):id(2),field==3?id(8):id(6),field==4?id(8):id(7)),target,model));
                require(!PanelProjectOutput.selected(original,PanelIdentity.developmentUsb(8710,"x".repeat(32)),model));
                require(!PanelProjectOutput.selected(new PanelUiState.Context(target.key,id(1),id(2),null,id(7)),target,model));
                model.workstreams.get(id(6)).put("current_execution_id", null);
                require(!PanelProjectOutput.selected(original,target,model));
                require(PanelProjectOutput.selected(new PanelUiState.Context(target.key,id(1),id(2),id(6),null),target,model));
                model.workstreams.get(id(6)).put("project_id",id(8));
                require(!PanelProjectOutput.selected(new PanelUiState.Context(target.key,id(1),id(2),id(6),null),target,model));
                break;
            }
            case "open": {
                for(int failure=0;failure<6;failure++) {
                    final int scenario=failure;List<String> seen=new ArrayList<>();int[] currents={0};
                    PanelProjectOutput.OpenResult result=PanelProjectOutput.open(new PanelProjectOutput.OpenOperations(){
                        public boolean current(){seen.add("current");return ++currents[0] != (scenario==1?1:scenario==2?2:scenario==3?3:-1);}
                        public boolean available(){seen.add("available");return scenario!=4;}
                        public boolean preserveDrafts(){seen.add("persist");return scenario!=5;}
                        public void start(){seen.add("start");}
                    });
                    require(result==(scenario==0?PanelProjectOutput.OpenResult.REQUESTED:scenario==4?PanelProjectOutput.OpenResult.UNAVAILABLE:scenario==5?PanelProjectOutput.OpenResult.UNSAVED:PanelProjectOutput.OpenResult.CHANGED));
                    require(seen.stream().filter("start"::equals).count()==(scenario==0?1:0));
                    if(scenario==0)require(seen.equals(java.util.Arrays.asList("current","available","current","persist","current","start")));
                }
                int[] starts={0};require(PanelProjectOutput.open(new PanelProjectOutput.OpenOperations(){
                    public boolean current(){return true;}public boolean available(){return true;}public boolean preserveDrafts(){return true;}
                    public void start(){starts[0]++;throw new SecurityException("synthetic refusal");}
                })==PanelProjectOutput.OpenResult.FAILED);require(starts[0]==1);
                break;
            }
            default: throw new AssertionError();
        }
        System.out.println("passed");
    }
}
