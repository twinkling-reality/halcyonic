#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI.Editor
{
    public static partial class GlazeRender
    {
        private static readonly Prompt CloseFrame = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);

        /// <summary>A column of a composition on the menu's plane: a frame or a side panel, or the closed bar.</summary>
        private sealed class FrameColumn
        {
            public FrameColumn(MenuFrameView view) => View = view;

            public FrameColumn(MenuBarView bar) => Bar = bar;

            public MenuFrameView? View { get; }

            public MenuBarView? Bar { get; }

            public float Width => View?.Width ?? Bar!.Size.x;

            public IReadOnlyList<Transform> Parts => View?.Parts ?? new[] { Bar!.transform };

            public IReadOnlyList<float> Heights => View?.Heights ?? new[] { Bar!.Size.y };
        }

        /// <summary>
        /// The menu's frames (ADR 0026), each composition on one plane facing the eyes and rendered as
        /// the eyes see it, at each text size: the menu on Tasks beside a file waiting for an answer;
        /// Usage with a limit chosen and its side panel; a file's Changes with a row chosen and its
        /// details; a file confirming an approval, its request in parts; and the menu closed. Each is
        /// held to one plane, type stepping down, one selection treatment, text as the eyes see it,
        /// targets' sizes, nothing of Halcyonic's own cut, a footer that fits, the content's light ending
        /// above its first target, and a Quest 3S's field with the head turned to it.
        /// </summary>
        private static IEnumerable<string> Frames(string folder, Camera camera, RenderTexture texture, GameObject root, Vector3 eyes)
        {
            var failures = new List<string>();
            var compositions = new (string Name, Func<Transform, List<FrameColumn>> Build)[]
            {
                ("frames-tasks-and-file", TasksAndFile),
                ("frames-usage-limit", UsageLimit),
                ("frames-changes-details", ChangesDetails),
                ("frames-approval-confirming", ApprovalConfirming),
                ("frames-closed", Closed),
            };
            var aim = camera.transform.rotation;
            foreach (var (name, build) in compositions)
            {
                var holder = new GameObject(name).transform;
                holder.SetParent(gallery, false);
                var columns = build(holder);
                var what = "component render: " + name;
                var (direction, shapes) = Compose(columns, eyes);
                camera.transform.rotation = PlaneLayout.Facing(direction);
                var targets = columns.SelectMany(column => column.View?.Targets ?? new[] { column.Bar!.Target }).ToList();
                failures.AddRange(Check(folder, "gallery-" + name + ".png", camera, texture, root, eyes,
                    targets.Select(target => (target, name + "'s " + target.name)).ToList(), columns.SelectMany(column => column.View?.MayCut ?? Enumerable.Empty<TMP_Text>()).ToList()));
                failures.AddRange(GlazeChecks.TextAsSeen(holder.gameObject, eyes, what));
                failures.AddRange(GlazeChecks.OnePlane(shapes, eyes, what));
                failures.AddRange(GlazeChecks.TypeStepsDown(shapes, eyes, what));
                failures.AddRange(GlazeChecks.OneSelectionTreatment(holder.GetComponentsInChildren<Surface>(false), eyes, what));
                foreach (var column in columns)
                {
                    if (column.View == null) continue;
                    if (!column.View.Footer.Fits)
                    {
                        var (needed, room) = column.View.Footer.Measure;
                        failures.Add(what + ": " + column.View.name + "'s footer needs " + Degrees(needed) + " degrees of its " + Degrees(room) + "; a footer holds what its words fit.");
                    }
                    failures.AddRange(GlazeChecks.GlowEndsAboveTargets(column.View.Content, column.View.Targets, what));
                }
                var composition = Composition(columns);
                var corners = shapes.SelectMany(column => column).SelectMany(Corners);
                var outside = GlazeChecks.InsideField(what, corners, eyes, GlazeChecks.CompositionCenter(shapes.SelectMany(column => column)),
                    WorkspacePlacement.ReadingPitch(composition.Size, direction.Elevation, MenuPage.Quest3S), MenuPage.Quest3S).ToList();
                failures.AddRange(outside);
                // The client core's measure, which screens pack pages by, sees the field as the render does.
                if (MenuPage.Fits(composition) != (outside.Count == 0))
                {
                    failures.Add(what + ": MenuPage.Fits says " + (outside.Count == 0 ? "it doesn't fit" : "it fits") + " where the render's field check says otherwise.");
                }
                holder.gameObject.SetActive(false);
            }
            camera.transform.rotation = aim;
            failures.AddRange(LineSpacingIsTheFonts());
            failures.AddRange(FooterCatchesWhatDoesNotFit());
            failures.AddRange(PartsLoseNoWord());
            failures.AddRange(GlowCatchesARowUnderIt());
            failures.AddRange(OutlineApartCatchesALabelOverAPart());
            return failures;
        }

        private static string Degrees(float units) => GlazeChecks.Degrees(2f * Mathf.Atan(units / 2f) * Mathf.Rad2Deg);

        private static IEnumerable<Vector3> Corners(GlazeChecks.PlaneShape shape)
        {
            foreach (var x in new[] { -0.5f, 0.5f })
            {
                foreach (var y in new[] { -0.5f, 0.5f }) yield return shape.Root.position + shape.Root.right * (x * shape.Size.x) + shape.Root.up * (y * shape.Size.y);
            }
        }

        private static PlaneComposition Composition(List<FrameColumn> columns) =>
            new PlaneComposition(columns.Select(column => new PlaneColumn(column.Width, column.Heights.ToArray())).ToList(), GlazeText.Scale);

        /// <summary>
        /// Lays the columns on one plane facing the eyes, grown whole by the text's scale, the plane's top
        /// <see cref="MenuPage.TopDegrees"/> below eye level, each frame's content stretched to the common
        /// bottom; returns the plane's direction and its parts for the checks.
        /// </summary>
        private static (PanelDirection Direction, List<IReadOnlyList<GlazeChecks.PlaneShape>> Shapes) Compose(List<FrameColumn> columns, Vector3 eyes)
        {
            var composition = Composition(columns);
            var zoom = composition.Zoom;
            var half = Mathf.Atan(composition.Height / 2f) * Mathf.Rad2Deg;
            var elevation = -MenuPage.TopDegrees - half;
            var direction = new PanelDirection(0f, elevation, true, false);
            Debug.Log("Halcyonic: component render: the plane holds " + Degrees(composition.Width) + " by " + Degrees(composition.Height) + " degrees, from "
                + GlazeChecks.Degrees(MenuPage.TopDegrees) + " to " + GlazeChecks.Degrees(MenuPage.TopDegrees + 2f * half) + " degrees below eye level along its middle; its parts "
                + string.Join(", ", columns.Select(column => string.Join(" + ", column.Heights.Select(Degrees)))) + ".");
            var shapes = new List<IReadOnlyList<GlazeChecks.PlaneShape>>();
            for (var c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var placed = composition.Parts.Where(part => part.Column == c).OrderBy(part => part.Index).ToList();
                var shapesOfColumn = new List<GlazeChecks.PlaneShape>();
                for (var index = 0; index < placed.Count; index++)
                {
                    var part = column.Parts[index];
                    PlaneLayout.Lay(part, eyes, direction, placed[index], zoom);
                    if (column.View != null && index == placed.Count - 1) column.View.Settle(placed[index], zoom);
                    shapesOfColumn.Add(new GlazeChecks.PlaneShape(part.parent.name + " " + part.name, part, new Vector2(placed[index].Width, placed[index].Height) * PlaneComposition.Distance));
                }
                shapes.Add(shapesOfColumn);
            }
            return (direction, shapes);
        }

        /// <summary>The columns' subjects take the tallest of them, a pill's room kept where a file shows, so the plates and titles stay level.</summary>
        private static float Subject(bool pillRoom, params (string Subject, float Column)[] subjects) =>
            subjects.Max(each => MenuFrameView.SubjectHeight(each.Subject, each.Column, pillRoom));

        private static IReadOnlyList<FrameSection> Places(MenuPlace chosen, params MenuPlace[] waiting) =>
            MenuBar.Places.Select(place => new FrameSection(place.ToString(), MenuBar.Word(place), chosen: place == chosen, waits: waiting.Contains(place))).ToList();

        private static IReadOnlyList<FrameSection> FileSections(string chosen, bool waits) =>
            new[] { "Waiting", "Activity", "Changes", "Checks" }.Select(words => new FrameSection(words, words, chosen: words == chosen, waits: waits && words == "Waiting")).ToList();

        /// <summary>The text size this pass draws at.</summary>
        private static TextSize TextSizeNow => GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard;

        private static StateBadge Waiting => StateLanguage.BadgeOf(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired));

        private static MenuFrameView View(Transform holder, string name) => MenuFrameView.Create(holder, name);

        /// <summary>The menu on Tasks, its waiting task chosen, and that task's file beside it, waiting for an answer.</summary>
        private static List<FrameColumn> TasksAndFile(Transform holder)
        {
            const string task = "Add rate limiting to the sign-in endpoint";
            var menu = new MenuFrame("1 task is waiting for you", new Footer(CloseFrame).WithNext(new Prompt(Footer.NextPage, "Next page", GlazeIcon.Next, PromptKind.NextPage)), subjectWaits: true,
                sections: Places(MenuPlace.Tasks, MenuPlace.Tasks),
                lines: new[]
                {
                    new PageLine(task, wordsAreData: true, icon: GlazeIcon.WaitingForYou, fact: "Storefront API", factIsData: true, tone: LineTone.Waiting, action: "open-task", key: "t1", opens: true, chosen: true),
                    new PageLine("Paginate the order history endpoint", wordsAreData: true, icon: GlazeIcon.Working, fact: "Storefront API", factIsData: true, action: "open-task", key: "t2", opens: true),
                    new PageLine("Send an order confirmation email", wordsAreData: true, icon: GlazeIcon.FinishedThisRound, fact: "Storefront API", factIsData: true, tone: LineTone.Good, action: "open-task", key: "t3", opens: true),
                    new PageLine("Refresh the checkout copy", wordsAreData: true, icon: GlazeIcon.CheckingItsWork, fact: "Docs site", factIsData: true, action: "open-task", key: "t4", opens: true),
                }.Take(MenuFrame.RowsAPage(TextSizeNow, sourceLine: false)).ToList());
            var file = new MenuFrame(task, new Footer(CloseFrame, secondary: new Prompt("talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true),
                    farRight: new Prompt("send", "Send answer", GlazeIcon.SendAnswer, main: true)),
                subjectIsData: true, pill: Waiting, sections: FileSections("Waiting", waits: true),
                lines: new[]
                {
                    new PageLine("How long should a sign-in lockout last?", wordsAreData: true, chip: "Agent says", claim: true, rows: 2),
                    new PageLine("15 minutes", wordsAreData: true, action: "answer", key: "0", choice: true, chosen: true),
                    new PageLine("1 hour", wordsAreData: true, action: "answer", key: "1", choice: true),
                    new PageLine("Type my answer", icon: GlazeIcon.Type, action: "answer", key: "own", choice: true, besideNext: true),
                    new PageLine("More answers, 2 of 2", action: "more-answers", key: "1"),
                });
            var subject = Subject(true, (menu.Subject, Glaze.Menu.MenuColumnDegrees), (file.Subject, Glaze.Menu.FileColumnDegrees));
            var menuView = View(holder, "Menu");
            menuView.Show(menu, Glaze.Menu.MenuColumnDegrees, subject, pillRoom: true);
            var fileView = View(holder, "File");
            fileView.Show(file, Glaze.Menu.FileColumnDegrees, subject, pillRoom: true);
            return new List<FrameColumn> { new FrameColumn(menuView), new FrameColumn(fileView) };
        }

        /// <summary>Usage, a limit chosen, and its side panel: when it was seen, when it resets and whose account it is.</summary>
        private static List<FrameColumn> UsageLimit(Transform holder)
        {
            var limits = new[] { ("Claude Code, 5-hour window", "At most 60% left"), ("Claude Code, weekly", "At most 85% left"), ("Codex, 5-hour window", "At most 3% left") }
                .Take(MenuFrame.RowsAPage(TextSizeNow, sourceLine: true)).ToArray();
            var side = new SidePanel(limits[0].Item1, facts: new[]
            {
                new SideFact("Seen", "Today at 15:18"),
                new SideFact("Resets", "Today at 18:00"),
                new SideFact("Account", "Not identified: it may be any account used on your computer"),
            }, source: UsageLeftPresenter.FromSeorak);
            var usage = new MenuFrame("How much is left before each limit?", new Footer(CloseFrame, rare: new Prompt("refresh", "Refresh", GlazeIcon.Refresh)),
                sections: Places(MenuPlace.Usage),
                lines: limits.Select((limit, index) => new PageLine(limit.Item1, fact: limit.Item2, action: "open-limit", key: index.ToString(), opens: true, chosen: index == 0)).ToList(),
                source: UsageLeftPresenter.FromSeorak, side: side);
            var subject = Subject(false, (usage.Subject, Glaze.Menu.MenuColumnDegrees), (side.Subject, Glaze.Menu.SideColumnDegrees));
            var menuView = View(holder, "Usage");
            menuView.Show(usage, Glaze.Menu.MenuColumnDegrees, subject, pillRoom: false);
            var sideView = View(holder, "Limit");
            sideView.Show(side, Glaze.Menu.SideColumnDegrees, subject, pillRoom: false);
            return new List<FrameColumn> { new FrameColumn(menuView), new FrameColumn(sideView) };
        }

        /// <summary>A finished task's file on Changes, its first row chosen and its details beside it; Tell it waits, and says why.</summary>
        private static List<FrameColumn> ChangesDetails(Transform holder)
        {
            const string task = "Send an order confirmation email";
            var side = new SidePanel("2 files changed, both new", lines: new[]
            {
                new PageLine("0012_order_emails.sql", wordsAreData: true, icon: GlazeIcon.DatabaseFile, fact: "+14"),
                new PageLine("Adds a table for the emails it sends.", chip: "Agent says", claim: true, rows: 2),
                new PageLine("src/orders/confirmation.ts", wordsAreData: true, icon: GlazeIcon.CodeFile, fact: "+57"),
                new PageLine("Sends the email once the order is paid.", chip: "Agent says", claim: true, rows: 2),
            }, source: "From the changes on your computer");
            var file = new MenuFrame(task, new Footer(CloseFrame, secondary: new Prompt("talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true),
                    farRight: new Prompt("tell", "Tell it", GlazeIcon.TellIt, main: true, available: false, reason: "It finished; start it again to tell it more.")),
                subjectIsData: true, pill: StateLanguage.BadgeOf(Character(CharacterActivity.TurnFinished, AttentionLevel.None)), sections: FileSections("Changes", waits: false),
                lines: new[]
                {
                    new PageLine("2 files changed, both new", icon: GlazeIcon.Change, fact: "+71", action: "open-changes", key: "all", opens: true, chosen: true),
                    new PageLine("No tests changed", tone: LineTone.Secondary),
                    new PageLine("Where it worked", fact: "storefront-api-checkout-and-orders-service-with-a-long-folder-name", factIsData: true, tone: LineTone.Secondary),
                },
                source: "From Salidium 0.9, read at 15:20", sourceIsData: true, side: side);
            var subject = Subject(true, (file.Subject, Glaze.Menu.FileColumnDegrees), (side.Subject, Glaze.Menu.SideColumnDegrees));
            var fileView = View(holder, "File");
            fileView.Show(file, Glaze.Menu.FileColumnDegrees, subject, pillRoom: true);
            var sideView = View(holder, "Details");
            sideView.Show(side, Glaze.Menu.SideColumnDegrees, subject, pillRoom: true);
            return new List<FrameColumn> { new FrameColumn(fileView), new FrameColumn(sideView) };
        }

        /// <summary>A file confirming an approval: its request's last part shown before Yes, Cancel where Approve stood.</summary>
        private static List<FrameColumn> ApprovalConfirming(Transform holder)
        {
            const string task = "Move sessions into their own table";
            var before = new Footer(CloseFrame, rare: new Prompt("stop", "Stop", GlazeIcon.Stop), secondary: new Prompt("deny", "Deny", GlazeIcon.Deny),
                farRight: new Prompt("approve", "Approve", GlazeIcon.Approve, main: true));
            var footer = Footer.Confirm(before, PromptSlot.FarRight, new Prompt("yes", "Yes, approve", GlazeIcon.Approve, PromptKind.Yes),
                new Prompt("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel));
            var file = new MenuFrame(task, footer, subjectIsData: true, pill: Waiting, sections: FileSections("Waiting", waits: true),
                lines: new[]
                {
                    new PageLine("It wants to run a command, part 2 of 2:"),
                    new PageLine(Request, wordsAreData: true, icon: GlazeIcon.ScriptFile, rows: 2, fromRow: 2),
                    new PageLine("Approve lets it go ahead. Deny refuses; it may try another way.", tone: LineTone.Secondary, rows: 2),
                });
            var view = View(holder, "File");
            view.Show(file, Glaze.Menu.FileColumnDegrees, Subject(true, (file.Subject, Glaze.Menu.FileColumnDegrees)), pillRoom: true);
            return new List<FrameColumn> { new FrameColumn(view) };
        }

        private const string Request = "make migrate NAME=move_sessions_into_their_own_table TARGET=production DRY_RUN=false VERIFY=true then restart the session workers and the sign-in service";

        /// <summary>The menu closed, saying what waits, on the plane's top line.</summary>
        private static List<FrameColumn> Closed(Transform holder)
        {
            var bar = MenuBarView.Create(holder, "Menu, closed");
            bar.Show(new MenuBar(MenuPlace.Tasks, "1 task is waiting for you", MenuPlace.Tasks), Glaze.Menu.MenuColumnDegrees);
            return new List<FrameColumn> { new FrameColumn(bar) };
        }

        /// <summary>The line spacing the client core measures pages by is the font's own (<see cref="Glaze.Menu.LineSpacing"/>).</summary>
        private static IEnumerable<string> LineSpacingIsTheFonts()
        {
            var holder = Holder("Line spacing", 0f, 0f);
            var label = GlazeText.Create(holder, "Words", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 1);
            var spacing = GlazeText.LineHeight(label) / GlazeTokens.Units(Glaze.Menu.BodyDegrees);
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
            if (Mathf.Abs(spacing - Glaze.Menu.LineSpacing) > 0.001f)
            {
                yield return "component render: the font's line height is " + spacing.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
                    + " of its em, not Glaze.Menu.LineSpacing's " + Glaze.Menu.LineSpacing.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + ".";
            }
        }

        /// <summary>A footer that doesn't fit its column fails: New project's Questions with Start over, in a file's 36 degrees.</summary>
        private static IEnumerable<string> FooterCatchesWhatDoesNotFit()
        {
            var holder = Holder("Footer that does not fit", 0f, 0f);
            var view = View(holder, "New project");
            var footer = new Footer(CloseFrame, rare: new Prompt("over", "Start over", GlazeIcon.Refresh), secondary: new Prompt("talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true),
                farRight: new Prompt("recap", "Make the recap", GlazeIcon.Next, main: true));
            view.Show(new MenuFrame("What would you like to work on?", footer), Glaze.Menu.FileColumnDegrees, Subject(false, ("x", Glaze.Menu.FileColumnDegrees)), pillRoom: false);
            var fits = view.Footer.Fits;
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
            if (fits) yield return "component render: the footer check passed New project's Questions with Start over, which needs more than a file's 36 degrees.";
        }

        /// <summary>
        /// A request shown in parts loses no word between them: each part's rows, laid as the whole
        /// request wraps, put together are the whole request.
        /// </summary>
        private static IEnumerable<string> PartsLoseNoWord()
        {
            var holder = Holder("Parts", 0f, 0f);
            var view = View(holder, "Parts");
            var probe = new PageLine(Request, wordsAreData: true, icon: GlazeIcon.ScriptFile);
            var rows = MenuFrameView.RowsOf(probe, Glaze.Menu.FileColumnDegrees);
            const int perPart = 2;
            var shown = new List<string>();
            for (var from = 0; from < rows; from += perPart)
            {
                view.Show(new MenuFrame("Parts", new Footer(CloseFrame), lines: new[] { new PageLine(Request, wordsAreData: true, icon: GlazeIcon.ScriptFile, rows: perPart, fromRow: from) }),
                    Glaze.Menu.FileColumnDegrees, Subject(false, ("Parts", Glaze.Menu.FileColumnDegrees)), pillRoom: false);
                var words = holder.GetComponentsInChildren<TMP_Text>(false).First(label => label.name == "Line 0 words");
                words.ForceMeshUpdate();
                if (words.isTextTruncated) yield return "component render: a part of a request ends in an ellipsis; a part shows exactly its rows.";
                shown.Add(words.text);
            }
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
            if (rows < 3) yield return "component render: the request for the parts check wraps to only " + rows + " rows; it needs three or more.";
            if (string.Concat(shown) != LabelText.ForTextMeshPro(Request))
            {
                yield return "component render: a request's parts put together are not the whole request: " + string.Join(" | ", shown);
            }
        }

        /// <summary>
        /// An icon's gap to its word reads as the eyes see it, the same laid gap the same at eye level and
        /// 49 degrees below it, where a gap seen as yaw would read half again as wide.
        /// </summary>
        private static IEnumerable<string> IconGapReadsTheSameBelowEyeLevel()
        {
            float GapAt(float elevation)
            {
                var holder = Holder("Icon gap at " + elevation, 0f, elevation);
                var icon = GlazeIcons.Create(holder, "Icon", Glaze.Menu.BodyDegrees, GlazeTokens.Text, 1);
                GlazeIcons.Show(icon, GlazeIcon.Close);
                var word = GlazeText.Create(holder, "Word", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, 1);
                word.rectTransform.pivot = new Vector2(0f, 0.5f);
                GlazeText.SetLiteral(word, "Close");
                GlazeText.Lay(word, GlazeTokens.Units(10f), 1);
                word.transform.localPosition = new Vector3(GlazeTokens.Units(1.2f), 0f, 0f);
                icon.ForceMeshUpdate();
                word.ForceMeshUpdate();
                var gap = GlazeChecks.IconGapDegrees(icon, word, galleryEyes) ?? float.NaN;
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
                return gap;
            }
            var level = GapAt(0f);
            var below = GapAt(-49f);
            Debug.Log("Halcyonic: component render: an icon's gap to its word reads " + level.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                + " degrees at eye level and " + below.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " at 49 degrees below it.");
            if (float.IsNaN(level) || float.IsNaN(below) || Mathf.Abs(level - below) > 0.01f)
            {
                yield return "component render: an icon's gap to its word reads " + level + " degrees at eye level but " + below + " at 49 degrees below; the eyes see the same gap.";
            }
        }

        /// <summary>
        /// A part's outline measures how far a label stands from it as the eyes see it: a label over its
        /// middle overlaps, and one two degrees above its top corner, the highest point of its top edge
        /// below eye level, stands two degrees off.
        /// </summary>
        private static IEnumerable<string> OutlineApartCatchesALabelOverAPart()
        {
            var (holder, column) = OneColumn("Outline", 24f);
            var part = column[0];
            var center = part.Root.position - galleryEyes;
            var yaw = Mathf.Atan2(center.x, center.z) * Mathf.Rad2Deg;
            var elevation = Mathf.Atan2(center.y, new Vector2(center.x, center.z).magnitude) * Mathf.Rad2Deg;
            var over = GlazeChecks.OutlineApart(part, galleryEyes, new GlazeChecks.Extent("over", yaw - 1f, yaw + 1f, elevation - 1f, elevation + 1f));
            var corner = part.Root.position + part.Root.right * (0.5f * part.Size.x) + part.Root.up * (0.5f * part.Size.y) - galleryEyes;
            var cornerYaw = Mathf.Atan2(corner.x, corner.z) * Mathf.Rad2Deg;
            var cornerUp = Mathf.Atan2(corner.y, new Vector2(corner.x, corner.z).magnitude) * Mathf.Rad2Deg;
            var past = GlazeChecks.OutlineApart(part, galleryEyes, new GlazeChecks.Extent("past", cornerYaw - 1f, cornerYaw + 1f, cornerUp + 2f, cornerUp + 4f));
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
            if (!(over < 0f)) yield return "component render: the outline check measured a label over a part's middle " + over + " degrees off; it overlaps.";
            if (Mathf.Abs(past - 2f) > 0.05f) yield return "component render: the outline check measured a label 2 degrees above a part's top corner as " + past + ".";
        }

        /// <summary>A content's light that reaches past its top padding, over its first row, fails the glow check.</summary>
        private static IEnumerable<string> GlowCatchesARowUnderIt()
        {
            var holder = Holder("Glow over a row", 0f, 0f);
            var glass = Surface.Create(holder, "Glass", 1);
            var size = new Vector2(GlazeTokens.Units(20f), GlazeTokens.Units(12f));
            glass.DrawGlass(size);
            var row = GlazeButton.Create(holder, "Row", ButtonRole.Row, compact: true);
            row.ShowArea(new Vector2(0f, size.y / 2f - GlazeTokens.Units(Glaze.Menu.PaddingDegrees) - GlazeButton.HeightOf(true) / 2f), new Vector2(size.x * 0.8f, GlazeButton.HeightOf(true)));
            var caught = GlazeChecks.GlowEndsAboveTargets(glass, new[] { row }, "proof").Any();
            glass.DrawGlass(size, GlazeTokens.Units(Glaze.Menu.PaddingDegrees));
            var passed = !GlazeChecks.GlowEndsAboveTargets(glass, new[] { row }, "proof").Any();
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
            if (!caught) yield return "component render: the glow check passed a light reaching over a row under the glass's top padding.";
            if (!passed) yield return "component render: the glow check failed a light ending in the top padding.";
        }
    }
}
