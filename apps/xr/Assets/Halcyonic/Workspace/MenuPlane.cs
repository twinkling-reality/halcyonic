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
    /// place over one short ease, so the plane re-centres as one piece and what was there shifts by half
    /// the new column's width; the menu steps aside to the left the same way, leaving the plane, and
    /// comes back from there. Closed with no file open, the menu is its bar. The light line joins the
    /// file's character's label to the file's subject.
    /// </summary>
    public sealed class MenuPlane : MonoBehaviour
    {
        /// <summary>How long a part takes to slide to its new place.</summary>
        public const float SlideSeconds = 0.25f;

        private const float LineStart = 0.003f;
        private const float LineEnd = 0.0012f;
        private const float LineStartAlpha = 0.55f;
        private const float LineEndAlpha = 0.12f;

        /// <summary>How often the label's lowest edge is measured again: it changes only as its words do.</summary>
        private const float MeasureSeconds = 0.5f;

        private readonly Dictionary<Transform, Slide> slides = new Dictionary<Transform, Slide>();
        private readonly List<BodyInView> scratch = new List<BodyInView>();
        private readonly List<(MenuColumn Kind, MenuFrameView View)> shown = new List<(MenuColumn, MenuFrameView)>();
        private MenuFrameView menu = null!;
        private MenuFrameView file = null!;
        private MenuFrameView side = null!;
        private MenuBarView bar = null!;
        private LineRenderer line = null!;
        private CharacterTarget? fileOf;
        private MenuFrameView? lineTo;
        private Vector3 eyes;
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

        /// <summary>The menu is open but stands aside for the file.</summary>
        public bool MenuAside { get; private set; }

        /// <summary>
        /// The menu's details stand in front of the file beside it: the menu's chosen row opened its side
        /// panel, so the file steps aside off the plane, to the right, until they close (ADR 0026).
        /// </summary>
        public bool FileAside { get; private set; }

        /// <summary>The closed bar, while it shows.</summary>
        public MenuBarView? Bar => bar.gameObject.activeSelf ? bar : null;

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

        /// <summary>The light line's two ends, while it shows.</summary>
        public (Vector3 From, Vector3 To)? LightLine => line.gameObject.activeSelf ? (line.GetPosition(0), line.GetPosition(1)) : ((Vector3, Vector3)?)null;

        public static MenuPlane Create(Transform parent)
        {
            var go = new GameObject("Menu plane");
            go.transform.SetParent(parent, false);
            var plane = go.AddComponent<MenuPlane>();
            plane.menu = plane.View("Menu", MenuColumn.Menu);
            plane.file = plane.View("File", MenuColumn.File);
            plane.side = plane.View("Side panel", MenuColumn.Side);
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
                if (Contains(shown, view)) Acted?.Invoke(kind, action, key, view.Frame, view.Side);
            };
            view.Drawn += drawn => Drawn?.Invoke(kind, drawn);
            view.HoldStarted += prompt =>
            {
                if (Contains(shown, view)) HoldStarted?.Invoke(kind, prompt, view.Frame, view.Side);
            };
            view.HoldEnded += (prompt, letGo) => HoldEnded?.Invoke(kind, prompt, letGo);
            view.Hide();
            return view;
        }

        /// <summary>
        /// Shows the menu open on <paramref name="menuFrame"/>, or closed to <paramref name="menuBar"/>
        /// when it is null, and <paramref name="fileFrame"/>, a task's file, beside
        /// <paramref name="fileCharacter"/>, or no file; with the eyes at <paramref name="at"/> looking
        /// along <paramref name="looking"/>, every character on the stage in <paramref name="all"/>, and
        /// the surface they stand on at <paramref name="surfaceHeight"/>, if any. The parts slide to
        /// their places, or stand there at once when <paramref name="immediately"/>.
        /// </summary>
        /// <param name="besideWindow">
        /// The characters stand either side of a window straight ahead (along <paramref name="looking"/>):
        /// the plane opens centred under it, a file not turned toward its character, the menu and a file
        /// one at a time, and no light line, which would run across the window.
        /// </param>
        public void Show(MenuBar menuBar, MenuFrame? menuFrame, MenuFrame? fileFrame, CharacterTarget? fileCharacter, IReadOnlyList<CharacterTarget> all,
            Vector3 at, Vector3 looking, float? surfaceHeight, bool immediately = false, bool besideWindow = false)
        {
            eyes = at;
            // The menu's details take the front over a file beside it: laid as if no file stood there, the
            // file stepping aside until they close, so what the person just chose is what they see.
            var wasFileAside = FileAside;
            FileAside = menuFrame?.Side != null && fileFrame != null;
            if (FileAside)
            {
                fileFrame = null;
                fileCharacter = null;
            }
            fileOf = fileFrame != null && !besideWindow ? fileCharacter : null;
            measured = -1f;
            var panel = fileFrame != null ? fileFrame.Side : menuFrame?.Side;
            Front = fileFrame ?? menuFrame;
            // Standing in its frame's place, a side panel is as wide as that frame, so the footer it carries fits.
            var frontDegrees = fileFrame != null ? Glaze.Menu.FileColumnDegrees : Glaze.Menu.MenuColumnDegrees;
            var pill = fileFrame?.Pill != null;
            var zoom = GlazeText.Scale;
            // With text a step larger, a frame and its side panel are too wide for a Quest 3S together;
            // and wherever the two would not fit as the stage places them, as beside a window, the side
            // panel takes its frame's place all the same.
            var inPlace = panel != null && (zoom > 1f || !FitWithSide(menuFrame, fileFrame, panel, pill, all, looking, surfaceHeight, besideWindow));

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
                // Where the stage would place the two under its labels, inside the headset's measured field.
                if (fits && ViewField.Current is ViewField field && fileCharacter != null)
                {
                    var placed = WorkspaceLayout.Place(fileCharacter, all, eyes, looking, surfaceHeight, scratch, beside.Size).Direction;
                    fits = placed.Clear && MenuPage.Inside(beside, placed, field);
                }
            }
            var kinds = MenuColumns.Arrange(menuFrame != null, fileFrame != null, panel != null, fits, inPlace);
            var wasAside = MenuAside;
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

            var before = new List<(MenuColumn, MenuFrameView)>(shown);
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
                // Coming back from stepping aside, the menu or the file takes no press as it slides in: its
                // buttons wait to settle again, though their words are as they were.
                if ((kind == MenuColumn.Menu && wasAside) || (kind == MenuColumn.File && wasFileAside)) view.Unsettle();
                shown.Add((kind, view));
            }
            lineTo = fileOf == null ? null : Contains(shown, file) ? file : inPlace ? side : null;

            if (shown.Count == 0)
            {
                // Closed with no file: the bar alone, where the person looks.
                Composition = null;
                foreach (var (_, view) in before) view.Hide();
                bar.Show(menuBar, Glaze.Menu.MenuColumnDegrees);
                var barSize = new PanelSize(PlaneComposition.Distance, bar.Size.x * zoom / 2f * PlaneComposition.Distance, bar.Size.y * zoom / 2f * PlaneComposition.Distance);
                Direction = (besideWindow ? WorkspaceLayout.PlaceAhead(all, eyes, looking, surfaceHeight, scratch, barSize)
                    : WorkspaceLayout.PlaceForeground(all, eyes, looking, surfaceHeight, scratch, barSize)).Direction;
                var placed = new PlanePart(0, 0, bar.Size.x * zoom, bar.Size.y * zoom, 0f, 0f);
                SlideTo(bar.transform, placed, zoom, null, true);
                line.gameObject.SetActive(false);
                return;
            }
            bar.Hide();

            var columns = new List<PlaneColumn>();
            foreach (var (_, view) in shown) columns.Add(ColumnOf(view));
            Composition = new PlaneComposition(columns, zoom);
            Direction = besideWindow ? WorkspaceLayout.PlaceAhead(all, eyes, looking, surfaceHeight, scratch, Composition.Size).Direction
                : fileOf != null ? WorkspaceLayout.Place(fileOf, all, eyes, looking, surfaceHeight, scratch, Composition.Size).Direction
                : WorkspaceLayout.PlaceForeground(all, eyes, looking, surfaceHeight, scratch, Composition.Size).Direction;

            for (var c = 0; c < shown.Count; c++)
            {
                var (kind, view) = shown[c];
                var parts = view.Parts;
                var index = 0;
                foreach (var placed in Composition.Parts)
                {
                    if (placed.Column != c) continue;
                    // The menu coming back slides in from where it stepped aside to, and the file from its side.
                    Vector3? from = kind == MenuColumn.Menu && wasAside ? Aside(placed, zoom)
                        : kind == MenuColumn.File && wasFileAside ? Aside(placed, zoom, toTheRight: true) : (Vector3?)null;
                    SlideTo(parts[index], placed, zoom, from, immediately || !WasShown(before, view));
                    if (index == parts.Count - 1) view.Settle(placed, zoom);
                    index++;
                }
            }
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
                else view.Hide();
            }
            // A view laid only to measure, as the menu beside a file too tall for it, never shows.
            foreach (var view in new[] { menu, file, side })
            {
                if (!Contains(shown, view) && !Contains(before, view)) view.Hide();
            }
            Advance(0f);
        }

        /// <summary>Whether the frame in front and its side panel fit together where the stage would place them, in the headset's measured field.</summary>
        private bool FitWithSide(MenuFrame? menuFrame, MenuFrame? fileFrame, SidePanel panel, bool pill, IReadOnlyList<CharacterTarget> all, Vector3 looking,
            float? surfaceHeight, bool besideWindow)
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
            return direction.Clear && MenuPage.Inside(together, direction, field);
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

        /// <summary>Moves every sliding part on by <paramref name="seconds"/>, and the light line with them; the renders step it themselves.</summary>
        public void Advance(float seconds)
        {
            foreach (var pair in slides)
            {
                var part = pair.Key;
                var slide = pair.Value;
                if (part == null) continue;
                slide.Progress = SlideSeconds <= 0f ? 1f : Mathf.Clamp01(slide.Progress + seconds / SlideSeconds);
                var eased = Mathf.SmoothStep(0f, 1f, slide.Progress);
                part.SetPositionAndRotation(Vector3.Lerp(slide.From, slide.To, eased), Quaternion.Slerp(slide.FromRotation, slide.ToRotation, eased));
                part.localScale = Vector3.one * Mathf.Lerp(slide.FromScale, slide.ToScale, eased);
                if (slide.Progress >= 1f && slide.HideAtEnd) part.parent.gameObject.SetActive(false);
            }
            UpdateLightLine();
        }

        private void LateUpdate() => Advance(Time.unscaledDeltaTime);

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
                : new Slide(part.position, part.rotation, part.localScale.x, position, rotation, scale);
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
            if (fileOf == null || to == null)
            {
                line.gameObject.SetActive(false);
                return;
            }
            var subject = to.Subject;
            if (Direction.Above)
            {
                var content = to.Parts[to.Parts.Count - 1];
                var bottom = -to.Content.Size.y / 2f;
                var top = fileOf.BodyPosition + Vector3.up * (CharacterView.BodyExtent * fileOf.Scale);
                Join(top, top, content.TransformPoint(new Vector3(-to.Width / 2f, bottom, 0f)), content.TransformPoint(new Vector3(to.Width / 2f, bottom, 0f)));
                return;
            }
            var plate = fileOf.View.Label.Plate.transform;
            if (measured < 0f || Time.unscaledTime - measured > MeasureSeconds)
            {
                measured = Time.unscaledTime;
                lowest = LabelOutline(fileOf.View).Covered.yMin;
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
            foreach (var filter in view.Label.GetComponentsInChildren<MeshFilter>(false))
            {
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

        /// <summary>Whether <paramref name="point"/>, seen from <paramref name="eyes"/>, falls on a label's outline (<see cref="LabelOutline"/>).</summary>
        public static bool OnLabel((Transform Plate, Rect Covered) label, Vector3 eyes, Vector3 point)
        {
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
            line.gameObject.SetActive(true);
            line.SetPosition(0, from);
            line.SetPosition(1, to);
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

            /// <summary>The part's column hides when the slide ends, as the menu stepping aside does.</summary>
            public bool HideAtEnd { get; set; }
        }
    }
}
