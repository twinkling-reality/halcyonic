#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
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
        private const string StepsLongFile = "src/scenes/kite-meadow/components/tail-streamer/StreamingTailRendererWithWindGusts.ts";

        private static List<TaskStep> StepsOf(string state)
        {
            TaskStep Step(TaskStepKind kind, GlazeIcon mark, string word, LineWords? line = null) => new TaskStep(kind, mark, word, line);
            // The request leaves the steps (the lead's 2026-11-17T00:06Z): its step says its word alone, as TaskSteps gives it.
            var request = Step(TaskStepKind.Done, GlazeIcon.FinishedThisRound, TaskSteps.YourRequest);
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
                        // The long state's act runs to a second line in its sub-tab, whole: a deep file of the project's own.
                        request, Step(TaskStepKind.Now, GlazeIcon.Working, TaskSteps.Working,
                            LineWords.Ours("Editing ") + LineWords.Theirs(state == "46-steps-long" ? StepsLongFile : "index.html")),
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

            var gap = RoomType.Scaled(RoomStepsView.StepGapDp);
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var railX = RoomType.Scaled(RoomStepsView.RailXDp);
            var indent = RoomType.Scaled(RoomStepsView.IndentDp);
            Require(view.Count == steps.Count && Mathf.Approximately(view.Size.y, tall), "the list reports " + view.Count + " steps, " + view.Size);
            // It only says: nothing in it takes a press or a ray.
            Require(view.GetComponentsInChildren<Collider>(true).Length == 0 && view.GetComponentsInChildren<KitPress>(true).Length == 0, "the list holds a part that takes a press");
            // The one language's steps (FIELD's sheet, tile f3): a thin rail down the left joining every mark, never a box per step.
            Require(steps.Count < 2 || view.Rail != null && Mathf.Abs(view.Rail.transform.localPosition.x - railX) < 1e-6f, "the steps stand on no rail down the left");
            Require(view.GetComponentsInChildren<RoomShape>(false).All(shape => shape.Size.x <= RoomType.Scaled(RoomStepsView.DotDp) + unit), "a step stands in a box of its own");
            static float Lin(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
            static float Over(float top, float alpha, float under) => top * alpha + under * (1f - alpha);
            var glass = Halcyonic.XR.UI.RoomSurface.Hex(AskGlass.FaceColour);
            var ground = Over(glass.r, AskGlass.FaceAlpha, 1f);
            var below = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var what = "step " + index + " (" + step + ")";
                var word = view.Word(index);
                word.ForceMeshUpdate(true);
                Require(word.gameObject.activeSelf && string.Equals(word.GetParsedText().Trim(), step.Word, StringComparison.OrdinalIgnoreCase)
                    && (word.fontStyle & FontStyles.UpperCase) != 0 && word.characterSpacing > 0f && !word.isTextOverflowing,
                    what + "'s word is not shown whole in spaced capitals: \"" + word.GetParsedText() + "\"");
                var top = -word.transform.localPosition.y;
                var wanted = index == 0 ? 0f : below + gap;
                Require(Mathf.Abs(top - wanted) < unit * 0.05f && Mathf.Abs(word.transform.localPosition.x - indent) < 1e-6f,
                    what + " does not stand " + RoomStepsView.StepGapDp.ToString("0") + " dp under the one before, at the steps' words");
                var colour = RoomStepsView.WordColourOf(step, RoomStepsView.IsNext(steps, index));
                Require(Mathf.Approximately(word.color.a, colour.a) && Mathf.Approximately(word.color.r, colour.r), what + "'s word is not in its colour");
                // 4.5:1 over the brightest wall: the panel's one glass over white, the word over the glass.
                var ink = Over(colour.r, colour.a, ground);
                var contrast = (Mathf.Max(Lin(ink), Lin(ground)) + 0.05f) / (Mathf.Min(Lin(ink), Lin(ground)) + 0.05f);
                Require(contrast >= 4.5f, what + "'s word holds " + contrast.ToString("0.0") + ":1 on the glass over a lit wall, under 4.5");
                var now = step.Kind == TaskStepKind.Now;
                if (now) Require(steps.Where((other, at) => at != index).All(other => view.Word(steps.IndexOf(other)).fontSize < word.fontSize), what + "'s word is not the largest");
                // Its mark on the rail at its first line's middle: a white dot where now, a hollow diamond still to come, else its glyph.
                var shape = view.Tile(index);
                var glyph = view.Mark(index);
                var markY = step.Kind is TaskStepKind.Now or TaskStepKind.NotYet ? -shape.transform.localPosition.y : -glyph.transform.localPosition.y;
                var markX = step.Kind is TaskStepKind.Now or TaskStepKind.NotYet ? shape.transform.localPosition.x : glyph.transform.localPosition.x;
                Require(Mathf.Abs(markX - railX) < 1e-6f && markY > top && markY < top + Mathf.Max(line, KitType.LinePitch(word)), what + "'s mark does not stand on the rail beside its word");
                if (now) Require(shape.gameObject.activeSelf && shape.Fill == Color.white && !glyph.gameObject.activeSelf, what + " is not marked by a white dot");
                else if (step.Kind == TaskStepKind.NotYet) Require(shape.gameObject.activeSelf && shape.Fill.a == 0f && !glyph.gameObject.activeSelf, what + " is not marked by a hollow diamond");
                else Require(!shape.gameObject.activeSelf && glyph.Glyph == RoomStepsView.MarkOf(step).Glyph, what + " carries " + (glyph.Glyph?.ToString() ?? "no glyph") + ", not its own mark");
                // The shape says the state, never a colour: every mark white but what waits for the person, on the attention amber.
                var markInk = RoomStepsView.InkOf(step);
                var amber = Halcyonic.XR.UI.RoomSurface.Hex(RoomCardView.Attention);
                Require(step.Kind == TaskStepKind.NeedsYou
                    ? markInk == amber
                    : Mathf.Approximately(markInk.r, 1f) && Mathf.Approximately(markInk.g, 1f) && Mathf.Approximately(markInk.b, 1f),
                    what + "'s mark is in " + markInk + ", not white" + (step.Kind == TaskStepKind.NeedsYou ? " amber" : ""));
                below = top + Mathf.Max(line, word.textInfo.lineCount * KitType.LinePitch(word));
                var under = view.Line(index);
                Require((under != null) == (step.Line != null), what + (step.Line != null ? " shows no line under it" : " shows a line it does not have"));
                if (under == null || step.Line == null) continue;
                under.ForceMeshUpdate(true);
                Require(Mathf.Abs(-under.transform.localPosition.y - below) < unit * 0.05f, what + "'s line does not stand straight under its word");
                // Every line whole; the act is our verb as built, never put in the agent's mouth; never the request.
                var shownLine = string.Join(" ", under.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
                Require(!under.isTextOverflowing && !under.isTextTruncated && shownLine == step.Line.Text && !shownLine.Contains("…") && !shownLine.Contains(KitMomentWords.TheAgentSays),
                    what + "'s line is not whole as built: \"" + shownLine + "\"");
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
                        "the usage figure does not stand the steps' gap under the last step, at the steps' words");
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
                    var step0 = steps[index];
                    var shapeView = view.Tile(index);
                    var groundLight = Light(At(dark, view.transform.TransformPoint(new Vector3(-RoomType.Units(2f), 0f, 0f))));
                    // The rail's marks in the pixels (FIELD's sheet, tile f3): a white dot where now, a hollow diamond's edge
                    // lighter than its inside still to come, a glyph's ink clear of the glass otherwise. A shape's points are in its unit square.
                    if (step0.Kind == TaskStepKind.Now)
                    {
                        var centre = At(dark, shapeView.transform.TransformPoint(Vector3.zero));
                        if (Off(centre, new Color32(255, 255, 255, 255)) > 30) failures.Add("step " + index + "'s dot is " + Off(centre, new Color32(255, 255, 255, 255)) + " of 255 from white");
                    }
                    else if (step0.Kind == TaskStepKind.NotYet)
                    {
                        var side = Mathf.Max(shapeView.Size.x, 1e-6f);
                        var edge = Light(At(dark, shapeView.transform.TransformPoint(new Vector3(0.5f - RoomType.Scaled(0.6f) / side, 0f, 0f))));
                        var inside = Light(At(dark, shapeView.transform.TransformPoint(Vector3.zero)));
                        if (!(edge > inside + 40)) failures.Add("step " + index + "'s diamond shows no edge: " + edge + " on its inside's " + inside + " of 255");
                    }
                    else if (RoomStepsView.MarkOf(step0).Glyph is RoomGlyph glyph)
                    {
                        var mark = view.Mark(index);
                        var ink = RoomStepsView.InkOf(step0);
                        foreach (var shape in RoomGlyphs.Of(glyph))
                        foreach (var middle in shape.Middles())
                        {
                            var pixel = At(dark, mark.transform.TransformPoint(new Vector3(middle.x / RoomGlyphs.Grid - 0.5f, 0.5f - middle.y / RoomGlyphs.Grid, 0f)));
                            if (ink.a > 0.99f ? Off(pixel, ink) > 30 : Light(pixel) < groundLight + 60)
                                failures.Add("step " + index + "'s glyph does not stand in its ink: " + pixel);
                        }
                    }
                    // The step's word is in the pixels: white full, a step not yet quieter but still well clear of the panel.
                    var word = view.Word(index);
                    var bounds = word.textBounds;
                    var most = 0;
                    for (var step = 0; step <= 60; step++)
                        most = Mathf.Max(most, Light(At(dark, word.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 60f), bounds.max.y - KitType.LinePitch(word) * 0.5f, 0f)))));
                    lightest.Add(most);
                    // Floors from the measured words (steps7: white 255, the next at 0.86 239, Done at 0.56 198, a later step at 0.52
                    // 190): a word meant white, or the next, under 230 has been dimmed (a white word at 0.5 reads about 187); only Done
                    // and the later steps, dimmed on purpose, may read down to 175.
                    var dimmed = steps[index].Kind == TaskStepKind.Done || steps[index].Kind == TaskStepKind.NotYet && !RoomStepsView.IsNext(steps, index);
                    if (most < (dimmed ? 175 : 230)) failures.Add("step " + index + " (" + steps[index].Kind + ")'s word reads " + most + " of 255 at its lightest, under " + (dimmed ? 175 : 230));
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
