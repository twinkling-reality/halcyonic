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
    public static partial class StageRender
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

        /// <summary>
        /// The batch entry point: exits with 0 when every check passes, 1 otherwise. Every shader is compiled before it draws:
        /// while one compiles in the background the Editor draws its stand-in, a flat cyan, so a render taken during the
        /// compile and one after it differ by that stand-in alone, which the still check read as motion under Keep things
        /// still (b167's stage journey at Standard, the first pass, 2026-11-14). Nothing on the stage moved.
        /// </summary>
        public static void Check()
        {
            var asyncWas = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Debug.Log("Halcyonic: stage render: shaders compile before they draw (asynchronous compilation off for the run).");
            List<string> failures;
            try { failures = GlazeChecks.AtEachTextSize(Run); }
            finally { ShaderUtil.allowAsyncCompilation = asyncWas; }
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>Checks body contact through all existing character states on the optional shelf, at both text sizes.</summary>
        public static void AgentShelfCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(AgentShelf);
            foreach (var failure in failures) Debug.LogError("Halcyonic: agent shelf: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: agent shelf: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
        private static List<string> AgentShelf(string variant)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "AgentShelfRenders", variant));
            Directory.CreateDirectory(folder);
            var previousField = ViewField.Current;
            try
            {
                ViewField.Current = FieldChecks.Quest3S;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var widths = StandInHoldsTheTag(folder);
                var cases = CharacterShelfCases(folder, features: false);
                cases.InsertRange(0, widths);
                return cases;
            }
            finally { ViewField.Current = previousField; WorkspaceRender.KeepFontAssetsAsCommitted(); }
        }

        /// <summary>The longest name a walk gives a task (JUDGE's 64 characters), the widest tag a real name draws on the shelf.</summary>
        /// <summary>The least gap between neighbouring drawn tags that reads as two plates on Quest 3, in degrees (AUDIT's 16:46Z).</summary>
        private const float NeighbourTagsApartDegrees = 0.5f;

        private const string LongShelfName = "A page that greets each visitor by name and counts their visits!";

        /// <summary>
        /// The real condition behind the shelf's envelope (the lead's 16:4xZ; AUDIT3's 17:40Z ruling): the check stands what the room DRAWS. One page at
        /// the count the room stands (<see cref="CharacterStage.ShelfCapacity"/>, read from the code, never a constant here), beside New Project's place at the
        /// shelf's first place, at the real pitch of the room's places, every name a long one (64 characters), every task needing the person and every tag
        /// picked (its ring, the widest it draws). Each DRAWN tag (its words and its plate's glass, as JUDGE's walk measures it, never the plate's soft shadow)
        /// stands half a degree or more from its neighbours' tags and from New Project's words and glyph, and clear of every character's body, as the eyes see
        /// them. The least gaps are logged in degrees; any overlap fails. Raising the capacity makes this same check judge the new count.
        /// </summary>
        private static List<string> StandInHoldsTheTag(string folder)
        {
            var failures = new List<string>();
            // The ruled case, and the film's sentence working and needing the person, each drawn into a frame for the look (AUDIT 17:47Z).
            foreach (var (name, title, needs) in new[] { ("page-long-needs", LongShelfName, true), ("page-film-working", FilmShelfName, false), ("page-film-needs", FilmShelfName, true) })
                failures.AddRange(ShelfPageHoldsTheTag(folder, name, title, needs));
            failures.AddRange(NewProjectEasesClear(folder));
            return failures;
        }

        /// <summary>
        /// New Project's place as the room moves it when the tag beside it widens (AUDIT 2026-11-16T18:04Z): the film's sentence working, then
        /// needing the person, at 72 frames a second. The place eases to its new turn, never jumps (under 60 percent of the way after 0.1 s),
        /// stands on it exactly by 1 s and holds it to the pixel after; frames at 0, 0.5 and 1 s go to the folder for the look.
        /// </summary>
        private static List<string> NewProjectEasesClear(string folder)
        {
            var failures = new List<string>();
            var holder = new GameObject("New Project easing clear");
            var designWas = RoomDesign.Enabled;
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var scale = GlazeText.Scale.ToString("0.00", CultureInfo.InvariantCulture);
            try
            {
                RoomDesign.Enabled = true;
                var capacity = CharacterStage.ShelfCapacity;
                var places = CharacterStage.ShelfPlaces;
                var first = places - capacity;
                if (first == 0) return failures;
                var eyes = Vector3.zero;
                var pose = new Pose(new Vector3(0f, -0.45f, AgentShelfSearch.MinimumDistance), Quaternion.identity);
                var pitch = CharacterStage.PitchOf(places, CharacterStage.FittedWidth(places));
                float PlaceX(int place) => -pitch * (places - 1) / 2f + pitch * place;
                CharacterPresentation Shown(int index, bool needs) => needs
                    ? new CharacterPresentation("ease-tag-" + index, FilmShelfName, CharacterActivity.WaitingForHuman, "Needs You", AttentionLevel.ActionRequired, Array.Empty<string>(), 2, false, false, false)
                    : new CharacterPresentation("ease-tag-" + index, FilmShelfName, CharacterActivity.Working, CharacterPresenter.LabelOf(CharacterActivity.Working), AttentionLevel.None, Array.Empty<string>(), 0, false, false, false);
                var views = new List<CharacterView>();
                for (var index = 0; index < capacity; index++)
                {
                    var view = CharacterView.Create(holder.transform, "ease-tag-" + index);
                    view.transform.SetPositionAndRotation(pose.position + Vector3.right * PlaceX(first + index), Quaternion.Euler(0f, 180f, 0f));
                    view.transform.localScale = Vector3.one * AgentShelfDraft.Scale;
                    RoomNameTag.On(view).Picked = true;
                    view.Show(Shown(index, needs: false));
                    view.PlaceShelfLabel(eyes);
                    views.Add(view);
                }
                var lip = pose.position + Vector3.right * PlaceX(0) - Vector3.forward * (AgentShelfDraft.Depth / 2f);
                var away = lip - eyes;
                var place = new GameObject("New Project place").transform;
                place.SetParent(holder.transform, false);
                var spot = new Pose(lip, Quaternion.LookRotation(new Vector3(away.x, 0f, away.z), Vector3.up));
                place.SetPositionAndRotation(spot.position, spot.rotation);
                place.localScale = Vector3.one * away.magnitude;
                var button = RoomButton.Create(place, RoomNewProjectPlace.Words);
                var target = RoomType.Units(RoomButton.HeightDp);
                var under = RoomType.Units(RoomNameTag.UnderDegrees * RoomGeometry.DpPerDegree);
                button.Show(RoomNewProjectPlace.Words, GlazeIcon.CreateProject, isMain: false, available: true, new Vector2(0f, -under - target / 2f), button.Measure(RoomNewProjectPlace.Words, 0f).x);
                SettlePage(holder, views, button);
                var start = RoomNewProjectPlace.ClearTurn(place, button, holder.transform, eyes);
                foreach (var (view, index) in views.Select((view, index) => (view, index)))
                {
                    view.Show(Shown(index, needs: true));
                    view.PlaceShelfLabel(eyes);
                }
                const int fps = 72;
                place.SetPositionAndRotation(spot.position, spot.rotation);
                var camera = WorkspaceRender.MakeCamera(holder.transform, eyes, texture);
                var turned = start;
                var speed = 0f;
                var settled = new List<float>();
                var largest = 0f;
                float? atTenth = null;
                var end = start;
                var frames = fps * 6 / 5;
                var ends = new float[frames + 1];
                ends[0] = start;
                for (var frame = 0; frame <= frames; frame++)
                {
                    if (frame > 0)
                    {
                        // The tag's Needs You arrives as the room draws it, a frame at a time, and the place eases after it.
                        foreach (var view in views) view.AdvanceForRender(1f / fps);
                        WorkspaceRender.ForceMeshes(holder);
                        var was = turned;
                        place.SetPositionAndRotation(spot.position, spot.rotation);
                        end = RoomNewProjectPlace.ClearTurn(place, button, holder.transform, eyes);
                        ends[frame] = end;
                        turned = RoomNewProjectPlace.Eased(turned, end, ref speed, 1f / fps);
                        largest = Mathf.Max(largest, Mathf.Abs(turned - was));
                    }
                    // Framed on the page as it starts, with room for the chip arriving at the right and the place easing left.
                    if (frame == 0) { FramePage(camera, eyes, holder.transform); camera.fieldOfView += 10f; }
                    place.SetPositionAndRotation(spot.position, spot.rotation);
                    RoomNewProjectPlace.Turn(place, eyes, turned);
                    if (frame == Mathf.RoundToInt(0.1f * fps)) atTenth = turned;
                    if (frame >= fps) settled.Add(turned - ends[frame]);
                    if (frame == 0 || frame == fps / 2 || frame == fps)
                    {
                        var image = WorkspaceRender.Render(camera, texture);
                        File.WriteAllBytes(Path.Combine(folder, "page-film-ease-" + (frame / (float)fps).ToString("0.0", CultureInfo.InvariantCulture) + "s.png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                }
                var change = end - start;
                if (change < 0.2f) failures.Add("New Project's place needs no new turn when the film's tag comes to need the person (" + GlazeChecks.Degrees(change) + " degrees), so its easing is not shown, at text scale " + scale);
                if (atTenth is float tenth && tenth - start > 0.6f * change) failures.Add("New Project's place jumps: " + GlazeChecks.Degrees(tenth - start) + " of its " + GlazeChecks.Degrees(change) + " degrees in 0.1 s, at text scale " + scale);
                if (settled.Count == 0 || settled.Any(each => each != 0f)) failures.Add("New Project's place does not stand still on its turn from 1 s, at text scale " + scale);
                Debug.Log("Halcyonic: agent shelf: at text scale " + scale + ", New Project's place eases " + GlazeChecks.Degrees(change) + " degrees further left as the film's tag comes to need the person: "
                    + GlazeChecks.Degrees((atTenth ?? start) - start) + " in 0.1 s, at most " + largest.ToString("0.00", CultureInfo.InvariantCulture) + " in a frame, still from 1 s.");
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                RoomDesign.Enabled = designWas;
                UnityEngine.Object.DestroyImmediate(holder);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>A shelf page as it stands once arrived: each character a second on (its tag's Needs You come), New Project's button whole.</summary>
        private static void SettlePage(GameObject holder, IEnumerable<CharacterView> views, RoomButton? button)
        {
            foreach (var view in views) Advance(view, 1f);
            button?.Settle();
            WorkspaceRender.ForceMeshes(holder);
        }

        /// <summary>
        /// Aims <paramref name="camera"/> from <paramref name="eyes"/> at the middle of everything drawn under <paramref name="page"/>, and opens its
        /// view to hold it all with two degrees to spare each way, so the character, its whole tag and New Project's place stand whole in the frame.
        /// </summary>
        private static void FramePage(Camera camera, Vector3 eyes, Transform page)
        {
            float left = float.PositiveInfinity, right = float.NegativeInfinity, low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (var part in page.GetComponentsInChildren<Renderer>(false))
            {
                if (!part.enabled) continue;
                var bounds = part.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(corner % 2 == 0 ? bounds.min.x : bounds.max.x, corner < 4 ? bounds.min.y : bounds.max.y, corner % 4 < 2 ? bounds.min.z : bounds.max.z) - eyes;
                    var yaw = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
                    var pitch = Mathf.Atan2(point.y, new Vector2(point.x, point.z).magnitude) * Mathf.Rad2Deg;
                    left = Mathf.Min(left, yaw); right = Mathf.Max(right, yaw); low = Mathf.Min(low, pitch); high = Mathf.Max(high, pitch);
                }
            }
            if (float.IsPositiveInfinity(left)) return;
            camera.transform.SetPositionAndRotation(eyes, Quaternion.Euler(-(low + high) / 2f, (left + right) / 2f, 0f));
            camera.fieldOfView = Mathf.Clamp(Mathf.Max(right - left, high - low) + 4f, 10f, 120f);
        }

        /// <summary>The longest filmed task name (FILM's run sheet), drawn beside New Project's place for the look.</summary>
        private const string FilmShelfName = "Make a game where I catch falling stars.";

        private static List<string> ShelfPageHoldsTheTag(string folder, string name, string title, bool needs)
        {
            var failures = new List<string>();
            var holder = new GameObject("Drawn tags on a shelf page");
            var designWas = RoomDesign.Enabled;
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                RoomDesign.Enabled = true;
                var capacity = CharacterStage.ShelfCapacity;
                var places = CharacterStage.ShelfPlaces;
                var first = places - capacity;
                var eyes = Vector3.zero;
                var pose = new Pose(new Vector3(0f, -0.45f, AgentShelfSearch.MinimumDistance), Quaternion.identity);
                var width = CharacterStage.FittedWidth(places);
                var pitch = CharacterStage.PitchOf(places, width);
                float PlaceX(int place) => -pitch * (places - 1) / 2f + pitch * place;
                var views = new List<CharacterView>();
                for (var index = 0; index < capacity; index++)
                {
                    var view = CharacterView.Create(holder.transform, "shelf-tag-" + index);
                    view.transform.SetPositionAndRotation(pose.position + Vector3.right * PlaceX(first + index), Quaternion.Euler(0f, 180f, 0f));
                    view.transform.localScale = Vector3.one * AgentShelfDraft.Scale;
                    var tag = RoomNameTag.On(view);
                    view.Show(needs
                        ? new CharacterPresentation("shelf-tag-" + index, title, CharacterActivity.WaitingForHuman, "Needs You", AttentionLevel.ActionRequired, Array.Empty<string>(), 2, false, false, false)
                        : new CharacterPresentation("shelf-tag-" + index, title, CharacterActivity.Working, CharacterPresenter.LabelOf(CharacterActivity.Working), AttentionLevel.None, Array.Empty<string>(), 0, false, false, false));
                    view.PlaceShelfLabel(eyes);
                    // Picked, the widest a tag draws: its ring is the one a person sees on the task they point at (AUDIT3's D1).
                    tag.Picked = true;
                    views.Add(view);
                }
                // New Project's place, as RoomNewProjectPlace stands it: at the front lip of the shelf's first place, facing the eyes, sized
                // for them as the labels are, its middle on the tags' line under the characters' feet.
                RoomButton? newProject = null;
                if (first > 0)
                {
                    var lip = pose.position + Vector3.right * PlaceX(0) - Vector3.forward * (AgentShelfDraft.Depth / 2f);
                    var away = lip - eyes;
                    var place = new GameObject("New Project place").transform;
                    place.SetParent(holder.transform, false);
                    place.SetPositionAndRotation(lip, Quaternion.LookRotation(new Vector3(away.x, 0f, away.z), Vector3.up));
                    place.localScale = Vector3.one * away.magnitude;
                    newProject = RoomButton.Create(place, RoomNewProjectPlace.Words);
                    var target = RoomType.Units(RoomButton.HeightDp);
                    var under = RoomType.Units(RoomNameTag.UnderDegrees * RoomGeometry.DpPerDegree);
                    newProject.Show(RoomNewProjectPlace.Words, GlazeIcon.CreateProject, isMain: false, available: true, new Vector2(0f, -under - target / 2f),
                        newProject.Measure(RoomNewProjectPlace.Words, 0f).x);
                    // And clear of the page's tags, as the room stands it each frame, once the tag's Needs You has arrived and the button stands whole.
                    SettlePage(holder, views, newProject);
                    var before = place.position - eyes;
                    RoomNewProjectPlace.StandClear(place, newProject, holder.transform, eyes);
                    Debug.Log("Halcyonic: agent shelf: " + name + " at text scale " + GlazeText.Scale.ToString("0.00", CultureInfo.InvariantCulture) + ": New Project's place stands "
                        + Vector3.Angle(new Vector3(before.x, 0f, before.z), new Vector3(place.position.x - eyes.x, 0f, place.position.z - eyes.z)).ToString("0.0", CultureInfo.InvariantCulture)
                        + " degrees further left, clear of the tag.");
                }
                WorkspaceRender.ForceMeshes(holder);
                GlazeChecks.Extent Drawn(CharacterView view)
                {
                    var tag = view.GetComponent<RoomNameTag>()?.Tag;
                    var parts = tag == null ? new List<Renderer>() : tag.GetComponentsInChildren<TMPro.TMP_Text>(false).Select(text => text.GetComponent<Renderer>())
                        .Concat(tag.GetComponentsInChildren<Halcyonic.XR.UI.RoomSurface>(false).Select(plate => (Renderer)plate.Glass))
                        .Where(part => part != null && part.enabled).ToList();
                    return GlazeChecks.Of(view.name + "'s tag", eyes, parts);
                }
                var tags = views.Select(Drawn).ToList();
                var bodies = views.Select(view => GlazeChecks.Of(view.name + "'s body", eyes, view.Body.GetComponentsInChildren<Renderer>(false))).ToList();
                var leastNeighbour = float.PositiveInfinity;
                var leastBody = float.PositiveInfinity;
                var leastNewProject = float.PositiveInfinity;
                var scale = GlazeText.Scale.ToString("0.00", CultureInfo.InvariantCulture) + " (" + name + ")";
                GlazeChecks.Extent? newProjectDrawn = null;
                if (first > 0)
                {
                    var parts = newProject == null ? new List<Renderer>() : newProject.GetComponentsInChildren<TMPro.TMP_Text>(false).Select(text => text.GetComponent<Renderer>())
                        .Concat(newProject.Glyph.GetComponentsInChildren<Renderer>(false)).Where(part => part != null && part.enabled).ToList();
                    if (parts.Count == 0) failures.Add("New Project's place draws no words or glyph beside the shelf page, at text scale " + scale);
                    else newProjectDrawn = GlazeChecks.Of("New Project's place", eyes, parts);
                }
                for (var index = 0; index < capacity; index++)
                {
                    if (index + 1 < capacity)
                    {
                        var apart = tags[index].Apart(tags[index + 1]);
                        leastNeighbour = Mathf.Min(leastNeighbour, apart);
                        // Under half a degree two plates read as one joined plate on the headset (AUDIT's 16:46Z floor).
                        if (apart < NeighbourTagsApartDegrees) failures.Add("the drawn tags of places " + index + " and " + (index + 1) + " meet, " + GlazeChecks.Degrees(apart) + " degrees apart, at text scale " + scale);
                    }
                    if (newProjectDrawn is { } place)
                    {
                        var apart = tags[index].Apart(place);
                        leastNewProject = Mathf.Min(leastNewProject, apart);
                        if (apart < NeighbourTagsApartDegrees) failures.Add("place " + index + "'s drawn tag meets New Project's place, " + GlazeChecks.Degrees(apart) + " degrees apart, at text scale " + scale);
                    }
                    for (var other = 0; other < capacity; other++)
                    {
                        var clear = tags[index].Apart(bodies[other]);
                        leastBody = Mathf.Min(leastBody, clear);
                        if (clear <= 0f) failures.Add("place " + index + "'s drawn tag stands over place " + other + "'s character, at text scale " + scale);
                    }
                }
                string Least(float degrees) => float.IsPositiveInfinity(degrees) ? "none on the page" : GlazeChecks.Degrees(degrees) + " degrees";
                Debug.Log("Halcyonic: agent shelf: at text scale " + scale + ", a page of " + capacity + " (its places " + places + " with New Project's) of " + title.Length + "-character tags " + (needs ? "needing the person" : "working") + ": least gap between neighbouring tags "
                    + Least(leastNeighbour) + ", least gap from a tag to New Project's place " + Least(leastNewProject) + ", least gap from a tag to any character " + Least(leastBody) + ".");
                // The frame, from the eyes, toward the page and New Project's place together.
                var camera = WorkspaceRender.MakeCamera(holder.transform, eyes, texture);
                FramePage(camera, eyes, holder.transform);
                var image = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                RoomDesign.Enabled = designWas;
                UnityEngine.Object.DestroyImmediate(holder);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
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
                // The peek's other two marks, each drawn once (AUDIT2 2026-11-16 11:31Z): practice builds nothing, and a recording of
                // real agent work plays back; the same tasks as the demo's, so only the mark and its line differ.
                failures.AddRange(Render("practice", folder, Marked(synthetic: true, recorded: false), desk: false, BannerKind.Practice,
                    DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null), AmbientText.NeedsYouLine(1), peekSlot: 2));
                // The recorded run stands under its own notice, as the bar says it while a recording plays.
                failures.AddRange(Render("recorded", folder, Marked(synthetic: false, recorded: true), desk: false, BannerKind.Practice,
                    DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null, recordedRun: true), AmbientText.NeedsYouLine(1), peekSlot: 2));
                // A name past four lines, never in a filmed or fixture state: its last line stops at a word with its tail faded.
                failures.AddRange(Render(LongNameScene, folder, LongName(), desk: false, BannerKind.Practice,
                    DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null), AmbientText.NeedsYouLine(1), peekSlot: 2));
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
                WorkspaceDirector.EnsureWorkspace();
                void One<T>(string name) where T : UnityEngine.Object
                {
                    var count = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                    if (count != 1) failures.Add("in " + what + ", the bootstrap leaves " + count + " " + name + ", not one.");
                }
                var stages = UnityEngine.Object.FindObjectsByType<CharacterStage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                var expected = what == "the stage scene" ? 1 : 0;
                if (stages.Length != expected) failures.Add("in " + what + ", bootstrap created a mandatory character stage.");
                foreach (var stage in stages) if (stage.Visible) failures.Add("bootstrap exposed agents before placement and enabling.");
                One<WorkspaceDirector>("workspaces");
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
                foreach (var (view, _) in characters) view.HoldLabelWordsAsSeen(eyes);

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
                if (!desk) KeepBannerClearOfLabels(bannerRoot, banner, characters, eyes, radius);
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
                // Each label held to 14 dp as it is placed, which is when the stage shows: measured shown, then hidden again.
                arc.SetActive(true);
                foreach (var (view, _) in characters) view.HoldLabelWordsAsSeen(eyes);
                arc.SetActive(false);
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
                KeepBannerClearOfLabels(bannerRoot, banner, characters, eyes, radius);
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
                var halfWidth = badge.Width / badge.transform.localScale.x / 2f;
                Judge(badge.transform.parent.name + "'s badge", badge.Word, badge.Icon, halfWidth);
                // Its count, where more than one waits, a figure of its own inside the pill's right end, clear of its word, the word
                // alone in its words (AUDIT 2026-11-16 11:31Z: never "Needs You · 2").
                if (badge.Shown is StateBadge shownBadge && shownBadge.Count > 1 != badge.ShowsCount)
                    failures.Add(name + ": " + badge.transform.parent.name + "'s badge waits on " + shownBadge.Count + " but " + (badge.ShowsCount ? "shows" : "draws no") + " count.");
                if (!badge.ShowsCount) continue;
                var figure = badge.CountFigure;
                figure.ForceMeshUpdate(true);
                var figureLeft = figure.transform.localPosition.x + figure.textBounds.min.x;
                var figureRight = figure.transform.localPosition.x + figure.textBounds.max.x;
                var wordRight = badge.Word.transform.localPosition.x + badge.Word.textBounds.max.x;
                if (figureRight > halfWidth + 1e-4f || figureLeft < wordRight + GlazeTokens.Units(0.2f) || badge.Word.text.Any(char.IsDigit))
                    failures.Add(name + ": " + badge.transform.parent.name + "'s badge count \"" + figure.text + "\" stands outside its pill's right end, against its word, or in the word \""
                        + badge.Word.text + "\".");
            }
            foreach (var mark in root.GetComponentsInChildren<MarkTag>(false)) Judge(mark.transform.parent.name + "'s mark", mark.Word, mark.Icon, mark.Width / 2f);
            return failures;
        }

        /// <summary>
        /// Lowers the banner from where the stage puts it until it stands <see cref="BannerClearDegrees"/> from every label as
        /// the eyes see it, its flat ends included, measured as the stage's apart check measures: a name on four lines hangs
        /// lower than the stage's place allows for (AUDIT 2026-11-16 11:49Z and 11:55Z: measured by the labels drawn, not the
        /// worst case). Short names leave it where it stands. The product draws no banner; this is the render's.
        /// </summary>
        private static void KeepBannerClearOfLabels(Transform bannerRoot, StageBanner banner, List<(CharacterView View, CharacterTarget Target)> characters,
            Vector3 eyes, float radius)
        {
            if (!banner.gameObject.activeInHierarchy) return;
            for (var pass = 0; pass < 8; pass++)
            {
                var drawn = GlazeChecks.Of("the banner", eyes, banner.gameObject);
                var nearest = characters.Min(character => GlazeChecks.Of("label", eyes, character.View.Label.gameObject).Apart(drawn));
                if (nearest >= BannerClearDegrees) return;
                bannerRoot.position += Vector3.down * (Mathf.Tan((BannerClearDegrees - nearest + 0.05f) * Mathf.Deg2Rad) * radius * 1.2f);
                WorkspaceRender.ForceMeshes(bannerRoot.gameObject);
            }
        }

        /// <summary>How far the banner keeps from every label in the render: the stage's degree apart and a quarter more.</summary>
        private const float BannerClearDegrees = 1.25f;

        /// <summary>The one scene whose name is over the four-line measure, so its label stops at a word and fades its tail.</summary>
        private const string LongNameScene = "long-name";

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
            // The peek's lines whole and none ending on a word alone where they run four or more (AUDIT's 11:31Z BLOCK, a and b:
            // "agent." alone at Standard, "make migr..." cut at the larger size).
            if (peek != null)
                foreach (var line in peek.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                {
                    line.ForceMeshUpdate(true);
                    var shownText = line.GetParsedText();
                    var info = line.textInfo;
                    if (line.isTextTruncated || shownText.Contains("…")) failures.Add(name + ": the peek's line \"" + shownText + "\" is cut");
                    if (info.lineCount < 2 || shownText.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length < 4) continue;
                    var last = info.lineInfo[info.lineCount - 1];
                    var lastWords = 0;
                    var inWord = false;
                    for (var index = last.firstCharacterIndex; index <= last.lastCharacterIndex && index < info.characterCount; index++)
                    {
                        var isSpace = char.IsWhiteSpace(info.characterInfo[index].character);
                        if (!isSpace && !inWord) lastWords++;
                        inWord = !isSpace;
                    }
                    if (lastWords == 1) failures.Add(name + ": the peek's line \"" + shownText + "\" ends on one word alone");
                }
            // Every character's name on the stage whole, never ending in an ellipsis (AUDIT's 11:42Z), on up to four lines; only
            // the long-name scene's name, over that measure, stops at a word break with its tail faded (11:55Z).
            foreach (var (view, _) in characters)
                foreach (var text in view.Label.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                {
                    text.ForceMeshUpdate(true);
                    var isName = text == view.Label.Title || text == view.Label.ShortTitle;
                    if (isName && view.Label.NameFaded)
                    {
                        var info = text.textInfo;
                        var whole = LabelText.ForTextMeshPro(view.Label.Name);
                        var drawnText = text.text;
                        if (!name.StartsWith(LongNameScene, StringComparison.Ordinal))
                            failures.Add(name + ": " + view.WorkstreamId + "'s name \"" + whole + "\" is over " + CharacterLabelView.MostTitleLines + " lines and fades, in a fixture state");
                        else if (!whole.StartsWith(drawnText, StringComparison.Ordinal) || drawnText.Length >= whole.Length || !char.IsWhiteSpace(whole[drawnText.Length]))
                            failures.Add(name + ": the long name's drawn words \"" + drawnText + "\" do not stop at a word break of \"" + whole + "\"");
                        else if (info.lineCount > CharacterLabelView.MostTitleLines || text.isTextTruncated || drawnText.Contains("…"))
                            failures.Add(name + ": the long name is drawn on " + info.lineCount + " lines, or cut, or with an ellipsis");
                        else
                            Debug.Log("Halcyonic: " + name + ": the name over " + CharacterLabelView.MostTitleLines + " lines, \"" + whole + "\", stops at \"" + drawnText
                                + "\" on " + info.lineCount + " lines, its tail faded.");
                        continue;
                    }
                    if (text.isTextTruncated || text.GetParsedText().Contains("…"))
                        failures.Add(name + ": " + view.WorkstreamId + "'s label \"" + text.GetParsedText() + "\" is cut");
                }
            // No two of the peek's drawn texts cross (AUDIT's 11:42Z: "Open to Answer" ran into "1 More Waiting"), each read by its
            // drawn letters' bounds in the peek's own space.
            if (peek != null)
            {
                var drawn = peek.GetComponentsInChildren<TMPro.TextMeshPro>(false).Where(each => each.textInfo.characterCount > 0).ToList();
                Rect Box(TMPro.TextMeshPro each)
                {
                    var bounds = each.textBounds;
                    var low = peek.transform.InverseTransformPoint(each.transform.TransformPoint(bounds.min));
                    var high = peek.transform.InverseTransformPoint(each.transform.TransformPoint(bounds.max));
                    return Rect.MinMaxRect(Mathf.Min(low.x, high.x), Mathf.Min(low.y, high.y), Mathf.Max(low.x, high.x), Mathf.Max(low.y, high.y));
                }
                for (var first = 0; first < drawn.Count; first++)
                for (var second = first + 1; second < drawn.Count; second++)
                    if (Box(drawn[first]).Overlaps(Box(drawn[second])))
                        failures.Add(name + ": the peek's \"" + drawn[first].GetParsedText() + "\" and \"" + drawn[second].GetParsedText() + "\" draw over each other");
            }
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
        /// Everything a renderer under <paramref name="views"/> draws with, by its path: whether it draws, where, its material and
        /// shader, every shader property its block or material holds, and a label's words, colour and size; and the stage's
        /// global light and noise. Numbers in round-trip form, so any change at all shows.
        /// </summary>
        private static SortedDictionary<string, string> DrawnValues(IEnumerable<CharacterView> views)
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            values["global light"] = Shader.GetGlobalVector("_HalcyonicKeyLight").ToString("R");
            values["global noise"] = (Shader.GetGlobalTexture("_HalcyonicNoise")?.GetInstanceID() ?? 0).ToString(CultureInfo.InvariantCulture);
            var block = new MaterialPropertyBlock();
            var index = 0;
            foreach (var view in views)
            {
                var root = view.transform;
                string PathOf(Transform each)
                {
                    var parts = new List<string>();
                    for (var at = each; at != null && at != root; at = at.parent) parts.Add(at.name);
                    parts.Add(root.name + "#" + index);
                    parts.Reverse();
                    return string.Join("/", parts);
                }
                foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                {
                    var key = PathOf(renderer.transform) + " (" + renderer.GetType().Name + ")";
                    var material = renderer.sharedMaterial;
                    values[key + " drawn"] = renderer.enabled + " " + renderer.gameObject.activeInHierarchy + " " + renderer.localToWorldMatrix.ToString("R")
                        + " " + renderer.bounds.ToString("R") + " material " + (material != null ? material.GetInstanceID() : 0) + " shader " + material?.shader?.name;
                    if (material == null || material.shader == null) continue;
                    renderer.GetPropertyBlock(block);
                    var shader = material.shader;
                    for (var property = 0; property < shader.GetPropertyCount(); property++)
                    {
                        var propertyName = shader.GetPropertyName(property);
                        var value = shader.GetPropertyType(property) switch
                        {
                            UnityEngine.Rendering.ShaderPropertyType.Color => block.HasColor(propertyName) ? "block " + block.GetColor(propertyName).ToString("R") : material.GetColor(propertyName).ToString("R"),
                            UnityEngine.Rendering.ShaderPropertyType.Vector => block.HasVector(propertyName) ? "block " + block.GetVector(propertyName).ToString("R") : material.GetVector(propertyName).ToString("R"),
                            UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range =>
                                block.HasFloat(propertyName) ? "block " + block.GetFloat(propertyName).ToString("R", CultureInfo.InvariantCulture) : material.GetFloat(propertyName).ToString("R", CultureInfo.InvariantCulture),
                            UnityEngine.Rendering.ShaderPropertyType.Texture => (block.HasTexture(propertyName) ? "block " + block.GetTexture(propertyName)?.GetInstanceID() : "" + material.GetTexture(propertyName)?.GetInstanceID()),
                            _ => "",
                        };
                        values[key + " " + propertyName] = value;
                    }
                }
                foreach (var label in view.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    values[PathOf(label.transform) + " (text)"] = label.isActiveAndEnabled + " \"" + label.text + "\" " + label.color.ToString("R") + " " + label.alpha.ToString("R", CultureInfo.InvariantCulture)
                        + " " + label.fontSize.ToString("R", CultureInfo.InvariantCulture);
                index++;
            }
            return values;
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
                var valuesBefore = CharacterFrameValues(characters.Select(each => each.View));
                var drawnBefore = DrawnValues(characters.Select(each => each.View));
                first = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + "-kept-still-before.png"), first.EncodeToPNG());
                Frames(72);
                second = WorkspaceRender.Render(camera, texture);
                SaveCharacterFrameDifferences(folder, name, valuesBefore, CharacterFrameValues(characters.Select(each => each.View)));
                File.WriteAllBytes(Path.Combine(folder, name + "-kept-still.png"), second.EncodeToPNG());
                var (changed, largest) = WorkspaceRender.Compare(first, second, all);
                // What each renderer under the characters drew with, labels and badges included, so a still failure names what moved
                // (AUDIT's 06:08Z: b167's 62 pixels sat at the characters' edges, none of them a shader's stand-in).
                var drawnAfter = DrawnValues(characters.Select(each => each.View));
                var drawnMoved = drawnBefore.Where(pair => !drawnAfter.TryGetValue(pair.Key, out var current) || current != pair.Value).Select(pair => pair.Key)
                    .Concat(drawnAfter.Keys.Where(key => !drawnBefore.ContainsKey(key))).Distinct().ToList();
                Debug.Log("Halcyonic: stage render " + name + ": kept still, " + drawnMoved.Count + " drawn values of " + drawnBefore.Count + " differ between the two renders.");
                foreach (var key in drawnMoved.Take(40))
                    Debug.Log("Halcyonic: stage render " + name + ": kept still, drawn value " + key + ": "
                        + (drawnBefore.TryGetValue(key, out var drawnWas) ? drawnWas : "absent") + " -> " + (drawnAfter.TryGetValue(key, out var drawnNow) ? drawnNow : "absent"));
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
        /// character looks down, one running tests a little down and open wider, one that can't be told half open, faint,
        /// its lids uneven and its look held aside and a little up.
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
                // One that can't be told holds a still look aside and a little up, its lids uneven: never centred, like a sleepy Working.
                var unsure = poses[2];
                if (Mathf.Abs(unsure.LookX) < 0.3f || unsure.LookY < 0.15f || Mathf.Abs(unsure.Uneven) < 0.15f)
                {
                    failures.Add(name + ": under Keep things still, State unknown's eyes stand at " + unsure.LookX.ToString("0.00", CultureInfo.InvariantCulture) + " across and "
                        + unsure.LookY.ToString("0.00", CultureInfo.InvariantCulture) + " up, lids " + unsure.Uneven.ToString("0.00", CultureInfo.InvariantCulture)
                        + " uneven; it holds a look aside and a little up, one lid lower than the other.");
                }
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

        /// <summary>The rest of the states, a practice run, a last known state, and a title too long for two lines, as long as a person would give.</summary>
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
            Character(5, "Write the onboarding guide for the payments service webhooks", CharacterActivity.TurnFinished),
        };

        /// <summary>The recorded demonstration's kind of stage: every character marked Demo, two-line titles, the deepest labels.</summary>
        /// <summary>The demo's tasks with one mark: practice (synthetic, not recorded) or a recording of real work (recorded, not synthetic).</summary>
        private static IReadOnlyList<CharacterPresentation> Marked(bool synthetic, bool recorded) => new[]
        {
            Character(0, "Paginate the order history endpoint for large accounts", CharacterActivity.Working, synthetic: synthetic, recorded: recorded),
            Character(1, "Send an order confirmation email after every purchase", CharacterActivity.TurnFinished, synthetic: synthetic, recorded: recorded),
            Character(2, "Add rate limiting to the sign-in endpoint", CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired,
                new[] { "It wants to use shell: Run make migrate" }, approvals: 1, synthetic: synthetic, recorded: recorded),
            Character(3, "Refresh the checkout copy for the spring sale", CharacterActivity.TurnFinished, synthetic: synthetic, recorded: recorded),
            Character(4, "Upgrade the image pipeline to the new encoder", CharacterActivity.Verifying, synthetic: synthetic, recorded: recorded),
            Character(5, "Tidy the release notes before the next version", CharacterActivity.Working, synthetic: synthetic, recorded: recorded),
        };

        /// <summary>The demo's tasks, one of them named past the four-line measure: never filmed, drawn once to show its fade.</summary>
        private static IReadOnlyList<CharacterPresentation> LongName()
        {
            var tasks = Demo().ToArray();
            tasks[4] = Character(4, "Move every payment, refund and payout report from the nightly batch job onto the streaming pipeline, keep the old"
                + " exports running until finance signs off on the new numbers, and write the runbook for both", CharacterActivity.Verifying,
                synthetic: true, recorded: true);
            return tasks;
        }

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
