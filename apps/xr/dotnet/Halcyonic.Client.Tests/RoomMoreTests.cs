using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// More under the redesign (SCREENS's SHAPE 11 R1 to R3, the lead's 03:06Z yes): never on the bar while the agent works, where the
/// column is the work's own (AUDIT's 07:30Z item 3: a test that fails if the guard goes).
/// </summary>
public class RoomMoreTests
{
    private static KitFileBinding Binding(ExecutionStatus status)
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = status; },
            status == ExecutionStatus.Completed ? WorkstreamStatus.Completed : WorkstreamStatus.Running);
        var now = work.Present();
        var offered = new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt };
        var workspace = new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered,
            offered.Where(now.RequiresConfirmation).ToList(), now.Commands, new ActivityEntry[0]);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var frame = FileScreens.Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true, CharacterColourOffered = true },
            new AnswerRoom(12));
        return KitFileBinding.Of(frame, steering.Armed, workspace, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!;
    }

    [Test]
    public void MoreNeverStandsWhileTheAgentWorks()
    {
        var binding = Binding(ExecutionStatus.Running);
        Assert.That(RoomBar.MoreRoutes(binding), Is.Not.Empty, "the file offers a route, so only the work keeps More away");
        var working = RoomBar.Of(binding, VoiceStage.Idle, false);
        Assert.That(working.Working, Is.True);
        var actions = RoomBar.WithMore(binding, working).Row.Concat(working.Column).Select(action => action.Control.Action);
        Assert.That(actions, Has.None.EqualTo(RoomBar.More), "at work the column is the work's own");
    }

    [Test]
    public void MoreStandsBesideAFinishedTasksResultBeforeTheWayBack()
    {
        var binding = Binding(ExecutionStatus.Completed);
        var result = RoomBar.Of(binding, VoiceStage.Idle, false);
        Assert.That(result.Result, Is.True, "the finished task stands as its result");
        var row = RoomBar.WithMore(binding, result).Row.Select(action => action.Control).ToList();
        var more = row.FindIndex(control => control.Action == RoomBar.More);
        Assert.That(more, Is.GreaterThanOrEqualTo(0), "a finished task's routes are drawn behind More");
        var back = row.FindIndex(control => control.Place is KitPlace.Close or KitPlace.Cancel);
        if (back >= 0) Assert.That(more, Is.LessThan(back), "before the way back");
    }
}
