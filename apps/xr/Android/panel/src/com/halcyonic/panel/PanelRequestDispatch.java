package com.halcyonic.panel;

import java.io.IOException;
import java.util.Arrays;
import java.util.Map;

/** The actual request POST requires a successfully persisted immutable command reference. */
final class PanelRequestDispatch {
    interface Store { boolean persist(String commandId); boolean attempted(String commandId); }
    static final class Prepared {
        final String id,type; private final String body; private final Store store; private boolean attempted;
        private Prepared(String id,String type,String body,Store store){this.id=id;this.type=type;this.body=body;this.store=store;}
        synchronized PanelTransport.Result post(PanelTransport transport,PanelIdentity target)throws IOException{
            if(attempted)throw new PanelTransport.Refused("request_already_attempted",true);
            attempted=true;if(!store.attempted(id))throw new PanelTransport.Refused("request_phase_unavailable",false);return transport.request(target,"POST","/api/commands",body);
        }
        PanelWorkspaceRequest.Recovery recover(PanelTransport transport,PanelIdentity target,String request,PanelUiState.Context context)throws IOException{PanelTransport.Result response=transport.request(target,"GET","/api/workspace/commands/"+id,null);return response.status==200?PanelWorkspaceRequest.command(response.body,id,request,context,type):null;}
    }
    static Prepared prepare(Map<String,Object> envelope,Store store){
        PanelJson.keys(envelope,"schema_version","command_id","issued_at","client","command_type","payload");
        String id=PanelJson.text(envelope.get("command_id")),type=PanelJson.text(envelope.get("command_type"));
        PanelContinuation.matches(id,PanelIdentity.UUID);
        if(!Arrays.asList("workspace.request.submit","workspace.request.resume","workspace.request.cancel").contains(type))throw PanelJson.invalid();
        String body=PanelJson.encode(envelope);
        return store.persist(id)?new Prepared(id,type,body,store):null;
    }
}
