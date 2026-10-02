using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class PanelModelTests
{
    private static PanelAction Action(string id, PanelActionRole role) => new(id, id, role);

    [Test]
    public void ABarAdmitsOnePrimaryTwoSecondaryOneDestructiveAndBack()
    {
        var set = new ActionSet(
            Action("primary", PanelActionRole.Primary),
            null,
            Action("second", PanelActionRole.Secondary),
            Action("back", PanelActionRole.Back),
            Action("stop", PanelActionRole.Destructive),
            Action("first", PanelActionRole.Secondary));
        Assert.That(set.All.Select(action => action.Id), Is.EqualTo(new[] { "back", "stop", "second", "first", "primary" }),
            "Back and the destructive action at the left, the primary at the right end");

        Assert.Throws<InvalidOperationException>(() => _ = new ActionSet(Action("a", PanelActionRole.Primary), Action("b", PanelActionRole.Primary)));
        Assert.Throws<InvalidOperationException>(() => _ = new ActionSet(
            Action("a", PanelActionRole.Secondary), Action("b", PanelActionRole.Secondary), Action("c", PanelActionRole.Secondary)));
        Assert.Throws<InvalidOperationException>(() => _ = new ActionSet(Action("a", PanelActionRole.Destructive), Action("b", PanelActionRole.Destructive)));
        Assert.Throws<InvalidOperationException>(() => _ = new ActionSet(Action("a", PanelActionRole.Back), Action("b", PanelActionRole.Back)));
        Assert.Throws<InvalidOperationException>(() => _ = new ActionSet(Action("a", PanelActionRole.Attention)), "attention goes in a banner");
        Assert.That(ActionSet.None.All, Is.Empty);
    }

    [Test]
    public void AConfirmationsYesIsItsPrimaryOrDestructiveAction()
    {
        var cancel = Action("cancel", PanelActionRole.Secondary);
        Assert.DoesNotThrow(() => _ = new ConfirmStep("Sure?", Action("yes", PanelActionRole.Primary), cancel));
        Assert.DoesNotThrow(() => _ = new ConfirmStep(null, Action("yes", PanelActionRole.Destructive), cancel));
        Assert.Throws<ArgumentException>(() => _ = new ConfirmStep("Sure?", Action("yes", PanelActionRole.Secondary), cancel));
    }

    [Test]
    public void OnlyAHeldActionShowsTheMicrophone()
    {
        Assert.Throws<ArgumentException>(() => _ = new PanelAction("approve", "Approve", PanelActionRole.Primary, icon: GlazeIcon.HoldToTalk));
        Assert.Throws<ArgumentException>(() => _ = new ConfirmStep("Sure?",
            new PanelAction("yes", "Yes, tell it", PanelActionRole.Primary, icon: GlazeIcon.HoldToTalk), Action("cancel", PanelActionRole.Secondary)));
        var hold = new PanelAction("hold", "Hold to talk", PanelActionRole.Secondary, holds: true, icon: GlazeIcon.HoldToTalk);
        Assert.That((hold.Holds, hold.Icon), Is.EqualTo((true, (GlazeIcon?)GlazeIcon.HoldToTalk)));
    }

    [Test]
    public void ABannerOffersAtMostTwoActions()
    {
        var banner = new PanelBanner("News", GlazeTone.Attention, Action("a", PanelActionRole.Attention), Action("b", PanelActionRole.Secondary));
        Assert.That(banner.Actions, Has.Count.EqualTo(2));
        Assert.Throws<ArgumentException>(() => _ = new PanelBanner("News", GlazeTone.Neutral,
            Action("a", PanelActionRole.Secondary), Action("b", PanelActionRole.Secondary), Action("c", PanelActionRole.Secondary)));
    }

    [Test]
    public void AnUnavailableActionKeepsItsPlaceAndSaysWhy()
    {
        var locked = new PanelAction("start", "Start building", PanelActionRole.Primary, available: false, reason: "Choose where its files live.");
        Assert.That((locked.Available, locked.Reason), Is.EqualTo((false, "Choose where its files live.")));
        var open = new PanelAction("start", "Start building", PanelActionRole.Primary, reason: "ignored");
        Assert.That(open.Reason, Is.Null, "an action that can be taken says nothing beside it");
    }
}
