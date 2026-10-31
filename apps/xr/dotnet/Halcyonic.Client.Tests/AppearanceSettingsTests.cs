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
    public void HistoricalGlazeKeepsExistingSurfaceAndSemanticColoursExactly()
    {
        Glaze.SetPalette(GlazePalette.Glaze);
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
    public void NeutralIsStableCurrentDefaultWithoutReinterpretingHistoricalKeys()
    {
        Assert.That(GlazePalette.Presets.Select(palette => palette.Key), Is.EqualTo(new[] { "neutral", "glaze", "graphite" }));
        Assert.That(GlazePalette.Default, Is.SameAs(GlazePalette.Neutral));
        Assert.That(GlazePalette.InstrumentStudy, Is.SameAs(GlazePalette.Neutral));
        Assert.That(Surfaces(GlazePalette.Neutral), Is.EqualTo(new[] { "#242424", "#2E2E2E", "#3A3A3A", "#494949", "#595959", "#1D1D1D", "#F2F2F2", "#C4C4C4", "#A0A0A0", "#8A8A8A" }));
        Assert.That(Surfaces(AppearanceSettings.Load("version=1;theme=glaze").Applied), Is.EqualTo(new[] { "#1B222D", "#242D3A", "#303C4E", "#3B4A61", "#46566F", "#151B24", "#EEF2F6", "#AAB5C2", "#8A96A5", "#73839A" }));
        Assert.That(Surfaces(AppearanceSettings.Load("version=1;theme=graphite").Applied), Is.EqualTo(new[] { "#20242A", "#2A3038", "#363F49", "#45505D", "#526071", "#191D23", "#EEF2F6", "#AAB5C2", "#8A96A5", "#73839A" }));
        Assert.That(GlazePalette.Graphite.Tone(GlazeTone.Accent).Strong.ToString(), Is.EqualTo("#98BAF0"));
        foreach (var palette in GlazePalette.Presets)
            foreach (var tone in new[] { GlazeTone.Active, GlazeTone.Attention, GlazeTone.Success, GlazeTone.Failure, GlazeTone.Unknown, GlazeTone.Simulated })
                Assert.That(palette.Tone(tone), Is.EqualTo(GlazePalette.Glaze.Tone(tone)), palette.Key + " " + tone);
        Assert.That(GlazePalette.Find("instrument-study"), Is.Null);
        Assert.That(GlazePalette.Find("Neutral"), Is.Null, "preset keys remain exact");
    }

    private static string[] Surfaces(GlazePalette palette) => new[] { palette.Panel, palette.Raised, palette.Control, palette.ControlHover, palette.ControlPressed, palette.Well, palette.Text, palette.TextSecondary, palette.TextDisabled, palette.Outline }.Select(colour => colour.ToString()).ToArray();

    [Test]
    public void NeutralPreviewFromHistoricalChoiceRollsBackAndOnlyExplicitApplySaves()
    {
        foreach (var leaving in new[] { "cancel", "focus", "close", "apply" })
        {
            var settings = AppearanceSettings.Load("version=1;theme=glaze");
            Glaze.SetPalette(settings.Applied);
            var saved = new List<string>();
            var column = new AppearanceColumn(settings, () => saved.Add(settings.Save()));
            column.Act(AppearanceColumn.Choose, "neutral");
            Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Glaze));
            column.Act(AppearanceColumn.Preview, null);
            Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Neutral));
            Assert.That(settings.Applied, Is.SameAs(GlazePalette.Glaze));
            Assert.That(saved, Is.Empty);
            if (leaving == "focus") column.FocusLeft();
            else column.Act(leaving == "cancel" ? AppearanceColumn.Cancel : leaving == "close" ? Footer.Close : AppearanceColumn.Apply, null);
            var expected = leaving == "apply" ? GlazePalette.Neutral : GlazePalette.Glaze;
            Assert.That(Glaze.Palette, Is.SameAs(expected), leaving);
            Assert.That(settings.Applied, Is.SameAs(expected), leaving);
            Assert.That(saved, Is.EqualTo(leaving == "apply" ? new[] { "version=1;theme=neutral" } : Array.Empty<string>()));
            Assert.That(AppearanceSettings.Load(settings.Save()).Applied, Is.SameAs(expected));
        }
    }

    [Test]
    public void AllPresetsKeepWordsReadableOverWhitePassthroughAndSelectedGlass()
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
    public void AllPresetsRoundTripOnlyVersionedAppearanceValues()
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
    public void DamagedOrUnsupportedPreferencesFallBackToCurrentDefaultAppearance(string? saved) =>
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
        Assert.That(column.Frame.Lines.Take(GlazePalette.Presets.Count).Select(line => line.AnswerMark), Is.All.EqualTo(AnswerMark.Single));
        column.Act(AppearanceColumn.Preview, null);
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Graphite));
        Assert.That(settings.Applied, Is.SameAs(GlazePalette.Default));
        Assert.That(saved, Is.Empty);
        Assert.That(column.Frame!.Subject, Is.EqualTo("Preview · Graphite"));
        Assert.That(column.Frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Apply Theme"));
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
        Assert.That(column.Frame!.Subject, Is.EqualTo("Restore Default?"));
        Assert.That(column.Frame.Lines.First().Words, Is.EqualTo("Neutral Interface"));
        Assert.That(saved, Is.Zero);
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
            Assert.That(frame.Subject, Does.StartWith("Preview · "));
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
        Assert.That(hub.Frame!.Lines.Select(line => (line.Words, line.Fact)), Is.EqualTo(new[] { ("Appearance", "Neutral"), ("Comfort", "Standard") }));
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

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void MeasuredCategoryHeadingKeepsEverySupportedGroupAndItsValueReachable(TextSize size)
    {
        var host = new FakeMenuHost { TextSize = size };
        host.MeasureTitle = (subject, width) =>
        {
            Assert.That((subject, width), Is.EqualTo((SettingsText.Subject, Glaze.Menu.MenuColumnDegrees)));
            return 2;
        };
        host.MeasurePage = (rows, besideMenu) =>
        {
            Assert.That((rows, besideMenu), Is.EqualTo((2, true)));
            return MenuPage.Rows(2) + MenuPage.TargetGap / 2f;
        };
        var comfort = new Comfort();
        var settings = ComfortSettings.Of(comfort, () => { }).Concat(new[]
        {
            new MenuSetting("room", SettingsText.YourSpace, "Surroundings", () => new SettingNow("Surroundings", "Your room", "Change", "", "Change room"), () => { }),
            new MenuSetting("pairing", SettingsText.YourMac, "Pairing", () => new SettingNow("192.0.2.40", "Paired with 192.0.2.40", "Forget", "", "Forget", valueIsData: true, about: "192.0.2.40"), () => { }),
        }).ToList();
        var hub = new SettingsHubColumn(host, settings, new AppearanceSettings(), () => { });
        var seen = new List<string>();
        for (var page = 0; page < 2; page++)
        {
            var frame = hub.Frame!;
            Assert.That(frame.Source, Is.Null);
            Assert.That(frame.Lines.Count, Is.EqualTo(2), "the full measured room has no reserved source line");
            Assert.That(MenuPage.Rows(frame.Lines.Count), Is.LessThanOrEqualTo(host.MeasurePage(2, true)));
            seen.AddRange(frame.Lines.Select(line => line.Key!));
            var computer = frame.Lines.SingleOrDefault(line => line.Key == SettingsText.YourMac);
            if (computer != null) Assert.That(computer.FactRuns!.AllTheirs, Is.True, "paging preserves the outside computer value");
            var next = frame.Footer.All.Single(each => each.Prompt.Id == Footer.NextPage).Prompt;
            Assert.That(next.Words, Is.EqualTo(Footer.NextPageWords(page, 2)));
            hub.Act(next.Id, null);
        }
        Assert.That(seen, Is.EqualTo(new[] { SettingsHubColumn.Appearance, Comfort.Heading, SettingsText.YourSpace, SettingsText.YourMac }));
        Assert.That(hub.Frame!.Lines.Select(line => line.Key), Is.EqualTo(seen.Take(2)), "the offered pager returns to the first categories");
        Assert.That(hub.Frame.Lines.Any(line => line.Key == Comfort.Heading), Is.True);
        hub.Act(SettingsHubColumn.OpenCategory, Comfort.Heading);
        Assert.That(hub.Frame!.Lines.First().Words, Is.EqualTo(Comfort.Heading), "the offered category opens its existing leaf");
        Assert.That(hub.Frame.Lines.Any(line => line.Action == SettingsColumn.OpenSetting), Is.True);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void DefensiveDepartureAlsoDropsTheApplyTarget()
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
    public void VisibleCancelThenBackReturnsFromPreviewToCategories()
    {
        var hub = new SettingsHubColumn(new FakeMenuHost(), Array.Empty<MenuSetting>(), new AppearanceSettings(), () => { });
        hub.Act(SettingsHubColumn.OpenCategory, SettingsHubColumn.Appearance);
        hub.Act(AppearanceColumn.Choose, "graphite");
        hub.Act(AppearanceColumn.Preview, null);
        Assert.That(hub.Frame!.Footer.All.Select(each => each.Prompt.Id), Does.Contain(AppearanceColumn.Cancel));
        Assert.That(hub.Frame.Footer.All.Select(each => each.Prompt.Id), Does.Not.Contain(SettingsHubColumn.Back));
        hub.Act(AppearanceColumn.Cancel, null);
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default));
        Assert.That(hub.Frame!.Footer.All.Select(each => each.Prompt.Id), Does.Contain(SettingsHubColumn.Back));
        hub.Act(SettingsHubColumn.Back, null);
        Assert.That(hub.Frame!.Lines.Single().Key, Is.EqualTo(SettingsHubColumn.Appearance));
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
        Assert.That(hub.Frame.Footer.All.Select(each => each.Prompt.Id), Does.Contain(SettingsColumn.Cancel));
        hub.Act(SettingsColumn.Cancel, null);
        hub.Act(SidePanel.Close, null);
        Assert.That(hub.Frame!.Footer.All.Select(each => each.Prompt.Id), Does.Contain(SettingsHubColumn.Back));
        hub.Act(SettingsHubColumn.Back, null);
        hub.Act(SettingsColumn.Yes, null);
        Assert.That(forgotten, Is.Zero, "leaving a leaf does not retain Forget authority");
    }

    [TestCase("address")]
    [TestCase("timeout")]
    [TestCase("focus")]
    [TestCase("close")]
    public void WrapperRechecksTheArmedReadingBeforeTheRealForgetCallback(string invalidation)
    {
        var host = new FakeMenuHost();
        var address = "192.0.2.40";
        var confirmed = new List<string>();
        var bare = 0;
        var pairing = new MenuSetting("pairing", SettingsText.YourMac, "Pairing",
            () => new SettingNow("Paired", "Paired with " + address, "Forget it", "This headset needs pairing again", "Forget this computer",
                confirm: "Yes, forget this computer", valueIsData: true, about: address), () => bare++,
            read => confirmed.Add(read.About!));
        var hub = new SettingsHubColumn(host, new[] { pairing }, new AppearanceSettings(), () => { });
        hub.Act(SettingsHubColumn.OpenCategory, SettingsText.YourMac);
        hub.Act(SettingsColumn.OpenSetting, "pairing");
        Assert.That(hub.Frame!.Footer.All.Any(each => each.Prompt.Id == SettingsColumn.ChangeSetting), Is.True);
        hub.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(hub.Frame!.Footer.All.Any(each => each.Prompt.Id == SettingsColumn.Yes), Is.True);
        if (invalidation == "address") address = "192.0.2.41";
        else if (invalidation == "timeout") host.Now += SettingsColumn.ConfirmSeconds;
        else if (invalidation == "focus") hub.FocusLeft();
        else hub.Act(Footer.Close, null);
        // Act calls Tick before passing to the leaf: no update frame is needed to expire authority.
        hub.Act(SettingsColumn.Yes, null);
        Assert.That(confirmed, Is.Empty);
        Assert.That(bare, Is.Zero, "Forget never falls back to the bare Change delegate");
        if (invalidation == "close") hub.Act(SettingsHubColumn.OpenCategory, SettingsText.YourMac);
        hub.Act(SettingsColumn.OpenSetting, "pairing");
        if (hub.Frame!.Side == null) hub.Act(SettingsColumn.OpenSetting, "pairing");
        hub.Act(SettingsColumn.ChangeSetting, null);
        Assert.That(hub.Frame!.Footer.All.Any(each => each.Prompt.Id == SettingsColumn.Yes), Is.True);
        hub.Act(SettingsColumn.Yes, null);
        Assert.That(confirmed, Is.EqualTo(new[] { address }), "fresh confirmation receives exactly the newly seen reading");
    }

    [TestCase("cancel")]
    [TestCase("close")]
    [TestCase("compact")]
    [TestCase("move")]
    [TestCase("focus")]
    [TestCase("renew")]
    [TestCase("place")]
    public void NavigatorDeparturesRollBackAndRefuseTheOldApplyFrame(string departure)
    {
        var host = new FakeMenuHost();
        var appearance = new AppearanceSettings();
        var saved = 0;
        var makers = MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => place == MenuPlace.Settings
            ? new SettingsHubColumn(host, Array.Empty<MenuSetting>(), appearance, () => saved++)
            : new SettingsColumn(host, Array.Empty<MenuSetting>())));
        var navigator = new MenuNavigator(makers);
        navigator.OpenMenu(MenuPlace.Settings);
        void Press(string action, string? key = null)
        {
            var frame = Draw();
            Assert.That(navigator.Act(MenuColumn.Menu, action, key, frame, null), Is.True);
        }
        MenuFrame Draw()
        {
            var frame = navigator.Frames(new MenuBar(navigator.Place, "Nothing waits")).Menu!;
            navigator.Drawn(MenuColumn.Menu, frame, null);
            return frame;
        }
        Press(SettingsHubColumn.OpenCategory, SettingsHubColumn.Appearance);
        var beforePreview = Draw();
        Assert.That(beforePreview.Footer.All.Any(each => each.Prompt.Id == SettingsHubColumn.Back), Is.True);
        Press(AppearanceColumn.Choose, "graphite");
        Press(AppearanceColumn.Preview);
        var preview = Draw();
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Graphite));
        Assert.That(navigator.Act(MenuColumn.Menu, SettingsHubColumn.Back, null, beforePreview, null), Is.False,
            "the Back target from the earlier picker is no longer standing");
        if (departure == "cancel") Press(AppearanceColumn.Cancel);
        else if (departure == "close") Press(Footer.Close);
        else if (departure == "compact") navigator.Compact();
        else if (departure == "move") { navigator.Compact(); navigator.Expand(); }
        else if (departure == "focus") navigator.FocusLeft();
        else if (departure == "renew") navigator.Renew();
        else Press(MenuFrame.ChooseSection, nameof(MenuPlace.Projects));
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default), departure);
        Assert.That(navigator.Act(MenuColumn.Menu, AppearanceColumn.Apply, null, preview, null), Is.False);
        Assert.That(saved, Is.Zero);
        Assert.That(appearance.Applied, Is.SameAs(GlazePalette.Default));
    }

    private sealed class CompositionFile : IMenuColumn
    {
        private MenuFrame frame;
        private readonly SidePanel? side;
        public CompositionFile(bool focused, SidePanel? side = null) { this.side = side; frame = Make(focused); }
        private MenuFrame Make(bool focused)
        {
            var next = new MenuFrame(focused ? "Your answer" : "Task file", new Footer(farRight: new Prompt("keep-draft", "Keep Draft", GlazeIcon.Change, safeInPlace: true)),
                lines: new[] { new PageLine("Draft retained", action: "draft-details", opens: true, chosen: side != null) }, side: side);
            return focused ? next.Focus() : next;
        }
        public MenuFrame? Frame => frame;
        public int Draws { get; private set; }
        public List<string> Acts { get; } = new();
        public event Action? Changed;
        public event Action? Closed;
        public void Focus(bool focused = true) { frame = Make(focused); Changed?.Invoke(); }
        public void Act(string id, string? key) { Acts.Add(id); if (id == Footer.Close) Closed?.Invoke(); }
        public void Drawn(MenuFrame drawn, Footer? sidePanel) => Draws++;
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string text) { }
        public void Said(string words) { }
        public void Tick() { }
        public void FocusLeft() { }
    }

    [TestCase("focused")]
    [TestCase("field")]
    [TestCase("existing-file-focuses")]
    public void ActualCompositionHidingTheRetainedMenuRollsBackOnceAndRequiresFreshDraw(string composition)
    {
        var host = new FakeMenuHost();
        var appearance = new AppearanceSettings();
        var saved = 0;
        var hub = new SettingsHubColumn(host, Array.Empty<MenuSetting>(), appearance, () => saved++);
        var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => place == MenuPlace.Settings
            ? hub : new SettingsColumn(host, Array.Empty<MenuSetting>()))));
        var bar = new MenuBar(MenuPlace.Settings, "Nothing waits");
        navigator.OpenMenu(MenuPlace.Settings);
        MenuFrame Draw()
        {
            var frame = navigator.Frames(bar).Menu!;
            navigator.MenuVisibility(true);
            navigator.Drawn(MenuColumn.Menu, frame, null);
            return frame;
        }
        void Press(string id, string? key = null) => Assert.That(navigator.Act(MenuColumn.Menu, id, key, Draw(), null), Is.True);
        Press(SettingsHubColumn.OpenCategory, SettingsHubColumn.Appearance);
        Press(AppearanceColumn.Choose, "graphite");
        Press(AppearanceColumn.Preview);
        var originalPreview = Draw();
        var file = new CompositionFile(composition == "focused");
        navigator.ShowBeside(file, "pending-task");
        if (composition == "existing-file-focuses")
        {
            var sideBySide = Draw();
            Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Graphite), "a visible ordinary file must retain preview");
            Assert.That(sideBySide.Footer.All.Any(each => each.Prompt.Id == AppearanceColumn.Cancel), Is.True);
            file.Focus();
        }
        var proposed = navigator.Frames(bar);
        Assert.That(proposed.Beside!.Focused, Is.EqualTo(composition != "field"));
        var menuLeaves = 0;
        navigator.Left += column => { if (column == hub) menuLeaves++; };
        var reentrant = true;
        navigator.Changed += () => { if (reentrant) Assert.That(navigator.MenuVisibility(false), Is.False, "identity is set before rollback raises Changed"); };
        // This is the actual composition notification supplied by MenuDirector after focused or MenuAside layout.
        Assert.That(navigator.MenuVisibility(false), Is.True);
        reentrant = false;
        Assert.That(navigator.MenuVisibility(false), Is.False);
        Assert.That((Glaze.Palette, menuLeaves, saved), Is.EqualTo((GlazePalette.Default, 1, 0)));
        Assert.That(navigator.IsOpen, Is.True);
        Assert.That(navigator.Beside, Is.SameAs(file));
        var retained = navigator.Frames(bar).Menu!;
        Assert.That(retained.Subject, Is.EqualTo("Appearance"));
        navigator.Drawn(MenuColumn.Menu, retained, null);
        Assert.That(navigator.Act(MenuColumn.Menu, AppearanceColumn.Preview, null, retained, null), Is.False, "a hidden retained frame cannot acquire drawn proof");
        Assert.That(navigator.Act(MenuColumn.Menu, AppearanceColumn.Apply, null, originalPreview, null), Is.False);
        navigator.CloseBeside();
        var returning = navigator.Frames(bar).Menu!;
        Assert.That(navigator.MenuVisibility(true), Is.True);
        Assert.That(navigator.Act(MenuColumn.Menu, AppearanceColumn.Preview, null, returning, null), Is.False, "returning does not prove a physical draw");
        navigator.Drawn(MenuColumn.Menu, returning, null);
        Assert.That(navigator.Act(MenuColumn.Menu, AppearanceColumn.Apply, null, originalPreview, null), Is.False);
        Assert.That(navigator.Act(MenuColumn.Menu, AppearanceColumn.Preview, null, returning, null), Is.True);
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default), "preview selection was rolled back to the applied preset");
        hub.FocusLeft();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HiddenSettingsDetailsGiveFocusedFileAuthorityAndRecoverTheirOwnerOnReturn(bool fileHasSide)
    {
        var host = new FakeMenuHost();
        var confirmed = new List<string>();
        var pairing = new MenuSetting("pairing", SettingsText.YourMac, "Pairing",
            () => new SettingNow("192.0.2.40", "Paired with 192.0.2.40", "Forget it", "This headset needs pairing again", "Forget this computer",
                confirm: "Yes, forget this computer", valueIsData: true, about: "192.0.2.40"), () => Assert.Fail("bare Change must not run"),
            read => confirmed.Add(read.About!));
        var hub = new SettingsHubColumn(host, new[] { pairing }, new AppearanceSettings(), () => { });
        var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => hub)));
        var bar = new MenuBar(MenuPlace.Settings, "Nothing waits");
        var fileSide = fileHasSide ? new SidePanel("File details", facts: new[] { new SideFact("Draft", "Retained") }) : null;
        var file = new CompositionFile(false, fileSide);
        navigator.OpenMenu(MenuPlace.Settings);
        navigator.ShowBeside(file, "pending-task");
        MenuFrame DrawMenu()
        {
            var frame = navigator.Frames(bar).Menu!;
            navigator.MenuVisibility(true);
            navigator.Drawn(MenuColumn.Menu, frame, null);
            return frame;
        }
        void Press(string id, string? key = null) => Assert.That(navigator.Act(MenuColumn.Menu, id, key, DrawMenu(), null), Is.True);
        Press(SettingsHubColumn.OpenCategory, SettingsText.YourMac);
        Press(SettingsColumn.OpenSetting, "pairing");
        var chosen = DrawMenu();
        Assert.That(chosen.Side, Is.Not.Null);
        Assert.That(navigator.BesideAside, Is.True);
        navigator.Drawn(MenuColumn.Side, chosen, chosen.Side);
        Assert.That(navigator.Act(MenuColumn.Side, SettingsColumn.ChangeSetting, null, chosen, chosen.Side), Is.True,
            "the visible in-place details carry the real Forget prompt");
        var armed = DrawMenu();
        Assert.That(armed.Footer.All.Any(each => each.Prompt.Id == SettingsColumn.Yes), Is.True);
        navigator.Drawn(MenuColumn.Side, armed, armed.Side);
        file.Focus();
        navigator.Frames(bar);
        Assert.That(navigator.MenuVisibility(false), Is.True);
        var hidden = navigator.Frames(bar);
        Assert.That(hidden.Menu!.Side, Is.Not.Null, "Settings retains its chosen details while confirmation authority is released");
        Assert.That(hidden.Menu.Footer.All.Any(each => each.Prompt.Id == SettingsColumn.Yes), Is.False);
        Assert.That(navigator.BesideAside, Is.False, "hidden retained menu details do not own the focused composition");
        Assert.That(navigator.ColumnOf(MenuColumn.Side), Is.SameAs(file));
        navigator.Drawn(MenuColumn.Side, hidden.Menu, hidden.Menu.Side);
        Assert.That(navigator.Standing(MenuColumn.Side, hidden.Menu, hidden.Menu.Side), Is.Null);
        Assert.That(navigator.Act(MenuColumn.Side, SettingsColumn.Yes, null, armed, armed.Side), Is.False);
        Assert.That(navigator.Act(MenuColumn.Menu, SettingsColumn.Yes, null, armed, null), Is.False);
        navigator.Drawn(MenuColumn.File, hidden.Beside, null);
        Assert.That(file.Draws, Is.EqualTo(1));
        Assert.That(hidden.Beside!.Footer.All.Any(each => each.Prompt.Id == "keep-draft"), Is.True);
        Assert.That(navigator.Act(MenuColumn.File, "keep-draft", null, hidden.Beside, null), Is.True);
        if (fileSide != null)
        {
            navigator.Drawn(MenuColumn.Side, hidden.Beside, fileSide);
            Assert.That(navigator.Act(MenuColumn.Side, "keep-draft", null, hidden.Beside, fileSide), Is.True);
        }
        var focusedFrame = hidden.Beside;
        file.Focus(false);
        navigator.Frames(bar);
        Assert.That(navigator.MenuVisibility(true), Is.True);
        var returned = navigator.Frames(bar);
        Assert.That(navigator.BesideAside, Is.True, "actual menu-owned details regain the front on return");
        Assert.That(navigator.ColumnOf(MenuColumn.Side), Is.SameAs(hub));
        Assert.That(navigator.Act(MenuColumn.File, "keep-draft", null, focusedFrame, null), Is.False);
        navigator.Drawn(MenuColumn.File, returned.Beside, null);
        Assert.That(file.Draws, Is.EqualTo(fileHasSide ? 2 : 1), "the retained file cannot earn proof behind menu details");
        Assert.That(navigator.Act(MenuColumn.Side, SettingsColumn.Yes, null, armed, armed.Side), Is.False);
        navigator.Drawn(MenuColumn.Side, returned.Menu, returned.Menu!.Side);
        Assert.That(navigator.Standing(MenuColumn.Side, returned.Menu, returned.Menu.Side), Is.SameAs(hub));
        Assert.That(navigator.Act(MenuColumn.Side, SettingsColumn.ChangeSetting, null, returned.Menu, returned.Menu.Side), Is.True,
            "a fresh physical details draw can arm a fresh confirmation");
        Assert.That(confirmed, Is.Empty);
        hub.FocusLeft();
    }

    [Test]
    public void VisibleMenuAndItsDetailsRetainPreviewWhileCompactDoesNotSuspendTwice()
    {
        var host = new FakeMenuHost();
        var hub = new SettingsHubColumn(host, Array.Empty<MenuSetting>(), new AppearanceSettings(), () => { });
        var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => hub)));
        var bar = new MenuBar(MenuPlace.Settings, "Nothing waits");
        navigator.OpenMenu(MenuPlace.Settings);
        hub.Act(SettingsHubColumn.OpenCategory, SettingsHubColumn.Appearance);
        hub.Act(AppearanceColumn.Choose, "graphite");
        hub.Act(AppearanceColumn.Preview, null);
        navigator.ShowBeside(new CompositionFile(false), "task");
        navigator.Frames(bar);
        Assert.That(navigator.MenuVisibility(true), Is.False, "actual menu parts or menu-owned details remain visible");
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Graphite));
        var leaves = 0;
        navigator.Left += column => { if (column == hub) leaves++; };
        navigator.Compact();
        Assert.That((Glaze.Palette, leaves), Is.EqualTo((GlazePalette.Default, 1)));
        Assert.That(navigator.MenuVisibility(false), Is.False);
        Assert.That(leaves, Is.EqualTo(1), "the actual compact composition does not suspend a retained column twice");
        navigator.Expand();
        navigator.Frames(bar);
        Assert.That(navigator.MenuVisibility(true), Is.True);
        Assert.That(Glaze.Palette, Is.SameAs(GlazePalette.Default));
    }

    [Test]
    public void ColourOnlyRedrawRevokesTargetProofWhileRetainingTheUnchangedFrame()
    {
        var host = new FakeMenuHost();
        var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place,
            place => (Func<IMenuColumn>)(() => new SettingsColumn(host, Array.Empty<MenuSetting>()))));
        navigator.OpenMenu(MenuPlace.Tasks);
        var bar = new MenuBar(MenuPlace.Tasks, "Nothing waits");
        var frame = navigator.Frames(bar).Menu!;
        navigator.Drawn(MenuColumn.Menu, frame, null);
        navigator.Moved();
        Assert.That(navigator.Frames(bar).Menu, Is.SameAs(frame), "colours do not alter content, layout or frame reading evidence");
        Assert.That(navigator.Act(MenuColumn.Menu, Footer.Close, null, frame, null), Is.False, "old target proof waits for redraw");
        navigator.Drawn(MenuColumn.Menu, frame, null);
        Assert.That(navigator.Act(MenuColumn.Menu, Footer.Close, null, frame, null), Is.True);
    }
}
