#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// ADR 0026 accepted, the other moments in its style, for the lanes to build from: the menu closed,
    /// a file on Changes with its side panel, Projects on a first visit, and New project's Questions
    /// beside the menu. Each on one plane facing the eyes, checked as the refined frames are.
    /// </summary>
    public static partial class DirectionsRender
    {
        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> RolloutShots() => new (string, bool, Action<Shot>)[]
        {
            ("r9-closed-bar", false, shot => Lay(Facing.Eyes, () => ClosedBar(shot))),
            ("r10-understanding-changes", false, shot => Lay(Facing.Eyes, () => ChangesWithSidePanel(shot))),
            ("r11-projects-first-visit", false, shot => Lay(Facing.Eyes, () => ProjectsFirstVisit(shot))),
            ("r12-creating-questions", false, shot => Lay(Facing.Eyes, () => NewProjectQuestions(shot))),
            ("r13-creating-questions-view", false, shot => Lay(Facing.Eyes, () => NewProjectQuestions(shot, view: true))),
            ("r14-creating-questions-too-long", false, shot => Lay(Facing.Eyes, () => NewProjectQuestions(shot, view: true, tooLong: true))),
        };

        private static readonly string[] Steps = { "Your idea", "Questions", "Recap", "Start building" };

        private static Color Plain => new Color(0.93f, 0.95f, 0.97f, 0.9f);

        // ---------------------------------------------------------------------------------------------
        // The menu closed: one rounded shape on the plane's top line, what waits, and Open.

        private static void ClosedBar(Shot shot)
        {
            var width = 2f * U(16f);
            var bar = shot.Board("Menu, closed", PlaneMeters);
            bar.Width = width;
            bar.Height = U(GlazeButton.HeightDegrees) + 2f * U(0.6f);
            var left = -width / 2f + U(PanelPadding);
            var right = width / 2f - U(PanelPadding);
            var middle = -bar.Height / 2f;
            Shape(bar, 0f, middle, width, bar.Height, sheen: true);
            Target(bar, "Open the menu", 0f, middle, width, compact: false);
            Glyph(bar, "Waits", GlazeIcon.WaitingForYou, left + U(IconColumn) / 2f, middle, AmberText, 1.1f);
            Body(bar, "Subject", "1 task is waiting for you", left + U(IconColumn) + U(Grid), middle + U(BodySize) * 0.6f, U(20f), AmberText);
            var cap = U(1.45f);
            var words = WidthOf(bar, "Open", GlazeType.Body);
            var capX = right - words - U(Grid) - cap / 2f;
            Plate(bar, "Cap", capX, middle, cap, cap, cap / 2f, Color.clear, new Color(1f, 1f, 1f, 0.6f), U(0.07f), 62, -U(0.04f));
            Glyph(bar, "Cap icon", GlazeIcon.Next, capX, middle, GlazeTokens.Text, 0.95f);
            Body(bar, "Open", "Open", right - words, middle + U(BodySize) * 0.6f, words + U(0.4f), GlazeTokens.Text);
            LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { bar } });
        }

        // ---------------------------------------------------------------------------------------------
        // A finished task's file on Changes, its first line chosen and its side panel slid out.

        private static void ChangesWithSidePanel(Shot shot)
        {
            var slot = shot.SlotOf(OpenedTitle);
            var who = shot.Characters[slot].View.Presentation!;
            var fileWidth = 2f * U(18f);
            var sideWidth = 2f * U(13f);
            var reserve = StateBadgeView.Height * PillScale / 2f;
            var success = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Success).Foreground);

            var head = SubjectShape(shot, "File subject", OpenedTitle, GlazeTokens.Text, fileWidth, null, reserve, who);
            var sideHead = SubjectShape(shot, "Side subject", "2 files changed, both new", GlazeTokens.Text, sideWidth, head.Height, reserve);
            var tabs = SectionShapes(shot, "File sections", FileSections, fileWidth, chosen: 2, waiting: -1);

            var file = shot.Board("Changes", PlaneMeters);
            var l = -fileWidth / 2f + U(PanelPadding);
            var r = fileWidth / 2f - U(PanelPadding);
            var y = -U(PanelPadding);
            RefinedRow(file, "2 files changed, both new", l, r, y, (cx, cy) => ProtoGlyph(file, "Files", GlyphDescription, cx, cy, GlazeTokens.Text), "+71 lines", success, chosen: true, more: true);
            y -= U(RowHeight) + file.TargetGap;
            RefinedRow(file, "Why it changed them", l, r, y, (cx, cy) => Glyph(file, "Why", GlazeIcon.TellIt, cx, cy, GlazeTokens.Text, 1.1f), "Agent says", Secondary, more: true);
            y -= U(RowHeight) + U(GroupGap);
            // Not a target: the inferred clause, chipped, never read as fact. At the plane's left edge a
            // caption would read under 14 dp as the eyes see it, so the chip takes the content's size.
            var chip = Chip(file, "Inferred", GlazeTone.Unknown, l, y - U(0.85f), GlazeType.Body);
            y -= Body(file, "Inferred line", "Nothing checked them after the last change.", l + chip + U(Grid), y, r - l - chip - U(Grid), Plain, lines: 2).Height + U(GroupGap);
            y -= Body(file, "Source", "From Salidium, 2 minutes ago", l, y, r - l, Secondary).Height;

            var side = shot.Board("Changed files", PlaneMeters);
            var sl = -sideWidth / 2f + U(PanelPadding);
            var sr = sideWidth / 2f - U(PanelPadding);
            var sy = -U(PanelPadding);
            foreach (var (glyph, name, lines) in new[] { (GlyphDatabase, "0012_sign_in_attempts.sql", "+14"), (GlyphCode, "src/auth/rate-limit.ts", "+57") })
            {
                var middle = sy - U(RowHeight) / 2f;
                ProtoGlyph(side, "Kind " + name, glyph, sl + U(IconColumn) / 2f, middle, GlazeTokens.Text);
                // At the plane's right edge a caption would read under 14 dp, so the count takes the content's size.
                var factWidth = WidthOf(side, lines, GlazeType.Body) + U(0.3f);
                Body(side, "File " + name, name, sl + U(IconColumn) + U(Grid), middle + U(BodySize) * 0.6f, sr - sl - U(IconColumn) - U(Grid) - factWidth - U(0.6f), Plain);
                Body(side, "Lines " + name, lines, sr, middle + U(BodySize) * 0.6f, factWidth, success, alignment: TMPro.TextAlignmentOptions.TopRight);
                sy -= U(RowHeight) + side.TargetGap;
            }
            sy -= Body(side, "Source", "From Salidium, 2 minutes ago", sl, sy, sr - sl, Secondary).Height;

            // Both columns end on one line, their footers on it: the side panel's content reaches down past the file's sections.
            var fileNeed = -y + U(GroupGap) + FooterRoom(file);
            var sideNeed = -sy + U(GroupGap) + FooterRoom(side);
            var fileHeight = Mathf.Max(fileNeed, sideNeed - tabs.Height - U(RowGap));
            ContentShape(file, fileWidth, fileHeight, new Prompt("Close", GlazeIcon.Close), null, new Prompt("Hold to talk", GlazeIcon.HoldToTalk),
                new Prompt("Tell it", GlazeIcon.TellIt, main: true));
            ContentShape(side, sideWidth, fileHeight + tabs.Height + U(RowGap), new Prompt("Close details", GlazeIcon.Close), null, null, null);

            LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { head, tabs, file }, new[] { sideHead, side } });
            LightLine(shot, slot, head, reserve);
        }

        // ---------------------------------------------------------------------------------------------
        // A first visit: nothing on the stage, the menu on Projects asking what to work on.

        private static void ProjectsFirstVisit(Shot shot)
        {
            foreach (var character in shot.Characters) character.View.gameObject.SetActive(false);
            var width = 2f * U(16f);
            var head = SubjectShape(shot, "Menu subject", "What would you like to work on?", GlazeTokens.Text, width);
            var places = SectionShapes(shot, "Places", Places, width, chosen: 1, waiting: -1);
            var menu = shot.Board("Projects", PlaneMeters);
            var l = -width / 2f + U(PanelPadding);
            var r = width / 2f - U(PanelPadding);
            var y = -U(PanelPadding);
            y -= Body(menu, "Folders", "Folders on your computer", l, y, r - l, Secondary).Height + U(Grid);
            foreach (var (name, fact) in new[] { ("storefront-api", "changed today"), ("docs-site", "3 days ago"), ("race-results", "last week") })
            {
                RefinedRow(menu, name, l, r, y, (cx, cy) => ProtoGlyph(menu, "Folder", GlyphFolder, cx, cy, GlazeTokens.Text), fact, Secondary, more: true);
                y -= U(RowHeight) + menu.TargetGap;
            }
            ContentShape(menu, width, -y + U(GroupGap) * 0.5f + FooterRoom(menu) - menu.TargetGap, new Prompt("Close", GlazeIcon.Close), null, null,
                new Prompt("New project", GlazeIcon.CreateProject, main: true));
            LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { head, places, menu } });
        }

        // ---------------------------------------------------------------------------------------------
        // New project, slid out beside the menu on Projects: the companion's question, its answers.

        /// <param name="view">With the companion's view that this can't be built over its question: the longest a reply shows, the quote then the question alone.</param>
        /// <param name="tooLong">The view over a quote of three rows, the companion's line and its question: past the field, kept to show why a quote holds two.</param>
        private static void NewProjectQuestions(Shot shot, bool view = false, bool tooLong = false)
        {
            var menuWidth = 2f * U(16f);
            var stepsWidth = 2f * U(19f);
            var head = SubjectShape(shot, "Menu subject", "What would you like to work on?", GlazeTokens.Text, menuWidth);
            // With the view, an idea that is not software yet, and a question that looks for the software in it.
            var idea = view ? "Get more of my running club to races" : "A page for my running club's race times";
            var newHead = SubjectShape(shot, "Idea subject", idea, GlazeTokens.Text, stepsWidth, head.Height);
            var places = SectionShapes(shot, "Places", Places, menuWidth, chosen: 1, waiting: -1);
            var steps = SectionShapes(shot, "Steps", Steps, stepsWidth, chosen: 1, waiting: -1);

            var menu = shot.Board("Projects", PlaneMeters);
            var ml = -menuWidth / 2f + U(PanelPadding);
            var mr = menuWidth / 2f - U(PanelPadding);
            var my = -U(PanelPadding);
            RefinedRow(menu, "Storefront API", ml, mr, my, (cx, cy) => Glyph(menu, "State", GlazeIcon.Working, cx, cy, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Foreground)),
                "2 tasks running", Secondary, more: true);
            my -= U(RowHeight) + menu.TargetGap;
            RefinedRow(menu, "Docs site", ml, mr, my, (cx, cy) => Glyph(menu, "State", GlazeIcon.CheckingItsWork, cx, cy, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Foreground)),
                "1 task checking its work", Secondary, more: true);
            my -= U(RowHeight) + U(GroupGap);
            my -= Body(menu, "Folders", "Folders on your computer", ml, my, mr - ml, Secondary).Height + U(Grid);
            RefinedRow(menu, "race-results", ml, mr, my, (cx, cy) => ProtoGlyph(menu, "Folder", GlyphFolder, cx, cy, GlazeTokens.Text), "last week", Secondary, more: true);
            my -= U(RowHeight);

            var page = shot.Board("Questions", PlaneMeters);
            var pl = -stepsWidth / 2f + U(PanelPadding);
            var pr = stepsWidth / 2f - U(PanelPadding);
            var py = -U(PanelPadding);
            if (view) py -= Body(page, "View", CompanionText.ThinksNotBuildable, pl, py, pr - pl, Secondary).Height + U(Grid);
            var says = tooLong
                ? CompanionText.Says("Getting people to races is more habit than software, but a page could help. Would a page of the club's next races help, or a reminder before each one?")
                : view
                    ? CompanionText.Says("Would a page of the club's next races help, or a reminder before each one?")
                    : CompanionText.Says("Should the page show each runner's best time, or every race?");
            py -= Body(page, "Asks", says, pl, py, pr - pl, GlazeTokens.Text, lines: tooLong ? 3 : 2, lean: true).Height + U(GroupGap);
            var inset = U(RowInset);
            var tall = U(RowHeight);
            var half = (pr - pl + 2f * inset - page.TargetGap) / 2f;
            var answers = view ? new[] { ("A page of next races", 0), ("A reminder before each", 1) } : new[] { ("Each runner's best", 0), ("Every race, newest first", 1) };
            foreach (var (answer, index) in answers)
            {
                var shapeLeft = pl - inset + index * (half + page.TargetGap);
                InnerShape(page, shapeLeft + half / 2f, py - tall / 2f, half, tall);
                Target(page, "Answer " + answer, shapeLeft + half / 2f, py - tall / 2f, half, compact: true);
                Body(page, "Answer " + answer + " words", answer, shapeLeft + inset, py - tall / 2f + U(BodySize) * 0.6f, half - U(1.6f), Plain);
            }
            py -= tall + page.TargetGap;
            var index2 = 0;
            foreach (var (row, icon) in new[] { (CompanionText.TypeAnswer, GlazeIcon.Type), (CompanionText.GoOnWithout, GlazeIcon.Next) })
            {
                var shapeLeft = pl - inset + index2 * (half + page.TargetGap);
                InnerShape(page, shapeLeft + half / 2f, py - tall / 2f, half, tall);
                Target(page, row, shapeLeft + half / 2f, py - tall / 2f, half, compact: true);
                Glyph(page, "Icon " + row, icon, shapeLeft + inset + U(IconColumn) / 2f, py - tall / 2f, GlazeTokens.Text, 1.1f);
                Body(page, "Row " + row, row, shapeLeft + inset + U(IconColumn) + U(Grid), py - tall / 2f + U(BodySize) * 0.6f, half - inset - U(IconColumn) - U(Grid) - U(0.6f), Plain);
                index2++;
            }
            py -= tall + U(GroupGap);
            py -= Body(page, "Source", CompanionText.Note, pl, py, pr - pl, Secondary, lines: 3).Height;

            var height = Mathf.Max(-my + U(GroupGap), -py + U(GroupGap)) + FooterRoom(page);
            ContentShape(menu, menuWidth, height, new Prompt("Close", GlazeIcon.Close), null, null, new Prompt("New project", GlazeIcon.CreateProject));
            // Three prompts: at 18 dp a footer this wide holds no fourth with these words, so Start over is not here.
            ContentShape(page, stepsWidth, height, new Prompt("Close", GlazeIcon.Close), null,
                new Prompt("Hold to talk", GlazeIcon.HoldToTalk), new Prompt(CompanionText.MakeTheRecap, GlazeIcon.Next, main: true));
            LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { head, places, menu }, new[] { newHead, steps, page } });
        }
    }
}
