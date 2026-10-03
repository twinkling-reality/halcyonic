using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class SettingsColumnTests
{
    private static (SettingsColumn Settings, Comfort Comfort, List<string> Saved, FakeMenuHost Host) Settings(TextSize text = TextSize.Standard)
    {
        var comfort = new Comfort();
        var saved = new List<string>();
        var host = new FakeMenuHost { TextSize = text };
        var space = new[]
        {
            new MenuSetting("around", SettingsText.YourSpace, "Around you", () => new SettingNow("Your room", "Your room, as the cameras show it", "A space of its own", "The room fades for a space of Halcyonic's", "Show a space of its own"), () => saved.Add("around")),
            new MenuSetting("characters", SettingsText.YourSpace, "The characters", () => new SettingNow("In front of you", "In front of you", "Make room for a window", "The characters turn to your right", SettingsText.MakeRoomForWindow), () => saved.Add("characters")),
            new MenuSetting("menu", SettingsText.YourSpace, "The menu", () => new SettingNow("Where you moved it", "Where you moved it", "Reset position", "The menu stands where it opens at first", "Reset position"), () => saved.Add("menu")),
        };
        var column = new SettingsColumn(host, space.Concat(ComfortSettings.Of(comfort, () => saved.Add(comfort.Save()))).ToList());
        return (column, comfort, saved, host);
    }

    [Test]
    public void EachSettingIsARowUnderItsGroupsHeadingItsValueTheSmallFact()
    {
        var (settings, _, _, _) = Settings();
        var frame = settings.Frame!;
        Assert.That(frame.Subject, Is.EqualTo("What would you like to change?"));
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Your space", "Around you", "The characters", "The menu" }), "a page a group, its heading counting as a row");
        Assert.That(frame.Lines[0].Action, Is.Null, "the heading says, and takes no press");
        Assert.That(frame.Lines[2].Fact, Is.EqualTo("In front of you"));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next page"));
        settings.Act(Footer.NextPage, null);
        Assert.That(settings.Frame!.Lines.Select(line => (line.Words, line.Fact)), Is.EqualTo(new[]
        {
            ("Comfort", (string?)null), ("Text size", "Standard"), ("Moving badges", "On"), ("Sounds", "On"),
        }));
    }

    [Test]
    public void AChosenSettingSaysWhatItIsAndWhatItsChangeDoesWhichIsTheMainAction()
    {
        var (settings, comfort, saved, _) = Settings();
        settings.Act(Footer.NextPage, null);
        settings.Act(SettingsColumn.OpenSetting, "text-size");
        var frame = settings.Frame!;
        Assert.That(frame.Side!.Subject, Is.EqualTo("Text size"));
        Assert.That(frame.Side.Facts.Select(fact => (fact.Name, fact.Value)), Is.EqualTo(new[]
        {
            ("Now", "The standard size"), ("A step larger", "Text 15 percent larger, and 3 rows a page"),
        }));
        var change = frame.Footer[PromptSlot.FarRight]!;
        Assert.That((change.Words, change.Main, change.Id), Is.EqualTo(("Make text larger", true, SettingsColumn.ChangeSetting)));
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(comfort.Text, Is.EqualTo(TextSize.Larger));
        Assert.That(saved.Last(), Does.StartWith("text=larger"), "the device keeps it");
        Assert.That(settings.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Make text standard"));
    }

    [Test]
    public void ASettingOfThreeValuesStepsToTheNextItsPromptNamingIt()
    {
        var (settings, comfort, _, _) = Settings();
        settings.Act(SettingsColumn.OpenSetting, "sounds");
        Assert.That(settings.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Make sounds quieter"));
        settings.Act(SettingsColumn.ChangeSetting, null);
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(comfort.Sounds, Is.EqualTo(SoundLevel.Off));
        Assert.That(settings.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Turn sounds on"));
        Assert.That(settings.Frame!.Lines.Single(line => line.Key == "sounds").Fact, Is.EqualTo("Off"));
    }

    [Test]
    public void PagingWaitsWhileASettingIsChosenAndCloseDetailsLetsGo()
    {
        var (settings, _, saved, _) = Settings();
        settings.Act(SettingsColumn.OpenSetting, "menu");
        Assert.That(settings.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Reset position"));
        settings.Act(Footer.NextPage, null);
        Assert.That(settings.Frame!.Lines.Any(line => line.Key == "menu"), Is.True, "paging waits");
        settings.Act(SidePanel.Close, null);
        Assert.That(settings.Frame!.Side, Is.Null);
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(saved, Is.Empty, "with nothing chosen, there's no change to make");
    }

    [Test]
    public void WithLargerTextAPageHoldsTwoRowsUnderItsHeading()
    {
        var (settings, _, _, _) = Settings(TextSize.Larger);
        Assert.That(settings.Frame!.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Your space", "Around you", "The characters" }));
        settings.Act(Footer.NextPage, null);
        Assert.That(settings.Frame!.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Your space", "The menu" }));
    }

    /// <summary>The computer's pairing as a setting that asks before it forgets, its state held by the test.</summary>
    private sealed class Pairing
    {
        public bool Paired { get; set; } = true;

        public string Address { get; set; } = "192.168.1.20";

        public string? Busy { get; set; }

        public int Forgotten { get; private set; }

        public MenuSetting Setting => new("pairing", "Your computer", "Pairing", () => Paired
                ? new SettingNow(Address, "Paired with " + Address, "Forget it", "This headset then needs pairing again to reach it", "Forget this computer",
                    reason: Busy, confirm: "Yes, forget this computer", valueIsData: true)
                : new SettingNow("Not paired", "Not paired with a computer", "Pair", "Pair this headset with your computer", "Pair with a computer", reason: Busy),
            () =>
            {
                if (!Paired) return;
                Paired = false;
                Forgotten++;
            });
    }

    private static (SettingsColumn Settings, Pairing Pairing, FakeMenuHost Host) Forgetting()
    {
        var pairing = new Pairing();
        var host = new FakeMenuHost();
        var settings = new SettingsColumn(host, new[] { pairing.Setting, new MenuSetting("menu", SettingsText.YourSpace, "The menu",
            () => new SettingNow("Where it opened", "Where it opened", "Reset position", "The menu stands where you look", "Reset position"), () => { }) });
        settings.Act(SettingsColumn.OpenSetting, "pairing");
        return (settings, pairing, host);
    }

    [Test]
    public void AChangeThatAsksFirstArmsWithCancelInItsPlaceAndYesInTheFreeMiddle()
    {
        var (settings, pairing, _) = Forgetting();
        Assert.That(settings.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Forget this computer"));
        Assert.That(settings.Frame!.Lines.Single(line => line.Key == "pairing").FactIsData, Is.True, "the address is the person's, shown as data");
        Assert.That(settings.Frame!.Side!.Facts[0].ValueIsData, Is.True);
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(pairing.Forgotten, Is.EqualTo(0), "the first press only arms");
        var footer = settings.Frame!.Footer;
        Assert.That(footer.Confirming, Is.True);
        Assert.That((footer[PromptSlot.FarRight]!.Id, footer[PromptSlot.FarRight]!.Kind), Is.EqualTo((SettingsColumn.Cancel, PromptKind.Cancel)), "Cancel where the first press was");
        Assert.That((footer[PromptSlot.Free]!.Words, footer[PromptSlot.Free]!.Kind), Is.EqualTo(("Yes, forget this computer", PromptKind.Yes)), "Yes where nothing stood");
        settings.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(pairing.Forgotten, Is.EqualTo(0), "a second press on the change does nothing while it is armed");
        settings.Act(SettingsColumn.Yes, null);
        Assert.That(pairing.Forgotten, Is.EqualTo(1));
        Assert.That(settings.Frame!.Footer.Confirming, Is.False);
        Assert.That(settings.Frame!.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Pair with a computer"));
        settings.Act(SettingsColumn.Yes, null);
        Assert.That(pairing.Forgotten, Is.EqualTo(1), "a Yes with nothing armed does nothing");
    }

    [Test]
    public void AnArmedChangeLapsesWithTimeWhenFocusLeavesOnCancelOnAnotherSettingOrOnceItCantBeMade()
    {
        var (settings, pairing, host) = Forgetting();
        settings.Act(SettingsColumn.ChangeSetting, null);
        host.Now += SettingsColumn.ConfirmSeconds;
        settings.Tick();
        Assert.That(settings.Frame!.Footer.Confirming, Is.False, "after its seconds");
        settings.Act(SettingsColumn.Yes, null);

        settings.Act(SettingsColumn.ChangeSetting, null);
        settings.FocusLeft();
        Assert.That(settings.Frame!.Footer.Confirming, Is.False, "focus left, or Settings left the plane");

        settings.Act(SettingsColumn.ChangeSetting, null);
        settings.Act(SettingsColumn.Cancel, null);
        Assert.That(settings.Frame!.Footer.Confirming, Is.False, "Cancel");

        settings.Act(SettingsColumn.ChangeSetting, null);
        settings.Act(SettingsColumn.OpenSetting, "menu");
        settings.Act(SettingsColumn.OpenSetting, "pairing");
        Assert.That(settings.Frame!.Footer.Confirming, Is.False, "another setting chosen");

        settings.Act(SettingsColumn.ChangeSetting, null);
        pairing.Busy = "Your computer is pairing now.";
        settings.Tick();
        Assert.That(settings.Frame!.Footer.Confirming, Is.False, "it can't be made now");
        settings.Act(SettingsColumn.Yes, null);
        Assert.That(pairing.Forgotten, Is.EqualTo(0), "none of them forgot anything");
    }

    [Test]
    public void AForgetArmedForOneComputerLapsesOnceAnotherIsPairedAndNeverForgetsIt()
    {
        var (settings, pairing, _) = Forgetting();
        settings.Act(SettingsColumn.ChangeSetting, null);
        // Pairing with another computer finished within the seconds the Forget stays armed.
        pairing.Address = "192.168.1.31";
        settings.Act(SettingsColumn.Yes, null);
        Assert.That(pairing.Forgotten, Is.EqualTo(0), "a Yes pressed before the frame showed the new computer forgets nothing");
        settings.Tick();
        Assert.That(settings.Frame!.Footer.Confirming, Is.False, "what it showed changed, so it lapsed");
        settings.Act(SettingsColumn.Yes, null);
        Assert.That(pairing.Forgotten, Is.EqualTo(0));
    }

    [Test]
    public void AValueChangedElsewhereDrawsAgainWithoutAPress()
    {
        var (settings, pairing, _) = Forgetting();
        _ = settings.Frame;
        var changes = 0;
        settings.Changed += () => changes++;
        settings.Tick();
        Assert.That(changes, Is.EqualTo(0), "nothing moved");
        pairing.Paired = false;
        settings.Tick();
        settings.Tick();
        Assert.That(changes, Is.EqualTo(2), "it moved, and the frame wasn't asked for again yet");
        Assert.That(settings.Frame!.Lines.Single(line => line.Key == "pairing").Fact, Is.EqualTo("Not paired"));
        settings.Tick();
        Assert.That(changes, Is.EqualTo(2), "drawn as it stands, nothing more");
    }
}
