using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>What the menu shows, and where each press and draw goes (ADR 0026).</summary>
[TestFixture]
public class MenuNavigatorTests
{
    /// <summary>A column that records what reaches it, and gives a new frame each time it changes, with a side panel while it has one.</summary>
    private sealed class Column : IMenuColumn
    {
        private readonly string name;
        private int version;

        public Column(string name) => this.name = name;

        public SidePanel? Side { get; set; }

        /// <summary>A prompt its footer offers at the far right, as a setting's change, or none.</summary>
        public Prompt? Offered { get; set; }

        public List<string> Got { get; } = new();

        public int Asked { get; private set; }

        public MenuFrame? Frame
        {
            get
            {
                Asked++;
                return new MenuFrame(name + " " + version, new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close), farRight: Offered),
                    lines: new[] { new PageLine("A line", action: "open", key: "k", opens: true, chosen: Side != null) }, side: Side);
            }
        }

        public event Action? Changed;

        public event Action? Closed;

        public void Change()
        {
            version++;
            Changed?.Invoke();
        }

        public void Close() => Closed?.Invoke();

        public void Act(string id, string? key)
        {
            Got.Add("act " + id + " " + key);
            // As every column does: its side panel's Close lets go of the row that opened it.
            if (id == SidePanel.Close) Side = null;
        }

        /// <summary>The footer the side panel drawn last showed, as the navigator told it.</summary>
        public Footer? SideFooter { get; private set; }

        public void Drawn(MenuFrame drawn, Footer? sidePanel)
        {
            Got.Add("drawn " + drawn.Subject + (sidePanel != null ? " side" : ""));
            if (sidePanel != null) SideFooter = sidePanel;
        }

        public void HoldStarted(string id) => Got.Add("hold " + id);

        public void HoldEnded(string id, bool letGo) => Got.Add("let go " + id + " " + letGo);

        public void Heard(string text) => Got.Add("heard " + text);

        public void Said(string words) => Got.Add("said " + words);

        public void Tick() => Got.Add("tick");

        public void FocusLeft() => Got.Add("focus left");
    }

    /// <summary>The menu, its places' columns made as it asks for them; the latest made of each is in Places.</summary>
    private static (MenuNavigator Navigator, Dictionary<MenuPlace, Column> Places) Menu()
    {
        var places = new Dictionary<MenuPlace, Column>();
        var makers = MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => places[place] = new Column(MenuBar.Word(place))));
        var navigator = new MenuNavigator(makers);
        return (navigator, places);
    }

    private static readonly MenuBar Bar = new(MenuPlace.Tasks, "1 task is waiting for you", MenuPlace.Tasks);

    /// <summary>
    /// Draws what shows as the plane would, each view reporting its frame, and the side panel of the frame
    /// in front, beside it or, with <paramref name="inPlace"/>, in its place: what a press carries.
    /// </summary>
    private static (MenuFrame? Menu, MenuFrame? Beside, SidePanel? Side) Draw(MenuNavigator menu, bool inPlace = false)
    {
        var (shown, beside) = menu.Frames(Bar);
        if (shown != null) menu.Drawn(MenuColumn.Menu, shown, null);
        if (beside != null && !menu.BesideAside) menu.Drawn(MenuColumn.File, beside, null);
        // As the plane has it: the menu's details in front of a file beside it, else the file's, else the menu's.
        var front = menu.BesideAside ? shown : beside ?? shown;
        if (front?.Side is SidePanel side) menu.Drawn(MenuColumn.Side, inPlace ? front : null, side);
        return (shown, beside, front?.Side);
    }

    private static SidePanel Details() => new("Details", facts: new[] { new SideFact("Seen", "just now") });

    [Test]
    public void EveryPlaceHasItsColumn()
    {
        var some = new Dictionary<MenuPlace, Func<IMenuColumn>> { [MenuPlace.Tasks] = () => new Column("Tasks") };
        Assert.Throws<ArgumentException>(() => _ = new MenuNavigator(some));
    }

    [Test]
    public void ClosedItShowsNoPlaceAndOpensOnTasksWhenSomethingWaitsElseWhereItWas()
    {
        var (menu, _) = Menu();
        Assert.That(menu.Frames(Bar).Menu, Is.Null, "closed, the menu is its bar");
        menu.OpenMenu(MenuPlace.Usage);
        menu.CloseMenu();
        menu.OpenMenu();
        Assert.That(menu.Place, Is.EqualTo(MenuPlace.Usage), "the place last open");
        menu.CloseMenu();
        menu.OpenMenu(somethingWaits: true);
        Assert.That(menu.Place, Is.EqualTo(MenuPlace.Tasks), "Tasks, when something waits");
    }

    [Test]
    public void APlacesFrameShowsTheMenusPlacesAndChoosingOneIsTheMenus()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Tasks);
        var shown = Draw(menu).Menu!;
        Assert.That(shown.Sections.Select(section => section.Words), Is.EqualTo(new[] { "Tasks", "Projects", "Usage", "Settings" }));
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings), shown, null);
        Assert.That(menu.Place, Is.EqualTo(MenuPlace.Settings));
        Assert.That(places[MenuPlace.Tasks].Got, Is.EqualTo(new[] { "drawn Tasks 0", "focus left" }), "choosing a place reaches no column; the place left lets go of what it armed");
        Assert.That(menu.Frames(Bar).Menu!.Subject, Does.StartWith("Settings"));
        Assert.That(places[MenuPlace.Settings].Got, Is.Empty);
    }

    [Test]
    public void EveryOtherPressGoesToTheColumnThatShowedItAndASidePanelsToTheFrameInFront()
    {
        var (menu, places) = Menu();
        var file = new Column("File") { Side = Details() };
        menu.OpenMenu(MenuPlace.Tasks);
        menu.Frames(Bar);
        places[MenuPlace.Tasks].Side = Details();
        places[MenuPlace.Tasks].Change();
        var (tasks, _, tasksSide) = Draw(menu);
        menu.Act(MenuColumn.Menu, "open", "k", tasks, null);
        menu.Act(MenuColumn.Side, SidePanel.Close, null, null, tasksSide);
        Assert.That(places[MenuPlace.Tasks].Got.Where(got => got.StartsWith("act")), Is.EqualTo(new[] { "act open k", "act " + SidePanel.Close + " " }),
            "the menu's side panel is its place's");

        menu.ShowBeside(file, "w1");
        var (_, shown, side) = Draw(menu);
        menu.Act(MenuColumn.File, Footer.Close, null, shown, null);
        menu.Act(MenuColumn.Side, SidePanel.Close, null, null, side);
        Assert.That(file.Got.Where(got => got.StartsWith("act")), Is.EqualTo(new[] { "act " + Footer.Close + " ", "act " + SidePanel.Close + " " }),
            "the file's own press, and its side panel's, now in front");
        Assert.That(menu.ColumnOf(MenuColumn.File), Is.SameAs(file));
    }

    [Test]
    public void AColumnLearnsOfADrawOnlyForTheVeryFrameItGave()
    {
        var (menu, places) = Menu();
        var file = new Column("File");
        menu.OpenMenu(MenuPlace.Tasks);
        menu.ShowBeside(file, "w1");
        var (shown, beside) = menu.Frames(Bar);
        menu.Drawn(MenuColumn.Menu, beside!, null);
        menu.Drawn(MenuColumn.File, beside!, null);
        menu.Drawn(MenuColumn.Menu, shown!, null);
        Assert.That(file.Got, Is.EqualTo(new[] { "drawn File 0" }));
        Assert.That(places[MenuPlace.Tasks].Got, Is.EqualTo(new[] { "drawn Tasks 0" }), "the place learns of its own frame, the menu's places aside");

        file.Side = Details();
        file.Change();
        menu.Drawn(MenuColumn.File, beside!, null);
        Assert.That(file.Got, Has.Count.EqualTo(1), "a frame it no longer stands by is passed over");
        var again = menu.Frames(Bar).Beside!;
        menu.Drawn(MenuColumn.Side, null, Details());
        Assert.That(file.Got, Has.Count.EqualTo(1), "a side panel it never gave is passed over");
        menu.Drawn(MenuColumn.Side, null, again.Side);
        Assert.That(file.Got.Last(), Is.EqualTo("drawn File 1 side"));
    }

    [Test]
    public void AColumnLearnsTheFooterItsSidePanelShowedBesideItsFrameOrInItsPlace()
    {
        var (menu, _) = Menu();
        var file = new Column("File") { Side = Details(), Offered = new Prompt("change", "Change", GlazeIcon.Change, safeInPlace: true) };
        menu.OpenMenu(MenuPlace.Tasks);
        menu.ShowBeside(file, "w1");

        // Beside its frame, the panel shows its own Close alone.
        Draw(menu);
        Assert.That(file.SideFooter, Is.SameAs(SidePanel.Footer));

        // In the frame's place, what the frame's footer carries there, its change with it: the footer presses on it count for.
        file.Change();
        var (_, beside, _) = Draw(menu, inPlace: true);
        var carried = beside!.Footer.InPlace(beside.Side!);
        Assert.That(file.SideFooter!.All.Select(each => each.Prompt.Id), Is.EqualTo(carried.All.Select(each => each.Prompt.Id)));
        Assert.That(file.SideFooter.All.Select(each => each.Prompt.Id), Does.Contain("change"));
        Assert.That(menu.Act(MenuColumn.Side, "change", null, null, beside.Side), Is.True, "what the column learnt it showed takes a press");
    }

    [Test]
    public void AColumnIsAskedForItsFrameOnceAChange()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Tasks);
        menu.Frames(Bar);
        menu.Frames(Bar);
        Assert.That(places[MenuPlace.Tasks].Asked, Is.EqualTo(1));
        var changes = 0;
        menu.Changed += () => changes++;
        places[MenuPlace.Tasks].Change();
        Assert.That(changes, Is.EqualTo(1));
        menu.Frames(Bar);
        Assert.That(places[MenuPlace.Tasks].Asked, Is.EqualTo(2));

        var tasks = places[MenuPlace.Tasks];
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage), Draw(menu).Menu, null);
        tasks.Change();
        Assert.That(changes, Is.EqualTo(2), "only choosing Usage: a place not shown changes nothing on the plane");
    }

    [Test]
    public void ClosingAColumnTakesItOffThePlane()
    {
        var (menu, places) = Menu();
        var file = new Column("File");
        menu.OpenMenu(MenuPlace.Usage);
        menu.Frames(Bar);
        menu.ShowBeside(file, "w1");
        file.Close();
        Assert.That((menu.Beside, menu.BesideTask), Is.EqualTo(((IMenuColumn?)null, (string?)null)), "the file's Close takes it off");
        Assert.That(menu.IsOpen, Is.True, "the menu stays");
        places[MenuPlace.Usage].Close();
        Assert.That(menu.IsOpen, Is.False, "the place's Close closes the menu to its bar");
    }

    [Test]
    public void EachOpeningMakesAPlacesColumnAfreshAndAClosedOneIsLetGo()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Projects);
        menu.Frames(Bar);
        var first = places[MenuPlace.Projects];
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage), Draw(menu).Menu, null);
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Projects), Draw(menu).Menu, null);
        menu.Frames(Bar);
        Assert.That(places[MenuPlace.Projects], Is.SameAs(first), "choosing another place and back keeps it while the menu stays open");
        menu.CloseMenu();
        menu.OpenMenu(MenuPlace.Projects);
        menu.Frames(Bar);
        Assert.That(places[MenuPlace.Projects], Is.Not.SameAs(first), "opened again, afresh");
        var second = places[MenuPlace.Projects];
        second.Close();
        menu.OpenMenu(MenuPlace.Projects);
        menu.Frames(Bar);
        Assert.That(places[MenuPlace.Projects], Is.Not.SameAs(second), "a column that closed takes nothing more; the next opening makes another");
        second.Act("ignored", null);
        menu.Act(MenuColumn.Menu, "open", "k", Draw(menu).Menu, null);
        Assert.That(places[MenuPlace.Projects].Got.Where(got => got.StartsWith("act")), Is.EqualTo(new[] { "act open k" }));
    }

    [Test]
    public void OpenColumnsTickAndLetGoWhenFocusLeaves()
    {
        var (menu, places) = Menu();
        var file = new Column("File");
        menu.ShowBeside(file, null);
        menu.Tick();
        menu.FocusLeft();
        Assert.That(file.Got, Is.EqualTo(new[] { "tick", "focus left" }), "New project beside a closed menu");
        Assert.That(places.Values.SelectMany(place => place.Got), Is.Empty, "a closed menu's places wait");
    }

    [Test]
    public void APressFromAFileSwappedForAnotherReachesNeitherAndTheFileLeavingLetsGoOfWhatItArmed()
    {
        var (menu, _) = Menu();
        var a = new Column("File A");
        var b = new Column("File B");
        menu.OpenMenu(MenuPlace.Tasks);
        menu.ShowBeside(a, "a");
        var drawnA = Draw(menu).Beside;
        menu.ShowBeside(b, "b");
        Assert.That(a.Got.Last(), Is.EqualTo("focus left"), "A's armed confirmation lapses as it leaves the plane");
        Assert.That(menu.Act(MenuColumn.File, "yes", null, drawnA, null), Is.False, "before B is drawn");
        Draw(menu);
        Assert.That(menu.Act(MenuColumn.File, "yes", null, drawnA, null), Is.False, "after B is drawn");
        Assert.That(menu.Standing(MenuColumn.File, drawnA, null), Is.Null, "a hold on A's frame records for no one");
        Assert.That(menu.Standing(MenuColumn.File, menu.Frames(Bar).Beside, null), Is.SameAs(b), "a hold on B's own frame is B's");
        Assert.That(a.Got.Concat(b.Got).Any(got => got.StartsWith("act")), Is.False, "A's yes reaches neither A nor B");
        var left = new List<IMenuColumn>();
        menu.Left += left.Add;
        menu.CloseBeside();
        Assert.That(b.Got.Last(), Is.EqualTo("focus left"), "closed, it lets go too");
        Assert.That(left, Is.EqualTo(new[] { b }), "and the director hears of it, so a voice it held stops");
    }

    [Test]
    public void AStalePressFromTasksNeverReachesAFreshUsage()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Tasks);
        var tasks = Draw(menu).Menu;
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage), tasks, null);
        Assert.That(menu.Act(MenuColumn.Menu, Footer.NextPage, null, tasks, null), Is.False, "before Usage is drawn");
        Draw(menu);
        Assert.That(menu.Act(MenuColumn.Menu, Footer.NextPage, null, tasks, null), Is.False, "after Usage is drawn");
        Assert.That(places[MenuPlace.Usage].Got.Any(got => got.StartsWith("act")), Is.False);
        Assert.That(places[MenuPlace.Tasks].Got.Any(got => got.StartsWith("act")), Is.False);
    }

    [Test]
    public void APressCountsOnlyOnceItsFrameIsDrawnAndUntilWhatShowsChanges()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Tasks);
        var given = menu.Frames(Bar).Menu;
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", given, null), Is.False, "given but not yet drawn");
        menu.Drawn(MenuColumn.Menu, given, null);
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", given, null), Is.True);
        places[MenuPlace.Tasks].Change();
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", given, null), Is.False, "the column changed: its old page no longer stands");
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", null, null), Is.False, "a press that names no frame");
        Assert.That(menu.Act(MenuColumn.Side, SidePanel.Close, null, null, Details()), Is.False, "a side panel never drawn");
        Assert.That(places[MenuPlace.Tasks].Got.Count(got => got.StartsWith("act")), Is.EqualTo(1));
        menu.CloseMenu();
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", Draw(menu).Menu, null), Is.False, "closed, the menu takes no press");
    }

    [Test]
    public void APlaneThatMovedTakesNoPressUntilItsFramesAreDrawnInTheirNewPlace()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Tasks);
        var shown = Draw(menu).Menu;
        menu.Moved();
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", shown, null), Is.False, "moved, as on Reset position");
        Assert.That(Draw(menu).Menu, Is.SameAs(shown), "the same frame, drawn again where it now stands");
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", shown, null), Is.True);
        Assert.That(places[MenuPlace.Tasks].Got.Count(got => got.StartsWith("act")), Is.EqualTo(1));
    }

    [Test]
    public void ASidePanelInItsFramesPlaceTakesOnlyItsCloseAndWhatItCarriesOfItsFrameNow()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Settings);
        menu.Frames(Bar);
        var settings = places[MenuPlace.Settings];
        settings.Side = Details();
        settings.Offered = new Prompt("change", "Make text standard", GlazeIcon.Change, main: true, safeInPlace: true);
        settings.Change();
        var (_, _, side) = Draw(menu, inPlace: true);
        Assert.That(menu.Act(MenuColumn.Side, "approve", null, null, side), Is.False, "nothing its frame doesn't offer, though the side panel stands");
        Assert.That(menu.Taking(MenuColumn.Side, "talk", null, side), Is.Null, "nor a hold its frame doesn't offer");
        Assert.That(menu.Act(MenuColumn.Side, "change", null, null, side), Is.True, "the change it offers");
        settings.Offered = new Prompt("change", "Make text standard", GlazeIcon.Change, main: true, available: false, reason: "Not now.");
        settings.Change();
        side = Draw(menu, inPlace: true).Side;
        Assert.That(menu.Act(MenuColumn.Side, "change", null, null, side), Is.False, "nor what it offers but doesn't allow now");
        Assert.That(menu.Act(MenuColumn.Side, SidePanel.Close, null, null, side), Is.True, "its own Close, always");
        settings.Side = Details();
        settings.Offered = new Prompt("change", "Make text standard", GlazeIcon.Change, main: true, safeInPlace: true);
        settings.Change();
        side = Draw(menu, inPlace: true).Side;
        menu.Moved();
        Assert.That(menu.Act(MenuColumn.Side, "change", null, null, side), Is.False, "the plane moved: nothing counts until drawn where it stands");
        Draw(menu, inPlace: true);
        Assert.That(menu.Act(MenuColumn.Side, "change", null, null, side), Is.True, "drawn again, it counts");
        Assert.That(settings.Got.Where(got => got.StartsWith("act")), Is.EqualTo(new[] { "act change ", "act " + SidePanel.Close + " ", "act change " }),
            "the change twice and Close details once, and nothing it doesn't offer");
    }

    [Test]
    public void ASidePanelTakesOnlyWhatItDrewItsCloseBesideItsFrameAndInItsPlaceWhatItCarries()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Settings);
        menu.Frames(Bar);
        var settings = places[MenuPlace.Settings];
        settings.Side = Details();
        settings.Offered = new Prompt("change", "Make text standard", GlazeIcon.Change, main: true, safeInPlace: true);
        settings.Change();
        var (shown, _, side) = Draw(menu);
        Assert.That(menu.Act(MenuColumn.Side, "change", null, null, side), Is.False, "beside its frame it shows only Close details; the change stands on the frame");
        Assert.That(menu.Act(MenuColumn.Menu, "change", null, shown, null), Is.True, "pressed on the frame, where it is drawn");

        settings.Offered = new Prompt("change", "Make text standard", GlazeIcon.Change, main: true);
        settings.Change();
        side = Draw(menu, inPlace: true).Side;
        Assert.That(menu.Act(MenuColumn.Side, "change", null, null, side), Is.False, "in its frame's place, nothing its column doesn't mark safe there, which it never carries");

        // The page's Next page beside a panel of one part would turn the page the panel hides.
        settings.Offered = new Prompt(Footer.NextPage, "Next page", GlazeIcon.Next, PromptKind.NextPage);
        settings.Change();
        side = Draw(menu, inPlace: true).Side;
        Assert.That(menu.Act(MenuColumn.Side, Footer.NextPage, null, null, side), Is.False, "paging of a page it hides");
        settings.Side = new SidePanel("Details", lines: new[] { new PageLine("Part of them") }, parts: (0, 2));
        settings.Change();
        side = Draw(menu, inPlace: true).Side;
        Assert.That(menu.Act(MenuColumn.Side, Footer.NextPage, null, null, side), Is.True, "paging of its own parts");
        // A column may give the same side panel with another frame: drawn in a frame's place that no longer stands, it counts for nothing.
        menu.Moved();
        menu.Drawn(MenuColumn.Side, new MenuFrame("Another", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)),
            lines: new[] { new PageLine("A line", action: "open", key: "k", opens: true, chosen: true) }, side: side), side);
        Assert.That(menu.Act(MenuColumn.Side, SidePanel.Close, null, null, side), Is.False, "drawn in another frame's place, it is passed over");
        Draw(menu, inPlace: true);
        Assert.That(menu.Act(MenuColumn.Side, SidePanel.Close, null, null, side), Is.True, "its own Close, either way");
        Assert.That(settings.Got.Where(got => got.StartsWith("act")), Is.EqualTo(new[] { "act change ", "act " + Footer.NextPage + " ", "act " + SidePanel.Close + " " }));
    }

    [Test]
    public void OpeningAFileBesideTheMenuLetsGoOfTheMenusChosenRowWhoseDetailsItWouldHide()
    {
        var (menu, places) = Menu();
        menu.OpenMenu(MenuPlace.Settings);
        menu.Frames(Bar);
        var settings = places[MenuPlace.Settings];
        settings.Side = Details();
        settings.Change();
        Draw(menu);
        menu.ShowBeside(new Column("File"), "w1");
        Assert.That(settings.Got.Last(), Is.EqualTo("act " + SidePanel.Close + " "), "its details close before the file takes the plane");

        var (other, otherPlaces) = Menu();
        other.OpenMenu(MenuPlace.Usage);
        other.Frames(Bar);
        other.ShowBeside(new Column("New project"), null);
        Assert.That(otherPlaces[MenuPlace.Usage].Got.Any(got => got.StartsWith("act")), Is.False, "nothing chosen, nothing let go");
    }

    [Test]
    public void TheMenusDetailsStandInFrontOfAFileBesideItWhichTakesNothingUntilItIsBack()
    {
        var (menu, places) = Menu();
        var file = new Column("File");
        menu.OpenMenu(MenuPlace.Settings);
        menu.ShowBeside(file, "w1");
        var fileFrame = Draw(menu).Beside;
        Assert.That(menu.Act(MenuColumn.File, "open", "k", fileFrame, null), Is.True, "beside the menu, the file takes its presses");

        // A setting chosen with the file beside the menu: its details take the front, the file steps aside.
        var settings = places[MenuPlace.Settings];
        settings.Side = Details();
        settings.Change();
        var (_, _, side) = Draw(menu);
        Assert.That(menu.BesideAside, Is.True);
        Assert.That(side, Is.SameAs(menu.Frames(Bar).Menu!.Side), "the side panel drawn is the menu's");
        Assert.That(menu.Act(MenuColumn.File, "open", "k", fileFrame, null), Is.False, "nothing on the file's last drawn frame counts while it stands aside");
        var read = file.Got.Count(got => got.StartsWith("drawn"));
        menu.Drawn(MenuColumn.File, menu.Frames(Bar).Beside, null);
        Assert.That(file.Got.Count(got => got.StartsWith("drawn")), Is.EqualTo(read), "a draw of it reported meanwhile counts nothing as read, since no one sees it");
        Assert.That(menu.Act(MenuColumn.File, "open", "k", menu.Frames(Bar).Beside, null), Is.False, "nor does a press on it count");
        Assert.That(menu.Taking(MenuColumn.File, "talk", menu.Frames(Bar).Beside, null), Is.Null, "nor a hold");
        Assert.That(menu.ColumnOf(MenuColumn.Side), Is.SameAs(settings), "a hold on the details ends at the menu's place, which took it");
        Assert.That(menu.Act(MenuColumn.Side, SidePanel.Close, null, null, side), Is.True);
        Assert.That(settings.Got.Last(), Is.EqualTo("act " + SidePanel.Close + " "), "its Close goes to the menu's place, whose details they are");

        // The details closed: until the plane is drawn again they still stand, so a hold on them ends at
        // the menu's place; then the file comes back and, drawn again, takes its presses.
        settings.Side = null;
        settings.Change();
        Assert.That(menu.BesideAside, Is.True, "what stands on the plane until it is drawn again");
        Assert.That(menu.ColumnOf(MenuColumn.Side), Is.SameAs(settings), "a hold on the details still ends where it started");
        var back = Draw(menu).Beside;
        Assert.That(menu.BesideAside, Is.False);
        Assert.That(menu.Act(MenuColumn.File, "open", "k", back, null), Is.True);
        Assert.That(file.Got.Count(got => got == "act open k"), Is.EqualTo(2), "once before the details, once after");
    }

    /// <summary>The menu with the first question, before the computer's first task; the latest question made is in Questions.</summary>
    private static (MenuNavigator Navigator, Dictionary<MenuPlace, Column> Places, List<Column> Questions) Asking()
    {
        var places = new Dictionary<MenuPlace, Column>();
        var questions = new List<Column>();
        var makers = MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => places[place] = new Column(MenuBar.Word(place))));
        var navigator = new MenuNavigator(makers, () =>
        {
            var question = new Column("Question");
            questions.Add(question);
            return question;
        }) { BeforeFirstTask = true };
        return (navigator, places, questions);
    }

    [Test]
    public void BeforeTheFirstTaskTheMenuOpensOnTheQuestionWithSettingsAloneUnlitAtTheRowsRightEnd()
    {
        var (menu, places, questions) = Asking();
        Assert.That(menu.Frames(Bar).Menu, Is.Null, "closed, the menu is its bar");
        menu.OpenMenu(somethingWaits: true);
        var (shown, _, _) = Draw(menu);
        Assert.That((menu.Asking, shown!.Subject), Is.EqualTo((true, "Question 0")));
        Assert.That(shown.Sections.Select(section => (section.Words, section.Chosen)), Is.EqualTo(new[] { ("Settings", false) }));
        Assert.That(shown.SectionSlots, Is.EqualTo(4), "Settings keeps the slot it has once the other places join it");
        Assert.That(places, Is.Empty, "no place's column is made");
        Assert.That(questions.Single().Got, Is.EqualTo(new[] { "drawn Question 0" }));
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", shown, null), Is.True);
        Assert.That(questions.Single().Got.Last(), Is.EqualTo("act open k"), "its presses are the question's");
        Assert.That(menu.ColumnOf(MenuColumn.Menu), Is.SameAs(questions.Single()), "and so is its held prompt");
    }

    [Test]
    public void ChoosingSettingsFromTheQuestionShowsItLitAndItsCloseGoesToTheBarWhoseOpenAsksAgain()
    {
        var (menu, places, questions) = Asking();
        menu.OpenMenu();
        var (shown, _, _) = Draw(menu);
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Tasks), shown, null);
        Assert.That(menu.Asking, Is.True, "no other place can be chosen yet");
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings), shown, null);
        Assert.That(questions.Single().Got.Last(), Is.EqualTo("focus left"), "the question left lets go of what it had armed");
        (shown, _, _) = Draw(menu);
        Assert.That((menu.Asking, menu.Place, shown!.Subject), Is.EqualTo((false, MenuPlace.Settings, "Settings 0")));
        Assert.That(shown.Sections.Select(section => (section.Words, section.Chosen)), Is.EqualTo(new[] { ("Settings", true) }));
        Assert.That(shown.SectionSlots, Is.EqualTo(4));

        places[MenuPlace.Settings].Close();
        Assert.That(menu.IsOpen, Is.False, "Close folds to the bar; there is no way back but through it");
        menu.OpenMenu();
        (shown, _, _) = Draw(menu);
        Assert.That((menu.Asking, shown!.Subject), Is.EqualTo((true, "Question 0")));
        Assert.That(questions, Has.Count.EqualTo(2), "asked afresh");
    }

    [Test]
    public void ShowMyProjectsOpensProjectsInTheQuestionsPlaceAndClosingItBringsTheQuestionBack()
    {
        var (menu, places, questions) = Asking();
        menu.OpenMenu();
        Draw(menu);
        // As the question's Show my projects does.
        menu.OpenMenu(MenuPlace.Projects);
        var (shown, _, _) = Draw(menu);
        Assert.That((menu.Asking, shown!.Subject), Is.EqualTo((false, "Projects 0")));
        Assert.That(shown.Sections.Select(section => (section.Words, section.Chosen)), Is.EqualTo(new[] { ("Settings", false) }), "nothing is lit");
        Assert.That(questions.Single().Got.Last(), Is.EqualTo("focus left"));

        places[MenuPlace.Projects].Close();
        menu.OpenMenu(somethingWaits: false);
        Assert.That(menu.Asking, Is.True, "while there is no task, the bar's Open brings back the question, not the place last open");
    }

    [Test]
    public void AColumnOpenedBeforeTheFirstTaskTakesTheMenusPlaceAndItsCloseGoesToTheBar()
    {
        var (menu, _, questions) = Asking();
        var newProject = new Column("New project");
        menu.OpenMenu();
        Draw(menu);
        menu.ShowBeside(newProject, null);
        var (shown, beside, _) = Draw(menu);
        Assert.That((menu.IsOpen, shown, beside!.Subject), Is.EqualTo((false, (MenuFrame?)null, "New project 0")), "alone, in the question's place");
        Assert.That(questions.Single().Got.Last(), Is.EqualTo("focus left"));
        newProject.Close();
        Assert.That(menu.Frames(Bar), Is.EqualTo(((MenuFrame?)null, (MenuFrame?)null)), "the bar alone");
    }

    [Test]
    public void TheFirstTaskBringsThePlacesWithSettingsStayingPutAndTheQuestionLeaves()
    {
        var (menu, _, questions) = Asking();
        menu.OpenMenu();
        Draw(menu);
        menu.BeforeFirstTask = false;
        var (shown, _, _) = Draw(menu);
        Assert.That((menu.IsOpen, menu.Asking), Is.EqualTo((true, false)), "the menu stays open");
        Assert.That(shown!.Sections.Select(section => section.Words), Is.EqualTo(new[] { "Tasks", "Projects", "Usage", "Settings" }));
        Assert.That(shown.SectionSlots, Is.EqualTo(4));
        Assert.That(questions.Single().Got.Last(), Is.EqualTo("focus left"));
        menu.CloseMenu();
        menu.OpenMenu();
        Assert.That(menu.Asking, Is.False);
    }

    [Test]
    public void APressOnTheQuestionAsDrawnIsPassedOverOnceTheMenuShowsSomethingElse()
    {
        var (menu, places, questions) = Asking();
        menu.OpenMenu();
        var (question, _, _) = Draw(menu);
        var left = new List<IMenuColumn>();
        menu.Left += left.Add;
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings), question, null);
        Assert.That(left, Is.EqualTo(new IMenuColumn[] { questions.Single() }), "the question leaving the plane stops the voice it held");
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", question, null), Is.False, "the question no longer stands");
        Draw(menu);
        Assert.That(menu.Act(MenuColumn.Menu, "open", "k", question, null), Is.False);
        Assert.That(questions.Single().Got.Where(got => got.StartsWith("act")), Is.Empty);
        Assert.That(places[MenuPlace.Settings].Got.Where(got => got.StartsWith("act")), Is.Empty, "nor reaches the place now shown");
    }

    [Test]
    public void TurningBackToBeforeTheFirstTaskWithAColumnBesideClosesTheMenuForIt()
    {
        var (menu, _, questions) = Asking();
        menu.BeforeFirstTask = false;
        var newProject = new Column("New project");
        menu.OpenMenu(MenuPlace.Tasks);
        menu.ShowBeside(newProject, null);
        menu.BeforeFirstTask = true;
        var (shown, beside, _) = Draw(menu);
        Assert.That((menu.IsOpen, shown, beside!.Subject), Is.EqualTo((false, (MenuFrame?)null, "New project 0")), "the column stands alone, in the question's place");
        Assert.That(questions, Is.Empty, "no question stands beside it");
    }

    [Test]
    public void WithoutAFirstQuestionTheMenuAlwaysShowsItsPlaces()
    {
        var (menu, _) = Menu();
        menu.BeforeFirstTask = true;
        menu.OpenMenu();
        Assert.That(Draw(menu).Menu!.Sections, Has.Count.EqualTo(4));
    }
}
