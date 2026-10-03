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
}
