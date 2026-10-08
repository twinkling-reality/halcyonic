#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>
        /// The menu's plane on a stage (ADR 0026), placed the way the director will place it: the menu on
        /// Tasks beside the waiting task's file; the file's cut answer chosen, its side panel sliding out
        /// and the menu stepping aside, checked halfway through the slide and at its end; the side panel
        /// closed and the menu back where it was; and the menu closed to its bar. Each state is held to one
        /// plane, type stepping down, one selection treatment, text as seen, its targets, a degree from
        /// every label and body, the measured field where there is one, and the light line crossing no
        /// label or character.
        /// </summary>
        /// <param name="besideWindow">The four characters either side of a video window straight ahead, the plane opening centred under it.</param>
        private static IEnumerable<string> RenderMenuPlane(string name, string folder, float radius, float? surfaceDrop, bool besideWindow = false)
        {
            var failures = new List<string>();
            var root = new GameObject("Menu plane render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var characters = Lineup(root.transform, eyes, radius, surfaceDrop, Presentation, besideWindow: besideWindow);
                var window = besideWindow ? Window(root.transform, eyes) : null;
                var targets = characters.ConvertAll(character => character.Target);
                var opened = characters[3];
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var looking = besideWindow ? Vector3.forward : opened.Target.BodyPosition - eyes;
                var plane = MenuPlane.Create(root.transform);
                var text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard;
                var bar = new MenuBar(MenuPlace.Tasks, "1 task is waiting for you", MenuPlace.Tasks);
                // Tasks beside a file packs its rows by the stage's real height there, when the menu opens.
                var besideTop = MenuPlane.TopLine(opened.Target, targets, eyes, looking, surface, besideWindow, besideMenu: true);
                var besideBudget = MenuPage.Height(text, 1, besideTop, ViewField.Current, besideMenu: true);
                var rows = Enumerable.Range(1, MenuFrame.RowsAPage(text, sourceLine: false)).LastOrDefault(count => MenuPage.Rows(count) <= besideBudget);
                Debug.Log("Halcyonic: workspace render " + name + ": beside a file the plane's top line is " + GlazeChecks.Degrees(besideTop) + " degrees below eye level, so Tasks holds "
                    + rows + " rows.");
                var menu = Tasks(text, opened.View.Presentation!.Title, Mathf.Max(1, rows));
                var badge = StateLanguage.BadgeOf(opened.View.Presentation!);
                // The file's page packed for this stage, as the screens pack it: as much as its top line and the field leave.
                var top = MenuPlane.TopLine(opened.Target, targets, eyes, looking, surface, besideWindow);
                var budget = MenuPage.Height(text, MenuFrameView.TitleRows(opened.View.Presentation!.Title, Glaze.Menu.FileColumnDegrees), top, ViewField.Current);
                Debug.Log("Halcyonic: workspace render " + name + ": the plane's top line here is " + GlazeChecks.Degrees(top) + " degrees below eye level; a file's page holds "
                    + GlazeChecks.Degrees(2f * Mathf.Atan(budget / 2f) * Mathf.Rad2Deg) + " degrees of lines.");

                // Where the menu and a file side by side would stand under these labels: Tasks' rows and the
                // file's subject level with them, each label cleared where it stands, and as it was, at the corners.
                if (!besideWindow) MeasureBeside(name, opened.Target, targets, eyes, looking, surface, text);

                // The menu alone, then the waiting task's file opening beside it: the menu moves over to make
                // room, easing in and out, seen a quarter of a slide's time in.
                plane.Show(bar, menu, null, null, targets, eyes, looking, surface, immediately: true, besideWindow: besideWindow);
                var alone = plane.Shown.Where(column => column.Kind == MenuColumn.Menu).Select(column => column.View.Parts[0].position).ToList();
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface, besideWindow: besideWindow);
                var moves = plane.Shown.Where(column => column.Kind == MenuColumn.Menu).Select(column => column.View).FirstOrDefault();
                plane.Advance(Glaze.SlideSeconds / 4f);
                var moveQuarter = moves != null ? moves.Parts[0].position : Vector3.zero;
                plane.Advance(Glaze.SlideSeconds);
                if (alone.Count > 0 && moves != null) failures.AddRange(EasedAlong(name + ": the menu moving over for a file", alone[0], moveQuarter, moves.Parts[0].position, Glaze.EaseInOut(0.25f)));
                else Debug.Log("Halcyonic: workspace render " + name + ": the menu and a file do not stand together here, so the menu moving over goes unseen.");

                // The menu on Tasks beside the waiting task's file.
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface, immediately: true, besideWindow: besideWindow);
                failures.AddRange(PlaneState(name + " menu and file", folder, camera, texture, plane, characters, eyes, window));
                // Where the file is too tall beside Tasks here, the menu stands aside from the start.
                var menuPlaced = plane.Shown.Where(column => column.Kind == MenuColumn.Menu).SelectMany(column => column.View.Parts).Select(part => part.position).ToList();
                if (menuPlaced.Count == 0) Debug.Log("Halcyonic: workspace render " + name + ": the file is too tall to stand beside Tasks here, so the menu stands aside.");
                var fileBefore = plane.Shown.First(column => column.Kind == MenuColumn.File).View.Subject.position;
                if (besideWindow && plane.Shown.Count != 1) failures.Add(name + ": beside a window the menu and a file stand together; they stand one at a time.");

                // A cut answer chosen: its side panel slides out, and the menu steps aside to the left. Each move
                // is seen a quarter of its own time in: the menu leaving eases in, the file moving eases in and out.
                var shifting = FindView(plane, "File");
                if (shifting != null) SettleLongAgo(shifting);
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: true, budget), opened.Target, targets, eyes, looking, surface, besideWindow: besideWindow);
                // The file moving over for its side panel takes no press as it slides: its buttons wait to settle again.
                if (shifting != null && plane.Shown.Any(column => column.View == shifting)) failures.AddRange(WaitsToSettle(name + " file moving over for its side panel", shifting));
                plane.Advance(Glaze.LeaveSeconds / 4f);
                var menuView = plane.Shown.All(column => column.Kind != MenuColumn.Menu) ? FindView(plane, "Menu") : null;
                var menuQuarter = menuView != null && menuView.Parts.Count > 0 ? menuView.Parts[0].position : (Vector3?)null;
                plane.Advance(Glaze.SlideSeconds / 4f - Glaze.LeaveSeconds / 4f);
                var fileQuarter = plane.Shown.Where(column => column.Kind == MenuColumn.File).Select(column => column.View.Subject.position).ToList();
                plane.Advance(Glaze.SlideSeconds / 4f);
                var fileHalf = plane.Shown.Where(column => column.Kind == MenuColumn.File).Select(column => column.View.Subject.position).ToList();
                var right = PlaneLayout.Facing(plane.Direction) * Vector3.right;
                if (!plane.MenuAside) failures.Add(name + ": the file's side panel opened beside the menu; the menu steps aside, never three columns.");
                if (!plane.Sliding && (menuPlaced.Count > 0 || fileHalf.Count > 0)) failures.Add(name + ": halfway through the slide, nothing is sliding.");
                if (menuView != null && menuPlaced.Count > 0 && Vector3.Dot(menuView.Parts[0].position - menuPlaced[0], right) >= 0f)
                {
                    failures.Add(name + ": halfway through stepping aside, the menu has not moved left.");
                }
                // Beside its side panel the file moves left toward the centre; where the panel takes its place, it gives way.
                if (fileHalf.Count > 0 && Vector3.Dot(fileHalf[0] - fileBefore, right) >= 0f) failures.Add(name + ": halfway through the slide, the file has not moved left toward the centre.");
                plane.Advance(Glaze.SlideSeconds);
                if (menuView != null && menuView.gameObject.activeSelf) failures.Add(name + ": the menu still shows after stepping aside.");
                if (menuView != null && menuView.Parts.Count > 0 && menuPlaced.Count > 0 && menuQuarter is Vector3 quarter)
                {
                    failures.AddRange(EasedAlong(name + ": the menu stepping aside", menuPlaced[0], quarter, menuView.Parts[0].position, Glaze.EaseIn(0.25f)));
                }
                if (fileQuarter.Count > 0 && fileHalf.Count > 0)
                {
                    var fileEnd = plane.Shown.First(column => column.Kind == MenuColumn.File).View.Subject.position;
                    failures.AddRange(EasedAlong(name + ": the file moving toward the centre", fileBefore, fileQuarter[0], fileEnd, Glaze.EaseInOut(0.25f)));
                }
                if (plane.Shown.Count > 2) failures.Add(name + ": " + plane.Shown.Count + " columns stand on the plane; there are never three.");
                if (plane.Shown.All(column => column.Kind != MenuColumn.Side)) failures.Add(name + ": the chosen answer's side panel does not show.");
                if (GlazeText.Scale > 1f && plane.Shown.Count != 1) failures.Add(name + ": with larger text the side panel stands beside its file; it takes the file's place.");
                if (fileHalf.Count == 0) Debug.Log("Halcyonic: workspace render " + name + ": the side panel stands in the file's place.");
                failures.AddRange(PlaneState(name + " file and side panel", folder, camera, texture, plane, characters, eyes, window));

                // Close details: the menu comes back where it was, taking no press until it settles.
                var menuAside = plane.MenuAside && menuView != null;
                if (menuAside) SettleLongAgo(menuView!);
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface, besideWindow: besideWindow);
                if (menuAside && !plane.MenuAside) failures.AddRange(WaitsToSettle(name + " menu back", menuView!));
                // The menu coming back arrives, easing out: seen a quarter of its own time in.
                var menuFrom = menuAside && !plane.MenuAside && menuView!.Parts.Count > 0 ? menuView.Parts[0].position : (Vector3?)null;
                plane.Advance(Glaze.AppearSeconds / 4f);
                var menuBackQuarter = menuFrom != null ? menuView!.Parts[0].position : Vector3.zero;
                plane.Advance(Glaze.SlideSeconds / 2f - Glaze.AppearSeconds / 4f);
                if (plane.MenuAside != (menuPlaced.Count == 0)) failures.Add(name + ": the side panel closed, and the menu stands " + (plane.MenuAside ? "aside" : "beside the file") + " where it stood the other way before.");
                plane.Advance(Glaze.SlideSeconds);
                var back = plane.Shown.Where(column => column.Kind == MenuColumn.Menu).SelectMany(column => column.View.Parts).Select(part => part.position).ToList();
                if (menuFrom is Vector3 aside && back.Count > 0) failures.AddRange(EasedAlong(name + ": the menu coming back", aside, menuBackQuarter, back[0], Glaze.EaseOut(0.25f)));
                if (back.Count != menuPlaced.Count) failures.Add(name + ": the menu did not come back as it stood.");
                for (var index = 0; index < Mathf.Min(back.Count, menuPlaced.Count); index++)
                {
                    var off = Vector3.Distance(back[index], menuPlaced[index]);
                    if (off > 0.001f) failures.Add(name + ": the menu came back " + (off * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm from where it was.");
                }
                failures.AddRange(PlaneState(name + " menu back", folder, camera, texture, plane, characters, eyes, window));

                // A setting chosen with the file beside the menu: the menu's details take the front, and the
                // file steps aside to the right, off the plane, until they close.
                var fileShown = plane.Shown.Where(column => column.Kind == MenuColumn.File).Select(column => column.View.Subject.position).ToList();
                plane.Show(bar, SettingChosen(), WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface,
                    besideWindow: besideWindow);
                plane.Advance(Glaze.SlideSeconds / 2f);
                var fileView = FindView(plane, "File");
                if (fileShown.Count > 0 && fileView != null && fileView.gameObject.activeSelf && Vector3.Dot(fileView.Subject.position - fileShown[0], PlaneLayout.Facing(plane.Direction) * Vector3.right) <= 0f)
                {
                    failures.Add(name + ": halfway through stepping aside for the menu's details, the file has not moved right.");
                }
                plane.Advance(Glaze.SlideSeconds);
                if (!plane.FileAside || plane.Shown.Any(column => column.Kind == MenuColumn.File)) failures.Add(name + ": a setting chosen beside a file, and the file still stands on the plane.");
                if (fileView != null && fileView.gameObject.activeSelf) failures.Add(name + ": the file still shows after stepping aside.");
                if (plane.Shown.All(column => column.Kind != MenuColumn.Side)) failures.Add(name + ": a setting chosen beside a file, and its details don't show.");
                failures.AddRange(PlaneState(name + " details over a file", folder, camera, texture, plane, characters, eyes, window));

                // Close details: the file comes back, taking no press until it settles.
                var fileAside = plane.FileAside && fileView != null;
                if (fileAside) SettleLongAgo(fileView!);
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface, besideWindow: besideWindow);
                if (fileAside && !plane.FileAside) failures.AddRange(WaitsToSettle(name + " file back", fileView!));
                plane.Advance(Glaze.SlideSeconds);
                if (plane.FileAside || plane.Shown.All(column => column.Kind != MenuColumn.File)) failures.Add(name + ": the menu's details closed, and the file did not come back.");
                failures.AddRange(PlaneState(name + " file back", folder, camera, texture, plane, characters, eyes, window));

                failures.AddRange(OpeningAllocatesNothing(name, plane,
                    () => plane.Show(bar, null, null, null, targets, eyes, looking, surface, immediately: true, besideWindow: besideWindow),
                    () => plane.Show(bar, null, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface,
                        besideWindow: besideWindow)));

                // Another task's file taking the file's place opens anew, taking nothing until it shows whole; the same
                // task's file laid again stands as it was.
                var oneTask = new object();
                var anotherTask = new object();
                void ShowOf(object task) => plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes,
                    looking, surface, besideWindow: besideWindow, fileColumn: task);
                ShowOf(oneTask);
                plane.Advance(Mathf.Max(Glaze.SlideSeconds, MenuPlane.OpeningSeconds));
                ShowOf(oneTask);
                if (plane.Opening(MenuColumn.File)) failures.Add(name + ": the same task's file laid again opens anew.");
                var swapTaken = new List<string>();
                var swapDrawn = 0;
                System.Action<MenuColumn, string, string?, MenuFrame?, SidePanel?> swapActed = (kind, action, key, frame, side) => swapTaken.Add(action);
                System.Action<MenuColumn, MenuFrameView> swapLaid = (kind, view) => { if (kind == MenuColumn.File) swapDrawn++; };
                plane.Acted += swapActed;
                plane.Drawn += swapLaid;
                ShowOf(anotherTask);
                if (!plane.Opening(MenuColumn.File)) failures.Add(name + ": another task's file taking the file's place is not opening, so it takes presses before it shows.");
                if (swapDrawn > 0) failures.Add(name + ": another task's file taking the file's place was counted drawn before it showed whole.");
                FindView(plane, "File")?.PressForRender("send");
                if (swapTaken.Count > 0) failures.Add(name + ": another task's file taking the file's place took " + string.Join(", ", swapTaken) + " as it opened.");
                plane.Advance(Mathf.Max(Glaze.SlideSeconds, MenuPlane.OpeningSeconds));
                if (swapDrawn != 1) failures.Add(name + ": another task's file taking the file's place was counted drawn " + swapDrawn + " times as it opened, not once.");
                plane.Acted -= swapActed;
                plane.Drawn -= swapLaid;

                failures.AddRange(TapCountsWhatItStillIs(name, root.transform));

                // The waiting task's file assembling from its character, then closing to the bar.
                failures.AddRange(OpeningStrip(name + " opening", folder, camera, texture, plane,
                    () => plane.Show(bar, null, null, null, targets, eyes, looking, surface, immediately: true, besideWindow: besideWindow),
                    () => plane.Show(bar, null, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface,
                        besideWindow: besideWindow),
                    () => plane.Show(bar, null, null, null, targets, eyes, looking, surface, besideWindow: besideWindow)));

                // A column opened takes nothing until it has opened, though it counts as drawn when laid.
                failures.AddRange(OpeningTakesNothing(name, plane, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget),
                    WaitingFile(opened.View.Presentation!.Title, badge, chosen: true, budget),
                    (frame, now) => plane.Show(bar, menu, frame, frame != null ? opened.Target : null, targets, eyes, looking, surface, immediately: now, besideWindow: besideWindow),
                    () => plane.Show(bar, null, null, null, targets, eyes, looking, surface, immediately: true, besideWindow: besideWindow)));

                // Closed, with no file open: the bar alone.
                plane.Show(bar, null, null, null, targets, eyes, looking, surface, immediately: true, besideWindow: besideWindow);
                if (plane.Bar == null) failures.Add(name + ": the menu closed with no file open shows no bar.");
                failures.AddRange(PlaneState(name + " closed", folder, camera, texture, plane, characters, eyes, window));
            }
            finally
            {
                Object.DestroyImmediate(root);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>
        /// Logs where the menu and a file side by side stand under the stage's labels: the plane's top
        /// below eye level as each label is cleared where it stands, as it would stand cleared at its
        /// corners under the deepest label, and whether it fits the measured field there.
        /// </summary>
        private static void MeasureBeside(string name, CharacterTarget opened, List<CharacterTarget> targets, Vector3 eyes, Vector3 looking, float? surface, TextSize text)
        {
            var page = MenuPage.Rows(MenuFrame.RowsAPage(text, sourceLine: false));
            var column = new[] { MenuPage.Subject(1, pill: true), MenuPage.Sections, MenuPage.Content(page) };
            var beside = new PlaneComposition(new[]
            {
                new PlaneColumn(PlaneComposition.Units(Glaze.Menu.MenuColumnDegrees), column),
                new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), column),
            }, GlazeText.Scale);
            var direction = WorkspaceLayout.Place(opened, targets, eyes, looking, surface, new List<BodyInView>(), beside.Size).Direction;
            var top = -(direction.Elevation + beside.Size.HalfHeightDegrees);
            var deepest = targets.Select(target => WorkspaceLayout.InView(target, eyes))
                .Where(body => Mathf.Abs(WorkspacePlacement.DeltaAngle(direction.Yaw, body.Yaw)) - body.HalfWidth < beside.Size.HalfWidthDegrees)
                .Min(body => body.Lowest);
            var corners = -WorkspacePlacement.EdgeForCorners(deepest - WorkspacePlacement.LabelClearanceDegrees, beside.Size.HalfWidthDegrees);
            var fits = !(ViewField.Current is ViewField field) || MenuPage.Inside(beside, direction, field);
            Debug.Log("Halcyonic: workspace render " + name + ": the menu and a file side by side, " + GlazeChecks.Degrees(2f * beside.Size.HalfHeightDegrees)
                + " degrees tall, stand with their top " + GlazeChecks.Degrees(top) + " degrees below eye level, each label cleared where it stands; cleared at the corners under the deepest label it would be "
                + GlazeChecks.Degrees(corners) + ". They " + (fits ? "fit" : "do not fit") + " the field there.");
        }

        /// <summary>
        /// A column newly on the plane takes nothing until it has opened (<see cref="MenuFrameView.OpeningSeconds"/>).
        /// On a file opened beside the menu asking Send answer, then Yes, then Clear: every button waits to settle,
        /// and pressed as a hand presses it drops the press before its flash, sound or hold's timer, even with its
        /// own settling long past, since it reads the column's opening; the subject plate waits too; the frame it
        /// opened in and one long frame never end its opening; what still reaches the plane past the file's
        /// buttons, a press, a hold, a subject press or hold, is refused as it opens and halfway; and once it has
        /// opened and settled, the press is taken. The menu opened from its bar and a side panel opening, carrying
        /// a Yes that is safe in place where it stands in its file's place, wait to settle and take no press
        /// through their buttons until they have opened. The file counts as drawn only once it has opened, as it
        /// does when it stood open, so a page counted while faint is never acted on before it shows; and the first
        /// press it takes, frame by frame at the headset's rate and on slow frames, finds every part whole and in place.
        /// </summary>
        private static IEnumerable<string> OpeningTakesNothing(string name, MenuPlane plane, MenuFrame waiting, MenuFrame chosen, System.Action<MenuFrame?, bool> show,
            System.Action closed)
        {
            var failures = new List<string>();
            var taken = new List<string>();
            var pressed = new List<string>();
            var drawn = new List<(MenuColumn Kind, MenuFrame? Frame)>();
            System.Action<MenuColumn, string, string?, MenuFrame?, SidePanel?> acted = (kind, action, key, frame, side) => taken.Add(kind + " " + action);
            System.Action<MenuColumn, Prompt, MenuFrame?, SidePanel?> held = (kind, prompt, frame, side) => taken.Add(kind + " a hold on " + prompt.Words);
            System.Action<MenuColumn, MenuFrame?> subject = (kind, frame) => taken.Add(kind + " a press on its subject");
            System.Action<MenuColumn, MenuFrame?, Vector3> subjectHeld = (kind, frame, point) => taken.Add(kind + " a hold on its subject");
            System.Action<MenuColumn, MenuFrameView> laid = (kind, view) => drawn.Add((kind, view.Frame));
            System.Action<GlazeButton> anyPressed = button => pressed.Add(button.Label.text);
            plane.Acted += acted;
            plane.HoldStarted += held;
            plane.SubjectPressed += subject;
            plane.SubjectHeld += subjectHeld;
            plane.Drawn += laid;
            GlazeButton.AnyPressed += anyPressed;
            var ended = Mathf.Max(Glaze.SlideSeconds, MenuPlane.OpeningSeconds);
            // Every button the column shows, pressed as a hand presses it, is dropped before its flash, sound and hold timer:
            // while its own settling runs, and again with that long past, since every button reads the column's opening.
            void NothingTaken(string what, MenuFrameView view, float since)
            {
                if (UnityEngine.Time.unscaledTime - since > GlazeButton.SettleSeconds / 2f)
                {
                    failures.Add(what + ": the render took too long to press it while its buttons settle, so nothing was checked.");
                    return;
                }
                PressEach(what, view);
                SettleLongAgo(view);
                view.SettleSubjectForRender();
                if (!view.Opening) failures.Add(what + ": it ended its opening while its buttons were pressed, so their settling long past is not checked.");
                PressEach(what + ", its buttons' own settling long past", view);
            }
            void PressEach(string what, MenuFrameView view)
            {
                taken.Clear();
                pressed.Clear();
                foreach (var button in view.GetComponentsInChildren<GlazeButton>())
                {
                    var at = button.transform.position;
                    button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Select, at);
                    if (button.PressUnderWay) failures.Add(what + ": " + button.Label.text + " started a hold's timer while its column opens.");
                    button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Unselect, at);
                    button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Unhover, at);
                }
                // Its subject plate, pressed as a hand presses it, is dropped by the plate itself, before the plane.
                if (view.SubjectHoldForRender is PointerTarget plate)
                {
                    var subjectPresses = 0;
                    void Counted() => subjectPresses++;
                    view.SubjectPressed += Counted;
                    var on = view.Subject.position;
                    plate.PointerForRender(8, Oculus.Interaction.PointerEventType.Select, on);
                    plate.PointerForRender(8, Oculus.Interaction.PointerEventType.Unselect, on);
                    plate.PointerForRender(8, Oculus.Interaction.PointerEventType.Unhover, on);
                    view.SubjectPressed -= Counted;
                    if (subjectPresses > 0) failures.Add(what + ": its subject plate, pressed as a hand presses it, took the press while the column opens.");
                }
                if (pressed.Count > 0) failures.Add(what + ": pressed as a hand presses them, " + string.Join(", ", pressed) + " flashed and sounded taken while the column opens.");
                if (taken.Count > 0) failures.Add(what + ": " + string.Join(", ", taken) + " reached the plane while the column opens.");
            }
            // Settled and opened, the press on <paramref name="button"/> as a hand presses it is taken.
            void Taken(string what, MenuFrameView view, GlazeButton? button, MenuColumn kind)
            {
                plane.Advance(ended);
                SettleLongAgo(view);
                taken.Clear();
                pressed.Clear();
                if (button == null)
                {
                    failures.Add(what + ": no button to press once it has opened.");
                    return;
                }
                var at = button.transform.position;
                button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Select, at);
                button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Unselect, at);
                button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Unhover, at);
                if (!taken.Any(each => each.StartsWith(kind + " ", System.StringComparison.Ordinal)))
                {
                    failures.Add(what + ": once it has opened and settled, " + button.Label.text + " pressed was not taken.");
                }
            }
            try
            {
                var clearing = new MenuFrame(waiting.Subject, new Footer(PlaneClose, farRight: new Prompt(NewProjectScreens.Clear, EntryText.Clear, GlazeIcon.Next, main: true)),
                    waiting.SubjectIsData, waiting.Pill, waiting.Sections, waiting.Lines, waiting.Source, waiting.Side, waiting.SourceIsData, waiting.SubjectWaits);
                var talk = new Prompt("talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true);
                foreach (var (frame, asks, what) in new[] { (waiting, "send", "Send answer"), (Confirming(waiting), "yes", "Yes"), (clearing, NewProjectScreens.Clear, "Clear") })
                {
                    var asking = name + ": a file asking " + what;
                    // Stood open: laid again, it reports what it draws and is not opening.
                    show(null, true);
                    plane.Advance(ended);
                    show(frame, true);
                    plane.Advance(ended);
                    drawn.Clear();
                    show(frame, false);
                    var stoodOpen = drawn.ToList();
                    if (plane.Opening(MenuColumn.File)) failures.Add(asking + " that stood open is opening again, laid as it was.");

                    // Opened now, from the menu alone, its buttons and subject settled long before.
                    show(null, true);
                    plane.Advance(ended);
                    var view = FindView(plane, "File");
                    if (view != null)
                    {
                        SettleLongAgo(view);
                        view.SettleSubjectForRender();
                    }
                    drawn.Clear();
                    var since = UnityEngine.Time.unscaledTime;
                    show(frame, false);
                    var openedNow = drawn.ToList();
                    if (view == null || plane.Shown.All(column => column.Kind != MenuColumn.File))
                    {
                        failures.Add(asking + " opened, and no file stands on the plane.");
                        continue;
                    }
                    if (!plane.Opening(MenuColumn.File)) failures.Add(asking + " just opened is not opening, so it takes presses before it shows.");
                    failures.AddRange(WaitsToSettle(asking + " just opened", view));
                    if (!view.SubjectSettling) failures.Add(asking + " just opened: its subject plate takes a press before it has settled.");
                    if (openedNow.Any(each => each.Kind == MenuColumn.File)) failures.Add(asking + " just opened was counted drawn before it showed whole.");
                    NothingTaken(asking + " just opened", view, since);

                    // What reaches the plane past its buttons is refused while it opens, at once and halfway.
                    foreach (var when in new[] { "as it opens", "halfway through its opening" })
                    {
                        if (when != "as it opens") plane.Advance(MenuPlane.OpeningSeconds / 2f);
                        if (!plane.Opening(MenuColumn.File)) failures.Add(asking + " is no longer opening " + when + ".");
                        taken.Clear();
                        view.PressForRender(asks);
                        view.HoldForRender(talk);
                        view.PressSubjectForRender();
                        view.HoldSubjectForRender(view.Subject.position);
                        if (taken.Count > 0) failures.Add(asking + " took " + string.Join(", ", taken) + " " + when + ".");
                    }
                    drawn.Clear();
                    Taken(asking + " that has opened", view, ButtonFor(view, asks), MenuColumn.File);
                    var openedWhole = drawn.Where(each => each.Kind == MenuColumn.File).ToList();
                    if (openedWhole.Count != 1 || openedWhole[0].Frame != frame)
                    {
                        failures.Add(asking + " was counted drawn " + openedWhole.Count + " times as its opening ended, not once for its own page.");
                    }
                    if (!stoodOpen.Contains((MenuColumn.File, frame))) failures.Add(asking + " that stood open, laid again, was not counted drawn.");
                    view.SettleSubjectForRender();
                    taken.Clear();
                    view.HoldSubjectForRender(view.Subject.position);
                    if (taken.Count == 0) failures.Add(asking + " that has opened took no hold on its subject.");
                }

                // Frame by frame, as the headset steps it: the frame a file opened in counts nothing toward its opening,
                // a long frame counts at most a twentieth of a second, and the opening still ends.
                show(null, true);
                plane.Advance(ended);
                show(waiting, false);
                var window = FindView(plane, "File")?.OpeningSeconds ?? MenuPlane.OpeningSeconds;
                plane.FrameForRender(0.25f);
                if (!plane.Opening(MenuColumn.File)) failures.Add(name + ": the frame a file opened in ended its opening.");
                plane.FrameForRender(0.25f);
                if (!plane.Opening(MenuColumn.File)) failures.Add(name + ": one long frame ended a file's opening before it was seen.");
                plane.Advance(window - 0.05f - 0.01f);
                if (!plane.Opening(MenuColumn.File)) failures.Add(name + ": the frame a file opened in counted toward its opening.");
                plane.Advance(0.02f);
                if (plane.Opening(MenuColumn.File)) failures.Add(name + ": a file's opening outlasted its time, locking it out.");

                // Frame by frame, at the headset's rate and on slow frames, pressing Send answer every frame with its
                // own settling long past: the first press taken finds every part of the file whole and in place.
                foreach (var seconds in new[] { 1f / 72f, 0.1f })
                {
                    failures.AddRange(FirstPressFindsItWhole(name + ": a file opened at frames of " + seconds.ToString("0.000", CultureInfo.InvariantCulture) + " s", plane, seconds,
                        () => show(null, true), () => show(waiting, false), "send", taken));
                }

                // The menu opened from its bar alone, its buttons settled long before: they wait to settle again and
                // take nothing until it has opened.
                closed();
                var menuView = FindView(plane, "Menu");
                if (menuView != null) SettleLongAgo(menuView);
                var menuSince = UnityEngine.Time.unscaledTime;
                show(null, false);
                if (menuView != null && plane.Shown.Any(column => column.Kind == MenuColumn.Menu))
                {
                    var since = menuSince;
                    if (!plane.Opening(MenuColumn.Menu)) failures.Add(name + ": the menu just opened is not opening.");
                    failures.AddRange(WaitsToSettle(name + ": the menu just opened", menuView));
                    NothingTaken(name + ": the menu just opened", menuView, since);
                    Taken(name + ": the menu that has opened", menuView,
                        menuView.GetComponentsInChildren<GlazeButton>().FirstOrDefault(button => !button.Static && button.Available && menuView.Footer.Shown.All(slot => slot.Button != button)), MenuColumn.Menu);
                }
                else failures.Add(name + ": the menu opened from its bar does not stand on the plane.");

                // A side panel opening, its buttons settled long before, carrying a Yes that is safe in place (as Start
                // over's) where it stands in its file's place: it waits to settle and takes nothing until it has opened.
                var startOver = new Prompt("yes", EntryText.ConfirmStartOver, GlazeIcon.StartOver, PromptKind.Yes, safeInPlace: true);
                var cancel = new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel);
                MenuFrame Asking(MenuFrame on) => new MenuFrame(on.Subject, Footer.Confirm(on.Footer, PromptSlot.FarRight, startOver, cancel),
                    on.SubjectIsData, on.Pill, on.Sections, on.Lines, on.Source, on.Side, on.SourceIsData, on.SubjectWaits);
                show(Asking(waiting), true);
                plane.Advance(ended);
                var sideView = FindView(plane, "Side panel");
                if (sideView != null) SettleLongAgo(sideView);
                var sideSince = UnityEngine.Time.unscaledTime;
                show(Asking(chosen), false);
                if (sideView != null && plane.Shown.Any(column => column.Kind == MenuColumn.Side))
                {
                    var since = sideSince;
                    var carries = ButtonFor(sideView, "yes");
                    var inPlace = plane.Shown.Count == 1;
                    var what = name + ": a side panel just opened" + (inPlace ? " in its file's place" : " beside its file") + (carries != null ? ", carrying Yes" : "");
                    if (inPlace && carries == null) failures.Add(what + " does not carry the file's Yes, safe in place.");
                    if (!plane.Opening(MenuColumn.Side)) failures.Add(what + " is not opening.");
                    failures.AddRange(WaitsToSettle(what, sideView));
                    NothingTaken(what, sideView, since);
                    Taken(name + ": a side panel that has opened", sideView, carries ?? sideView.Footer.Shown.Select(slot => slot.Button).FirstOrDefault(), MenuColumn.Side);
                }
                else failures.Add(name + ": a chosen answer's side panel does not stand on the plane.");
            }
            finally
            {
                plane.Acted -= acted;
                plane.HoldStarted -= held;
                plane.SubjectPressed -= subject;
                plane.SubjectHeld -= subjectHeld;
                plane.Drawn -= laid;
                GlazeButton.AnyPressed -= anyPressed;
            }
            return failures;
        }

        /// <summary>
        /// A file opened after <paramref name="before"/> by <paramref name="open"/>, stepped frame by frame at
        /// <paramref name="frameSeconds"/>, its prompt <paramref name="asks"/> pressed as a hand presses it every
        /// frame with its own settling long past: the opening shows its shapes and its words partly faded, top to
        /// bottom, each part as drawn following its fade on the view's one clock of its opening, its light line
        /// drawing from the character where one stands; and the first press taken finds every shape and every word
        /// on the file whole, nothing on the plane still sliding, and that clock past the end of every part's fade,
        /// so the opening never ends on a pop before its fade has run.
        /// </summary>
        private static IEnumerable<string> FirstPressFindsItWhole(string what, MenuPlane plane, float frameSeconds, System.Action before, System.Action open, string asks,
            List<string> taken)
        {
            var failures = new List<string>();
            before();
            plane.Advance(Mathf.Max(Glaze.SlideSeconds, MenuPlane.OpeningSeconds));
            open();
            var view = FindView(plane, "File");
            var button = view != null ? ButtonFor(view, asks) : null;
            if (view == null || button == null || !view.gameObject.activeSelf)
            {
                failures.Add(what + ": no file asking " + asks + " stands on the plane.");
                return failures;
            }
            var faintShapes = false;
            var faintWords = false;
            var lineDrew = false;
            var offClock = false;
            for (var frame = 0; frame < 200; frame++)
            {
                SettleLongAgo(view);
                view.SettleSubjectForRender();
                taken.Clear();
                var at = button.transform.position;
                button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Select, at);
                button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Unselect, at);
                button.Target.PointerForRender(7, Oculus.Interaction.PointerEventType.Unhover, at);
                if (taken.Any(each => each.StartsWith(MenuColumn.File + " ", System.StringComparison.Ordinal)))
                {
                    failures.AddRange(NotWhole(what + ", at the first press taken", view));
                    if (plane.Sliding) failures.Add(what + ": at the first press taken, parts of the plane are still sliding to their places.");
                    if (plane.LightLine != null && plane.LineShown < 1f) failures.Add(what + ": at the first press taken, its light line has not reached the file.");
                    for (var index = 0; index < view.Parts.Count; index++)
                    {
                        var due = Glaze.PartShown(view.MovedFor, index, view.DrawsFromLine);
                        if (due < 0.999f)
                        {
                            failures.Add(what + ": its opening ended " + view.MovedFor.ToString("0.000", CultureInfo.InvariantCulture) + " s in, with its " + view.Parts[index].name
                                + " faded in only to " + due.ToString("0.000", CultureInfo.InvariantCulture) + ", so it shows whole on a jump the moment it takes presses.");
                        }
                    }
                    if (!faintShapes) failures.Add(what + ": no frame showed a part's shapes partly faded, so they never assembled.");
                    if (!faintWords) failures.Add(what + ": no frame showed a part's words partly faded, so they never assembled.");
                    if (plane.LightLine != null && !lineDrew) failures.Add(what + ": its light line stood whole from the first frame; it draws from the character.");
                    return failures;
                }
                var parts = view.Parts;
                for (var index = 0; index < parts.Count; index++)
                {
                    var (shapes, words) = ShapesAndWordsShown(parts[index]);
                    if (shapes > 0f && shapes < 1f) faintShapes = true;
                    if (words > 0f && words < 1f) faintWords = true;
                    var shown = Mathf.Min(shapes, words);
                    if (index > 0 && shown > PartShownAsDrawn(parts[index - 1]) + 1e-4f) failures.Add(what + ": " + parts[index].name + " shows more than the part above it; it assembles top to bottom.");
                    // Drawn as the one clock of its opening says, its shapes and its words alike.
                    var due = view.Opening ? Glaze.PartShown(view.MovedFor, index, view.DrawsFromLine) : 1f;
                    if (!offClock && (Mathf.Abs(shapes - due) > 0.01f || Mathf.Abs(words - due) > 0.01f))
                    {
                        offClock = true;
                        failures.Add(what + ": " + view.MovedFor.ToString("0.000", CultureInfo.InvariantCulture) + " s into its opening, its " + parts[index].name + " is drawn at "
                            + shapes.ToString("0.000", CultureInfo.InvariantCulture) + " (shapes) and " + words.ToString("0.000", CultureInfo.InvariantCulture)
                            + " (words), not the " + due.ToString("0.000", CultureInfo.InvariantCulture) + " its opening's clock gives.");
                    }
                }
                if (plane.LightLine != null && plane.LineShown < 1f) lineDrew = true;
                plane.FrameForRender(frameSeconds);
            }
            failures.Add(what + ": " + asks + " was never taken in 200 frames.");
            return failures;
        }

        /// <summary>How visible <paramref name="part"/> is as drawn: the least of its shown shapes and words.</summary>
        private static float PartShownAsDrawn(Transform part)
        {
            var (shapes, words) = ShapesAndWordsShown(part);
            return Mathf.Min(shapes, words);
        }

        /// <summary>How visible <paramref name="part"/>'s shown shapes are as drawn, the least of them, and its shown words; 1 for none.</summary>
        private static (float Shapes, float Words) ShapesAndWordsShown(Transform part)
        {
            var shapes = 1f;
            var words = 1f;
            foreach (var shape in part.GetComponentsInChildren<Surface>()) shapes = Mathf.Min(shapes, shape.Shown);
            foreach (var label in part.GetComponentsInChildren<TMP_Text>()) words = Mathf.Min(words, GlazeText.ShownOf(label));
            return (shapes, words);
        }

        /// <summary>Every shape and word <paramref name="view"/> shows that is not drawn whole.</summary>
        private static IEnumerable<string> NotWhole(string what, MenuFrameView view) => view.Parts
            .Where(part => PartShownAsDrawn(part) < 1f)
            .Select(part => what + ": its " + part.name + " is drawn at " + PartShownAsDrawn(part).ToString("0.000", CultureInfo.InvariantCulture) + " of its opacity.");

        /// <summary>
        /// A short tap on a hold prompt, let go before its hold starts, counts as a press only of what the button
        /// still is: taken on a settled Hold to talk, and dropped where the button took new words and a new kind
        /// under the hand, as a slot turning into Yes.
        /// </summary>
        private static IEnumerable<string> TapCountsWhatItStillIs(string name, Transform parent)
        {
            var failures = new List<string>();
            var holder = new GameObject("Tap check");
            holder.transform.SetParent(parent, false);
            try
            {
                var button = GlazeButton.Create(holder.transform, "Prompt", ButtonRole.Prompt);
                button.Holds = true;
                button.ShowPrompt("Hold to talk", GlazeIcon.HoldToTalk, Vector2.zero, button.MeasurePrompt("Hold to talk", false), false);
                var presses = 0;
                button.Pressed += () => presses++;
                void Tap(System.Action? underTheHand)
                {
                    ShownAt.SetValue(button, SettledLongAgo);
                    var at = button.transform.position;
                    button.Target.PointerForRender(9, Oculus.Interaction.PointerEventType.Select, at);
                    underTheHand?.Invoke();
                    button.Target.PointerForRender(9, Oculus.Interaction.PointerEventType.Unselect, at);
                    button.Target.PointerForRender(9, Oculus.Interaction.PointerEventType.Unhover, at);
                }
                Tap(null);
                if (presses != 1) failures.Add(name + ": a short tap on a settled Hold to talk was taken " + presses + " times, not once.");
                presses = 0;
                Tap(() =>
                {
                    button.Holds = false;
                    button.ShowPrompt("Yes", GlazeIcon.Approve, Vector2.zero, button.MeasurePrompt("Yes", true), true);
                });
                if (presses > 0) failures.Add(name + ": a short tap begun on Hold to talk, let go once the button had turned into Yes, pressed Yes.");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
            return failures;
        }

        /// <summary>
        /// A frame of a file's opening, and of its closing, allocates nothing (ADR 0027): twenty frames of each, each
        /// at another point of it, a frame failing only where it allocated in every try (<see cref="GlazeChecks.Allocations"/>).
        /// </summary>
        private static IEnumerable<string> OpeningAllocatesNothing(string name, MenuPlane plane, System.Action before, System.Action open)
        {
            var failures = new List<string>();
            before();
            plane.Advance(Mathf.Max(Glaze.SlideSeconds, MenuPlane.OpeningSeconds));
            open();
            var view = FindView(plane, "File");
            if (view == null || !view.gameObject.activeSelf)
            {
                failures.Add(name + ": no file stands on the plane to open.");
                return failures;
            }
            foreach (var closing in new[] { false, true })
            {
                var what = closing ? "closing" : "opening";
                // Each frame somewhere short of the end, so the frame neither ends it nor reports the page drawn.
                var span = closing ? Glaze.LeaveSeconds : view.OpeningSeconds;
                if (!(GlazeChecks.Allocations(step =>
                    {
                        view.MovedForRender(span * 0.8f * step / 20f, closing);
                        plane.FrameForRender(1f / 72f);
                    }, 20) is (int every, int some, long bytes)))
                {
                    failures.Add(name + ": this editor cannot count allocations, so a file's " + what + " cannot be checked.");
                    continue;
                }
                if (every > 0) failures.Add(name + ": " + every + " of twenty frames of a file's " + what + " allocate in each of 3 tries; a frame of it allocates nothing.");
                Debug.Log("Halcyonic: workspace render " + name + ": twenty frames of a file's " + what + ": " + every + " allocate in every try, " + some + " in some; the quietest try counts "
                    + bytes + " bytes on every thread.");
            }
            view.Stop();
            return failures;
        }

        /// <summary>
        /// A file assembling and fading away, as the eyes see it: one strip of moments of its opening, from the
        /// press, the light line drawing from its character and the parts fading in from the top, then of its
        /// closing. Read from the captured pixels, as the eyes see them: each part's largest shape, its glass, a
        /// tab or a well, at least <see cref="SeenFadeLevels"/> of 255 dimmer at the press than once the file
        /// shows whole, and as dim again near the end of its closing.
        /// </summary>
        private static IEnumerable<string> OpeningStrip(string what, string folder, Camera camera, RenderTexture texture, MenuPlane plane, System.Action before,
            System.Action open, System.Action close)
        {
            var failures = new List<string>();
            before();
            plane.Advance(Mathf.Max(Glaze.SlideSeconds, MenuPlane.OpeningSeconds));
            open();
            var view = FindView(plane, "File");
            var frames = new List<Texture2D>();
            var rotation = camera.transform.rotation;
            camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
            try
            {
                var at = 0f;
                Texture2D? pressed = null;
                Texture2D? whole = null;
                foreach (var moment in new[] { 0f, 0.05f, 0.12f, 0.18f, 0.24f, 0.3f, MenuPlane.OpeningSeconds })
                {
                    plane.Advance(moment - at);
                    at = moment;
                    ForceMeshes(plane.gameObject);
                    frames.Add(Render(camera, texture));
                    if (moment == 0f) pressed = frames[frames.Count - 1];
                    whole = frames[frames.Count - 1];
                }
                // Where each part's shapes stand on the render, the area they cover together, read once the file shows whole.
                var shapes = new List<(string Part, RectInt Rect)>();
                foreach (var part in view?.Parts ?? new List<Transform>())
                {
                    var covered = part.GetComponentsInChildren<Surface>().Where(shape => shape.Size.x > 0f && shape.Size.y > 0f)
                        .Select(shape => Around(camera, shape.transform, Vector2.one, 0.5f, 0.5f)).ToList();
                    if (covered.Count == 0)
                    {
                        failures.Add(what + ": the " + part.name + " has no shape to read on the render.");
                        continue;
                    }
                    var leftmost = covered.Min(rect => rect.xMin);
                    var lowest = covered.Min(rect => rect.yMin);
                    var area = new RectInt(leftmost, lowest, covered.Max(rect => rect.xMax) - leftmost, covered.Max(rect => rect.yMax) - lowest);
                    area.ClampToBounds(new RectInt(0, 0, Size, Size));
                    shapes.Add((part.name, area));
                }
                close();
                // The bar shows at once where the file's subject stood: what it covers is read from outside it.
                var bar = plane.Bar is MenuBarView closed ? Around(camera, closed.transform, closed.Size, 0.6f, 0.9f) : new RectInt();
                at = 0f;
                Texture2D? closing = null;
                foreach (var moment in new[] { 0.06f, 0.1f, 0.13f, 0.148f })
                {
                    plane.Advance(moment - at);
                    at = moment;
                    frames.Add(Render(camera, texture));
                    closing = frames[frames.Count - 1];
                }
                plane.Advance(Glaze.LeaveSeconds);
                foreach (var (part, rect) in shapes)
                {
                    var shown = Brightness(whole!, rect);
                    var atPress = Brightness(pressed!, rect);
                    var uncovered = Brightness(whole!, rect, bar);
                    var nearlyGone = Brightness(closing!, rect, bar);
                    if (Uncovered(rect, bar) < rect.width * rect.height / 5)
                    {
                        Debug.Log("Halcyonic: workspace render " + what + ": the " + part + " stands under the bar as it closes, so its closing is not read.");
                        uncovered = nearlyGone = float.NaN;
                    }
                    Debug.Log("Halcyonic: workspace render " + what + ": the " + part + "'s shape reads " + atPress.ToString("0", CultureInfo.InvariantCulture) + " at the press, "
                        + shown.ToString("0", CultureInfo.InvariantCulture) + " whole and " + nearlyGone.ToString("0", CultureInfo.InvariantCulture) + " near the end of its closing, of 255.");
                    if (shown - atPress < SeenFadeLevels) failures.Add(what + ": as the eyes see it, the " + part + "'s shape is only " + (shown - atPress).ToString("0", CultureInfo.InvariantCulture)
                        + " of 255 dimmer at the press than whole; it does not fade in.");
                    if (uncovered - nearlyGone < SeenFadeLevels) failures.Add(what + ": as the eyes see it, the " + part + "'s shape, outside the bar, is only "
                        + (uncovered - nearlyGone).ToString("0", CultureInfo.InvariantCulture) + " of 255 dimmer near the end of its closing than whole; it does not fade away.");
                }
                var strip = new Texture2D(frames.Sum(frame => frame.width), frames.Max(frame => frame.height), TextureFormat.RGBA32, false);
                var left = 0;
                foreach (var frame in frames)
                {
                    strip.SetPixels(left, 0, frame.width, frame.height, frame.GetPixels());
                    left += frame.width;
                }
                strip.Apply();
                File.WriteAllBytes(Path.Combine(folder, what.Replace(' ', '-') + ".png"), strip.EncodeToPNG());
                Object.DestroyImmediate(strip);
            }
            finally
            {
                camera.transform.rotation = rotation;
                foreach (var frame in frames) Object.DestroyImmediate(frame);
            }
            return failures;
        }

        /// <summary>How much dimmer, of 255, a part's shape must read as the eyes see it before it opens, and near the end of its closing, than whole.</summary>
        private const float SeenFadeLevels = 30f;

        /// <summary>
        /// How bright <paramref name="rect"/> reads on <paramref name="image"/>, outside <paramref name="skip"/>: the
        /// mean of each pixel's brightest channel, of 255.
        /// </summary>
        private static float Brightness(Texture2D image, RectInt rect, RectInt skip = default)
        {
            var sum = 0f;
            var count = 0;
            for (var y = rect.yMin; y < rect.yMax; y++)
            {
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    if (skip.Contains(new Vector2Int(x, y))) continue;
                    var pixel = image.GetPixel(x, y);
                    sum += Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                    count++;
                }
            }
            return count == 0 ? 0f : 255f * sum / count;
        }

        /// <summary>How many pixels of <paramref name="rect"/> stand outside <paramref name="skip"/>.</summary>
        private static int Uncovered(RectInt rect, RectInt skip)
        {
            var count = 0;
            for (var y = rect.yMin; y < rect.yMax; y++)
            {
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    if (!skip.Contains(new Vector2Int(x, y))) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// A shape <paramref name="size"/> big in its own units on the render, out to <paramref name="across"/> and
        /// <paramref name="up"/> of its size either side of its centre: half for the shape itself, more for room around
        /// it, as a bar's pill and soft edge.
        /// </summary>
        private static RectInt Around(Camera camera, Transform shape, Vector2 size, float across, float up)
        {
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) }
                .Select(corner => camera.WorldToScreenPoint(shape.TransformPoint(new Vector3(corner.x * size.x * across, corner.y * size.y * up, 0f))))
                .ToList();
            var left = Mathf.FloorToInt(corners.Min(corner => corner.x));
            var bottom = Mathf.FloorToInt(corners.Min(corner => corner.y));
            return new RectInt(left, bottom, Mathf.CeilToInt(corners.Max(corner => corner.x)) - left, Mathf.CeilToInt(corners.Max(corner => corner.y)) - bottom);
        }

        /// <summary>The button <paramref name="view"/>'s footer shows for the prompt <paramref name="id"/>, if it shows one.</summary>
        private static GlazeButton? ButtonFor(MenuFrameView view, string id)
        {
            foreach (var (slot, button) in view.Footer.Shown)
            {
                if (view.Footer.Showing?[slot]?.Id == id) return button;
            }
            return null;
        }

        /// <summary>
        /// A part seen a quarter of its move's own time in stands <paramref name="expected"/> of its way from
        /// <paramref name="from"/> to <paramref name="to"/> (ADR 0027): its kind's time and easing, which a
        /// straight line between the two shows exactly.
        /// </summary>
        private static IEnumerable<string> EasedAlong(string what, Vector3 from, Vector3 quarter, Vector3 to, float expected)
        {
            var way = Vector3.Distance(from, to);
            if (way < 0.001f) yield break;
            var along = Vector3.Distance(from, quarter) / way;
            if (Mathf.Abs(along - expected) > 0.01f)
            {
                yield return what + " stood " + (along * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " percent of its way a quarter of its time in, not "
                    + (expected * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " as its kind eases.";
            }
        }

        /// <summary>A browser video window of a typical size, 1.4 by 0.79 m at 1.6 m, centred at eye level, as AmbientRender's.</summary>
        private static Transform Window(Transform parent, Vector3 eyes)
        {
            var window = GameObject.CreatePrimitive(PrimitiveType.Quad);
            window.name = "Video window";
            Object.DestroyImmediate(window.GetComponent<Collider>());
            window.transform.SetParent(parent, false);
            window.transform.SetPositionAndRotation(eyes + Vector3.forward * 1.6f, Quaternion.identity);
            window.transform.localScale = new Vector3(1.4f, 0.79f, 1f);
            window.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.25f, 0.3f, 0.4f) };
            return window.transform;
        }

        private static MenuFrameView? FindView(MenuPlane plane, string name) => plane.GetComponentsInChildren<MenuFrameView>(true).FirstOrDefault(view => view.name == name);

        /// <summary>When a button last took a new action, after which its presses count once it settles.</summary>
        private static readonly FieldInfo ShownAt = typeof(GlazeButton).GetField("shownAt", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingFieldException(nameof(GlazeButton), "shownAt");

        private const float SettledLongAgo = -100f;

        /// <summary>Every button of <paramref name="view"/>, shown or not, as if it took its action long ago and settled.</summary>
        private static void SettleLongAgo(MenuFrameView view)
        {
            foreach (var button in view.GetComponentsInChildren<GlazeButton>(true)) ShownAt.SetValue(button, SettledLongAgo);
        }

        /// <summary>Every button <paramref name="view"/> shows waits to settle again, as one sliding back onto the plane must.</summary>
        private static IEnumerable<string> WaitsToSettle(string what, MenuFrameView view) => view.GetComponentsInChildren<GlazeButton>()
            .Where(button => (float)ShownAt.GetValue(button)! == SettledLongAgo)
            .Select(button => what + ": " + button.name + " takes a press as it slides back, before it settles.");

        private static readonly Prompt PlaneClose = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);

        /// <summary>Settings with Text size chosen: its details beside it, its change the main action, safe on them.</summary>
        private static MenuFrame SettingChosen() => new MenuFrame(SettingsText.Subject,
            new Footer(PlaneClose, farRight: new Prompt("change", "Make text larger", GlazeIcon.Change, main: true, safeInPlace: true)),
            sections: MenuBar.Places.Select(place => new FrameSection(place.ToString(), MenuBar.Word(place), chosen: place == MenuPlace.Settings)).ToList(),
            lines: new[]
            {
                new PageLine("Comfort", tone: LineTone.Secondary),
                new PageLine("Text size", fact: "Standard", action: "settings-open-setting", key: "text-size", opens: true, chosen: true),
                new PageLine("Motion", fact: "On", action: "settings-open-setting", key: "moving-badges", opens: true),
            },
            side: new SidePanel("Text size", facts: new[] { new SideFact("Now", "The standard size"), new SideFact("A step larger", "Text 15 percent larger, and 3 rows a page") }));

        /// <summary>Tasks: the waiting task chosen, <paramref name="count"/> rows, as many as the stage holds beside a file.</summary>
        private static MenuFrame Tasks(TextSize text, string waiting, int count)
        {
            var rows = new[]
            {
                new PageLine(waiting, wordsAreData: true, icon: GlazeIcon.WaitingForYou, fact: "Storefront API", factIsData: true, tone: LineTone.Waiting, action: "open-task", key: "t3", opens: true, chosen: true),
                new PageLine("Paginate the order history endpoint", wordsAreData: true, icon: GlazeIcon.Working, fact: "Storefront API", factIsData: true, action: "open-task", key: "t0", opens: true),
                new PageLine("Send an order confirmation email", wordsAreData: true, icon: GlazeIcon.FinishedThisRound, fact: "Storefront API", factIsData: true, tone: LineTone.Good, action: "open-task", key: "t1", opens: true),
                new PageLine("Refresh the checkout copy", wordsAreData: true, icon: GlazeIcon.CheckingItsWork, fact: "Docs site", factIsData: true, action: "open-task", key: "t2", opens: true),
            };
            var places = MenuBar.Places.Select(place => new FrameSection(place.ToString(), MenuBar.Word(place), chosen: place == MenuPlace.Tasks, waits: place == MenuPlace.Tasks)).ToList();
            return new MenuFrame("1 task is waiting for you", new Footer(PlaneClose).WithNext(new Prompt(Footer.NextPage, "Next page", GlazeIcon.Next, PromptKind.NextPage)),
                subjectWaits: true, sections: places, lines: rows.Take(Mathf.Min(count, MenuFrame.RowsAPage(text, sourceLine: false))).ToList());
        }

        /// <summary>
        /// The waiting task's file: its question and answers, one cut to fit; chosen, it slides out its side
        /// panel with all its words. Packed against <paramref name="budget"/>, the height its page holds on
        /// this stage, as the screens pack it: what doesn't fit goes to the next page.
        /// </summary>
        private static MenuFrame WaitingFile(string title, StateBadge badge, bool chosen, float budget)
        {
            const string cut = "Lock the account for 15 minutes after five failed sign-ins, then email its owner a link to reset the password and unlock it at once";
            var sections = new[] { "Waiting", "Activity", "Changes", "Checks" }.Select(words => new FrameSection(words, words, chosen: words == "Waiting", waits: words == "Waiting")).ToList();
            var send = new Prompt("send", "Send answer", GlazeIcon.SendAnswer, main: true);
            var talk = new Prompt("talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true);
            return new MenuFrame(title, new Footer(PlaneClose, secondary: talk, farRight: send), subjectIsData: true, pill: badge, sections: sections,
                lines: Packed(new[]
                {
                    new PageLine("How long should a sign-in lockout last?", wordsAreData: true, chip: "Agent says", claim: true, rows: 2),
                    new PageLine("15 minutes", wordsAreData: true, action: "answer", key: "0", choice: true),
                    new PageLine("1 hour", wordsAreData: true, action: "answer", key: "1", choice: true),
                    new PageLine(cut, wordsAreData: true, action: "answer", key: "2", choice: true, chosen: chosen, rows: 2),
                    new PageLine("Type my answer", icon: GlazeIcon.Type, action: "answer", key: "own", choice: true),
                }, budget),
                side: chosen ? new SidePanel("The whole answer", lines: new[] { new PageLine(cut, wordsAreData: true, claim: true, rows: 4) }, source: "As the agent offered it") : null);
        }

        /// <summary>
        /// The lines that fit <paramref name="budget"/> from the top, measured as the client core measures
        /// them (<see cref="MenuPage"/>): each line its rows, two short answers sharing a row, 12 mm between
        /// targets and a grid step elsewhere. The first four always stand, as this page needs them.
        /// </summary>
        private static IReadOnlyList<PageLine> Packed(PageLine[] lines, float budget)
        {
            var kept = new List<PageLine>();
            var used = 0f;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var paired = index + 1 < lines.Length && line.Choice && lines[index + 1].Choice
                    && MenuFrameView.FitsHalf(line, Glaze.Menu.FileColumnDegrees) && MenuFrameView.FitsHalf(lines[index + 1], Glaze.Menu.FileColumnDegrees);
                var rows = Mathf.Min(line.Rows, MenuFrameView.RowsOf(line, Glaze.Menu.FileColumnDegrees));
                var height = line.Action == null ? MenuPage.Words(rows) : MenuPage.Target(rows);
                var gap = kept.Count == 0 ? 0f : kept[kept.Count - 1].Action != null && line.Action != null ? MenuPage.TargetGap : MenuPage.Grid;
                if (index >= 4 && used + gap + height > budget) break;
                used += gap + height;
                kept.Add(line);
                if (paired) kept.Add(lines[++index]);
            }
            return kept;
        }

        /// <summary>One state of the plane, checked and rendered as the eyes see it, aimed at its centre.</summary>
        /// <param name="lightLine">The column beside the menu is a task's file, joined to its character by the light line; false for New project, which has no character.</param>
        private static IEnumerable<string> PlaneState(string what, string folder, Camera camera, RenderTexture texture, MenuPlane plane,
            List<(CharacterView View, CharacterTarget Target)> characters, Vector3 eyes, Transform? window, bool lightLine = true)
        {
            var failures = new List<string>();
            // A still shows the plane as it stands once every column has opened and what closed has gone.
            plane.Advance(MenuPlane.OpeningSeconds);
            ForceMeshes(plane.gameObject);
            var columns = new List<IReadOnlyList<GlazeChecks.PlaneShape>>();
            if (plane.Composition is PlaneComposition composition)
            {
                for (var c = 0; c < plane.Shown.Count; c++)
                {
                    var view = plane.Shown[c].View;
                    var placed = composition.Parts.Where(part => part.Column == c).ToList();
                    columns.Add(view.Parts.Select((part, index) => new GlazeChecks.PlaneShape(view.name + " " + part.name, part,
                        new Vector2(placed[index].Width, placed[index].Height) * PlaneComposition.Distance)).ToList());
                }
            }
            else if (plane.Bar is MenuBarView bar)
            {
                columns.Add(new[] { new GlazeChecks.PlaneShape("the closed bar", bar.transform, bar.Size * bar.transform.localScale.x) });
            }
            failures.AddRange(GlazeChecks.OnePlane(columns, eyes, what));
            failures.AddRange(GlazeChecks.TypeStepsDown(columns, eyes, what));
            failures.AddRange(GlazeChecks.OneSelectionTreatment(plane.GetComponentsInChildren<Surface>(false), eyes, what));
            failures.AddRange(GlazeChecks.TextAsSeen(plane.gameObject, eyes, what));
            var buttons = plane.Shown.SelectMany(column => column.View.Targets).Concat(plane.Bar != null ? new[] { plane.Bar.Target } : new GlazeButton[0]).ToList();
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons, eyes, what));
            foreach (var (_, view) in plane.Shown)
            {
                if (!view.Footer.Fits) failures.Add(what + ": " + view.name + "'s footer does not fit its column.");
                failures.AddRange(GlazeChecks.GlowEndsAboveTargets(view.Content, view.Targets, what));
            }

            // A degree from every label and body, as the eyes see them, and inside the measured field.
            var shapes = columns.SelectMany(column => column).ToList();
            var others = characters.Where(character => character.View.gameObject.activeInHierarchy)
                .SelectMany(character => new[] { GlazeChecks.Of(character.View.WorkstreamId + "'s label", eyes, character.View.Label.gameObject), BodyExtent(character.View, eyes) })
                .ToList();
            foreach (var shape in shapes)
            {
                var seen = GlazeChecks.Of(shape.Name, eyes, shape.Root.gameObject);
                foreach (var other in others)
                {
                    var apart = GlazeChecks.OutlineApart(shape, eyes, other);
                    if (apart < GlazeChecks.GapDegrees - 0.01f) failures.Add(what + ": " + shape.Name + " is " + GlazeChecks.Degrees(apart) + " degrees from " + other.Name + "; a degree at least: " + seen + ", " + other + ".");
                }
            }
            if (ViewField.Current is ViewField field && shapes.Count > 0)
            {
                var corners = shapes.SelectMany(shape => new[] { -0.5f, 0.5f }.SelectMany(x => new[] { -0.5f, 0.5f }
                    .Select(y => shape.Root.position + shape.Root.right * (x * shape.Size.x) + shape.Root.up * (y * shape.Size.y)))).ToList();
                var size = plane.Composition?.Size ?? new PanelSize(PlaneComposition.Distance, 0f, 0f);
                failures.AddRange(GlazeChecks.InsideField(what, corners, eyes, GlazeChecks.CompositionCenter(shapes), WorkspacePlacement.ReadingPitch(size, plane.Direction.Elevation, field), field));
            }

            // Every prompt the frame in front offers is drawn somewhere on the plane, so nothing a chosen
            // row offers, as a setting's change, is out of reach; or, on its side panel in the frame's place,
            // where only what the panel shows everything of is carried, and paging only of its own parts,
            // Close details is drawn to bring the page back. A side panel in its frame's place says its
            // frame's own reason, of what it carries.
            if (plane.Front is MenuFrame front)
            {
                var drawn = plane.Shown.SelectMany(column => column.View.Footer.Showing?.All.Select(each => each.Prompt.Id) ?? Enumerable.Empty<string>()).ToHashSet();
                var inPlace = plane.Shown.Count == 1 && plane.Shown[0].Kind == MenuColumn.Side ? plane.Shown[0].View.Side : null;
                var carried = inPlace != null ? front.Footer.InPlace(inPlace) : null;
                foreach (var (_, prompt) in front.Footer.All)
                {
                    if (prompt.Kind == PromptKind.Close || drawn.Contains(prompt.Id)) continue;
                    if (carried == null || carried.All.Any(each => each.Prompt.Id == prompt.Id) || !drawn.Contains(SidePanel.Close))
                    {
                        failures.Add(what + ": the frame in front offers \"" + prompt.Words + "\", but nothing on the plane draws it, nor Close details to bring the page back.");
                    }
                }
                var said = carried?.Reason;
                if (inPlace != null && plane.Shown[0].View.ReasonShown != said)
                {
                    failures.Add(what + ": the side panel in its frame's place says \"" + plane.Shown[0].View.ReasonShown + "\", not its frame's own reason \"" + said + "\".");
                }
            }
            // No frame drawn acts on a side panel that isn't: a chosen row's details are drawn with it.
            foreach (var (_, view) in plane.Shown)
            {
                if (view.Frame?.Side is SidePanel details && !plane.Shown.Any(column => column.View.Side == details))
                {
                    failures.Add(what + ": " + view.name + " shows a chosen row whose side panel isn't drawn, so its footer would act on details no one sees.");
                }
            }

            // The light line crosses no label and no character, seen from the eyes: a label by its own
            // outline, since a flat label's bottom edge rises toward its ends as seen from below, which a
            // box round it in angles would count as crossed; a body by its extent.
            if (plane.LightLine is (Vector3 from, Vector3 to))
            {
                foreach (var character in characters.Where(character => character.View.gameObject.activeInHierarchy))
                {
                    var label = MenuPlane.LabelOutline(character.View);
                    var body = BodyExtent(character.View, eyes);
                    for (var step = 1; step < 40; step++)
                    {
                        var at = Vector3.Lerp(from, to, step / 40f);
                        var point = at - eyes;
                        var across = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
                        var up = Mathf.Atan2(point.y, new Vector2(point.x, point.z).magnitude) * Mathf.Rad2Deg;
                        var onBody = across >= body.Left && across <= body.Right && up >= body.Bottom && up <= body.Top;
                        if (!onBody && !MenuPlane.OnLabel(label, eyes, at)) continue;
                        failures.Add(what + ": the light line crosses " + (onBody ? body.Name : character.View.WorkstreamId + "'s label")
                            + "; it leaves from under its label and crosses no label or character.");
                        break;
                    }
                }
            }
            else if (lightLine && window == null && plane.Shown.Any(column => column.Kind == MenuColumn.File)) failures.Add(what + ": a file shows without its light line.");
            if (window != null)
            {
                // Beside a window nothing of the plane comes within a degree of it, and no light line runs across it.
                var seenWindow = GlazeChecks.Of("the video window", eyes, window.gameObject);
                foreach (var shape in shapes)
                {
                    var apart = GlazeChecks.OutlineApart(shape, eyes, seenWindow);
                    if (apart < GlazeChecks.GapDegrees - 0.01f) failures.Add(what + ": " + shape.Name + " comes within " + GlazeChecks.Degrees(apart) + " degrees of the video window.");
                }
                if (plane.LightLine != null) failures.Add(what + ": a light line shows beside a window, where it would run across it.");
            }

            // As the eyes see it, aimed at the plane's centre.
            var rotation = camera.transform.rotation;
            camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
            var image = Render(camera, texture);
            camera.transform.rotation = rotation;
            File.WriteAllBytes(Path.Combine(folder, what.Replace(' ', '-') + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            Debug.Log("Halcyonic: workspace render " + what + ": the plane's centre is " + GlazeChecks.Degrees(plane.Direction.Elevation) + " degrees from eye level, "
                + (plane.Direction.Clear ? "clear of" : "over") + " the characters it passes; " + plane.Shown.Count + " columns.");
            if (!plane.Direction.Clear) failures.Add(what + ": the plane covers a character or its label.");
            return failures;
        }
    }
}
