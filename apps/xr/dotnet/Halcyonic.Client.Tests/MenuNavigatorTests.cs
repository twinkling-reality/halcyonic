using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>What the menu shows, and where each press and draw goes (ADR 0026).</summary>
[TestFixture]
public class MenuNavigatorTests
{
    /// <summary>A column that records what reaches it, and gives a new frame each time it changes.</summary>
    private sealed class Column : IMenuColumn
    {
        private readonly string name;
        private int version;

        public Column(string name) => this.name = name;

        public List<string> Got { get; } = new();

        public int Asked { get; private set; }

        public MenuFrame? Frame
        {
            get
            {
                Asked++;
                return new MenuFrame(name + " " + version, new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)),
                    lines: new[] { new PageLine("A line", action: "open", key: "k", opens: true) });
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
        var shown = menu.Frames(Bar).Menu!;
        Assert.That(shown.Sections.Select(section => section.Words), Is.EqualTo(new[] { "Tasks", "Projects", "Usage", "Settings" }));
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Settings));
        Assert.That(menu.Place, Is.EqualTo(MenuPlace.Settings));
        Assert.That(places[MenuPlace.Tasks].Got, Is.Empty, "choosing a place reaches no column");
        Assert.That(menu.Frames(Bar).Menu!.Subject, Does.StartWith("Settings"));
        Assert.That(places[MenuPlace.Settings].Got, Is.Empty);
    }

    [Test]
    public void EveryOtherPressGoesToTheColumnThatShowedItAndASidePanelsToTheFrameInFront()
    {
        var (menu, places) = Menu();
        var file = new Column("File");
        menu.OpenMenu(MenuPlace.Tasks);
        menu.Act(MenuColumn.Menu, "open", "k");
        menu.Act(MenuColumn.Side, SidePanel.Close, null);
        Assert.That(places[MenuPlace.Tasks].Got, Is.EqualTo(new[] { "act open k", "act " + SidePanel.Close + " " }), "the menu's side panel is its place's");

        menu.ShowBeside(file, "w1");
        menu.Act(MenuColumn.File, Footer.Close, null);
        menu.Act(MenuColumn.Side, SidePanel.Close, null);
        Assert.That(file.Got, Is.EqualTo(new[] { "act " + Footer.Close + " ", "act " + SidePanel.Close + " " }), "the file's own press, and its side panel's");
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
        menu.Drawn(beside!, sidePanel: false);
        menu.Drawn(shown!, sidePanel: false);
        Assert.That(file.Got, Is.EqualTo(new[] { "drawn File 0" }));
        Assert.That(places[MenuPlace.Tasks].Got, Is.EqualTo(new[] { "drawn Tasks 0" }), "the place learns of its own frame, the menu's places aside");

        file.Change();
        menu.Drawn(beside!, sidePanel: true);
        Assert.That(file.Got, Has.Count.EqualTo(1), "a frame it no longer stands by is passed over");
        var again = menu.Frames(Bar).Beside!;
        menu.Drawn(again, sidePanel: true);
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
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage));
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
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Usage));
        menu.Frames(Bar);
        menu.Act(MenuColumn.Menu, MenuFrame.ChooseSection, nameof(MenuPlace.Projects));
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
        menu.Act(MenuColumn.Menu, "open", "k");
        Assert.That(places[MenuPlace.Projects].Got, Is.EqualTo(new[] { "act open k" }));
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
}
