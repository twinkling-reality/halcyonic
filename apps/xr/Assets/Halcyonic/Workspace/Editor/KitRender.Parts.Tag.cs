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
    /// The name tag (<see cref="RoomTag"/>, DESIGN 1.2): the plate that names the agent and its level while it works, and a
    /// picked tag with its ring. Measured (as wide as its words and 14 dp each side, a line and 6 dp above and below, the
    /// tag kind's 14 dp corner) and read in the pixels (the words; the ring's colour at the picked tag's edge and none at
    /// the plate's).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] TagStates = { "46-tag-plate", "46-tag-picked" };

        private static PartShot TagShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var picked = state == "46-tag-picked";
            var said = picked ? LineWords.TheirName("Kite Run") : LineWords.TheirName("Glimmer 30B") + LineWords.Ours(KitMomentWords.Dot + AgentColumn.NeverAskWords);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var tag = RoomTag.Create(plane);
            tag.Show(said, picked);
            Place(plane, eyes, 0f, shot.Elevation);

            var unit = RoomType.Units(1f);
            var wide = RoomCursor.WordsWidth(tag.Words, 0f);
            Require(tag.Words.textInfo.lineCount == 1 && !tag.Words.isTextOverflowing, "the tag's words are not one whole line");
            Require(Mathf.Abs(tag.Size.x - (wide + 2f * RoomType.Scaled(RoomTag.PadXDp))) < unit * 0.05f, "the tag is " + (tag.Size.x / unit).ToString("0.0") + " dp wide for words " + (wide / unit).ToString("0.0") + " dp wide");
            Require(Mathf.Abs(tag.Size.y - RoomType.Scaled(RoomGeometry.LineDp + 2f * RoomTag.PadYDp)) < unit * 0.05f, "the tag is " + (tag.Size.y / unit).ToString("0.0") + " dp tall");
            Require(Mathf.Approximately(Halcyonic.XR.UI.RoomSurface.CornerDp(RoomSurfaceKind.Tag), 14f) && tag.Plate.Size == tag.Size, "the plate is not the tag's own, corner 14 dp");
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
                if (inside > 110 || inside < 25) failures.Add("the tag's ground reads " + inside + " of 255 inside its edge");
                var bounds = tag.Words.textBounds;
                var most = 0;
                for (var step = 0; step <= 80; step++)
                    most = Mathf.Max(most, Light(At(dark, tag.Words.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 80f), bounds.center.y, 0f)))));
                if (most < 170) failures.Add("the tag's words are not in the pixels, the lightest reads " + most + " of 255");
                Debug.Log("Halcyonic: parts: " + name + ": \"" + said.Text + "\" on a tag " + (tag.Size.x / unit).ToString("0.0", CultureInfo.InvariantCulture) + " by "
                    + (tag.Size.y / unit).ToString("0.0", CultureInfo.InvariantCulture) + " dp; its edge reads " + edge + ", its ground " + inside + " of 255.");
            };
            return shot;
        }
    }
}
