#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The information panel's rows (<see cref="RoomRowsView"/>) on the panel itself: New Project's recap, rows with no
    /// mark and one that wraps; its steps while it starts, two confirmed with their checks and one not yet; a check's four
    /// steps, one failed (2.8); and a page of eight observed files (2.6). Each is measured (a hairline 9 dp under one row
    /// and 9 dp over the next, every row's words on one line down the panel) and read in the pixels (the hairline lighter
    /// than the panel by what the page's 12 percent gives, each mark in its own colour).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] RowsStates = { "45-rows-recap", "45-rows-steps", "45-rows-checks", "45-rows-changes", "45-rows-part", "45-rows-arriving" };

        /// <summary>A first task long enough to be read in parts.</summary>
        private const string RowsLongTask = "Make a kite that follows my hand and climbs when I raise it, with a tail that streams behind it and a score that counts how high it has climbed since the last time it fell";

        private static List<RoomRow> RowsOf(string state)
        {
            RoomRow Fact(string label, string theirs, RoomRowMark mark = RoomRowMark.None) => new RoomRow(LineWords.Ours(label) + LineWords.Theirs(theirs), mark);
            RoomRow Ours(string words, RoomRowMark mark = RoomRowMark.None) => new RoomRow(LineWords.Ours(words), mark);
            switch (state)
            {
                case "45-rows-recap":
                case "45-rows-arriving":
                    return new List<RoomRow>
                    {
                        Fact("Project · ", "Kite Run"), Fact("First Task · ", "Make a kite that follows my hand and climbs when I raise it"), Fact("Folder · ", "kite-run"),
                        Fact("Model · ", "Glimmer 30B · Ask First"),
                    };
                case "45-rows-steps":
                    return new List<RoomRow> { Ours("Create Project · Confirmed", RoomRowMark.Check), Ours("Create First Task · Confirmed", RoomRowMark.Check), Ours("Start Task · Sent · Awaiting Agent") };
                case "45-rows-part":
                    // A review's second part: the first task from its third line, three lines of it, then the next item whole.
                    return new List<RoomRow> { new RoomRow(LineWords.Ours("First Task · ") + LineWords.Theirs(RowsLongTask), fromLine: 2, lines: 3), new RoomRow(LineWords.Ours("Folder · ") + LineWords.Theirs("kite-run"), fromLine: 0, lines: 1) };
                case "45-rows-checks":
                    return new List<RoomRow> { Ours("Type Check", RoomRowMark.Check), Ours("Build", RoomRowMark.Check), Ours("Page Drawn", RoomRowMark.Cross), Ours("VR Offered", RoomRowMark.Check) };
                default:
                    // A page of Changes as the screen words its rows (2.6): the kind, a change's lines, then the path, the agent's own word, last.
                    return new[] { ("Changed", "src/game.js", "120 to 134 Lines"), ("Changed", "src/balloons/golden/burst-particles.js", "41 to 54 Lines"), ("Made", "src/gold.js", ""),
                            ("Changed", "src/score.js", "40 to 52 Lines"), ("Changed", "src/sounds.js", "12 to 15 Lines"), ("Made", "assets/pop-gold.wav", ""), ("Changed", "src/menu.js", "88 to 90 Lines"),
                            ("Removed", "notes/old-plan.md", "") }
                        .Select(file => new RoomRow(LineWords.Ours(file.Item1 + KitMomentWords.Dot + (file.Item3.Length > 0 ? file.Item3 + KitMomentWords.Dot : "")) + LineWords.Theirs(file.Item2))).ToList();
            }
        }

        private static PartShot RowsShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var rows = RowsOf(state);
            Require(rows.Count <= RoomRows.APage && RoomRows.Page(rows, 0).Count == rows.Count, "the page holds more rows than eight");
            var unit = RoomType.Units(1f);
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(RoomGeometry.PanelDp);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var panel = Halcyonic.XR.UI.RoomSurface.Create(plane, "Information panel", RoomSurfaceKind.Information, KitType.Order.Plate);
            var view = RoomRowsView.Create(panel.transform);
            var tall = view.Show(rows, width - 2f * pad);
            var size = new Vector2(width, tall + 2f * pad);
            panel.InnerBar = null;
            panel.Draw(size);
            // Half way in (the panel's arrival, DESIGN 1.5): its glass at half and its words with it.
            var arriving = state == "45-rows-arriving";
            if (arriving)
            {
                panel.Shown = 0.5f;
                foreach (var words in panel.GetComponentsInChildren<TMPro.TMP_Text>(false)) words.alpha = 0.5f;
            }
            view.transform.localPosition = new Vector3(-width / 2f + pad, size.y / 2f - pad, -RoomType.Units(0.5f));
            Place(plane, eyes, 0f, shot.Elevation);

            // The measures: each row's words whole on their lines, every row's words starting on one line down the panel, a
            // hairline 9 dp under each row but the last and 9 dp over the next, a mark only where the row carries one.
            var gap = RoomType.Scaled(RoomRows.GapDp);
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var indent = RoomType.Scaled(RoomRows.IndentDp(rows));
            Require(view.Count == rows.Count && Mathf.Approximately(view.Size.x, width - 2f * pad) && Mathf.Approximately(view.Size.y, tall), "the rows report " + view.Count + " rows, " + view.Size);
            Require(RoomRows.Marked(rows) == (state is "45-rows-steps" or "45-rows-checks"), "the page's marks are not as its rows say");
            for (var index = 0; index < rows.Count; index++)
            {
                var label = view.Label(index);
                label.ForceMeshUpdate(true);
                var what = "row " + index + " (\"" + rows[index].Words.Text + "\")";
                Require(label.gameObject.activeSelf && label.text.Length > 0 && !label.isTextOverflowing && label.textInfo.characterCount > 0, what + " shows no words, or cuts them");
                Require(Mathf.Abs(label.transform.localPosition.x - indent) < 1e-6f, what + " starts " + (label.transform.localPosition.x / unit).ToString("0.0") + " dp in, not " + (indent / unit).ToString("0.0"));
                var lines = label.textInfo.lineCount;
                // At the standard size the page's own breaks; larger, a row may take more lines, never more than a row's most.
// No line of a row ends in the separator: the break stands in its place, the dot dropped (WORDS rule 2).
                for (var each = 0; each < lines - 1; each++)
                    Require(label.textInfo.characterInfo[label.textInfo.lineInfo[each].lastVisibleCharacterIndex].character != '·', what + " ends its line " + (each + 1) + " in the separator");
                if (state == "45-rows-changes" && index == 1)
                {
                    var shown = label.GetParsedText();
                    // The first line is Halcyonic's own words whole; the path follows on its own line (or lines, where it is wider than one).
                    var parts = shown.Split('\n');
                    Require(parts.First().Trim() == "Changed · 41 to 54 Lines" && string.Concat(parts.Skip(1).Select(part => part.Trim())) == "src/balloons/golden/burst-particles.js",
                        "the long path's row does not break at its separator, the path whole on its own line: \"" + shown.Replace("\n", " / ") + "\"");
                }
                var wantedLines = rows[index].Lines ?? ((state is "45-rows-recap" or "45-rows-arriving") && index == 1 || state == "45-rows-changes" && index == 1 ? 2 : 1);
                Require(rows[index].Lines != null ? lines == wantedLines : GlazeText.Scale > 1.001f ? lines >= wantedLines && lines <= RoomRowsView.RowLines : lines == wantedLines,
                    what + " takes " + lines + " lines");
                if (state == "45-rows-part" && index == 0)
                {
                    // The part is the words' own lines from the third: it does not start the task again, and it is words of the task.
                    var part = string.Join(" ", label.GetParsedText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    Require(!part.StartsWith("First Task", StringComparison.Ordinal) && !part.StartsWith("Make a kite", StringComparison.Ordinal) && RowsLongTask.Contains(part),
                        "the part is not the task's own later lines: \"" + part + "\"");
                }
                var bottom = -label.transform.localPosition.y + Mathf.Max(line, lines * KitType.LinePitch(label));
                var mark = view.Mark(index);
                var wanted = rows[index].Mark == RoomRowMark.Check ? RoomGlyph.Check : rows[index].Mark == RoomRowMark.Cross ? RoomGlyph.Cross : (RoomGlyph?)null;
                Require(mark.Glyph == wanted, what + " carries " + (mark.Glyph?.ToString() ?? "no mark"));
                if (wanted != null)
                    Require(Mathf.Abs(-mark.transform.localPosition.y - (bottom - Mathf.Max(line, lines * KitType.LinePitch(label)) / 2f)) < unit * 0.5f
                        && Mathf.Abs(mark.transform.localPosition.x - RoomType.Scaled(RoomRows.MarkDp) / 2f) < 1e-6f, what + "'s mark does not stand before its words, at their middle");
                var hairline = view.Hairline(index);
                Require(hairline.gameObject.activeSelf == (index < rows.Count - 1), what + (index < rows.Count - 1 ? " has no hairline under it" : ", the last, has a hairline under it"));
                if (index < rows.Count - 1)
                {
                    var at = -hairline.transform.localPosition.y;
                    var next = -view.Label(index + 1).transform.localPosition.y;
                    Require(Mathf.Abs(at - unit * 0.5f - (bottom + gap)) < unit * 0.05f && Mathf.Abs(next - (at + unit * 0.5f + gap)) < unit * 0.05f,
                        what + "'s hairline stands " + ((at - unit * 0.5f - bottom) / unit).ToString("0.00") + " dp under it and " + ((next - at - unit * 0.5f) / unit).ToString("0.00") + " dp over the next");
                }
            }

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                var lifts = new List<int>();
                for (var index = 0; index < rows.Count; index++)
                {
                    // The hairline, read at the panel's right where no words stand: lighter than the panel 4 dp above it, by about what white at 12 percent gives.
                    if (index < rows.Count - 1)
                    {
                        var hairline = view.Hairline(index).transform;
                        var on = Light(At(dark, hairline.TransformPoint(new Vector3(0.42f, 0f, 0f))));
                        var above = Light(At(dark, hairline.TransformPoint(new Vector3(0.42f, 0f, 0f)) + plane.up * (4f * unit)));
                        lifts.Add(on - above);
                        if (!arriving && (on - above < 8 || on - above > 40)) failures.Add("the hairline under row " + index + " reads " + on + " on the panel's " + above + ": not the page's white at 12 percent");
                    }
                    // A mark's ink, in its own colour, on the middle of each of its glyph's shapes.
                    var mark = view.Mark(index);
                    if (mark.Glyph is RoomGlyph glyph)
                    {
                        var ink = (Color32)(glyph == RoomGlyph.Check ? RoomRowsView.PassedColour : RoomRowsView.FailedColour);
                        foreach (var shape in RoomGlyphs.Of(glyph))
                        foreach (var middle in shape.Middles())
                        {
                            var pixel = At(dark, mark.transform.TransformPoint(new Vector3(middle.x / RoomGlyphs.Grid - 0.5f, 0.5f - middle.y / RoomGlyphs.Grid, 0f)));
                            var off = Mathf.Max(Mathf.Abs(pixel.r - ink.r), Mathf.Max(Mathf.Abs(pixel.g - ink.g), Mathf.Abs(pixel.b - ink.b)));
                            if (off > 14) failures.Add("row " + index + "'s mark is " + off + " of 255 from its colour");
                        }
                    }
                    // The row's words are in the pixels.
                    var label = view.Label(index);
                    var bounds = label.textBounds;
                    var most = 0;
                    for (var step = 0; step <= 80; step++)
                        most = Mathf.Max(most, Light(At(dark, label.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 80f), bounds.max.y - KitType.LinePitch(label) * 0.5f, 0f)))));
                    if (most < (arriving ? 90 : 170)) failures.Add("row " + index + "'s words are not in the pixels, the lightest reads " + most + " of 255");
                }
                if (arriving)
                {
                    // Half way in: the glass over the bright room reads between the room and the glass whole: darker than the room.
                    // Inside the panel's top padding, and the plain wall just above it (the fixture room has dark blocks lower down).
                    var inside = Light(At(bright, panel.transform.TransformPoint(new Vector3(size.x * 0.4f, size.y * 0.47f, 0f))));
                    var room = Light(At(bright, panel.transform.TransformPoint(new Vector3(size.x * 0.4f, size.y * 0.7f, 0f))));
                    if (!(inside < room - 15 && inside > 110)) failures.Add("the half-shown panel reads " + inside + " over the bright room's " + room + " of 255");
                    Debug.Log("Halcyonic: parts: " + name + ": half shown, the panel reads " + inside + " over the bright room's " + room + " of 255.");
                }
                Debug.Log("Halcyonic: parts: " + name + ": " + rows.Count + " rows, their hairlines lighter than the panel by " + string.Join(", ", lifts.Select(lift => lift.ToString(CultureInfo.InvariantCulture)))
                    + " of 255, " + rows.Count(row => row.Mark != RoomRowMark.None) + " marks in their colours.");
            };
            return shot;
        }
    }
}
