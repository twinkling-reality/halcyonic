package android.app;
import android.content.Intent;
import android.content.pm.PackageManager;
public class Activity {
    public final PackageManager manager=new PackageManager(); public final java.util.List<Intent> starts=new java.util.ArrayList<>();
    public boolean refuse; public String allowedPermission;
    public PackageManager getPackageManager(){return manager;}
    public String getPackageName(){return "com.halcyonic.panel";}
    public int checkSelfPermission(String permission){return permission.equals(allowedPermission)?PackageManager.PERMISSION_GRANTED:-1;}
    public void startActivity(Intent intent){starts.add(intent);if(refuse)throw new SecurityException("Synthetic refusal");}
}
