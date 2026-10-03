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
        /// <summary>The id of a start whose outcome is unknown, kept only while the render runs.</summary>
        private sealed class KeptInMemory : IKeptCommand
        {
            public string? Id { get; set; }
        }

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
                // Where the head points; turned below to check the plane stays put until Reset position.
                var looking = Vector3.zero;
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = Lineup(root.transform, eyes, radius, surfaceDrop, Presentation);
                var targets = characters.ConvertAll(character => character.Target);
                var state = Projection(characters);
                var opened = characters[3];
                looking = opened.Target.BodyPosition - eyes;
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var overview = WorkOverview.Of(state, new StageVisibility(), _ => true);
                StubColumn? file = null;
                NewProjectFlow? flow = null;
                IMenuHost? fileHost = null;
                IMenuHost? flowHost = null;
                float? made = null;
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                // Sessions never started: only which one is shown, and which one a send was for, matter here.
                var demonstration = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                var shownSession = demonstration;
                var demonstrationPlays = true;
                var sent = new List<(RealtimeSession Session, CommandEnvelope Command)>();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands,
                    Overview = () => overview,
                    ReadLocations = _ => System.Threading.Tasks.Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    Comfort = comfort,
                    // Your space as the room and a paired computer give it, in a development build.
                    Space = () => SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront,
                        new PairingNow("192.168.1.23:47801", PairingStep.Idle, true)), _ => { }, _ => { }, pairing: true),
                    File = (host, task) =>
                    {
                        fileHost = host;
                        // The file reads its page height while it is made: against its own character's top line.
                        made = host.PageHeight(1, besideMenu: false);
                        return file = new StubColumn(() => WaitingFile(opened.View.Presentation!.Title, StateLanguage.BadgeOf(opened.View.Presentation!), chosen: false,
                        host.PageHeight(host.TitleRows(opened.View.Presentation!.Title, Glaze.Menu.FileColumnDegrees), besideMenu: false)));
                    },
                    MakeNewProject = host =>
                    {
                        flowHost = host;
                        // Lane C's flow, keeping nothing on this machine.
                        return flow = new NewProjectFlow(host, commands, new KeptInMemory());
                    },
                    Session = () => shownSession,
                    Submit = (session, command) =>
                    {
                        sent.Add((session, command));
                        return null;
                    },
                    Demonstration = () => demonstrationPlays,
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, looking, targets, surface, false),
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
                fileHost?.Submit(commands.SendInstruction("render-execution", "Carry on"));
                if (sent.Count != 1 || sent[0].Session != demonstration) failures.Add(name + ": in the demonstration, a column's send did not reach the host's submissions for its session.");
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

                // Folded away, the person is elsewhere: a draw counts for nothing, and a press on it is not taken.
                if (file != null)
                {
                    var counted = file.DrawnFrames.Count;
                    FocusGuard.FoldForRender(true);
                    try
                    {
                        file.Change();
                        director.DrawNow();
                    }
                    finally
                    {
                        FocusGuard.FoldForRender(null);
                    }
                    if (file.DrawnFrames.Count != counted) failures.Add(name + ": a draw while the plane was folded counted as read.");
                    if (director.Navigator.Act(MenuColumn.File, Footer.Close, null, director.Plane.Showing(MenuColumn.File), null))
                    {
                        failures.Add(name + ": a press on a frame drawn while folded was taken.");
                    }
                    director.DrawNow();
                    if (file.DrawnFrames.Count != counted + 1) failures.Add(name + ": drawn again on return, the file did not learn of it.");
                }

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

                // A setting chosen with the file beside the menu: the details take the front, the file steps
                // aside and takes nothing meanwhile; another setting chosen, then Close details, and it is back.
                var fileDrawn = director.Plane.Showing(MenuColumn.File);
                director.Navigator.Act(MenuColumn.Menu, SettingsColumn.OpenSetting, "text-size", director.Plane.Showing(MenuColumn.Menu), null);
                director.DrawNow();
                if (!director.Plane.FileAside || director.Plane.Showing(MenuColumn.File) != null) failures.Add(name + ": a setting chosen beside the file, and the file still stands on the plane.");
                if (director.Navigator.Act(MenuColumn.File, "render-press", null, fileDrawn, null)) failures.Add(name + ": the file took a press while it stood aside.");
                failures.AddRange(PlaneState(name + " director setting over a file", folder, camera, texture, director.Plane, characters, eyes, null));
                director.Navigator.Act(MenuColumn.Menu, SettingsColumn.OpenSetting, "sounds", director.Plane.Showing(MenuColumn.Menu), null);
                director.DrawNow();
                failures.AddRange(PlaneState(name + " director another setting over a file", folder, camera, texture, director.Plane, characters, eyes, null));
                var details = director.Plane.Shown.FirstOrDefault(column => column.Kind == MenuColumn.Side).View;
                if (details == null || !director.Navigator.Act(MenuColumn.Side, SidePanel.Close, null, null, details.Side)) failures.Add(name + ": the setting's details took no Close.");
                director.DrawNow();
                if (director.Plane.FileAside || director.Plane.Showing(MenuColumn.File) == null) failures.Add(name + ": the details closed, and the file did not come back.");
                failures.AddRange(PlaneState(name + " director file back from a setting", folder, camera, texture, director.Plane, characters, eyes, null));

                // The file's Close, then the menu's: the closed bar alone.
                // The file's Close; then, the menu alone on Settings, its text size chosen: its change stays
                // drawn and pressable, at the larger text too, so the person can always turn it back.
                file?.Close();
                director.DrawNow();
                director.Navigator.Act(MenuColumn.Menu, SettingsColumn.OpenSetting, "text-size", director.Plane.Showing(MenuColumn.Menu), null);
                director.DrawNow();
                failures.AddRange(PlaneState(name + " director text size chosen", folder, camera, texture, director.Plane, characters, eyes, null));
                var change = director.Navigator.Frames(TasksColumn.Bar(MenuPlace.Settings, state)).Menu?.Footer[PromptSlot.FarRight];
                var inPlace = director.Plane.Shown.Count == 1 && director.Plane.Shown[0].Kind == MenuColumn.Side;
                if (change != null && inPlace && !director.Navigator.Act(MenuColumn.Side, change.Id, null, null, director.Plane.Shown[0].View.Side))
                {
                    failures.Add(name + ": the text size's change, drawn on its side panel in the frame's place, takes no press.");
                }
                // The press changed the reading size; the render keeps drawing at its own, so it is put back.
                comfort.Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard;

                // A file opened over that chosen setting: the menu lets go of its side panel, which the file
                // would leave undrawn, so its change can't act on details no one sees.
                director.OpenFile(opened.View.WorkstreamId);
                director.DrawNow();
                failures.AddRange(PlaneState(name + " director file over a chosen setting", folder, camera, texture, director.Plane, characters, eyes, null));
                file?.Close();
                director.DrawNow();

                // The menu's Close: the closed bar alone.
                director.CloseMenu();
                director.DrawNow();
                if (director.Plane.Bar == null) failures.Add(name + ": closed with no file open, the menu shows no bar.");
                failures.AddRange(PlaneState(name + " director closed", folder, camera, texture, director.Plane, characters, eyes, null));

                // The head turns: a redraw keeps the plane where it was; Reset position places it where the person looks.
                var stood = director.Plane.Direction.Yaw;
                var ahead = looking;
                looking = Quaternion.Euler(0f, 25f, 0f) * ahead;
                director.Redraw();
                director.DrawNow();
                if (Mathf.Abs(Mathf.DeltaAngle(director.Plane.Direction.Yaw, stood)) > 0.01f) failures.Add(name + ": a redraw moved the plane after the head turned; it stays until Reset position.");
                director.ResetPosition();
                director.DrawNow();
                if (Mathf.Abs(Mathf.DeltaAngle(director.Plane.Direction.Yaw, stood)) < 5f) failures.Add(name + ": Reset position left the plane where it was, not where the person looks.");
                looking = ahead;
                director.ResetPosition();
                director.DrawNow();

                // The computer's live session takes the demonstration's place: the file made in it sends nothing there.
                var live = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                shownSession = live;
                demonstrationPlays = false;
                director.DrawNow();
                if (fileHost?.Submit(commands.SendInstruction("render-execution", "Yes")) != null || sent.Count != 1)
                {
                    failures.Add(name + ": a column made in the demonstration sent once the live session showed.");
                }

                // New project, its build begun live; then the headset is paired again, another session showing.
                director.OpenNewProject(null, null);
                director.DrawNow();
                var first = flow;
                if (first == null || director.Navigator.Beside != first || director.Memory.NewProject != first) failures.Add(name + ": New project did not open beside the menu, kept for the session.");
                flowHost?.Submit(commands.CreateProject("Shop"));
                shownSession = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                director.DrawNow();
                if (director.Memory.NewProject != null || director.Navigator.Beside != null) failures.Add(name + ": New project made for the last session stayed, to tick, once another showed.");
                if (flowHost?.Submit(commands.SendInstruction("render-execution", "Begin")) != null || sent.Count != 2 || sent[1].Session != live)
                {
                    failures.Add(name + ": a build begun on one session sent its next step to another.");
                }
                director.OpenNewProject(null, null);
                flowHost?.Submit(commands.CreateProject("Shop"));
                if (flow == first || sent.Count != 3 || sent[2].Session != shownSession) failures.Add(name + ": opened again, New project was not made afresh for the session showing.");
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
