#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders the stage as ADR 0023 arranges it: every state of a task on a character at the stage's
    /// default distance and height, practice, demonstration and last known work among them, with the
    /// banner under the labels and the peek under one label, and then the characters on a desk with
    /// the peek over one. It checks the interface's rules (<see cref="GlazeChecks"/>): nothing touches
    /// as seen from the eyes, every word is at least the caption's size, plates are opaque, every
    /// badge says its whole state and shows its icon only within the plate's room, a plate reaches
    /// the render in its token's colour, and titles contrast with their plates. It saves each render in apps/xr/Builds/StageRenders, which git
    /// ignores, with close-ups at a Quest 3's pixels per degree. In the editor: Halcyonic > Render
    /// Every State on the Stage. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it
    /// exits with 1 when a check fails.
    /// </summary>
    public static class StageRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;

        /// <summary>A Quest 3's resolution near the middle of its lenses.</summary>
        private const float QuestPixelsPerDegree = 25f;

        /// <summary>The least a character's own label keeps from its body, as seen from the eyes.</summary>
        private const float OwnLabelGapDegrees = 0.3f;

        /// <summary>A desk 0.46 m below the eyes, its lineup 0.55 m away, as the workspace render's.</summary>
        private const float DeskDrop = 0.46f;

        private const float DeskRadius = 0.55f;

        [MenuItem("Halcyonic/Render Every State on the Stage")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = GlazeChecks.AtEachTextSize(Run);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Stage render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = GlazeChecks.AtEachTextSize(Run);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <param name="variant">A folder of its own for the renders of a pass, such as the larger text's; empty for the standard pass.</param>
        private static List<string> Run(string variant)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "StageRenders", variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                // What runs beside the stage, once, in the stage scene and in a scene with nothing.
                if (string.IsNullOrEmpty(variant)) failures.AddRange(Bootstrapped());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(Render("states-a", folder, StatesA(), desk: false, BannerKind.Live, "Connected to " + HostText.Your, null, peekSlot: 4));
                failures.AddRange(Render("states-b", folder, StatesB(), desk: false, BannerKind.NotLive,
                    "Last known: can't reach " + HostText.Your + ". Trying again… (connection refused)", null, peekSlot: 1));
                failures.AddRange(Render("demo", folder, Demo(), desk: false, BannerKind.Practice,
                    DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null), AmbientText.NeedsYouLine(1), peekSlot: 2));
                failures.AddRange(Render("desk", folder, StatesA(), desk: true, BannerKind.Live, "Connected to " + HostText.Your, null, peekSlot: 4));
                // Built and shown before the stage is placed, under its hidden arc, as on the headset when
                // the work arrives first: every badge, mark, banner and peek measured all the same.
                failures.AddRange(RenderBuiltHidden("built-hidden", folder, Demo(), StatesA(), peekSlot: 4));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: stage render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: stage render: every check passed; the renders are in " + folder);
            return failures;
        }

        /// <summary>
        /// The bootstrap in the stage scene, as the app starts in it, and in an empty scene: exactly one stage,
        /// connection, focus guard and device measures in each, so the headset's field is measured and
        /// the layout keeps to it. The stage scene is opened, never saved.
        /// </summary>
        private static IEnumerable<string> Bootstrapped()
        {
            var failures = new List<string>();
            foreach (var (what, open) in new (string, Action)[]
            {
                ("the stage scene", () => EditorSceneManager.OpenScene("Assets/Halcyonic/Scenes/Stage.unity", OpenSceneMode.Single)),
                ("an empty scene", () => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)),
            })
            {
                open();
                var made = HalcyonicBootstrap.EnsureStage();
                // Started again, as a second scene load would: nothing is added twice.
                HalcyonicBootstrap.EnsureStage();
                void One<T>(string name) where T : UnityEngine.Object
                {
                    var count = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                    if (count != 1) failures.Add("in " + what + ", the bootstrap leaves " + count + " " + name + ", not one.");
                }
                One<CharacterStage>("character stages");
                One<ControlPlaneConnection>("control plane connections");
                One<FocusGuard>("focus guards");
                One<DeviceMeasures>("device measures");
                if (made != null) UnityEngine.Object.DestroyImmediate(made);
            }
            return failures;
        }

        /// <summary>Six characters as the stage stands them, the banner as it shows it, then the peek on one.</summary>
        private static IEnumerable<string> Render(string name, string folder, IReadOnlyList<CharacterPresentation> shown, bool desk, BannerKind kind,
            string bannerText, string? waiting, int peekSlot)
        {
            var failures = new List<string>();
            var root = new GameObject("Stage render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var radius = desk ? DeskRadius : CharacterStage.DefaultDistance;
                // In front of the person at the stage's height; on a desk, each label resting on it.
                var characters = WorkspaceRender.Lineup(root.transform, eyes, radius, desk ? DeskDrop : (float?)null, (_, slot) => shown[slot]);

                // The banner, where the stage puts it, reading away from the person as the arc does.
                var bannerRoot = new GameObject("Banner root").transform;
                bannerRoot.SetParent(root.transform, false);
                var surface = eyes.y - DeskDrop;
                bannerRoot.position = desk
                    ? new Vector3(0f, surface + CharacterStage.BannerBottomOnSurface(radius, DeskDrop), radius)
                    : eyes + new Vector3(0f, CharacterStage.BannerTop(radius, CharacterStage.DefaultHeightFromEyes), radius);
                bannerRoot.localScale = Vector3.one * radius;
                var banner = StageBanner.Create(bannerRoot);
                banner.Show(bannerText, kind, waiting);
                if (desk) banner.transform.localPosition = new Vector3(0f, banner.Height, 0f);
                banner.gameObject.SetActive(!AmbientCover.Any);

                WorkspaceRender.ForceMeshes(root);
                failures.AddRange(Checks(name, root, eyes, characters, banner, null));
                var whole = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), whole.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(whole);
                failures.AddRange(CloseUps(name, folder, camera, texture, characters));
                if (name == "states-a" || name == "states-b") failures.AddRange(KeptStill(name, folder, camera, texture, characters));
                if (name == "states-a") failures.AddRange(StillStatesDiffer(name, root.transform, eyes, radius));

                // The peek, as the director shows it: under its label in front of the person, over the
                // character on a desk; the banner steps aside for it.
                var peek = PeekLabel.Create(root.transform);
                var target = characters[peekSlot].Target;
                var workspace = new WorkspacePresentation(shown[peekSlot], null, null, null, Array.Empty<WorkspaceAction>(), Array.Empty<WorkspaceAction>(),
                    Array.Empty<CommandFeedback>(), new[] { new ActivityEntry(1, "2026-10-01T09:00:00.000Z", ActivityKind.Tool, "edit: src/orders/history.ts", false) });
                peek.Show(target, characters.ConvertAll(character => character.Target), PeekCard.Of(workspace), 1f, aboveCharacter: desk);
                banner.gameObject.SetActive(!AmbientCover.Any);
                if (banner.gameObject.activeSelf) failures.Add(name + ": the banner still shows while the peek is where it goes.");
                WorkspaceRender.ForceMeshes(root);
                failures.AddRange(Checks(name + " with the peek", root, eyes, characters, banner, peek.Card));
                failures.AddRange(WorkspaceRender.AllShowLiterally(peek.gameObject, "stage render " + name + " peek", eyes));
                var withPeek = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + "-peek.png"), withPeek.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(withPeek);
                var peekCloseUp = CloseUp(camera, texture, peek.transform.position, out _);
                File.WriteAllBytes(Path.Combine(folder, name + "-peek-closeup.png"), peekCloseUp.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(peekCloseUp);
                UnityEngine.Object.DestroyImmediate(peek.gameObject);
                banner.gameObject.SetActive(!AmbientCover.Any);
                if (!banner.gameObject.activeSelf) failures.Add(name + ": the banner does not come back when the peek goes.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>
        /// The stage's characters, its banner and a peek built and shown under a parent not yet shown, as
        /// the stage's arc is until it is placed, then shown: each laid as if built in view, every pill
        /// round its words. The practice stage's marks, and then another state on each, as work goes on.
        /// </summary>
        private static IEnumerable<string> RenderBuiltHidden(string name, string folder, IReadOnlyList<CharacterPresentation> shown,
            IReadOnlyList<CharacterPresentation> later, int peekSlot)
        {
            var failures = new List<string>();
            var root = new GameObject("Stage render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var arc = new GameObject("Arc, hidden until placed");
                arc.transform.SetParent(root.transform, false);
                arc.SetActive(false);
                var radius = CharacterStage.DefaultDistance;
                var characters = WorkspaceRender.Lineup(arc.transform, eyes, radius, null, (_, slot) => shown[slot]);
                var bannerRoot = new GameObject("Banner root").transform;
                bannerRoot.SetParent(arc.transform, false);
                bannerRoot.position = eyes + new Vector3(0f, CharacterStage.BannerTop(radius, CharacterStage.DefaultHeightFromEyes), radius);
                bannerRoot.localScale = Vector3.one * radius;
                var banner = StageBanner.Create(bannerRoot);
                banner.Show("Practice: nothing here runs on " + HostText.Your, BannerKind.Practice, null);
                var peek = PeekLabel.Create(arc.transform);
                var workspace = new WorkspacePresentation(shown[peekSlot], null, null, null, Array.Empty<WorkspaceAction>(), Array.Empty<WorkspaceAction>(),
                    Array.Empty<CommandFeedback>(), Array.Empty<ActivityEntry>());
                peek.Show(characters[peekSlot].Target, characters.ConvertAll(character => character.Target), PeekCard.Of(workspace), 1f, aboveCharacter: false);
                arc.SetActive(true);
                // The banner steps aside for the peek, as on the stage.
                banner.gameObject.SetActive(!AmbientCover.Any);
                WorkspaceRender.ForceMeshes(root);
                failures.AddRange(PillsHoldTheirWords(name, root));
                failures.AddRange(Checks(name, root, eyes, characters, banner, peek.Card));
                var whole = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), whole.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(whole);

                // Shown again with other states once in view: still round their words.
                for (var slot = 0; slot < characters.Count && slot < later.Count; slot++) characters[slot].View.Show(later[slot]);
                WorkspaceRender.ForceMeshes(root);
                failures.AddRange(PillsHoldTheirWords(name + " later", root));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>
        /// Every badge's and mark's words lie inside its pill, clear of its icon, as drawn: the words' own
        /// bounds against the pill's width and the icon's em, in the badge's units.
        /// </summary>
        private static IEnumerable<string> PillsHoldTheirWords(string name, GameObject root)
        {
            var failures = new List<string>();
            var em = GlazeTokens.Units(GlazeIcons.BadgeDegrees);
            void Judge(string what, TMPro.TextMeshPro word, TMPro.TextMeshPro icon, float halfWidth)
            {
                if (string.IsNullOrEmpty(word.text)) return;
                var bounds = word.textBounds;
                var left = word.transform.localPosition.x + bounds.min.x;
                var right = word.transform.localPosition.x + bounds.max.x;
                if (bounds.size.x <= 0f || left < -halfWidth - 1e-4f || right > halfWidth + 1e-4f)
                {
                    failures.Add(name + ": " + what + "'s words \"" + word.text + "\" run outside its pill (" + GlazeChecks.Degrees(GlazeTokens.DegreesOf(right - left))
                        + " degrees of words in " + GlazeChecks.Degrees(GlazeTokens.DegreesOf(2f * halfWidth)) + " of pill).");
                }
                else if (icon.gameObject.activeSelf && left < icon.transform.localPosition.x + em / 2f - 1e-4f)
                {
                    failures.Add(name + ": " + what + "'s icon lies over its words \"" + word.text + "\".");
                }
            }
            foreach (var badge in root.GetComponentsInChildren<StateBadgeView>(false))
            {
                Judge(badge.transform.parent.name + "'s badge", badge.Word, badge.Icon, badge.Width / badge.transform.localScale.x / 2f);
            }
            foreach (var mark in root.GetComponentsInChildren<MarkTag>(false)) Judge(mark.transform.parent.name + "'s mark", mark.Word, mark.Icon, mark.Width / 2f);
            return failures;
        }

        /// <summary>The interface's rules on the stage as built: apart, large enough, opaque, every state in words.</summary>
        private static IEnumerable<string> Checks(string name, GameObject root, Vector3 eyes, List<(CharacterView View, CharacterTarget Target)> characters,
            StageBanner banner, PeekCardView? peek)
        {
            var failures = new List<string>();
            var labels = characters.Select(character => GlazeChecks.Of(character.View.WorkstreamId + "'s label", eyes, character.View.Label.gameObject)).ToList();
            var bodies = characters.Select(character => WorkspaceRender.BodyExtent(character.View, eyes)).ToList();

            // Different things keep a degree apart: labels from each other and from other bodies, and the banner and the peek from all.
            var apart = new List<GlazeChecks.Extent>(labels);
            if (banner.gameObject.activeInHierarchy) apart.Add(GlazeChecks.Of("the banner", eyes, banner.gameObject));
            if (peek != null) apart.Add(GlazeChecks.Of("the peek", eyes, peek.gameObject));
            failures.AddRange(GlazeChecks.Apart(apart).Select(failure => name + ": " + failure));
            for (var index = 0; index < characters.Count; index++)
            {
                var others = apart.Where((_, other) => other != index).ToList();
                others.Add(bodies[index]);
                foreach (var failure in GlazeChecks.Apart(others))
                {
                    if (failure.Contains(bodies[index].Name)) failures.Add(name + ": " + failure);
                }
                // Its own label hangs close under it, but never touches it.
                var own = labels[index].Apart(bodies[index]);
                if (own < OwnLabelGapDegrees) failures.Add(name + ": " + labels[index] + " is " + GlazeChecks.Degrees(own) + " degrees from its own body.");
            }

            failures.AddRange(GlazeChecks.TextLargeEnough(root, eyes, name));
            // The stage's labels face the eyes, leaning back on a desk, so every word reads at 14 dp or more as the eyes see it.
            failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
            var plates = characters.Select(character => character.View.Label.Plate).ToList();
            plates.Add(banner.Plate);
            if (peek != null) plates.Add(peek.Card);
            failures.AddRange(GlazeChecks.PlatesOpaque(plates, name));
            var badges = characters.Select(character => character.View.Label.Badge).ToList();
            if (peek != null) badges.Add(peek.Badge);
            failures.AddRange(GlazeChecks.BadgesSayTheirState(badges, name));
            failures.AddRange(BadgesKeepTheirRoom(name, characters));
            failures.AddRange(PillsHoldTheirWords(name, root));
            failures.AddRange(WorkspaceRender.AllShowLiterally(root, "stage render " + name, eyes));
            var lowest = labels.Min(label => label.Bottom);
            Debug.Log("Halcyonic: stage render " + name + ": labels end " + GlazeChecks.Degrees(-lowest) + " degrees below eye level; widest "
                + GlazeChecks.Degrees(labels.Max(label => label.Right - label.Left)) + " degrees.");
            return failures;
        }

        /// <summary>
        /// Every label's badge has the widest plate's room, 10.5 degrees, and shows its icon exactly
        /// when the badge with it fits there; the log says each badge's width and whether its icon shows.
        /// </summary>
        private static IEnumerable<string> BadgesKeepTheirRoom(string name, List<(CharacterView View, CharacterTarget Target)> characters)
        {
            var failures = new List<string>();
            var room = GlazeTokens.Units(CharacterLabelView.MaxWidthDegrees);
            var widths = new List<string>();
            foreach (var (view, _) in characters)
            {
                var badge = view.Label.Badge;
                if (badge.Shown == null) continue;
                var what = name + ": " + view.WorkstreamId + "'s badge, \"" + badge.Shown.Text + "\",";
                if (badge.MaxWidth is not float most || Mathf.Abs(most - room) > 1e-6f)
                {
                    failures.Add(what + " does not have the plate's room of " + GlazeChecks.Degrees(CharacterLabelView.MaxWidthDegrees) + " degrees.");
                    continue;
                }
                if (badge.ShowsIcon && badge.Width > room + 1e-5f) failures.Add(what + " is wider than its room with its icon.");
                if (!badge.ShowsIcon && badge.Width + StateBadgeView.IconWidth <= room + 1e-5f) failures.Add(what + " leaves out its icon, which had room.");
                widths.Add("\"" + badge.Shown.Text + "\" " + GlazeChecks.Degrees(GlazeTokens.DegreesOf(badge.Width))
                    + (badge.ShowsIcon ? " with its icon" : " with no icon (" + GlazeChecks.Degrees(GlazeTokens.DegreesOf(badge.Width + StateBadgeView.IconWidth)) + " with it)"));
            }
            Debug.Log("Halcyonic: stage render " + name + ": badges, at most " + GlazeChecks.Degrees(CharacterLabelView.MaxWidthDegrees) + " degrees with an icon: "
                + string.Join("; ", widths) + ".");
            return failures;
        }

        /// <summary>
        /// The middle two labels as a Quest 3 shows them, to judge them by eye, and two pixel checks on
        /// a label with no light of its character behind it: its plate in its token's colour, and its
        /// title's contrast with the plate.
        /// </summary>
        private static IEnumerable<string> CloseUps(string name, string folder, Camera camera, RenderTexture texture,
            List<(CharacterView View, CharacterTarget Target)> characters)
        {
            var failures = new List<string>();
            var unlit = Enumerable.Range(0, characters.Count).First(index => CharacterCues.Of(characters[index].View.Presentation!).Halo == CharacterHalo.None);
            foreach (var slot in new[] { 2, 3, unlit }.Distinct())
            {
                var label = characters[slot].View.Label;
                var center = label.Plate.transform.position;
                var image = CloseUp(camera, texture, center, out var pose);
                File.WriteAllBytes(Path.Combine(folder, name + "-label-" + slot.ToString(CultureInfo.InvariantCulture) + ".png"), image.EncodeToPNG());
                if (slot == unlit)
                {
                    var rotation = camera.transform.rotation;
                    var fieldOfView = camera.fieldOfView;
                    camera.transform.rotation = pose.rotation;
                    camera.fieldOfView = pose.fieldOfView;
                    // Inside the plate's side padding, halfway up: plate and nothing else.
                    var plate = label.Plate.transform;
                    var inset = GlazeTokens.Units(0.35f) / Mathf.Max(label.Plate.Size.x, 1e-4f);
                    var point = camera.WorldToScreenPoint(plate.TransformPoint(new Vector3(-0.5f + inset, 0f, 0f)));
                    var pixel = new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
                    failures.AddRange(GlazeChecks.AsAuthored(image, pixel, Glaze.Panel, Glaze.PlateOpacity, name + " slot " + slot + "'s plate"));
                    var title = label.Title.GetComponent<Renderer>().bounds;
                    var low = camera.WorldToScreenPoint(title.min);
                    var high = camera.WorldToScreenPoint(title.max);
                    var rect = new RectInt(Mathf.FloorToInt(Mathf.Min(low.x, high.x)), Mathf.FloorToInt(Mathf.Min(low.y, high.y)),
                        Mathf.CeilToInt(Mathf.Abs(high.x - low.x)), Mathf.CeilToInt(Mathf.Abs(high.y - low.y)));
                    var contrast = GlazeChecks.Contrast(image, rect, image.GetPixel(pixel.x, pixel.y));
                    Debug.Log("Halcyonic: stage render " + name + ": slot " + slot + "'s title reaches " + contrast.ToString("0.0", CultureInfo.InvariantCulture)
                        + ":1 on its plate as drawn.");
                    if (contrast < 7f) failures.Add(name + ": slot " + slot + "'s title reaches only " + contrast.ToString("0.0", CultureInfo.InvariantCulture) + ":1 on its plate.");
                    camera.transform.rotation = rotation;
                    camera.fieldOfView = fieldOfView;
                }
                UnityEngine.Object.DestroyImmediate(image);
            }
            return failures;
        }

        /// <summary>
        /// Keep things still (ADR 0027) on the stage: stepped at the headset's rate for three seconds to stand at rest,
        /// then a second more, every character is the same picture, its pose, its surface's flow, its sweep ring and
        /// Waiting for you's halo all standing; let move again, the same second changes the picture, so the check
        /// sees motion where there is some. Sixty frames of the stage kept still allocate nothing.
        /// </summary>
        private static IEnumerable<string> KeptStill(string name, string folder, Camera camera, RenderTexture texture, List<(CharacterView View, CharacterTarget Target)> characters)
        {
            var failures = new List<string>();
            var was = GlazeMotion.Still;
            void Frames(int count)
            {
                for (var frame = 0; frame < count; frame++)
                {
                    foreach (var (view, _) in characters) view.AdvanceForRender(1f / 72f);
                }
            }
            var all = new RectInt(0, 0, Size, Size);
            Texture2D? first = null;
            Texture2D? second = null;
            Texture2D? moved = null;
            try
            {
                GlazeMotion.Still = true;
                Frames(216);
                first = WorkspaceRender.Render(camera, texture);
                Frames(72);
                second = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + "-kept-still.png"), second.EncodeToPNG());
                var (changed, largest) = WorkspaceRender.Compare(first, second, all);
                if (changed > 0)
                {
                    failures.Add(name + ": under Keep things still, " + changed + " pixels of the stage changed in a second, by up to "
                        + (largest * 255f).ToString("0", CultureInfo.InvariantCulture) + " of 255; nothing keeps moving on its own.");
                }
                if (!(GlazeChecks.Allocations(_ => Frames(1), 60) is (int every, int some, long bytes))) failures.Add(name + ": this editor cannot count allocations, so the stage kept still cannot be checked.");
                else
                {
                    if (every > 0) failures.Add(name + ": " + every + " of sixty frames of the stage kept still allocate in each of 3 tries; a frame allocates nothing.");
                    Debug.Log("Halcyonic: stage render " + name + ": sixty frames kept still: " + every + " allocate in every try, " + some + " in some; the quietest try counts " + bytes + " bytes on every thread.");
                }
                GlazeMotion.Still = false;
                Frames(72);
                moved = WorkspaceRender.Render(camera, texture);
                if (WorkspaceRender.Compare(second, moved, all).Changed == 0) failures.Add(name + ": let move, the stage did not change in a second either, so keeping it still checks nothing.");
            }
            finally
            {
                GlazeMotion.Still = was;
                foreach (var image in new[] { first, second, moved }) if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
            return failures;
        }

        /// <summary>
        /// Under Keep things still, Working, Running tests and State unknown still tell apart by their eyes: a working
        /// character looks down, one running tests a little down and open wider, one that can't be told half open and faint.
        /// </summary>
        private static IEnumerable<string> StillStatesDiffer(string name, Transform parent, Vector3 eyes, float radius)
        {
            var failures = new List<string>();
            var was = GlazeMotion.Still;
            var holder = new GameObject("Kept still states");
            holder.transform.SetParent(parent, false);
            var activities = new[] { CharacterActivity.Working, CharacterActivity.Verifying, CharacterActivity.Unknown };
            try
            {
                GlazeMotion.Still = true;
                var characters = WorkspaceRender.Lineup(holder.transform, eyes, radius, null, (_, slot) => Character(slot, "A task", activities[slot % activities.Length]));
                for (var frame = 0; frame < 216; frame++)
                {
                    foreach (var (view, _) in characters) view.AdvanceForRender(1f / 72f);
                }
                var poses = characters.Take(activities.Length).Select(character => character.View.PoseForRender).ToList();
                for (var one = 0; one < poses.Count; one++)
                {
                    for (var other = one + 1; other < poses.Count; other++)
                    {
                        var (a, b) = (poses[one], poses[other]);
                        if (Mathf.Abs(a.Open - b.Open) < 0.05f && Mathf.Abs(a.LookY - b.LookY) < 0.05f && Mathf.Abs(a.Ink - b.Ink) < 0.05f)
                        {
                            failures.Add(name + ": under Keep things still, " + activities[one] + " and " + activities[other] + " have the same eyes; their states still differ by eyes.");
                        }
                    }
                }
                if (poses[0].LookY > -0.5f) failures.Add(name + ": under Keep things still, a working character's eyes look up from its work.");
            }
            finally
            {
                GlazeMotion.Still = was;
                UnityEngine.Object.DestroyImmediate(holder);
            }
            return failures;
        }

        /// <summary>The eyes turned to <paramref name="center"/> at a Quest 3's pixels per degree; the pose it took, to project points the same way.</summary>
        private static Texture2D CloseUp(Camera camera, RenderTexture texture, Vector3 center, out (Quaternion rotation, float fieldOfView) pose)
        {
            var rotation = camera.transform.rotation;
            var fieldOfView = camera.fieldOfView;
            camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
            camera.fieldOfView = Size / QuestPixelsPerDegree;
            pose = (camera.transform.rotation, camera.fieldOfView);
            var image = WorkspaceRender.Render(camera, texture);
            camera.transform.rotation = rotation;
            camera.fieldOfView = fieldOfView;
            return image;
        }

        private static CharacterPresentation Character(int slot, string title, CharacterActivity activity, AttentionLevel attention = AttentionLevel.None,
            string[]? notes = null, string[]? details = null, int approvals = 0, bool synthetic = false, bool recorded = false, bool stale = false) =>
            new CharacterPresentation("render-" + slot.ToString(CultureInfo.InvariantCulture), title, activity, CharacterPresenter.LabelOf(activity), attention,
                notes ?? Array.Empty<string>(), approvals, synthetic, recorded, stale, details);

        /// <summary>The first six states, the two widest badges side by side at the end.</summary>
        private static IReadOnlyList<CharacterPresentation> StatesA() => new[]
        {
            Character(0, "Write the import parser", CharacterActivity.Idle),
            Character(1, "Add a search page", CharacterActivity.Starting),
            Character(2, "Paginate the order history endpoint", CharacterActivity.Working),
            Character(3, "Fix the flaky checkout test", CharacterActivity.Verifying),
            Character(4, "Add rate limiting to the sign-in endpoint", CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired,
                new[] { "It wants to use shell: make migrate", "It asks you: Which colour scheme?" }, approvals: 2),
            Character(5, "Upgrade the image pipeline", CharacterActivity.TurnFinished),
        };

        /// <summary>The rest of the states, a practice run, a last known state, and a title too long for two lines.</summary>
        private static IReadOnlyList<CharacterPresentation> StatesB() => new[]
        {
            Character(0, "Refresh the checkout copy", CharacterActivity.TurnFinished, AttentionLevel.Notice,
                new[] { "Checks: 1 failed, 23 passed" }, new[] { "1 failed, 23 passed" }),
            Character(1, "Send an order confirmation email", CharacterActivity.Failed, AttentionLevel.Notice,
                new[] { "Couldn't finish this round. Tell it to try again, or what to do instead." }, new[] { "Tell it to try again, or what to do instead." }),
            Character(2, "Tidy the release notes", CharacterActivity.Interrupted),
            Character(3, "Migrate the user table", CharacterActivity.Unknown, AttentionLevel.Notice, new[] { "Can't tell what it's doing right now." }, new[] { "" },
                synthetic: true),
            Character(4, "Rename the billing module", CharacterActivity.Working, stale: true),
            Character(5, "Write the onboarding guide for new contributors to the payments service and its webhooks", CharacterActivity.TurnFinished),
        };

        /// <summary>The recorded demonstration's kind of stage: every character marked Demo, two-line titles, the deepest labels.</summary>
        private static IReadOnlyList<CharacterPresentation> Demo() => new[]
        {
            Character(0, "Paginate the order history endpoint for large accounts", CharacterActivity.Working, synthetic: true, recorded: true),
            Character(1, "Send an order confirmation email after every purchase", CharacterActivity.TurnFinished, synthetic: true, recorded: true),
            Character(2, "Add rate limiting to the sign-in endpoint", CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired,
                new[] { "It wants to use shell: Run make migrate" }, approvals: 1, synthetic: true, recorded: true),
            Character(3, "Refresh the checkout copy for the spring sale", CharacterActivity.TurnFinished, synthetic: true, recorded: true),
            Character(4, "Upgrade the image pipeline to the new encoder", CharacterActivity.Verifying, synthetic: true, recorded: true),
            Character(5, "Tidy the release notes before the next version", CharacterActivity.Working, synthetic: true, recorded: true),
        };
    }
}
