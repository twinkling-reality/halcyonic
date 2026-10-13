package android.content;
/** Test-only recording surface; production is compiled separately against Android API 36. */
public class ComponentName {
    private final String packageName, className;
    public ComponentName(String packageName,String className){this.packageName=packageName;this.className=className;}
    public String getPackageName(){return packageName;} public String getClassName(){return className;}
}
