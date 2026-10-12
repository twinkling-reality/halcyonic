#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Real production pages at both text sizes and presets, plus same-pose cache witnesses. No preferences are read or saved.</summary>
        public static void AppearanceCheck()
        {
            var previousPalette = Glaze.Palette;
            var previousField = ViewField.Current;
            var previousScale = GlazeText.Scale;
            var failures = new List<string>();
            try
            {
                foreach (var palette in GlazePalette.Presets)
                {
                    Glaze.SetPalette(palette);
                    GlazeText.SetScale(1f);
                    failures.AddRange(GlazeRender.PalettePass(palette));
                    failures.AddRange(PalettePass(palette));
                    failures.AddRange(FileRender.PalettePass(palette));
                    failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                    {
                        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "AppearanceRenders", palette.Key, variant));
                        Directory.CreateDirectory(folder);
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        ViewField.Current = FieldChecks.Quest3S;
                        var result = AppearancePages(folder, palette);
                        result.AddRange(AppearanceVisibility(folder, palette, besideWindow: false));
                        result.AddRange(AppearanceVisibility(folder, palette, besideWindow: true));
                        result.AddRange(AppearanceCaches(folder, palette));
                        return result;
                    }));
                    if (Glaze.Palette != palette) failures.Add(palette.Key + ": the production pass leaked another applied palette.");
                }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                Glaze.SetPalette(previousPalette);
                GlazeText.SetScale(previousScale);
                ViewField.Current = previousField;
                KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: appearance render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: appearance render: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> AppearancePages(string folder, GlazePalette palette)
        {
            var failures = new List<string>();
            var root = new GameObject("Production Appearance pages");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var settings = new AppearanceSettings();
            settings.Apply(palette);
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                camera.backgroundColor = Color.white;
                var characters = Lineup(root.transform, eyes, CharacterStage.DefaultDistance, null, Presentation);
                var targets = characters.ConvertAll(character => character.Target);
                var state = Projection(characters);
                var saves = 0;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    State = () => state,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Appearance = settings,
                    AppearanceSaved = () => saves++,
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront,
                        new PairingNow("192.0.2.23:47801", PairingStep.Idle, true)), _ => { }, _ => { }, pairing: true),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, targets, null, false),
                });
                void Shot(string name)
                {
                    director.DrawNow();
                    failures.AddRange(PlaneState("appearance " + name, folder, camera, texture, director.Plane, characters, eyes, null));
                }
                void Press(string id, string? key = null)
                {
                    var drawn = director.Plane.Showing(MenuColumn.Menu);
                    if (drawn == null || !(drawn.Lines.Any(line => line.Action == id && line.Key == key)
                        || drawn.Footer.All.Any(each => each.Prompt.Id == id))
                        || !director.Navigator.Act(MenuColumn.Menu, id, key, drawn, null))
                        failures.Add("appearance: no visible drawn route for " + id + " " + key + ".");
                }
                director.Open(MenuPlace.Settings);
                Shot("categories");
                Press(SettingsHubColumn.OpenCategory, SettingsHubColumn.Appearance);
                Shot("choices");
                var other = palette == GlazePalette.Default ? GlazePalette.Graphite : GlazePalette.Default;
                Press(AppearanceColumn.Choose, other.Key);
                Shot("local choice");
                if (Glaze.Palette != palette || saves != 0) failures.Add("appearance: a local choice changed or saved the palette.");
                Press(AppearanceColumn.Preview);
                for (var page = 0; page < 5; page++)
                {
                    Shot("preview " + page);
                    if (page < 4) Press(Footer.NextPage);
                }
                Press(AppearanceColumn.Cancel);
                Shot("cancelled");
                if (Glaze.Palette != palette || saves != 0) failures.Add("appearance: Cancel failed to restore the applied preset without saving.");
                Press(AppearanceColumn.Choose, other.Key);
                Shot("choice for apply");
                Press(AppearanceColumn.Preview);
                Shot("preview for apply");
                Press(AppearanceColumn.Apply);
                Shot("applied");
                if (Glaze.Palette != other || settings.Applied != other || saves != 1) failures.Add("appearance: the visible Apply route did not apply and save once.");
                Press(AppearanceColumn.ReadRestore);
                Shot("restore explanation");
                Press(AppearanceColumn.Restore);
                Shot("restored default");
                if (Glaze.Palette != GlazePalette.Default || saves != 2) failures.Add("appearance: Restore failed to apply the default and save once.");
            }
            finally
            {
                // The fixture owns its applied value. Destruction must not roll back to a different pass's preset.
                settings.Apply(palette);
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
                Glaze.SetPalette(palette);
            }
            return failures;
        }

        private static List<string> AppearanceVisibility(string folder, GlazePalette palette, bool besideWindow)
        {
            var failures = new List<string>();
            var root = new GameObject("Appearance visibility");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var settings = new AppearanceSettings();
            settings.Apply(palette);
            var name = besideWindow ? "window" : "normal";
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                camera.backgroundColor = Color.white;
                var characters = Lineup(root.transform, eyes, CharacterStage.DefaultDistance, null, Presentation, besideWindow: besideWindow);
                var targets = characters.ConvertAll(character => character.Target);
                var window = besideWindow ? Window(root.transform, eyes) : null;
                var state = Projection(characters);
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    State = () => state,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Appearance = settings,
                    CharacterOf = id => targets.FirstOrDefault(target => target.View.WorkstreamId == id),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, targets, null, besideWindow),
                });
                void Shot(string step)
                {
                    director.DrawNow();
                    failures.AddRange(PlaneState("appearance visibility " + name + " " + step, folder, camera, texture, director.Plane, characters, eyes, window));
                }
                void Press(string id, string? key = null)
                {
                    var frame = director.Plane.Showing(MenuColumn.Menu);
                    if (frame == null || !(frame.Lines.Any(line => line.Action == id && line.Key == key)
                        || frame.Footer.All.Any(each => each.Prompt.Id == id))
                        || !director.Navigator.Act(MenuColumn.Menu, id, key, frame, null))
                        failures.Add(name + ": Appearance offered no visible " + id + ".");
                }
                director.Open(MenuPlace.Settings);
                Shot("index");
                Press(SettingsHubColumn.OpenCategory, SettingsHubColumn.Appearance);
                Shot("choices");
                var other = palette == GlazePalette.Default ? GlazePalette.Graphite : GlazePalette.Default;
                MenuFrame? Preview()
                {
                    Press(AppearanceColumn.Choose, other.Key);
                    Shot("choice");
                    Press(AppearanceColumn.Preview);
                    Shot("preview");
                    return director.Plane.Showing(MenuColumn.Menu);
                }
                void Return(MenuFrame? preview)
                {
                    director.Navigator.CloseBeside();
                    Shot("returned");
                    if (director.Plane.Showing(MenuColumn.Menu) == null) failures.Add(name + ": closing the focused/aside fixture did not return Settings.");
                    if (director.Navigator.Act(MenuColumn.Menu, AppearanceColumn.Apply, null, preview, null)) failures.Add(name + ": the previous Apply acquired authority after returning.");
                }
                var beforeOrdinary = Preview();
                // Stable ordinary file frame, through the production navigator and actual measured composition.
                var ordinary = new MenuFrame("Task file", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)), lines: new[] { new PageLine("Example task") });
                director.Navigator.ShowBeside(new StubColumn(() => ordinary), targets[3].View.WorkstreamId);
                Shot("ordinary file");
                var hidden = director.Plane.MenuAside;
                if (besideWindow && !hidden) failures.Add(name + ": the supported beside-window composition never exercised MenuAside.");
                if (Glaze.Palette != (hidden ? palette : other)) failures.Add(name + ": ordinary file visibility and preview rollback disagree.");
                if (hidden && director.Plane.Showing(MenuColumn.Menu) != null) failures.Add(name + ": MenuAside retained a drawn menu frame.");
                Return(beforeOrdinary);
                // A normal side-by-side file preserves preview. Explicit Cancel resets it for the focused case.
                if (!hidden) { Press(AppearanceColumn.Cancel); Shot("visible preview cancelled"); }
                var beforeFocused = Preview();
                var focused = new MenuFrame("Your answer", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)),
                    lines: new[] { new PageLine("Draft retained", action: "render-input", control: LineControl.Input, inputFocused: true) }).Focus();
                director.Navigator.ShowBeside(new StubColumn(() => focused), targets[3].View.WorkstreamId);
                Shot("focused file");
                if (director.Plane.Showing(MenuColumn.Menu) != null || Glaze.Palette != palette)
                    failures.Add(name + ": the focused file hid Settings without rolling its preview back.");
                if (director.Navigator.Act(MenuColumn.Menu, AppearanceColumn.Apply, null, beforeFocused, null)) failures.Add(name + ": hidden Apply remained authorized.");
                Return(beforeFocused);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
                Glaze.SetPalette(palette);
            }
            return failures;
        }

        private static List<string> AppearanceCaches(string folder, GlazePalette palette)
        {
            var failures = new List<string>();
            var presentation = Presentation("render-appearance", 3);
            var peek = PeekCard.Of(new WorkspacePresentation(presentation, null, null, null, Array.Empty<WorkspaceAction>(), Array.Empty<WorkspaceAction>(), Array.Empty<CommandFeedback>(), Array.Empty<ActivityEntry>()));
            foreach (var fixture in new (string Name, Func<Transform, GameObject> Make, bool Changes)[]
            {
                ("label", parent => { var view = CharacterLabelView.Create(parent); view.Show(CharacterLabel.Of(presentation)); return view.gameObject; }, true),
                ("busy-badge", parent => { var view = StateBadgeView.Create(parent, "Working", 0); view.Show(StateLanguage.BadgeOf(Presentation("render-busy", 2))); return view.gameObject; }, false),
                ("waiting-badge", parent => { var view = StateBadgeView.Create(parent, "Waiting", 0); view.Show(StateLanguage.BadgeOf(presentation)); return view.gameObject; }, false),
                ("peek", parent => { var view = PeekCardView.Create(parent); view.Show(peek); view.Fade(0.43f); return view.gameObject; }, true),
                // Simulated semantics deliberately have identical colors across these presets.
                ("mark", parent => { var view = MarkTag.Create(parent, "Practice", 0); view.Show(new WorkMark("Practice", GlazeIcon.Practice)); view.Fade(0.43f); return view.gameObject; }, false),
                ("banner", parent => { var view = StageBanner.Create(parent); view.Show("Connected to your computer", BannerKind.Live, "One task waits for you", stillOpen: LineWords.Ours("Open: ") + LineWords.Theirs("Example task")); return view.gameObject; }, true),
            })
            {
                var root = new GameObject("Appearance cache " + fixture.Name);
                var holder = new GameObject("Cached").transform;
                holder.SetParent(root.transform, false);
                var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                var shots = new List<Texture2D>();
                try
                {
                    Glaze.SetPalette(palette);
                    var eyes = new Vector3(0f, EyeHeight, 0f);
                    var camera = MakeCamera(root.transform, eyes, texture);
                    camera.backgroundColor = Color.white;
                    holder.localPosition = eyes + Vector3.forward * PlaneComposition.Distance;
                    holder.localScale = Vector3.one * PlaneComposition.Distance;
                    var old = fixture.Make(holder);
                    AppearanceLifecycle(old, true);
                    SeedAppearancePose(old);
                    Texture2D Shot(string name)
                    {
                        ForceMeshes(root);
                        var image = Render(camera, texture);
                        shots.Add(image);
                        File.WriteAllBytes(Path.Combine(folder, fixture.Name + "-" + name + ".png"), image.EncodeToPNG());
                        return image;
                    }
                    var rect = new RectInt(0, 0, Size, Size);
                    AppearanceActive(old, false);
                    var empty = Shot("empty");
                    AppearanceActive(old, true);
                    var initial = Shot("initial");
                    if (CompareAppearancePixels(initial, empty, rect).Changed == 0) failures.Add(fixture.Name + ": the witness drew no visible pixels.");
                    void AgainstFresh(string name)
                    {
                        CheckAppearancePose(old, fixture.Name + " " + name, failures);
                        var actual = Shot(name);
                        AppearanceActive(old, false);
                        var fresh = fixture.Make(holder);
                        AppearanceLifecycle(fresh, true);
                        SeedAppearancePose(fresh);
                        var expected = Shot(name + "-fresh");
                        var (changed, largest) = CompareAppearancePixels(actual, expected, rect);
                        if (changed != 0) failures.Add(fixture.Name + " " + name + ": cached versus fresh differs in " + changed + " pixels, largest channel " + largest + ".");
                        AppearanceLifecycle(fresh, false);
                        UnityEngine.Object.DestroyImmediate(fresh);
                        AppearanceActive(old, true);
                    }
                    var other = palette == GlazePalette.Default ? GlazePalette.Graphite : GlazePalette.Default;
                    Glaze.SetPalette(other);
                    AgainstFresh("active-changed");
                    var changedPixels = CompareAppearancePixels(initial, shots[2], rect).Changed;
                    if (fixture.Changes && changedPixels == 0) failures.Add(fixture.Name + ": changed neutral surfaces never reached rendered pixels.");
                    if (!fixture.Changes && changedPixels != 0) failures.Add(fixture.Name + ": unchanged status/practice semantics or saved fade/pose changed rendered pixels.");
                    AppearanceActive(old, false);
                    Glaze.SetPalette(palette);
                    AppearanceActive(old, true);
                    AgainstFresh("reactivated-restored");
                    if (CompareAppearancePixels(initial, shots[4], rect).Changed != 0) failures.Add(fixture.Name + ": restoring the preset did not restore the same rendered pose/fade.");
                }
                finally
                {
                    AppearanceLifecycle(root, false);
                    UnityEngine.Object.DestroyImmediate(root);
                    foreach (var shot in shots) UnityEngine.Object.DestroyImmediate(shot);
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                    Glaze.SetPalette(palette);
                }
            }
            return failures;
        }

        // Plain MonoBehaviours do not run these subscriptions in a synchronous edit-mode fixture.
        // Invoke the existing production lifecycle explicitly, unsubscribing first to avoid duplication.
        // This seeds lifecycle coverage; actual play-mode/device activation remains a separate observation.
        private static void AppearanceActive(GameObject root, bool active)
        {
            root.SetActive(active);
            AppearanceLifecycle(root, active);
        }

        private static void AppearanceLifecycle(GameObject root, bool active)
        {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(component is CharacterLabelView || component is StateBadgeView || component is PeekCardView
                    || component is MarkTag || component is StageBanner)) continue;
                var type = component.GetType();
                type.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
                if (active) type.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
            }
        }

        private static (int Changed, int Largest) CompareAppearancePixels(Texture2D actual, Texture2D expected, RectInt rect)
        {
            var a = actual.GetPixels32();
            var b = expected.GetPixels32();
            var changed = 0;
            var largest = 0;
            for (var y = rect.yMin; y < rect.yMax; y++)
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    var index = y * actual.width + x;
                    var delta = Math.Max(Math.Abs(a[index].r - b[index].r), Math.Max(Math.Abs(a[index].g - b[index].g), Math.Abs(a[index].b - b[index].b)));
                    largest = Math.Max(largest, delta);
                    if (delta != 0) changed++;
                }
            return (changed, largest);
        }

        // Editor-only seeded pose, not a claim about live timing. Both cached and fresh use exactly the same transforms,
        // breath, turn and fade. These witnesses fail if refresh resets the loop or draws a new half-breath.
        private static void SeedAppearancePose(GameObject root)
        {
            foreach (var badge in root.GetComponentsInChildren<StateBadgeView>(true))
            {
                typeof(StateBadgeView).GetField("breathing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(badge, badge.Shown!.Breathes ? 1.7f : 0f);
                typeof(StateBadgeView).GetField("turning", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(badge, badge.Shown!.Turns ? 0.23f : 0f);
                badge.Icon.transform.localRotation = Quaternion.Euler(0f, 0f, badge.Shown!.Turns ? -360f * 0.23f : 0f);
                typeof(StateBadgeView).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(badge, new object[] { badge.Shown!.Breathes ? 0.65f : 0f });
                badge.Fade(0.43f);
            }
        }

        private static void CheckAppearancePose(GameObject root, string name, List<string> failures)
        {
            foreach (var badge in root.GetComponentsInChildren<StateBadgeView>(true))
            {
                var breathing = (float)typeof(StateBadgeView).GetField("breathing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(badge)!;
                var turning = (float)typeof(StateBadgeView).GetField("turning", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(badge)!;
                if (breathing != (badge.Shown!.Breathes ? 1.7f : 0f) || turning != (badge.Shown.Turns ? 0.23f : 0f) || Mathf.Abs(badge.Word.alpha - 0.43f) > 1e-5f
                    || Quaternion.Angle(badge.Icon.transform.localRotation, Quaternion.Euler(0f, 0f, badge.Shown!.Turns ? -360f * 0.23f : 0f)) > 0.01f)
                    failures.Add(name + ": refreshing changed the saved loop pose or opacity.");
            }
        }
    }
}
