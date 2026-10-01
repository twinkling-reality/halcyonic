#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.UI.Editor
{
    /// <summary>
    /// Renders every component of the interface in every state it has, on a panel at touch distance
    /// (ADR 0023): the state badges, the last known badge and a count, the marks, every role of
    /// button at rest, pointed at, pressed, unavailable and done, compact buttons and buttons with a
    /// second line, and the banner's kinds. It checks each by <see cref="GlazeChecks"/>: words at
    /// least the caption's size, targets at least 60 dp (48 compact), nothing of ours cut short,
    /// every badge's whole word, and each button's label contrasting with its own fill as drawn, at
    /// least 4.5:1. A token changed shows here everywhere at once. It saves the gallery at a Quest
    /// 3's 25 pixels per degree in apps/xr/Builds/GlazeRenders, which git ignores. In the editor:
    /// Halcyonic > Render Every Component. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md;
    /// it exits with 1 when a check fails.
    /// </summary>
    public static class GlazeRender
    {
        private const int Size = 2048;

        /// <summary>A Quest 3's resolution near the middle of its lenses.</summary>
        private const float PixelsPerDegree = 25f;

        /// <summary>Foreground panels open this far from the eyes (ADR 0023).</summary>
        private const float Distance = 0.46f;

        private const float EyeHeight = 1.2f;

        private static Transform gallery = null!;
        private static Vector3 galleryEyes;

        [MenuItem("Halcyonic/Render Every Component")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = Run();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Component render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "GlazeRenders"));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = (GameObject?)null;
            RenderTexture? texture = null;
            try
            {
                // A new scene unloads what nothing references, so the target comes after it.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                root = new GameObject("Component render");
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = Camera(root.transform, eyes, texture);
                gallery = root.transform;
                galleryEyes = eyes;

                // A panel behind it all, as every component sits on one, a little farther than they stand.
                var behind = Holder("Panel", 0f, 0f);
                behind.localPosition = eyes + Vector3.forward * (Distance * 1.25f);
                behind.localScale = Vector3.one * (Distance * 1.25f);
                var panel = Surface.Create(behind, "Panel", -1);
                panel.Draw(new Vector2(GlazeTokens.Units(90f), GlazeTokens.Units(60f)), GlazeTokens.Units(Glaze.PanelRadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel));

                var first = new List<Transform>();
                var badges = Badges();
                var buttons = Buttons();
                Banners();
                foreach (Transform holder in gallery) first.Add(holder);
                failures.AddRange(Check(folder, "gallery.png", camera, texture, root, eyes, buttons));
                failures.AddRange(GlazeChecks.BadgesSayTheirState(badges, "component render"));

                // A panel's list rows on a page of their own, in the middle of the view.
                foreach (var holder in first)
                {
                    if (holder != behind) holder.gameObject.SetActive(false);
                }
                failures.AddRange(Check(folder, "gallery-rows.png", camera, texture, root, eyes, Rows()));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: component render: every check passed; the render is in " + folder);
            return failures;
        }

        /// <summary>
        /// Renders what shows now as <paramref name="file"/> and checks it: words large enough, targets
        /// large enough, nothing cut, and each button's label contrasting with its own fill as drawn.
        /// </summary>
        private static IEnumerable<string> Check(string folder, string file, Camera camera, RenderTexture texture, GameObject root, Vector3 eyes,
            List<(GlazeButton Button, string What)> buttons)
        {
            var failures = new List<string>();
            ForceMeshes(root);
            var render = Render(camera, texture);
            File.WriteAllBytes(Path.Combine(folder, file), render.EncodeToPNG());
            failures.AddRange(GlazeChecks.TextLargeEnough(root, eyes, "component render"));
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons.Where(button => !button.Button.Static).Select(button => button.Button), eyes, "component render"));
            failures.AddRange(GlazeChecks.NothingCut(root.GetComponentsInChildren<TMP_Text>(false), "component render"));
            foreach (var (button, what) in buttons)
            {
                var contrast = LabelContrast(camera, render, button);
                Debug.Log("Halcyonic: component render: " + what + "'s label reaches " + contrast.ToString("0.0", CultureInfo.InvariantCulture) + ":1 on its fill.");
                if (contrast < 4.5f) failures.Add("component render: " + what + "'s label reaches only " + contrast.ToString("0.0", CultureInfo.InvariantCulture) + ":1 on its fill.");
            }
            UnityEngine.Object.DestroyImmediate(render);
            return failures;
        }

        /// <summary>Every state's badge, a count and a last known one, and the three marks, in rows from the top.</summary>
        private static List<StateBadgeView> Badges()
        {
            var badges = new List<StateBadgeView>();
            var states = (CharacterActivity[])Enum.GetValues(typeof(CharacterActivity));
            var shown = states.Select(activity => Character(activity, AttentionLevel.None)).ToList();
            shown.Add(Character(CharacterActivity.TurnFinished, AttentionLevel.Notice));
            shown.Add(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, waiting: 2));
            shown.Add(Character(CharacterActivity.Working, AttentionLevel.None, stale: true));
            var x = -33f;
            var y = 21f;
            foreach (var presentation in shown)
            {
                var holder = Holder("Badge " + badges.Count, 0f, 0f);
                var badge = StateBadgeView.Create(holder, "Badge", 1);
                badge.Show(StateLanguage.BadgeOf(presentation));
                var width = GlazeTokens.DegreesOf(badge.Width);
                if (x + width > 33f)
                {
                    x = -33f;
                    y -= 3f;
                }
                Aim(holder, x + width / 2f, y);
                x += width + 1.5f;
                badges.Add(badge);
            }
            x = -33f;
            y -= 3f;
            foreach (var word in new[] { StateLanguage.Practice, StateLanguage.Demo, StateLanguage.Recorded })
            {
                var holder = Holder("Mark " + word, 0f, 0f);
                var mark = MarkTag.Create(holder, "Mark", 1);
                mark.Show(new WorkMark(word, GlazeIcon.Practice));
                var width = GlazeTokens.DegreesOf(mark.Width);
                Aim(holder, x + width / 2f, y);
                x += width + 1.5f;
            }
            return badges;
        }

        /// <summary>Every role at rest, pointed at and pressed, then unavailable, done, compact and with a second line.</summary>
        private static List<(GlazeButton Button, string What)> Buttons()
        {
            var buttons = new List<(GlazeButton, string)>();
            var roles = new (ButtonRole Role, bool On, string Label)[]
            {
                (ButtonRole.Primary, false, "Start building"),
                (ButtonRole.Secondary, false, "More options"),
                (ButtonRole.Destructive, false, "Start over"),
                (ButtonRole.Filter, true, "Storefront API"),
                (ButtonRole.Filter, false, "Recipe tracker"),
            };
            var y = 7f;
            foreach (var (pointed, pressed, state) in new[] { (false, false, "at rest"), (true, false, "pointed at"), (false, true, "pressed") })
            {
                var x = -33f;
                foreach (var (role, on, label) in roles)
                {
                    var button = GlazeButton.Create(Holder(label + " " + state, 0f, 0f), "Button", role);
                    button.On = on;
                    x = Place(button, label, null, null, x, y);
                    button.PaintForRender(pointed, pressed);
                    buttons.Add((button, label + " " + state));
                }
                y -= 5f;
            }
            var row = -33f;
            var unavailable = GlazeButton.Create(Holder("Unavailable", 0f, 0f), "Button", ButtonRole.Secondary);
            unavailable.Available = false;
            row = Place(unavailable, "Approve", null, null, row, y);
            buttons.Add((unavailable, "an unavailable button"));
            var done = GlazeButton.Create(Holder("Done", 0f, 0f), "Button", ButtonRole.Secondary);
            done.Done = true;
            row = Place(done, "Approved", null, null, row, y);
            buttons.Add((done, "a done button"));
            var counted = GlazeButton.Create(Holder("With a line", 0f, 0f), "Button", ButtonRole.Filter);
            counted.On = true;
            row = Place(counted, "Storefront API", "1 task is waiting for you", GlazeTone.Attention, row, y);
            buttons.Add((counted, "a filter with a waiting line"));
            var compact = GlazeButton.Create(Holder("Compact", 0f, 0f), "Button", ButtonRole.Secondary, compact: true);
            Place(compact, "Settings", null, null, row, y);
            buttons.Add((compact, "a compact button"));
            foreach (var (button, _) in buttons)
            {
                if (button != counted && button != compact && button != unavailable && button != done) continue;
                button.PaintForRender(false, false);
            }
            return buttons;
        }

        /// <summary>
        /// A panel's list rows: a choice at rest, pointed at, pressed and chosen, a filter on and off, a
        /// fact with its line over the title and its end word, an unavailable one and one that only says
        /// something; then a card, the attention button and a destructive action's confirmation.
        /// </summary>
        private static List<(GlazeButton Button, string What)> Rows()
        {
            var rows = new List<(GlazeButton, string)>();
            var width = GlazeTokens.Units(19f);
            var x = -31f;
            var y = 18f;
            void Add(string what, PanelRow words, ButtonRole role, bool pointed = false, bool pressed = false, bool on = false, bool available = true, bool still = false)
            {
                var button = GlazeButton.Create(Holder("Row " + what, 0f, 0f), "Row", role);
                button.On = on;
                button.Available = available;
                button.Static = still;
                var height = button.LayRow(words, width, still ? 0f : (float?)null);
                button.ShowRow(words, Vector2.zero, new Vector2(width, height), still ? 0f : (float?)null);
                if (x + 19f > 33f)
                {
                    x = -31f;
                    y -= 9f;
                }
                Aim(button.transform.parent, x + 9.5f, y);
                x += 19f + 1.5f;
                button.PaintForRender(pointed, pressed);
                rows.Add((button, what));
            }
            var choice = new PanelRow { Title = "New folder in Projects", Detail = "Your Mac makes a new, empty folder", Action = "choose" };
            Add("a choice at rest", choice, ButtonRole.Choice);
            Add("a choice pointed at", choice, ButtonRole.Choice, pointed: true);
            Add("a choice pressed", choice, ButtonRole.Choice, pressed: true);
            Add("a chosen choice", new PanelRow { Title = "shop", Detail = EntryText.Chosen + " · In Projects", Chosen = true, Action = "choose" }, ButtonRole.Choice, on: true);
            Add("a filter shown", new PanelRow { Title = "Storefront API", Detail = "1 task waiting", DetailTone = GlazeTone.Attention, Filter = true, Chosen = true, Action = "toggle" },
                ButtonRole.Filter, on: true);
            Add("a filter hidden", new PanelRow { Title = "Recipe tracker", Detail = "Hidden · 1 waiting", DetailTone = GlazeTone.Attention, Filter = true, Action = "toggle" },
                ButtonRole.Filter);
            Add("a fact", new PanelRow { Overline = EntryText.HowItRuns, Title = "On your Mac", Detail = "Chosen for you. Change it in More options.", DetailLines = 2,
                End = EntryText.MoreOptions, Action = "options" }, ButtonRole.Choice);
            Add("an unavailable choice", new PanelRow { Title = "Code", Detail = "Not on your Mac right now", Available = false, Action = "choose" }, ButtonRole.Choice, available: false);
            Add("a step and how it went", new PanelRow { Title = "Create the project", Detail = "Confirmed", DetailTone = GlazeTone.Success }, ButtonRole.Choice, still: true);
            Add("a card", new PanelRow { Card = true, Title = EntryText.CreateProject, Detail = EntryText.CreateInvite, Action = "create" }, ButtonRole.Choice);
            var attention = GlazeButton.Create(Holder("Attention", 0f, 0f), "Button", ButtonRole.Attention, compact: true);
            var after = Place(attention, EntryText.OpenNow, null, null, x, y);
            attention.PaintForRender(false, false);
            rows.Add((attention, "the attention button"));
            var confirm = GlazeButton.Create(Holder("Confirm destructive", 0f, 0f), "Button", ButtonRole.Destructive);
            confirm.On = true;
            Place(confirm, EntryText.ConfirmStartOver, null, null, after, y);
            confirm.PaintForRender(false, false);
            rows.Add((confirm, "a destructive action's confirmation"));
            return rows;
        }

        private static float Place(GlazeButton button, string label, string? detail, GlazeTone? tone, float x, float y)
        {
            var width = button.Measure(label, detail);
            var degrees = GlazeTokens.DegreesOf(width);
            button.Show(label, Vector2.zero, width, detail, tone);
            Aim(button.transform.parent, x + degrees / 2f, y);
            return x + degrees + 1.5f;
        }

        /// <summary>A holder for one component, at the distance it was built for and facing the eyes.</summary>
        private static Transform Holder(string name, float xDegrees, float yDegrees)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(gallery, false);
            holder.localScale = Vector3.one * Distance;
            Aim(holder, xDegrees, yDegrees);
            return holder;
        }

        /// <summary>
        /// Stands a holder <paramref name="xDegrees"/> across and <paramref name="yDegrees"/> up from
        /// straight ahead, on a sphere round the eyes, so each component is seen at its own distance.
        /// </summary>
        private static void Aim(Transform holder, float xDegrees, float yDegrees)
        {
            var direction = Quaternion.Euler(-yDegrees, xDegrees, 0f) * Vector3.forward;
            holder.SetPositionAndRotation(galleryEyes + direction * Distance, Quaternion.LookRotation(direction, Vector3.up));
        }

        /// <summary>The banner's three kinds, the last with what waits for the person.</summary>
        private static void Banners()
        {
            var x = -33f;
            foreach (var (kind, text, waiting) in new[]
            {
                (BannerKind.Live, "Connected to your Mac", (string?)null),
                (BannerKind.NotLive, "Last known: can't reach your Mac. Trying again…", null),
                (BannerKind.Practice, "Recorded practice: nothing here is live.", EntryText.WaitingForYou(1)),
            })
            {
                var holder = Holder("Banner " + kind, 0f, 0f);
                var banner = StageBanner.Create(holder);
                banner.Show(text, kind, waiting);
                var width = GlazeTokens.DegreesOf(banner.Width);
                Aim(holder, x + width / 2f, -14f);
                x += width + 1.5f;
            }
        }

        /// <summary>The contrast a button's label reaches on its own fill, as drawn: its fill sampled inside its padding, its strokes at their strongest.</summary>
        private static float LabelContrast(Camera camera, Texture2D render, GlazeButton button)
        {
            var plate = button.transform;
            var inside = camera.WorldToScreenPoint(plate.TransformPoint(new Vector3(-button.Size.x / 2f + GlazeTokens.Units(0.5f), 0f, 0f)));
            var fill = render.GetPixel(Mathf.RoundToInt(inside.x), Mathf.RoundToInt(inside.y));
            var bounds = button.Label.GetComponent<Renderer>().bounds;
            var low = camera.WorldToScreenPoint(bounds.min);
            var high = camera.WorldToScreenPoint(bounds.max);
            var rect = new RectInt(Mathf.FloorToInt(Mathf.Min(low.x, high.x)), Mathf.FloorToInt(Mathf.Min(low.y, high.y)),
                Mathf.CeilToInt(Mathf.Abs(high.x - low.x)), Mathf.CeilToInt(Mathf.Abs(high.y - low.y)));
            return GlazeChecks.Contrast(render, rect, fill);
        }

        private static CharacterPresentation Character(CharacterActivity activity, AttentionLevel attention, int waiting = 0, bool stale = false) =>
            new CharacterPresentation("render", "Render", activity, CharacterPresenter.LabelOf(activity), attention,
                Enumerable.Range(0, waiting).Select(index => "Waiting " + index).ToList(), waiting, false, false, stale);

        private static Camera Camera(Transform parent, Vector3 eyes, RenderTexture texture)
        {
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, Quaternion.identity);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = Size / PixelsPerDegree;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            return camera;
        }

        private static Texture2D Render(Camera camera, RenderTexture texture)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        private static void ForceMeshes(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        }

        /// <summary>Drawing text in the editor marks the committed font assets changed; this check must not change them.</summary>
        private static void KeepFontAssetsAsCommitted()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (!AssetDatabase.Contains(font)) continue;
                EditorUtility.ClearDirty(font);
                if (font.material != null) EditorUtility.ClearDirty(font.material);
                if (font.atlasTextures == null) continue;
                foreach (var atlas in font.atlasTextures)
                {
                    if (atlas != null) EditorUtility.ClearDirty(atlas);
                }
            }
        }
    }
}
