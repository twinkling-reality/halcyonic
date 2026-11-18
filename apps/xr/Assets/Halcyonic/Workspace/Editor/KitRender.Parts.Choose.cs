#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Choose for the evidence view's strip (<see cref="EvidenceChooseView"/>) from a synthetic read in the reads' own shape: lines, a
    /// check, time, cost, tool calls, commits and errors measured, lines kept not, and one choice made by a press of Tool Calls on the
    /// strip's default. Measured: the press raised the new choice; every row's words whole and inside its row; at rest no bar, a chosen
    /// row's words white with the kit's check, an open row's quiet with none; one chosen row pointed at as a hover draws it, the white
    /// bar with its words and check dark, in the pixels too; the missing kind reading "Not Measured" and taking no press; every target
    /// 12 mm from the next; no hue on the view's own parts; no part outside the panel; and the head turned up so no furniture of the
    /// bright room stands between the eye and the panel.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] EvidenceChooseStates = { "49-evidence-choose" };

        /// <summary>
        /// The bright room's monitor and desk as <see cref="RoomAlong"/> draws them, in metres from the eyes (x right, y up): the furniture
        /// Choose must stand clear of. The monitor's face is <see cref="Screen"/> ahead, the desk's top <see cref="DeskTop"/> below.
        /// </summary>
        private const float ChooseMonitorLeft = 0.04f, ChooseMonitorRight = 0.62f, ChooseMonitorBottom = -0.43f, ChooseMonitorTop = -0.07f, ChooseDeskHalfWidth = 1.25f;

        /// <summary>How far above the monitor's top, as the eye sees it, Choose's foot stands.</summary>
        private const float ChooseClearDegrees = 2f;

        private static EvidenceStrip ChooseSampleStrip()
        {
            var available = new EvaluationAvailability { State = EvaluationAvailabilityState.Available };
            return EvidenceStrip.Of(new Evaluation
            {
                Cost = new EvaluationCost { Availability = available, EstimatedUsd = 0.84 },
                Activity = new EvaluationActivity { Availability = available, ElapsedSeconds = 1080, ToolCalls = 46 },
                Outcome = new EvaluationOutcome
                {
                    Availability = available,
                    Measure = new EvaluationOutcomeMeasure
                    {
                        Uncommitted = new EvaluationUncommitted { FilesTouched = 4, LinesAdded = 212, LinesRemoved = 38 },
                        CommitsLanded = 2,
                        ErrorCount = 0,
                    },
                },
                Verification = new EvaluationVerification
                {
                    Availability = available,
                    Lens = new EvaluationVerificationLens
                    {
                        ByKind = new List<EvaluationVerificationKind> { new EvaluationVerificationKind { Label = "test", Runs = 12, Passed = 12, PassRate = 1 } },
                    },
                },
            })!;
        }

        private static PartShot EvidenceChooseShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var strip = ChooseSampleStrip();
            Require(!strip.All.Any(stat => stat.Key == "lines-kept"), "the sample measures lines kept: it must lack one kind");

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = EvidenceChooseView.Create(plane);
            IReadOnlyList<string>? raised = null;
            view.Chosen += choice => raised = choice;
            var doneRaised = false;
            view.Done += () => doneRaised = true;
            view.Show(strip, null);
            // The head turned up until the panel's foot stands over the monitor on the desk ahead and to the right, at either text size:
            // nothing of the room's furniture stands between the eye and the rows (AUDIT's 8.5: the monitor crossed the rows' right side).
            shot.Elevation = Mathf.Atan2(ChooseMonitorTop, Screen) * Mathf.Rad2Deg + ChooseClearDegrees + SpatialKit.DegreesTo(view.Size.y / 2f);
            Place(plane, eyes, 0f, shot.Elevation);

            // The one choice, made as a person makes it: a press of the open Tool Calls row.
            var toolCalls = view.Drawn.FirstOrDefault(drawn => drawn.Row.Key == "tool-calls");
            Require(toolCalls.Row != null && toolCalls.Press != null && toolCalls.Row.State == EvidenceChoiceState.Open, "Tool Calls is not an open row with a press");
            toolCalls.Press?.SettleForRender();
            toolCalls.Press?.PressForRender(990);
            var expected = EvidenceChoice.Toggle(null, "tool-calls", strip);
            Require(raised != null && raised.SequenceEqual(expected), "the press raised " + (raised == null ? "nothing" : string.Join(", ", raised)) + ", not " + string.Join(", ", expected));
            Require(view.Choice != null && view.Choice.SequenceEqual(expected), "the view does not draw the choice its press made");
            view.DonePress.Press.SettleForRender();
            view.DonePress.Settle();
            view.DonePress.Press.PressForRender(991);
            Require(doneRaised, "Done's press raised nothing");
            // Drawn at rest, as a person meets it: the press's flash is the render's own doing.
            view.DonePress.Press.EndFlash();
            view.DonePress.Settle();

            var rows = EvidenceChoice.Rows(strip, expected);
            var drawn = view.Drawn;
            Require(drawn.Select(each => each.Row.Key).SequenceEqual(rows.Select(row => row.Key)), "the rows drawn are not one a kind in the strip's order: " + string.Join(", ", drawn.Select(each => each.Row.Key)));
            Require(drawn.Any(each => each.Row.State == EvidenceChoiceState.Chosen) && drawn.Any(each => each.Row.State == EvidenceChoiceState.Open)
                && drawn.Count(each => each.Row.State == EvidenceChoiceState.NotMeasured) == 1, "the sample does not draw chosen, open and one not measured row");

            var unit = RoomType.Units(1f);
            var half = view.Size / 2f;
            const float tolerance = 1e-4f;
            bool Inside(Vector3 min, Vector3 max, Vector2 boxHalf, Vector2 boxCentre) =>
                min.x >= boxCentre.x - boxHalf.x - tolerance && max.x <= boxCentre.x + boxHalf.x + tolerance && min.y >= boxCentre.y - boxHalf.y - tolerance && max.y <= boxCentre.y + boxHalf.y + tolerance;
            (Vector3 Min, Vector3 Max) InView(TMP_Text label)
            {
                label.ForceMeshUpdate(true);
                var b = label.textBounds;
                var a = view.transform.InverseTransformPoint(label.transform.TransformPoint(b.min));
                var c = view.transform.InverseTransformPoint(label.transform.TransformPoint(b.max));
                return (Vector3.Min(a, c), Vector3.Max(a, c));
            }
            bool Grey(Color colour) => Mathf.Abs(colour.r - colour.g) < 0.01f && Mathf.Abs(colour.g - colour.b) < 0.01f;

            Color GlyphColour(RoomGlyphView glyph)
            {
                var block = new MaterialPropertyBlock();
                glyph.Ink.GetPropertyBlock(block);
                var ink = block.GetVector("_Colour");
                return new Color(ink.x, ink.y, ink.z, ink.w);
            }
            bool Dark(Color colour) => colour.r < 0.15f && colour.a > 0.99f;
            bool White(Color colour) => colour.r > 0.99f && colour.a > 0.99f;

            // At rest, no row pointed at: no bar anywhere; a chosen row's words white with its check, an open one's quiet with none.
            foreach (var (row, plate, words, kind, check, press) in drawn)
            {
                var plateCentre = (Vector2)view.transform.InverseTransformPoint(plate.transform.position);
                var plateHalf = plate.Size / 2f;
                foreach (var (label, said) in new (TMP_Text? Label, string? Said)[] { (words, row.Head), (kind, row.Kind) })
                {
                    if (said == null) continue;
                    if (label == null)
                    {
                        Require(false, row.Key + ": \"" + said + "\" is not drawn");
                        continue;
                    }
                    // Laid now, as the eyes would see it, before its text is read (the label laid at Show may not have meshed yet).
                    label.ForceMeshUpdate(true, true);
                    var shown = label.GetParsedText();
                    var whole = label == kind ? said.ToUpperInvariant() : said;
                    var visible = Enumerable.Range(0, label.textInfo.characterCount).Count(at => label.textInfo.characterInfo[at].isVisible);
                    Require(shown == whole && visible == whole.Count(ch => !char.IsWhiteSpace(ch)) && !label.isTextOverflowing, row.Key + ": \"" + shown + "\" is not \"" + whole + "\" whole");
                    var (min, max) = InView(label);
                    Require(Inside(min, max, plateHalf, plateCentre), row.Key + ": \"" + shown + "\" stands past its bar");
                    Require(Grey(label.color), row.Key + ": \"" + shown + "\" carries a hue");
                }
                Require(row.Kind == null || row.State == EvidenceChoiceState.NotMeasured, row.Key + ": a measured row carries a kind label (" + row.Kind + ")");
                if (kind != null)
                    Require(InView(words).Max.x + RoomType.Scaled(EvidenceChooseView.KindGapDp) <= InView(kind).Min.x + tolerance, row.Key + ": its head runs into its kind");
                if (check != null)
                {
                    var checkAt = (Vector2)view.transform.InverseTransformPoint(check.transform.position);
                    var side = RoomType.Units(check.SizeDp);
                    Require(InView(words).Max.x + RoomType.Scaled(EvidenceChooseView.KindGapDp) <= checkAt.x - side / 2f + tolerance, row.Key + ": its head runs into its check");
                    Require(Inside(checkAt - Vector2.one * side / 2f, checkAt + Vector2.one * side / 2f, plateHalf, plateCentre), row.Key + ": its check stands past its bar");
                }
                Require(!plate.gameObject.activeSelf, row.Key + ": a row no pointer is on draws a bar");
                Require(Grey(plate.Fill), row.Key + ": its bar carries a hue");
                switch (row.State)
                {
                    case EvidenceChoiceState.Chosen:
                        Require(White(words.color), row.Key + ": a chosen row's words are not white at rest");
                        Require(check != null && check.Glyph == RoomGlyph.Check && White(GlyphColour(check)), row.Key + ": a chosen row has no white check at rest");
                        Require(press != null, row.Key + ": a chosen row takes no press");
                        break;
                    case EvidenceChoiceState.Open:
                        Require(words.color.r > 0.99f && Mathf.Abs(words.color.a - RoomType.LabelAlpha) < 0.01f, row.Key + ": an open row's words are not quiet white");
                        Require(check == null, row.Key + ": an open row draws a check");
                        Require(press != null, row.Key + ": an open row takes no press");
                        break;
                    case EvidenceChoiceState.NotMeasured:
                        Require(kind != null && kind.GetParsedText() == EvidenceChoice.Words.NotMeasured.ToUpperInvariant(), row.Key + ": the missing kind does not read \"Not Measured\"");
                        Require(words.color.a < 0.99f, row.Key + ": a not measured row's words are not quiet");
                        Require(check == null, row.Key + ": a not measured row draws a check");
                        Require(press == null, row.Key + ": a not measured row takes a press");
                        break;
                }
            }

            // One row pointed at, as a hover draws it: a chosen row becomes the full-width white bar, its words and its check dark on it.
            const string pointedKey = "lines-added";
            view.PointForRender(pointedKey);
            var lit = view.Drawn.FirstOrDefault(each => each.Row.Key == pointedKey);
            Require(lit.Row != null && lit.Row.State == EvidenceChoiceState.Chosen, "the pointed row " + pointedKey + " is not a chosen row");
            if (lit.Row != null)
            {
                Require(lit.Plate.gameObject.activeSelf && White(lit.Plate.Fill), "the pointed row is not a white bar");
                Require(Mathf.Abs(lit.Plate.Size.x - (view.Size.x - 2f * RoomType.Scaled(RoomGeometry.PanelPadDp))) < tolerance, "the pointed row's bar is not the text column wide");
                Require(Dark(lit.Words.color), "the pointed row's words are not dark");
                Require(lit.Check != null && Dark(GlyphColour(lit.Check)), "the pointed chosen row's check is not dark on its bar");
            }
            Require(view.Drawn.Count(each => each.Plate.gameObject.activeSelf) == 1, "a bar stands on a row no pointer is on");

            // Every target, the rows' and Done's, 12 mm from the next (SpatialKit.TargetGapDegrees), and inside the panel.
            var targets = drawn.Where(each => each.Press != null).Select(each => (What: each.Row.Key, Press: each.Press!)).Append((What: "Done", Press: view.DonePress.Press))
                .Select(each =>
                {
                    var centre = view.transform.InverseTransformPoint(each.Press.transform.position);
                    return (each.What, Top: centre.y + each.Press.Size.y / 2f, Bottom: centre.y - each.Press.Size.y / 2f, Left: centre.x - each.Press.Size.x / 2f, Right: centre.x + each.Press.Size.x / 2f);
                })
                .OrderByDescending(each => each.Top).ToList();
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            for (var index = 1; index < targets.Count; index++)
            {
                var between = targets[index - 1].Bottom - targets[index].Top;
                Require(between >= gap - 1e-5f, targets[index - 1].What + " and " + targets[index].What + " stand " + (between / unit).ToString("0.0", CultureInfo.InvariantCulture) + " dp apart, under 12 mm");
            }
            foreach (var each in targets)
                Require(each.Left >= -half.x - tolerance && each.Right <= half.x + tolerance && each.Bottom >= -half.y - tolerance && each.Top <= half.y + tolerance, each.What + "'s target stands outside the panel");

            // No part outside the panel: every shape by its renderer, every word by its own bounds, Done by its core.
            foreach (var shape in view.GetComponentsInChildren<RoomShape>(false).Where(each => each.GetComponentInParent<RoomButton>() == null))
            {
                var b = shape.Renderer.bounds;
                var a = view.transform.InverseTransformPoint(b.min);
                var c = view.transform.InverseTransformPoint(b.max);
                Require(Inside(Vector3.Min(a, c), Vector3.Max(a, c), half, Vector2.zero), shape.name + " stands outside the panel");
            }
            foreach (var label in view.GetComponentsInChildren<TMP_Text>(false).Where(each => each.GetComponentInParent<RoomButton>() == null))
            {
                var (min, max) = InView(label);
                Require(Inside(min, max, half, Vector2.zero), "\"" + label.GetParsedText() + "\" stands outside the panel");
            }
            foreach (var glyph in view.GetComponentsInChildren<RoomGlyphView>(false).Where(each => each.GetComponentInParent<RoomButton>() == null))
            {
                var b = glyph.Ink.bounds;
                var a = view.transform.InverseTransformPoint(b.min);
                var c = view.transform.InverseTransformPoint(b.max);
                Require(Inside(Vector3.Min(a, c), Vector3.Max(a, c), half, Vector2.zero), glyph.name + " stands outside the panel");
            }
            var doneAt = (Vector2)view.transform.InverseTransformPoint(view.DonePress.transform.position);
            Require(Inside(doneAt - view.DonePress.Core / 2f, doneAt + view.DonePress.Core / 2f, half, Vector2.zero), "Done stands outside the panel");
            Require(view.DonePress.MarksInside, "Done's mark stands past its edge");
            Require(view.Title.GetParsedText() == EvidenceChoice.Words.Title.ToUpperInvariant(), "the title does not read \"Choose Stats\"");

            // No furniture between the eye and the panel: the panel's rect, seen from the eyes, misses the monitor nearer than it and never
            // reaches down past the desk's top before it.
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(0f, -1f) }
                .Select(each => view.transform.TransformPoint(new Vector3(each.x * half.x, each.y * half.y, 0f)) - eyes).ToList();
            var onScreen = corners.Take(4).Where(each => each.z > Screen).Select(each => each * (Screen / each.z)).ToList();
            // A panel nearer the eye than the monitor stands in front of it: nothing of the monitor can stand between. Only a panel wholly
            // beyond the monitor's plane is held against its rect.
            if (onScreen.Count == 4)
                Require(onScreen.Max(at => at.x) < ChooseMonitorLeft || onScreen.Min(at => at.x) > ChooseMonitorRight || onScreen.Max(at => at.y) < ChooseMonitorBottom || onScreen.Min(at => at.y) > ChooseMonitorTop,
                    "the bright room's monitor stands between the eye and the panel");
            foreach (var corner in corners.Where(each => each.y < 0f))
            {
                var t = DeskTop / corner.y;
                var meets = corner * t;
                Require(t >= 1f || meets.z < DeskNear || meets.z > DeskFar || Mathf.Abs(meets.x) > ChooseDeskHalfWidth, "the bright room's desk stands between the eye and the panel");
            }

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                foreach (var (row, plate, words, _, check, _) in view.Drawn)
                {
                    if (row.State == EvidenceChoiceState.NotMeasured) continue;
                    if (InkFault(words, world => At(dark, world), 150) is string fault) failures.Add(row.Key + ": " + fault);
                    if (!plate.gameObject.activeSelf) continue;
                    // The pointed bar between its head and its check, clear of both: white, and no hue, over the dark room.
                    var (_, headMax) = InView(words);
                    var plateRight = check != null ? view.transform.InverseTransformPoint(check.transform.position).x - RoomType.Units(check.SizeDp) / 2f
                        : view.transform.InverseTransformPoint(plate.transform.position).x + plate.Size.x / 2f;
                    var sample = view.transform.TransformPoint(new Vector3((headMax.x + plateRight) / 2f, view.transform.InverseTransformPoint(plate.transform.position).y, 0f));
                    var pixel = At(dark, sample);
                    if (pixel.r < 235 || pixel.g < 235 || pixel.b < 235 || Mathf.Max(pixel.r, pixel.g, pixel.b) - Mathf.Min(pixel.r, pixel.g, pixel.b) > 3)
                        failures.Add(row.Key + ": the pointed bar reads " + pixel.r + ", " + pixel.g + ", " + pixel.b + " in the pixels, not white");
                }
                Debug.Log("Halcyonic: parts: " + name + ": Choose laid " + (view.Size.x / unit).ToString("0", CultureInfo.InvariantCulture) + " by "
                    + (view.Size.y / unit).ToString("0", CultureInfo.InvariantCulture) + " dp, the head turned " + shot.Elevation.ToString("0.0", CultureInfo.InvariantCulture)
                    + " degrees up, " + view.Drawn.Count + " rows: "
                    + string.Join(", ", view.Drawn.Select(each => each.Row.Key + " " + each.Row.State)) + ".");
            };
            return shot;
        }
    }
}
