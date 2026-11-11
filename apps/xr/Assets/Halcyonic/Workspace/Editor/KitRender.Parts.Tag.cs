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
    /// The name tag (<see cref="RoomTag"/>, DESIGN 1.2): the plate that names the agent with its level as a quiet chip
    /// while it works, a picked tag with its ring, and a tag whose task needs the person with its tinted chip (the
    /// addendum's A17: never a suffix after a dot). Measured (as wide as its words, its chip and 14 dp each side, a line
    /// and 6 dp above and below, the tag kind's 14 dp corner) and read in the pixels (the words; the ring's colour at the
    /// picked tag's edge and none at the plate's; the chip's tint).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] TagStates = { "46-tag-plate", "46-tag-picked", "46-tag-needs-you" };

        private static PartShot TagShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var picked = state == "46-tag-picked";
            var needsYou = state == "46-tag-needs-you";
            var said = picked || needsYou ? LineWords.TheirName("Kite Run") : LineWords.TheirName("Glimmer 30B");
            var level = picked || needsYou ? null : AgentColumn.NeverAskWords;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var tag = RoomTag.Create(plane);
            var plated = !picked && !needsYou;
            tag.Show(said, picked, needsYou, level, plate: plated);
            Place(plane, eyes, 0f, shot.Elevation);

            var unit = RoomType.Units(1f);
            var wide = RoomCursor.WordsWidth(tag.Words, 0f);
            Require(tag.Words.textInfo.lineCount == 1 && !tag.Words.isTextOverflowing, "the tag's words are not one whole line");
            var chipped = needsYou || level != null;
            Require(!tag.Words.GetParsedText().Contains(KitMomentWords.Dot.Trim()), "the tag's words join facts on a dot: \"" + tag.Words.GetParsedText() + "\"");
            Require(tag.NeedsYou == needsYou && tag.Chipped == chipped, "the tag's chip " + (tag.Chipped ? "shows" : "does not show"));
            if (chipped)
                Require(tag.ChipWords.GetParsedText() == (needsYou ? StateLanguage.WordOf(WorkState.WaitingForYou) : level), "the chip reads \"" + tag.ChipWords.GetParsedText() + "\"");
            var glyphRoom = plated ? RoomType.Scaled(RoomTag.AgentGlyphDp + RoomTag.AgentGlyphGapDp) : 0f;
            Require(tag.IsPlate == plated && (tag.AgentGlyph.Glyph == RoomGlyph.Agent) == plated, "the agent's plate does not carry the agent's glyph, or a task's tag does");
            Require(tag.Plate.gameObject.activeSelf == !plated && tag.Ground.gameObject.activeSelf == plated, "the agent's plate wears a tag's glass, or a tag wears none");
            Require(Mathf.Abs(tag.Size.x - (glyphRoom + wide + 2f * RoomType.Scaled(RoomTag.PadXDp))) < unit * 0.05f, "the tag is " + (tag.Size.x / unit).ToString("0.0") + " dp wide for words " + (wide / unit).ToString("0.0") + " dp wide");
            if (chipped)
            {
                var chipLeft = tag.Chip.transform.localPosition.x - tag.Chip.Size.x / 2f;
                Require(Mathf.Abs(chipLeft - (tag.Size.x / 2f + RoomType.Scaled(RoomTag.ChipGapDp))) < unit * 0.05f, "the chip does not float 10 dp beside the plate");
                Require(Mathf.Abs(tag.Chip.transform.localPosition.y) < unit * 0.05f, "the chip is not on the plate's middle line");
            }
            Require(Mathf.Abs(tag.Size.y - RoomType.Scaled(RoomGeometry.LineDp + 2f * RoomTag.PadYDp)) < unit * 0.05f, "the tag is " + (tag.Size.y / unit).ToString("0.0") + " dp tall");
            Require(Mathf.Approximately(Halcyonic.XR.UI.RoomSurface.CornerDp(RoomSurfaceKind.Tag), 14f) && (plated ? tag.Ground.Size == tag.Size : tag.Plate.Size == tag.Size), "the plate is not the tag's own size, corner 14 dp");
            Require(tag.Picked == picked, "the ring " + (tag.Picked ? "shows" : "does not show"));

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                // The ring: 1.5 dp inside the tag's left edge at its middle height, #E4E8EA where picked, the plate's own edge where not.
                var edge = Light(At(dark, tag.transform.TransformPoint(new Vector3(-tag.Size.x / 2f + unit * RoomTag.RingDp / 2f, 0f, 0f))));
                var inside = Light(At(dark, tag.transform.TransformPoint(new Vector3(-tag.Size.x / 2f + unit * 6f, 0f, 0f))));
                if (picked && edge < 190) failures.Add("the picked tag's ring reads " + edge + " of 255 at its edge");
                if (!picked && edge > 130) failures.Add("the plate has a ring: its edge reads " + edge + " of 255");
                if (!plated && (inside > 110 || inside < 25)) failures.Add("the tag's ground reads " + inside + " of 255 inside its edge");
                var bounds = tag.Words.textBounds;
                var most = 0;
                for (var step = 0; step <= 80; step++)
                    most = Mathf.Max(most, Light(At(dark, tag.Words.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 80f), bounds.center.y, 0f)))));
                if (most < 170) failures.Add("the tag's words are not in the pixels, the lightest reads " + most + " of 255");
                if (chipped)
                {
                    // The chip's ground, in its room beside its word: the attention colour's warm ground where the task needs the person, a quiet grey for a level.
                    var pixel = At(dark, tag.Chip.transform.TransformPoint(new Vector3(-0.5f + RoomTag.ChipPadXDp / 2f / (tag.Chip.Size.x / unit), 0f, 0f)));
                    var warm = pixel.r - pixel.b;
                    if (needsYou && warm < 18) failures.Add("the needs-you chip is not in the attention colour: its ground reads " + pixel);
                    if (!needsYou && Mathf.Abs(warm) > 14) failures.Add("the level's chip is not a quiet grey: its ground reads " + pixel);
                    var ground = Light(pixel);
                    if (ground < 12) failures.Add("the chip's own backing is not in the pixels: " + ground + " of 255");
                }
                Debug.Log("Halcyonic: parts: " + name + ": \"" + said.Text + "\" on a tag " + (tag.Size.x / unit).ToString("0.0", CultureInfo.InvariantCulture) + " by "
                    + (tag.Size.y / unit).ToString("0.0", CultureInfo.InvariantCulture) + " dp; its edge reads " + edge + ", its ground " + inside + " of 255.");
            };
            return shot;
        }
    }
}
