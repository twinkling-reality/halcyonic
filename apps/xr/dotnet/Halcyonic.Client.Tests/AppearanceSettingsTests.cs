using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture, NonParallelizable]
public class AppearanceSettingsTests
{
    private GlazePalette previous = null!;

    [SetUp]
    public void IsolatePalette() { previous = Glaze.Palette; Glaze.SetPalette(GlazePalette.Default); }

    [TearDown]
    public void RestorePalette() => Glaze.SetPalette(previous);

    [Test]
    public void DefaultKeepsExistingSurfaceAndSemanticColoursExactly()
    {
        Assert.That(Glaze.Panel.ToString(), Is.EqualTo("#1B222D"));
        Assert.That(Glaze.Raised.ToString(), Is.EqualTo("#242D3A"));
        Assert.That(Glaze.Control.ToString(), Is.EqualTo("#303C4E"));
        Assert.That(Glaze.Well.ToString(), Is.EqualTo("#151B24"));
        Assert.That(Glaze.Text.ToString(), Is.EqualTo("#EEF2F6"));
        Assert.That(Glaze.TextSecondary.ToString(), Is.EqualTo("#AAB5C2"));
        Assert.That(Glaze.Tone(GlazeTone.Accent).Strong.ToString(), Is.EqualTo("#7FA9F8"));
        Assert.That(GlazePalette.Graphite.Tone(GlazeTone.Simulated), Is.EqualTo(GlazePalette.Default.Tone(GlazeTone.Simulated)));
        foreach (var tone in new[] { GlazeTone.Attention, GlazeTone.Failure, GlazeTone.Success, GlazeTone.Unknown, GlazeTone.Active })
            Assert.That(GlazePalette.Graphite.Tone(tone), Is.EqualTo(GlazePalette.Default.Tone(tone)), tone.ToString());
        Assert.Throws<NotSupportedException>(() => ((IList<GlazePalette>)GlazePalette.Presets).Add(GlazePalette.Default));
    }

    [Test]
    public void BothPresetsKeepWordsReadableOverWhitePassthroughAndSelectedGlass()
    {
        foreach (var palette in GlazePalette.Presets)
        {
            var glass = palette.Panel.Over(GlazeColor.Hex(0xFFFFFF), Glaze.Menu.GlassOpacity);
            var lit = palette.Selection.Over(glass, Glaze.Menu.LitFillOpacity);
            foreach (var under in new[] { glass, lit })
            {
                foreach (var words in new[] { palette.Text, palette.TextSecondary }.Concat(Enum.GetValues<GlazeTone>().Select(tone => palette.Tone(tone).Foreground)))
                    Assert.That(GlazeColor.Contrast(words, under), Is.GreaterThanOrEqualTo(4.5), $"{palette.Name} {words} on {under}");
            }
            foreach (var tone in Enum.GetValues<GlazeTone>())
            {
                var colours = palette.Tone(tone);
                Assert.That(GlazeColor.Contrast(colours.Foreground, colours.Container), Is.GreaterThanOrEqualTo(4.5));
                Assert.That(GlazeColor.Contrast(colours.OnStrong, colours.Strong), Is.GreaterThanOrEqualTo(4.5));
            }
        }
    }

    [Test]
    public void BothPresetsRoundTripOnlyVersionedAppearanceValues()
    {
        foreach (var palette in GlazePalette.Presets)
        {
            var settings = new AppearanceSettings();
            settings.Apply(palette);
            Assert.That(settings.Save(), Is.EqualTo("version=1;theme=" + palette.Key));
            Assert.That(AppearanceSettings.Load(settings.Save()).Applied, Is.SameAs(palette));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("version=2;theme=graphite")]
    [TestCase("version=1;theme=missing")]
    [TestCase("version=1;theme=graphite;theme=glaze")]
    [TestCase("theme=graphite")]
    [TestCase("version=1;theme=graphite;token=anything")]
    public void DamagedOrUnsupportedPreferencesFallBackToTheOriginalAppearance(string? saved) =>
        Assert.That(AppearanceSettings.Load(saved).Applied, Is.SameAs(GlazePalette.Default));

    [Test]
    public void ChoosingIsLocalPreviewIsTemporaryApplyAlonePersists()
    {
        var settings = new AppearanceSettings();
        var saved = new List<string>();
        var column = new AppearanceColumn(settings, () => saved.Add(settings.Save()));
        column.Act(AppearanceColumn.Choose, "graphite");
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default));
        Assert.That(column.Frame!.Lines.Single(line => line.Key == "graphite").Chosen, Is.True);
        Assert.That(column.Frame.Lines.Take(2).Select(line => line.AnswerMark), Is.All.EqualTo(AnswerMark.Single));
        column.Act(AppearanceColumn.Preview, null);
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Graphite));
        Assert.That(settings.Applied, Is.SameAs(GlazePalette.Default));
        Assert.That(saved, Is.Empty);
        Assert.That(column.Frame!.Subject, Is.EqualTo("Preview: Graphite"));
        Assert.That(column.Frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Apply theme"));
        column.Act(AppearanceColumn.Apply, null);
        Assert.That(settings.Applied, Is.SameAs(GlazePalette.Graphite));
        Assert.That(saved, Is.EqualTo(new[] { "version=1;theme=graphite" }));
        column.FocusLeft();
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Graphite), "departure restores the newly applied appearance");
    }

    [Test]
    public void CancelFocusAndCloseRollBackWithoutSavingOrLateApply()
    {
        foreach (var leaving in new[] { "cancel", "focus", "close" })
        {
            var settings = new AppearanceSettings();
            var saved = 0;
            var column = new AppearanceColumn(settings, () => saved++);
            column.Act(AppearanceColumn.Choose, "graphite");
            column.Act(AppearanceColumn.Preview, null);
            if (leaving == "focus") column.FocusLeft();
            else column.Act(leaving == "cancel" ? AppearanceColumn.Cancel : Footer.Close, null);
            column.Act(AppearanceColumn.Apply, null);
            Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default), leaving);
            Assert.That(saved, Is.Zero, leaving);
        }
    }

    [Test]
    public void RestoreRequiresItsExplicitPageAndTouchesOnlyAppearance()
    {
        var settings = new AppearanceSettings();
        settings.Apply(GlazePalette.Graphite);
        Glaze.SetPalette(settings.Applied);
        var comfort = new Comfort { Still = true, Sounds = SoundLevel.Off, Text = TextSize.Larger };
        var originalComfort = comfort.Save();
        var saved = 0;
        var column = new AppearanceColumn(settings, () => saved++);
        column.Act(AppearanceColumn.Restore, null);
        Assert.That(settings.Applied, Is.SameAs(GlazePalette.Graphite));
        column.Act(AppearanceColumn.ReadRestore, null);
        Assert.That(column.Frame!.Subject, Is.EqualTo("Restore default appearance?"));
        column.Act(AppearanceColumn.Restore, null);
        Assert.That(settings.Applied, Is.SameAs(GlazePalette.Default));
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default));
        Assert.That(saved, Is.EqualTo(1));
        Assert.That(comfort.Save(), Is.EqualTo(originalComfort));
    }

    [Test]
    public void PreviewExamplesAreInertAndAllFivePagesRetainThePreviewLabel()
    {
        var settings = new AppearanceSettings();
        var saved = 0;
        var column = new AppearanceColumn(settings, () => saved++);
        column.Act(AppearanceColumn.Preview, null);
        var found = new List<PageLine>();
        for (var page = 0; page < 5; page++)
        {
            var frame = column.Frame!;
            Assert.That(frame.Subject, Does.StartWith("Preview: "));
            found.AddRange(frame.Lines);
            Assert.That(frame.Footer.All.Count(), Is.LessThanOrEqualTo(3));
            Assert.That(frame.Lines.Count, Is.LessThanOrEqualTo(3), "body paging fits the larger text's page");
            foreach (var line in frame.Lines) if (line.Action != null && line.Action != Footer.NextPage) column.Act(line.Action, line.Key);
            column.Act(Footer.NextPage, null);
        }
        Assert.That(found.Any(line => line.Opens), Is.True);
        Assert.That(found.Any(line => line.Control == LineControl.Input), Is.True);
        Assert.That(found.Any(line => !line.Available), Is.True);
        Assert.That(found.Any(line => line.Runs.AnyTheirs), Is.True);
        Assert.That(saved, Is.Zero);
        column.FocusLeft();
    }

    [Test]
    public void CategoryIndexContainsOnlySupportedGroupsWithCurrentValues()
    {
        var appearance = new AppearanceSettings();
        var comfort = new Comfort();
        var rows = ComfortSettings.Of(comfort, () => { });
        var hub = new SettingsHubColumn(new FakeMenuHost(), rows, appearance, () => { });
        Assert.That(hub.Frame!.Lines.Select(line => (line.Words, line.Fact)), Is.EqualTo(new[] { ("Appearance", "Glaze"), ("Comfort", "Standard") }));
        Assert.That(hub.Frame.Lines.All(line => line.Opens && !line.Choice), Is.True);
        hub.Act(SettingsHubColumn.OpenCategory, "Comfort");
        Assert.That(hub.Frame!.Lines.Any(line => line.Key == "moving-badges"), Is.True);
        hub.Act(SettingsColumn.OpenSetting, "moving-badges");
        hub.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(comfort.Still, Is.True);
        hub.Act(SettingsHubColumn.Back, null);
        Assert.That(hub.Frame!.Lines.Any(line => line.Key == "Comfort"), Is.True);
        hub.Act(SettingsHubColumn.OpenCategory, "Characters");
        Assert.That(hub.Frame.Lines.All(line => line.Action == SettingsHubColumn.OpenCategory), Is.True);
    }

    [Test]
    public void BackFromAppearanceRollsBackAndDropsTheApplyTarget()
    {
        var settings = new AppearanceSettings();
        var saved = 0;
        var hub = new SettingsHubColumn(new FakeMenuHost(), Array.Empty<MenuSetting>(), settings, () => saved++);
        hub.Act(SettingsHubColumn.OpenCategory, SettingsHubColumn.Appearance);
        hub.Act(AppearanceColumn.Choose, "graphite");
        hub.Act(AppearanceColumn.Preview, null);
        hub.Act(SettingsHubColumn.Back, null);
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default));
        hub.Act(AppearanceColumn.Apply, null);
        Assert.That(saved, Is.Zero);
    }

    [Test]
    public void ComputerCategoryPreservesOutsideAddressAndBoundedForgetConfirmation()
    {
        const string address = "192.0.2.40";
        var forgotten = 0;
        var pairing = new MenuSetting("pairing", SettingsText.YourMac, "Pairing",
            () => new SettingNow(address, "Paired with " + address, "Forget it", "This headset needs pairing again", "Forget this computer",
                confirm: "Yes, forget this computer", valueIsData: true, about: address), () => forgotten++);
        var hub = new SettingsHubColumn(new FakeMenuHost(), new[] { pairing }, new AppearanceSettings(), () => { });
        var category = hub.Frame!.Lines.Single(line => line.Key == SettingsText.YourMac);
        Assert.That(category.FactRuns!.AllTheirs, Is.True);
        Assert.That(category.FactIsData, Is.True);
        hub.Act(SettingsHubColumn.OpenCategory, SettingsText.YourMac);
        hub.Act(SettingsColumn.OpenSetting, "pairing");
        hub.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(hub.Frame!.Footer.Confirming, Is.True);
        Assert.That(hub.Frame.Footer.All.Count(), Is.EqualTo(3), "the wrapper adds no fourth control to a confirmation");
        hub.Act(SettingsHubColumn.Back, null);
        hub.Act(SettingsColumn.Yes, null);
        Assert.That(forgotten, Is.Zero, "leaving a leaf does not retain Forget authority");
    }
}
