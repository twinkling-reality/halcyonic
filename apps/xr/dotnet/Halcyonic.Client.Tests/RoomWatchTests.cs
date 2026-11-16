using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Watch It Work's button on the redesign's working bar (SCREENS's SHAPE 11 W1, AUDIT's 03:05Z yes): offered only on live work whose
/// folder the host observes, the one accent with Stop plain after it, and the same control closing the view.
/// </summary>
public class RoomWatchTests
{
    private static KitFileBinding Binding(bool observed, ConnectionPhase phase = ConnectionPhase.Live)
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
            execution.Writing = observed
                ? new ExecutionWritingView { Paths = new List<string> { "src/game.js" }, PathsTotal = 1, TotalComplete = true, At = "2026-11-16T07:40:00Z" }
                : null;
        }, WorkstreamStatus.Running);
        var now = work.Present();
        var offered = new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt };
        var workspace = new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered,
            offered.Where(now.RequiresConfirmation).ToList(), now.Commands, new ActivityEntry[0]);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var frame = FileScreens.Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true }, new AnswerRoom(12));
        return KitFileBinding.Of(frame, steering.Armed, workspace, "", new ConnectionStatus(phase), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!;
    }

    [Test]
    public void AtWorkOnObservedLiveWorkWatchItWorkLeadsAndStopStaysPlainAfterIt()
    {
        var binding = Binding(observed: true);
        Assert.That(binding.Model.Watch, Is.True);
        var bar = RoomBar.WithWatch(binding, RoomBar.Of(binding, VoiceStage.Idle, false), open: false);
        Assert.That(bar.Column.Select(action => (action.Control.Action, action.Words, action.Main)), Is.EqualTo(new[]
        {
            (RoomBar.Watch, RoomBar.WatchWords, true), (bar.Column[1].Control.Action, bar.Column[1].Words, false),
        }));
        Assert.That(bar.Column[1].Control.Place, Is.EqualTo(KitPlace.Stop), "Stop, plain, after it");
        Assert.That(bar.Column.Count(action => action.Main), Is.EqualTo(1), "one accent");
    }

    [Test]
    public void OpenTheSameControlInTheSamePlaceClosesTheView()
    {
        var binding = Binding(observed: true);
        var bar = RoomBar.WithWatch(binding, RoomBar.Of(binding, VoiceStage.Idle, false), open: true);
        Assert.That(bar.Column[0].Control.Action, Is.EqualTo(RoomBar.Watch));
        Assert.That(bar.Column[0].Words, Is.EqualTo(RoomBar.WatchDoneWords));
        Assert.That(bar.Column[0].Words, Does.Not.Contain("Stop"), "it stands beside the task's own Stop");
    }

    [Test]
    public void NeverOfferedWhereTheHostObservesNothingOrTheWorkIsNotLive()
    {
        foreach (var binding in new[] { Binding(observed: false), Binding(observed: true, ConnectionPhase.Synchronizing) })
        {
            Assert.That(binding.Model.Watch, Is.False);
            var bar = RoomBar.WithWatch(binding, RoomBar.Of(binding, VoiceStage.Idle, false), open: false);
            Assert.That(bar.Column.Select(action => action.Control.Action), Has.None.EqualTo(RoomBar.Watch));
        }
    }
}
