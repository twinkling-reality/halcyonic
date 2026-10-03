#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>A column standing in for another lane's, that keeps what reached it.</summary>
        private sealed class StubColumn : IMenuColumn
        {
            private readonly Func<MenuFrame> build;

            public StubColumn(Func<MenuFrame> build) => this.build = build;

            public MenuFrame? Last { get; private set; }

            public List<(MenuFrame Frame, bool Side)> DrawnFrames { get; } = new List<(MenuFrame, bool)>();

            public MenuFrame? Frame => Last = build();

            public event Action? Changed;

            public event Action? Closed;

            public void Change() => Changed?.Invoke();

            public void Close() => Closed?.Invoke();

            public void Act(string id, string? key)
            {
            }

            public void Drawn(MenuFrame drawn, bool sidePanel) => DrawnFrames.Add((drawn, sidePanel));

            public void HoldStarted(string id)
            {
            }

            public void HoldEnded(string id, bool letGo)
            {
            }

            public void Heard(string text)
            {
            }

            public void Said(string words)
            {
            }

            public void Tick()
            {
            }

            public void FocusLeft()
            {
            }
        }

        /// <summary>
        /// The menu's director on a stage, as WorkspaceDirector will host it: Tasks from the session, its
        /// waiting task's file opened from its row, the places switched, and the menu closed to its bar.
        /// Each state is drawn on one plane and held to the plane's checks; a file learns of the very
        /// frame it gave being drawn, and the menu's Tasks shows the open file's row chosen.
        /// </summary>
        private static IEnumerable<string> RenderMenuDirector(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("Menu director render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = Lineup(root.transform, eyes, radius, surfaceDrop, Presentation);
                var targets = characters.ConvertAll(character => character.Target);
                var state = Projection(characters);
                var opened = characters[3];
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var overview = WorkOverview.Of(state, new StageVisibility(), _ => true);
                StubColumn? file = null;
                float? made = null;
                var sent = new List<CommandEnvelope>();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" }),
                    Overview = () => overview,
                    ReadLocations = _ => System.Threading.Tasks.Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    Comfort = comfort,
                    File = (host, task) =>
                    {
                        // The file reads its page height while it is made: against its own character's top line.
                        made = host.PageHeight(1, besideMenu: false);
                        return file = new StubColumn(() => WaitingFile(opened.View.Presentation!.Title, StateLanguage.BadgeOf(opened.View.Presentation!), chosen: false,
                        host.PageHeight(host.TitleRows(opened.View.Presentation!.Title, Glaze.Menu.FileColumnDegrees), besideMenu: false)));
                    },
                    Submit = command =>
                    {
                        sent.Add(command);
                        return null;
                    },
                    Demonstration = () => true,
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, opened.Target.BodyPosition - eyes, targets, surface, false),
                    CharacterOf = task => targets.FirstOrDefault(target => target.View.WorkstreamId == task),
                    Bar = place => TasksColumn.Bar(place, state),
                    SomethingWaits = () => true,
                });

                // Opened as the closed bar's Open does when something waits: on Tasks.
                director.Open();
                director.DrawNow();
                if (director.Navigator.Place != MenuPlace.Tasks) failures.Add(name + ": the menu opened on " + director.Navigator.Place + " while something waits; it opens on Tasks.");
                var tasksFrame = director.Navigator.Frames(TasksColumn.Bar(MenuPlace.Tasks, state)).Menu;
                if (tasksFrame == null || tasksFrame.Sections.Count != 4) failures.Add(name + ": Tasks shows without the menu's four places.");
                else if (tasksFrame.Lines.FirstOrDefault()?.Key != opened.View.WorkstreamId) failures.Add(name + ": the waiting task is not Tasks' first row.");
                failures.AddRange(PlaneState(name + " director tasks", folder, camera, texture, director.Plane, characters, eyes, null));

                // Its row opens its file beside the menu, and the row stays chosen.
                var drawnTasks = director.Plane.Showing(MenuColumn.Menu);
                if (!director.Navigator.Act(MenuColumn.Menu, TasksColumn.OpenTask, opened.View.WorkstreamId, drawnTasks, null))
                {
                    failures.Add(name + ": a press on Tasks as drawn was not taken.");
                }
                director.DrawNow();
                if (file == null || director.Navigator.Beside != file) failures.Add(name + ": pressing the waiting task's row opened no file beside the menu.");
                var expected = MenuPage.Height(comfort.Text, 1, MenuPlane.TopLine(opened.Target, targets, eyes, opened.Target.BodyPosition - eyes, surface), ViewField.Current);
                if (made is not float height || Mathf.Abs(height - expected) > 1e-5f)
                {
                    failures.Add(name + ": the file read its page height as " + made + " while it was made, not against its own character's top line (" + expected + ").");
                }

                // In the demonstration a column's send still goes through the host's submissions, to the demonstration's session.
                director.Submit(new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" }).SendInstruction("render-execution", "Carry on"));
                if (sent.Count != 1) failures.Add(name + ": in the demonstration, a column's send did not reach the host's submissions.");
                else
                {
                    if (file.Last == null || !file.DrawnFrames.Any(drawn => drawn.Frame == file.Last && !drawn.Side))
                    {
                        failures.Add(name + ": the file did not learn of the very frame it gave being drawn.");
                    }
                    var chosen = director.Navigator.Frames(TasksColumn.Bar(MenuPlace.Tasks, state)).Menu?.Lines.FirstOrDefault(line => line.Chosen)?.Key;
                    if (chosen != opened.View.WorkstreamId) failures.Add(name + ": Tasks does not show the open file's row chosen.");
                    if (director.Plane.LightLine == null) failures.Add(name + ": the file opened from Tasks shows no light line.");
                }
                failures.AddRange(PlaneState(name + " director file", folder, camera, texture, director.Plane, characters, eyes, null));

                // Choosing a place is the menu's own; the file stays beside it.
                // A press from the frame drawn before the file opened is passed over: it no longer stands.
                if (director.Navigator.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage), drawnTasks, null))
                {
                    failures.Add(name + ": a press on a frame no longer standing was taken.");
                }
                director.Navigator.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings), director.Plane.Showing(MenuColumn.Menu), null);
                director.DrawNow();
                var settings = director.Navigator.Frames(TasksColumn.Bar(MenuPlace.Settings, state)).Menu;
                if (settings?.Subject != SettingsText.Subject) failures.Add(name + ": choosing Settings did not show Settings.");
                if (director.Navigator.Beside != file) failures.Add(name + ": choosing a place took the file away.");
                failures.AddRange(PlaneState(name + " director settings", folder, camera, texture, director.Plane, characters, eyes, null));

                // The file's Close, then the menu's: the closed bar alone.
                file?.Close();
                director.CloseMenu();
                director.DrawNow();
                if (director.Plane.Bar == null) failures.Add(name + ": closed with no file open, the menu shows no bar.");
                failures.AddRange(PlaneState(name + " director closed", folder, camera, texture, director.Plane, characters, eyes, null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>The session as the stage shows it: each character a task of one project, the opened one waiting for the person.</summary>
        private static ClientProjection Projection(List<(CharacterView View, CharacterTarget Target)> characters)
        {
            const string Time = "2026-10-02T09:00:00.000Z";
            var workstreams = characters.Select((character, slot) =>
            {
                var waits = character.View.Presentation!.Activity == CharacterActivity.WaitingForHuman;
                return new WorkstreamView
                {
                    WorkstreamId = character.View.WorkstreamId,
                    ProjectId = "storefront",
                    Title = character.View.Presentation!.Title,
                    Status = waits ? WorkstreamStatus.WaitingForHuman : WorkstreamStatus.Running,
                    Attention = new Attention { Level = waits ? AttentionLevel.ActionRequired : AttentionLevel.None, Reasons = new List<AttentionReason>() },
                    ExecutionIds = new List<string>(),
                    CreatedAt = Time,
                    UpdatedAt = "2026-10-02T09:" + slot.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ":00.000Z",
                };
            }).ToList();
            var state = new ClientProjection();
            state.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = "render", Origin = JournalOrigin.Live },
                Position = 1,
                Projects = new List<ProjectView> { new ProjectView { ProjectId = "storefront", Name = "Storefront API", CreatedAt = Time, UpdatedAt = Time } },
                Workstreams = workstreams,
                Executions = new List<ExecutionView>(),
                Commands = new List<CommandView>(),
                Runtimes = new List<RuntimeDescriptor>(),
            }, new StateChanges());
            return state;
        }
    }
}
