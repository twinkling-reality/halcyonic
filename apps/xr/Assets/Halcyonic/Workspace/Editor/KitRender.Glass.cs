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
    /// The room's window glass (ART8, 2026-10-31, at the floor-safe value; <see cref="KitGlassMaterial"/>): panels at rest and
    /// in each state tone at the bloom's peak, at each inset, over the art direction's dark room, lit room and white wall. Each
    /// layer is read back from the eye buffer and set against the prototype's own composite: the face with its top light and
    /// the bloom must land within 6/255 over every room, out to where the quiet glass's rim stood (no part lets the room
    /// through), and the shadow over the lit room; every other room is reported. The panels' words are synthetic and name
    /// the state their light shows.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] GlassStates = { "23-glass" };

        private const float GlassWidthDp = 168f, GlassHeightDp = 104f, GlassRadiusDp = 24f, GlassGapDp = 40f, GlassRowDp = 224f;

        /// <summary>The worst a matched layer may land from the prototype's composite, in 255ths (handoff 7.3).</summary>
        private const double GlassTolerance = 6;

        private static readonly (string Name, GlazeColor Room)[] GlassRooms =
            { ("dark room", KitGlassMaterial.DarkRoom), ("lit room", KitGlassMaterial.LitRoom), ("white wall", KitGlassMaterial.WhiteWall) };

        private static readonly (string Words, float Rim, GlazeColor? Tone, GlazeColor? Tint)[] GlassPanels =
        {
            ("At Rest", KitGlassMaterial.RimDp, null, null),
            ("Needs You", KitGlassMaterial.RimDp, SpatialKit.Attention, null),
            ("Passed", KitGlassMaterial.RimDp, SpatialKit.Passed, null),
            ("Failed", KitGlassMaterial.RimDp, SpatialKit.Failure, null),
            ("Plate Rim", KitGlassMaterial.PlateRimDp, null, SpatialKit.AvatarTint),
            ("Tile Rim", KitGlassMaterial.SmallRimDp, null, null),
        };

        /// <summary>The room's panels and what each one's glass costs in fill (handoff section 5's sizes, in dp).</summary>
        private static readonly (string Part, float Width, float Height)[] GlassParts =
            { ("screen", 896f, 504f), ("reader", 704f, 528f), ("moment card", 368f, 400f), ("bar", 896f, 86f), ("tile", 224f, 200f), ("tag", 200f, 56f) };

        private static Shot GlassShot(string state, Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "The window glass at rest and in each state, over a dark room, a lit room and a white wall, synthetic words" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var view = new GameObject("Glass").transform;
            view.SetParent(root, false);
            var unit = KitGlass.Units(1f);
            var panels = new List<(KitCarrier Carrier, string Room, GlazeColor Ground, float Rim, GlazeColor? Tone, GlazeColor? Tint)>();
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
                    var (words, rim, tone, tint) = GlassPanels[index];
                    var x = (index - (GlassPanels.Length - 1) / 2f) * (GlassWidthDp + GlassGapDp);
                    var carrier = KitCarrier.Create(view, words + " over the " + roomName);
                    carrier.DrawGlass(new Vector2(GlassWidthDp, GlassHeightDp) * unit, GlassRadiusDp * unit, rim, tone, tone != null ? 1f : 0f, tint: tint);
                    carrier.transform.localPosition = new Vector3(x * unit, y * unit, 0f);
                    // Strong, not Label: the page's outer panels stand 30 degrees out, where Label would read under the floor.
                    var label = KitType.Create(carrier.transform, "Words", KitRole.Strong, KitType.Text, KitType.Order.Words, room: true);
                    GlazeText.SetLiteral(label, words);
                    var inset = KitGlassMaterial.RimDp + 14f;
                    var (lines, _, _) = KitType.Lay(label, (GlassWidthDp - 2f * inset) * unit, 1);
                    Require(lines == 1 && !label.isTextTruncated, words + " is cut");
                    label.transform.localPosition = new Vector3((-GlassWidthDp / 2f + inset) * unit, (GlassHeightDp / 2f - inset) * unit, -0.0005f);
                    panels.Add((carrier, roomName, ground, rim, tone, tint));
                }
            }
            shot.Elevation = 0f;
            shot.Centre = Place(view, eyes, 0f, shot.Elevation);

            var carriers = panels.Select(each => each.Carrier).ToList();
            Require(carriers.All(carrier => carrier.Glass != null), "a panel did not draw in glass");
            foreach (var failure in GlassTruth(carriers.Select(carrier => (carrier, (IEnumerable<TMP_Text>)carrier.GetComponentsInChildren<TMP_Text>(false))))) shot.Failures.Add(failure);
            // The truth check's own witnesses (AUDIT's A2-S1 and A3A-S1): a panel lit for a state must be refused where its only word
            // for it is an agent's, in its run, or ours inside another word or negated. Each is checked and taken away before the render.
            foreach (var (tone, words, theirsRun) in new (GlazeColor, string, bool)[]
            {
                (SpatialKit.Passed, "Passed", true), (SpatialKit.Passed, "Not Ready", false), (SpatialKit.Passed, "Already Answered", false),
                (SpatialKit.Failure, "Not Failed", false), (SpatialKit.Attention, "No Needs", false),
            })
            {
                var witness = KitCarrier.Create(view, "Witness: " + words);
                witness.DrawGlass(new Vector2(GlassWidthDp, GlassHeightDp) * unit, GlassRadiusDp * unit, KitGlassMaterial.RimDp, tone);
                var label = KitType.Create(witness.transform, "Words", KitRole.Strong, KitType.Text, KitType.Order.Words, room: true);
                GlazeRuns.Of(label).Set(theirsRun ? LineWords.Theirs(words) : LineWords.Ours(words));
                KitType.Lay(label, (GlassWidthDp - 2f * (KitGlassMaterial.RimDp + 14f)) * unit, 1);
                label.transform.localPosition = new Vector3((-GlassWidthDp / 2f + KitGlassMaterial.RimDp + 14f) * unit, (GlassHeightDp / 2f - KitGlassMaterial.RimDp - 14f) * unit, -0.0005f);
                if (!GlassTruth(new[] { (witness, (IEnumerable<TMP_Text>)new TMP_Text[] { label }) }).Any())
                    shot.Failures.Add("the truth check let \"" + words + "\"" + (theirsRun ? ", an agent's," : "") + " light its state");
                else Debug.Log("Halcyonic: spatial kit: the glass truth check refused a panel lit by \"" + words + "\", as it must.");
                UnityEngine.Object.DestroyImmediate(witness.gameObject);
            }
            Debug.Log("Halcyonic: spatial kit: glass fill: " + string.Join(", ", GlassParts.Select(part =>
                part.Part + " " + KitGlassMaterial.Overdraw(part.Width, part.Height).ToString("0.00") + "x its area")) + ".");

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
                foreach (var (carrier, room, ground, rim, tone, tint) in panels)
                {
                    var (w, h) = (GlassWidthDp, GlassHeightDp);
                    Vector3 At(float x, float y) => carrier.transform.TransformPoint(new Vector3(x * unit, y * unit, 0f));
                    var name = carrier.name;
                    // The face, opaque over the whole panel and the same over every room: the grey, its soft top light, the bloom
                    // where a tone stands; toward the right, clear of the words at the top left and of the inner edge.
                    var x = w / 2f - rim - 8f;
                    var across = x / (w / 2.0);
                    foreach (var tf in new[] { 0.10, 0.36, 0.90 })
                    {
                        var face = KitGlassMaterial.LitFace(tf, across, tint);
                        var expected = tone is GlazeColor shown ? KitGlassMaterial.Prototype(KitGlassMaterial.Channels(shown), KitGlassMaterial.BloomAt(tf), KitGlassMaterial.ToColour(face)) : face;
                        Sample(tone != null ? "bloom" : tint != null ? "tint" : "face", room, At(x, (float)(h / 2f - tf * h)), expected, true, name + " face at " + tf.ToString("0.00"));
                    }
                    if (tint != null)
                    {
                        var leftX = -w / 2f + rim + 8f;
                        var leftY = -h / 2f + rim + 14f;
                        var leftTf = (h / 2.0 - leftY) / h;
                        var leftExpected = KitGlassMaterial.LitFace(leftTf, leftX / (w / 2.0), tint);
                        Sample("tint", room, At(leftX, leftY), leftExpected, true, name + " tinted left edge");
                    }
                    // Where the quiet glass's rim stood the face goes on: opaque, the same grey, never the room.
                    foreach (var (rx, ry, where) in new[] { (0f, -h / 2f + rim * 0.6f, "foot"), (w / 2f - rim * 0.6f, 0f, "side") })
                    {
                        var t = (h / 2.0 - ry) / h;
                        var lit = KitGlassMaterial.LitFace(t, rx / (w / 2.0), tint);
                        var rimExpected = tone is GlazeColor bloomTone ? KitGlassMaterial.Prototype(KitGlassMaterial.Channels(bloomTone), KitGlassMaterial.BloomAt(t), KitGlassMaterial.ToColour(lit)) : lit;
                        Sample("face", room, At(rx, ry), rimExpected, true, name + " face at the former rim's " + where);
                    }
                    // Outside: the shadow under the foot, matched over the lit room.
                    Sample("shadow", room, At(0f, -h / 2f - 20f), KitGlassMaterial.PrototypeOutside(0, -h / 2.0 - 20, w, h, GlassRadiusDp, ground, glow: false), ground.Equals(KitGlassMaterial.LitRoom), name + " shadow");
                }
                foreach (var layer in worst.Keys.Select(key => key.Layer).Distinct())
                    Debug.Log("Halcyonic: spatial kit: glass " + layer + " from the prototype, worst: " + string.Join(", ", GlassRooms.Where(room => worst.ContainsKey((layer, room.Name)))
                        .Select(room => room.Name + " " + worst[(layer, room.Name)].ToString("0.0") + "/255")) + ".");
            };
            return shot;
        }

        /// <summary>
        /// What the glass may say (AUDIT, A2 and its S1): a state's light (its edge, its bloom) stands only on a panel whose own
        /// words, Halcyonic's, say that state: an agent's "Passed" in its run never lights the passed edge. Needs is said by a
        /// question of ours or "Needs"; a pass by "passed" or "ready"; a failure by "failed"; each as a whole word of ours, never negated. No word stands on a rim, which lets the
        /// room through.
        /// </summary>
        private static IEnumerable<string> GlassTruth(IEnumerable<(KitCarrier Carrier, IEnumerable<TMP_Text> Words)> panels)
        {
            foreach (var (carrier, panelWords) in panels)
            {
                if (carrier.Glass is not KitGlass glass) continue;
                var labels = panelWords.Where(label => label.gameObject.activeInHierarchy).ToList();
                if (glass.Tone is GlazeColor tone)
                {
                    var word = tone.Equals(SpatialKit.Attention) ? "needs" : tone.Equals(SpatialKit.Passed) ? "passed" : tone.Equals(SpatialKit.Failure) ? "failed" : null;
                    if (word == null) yield return carrier.name + " is lit in a colour that means nothing";
                    else if (!labels.Any(label => SaysState(Ours(label), tone)))
                        yield return carrier.name + " is lit for its state without words of ours that say so";
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
                            yield return carrier.name + "'s words \"" + label.text + "\" reach onto its rim (a corner at " + local.x.ToString("0.0000") + ", " + local.y.ToString("0.0000") + "; the face to " + face.x.ToString("0.0000") + ", " + face.y.ToString("0.0000") + ")";
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether our <paramref name="ours"/> words say the state a <paramref name="tone"/> lights (AUDIT's A3A-S1): the state's
        /// whole word, never inside another ("Already"), never negated ("Not Ready", "Not Failed", "No"); a pass is "passed" or
        /// "ready" (a ready moment stands only after the computer's own passed check), a failure "failed", and needs "needs" or a
        /// question of ours, ending in "?".
        /// </summary>
        private static bool SaysState(string ours, GlazeColor tone)
        {
            var words = System.Text.RegularExpressions.Regex.Matches(ours.ToLowerInvariant(), "[a-z]+").Cast<System.Text.RegularExpressions.Match>().Select(match => match.Value).ToList();
            bool Said(params string[] states)
            {
                for (var i = 0; i < words.Count; i++)
                    if (states.Contains(words[i]) && (i == 0 || (words[i - 1] != "not" && words[i - 1] != "no" && words[i - 1] != "never"))) return true;
                return false;
            }
            if (tone.Equals(SpatialKit.Passed)) return Said("passed", "ready");
            if (tone.Equals(SpatialKit.Failure)) return Said("failed");
            if (tone.Equals(SpatialKit.Attention)) return Said("needs") || ours.TrimEnd().EndsWith("?", StringComparison.Ordinal);
            return false;
        }

        /// <summary>A panel's own words alone: the label's text less every run that is theirs.</summary>
        private static string Ours(TMP_Text label)
        {
            var text = label.text ?? "";
            var theirs = GlazeRuns.Of(label).Theirs;
            var ours = new System.Text.StringBuilder();
            for (var i = 0; i < text.Length; i++)
                if (!theirs.Any(run => i >= run.Start && i < run.End)) ours.Append(text[i]);
            return ours.ToString();
        }

        /// <summary>
        /// A glass panel's face is opaque as drawn: on a grid over the face inside its rim, the eye buffer lets none of the room
        /// through (as <c>Opaque</c> holds a <see cref="Surface"/> face).
        /// </summary>
        private static IEnumerable<string> GlassFaceOpaque(KitCarrier carrier, Camera camera, Color32[] eye, string what)
        {
            if (carrier.Glass is not KitGlass glass) yield break;
            var half = glass.FaceSize / 2f;
            var least = 255;
            for (var i = 1; i < 12; i++)
            for (var j = 1; j < 12; j++)
            {
                var local = new Vector3(half.x * (-0.92f + 1.84f * i / 12f), half.y * (-0.92f + 1.84f * j / 12f), 0f);
                var screen = camera.WorldToScreenPoint(carrier.transform.TransformPoint(local));
                var (x, y) = (Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y));
                if (screen.z <= 0f || x < 0 || y < 0 || x >= Width || y >= Height) continue;
                least = Mathf.Min(least, eye[y * Width + x].a);
            }
            if (least < 254) yield return what + ": " + carrier.name + "'s glass face lets the room through (alpha " + least + " of 255).";
        }

        /// <summary>
        /// The opacity check for a glass panel, with its own witness: the face holds as rendered, and the same panel drawn at
        /// 15 percent must fail it; then the panel is drawn whole again.
        /// </summary>
        private static IEnumerable<string> GlassFaceHolds(KitCarrier carrier, Camera camera, Color32[] eye, string what)
        {
            var found = GlassFaceOpaque(carrier, camera, eye, what).ToList();
            if (carrier.Glass is not KitGlass glass) return found;
            glass.Fade(0.15f);
            var faded = Render(camera, camera.targetTexture);
            try
            {
                if (!GlassFaceOpaque(carrier, camera, faded.GetPixels32(), what).Any()) found.Add(what + ": the glass opacity check accepted a face drawn at 15 percent");
                else Debug.Log("Halcyonic: spatial kit: " + what + ": the glass opacity check refused the translucent witness, as it must.");
            }
            finally
            {
                glass.Fade(1f);
                UnityEngine.Object.DestroyImmediate(faded);
            }
            return found;
        }
    }
}
