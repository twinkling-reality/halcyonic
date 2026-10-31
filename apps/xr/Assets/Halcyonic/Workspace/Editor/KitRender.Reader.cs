#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The reading surface at its start, its middle and its end (the art direction's d06, d06b, d07; SECURITY.md: a command is
    /// read whole before it can run): a synthetic 31-line command with comments and one line of 1,000 characters, longer than
    /// the well holds, with a long unbroken token in it. Each frame holds: Run only once the last row has been shown, Read to
    /// End filled as far as read, the end line only at the end, a fade only at an edge where rows are hidden, the foot where it
    /// stood when the reader opened, every row whole (one row of its line, never cut), the rows adding up to every line exactly,
    /// comments quieter, and the glass's truth.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ReaderStates = { "25-reader-start", "25-reader-middle", "25-reader-end" };

        private static string ReaderCommand()
        {
            var lines = new List<string>();
            for (var i = 1; i <= 30; i++)
                lines.Add(i % 5 == 1 ? "# step " + i + ": prepare the build" : "npm run step-" + i + " --workspace towers");
            lines.Insert(12, LongLine());
            return string.Join("\n", lines);
        }

        /// <summary>A line of exactly 1,000 characters: flags with spaces, then a 240-character token with none to wrap at.</summary>
        private static string LongLine()
        {
            var head = "node scripts/check-every-tower.js --config apps/tower-toss/config/towers.production.json --sign ";
            var token = string.Concat(Enumerable.Range(0, 24).Select(i => "c2lnbmVk" + i.ToString("00")));
            var flags = string.Concat(Enumerable.Range(0, 80).Select(i => " --tower tower-" + i.ToString("00") + "-check"));
            return (head + token + flags).Substring(0, 1000);
        }

        private static Shot ReaderShot(string state, Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "The reading surface, " + state.Substring("25-reader-".Length) + " of a command, synthetic data" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var moment = KitMoment.Question("Codex", ReaderCommand(), KitSandbox.MayLeave, complete: true, MomentAt);
            var reader = KitReader.Create(root);
            reader.Show(moment, ReaderCommand());
            Require(reader.Reading.Lines.Count == 31 && reader.Reading.Lines[12].Length == 1000, "the reader did not get the command's lines (" + reader.Reading.Lines.Count + ")");
            // Every line's rows add up to it exactly, and the long line runs past what the well holds, so it is read by paging.
            for (var line = 0; line < reader.Reading.Lines.Count; line++)
                Require(string.Concat(reader.Reading.Rows.Where(row => row.Line == line).Select(row => row.Text)) == reader.Reading.ShownLine(line), "line " + (line + 1) + "'s rows do not add up to it");
            var longRows = reader.Reading.Rows.Count(row => row.Line == 12);
            var longStart = reader.Reading.Rows.TakeWhile(row => row.Line < 12).Count();
            Require(longRows > reader.Fits + 1, "the long line takes " + longRows + " rows, not more than the well's " + reader.Fits);
            var foot = new[] { reader.Primary, reader.Decline, reader.Reply }.Select(button => button.transform.localPosition).ToList();
            var opened = reader.Primary.Words.text;
            Require(!reader.Reading.ReadWhole && opened == LabelText.ForTextMeshPro(KitReaderWords.ReadToEnd), "the reader opens with Run before the command is read");
            // Read as a person would: from the top, as far as the frame says. The middle stands inside the long line: at its start,
            // a page on, then back to its second row, so Read to End keeps the furthest it reached.
            var furthest = reader.Reading.First + reader.Shown.Count;
            // Each turn: Run never unlocks while a row the reader has not yet shown remains, and Read to End is filled exactly as
            // far as rows have been shown, so a line never counts as read before its last row has been.
            void Turn(int first)
            {
                reader.ScrollTo(first);
                furthest = Math.Max(furthest, reader.Reading.First + reader.Shown.Count);
                Require(!reader.Reading.ReadWhole || furthest >= reader.Reading.Rows.Count, "Run unlocked with rows still unshown (" + furthest + " of " + reader.Reading.Rows.Count + " shown)");
                if (!reader.Reading.ReadWhole)
                    Require(Mathf.Abs(reader.Primary.Progress - (float)furthest / reader.Reading.Rows.Count) < 1e-4f, "on a turn, Read to End is not filled as far as rows have been shown ("
                        + reader.Primary.Progress.ToString("0.000") + " for " + furthest + " of " + reader.Reading.Rows.Count + ")");
            }
            var middle = state.EndsWith("middle", StringComparison.Ordinal);
            var pages = state.EndsWith("start", StringComparison.Ordinal) ? 0 : middle ? 0 : 99;
            if (middle)
            {
                Turn(longStart);
                Turn(longStart + reader.Fits);
                Turn(longStart + 1);
                Require(reader.Shown.Count > 0 && Enumerable.Range(reader.Reading.First, reader.Shown.Count).All(row => reader.Reading.Rows[row].Line == 12)
                    && reader.Reading.HiddenAbove && reader.Reading.HiddenBelow(reader.Shown.Count), "the middle does not stand inside the long line");
            }
            for (var page = 1; page <= pages && !reader.Reading.ReadWhole; page++) Turn(reader.Reading.First + Math.Max(1, reader.Fits));
            if (pages > 1) Turn(int.MaxValue);

            shot.Elevation = 0f;
            shot.Centre = Place(reader.transform, eyes, 0f, shot.Elevation);
            // The panel subtends its handoff size from the eyes: 704 by 528 dp, 44 by 33 degrees.
            float Subtends(Vector3 a, Vector3 b) => Vector3.Angle(reader.transform.TransformPoint(a) - eyes, reader.transform.TransformPoint(b) - eyes);
            var (wide, high) = (Subtends(new Vector3(-reader.Size.x / 2f, 0f, 0f), new Vector3(reader.Size.x / 2f, 0f, 0f)), Subtends(new Vector3(0f, -reader.Size.y / 2f, 0f), new Vector3(0f, reader.Size.y / 2f, 0f)));
            Require(Mathf.Abs(wide - SpatialKit.Degrees(SpatialKit.ReaderWidthDp)) < 0.3f && Mathf.Abs(high - SpatialKit.Degrees(SpatialKit.ReaderHeightDp)) < 0.3f,
                "the reader subtends " + wide.ToString("0.0") + " by " + high.ToString("0.0") + " degrees, not its 44 by 33");
            var carriers = new[] { reader.Carrier };
            Stand(shot, reader.transform, eyes, carriers, reader.Size);
            shot.Targets.Add(("reader", reader.transform, reader.Targets));
            var words = reader.GetComponentsInChildren<TextMeshPro>(false).Where(label => !GlazeIcons.IsIcon(label)).Cast<TMP_Text>().ToList();
            foreach (var failure in GlassTruth(new[] { (reader.Carrier, (IEnumerable<TMP_Text>)words) })) shot.Failures.Add(failure);
            shot.AfterEye = (camera, eye, found) => found.AddRange(GlassFaceHolds(reader.Carrier, camera, eye, "the reader"));

            var reading = reader.Reading;
            var end = state.EndsWith("end", StringComparison.Ordinal);
            // Run only once the last row has been shown; until then Read to End, locked, filled as far as read.
            Require(reading.ReadWhole == end, "the reading is " + (reading.ReadWhole ? "" : "not ") + "whole at its " + state);
            Require(reader.Primary.Available == reading.ReadWhole, "Run stands before the last line has been shown, or stays locked after");
            Require((reader.Primary.Words.text == LabelText.ForTextMeshPro(KitMomentWords.Action(KitMomentAction.YesRunIt))) == reading.ReadWhole, "the primary's word is not Run exactly when the command is read whole");
            if (!reading.ReadWhole)
                Require(Mathf.Abs(reader.Primary.Progress - (float)furthest / reading.Rows.Count) < 1e-4f && reader.Primary.ProgressFill.gameObject.activeSelf,
                    "Read to End is not filled as far as the command has been read (" + reader.Primary.Progress.ToString("0.000") + " for " + furthest + " of " + reading.Rows.Count + ")");
            Require(reader.EndLine.gameObject.activeSelf == end, "the end line stands where the command goes on, or is missing at its end");
            if (end) Require(reader.Shown.Count > 1, "the last page shows only " + reader.Shown.Count + " line: it is not full");
            // The head as the card's: the mark with the question's sign, then the eyebrow beside it.
            Require(reader.Mark.gameObject.activeSelf && reader.MarkSymbol.gameObject.activeSelf && reader.MarkSymbol.text == "?"
                && reader.Eyebrow.transform.localPosition.x > reader.Mark.transform.localPosition.x, "the reader's head has no mark with its sign before the eyebrow");
            // Where rows are hidden, the next row stands half-faded under the fade at that edge, not counted as read.
            var shownCount = reader.Shown.Count;
            Require(reader.PeekAbove.gameObject.activeSelf == reading.HiddenAbove && reader.PeekBelow.gameObject.activeSelf == reading.HiddenBelow(shownCount),
                "a half-shown row stands where nothing is hidden, or is missing where rows are");
            if (reading.HiddenAbove) Require(reader.PeekAbove.text == reading.RowWords(reading.First - 1).ForTextMeshPro().Text, "the row above is not the one before the first shown");
            if (reading.HiddenBelow(shownCount)) Require(reader.PeekBelow.text == reading.RowWords(reading.First + shownCount).ForTextMeshPro().Text, "the row below is not the one after the last shown");
            // A fade only at an edge where rows are hidden.
            Require(reader.FadedAbove == reading.HiddenAbove && reader.FadedBelow == reading.HiddenBelow(reader.Shown.Count), "a fade stands where nothing is hidden, or is missing");
            // The foot stands where it stood when the reader opened.
            Require(new[] { reader.Primary, reader.Decline, reader.Reply }.Select(button => button.transform.localPosition).SequenceEqual(foot), "the foot moved as the command was read");
            // The half-shown rows whole too.
            foreach (var peek in new[] { reader.PeekAbove, reader.PeekBelow }.Where(label => label.gameObject.activeSelf))
            {
                peek.ForceMeshUpdate(true);
                Require(!peek.isTextTruncated && peek.textInfo.lineCount <= 1, "the half-shown " + peek.name + " is cut");
            }
            // Every shown row whole: one row of its line, never cut; comments quieter; theirs.
            for (var i = 0; i < reader.Shown.Count; i++)
            {
                var label = reader.Shown[i];
                var row = reading.Rows[reading.First + i];
                var what = "row " + (reading.First + i + 1) + " (line " + (row.Line + 1) + ")";
                label.ForceMeshUpdate(true);
                var info = label.textInfo;
                Require(!label.isTextTruncated && info.lineCount <= 1 && !info.characterInfo.Take(info.characterCount).Any(character => character.isVisible && character.character == '…'),
                    "the command's " + what + " is cut");
                Require(SameColour(label.color, reading.IsComment(row.Line) ? KitType.Colour(SpatialKit.TextTertiary) : KitType.Text), what + " is not in its colour");
                Require(GlazeRuns.Of(label).Theirs.Count == (row.Text.Length == 0 ? 0 : 1), what + " is not theirs");
                Require(reader.Indent > 0f && Mathf.Abs(label.transform.localPosition.x - (reader.RowLeft + (row.Continued ? reader.Indent : 0f))) < 1e-6f, what + " does not hang as its line wraps");
            }
            FacesTheEyes(shot, reader.transform, carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: reader " + state + ": rows " + (reading.First + 1) + " to " + (reading.First + reader.Shown.Count) + " of " + reading.Rows.Count
                + ", read " + reading.Progress.ToString("0.00") + ", primary \"" + reader.Primary.Words.text + "\".");
            return shot;
        }
    }
}
