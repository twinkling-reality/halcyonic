#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The menu's plane on the stage (ADR 0026): the menu, a task's file and a side panel as the columns
    /// of one composition facing the eyes, arranged by <see cref="MenuColumns"/>, placed as one panel of
    /// its size by <see cref="WorkspacePlacement"/> beside the file's character, or where the person
    /// looks, and each part laid on the plane. When what shows changes, every part slides to its new
    /// place, easing in and out (ADR 0027), so the plane re-centres as one piece and what was there shifts
    /// by half the new column's width; the menu steps aside to the left, easing in as what leaves does,
    /// and comes back from there, easing out as what arrives does. Closed with no file open, the menu is its bar. The light line joins the
    /// file's character's label to the file's subject.
    /// </summary>
    public sealed class MenuPlane : MonoBehaviour
    {
        private const float LineStart = 0.003f;
        private const float LineEnd = 0.0012f;
        private const float LineStartAlpha = 0.55f;
        private const float LineEndAlpha = 0.12f;

        /// <summary>How often the label's lowest edge is measured again: it changes only as its words do.</summary>
        private const float MeasureSeconds = 0.5f;

        private readonly Dictionary<Transform, Slide> slides = new Dictionary<Transform, Slide>();

        /// <summary>The most one frame counts toward a column's opening, so one long frame never opens it before it is seen.</summary>
        private const float OpeningFrameSeconds = 0.05f;

        /// <summary>The frames stepped so far, to know the frame a column opened in.</summary>
        private int frames;
        private readonly List<BodyInView> scratch = new List<BodyInView>();
        private readonly List<(MenuColumn Kind, MenuFrameView View)> shown = new List<(MenuColumn, MenuFrameView)>();
        private MenuFrameView menu = null!;
        private MenuFrameView file = null!;
        private MenuFrameView side = null!;

        /// <summary>The menu, the file and the side panel, made once, for stepping each frame without allocating.</summary>
        private MenuFrameView[] frameViews = null!;
        private MenuBarView bar = null!;

        /// <summary>The closed bar as laid while it stands alone, for its top edge (<see cref="BarElevation"/>).</summary>
        private PlanePart barPart;
        private LineRenderer line = null!;

        /// <summary>Covers the stage's banner's place while the menu is open or a file stands on the plane, not for the closed bar alone (<see cref="AmbientCover"/>).</summary>
        private GameObject cover = null!;
        private CharacterTarget? fileOf;
        private MenuFrameView? lineTo;
        private Vector3 eyes;

        /// <summary>How far below the eyes the surface under the characters is, in meters, as the plane was laid; null with none.</summary>
        private float? surfaceDrop;
        private float lowest = -0.5f;
        private float measured = -1f;

        /// <summary>
        /// Something on the plane was pressed: in which column, the action and its key, with the frame the
        /// view showed, or the side panel for a side panel's view, so a press on a frame no longer standing
        /// reaches nothing. A view leaving the plane, as the menu stepping aside, raises none.
        /// </summary>
        public event Action<MenuColumn, string, string?, MenuFrame?, SidePanel?>? Acted;

        /// <summary>The closed bar was pressed.</summary>
        public event Action? Opened;

        /// <summary>A column's words are laid as the plane has them; the director passes it on to the screen that counts what was read.</summary>
        public event Action<MenuColumn, MenuFrameView>? Drawn;

        /// <summary>
        /// A held prompt, as Hold to talk, started, with the frame its view showed or its side panel, as a
        /// press carries; or ended (let go on it, or dropped), which always passes, so a recording ends.
        /// </summary>
        public event Action<MenuColumn, Prompt, MenuFrame?, SidePanel?>? HoldStarted;

        public event Action<MenuColumn, Prompt, bool>? HoldEnded;

        /// <summary>Where the composition's centre stands from the eyes.</summary>
        public PanelDirection Direction { get; private set; }

        /// <summary>The composition as laid, or null while only the bar shows.</summary>
        public PlaneComposition? Composition { get; private set; }

        /// <summary>
        /// The highest the composition's parts reach as laid, in degrees from eye level, the edges of
        /// their top sides; null while it covers nothing, as with the closed bar alone. From the layout,
        /// not the parts as they slide, so it holds still while they move.
        /// </summary>
        public float? TopElevation
        {
            get
            {
                if (Composition == null || !cover.activeSelf) return null;
                var top = float.MinValue;
                var parts = Composition.Parts;
                for (var p = 0; p < parts.Count; p++)
                {
                    var part = parts[p];
                    top = Mathf.Max(top, ElevationOf(PlaneLayout.PointOf(eyes, Direction, part.Left, part.Top)),
                        ElevationOf(PlaneLayout.PointOf(eyes, Direction, part.Right + part.Width / 2f, part.Top)));
                }
                return top;
            }
        }

        /// <summary>
        /// The highest the closed bar reaches as laid while it stands alone, in degrees from eye level, the
        /// edges of its top side; null while the menu or a column stands on the plane.
        /// </summary>
        public float? BarElevation
        {
            get
            {
                if (Composition != null || bar == null || !bar.gameObject.activeInHierarchy) return null;
                var part = barPart;
                return Mathf.Max(ElevationOf(PlaneLayout.PointOf(eyes, Direction, part.Left, part.Top)),
                    ElevationOf(PlaneLayout.PointOf(eyes, Direction, part.Right + part.Width / 2f, part.Top)));
            }
        }

        private float ElevationOf(Vector3 point)
        {
            var toward = point - eyes;
            return Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>The columns that stand on the plane, left to right, with their views.</summary>
        public IReadOnlyList<(MenuColumn Kind, MenuFrameView View)> Shown => shown;

        /// <summary>The frame <paramref name="kind"/>'s view shows on the plane, or null where it shows a side panel or nothing.</summary>
        public MenuFrame? Showing(MenuColumn kind)
        {
            foreach (var (column, view) in shown)
            {
                if (column == kind) return view.Frame;
            }
            return null;
        }

        /// <summary>The frame in front as laid last, the file's where one stands, else the menu's: its side panel is the one shown.</summary>
        public MenuFrame? Front { get; private set; }

        /// <summary>
        /// Where Hold to talk's one voice stands, as the next <see cref="Show"/> draws it (ADR 0027): the column
        /// whose prompt was held, that prompt's id and the stage; null while the voice is idle.
        /// </summary>
        public (MenuColumn Kind, string Id, VoiceStage Stage)? Voice { get; set; }

        /// <summary>The menu is open but stands aside for the file.</summary>
        public bool MenuAside { get; private set; }

        /// <summary>
        /// The menu's details stand in front of the file beside it: the menu's chosen row opened its side
        /// panel, so the file steps aside off the plane, to the right, until they close (ADR 0026).
        /// </summary>
        public bool FileAside { get; private set; }

        /// <summary>Where the stage placed the composition's centre, before any drag (<see cref="Direction"/> is where it stands).</summary>
        public PanelDirection Placed { get; private set; }

        /// <summary>How much of a drag's offset the plane stands at, as laid last: what still holds of it for this composition (<see cref="MenuDrag.Kept"/>).</summary>
        public (float Yaw, float Elevation) Moved { get; private set; }

        /// <summary>
        /// The field a drag keeps to: the headset's measured field, else a Quest 3S's until it is measured,
        /// so no drag goes unbounded in the seconds before the field is read, nor where it never is.
        /// </summary>
        public static ViewField DragField => ViewField.Current ?? MenuPage.Quest3S;

        /// <summary>How far a part may move under a re-lay, in meters, before its column waits to settle again: a millimetre.</summary>
        private const float MovedMeters = 0.001f;

        /// <summary>
        /// A drag starts or ends, moving the plane: every column on it takes no new press until it settles
        /// again, and every press begun at <paramref name="since"/> or later ends, a hold with it.
        /// </summary>
        public void UnsettleShown(float since)
        {
            for (var index = 0; index < shown.Count; index++)
            {
                shown[index].View.Unsettle();
                shown[index].View.EndPressesSince(since);
            }
        }

        /// <summary>The eyes the plane was placed from, which a drag turns it round.</summary>
        public Vector3 Eyes => eyes;

        /// <summary>
        /// A file's subject was held long enough to drag the plane by, from a view on the plane: its
        /// column, the frame it showed, and the point taken hold of in the world.
        /// </summary>
        public event Action<MenuColumn, MenuFrame?, Vector3>? SubjectHeld;

        /// <summary>A file's subject was pressed, from a view on the plane: its column and the frame it showed.</summary>
        public event Action<MenuColumn, MenuFrame?>? SubjectPressed;

        /// <summary>While a subject is held: the held point moved, to here in the world.</summary>
        public event Action<Vector3>? SubjectDragged;

        /// <summary>The subject's press ended, its hold matured or not.</summary>
        public event Action? SubjectLetGo;

        /// <summary>The closed bar, while it shows.</summary>
        public MenuBarView? Bar => bar.gameObject.activeSelf ? bar : null;

        /// <summary>
        /// The longest a column newly on the plane opens: a file whose light line draws first, with sections.
        /// While a column opens (<see cref="MenuFrameView.Open"/>, ADR 0027) it takes no press, hold or subject
        /// press, so nothing on it, Yes, Send answer or Clear among them, is acted on before every part shows
        /// whole and in place. It widens ADR 0026's amendment of 2026-10-03, which held a column back from
        /// stepping aside, to every column newly on the plane. The one value the view keeps of its opening is
        /// what its parts' fade, the light line, its buttons and subject plate (which drop a press before its
        /// flash, sound or hold timer) and this plane's refusal all read, and its page counts as drawn
        /// (<see cref="Drawn"/>) only when the opening ends.
        /// </summary>
        public static float OpeningSeconds => Glaze.OpeningSeconds(3, drawn: true);

        /// <summary>Whether <paramref name="kind"/>'s column is on the plane and still opening, taking nothing yet.</summary>
        public bool Opening(MenuColumn kind)
        {
            foreach (var (each, view) in shown)
            {
                if (each == kind) return view.Opening;
            }
            return false;
        }

        /// <summary>Parts are still sliding to their places.</summary>
        public bool Sliding
        {
            get
            {
                foreach (var slide in slides.Values)
                {
                    if (slide.Progress < 1f) return true;
                }
                return false;
            }
        }

        /// <summary>How much of the light line is drawn from its character's end: less while its file opens or closes.</summary>
        public float LineShown => drawnShare;

        /// <summary>The light line's two ends, while it shows.</summary>
        public (Vector3 From, Vector3 To)? LightLine => line.gameObject.activeSelf ? (line.GetPosition(0), line.GetPosition(1)) : ((Vector3, Vector3)?)null;

        /// <param name="openAs">
        /// What the stage's banner names as still open while the plane is folded away, as a task's
        /// title or the menu's place; null while nothing is open (<see cref="AmbientCover.OpenPanel"/>).
        /// </param>
        public static MenuPlane Create(Transform parent, Func<string?>? openAs = null)
        {
            var go = new GameObject("Menu plane");
            go.transform.SetParent(parent, false);
            var plane = go.AddComponent<MenuPlane>();
            plane.cover = new GameObject("Covers the banner");
            plane.cover.transform.SetParent(go.transform, false);
            AmbientCover.Add(plane.cover, panel: true, openAs, () => plane.TopElevation, () => plane.BarElevation);
            plane.cover.SetActive(false);
            plane.menu = plane.View("Menu", MenuColumn.Menu);
            plane.file = plane.View("File", MenuColumn.File);
            // A file's subject drags the whole plane (ADR 0026).
            plane.file.EnableSubjectHold();
            plane.side = plane.View("Side panel", MenuColumn.Side);
            plane.frameViews = new[] { plane.menu, plane.file, plane.side };
            plane.bar = MenuBarView.Create(go.transform, "Menu, closed");
            plane.bar.Acted += _ => plane.Opened?.Invoke();
            plane.bar.Hide();
            plane.line = WorkspaceVisuals.Line(go.transform, "Light line", 2, 1f, loop: false);
            plane.line.widthCurve = AnimationCurve.Linear(0f, LineStart, 1f, LineEnd);
            var holo = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Strong);
            plane.line.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(holo, 0f), new GradientColorKey(holo, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(LineStartAlpha, 0f), new GradientAlphaKey(LineEndAlpha, 1f) },
            };
            plane.line.gameObject.SetActive(false);
            return plane;
        }

        private MenuFrameView View(string name, MenuColumn kind)
        {
            var view = MenuFrameView.Create(transform, name);
            view.Acted += (action, key) =>
            {
                if (Takes(view)) Acted?.Invoke(kind, action, key, view.Frame, view.Side);
            };
            view.Drawn += drawn => Drawn?.Invoke(kind, drawn);
            view.HoldStarted += prompt =>
            {
                if (Takes(view)) HoldStarted?.Invoke(kind, prompt, view.Frame, view.Side);
            };
            view.HoldEnded += (prompt, letGo) => HoldEnded?.Invoke(kind, prompt, letGo);
            view.SubjectHeld += point =>
            {
                if (Takes(view)) SubjectHeld?.Invoke(kind, view.Frame, point);
            };
            view.SubjectPressed += () =>
            {
                if (Takes(view)) SubjectPressed?.Invoke(kind, view.Frame);
            };
            view.SubjectDragged += point => SubjectDragged?.Invoke(point);
            view.SubjectLetGo += () => SubjectLetGo?.Invoke();
            view.Hide();
            return view;
        }

        /// <summary>A column takes a press, a hold or a subject press only while it stands on the plane and has opened.</summary>
        private bool Takes(MenuFrameView view) => Contains(shown, view) && !view.Opening;

        /// <summary>
        /// Shows the menu open on <paramref name="menuFrame"/>, or closed to <paramref name="menuBar"/>
        /// when it is null, and <paramref name="fileFrame"/>, a task's file, beside
        /// <paramref name="fileCharacter"/>, or no file; with the eyes at <paramref name="at"/> looking
        /// along <paramref name="looking"/>, every character on the stage in <paramref name="all"/>, and
        /// the surface they stand on at <paramref name="surfaceHeight"/>, if any. The parts slide to
        /// their places, or stand there at once when <paramref name="immediately"/>.
        /// </summary>
        /// <param name="moved">How far a drag left the plane from where the stage places it (<see cref="MenuDrag"/>).</param>
        /// <param name="fileColumn">
        /// What the file belongs to, as a task's file or New project: another in the file's place opens anew,
        /// as a file newly on the plane does, though the view stands where it stood.
        /// </param>
        /// <param name="besideWindow">
        /// The characters stand either side of a window straight ahead (along <paramref name="looking"/>):
        /// the plane opens centred under it, a file not turned toward its character, the menu and a file
        /// one at a time, and no light line, which would run across the window.
        /// </param>
        public void Show(MenuBar menuBar, MenuFrame? menuFrame, MenuFrame? fileFrame, CharacterTarget? fileCharacter, IReadOnlyList<CharacterTarget> all,
            Vector3 at, Vector3 looking, float? surfaceHeight, bool immediately = false, bool besideWindow = false, (float Yaw, float Elevation) moved = default,
            object? fileColumn = null)
        {
            eyes = at;
            surfaceDrop = surfaceHeight.HasValue ? at.y - surfaceHeight.Value : (float?)null;
            menu.Voice = Voice is (MenuColumn.Menu, string menuHeld, VoiceStage menuStage) ? (menuHeld, menuStage) : null;
            file.Voice = Voice is (MenuColumn.File, string fileHeld, VoiceStage fileStage) ? (fileHeld, fileStage) : null;
            // The menu's details take the front over a file beside it: laid as if no file stood there, the
            // file stepping aside until they close, so what the person just chose is what they see.
            var wasFileAside = FileAside;
            FileAside = menuFrame?.Side != null && fileFrame != null;
            if (FileAside)
            {
                fileFrame = null;
                fileCharacter = null;
            }
            var lineWas = fileOf != null && lineTo != null && lineTo.gameObject.activeSelf ? (lineTo, fileOf) : ((MenuFrameView, CharacterTarget)?)null;
            fileOf = fileFrame != null && !besideWindow ? fileCharacter : null;
            measured = -1f;
            SeeStage(all);
            var panel = fileFrame != null ? fileFrame.Side : menuFrame?.Side;
            Front = fileFrame ?? menuFrame;
            // Standing in its frame's place, a side panel is as wide as that frame, so the footer it carries fits.
            var frontDegrees = fileFrame != null ? Glaze.Menu.FileColumnDegrees : Glaze.Menu.MenuColumnDegrees;
            var pill = fileFrame?.Pill != null;
            var zoom = GlazeText.Scale;
            var wasAside = MenuAside;
            var before = new List<(MenuColumn, MenuFrameView)>(shown);
            var wasLaid = (MenuColumn.Menu, MenuColumn.Menu, MenuColumn.Menu, before.Count);
            if (before.Count > 0) wasLaid.Item1 = before[0].Item1;
            if (before.Count > 1) wasLaid.Item2 = before[1].Item1;
            if (before.Count > 2) wasLaid.Item3 = before[2].Item1;
            // The layout is decided at the drag's offset; where only part of it holds for what is laid, it is decided
            // again at the part kept, so what stands beside what is judged where the plane will stand.
            var offset = moved;
            var inPlace = false;
            for (var pass = 0; ; pass++)
            {
                // With text a step larger, a frame and its side panel are too wide for a Quest 3S together;
                // and wherever the two would not fit as the stage places them, as beside a window, the side
                // panel takes its frame's place all the same.
                inPlace = panel != null && (zoom > 1f || !FitWithSide(menuFrame, fileFrame, panel, pill, all, looking, surfaceHeight, besideWindow, offset));

                // Whether the menu and the file fit side by side, their subjects level.
                var fits = !besideWindow;
                if (fits && menuFrame != null && fileFrame != null)
                {
                    var level = Mathf.Max(MenuFrameView.SubjectHeight(menuFrame.Subject, Glaze.Menu.MenuColumnDegrees, pill),
                        MenuFrameView.SubjectHeight(fileFrame.Subject, Glaze.Menu.FileColumnDegrees, pill));
                    menu.Show(menuFrame, Glaze.Menu.MenuColumnDegrees, level, pill);
                    file.Show(fileFrame, Glaze.Menu.FileColumnDegrees, level, pill);
                    var beside = new PlaneComposition(new[] { ColumnOf(menu), ColumnOf(file) }, zoom);
                    fits = MenuPage.Fits(beside);
                    // Where the stage would place the two under its labels, moved as far as the drag holds,
                    // inside the headset's measured field and clear of every character.
                    if (fits && ViewField.Current is ViewField field && fileCharacter != null)
                    {
                        var placed = WorkspaceLayout.Place(fileCharacter, all, eyes, looking, surfaceHeight, scratch, beside.Size).Direction;
                        var dragged = MenuDrag.Turned(placed, offset);
                        fits = placed.Clear && MenuPage.Inside(beside, dragged, field) && WorkspacePlacement.Clears(dragged, bodies, beside.Size);
                    }
                }
                var kinds = MenuColumns.Arrange(menuFrame != null, fileFrame != null, panel != null, fits, inPlace);
                MenuAside = MenuColumns.MenuAside(menuFrame != null, fileFrame != null, panel != null, fits);

                // Every column's subject the tallest of them, a pill's room kept where a file shows.
                var subject = 0f;
                foreach (var kind in kinds)
                {
                    var (words, degrees) = kind switch
                    {
                        MenuColumn.Menu => (menuFrame!.Subject, Glaze.Menu.MenuColumnDegrees),
                        MenuColumn.File => (fileFrame!.Subject, Glaze.Menu.FileColumnDegrees),
                        _ => (panel!.Subject, inPlace ? frontDegrees : Glaze.Menu.SideColumnDegrees),
                    };
                    subject = Mathf.Max(subject, MenuFrameView.SubjectHeight(words, degrees, pill));
                }

                shown.Clear();
                foreach (var kind in kinds)
                {
                    var view = kind switch { MenuColumn.Menu => menu, MenuColumn.File => file, _ => side };
                    if (kind == MenuColumn.Menu) menu.Show(menuFrame!, Glaze.Menu.MenuColumnDegrees, subject, pill);
                    else if (kind == MenuColumn.File) file.Show(fileFrame!, Glaze.Menu.FileColumnDegrees, subject, pill);
                    // In the file's place, the side panel wears its pill and keeps its light line.
                    // In its frame's place it also carries the frame's footer and reason, so nothing the frame offers is lost.
                    else side.Show(panel!, inPlace ? frontDegrees : Glaze.Menu.SideColumnDegrees, subject, pill, inPlace ? fileFrame?.Pill : null,
                        inPlace ? Front : null);
                    shown.Add((kind, view));
                }
                lineTo = fileOf == null ? null : Contains(shown, file) ? file : inPlace ? side : null;
                lineColumn = -1;
                for (var index = 0; index < shown.Count; index++)
                {
                    if (shown[index].View == lineTo) lineColumn = index;
                }
                if (shown.Count == 0) break;

                var columns = new List<PlaneColumn>();
                foreach (var (_, view) in shown) columns.Add(ColumnOf(view));
                Composition = new PlaneComposition(columns, zoom);
                Placed = besideWindow ? WorkspaceLayout.PlaceAhead(all, eyes, looking, surfaceHeight, scratch, Composition.Size).Direction
                    : fileOf != null ? WorkspaceLayout.Place(fileOf, all, eyes, looking, surfaceHeight, scratch, Composition.Size).Direction
                    : WorkspaceLayout.PlaceForeground(all, eyes, looking, surfaceHeight, scratch, Composition.Size).Direction;
                // A drag's offset holds only as far as every rule of a drag still does for this composition.
                var laid = Composition;
                Moved = MenuDrag.Kept(offset, kept => Allows(laid, MenuDrag.Turned(Placed, kept)));
                // At most three passes: should the third still keep less, the plane stands at the part kept,
                // with the layout chosen at the pass before. That part is one every rule allows for what is laid,
                // unless it is none: Kept never judges the placement itself, which the stage chose, not the drag.
                if (Moved == offset || pass == 2) break;
                offset = Moved;
            }
            // What the drag itself changed under the hand, never a re-lay that follows the stage: less of it
            // kept than was dragged, or another layout standing where it was.
            var laidNow = (MenuColumn.Menu, MenuColumn.Menu, MenuColumn.Menu, shown.Count);
            if (shown.Count > 0) laidNow.Item1 = shown[0].Kind;
            if (shown.Count > 1) laidNow.Item2 = shown[1].Kind;
            if (shown.Count > 2) laidNow.Item3 = shown[2].Kind;
            var dragMoved = moved != default && (Moved != moved || laidNow != wasLaid);
            // Coming back from stepping aside, the menu or the file takes no press as it slides in: its
            // buttons wait to settle again, though their words are as they were.
            foreach (var (kind, view) in shown)
            {
                if ((kind == MenuColumn.Menu && wasAside) || (kind == MenuColumn.File && wasFileAside)) view.Unsettle();
            }

            if (shown.Count == 0)
            {
                // Closed with no file: the bar alone, where the person looks, which leaves the banner be.
                cover.SetActive(false);
                Composition = null;
                // What was open fades away together, the light line drawing back to its character; the bar shows at once.
                foreach (var (_, view) in before)
                {
                    if (immediately) view.Hide();
                    else view.Close();
                }
                lineTo = null;
                LeaveLine(lineWas, immediately);
                bar.Show(menuBar, Glaze.Menu.MenuColumnDegrees);
                var barSize = new PanelSize(PlaneComposition.Distance, bar.Size.x * zoom / 2f * PlaneComposition.Distance, bar.Size.y * zoom / 2f * PlaneComposition.Distance);
                Placed = (besideWindow ? WorkspaceLayout.PlaceAhead(all, eyes, looking, surfaceHeight, scratch, barSize)
                    : WorkspaceLayout.PlaceForeground(all, eyes, looking, surfaceHeight, scratch, barSize)).Direction;
                // Only a file's subject drags the plane; the bar alone stands where the stage puts it.
                Moved = default;
                Direction = Placed;
                var placedBar = new PlanePart(0, 0, bar.Size.x * zoom, bar.Size.y * zoom, 0f, 0f);
                barPart = placedBar;
                SlideTo(bar.transform, placedBar, zoom, null, true);
                UpdateLightLine();
                return;
            }
            bar.Hide();
            cover.SetActive(true);
            Direction = MenuDrag.Turned(Placed, Moved);

            for (var c = 0; c < shown.Count; c++)
            {
                var (kind, view) = shown[c];
                var parts = view.Parts;
                var index = 0;
                var unsettled = false;
                // Newly on the plane, as opened or back from stepping aside, or another task's file or New project in
                // the file's place: it opens, taking nothing until every part shows whole, its page counted drawn only
                // then. A file's light line draws from its character first.
                if (!WasShown(before, view) || (kind == MenuColumn.File && fileColumn != shownFileColumn)) view.Open(fromLine: view == lineTo && kind == MenuColumn.File, frames);
                foreach (var placed in Composition.Parts)
                {
                    if (placed.Column != c) continue;
                    // The menu coming back slides in from where it stepped aside to, and the file from its side.
                    Vector3? from = kind == MenuColumn.Menu && wasAside ? Aside(placed, zoom)
                        : kind == MenuColumn.File && wasFileAside ? Aside(placed, zoom, toTheRight: true) : (Vector3?)null;
                    // A column the drag moves, as when a drag let go keeps less of it, or one that slides to a new place
                    // because the columns beside it changed, as a side panel opening beside a file, takes no new press
                    // until it settles again, longer than the slide; a re-lay that follows the stage, as the characters
                    // bob, never does.
                    if ((dragMoved || laidNow != wasLaid) && from == null && !unsettled && WasShown(before, view)
                        && Vector3.Distance(parts[index].position, PlaneLayout.PointOf(eyes, Direction, placed.Right, placed.Up)) > MovedMeters)
                    {
                        view.Unsettle();
                        unsettled = true;
                    }
                    SlideTo(parts[index], placed, zoom, from, immediately || !WasShown(before, view));
                    if (index == parts.Count - 1) view.Settle(placed, zoom);
                    index++;
                }
            }
            shownFileColumn = Contains(shown, file) ? fileColumn : null;
            foreach (var (kind, view) in before)
            {
                if (Contains(shown, view) || !view.gameObject.activeSelf) continue;
                if (kind == MenuColumn.Menu && MenuAside && !immediately)
                {
                    // Steps aside: slides left by its width and the gap, off the plane, then hides.
                    foreach (var part in view.Parts) SlideAway(part, Vector3.left);
                }
                else if (kind == MenuColumn.File && FileAside && !immediately)
                {
                    // Steps aside for the menu's details: slides right, off the plane, then hides.
                    foreach (var part in view.Parts) SlideAway(part, Vector3.right);
                }
                // Leaving the plane otherwise, it fades away; the light line draws back to its character with it.
                else if (immediately) view.Hide();
                else view.Close();
            }
            foreach (var view in frameViews)
            {
                // Stepping aside, it stands whole as it slides away, taking nothing.
                if (!Contains(shown, view) && view.Opening) view.Stop();
            }
            LeaveLine(lineWas, immediately);
            // A view laid only to measure, as the menu beside a file too tall for it, never shows; one still closing fades on.
            foreach (var view in frameViews)
            {
                if (!Contains(shown, view) && !Contains(before, view) && !view.Closing) view.Hide();
            }
            Advance(0f);
        }

        /// <summary>
        /// A drag: the plane stands <paramref name="moved"/> from where the stage placed it, every part
        /// there at once, as laid; its words are not laid again.
        /// </summary>
        public void Turn((float Yaw, float Elevation) moved)
        {
            if (Composition == null) return;
            Direction = MenuDrag.Turned(Placed, moved);
            var zoom = Composition.Zoom;
            var all = Composition.Parts;
            // Indexed, never through an interface's enumerator, which Mono and IL2CPP box: this runs every frame of a drag.
            for (var c = 0; c < shown.Count; c++)
            {
                var parts = shown[c].View.Parts;
                var index = 0;
                for (var p = 0; p < all.Count; p++)
                {
                    var placed = all[p];
                    if (placed.Column != c) continue;
                    Stand(parts[index], placed, zoom);
                    index++;
                }
            }
            UpdateLightLine();
        }

        /// <summary>A part at its place at once, any slide it had dropped, allocating nothing: a drag moves it every frame.</summary>
        private void Stand(Transform part, PlanePart placed, float zoom)
        {
            slides.Remove(part);
            part.SetPositionAndRotation(PlaneLayout.PointOf(eyes, Direction, placed.Right, placed.Up), PlaneLayout.Facing(Direction));
            part.localScale = Vector3.one * (PlaneComposition.Distance * zoom);
        }

        /// <summary>Whether the frame in front and its side panel fit together where the stage would place them, in the headset's measured field.</summary>
        private bool FitWithSide(MenuFrame? menuFrame, MenuFrame? fileFrame, SidePanel panel, bool pill, IReadOnlyList<CharacterTarget> all, Vector3 looking,
            float? surfaceHeight, bool besideWindow, (float Yaw, float Elevation) moved)
        {
            if (!(ViewField.Current is ViewField field)) return true;
            var front = fileFrame ?? menuFrame;
            if (front == null) return true;
            var frontView = fileFrame != null ? file : menu;
            var degrees = fileFrame != null ? Glaze.Menu.FileColumnDegrees : Glaze.Menu.MenuColumnDegrees;
            var level = Mathf.Max(MenuFrameView.SubjectHeight(front.Subject, degrees, pill), MenuFrameView.SubjectHeight(panel.Subject, Glaze.Menu.SideColumnDegrees, pill));
            frontView.Show(front, degrees, level, pill);
            side.Show(panel, Glaze.Menu.SideColumnDegrees, level, pill);
            var together = new PlaneComposition(new[] { ColumnOf(frontView), ColumnOf(side) }, GlazeText.Scale);
            var direction = besideWindow ? WorkspaceLayout.PlaceAhead(all, eyes, looking, surfaceHeight, scratch, together.Size).Direction
                : fileOf != null ? WorkspaceLayout.Place(fileOf, all, eyes, looking, surfaceHeight, scratch, together.Size).Direction
                : WorkspaceLayout.PlaceForeground(all, eyes, looking, surfaceHeight, scratch, together.Size).Direction;
            // Where the person dragged the plane, as far as they did.
            var dragged = MenuDrag.Turned(direction, moved);
            return direction.Clear && MenuPage.Inside(together, dragged, field) && WorkspacePlacement.Clears(dragged, bodies, together.Size);
        }

        /// <summary>The characters as seen from the eyes the plane is laid from: every body, and each label as laid, the file's own marked.</summary>
        private void SeeStage(IReadOnlyList<CharacterTarget> all)
        {
            bodies.Clear();
            standing.Clear();
            var across = Vector3.zero;
            for (var index = 0; index < all.Count; index++)
            {
                var character = all[index];
                if (character == null) continue;
                var body = WorkspaceLayout.InView(character, eyes);
                bodies.Add(body);
                standing.Add((body, LabelOutline(character.View), character == fileOf));
                var toward = character.BodyPosition - eyes;
                across += new Vector3(toward.x, 0f, toward.z).normalized;
            }
            StageYaw = bodies.Count > 0 ? Mathf.Atan2(across.x, across.z) * Mathf.Rad2Deg : (float?)null;
        }

        /// <summary>
        /// Whether the plane, laid as it is now, may stand centred at <paramref name="at"/>: what a drag keeps
        /// to, inside the headset's measured field, clear of every character's body and label, and with its
        /// light line crossing no label or body as the eyes see them. Judged from the geometry alone, moving nothing.
        /// </summary>
        public bool Allows(PanelDirection at) => Composition != null && Allows(Composition, at);

        private bool Allows(PlaneComposition composition, PanelDirection at)
        {
            var field = DragField;
            // A drag's own band (PanelDrag): no higher than placement goes, no lower than the field allows, or
            // than where the stage placed it if that is lower, nor into a surface under the characters.
            var floor = Mathf.Min(WorkspacePlacement.Lowest(composition.Size, field), Placed.Elevation);
            if (surfaceDrop is float drop) floor = Mathf.Max(floor, WorkspacePlacement.LowestAboveSurface(composition.Size, drop));
            if (at.Elevation > WorkspacePlacement.HighestDegrees + 1e-3f || at.Elevation < Mathf.Min(floor, WorkspacePlacement.HighestDegrees) - 1e-3f) return false;
            if (!MenuPage.Inside(composition, at, field)) return false;
            if (StageYaw is float stage && !MenuDrag.WithinReach(at, stage, field)) return false;
            if (!WorkspacePlacement.Clears(at, bodies, composition.Size)) return false;
            return !(LineAt(composition, at) is (Vector3 from, Vector3 to)) || !Crosses(from, to);
        }

        /// <summary>The light line as it would run with <paramref name="composition"/> centred at <paramref name="at"/>, as <see cref="UpdateLightLine"/> draws it; null with none.</summary>
        private (Vector3 From, Vector3 To)? LineAt(PlaneComposition composition, PanelDirection at)
        {
            if (fileOf == null || lineTo == null || lineColumn < 0) return null;
            PlanePart? first = null;
            PlanePart? last = null;
            var parts = composition.Parts;
            for (var index = 0; index < parts.Count; index++)
            {
                if (parts[index].Column != lineColumn) continue;
                first ??= parts[index];
                last = parts[index];
            }
            if (!(first is PlanePart subject) || !(last is PlanePart content)) return null;
            var rotation = PlaneLayout.Facing(at);
            var scale = PlaneComposition.Distance * composition.Zoom;
            Vector3 Point(PlanePart part, float x, float y) => PlaneLayout.PointOf(eyes, at, part.Right, part.Up) + rotation * (new Vector3(x, y, 0f) * scale);
            var half = lineTo.Width / 2f;
            if (at.Above)
            {
                var top = fileOf.RestPosition + Vector3.up * (CharacterView.BodyExtent * fileOf.Scale);
                var bottom = -lineTo.Content.Size.y / 2f;
                return Joined(top, top, Point(content, -half, bottom), Point(content, half, bottom));
            }
            var (plate, covered) = OwnLabel();
            return Joined(plate.TransformPoint(new Vector3(-0.5f, covered.yMin, 0f)), plate.TransformPoint(new Vector3(0.5f, covered.yMin, 0f)),
                Point(subject, -half, lineTo.PlateTop), Point(subject, half, lineTo.PlateTop));
        }

        /// <summary>The file's own character's label, as seen when the plane was laid.</summary>
        private (Transform Plate, Rect Covered) OwnLabel()
        {
            for (var index = 0; index < standing.Count; index++)
            {
                if (standing[index].Own) return standing[index].Label;
            }
            return LabelOutline(fileOf!.View);
        }

        /// <summary>Whether a light line from <paramref name="from"/> to <paramref name="to"/> passes a character's label or body as the eyes see them, as the renders check it.</summary>
        private bool Crosses(Vector3 from, Vector3 to)
        {
            for (var step = 1; step < 40; step++)
            {
                var point = Vector3.Lerp(from, to, step / 40f);
                var toward = point - eyes;
                var yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                var up = Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
                for (var index = 0; index < standing.Count; index++)
                {
                    var (body, label, own) = standing[index];
                    if (OnLabel(label, eyes, point)) return true;
                    // The file's own body is where the line starts over a desk, so only the others count.
                    if (!own && Mathf.Abs(Mathf.DeltaAngle(body.Yaw, yaw)) <= body.Radius + LineMarginDegrees
                        && Mathf.Abs(up - body.Elevation) <= body.Radius + LineMarginDegrees) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Where the plane's top line stands below eye level for a file of
        /// <paramref name="character"/>, or where the person looks without one: under the stage's labels,
        /// as the plane would be placed, for <see cref="MenuPage.Height"/> to pack a page for this stage.
        /// Over a desk, where the plane stands above the characters and grows upward, the reference line.
        /// </summary>
        /// <param name="besideMenu">For the menu and a file side by side, whose wider plane stands lower under the same labels.</param>
        public static float TopLine(CharacterTarget? character, IReadOnlyList<CharacterTarget> all, Vector3 eyes, Vector3 looking, float? surfaceHeight,
            bool besideWindow = false, bool besideMenu = false)
        {
            var page = MenuPage.Content(MenuPage.Rows(MenuFrame.RowsAPage(GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard, sourceLine: false)));
            PlaneColumn Column(float degrees) => new PlaneColumn(PlaneComposition.Units(degrees), MenuPage.Subject(1, pill: true), MenuPage.Sections, page);
            var columns = besideMenu ? new[] { Column(Glaze.Menu.MenuColumnDegrees), Column(Glaze.Menu.FileColumnDegrees) } : new[] { Column(Glaze.Menu.FileColumnDegrees) };
            var size = new PlaneComposition(columns, GlazeText.Scale).Size;
            var scratch = new List<BodyInView>();
            var direction = besideWindow ? WorkspaceLayout.PlaceAhead(all, eyes, looking, surfaceHeight, scratch, size).Direction
                : character != null ? WorkspaceLayout.Place(character, all, eyes, looking, surfaceHeight, scratch, size).Direction
                : WorkspaceLayout.PlaceForeground(all, eyes, looking, surfaceHeight, scratch, size).Direction;
            return direction.Above ? MenuPage.TopDegrees : -(direction.Elevation + size.HalfHeightDegrees);
        }

        /// <summary>Moves every sliding part and every column's opening on by <paramref name="seconds"/>, and the light line with them; the renders step it themselves.</summary>
        public void Advance(float seconds) => Step(seconds, frame: false);

#if UNITY_EDITOR
        /// <summary>For the editor's renders: one frame of <paramref name="seconds"/>, as the headset steps it.</summary>
        public void FrameForRender(float seconds) => Step(seconds, frame: true);
#endif

        /// <summary>
        /// Moves the plane on by <paramref name="seconds"/>. A <paramref name="frame"/> of the headset's skips a
        /// column that opened in it and counts at most <see cref="OpeningFrameSeconds"/> toward an opening.
        /// </summary>
        private void Step(float seconds, bool frame)
        {
            var toward = frame ? Mathf.Min(seconds, OpeningFrameSeconds) : seconds;
            for (var index = 0; index < frameViews.Length; index++)
            {
                var view = frameViews[index];
                if (view.Opening && !(frame && view.OpenedIn == frames)) view.Step(toward);
                else if (view.Closing) view.Step(seconds);
            }
            if (frame) frames++;
            foreach (var pair in slides)
            {
                var part = pair.Key;
                var slide = pair.Value;
                if (part == null) continue;
                slide.Progress = slide.Seconds <= 0f ? 1f : Mathf.Clamp01(slide.Progress + seconds / slide.Seconds);
                var eased = slide.Eased();
                part.SetPositionAndRotation(Vector3.Lerp(slide.From, slide.To, eased), Quaternion.Slerp(slide.FromRotation, slide.ToRotation, eased));
                part.localScale = Vector3.one * Mathf.Lerp(slide.FromScale, slide.ToScale, eased);
                if (slide.Progress >= 1f && slide.HideAtEnd) part.parent.gameObject.SetActive(false);
            }
            UpdateLightLine();
        }

        private void LateUpdate() => Step(Time.unscaledDeltaTime, frame: true);

        private static PlaneColumn ColumnOf(MenuFrameView view) => new PlaneColumn(view.Width, ToArray(view.Heights));

        private static float[] ToArray(IReadOnlyList<float> heights)
        {
            var array = new float[heights.Count];
            for (var index = 0; index < heights.Count; index++) array[index] = heights[index];
            return array;
        }

        private static bool WasShown(List<(MenuColumn Kind, MenuFrameView View)> before, MenuFrameView view) => Contains(before, view) && view.gameObject.activeSelf;

        private static bool Contains(List<(MenuColumn Kind, MenuFrameView View)> columns, MenuFrameView view)
        {
            foreach (var (_, each) in columns)
            {
                if (each == view) return true;
            }
            return false;
        }

        /// <summary>Where a part stood aside: its place on the plane, left, or for a file to the right, by its column's width and the gap.</summary>
        private Vector3 Aside(PlanePart placed, float zoom, bool toTheRight = false)
        {
            var shift = placed.Width + PlaneComposition.ColumnGapMeters / PlaneComposition.Distance * zoom;
            return PlaneLayout.PointOf(eyes, Direction, placed.Right + (toTheRight ? shift : -shift), placed.Up);
        }

        private void SlideTo(Transform part, PlanePart placed, float zoom, Vector3? from, bool immediately)
        {
            var position = PlaneLayout.PointOf(eyes, Direction, placed.Right, placed.Up);
            var rotation = PlaneLayout.Facing(Direction);
            var scale = PlaneComposition.Distance * zoom;
            if (from is Vector3 start)
            {
                part.SetPositionAndRotation(start, rotation);
                part.localScale = Vector3.one * scale;
                immediately = false;
            }
            slides[part] = immediately
                ? new Slide(position, rotation, scale, position, rotation, scale) { Progress = 1f }
                : new Slide(part.position, part.rotation, part.localScale.x, position, rotation, scale) { Arrives = from != null };
            if (immediately)
            {
                part.SetPositionAndRotation(position, rotation);
                part.localScale = Vector3.one * scale;
            }
        }

        private void SlideAway(Transform part, Vector3 direction)
        {
            var width = part.parent.TryGetComponent<MenuFrameView>(out var view) ? view.Width * part.localScale.x : 0f;
            var gap = PlaneComposition.ColumnGapMeters * part.localScale.x / PlaneComposition.Distance;
            var to = part.position + part.rotation * direction * (width + gap);
            slides[part] = new Slide(part.position, part.rotation, part.localScale.x, to, part.rotation, part.localScale.x) { HideAtEnd = true };
        }

        /// <summary>
        /// The light line (ADR 0026): one leg from under the file's character's label to the file's
        /// subject plate's top edge. Where the label stands over the plate, as the eyes see them, it drops
        /// straight down from the middle of their overlap; else it joins the label's nearer bottom corner to
        /// the plate's nearer top corner. Leaving from under the label's lowest part, below any mark, it
        /// crosses no words. Where the plane stands above the characters, as over a desk, it rises the same
        /// way from the top of the character's body to the file's bottom edge, so it crosses neither the
        /// label under the body nor the file's page.
        /// </summary>
        private void UpdateLightLine()
        {
            var to = lineTo != null && lineTo.gameObject.activeSelf ? lineTo : null;
            var of = fileOf;
            // From its character as the file opens; back to it as the file closes; whole otherwise.
            drawnShare = to == null ? 1f : to.Opening && to.DrawsFromLine ? Glaze.LineShown(to.MovedFor) : 1f;
            if (to == null && leavingLine is (MenuFrameView leaving, CharacterTarget leftFrom) && leaving.Closing && leftFrom != null)
            {
                to = leaving;
                of = leftFrom;
                drawnShare = Glaze.LineLeft(leaving.MovedFor);
            }
            if (of == null || to == null || drawnShare <= 0f)
            {
                line.gameObject.SetActive(false);
                return;
            }
            var subject = to.Subject;
            if (Direction.Above)
            {
                var content = to.Parts[to.Parts.Count - 1];
                var bottom = -to.Content.Size.y / 2f;
                var top = of.BodyPosition + Vector3.up * (CharacterView.BodyExtent * of.Scale);
                Join(top, top, content.TransformPoint(new Vector3(-to.Width / 2f, bottom, 0f)), content.TransformPoint(new Vector3(to.Width / 2f, bottom, 0f)));
                return;
            }
            var plate = of.View.Label.Plate.transform;
            if (measured < 0f || Time.unscaledTime - measured > MeasureSeconds)
            {
                measured = Time.unscaledTime;
                lowest = LabelOutline(of.View).Covered.yMin;
            }
            Join(plate.TransformPoint(new Vector3(-0.5f, lowest, 0f)), plate.TransformPoint(new Vector3(0.5f, lowest, 0f)),
                subject.TransformPoint(new Vector3(-to.Width / 2f, to.PlateTop, 0f)), subject.TransformPoint(new Vector3(to.Width / 2f, to.PlateTop, 0f)));
        }

        /// <summary>
        /// A character's label as laid on its plate: the plate, and the rectangle its parts cover in the
        /// plate's own space, the plate itself included, so what falls on it as the eyes see it can be told
        /// exactly (<see cref="OnLabel"/>): a flat label's bottom edge rises toward its ends as seen from
        /// below, which a box round it in angles misses.
        /// </summary>
        public static (Transform Plate, Rect Covered) LabelOutline(CharacterView view)
        {
            var plate = view.Label.Plate.transform;
            float left = -0.5f, right = 0.5f, bottom = -0.5f, top = 0.5f;
            // Into a list kept for it, so measuring again allocates nothing.
            view.Label.GetComponentsInChildren(false, labelFilters);
            for (var index = 0; index < labelFilters.Count; index++)
            {
                var filter = labelFilters[index];
                if (filter.sharedMesh == null || !filter.TryGetComponent<Renderer>(out var drawn) || !drawn.enabled) continue;
                var bounds = filter.sharedMesh.bounds;
                for (var corner = 0; corner < 4; corner++)
                {
                    var local = plate.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        corner % 2 == 0 ? bounds.min.x : bounds.max.x, corner < 2 ? bounds.min.y : bounds.max.y, bounds.center.z)));
                    left = Mathf.Min(left, local.x);
                    right = Mathf.Max(right, local.x);
                    bottom = Mathf.Min(bottom, local.y);
                    top = Mathf.Max(top, local.y);
                }
            }
            return (plate, Rect.MinMaxRect(left, bottom, right, top));
        }

        private static readonly List<MeshFilter> labelFilters = new List<MeshFilter>();

        /// <summary>Every character as seen from the eyes when the plane was laid last: each body, as placement sees it.</summary>
        private readonly List<BodyInView> bodies = new List<BodyInView>();

        /// <summary>The same characters with their labels as laid, which the light line must not cross, the file's own marked.</summary>
        private readonly List<(BodyInView Body, (Transform Plate, Rect Covered) Label, bool Own)> standing = new List<(BodyInView, (Transform, Rect), bool)>();

        /// <summary>How far, in degrees, the light line keeps from a character's body; a label is told by its own outline.</summary>
        private const float LineMarginDegrees = 0.5f;

        /// <summary>
        /// With no light line laid now, the one that stood (<paramref name="was"/>) draws back to its character
        /// while its file closes, or one already drawing back goes on; laid at once, or with a line laid, none does.
        /// </summary>
        private void LeaveLine((MenuFrameView View, CharacterTarget Of)? was, bool immediately)
        {
            if (lineTo != null || immediately) leavingLine = null;
            else if (was is (MenuFrameView leaving, CharacterTarget _) && leaving.Closing) leavingLine = was;
            else if (!(leavingLine is (MenuFrameView still, CharacterTarget _) && still.Closing)) leavingLine = null;
        }

        /// <summary>A closing file the light line draws back from, to its character, until it is gone; null with none.</summary>
        private (MenuFrameView View, CharacterTarget Of)? leavingLine;

        /// <summary>How much of the light line is drawn now, from its character's end: less as a file opens or closes.</summary>
        private float drawnShare = 1f;

        /// <summary>What the file on the plane belongs to, as laid last (<see cref="Show"/>'s fileColumn).</summary>
        private object? shownFileColumn;

        /// <summary>Which column of the composition the light line reaches, or -1.</summary>
        private int lineColumn = -1;

        /// <summary>Where the stage's centre stands from the eyes, yaw to the right, as laid last: the characters' mean direction; null with none.</summary>
        public float? StageYaw { get; private set; }

        /// <summary>Whether <paramref name="point"/>, seen from <paramref name="eyes"/>, falls on a label's outline (<see cref="LabelOutline"/>).</summary>
        public static bool OnLabel((Transform Plate, Rect Covered) label, Vector3 eyes, Vector3 point)
        {
            // A label whose character has left the stage stands nowhere.
            if (label.Plate == null) return false;
            var toward = point - eyes;
            var normal = label.Plate.forward;
            var across = Vector3.Dot(toward, normal);
            if (Mathf.Abs(across) < 1e-6f) return false;
            var along = Vector3.Dot(label.Plate.position - eyes, normal) / across;
            if (along <= 0f) return false;
            var local = label.Plate.InverseTransformPoint(eyes + toward * along);
            return label.Covered.Contains(new Vector2(local.x, local.y));
        }

        /// <summary>
        /// Draws the light line from one edge to the other: straight across from the middle of their
        /// overlap, as the eyes see them, else between their nearer ends.
        /// </summary>
        private void Join(Vector3 labelLeft, Vector3 labelRight, Vector3 plateLeft, Vector3 plateRight)
        {
            var (from, to) = Joined(labelLeft, labelRight, plateLeft, plateRight);
            line.gameObject.SetActive(true);
            line.SetPosition(0, from);
            // Drawn from the character's end as far as it has come.
            line.SetPosition(1, Vector3.Lerp(from, to, drawnShare));
        }

        /// <summary>The light line's two ends between the label's edge and the plate's: straight across from the middle of their overlap, as the eyes see them, else their nearer ends.</summary>
        private (Vector3 From, Vector3 To) Joined(Vector3 labelLeft, Vector3 labelRight, Vector3 plateLeft, Vector3 plateRight)
        {
            float Across(Vector3 point) => Mathf.Atan2(point.x - eyes.x, point.z - eyes.z) * Mathf.Rad2Deg;
            Vector3 At(Vector3 left, Vector3 right, float across) => Vector3.Lerp(left, right, Mathf.InverseLerp(Across(left), Across(right), across));
            var overlapLeft = Mathf.Max(Across(labelLeft), Across(plateLeft));
            var overlapRight = Mathf.Min(Across(labelRight), Across(plateRight));
            Vector3 from, to;
            if (overlapLeft <= overlapRight)
            {
                var middle = (overlapLeft + overlapRight) / 2f;
                (from, to) = (At(labelLeft, labelRight, middle), At(plateLeft, plateRight, middle));
            }
            else if (Across(labelRight) < Across(plateLeft)) (from, to) = (labelRight, plateLeft);
            else (from, to) = (labelLeft, plateRight);
            return (from, to);
        }

        /// <summary>A part's slide from where it stood to where it goes.</summary>
        private sealed class Slide
        {
            public Slide(Vector3 from, Quaternion fromRotation, float fromScale, Vector3 to, Quaternion toRotation, float toScale)
            {
                From = from;
                FromRotation = fromRotation;
                FromScale = fromScale;
                To = to;
                ToRotation = toRotation;
                ToScale = toScale;
            }

            public Vector3 From { get; }

            public Quaternion FromRotation { get; }

            public float FromScale { get; }

            public Vector3 To { get; }

            public Quaternion ToRotation { get; }

            public float ToScale { get; }

            public float Progress { get; set; }

            /// <summary>The part's column hides when the slide ends, as the menu stepping aside does: it leaves.</summary>
            public bool HideAtEnd { get; set; }

            /// <summary>The part comes onto the plane, as the menu back from stepping aside: it arrives.</summary>
            public bool Arrives { get; set; }

            /// <summary>How long the slide takes, by its kind (ADR 0027).</summary>
            public float Seconds => HideAtEnd ? Glaze.LeaveSeconds : Arrives ? Glaze.AppearSeconds : Glaze.SlideSeconds;

            /// <summary>How far along its way the part stands now, by its kind's easing: what leaves eases in, what arrives eases out, a move eases in and out.</summary>
            public float Eased() => HideAtEnd ? Glaze.EaseIn(Progress) : Arrives ? Glaze.EaseOut(Progress) : Glaze.EaseInOut(Progress);
        }
    }
}
