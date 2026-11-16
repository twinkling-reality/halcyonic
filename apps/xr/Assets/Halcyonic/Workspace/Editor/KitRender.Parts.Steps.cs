#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The plain "what is happening" list (<see cref="RoomStepsView"/>) on the information panel: a task at work, one that waits
    /// for the person, one ready, one whose checks failed, and one whose request runs long. Each is measured (every step under
    /// the last with the rows' gap and never over it, every word whole, a line only where its step has one and laid whole, a
    /// mark's tile before each word at its first line's middle, nothing in the list that takes a press) and read in the pixels
    /// (each tile in its tone, each glyph's ink, each word in the pixels, the line amber where it says what waits).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] StepsStates = { "46-steps-working", "46-steps-needs-you", "46-steps-ready", "46-steps-failed", "46-steps-long", "46-steps-usage" };

        /// <summary>A request longer than three lines at either size, laid whole.</summary>
        private const string StepsLongRequest = "Make a kite that follows my hand and climbs when I raise it, with a tail that streams behind it, a score that counts how high it has climbed since the last time it fell, and a gentle sound each time it turns";

        private static List<TaskStep> StepsOf(string state)
        {
            TaskStep Step(TaskStepKind kind, GlazeIcon mark, string word, LineWords? line = null) => new TaskStep(kind, mark, word, line);
            var request = Step(TaskStepKind.Done, GlazeIcon.FinishedThisRound, TaskSteps.YourRequest, LineWords.Theirs(state == "46-steps-long" ? StepsLongRequest : "Make a page that says hello and counts my visits"));
            switch (state)
            {
                case "46-steps-needs-you":
                    return new List<TaskStep>
                    {
                        request, Step(TaskStepKind.NeedsYou, GlazeIcon.WaitingForYou, TaskSteps.Working, LineWords.Ours(TaskSteps.NeedsYou + ": " + KitMomentWords.RunThisCommand)),
                        Step(TaskStepKind.NotYet, GlazeIcon.NotStarted, TaskSteps.Check), Step(TaskStepKind.NotYet, GlazeIcon.NotStarted, TaskSteps.Ready),
                    };
                case "46-steps-ready":
                    return new List<TaskStep>
                    {
                        request, Step(TaskStepKind.Done, GlazeIcon.FinishedThisRound, TaskSteps.Working, LineWords.Ours(TaskSteps.Changed(3))),
                        Step(TaskStepKind.Done, GlazeIcon.FinishedThisRound, TaskSteps.Check), Step(TaskStepKind.Done, GlazeIcon.FinishedThisRound, TaskSteps.Ready),
                    };
                case "46-steps-failed":
                    return new List<TaskStep>
                    {
                        request, Step(TaskStepKind.Done, GlazeIcon.FinishedThisRound, TaskSteps.Working, LineWords.Ours(TaskSteps.Changed(3))),
                        Step(TaskStepKind.End, GlazeIcon.ChecksFailed, TaskSteps.ChecksFailed),
                    };
                default:
                    return new List<TaskStep>
                    {
                        request, Step(TaskStepKind.Now, GlazeIcon.Working, TaskSteps.Working, LineWords.Ours("Editing ") + LineWords.Theirs("index.html")),
                        Step(TaskStepKind.NotYet, GlazeIcon.NotStarted, TaskSteps.Check, LineWords.Ours(TaskSteps.CheckDoes)), Step(TaskStepKind.NotYet, GlazeIcon.NotStarted, TaskSteps.Ready),
                    };
            }
        }

        private static PartShot StepsShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var steps = StepsOf(state);
            var unit = RoomType.Units(1f);
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(RoomGeometry.PanelDp);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var panel = Halcyonic.XR.UI.RoomSurface.Create(plane, "Information panel", RoomSurfaceKind.Information, KitType.Order.Plate);
            var view = RoomStepsView.Create(panel.transform);
            // At work where the agent app reports usage: the tokens figure under the list (XR's 09:33Z (c), AUDIT's 09:34Z).
            var usage = state == "46-steps-usage" ? new TaskUsageFigure(TaskUsage.TokensUsed, "12,400", new[] { "Counted by the agent app" }, true) : null;
            var tall = view.Show(steps, width - 2f * pad, usage);
            var size = new Vector2(width, tall + 2f * pad);
            panel.InnerBar = null;
            panel.Draw(size);
            view.transform.localPosition = new Vector3(-width / 2f + pad, size.y / 2f - pad, -RoomType.Units(0.5f));
            Place(plane, eyes, 0f, shot.Elevation);

            var gap = RoomType.Scaled(RoomRows.GapDp);
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var tile = RoomType.Scaled(RoomStepsView.TileDp);
            var indent = tile + RoomType.Scaled(RoomStepsView.TileGapDp);
            Require(view.Count == steps.Count && Mathf.Approximately(view.Size.y, tall), "the list reports " + view.Count + " steps, " + view.Size);
            // It only says: nothing in it takes a press or a ray.
            Require(view.GetComponentsInChildren<Collider>(true).Length == 0 && view.GetComponentsInChildren<KitPress>(true).Length == 0, "the list holds a part that takes a press");
            var below = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var what = "step " + index + " (" + step + ")";
                var word = view.Word(index);
                word.ForceMeshUpdate(true);
                Require(word.gameObject.activeSelf && word.GetParsedText().Trim() == step.Word && !word.isTextOverflowing, what + "'s word is not shown whole: \"" + word.GetParsedText() + "\"");
                Require(Mathf.Abs(word.transform.localPosition.x - indent) < 1e-6f, what + "'s word starts " + (word.transform.localPosition.x / unit).ToString("0.0") + " dp in, not " + (indent / unit).ToString("0.0"));
                var top = -word.transform.localPosition.y;
                var wanted = index == 0 ? 0f : below + gap;
                Require(Mathf.Abs(top - wanted) < unit * 0.05f, what + " stands " + ((top - below) / unit).ToString("0.00") + " dp under the step before, not the rows' " + RoomRows.GapDp.ToString("0") + " dp");
                var colour = RoomStepsView.WordColourOf(step);
                Require(Mathf.Approximately(word.color.a, colour.a) && Mathf.Approximately(word.color.r, colour.r), what + "'s word is not in its colour");
                // The tile before its word's first line, at its middle; its glyph on it, as the mark says.
                var expected = RoomStepsView.MarkOf(step);
                var tileView = view.Tile(index);
                Require(tileView.gameObject.activeSelf && Mathf.Abs(tileView.Size.x - tile) < unit * 0.05f, what + "'s tile is not " + RoomStepsView.TileDp.ToString("0") + " dp");
                Require(Mathf.Abs(-tileView.transform.localPosition.y - (top + line / 2f)) < unit * 0.5f && Mathf.Abs(tileView.transform.localPosition.x - tile / 2f) < 1e-6f,
                    what + "'s tile does not stand before its word, at its first line's middle");
                Require(view.Mark(index).Glyph == expected.Glyph, what + " carries " + (view.Mark(index).Glyph?.ToString() ?? "no glyph") + ", not " + (expected.Glyph?.ToString() ?? "none"));
                below = top + Mathf.Max(line, word.textInfo.lineCount * KitType.LinePitch(word));
                var under = view.Line(index);
                Require((under != null) == (step.Line != null), what + (step.Line != null ? " shows no line under it" : " shows a line it does not have"));
                if (under == null || step.Line == null) continue;
                under.ForceMeshUpdate(true);
                Require(Mathf.Abs(-under.transform.localPosition.y - below) < unit * 0.05f, what + "'s line does not stand straight under its word");
                // Every line whole, the person's long request included: nothing on the list is cut.
                var shownLine = string.Join(" ", under.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
                Require(!under.isTextOverflowing && !under.isTextTruncated && shownLine == step.Line.Text && !shownLine.Contains("…"), what + "'s line is not whole: \"" + shownLine + "\"");
                below += Mathf.Max(line, under.textInfo.lineCount * KitType.LinePitch(under));
            }
            if (usage != null)
            {
                // The figure's label quiet and its value apart on one line, under the last step by the rows' gap, aligned with the
                // steps' words; its note a quiet line under it; nothing joined on a dot; the list as tall as its last line.
                var label = view.UsageLabel;
                var value = view.UsageValue;
                Require(label != null && value != null, "the usage figure is not shown");
                if (label != null && value != null)
                {
                    label.ForceMeshUpdate(true);
                    value.ForceMeshUpdate(true);
                    Require(label.GetParsedText().Trim() == usage.Label && value.GetParsedText().Trim() == usage.Value, "the usage figure reads \"" + label.GetParsedText() + "\" and \"" + value.GetParsedText() + "\"");
                    Require(-label.transform.localPosition.y >= below + gap - unit * 0.05f && -label.transform.localPosition.y <= below + gap + line / 2f
                        && Mathf.Abs(label.transform.localPosition.x - indent) < 1e-6f,
                        "the usage figure does not stand the rows' gap under the last step, at the steps' words");
                    // As the usage tile (AUDIT 2026-11-16 12:19Z): the label a step smaller and quiet, the value white at the row's size;
                    // beside the label on its line where it fits whole, else under it at the steps' words; whole, never a word alone
                    // on its last line.
                    Require(label.fontSize < value.fontSize - 0.01f && label.color.a < value.color.a - 0.05f && value.color.r > 0.99f,
                        "the usage figure's label is not smaller and quieter than its value");
                    var besideIt = value.transform.localPosition.x >= label.transform.localPosition.x + RoomCursor.WordsWidth(label, 0f) + RoomType.Scaled(RoomStepsView.UsageGapDp) - unit * 0.05f
                        && Mathf.Abs(value.transform.localPosition.y - label.transform.localPosition.y) < line;
                    var underIt = Mathf.Abs(value.transform.localPosition.x - indent) < 1e-6f
                        && -value.transform.localPosition.y >= -label.transform.localPosition.y + RoomType.Scaled(RoomCardView.GaugeLabelLineDp) - unit * 0.05f;
                    Require(besideIt || underIt, "the usage figure's value stands neither apart beside its label nor under it");
                    Require(besideIt ? value.textInfo.lineCount == 1 : true, "the usage figure's value beside its label wraps: \"" + value.GetParsedText() + "\"");
                    Require(!value.isTextTruncated && !value.GetParsedText().Contains("…"), "the usage figure's value is cut");
                    if (value.textInfo.lineCount >= 2)
                    {
                        var lastLine = value.textInfo.lineInfo[value.textInfo.lineCount - 1];
                        var spaced = false;
                        for (var at = lastLine.firstCharacterIndex; at <= lastLine.lastVisibleCharacterIndex && at < value.textInfo.characterCount; at++)
                            spaced |= char.IsWhiteSpace(value.textInfo.characterInfo[at].character);
                        Require(spaced, "the usage figure's value ends on a word alone: \"" + value.GetParsedText() + "\"");
                    }
                    Require(!label.GetParsedText().Contains(KitMomentWords.Dot.Trim()) && !value.GetParsedText().Contains(KitMomentWords.Dot.Trim()), "the usage figure joins on a dot");
                    var notes = view.UsageNotes.ToList();
                    Require(notes.Count == 1 && notes[0].GetParsedText().Trim() == usage.Notes[0], "the usage figure's note is not under it");
                    var last = notes.Count > 0 ? notes[notes.Count - 1] : value;
                    last.ForceMeshUpdate(true);
                    var ends = -last.transform.localPosition.y + Mathf.Max(line, last.textInfo.lineCount * KitType.LinePitch(last));
                    Require(Mathf.Abs(ends - tall) < unit * 0.05f, "the list says it stands " + (tall / unit).ToString("0.0") + " dp, its usage ends at " + (ends / unit).ToString("0.0"));
                }
            }
            else
            {
                Require(view.UsageLabel == null, "a usage figure stands where none was handed");
                Require(Mathf.Abs(below - tall) < unit * 0.05f, "the list says it stands " + (tall / unit).ToString("0.0") + " dp, its last step ends at " + (below / unit).ToString("0.0"));
            }

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                int Off(Color32 pixel, Color32 want) => Mathf.Max(Mathf.Abs(pixel.r - want.r), Mathf.Max(Mathf.Abs(pixel.g - want.g), Mathf.Abs(pixel.b - want.b)));
                var lightest = new List<int>();
                for (var index = 0; index < steps.Count; index++)
                {
                    var expected = RoomStepsView.MarkOf(steps[index]);
                    var tileView = view.Tile(index).transform;
                    // The tile's own tone, read inside its corner where no glyph stands; the empty tile's edge lighter than the panel.
                    // A shape's transform is scaled to its size, so its points are in its own unit square.
                    if (expected.Fill.a > 0.99f)
                    {
                        var corner = At(dark, tileView.TransformPoint(new Vector3(-0.32f, 0.32f, 0f)));
                        if (Off(corner, expected.Fill) > 16) failures.Add("step " + index + "'s tile is " + Off(corner, expected.Fill) + " of 255 from its tone");
                    }
                    else
                    {
                        var edge = Light(At(dark, tileView.TransformPoint(new Vector3(0.5f - RoomType.Scaled(1f) / tile, 0f, 0f))));
                        var inside = Light(At(dark, tileView.TransformPoint(Vector3.zero)));
                        if (!(edge > inside + 40)) failures.Add("step " + index + "'s empty tile shows no edge: " + edge + " on its inside's " + inside + " of 255");
                    }
                    // The glyph's ink, on the middle of each of its shapes.
                    var mark = view.Mark(index);
                    if (expected.Glyph is RoomGlyph glyph)
                        foreach (var shape in RoomGlyphs.Of(glyph))
                        foreach (var middle in shape.Middles())
                        {
                            var at = mark.transform.TransformPoint(new Vector3(middle.x / RoomGlyphs.Grid - 0.5f, 0.5f - middle.y / RoomGlyphs.Grid, 0f));
                            var off = Off(At(dark, at), expected.Ink);
                            if (off > 20) failures.Add("step " + index + "'s glyph is " + off + " of 255 from its ink");
                        }
                    // The step's word is in the pixels: white full, a step not yet quieter but still well clear of the panel.
                    var word = view.Word(index);
                    var bounds = word.textBounds;
                    var most = 0;
                    for (var step = 0; step <= 60; step++)
                        most = Mathf.Max(most, Light(At(dark, word.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 60f), bounds.max.y - KitType.LinePitch(word) * 0.5f, 0f)))));
                    lightest.Add(most);
                    if (most < (steps[index].Kind == TaskStepKind.NotYet ? 120 : 160)) failures.Add("step " + index + "'s word is not in the pixels, the lightest reads " + most + " of 255");
                    // What waits for the person is said amber, in its line.
                    if (steps[index].Kind == TaskStepKind.NeedsYou && view.Line(index) is TMPro.TextMeshPro waits)
                    {
                        var lineBounds = waits.textBounds;
                        Color32 brightest = default;
                        var lightestLine = 0;
                        for (var step = 0; step <= 60; step++)
                        {
                            var pixel = At(dark, waits.transform.TransformPoint(new Vector3(Mathf.Lerp(lineBounds.min.x, lineBounds.max.x, step / 60f), lineBounds.max.y - KitType.LinePitch(waits) * 0.5f, 0f)));
                            if (Light(pixel) > lightestLine) { lightestLine = Light(pixel); brightest = pixel; }
                        }
                        if (!(brightest.r > brightest.b + 60 && brightest.g > brightest.b + 30)) failures.Add("step " + index + " waits for the person but its line is not amber: " + brightest);
                    }
                }
                Debug.Log("Halcyonic: parts: " + name + ": " + steps.Count + " steps, " + (view.Size.y / unit).ToString("0", CultureInfo.InvariantCulture) + " dp tall, the words' lightest "
                    + string.Join(", ", lightest.Select(value => value.ToString(CultureInfo.InvariantCulture))) + " of 255, marks "
                    + string.Join(", ", steps.Select(each => RoomStepsView.MarkOf(each).Glyph?.ToString() ?? "empty")) + ".");
            };
            return shot;
        }
    }
}
