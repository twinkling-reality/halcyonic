#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

                // The menu on Tasks beside the waiting task's file.
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface, immediately: true, besideWindow: besideWindow);
                failures.AddRange(PlaneState(name + " menu and file", folder, camera, texture, plane, characters, eyes, window));
                // Where the file is too tall beside Tasks here, the menu stands aside from the start.
                var menuPlaced = plane.Shown.Where(column => column.Kind == MenuColumn.Menu).SelectMany(column => column.View.Parts).Select(part => part.position).ToList();
                if (menuPlaced.Count == 0) Debug.Log("Halcyonic: workspace render " + name + ": the file is too tall to stand beside Tasks here, so the menu stands aside.");
                var fileBefore = plane.Shown.First(column => column.Kind == MenuColumn.File).View.Subject.position;
                if (besideWindow && plane.Shown.Count != 1) failures.Add(name + ": beside a window the menu and a file stand together; they stand one at a time.");

                // A cut answer chosen: its side panel slides out, and the menu steps aside to the left.
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: true, budget), opened.Target, targets, eyes, looking, surface, besideWindow: besideWindow);
                plane.Advance(MenuPlane.SlideSeconds / 2f);
                var menuView = plane.Shown.All(column => column.Kind != MenuColumn.Menu) ? FindView(plane, "Menu") : null;
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
                plane.Advance(MenuPlane.SlideSeconds);
                if (menuView != null && menuView.gameObject.activeSelf) failures.Add(name + ": the menu still shows after stepping aside.");
                if (plane.Shown.Count > 2) failures.Add(name + ": " + plane.Shown.Count + " columns stand on the plane; there are never three.");
                if (plane.Shown.All(column => column.Kind != MenuColumn.Side)) failures.Add(name + ": the chosen answer's side panel does not show.");
                if (GlazeText.Scale > 1f && plane.Shown.Count != 1) failures.Add(name + ": with larger text the side panel stands beside its file; it takes the file's place.");
                if (fileHalf.Count == 0) Debug.Log("Halcyonic: workspace render " + name + ": the side panel stands in the file's place.");
                failures.AddRange(PlaneState(name + " file and side panel", folder, camera, texture, plane, characters, eyes, window));

                // Close details: the menu comes back where it was.
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface, besideWindow: besideWindow);
                plane.Advance(MenuPlane.SlideSeconds / 2f);
                if (plane.MenuAside != (menuPlaced.Count == 0)) failures.Add(name + ": the side panel closed, and the menu stands " + (plane.MenuAside ? "aside" : "beside the file") + " where it stood the other way before.");
                plane.Advance(MenuPlane.SlideSeconds);
                var back = plane.Shown.Where(column => column.Kind == MenuColumn.Menu).SelectMany(column => column.View.Parts).Select(part => part.position).ToList();
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
                plane.Advance(MenuPlane.SlideSeconds / 2f);
                var fileView = FindView(plane, "File");
                if (fileShown.Count > 0 && fileView != null && fileView.gameObject.activeSelf && Vector3.Dot(fileView.Subject.position - fileShown[0], PlaneLayout.Facing(plane.Direction) * Vector3.right) <= 0f)
                {
                    failures.Add(name + ": halfway through stepping aside for the menu's details, the file has not moved right.");
                }
                plane.Advance(MenuPlane.SlideSeconds);
                if (!plane.FileAside || plane.Shown.Any(column => column.Kind == MenuColumn.File)) failures.Add(name + ": a setting chosen beside a file, and the file still stands on the plane.");
                if (fileView != null && fileView.gameObject.activeSelf) failures.Add(name + ": the file still shows after stepping aside.");
                if (plane.Shown.All(column => column.Kind != MenuColumn.Side)) failures.Add(name + ": a setting chosen beside a file, and its details don't show.");
                failures.AddRange(PlaneState(name + " details over a file", folder, camera, texture, plane, characters, eyes, window));

                // Close details: the file comes back.
                plane.Show(bar, menu, WaitingFile(opened.View.Presentation!.Title, badge, chosen: false, budget), opened.Target, targets, eyes, looking, surface, besideWindow: besideWindow);
                plane.Advance(MenuPlane.SlideSeconds);
                if (plane.FileAside || plane.Shown.All(column => column.Kind != MenuColumn.File)) failures.Add(name + ": the menu's details closed, and the file did not come back.");
                failures.AddRange(PlaneState(name + " file back", folder, camera, texture, plane, characters, eyes, window));

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

        private static readonly Prompt PlaneClose = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);

        /// <summary>Tasks: the waiting task chosen, <paramref name="count"/> rows, as many as the stage holds beside a file.</summary>
        /// <summary>Settings with Text size chosen: its details beside it, its change the main action, safe on them.</summary>
        private static MenuFrame SettingChosen() => new MenuFrame(SettingsText.Subject,
            new Footer(PlaneClose, farRight: new Prompt("change", "Make text larger", GlazeIcon.Change, main: true, safeInPlace: true)),
            sections: MenuBar.Places.Select(place => new FrameSection(place.ToString(), MenuBar.Word(place), chosen: place == MenuPlace.Settings)).ToList(),
            lines: new[]
            {
                new PageLine("Comfort", tone: LineTone.Secondary),
                new PageLine("Text size", fact: "Standard", action: "settings-open-setting", key: "text-size", opens: true, chosen: true),
                new PageLine("Moving badges", fact: "On", action: "settings-open-setting", key: "moving-badges", opens: true),
            },
            side: new SidePanel("Text size", facts: new[] { new SideFact("Now", "The standard size"), new SideFact("A step larger", "Text 15 percent larger, and 3 rows a page") }));

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
            // where only what the panel shows everything of is carried, Close details is drawn to bring the
            // page back. A side panel in its frame's place says its frame's own reason, of what it carries.
            if (plane.Front is MenuFrame front)
            {
                var drawn = plane.Shown.SelectMany(column => column.View.Footer.Showing?.All.Select(each => each.Prompt.Id) ?? Enumerable.Empty<string>()).ToHashSet();
                foreach (var (_, prompt) in front.Footer.All)
                {
                    if (prompt.Kind == PromptKind.Close || drawn.Contains(prompt.Id)) continue;
                    if (prompt.SafeInPlace || prompt.Kind == PromptKind.NextPage || prompt.Kind == PromptKind.Cancel || !drawn.Contains(SidePanel.Close))
                    {
                        failures.Add(what + ": the frame in front offers \"" + prompt.Words + "\", but nothing on the plane draws it, nor Close details to bring the page back.");
                    }
                }
                var inPlace = plane.Shown.Count == 1 && plane.Shown[0].Kind == MenuColumn.Side;
                var said = front.Footer.InPlace(SidePanel.Footer[PromptSlot.Close]!).Reason;
                if (inPlace && plane.Shown[0].View.ReasonShown != said)
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

            // The light line crosses no label and no character, seen from the eyes.
            if (plane.LightLine is (Vector3 from, Vector3 to))
            {
                foreach (var extent in others)
                {
                    for (var step = 1; step < 40; step++)
                    {
                        var point = Vector3.Lerp(from, to, step / 40f) - eyes;
                        var across = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
                        var up = Mathf.Atan2(point.y, new Vector2(point.x, point.z).magnitude) * Mathf.Rad2Deg;
                        if (across < extent.Left || across > extent.Right || up < extent.Bottom || up > extent.Top) continue;
                        failures.Add(what + ": the light line crosses " + extent.Name + "; it leaves from under its label and crosses no label or character.");
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
