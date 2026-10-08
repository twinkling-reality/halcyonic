using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A judge on the plane through the recording's timed holds: the 60 seconds its instructions are
/// offered after the approved turn, and the 20 seconds its end holds before it starts again. While
/// anything is open (the menu, the directed task's file, New project) neither runs, so a judge
/// reading Changes or Checks, or talking an idea through, is never cut off; once everything has
/// folded to the bar, each runs whole from then. Everything opens and closes through the menu's own
/// navigator, and the player is told as the workspace's director tells it, each frame.
/// </summary>
public class JudgeHoldsTests
{
    /// <summary>Fifty times the recorded pace: the instruction hold lasts 1.2 s and the end hold 0.4 s.</summary>
    private const double Speed = 50;

    private static readonly TimeSpan InstructionHold = TimeSpan.FromSeconds(60 / Speed);
    private static readonly TimeSpan EndHold = TimeSpan.FromSeconds(20 / Speed);

    private DemonstrationPlayer? player;

    [TearDown]
    public async Task StopSession()
    {
        if (player != null) await player.Session.StopAsync();
    }

    [Test]
    public async Task AFileKeptOpenPastTheInstructionHoldStaysAsItIsAndTheHoldRunsWholeFromTheFold()
    {
        var host = await OpenTheDirectedFileAtTheApprovalAsync();
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer;
        await ApproveAsync(host);

        await Frames(host, InstructionHold * 3);
        Assert.That(player!.Plays, Is.EqualTo(1), "the demonstration has not started again");
        Assert.That(player.InstructionsFor(approve.ExecutionId).Select(preset => preset.Label),
            Is.EqualTo(new[] { "Count per account too", "Change the test instead" }), "its instructions are still offered");
        Assert.That(host.Navigator.BesideTask, Is.EqualTo(Demonstration.DirectedWorkstream(player.Session).WorkstreamId), "the file is still open");

        var closed = await CloseAndWaitForTheStartAgainAsync(host);
        Assert.That(closed, Is.GreaterThanOrEqualTo(InstructionHold), "the hold ran whole from the fold");
    }

    [Test]
    public async Task AFileKeptOpenPastTheEndHoldStaysAsItIsAndTheHoldRunsWholeFromTheFold()
    {
        var host = await OpenTheDirectedFileAtTheApprovalAsync();
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer;
        await ApproveAsync(host);
        var instruction = Demonstration.InstructionsAfterApproving()[0].Answer;
        await player!.Session.SubmitAsync(new CommandFactory(Samples.Client).SendInstruction(approve.ExecutionId, instruction.Text!));
        await Until(host, () => player.Ended, "the second round ends and its end holds");

        await Frames(host, EndHold * 4);
        Assert.That((player.Plays, player.Ended), Is.EqualTo((1, true)), "the end still holds");
        Assert.That(Demonstration.DirectedExecution(player.Session)!.LastTestRun!.Outcome, Is.EqualTo(TestOutcome.Passed), "the passing checks still show");
        Assert.That(host.Navigator.BesideTask, Is.EqualTo(Demonstration.DirectedWorkstream(player.Session).WorkstreamId), "the file is still open");

        var closed = await CloseAndWaitForTheStartAgainAsync(host);
        Assert.That(closed, Is.GreaterThanOrEqualTo(EndHold), "the hold ran whole from the fold");
    }

    [Test]
    public async Task NewProjectKeptOpenPastBothHoldsStaysAsItIsAndTheMenuAloneStillHolds()
    {
        player = new DemonstrationPlayer(Demonstration.Recording(), Samples.Client, new DemonstrationOptions { Speed = Speed });
        player.Session.Start();
        var host = new DemonstrationMenuHost(player);
        await Demonstration.ToTheApprovalAsync(player.Session);
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer;
        await ApproveAsync(host);

        // Projects, then New project's recorded companion beside the menu, through the instruction hold.
        host.Navigator.OpenMenu(MenuPlace.Projects);
        host.Press(MenuColumn.Menu, ProjectsScreens.NewProject);
        Assert.That(host.Navigator.Beside, Is.SameAs(host.NewProject), "New project opens beside the menu");
        Follow(host);
        Assert.That(player.Reading, Is.True, "something is open on the plane");
        await Frames(host, InstructionHold * 3);
        Assert.That(player.Plays, Is.EqualTo(1), "the demonstration has not started again");
        Assert.That(player.InstructionsFor(approve.ExecutionId), Has.Count.EqualTo(2), "its instructions are still offered");

        // Still there through the end hold.
        var instruction = Demonstration.InstructionsAfterApproving()[0].Answer;
        await player.Session.SubmitAsync(new CommandFactory(Samples.Client).SendInstruction(approve.ExecutionId, instruction.Text!));
        await Until(host, () => player.Ended, "the second round ends and its end holds");
        await Frames(host, EndHold * 4);
        Assert.That((player.Plays, player.Ended), Is.EqualTo((1, true)), "the end still holds");
        Assert.That(host.Navigator.Beside, Is.SameAs(host.NewProject), "New project is still open");

        // New project closed, the menu still open on Projects: it still holds.
        host.Navigator.CloseBeside();
        Follow(host);
        Assert.That((host.Navigator.IsOpen, player.Reading), Is.EqualTo((true, true)), "the menu alone is open");
        await Frames(host, EndHold * 4);
        Assert.That((player.Plays, player.Ended), Is.EqualTo((1, true)), "the end still holds under the menu");

        var folding = Stopwatch.StartNew();
        host.Navigator.CloseMenu();
        Follow(host);
        Assert.That(player.Reading, Is.False, "everything has folded to the bar");
        await Until(host, () => player.Plays == 2, "the demonstration starts again");
        Assert.That(folding.Elapsed, Is.GreaterThanOrEqualTo(EndHold), "the hold ran whole from the fold");
    }

    /// <summary>Plays to the approval, then opens the directed task's file from Tasks, as a judge does.</summary>
    private async Task<DemonstrationMenuHost> OpenTheDirectedFileAtTheApprovalAsync()
    {
        player = new DemonstrationPlayer(Demonstration.Recording(), Samples.Client, new DemonstrationOptions { Speed = Speed });
        player.Session.Start();
        var host = new DemonstrationMenuHost(player);
        await Demonstration.ToTheApprovalAsync(player.Session);
        host.Navigator.OpenMenu(somethingWaits: true);
        Assert.That(host.Press(MenuColumn.Menu, TasksColumn.OpenTask, Demonstration.DirectedWorkstream(player.Session).WorkstreamId), Is.True);
        Follow(host);
        Assert.That(player.Reading, Is.True, "a task's file is open beside the menu");
        return host;
    }

    private async Task ApproveAsync(DemonstrationMenuHost host)
    {
        var approve = Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Answer;
        await player!.Session.SubmitAsync(new CommandFactory(Samples.Client).RespondToApproval(approve.ExecutionId, approve.ApprovalId!, ApprovalDecision.Approve));
        await Until(host, () => player.InstructionsFor(approve.ExecutionId).Count > 0, "the approved turn ends and its instructions are offered");
    }

    /// <summary>
    /// Closes the file with its Close, then the menu to the bar, then plays frames until the
    /// demonstration starts again; returns how long that took from the fold.
    /// </summary>
    private async Task<TimeSpan> CloseAndWaitForTheStartAgainAsync(DemonstrationMenuHost host)
    {
        host.Press(MenuColumn.File, Footer.Close);
        Follow(host);
        Assert.That((host.Navigator.Beside, player!.Reading), Is.EqualTo(((IMenuColumn?)null, true)), "the file is closed and the menu still holds");
        var closing = Stopwatch.StartNew();
        host.Navigator.CloseMenu();
        Follow(host);
        Assert.That(player.Reading, Is.False, "everything has folded to the bar");
        await Until(host, () => player.Plays == 2, "the demonstration starts again");
        return closing.Elapsed;
    }

    /// <summary>As the workspace's director tells the player each frame: whether anything is open on the plane.</summary>
    private void Follow(DemonstrationMenuHost host) => player!.Reading = host.Navigator.ShowsAnything;

    /// <summary>Plays frames for <paramref name="time"/>, as the game loop does.</summary>
    private async Task Frames(DemonstrationMenuHost host, TimeSpan time)
    {
        var playing = Stopwatch.StartNew();
        while (playing.Elapsed < time)
        {
            host.Pump();
            Follow(host);
            await Task.Delay(5);
        }
    }

    /// <summary>Plays frames until <paramref name="condition"/> holds.</summary>
    private async Task Until(DemonstrationMenuHost host, Func<bool> condition, string description)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(10)) Assert.Fail("Timed out waiting until " + description + ". Plays: " + player!.Plays + ".");
            host.Pump();
            Follow(host);
            await Task.Delay(5);
        }
    }
}
