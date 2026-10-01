using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class FocusPresenceTests
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void StartsFocusedAndReadyAndSuspendsInputTheMomentFocusGoes()
    {
        var focus = new FocusPresence(At(0));
        focus.Tick(At(0));
        Assert.That(focus.InputSuspended, Is.False);
        focus.Report(false, At(1));
        Assert.That(focus.InputSuspended, Is.True, "before the next tick");
        Assert.That(focus.Tick(At(1)).Left, Is.True);
        Assert.That(focus.Folded, Is.False, "not yet: focus may come straight back");
    }

    [Test]
    public void TheGestureThatReturnsFocusPressesNothing()
    {
        var focus = new FocusPresence(At(0));
        focus.Report(false, At(1));
        focus.Tick(At(5));
        focus.Report(true, At(10));
        focus.Tick(At(10));
        Assert.That(focus.InputSuspended, Is.True, "the pinch that brought focus back");
        focus.Tick(At(10.4));
        Assert.That(focus.InputSuspended, Is.True);
        focus.Tick(At(10.5));
        Assert.That(focus.InputSuspended, Is.False, "ready once focus has stayed half a second");
    }

    [Test]
    public void FoldsOnlyAfterFocusStaysAwayAndRestoresOnReturn()
    {
        var focus = new FocusPresence(At(0));
        focus.Report(false, At(1));
        Assert.That(focus.Tick(At(3.9)).FoldChanged, Is.False);
        var fold = focus.Tick(At(4));
        Assert.That(fold.FoldChanged && focus.Folded, Is.True, "three seconds away");
        focus.Report(true, At(20));
        Assert.That(focus.Tick(At(20.2)).FoldChanged, Is.False, "still folded while input is not ready");
        var back = focus.Tick(At(20.5));
        Assert.That(back.FoldChanged, Is.True);
        Assert.That(focus.Folded, Is.False);
    }

    [Test]
    public void FlappingFocusNeverRearrangesTheStage()
    {
        // The Quest's system windows made focus flap many times a second (quest-3-device.md).
        var focus = new FocusPresence(At(0));
        var t = 1.0;
        var foldChanges = 0;
        for (var i = 0; i < 40; i++)
        {
            focus.Report(i % 2 == 1, At(t));
            if (focus.Tick(At(t)).FoldChanged) foldChanges++;
            t += 0.1;
        }
        Assert.That(foldChanges, Is.Zero);
        Assert.That(focus.Folded, Is.False);
        Assert.That(focus.Epoch, Is.EqualTo(20), "every loss still resets half-done confirmations");
    }

    [Test]
    public void TheAppsOwnKeyboardFoldsNothingAndResetsNoConfirmation()
    {
        var focus = new FocusPresence(At(0));
        focus.KeyboardOpened();
        focus.Report(false, At(1));
        var typing = focus.Tick(At(30));
        Assert.That(focus.InputSuspended, Is.True, "the keyboard has the input");
        Assert.That(typing.Left, Is.False);
        Assert.That(focus.Folded, Is.False, "typing for half a minute rearranges nothing");
        Assert.That(focus.Epoch, Is.Zero);
        focus.Report(true, At(31));
        focus.KeyboardClosed();
        focus.Tick(At(31.5));
        Assert.That(focus.InputSuspended, Is.False);

        focus.Report(false, At(40));
        Assert.That(focus.Tick(At(40)).Left, Is.True, "a window after the keyboard closed counts");
        focus.Tick(At(43));
        Assert.That(focus.Folded, Is.True);
    }

    [Test]
    public void ALossBetweenTicksIsReportedOnceEvenIfFocusAlreadyCameBack()
    {
        var focus = new FocusPresence(At(0));
        focus.Report(false, At(1));
        focus.Report(true, At(1.05));
        Assert.That(focus.Tick(At(1.1)).Left, Is.True, "a confirmation half done must be confirmed afresh");
        Assert.That(focus.Tick(At(1.2)).Left, Is.False);
        Assert.That(focus.InputSuspended, Is.True, "within the grace");
    }
}
