#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The redesign's actions in every look (DESIGN's BUILD-SPEC-v4 1.4 as F6 changed it on 2026-11-10, after the owner
    /// found the actions camouflaged): the one main action with the accent on its chip and in its backing, a neutral one at
    /// rest, pointed (its chip light, 6 percent larger), the main one pointed, held, Confirm behind its lock (the main action's own chip,
    /// its ring white and whole outside it), and an answer's marks, not chosen and chosen. Each is drawn in the room's halo,
    /// never a box, with DESIGN's own glyph named by its words; its words whole in the one size, never cut however long;
    /// its target Meta's hand target at least, 12 mm from the next; its glyph at 4.5 to 1 on its chip; and its arrival and
    /// its answer to pointing on RoomMotion's curves at fixed clock times.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ActionStates = { "32-actions" };

        /// <summary>An answer far longer than a panel is wide, synthetic: about 500 characters, one separator.</summary>
        private static readonly string ActionLongAnswer = "Light · " + string.Join(" ", Enumerable.Repeat("Dark text on a light background, as the game first drew it,", 8)) + " and nothing else.";

        private static Shot ActionsShot(string state, Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "The redesign's actions in every look: main, rest, pointed, main pointed, held, Confirm locking, an answer not chosen and chosen, synthetic data" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var holder = new GameObject("Actions").transform;
            holder.SetParent(root, false);
            var unit = RoomType.Units(1f);
            var looks = new (string Words, GlazeIcon? Icon, bool Main, RoomButtonLook? Look, float? Lock, AnswerMark Mark, bool Chosen, RoomButtonLook Expected, RoomGlyph? Glyph, int Chip, int Ink)[]
            {
                ("Approve", GlazeIcon.Approve, true, null, null, AnswerMark.None, false, RoomButtonLook.Main, RoomGlyph.Check, RoomButton.AccentColour, RoomButton.InkColour),
                ("Deny", GlazeIcon.Deny, false, null, null, AnswerMark.None, false, RoomButtonLook.Rest, RoomGlyph.Barred, RoomButton.RestChipColour, 0xFFFFFF),
                ("Changes", null, false, RoomButtonLook.Pointed, null, AnswerMark.None, false, RoomButtonLook.Pointed, RoomGlyph.Changes, RoomButton.LightColour, RoomButton.InkColour),
                ("Make VR Version", null, true, RoomButtonLook.MainPointed, null, AnswerMark.None, false, RoomButtonLook.MainPointed, RoomGlyph.Cube, RoomButton.AccentColour, RoomButton.InkColour),
                ("Checks", null, false, RoomButtonLook.Pressed, null, AnswerMark.None, false, RoomButtonLook.Pressed, RoomGlyph.Checks, -1, RoomButton.InkColour),
                ("Confirm", GlazeIcon.Approve, false, null, 0.5f, AnswerMark.None, false, RoomButtonLook.Rest, RoomGlyph.Check, RoomButton.AccentColour, RoomButton.InkColour),
                ("Cancel", null, false, null, null, AnswerMark.None, false, RoomButtonLook.Rest, RoomGlyph.Cross, RoomButton.RestChipColour, 0xFFFFFF),
                ("Light", null, false, null, null, AnswerMark.Single, false, RoomButtonLook.Rest, null, -1, 0),
                ("Dark", null, false, null, null, AnswerMark.Single, true, RoomButtonLook.Rest, RoomGlyph.Check, RoomButton.AccentColour, RoomButton.InkColour),
                ("Sound", null, false, null, null, AnswerMark.Multiple, true, RoomButtonLook.Rest, RoomGlyph.Check, RoomButton.AccentColour, RoomButton.InkColour),
            };
            var pitch = RoomType.Units(RoomButton.HeightDp) + KitType.Units(SpatialKit.TargetGapDegrees);
            var buttons = new List<RoomButton>();
            for (var index = 0; index < looks.Length; index++)
            {
                var (words, icon, main, look, locking, mark, chosen, expected, glyph, _, _) = looks[index];
                var button = RoomButton.Create(holder, words);
                button.LookForRender = look;
                var width = button.Measure(words);
                // Left-aligned in the column, one under another, the main one first.
                button.Show(words, icon, main, true, new Vector2(width / 2f, -index * pitch), 0f, mark, chosen);
                if (locking != null) button.Lock(locking);
                button.Press.SettleForRender();
                button.Settle();
                buttons.Add(button);
                Require(button.LookNow() == expected, words + " shows " + button.LookNow() + ", not " + expected);
                Require(button.Size.y >= RoomType.Units(RoomButton.HeightDp) - 1e-5f && button.Size.x >= RoomType.Units(RoomButton.HeightDp) - 1e-5f,
                    words + "'s target is under Meta's hand target");
                button.Words.ForceMeshUpdate(true);
                Require(!button.Words.isTextTruncated && button.Words.textInfo.lineCount == 1 && button.Words.overflowMode == TMPro.TextOverflowModes.Overflow, words + "'s words are cut, or can be");
                Require(button.Words.fontSharedMaterial != null && button.Words.fontSharedMaterial.name.EndsWith("Underlay Action"), words + "'s words do not stand on the action underlay");
                var renderer = button.GetComponentInChildren<MeshRenderer>();
                Require(renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "Halcyonic/Room Halo", words + " is not drawn in the room's halo");
                // The glyph is DESIGN's own, named by the action's words or, for Confirm, by what it confirms; drawn from geometry, never the icon font.
                Require(button.Glyph.Glyph == glyph, words + " carries " + (button.Glyph.Glyph?.ToString() ?? "no glyph") + ", not " + (glyph?.ToString() ?? "none"));
                Require(button.GetComponentsInChildren<TMPro.TMP_Text>(false).All(label => !GlazeIcons.IsIcon(label)), words + " still draws an icon from the font");
            }
            // The accent on the main actions' chips and on Confirm's behind its lock, which is the moment's main action waiting (the lead's 0529Z word), and on no other.
            var leading = looks.Count(row => row.Main || row.Lock != null);
            Require(leading == 3 && buttons.Count(button => button.ChipColour == (Vector4)Halcyonic.XR.UI.RoomSurface.Hex(RoomButton.AccentColour) && button.Mark == AnswerMark.None) == leading,
                "the accent is not on the main actions' chips alone");
            for (var index = 1; index < buttons.Count; index++)
            {
                var gap = (buttons[index - 1].transform.localPosition.y - buttons[index - 1].Size.y / 2f) - (buttons[index].transform.localPosition.y + buttons[index].Size.y / 2f);
                Require(gap >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, buttons[index - 1].name + " and " + buttons[index].name + "'s targets stand under 12 mm apart");
            }

            // Never cut: an answer of about 500 characters wraps at the panel's width, breaks at its first separator with the
            // dot dropped (WORDS rule 2), stands as tall as its lines, and reads back whole from its label.
            var longer = RoomButton.Create(holder, "Long answer");
            var wrap = RoomType.Units(RoomGeometry.PanelDp);
            longer.Show(ActionLongAnswer, null, false, true, Vector2.zero, 0f, AnswerMark.Single, false, wrap);
            longer.Words.ForceMeshUpdate(true);
            var read = longer.Words.GetParsedText();
            Require(read == ActionLongAnswer.Replace(" · ", "\n"), "a long answer reads back as " + read.Length + " characters of its " + ActionLongAnswer.Length + ", or its separator did not break the line");
            Require(!longer.Words.isTextTruncated && longer.Words.textInfo.lineCount > 6, "a long answer is cut at " + longer.Words.textInfo.lineCount + " lines");
            Require(Mathf.Abs(longer.Core.y - (RoomType.Units(longer.CoreHeightDp) + (longer.Words.textInfo.lineCount - 1) * RoomType.Scaled(RoomGeometry.LineDp))) < 1e-4f, "a long answer's box is not as tall as its lines");
            longer.Hide();

            // Motion at fixed clock times (RoomMotion): three actions shown together arrive 60 ms apart; pointing eases over 150 ms; held is at once.
            var stillWas = GlazeMotion.Still;
            GlazeMotion.Still = false;
            try
            {
                var clock = 50f;
                var moving = new[] { "First", "Second", "Third" }.Select(name => RoomButton.Create(holder, name)).ToList();
                foreach (var each in moving)
                {
                    each.Clock = () => clock;
                    each.Show("Changes", null, false, true, Vector2.zero, 0f);
                }
                Require(moving.All(each => each.ArrivalShown < 1e-4f), "actions do not arrive from nothing");
                clock += 0.12f;
                foreach (var each in moving) each.Draw();
                for (var index = 0; index < moving.Count; index++)
                    Require(Mathf.Abs(moving[index].ArrivalShown - RoomMotion.LineArrives(0.12f, index).Shown) < 1e-3f, "120 ms in, action " + index + " is " + moving[index].ArrivalShown + " into view");
                clock += 1f;
                foreach (var each in moving) each.Draw();
                Require(moving.All(each => each.ArrivalShown == 1f), "a second on, the actions have not arrived");
                var first = moving[0];
                first.LookForRender = RoomButtonLook.Pointed;
                first.Draw();
                clock += 0.075f;
                first.Draw();
                Require(Mathf.Abs(first.Scale - (1f + (RoomMotion.PointedScale - 1f) * RoomMotion.Pointed(0.075f))) < 1e-3f, "75 ms into pointing, its scale is " + first.Scale);
                var light = (Vector4)Halcyonic.XR.UI.RoomSurface.Hex(RoomButton.LightColour);
                var rest = (Vector4)Halcyonic.XR.UI.RoomSurface.Hex(RoomButton.RestChipColour);
                Require((first.ChipColour - Vector4.Lerp(rest, light, RoomMotion.Pointed(0.075f))).magnitude < 1e-2f, "75 ms into pointing, its chip is not seven eighths of the way to light");
                clock += 0.2f;
                first.Draw();
                Require(Mathf.Abs(first.Scale - RoomMotion.PointedScale) < 1e-4f, "pointed, it is not 6 percent larger");
                first.LookForRender = RoomButtonLook.Pressed;
                first.Draw();
                Require(Mathf.Abs(first.Scale - RoomMotion.PressedScale) < 1e-4f, "held, it does not shrink at once");
                foreach (var each in moving) each.Hide();
            }
            finally { GlazeMotion.Still = stillWas; }

            var height = (looks.Length - 1) * pitch + RoomType.Units(RoomButton.HeightDp);
            var widest = buttons.Max(button => button.Size.x);
            holder.localPosition = new Vector3(-widest / 2f, height / 2f - RoomType.Units(RoomButton.HeightDp) / 2f, 0f);
            var view = new GameObject("Actions view").transform;
            view.SetParent(root, false);
            holder.SetParent(view, false);
            shot.Elevation = 0f;
            shot.Centre = Place(view, eyes, 0f, shot.Elevation);
            Stand(shot, view, eyes, new KitCarrier[0], new Vector2(widest, height));
            foreach (var button in buttons)
            {
                var half = button.Size / 2f;
                foreach (var corner in new[] { new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f), new Vector3(-half.x, half.y, 0f), new Vector3(half.x, half.y, 0f) })
                    shot.Corners.Add(button.transform.TransformPoint(corner));
            }
            shot.Targets.Add(("the actions", view, buttons.Select(button => button.Press)));

            // In the pixels: each chip in its colour, and its glyph at 4.5 to 1 on it (the chips are opaque, so over any room).
            shot.AfterEye = (camera, eye, failures) =>
            {
                Color32 At(Transform glyph, Vector2 local)
                {
                    var screen = camera.WorldToScreenPoint(glyph.TransformPoint(new Vector3(local.x, local.y, 0f)));
                    return eye[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                var weakest = double.MaxValue;
                for (var index = 0; index < looks.Length; index++)
                {
                    var row = looks[index];
                    if (row.Glyph is not RoomGlyph glyph) continue;
                    var at = buttons[index].Glyph.transform;
                    // Beside the glyph's box, inside the chip; and on the middle of the glyph's first shape.
                    var chip = At(at, row.Mark == AnswerMark.None ? new Vector2(-0.62f, 0f) : new Vector2(-0.58f, 0f));
                    var middle = RoomGlyphs.Of(glyph)[0].Middles().First();
                    var ink = At(at, new Vector2(middle.x / RoomGlyphs.Grid - 0.5f, 0.5f - middle.y / RoomGlyphs.Grid));
                    if (row.Chip >= 0)
                    {
                        var want = (Color32)Halcyonic.XR.UI.RoomSurface.Hex(row.Chip);
                        if (chip.a < 250 || Mathf.Abs(chip.r - want.r) > 6 || Mathf.Abs(chip.g - want.g) > 6 || Mathf.Abs(chip.b - want.b) > 6)
                            failures.Add(row.Words + "'s chip is drawn (" + chip.r + ", " + chip.g + ", " + chip.b + ", " + chip.a + "), not #" + row.Chip.ToString("X6"));
                    }
                    var wantInk = (Color32)Halcyonic.XR.UI.RoomSurface.Hex(row.Ink);
                    if (Mathf.Abs(ink.r - wantInk.r) > 6 || Mathf.Abs(ink.g - wantInk.g) > 6 || Mathf.Abs(ink.b - wantInk.b) > 6)
                        failures.Add(row.Words + "'s glyph is drawn (" + ink.r + ", " + ink.g + ", " + ink.b + "), not #" + row.Ink.ToString("X6"));
                    double a = Luminance(chip), b = Luminance(ink);
                    var ratio = (System.Math.Max(a, b) + 0.05) / (System.Math.Min(a, b) + 0.05);
                    weakest = System.Math.Min(weakest, ratio);
                    if (ratio < 4.5) failures.Add(row.Words + "'s glyph reaches " + ratio.ToString("0.00") + " to 1 on its chip");
                }
                Debug.Log("Halcyonic: spatial kit: " + state + ": the weakest glyph on its chip is " + weakest.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " to 1.");
            };
            return shot;
        }
    }
}
