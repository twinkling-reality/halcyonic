package android.content.pm;
public class ResolveInfo { public ActivityInfo activityInfo; public CharSequence label;
    public CharSequence loadLabel(PackageManager manager){return label;}
}
