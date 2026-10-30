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
    /// The room's quiet glass (the art direction's handoff, 2026-10-30, section 1; <see cref="KitGlassMaterial"/>): panels at
    /// rest and in each state tone at the bloom's peak, at each rim width, over the art direction's dark room, lit room and
    /// white wall. Each layer is read back from the eye buffer and set against the prototype's own composite: the face and
    /// the bloom must land within 6/255 over every room, the rim over the lit room, the edge over the dark room (where it
    /// must also keep 3:1), the shadow over the lit room and the glow over the dark room; every other room is reported. The
    /// panels' words are synthetic and name the state their light shows.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] GlassStates = { "23-glass" };

        private const float GlassWidthDp = 168f, GlassHeightDp = 104f, GlassRadiusDp = 24f, GlassGapDp = 40f, GlassRowDp = 224f;

        /// <summary>The worst a matched layer may land from the prototype's composite, in 255ths (handoff 7.3).</summary>
        private const double GlassTolerance = 6;

        private static readonly (string Name, GlazeColor Room)[] GlassRooms =
            { ("dark room", KitGlassMaterial.DarkRoom), ("lit room", KitGlassMaterial.LitRoom), ("white wall", KitGlassMaterial.WhiteWall) };

        private static readonly (string Words, float Rim, GlazeColor? Tone)[] GlassPanels =
        {
            ("At Rest", KitGlassMaterial.RimDp, null),
            ("Needs You", KitGlassMaterial.RimDp, SpatialKit.Attention),
            ("Passed", KitGlassMaterial.RimDp, SpatialKit.Passed),
            ("Failed", KitGlassMaterial.RimDp, SpatialKit.Failure),
            ("Plate Rim", KitGlassMaterial.PlateRimDp, null),
            ("Tile Rim", KitGlassMaterial.SmallRimDp, null),
        };

        /// <summary>The room's panels and what each one's glass costs in fill (handoff section 5's sizes, in dp).</summary>
        private static readonly (string Part, float Width, float Height)[] GlassParts =
            { ("screen", 896f, 504f), ("reader", 704f, 528f), ("moment card", 368f, 400f), ("bar", 896f, 86f), ("tile", 224f, 200f), ("tag", 200f, 56f) };

        private static Shot GlassShot(string state, Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "The quiet glass at rest and in each state, over a dark room, a lit room and a white wall, synthetic words" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var view = new GameObject("Glass").transform;
            view.SetParent(root, false);
            var unit = KitGlass.Units(1f);
            var panels = new List<(KitCarrier Carrier, string Room, GlazeColor Ground, float Rim, GlazeColor? Tone)>();
            var rowWidth = GlassPanels.Length * GlassWidthDp + (GlassPanels.Length + 1) * GlassGapDp;
            for (var row = 0; row < GlassRooms.Length; row++)
            {
                var (roomName, ground) = GlassRooms[row];
                var y = (1 - row) * GlassRowDp;
                var plate = Surface.Create(view, "Ground: " + roomName, KitType.Order.Carrier - 4);
                plate.Draw(new Vector2(rowWidth, GlassRowDp) * unit, 0f, KitType.Colour(ground));
                plate.transform.localPosition = new Vector3(0f, y * unit, 0.002f);
                for (var index = 0; index < GlassPanels.Length; index++)
                {
                    var (words, rim, tone) = GlassPanels[index];
                    var x = (index - (GlassPanels.Length - 1) / 2f) * (GlassWidthDp + GlassGapDp);
                    var carrier = KitCarrier.Create(view, words + " over the " + roomName);
                    carrier.DrawGlass(new Vector2(GlassWidthDp, GlassHeightDp) * unit, GlassRadiusDp * unit, rim, tone, tone != null ? 1f : 0f);
                    carrier.transform.localPosition = new Vector3(x * unit, y * unit, 0f);
                    // Strong, not Label: the page's outer panels stand 30 degrees out, where Label would read under the floor.
                    var label = KitType.Create(carrier.transform, "Words", KitRole.Strong, KitType.Text, KitType.Order.Words, room: true);
                    GlazeText.SetLiteral(label, words);
                    var inset = KitGlassMaterial.RimDp + 14f;
                    var (lines, _, _) = KitType.Lay(label, (GlassWidthDp - 2f * inset) * unit, 1);
                    Require(lines == 1 && !label.isTextTruncated, words + " is cut");
                    label.transform.localPosition = new Vector3((-GlassWidthDp / 2f + inset) * unit, (GlassHeightDp / 2f - inset) * unit, -0.0005f);
                    panels.Add((carrier, roomName, ground, rim, tone));
                }
            }
            shot.Elevation = 0f;
            shot.Centre = Place(view, eyes, 0f, shot.Elevation);

            var carriers = panels.Select(each => each.Carrier).ToList();
            Require(carriers.All(carrier => carrier.Glass != null), "a panel did not draw in glass");
            foreach (var failure in GlassTruth(carriers)) shot.Failures.Add(failure);
            Debug.Log("Halcyonic: spatial kit: glass fill: " + string.Join(", ", GlassParts.Select(part =>
                part.Part + " " + KitGlassMaterial.Overdraw(part.Width, part.Height).ToString("0.00") + "x its area")) + "; the glow can be dropped first.");

            shot.AfterEye = (camera, eye, found) =>
            {
                var worst = new Dictionary<(string Layer, string Room), double>();
                void Sample(string layer, string room, Vector3 world, double[] expected, bool matched, string where)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    var (px, py) = (Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y));
                    if (px < 0 || py < 0 || px >= Width || py >= Height) { found.Add(where + " is outside the frame"); return; }
                    var pixel = eye[py * Width + px];
                    var drawn = new double[] { pixel.r / 255.0, pixel.g / 255.0, pixel.b / 255.0 };
                    var off = KitGlassMaterial.Deviation(drawn, expected);
                    worst[(layer, room)] = Math.Max(worst.TryGetValue((layer, room), out var was) ? was : 0, off);
                    if (matched && off > GlassTolerance)
                        found.Add(where + " drew " + KitGlassMaterial.ToColour(drawn) + " for the prototype's " + KitGlassMaterial.ToColour(expected) + ", " + off.ToString("0.0") + "/255 off");
                }
                foreach (var (carrier, room, ground, rim, tone) in panels)
                {
                    var (w, h) = (GlassWidthDp, GlassHeightDp);
                    Vector3 At(float x, float y) => carrier.transform.TransformPoint(new Vector3(x * unit, y * unit, 0f));
                    var name = carrier.name;
                    // The face, opaque: the same over every room, with the bloom where a tone stands.
                    var faceHeight = h - 2f * rim;
                    foreach (var tf in new[] { 0.10, 0.36, 0.90 })
                    {
                        var (face, _) = KitGlassMaterial.At(KitGlassMaterial.Face, tf);
                        var expected = tone is GlazeColor shown
                            ? KitGlassMaterial.Prototype(KitGlassMaterial.Channels(shown), KitGlassMaterial.BloomAt(tf), KitGlassMaterial.ToColour(face))
                            : face;
                        // Toward the right of the face, clear of its words at the top left.
                        Sample(tone != null ? "bloom" : "face", room, At(w / 2f - rim - 8f, (float)(faceHeight / 2f - tf * faceHeight)), expected, true, name + " face at " + tf.ToString("0.00"));
                    }
                    // The rim, 6 dp in from the edge (inside the rim, clear of the edge light), and the edge 1 dp in.
                    var edgeStops = tone is GlazeColor lit ? KitGlassMaterial.StateEdge(lit) : KitGlassMaterial.Edge;
                    foreach (var (x, y, where) in new[] { (0f, h / 2f - rim * 0.6f, "top"), (w / 2f - rim * 0.6f, 0f, "side"), (0f, -h / 2f + rim * 0.6f, "bottom") })
                    {
                        var t = (h / 2.0 - y) / h;
                        var (colour, alpha) = KitGlassMaterial.At(KitGlassMaterial.Rim, t);
                        var rimExpected = KitGlassMaterial.Prototype(colour, alpha, ground);
                        Sample("rim", room, At(x, y), rimExpected, ground.Equals(KitGlassMaterial.LitRoom), name + " rim " + where);
                        // The rim lets the room through and tints it no colour (ART, 2026-10-30): its blue less red within 5/255 of the prototype's.
                        var rimScreenAt = camera.WorldToScreenPoint(At(x, y));
                        var rimDrawn = eye[Mathf.RoundToInt(rimScreenAt.y) * Width + Mathf.RoundToInt(rimScreenAt.x)];
                        var cast = (rimDrawn.b - rimDrawn.r) - (rimExpected[2] - rimExpected[0]) * 255;
                        if (Math.Abs(cast) > 5) found.Add(name + " rim " + where + " casts a tint, its blue less red " + cast.ToString("0.0") + "/255 from the prototype's");
                    }
                    // The foot too: there the edge is at its faintest over the dark room (3.2:1 as built), which the mutation run showed.
                    foreach (var (x, y, where) in new[] { (0f, h / 2f - 1f, "top"), (w / 2f - 1f, 0f, "side"), (0f, -h / 2f + 1f, "foot") })
                    {
                        var t = (h / 2.0 - y) / h;
                        var ring = KitGlassMaterial.Over(KitGlassMaterial.At(edgeStops, t), KitGlassMaterial.At(KitGlassMaterial.Rim, t));
                        Sample(tone != null ? "state edge" : "edge", room, At(x, y), KitGlassMaterial.Prototype(ring.Colour, ring.Alpha, ground), ground.Equals(KitGlassMaterial.DarkRoom), name + " edge " + where);
                        if (!ground.Equals(KitGlassMaterial.DarkRoom)) continue;
                        // The boundary in the dark: the edge as drawn against the room, at 3:1 for non-text.
                        var screen = camera.WorldToScreenPoint(At(x, y));
                        var pixel = eye[Mathf.RoundToInt(screen.y) * Width + Mathf.RoundToInt(screen.x)];
                        var contrast = GlazeColor.Contrast(new GlazeColor(pixel.r, pixel.g, pixel.b), ground);
                        if (contrast < 3) found.Add(name + " edge " + where + " stands only " + contrast.ToString("0.00") + " to 1 against the dark room");
                        // The edge stays a line of its own over the rim beside it (the art direction's rule: 1.4:1 or more in the dark).
                        var rimAt = where == "top" ? At(0f, h / 2f - rim * 0.6f) : where == "foot" ? At(0f, -h / 2f + rim * 0.6f) : At(w / 2f - rim * 0.6f, 0f);
                        var rimScreen = camera.WorldToScreenPoint(rimAt);
                        var rimPixel = eye[Mathf.RoundToInt(rimScreen.y) * Width + Mathf.RoundToInt(rimScreen.x)];
                        var overRim = GlazeColor.Contrast(new GlazeColor(pixel.r, pixel.g, pixel.b), new GlazeColor(rimPixel.r, rimPixel.g, rimPixel.b));
                        if (overRim < 1.4) found.Add(name + " edge " + where + " stands only " + overRim.ToString("0.00") + " to 1 above its rim in the dark room");
                    }
                    // Outside: the shadow under the foot, the glow beside the edge.
                    Sample("shadow", room, At(0f, -h / 2f - 10f), KitGlassMaterial.PrototypeOutside(0, -h / 2.0 - 10, w, h, GlassRadiusDp, ground), ground.Equals(KitGlassMaterial.LitRoom), name + " shadow");
                    Sample("glow", room, At(w / 2f + 5f, h / 4f), KitGlassMaterial.PrototypeOutside(w / 2.0 + 5, h / 4.0, w, h, GlassRadiusDp, ground), ground.Equals(KitGlassMaterial.DarkRoom), name + " glow");
                }
                foreach (var layer in worst.Keys.Select(key => key.Layer).Distinct())
                    Debug.Log("Halcyonic: spatial kit: glass " + layer + " from the prototype, worst: " + string.Join(", ", GlassRooms.Where(room => worst.ContainsKey((layer, room.Name)))
                        .Select(room => room.Name + " " + worst[(layer, room.Name)].ToString("0.0") + "/255")) + ".");
            };
            return shot;
        }

        /// <summary>
        /// What the glass may say (AUDIT, A2): a state's light (its edge, its bloom) stands only on a panel whose words say
        /// that state, and no word stands on a rim, which lets the room through.
        /// </summary>
        private static IEnumerable<string> GlassTruth(IEnumerable<KitCarrier> carriers)
        {
            foreach (var carrier in carriers)
            {
                if (carrier.Glass is not KitGlass glass) continue;
                var labels = carrier.GetComponentsInChildren<TextMeshPro>(false);
                if (glass.Tone is GlazeColor tone)
                {
                    var word = tone.Equals(SpatialKit.Attention) ? "Needs" : tone.Equals(SpatialKit.Passed) ? "Passed" : tone.Equals(SpatialKit.Failure) ? "Failed" : null;
                    if (word == null) yield return carrier.name + " is lit in a colour that means nothing";
                    else if (!labels.Any(label => (label.text ?? "").IndexOf(word, StringComparison.Ordinal) >= 0))
                        yield return carrier.name + " is lit for " + word + " without words that say so";
                }
                var face = glass.FaceSize / 2f;
                foreach (var label in labels)
                {
                    label.ForceMeshUpdate(true);
                    var bounds = label.textBounds;
                    foreach (var corner in new[] { bounds.min, bounds.max, new Vector3(bounds.min.x, bounds.max.y, 0f), new Vector3(bounds.max.x, bounds.min.y, 0f) })
                    {
                        var local = carrier.transform.InverseTransformPoint(label.transform.TransformPoint(corner));
                        if (Mathf.Abs(local.x) > face.x + 1e-5f || Mathf.Abs(local.y) > face.y + 1e-5f)
                        {
                            yield return carrier.name + "'s words \"" + label.text + "\" reach onto its rim";
                            break;
                        }
                    }
                }
            }
        }
    }
}
