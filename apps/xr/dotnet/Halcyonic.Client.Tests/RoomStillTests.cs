using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The photo or video given to a task heads its result's card, where the host made a still of it (ADR 0066, protocol 3).</summary>
public class RoomStillTests
{
    private static RoomBar Result(List<ExecutionMediaView>? media)
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Completed;
            execution.Media = media;
        }, WorkstreamStatus.Completed);
        var now = work.Present();
        var offered = new[] { WorkspaceAction.Instruct };
        var workspace = new WorkspacePresentation(now.Character, now.Objective, now.Execution, now.Runtime, offered,
            offered.Where(now.RequiresConfirmation).ToList(), now.Commands, new ActivityEntry[0]);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var frame = FileScreens.Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true }, new AnswerRoom(12));
        var binding = KitFileBinding.Of(frame, steering.Armed, workspace, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true)!;
        return RoomBar.Of(binding, VoiceStage.Idle, false);
    }

    [Test]
    public void TheFirstMediumWithAStillHeadsTheResultsCardInItsProject()
    {
        var bar = Result(new List<ExecutionMediaView>
        {
            new() { Path = ".halora/media/long.mov", MediaType = MediaType.VideoQuicktime, Still = null },
            new() { Path = ".halora/media/kite.mov", MediaType = MediaType.VideoQuicktime, Still = ".halora/media/kite.still.png" },
        });
        Assert.That(bar.Result, Is.True);
        Assert.That(bar.Card?.Head, Is.EqualTo(RoomCardHead.Picture));
        Assert.That(bar.Card?.Still, Is.EqualTo(new TaskStill(new WaitingWork().Present().Execution!.ProjectId, ".halora/media/kite.still.png", TaskMediaKind.Video)),
            "the first with a still, never one the host made none of");
    }

    [Test]
    public void ACardWithNoStillDrawsItsOwnHeadAndAStillNeverMovesToAnotherHead()
    {
        Assert.That(Result(null).Card?.Still, Is.Null, "a run before protocol 3, or one given nothing");
        Assert.That(Result(new List<ExecutionMediaView> { new() { Path = "a.mov", MediaType = MediaType.VideoMp4, Still = null } }).Card?.Still, Is.Null,
            "no still made");
        var still = new TaskStill("p", "a.png", TaskMediaKind.Photo);
        Assert.That(new RoomCard(LineWords.Ours("Task"), head: RoomCardHead.Project).WithStill(still).Still, Is.Null, "only a picture head draws a still");
        var card = new RoomCard(LineWords.Ours("Task"), head: RoomCardHead.Picture).WithStill(still);
        Assert.That(card.WithStatus(new RoomCardStatus("Done", null, RoomTone.Good)).Still, Is.EqualTo(still), "every copy keeps it");
    }
}
