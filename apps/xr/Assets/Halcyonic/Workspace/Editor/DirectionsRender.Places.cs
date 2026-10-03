#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// ADR 0026's last two places in its style, for lane U to build from. Usage: each limit a row, its
    /// share left the small fact, one chosen with its side panel. A meter under each row read as an
    /// underline, which the selection check refuses, so the words carry the share alone. Settings:
    /// each setting a row, its value the small fact, one chosen with the one change it offers as the
    /// main action. Each on one plane facing the eyes, checked as the refined frames are.
    /// </summary>
    public static partial class DirectionsRender
    {
        private static IEnumerable<(string Name, bool Window, Action<Shot> Build)> PlacesShots() => new (string, bool, Action<Shot>)[]
        {
            ("r15-usage", false, shot => Lay(Facing.Eyes, () => Usage(shot, chosen: false))),
            ("r16-usage-limit", false, shot => Lay(Facing.Eyes, () => Usage(shot, chosen: true))),
            ("r17-settings", false, shot => Lay(Facing.Eyes, () => Settings(shot, chosen: false))),
            ("r18-settings-text-size", false, shot => Lay(Facing.Eyes, () => Settings(shot, chosen: true))),
        };

        /// <summary>Each limit as the reading shows it: whose and which window, and the share left, rounded up.</summary>
        private static readonly (string Words, string Fact)[] Limits =
        {
            ("Claude Code, 5-hour window", "At most 60% left"),
            ("Claude Code, weekly", "At most 85% left"),
            ("Codex, 5-hour window", "At most 3% left"),
            ("Codex, weekly", "At most 40% left"),
        };

        // ---------------------------------------------------------------------------------------------
        // Usage: each limit a row; chosen, its side panel says when it was seen, when it resets and
        // whose account it is.

        private static void Usage(Shot shot, bool chosen)
        {
            var width = 2f * U(16f);
            var sideWidth = 2f * U(13f);
            var head = SubjectShape(shot, "Menu subject", "How much is left before each limit?", GlazeTokens.Text, width);
            var places = SectionShapes(shot, "Places", Places, width, chosen: 2, waiting: 0);

            var menu = shot.Board("Usage", PlaneMeters);
            var l = -width / 2f + U(PanelPadding);
            var r = width / 2f - U(PanelPadding);
            var y = -U(PanelPadding);
            // A page's source line counts as one of its rows: with a side panel open, 4 rows and a source
            // reach 0.1 degrees past a Quest 3S's field. So 3 limits a page, and the fourth on the next.
            for (var index = 0; index < 3; index++)
            {
                var (words, fact) = Limits[index];
                RefinedRow(menu, words, l, r, y, null, fact, Secondary, chosen: chosen && index == 0, more: true);
                y -= U(RowHeight) + menu.TargetGap;
            }
            y -= U(GroupGap) - menu.TargetGap;
            y -= Body(menu, "Source", UsageLeftPresenter.FromSeorak, l, y, r - l, Secondary).Height;
            var menuNeed = -y + U(GroupGap) + FooterRoom(menu);
            var close = new Prompt("Close", GlazeIcon.Close);
            var refresh = new Prompt(WorkspaceText.Refresh, GlazeIcon.Refresh);
            if (!chosen)
            {
                ContentShape(menu, width, menuNeed, close, refresh, null, new Prompt("Next page", GlazeIcon.Next));
                LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { head, places, menu } });
                return;
            }

            var sideHead = SubjectShape(shot, "Side subject", Limits[0].Words, GlazeTokens.Text, sideWidth, head.Height);
            var side = shot.Board("Limit", PlaneMeters);
            var sl = -sideWidth / 2f + U(PanelPadding);
            var sr = sideWidth / 2f - U(PanelPadding);
            var sy = -U(PanelPadding);
            // What the row does not say already: its share left is on the row.
            foreach (var (name, value, lines) in new[]
            {
                ("Seen", "Today at 15:18", 1),
                ("Resets", "Today at 18:00", 1),
                ("Account", "Not identified: it may be any account used on your computer", 2),
            })
            {
                sy -= SideFact(side, name, value, sl, sr, sy, lines) + U(GroupGap);
            }
            sy -= Body(side, "Source", UsageLeftPresenter.FromSeorak, sl, sy, sr - sl, Secondary, lines: 2).Height;
            var sideNeed = -sy + U(GroupGap) + FooterRoom(side);
            // Both columns end on one line, their footers on it. Paging waits while a row is chosen.
            var menuHeight = Mathf.Max(menuNeed, sideNeed - places.Height - U(RowGap));
            ContentShape(menu, width, menuHeight, close, refresh, null, null);
            ContentShape(side, sideWidth, menuHeight + places.Height + U(RowGap), new Prompt("Close details", GlazeIcon.Close), null, null, null);
            LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { head, places, menu }, new[] { sideHead, side } });
        }

        /// <summary>
        /// A side panel's fact: its name in the secondary colour, its value under it, both at the
        /// content's size, since type only steps down; returns the height it took.
        /// </summary>
        private static float SideFact(Board board, string name, string value, float left, float right, float top, int lines = 1)
        {
            var nameHeight = Body(board, "Fact " + name, name, left, top, right - left, Secondary).Height;
            return nameHeight + U(Grid) + Body(board, "Value " + name, value, left, top - nameHeight - U(Grid), right - left, Plain, lines: lines).Height;
        }

        // ---------------------------------------------------------------------------------------------
        // Settings: each setting a row under its group's heading, its value the small fact; chosen, its
        // side panel says what it is now and what the change does, and the footer offers that change.

        private static void Settings(Shot shot, bool chosen)
        {
            var width = 2f * U(16f);
            var sideWidth = 2f * U(13f);
            var head = SubjectShape(shot, "Menu subject", "What would you like to change?", GlazeTokens.Text, width);
            var places = SectionShapes(shot, "Places", Places, width, chosen: 3, waiting: 0);

            // The second page while a comfort setting is chosen, the first otherwise.
            var (heading, rows) = chosen
                ? ("Comfort", new[] { ("Text size", "Standard"), ("Moving badges", "On"), ("Sounds", "On") })
                : ("Your space", new[] { ("Around you", "Your room"), ("The characters", "In front of you"), ("The menu", "Where you moved it") });
            var menu = shot.Board("Settings", PlaneMeters);
            var l = -width / 2f + U(PanelPadding);
            var r = width / 2f - U(PanelPadding);
            var y = -U(PanelPadding);
            y -= Body(menu, "Heading", heading, l, y, r - l, Secondary).Height + U(Grid);
            for (var index = 0; index < rows.Length; index++)
            {
                var (words, fact) = rows[index];
                RefinedRow(menu, words, l, r, y, null, fact, Secondary, chosen: chosen && index == 0, more: true);
                y -= U(RowHeight) + menu.TargetGap;
            }
            var menuNeed = -y + U(GroupGap) * 0.5f + FooterRoom(menu) - menu.TargetGap;
            var close = new Prompt("Close", GlazeIcon.Close);
            if (!chosen)
            {
                // A list that pages: Next page at the far right, where nothing is the main action.
                ContentShape(menu, width, menuNeed, close, null, null, new Prompt("Next page", GlazeIcon.Next));
                LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { head, places, menu } });
                return;
            }

            var sideHead = SubjectShape(shot, "Side subject", "Text size", GlazeTokens.Text, sideWidth, head.Height);
            var side = shot.Board("Setting", PlaneMeters);
            var sl = -sideWidth / 2f + U(PanelPadding);
            var sr = sideWidth / 2f - U(PanelPadding);
            var sy = -U(PanelPadding);
            sy -= SideFact(side, "Now", "The standard size", sl, sr, sy) + U(GroupGap);
            sy -= SideFact(side, "A step larger", "Text 15 percent larger, and 3 rows a page", sl, sr, sy, 2);
            var sideNeed = -sy + U(GroupGap) + FooterRoom(side);
            var menuHeight = Mathf.Max(menuNeed, sideNeed - places.Height - U(RowGap));
            // While a row is chosen, its change is the main action, and paging waits.
            ContentShape(menu, width, menuHeight, close, null, null, new Prompt("Make text larger", GlazeIcon.Change, main: true));
            ContentShape(side, sideWidth, menuHeight + places.Height + U(RowGap), new Prompt("Close details", GlazeIcon.Close), null, null, null);
            LayOnPlane(shot, 0f, new List<IReadOnlyList<Board>> { new[] { head, places, menu }, new[] { sideHead, side } });
        }
    }
}
