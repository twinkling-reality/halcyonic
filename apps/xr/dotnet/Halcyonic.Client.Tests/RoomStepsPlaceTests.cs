using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The task's steps list in the redesign's bar (SCREENS's TaskSteps, XR's places, SCREENS's 08:52Z yes): at work in the panel's place
/// instead of the working field (a), and at rest where the result says nothing instead of how the run stands (b).
/// </summary>
public class RoomStepsPlaceTests
{
    private static readonly ProjectView Project = new() { ProjectId = "p1", Name = "Storefront", Starter = "web-game" };

    private static (RoomBar Bar, KitFileBinding Binding) Bar(ExecutionStatus status, bool withSteps = true)
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = status; },
            status == ExecutionStatus.Completed ? WorkstreamStatus.Completed : WorkstreamStatus.Running);
        var now = work.Present();
        var offered = new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt };
        var workspace = new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered,
            offered.Where(now.RequiresConfirmation).ToList(), now.Commands, new ActivityEntry[0]);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var frame = FileScreens.Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true }, new AnswerRoom(12));
        var steps = withSteps ? RoomSteps.Of(now.Execution, Project, null, null) : null;
        var binding = KitFileBinding.Of(frame, steering.Armed, workspace, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true, steps: steps)!;
        return (RoomBar.Of(binding, VoiceStage.Idle, false), binding);
    }

    [Test]
    public void TheStepsAreReadFromTheRunAndItsProjectAndNoneWithoutEither()
    {
        var running = new WaitingWork().Present().Execution;
        Assert.That(RoomSteps.Of(null, Project, null, null), Is.Empty);
        Assert.That(RoomSteps.Of(running, null, null, null), Is.Empty);
        Assert.That(RoomSteps.Of(running, Project, null, null).First().Word, Is.EqualTo(TaskSteps.YourRequest));
    }

    [Test]
    public void AtWorkTheStepsListHoldsThePanelsPlace()
    {
        var (bar, binding) = Bar(ExecutionStatus.Running);
        Assert.That(bar.Working, Is.True);
        Assert.That(bar.Steps, Is.EqualTo(binding.Model.Steps).And.Not.Empty, "(a): the list stands at work");
        Assert.That(bar.Steps.Any(step => step.Kind == TaskStepKind.Now && step.Word == TaskSteps.Working), Is.True, "Working is now");
        Assert.That(Bar(ExecutionStatus.Running, withSteps: false).Bar.Steps, Is.Empty, "no list where none is read");
    }

    [Test]
    public void AtRestWhereTheResultSaysNothingTheListStandsInsteadOfHowTheRunStands()
    {
        var (bar, binding) = Bar(ExecutionStatus.Completed);
        Assert.That(binding.Model.Standing, Is.Not.Null, "the run at rest has a standing card to replace");
        Assert.That(bar.Result, Is.True);
        Assert.That(bar.Card, Is.Null, "(b): Turn Finished goes once the list stands");
        Assert.That(bar.Steps, Is.Not.Empty);
        Assert.That(bar.Wide, Is.False, "the panel stands for the list");
        var (plain, _) = Bar(ExecutionStatus.Completed, withSteps: false);
        Assert.That(plain.Card, Is.Not.Null, "with no list the standing card stays");
        Assert.That(plain.Steps, Is.Empty);
    }
}
