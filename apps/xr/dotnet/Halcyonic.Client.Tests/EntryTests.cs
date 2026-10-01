using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Projects and their work, as a snapshot carries them, for the rail, Connect projects and More work.</summary>
internal sealed class Portfolio
{
    private readonly List<ProjectView> projects = new();
    private readonly List<WorkstreamView> workstreams = new();

    public ClientProjection State { get; } = new();

    public Portfolio Project(string id, string name)
    {
        projects.Add(new ProjectView { ProjectId = id, Name = name, CreatedAt = Samples.Time, UpdatedAt = Samples.Time });
        return this;
    }

    public Portfolio Work(string id, string project, WorkstreamStatus status, int minute = 0)
    {
        var level = status switch
        {
            WorkstreamStatus.WaitingForHuman => AttentionLevel.ActionRequired,
            WorkstreamStatus.Failed or WorkstreamStatus.Unknown => AttentionLevel.Notice,
            _ => AttentionLevel.None,
        };
        var workstream = Samples.Workstream(id, status, level: level);
        workstream.ProjectId = project;
        workstream.UpdatedAt = $"2026-09-26T09:{minute:00}:00.000Z";
        workstreams.Add(workstream);
        return this;
    }

    public ClientProjection Apply(string journal = Samples.JournalId)
    {
        var snapshot = Samples.Snapshot(1, workstreams, journal: Samples.Journal(journal));
        snapshot.Projects = projects.ToList();
        State.ApplySnapshot(snapshot, new StateChanges());
        return State;
    }

    /// <summary>What the stage does: the lineup over the work of shown projects, and the one asked for.</summary>
    public static CharacterLineup Stage(ClientProjection state, StageVisibility visibility, int capacity = 6)
    {
        var lineup = new CharacterLineup(capacity);
        lineup.Update(state.Workstreams.Values.Where(work => visibility.Shows(work.ProjectId) || work.WorkstreamId == lineup.Requested));
        return lineup;
    }
}

public class StageVisibilityTests
{
    [Test]
    public void EveryProjectShowsUntilThePersonChooses()
    {
        var visibility = new StageVisibility();
        visibility.UseJournal("journal");
        Assert.That(visibility.All, Is.True);
        Assert.That(visibility.Shows("anything"), Is.True);

        visibility.Hide("b", new[] { "a", "b", "c" });
        Assert.That(visibility.All, Is.False);
        Assert.That(new[] { "a", "b", "c" }.Where(visibility.Shows), Is.EqualTo(new[] { "a", "c" }));
        Assert.That(visibility.Shows("later"), Is.False, "once chosen, a project from elsewhere waits to be shown");

        visibility.Toggle("b", new[] { "a", "b", "c" });
        Assert.That(visibility.Shows("b"), Is.True);
        visibility.ShowAll();
        Assert.That(visibility.All, Is.True);
    }

    [Test]
    public void EachJournalKeepsItsOwnChoice()
    {
        var visibility = new StageVisibility();
        visibility.UseJournal("live");
        visibility.Hide("a", new[] { "a", "b" });
        Assert.That(visibility.UseJournal("demonstration"), Is.True);
        Assert.That(visibility.All, Is.True, "project ids mean nothing in another journal");
        Assert.That(visibility.UseJournal("demonstration"), Is.False);
        visibility.UseJournal("live");
        Assert.That(visibility.Shows("a"), Is.False);
        Assert.That(visibility.Shows("b"), Is.True);
    }

    [Test]
    public void TheChoiceIsSavedAndReadBack()
    {
        var visibility = new StageVisibility();
        visibility.UseJournal("live");
        visibility.Hide("a", new[] { "a", "b", "c" });
        visibility.UseJournal("other");
        visibility.Hide("x", new[] { "x" });

        var read = StageVisibility.Load(visibility.Save());
        read.UseJournal("live");
        Assert.That(new[] { "a", "b", "c" }.Where(read.Shows), Is.EqualTo(new[] { "b", "c" }));
        read.UseJournal("other");
        Assert.That(read.Shows("x"), Is.False);
        read.UseJournal("new");
        Assert.That(read.All, Is.True);
    }

    [Test]
    public void ADamagedPreferenceShowsEverything()
    {
        foreach (var saved in new[] { null, "", "not json", "{\"version\":9}", "{\"version\":1,\"journals\":[{\"journal\":3}]}", "[]" })
        {
            var visibility = StageVisibility.Load(saved);
            visibility.UseJournal("live");
            Assert.That(visibility.All, Is.True, saved ?? "null");
        }
    }

    [Test]
    public void OnlyTheMostRecentJournalsAreRemembered()
    {
        var visibility = new StageVisibility();
        for (var journal = 0; journal < StageVisibility.Journals + 2; journal++)
        {
            visibility.UseJournal("journal " + journal);
            visibility.Hide("a", new[] { "a", "b" });
        }
        var read = StageVisibility.Load(visibility.Save());
        read.UseJournal("journal 0");
        Assert.That(read.All, Is.True, "the oldest is forgotten");
        read.UseJournal("journal " + (StageVisibility.Journals + 1));
        Assert.That(read.Shows("a"), Is.False);
    }
}

public class WorkOverviewTests
{
    private static IEnumerable<int> Sizes() => new[] { 0, 1, 6, 7, 40 };

    /// <summary>
    /// For 0, 1, 6 and many workstreams, in shown and hidden projects, every workstream that needs the
    /// person either has a character or is listed first in More work and counted on the rail.
    /// </summary>
    [TestCaseSource(nameof(Sizes))]
    public void NothingThatNeedsYouDisappears(int count)
    {
        var portfolio = new Portfolio().Project("p-shown", "Shown project").Project("p-hidden", "Hidden project");
        for (var index = 0; index < count; index++)
        {
            var status = (index % 4) switch
            {
                0 => WorkstreamStatus.WaitingForHuman,
                1 => WorkstreamStatus.Running,
                2 => WorkstreamStatus.Completed,
                _ => WorkstreamStatus.Failed,
            };
            portfolio.Work("w" + index.ToString("00"), index % 2 == 0 ? "p-hidden" : "p-shown", status, index % 60);
        }
        var state = portfolio.Apply();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        visibility.Hide("p-hidden", state.Projects.Keys);
        var lineup = Portfolio.Stage(state, visibility);
        var overview = WorkOverview.Of(state, visibility, id => lineup.SlotOf(id) >= 0);

        var needing = state.Workstreams.Values.Where(work => work.Status == WorkstreamStatus.WaitingForHuman).ToList();
        Assert.That(overview.NeedsYou, Is.EqualTo(needing.Count));
        foreach (var work in needing)
        {
            var onStage = lineup.SlotOf(work.WorkstreamId) >= 0;
            var listed = overview.OffStage.Any(off => off.Workstream.WorkstreamId == work.WorkstreamId);
            Assert.That(onStage ^ listed, Is.True, work.WorkstreamId + " is either on the stage or in More work");
        }
        Assert.That(overview.NeedsYouOffStage, Is.EqualTo(needing.Count(work => lineup.SlotOf(work.WorkstreamId) < 0)));
        Assert.That(overview.Projects.Sum(project => project.NeedsYou), Is.EqualTo(needing.Count), "hidden projects keep their attention count");
        Assert.That(overview.OffStage.Count + lineup.Slots.Count(slot => slot != null), Is.EqualTo(count), "every workstream is somewhere");
        var firstCalm = overview.OffStage.ToList().FindIndex(off => off.Workstream.Status != WorkstreamStatus.WaitingForHuman);
        if (firstCalm >= 0)
        {
            Assert.That(overview.OffStage.Skip(firstCalm).Any(off => off.Workstream.Status == WorkstreamStatus.WaitingForHuman), Is.False,
                "what needs the person comes first in More work");
        }
        Assert.That(overview.OffStage.Where(off => off.Workstream.ProjectId == "p-hidden").All(off => off.Reason == OffStageReason.ProjectHidden), Is.True);
        Assert.That(overview.OffStage.Where(off => off.Workstream.ProjectId == "p-shown").All(off => off.Reason == OffStageReason.StageFull), Is.True);
    }

    [Test]
    public void ProjectsAreCountedByTheLineupsTiers()
    {
        var state = new Portfolio()
            .Project("b", "beta").Project("a", "Alpha").Project("c", "Empty")
            .Work("w1", "a", WorkstreamStatus.WaitingForHuman)
            .Work("w2", "a", WorkstreamStatus.Running)
            .Work("w3", "a", WorkstreamStatus.Verifying)
            .Work("w4", "a", WorkstreamStatus.Unknown)
            .Work("w5", "b", WorkstreamStatus.Completed)
            .Apply();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        visibility.Hide("b", state.Projects.Keys);
        var overview = WorkOverview.Of(state, visibility, id => id != "w2");

        Assert.That(overview.Projects.Select(project => project.Name), Is.EqualTo(new[] { "Alpha", "beta", "Empty" }), "by name, ignoring case");
        var alpha = overview.Projects[0];
        Assert.That((alpha.Work, alpha.NeedsYou, alpha.Notice, alpha.Active, alpha.OffStage, alpha.Shown), Is.EqualTo((4, 1, 1, 2, 1, true)));
        Assert.That(EntryText.ProjectDetail(alpha), Is.EqualTo("Shown · 1 task waiting for you, 1 finished, 2 running"));
        Assert.That(EntryText.ProjectDetail(overview.Projects[1]), Is.EqualTo("Hidden · 1 task paused"));
        Assert.That(EntryText.ProjectDetail(overview.Projects[2]), Is.EqualTo("Shown · no work yet"));
        Assert.That(EntryText.ConnectDetail(overview), Is.EqualTo("2 of 3 shown"));
        Assert.That(EntryText.MoreWorkDetail(overview), Is.EqualTo("1 task"));
        Assert.That(EntryText.OffStageDetail(overview.OffStage[0], live: true), Is.EqualTo("Working · Alpha · no room on the stage"));
        Assert.That(EntryText.OffStageDetail(overview.OffStage[0], live: false), Is.EqualTo("Last known: Working · Alpha · no room on the stage"));
    }

    [Test]
    public void TheRailLeadsWithWhatNeedsYouThenWhatShows()
    {
        var state = new Portfolio()
            .Project("a", "A").Project("b", "B").Project("c", "C").Project("d", "D")
            .Work("w1", "d", WorkstreamStatus.WaitingForHuman)
            .Apply();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        visibility.Hide("a", state.Projects.Keys);
        visibility.Hide("d", state.Projects.Keys);
        var overview = WorkOverview.Of(state, visibility, _ => false);
        Assert.That(overview.ForRail(3).Select(project => project.Name), Is.EqualTo(new[] { "D", "B", "C" }),
            "a hidden project that needs the person is never pushed off the rail by quieter ones");
        Assert.That(overview.ForRail(0), Is.Empty);
    }

    [Test]
    public void ProjectNamesShowByTheOneRule()
    {
        var state = new Portfolio().Project("a", "Shop‮<b>x</b>\nnew").Work("w1", "a", WorkstreamStatus.Running).Apply();
        var visibility = new StageVisibility();
        var overview = WorkOverview.Of(state, visibility, _ => false);
        Assert.That(overview.Projects[0].Name, Is.EqualTo(LabelText.Plain("Shop‮<b>x</b>\nnew")));
        Assert.That(overview.OffStage[0].ProjectName, Does.Contain("‹U+202E›"));
    }
}

public class LineupRequestTests
{
    private static WorkstreamView Work(string id, WorkstreamStatus status, int minute)
    {
        var workstream = Samples.Workstream(id, status, level: status == WorkstreamStatus.WaitingForHuman ? AttentionLevel.ActionRequired : AttentionLevel.None);
        workstream.UpdatedAt = $"2026-09-26T09:{minute:00}:00.000Z";
        return workstream;
    }

    [Test]
    public void WorkAskedForTakesTheWeakestSlotAndKeepsIt()
    {
        var lineup = new CharacterLineup(3);
        var work = new List<WorkstreamView>
        {
            Work("needs", WorkstreamStatus.WaitingForHuman, 1),
            Work("running", WorkstreamStatus.Running, 2),
            Work("recent", WorkstreamStatus.Completed, 30),
            Work("old", WorkstreamStatus.Completed, 3),
        };
        lineup.Update(work);
        Assert.That(lineup.SlotOf("old"), Is.EqualTo(-1));

        lineup.Request("old");
        lineup.Update(work);
        Assert.That(lineup.SlotOf("old"), Is.GreaterThanOrEqualTo(0), "the person asked for it");
        Assert.That(lineup.SlotOf("recent"), Is.EqualTo(-1), "it took the weakest character's place");
        Assert.That(lineup.SlotOf("needs"), Is.GreaterThanOrEqualTo(0));

        work.Add(Work("newer", WorkstreamStatus.Completed, 50));
        lineup.Update(work);
        Assert.That(lineup.SlotOf("old"), Is.GreaterThanOrEqualTo(0), "a more recent rest does not push out what was asked for");

        lineup.Request(null);
        lineup.Update(work);
        Assert.That(lineup.SlotOf("old"), Is.EqualTo(-1), "once withdrawn, it waits by rank again");
    }

    [Test]
    public void ARequestEndsWhenItsWorkLeaves()
    {
        var lineup = new CharacterLineup(1);
        var work = new List<WorkstreamView> { Work("a", WorkstreamStatus.WaitingForHuman, 1), Work("b", WorkstreamStatus.Completed, 2) };
        lineup.Request("b");
        lineup.Update(work);
        Assert.That(lineup.Slots, Is.EqualTo(new[] { "b" }), "with one slot, what was asked for stands there");
        work.RemoveAt(1);
        lineup.Update(work);
        Assert.That(lineup.Requested, Is.Null);
        Assert.That(lineup.Slots, Is.EqualTo(new[] { "a" }));
    }
}

public class ProjectIdeaTests
{
    [Test]
    public void APreciseIdeaGoesStraightToTheRecap()
    {
        var idea = new ProjectIdea();
        idea.UseIdea("  A recipe tracker that suggests dinners from what is in my fridge.\nIt should work offline.");
        Assert.That(idea.HasRecap, Is.True);
        Assert.That(idea.FirstTask, Does.StartWith("A recipe tracker"));
        Assert.That(idea.Name, Is.EqualTo("A recipe tracker that suggests dinners"));
        Assert.That(idea.Name.Length, Is.LessThanOrEqualTo(ProjectIdea.SuggestedNameLength));
        Assert.That(idea.Problem, Is.Null);

        Assert.That(idea.Rename("Fridge dinners"), Is.True);
        idea.UseIdea("Something else entirely.");
        Assert.That(idea.Name, Is.EqualTo("Fridge dinners"), "a typed name is kept");
        Assert.That(idea.Rewrite("   "), Is.False);
        Assert.That(idea.FirstTask, Is.EqualTo("Something else entirely."));
    }

    [Test]
    public void AVagueIdeaIsShapedByFixedQuestions()
    {
        var idea = new ProjectIdea();
        idea.BeginGuide();
        Assert.That(ProjectIdea.Questions[idea.Question].Prompt, Is.EqualTo("What kind of thing is it?"));
        Assert.That(idea.Answer(""), Is.False, "the kind is needed");
        Assert.That(idea.Answer("A website"), Is.True);
        Assert.That(idea.Answer("My team"), Is.True);
        Assert.That(idea.Choices, Is.EqualTo(new[] { "Show one page that says what it is", "Let visitors leave their email" }));
        Assert.That(idea.Answer(idea.Choices[0]), Is.True);
        Assert.That(idea.Skip(), Is.True);
        Assert.That(idea.FirstTask, Is.EqualTo("Make a website for my team. First, show one page that says what it is."));
        Assert.That(idea.Name, Is.EqualTo("New website"));

        var again = new ProjectIdea();
        again.BeginGuide();
        foreach (var answer in new[] { "A website", "My team", "Show one page that says what it is" }) again.Answer(answer);
        again.Skip();
        Assert.That(again.FirstTask, Is.EqualTo(idea.FirstTask), "the same answers always give the same recap");
    }

    [Test]
    public void TypedAnswersGoInAsTyped()
    {
        var idea = new ProjectIdea();
        idea.BeginGuide();
        idea.Answer("IoT sensor dashboard");
        idea.Answer("Our greenhouse volunteers");
        Assert.That(idea.Choices, Is.Empty, "no first steps are offered for a kind of the person's own");
        idea.Answer("Plot the humidity readings.");
        idea.Answer("Greenhouse");
        Assert.That(idea.FirstTask, Is.EqualTo("Make IoT sensor dashboard for Our greenhouse volunteers. First, Plot the humidity readings."));
        Assert.That(idea.Name, Is.EqualTo("Greenhouse"));
    }

    [Test]
    public void ChangingTheKindForgetsAFirstStepThatNoLongerFits()
    {
        var idea = new ProjectIdea();
        idea.BeginGuide();
        idea.Answer("An app");
        idea.Answer("Just me");
        idea.Answer("Keep a list that I can add to");
        Assert.That(idea.Back(), Is.True);
        Assert.That(idea.Back(), Is.True);
        Assert.That(idea.Back(), Is.True);
        Assert.That(idea.Back(), Is.False);
        idea.Answer("A tool or script");
        Assert.That(idea.AnswerTo(ProjectIdea.FirstStepQuestion), Is.Null);
        idea.Answer("Just me");
        idea.Answer("Rename files by a pattern");
        idea.Skip();
        Assert.That(idea.FirstTask, Is.EqualTo("Make a tool or script for me. First, rename files by a pattern."));
        Assert.That(idea.Name, Is.EqualTo("New tool"));
    }

    [Test]
    public void WorkForAnExistingProjectAsksNoName()
    {
        var idea = new ProjectIdea("project-1", "Storefront");
        Assert.That(idea.Name, Is.EqualTo("Storefront"));
        idea.BeginGuide();
        idea.Answer("An app");
        idea.Answer("Other people");
        idea.Answer("Do its main job on one screen");
        Assert.That(idea.Question, Is.EqualTo(ProjectIdea.Questions.Count), "the name question is skipped");
        Assert.That(idea.Name, Is.EqualTo("Storefront"));
        Assert.That(idea.Rename("Other"), Is.False);
        Assert.That(idea.Back(), Is.True);
        Assert.That(idea.Question, Is.EqualTo(ProjectIdea.FirstStepQuestion));
    }

    [Test]
    public void TheRecapStaysWithinTheContract()
    {
        var idea = new ProjectIdea();
        Assert.That(idea.Problem, Is.EqualTo("Name the project in at most 200 characters."));
        idea.UseIdea(new string('x', 4001));
        Assert.That(idea.Name.Length, Is.EqualTo(ProjectIdea.SuggestedNameLength));
        Assert.That(idea.Problem, Does.Contain("4,000"));
        idea.Rename(new string('n', 201));
        Assert.That(idea.Problem, Does.Contain("200"));
        Assert.That(ProjectIdea.NameFrom("...?"), Is.EqualTo("New project"));
        Assert.That(ProjectIdea.NameFrom("Fix it."), Is.EqualTo("Fix it"));
    }
}

public class AttentionWatchTests
{
    [Test]
    public void OffersOnlyWorkThatNewlyNeedsYou()
    {
        var portfolio = new Portfolio().Project("a", "A").Project("b", "B")
            .Work("already", "a", WorkstreamStatus.WaitingForHuman)
            .Work("calm", "b", WorkstreamStatus.Running);
        var state = portfolio.Apply();
        var watch = new AttentionWatch();
        watch.Begin(state);
        Assert.That(watch.Next(state), Is.Null, "what needed the person already is on the stage and the rail");

        var changed = new Portfolio().Project("a", "A").Project("b", "B")
            .Work("already", "a", WorkstreamStatus.WaitingForHuman)
            .Work("calm", "b", WorkstreamStatus.WaitingForHuman);
        var next = changed.Apply();
        Assert.That(watch.Next(next)?.WorkstreamId, Is.EqualTo("calm"));
        Assert.That(watch.Next(next)?.WorkstreamId, Is.EqualTo("calm"), "it stays offered until answered");
        watch.Dismiss("calm");
        Assert.That(watch.Next(next), Is.Null);

        var answered = new Portfolio().Project("a", "A").Project("b", "B")
            .Work("already", "a", WorkstreamStatus.Running)
            .Work("calm", "b", WorkstreamStatus.Running).Apply();
        Assert.That(watch.Next(answered), Is.Null);
        var again = new Portfolio().Project("a", "A").Project("b", "B")
            .Work("already", "a", WorkstreamStatus.WaitingForHuman)
            .Work("calm", "b", WorkstreamStatus.Running).Apply();
        Assert.That(watch.Next(again)?.WorkstreamId, Is.EqualTo("already"), "needing the person again is news again");
    }
}

public class BuildSequenceTests
{
    private static readonly CommandFactory Commands = new(new ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" });

    private static NewWorkDraft Draft(string? project = null)
    {
        var draft = new NewWorkDraft(Commands) { ProjectId = project, Objective = "Make a website for my team." };
        draft.ChooseRuntime(new RuntimeDescriptor
        {
            RuntimeId = "mock", DisplayName = "Mock runtime", Kind = "mock", Synthetic = true, ModelChoice = ModelChoice.None,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        });
        return draft;
    }

    private static CommandView Completed(CommandEnvelope command, CommandResult result) =>
        new() { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = result };

    private static ClientProjection With(params CommandView[] commands)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = commands.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    [Test]
    public void SendsEachCommandOnlyAfterTheOneBeforeCompleted()
    {
        var draft = Draft();
        var sequence = new BuildSequence(draft, Commands, "Recipes");
        Assert.That(sequence.Steps.Select(step => step.Kind), Is.EqualTo(new[] { BuildStepKind.CreateProject, BuildStepKind.CreateWorkstream, BuildStepKind.StartWork }));
        var project = sequence.Begin();
        Assert.That(project, Is.InstanceOf<ProjectCreateCommand>());
        Assert.That(sequence.Unresolved, Is.EqualTo(project.CommandId));
        Assert.That(sequence.Advance(With()), Is.Null);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo("Sent, waiting for the result"));

        var accepted = new CommandView { CommandId = project.CommandId, Status = CommandStatus.Accepted };
        Assert.That(sequence.Advance(With(accepted)), Is.Null, "accepted is not done");

        var workstream = sequence.Advance(With(Completed(project, new ProjectCreatedResult { ProjectId = "p1" })));
        Assert.That(workstream, Is.InstanceOf<WorkstreamCreateCommand>());
        Assert.That(((WorkstreamCreateCommand)workstream!).Payload.ProjectId, Is.EqualTo("p1"));
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo("Confirmed"));

        var start = sequence.Advance(With(Completed(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" })));
        Assert.That(start, Is.InstanceOf<ExecutionStartCommand>());
        Assert.That(((ExecutionStartCommand)start!).Payload.WorkstreamId, Is.EqualTo("w1"));
        Assert.That(((ExecutionStartCommand)start).Payload.Instruction, Is.EqualTo("Make a website for my team."));
        Assert.That(sequence.Started, Is.False);

        Assert.That(sequence.Advance(With(Completed(start, new ExecutionCreatedResult { ExecutionId = "e1" }))), Is.Null);
        Assert.That(sequence.Started, Is.True);
        Assert.That(sequence.Unresolved, Is.Null);
        Assert.That(sequence.Steps.All(step => step.Status == BuildStepStatus.Confirmed), Is.True);
    }

    [Test]
    public void WorkForAnExistingProjectCreatesNoProject()
    {
        var sequence = new BuildSequence(Draft("existing"), Commands, null);
        Assert.That(sequence.Steps.Select(step => step.Kind), Is.EqualTo(new[] { BuildStepKind.CreateWorkstream, BuildStepKind.StartWork }));
        Assert.That(((WorkstreamCreateCommand)sequence.Begin()).Payload.ProjectId, Is.EqualTo("existing"));
    }

    [Test]
    public void AProjectedCompletionWinsOverALostAcknowledgement()
    {
        var sequence = new BuildSequence(Draft("existing"), Commands, null);
        var workstream = sequence.Begin();
        sequence.AcknowledgementLost(new CommandOutcomeUnknownException(workstream.CommandId, "The socket closed."));
        Assert.That(sequence.Advance(With()), Is.Null);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo("Effect unknown. Check the work before trying again."));
        Assert.That(sequence.Unresolved, Is.EqualTo(workstream.CommandId), "an unknown outcome is kept");
        Assert.That(sequence.CanRetry, Is.False);
        Assert.That(sequence.Advance(With(Completed(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" }))), Is.InstanceOf<ExecutionStartCommand>());
    }

    [Test]
    public void ARefusalStopsAndCanBeSentAgainAsANewCommand()
    {
        var draft = Draft("existing");
        var sequence = new BuildSequence(draft, Commands, null);
        var workstream = sequence.Begin();
        var start = sequence.Advance(With(Completed(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" })))!;
        var refused = new CommandView
        {
            CommandId = start.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "The runtime needs a working directory." },
        };
        Assert.That(sequence.Advance(With(refused)), Is.Null);
        Assert.That(sequence.Stopped, Is.True);
        Assert.That(EntryText.StepStatus(sequence.Steps[1]), Is.EqualTo("Could not do that: The runtime needs a working directory."));
        Assert.That(sequence.CanRetry, Is.True, "a refused command cannot have run");
        var again = sequence.Retry();
        Assert.That(again, Is.InstanceOf<ExecutionStartCommand>());
        Assert.That(again.CommandId, Is.Not.EqualTo(start.CommandId));
        Assert.That(((ExecutionStartCommand)again).Payload.WorkstreamId, Is.EqualTo("w1"), "the work already created is reused");
    }

    [Test]
    public void AFailureThatMayHaveRunOrAnUnexpectedResultKeepsTheGuard()
    {
        var sequence = new BuildSequence(Draft("existing"), Commands, null);
        var workstream = sequence.Begin();
        var failed = new CommandView
        {
            CommandId = workstream.CommandId, Status = CommandStatus.Failed,
            Failure = new CommandFailure { Code = "timeout", Message = "No answer.", Effect = FailureEffect.Unknown },
        };
        sequence.Advance(With(failed));
        Assert.That(sequence.Unresolved, Is.EqualTo(workstream.CommandId));
        Assert.That(sequence.CanRetry, Is.False);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Does.EndWith("check the work before trying again."));

        var other = new BuildSequence(Draft("existing"), Commands, null);
        var command = other.Begin();
        other.Advance(With(Completed(command, new ProjectCreatedResult { ProjectId = "p" })));
        Assert.That(other.Steps[0].Status, Is.EqualTo(BuildStepStatus.Unexpected));
        Assert.That(other.Unresolved, Is.EqualTo(command.CommandId));
        Assert.That(other.CanRetry, Is.False);
    }

    [Test]
    public void ACommandNeverSentIsReleased()
    {
        var sequence = new BuildSequence(Draft(), Commands, "Recipes");
        sequence.Begin();
        sequence.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        sequence.Advance(null);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo("Not sent: your Mac is not connected."));
        Assert.That(sequence.Unresolved, Is.Null);
        Assert.That(sequence.Retry("Recipes, renamed"), Is.InstanceOf<ProjectCreateCommand>());
    }
}

public class EntryWordsTests
{
    [Test]
    public void TheRailSaysOnlyWhatMattersMostAboutEachProject()
    {
        ProjectSummary Project(bool shown, int work, int active = 0, int needsYou = 0, int notice = 0) =>
            new("p", "Project", shown, work, active, needsYou, notice, 0);
        Assert.That(EntryText.ChipDetail(Project(true, 4, active: 2, needsYou: 1, notice: 1)), Is.EqualTo("1 task waiting"));
        Assert.That(EntryText.ChipDetail(Project(false, 4, active: 2, notice: 1)), Is.EqualTo("Hidden · 1 finished"));
        Assert.That(EntryText.ChipDetail(Project(true, 2, active: 2)), Is.EqualTo("2 tasks running"));
        Assert.That(EntryText.ChipDetail(Project(true, 2)), Is.EqualTo("2 tasks paused"));
        Assert.That(EntryText.ChipDetail(Project(false, 0)), Is.EqualTo("Hidden · no work yet"));
    }

    [Test]
    public void TasksWaitingForYouAreSaidAsAPersonWouldSayThem()
    {
        ProjectSummary Project(bool shown, int needsYou) => new("p", "Project", shown, 5, 0, needsYou, 0, 0);
        Assert.That(EntryText.WaitingForYou(1), Is.EqualTo("1 task is waiting for you"));
        Assert.That(EntryText.WaitingForYou(2), Is.EqualTo("2 tasks are waiting for you"));
        Assert.That(EntryText.ChipDetail(Project(true, 2)), Is.EqualTo("2 tasks waiting"));
        Assert.That(EntryText.ChipDetail(Project(false, 3)), Is.EqualTo("Hidden · 3 waiting"));
        Assert.That(EntryText.ProjectDetail(Project(true, 2)), Is.EqualTo("Shown · 2 tasks are waiting for you"));
        Assert.That(EntryText.ProjectDetail(Project(true, 1)), Is.EqualTo("Shown · 1 task is waiting for you"));
        Assert.That(EntryText.WaitingNow("Import recipes"), Is.EqualTo("\u201CImport recipes\u201D is waiting for you."));
        Assert.That(AmbientText.NeedsYouLine(2), Is.EqualTo(EntryText.WaitingForYou(2)), "the ambient line and the entry agree");
    }

    [Test]
    public void TheRecapSaysWhereTheModelRunsAndChoosesNothing()
    {
        var draft = new NewWorkDraft(new CommandFactory(Samples.Client));
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Runs with: not chosen yet"));
        Assert.That(EntryText.ModelLine(draft), Does.Contain("Nothing is chosen for you"));

        draft.ChooseRuntime(Samples.MockRuntime());
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Runs with: Mock runtime (simulated)"), "the recorded demonstration keeps its names");
        Assert.That(EntryText.RunsWith(draft, live: true), Is.EqualTo("Runs with: " + EntryText.PracticeRun));
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("The runtime chooses its model. Simulated: nothing is built."));

        var listed = Samples.MockRuntime();
        listed.RuntimeId = "local";
        listed.DisplayName = "Local‮ agent";
        listed.Synthetic = false;
        listed.ModelChoice = ModelChoice.Listed;
        draft.ChooseRuntime(listed);
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Runs with: Local‹U+202E› agent, no model yet"), "a runtime's name shows by the one rule");
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("Model: Reading this runtime's models."));
        var remote = new RuntimeModel { ModelRef = "hosted/x", DisplayName = "Hosted", Served = ModelServed.Remote, ToolCalling = ModelToolCalling.Declared };
        draft.SetModels(new RuntimeModelsResponse { RuntimeId = "local", Result = new AvailableModels { Models = new List<RuntimeModel> { remote } } });
        Assert.That(draft.Model, Is.Null, "a remote model is never chosen for the person");
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Runs with: Local‹U+202E› agent, no model yet"));
        Assert.That(draft.ChooseModel(remote), Is.False, "the first press only says where it runs");
        Assert.That(EntryText.ConfirmElsewhere(remote), Is.EqualTo("Runs on a remote service: your code and instructions go there. Press again to use it."));
        Assert.That(draft.ChooseModel(remote), Is.True);
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("Runs on a remote service: your code and instructions go there; tools declared."));
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Runs with: Local‹U+202E› agent, Hosted, remote"));

        var local = new RuntimeModel { ModelRef = "ollama/qwen3.6", DisplayName = "qwen3.6 (Ollama)", Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared };
        draft.ChooseRuntime(listed);
        draft.SetModels(new RuntimeModelsResponse { RuntimeId = "local", Result = new AvailableModels { Models = new List<RuntimeModel> { remote, local } } });
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Runs with: Local‹U+202E› agent, qwen3.6 (Ollama), on your Mac"));
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("Chosen for you: it runs on your Mac; tools declared."));
    }

    [Test]
    public void APracticeRunIsNamedForWhatItDoesAndOfferedLast()
    {
        var mock = Samples.MockRuntime();
        var real = Samples.MockRuntime();
        real.RuntimeId = "opencode";
        real.DisplayName = "OpenCode 2.0.18";
        real.Synthetic = false;
        var cannotStart = Samples.MockRuntime();
        cannotStart.RuntimeId = "watch-only";
        cannotStart.Synthetic = false;
        cannotStart.Capabilities.StartExecution = false;
        // The fifth headset session: "Mock runtime (development fixture)" beside OpenCode was taken for a real one.
        Assert.That(EntryText.RuntimeName(mock, live: true), Is.EqualTo("Practice run: builds nothing"));
        Assert.That(EntryText.RuntimeName(mock), Does.EndWith("(simulated)"), "the demonstration is unaffected");
        Assert.That(EntryText.RuntimeName(real, live: true), Is.EqualTo("OpenCode 2.0.18"));
        Assert.That(EntryText.RuntimeChoices(new[] { mock, cannotStart, real }).Select(runtime => runtime.RuntimeId),
            Is.EqualTo(new[] { "opencode", mock.RuntimeId }), "real runtimes first, a simulated one last, and none that cannot start work");
    }

    [Test]
    public void TheWordsAreShortPlainAndNameNoBrand()
    {
        var words = new List<string>
        {
            EntryText.ConnectProjects, EntryText.CreateProject, EntryText.MoreWork, EntryText.ContinueCreating, EntryText.WelcomeTitle,
            EntryText.WelcomeLine, EntryText.ConnectInvite, EntryText.CreateInvite, EntryText.NotNow, EntryText.ConnectLine,
            EntryText.NoProjects, EntryText.LastKnownProjects, EntryText.ExampleProjects, EntryText.ShowAll, EntryText.AddWork,
            EntryText.MoreWorkLine, EntryText.AllOnStage, EntryText.IdeaPrompt, EntryText.WorkPrompt, EntryText.TypeIdea,
            EntryText.TypeIdeaInvite, EntryText.HelpMe, EntryText.HelpMeInvite, EntryText.NothingStartsYet, EntryText.GuideNote,
            EntryText.Back, EntryText.RecapTitle, EntryText.WorkRecapTitle, EntryText.StartBuilding, EntryText.MoreOptions,
            EntryText.ChooseHowItRuns, EntryText.FolderTitle, EntryText.FolderLine, EntryText.ReadingFolders, EntryText.NoFolders, EntryText.FoldersCut, EntryText.NewFolderPrompt, EntryText.NewFolderRule, EntryText.ChooseFolder, EntryText.UseThatFolder, EntryText.ChooseAnotherFolder, EntryText.RebindWarning, EntryText.OptionsTitle, EntryText.OptionsLine, EntryText.NoRuntimes,
            EntryText.Done, EntryText.ConfirmStart, EntryText.Change, EntryText.SendingTitle, EntryText.TryAgain, EntryText.Started,
            EntryText.PreviousRequestTitle, EntryText.ICheckedTheWork, EntryText.ClearAfterChecking, EntryText.OpenNow,
            EntryText.KeepCreating, EntryText.ResetPosition, EntryText.Close, EntryText.Move(0), EntryText.Move(1), EntryText.Move(-1),
            EntryText.Question(0, 4), EntryText.ReviewTitle(0, 2), EntryText.CreateTitle(null),
            WorkspaceText.WhatIsItDoing, WorkspaceText.HelpMeUnderstand, WorkspaceText.WhatWasChecked, WorkspaceText.WhatDoYouNeed,
        };
        words.AddRange(ProjectIdea.Questions.SelectMany(question =>
            question.Choices.Concat(new[] { question.Prompt, question.TypeLabel, question.SkipLabel ?? "" })));
        words.AddRange(Enum.GetValues<BuildStepKind>().SelectMany(kind => new[] { EntryText.StepName(kind, true), EntryText.StepName(kind, false) }));
        words.AddRange(Enum.GetValues<ModelServed>().Select(EntryText.Served));
        var brands = new[] { "Meta", "Quest", "Oculus", "Horizon", "Unity", "Claude", "Anthropic", "Codex", "OpenAI", "OpenCode", "Salidium", "Seorak" };
        foreach (var word in words)
        {
            foreach (var brand in brands)
            {
                Assert.That(Regex.IsMatch(word, @"\b" + brand + @"\b"), Is.False, word + ": data may name a runtime; the app's own words do not");
            }
            Assert.That(word, Does.Not.Contain("\u2014"), "no em dash");
            Assert.That(word.Length, Is.LessThanOrEqualTo(110), word);
        }
        Assert.That(EntryText.ConnectLine, Does.Contain("Halcyonic knows"), "Connect claims no discovery");
        Assert.That(EntryText.GuideNote, Does.Contain("not an AI"));
    }

    [Test]
    public void EachQuestionFitsTwoTabLinesWithoutShortening()
    {
        foreach (var question in Enum.GetValues<WorkspaceQuestion>())
        {
            var lines = WorkspaceText.TabLines(question);
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(string.Join(" ", lines), Is.EqualTo(WorkspaceText.Question(question)));
        }
    }
}

public class QuestionLedWorkspaceTests
{
    [Test]
    public void WhatDoYouNeedFromMeIsAskedOnlyWhileARealRequestWaits()
    {
        var work = new WaitingWork();
        var waiting = work.Present();
        Assert.That(WorkspaceText.Questions(waiting), Does.Contain(WorkspaceQuestion.NeedFromYou));
        Assert.That(WorkspaceText.FirstQuestion(waiting), Is.EqualTo(WorkspaceQuestion.NeedFromYou));
        var need = WorkspaceText.NeedFromYou(waiting)!;
        Assert.That(need.Asks, Is.EqualTo("It asks for approval to use bash:"));
        Assert.That(need.Request, Is.EqualTo("Run the migration"));
        Assert.That(need.Notes, Has.Some.Contains("runtime confirms"), "an answer counts once confirmed, not when sent");

        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", "Drop the table", "2026-09-26T09:05:00.000Z")));
        Assert.That(WorkspaceText.NeedFromYou(work.Present())!.Request, Is.EqualTo("Run the migration"), "the oldest, which an answer goes to");
        Assert.That(WorkspaceText.NeedFromYou(work.Present())!.Notes, Has.Some.EqualTo("2 requests wait; this is the oldest."));

        work.Change(execution => execution.PendingApprovals[0].Subject = new ToolUseSubject { ToolName = "sh\u202Eell", Summary = "Line one\nline two <b>x</b>" });
        var plain = WorkspaceText.NeedFromYou(work.Present())!;
        Assert.That(plain.Asks, Does.Contain("‹U+202E›"), "a tool's name shows by the one rule");
        Assert.That(plain.Request, Is.EqualTo("Line one line two <b>x</b>"));

        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var running = work.Present();
        Assert.That(WorkspaceText.Questions(running), Does.Not.Contain(WorkspaceQuestion.NeedFromYou));
        Assert.That(WorkspaceText.FirstQuestion(running), Is.EqualTo(WorkspaceQuestion.Doing));
        Assert.That(WorkspaceText.NeedFromYou(running), Is.Null);
    }

    [Test]
    public void TheOpenWorkLeadsWithItsGoalAndOnePlainAnswer()
    {
        var work = new WaitingWork();
        work.Workstream.Objective = "Migrate\nthe database";
        var waiting = work.Present();
        Assert.That(WorkspaceText.Goal(waiting), Is.EqualTo("Goal: Migrate the database"));
        Assert.That(WorkspaceText.Answer(waiting), Is.EqualTo(WorkspaceText.Attention(waiting)));

        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        Assert.That(WorkspaceText.Answer(work.Present()), Is.EqualTo(new[] { "Nothing is waiting for you.", "Latest: Working" }));
        Assert.That(WorkspaceText.Answer(work.Present(live: false))[1], Is.EqualTo("Last known. Latest: Working"));
    }
}
