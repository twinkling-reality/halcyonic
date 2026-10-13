package android.content.pm;
import android.content.Intent;
import java.util.ArrayList;
import java.util.List;
public class PackageManager {
    public static final int PERMISSION_GRANTED=0;
    public final List<ResolveInfo> results=new ArrayList<>(); public final List<Intent> queries=new ArrayList<>();
    public List<ResolveInfo> queryIntentActivities(Intent query,int flags){if(flags!=0)throw new AssertionError("Default-only filtering");queries.add(query);return results;}
}
