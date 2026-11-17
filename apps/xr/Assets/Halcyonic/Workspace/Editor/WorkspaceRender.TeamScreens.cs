#nullable enable
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
    public static partial class WorkspaceRender
    {
        /// <summary>
        /// The room's project screens at once (the lead's 2026-11-17T18:55Z (d)): the release MenuDirector opens three projects' screens; the
        /// last opened stands forward as the work and the other two stand over their own places (FIELD's places, 46.75 degrees either side),
        /// 24 degrees across, their lower edges 8 degrees up, none in the clear front, none past the seated reach, none over another or the
        /// forward screen. Then the lead's walk: B closed, A and C stay; A pressed comes forward, C goes over its place; a fourth is refused
        /// in words. At both text sizes; renders land in Builds/TeamScreenRenders. Every name is synthetic.
        /// </summary>
        public static void TeamScreensCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "TeamScreenRenders", variant));
                Directory.CreateDirectory(folder);
                foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                List<string> found;
                try { found = RenderTeamScreens(folder); }
                finally { KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: team screens: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: team screens: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderTeamScreens(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Team screens witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var work = Work.Running();
                // Four projects, the running task in the first.
                var projects = new[] { ("render-project", "Storefront API"), ("project-b", "Kite Run"), ("project-c", "Tide Charts"), ("project-d", "Lantern Notes") };
                work.State.ApplySnapshot(new Snapshot
                {
                    Journal = new JournalInfo { JournalId = "render-journal", Origin = JournalOrigin.Live },
                    Position = 2,
                    Projects = projects.Select(project => new ProjectView { ProjectId = project.Item1, Name = project.Item2, CreatedAt = Time, UpdatedAt = Time }).ToList(),
                    Workstreams = new List<WorkstreamView> { work.Workstream },
                    Executions = new List<ExecutionView> { work.Execution },
                    Commands = new List<CommandView>(),
                    Runtimes = work.State.Runtimes.ToList(),
                }, new StateChanges());
                var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Presentation = presentation,
                    SpatialWorkspace = true,
                    Commands = commands,
                    Overview = () => WorkOverview.Of(work.State, new StageVisibility(), _ => true),
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    File = (host, _) => new FileColumn(host, () => work.Present(), commands, () => null, _ => null, () => ""),
                    State = () => work.State,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward, new List<CharacterTarget>(), null, false),
                    Bar = place => TasksColumn.Bar(place, work.State),
                    // Each project's place in the room's order: A first, then B, then C.
                    PlaceOf = id => System.Array.FindIndex(projects, project => project.Item1 == id),
                });
                var plane = director.Plane;
                var camera = MakeCamera(root.transform, eyes, texture);
                camera.fieldOfView = 120f;
                var (a, b, c, d) = (projects[0].Item1, projects[1].Item1, projects[2].Item1, projects[3].Item1);
                foreach (var project in new[] { a, b, c })
                {
                    if (!director.ShowProjectScreen(project)) failures.Add("opening " + project + "'s screen was refused");
                    director.DrawNow();
                }

                void Judge(string when, string forward, params string[] over)
                {
                    if (plane.ScreenProjectId != forward) failures.Add(when + ": " + (plane.ScreenProjectId ?? "none") + " stands forward, not " + forward);
                    var stood = plane.TeamScreenPoses;
                    if (!stood.Keys.OrderBy(id => id).SequenceEqual(over.OrderBy(id => id)))
                        failures.Add(when + ": the screens over their teams are " + string.Join(", ", stood.Keys) + ", not " + string.Join(", ", over));
                    var ahead = plane.Direction.Yaw;
                    var forwardPose = plane.ScreenPose;
                    foreach (var (id, pose) in stood)
                    {
                        var about = RoomLayout.Wrap(pose.Bearing - ahead);
                        var place = PlaceBearings.Of(System.Array.FindIndex(projects, project => project.Item1 == id)).YawDegrees;
                        var across = RoomLayout.TeamScreenDegrees / 2f;
                        if (Mathf.Abs(about) - across < RoomLayout.ClearAheadDegrees - 0.01f) failures.Add(when + ": " + id + "'s screen stands in the clear front, at " + about.ToString("0.0", CultureInfo.InvariantCulture));
                        if (Mathf.Abs(about) + across > RoomLayout.SeatedReachDegrees + 0.01f) failures.Add(when + ": " + id + "'s screen stands past the seated reach, at " + about.ToString("0.0", CultureInfo.InvariantCulture));
                        if (Mathf.Abs(about - place) > 0.01f) failures.Add(when + ": " + id + "'s screen stands at " + about.ToString("0.0", CultureInfo.InvariantCulture) + ", not over its place at " + place.ToString("0.0", CultureInfo.InvariantCulture));
                        var low = new Vector3(pose.Centre.X, pose.Centre.Y, pose.Centre.Z) - new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z) * (pose.Height / 2f) - eyes;
                        var lowUp = Mathf.Atan2(low.y, new Vector2(low.x, low.z).magnitude) * Mathf.Rad2Deg;
                        if (lowUp < RoomLayout.TeamScreenLowestDegrees - 0.1f) failures.Add(when + ": " + id + "'s screen's lower edge stands " + lowUp.ToString("0.0", CultureInfo.InvariantCulture) + " degrees up, under its team's tags");
                        if (forwardPose is RoomPose front && Mathf.Abs(RoomLayout.Wrap(pose.Bearing - front.Bearing)) < across + SpatialKit.ScreenDegreesAt(GlazeText.Scale) / 2f)
                            failures.Add(when + ": " + id + "'s screen stands over the forward screen");
                        var screen = plane.TeamScreen(id);
                        if (screen == null || !screen.gameObject.activeInHierarchy) failures.Add(when + ": " + id + "'s screen draws nothing");
                        else
                        {
                            // Where it is DRAWN, from its renderers, not only where the pose says (the first frames stood them at the field's edge).
                            var drawn = screen.GetComponentsInChildren<Renderer>(false).Where(each => each.enabled).Select(each => each.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                            var toward = drawn.center - eyes;
                            var drawnAbout = RoomLayout.Wrap(Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg - ahead);
                            if (Mathf.Abs(drawnAbout - about) > 1f)
                                failures.Add(when + ": " + id + "'s screen is drawn at " + drawnAbout.ToString("0.0", CultureInfo.InvariantCulture) + " degrees, not at its pose's " + about.ToString("0.0", CultureInfo.InvariantCulture)
                                    + " (drawn centre " + drawn.center.ToString("F2") + ", size " + drawn.size.ToString("F2") + ", transform " + screen.transform.position.ToString("F2") + ", scale " + screen.transform.lossyScale.x.ToString("0.00") + ")");
                            if (!screen.ReadsWhole)
                                failures.Add(when + ": " + id + "'s screen does not read whole at " + RoomLayout.TeamScreenDegrees + " degrees (name \"" + screen.Name.text + "\" "
                                + (screen.Name.isTextTruncated ? "cut" : "whole") + ", title " + (screen.Title.isTextTruncated ? "cut" : "whole") + ", state " + (screen.Facts.isTextTruncated ? "cut" : "whole") + ")");
                        }
                        Debug.Log("Halcyonic: team screens: " + when + ": " + id + " over its place at " + about.ToString("0.00", CultureInfo.InvariantCulture) + " degrees, "
                            + Vector3.Distance(new Vector3(pose.Centre.X, pose.Centre.Y, pose.Centre.Z), eyes).ToString("0.00", CultureInfo.InvariantCulture) + " m, lower edge "
                            + lowUp.ToString("0.0", CultureInfo.InvariantCulture) + " degrees up, " + (screen != null && screen.ReadsWhole ? "reading whole" : "not reading whole"));
                    }
                    foreach (var look in new[] { -50f, 0f, 50f })
                    {
                        ForceMeshes(root);
                        camera.transform.rotation = Quaternion.Euler(-10f, ahead + look, 0f);
                        var image = Render(camera, texture);
                        File.WriteAllBytes(Path.Combine(folder, "teams-" + when.Replace(' ', '-') + "-" + look.ToString("+0;-0;0", CultureInfo.InvariantCulture) + ".png"), image.EncodeToPNG());
                        Object.DestroyImmediate(image);
                    }
                }

                Judge("three open", c, a, b);
                // A fourth is refused in words, and nothing open changes.
                if (director.ShowProjectScreen(d)) failures.Add("a fourth screen was opened");
                if (!presentation.ScreenRefused) failures.Add("a fourth screen's refusal is not said");
                director.DrawNow();
                Judge("fourth refused", c, a, b);
                // The lead's walk: B closed, A and C stay.
                if (!presentation.CloseProjectScreen(b)) failures.Add("B's screen did not close");
                director.DrawNow();
                Judge("B closed", c, a);
                // A pressed comes forward; C goes over its own place.
                if (plane.TeamScreen(a)?.GetComponentInChildren<KitPress>() is KitPress press)
                {
                    press.SettleForRender();
                    press.PressForRender(985);
                    director.DrawNow();
                    Judge("A pressed", a, c);
                }
                else failures.Add("A's screen takes no press");
                director.CloseProjectScreen();
                director.CloseProjectScreen();
                director.DrawNow();
                if (plane.TeamScreenPoses.Count != 0 || plane.RoomScreen != null) failures.Add("every screen closed, and a screen still stands");
            }
            finally
            {
                Object.DestroyImmediate(root);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            return failures;
        }
    }
}
