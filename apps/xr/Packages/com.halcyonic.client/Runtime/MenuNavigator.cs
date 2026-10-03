#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the menu shows and where each press and draw goes (ADR 0026), engine-free, for the menu's
    /// director to run on the stage: the menu open on one of its places or closed to its bar, and the
    /// column beside it, a task's file or New project, each an <see cref="IMenuColumn"/>. Choosing a
    /// place is the director's; every other press goes to the column that showed it, its side panel's
    /// to the frame in front, and only that column's rules act on it. A draw reaches a column only for
    /// the very frame it gave. A place's column is made when the menu first shows it after opening, and
    /// let go when the menu closes or the column closes, so each opening starts afresh, as Tasks decides
    /// its rows then and Projects' closed column takes nothing more.
    /// </summary>
    public sealed class MenuNavigator
    {
        private readonly IReadOnlyDictionary<MenuPlace, Func<IMenuColumn>> makers;
        private readonly Dictionary<MenuPlace, IMenuColumn> places = new Dictionary<MenuPlace, IMenuColumn>();
        private MenuFrame? placeFrame;
        private MenuFrame? menuFrame;
        private MenuFrame? besideFrame;
        private MenuBar? lastBar;
        private bool changed = true;

        /// <param name="places">How each of the menu's places makes its column: Tasks, Projects, Usage and Settings.</param>
        public MenuNavigator(IReadOnlyDictionary<MenuPlace, Func<IMenuColumn>> places)
        {
            foreach (var place in MenuBar.Places)
            {
                if (!places.ContainsKey(place)) throw new ArgumentException("Every place of the menu has its column: " + MenuBar.Word(place) + " has none.", nameof(places));
            }
            makers = places;
        }

        /// <summary>What shows changed: the director draws again.</summary>
        public event Action? Changed;

        /// <summary>The menu is open on a place, not closed to its bar.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The place the menu shows, or showed last.</summary>
        public MenuPlace Place { get; private set; } = MenuPlace.Tasks;

        /// <summary>The column beside the menu, a task's file or New project, or null.</summary>
        public IMenuColumn? Beside { get; private set; }

        /// <summary>The task whose file stands beside the menu, for its character's light line and the chosen row on Tasks; null for New project.</summary>
        public string? BesideTask { get; private set; }

        /// <summary>The column of the place the menu shows, made when first shown since the menu opened.</summary>
        public IMenuColumn PlaceColumn
        {
            get
            {
                if (places.TryGetValue(Place, out var column)) return column;
                var place = Place;
                column = makers[place]();
                places[place] = column;
                column.Changed += () => Touch(column);
                column.Closed += () =>
                {
                    if (places.TryGetValue(place, out var shown) && shown == column) Drop(place);
                    if (IsOpen && Place == place) CloseMenu();
                };
                return column;
            }
        }

        /// <summary>
        /// Opens the menu: on <paramref name="place"/> when given, else on Tasks when something waits for
        /// the person, else on the place last open, as the closed bar's Open does.
        /// </summary>
        public void OpenMenu(MenuPlace? place = null, bool somethingWaits = false)
        {
            Place = place ?? (somethingWaits ? MenuPlace.Tasks : Place);
            IsOpen = true;
            Raise();
        }

        /// <summary>Closes the menu to its bar, letting its places' columns go; a column beside it stays.</summary>
        public void CloseMenu()
        {
            IsOpen = false;
            foreach (var place in new List<MenuPlace>(places.Keys)) Drop(place);
            Raise();
        }

        /// <summary>Lets a place's column go: the next showing makes it afresh.</summary>
        private void Drop(MenuPlace place) => places.Remove(place);

        /// <summary>Opens <paramref name="column"/> beside the menu in place of what stood there: a task's file, for <paramref name="task"/>, or New project.</summary>
        public void ShowBeside(IMenuColumn column, string? task)
        {
            if (Beside == column && BesideTask == task) return;
            Unhook();
            Beside = column;
            BesideTask = task;
            column.Changed += OnBesideChanged;
            column.Closed += OnBesideClosed;
            besideFrame = null;
            Raise();
        }

        /// <summary>Takes the column beside the menu off the plane, as its Close does.</summary>
        public void CloseBeside()
        {
            if (Beside == null) return;
            Unhook();
            Beside = null;
            BesideTask = null;
            besideFrame = null;
            Raise();
        }

        /// <summary>
        /// The frames to draw, each asked of its column once a change: the menu's, with
        /// <paramref name="bar"/>'s places as its sections, while open, and the column's beside it. The
        /// director passes the same bar until it changes, draws these very objects, and hands them back
        /// in <see cref="Drawn"/>.
        /// </summary>
        public (MenuFrame? Menu, MenuFrame? Beside) Frames(MenuBar bar)
        {
            if (changed)
            {
                placeFrame = IsOpen ? PlaceColumn.Frame : null;
                besideFrame = Beside?.Frame;
            }
            if (changed || bar != lastBar) menuFrame = placeFrame?.WithSections(bar.Sections());
            changed = false;
            lastBar = bar;
            return (menuFrame, besideFrame);
        }

        /// <summary>
        /// A press on the plane, as the view reports it, from the menu's column, the column beside it, or
        /// a side panel, which belongs to the frame in front: the column beside the menu where there is
        /// one, else the menu's place. Choosing a place switches the menu; everything else goes to its
        /// column, whose own rules decide.
        /// </summary>
        public void Act(MenuColumn from, string action, string? key)
        {
            if (from == MenuColumn.Menu && action == MenuFrame.ChooseSection)
            {
                if (key != null && Enum.TryParse<MenuPlace>(key, out var place) && place != Place)
                {
                    Place = place;
                    Raise();
                }
                return;
            }
            ColumnOf(from)?.Act(action, key);
        }

        /// <summary>
        /// A view drew <paramref name="drawn"/> whole: the column that gave it learns of it, its page or,
        /// with <paramref name="sidePanel"/>, its side panel; a frame no column gave now is passed over.
        /// </summary>
        public void Drawn(MenuFrame drawn, bool sidePanel)
        {
            // The place's column gave its frame without the menu's places; it learns of that very frame.
            if (Beside != null && besideFrame != null && drawn == besideFrame) Beside.Drawn(drawn, sidePanel);
            else if (IsOpen && placeFrame != null && drawn == menuFrame) PlaceColumn.Drawn(placeFrame, sidePanel);
        }

        /// <summary>The column whose held prompt, or press, came from <paramref name="from"/>.</summary>
        public IMenuColumn? ColumnOf(MenuColumn from) => from switch
        {
            MenuColumn.Menu => IsOpen ? PlaceColumn : null,
            MenuColumn.File => Beside,
            _ => Beside ?? (IsOpen ? PlaceColumn : null),
        };

        /// <summary>Once a frame: every open column looks at what it awaits.</summary>
        public void Tick()
        {
            if (IsOpen) PlaceColumn.Tick();
            Beside?.Tick();
        }

        /// <summary>Another window took focus: every open column lets go of what was armed.</summary>
        public void FocusLeft()
        {
            if (IsOpen) PlaceColumn.FocusLeft();
            Beside?.FocusLeft();
        }

        private void Touch(IMenuColumn column)
        {
            if (IsOpen && places.TryGetValue(Place, out var shown) && shown == column) Raise();
        }

        private void OnBesideChanged() => Raise();

        private void OnBesideClosed() => CloseBeside();

        private void Unhook()
        {
            if (Beside == null) return;
            Beside.Changed -= OnBesideChanged;
            Beside.Closed -= OnBesideClosed;
        }

        /// <summary>What shows changed: the frames drawn so far are no longer what the columns stand by, so a late draw of one is passed over.</summary>
        private void Raise()
        {
            changed = true;
            placeFrame = null;
            menuFrame = null;
            besideFrame = null;
            Changed?.Invoke();
        }
    }
}
