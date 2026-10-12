package com.halcyonic.panel;

import java.io.IOException;

/** A private write returns only after both the record and its directory entry are durable. */
final class PanelAtomicWrite {
    interface Operations {
        void writeTemporary() throws IOException;
        void rename() throws IOException;
        void syncDirectory() throws IOException;
        void cleanup();
    }
    static void commit(Operations io)throws IOException{
        try{io.writeTemporary();io.rename();io.syncDirectory();}finally{io.cleanup();}
    }
}
