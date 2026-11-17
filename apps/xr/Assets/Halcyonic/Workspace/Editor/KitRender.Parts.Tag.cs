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
    /// The name tag (<see cref="RoomTag"/>, DESIGN 1.2) in the one language (FIELD's sheet, tile f1): the plate that names the
    /// agent with its level under its name while it works, a picked tag with its ring, and a tag whose task needs the person
    /// with Needs You under its name, each in the label role, never a chip (the lead's 2026-11-17T02:02Z; the addendum's A17:
    /// never a suffix after a dot). Measured (as wide as its widest line and 14 dp each side, its lines and 6 dp above and
    /// below, the tag kind's 14 dp corner, no shape beside the name) and read in the pixels (the words and the words under
    /// them; the ring's colour at the picked tag's edge and none at the plate's).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] TagStates = { "46-tag-plate", "46-tag-picked", "46-tag-needs-you", "46-tag-long-name" };

        /// <summary>JUDGE's live walk's long task title (08:57Z): its tag stood 27 degrees wide on one line.</summary>
        private const string TagLongTitle = "A page that says hello and counts my visits";

        /// <summary>
        /// A task's name tag with a long title, as the shelf stands it (RoomNameTag): its words at most the tag's bound wide,
        /// broken at words, the name whole on its lines (no ellipsis, nothing cut), two lines for this title, the plate as tall
        /// as its lines.
        /// </summary>
        /// <summary>The task names the film shows, as FILM's run sheet holds them (2026-11-16 13:17Z).</summary>
        private static readonly string[] FilmedNames =
        {
            "Make a game where I catch falling stars.", "Make a room of lanterns that light up.", "Make a place from this video, at dusk.",
            "Piano of Light", "Dunes at Dusk",
        };

        private static PartShot LongTagShot(Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var tag = RoomTag.Create(plane);
            tag.Show(LineWords.TheirName(TagLongTitle), mostWordsDp: RoomTag.NameWordsMostDp);
            Place(plane, eyes, 0f, shot.Elevation);
            var unit = RoomType.Units(1f);
            tag.Words.ForceMeshUpdate(true);
            var shown = string.Join(" ", tag.Words.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
            Require(shown == TagLongTitle && !tag.Words.isTextTruncated && !shown.Contains(LineWords.Ellipsis), "the long name is not whole: \"" + shown + "\"");
            Require(tag.Lines == 2 && tag.Words.textInfo.lineCount == 2, "the long name takes " + tag.Lines + " lines, not two");
            var bound = RoomType.Scaled(RoomTag.NameWordsMostDp);
            var wide = tag.Size.x - 2f * RoomType.Scaled(RoomTag.PadXDp);
            Require(wide <= bound + unit * 0.05f, "the tag's words stand " + (wide / unit).ToString("0.0") + " dp wide, past the bound's " + (bound / unit).ToString("0"));
            // Each line breaks at a word: no line ends or starts inside one.
            var info = tag.Words.textInfo;
            for (var line = 1; line < info.lineCount; line++)
            {
                var first = info.lineInfo[line].firstCharacterIndex;
                Require(first > 0 && char.IsWhiteSpace(info.characterInfo[first - 1].character), "line " + line + " starts inside a word");
            }
            var pitch = KitType.LinePitch(tag.Words);
            Require(tag.Size.y >= tag.Lines * pitch - unit * 0.05f, "the plate is " + (tag.Size.y / unit).ToString("0.0") + " dp tall for " + tag.Lines + " lines");
            // The names the film shows (FILM 2026-11-16 13:17Z), each whole on two lines at most at the size in use (AUDIT 13:22Z).
            foreach (var filmed in FilmedNames)
            {
                var probe = RoomTag.Create(plane);
                probe.Show(LineWords.TheirName(filmed), mostWordsDp: RoomTag.NameWordsMostDp);
                probe.Words.ForceMeshUpdate(true);
                var probeShown = string.Join(" ", probe.Words.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
                Require(probeShown == filmed && !probe.Words.isTextTruncated && probe.Lines <= 2,
                    "the filmed name \"" + filmed + "\" takes " + probe.Lines + " lines on its tag, past two, or is not whole");
                Debug.Log("Halcyonic: parts: " + name + ": the filmed name \"" + filmed + "\" stands on " + probe.Lines + " line" + (probe.Lines == 1 ? "" : "s") + " on its tag.");
                UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }
            shot.Read = (camera, eye, white, dark, bright, failures) =>
                Debug.Log("Halcyonic: parts: " + name + ": the long name on " + tag.Lines + " lines, " + (tag.Size.x / unit).ToString("0") + " by " + (tag.Size.y / unit).ToString("0") + " dp, "
                    + SpatialKit.Degrees(tag.Size.x / unit).ToString("0.0") + " degrees at the plane's distance.");
            return shot;
        }

        private static PartShot TagShot(string state, Transform root, Vector3 eyes, string name)
        {
            if (state == "46-tag-long-name") return LongTagShot(root, eyes, name);
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
            // As the room shows them: a task's tag with its state under the name (RoomNameTag, the badge's own word), the agent's
            // plate with its level under its name (RoomBarView's plate); never a chip (the lead's 02:02Z, AUDIT's 5).
            var stateWord = plated ? null : needsYou ? StateLanguage.WordOf(WorkState.WaitingForYou) : StateLanguage.WordOf(WorkState.Working);
            tag.Show(said, picked, needsYou, level, plate: plated, state: stateWord);
            Place(plane, eyes, 0f, shot.Elevation);

            var unit = RoomType.Units(1f);
            var wide = RoomCursor.WordsWidth(tag.Words, 0f);
            Require(tag.Words.textInfo.lineCount == 1 && !tag.Words.isTextOverflowing, "the tag's words are not one whole line");
            Require(!tag.Words.GetParsedText().Contains(KitMomentWords.Dot.Trim()), "the tag's words join facts on a dot: \"" + tag.Words.GetParsedText() + "\"");
            // What stands under the name, in the label role: the task's state, or the plate's level; no chip, no filled mark beside it.
            var under = plated ? level : stateWord;
            var stateLabel = tag.StateWords;
            Require(tag.NeedsYou == needsYou && stateLabel != null, "nothing stands under the tag's name");
            if (stateLabel != null)
            {
                stateLabel.ForceMeshUpdate(true, true);
                Require(string.Equals(stateLabel.GetParsedText().Trim(), under, System.StringComparison.OrdinalIgnoreCase) && stateLabel.characterSpacing > 0f && !stateLabel.isTextOverflowing,
                    "under the name reads \"" + stateLabel.GetParsedText() + "\", not \"" + under + "\" in the label role");
                Require(stateLabel.transform.localPosition.y < tag.Words.transform.localPosition.y, "the state does not stand under the name");
                Require(Mathf.Approximately(stateLabel.color.r, stateLabel.color.g) && Mathf.Approximately(stateLabel.color.g, stateLabel.color.b), "the words under the name are tinted, not white");
            }
            Require(tag.GetComponentsInChildren<RoomShape>(false).All(shape => shape == tag.Ground || shape == tag.Stem || shape == tag.Diamond || shape.transform.parent != tag.transform),
                "the tag draws a shape of its own beside the name: a chip");
            var glyphRoom = plated ? RoomType.Scaled(RoomTag.AgentGlyphDp + RoomTag.AgentGlyphGapDp) : 0f;
            Require(tag.IsPlate == plated && (tag.AgentGlyph.Glyph == RoomGlyph.Agent) == plated, "the agent's plate does not carry the agent's glyph, or a task's tag does");
            Require(tag.Plate.gameObject.activeSelf == !plated && tag.Ground.gameObject.activeSelf == plated, "the agent's plate wears a tag's glass, or a tag wears none");
            var stateWide = stateLabel != null ? RoomCursor.WordsWidth(stateLabel, 0f) : 0f;
            Require(Mathf.Abs(tag.Size.x - (glyphRoom + Mathf.Max(wide, stateWide) + 2f * RoomType.Scaled(RoomTag.PadXDp))) < unit * 0.05f, "the tag is " + (tag.Size.x / unit).ToString("0.0") + " dp wide for words " + (wide / unit).ToString("0.0") + " dp wide");
            var stateLine = stateLabel != null ? KitType.LinePitch(stateLabel) + RoomType.Scaled(RoomTag.StateGapDp) : 0f;
            Require(Mathf.Abs(tag.Size.y - (RoomType.Scaled(RoomGeometry.LineDp + 2f * RoomTag.PadYDp) + stateLine)) < unit * 0.05f, "the tag is " + (tag.Size.y / unit).ToString("0.0") + " dp tall");
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
                // The panels' glass over the dark room: the owner's reference black reads about 18 there (the lead's 13:33Z), never
                // clear (the room's own 30) nor a light plate.
                if (!plated && (inside > 110 || inside < 8)) failures.Add("the tag's ground reads " + inside + " of 255 inside its edge");
                var bounds = tag.Words.textBounds;
                var most = 0;
                for (var step = 0; step <= 80; step++)
                    most = Mathf.Max(most, Light(At(dark, tag.Words.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 80f), bounds.center.y, 0f)))));
                if (most < 170) failures.Add("the tag's words are not in the pixels, the lightest reads " + most + " of 255");
                // The words under the name read in the pixels, quiet but clear of the glass: the label role at about 185 to 195 on dark glass
                // (the lead's 2026-11-17T02:29Z).
                if (stateLabel != null && InkFault(stateLabel, world => At(dark, world), 175) is string fault) failures.Add("\"" + stateLabel.GetParsedText() + "\" under the name " + fault);
                Debug.Log("Halcyonic: parts: " + name + ": \"" + said.Text + "\" on a tag " + (tag.Size.x / unit).ToString("0.0", CultureInfo.InvariantCulture) + " by "
                    + (tag.Size.y / unit).ToString("0.0", CultureInfo.InvariantCulture) + " dp; its edge reads " + edge + ", its ground " + inside + " of 255.");
            };
            return shot;
        }
    }
}
