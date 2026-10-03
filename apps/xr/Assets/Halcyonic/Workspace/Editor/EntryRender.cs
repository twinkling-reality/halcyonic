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
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders every screen of the entry panel and the Settings sheet over the stage, with the
    /// characters 2.4 m away and on a desk half a meter away, and checks what the headset showed wrong
    /// with the New work panel: that the panel covers no character, lets nothing behind it show
    /// through and stays in the comfortable band; that none of Halcyonic's own words is cut short; and
    /// that names and titles from outside show by the one rule. Creating is New project's, on the menu,
    /// rendered with it. It saves each render in apps/xr/Builds/EntryRenders, which git ignores, with a
    /// close-up at a Quest 3's 25 pixels per degree. In the editor: Halcyonic > Render the Entry Panel
    /// Over the Stage. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1
    /// when a check fails.
    /// </summary>
    public static class EntryRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;
        private const string Journal = "01a0dcf1-5a80-7000-8000-000000000001";
        private const string Time = "2026-09-30T09:00:00.000Z";

        /// <summary>
        /// Where the room and pairing controls' buttons begin, to either side: 26 degrees less half of
        /// the widest resting button, the pairing panel's 0.24 of the design width at its scale, 0.4 m out.
        /// </summary>

        [MenuItem("Halcyonic/Render the Entry Panel Over the Stage")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = GlazeChecks.AtEachTextSize(Run);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Entry render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
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
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "EntryRenders", variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(RenderStage("far", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null, hostile: false));
                failures.AddRange(RenderStage("desk", folder, radius: 0.55f, surfaceDrop: 0.46f, hostile: false));
                failures.AddRange(RenderStage("far-untrusted", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null, hostile: true));
                // Again with a Quest 3S's narrower field measured: every panel stays inside it.
                ViewField.Current = FieldChecks.Quest3S;
                failures.AddRange(RenderStage("far-3s", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null, hostile: false));
                failures.AddRange(RenderStage("desk-3s", folder, radius: 0.55f, surfaceDrop: 0.46f, hostile: false));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                ViewField.Current = null;
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: entry render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: entry render: every check passed; the renders are in " + folder);
            return failures;
        }

        private static IEnumerable<string> RenderStage(string name, string folder, float radius, float? surfaceDrop, bool hostile)
        {
            var failures = new List<string>();
            var root = new GameObject("Entry render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var characters = WorkspaceRender.Lineup(root.transform, eyes, radius, surfaceDrop, WorkspaceRender.Presentation);
                var targets = characters.ConvertAll(character => character.Target);
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;

                var state = Portfolio(hostile, needsYouNow: true);
                var visibility = new StageVisibility();
                visibility.UseJournal(Journal);
                visibility.Hide("p-recipes", state.Projects.Keys);
                var lineup = new CharacterLineup(6);
                lineup.Update(state.Workstreams.Values.Where(work => visibility.Shows(work.ProjectId)));
                var overview = WorkOverview.Of(state, visibility, id => lineup.SlotOf(id) >= 0);

                // The Settings sheet, on a host of its own, as the stage gives it one.
                var host = new GameObject("Settings host");
                host.transform.SetParent(root.transform, false);
                failures.AddRange(SettingsFits(name, folder, root, host, camera, texture, characters, surface, hostile));

                var panel = EntryPanel.ForRender(root.transform, state, overview, targets, surface);
                var places = new List<(string Screen, Places Places)>();
                foreach (var (suffix, show) in Screens())
                {
                    show(panel);
                    WorkspaceRender.ForceMeshes(root);
                    var what = name + " " + suffix;
                    var withStage = WorkspaceRender.Render(camera, texture);
                    foreach (var (view, _) in characters) view.gameObject.SetActive(false);
                    var alone = WorkspaceRender.Render(camera, texture);
                    foreach (var (view, _) in characters) view.gameObject.SetActive(true);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + ".png"), withStage.EncodeToPNG());
                    var closeUp = WorkspaceRender.CloseUp(camera, texture, panel.Root);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + "-closeup.png"), closeUp.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(closeUp);
                    var whole = Whole(camera, texture, panel.Root);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + "-panel.png"), whole.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(whole);

                    var frame = panel.Frame;
                    var rect = WorkspaceRender.ScreenRect(camera, frame.transform, frame.Size);
                    var (changed, compared) = ChangedInPanel(withStage, alone, camera, frame);
                    UnityEngine.Object.DestroyImmediate(withStage);
                    UnityEngine.Object.DestroyImmediate(alone);
                    if (compared == 0) failures.Add(what + ": the panel is not in the render, so the render checks nothing.");
                    if (changed > 0) failures.Add(what + ": " + changed + " pixels of the panel change when the stage behind it is drawn.");
                    foreach (var (view, target) in characters)
                    {
                        if (WorkspaceRender.Covered(camera, target, rect)) failures.Add(what + ": " + view.WorkstreamId + "'s body is behind the panel.");
                    }
                    failures.AddRange(PanelFits(what, frame, characters, eyes));
                    if (ViewField.Current is ViewField shownField)
                    {
                        // A panel is read with the head turned toward it, over a desk as well, where it opens above the
                        // lineup, tipped down as much as its bottom needs, at most 8 degrees (WorkspacePlacement.ReadingPitch).
                        var pitch = WorkspacePlacement.ReadingPitch(new PanelSize(PanelFrame.Distance, frame.Size.x / 2f * PanelFrame.Scale, frame.Size.y / 2f * PanelFrame.Scale),
                            FieldChecks.ElevationOf(eyes, frame.transform.position), shownField);
                        failures.AddRange(FieldChecks.Inside(what + ": the panel", FieldChecks.Corners(frame), eyes, frame.transform.position, pitch, shownField));
                    }
                    if (!hostile) failures.AddRange(NothingOfOursCut(panel.ShownParts, what, frame));
                    places.Add((suffix, Places.Of(frame)));
                    // Every confirmation, reached by showing its screen and then pressing: Yes clear of every control shown before it and since.
                    if (frame.Shown?.Confirm != null) failures.AddRange(WorkspaceRender.YesClear(frame, what));
                    if (frame.Shown?.Confirm != null && frame.Shown.Parts is (_, var parts) && parts > 1) failures.AddRange(PagerOnTop(frame, what));
                    if (hostile) failures.AddRange(WorkspaceRender.AllShowLiterally(panel.Root.gameObject, "entry render " + what, eyes));
                }
                failures.AddRange(PlacesHold(name, places));
                if (!hostile) failures.AddRange(MovesByHand(name, panel, eyes));
                var size = new PanelSize(PanelFrame.Distance, panel.Frame.Size.x / 2f * PanelFrame.Scale, panel.Frame.Size.y / 2f * PanelFrame.Scale);
                var (_, direction) = WorkspaceLayout.PlaceForeground(targets, eyes, camera.transform.forward, surface, new List<BodyInView>(), size);
                Debug.Log("Halcyonic: entry render " + name + ": the panel's center is " + WorkspaceRender.Degrees(direction.Elevation)
                    + " degrees from eye level, " + (direction.Above ? "above" : "below") + " the characters it passes, "
                    + (direction.Clear ? "clear of every body." : "over a body."));
                if (!direction.Clear) failures.Add(name + ": the panel covers a character's body.");
                if (direction.Elevation < WorkspacePlacement.Lowest(size) - 0.01f || direction.Elevation > WorkspacePlacement.HighestDegrees + 0.01f)
                {
                    failures.Add(name + ": the panel's center is outside the comfortable band.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>Each screen, as the person reaches it.</summary>
        private static IEnumerable<(string Suffix, Action<EntryPanel> Show)> Screens()
        {
            yield return ("welcome", panel => panel.ShowForRender(EntryPanel.Screen.Welcome));
            yield return ("connect", panel => panel.ShowForRender(EntryPanel.Screen.Connect));
            yield return ("more-work", panel => panel.ShowForRender(EntryPanel.Screen.MoreWork));
        }

        /// <summary>
        /// Move held and dragged 10 degrees right and 4 up, as a hand would: the panel follows by as
        /// much, at its distance and facing the eyes.
        /// </summary>
        private static IEnumerable<string> MovesByHand(string name, EntryPanel panel, Vector3 eyes)
        {
            var failures = new List<string>();
            static (float Yaw, float Elevation) Angles(Vector3 toward) =>
                (Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg);
            Vector3 Turned(Vector3 point, float right, float up)
            {
                var (yaw, elevation) = Angles(point - eyes);
                return eyes + Quaternion.Euler(-(elevation + up), yaw + right, 0f) * Vector3.forward * Vector3.Distance(eyes, point);
            }
            panel.ShowForRender(EntryPanel.Screen.Connect);
            var root = panel.Root;
            var move = panel.Frame.ButtonFor(PanelModel.Move);
            if (move == null || !move.Holds)
            {
                failures.Add(name + " move: Move does not show, or cannot be held to move the panel with the hand.");
                return failures;
            }
            var (yawBefore, elevationBefore) = Angles(root.position - eyes);
            var distance = Vector3.Distance(eyes, root.position);
            var from = move.transform.position;
            panel.DragForRender(eyes, from, Turned(from, 10f, 4f));
            var (yawAfter, elevationAfter) = Angles(root.position - eyes);
            Debug.Log("Halcyonic: entry render " + name + ": Move held and dragged 10 degrees right and 4 up moves the panel "
                + WorkspaceRender.Degrees(Mathf.DeltaAngle(yawBefore, yawAfter)) + " right and " + WorkspaceRender.Degrees(elevationAfter - elevationBefore) + " up.");
            if (Mathf.Abs(Mathf.DeltaAngle(yawBefore, yawAfter) - 10f) > 0.05f || Mathf.Abs(elevationAfter - elevationBefore - 4f) > 0.05f)
            {
                failures.Add(name + " move: the panel does not follow the hand by as much as it moved.");
            }
            if (Mathf.Abs(Vector3.Distance(eyes, root.position) - distance) > 1e-4f) failures.Add(name + " move: the panel leaves touch distance as it moves.");
            if (Vector3.Angle(root.forward, root.position - eyes) > 0.1f) failures.Add(name + " move: the panel no longer faces the eyes once moved.");
            return failures;
        }

        /// <summary>
        /// Three projects with the demonstration's kind of work: ten workstreams, of which six stand on
        /// the stage; one project hidden, with work that needs the person. With <paramref name="needsYouNow"/>
        /// another comes to need the person, for the banner; <paramref name="hostile"/> names every
        /// project and titles every workstream with text from outside at its worst.
        /// </summary>
        internal static ClientProjection Portfolio(bool hostile, bool needsYouNow)
        {
            string Named(string plain, string field) => hostile ? WorkspaceRender.Hostile(field) : plain;
            var projects = new List<ProjectView>
            {
                new()
                {
                    ProjectId = "p-store", Name = Named("Storefront API", "project"), CreatedAt = Time, UpdatedAt = Time,
                    Location = new ProjectLocation { Path = "/Users/person/Projects/storefront-api", Name = Named("storefront-api", "folder"), Created = false },
                },
                new() { ProjectId = "p-docs", Name = Named("Docs site", "project"), CreatedAt = Time, UpdatedAt = Time },
                new() { ProjectId = "p-recipes", Name = Named("Recipe tracker", "project"), CreatedAt = Time, UpdatedAt = Time },
            };
            var work = new List<WorkstreamView>
            {
                Work("w-a", "p-store", Named("Add rate limiting to sign-in", "title"), needsYouNow ? WorkstreamStatus.WaitingForHuman : WorkstreamStatus.Running, 1),
                Work("w-b", "p-store", Named("Paginate the order history", "title"), WorkstreamStatus.Running, 2),
                Work("w-c", "p-store", Named("Send order confirmations", "title"), WorkstreamStatus.Verifying, 3),
                Work("w-d", "p-store", Named("Refresh the checkout copy", "title"), WorkstreamStatus.Completed, 4),
                Work("w-e", "p-store", Named("Upgrade the image pipeline", "title"), WorkstreamStatus.Failed, 5),
                Work("w-f", "p-docs", Named("Rewrite the quick start", "title"), WorkstreamStatus.Running, 6),
                Work("w-g", "p-docs", Named("Fix broken links", "title"), WorkstreamStatus.Completed, 7),
                Work("w-h", "p-docs", Named("Add a search page", "title"), WorkstreamStatus.Created, 8),
                Work("w-i", "p-recipes", Named("Import recipes from a file", "title"), WorkstreamStatus.WaitingForHuman, 9),
                Work("w-j", "p-recipes", Named("Plan the week's dinners", "title"), WorkstreamStatus.Completed, 10),
            };
            var state = new ClientProjection();
            state.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = Journal, Origin = JournalOrigin.Live },
                Position = 1,
                Projects = projects,
                Workstreams = work,
                Executions = new List<ExecutionView>(),
                Commands = new List<CommandView>(),
                Runtimes = new List<RuntimeDescriptor>
                {
                    Runtime("mock", "Simulated agent (render)", synthetic: true, ModelChoice.None),
                    Runtime("local", "Local agent (render)", synthetic: false, ModelChoice.Listed),
                },
            }, new StateChanges());
            return state;
        }

        private static WorkstreamView Work(string id, string project, string title, WorkstreamStatus status, int minute) => new()
        {
            WorkstreamId = id,
            ProjectId = project,
            Title = title,
            Objective = null,
            Status = status,
            Attention = new Attention
            {
                Level = status == WorkstreamStatus.WaitingForHuman ? AttentionLevel.ActionRequired
                    : status == WorkstreamStatus.Failed ? AttentionLevel.Notice : AttentionLevel.None,
                Reasons = new List<AttentionReason>(),
            },
            CurrentExecutionId = null,
            ExecutionIds = new List<string>(),
            CreatedAt = Time,
            UpdatedAt = "2026-09-30T09:" + minute.ToString("00", CultureInfo.InvariantCulture) + ":00.000Z",
        };

        private static RuntimeDescriptor Runtime(string id, string name, bool synthetic, ModelChoice choice) => new()
        {
            RuntimeId = id,
            Kind = id,
            DisplayName = name,
            Synthetic = synthetic,
            ModelChoice = choice,
            // A real runtime works in the project's folder; the simulated one needs none.
            UsesProjectLocation = !synthetic,
            Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, Interrupt = true },
        };

        /// <summary>
        /// The Settings sheet, opened as the rail's Settings opens it, with the sections the room, where
        /// the characters stand under it, and pairing fill: it opens clear of every character and label, a degree or more, in the
        /// comfortable band, its targets 60 dp, its words whole and large enough, and a refusal from
        /// whatever answered at the typed address shows as written.
        /// </summary>
        private static IEnumerable<string> SettingsFits(string name, string folder, GameObject root, GameObject host, Camera camera, RenderTexture texture,
            List<(CharacterView View, CharacterTarget Target)> characters, float? surface, bool hostile)
        {
            var failures = new List<string>();
            var sheet = SettingsSheet.On(host);
            var room = sheet.Section(SettingsText.YourRoom, 0);
            room.Say(surface.HasValue ? "Your agents are on your desk." : "No free desk or table in reach, so your agents stand in front of you.");
            room.Offer(room.Button("Space switch", ButtonRole.Secondary), "Show a virtual space");
            // Where the characters stand, under the room's buttons, with its longest line: beside a window, the other two offered.
            var window = sheet.Continuation(room);
            window.Say(surface.HasValue ? SettingsText.ArrangedByRoom : SettingsText.Arrangement(StageArrangement.BesideAWindow));
            if (!surface.HasValue)
            {
                window.Offer(window.Button("Arrangement 0", ButtonRole.Secondary), SettingsText.ChangeTo(StageArrangement.InFront));
                window.Offer(window.Button("Arrangement 1", ButtonRole.Secondary), SettingsText.ChangeTo(StageArrangement.TurnedAside));
            }
            // The comfort settings, as the text's size stands in this pass.
            ComfortControls.ForRender(host, new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard });
            var mac = sheet.Section(SettingsText.YourMac, 1);
            mac.Say(hostile ? "Pairing failed: " + WorkspaceRender.Hostile("refusal") : "Paired with " + HostText.Your + " at 192.168.1.23:47801. Connecting over Wi-Fi.");
            mac.Offer(mac.Button("Pairing", ButtonRole.Destructive), "Forget this " + HostText.Noun);
            sheet.OpenForRender(characters.ConvertAll(character => character.Target), surface);
            WorkspaceRender.ForceMeshes(root);
            var image = WorkspaceRender.Render(camera, texture);
            File.WriteAllBytes(Path.Combine(folder, name + "-settings.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            var closeUp = WorkspaceRender.CloseUp(camera, texture, sheet.Root);
            File.WriteAllBytes(Path.Combine(folder, name + "-settings-closeup.png"), closeUp.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(closeUp);

            var eyes = camera.transform.position;
            var others = new List<GlazeChecks.Extent> { GlazeChecks.Of("the Settings sheet", eyes, sheet.Root.gameObject) };
            foreach (var (view, _) in characters)
            {
                others.Add(GlazeChecks.Of(view.WorkstreamId + "'s label", eyes, view.Label.gameObject));
                others.Add(WorkspaceRender.BodyExtent(view, eyes));
            }
            failures.AddRange(GlazeChecks.Apart(others).Where(failure => failure.Contains("the Settings sheet")).Select(failure => name + ": " + failure));
            failures.AddRange(GlazeChecks.TargetsLargeEnough(sheet.Root.GetComponentsInChildren<GlazeButton>(false), eyes, name + " settings"));
            failures.AddRange(GlazeChecks.TextLargeEnough(sheet.Root.gameObject, eyes, name + " settings"));
            GlazeChecks.ListTextAsSeen(sheet.Root.gameObject, eyes, name + " settings");
            if (hostile) failures.AddRange(WorkspaceRender.AllShowLiterally(sheet.Root.gameObject, name + " settings", eyes));
            else failures.AddRange(NothingOfOursCut(sheet.Root.GetComponentsInChildren<TMP_Text>(false).Cast<Component>()
                .Concat(sheet.Root.GetComponentsInChildren<GlazeButton>(false)), name + " settings"));
            var toward = sheet.Root.position - eyes;
            var elevation = Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
            Debug.Log("Halcyonic: entry render " + name + ": Settings opens " + WorkspaceRender.Degrees(-elevation) + " degrees below eye level, "
                + WorkspaceRender.Degrees(GlazeTokens.DegreesOf(sheet.Size.x)) + " by " + WorkspaceRender.Degrees(GlazeTokens.DegreesOf(sheet.Size.y)) + " degrees.");
            if (elevation < WorkspacePlacement.Lowest(sheet.PanelSize) - 0.01f || elevation > WorkspacePlacement.HighestDegrees + 0.01f)
            {
                failures.Add(name + ": Settings opens outside the comfortable band.");
            }
            sheet.Close();
            return failures;
        }

        /// <summary>
        /// Nothing showing is cut short, with the render's short names and titles: only text from
        /// outside that a row or a title says is data may end in an ellipsis, as a long first task does.
        /// </summary>
        internal static IEnumerable<string> NothingOfOursCut(IEnumerable<Component> parts, string what, PanelFrame? frame = null)
        {
            var failures = new List<string>();
            foreach (var part in parts)
            {
                var labels = part is GlazeButton button ? new TMP_Text?[] { button.Label, button.Detail, button.Overline, button.End } : new[] { part as TMP_Text };
                foreach (var label in labels)
                {
                    if (label == null || !label.gameObject.activeInHierarchy) continue;
                    if (frame != null && frame.HoldsData(label)) continue;
                    label.ForceMeshUpdate();
                    if (!label.isTextTruncated || label.name == "First task") continue;
                    failures.Add(what + ": " + part.name + " cuts its words short: " + label.text);
                }
            }
            return failures;
        }

        /// <summary>
        /// The panel as the eyes see it: a degree or more from every character's label and body, its
        /// targets 60 dp (48 for the window controls and the pager) and 12 mm apart, and every word at
        /// least the caption's size at its own distance.
        /// </summary>
        private static IEnumerable<string> PanelFits(string what, PanelFrame frame, List<(CharacterView View, CharacterTarget Target)> characters, Vector3 eyes)
        {
            var failures = new List<string>();
            var near = new List<GlazeChecks.Extent> { GlazeChecks.Of("the entry panel", eyes, frame.gameObject) };
            foreach (var (view, _) in characters)
            {
                near.Add(GlazeChecks.Of(view.WorkstreamId + "'s label", eyes, view.Label.gameObject));
                near.Add(WorkspaceRender.BodyExtent(view, eyes));
            }
            failures.AddRange(GlazeChecks.Apart(near).Where(failure => failure.Contains("the entry panel")).Select(failure => what + ": " + failure));
            // A row that only says something takes no press, so it is no target.
            var buttons = frame.Buttons.Where(button => !button.Static).ToList();
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons, eyes, what));
            failures.AddRange(GlazeChecks.MicrophoneOnlyWhereHeld(buttons, what));
            failures.AddRange(GlazeChecks.TextLargeEnough(frame.gameObject, eyes, what));
            GlazeChecks.ListTextAsSeen(frame.gameObject, eyes, what);
            if (!frame.BarIcons) Debug.Log("Halcyonic: entry render " + what + ": the bar has no room for its icons, so its words stand alone.");
            var gap = TargetGap(frame);
            for (var a = 0; a < buttons.Count; a++)
            {
                for (var b = a + 1; b < buttons.Count; b++)
                {
                    if (!Apart(RectOf(buttons[a]), RectOf(buttons[b]), gap)) failures.Add(what + ": " + buttons[a].name + " and " + buttons[b].name + " are closer than 12 mm.");
                }
            }
            var size = frame.Size;
            foreach (var button in buttons)
            {
                var rect = RectOf(button);
                if (rect.xMin < -size.x / 2f || rect.xMax > size.x / 2f || rect.yMin < -size.y / 2f || rect.yMax > size.y / 2f)
                {
                    failures.Add(what + ": " + button.name + " runs past the panel's edge.");
                }
            }
            return failures;
        }

        /// <summary>Where the parts that never move stand on one screen: Close, the bar's right end, Back, and the pager.</summary>
        private readonly struct Places
        {
            private Places(Vector2? close, Vector2? rightEnd, Vector2? back, Vector2? pager)
            {
                Close = close;
                RightEnd = rightEnd;
                Back = back;
                Pager = pager;
            }

            /// <summary>Close's right edge and its middle's height: Not now, in its place on the welcome, is wider.</summary>
            public Vector2? Close { get; }

            /// <summary>The bar's right end: its right edge and its middle's height.</summary>
            public Vector2? RightEnd { get; }

            /// <summary>Back's left edge and its middle's height.</summary>
            public Vector2? Back { get; }

            /// <summary>Next's right edge and its middle's height.</summary>
            public Vector2? Pager { get; }

            public static Places Of(PanelFrame frame)
            {
                var close = frame.ButtonFor(PanelModel.Close);
                var back = frame.Back;
                var end = frame.RightEnd;
                var rightEnd = end != null ? new Vector2(RectOf(end).xMax, RectOf(end).center.y) : (Vector2?)null;
                var backRect = back != null ? RectOf(back) : (Rect?)null;
                // A confirmation's pager stands at the top, away from Yes (PagerOnTop); every other screen's at the body's bottom right.
                var next = frame.Shown?.Confirm == null ? frame.NextPage : null;
                return new Places(close != null ? new Vector2(RectOf(close).xMax, RectOf(close).center.y) : (Vector2?)null, rightEnd,
                    backRect.HasValue ? new Vector2(backRect.Value.xMin, backRect.Value.center.y) : (Vector2?)null,
                    next != null ? new Vector2(RectOf(next).xMax, RectOf(next).center.y) : (Vector2?)null);
            }
        }

        /// <summary>Navigation and the primary never move: Close, the bar's right end, Back and the pager stand in the same place on every screen.</summary>
        private static IEnumerable<string> PlacesHold(string name, List<(string Screen, Places Places)> screens)
        {
            const float tolerance = 1e-4f;
            IEnumerable<string> Same(string part, Func<Places, Vector2?> of)
            {
                var first = screens.FirstOrDefault(screen => of(screen.Places).HasValue);
                if (first.Screen == null) yield break;
                var at = of(first.Places)!.Value;
                foreach (var (screen, places) in screens)
                {
                    var here = of(places);
                    if (here.HasValue && (Mathf.Abs(here.Value.x - at.x) > tolerance || Mathf.Abs(here.Value.y - at.y) > tolerance))
                    {
                        yield return name + " " + screen + ": " + part + " stands somewhere else than on " + first.Screen + ".";
                    }
                }
            }
            return Same("Close", places => places.Close)
                .Concat(Same("the bar's right end", places => places.RightEnd))
                .Concat(Same("Back", places => places.Back))
                .Concat(Same("the pager", places => places.Pager));
        }

        /// <summary>While a confirmation pages through what it confirms, its pager stands under the header, above the body, away from Yes.</summary>
        private static IEnumerable<string> PagerOnTop(PanelFrame frame, string what)
        {
            var next = frame.NextPage;
            if (next == null) yield return what + ": the confirmation pages but shows no pager.";
            else if (RectOf(next).yMin < frame.CustomBody.yMax - 1e-4f) yield return what + ": the confirmation's pager does not stand above the body.";
        }

        /// <summary>The whole panel, the eyes turned to its middle, a little wider than the panel: to see every edge, not to judge size.</summary>
        private static Texture2D Whole(Camera camera, RenderTexture texture, Transform panel)
        {
            var rotation = camera.transform.rotation;
            var fieldOfView = camera.fieldOfView;
            camera.transform.rotation = Quaternion.LookRotation(panel.position - camera.transform.position, Vector3.up);
            camera.fieldOfView = PanelFrame.WidthDegrees + 6f;
            var image = WorkspaceRender.Render(camera, texture);
            camera.transform.rotation = rotation;
            camera.fieldOfView = fieldOfView;
            return image;
        }

        /// <summary>A button's outline in the frame's units.</summary>
        private static Rect RectOf(GlazeButton button)
        {
            var place = button.transform.localPosition;
            return new Rect(place.x - button.Size.x / 2f, place.y - button.Size.y / 2f, button.Size.x, button.Size.y);
        }

        /// <summary>12 mm at the panel's distance, in its units.</summary>
        private static float TargetGap(PanelFrame frame) => Glaze.TargetGapMeters / PanelFrame.Distance;

        /// <summary>Two outlines at least <paramref name="gap"/> apart one way or the other.</summary>
        private static bool Apart(Rect a, Rect b, float gap)
        {
            var dx = Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax);
            var dy = Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax);
            return dx >= gap * 0.99f || dy >= gap * 0.99f;
        }

        internal static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;

        /// <summary>World bounds on the render, as a rectangle.</summary>
        internal static Rect ScreenBounds(Camera camera, Bounds bounds)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                var screen = camera.WorldToScreenPoint(point);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// How many pixels differ between two renders inside the panel's own outline on the render, a
        /// little in from its rounded edge, and how many were compared. A panel turned toward the eyes
        /// projects as a quadrilateral, whose bounding box would also hold pixels beside it.
        /// </summary>
        private static (int Changed, int Compared) ChangedInPanel(Texture2D a, Texture2D b, Camera camera, PanelFrame frame)
        {
            const float inset = 0.03f;
            var quad = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) }
                .Select(corner => (Vector2)camera.WorldToScreenPoint(frame.transform.TransformPoint(new Vector3(
                    corner.x * (frame.Size.x / 2f - inset), corner.y * (frame.Size.y / 2f - inset), 0f))))
                .ToArray();
            var minX = Mathf.Clamp(Mathf.FloorToInt(quad.Min(point => point.x)), 0, Size);
            var maxX = Mathf.Clamp(Mathf.CeilToInt(quad.Max(point => point.x)), 0, Size);
            var minY = Mathf.Clamp(Mathf.FloorToInt(quad.Min(point => point.y)), 0, Size);
            var maxY = Mathf.Clamp(Mathf.CeilToInt(quad.Max(point => point.y)), 0, Size);
            var changed = 0;
            var compared = 0;
            for (var y = minY; y < maxY; y++)
            {
                for (var x = minX; x < maxX; x++)
                {
                    if (!Inside(quad, new Vector2(x + 0.5f, y + 0.5f))) continue;
                    compared++;
                    var p = a.GetPixel(x, y);
                    var q = b.GetPixel(x, y);
                    if (Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b))) > 2f / 255f) changed++;
                }
            }
            return (changed, compared);
        }

        /// <summary>Whether a point lies inside a convex quadrilateral, whichever way round its corners go.</summary>
        private static bool Inside(Vector2[] quad, Vector2 point)
        {
            var sign = 0f;
            for (var index = 0; index < quad.Length; index++)
            {
                var from = quad[index];
                var to = quad[(index + 1) % quad.Length];
                var cross = (to.x - from.x) * (point.y - from.y) - (to.y - from.y) * (point.x - from.x);
                if (Mathf.Abs(cross) < 1e-6f) continue;
                if (sign == 0f) sign = Mathf.Sign(cross);
                else if (Mathf.Sign(cross) != sign) return false;
            }
            return true;
        }
    }
}
