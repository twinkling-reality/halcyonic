package android.content;
import java.util.LinkedHashSet;
import java.util.Set;
/** Test-only recording surface, not an Android lifecycle or launch simulation. */
public class Intent {
    public static final String ACTION_MAIN="android.intent.action.MAIN", CATEGORY_LAUNCHER="android.intent.category.LAUNCHER";
    public static final int FLAG_ACTIVITY_NEW_TASK=0x10000000;
    private String action, packageName; private ComponentName component; private int flags;
    private final Set<String> categories=new LinkedHashSet<>();
    public Intent(String action){this.action=action;}
    public Intent addCategory(String category){categories.add(category);return this;}
    public Intent setPackage(String value){packageName=value;return this;}
    public Intent addFlags(int value){flags|=value;return this;}
    public static Intent makeMainActivity(ComponentName component){Intent value=new Intent(ACTION_MAIN).addCategory(CATEGORY_LAUNCHER);value.component=component;return value;}
    public String getAction(){return action;} public String getPackage(){return packageName;}
    public ComponentName getComponent(){return component;} public int getFlags(){return flags;} public Set<String> getCategories(){return categories;}
}
