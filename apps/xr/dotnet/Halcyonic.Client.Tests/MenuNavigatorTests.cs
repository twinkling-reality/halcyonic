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

        public List<string> Got { get; } = new();

        public int Asked { get; private set; }

        public MenuFrame? Frame
        {
            get
            {
                Asked++;
                return new MenuFrame(name + " " + version, new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)),
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

        public void Act(string id, string? key) => Got.Add("act " + id + " " + key);

        public void Drawn(MenuFrame drawn, bool sidePanel) => Got.Add("drawn " + drawn.Subject + (sidePanel ? " side" : ""));

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

    /// <summary>Draws what shows as the plane would, each view reporting its frame, and the side panel of the frame in front: what a press carries.</summary>
    private static (MenuFrame? Menu, MenuFrame? Beside, SidePanel? Side) Draw(MenuNavigator menu)
    {
        var (shown, beside) = menu.Frames(Bar);
        if (shown != null) menu.Drawn(MenuColumn.Menu, shown, null);
        if (beside != null) menu.Drawn(MenuColumn.File, beside, null);
        var side = (beside ?? shown)?.Side;
        if (side != null) menu.Drawn(MenuColumn.Side, null, side);
        return (shown, beside, side);
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
}
