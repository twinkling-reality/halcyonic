#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using Unity.Profiling;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>
        /// How many director renders laid a dragged plane anew at the larger text where the whole drag no
        /// longer held, so it kept less (<see cref="MenuDrag.Kept"/>): at least one must, or nothing checks it.
        /// </summary>
        private static int keptLessAtLarger;

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

            public List<(MenuFrame Frame, Footer? Side)> DrawnFrames { get; } = new List<(MenuFrame, Footer?)>();

            public MenuFrame? Frame => Last = build();

            public event Action? Changed;

            public event Action? Closed;

            public void Change() => Changed?.Invoke();

            public void Close() => Closed?.Invoke();

            public void Act(string id, string? key)
            {
            }

            public void Drawn(MenuFrame drawn, Footer? sidePanel) => DrawnFrames.Add((drawn, sidePanel));

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
        /// <param name="besideWindow">The characters either side of a window straight ahead: the plane opens under its lane, and a file's subject drags nothing.</param>
        private static IEnumerable<string> RenderMenuDirector(string name, string folder, float radius, float? surfaceDrop, bool besideWindow = false)
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
                var characters = Lineup(root.transform, eyes, radius, surfaceDrop, Presentation, besideWindow: besideWindow);
                var window = besideWindow ? Window(root.transform, eyes) : null;
                var targets = characters.ConvertAll(character => character.Target);
                var state = Projection(characters);
                var opened = characters[3];
                // The file's title, which the drag's checks lengthen to make the file taller under a drag,
                // and whether it asks its Yes, as a confirmation standing on the plane.
                var fileTitle = opened.View.Presentation!.Title;
                var fileConfirming = false;
                looking = besideWindow ? Vector3.forward : opened.Target.BodyPosition - eyes;
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                // The stage as the director reads it: the drag's checks arrange it beside a window, or on another surface, for a while.
                var windowNow = besideWindow;
                var surfaceNow = surface;
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
                        return file = new StubColumn(() =>
                        {
                            var waiting = WaitingFile(fileTitle, StateLanguage.BadgeOf(opened.View.Presentation!), chosen: false,
                                host.PageHeight(host.TitleRows(fileTitle, Glaze.Menu.FileColumnDegrees), besideMenu: false));
                            return fileConfirming ? Confirming(waiting) : waiting;
                        });
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
                    StageNow = () => new MenuDirector.Stage(eyes, looking, targets, surfaceNow, windowNow),
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
                failures.AddRange(PlaneState(name + " director tasks", folder, camera, texture, director.Plane, characters, eyes, window));

                // Its row opens its file beside the menu, and the row stays chosen.
                var drawnTasks = director.Plane.Showing(MenuColumn.Menu);
                if (!director.Navigator.Act(MenuColumn.Menu, TasksColumn.OpenTask, opened.View.WorkstreamId, drawnTasks, null))
                {
                    failures.Add(name + ": a press on Tasks as drawn was not taken.");
                }
                director.DrawNow();
                if (file == null || director.Navigator.Beside != file) failures.Add(name + ": pressing the waiting task's row opened no file beside the menu.");
                var expected = MenuPage.Height(comfort.Text, 1, MenuPlane.TopLine(opened.Target, targets, eyes, looking, surface, besideWindow), ViewField.Current);
                if (made is not float height || Mathf.Abs(height - expected) > 1e-5f)
                {
                    failures.Add(name + ": the file read its page height as " + made + " while it was made, not against its own character's top line (" + expected + ").");
                }

                // In the demonstration a column's send still goes through the host's submissions, to the demonstration's session.
                fileHost?.Submit(commands.SendInstruction("render-execution", "Carry on"));
                if (sent.Count != 1 || sent[0].Session != demonstration) failures.Add(name + ": in the demonstration, a column's send did not reach the host's submissions for its session.");
                else
                {
                    if (file.Last == null || !file.DrawnFrames.Any(drawn => drawn.Frame == file.Last && drawn.Side == null))
                    {
                        failures.Add(name + ": the file did not learn of the very frame it gave being drawn.");
                    }
                    var chosen = director.Navigator.Frames(TasksColumn.Bar(MenuPlace.Tasks, state)).Menu?.Lines.FirstOrDefault(line => line.Chosen)?.Key;
                    if (chosen != opened.View.WorkstreamId) failures.Add(name + ": Tasks does not show the open file's row chosen.");
                    if (director.Plane.LightLine == null && !besideWindow) failures.Add(name + ": the file opened from Tasks shows no light line.");
                }
                failures.AddRange(PlaneState(name + " director file", folder, camera, texture, director.Plane, characters, eyes, window));

                // Beside a window the plane stands under the window's lane, and a file's subject drags nothing: no drag can take it into the lane.
                if (besideWindow)
                {
                    var under = director.Plane.Shown.FirstOrDefault(shown => shown.Kind == MenuColumn.File).View;
                    if (under == null) failures.Add(name + ": no file stands on the plane beside the window.");
                    else
                    {
                        var underLane = director.Plane.Direction;
                        director.HoldSubjectForRender(under.Subject.position);
                        if (director.Dragging) failures.Add(name + ": beside a window, holding the file's subject took hold of the plane.");
                        director.DragSubjectForRender(eyes + Quaternion.Euler(-20f, 0f, 0f) * (under.Subject.position - eyes));
                        director.LetGoForRender();
                        director.DrawNow();
                        if (Mathf.Abs(director.Plane.Direction.Elevation - underLane.Elevation) > 0.01f || director.MovedByHand) failures.Add(name + ": beside a window, the plane moved by hand.");
                        failures.AddRange(PlaneState(name + " director held beside the window", folder, camera, texture, director.Plane, characters, eyes, window));
                    }
                    return failures;
                }

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

                // The characters hop and rise, as they do while they work or wait: the plane, placed against them
                // at rest, stays where it is, its prompts take presses, and a press under way goes on.
                if (director.Plane.Shown.FirstOrDefault(shown => shown.Kind == MenuColumn.File).View is MenuFrameView bobbing
                    && bobbing.Targets.FirstOrDefault(button => button.Available) is GlazeButton pressing)
                {
                    var still = director.Plane.Direction;
                    var waited = bobbing.Unsettles;
                    pressing.HoldPressForRender();
                    foreach (var lift in new[] { 0.0145f, 0.008f, 0f })
                    {
                        foreach (var (view, _) in characters) view.Body.localPosition = new Vector3(0f, lift, 0f);
                        file?.Change();
                        director.DrawNow();
                        var off = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(still.Yaw, director.Plane.Direction.Yaw)), Mathf.Abs(still.Elevation - director.Plane.Direction.Elevation));
                        if (off > 0.001f) failures.Add(name + ": a character's hop moved the plane " + GlazeChecks.Degrees(off) + " degrees; it stands against them at rest.");
                    }
                    if (bobbing.Unsettles != waited) failures.Add(name + ": the characters' hops made the file's prompts wait to settle, refusing presses.");
                    if (!pressing.PressUnderWay) failures.Add(name + ": a press under way ended as the characters hopped.");
                    pressing.EndPressSince(float.MinValue);
                    director.DrawNow();

                    // The file grows taller with no drag: laid again where the stage puts it, its prompts take
                    // presses as before. Only a drag's move makes them wait to settle.
                    var oneRow = fileTitle;
                    var oneRowAt = bobbing.Parts.Select(part => part.position).ToList();
                    fileTitle = oneRow + ", and keep a record of every lockout for the security review at the end of the month";
                    file?.Change();
                    director.DrawNow();
                    if (!bobbing.Parts.Where((part, index) => index < oneRowAt.Count && Vector3.Distance(part.position, oneRowAt[index]) > 0.001f).Any())
                    {
                        failures.Add(name + ": a title in two rows moved none of the file's parts.");
                    }
                    if (bobbing.Unsettles != waited) failures.Add(name + ": the file grown taller with no drag made its prompts wait to settle, refusing presses.");
                    fileTitle = oneRow;
                    file?.Change();
                    director.DrawNow();
                }
                else failures.Add(name + ": no file with a prompt to press stands on the plane while the characters hop.");

                // Holding the file's subject drags the whole plane round the eyes; nothing pressed counts meanwhile.
                // A character standing in for one that comes or goes during a drag, behind the person, where it changes no placement.
                CharacterTarget Extra(string id)
                {
                    var view = CharacterView.Create(root.transform, id);
                    view.Show(Presentation(id, 0));
                    view.transform.SetPositionAndRotation(eyes + Vector3.back * radius, Quaternion.LookRotation(Vector3.forward, Vector3.up));
                    return CharacterTarget.Attach(view, id);
                }
                failures.AddRange(DragMenu(name, folder, camera, texture, director, characters, targets, eyes, title =>
                {
                    fileTitle = title;
                    file?.Change();
                }, confirming =>
                {
                    fileConfirming = confirming;
                    file?.Change();
                }, scale =>
                {
                    GlazeText.SetScale(scale);
                    comfort.Text = scale > 1f ? TextSize.Larger : TextSize.Standard;
                }, (window, standOn) =>
                {
                    windowNow = window;
                    surfaceNow = standOn;
                }, () => surfaceNow, Extra));

                // Folded while another window keeps focus: the plane goes, the banner shows again naming the task still open.
                failures.AddRange(FoldMenu(name, folder, camera, texture, root, director, opened.View.Presentation!.Title));

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
                // Beside its frame, the side panel shows only Close details: the change is the frame's to take.
                var textSize = director.Plane.Shown.FirstOrDefault(column => column.Kind == MenuColumn.Side).View;
                if (change != null && !inPlace && textSize != null && director.Navigator.Act(MenuColumn.Side, change.Id, null, null, textSize.Side))
                {
                    failures.Add(name + ": the text size's change was taken from its side panel beside the frame, which draws only Close details.");
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
                if (AmbientCover.PanelShowing) failures.Add(name + ": the closed bar alone covers the stage's banner.");
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

        /// <summary>
        /// A task's file, the real column, asking a question of two prompts, as the practice scenario does:
        /// each prompt read and answered, then Your answers drawn, held to the plane's checks, and the answer
        /// sent from it once. Drawing Your answers once threw for want of a prompt to quote, so it never
        /// counted as read and the answer could not be sent.
        /// </summary>
        private static IEnumerable<string> RenderYourAnswers(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("Your answers render " + name);
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
                var work = Work.Asking(new QuestionView
                {
                    QuestionId = "render-two-prompts",
                    Answerable = true,
                    AskedAt = Time,
                    Prompts = new List<QuestionPrompt>
                    {
                        new QuestionPrompt
                        {
                            Key = "q0", Header = "Lockout", Text = "How long should a sign-in lockout last?", Multiple = false, FreeText = true,
                            Options = new List<QuestionOption> { new QuestionOption { Label = "15 minutes" }, new QuestionOption { Label = "1 hour" } },
                        },
                        new QuestionPrompt
                        {
                            Key = "q1", Header = "Notice", Text = "Should the person be told when their account locks?", Multiple = false, FreeText = false,
                            Options = new List<QuestionOption> { new QuestionOption { Label = "Yes, by email" }, new QuestionOption { Label = "No" } },
                        },
                    },
                });
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                var sent = new List<CommandEnvelope>();
                FileColumn? file = null;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    File = (host, task) => file = new FileColumn(host, () => work.Present(), commands, () => null, _ => null, () => ""),
                    Connected = () => true,
                    Session = () => session,
                    Submit = (_, command) =>
                    {
                        sent.Add(command);
                        return null;
                    },
                    State = () => state,
                    StageNow = () => new MenuDirector.Stage(eyes, opened.Target.BodyPosition - eyes, targets, surface, false),
                    CharacterOf = _ => opened.Target,
                    Bar = place => TasksColumn.Bar(place, state),
                    SomethingWaits = () => true,
                    // Voice is offered in the editor, so the question's pages carry Hold to talk.
                    Voice = root.AddComponent<HoldToTalk>(),
                });
                var plane = director.Plane;
                bool Press(string action, string? key = null)
                {
                    director.DrawNow();
                    // What the file's page offers takes a press only once it has stood a moment.
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    var taken = director.Navigator.Act(MenuColumn.File, action, key, plane.Showing(MenuColumn.File), null);
                    director.DrawNow();
                    return taken;
                }
                void ReadQuestion()
                {
                    for (var step = 0; step < 8 && file?.Screen.Question.QuestionPart != null; step++) Press(FileScreens.NextPart, FileScreens.QuestionKey);
                }
                director.Open(MenuPlace.Tasks);
                director.DrawNow();
                director.OpenFile(work.Workstream.WorkstreamId);
                director.DrawNow();
                if (file == null)
                {
                    failures.Add(name + ": no file opened for the question of two prompts.");
                    return failures;
                }
                ReadQuestion();

                // The voice says it listens, then writes down: on Hold to talk itself, so the page never grows and
                // nothing under the hand moves; a press under way goes on, and let go, Hold to talk says so again.
                director.DrawNow();
                if (plane.Shown.FirstOrDefault(shown => shown.Kind == MenuColumn.File).View is MenuFrameView page
                    && page.Targets.FirstOrDefault(button => button.Label.text == VoiceText.HoldToTalk) is GlazeButton held)
                {
                    var content = page.Parts[page.Parts.Count - 1].position;
                    held.HoldPressForRender();
                    file.HoldStarted(FileScreens.SpeakAnswer);
                    foreach (var (said, shows) in new[] { (VoiceText.Listening, VoiceText.ListeningWords), (VoiceText.Hearing, VoiceText.WritingDownWords) })
                    {
                        file.Said(said);
                        director.DrawNow();
                        if (held.Label.text != shows) failures.Add(name + ": the voice saying \"" + said + "\", Hold to talk read \"" + held.Label.text + "\", not \"" + shows + "\".");
                        var drift = Vector3.Distance(content, page.Parts[page.Parts.Count - 1].position);
                        if (drift > 0.001f)
                        {
                            failures.Add(name + ": the voice saying \"" + said + "\" moved the file's page " + (drift * 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                                + " mm, its lines \"" + string.Join(" / ", page.Frame?.Lines.Select(line => line.Words) ?? Enumerable.Empty<string>()) + "\".");
                        }
                    }
                    if (!held.PressUnderWay) failures.Add(name + ": a press under way ended as the voice said where it stands.");
                    held.EndPressSince(float.MinValue);
                    file.HoldEnded(FileScreens.SpeakAnswer, letGo: false);
                    director.DrawNow();
                    if (held.Label.text != VoiceText.HoldToTalk) failures.Add(name + ": the hold dropped, Hold to talk read \"" + held.Label.text + "\".");
                }
                else failures.Add(name + ": no Hold to talk on the question's page to watch as the voice speaks.");
                Press(FileScreens.Choose, "0");
                Press(FileScreens.NextQuestion);
                ReadQuestion();
                Press(FileScreens.Choose, "0");
                Press(FileScreens.NextQuestion);
                if (!file.Screen.Question.Reviewing) failures.Add(name + ": the second prompt answered, Next question did not bring Your answers.");

                failures.AddRange(PlaneState(name + " your answers", folder, camera, texture, plane, characters, eyes, null));
                // Drawn, Your answers counts as read: Send answer takes the press, once.
                director.DrawNow();
                if (FileScreens.WhySendWaits(file.Screen) is string waits) failures.Add(name + ": Your answers drawn, Send answer still waits: \"" + waits + "\"");
                if (!Press(FileScreens.SendAnswer)) failures.Add(name + ": Send answer on Your answers took no press.");
                if (sent.OfType<ExecutionAnswerQuestionCommand>().Count() != 1) failures.Add(name + ": Your answers sent " + sent.OfType<ExecutionAnswerQuestionCommand>().Count() + " answers, not one.");
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
        /// The menu and a file dragged by the file's subject (ADR 0026): it follows the hand round the
        /// eyes, takes no press until let go and drawn, keeps where it was left across a redraw, refuses a
        /// step into the labels, and Reset position places it afresh. The dragged plane is held to every
        /// check a placed one is.
        /// </summary>
        /// <param name="retitle">Gives the file another title, as one in two rows that makes it taller.</param>
        /// <param name="confirm">Has the file ask its Yes, or not, as a confirmation standing on the plane.</param>
        /// <param name="textAt">Draws reading text at a scale, the person's text size following it, as Settings' Larger text does.</param>
        /// <param name="targets">The characters the director reads from the stage, which the checks change as characters come and go.</param>
        /// <param name="arrange">Arranges the stage beside a window or not, on a surface this high or none.</param>
        /// <param name="surfaceNow">The surface the stage stands on now.</param>
        /// <param name="extra">Makes a character standing in for one that comes or goes.</param>
        private static IEnumerable<string> DragMenu(string name, string folder, Camera camera, RenderTexture texture, MenuDirector director,
            List<(CharacterView View, CharacterTarget Target)> characters, List<CharacterTarget> targets, Vector3 eyes, Action<string> retitle, Action<bool> confirm,
            Action<float> textAt, Action<bool, float?> arrange, Func<float?> surfaceNow, Func<string, CharacterTarget> extra)
        {
            var failures = new List<string>();
            var plane = director.Plane;
            var placed = plane.Direction;
            var subject = plane.Shown.FirstOrDefault(shown => shown.Kind == MenuColumn.File).View;
            if (subject == null)
            {
                failures.Add(name + ": no file stands on the plane to drag it by.");
                return failures;
            }
            Vector3 Turned(Vector3 point, float right, float down) => eyes + Quaternion.Euler(down, right, 0f) * (point - eyes);
            var held = subject.Subject.position;
            director.HoldSubjectForRender(held);
            if (!director.Dragging) failures.Add(name + ": holding the file's subject did not take hold of the plane.");
            director.DragSubjectForRender(Turned(held, 4f, 0f));
            if (Mathf.Abs(Mathf.DeltaAngle(placed.Yaw, plane.Direction.Yaw) - 4f) > 0.05f)
            {
                failures.Add(name + ": the plane turned " + GlazeChecks.Degrees(Mathf.DeltaAngle(placed.Yaw, plane.Direction.Yaw)) + " degrees with a hand that moved 4.");
            }
            // Mid-drag the director takes no press and starts no hold, even where the navigator would.
            director.Navigator.Drawn(MenuColumn.File, plane.Showing(MenuColumn.File), null);
            if (director.PressForRender(MenuColumn.File, "render-press", null, plane.Showing(MenuColumn.File), null)) failures.Add(name + ": a press counted mid-drag.");
            var begun = 0;
            director.VoiceForRender(new MenuVoice(() => false, () => begun++, () => { }, () => { }));
            var talk = new Prompt("talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true);
            director.HoldPromptForRender(MenuColumn.File, talk, plane.Showing(MenuColumn.File), null);
            if (begun > 0) failures.Add(name + ": a held prompt started the voice mid-drag.");
            failures.AddRange(DragAllocatesNothing(name, director, step => Turned(held, 4f + (step % 2) * 0.5f, 0f)));
            director.DragSubjectForRender(Turned(held, 4f, 0f));
            director.LetGoForRender();
            director.DrawNow();
            if (director.Dragging) failures.Add(name + ": let go, the plane still followed the hand.");
            if (!director.MovedByHand) failures.Add(name + ": dragged, the director does not say the person moved the plane, so Settings would not.");
            failures.AddRange(PlaneState(name + " director dragged", folder, camera, texture, plane, characters, eyes, null));
            if (!director.PressForRender(MenuColumn.File, "render-press", null, plane.Showing(MenuColumn.File), null))
            {
                failures.Add(name + ": drawn where it was left, the plane took no press.");
            }
            director.HoldPromptForRender(MenuColumn.File, talk, plane.Showing(MenuColumn.File), null);
            if (begun != 1) failures.Add(name + ": drawn where it was left, a held prompt started no voice.");

            // A redraw keeps it where it was left.
            var left = plane.Direction;
            director.Redraw();
            director.DrawNow();
            if (Mathf.Abs(Mathf.DeltaAngle(left.Yaw, plane.Direction.Yaw)) > 0.01f || Mathf.Abs(left.Elevation - plane.Direction.Elevation) > 0.01f)
            {
                failures.Add(name + ": a redraw moved the dragged plane.");
            }

            // Into the characters, holding does nothing: up into their labels, or over a desk, down into their bodies.
            held = subject.Subject.position;
            director.HoldSubjectForRender(held);
            if (!director.Dragging) failures.Add(name + ": held again where it was left, the file's subject took no hold of the plane.");
            director.DragSubjectForRender(Turned(held, 0f, plane.Direction.Above ? 15f : -15f));
            if (Mathf.Abs(left.Elevation - plane.Direction.Elevation) > 0.01f) failures.Add(name + ": the plane was dragged into the characters.");
            director.LetGoForRender();
            director.DrawNow();

            // Swept far to the side, it stops where its centre would leave the field as seen turned to the stage.
            if (ViewField.Current is ViewField field && plane.StageYaw is float stage)
            {
                held = subject.Subject.position;
                var before = plane.Direction.Yaw;
                director.HoldSubjectForRender(held);
                if (!director.Dragging) failures.Add(name + ": held to sweep it aside, the file's subject took no hold of the plane.");
                for (var step = 1; step <= 100; step++) director.DragSubjectForRender(Turned(held, step * 1f, 0f));
                if (Mathf.Abs(Mathf.DeltaAngle(before, plane.Direction.Yaw)) < 1f) failures.Add(name + ": swept aside, the plane stayed where it was.");
                var off = Mathf.Abs(Mathf.DeltaAngle(stage, plane.Direction.Yaw));
                if (off > field.Right - ViewField.EdgeMarginDegrees + 0.01f)
                {
                    failures.Add(name + ": swept aside, the plane's centre stands " + GlazeChecks.Degrees(off) + " degrees from the stage's, out of the field turned to it.");
                }
                director.LetGoForRender();
                director.ResetPosition();
                director.DrawNow();
            }

            // Before the headset's field is measured, a sweep aside still stops in reach of the stage, as in a Quest 3S's field.
            if (plane.StageYaw is float centre)
            {
                var measured = ViewField.Current;
                try
                {
                    ViewField.Current = null;
                    held = subject.Subject.position;
                    director.HoldSubjectForRender(held);
                    if (!director.Dragging) failures.Add(name + ": held with no field measured, the file's subject took no hold of the plane.");
                    for (var step = 1; step <= 170; step++) director.DragSubjectForRender(Turned(held, step * 1f, 0f));
                    var off = Mathf.Abs(Mathf.DeltaAngle(centre, plane.Direction.Yaw));
                    if (off > MenuPage.Quest3S.Right - ViewField.EdgeMarginDegrees + 0.01f)
                    {
                        failures.Add(name + ": swept aside with no field measured, the plane's centre stands " + GlazeChecks.Degrees(off) + " degrees from the stage's, past a Quest 3S's field.");
                    }
                    director.LetGoForRender();
                }
                finally
                {
                    ViewField.Current = measured;
                }
                director.ResetPosition();
                director.DrawNow();
            }

            // Dragged as far from the characters as it goes, then laid anew taller, a title in two rows: it
            // keeps only as much of the drag as still holds, so it stays inside the field and clear of every character.
            var title = subject.Frame!.Subject;
            held = subject.Subject.position;
            var level = plane.Direction.Elevation;
            var away = plane.Direction.Above ? -0.5f : 0.5f;
            // A plane already at the field's edge that way has no room to be dragged: there is no drag to keep.
            var room = !(ViewField.Current is ViewField seen) || !(plane.Composition is PlaneComposition laid)
                || MenuPage.Inside(laid, MenuDrag.Turned(plane.Direction, (0f, -2f * away)), seen);
            director.HoldSubjectForRender(held);
            if (!director.Dragging) failures.Add(name + ": held to drag it away from the characters, the file's subject took no hold of the plane.");
            for (var step = 1; step <= 60; step++) director.DragSubjectForRender(Turned(held, 0f, away * step));
            if (!room) Debug.Log("Halcyonic: workspace render: " + name + " stands at the field's edge away from the characters, so no drag is kept as it grows taller.");
            else if (Mathf.Abs(level - plane.Direction.Elevation) < 1f) failures.Add(name + ": dragged away from the characters, the plane stayed where it was.");
            director.LetGoForRender();
            director.DrawNow();
            retitle(title + ", and keep a record of every lockout for the security review at the end of the month");
            director.DrawNow();
            failures.AddRange(PlaneState(name + " director dragged then taller", folder, camera, texture, plane, characters, eyes, null));
            retitle(title);
            director.DrawNow();

            // Dragged as far as it goes at the standard text, aside and then away from the characters, then laid
            // anew at the larger, a plane wider and placed elsewhere by as much: where the whole drag would take it
            // where no drag may, as its centre out of sight of the stage or above the band a drag keeps to, it
            // keeps only as much as every rule still allows. Each place's column is made afresh at each size, as
            // when the menu opens again, so the drag alone is judged, not a page packed for the other size.
            var pass = GlazeText.Scale;
            try
            {
                foreach (var (way, aside) in new[] { ("aside", true), ("away", false) })
                {
                    textAt(1f);
                    director.Navigator.Renew();
                    director.ResetPosition();
                    director.DrawNow();
                    held = subject.Subject.position;
                    var off = plane.Direction.Above ? -1f : 1f;
                    director.HoldSubjectForRender(held);
                    if (!director.Dragging) failures.Add(name + ": held at the standard text, the file's subject took no hold of the plane.");
                    for (var step = 1; step <= 100; step++) director.DragSubjectForRender(aside ? Turned(held, step * 1f, 0f) : Turned(held, 0f, off * step * 0.5f));
                    director.LetGoForRender();
                    director.DrawNow();
                    var dragged = plane.Moved;
                    if (dragged == default) Debug.Log("Halcyonic: workspace render: " + name + " takes no drag " + way + " at the standard text.");
                    var waited = subject.Unsettles;
                    textAt(Comfort.LargerTextScale);
                    director.Navigator.Renew();
                    director.DrawNow();
                    if (dragged != default && !plane.Allows(MenuDrag.Turned(plane.Placed, dragged)))
                    {
                        keptLessAtLarger++;
                        // Laid anew where it keeps less of the drag, the file moved: its prompts wait to settle again.
                        if (subject.Unsettles == waited) failures.Add(name + ": laid anew keeping less of its drag " + way + ", the file moved and its prompts took presses at once.");
                        Debug.Log("Halcyonic: workspace render: " + name + " keeps (" + GlazeChecks.Degrees(plane.Moved.Yaw) + ", " + GlazeChecks.Degrees(plane.Moved.Elevation)
                            + ") of a (" + GlazeChecks.Degrees(dragged.Yaw) + ", " + GlazeChecks.Degrees(dragged.Elevation) + ") degree drag " + way + " at the larger text.");
                    }
                    else Debug.Log("Halcyonic: workspace render: " + name + " has room at the larger text for its drag " + way + ", so keeps it whole.");
                    if (!plane.Allows(plane.Direction)) failures.Add(name + ": laid anew at the larger text, the plane kept more of the drag " + way + " than its rules allow.");
                    failures.AddRange(PlaneState(name + " director dragged " + way + " then larger text", folder, camera, texture, plane, characters, eyes, null));
                    failures.AddRange(InSightOfStage(name + " director dragged " + way + " then larger text", plane, eyes));
                    // A drag never takes the plane's centre above the highest a placement goes, nor below the lowest
                    // the field lets a placement go, or where the stage placed it if that is lower.
                    if (plane.Direction.Elevation > WorkspacePlacement.HighestDegrees + 0.01f)
                    {
                        failures.Add(name + " director dragged " + way + " then larger text: the plane's centre stands " + GlazeChecks.Degrees(plane.Direction.Elevation)
                            + " degrees up, above the " + GlazeChecks.Degrees(WorkspacePlacement.HighestDegrees) + " a drag goes no higher than.");
                    }
                    if (plane.Composition is PlaneComposition laidLarger)
                    {
                        var lowest = Mathf.Min(WorkspacePlacement.Lowest(laidLarger.Size, ViewField.Current), plane.Placed.Elevation);
                        if (plane.Direction.Elevation < lowest - 0.01f)
                        {
                            failures.Add(name + " director dragged " + way + " then larger text: the plane's centre stands " + GlazeChecks.Degrees(plane.Direction.Elevation)
                                + " degrees from eye level, below the " + GlazeChecks.Degrees(lowest) + " a drag goes no lower than.");
                        }
                    }
                }
            }
            finally
            {
                textAt(pass);
            }
            director.Navigator.Renew();
            director.ResetPosition();
            director.DrawNow();

            // A confirmation standing on the plane keeps it where it is, held then or pressed then.
            confirm(true);
            director.DrawNow();
            director.HoldSubjectForRender(subject.Subject.position);
            if (director.Dragging) failures.Add(name + ": held while a confirmation stands, the plane was dragged.");
            director.LetGoForRender();
            director.DrawNow();
            director.PressSubjectForRender();
            confirm(false);
            director.DrawNow();
            director.MatureHoldForRender(subject.Subject.position);
            if (director.Dragging) failures.Add(name + ": pressed while a confirmation stood, its hold dragged the plane once it went.");
            director.LetGoForRender();
            director.DrawNow();

            // Hold to talk pressed under the moving plane: let go, its hold ends with the drag, so no voice starts after.
            var talking = subject.Targets.FirstOrDefault(button => button.Holds);
            if (talking == null) failures.Add(name + ": the file offers no held prompt to press under a drag.");
            else
            {
                // Pressed just before, its press ends as the plane starts to move.
                talking.HoldPressForRender();
                director.HoldSubjectForRender(subject.Subject.position);
                if (talking.PressUnderWay) failures.Add(name + ": Hold to talk pressed as the drag began was still pressed while the plane moved.");
                talking.HoldPressForRender();
                director.DragSubjectForRender(Turned(subject.Subject.position, 1f, 0f));
                director.LetGoForRender();
                director.DrawNow();
                if (talking.PressUnderWay) failures.Add(name + ": Hold to talk pressed under a drag was still pressed after the plane was let go, so its hold could start the voice.");
            }

            // Pressed where a drag may start, then a confirmation standing by the time the hold matures: it drags nothing.
            director.PressSubjectForRender();
            confirm(true);
            director.DrawNow();
            director.MatureHoldForRender(subject.Subject.position);
            if (director.Dragging) failures.Add(name + ": pressed before a confirmation stood, its hold dragged the plane once it matured with the confirmation standing.");
            director.LetGoForRender();
            confirm(false);
            director.DrawNow();

            // A press let go before its hold matured counts no more: a hold maturing later, with no press of its own, drags nothing.
            director.PressSubjectForRender();
            director.LetGoForRender();
            director.MatureHoldForRender(subject.Subject.position);
            if (director.Dragging) failures.Add(name + ": a press let go before its hold matured still dragged the plane when a hold matured later.");
            director.LetGoForRender();
            director.DrawNow();

            // Focus leaving ends a drag, and so does what stands on the plane changing under it.
            director.HoldSubjectForRender(subject.Subject.position);
            if (!director.Dragging) failures.Add(name + ": the file's subject, held, took no hold of the plane.");
            director.FocusLeftForRender();
            if (director.Dragging) failures.Add(name + ": focus left, and the drag went on.");
            director.DrawNow();
            director.HoldSubjectForRender(subject.Subject.position);
            if (!director.Dragging) failures.Add(name + ": focus back and the plane drawn, the file's subject took no hold of the plane.");
            director.Navigator.CloseMenu();
            director.DrawNow();
            if (director.Dragging) failures.Add(name + ": the menu closed under a drag, and the drag went on.");
            director.Open(MenuPlace.Tasks);
            director.DrawNow();

            // Two hands on the subject: the second's press is no press and its release ends nothing; only the
            // first's release lets go, so a drag is never orphaned, refusing every press while it seems over.
            if (subject.SubjectHoldForRender is PointerTarget hand)
            {
                var selects = 0;
                var releases = 0;
                void Counted() => selects++;
                void Ended(bool _) => releases++;
                hand.Selected += Counted;
                hand.Released += Ended;
                try
                {
                    var at = subject.Subject.position;
                    director.ResetPosition();
                    director.DrawNow();
                    hand.PointerForRender(1, Oculus.Interaction.PointerEventType.Select, at);
                    // As the first hand's hold matures.
                    director.MatureHoldForRender(at);
                    if (!director.Dragging) failures.Add(name + ": the first hand's hold took no hold of the plane.");
                    hand.PointerForRender(2, Oculus.Interaction.PointerEventType.Select, at);
                    hand.PointerForRender(2, Oculus.Interaction.PointerEventType.Unselect, at);
                    if (selects != 1 || releases != 0) failures.Add(name + ": a second hand's press on the subject counted as " + selects + " presses and " + releases + " releases; it is no press.");
                    if (!director.Dragging) failures.Add(name + ": a second hand's pinch and release on the subject ended the first hand's drag.");
                    hand.PointerForRender(1, Oculus.Interaction.PointerEventType.Unselect, at);
                    if (releases != 1 || director.Dragging) failures.Add(name + ": the first hand let go of the subject, and the drag went on.");
                }
                finally
                {
                    hand.Selected -= Counted;
                    hand.Released -= Ended;
                }
                director.DrawNow();
            }
            else failures.Add(name + ": the file's subject has no hold to drag by.");

            // Dragged, then the stage arranged beside a window, or on another surface: the drag made for the last
            // arrangement holds no more, and does not come back with it.
            var standOn = surfaceNow();
            foreach (var (window, surface, what) in new[] { (true, standOn, "arranged beside a window"), (false, (float?)(EyeHeight - 1.1f), "on another surface") })
            {
                director.ResetPosition();
                director.DrawNow();
                director.HoldSubjectForRender(subject.Subject.position);
                for (var step = 1; step <= 8; step++) director.DragSubjectForRender(Turned(subject.Subject.position, 1f, 0f));
                director.LetGoForRender();
                director.DrawNow();
                if (!director.MovedByHand) failures.Add(name + ": dragged before the stage was " + what + ", the plane kept no drag.");
                arrange(window, surface);
                director.DrawNow();
                if (director.MovedByHand || plane.Moved != default) failures.Add(name + ": the stage " + what + ", the plane kept the drag made for the last arrangement.");
                if (window)
                {
                    director.HoldSubjectForRender(subject.Subject.position);
                    if (director.Dragging) failures.Add(name + ": the stage " + what + ", holding the file's subject took hold of the plane.");
                    director.LetGoForRender();
                }
                arrange(false, standOn);
                director.DrawNow();
                if (director.MovedByHand) failures.Add(name + ": the stage back as it was, the drag made before came back with it.");
            }

            // A character leaving the stage mid-drag, or one arriving: the drag ends, as its rules were judged against the stage as it began.
            var gone = extra("render-leaves");
            targets.Add(gone);
            director.ResetPosition();
            director.DrawNow();
            director.HoldSubjectForRender(subject.Subject.position);
            if (!director.Dragging) failures.Add(name + ": with one more character on the stage, the file's subject took no hold of the plane.");
            UnityEngine.Object.DestroyImmediate(gone.View.gameObject);
            try
            {
                // Its label gone, nothing that judges the plane reads it.
                plane.Allows(plane.Direction);
                director.DragSubjectForRender(Turned(subject.Subject.position, 1f, 0f));
            }
            catch (Exception error)
            {
                failures.Add(name + ": a character left the stage mid-drag, and the next step threw " + error.GetType().Name + ".");
            }
            if (director.Dragging) failures.Add(name + ": a character left the stage mid-drag, and the drag went on.");
            director.LetGoForRender();
            targets.Remove(gone);
            director.DrawNow();
            director.HoldSubjectForRender(subject.Subject.position);
            var arrives = extra("render-arrives");
            targets.Add(arrives);
            director.StageRefreshedForRender();
            if (director.Dragging) failures.Add(name + ": a character arrived on the stage mid-drag, and the drag went on.");
            director.LetGoForRender();
            targets.Remove(arrives);
            UnityEngine.Object.DestroyImmediate(arrives.View.gameObject);
            director.DrawNow();

            // Reset position places it afresh.
            director.ResetPosition();
            director.DrawNow();
            if (Mathf.Abs(Mathf.DeltaAngle(placed.Yaw, plane.Direction.Yaw)) > 0.01f || Mathf.Abs(placed.Elevation - plane.Direction.Elevation) > 0.01f || director.MovedByHand)
            {
                failures.Add(name + ": Reset position kept the drag.");
            }
            return failures;
        }

        /// <summary>
        /// The plane's centre, as its parts are drawn, inside the measured field less its margin with the head
        /// turned to the stage's centre: a drag, and a plane laid anew under one, never takes it, or an
        /// approval that may come to it, out of sight of the characters.
        /// </summary>
        private static IEnumerable<string> InSightOfStage(string what, MenuPlane plane, Vector3 eyes)
        {
            if (!(ViewField.Current is ViewField field) || !(plane.StageYaw is float stage) || !(plane.Composition is PlaneComposition composition)) yield break;
            // Each part where it is drawn, its size as laid, as the plane's checks take them.
            var parts = new List<GlazeChecks.PlaneShape>();
            for (var c = 0; c < plane.Shown.Count; c++)
            {
                var placed = composition.Parts.Where(part => part.Column == c).ToList();
                var view = plane.Shown[c].View;
                parts.AddRange(view.Parts.Select((part, index) => new GlazeChecks.PlaneShape(part.name, part,
                    new Vector2(placed[index].Width, placed[index].Height) * PlaneComposition.Distance)));
            }
            var toward = GlazeChecks.CompositionCenter(parts) - eyes;
            var yaw = Mathf.DeltaAngle(stage, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg);
            var elevation = Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
            var margin = ViewField.EdgeMarginDegrees - 0.01f;
            var shrunk = new ViewField(field.Left - margin, field.Right - margin, field.Up - margin, field.Down - margin);
            if (!shrunk.Shows(yaw, elevation))
            {
                yield return what + ": the plane's centre stands " + GlazeChecks.Degrees(yaw) + " degrees across and " + GlazeChecks.Degrees(elevation)
                    + " up from the stage's centre, out of the field turned to it.";
            }
        }

        /// <summary>A file asking its Yes on its Send answer: Cancel where Send answer stood, Yes in the free middle.</summary>
        private static MenuFrame Confirming(MenuFrame waiting) => new MenuFrame(waiting.Subject,
            Footer.Confirm(waiting.Footer, PromptSlot.FarRight, new Prompt("yes", "Yes, send", GlazeIcon.SendAnswer, PromptKind.Yes),
                new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel)),
            waiting.SubjectIsData, waiting.Pill, waiting.Sections, waiting.Lines, waiting.Source, waiting.Side, waiting.SourceIsData, waiting.SubjectWaits);

        /// <summary>
        /// A drag's step allocates nothing in Unity's own runtime, as GC Allocated In Frame counts it, where
        /// an enumerator through an interface would be boxed: twenty steps, the least of three tries.
        /// </summary>
        private static IEnumerable<string> DragAllocatesNothing(string name, MenuDirector director, Func<int, Vector3> pointAt)
        {
            var failures = new List<string>();
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            for (var step = 0; step < 4; step++) director.DragSubjectForRender(pointAt(step));
            var probe = recorder.CurrentValue;
            var kept = new byte[256];
            if (recorder.CurrentValue - probe < kept.Length)
            {
                failures.Add(name + ": this editor cannot count allocations, so a drag's steps cannot be checked.");
                return failures;
            }
            var bytes = long.MaxValue;
            for (var repeat = 0; repeat < 3; repeat++)
            {
                var before = recorder.CurrentValue;
                for (var step = 0; step < 20; step++) director.DragSubjectForRender(pointAt(step));
                bytes = Math.Min(bytes, recorder.CurrentValue - before);
            }
            if (bytes > 0) failures.Add(name + ": twenty steps of a drag allocate " + bytes + " bytes; a drag step allocates nothing.");
            Debug.Log("Halcyonic: workspace render: " + name + " drags twenty steps allocating " + bytes + " bytes.");
            return failures;
        }

        /// <summary>
        /// The menu and a file folded while another window keeps focus, then restored (ADR 0023's fold,
        /// on the menu): open, the plane covers the stage's banner; folded, nothing of it shows, nothing
        /// covers the banner, and the banner names <paramref name="title"/> as still open; restored, it
        /// comes back pixel for pixel.
        /// </summary>
        private static IEnumerable<string> FoldMenu(string name, string folder, Camera camera, RenderTexture texture, GameObject root, MenuDirector director, string title)
        {
            var failures = new List<string>();
            try
            {
                FocusGuard.FoldForRender(false);
                director.DrawNow();
                if (!AmbientCover.PanelShowing) failures.Add(name + ": the open menu leaves the stage's banner where it stands.");
                ForceMeshes(root);
                var open = Render(camera, texture);
                FocusGuard.FoldForRender(true);
                director.DrawNow();
                var folded = Render(camera, texture);
                if (director.Plane.gameObject.activeInHierarchy) failures.Add(name + ": the menu still shows while folded.");
                if (AmbientCover.PanelShowing) failures.Add(name + ": the menu still covers the stage's banner while folded.");
                if (AmbientCover.OpenPanel != title) failures.Add(name + ": the banner would say \"" + AmbientCover.OpenPanel + "\" is still open, not \"" + title + "\".");
                FocusGuard.FoldForRender(false);
                director.DrawNow();
                ForceMeshes(root);
                var restored = Render(camera, texture);
                var whole = new RectInt(0, 0, texture.width, texture.height);
                var (gone, _) = Compare(open, folded, whole);
                var (moved, largest) = Compare(open, restored, whole);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + "-director-folded.png"), folded.EncodeToPNG());
                if (gone == 0) failures.Add(name + ": folding the menu changed nothing on the render.");
                if (moved > 0) failures.Add(name + ": " + moved + " pixels differ after the menu was restored (largest " + largest.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ").");
                Debug.Log("Halcyonic: workspace render: " + name + " folds the menu away (" + gone + " pixels) and it comes back " + (moved == 0 ? "exactly." : "with " + moved + " pixels changed."));
                UnityEngine.Object.DestroyImmediate(open);
                UnityEngine.Object.DestroyImmediate(folded);
                UnityEngine.Object.DestroyImmediate(restored);

                // Folded under a drag, the plane lets go: no hand can let go of a plane that does not show.
                var subject = director.Plane.Shown.FirstOrDefault(shown => shown.Kind == MenuColumn.File).View;
                if (subject == null) failures.Add(name + ": no file stands on the plane to drag it by as it folds.");
                else
                {
                    director.HoldSubjectForRender(subject.Subject.position);
                    if (!director.Dragging) failures.Add(name + ": the file's subject, held before folding, took no hold of the plane.");
                    FocusGuard.FoldForRender(true);
                    director.DrawNow();
                    if (director.Dragging) failures.Add(name + ": folded under a drag, the plane still followed the hand.");
                    FocusGuard.FoldForRender(false);
                    director.DrawNow();
                }
            }
            finally
            {
                FocusGuard.FoldForRender(null);
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
