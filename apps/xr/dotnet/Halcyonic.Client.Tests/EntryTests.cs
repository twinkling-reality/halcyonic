using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Projects and their work, as a snapshot carries them, for Projects and the stage.</summary>
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
        var project = sequence.Begin(Samples.Reviewed(sequence));
        Assert.That(project, Is.InstanceOf<ProjectCreateCommand>());
        Assert.That(sequence.Unresolved, Is.EqualTo(project.CommandId));
        Assert.That(sequence.Advance(With()), Is.Null);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo("Sent. Waiting for your computer…"));

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
        Assert.That(((WorkstreamCreateCommand)sequence.Begin(Samples.Reviewed(sequence))).Payload.ProjectId, Is.EqualTo("existing"));
    }

    [Test]
    public void AProjectedCompletionWinsOverALostAcknowledgement()
    {
        var sequence = new BuildSequence(Draft("existing"), Commands, null);
        var workstream = sequence.Begin(Samples.Reviewed(sequence));
        sequence.AcknowledgementLost(new CommandOutcomeUnknownException(workstream.CommandId, "The socket closed."));
        Assert.That(sequence.Advance(With()), Is.Null);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo(EntryText.NotSureItHappened));
        Assert.That(sequence.Unresolved, Is.EqualTo(workstream.CommandId), "an unknown outcome is kept");
        Assert.That(sequence.CanRetry, Is.False);
        Assert.That(sequence.Advance(With(Completed(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" }))), Is.InstanceOf<ExecutionStartCommand>());
    }

    [Test]
    public void ARefusalStopsAndCanBeSentAgainAsANewCommand()
    {
        var draft = Draft("existing");
        var sequence = new BuildSequence(draft, Commands, null);
        var workstream = sequence.Begin(Samples.Reviewed(sequence));
        var start = sequence.Advance(With(Completed(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" })))!;
        var refused = new CommandView
        {
            CommandId = start.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "The runtime needs a working directory." },
        };
        Assert.That(sequence.Advance(With(refused)), Is.Null);
        Assert.That(sequence.Stopped, Is.True);
        Assert.That(EntryText.StepStatus(sequence.Steps[1]), Is.EqualTo("Couldn't do that: The runtime needs a working directory."));
        Assert.That(sequence.CanRetry, Is.True, "a refused command cannot have run");
        var again = sequence.Retry(Samples.Reviewed(sequence));
        Assert.That(again, Is.InstanceOf<ExecutionStartCommand>());
        Assert.That(again.CommandId, Is.Not.EqualTo(start.CommandId));
        Assert.That(((ExecutionStartCommand)again).Payload.WorkstreamId, Is.EqualTo("w1"), "the work already created is reused");
    }

    [Test]
    public void AFailureThatMayHaveRunOrAnUnexpectedResultKeepsTheGuard()
    {
        var sequence = new BuildSequence(Draft("existing"), Commands, null);
        var workstream = sequence.Begin(Samples.Reviewed(sequence));
        var failed = new CommandView
        {
            CommandId = workstream.CommandId, Status = CommandStatus.Failed,
            Failure = new CommandFailure { Code = "timeout", Message = "No answer.", Effect = FailureEffect.Unknown },
        };
        sequence.Advance(With(failed));
        Assert.That(sequence.Unresolved, Is.EqualTo(workstream.CommandId));
        Assert.That(sequence.CanRetry, Is.False);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo(EntryText.NotSureItHappened), "an effect that may have happened is never said not to have");

        var other = new BuildSequence(Draft("existing"), Commands, null);
        var command = other.Begin(Samples.Reviewed(other));
        other.Advance(With(Completed(command, new ProjectCreatedResult { ProjectId = "p" })));
        Assert.That(other.Steps[0].Status, Is.EqualTo(BuildStepStatus.Unexpected));
        Assert.That(other.Unresolved, Is.EqualTo(command.CommandId));
        Assert.That(other.CanRetry, Is.False);
    }

    [Test]
    public void ACommandNeverSentIsReleased()
    {
        var sequence = new BuildSequence(Draft(), Commands, "Recipes");
        sequence.Begin(Samples.Reviewed(sequence));
        sequence.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        sequence.Advance(null);
        Assert.That(EntryText.StepStatus(sequence.Steps[0]), Is.EqualTo("Couldn't send: your computer isn't connected. Try again when it is."));
        Assert.That(sequence.Unresolved, Is.Null);
        Assert.That(sequence.Retry(Samples.Reviewed(sequence, "Recipes, renamed"), "Recipes, renamed"), Is.InstanceOf<ProjectCreateCommand>());
    }

    [Test]
    public void NothingIsSentButWhatAReviewReadToItsEndShowsAndEachReviewSendsOnce()
    {
        var sequence = new BuildSequence(Draft(), Commands, "Recipes");
        var unread = new NewWorkReview("Recipes", "Title", "Agent", "Model", "on your computer", "none", "Make a website for my team.");
        unread.Paginate(unread.Items.Select(_ => 1).ToList(), 2);
        unread.Drawn(0);
        Assert.Throws<InvalidOperationException>(() => sequence.Begin(unread), "a review not read to its end confirms nothing");
        Assert.That(sequence.Current, Is.Null, "nothing was sent");
        Assert.Throws<InvalidOperationException>(() => sequence.Begin(Samples.Reviewed(sequence, "Recipes, before it was renamed")));
        var otherTask = new BuildSequence(Draft(), Commands, "Recipes");
        otherTask.Draft.Objective = "Something the person never read.";
        Assert.Throws<InvalidOperationException>(() => otherTask.Begin(Samples.Reviewed(sequence)), "the first task is the one read");
        Assert.That(sequence.Current, Is.Null);

        var reviewed = Samples.Reviewed(sequence);
        sequence.Begin(reviewed);
        Assert.That((reviewed.Spent, reviewed.CanConfirm), Is.EqualTo((true, false)), "its Yes is taken");
        sequence.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        sequence.Advance(null);
        Assert.That(sequence.CanRetry, Is.True);
        Assert.Throws<InvalidOperationException>(() => sequence.Retry(reviewed), "Try again reads the request again");
        Assert.Throws<InvalidOperationException>(() => sequence.Retry(Samples.Reviewed(sequence), "Recipes, renamed in the recap"),
            "a name changed since the review is never sent unread");
        Assert.That(sequence.Retry(Samples.Reviewed(sequence, "Recipes, renamed in the recap"), "Recipes, renamed in the recap"),
            Is.InstanceOf<ProjectCreateCommand>());
    }

    [Test]
    public void EveryStepGoesOutAsConfirmedWhateverTheDraftBecomesAfterYes()
    {
        var draft = Draft();
        draft.Objective = "Plan dinners for the week.";
        var sequence = new BuildSequence(draft, Commands, "Dinners");
        var project = sequence.Begin(Samples.Reviewed(sequence));
        // While project.create is on its way, the steps take the person back to the recap, and they change things.
        draft.Objective = "Something never confirmed.";
        draft.ChooseRuntime(new RuntimeDescriptor
        {
            RuntimeId = "other", DisplayName = "Other runtime", Kind = "mock", Synthetic = true, ModelChoice = ModelChoice.None,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        });
        var workstream = (WorkstreamCreateCommand)sequence.Advance(With(Completed(project, new ProjectCreatedResult { ProjectId = "p1" })))!;
        Assert.That((workstream.Payload.Objective, workstream.Payload.Title), Is.EqualTo(("Plan dinners for the week.", "Plan dinners for the week.")));
        var start = (ExecutionStartCommand)sequence.Advance(With(Completed(workstream, new WorkstreamCreatedResult { WorkstreamId = "w1" })))!;
        Assert.That((start.Payload.Instruction, start.Payload.RuntimeId), Is.EqualTo(("Plan dinners for the week.", "mock")), "the runtime and task confirmed, not chosen after");
    }

    [Test]
    public void APlaceSharingAnotherPlacesNameIsNotTheFolderReviewed()
    {
        var home = new NewFolderChoice { Root = "/Users/person/Projects", FolderName = "dinners" };
        var work = new NewFolderChoice { Root = "/Volumes/Work/Projects", FolderName = "dinners" };
        static NewWorkReview In(NewFolderChoice choice) =>
            new("Dinners", "Title", "Agent", "Model", "on your computer", "none", "Plan dinners.", "a new folder, dinners, in Projects", folderChoice: choice);
        Assert.That(In(home).Items.Select(item => item.Text), Is.EqualTo(In(work).Items.Select(item => item.Text)), "the words read the same");
        Assert.That(In(home).SameRequest(In(work)), Is.False, "the folders themselves differ");
        Assert.That(In(home).SameRequest(In(home)), Is.True);

        var draft = Draft();
        var sequence = new BuildSequence(draft, Commands, "Dinners", work);
        Assert.Throws<InvalidOperationException>(() => sequence.Begin(Samples.Reviewed(sequence, folder: home)), "never sent to a place not reviewed");
        Assert.That(sequence.Current, Is.Null);
        Assert.That(sequence.Begin(Samples.Reviewed(sequence)), Is.InstanceOf<ProjectCreateCommand>());
    }

    private static NewWorkReview Read(NewWorkReview review)
    {
        review.Paginate(review.Items.Select(_ => 1).ToList(), review.Items.Count);
        Samples.ReadThrough(review);
        return review;
    }

    [Test]
    public void AProjectMadeInItsChosenFolderIsNotShownAsMovingWhenItsWorkIsTriedAgain()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
            Folders = new List<LocationFolder>(),
        };
        var idea = new ProjectIdea();
        idea.UseIdea("Make a website for my team.");
        idea.Rename("Recipes");
        idea.ChooseFolder(ProjectFolder.New(root, "recipes"));
        var draft = Draft();
        draft.Objective = idea.FirstTask;
        var sequence = new BuildSequence(draft, Commands, idea.Name, idea.Folder!.ToContract());
        var project = sequence.Begin(Read(EntryScreens.ReviewOf(idea, draft, null, live: true)));
        var workstream = sequence.Advance(With(Completed(project, new ProjectCreatedResult { ProjectId = "p1" })))!;
        sequence.Advance(With(new CommandView
        {
            CommandId = workstream.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "Not now." },
        }));
        Assert.That(sequence.CanRetry, Is.True);

        idea.ProjectMade("p1", "Recipes");
        var there = new ProjectLocation { Path = "/Users/person/Projects/recipes", Name = "recipes", Created = true };
        Assert.That(EntryScreens.Moves(idea, there), Is.False, "the project is already where the choice points");
        var again = EntryScreens.ReviewOf(idea, draft, there, live: true);
        Assert.That(again.Items.Select(item => item.Label), Has.No.Member("Folder now: ").And.No.Member("Folder from now on: "));
        Assert.That(again.Items.Single(item => item.Label == "Where its files live: ").Value, Is.EqualTo("recipes"));
        Assert.That(again.FolderChoice, Is.Null, "nothing to send about the folder");
        Assert.That(NewProjectScreens.Recap(idea, draft, there, live: true, notice: null, problem: null).Lines[0].Words, Is.EqualTo(EntryText.RecapLine),
            "no warning about a move");
        Assert.That(sequence.Retry(Read(again)), Is.InstanceOf<WorkstreamCreateCommand>(), "Try again sends, never throws");

        var moved = new ProjectIdea("p1", "Recipes");
        moved.UseIdea("Make a website for my team.");
        moved.ChooseFolder(ProjectFolder.New(root, "recipes-v2"));
        Assert.That(EntryScreens.Moves(moved, there), Is.True);
        var move = EntryScreens.ReviewOf(moved, draft, there, live: true);
        Assert.That(move.Items.Select(item => item.Label), Has.Member("Folder now: ").And.Member("Folder from now on: "));
        Assert.That(move.SendsFolder(moved.Folder!.ToContract()), Is.True);
    }

    [Test]
    public void TheFolderASendCarriesIsDecidedOnceAndEveryYesSends()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
            Folders = new List<LocationFolder> { new() { Name = "recipes", Path = "/Users/person/Projects/recipes" } },
        };
        var there = new ProjectLocation { Path = "/Users/person/Projects/recipes", Name = "recipes", Created = false };

        // Add a task, choosing the folder the project is already in: nothing about the folder is sent.
        var task = new ProjectIdea("p1", "Recipes");
        task.UseIdea("Make a website for my team.");
        task.ChooseFolder(ProjectFolder.Existing(root, root.Folders[0]));
        Assert.That(EntryScreens.FolderSent(task, there), Is.Null);
        var draft = Draft("p1");
        draft.Objective = task.FirstTask;
        var stays = new BuildSequence(draft, Commands, null, EntryScreens.FolderSent(task, there)?.ToContract());
        Assert.That(stays.Begin(Read(EntryScreens.ReviewOf(task, draft, there, live: true))), Is.InstanceOf<WorkstreamCreateCommand>(),
            "Yes sends, and binds nothing");

        // A real move: the review shows it, and the same decision sends it.
        task.ChooseFolder(ProjectFolder.New(root, "recipes-v2"));
        Assert.That(EntryScreens.FolderSent(task, there), Is.SameAs(task.Folder));
        var moves = new BuildSequence(draft, Commands, null, EntryScreens.FolderSent(task, there)?.ToContract());
        Assert.That(moves.Begin(Read(EntryScreens.ReviewOf(task, draft, there, live: true))), Is.InstanceOf<ProjectSetLocationCommand>());

        // A project made in its folder, a later step refused, and its own folder chosen again: Try again sends.
        var idea = new ProjectIdea();
        idea.UseIdea("Make a website for my team.");
        idea.Rename("Recipes");
        idea.ChooseFolder(ProjectFolder.New(root, "made"));
        var fresh = Draft();
        fresh.Objective = idea.FirstTask;
        var sequence = new BuildSequence(fresh, Commands, idea.Name, EntryScreens.FolderSent(idea, null)?.ToContract());
        var project = sequence.Begin(Read(EntryScreens.ReviewOf(idea, fresh, null, live: true)));
        var workstream = sequence.Advance(With(Completed(project, new ProjectCreatedResult { ProjectId = "p2" })))!;
        sequence.Advance(With(new CommandView
        {
            CommandId = workstream.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "Not now." },
        }));
        idea.ProjectMade("p2", "Recipes");
        var made = new ProjectLocation { Path = "/Users/person/Projects/made", Name = "made", Created = true };
        idea.ChooseFolder(ProjectFolder.Existing(root, new LocationFolder { Name = "made", Path = "/Users/person/Projects/made" }));
        Assert.That(EntryScreens.FolderSent(idea, made), Is.Null, "chosen again, its own folder is no move");
        Assert.That(sequence.Retry(Read(EntryScreens.ReviewOf(idea, fresh, made, live: true)), null, EntryScreens.FolderSent(idea, made)?.ToContract()),
            Is.InstanceOf<WorkstreamCreateCommand>());
    }

    [Test]
    public void ARefusedMoveThePersonNoLongerWantsIsDroppedAndTheTaskGoesOn()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
            Folders = new List<LocationFolder> { new() { Name = "recipes", Path = "/Users/person/Projects/recipes" } },
        };
        var there = new ProjectLocation { Path = "/Users/person/Projects/recipes", Name = "recipes", Created = false };
        var task = new ProjectIdea("p1", "Recipes");
        task.UseIdea("Make a website for my team.");
        task.ChooseFolder(ProjectFolder.New(root, "recipes-v2"));
        var draft = Draft("p1");
        draft.Objective = task.FirstTask;
        var sequence = new BuildSequence(draft, Commands, null, EntryScreens.FolderSent(task, there)?.ToContract());
        var move = sequence.Begin(Read(EntryScreens.ReviewOf(task, draft, there, live: true)));
        Assert.That(move, Is.InstanceOf<ProjectSetLocationCommand>());
        sequence.Advance(With(new CommandView
        {
            CommandId = move.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.LocationMissing, Message = "That folder is gone." },
        }));
        Assert.That(sequence.CanRetry, Is.True);

        // The person chooses the folder the project is already in: the review shows no move.
        task.ChooseFolder(ProjectFolder.Existing(root, root.Folders[0]));
        Assert.That(EntryScreens.FolderSent(task, there), Is.Null);
        var again = sequence.Retry(Read(EntryScreens.ReviewOf(task, draft, there, live: true)), null, EntryScreens.FolderSent(task, there)?.ToContract());
        Assert.That(again, Is.InstanceOf<WorkstreamCreateCommand>(), "the move is dropped and the task is sent, never a refusal on every press");
        Assert.That(sequence.Steps.Select(step => step.Kind), Is.EqualTo(new[] { BuildStepKind.CreateWorkstream, BuildStepKind.StartWork }));

        // Kept, the move is sent again.
        task.ChooseFolder(ProjectFolder.New(root, "recipes-v2"));
        var moving = new BuildSequence(draft, Commands, null, EntryScreens.FolderSent(task, there)?.ToContract());
        var first = moving.Begin(Read(EntryScreens.ReviewOf(task, draft, there, live: true)));
        moving.Advance(With(new CommandView
        {
            CommandId = first.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.LocationMissing, Message = "That folder is gone." },
        }));
        Assert.That(moving.Retry(Read(EntryScreens.ReviewOf(task, draft, there, live: true)), null, EntryScreens.FolderSent(task, there)?.ToContract()),
            Is.InstanceOf<ProjectSetLocationCommand>());
    }

    [Test]
    public void AReviewMadeAfterAChangeIsNotTheSameRequest()
    {
        static NewWorkReview Of(string project, string task) => new(project, "Title", "Agent", "Model", "on your computer", "none", task);
        Assert.That(Of("Recipes", "Plan dinners.").SameRequest(Of("Recipes", "Plan dinners.")), Is.True);
        Assert.That(Of("Recipes", "Plan dinners.").SameRequest(Of("Recipes, renamed", "Plan dinners.")), Is.False);
        Assert.That(Of("Recipes", "Plan dinners.").SameRequest(Of("Recipes", "Plan lunches.")), Is.False);
        Assert.That(Of("Recipes", "Plan dinners.").SameRequest(null), Is.False);
        var moved = new NewWorkReview("Recipes", "Title", "Agent", "Model", "on your computer", "none", "Plan dinners.", "recipes in Projects");
        Assert.That(Of("Recipes", "Plan dinners.").SameRequest(moved), Is.False, "a folder chosen since is a change");
    }
}

public class EntryWordsTests
{
    [Test]
    public void TasksWaitingForYouAreSaidAsAPersonWouldSayThem()
    {
        Assert.That(EntryText.WaitingForYou(1), Is.EqualTo("1 task is waiting for you"));
        Assert.That(EntryText.WaitingForYou(2), Is.EqualTo("2 tasks are waiting for you"));
        Assert.That(AmbientText.NeedsYouLine(2), Is.EqualTo(EntryText.WaitingForYou(2)), "the ambient line and the entry agree");
    }

    [Test]
    public void TheRecapSaysWhereTheModelRunsAndChoosesNothing()
    {
        var draft = new NewWorkDraft(new CommandFactory(Samples.Client));
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Not chosen yet"));
        Assert.That(EntryText.ModelLine(draft), Does.Contain("Nothing is chosen for you"));

        draft.ChooseRuntime(Samples.MockRuntime());
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Mock runtime (simulated)"), "the recorded demonstration keeps its names");
        Assert.That(EntryText.RunsWith(draft, live: true), Is.EqualTo(EntryText.PracticeRun));
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("Practice: no agent, no files."));

        var listed = Samples.MockRuntime();
        listed.RuntimeId = "local";
        listed.DisplayName = "Local‮ agent";
        listed.Synthetic = false;
        listed.ModelChoice = ModelChoice.Listed;
        draft.ChooseRuntime(listed);
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Not finished choosing"));
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo(EntryText.FinishChoosing));
        var remote = new RuntimeModel { ModelRef = "hosted/x", DisplayName = "Hosted", Served = ModelServed.Remote, ToolCalling = ModelToolCalling.Declared };
        draft.SetModels(new RuntimeModelsResponse { RuntimeId = "local", Result = new AvailableModels { Models = new List<RuntimeModel> { remote } } });
        Assert.That(draft.Model, Is.Null, "a remote model is never chosen for the person");
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Not finished choosing"));
        Assert.That(draft.ChooseModel(remote), Is.False, "the first press only says where it runs");
        Assert.That(EntryText.ConfirmElsewhere(remote), Is.EqualTo("Runs on a remote service: your code and instructions go there. Press again to use it."));
        Assert.That(draft.ChooseModel(remote), Is.True);
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("On a remote service"));
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("It runs on a remote service: your code and instructions go there."));

        var local = new RuntimeModel { ModelRef = "ollama/qwen3.6", DisplayName = "qwen3.6 (Ollama)", Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared };
        draft.ChooseRuntime(listed);
        draft.SetModels(new RuntimeModelsResponse { RuntimeId = "local", Result = new AvailableModels { Models = new List<RuntimeModel> { remote, local } } });
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("On your computer"));
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("Chosen for you. Change it in More options."), "chosen for the person, and said so");

        // An agent app that picks its own model is named, by the one rule, since where its model runs isn't known here.
        listed.ModelChoice = ModelChoice.None;
        draft.ChooseRuntime(listed);
        Assert.That(EntryText.RunsWith(draft), Is.EqualTo("Local‹U+202E› agent"));
        Assert.That(EntryText.ModelLine(draft), Is.EqualTo("Where it runs isn't known: your code and instructions may go elsewhere."));
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
            EntryText.CreateProject, EntryText.CreateInvite, EntryText.NotNow,
            EntryText.ShowAll, EntryText.AddTask, EntryText.WaitingForMac, EntryText.IdeaPrompt, EntryText.WorkPrompt, EntryText.TypeIdea,
            EntryText.NothingStartsYet, EntryText.GuideNote,
            EntryText.Back, EntryText.Chosen, EntryText.ChosenForYou,
            EntryText.RecapLine, EntryText.StartBuilding, EntryText.StartOver, EntryText.ConfirmStartOver, EntryText.StartOverClears(false), EntryText.StartOverClears(true),
            EntryText.MoreOptions, EntryText.ProjectName, EntryText.FirstTask, EntryText.NotNamedYet, EntryText.HowItRuns, EntryText.NameTheProject,
            EntryText.WhatFirstTask, EntryText.FolderTitle, EntryText.FolderLine, EntryText.ReadingFolders, EntryText.NoFolders, EntryText.FoldersCut,
            EntryText.NewFolderPrompt, EntryText.NewFolderRule, EntryText.UseThatFolder, EntryText.ChooseAnotherFolder,
            EntryText.RebindWarning, EntryText.FoldersUnread("no reason given."), EntryText.OptionsLine, EntryText.NoRuntimes,
            EntryText.ChangeAgentApp, EntryText.ListsModels, EntryText.ChoosesModel, EntryText.NoModels, EntryText.Practice, EntryText.PracticeRun,
            EntryText.PracticeDetail, EntryText.Done, EntryText.ReviewLine, EntryText.ConfirmStart, EntryText.Change,
            EntryText.ReadToPart(4), EntryText.Previous, EntryText.Next, EntryText.Page(0, 2), EntryText.Part(0, 2), EntryText.NextPage(0, 2), EntryText.NextPage(1, 2),
            EntryText.SendingLine, EntryText.TryAgain, EntryText.Started, EntryText.NotSureItHappened,
            EntryText.PreviousRequestTitle, EntryText.PreviousRequestLine, EntryText.CheckFirst, EntryText.Clear, EntryText.ClearOnceChecked,
            EntryText.ClearOnlyAfterChecking, EntryText.ConfirmClear, EntryText.Cancel, EntryText.Cleared, EntryText.Reference("id"),
            EntryText.OpenNow, EntryText.KeepCreating, EntryText.Move, EntryText.ResetPosition, EntryText.Close,
            EntryText.DemoCannotStart, EntryText.ChooseHowItRuns, EntryText.ChooseAgain, EntryText.FinishChoosing, EntryText.ProjectGone,
            EntryText.ChooseWhereFilesLive, EntryText.ReviewAfresh,
            EntryText.Question(0, 4), EntryText.CreateTitle(null),
            WorkspaceText.WhatIsItDoing, WorkspaceText.HelpMeUnderstand, WorkspaceText.WhatWasChecked, WorkspaceText.WhatDoYouNeed,
        };
        words.AddRange(ProjectIdea.Questions.SelectMany(question =>
            question.Choices.Concat(new[] { question.Prompt, question.TypeLabel, question.SkipLabel ?? "" })));
        words.AddRange(Enum.GetValues<BuildStepKind>().SelectMany(kind => new[] { EntryText.StepName(kind, true), EntryText.StepName(kind, false) }));
        words.AddRange(Enum.GetValues<ModelServed>().SelectMany(served => new[] { EntryText.Served(served), EntryText.ServedShort(served), EntryText.ServedInSentence(served) }));
        words.AddRange(Enum.GetValues<ModelToolCalling>().Select(EntryText.Tools));
        words.AddRange(new CommandStatus?[] { null, CommandStatus.Accepted, CommandStatus.Completed, CommandStatus.Rejected, CommandStatus.Failed }.Select(EntryText.Recorded));
        var brands = new[] { "Meta", "Quest", "Oculus", "Horizon", "Unity", "Claude", "Anthropic", "Codex", "OpenAI", "OpenCode", "Salidium", "Seorak", "Mac" };
        foreach (var word in words)
        {
            foreach (var brand in brands)
            {
                Assert.That(Regex.IsMatch(word, @"\b" + brand + @"\b"), Is.False, word + ": data may name a runtime; the app's own words do not");
            }
            Assert.That(word, Does.Not.Contain("\u2014"), "no em dash");
            Assert.That(word.Length, Is.LessThanOrEqualTo(110), word);
        }
        Assert.That(words.Where(word => Regex.IsMatch(word, @"\b(runtime|workstream|control plane)\b", RegexOptions.IgnoreCase)), Is.Empty,
            "the glossary's words: an agent app, a task, your computer");
        Assert.That(EntryText.GuideNote, Does.Contain("not an AI"));
        Assert.That(words.Where(word => Regex.IsMatch(word, @"\b(Connect projects|More tasks|See other tasks)\b")), Is.Empty,
            "no word sends the person to a place the menu retired (ADR 0026)");
    }

    [Test]
    public void EachQuestionHasAShortTabAndKeepsItsWholeQuestion()
    {
        var labels = Enum.GetValues<WorkspaceQuestion>().Select(WorkspaceText.TabLabel).ToList();
        Assert.That(labels, Is.EqualTo(new[] { "Doing", "Understand", "Checked", "Waiting for you" }));
        Assert.That(labels.Distinct().Count(), Is.EqualTo(labels.Count));
        Assert.That(WorkspaceText.TabLabel(WorkspaceQuestion.NeedFromYou), Is.EqualTo(StateLanguage.WordOf(WorkState.WaitingForYou)), "the state's own word");
        foreach (var question in Enum.GetValues<WorkspaceQuestion>())
        {
            Assert.That(WorkspaceText.Question(question), Does.Not.Contain("…"), "the whole question heads the tab's answer");
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
        Assert.That(need.Asks, Is.EqualTo("It wants to run a command:"));
        Assert.That(need.Request, Is.EqualTo("Run the migration"));
        Assert.That(need.Notes, Has.Some.Contains("once the agent confirms"), "an answer counts once confirmed, not when sent");

        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", "Drop the table", "2026-09-26T09:05:00.000Z")));
        Assert.That(WorkspaceText.NeedFromYou(work.Present())!.Request, Is.EqualTo("Run the migration"), "the oldest, which an answer goes to");
        Assert.That(WorkspaceText.NeedFromYou(work.Present())!.Notes, Has.Some.EqualTo("2 requests are waiting. This is the oldest."));

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
        Assert.That(WorkspaceText.Answer(waiting), Is.EqualTo(new[] { "It wants your approval. See it under Waiting for you." }),
            "the request itself shows whole under its tab");

        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        Assert.That(WorkspaceText.Answer(work.Present()), Is.EqualTo(new[] { "Nothing is waiting for you.", "Latest: Working" }));
        Assert.That(WorkspaceText.Answer(work.Present(live: false))[1], Is.EqualTo("Last known. Latest: Working"));
    }
}
