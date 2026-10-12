package com.halcyonic.panel;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/** Local input safety metadata only; absence never proves a command was not attempted. */
final class PanelRequestSends {
    static final int MAXIMUM_BYTES=4*1024*1024;
    private final List<Object> sends;
    PanelRequestSends(){sends=new ArrayList<>();}
    private PanelRequestSends(List<Object> sends){this.sends=sends;}
    static PanelRequestSends parse(String json){
        if(json.getBytes(StandardCharsets.UTF_8).length>MAXIMUM_BYTES)throw PanelJson.invalid();
        Map<String,Object> root=PanelJson.object(PanelJson.parse(json));PanelJson.keys(root,"version","sends");PanelJson.integer(root.get("version"),1,1);
        List<Object> records=PanelJson.array(root.get("sends"));java.util.Set<String> ids=new java.util.HashSet<>();
        for(Object item:records){Map<String,Object> entry=PanelJson.object(item);PanelJson.keys(entry,"command_id","connection_key","journal_id","project_id","workstream_id","execution_id","request_id","command_type","phase");PanelContinuation.matches(entry.get("command_id"),PanelIdentity.UUID);PanelContinuation.matches(entry.get("connection_key"),"[0-9a-f]{64}");for(String key:new String[]{"journal_id","project_id","workstream_id","execution_id"})PanelContinuation.matches(entry.get(key),PanelIdentity.ENTITY);if(entry.get("request_id")!=null)PanelContinuation.matches(entry.get("request_id"),PanelIdentity.ENTITY);if(!java.util.Arrays.asList("unsent","unknown").contains(entry.get("phase")))throw PanelJson.invalid();String type=PanelJson.text(entry.get("command_type"));if(!java.util.Arrays.asList("workspace.request.submit","workspace.request.resume","workspace.request.cancel").contains(type)||("workspace.request.submit".equals(type)!=(entry.get("request_id")==null)))throw PanelJson.invalid();if(!ids.add(PanelJson.text(entry.get("command_id"))))throw PanelJson.invalid();}
        return new PanelRequestSends(records);
    }
    String json(){String result=PanelJson.encode(PanelJson.map("version",1L,"sends",sends));if(result.getBytes(StandardCharsets.UTF_8).length>MAXIMUM_BYTES)throw PanelJson.invalid();return result;}
    private Map<String,Object> find(String id){for(Object item:sends){Map<String,Object> entry=PanelJson.object(item);if(id.equals(entry.get("command_id")))return entry;}return null;}
    private static boolean matches(Map<String,Object> entry,PanelUiState.Context c,String request,String type){return entry!=null&&c.connection.equals(entry.get("connection_key"))&&c.journal.equals(entry.get("journal_id"))&&c.project.equals(entry.get("project_id"))&&c.work.equals(entry.get("workstream_id"))&&c.execution.equals(entry.get("execution_id"))&&java.util.Objects.equals(request,entry.get("request_id"))&&(type==null||type.equals(entry.get("command_type")));}
    void stage(String id,PanelUiState.Context c,String request,String type){
        if(find(id)!=null)throw PanelJson.invalid();
        Map<String,Object> entry=PanelJson.map("command_id",id,"connection_key",c.connection,"journal_id",c.journal,"project_id",c.project,"workstream_id",c.work,"execution_id",c.execution,"request_id",request,"command_type",type,"phase","unsent");
        List<Object> candidate=new ArrayList<>(sends);candidate.add(entry);parse(PanelJson.encode(PanelJson.map("version",1L,"sends",candidate)));sends.add(entry);
    }
    boolean unsent(String id,PanelUiState.Context c,String request){return matches(find(id),c,request,null)&&"unsent".equals(find(id).get("phase"));}
    String type(String id,PanelUiState.Context c,String request){Map<String,Object> entry=find(id);return matches(entry,c,request,null)?PanelJson.text(entry.get("command_type")):null;}
    void attempted(String id,PanelUiState.Context c,String request,String type){Map<String,Object> entry=find(id);if(!matches(entry,c,request,type)||!"unsent".equals(entry.get("phase")))throw PanelJson.invalid();entry.put("phase","unknown");}
    void removeUnsent(String id,PanelUiState.Context c,String request){if(!unsent(id,c,request))throw PanelJson.invalid();sends.remove(find(id));}
    void forgetSettled(String id,PanelUiState.Context c,String request){Map<String,Object> entry=find(id);if(matches(entry,c,request,null))sends.remove(entry);}
}
