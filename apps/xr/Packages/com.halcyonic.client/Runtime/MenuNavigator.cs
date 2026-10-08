#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the menu shows and where each press and draw goes (ADR 0026), engine-free, for the menu's
    /// director to run on the stage: the menu open on one of its places or closed to its bar, and the
    /// column beside it, a task's file or New project, each an <see cref="IMenuColumn"/>. Choosing a
    /// place is the director's; every other press goes to the column that showed it, its side panel's
    /// to the frame in front, and only that column's rules act on it. A draw reaches a column only for
    /// the very frame it gave, and a press only from the frame last drawn in its slot, so a press on a
    /// frame no longer standing, as a file swapped for another or a place left, reaches nothing. A
    /// place's column is made when the menu first shows it after opening, and let go when the menu
    /// closes or the column closes, so each opening starts afresh, as Tasks decides its rows then and
    /// Projects' closed column takes nothing more; a column leaving the plane lets go of what was armed.
    /// </summary>
    public sealed class MenuNavigator
    {
        private readonly IReadOnlyDictionary<MenuPlace, Func<IMenuColumn>> makers;
        private readonly Dictionary<MenuPlace, IMenuColumn> places = new Dictionary<MenuPlace, IMenuColumn>();
        private MenuFrame? placeFrame;
        private MenuFrame? menuFrame;
        private MenuFrame? besideFrame;

        /// <summary>The frames last drawn in each slot, and the side panel, while they still stand: a press comes only from one of these.</summary>
        private MenuFrame? drawnMenu;
        private MenuFrame? drawnBeside;
        private SidePanel? drawnSide;
        private IMenuColumn? drawnSideOf;

        /// <summary>
        /// The footer the side panel drawn last shows: in its frame's place, what <see cref="Footer.InPlace"/>
        /// carries of that frame's; beside it, its own Close alone. A press there counts only for what it shows.
        /// </summary>
        private Footer? drawnSideFooter;

        /// <summary>Whether the menu's details stood in front of the column beside it in the frames handed out last.</summary>
        private bool besideAside;
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

        /// <summary>A column left the plane, swapped, closed or its place left: the voice it held stops.</summary>
        public event Action<IMenuColumn>? Left;

        /// <summary>The menu is open on a place, not closed to its bar.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The place the menu shows, or showed last.</summary>
        public MenuPlace Place { get; private set; } = MenuPlace.Tasks;

        /// <summary>The column beside the menu, a task's file or New project, or null.</summary>
        public IMenuColumn? Beside { get; private set; }

        /// <summary>
        /// Anything stands on the plane: the menu, or a column beside it, with any side panel in front of
        /// either; false once everything has folded to the bar.
        /// </summary>
        public bool ShowsAnything => IsOpen || Beside != null;

        /// <summary>
        /// The menu's details stand in front of the column beside it: the menu's chosen row opened its side
        /// panel, so that column steps aside off the plane until they close, and nothing on its last drawn
        /// frame counts meanwhile. Whether the menu's details stand is as in the frames handed out last
        /// (<see cref="Frames"/>), so it holds between a change and the next draw, as what stands on the
        /// plane does; whether the menu is open and a column beside it, as now.
        /// </summary>
        public bool BesideAside => besideAside && IsOpen && Beside != null;

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

        /// <summary>Closes the menu to its bar, letting its places' columns go, each letting go of what was armed; a column beside it stays.</summary>
        public void CloseMenu()
        {
            IsOpen = false;
            foreach (var place in new List<MenuPlace>(places.Keys)) Drop(place);
            Raise();
        }

        /// <summary>
        /// Lets every place's column go, each made afresh when next shown, the menu staying where it is:
        /// as when another session shows, so no column made for the last one stays on the plane.
        /// </summary>
        public void Renew()
        {
            foreach (var place in new List<MenuPlace>(places.Keys)) Drop(place);
            Raise();
        }

        /// <summary>Lets a place's column go, and what it had armed: the next showing makes it afresh.</summary>
        private void Drop(MenuPlace place)
        {
            if (!places.TryGetValue(place, out var column)) return;
            places.Remove(place);
            Leaving(column);
        }

        /// <summary>Opens <paramref name="column"/> beside the menu in place of what stood there: a task's file, for <paramref name="task"/>, or New project.</summary>
        public void ShowBeside(IMenuColumn column, string? task)
        {
            if (Beside == column && BesideTask == task) return;
            // The menu's chosen row lets go of its side panel, which a column beside it leaves undrawn,
            // so nothing in the menu acts on details no one sees.
            if (IsOpen && places.TryGetValue(Place, out var place) && place.Frame?.Side != null) place.Act(SidePanel.Close, null);
            Leave();
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
            Leave();
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
                besideAside = IsOpen && Beside != null && placeFrame?.Side != null;
            }
            if (changed || bar != lastBar) menuFrame = placeFrame?.WithSections(bar.Sections());
            changed = false;
            lastBar = bar;
            return (menuFrame, besideFrame);
        }

        /// <summary>
        /// A press on the plane, as the view reports it, from the menu's column, the column beside it, or
        /// a side panel, which belongs to the frame in front: the column beside the menu where there is
        /// one, else the menu's place. It carries what the view showed, <paramref name="frame"/> or, from
        /// a side panel, <paramref name="side"/>, and is passed over unless that is what was drawn last
        /// in its slot and still stands. Choosing a place switches the menu; everything else goes to its
        /// column, whose own rules decide. True when it was taken.
        /// </summary>
        public bool Act(MenuColumn from, string action, string? key, MenuFrame? frame, SidePanel? side)
        {
            if (!(Taking(from, action, frame, side) is IMenuColumn column)) return false;
            if (from == MenuColumn.Menu && action == MenuFrame.ChooseSection)
            {
                if (key != null && Enum.TryParse<MenuPlace>(key, out var place) && place != Place)
                {
                    // The place left lets go of what it had armed; it keeps its page until the menu closes.
                    if (places.TryGetValue(Place, out var left)) Leaving(left);
                    Place = place;
                    Raise();
                }
                return true;
            }
            column.Act(action, key);
            return true;
        }

        /// <summary>
        /// The column whose <paramref name="frame"/>, or side panel <paramref name="side"/>, is what was
        /// drawn last in <paramref name="from"/>'s slot and still stands, as a press or a hold must come
        /// from; null for one that no longer stands.
        /// </summary>
        public IMenuColumn? Standing(MenuColumn from, MenuFrame? frame, SidePanel? side) => from switch
        {
            MenuColumn.Menu => IsOpen && frame != null && frame == drawnMenu ? PlaceColumn : null,
            MenuColumn.File => Beside != null && !BesideAside && frame != null && frame == drawnBeside ? Beside : null,
            _ => side != null && side == drawnSide ? drawnSideOf : null,
        };

        /// <summary>
        /// A view in <paramref name="from"/>'s slot drew <paramref name="frame"/> whole, or a side panel's
        /// view drew <paramref name="side"/>, which belongs to the frame in front, standing in that frame's
        /// place where <paramref name="frame"/> is given and beside it where it is null: the column that gave
        /// it learns of it, its page or its side panel, and presses on it count from now; a frame or side
        /// panel no column gave now is passed over.
        /// </summary>
        public void Drawn(MenuColumn from, MenuFrame? frame, SidePanel? side)
        {
            switch (from)
            {
                case MenuColumn.Menu:
                    // The place's column gave its frame without the menu's places; it learns of that very frame.
                    if (!IsOpen || placeFrame == null || frame == null || frame != menuFrame) return;
                    drawnMenu = frame;
                    PlaceColumn.Drawn(placeFrame, null);
                    return;
                case MenuColumn.File:
                    if (Beside == null || BesideAside || besideFrame == null || frame != besideFrame) return;
                    drawnBeside = frame;
                    Beside.Drawn(besideFrame, null);
                    return;
                default:
                    // As the plane has it: the file's side panel where a file stands, else the menu's, as
                    // when the menu's details stand in front of the file.
                    var front = BesideAside ? menuFrame : besideFrame ?? menuFrame;
                    if (side == null || front == null || side != front.Side || (frame != null && frame != front)) return;
                    drawnSide = side;
                    drawnSideFooter = frame != null ? front.Footer.InPlace(side) : SidePanel.Footer;
                    drawnSideOf = front == besideFrame ? Beside! : PlaceColumn;
                    // The column learns of the very footer presses on the panel count for.
                    drawnSideOf.Drawn(front == besideFrame ? besideFrame : placeFrame!, drawnSideFooter);
                    return;
            }
        }

        /// <summary>
        /// The column a press or a hold of <paramref name="action"/> from <paramref name="from"/> goes to:
        /// only from what was drawn last in its slot and still stands (<see cref="Standing"/>), and from a
        /// side panel only what its footer showed and allows: its own Close, and, standing in its frame's
        /// place, what it carries of that frame's; null for anything else.
        /// </summary>
        public IMenuColumn? Taking(MenuColumn from, string action, MenuFrame? frame, SidePanel? side)
        {
            if (!(Standing(from, frame, side) is IMenuColumn column)) return null;
            if (from == MenuColumn.Side && !Offers(drawnSideFooter, action)) return null;
            return column;
        }

        /// <summary>Whether <paramref name="footer"/> offers a prompt <paramref name="action"/>, available now.</summary>
        private static bool Offers(Footer? footer, string action) =>
            footer != null && footer.All.Any(each => each.Prompt.Id == action && each.Prompt.Available);

        /// <summary>The column whose held prompt, or press, came from <paramref name="from"/>.</summary>
        public IMenuColumn? ColumnOf(MenuColumn from) => from switch
        {
            MenuColumn.Menu => IsOpen ? PlaceColumn : null,
            MenuColumn.File => Beside,
            _ => BesideAside ? PlaceColumn : Beside ?? (IsOpen ? PlaceColumn : null),
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

        /// <summary>The column beside the menu leaves the plane: it is heard no more and lets go of what it had armed, as a confirmation.</summary>
        private void Leave()
        {
            if (Beside == null) return;
            Beside.Changed -= OnBesideChanged;
            Beside.Closed -= OnBesideClosed;
            Leaving(Beside);
        }

        /// <summary><paramref name="column"/> leaves the plane: what it had armed, as a confirmation, lapses, and the director hears of it.</summary>
        private void Leaving(IMenuColumn column)
        {
            column.FocusLeft();
            Left?.Invoke(column);
        }

        /// <summary>
        /// The plane moved, as on Reset position: the same frames stand, but not where they were drawn, so
        /// a press on them is passed over until they are drawn in their new place.
        /// </summary>
        public void Moved()
        {
            drawnMenu = null;
            drawnBeside = null;
            drawnSide = null;
            drawnSideOf = null;
            drawnSideFooter = null;
        }

        /// <summary>
        /// What shows changed: the frames drawn so far are no longer what the columns stand by, so a late
        /// draw of one, or a press on one, is passed over until they are drawn again.
        /// </summary>
        private void Raise()
        {
            changed = true;
            placeFrame = null;
            menuFrame = null;
            besideFrame = null;
            Moved();
            Changed?.Invoke();
        }
    }
}
