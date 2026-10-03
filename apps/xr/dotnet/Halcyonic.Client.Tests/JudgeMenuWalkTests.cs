using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The menu's host as the director is in the recorded demonstration: the demonstration's session and
/// its recorded reads, instructions and usage limits; no keyboard, no voice, nothing asked of a
/// computer. A task's file opens beside the menu as the director opens one, from Tasks' row. Rows are
/// measured roughly, forty characters to a row, and a page's height is a Quest 3S's.
/// </summary>
internal sealed class DemonstrationMenuHost : IMenuHost
{
    private readonly DemonstrationPlayer player;
    private readonly CommandFactory commands = new(Samples.Client);
    private readonly ActivityLog activity = new();

    public DemonstrationMenuHost(DemonstrationPlayer player, TextSize text = TextSize.Standard)
    {
        this.player = player;
        TextSize = text;
        Comfort = new Comfort { Text = text };
        Navigator = new MenuNavigator(new Dictionary<MenuPlace, Func<IMenuColumn>>
        {
            [MenuPlace.Tasks] = () => new TasksColumn(this),
            // As the director makes it: nothing read from a computer in the demonstration.
            [MenuPlace.Projects] = () => new ProjectsColumn(this, commands, new ProjectsMemory(), () => State == null ? null : WorkOverview.Of(State, new StageVisibility(), _ => true),
                (_, _) => { }),
            [MenuPlace.Usage] = () => new UsageColumn(this, at => player.Recording?.UsageLimitsAt(at)),
            // As the director gives them in the release build: Your space, with no Your computer, then Comfort.
            [MenuPlace.Settings] = () => new SettingsColumn(this, SpaceSettings.Of(() => new SpaceNow(RoomStatus.Initial, RoomOffer.None, StageArrangement.InFront, null),
                id => SpaceActs.Add(id), _ => { }).Concat(ComfortSettings.Of(Comfort, () => { })).ToList()),
        });
        Navigator.Changed += () => changed = true;
    }

    private bool changed = true;

    /// <summary>What Your space's rows raised, for the headset's own layer to do.</summary>
    public List<string> SpaceActs { get; } = new();

    public MenuNavigator Navigator { get; }

    public Comfort Comfort { get; }

    /// <summary>Every command a column sent, through this host, to the demonstration's session.</summary>
    public List<CommandEnvelope> Sent { get; } = new();

    /// <summary>The acknowledgements of what was sent, the recording's words for each.</summary>
    public List<Task<CommandAckMessage>> Acks { get; } = new();

    /// <summary>Every frame drawn, menu, file and side panel, for the brand scan.</summary>
    public List<MenuFrame> Drawn { get; } = new();

    public List<SidePanel> DrawnSides { get; } = new();

    public RealtimeSession Session => player.Session;

    public ClientProjection? State => player.Session.State;

    public bool Connected => player.Session.Status.IsLive;

    public bool Demonstration => true;

    public double Now { get; private set; } = 100;

    public DateTimeOffset Clock { get; private set; } = DateTimeOffset.Parse("2026-10-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    public TimeZoneInfo Zone => TimeZoneInfo.Utc;

    public TextSize TextSize { get; }

    public bool VoiceOffered => false;

    public ControlPlaneApi? Api => null;

    public Task<CommandAckMessage>? Submit(CommandEnvelope command)
    {
        Sent.Add(command);
        var ack = player.Session.SubmitAsync(command);
        Acks.Add(ack);
        return ack;
    }

    public bool KeyboardOffered => false;

    public void OpenKeyboard(string text, string prompt, Action<string> done) => throw new InvalidOperationException("No keyboard opens in the walk.");

    public int RowsOf(string words, float columnDegrees) => Math.Max(1, (words.Length + 39) / 40);

    public int RowsOf(PageLine line, float columnDegrees) => RowsOf(line.Words, columnDegrees);

    public bool FitsHalf(PageLine answer, float columnDegrees) => answer.Words.Length <= 18;

    public int TitleRows(string subject, float columnDegrees) => subject.Length > 40 ? 2 : 1;

    public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);

    public float PageHeight(int subjectRows, bool besideMenu) => MenuPage.Height(TextSize, subjectRows, besideMenu: besideMenu);

    /// <summary>The task whose file the walk opened, as the director opens one beside the menu.</summary>
    public FileColumn? File { get; private set; }

    public void OpenFile(string workstreamId)
    {
        File = new FileColumn(this, () => Present(workstreamId), commands, () => player.Reads, execution => player.InstructionsFor(execution), () => "");
        Navigator.ShowBeside(File, workstreamId);
    }

    /// <summary>What asked to open New project beside the menu: Projects' New project (null, null), or Add a task to a project.</summary>
    public List<(string? Project, string? Name)> NewProjects { get; } = new();

    /// <summary>The start a build may have made, kept only while the walk runs.</summary>
    private sealed class Kept : IKeptCommand
    {
        public string? Id { get; set; }
    }

    private readonly CompanionRecording companion = CompanionRecording.Parse(System.IO.File.ReadAllText(Repository.PathTo(
        "apps/xr/Assets/Halcyonic/Resources/" + CompanionRecording.ResourceName + ".json")));

    /// <summary>The bundled recording of the companion, as the director gives it in the demonstration.</summary>
    public CompanionRecording Companion => companion;

    /// <summary>New project beside the menu, as the director opens it: lane C's flow, playing the companion's recording.</summary>
    public NewProjectFlow? NewProject { get; private set; }

    public void OpenNewProject(string? projectId, string? projectName)
    {
        NewProjects.Add((projectId, projectName));
        NewProject = new NewProjectFlow(this, commands, new Kept(), null, companion);
        NewProject.Open(projectId, projectName);
        Navigator.ShowBeside(NewProject, null);
    }

    private WorkspacePresentation? Present(string workstreamId)
    {
        var state = player.Session.State;
        if (!state.Workstreams.TryGetValue(workstreamId, out var workstream)) return null;
        return WorkspacePresenter.Present(workstream, state, activity, player.Session.Status.IsLive);
    }

    /// <summary>Pumps the session as a frame of the game loop does, and keeps its events for the activity.</summary>
    public void Pump() => activity.Record(player.Session.Pump().Events);

    /// <summary>Time passes: long enough for a part or page drawn before to take a press.</summary>
    public void Wait(double seconds = 1)
    {
        Now += seconds;
        Clock += TimeSpan.FromSeconds(seconds);
    }

    /// <summary>The bar as the session stands, the place showing chosen.</summary>
    public MenuBar Bar => TasksColumn.Bar(Navigator.Place, State);

    /// <summary>
    /// Draws the plane as the director does, frame after frame until it stands still: the columns tick,
    /// the menu, the file and the side panel in front are drawn whole, and each learns of it.
    /// </summary>
    public (MenuFrame? Menu, MenuFrame? File) Draw()
    {
        (MenuFrame? Menu, MenuFrame? File) frames = (null, null);
        for (var frame = 0; frame < 20; frame++)
        {
            Pump();
            Navigator.Tick();
            changed = false;
            frames = Navigator.Frames(Bar);
            if (frames.Menu != null)
            {
                Drawn.Add(frames.Menu);
                Navigator.Drawn(MenuColumn.Menu, frames.Menu, null);
            }
            if (frames.File != null)
            {
                Drawn.Add(frames.File);
                Navigator.Drawn(MenuColumn.File, frames.File, null);
            }
            if ((frames.File ?? frames.Menu)?.Side is SidePanel side)
            {
                DrawnSides.Add(side);
                Navigator.Drawn(MenuColumn.Side, null, side);
            }
            if (!changed) return frames;
            Wait(0.1);
        }
        return frames;
    }

    /// <summary>A press on what is drawn now in <paramref name="from"/>'s slot; true when it was taken.</summary>
    public bool Press(MenuColumn from, string action, string? key = null)
    {
        var (menu, file) = Draw();
        var taken = Navigator.Act(from, action, key, from == MenuColumn.Menu ? menu : file, null);
        Draw();
        return taken;
    }
}

/// <summary>
/// A judge's walk through the whole menu (ADR 0026) on the recorded demonstration, played as the
/// headset plays it, through the menu's own navigator and columns: the closed bar saying a task
/// waits, the menu opened on Tasks with the waiting task first, its file beside the menu on Waiting
/// under its pill, the question's answers and Send answer, the approval's request in parts and Yes
/// only after the last, Checks, Tell it with the recorded instructions, Usage with the recorded
/// limits, Projects and New project's recorded companion to a recap that can't start, and Settings;
/// then the bar closed again. Every frame drawn on the way, and every side
/// panel, is scanned for brand names.
/// </summary>
public class JudgeMenuWalkTests
{
    private DemonstrationPlayer? player;

    [TearDown]
    public async Task StopSession()
    {
        if (player != null) await player.Session.StopAsync();
    }

    private static string Directed(RealtimeSession session) => Demonstration.DirectedWorkstream(session).WorkstreamId;

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public async Task AJudgeWalksTheWholeMenuFromTheClosedBar(TextSize text)
    {
        player = new DemonstrationPlayer(Demonstration.Recording(), Samples.Client, Demonstration.Fast());
        player.Session.Start();
        var host = new DemonstrationMenuHost(player, text);
        var navigator = host.Navigator;
        await Pumping.Until(player.Session, Demonstration.AsksItsQuestion, "the directed work asks its question");

        // The closed bar: one line saying a task waits, Tasks' amber dot.
        Assert.That(navigator.IsOpen, Is.False);
        Assert.That(host.Draw(), Is.EqualTo(((MenuFrame?)null, (MenuFrame?)null)), "closed, the plane holds only the bar");
        Assert.That(host.Bar.ClosedLine, Is.EqualTo("1 task is waiting for you"));
        Assert.That(host.Bar.Sections().Single(section => section.Waits).Words, Is.EqualTo("Tasks"));

        // Opened as the bar's Open does while something waits: on Tasks, the waiting task first.
        navigator.OpenMenu(somethingWaits: true);
        var (tasks, _) = host.Draw();
        Assert.That(navigator.Place, Is.EqualTo(MenuPlace.Tasks));
        Assert.That(tasks!.Subject, Is.EqualTo("1 task is waiting for you"));
        Assert.That(tasks.SubjectWaits, Is.True);
        Assert.That(tasks.Sections.Select(section => section.Words), Is.EqualTo(new[] { "Tasks", "Projects", "Usage", "Settings" }));
        var directed = Directed(player.Session);
        Assert.That(tasks.Lines[0].Key, Is.EqualTo(directed), "the waiting task is the first row");
        Assert.That(tasks.Lines[0].Tone, Is.EqualTo(LineTone.Waiting));

        // Its row opens its file beside the menu, on Waiting under its pill.
        Assert.That(host.Press(MenuColumn.Menu, TasksColumn.OpenTask, directed), Is.True);
        var file = host.File!;
        var (_, frame) = host.Draw();
        Assert.That(navigator.Beside, Is.SameAs(file));
        Assert.That(StateLanguage.WordOf(frame!.Pill!.State), Is.EqualTo("Waiting for you"));
        Assert.That(frame.Sections.Single(section => section.Chosen).Words, Is.EqualTo("Waiting"));

        // The question, read to its end where it has parts, then its answers a page at a time.
        while (frame!.Lines.FirstOrDefault(line => line.Action == FileScreens.NextPart && line.Key == FileScreens.QuestionKey) != null)
        {
            Assert.That(JudgePath.Options(frame), Is.Empty, "no answer before the whole question has shown");
            host.Wait();
            host.Press(MenuColumn.File, FileScreens.NextPart, FileScreens.QuestionKey);
            (_, frame) = host.Draw();
        }
        var question = file.Now!.QuestionToAnswer!;
        var labels = question.Prompts[0].Options.Select(option => option.Label).ToList();
        var offered = new HashSet<string>();
        for (var turn = 0; turn < 4 && !JudgePath.Options(frame!).Contains("0"); turn++)
        {
            host.Wait();
            host.Press(MenuColumn.File, FileScreens.MoreAnswers);
            (_, frame) = host.Draw();
        }
        for (var turn = 0; turn < labels.Count + 1; turn++)
        {
            offered.UnionWith(JudgePath.Options(frame!));
            if (frame!.Lines.All(line => line.Action != FileScreens.MoreAnswers)) break;
            host.Wait();
            host.Press(MenuColumn.File, FileScreens.MoreAnswers);
            (_, frame) = host.Draw();
        }
        Assert.That(offered.Select(key => labels[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)]), Is.EquivalentTo(labels),
            "every option the recording answers");
        while (!JudgePath.Options(frame!).Contains("0"))
        {
            host.Wait();
            host.Press(MenuColumn.File, FileScreens.MoreAnswers);
            (_, frame) = host.Draw();
        }
        host.Press(MenuColumn.File, FileScreens.Choose, "0");
        (_, frame) = host.Draw();
        Assert.That(frame!.Footer[PromptSlot.FarRight]!.Available, Is.True, "Send answer, once the answer chosen is read and in view");
        host.Press(MenuColumn.File, FileScreens.SendAnswer);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ExecutionAnswerQuestionCommand>());
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent.Single()).Payload.Answers.Single().Selected, Is.EqualTo(new[] { Demonstration.FirstOption }));
        await host.Acks.Last();

        // The approval: Waiting under the pill again; Deny's Yes from the first part, and Cancel sends nothing.
        await Pumping.Until(player.Session, Demonstration.AsksForApproval, "the directed work asks for approval");
        host.Press(MenuColumn.File, MenuFrame.ChooseSection, FileScreens.Key(FileSection.Waiting));
        (_, frame) = host.Draw();
        Assert.That(frame!.Sections.Single(section => section.Waits).Words, Is.EqualTo("Waiting"));
        host.Press(MenuColumn.File, FileScreens.Deny);
        (_, frame) = host.Draw();
        Assert.That(frame!.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, deny"));
        host.Press(MenuColumn.File, FileScreens.Cancel);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "Cancel sent nothing");

        // Approve: the whole request again, in parts, and Yes only once the last part has been drawn.
        host.Press(MenuColumn.File, FileScreens.Approve);
        (_, frame) = host.Draw();
        var parts = 1;
        while (frame!.Footer[PromptSlot.Free] == null)
        {
            Assert.That(frame.Lines.Any(line => line.Action == FileScreens.NextPart && line.Key == FileScreens.RequestKey), Is.True,
                "with no Yes yet, there is a part still to read");
            host.Wait();
            host.Press(MenuColumn.File, FileScreens.NextPart, FileScreens.RequestKey);
            (_, frame) = host.Draw();
            parts++;
            Assert.That(parts, Is.LessThan(20));
        }
        Assert.That(frame.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, approve"));
        // Laid out again once read, as the question under it changes, a request read whole stays read.
        Assert.That(parts, Is.GreaterThanOrEqualTo(file.Screen.RequestParts), "Yes came with the last part, not before");
        Assert.That(file.Screen.RequestDrawnWhole, Is.True);
        host.Press(MenuColumn.File, FileScreens.Yes);
        Assert.That(host.Sent, Has.Count.EqualTo(2));
        Assert.That(host.Sent.Last(), Is.InstanceOf<ExecutionRespondToApprovalCommand>());
        Assert.That(host.Press(MenuColumn.File, FileScreens.Yes), Is.True);
        Assert.That(host.Sent, Has.Count.EqualTo(2), "Yes sends once");
        await host.Acks.Last();

        // Checks, from the recording's simulated sources, once the approved turn has ended.
        await Pumping.Until(player.Session, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.Completed, "the approved turn ends");
        host.Press(MenuColumn.File, MenuFrame.ChooseSection, FileScreens.Key(FileSection.Checks));
        for (var tick = 0; tick < 50 && !(frame!.Lines.FirstOrDefault()?.Words.StartsWith("Tests failed", StringComparison.Ordinal) ?? false); tick++)
        {
            host.Wait(0.5);
            (_, frame) = host.Draw();
        }
        Assert.That(frame!.Lines[0].Words, Does.StartWith("Tests failed").And.Contains("1 failed, 23 passed"));
        Assert.That(frame.Source, Does.StartWith("Simulated checks · recorded at "));

        // Tell it: the recorded instructions as rows, one chosen, sent in its words as shown.
        host.Press(MenuColumn.File, MenuFrame.ChooseSection, FileScreens.Key(FileSection.Activity));
        host.Press(MenuColumn.File, FileScreens.TellIt);
        (_, frame) = host.Draw();
        var presets = player.InstructionsFor(file.Now!.Execution!.ExecutionId);
        Assert.That(JudgePath.Instructions(frame!), Is.EqualTo(presets.Select(preset => WorkspaceText.OneLine(preset.Text))));
        host.Press(MenuColumn.File, FileScreens.Preset, "0");
        host.Press(MenuColumn.File, FileScreens.TellIt);
        Assert.That(host.Sent, Has.Count.EqualTo(3));
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Last()).Payload.Text, Is.EqualTo(presets[0].Text));
        await host.Acks.Last();
        await Pumping.Until(player.Session, s => s.State.Runtimes.Count == 0, "the recording plays the instruction and ends");
        Assert.That(Demonstration.DirectedExecution(player.Session)!.LastTestRun!.Outcome, Is.EqualTo(TestOutcome.Passed));
        foreach (var ack in host.Acks)
        {
            Assert.That((await ack).Disposition, Is.EqualTo(CommandAckDisposition.Rejected), "nothing a judge pressed reached an agent");
        }

        // Usage: the recording's limits, no Refresh, each limit's side panel saying it is part of the recording.
        var (menu, _) = host.Draw();
        Assert.That(host.Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage)), Is.True);
        (menu, _) = host.Draw();
        Assert.That(menu!.Subject, Is.EqualTo(UsageText.Subject));
        Assert.That(menu.Lines.Select(line => (line.Words, line.Fact)), Is.EqualTo(new[]
        {
            ("Practice agent, 5-hour window", (string?)"At most 62% left"),
            ("Practice agent, weekly", (string?)"At most 79% left"),
        }));
        Assert.That(menu.Source, Is.EqualTo(UsageLeftPresenter.Recorded));
        Assert.That(menu.Footer[PromptSlot.Rare], Is.Null, "no Refresh in the demonstration");
        host.Press(MenuColumn.Menu, UsageColumn.OpenLimit, "0");
        (menu, _) = host.Draw();
        var side = menu!.Side ?? host.DrawnSides.Last();
        Assert.That(side.Facts.Select(fact => fact.Name), Is.EqualTo(new[] { UsageText.Seen, UsageText.Resets, UsageText.Account }));
        Assert.That(side.Facts.Last().Value, Is.EqualTo("Part of the recording"));

        // Projects: the demonstration's own projects, nothing read from a computer.
        host.Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Projects));
        (menu, _) = host.Draw();
        Assert.That(menu!.Sections.Single(section => section.Chosen).Words, Is.EqualTo("Projects"));
        var projects = player.Session.State.Projects.Values.Select(project => project.Name).ToList();
        Assert.That(menu.Lines.Where(line => line.Action == ProjectsScreens.ChooseProject).Select(line => line.Words), Is.EquivalentTo(projects),
            "the demonstration's own projects");
        Assert.That(menu.Lines.Select(line => line.Words), Has.None.EqualTo(ProjectsText.FoldersHeading), "the demonstration connects no folder, so it lists none");
        // New project is the main prompt; in the demonstration it opens lane C's flow, which plays its own recording.
        Assert.That(menu.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(ProjectsScreens.NewProject));
        var sentBefore = host.Sent.Count;
        host.Press(MenuColumn.Menu, ProjectsScreens.NewProject);
        Assert.That(host.NewProjects, Is.EqualTo(new[] { ((string?)null, (string?)null) }), "New project opens beside the menu");
        Assert.That(navigator.Beside, Is.SameAs(host.NewProject));

        // Your idea: nobody types; talking it through with the companion is the main action, and the recording brings its idea.
        var (_, step) = host.Draw();
        Assert.That(step!.Lines.Any(line => line.Action == NewProjectScreens.TypeIdea), Is.False, "no keyboard opens here");
        Assert.That(step.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.BeginCompanion));
        host.Press(MenuColumn.File, NewProjectScreens.BeginCompanion);
        var flow = host.NewProject!;
        Assert.That((flow.Step, flow.Idea!.OwnWords), Is.EqualTo((NewProjectStep.Questions, host.Companion.Idea)));

        // Questions: the companion's words quoted as its own, said to be recorded, and only the recorded answer to press.
        (_, step) = host.Draw();
        Assert.That(step!.Source, Is.EqualTo(CompanionText.Recorded));
        Assert.That(step.Lines.Any(line => line.Claim && line.Words.StartsWith("The companion says: “", StringComparison.Ordinal)), Is.True,
            "the companion's words are quoted as its own");
        var exchange = flow.Idea.Companion!;
        var recorded = NewProjectScreens.AnswerKey(exchange.Generation, host.Companion.RecordedAnswer(exchange)!);
        Assert.That(step.Lines.Where(line => line.Action == NewProjectScreens.ChooseSuggestion && line.Pressable).Select(line => line.Key),
            Is.EqualTo(new[] { recorded }));
        host.Press(MenuColumn.File, NewProjectScreens.ChooseSuggestion, recorded);
        host.Press(MenuColumn.File, NewProjectScreens.SendAnswer);

        // The recap: the proposal marked as the companion's, the note that it is an AI, and no start in the demonstration.
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        (_, step) = host.Draw();
        Assert.That(step!.Source, Is.EqualTo(CompanionText.Note), "the companion is an AI on the computer, and can be wrong");
        Assert.That(step.Lines.Any(line => line.Fact == CompanionText.SuggestedShort), Is.True, "what the companion suggested is marked");
        var start = step.Footer[PromptSlot.FarRight]!;
        Assert.That((start.Id, start.Available, step.Reason), Is.EqualTo((NewProjectScreens.StartBuilding, false, (string?)EntryText.DemoCannotStart)));
        host.Press(MenuColumn.File, NewProjectScreens.StartBuilding);
        Assert.That((flow.Step, flow.Review), Is.EqualTo((NewProjectStep.Recap, (NewWorkReview?)null)), "Start building starts nothing");
        Assert.That(host.Sent, Has.Count.EqualTo(sentBefore), "New project sent nothing");
        host.Press(MenuColumn.File, Footer.Close);
        // A project chosen: its side panel, and Add a task opening New project for it.
        var projectRow = menu.Lines.First(line => line.Action == ProjectsScreens.ChooseProject);
        host.Press(MenuColumn.Menu, ProjectsScreens.ChooseProject, projectRow.Key);
        (menu, _) = host.Draw();
        Assert.That((menu!.Side ?? host.DrawnSides.Last()).Subject, Is.EqualTo(projectRow.Words));
        if (menu.Footer.All.Any(each => each.Prompt.Id == ProjectsScreens.AddTask && each.Prompt.Available))
        {
            host.Press(MenuColumn.Menu, ProjectsScreens.AddTask);
            Assert.That(host.NewProjects.Last(), Is.EqualTo(((string?)projectRow.Key, (string?)projectRow.Words)));
        }
        Assert.That(host.Sent, Has.Count.EqualTo(sentBefore), "Projects itself sent nothing");
        host.Press(MenuColumn.Menu, SidePanel.Close);

        // Settings: a page a group, Your space then Comfort, each setting a row with its value, chosen to
        // show what it is and what its change does. No Your computer: the release build pairs nothing.
        host.Press(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
        (menu, _) = host.Draw();
        Assert.That(menu!.Subject, Is.EqualTo(SettingsText.Subject));
        var groups = new List<string>();
        var firstPage = string.Join("|", menu.Lines.Select(line => line.Words));
        for (var page = 0; page < 8; page++)
        {
            // A group longer than a page goes on to the next under its heading again; the pages come round to the first.
            if (page > 0 && string.Join("|", menu!.Lines.Select(line => line.Words)) == firstPage) break;
            if (!groups.Contains(menu!.Lines[0].Words)) groups.Add(menu.Lines[0].Words);
            foreach (var row in menu.Lines.Where(line => line.Action == SettingsColumn.OpenSetting).ToList())
            {
                host.Press(MenuColumn.Menu, SettingsColumn.OpenSetting, row.Key);
                var (chosen, _) = host.Draw();
                Assert.That(chosen!.Side!.Subject, Is.EqualTo(row.Words));
                Assert.That(chosen.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(SettingsColumn.ChangeSetting));
                host.Press(MenuColumn.Menu, SidePanel.Close);
            }
            host.Press(MenuColumn.Menu, Footer.NextPage);
            (menu, _) = host.Draw();
        }
        Assert.That(groups, Is.EqualTo(new[] { SettingsText.YourSpace, Comfort.Heading }));
        Assert.That(host.SpaceActs, Is.Empty, "choosing a setting changes nothing");

        // Closed again: the bar alone, the file closed too, saying nothing waits.
        host.Press(MenuColumn.File, Footer.Close);
        navigator.CloseMenu();
        Assert.That(host.Draw(), Is.EqualTo(((MenuFrame?)null, (MenuFrame?)null)));
        Assert.That(host.Bar.ClosedLine, Is.EqualTo("Nothing is waiting for you."));

        // Every word drawn on the way names no brand.
        var words = new HashSet<string> { host.Bar.ClosedLine, "1 task is waiting for you" };
        foreach (var drawn in host.Drawn) words.UnionWith(JudgeWordsTests.WordsOf(drawn));
        foreach (var drawnSide in host.DrawnSides) words.UnionWith(JudgeWordsTests.WordsOf(drawnSide));
        Assert.That(JudgeWordsTests.Branded(words), Is.Empty);
        Assert.That(words, Is.SupersetOf(new[] { "Yes, approve", "Yes, deny", "Part of the recording", UsageLeftPresenter.Recorded, Comfort.Heading, SettingsText.YourSpace }));
    }
}
