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
    /// DESIGN's glyph set (BUILD-SPEC-v4 1.4's table) drawn from its geometry: every glyph in an action's chip at rest
    /// (white on the neutral chip) and in the main action's accent chip (#172024), 20 dp across, with the two home-size
    /// actions at 24 dp; every glyph alone at 96 dp, for holding against the design's own path data; and the card's kind
    /// marks alone at 96 dp, which stand in tiles and before list rows and never on an action (the addendum's A17). Each glyph is
    /// read in the pixels: its colour on the middle of every one of its shapes and wherever its geometry says ink, and no
    /// ink where its geometry says none.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] GlyphStates = { "43-glyphs", "43-glyphs-rest", "43-glyphs-large", "43-glyphs-marks" };

        /// <summary>Each glyph with an action that carries it, in the product's own words.</summary>
        private static readonly (RoomGlyph Glyph, string Words)[] GlyphActions =
        {
            (RoomGlyph.Send, "Send Instruction"), (RoomGlyph.Microphone, "Hold to Talk"), (RoomGlyph.Keyboard, "Type"), (RoomGlyph.Stop, "Stop"),
            (RoomGlyph.Check, "Approve"), (RoomGlyph.Barred, "Deny"), (RoomGlyph.Cross, "Cancel"), (RoomGlyph.Cube, "Make VR Version"),
            (RoomGlyph.Changes, "Changes"), (RoomGlyph.Checks, "Checks"), (RoomGlyph.Back, "Back"), (RoomGlyph.Next, "Next Part · 2 of 3"),
            (RoomGlyph.Previous, "Previous Part"), (RoomGlyph.Play, "Try Build"), (RoomGlyph.Plus, "New Project"), (RoomGlyph.Pencil, "Request Change"),
            (RoomGlyph.Continue, "Continue"), (RoomGlyph.Lines, "Summarize"), (RoomGlyph.Again, "Run Checks Again"),
            (RoomGlyph.Agent, "Tide 12B · Ask First"), (RoomGlyph.Settings, "Settings"), (RoomGlyph.Bubble, "Write Instruction"), (RoomGlyph.Undo, "Restore"), (RoomGlyph.Clock, "History"),
        };

        /// <summary>How many actions stand in each pair of the sheet's columns; the two home-size actions stand under the second pair.</summary>
        private const int GlyphRows = 12;

        /// <summary>The large sheet: five glyphs a row in the set's order, each 96 dp across in a 120 dp cell.</summary>
        private const float GlyphLargeDp = 96f;
        private const float GlyphCellDp = 120f;

        private static PartShot GlyphShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var unit = RoomType.Units(1f);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var drawn = new List<(RoomGlyphView View, RoomGlyph Glyph, Color Colour, string What)>();
            var all = (RoomGlyph[])Enum.GetValues(typeof(RoomGlyph));

            // The table itself: every glyph has shapes, no more than the shader holds, all inside the grid with their stroke.
            Require(GlyphActions.Select(action => action.Glyph).Distinct().Count() == all.Length - RoomGlyphs.Marks.Length && GlyphActions.All(action => !RoomGlyphs.Marks.Contains(action.Glyph)),
                "the sheet does not show every action's glyph once");
            Require(RoomGlyphs.Marks.Distinct().Count() == RoomGlyphs.Marks.Length, "a mark is listed twice");
            foreach (var glyph in all)
            {
                var shapes = RoomGlyphs.Of(glyph);
                Require(shapes.Count >= 1 && shapes.Count <= RoomGlyphs.MostShapes, glyph + " has " + shapes.Count + " shapes");
                foreach (var shape in shapes)
                foreach (var middle in shape.Middles())
                    Require(middle.x >= RoomGlyphs.Stroke / 2f && middle.y >= RoomGlyphs.Stroke / 2f && middle.x <= RoomGlyphs.Grid - RoomGlyphs.Stroke / 2f && middle.y <= RoomGlyphs.Grid - RoomGlyphs.Stroke / 2f,
                        glyph + " reaches past its 24-unit box");
            }
            foreach (var (glyph, words) in GlyphActions) Require(RoomGlyphs.Named(words) == glyph, "\"" + words + "\" is not named " + glyph);
            Require(RoomGlyphs.Named("Talk Again") == RoomGlyph.Microphone && RoomGlyphs.Named("Next Page") == RoomGlyph.Next && RoomGlyphs.Named("Write Instruction") == RoomGlyph.Bubble
                && RoomGlyphs.Named("Choose Instruction") == RoomGlyph.Lines && RoomGlyphs.Named("Send Answer") == RoomGlyph.Send && RoomGlyphs.Named("Confirm") == null, "an action's glyph is misnamed");
            // The agent's own glyph goes with the action that names an agent and its level, whatever the agent is called; a level alone is an answer.
            Require(RoomGlyphs.Named("Kite Run · Never Ask") == RoomGlyph.Agent && RoomGlyphs.Named("Ember 7B · Next · Ask First") == RoomGlyph.Agent && RoomGlyphs.Named("Ask First") == null
                && RoomGlyphs.Named("Never Ask") == null && RoomGlyphs.Named(" · Ask First") == null && RoomGlyphs.Named("Tide 12B · Ask") == null, "the agent's glyph is misnamed");

            if (state == "43-glyphs-large" || state == "43-glyphs-marks")
            {
                var shown = state == "43-glyphs-marks" ? RoomGlyphs.Marks : all.Where(glyph => !RoomGlyphs.Marks.Contains(glyph)).ToArray();
                for (var index = 0; index < shown.Length; index++)
                {
                    var view = RoomGlyphView.Create(plane, shown[index].ToString());
                    view.Show(shown[index], GlyphLargeDp, RoomGlyphs.OnMain);
                    view.transform.localPosition = new Vector3((index % 5 - 2) * GlyphCellDp * unit, (2f - index / 5) * GlyphCellDp * unit, 0f);
                    drawn.Add((view, shown[index], RoomGlyphs.OnMain, shown[index] + " at 96 dp"));
                }
            }
            else
            {
                // Each glyph in a real action, at rest and as the main one, in two pairs of columns; then the two home-size actions.
                var pitch = RoomType.Units(RoomButton.HeightDp) + KitType.Units(SpatialKit.TargetGapDegrees);
                var column = 300f * unit;
                void Stand(RoomGlyph glyph, string words, bool main, RoomButtonSize size, int col, int row)
                {
                    var button = RoomButton.Create(plane, words + (main ? " main" : " rest"), size);
                    var width = button.Measure(words);
                    // The action draws its own glyph, named by its words; no icon is handed to it.
                    button.Show(words, null, main, true, new Vector2((col - 2) * column + width / 2f, (GlyphRows / 2f - row) * pitch), 0f);
                    button.Press.SettleForRender();
                    button.Settle();
                    var home = size == RoomButtonSize.Home;
                    var view = button.Glyph;
                    Require(Mathf.Approximately(view.SizeDp, (home ? RoomGlyphs.HomeDp : RoomGlyphs.ChipDp) * GlazeText.Scale) && view.Glyph == glyph, words + "'s glyph is not its own at its size");
                    Require(button.LookNow() == (main ? RoomButtonLook.Main : RoomButtonLook.Rest), words + " shows " + button.LookNow());
                    drawn.Add((view, glyph, main ? RoomGlyphs.OnMain : RoomGlyphs.OnRest, words + (main ? " (main chip)" : " (rest chip)") + (home ? " at home size" : "")));
                }
                // The first sheet holds as many as two pairs of columns hold with a row left for the home-size actions; the
                // rest stand on a sheet of their own, so no sheet grows past the rows the view reads at their size.
                var first = 2 * GlyphRows - 1;
                var shown = state == "43-glyphs-rest" ? GlyphActions.Skip(first).ToArray() : GlyphActions.Take(first).ToArray();
                Require(state != "43-glyphs-rest" || shown.Length > 0, "the rest sheet has nothing to show");
                for (var index = 0; index < shown.Length; index++)
                {
                    var (glyph, words) = shown[index];
                    var pair = index < GlyphRows ? 0 : 2;
                    Stand(glyph, words, false, RoomButtonSize.Action, pair, index % GlyphRows);
                    Stand(glyph, words, true, RoomButtonSize.Action, pair + 1, index % GlyphRows);
                }
                if (state != "43-glyphs-rest")
                {
                    Stand(RoomGlyph.Keyboard, "Type", false, RoomButtonSize.Home, 2, GlyphRows - 1);
                    Stand(RoomGlyph.Microphone, "Hold to Talk", true, RoomButtonSize.Home, 3, GlyphRows - 1);
                }
            }
            Place(plane, eyes, 0f, shot.Elevation);

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                var inked = 0;
                var bare = 0;
                foreach (var (view, glyph, colour, what) in drawn)
                {
                    var ink = (Color32)colour;
                    Color32 At(Vector2 place)
                    {
                        var screen = camera.WorldToScreenPoint(view.transform.TransformPoint(new Vector3(place.x / RoomGlyphs.Grid - 0.5f, 0.5f - place.y / RoomGlyphs.Grid, 0f)));
                        var x = Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1);
                        var y = Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1);
                        return eye[y * Width + x];
                    }
                    int Off(Color32 pixel) => pixel.a < 250 ? 255 : Mathf.Max(Mathf.Abs(pixel.r - ink.r), Mathf.Max(Mathf.Abs(pixel.g - ink.g), Mathf.Abs(pixel.b - ink.b)));
                    // The middle of every shape is ink.
                    foreach (var shape in RoomGlyphs.Of(glyph))
                    foreach (var middle in shape.Middles())
                    {
                        inked++;
                        var off = Off(At(middle));
                        if (off > 6) failures.Add(what + ": no ink on a shape's middle at (" + middle.x.ToString("0.0") + ", " + middle.y.ToString("0.0") + "), " + off + " of 255 from its colour");
                    }
                    // Across the whole box: ink where the geometry says ink, none where it says none; a place on an edge is not read.
                    var steps = view.SizeDp > 40f ? 32 : 10;
                    var pixel = RoomGlyphs.Grid / (view.SizeDp * 1.3745f);
                    for (var i = 0; i < steps; i++)
                    for (var j = 0; j < steps; j++)
                    {
                        var place = new Vector2((i + 0.5f) / steps, (j + 0.5f) / steps) * RoomGlyphs.Grid;
                        var within = RoomGlyphs.Inked(glyph, place, -1.2f * pixel);
                        var without = !RoomGlyphs.Inked(glyph, place, 1.2f * pixel);
                        if (!within && !without) continue;
                        var off = Off(At(place));
                        if (within) inked++; else bare++;
                        if (within && off > 6) failures.Add(what + ": no ink at (" + place.x.ToString("0.0") + ", " + place.y.ToString("0.0") + ") where its geometry has ink");
                        if (without && off < 30) failures.Add(what + ": ink at (" + place.x.ToString("0.0") + ", " + place.y.ToString("0.0") + ") where its geometry has none");
                    }
                }
                Debug.Log("Halcyonic: parts: " + name + ": " + drawn.Count + " glyphs drawn, " + inked.ToString(CultureInfo.InvariantCulture) + " places read as ink and " + bare.ToString(CultureInfo.InvariantCulture)
                    + " as bare, each as its geometry says.");
            };
            return shot;
        }
    }
}
