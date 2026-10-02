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
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(Render("states-a", folder, StatesA(), desk: false, BannerKind.Live, "Connected to " + HostText.Your, null, peekSlot: 4));
                failures.AddRange(Render("states-b", folder, StatesB(), desk: false, BannerKind.NotLive,
                    "Last known: can't reach " + HostText.Your + ". Trying again… (connection refused)", null, peekSlot: 1));
                failures.AddRange(Render("demo", folder, Demo(), desk: false, BannerKind.Practice,
                    DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null), AmbientText.NeedsYouLine(1), peekSlot: 2));
                failures.AddRange(Render("desk", folder, StatesA(), desk: true, BannerKind.Live, "Connected to " + HostText.Your, null, peekSlot: 4));
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
                new[] { "Couldn't finish: The model provider rejected the request (rate limit)." }, new[] { "The model provider rejected the request (rate limit)." }),
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
