#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The owner's second brief (2026-10-02): a task as a case file of plainly named sections, one on
    /// screen at a time, every button in one fixed place, few words, thin lines. Two directions:
    /// D, a game menu, its sections named across the top, one focus area, and its buttons in a footer
    /// of prompts, Close always far left and the main action always far right; and E, a case file
    /// the task's character projects in front of you, as Dead Space's suit projects its menus into the
    /// world, laid out as a detective's notebook: an index of the sections with a line each, the
    /// chosen section's page beside it, and the same footer under both.
    /// </summary>
    public static partial class DirectionsRender
    {
        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> MenuShots() => new (string, bool, Action<Shot>)[]
        {
            ("d1-arriving", false, shot => MenuBar(shot, projected: false)),
            ("d2-waiting-question", false, shot => MenuFile(shot, FileSection.Needs, Page.Question, window: false)),
            ("d2-waiting-approve", false, shot => MenuFile(shot, FileSection.Needs, Page.Confirm, window: false)),
            ("d3-understanding", false, shot => MenuFile(shot, FileSection.Changed, Page.Changed, window: false)),
            ("d4-creating", false, shot => MenuCreate(shot)),
            ("d5-video-opened", true, shot => MenuFile(shot, FileSection.Needs, Page.Question, window: true)),
            ("e1-arriving", false, shot => MenuBar(shot, projected: true)),
            ("e2-waiting-question", false, shot => CaseFile(shot, FileSection.Needs, Page.Question, window: false)),
            ("e2-waiting-approve", false, shot => CaseFile(shot, FileSection.Needs, Page.Confirm, window: false)),
            ("e3-understanding", false, shot => CaseFile(shot, FileSection.Changed, Page.Changed, window: false)),
            ("e4-creating", false, shot => CaseCreate(shot)),
            ("e5-video-opened", true, shot => CaseFile(shot, FileSection.Needs, Page.Question, window: true)),
        };

        private enum FileSection
        {
            Needs,
            Doing,
            Changed,
            Checked,
        }

        private enum Page
        {
            Question,
            Confirm,
            Changed,
        }

        /// <summary>The case file's sections, named as the person's own questions.</summary>
        private static readonly string[] SectionNames = { "What it needs", "What it's doing", "What it changed", "What it checked" };

        private const float SleekRadius = 0.6f;
        private const float Hairline = 0.06f;

        private static Color HairlineColor => GlazeTokens.ColorOf(Glaze.Outline, 0.45f);

        /// <summary>The projected look's line colour: the active tone's light blue.</summary>
        private static Color Holo => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Strong);

        private static Color Amber => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Strong);

        // ---------------------------------------------------------------------------------------------
        // The sleek parts: hairlines, words for tabs, prompts with a hairline edge, one filled action.

        private static void Hair(Board board, float left, float right, float y, Color? colour = null, float weight = Hairline) =>
            Plate(board, "Hairline", (left + right) / 2f, y, right - left, U(weight), 0f, colour ?? HairlineColor, order: 53, depth: -U(0.02f));

        /// <summary>Draws a button's plate again: the sleek look's clear fill and hairline edge, or none.</summary>
        private static void Restyle(GlazeButton button, Color fill, Color edge)
        {
            var plate = button.transform.Find("Plate").GetComponent<Surface>();
            plate.Draw(button.Size, U(SleekRadius), fill, edge, edge.a > 0f ? U(Hairline * 1.6f) : 0f);
        }

        private sealed class Slot
        {
            public Slot(string text, GlazeIcon? icon, ButtonRole role = ButtonRole.Secondary)
            {
                Text = text;
                Icon = icon;
                Role = role;
            }

            public string Text { get; }

            public GlazeIcon? Icon { get; }

            public ButtonRole Role { get; }
        }

        private static readonly Slot CloseSlot = new Slot("Close", GlazeIcon.Close);

        /// <summary>
        /// Every button of a screen in one footer, in fixed places as a game's prompts: Close always
        /// at the far left, the screen's one main action always at the far right and filled, a second
        /// action beside it, and a rare one beside Close; the rest drawn as words in a hairline edge.
        /// Says where its buttons end.
        /// </summary>
        private static float Footer(Board board, float left, float right, float top, Slot? farLeft, Slot? nearLeft, Slot? nearRight, Slot? farRight,
            Color? line = null)
        {
            Hair(board, left - U(Pad) + U(0.3f), right + U(Pad) - U(0.3f), top, line);
            var tall = U(GlazeButton.HeightDegrees);
            var y = top - board.TargetGap - tall / 2f;
            var x = left;
            foreach (var slot in new[] { farLeft, nearLeft })
            {
                if (slot == null) continue;
                var width = Measure(board, slot.Role, slot.Text, slot.Icon);
                var button = Button(board, slot.Text, slot.Role, slot.Text, x + width / 2f, y, width, slot.Icon);
                if (slot.Role == ButtonRole.Secondary) Restyle(button, Color.clear, GlazeTokens.ColorOf(Glaze.Outline, 0.85f));
                x += width + board.TargetGap;
            }
            var end = right;
            foreach (var slot in new[] { farRight, nearRight })
            {
                if (slot == null) continue;
                var width = Measure(board, slot.Role, slot.Text, slot.Icon);
                var button = Button(board, slot.Text, slot.Role, slot.Text, end - width / 2f, y, width, slot.Icon);
                if (slot.Role == ButtonRole.Secondary) Restyle(button, Color.clear, GlazeTokens.ColorOf(Glaze.Outline, 0.85f));
                end -= width + board.TargetGap;
            }
            return top - board.TargetGap - tall;
        }

        /// <summary>
        /// Sections named across the top, as a game names its menu's screens: words alone, the chosen one
        /// bright with an accent line under it, those not reached yet quieter, an amber dot by the one that
        /// waits; each a 48 dp target 12 mm from the next. Says where the row ends.
        /// </summary>
        private static float Sections(Board board, IReadOnlyList<string> names, float left, float right, float top, int chosen, int waiting, int reached = int.MaxValue)
        {
            var tall = U(GlazeButton.CompactHeightDegrees);
            var widths = names.Select(name => WidthOf(board, name, GlazeType.Caption) * 1.06f + U(1.0f)).ToList();
            var gap = Mathf.Max(board.TargetGap, (right - left - widths.Sum()) / Mathf.Max(1, names.Count - 1));
            var x = left;
            for (var index = 0; index < names.Count; index++)
            {
                var width = widths[index];
                var tab = Button(board, "Section " + names[index], ButtonRole.Secondary, "", x + width / 2f, top - tall / 2f, width, compact: true);
                tab.Label.gameObject.SetActive(false);
                Restyle(tab, Color.clear, Color.clear);
                var colour = index == chosen ? GlazeTokens.Text : index <= reached ? GlazeTokens.TextSecondary : GlazeTokens.ColorOf(Glaze.TextDisabled);
                var (words, _) = Text(board, "Section words " + names[index], names[index], GlazeType.Caption, colour, x + U(0.6f), top - (tall - U(Glaze.CaptionDegrees) * 1.15f) / 2f,
                    width - U(0.6f), order: 62, strong: index == chosen);
                words.characterSpacing = 3f;
                if (index == chosen)
                {
                    Plate(board, "Chosen line", x + width / 2f, top - tall + U(0.12f), width - U(1.0f), U(0.16f), U(0.08f), Accent, order: 62, depth: -U(0.03f));
                }
                if (index == waiting)
                {
                    Plate(board, "Waiting dot", x + width - U(0.25f), top - U(0.75f), U(0.6f), U(0.6f), U(0.3f), Amber, order: 62, depth: -U(0.03f));
                }
                x += width + gap;
            }
            Hair(board, left - U(Pad) + U(0.3f), right + U(Pad) - U(0.3f), top - tall - U(0.25f));
            return top - tall - U(0.25f);
        }

        /// <summary>A panel's plate in the sleek look: a small radius, a hairline edge, or the projected look's light-blue edge and corner marks.</summary>
        private static void SleekPlate(Board board, float width, float bottom, bool projected)
        {
            board.Height = -bottom;
            board.Width = width;
            var edge = projected ? new Color(Holo.r, Holo.g, Holo.b, 0.55f) : GlazeTokens.ColorOf(Glaze.Outline, 0.4f);
            Plate(board, board.Name, 0f, -board.Height / 2f, width, board.Height, U(SleekRadius), GlazeTokens.ColorOf(Glaze.Panel, Glaze.PlateOpacity), edge, U(Hairline), 49);
            if (!projected) return;
            // Corner marks, as a projected display frames itself.
            var arm = U(1.4f);
            var weight = U(0.14f);
            foreach (var (x, y) in new[] { (-1f, 1f), (1f, 1f), (-1f, -1f), (1f, -1f) })
            {
                var cx = x * (width / 2f - U(0.05f));
                var cy = -board.Height / 2f + y * (board.Height / 2f - U(0.05f));
                Plate(board, "Corner", cx - x * arm / 2f, cy, arm, weight, 0f, Holo, order: 54, depth: -U(0.02f));
                Plate(board, "Corner", cx, cy - y * arm / 2f, weight, arm, 0f, Holo, order: 54, depth: -U(0.02f));
            }
        }

        // ---------------------------------------------------------------------------------------------
        // The top level: the stage, and a menu bar in the rail's place naming the few places there are.

        private static void MenuBar(Shot shot, bool projected)
        {
            var board = shot.Board(projected ? "Projected menu" : "Menu");
            var width = U(52f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = -U(0.9f);
            var names = new[] { "Tasks", "Projects", "New project", "Usage left", "Settings" };
            y = Sections(board, names, left, right, y, chosen: 0, waiting: 0);
            y -= U(0.5f);
            Text(board, "Waiting", "1 task is waiting for you", GlazeType.Caption, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground), left + U(0.6f), y, U(20f));
            Text(board, "How to open", "Look at a task, then pinch to open its file", GlazeType.Caption, GlazeTokens.TextSecondary, right, y, U(28f),
                alignment: TextAlignmentOptions.TopRight);
            y -= U(Glaze.CaptionDegrees) * 1.2f + U(0.9f);
            SleekPlate(board, width, y, projected);
            PlaceByTop(board, shot.Eyes, 0f, -28.5f);
            Hint(shot, shot.SlotOf(OpenedTitle));
        }

        // ---------------------------------------------------------------------------------------------
        // D: the task's file as a game menu.

        private static void MenuFile(Shot shot, FileSection section, Page page, bool window)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var view = shot.Characters[slot].View;
            var who = view.Presentation!;
            var board = shot.Board("Task file");
            var width = U(40f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = -U(Pad);
            // Where you are: the task's name and its state, as a game's screen names itself.
            var badge = StateBadgeView.Create(board.Content, "Badge", 58);
            badge.Show(StateLanguage.BadgeOf(who));
            var (_, titleHeight) = Text(board, "Title", OpenedTitle, GlazeType.Display, GlazeTokens.Text, left, y, right - left - badge.Width - U(1.5f), strong: false);
            badge.transform.localPosition = new Vector3(right - badge.Width / 2f, y - titleHeight / 2f, -U(0.05f));
            y -= titleHeight + U(0.7f);
            y = Sections(board, SectionNames, left, right, y, (int)section, who.Attention == AttentionLevel.ActionRequired ? 0 : -1);
            y -= U(1.4f);
            var (farLeft, nearLeft, nearRight, farRight) = PageBody(board, page, left, right, ref y, compact: false);
            y -= U(0.6f);
            y = Footer(board, left, right, y, farLeft, nearLeft, nearRight, farRight);
            y -= U(Pad);
            SleekPlate(board, width, y, projected: false);
            if (window) PlaceByTop(board, shot.Eyes, 0f, -17.6f);
            else PlaceUnderLabels(shot, board, CardYaw(shot, slot, 23f, 0f));
            Tether(shot, board, slot);
        }

        /// <summary>
        /// One section's page: only what that section is for, with room round it. Says the four
        /// footer slots the page's actions take.
        /// </summary>
        private static (Slot? FarLeft, Slot? NearLeft, Slot? NearRight, Slot? FarRight) PageBody(Board board, Page page, float left, float right, ref float y, bool compact)
        {
            var tall = U(GlazeButton.HeightDegrees);
            switch (page)
            {
                case Page.Question:
                {
                    y -= Text(board, "Asks", "It asks", GlazeType.Caption, GlazeTokens.TextSecondary, left, y, right - left).Height + U(0.3f);
                    y -= Text(board, "Question", "“How long should a sign-in lockout last?”", GlazeType.Title, GlazeTokens.Text, left, y, right - left, 2, lean: true, strong: false).Height + U(1.1f);
                    var answers = new[] { ("15 minutes", true), ("1 hour", false) };
                    var columnWidth = (right - left - board.TargetGap) / 2f;
                    for (var index = 0; index < answers.Length; index++)
                    {
                        var (answer, chosen) = answers[index];
                        var words = compact
                            ? new PanelRow { Title = answer, Detail = chosen ? "Chosen" : null }
                            : new PanelRow { Title = answer, End = chosen ? "Chosen" : null };
                        var row = Row(board, answer, ButtonRole.Choice, words, left + index * (columnWidth + board.TargetGap), y, columnWidth);
                        Restyle(row, chosen ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).Container) : GlazeTokens.ColorOf(Glaze.Raised, 0.55f),
                            chosen ? Accent : GlazeTokens.ColorOf(Glaze.Outline, 0.5f));
                    }
                    y -= tall;
                    if (compact)
                    {
                        // Typing is one more answer, under the others, where the page is narrow.
                        y -= board.TargetGap;
                        var own = Row(board, "Type my own answer", ButtonRole.Choice, new PanelRow { Title = "Type my own answer" }, left, y, right - left);
                        Restyle(own, Color.clear, GlazeTokens.ColorOf(Glaze.Outline, 0.5f));
                        y -= tall;
                    }
                    return (CloseSlot, compact ? null : new Slot("Type my own", GlazeIcon.Type), new Slot("Hold to talk", GlazeIcon.HoldToTalk), new Slot("Send answer", GlazeIcon.SendAnswer, ButtonRole.Primary));
                }
                case Page.Confirm:
                {
                    y -= Text(board, "Wants", "It wants to run a command", GlazeType.Caption, GlazeTokens.TextSecondary, left, y, right - left).Height + U(0.4f);
                    var well = U(3f);
                    Plate(board, "Command", (left + right) / 2f, y - well / 2f, right - left, well, U(SleekRadius), GlazeTokens.ColorOf(Glaze.Well), GlazeTokens.ColorOf(Glaze.Outline, 0.35f), U(Hairline), 52);
                    Text(board, "Command words", "make migrate", GlazeType.Body, GlazeTokens.Text, left + U(0.9f), y - (well - U(Glaze.BodyDegrees) * 1.15f) / 2f, right - left - U(1.8f), order: 56);
                    y -= well + U(1.1f);
                    y -= Text(board, "Asks to confirm", "Approve the request above?", GlazeType.Title, GlazeTokens.Text, left, y, right - left, strong: false).Height;
                    // Confirming: Yes stands where no button stood, Cancel where Approve was pressed; Deny waits.
                    return (CloseSlot, new Slot("Yes, approve", GlazeIcon.Approve, ButtonRole.Primary), null, new Slot("Cancel", GlazeIcon.Close));
                }
                default:
                {
                    y -= Text(board, "Lead", "2 files changed, both new.", GlazeType.Title, GlazeTokens.Text, left, y, right - left, strong: false).Height + U(0.9f);
                    foreach (var (kind, file, lines) in new[] { ("New", "0012_sign_in_attempts.sql", "+14"), ("New", "src/auth/rate-limit.ts", "+57") })
                    {
                        Hair(board, left, right, y + U(0.35f));
                        var h = Text(board, "File " + file, kind + "   " + file, GlazeType.Body, GlazeTokens.Text, left, y - U(0.15f), right - left - U(4f)).Height;
                        Text(board, "Lines " + file, lines, GlazeType.Body, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Success).Foreground), right, y - U(0.15f), U(4f), alignment: TextAlignmentOptions.TopRight);
                        y -= h + U(0.75f);
                    }
                    Hair(board, left, right, y + U(0.35f));
                    y -= U(0.5f);
                    var chip = Chip(board, "Inferred", GlazeTone.Unknown, left, y - U(0.75f));
                    y -= Text(board, "Inferred line", "Nothing checked these files after the last change.", GlazeType.Body, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground),
                        left + chip + U(0.6f), y, right - left - chip - U(0.6f), 2).Height + U(0.8f);
                    var said = Chip(board, "Agent says", GlazeTone.Neutral, left, y - U(0.75f));
                    y -= Text(board, "Reason", "“Add a table of failed sign-ins, then limit tries per address and per account.”", GlazeType.Body, GlazeTokens.Text,
                        left + said + U(0.6f), y, right - left - said - U(0.6f), 3, lean: true).Height + U(0.9f);
                    y -= Text(board, "Source", "From Salidium · 2 minutes ago", GlazeType.Caption, GlazeTokens.TextSecondary, left, y, right - left).Height;
                    return (CloseSlot, null, new Slot("Hold to talk", GlazeIcon.HoldToTalk), new Slot("Tell it", GlazeIcon.TellIt, ButtonRole.Primary));
                }
            }
        }

        /// <summary>D's New project: the same frame, its steps named across the top as a game names a creator's steps.</summary>
        private static void MenuCreate(Shot shot)
        {
            var board = shot.Board("New project");
            var width = U(44f);
            var left = -width / 2f + U(Pad);
            var right = width / 2f - U(Pad);
            var y = -U(Pad);
            var mark = 2.6f;
            CompanionMark(board, left + U(mark / 2f), y - U(mark / 2f) + U(0.15f), mark);
            var titleHeight = Text(board, "Title", "New project", GlazeType.Display, GlazeTokens.Text, left + U(mark + 0.9f), y, right - left - U(mark + 0.9f), strong: false).Height;
            y -= Mathf.Max(titleHeight, U(mark)) + U(0.7f);
            y = Sections(board, new[] { "Your idea", "Questions", "Recap", "Start building" }, left, right, y, chosen: 1, waiting: -1, reached: 1);
            y -= U(1.4f);
            y -= Text(board, "Companion says", "The companion says: “A running club could use a page that keeps everyone's race times in one place.”",
                GlazeType.Body, GlazeTokens.Text, left, y, right - left, 2, lean: true).Height + U(0.8f);
            y -= Text(board, "Question", "Who enters the times after each race?", GlazeType.Title, GlazeTokens.Text, left, y, right - left, strong: false).Height + U(1.0f);
            var tall = U(GlazeButton.HeightDegrees);
            var x = left;
            foreach (var answer in new[] { "Each runner", "One organiser", "Both", "Not sure yet" })
            {
                var w = Measure(board, ButtonRole.Choice, answer);
                var tile = Button(board, answer, ButtonRole.Choice, answer, x + w / 2f, y - tall / 2f, w);
                Restyle(tile, GlazeTokens.ColorOf(Glaze.Raised, 0.55f), GlazeTokens.ColorOf(Glaze.Outline, 0.5f));
                x += w + board.TargetGap;
            }
            y -= tall + U(0.9f);
            y -= Text(board, "Disclaimer", "The companion is an AI on your computer. It can be wrong, and you can change everything before you start.",
                GlazeType.Caption, GlazeTokens.TextSecondary, left, y, right - left, 2).Height + U(0.6f);
            y = Footer(board, left, right, y, CloseSlot, new Slot("Type my own", GlazeIcon.Type), new Slot("Hold to talk", GlazeIcon.HoldToTalk),
                new Slot("Make the recap", GlazeIcon.Next, ButtonRole.Primary));
            y -= U(Pad);
            SleekPlate(board, width, y, projected: false);
            PlaceUnderLabels(shot, board, 0f);
        }

        // ---------------------------------------------------------------------------------------------
        // E: the case file the character projects, a notebook's index and page.

        /// <summary>One index entry: the section's name, a line saying what is in it, and whether it waits.</summary>
        private static IReadOnlyList<(string Name, string Line, bool Waits)> Index(Page page) => page == Page.Changed
            ? new[]
            {
                ("What it needs", "Nothing is waiting for you", false),
                ("What it's doing", "Its round ended", false),
                ("What it changed", "2 files, both new", false),
                ("What it checked", "1 of 24 checks failed", false),
            }
            : new[]
            {
                ("What it needs", page == Page.Confirm ? "Approval to run a command" : "An answer about lockouts", true),
                ("What it's doing", "Waiting for you", false),
                ("What it changed", "2 files so far", false),
                ("What it checked", "Nothing yet", false),
            };

        private static void CaseFile(Shot shot, FileSection section, Page page, bool window)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var view = shot.Characters[slot].View;
            var who = view.Presentation!;
            var index = shot.Board("Case index");
            var sheet = shot.Board("Case page");
            const float indexWidth = 16f;
            const float pageWidth = 23.5f;

            // The index: the file's name, then each section with a line saying what is in it.
            var il = -U(indexWidth) / 2f + U(Pad);
            var ir = U(indexWidth) / 2f - U(Pad);
            var iy = -U(1f);
            var entries = Index(page);
            for (var entry = 0; entry < entries.Count; entry++)
            {
                var (name, line, waits) = entries[entry];
                var chosen = entry == (int)section;
                var row = Row(index, name, ButtonRole.Choice, new PanelRow
                {
                    Title = name,
                    Detail = line,
                    DetailTone = waits ? GlazeTone.Attention : (GlazeTone?)null,
                }, il, iy, ir - il, U(GlazeButton.CompactHeightDegrees));
                Restyle(row, chosen ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Container, 0.9f) : Color.clear, Color.clear);
                row.Label.color = chosen ? GlazeTokens.Text : GlazeTokens.TextSecondary;
                if (chosen) Plate(index, "Chosen bar", il - U(0.55f), iy - row.Size.y / 2f, U(0.22f), row.Size.y - U(0.6f), U(0.11f), Holo, order: 62, depth: -U(0.03f));
                iy -= row.Size.y + index.TargetGap;
            }
            iy += index.TargetGap;

            // The page: the chosen section's name, and only what it is for.
            var pl = -U(pageWidth) / 2f + U(Pad);
            var pr = U(pageWidth) / 2f - U(Pad);
            var py = -U(1f);
            py -= Text(sheet, "File", OpenedTitle, GlazeType.Caption, GlazeTokens.TextSecondary, pl, py, pr - pl).Height + U(0.3f);
            py -= Text(sheet, "Section", SectionNames[(int)section], GlazeType.Title, GlazeTokens.Text, pl, py, pr - pl, strong: false).Height + U(0.8f);
            var (farLeft, nearLeft, nearRight, farRight) = PageBody(sheet, page, pl, pr, ref py, compact: true);

            // One footer under both pages, its buttons in the same places: Close and the rare one under
            // the index, the second and the main action under the page.
            var bottom = Mathf.Min(iy, py) - U(0.6f);
            var footerLine = new Color(Holo.r, Holo.g, Holo.b, 0.35f);
            var indexEnd = Footer(index, il, ir, bottom, farLeft, nearLeft, null, null, footerLine);
            var pageEnd = Footer(sheet, pl, pr, bottom, null, null, nearRight, farRight, footerLine);
            var end = Mathf.Min(indexEnd, pageEnd) - U(1f);
            SleekPlate(index, U(indexWidth), end, projected: true);
            SleekPlate(sheet, U(pageWidth), end, projected: true);

            PlaceBeside(shot, index, sheet, -12.4f, 1, window ? -16.5f : (float?)null, centered: window);
            Projection(shot, slot, index, sheet);
        }

        /// <summary>E's New project: the same file, its index the steps of making it, projected from the companion's mark.</summary>
        private static void CaseCreate(Shot shot)
        {
            var index = shot.Board("Case index");
            var sheet = shot.Board("Case page");
            const float indexWidth = 19f;
            const float pageWidth = 31f;
            var il = -U(indexWidth) / 2f + U(Pad);
            var ir = U(indexWidth) / 2f - U(Pad);
            var iy = -U(Pad);
            var mark = 2.2f;
            CompanionMark(index, il + U(mark / 2f), iy - U(mark / 2f), mark);
            iy -= Mathf.Max(Text(index, "File", "New project", GlazeType.Title, GlazeTokens.Text, il + U(mark + 0.8f), iy, ir - il - U(mark + 0.8f), strong: false).Height, U(mark)) + U(0.9f);
            var steps = new[]
            {
                ("Your idea", "A page for race times", true, false),
                ("Questions", "The companion asks one", true, true),
                ("Recap", "Not yet", false, false),
                ("Start building", "Not yet", false, false),
            };
            foreach (var (name, line, reached, chosen) in steps)
            {
                var row = Row(index, name, ButtonRole.Choice, new PanelRow { Title = name, Detail = line }, il, iy, ir - il, U(GlazeButton.CompactHeightDegrees));
                Restyle(row, chosen ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Container, 0.9f) : Color.clear, Color.clear);
                row.Label.color = chosen ? GlazeTokens.Text : reached ? GlazeTokens.TextSecondary : GlazeTokens.ColorOf(Glaze.TextDisabled);
                if (chosen) Plate(index, "Chosen bar", il - U(0.55f), iy - row.Size.y / 2f, U(0.22f), row.Size.y - U(0.6f), U(0.11f), Holo, order: 62, depth: -U(0.03f));
                iy -= row.Size.y + index.TargetGap;
            }
            iy += index.TargetGap;

            var pl = -U(pageWidth) / 2f + U(Pad);
            var pr = U(pageWidth) / 2f - U(Pad);
            var py = -U(Pad);
            py -= Text(sheet, "Section", "Questions", GlazeType.Title, GlazeTokens.Text, pl, py, pr - pl, strong: false).Height + U(0.9f);
            py -= Text(sheet, "Companion says", "The companion says: “A running club could use a page that keeps everyone's race times in one place.”",
                GlazeType.Body, GlazeTokens.Text, pl, py, pr - pl, 3, lean: true).Height + U(0.7f);
            py -= Text(sheet, "Question", "Who enters the times after each race?", GlazeType.Title, GlazeTokens.Text, pl, py, pr - pl, 2, strong: false).Height + U(0.9f);
            var tall = U(GlazeButton.HeightDegrees);
            var half = (pr - pl - sheet.TargetGap) / 2f;
            var answers = new[] { "Each runner", "One organiser", "Both", "Not sure yet" };
            for (var answer = 0; answer < answers.Length; answer++)
            {
                var column = answer % 2;
                var tile = Button(sheet, answers[answer], ButtonRole.Choice, answers[answer], pl + column * (half + sheet.TargetGap) + half / 2f, py - tall / 2f, half);
                Restyle(tile, GlazeTokens.ColorOf(Glaze.Raised, 0.55f), GlazeTokens.ColorOf(Glaze.Outline, 0.5f));
                if (column == 1) py -= tall + sheet.TargetGap;
            }
            py += sheet.TargetGap - U(0.7f);
            py -= Text(sheet, "Disclaimer", "The companion is an AI on your computer. It can be wrong, and you can change everything before you start.",
                GlazeType.Caption, GlazeTokens.TextSecondary, pl, py, pr - pl, 3).Height;

            var bottom = Mathf.Min(iy, py) - U(0.9f);
            var footerLine = new Color(Holo.r, Holo.g, Holo.b, 0.35f);
            var indexEnd = Footer(index, il, ir, bottom, CloseSlot, new Slot("Type my own", GlazeIcon.Type), null, null, footerLine);
            var pageEnd = Footer(sheet, pl, pr, bottom, null, null, new Slot("Hold to talk", GlazeIcon.HoldToTalk), new Slot("Make the recap", GlazeIcon.Next, ButtonRole.Primary), footerLine);
            var end = Mathf.Min(indexEnd, pageEnd) - U(Pad);
            SleekPlate(index, U(indexWidth), end, projected: true);
            SleekPlate(sheet, U(pageWidth), end, projected: true);
            PlaceBeside(shot, index, sheet, -15.6f, 1);
        }

        /// <summary>The light lines of the shot being built, from a character to its file, for their check.</summary>
        private static readonly List<(Vector3 From, Vector3 To, int Slot)> lightLines = new List<(Vector3, Vector3, int)>();

        /// <summary>
        /// The light line (ADR 0026): one leg from under the character's label to its file's subject
        /// plate. Where the label stands over the plate, as the eyes see them, it drops straight down
        /// from the middle of their overlap; else it joins the label's nearer bottom corner to the
        /// plate's nearer top corner. It leaves from under the label, so it crosses no words, its own
        /// task's included.
        /// </summary>
        /// <param name="drop">How far below its board's top the plate's top edge stands, as under a split header's state pill.</param>
        private static void LightLine(Shot shot, int slot, Board subject, float drop = 0f)
        {
            var label = shot.Characters[slot].View.Label;
            var plate = label.Plate.transform;
            // The label's lowest part, its plate or a mark hanging from the plate's edge, in the plate's units.
            var lowest = label.GetComponentsInChildren<MeshFilter>(false)
                .Where(filter => filter.sharedMesh != null && filter.TryGetComponent<Renderer>(out var drawn) && drawn.enabled)
                .SelectMany(filter => Corners(filter.sharedMesh.bounds).Select(corner => plate.InverseTransformPoint(filter.transform.TransformPoint(corner)).y))
                .Append(-0.5f)
                .Min();
            var labelLeft = plate.TransformPoint(new Vector3(-0.5f, lowest, 0f));
            var labelRight = plate.TransformPoint(new Vector3(0.5f, lowest, 0f));
            var plateLeft = subject.Root.TransformPoint(new Vector3(-0.5f * subject.Width, subject.Height / 2f - drop, 0f));
            var plateRight = subject.Root.TransformPoint(new Vector3(0.5f * subject.Width, subject.Height / 2f - drop, 0f));
            float Across(Vector3 point) => Mathf.Atan2(point.x - shot.Eyes.x, point.z - shot.Eyes.z) * Mathf.Rad2Deg;
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
            var go = new GameObject("Light line");
            go.transform.SetParent(shot.Root, false);
            var beam = go.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.material = new Material(Shader.Find("Sprites/Default"));
            beam.positionCount = 2;
            beam.SetPosition(0, from);
            beam.SetPosition(1, to);
            beam.startWidth = 0.003f;
            beam.endWidth = 0.0012f;
            beam.startColor = new Color(Holo.r, Holo.g, Holo.b, 0.55f);
            beam.endColor = new Color(Holo.r, Holo.g, Holo.b, 0.12f);
            lightLines.Add((from, to, slot));
        }

        private static IEnumerable<Vector3> Corners(Bounds bounds) => Enumerable.Range(0, 8)
            .Select(corner => bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));

        /// <summary>
        /// Light from the task's character to the file's outer top corners, as Dead Space's suit casts
        /// its menus into the room: the file is the character's, opened toward you.
        /// </summary>
        /// <param name="drop">How far below its board's top a plate's top edge stands, as under a split header's state pill.</param>
        private static void Projection(Shot shot, int slot, Board left, Board right, float drop = 0f)
        {
            var body = shot.Characters[slot].View.Body.position;
            var corners = shot.Window
                ? new[] { (left, -0.5f), (left, 0.5f) }
                : new[] { (left, -0.5f), (right, 0.5f) };
            foreach (var (board, x) in corners)
            {
                var corner = board.Root.TransformPoint(new Vector3(x * board.Width, board.Height / 2f - drop, 0f));
                var go = new GameObject("Projection");
                go.transform.SetParent(shot.Root, false);
                var beam = go.AddComponent<LineRenderer>();
                beam.useWorldSpace = true;
                beam.material = new Material(Shader.Find("Sprites/Default"));
                beam.positionCount = 2;
                beam.SetPosition(0, body);
                beam.SetPosition(1, corner);
                beam.startWidth = 0.006f;
                beam.endWidth = 0.0012f;
                beam.startColor = new Color(Holo.r, Holo.g, Holo.b, 0.55f);
                beam.endColor = new Color(Holo.r, Holo.g, Holo.b, 0.12f);
                beam.sortingOrder = 40;
            }
        }
    }
}
