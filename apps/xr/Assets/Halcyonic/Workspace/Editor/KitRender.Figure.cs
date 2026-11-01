#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class KitRender
    {
        private const float FigureCloseM = 0.65f;

        private static readonly string[] FigureStates =
        {
            "27-figure-not-started",
            "27-figure-starting",
            "27-figure-running",
            "27-figure-checking",
            "27-figure-needs-you",
            "27-figure-round-ended",
            "27-figure-checks-passed",
            "27-figure-checks-failed",
            "27-figure-failed",
            "27-figure-stopped",
            "27-figure-unknown",
            "27-figure-last-known",
            "27-figure-features",
            "27-figure-room",
        };

        private static Shot FigureShot(string state, Transform root, Vector3 eyes) => state switch
        {
            "27-figure-not-started" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.NotStarted, false)),
            "27-figure-starting" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.Starting, false)),
            "27-figure-running" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.Working, false)),
            "27-figure-checking" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.CheckingItsWork, false)),
            "27-figure-needs-you" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.WaitingForYou, false)),
            "27-figure-round-ended" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.FinishedThisRound, false)),
            "27-figure-checks-passed" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.FinishedThisRound, true)),
            "27-figure-checks-failed" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.ChecksFailed, false)),
            "27-figure-failed" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.CouldNotFinish, false)),
            "27-figure-stopped" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.Stopped, false)),
            "27-figure-unknown" => SingleFigure(root, eyes, state, KitFigureLook.Of(WorkState.CantTellYet, false)),
            "27-figure-last-known" => SingleFigure(root, eyes, state, KitFigureLook.Unreachable(KitFigureLook.Of(WorkState.Working, false))),
            "27-figure-features" => FigureFeatures(root, eyes),
            _ => RoomFigure(root, eyes),
        };

        private static Shot SingleFigure(Transform root, Vector3 eyes, string state, KitFigureLook look)
        {
            var shot = new Shot { Caption = "ROBOT6 character figure · " + state + ", synthetic data", Yaw = 0f, Elevation = -8.5f };
            var figure = KitFigure.Create(root, KitFigureFeature.Antenna, KitFigureMeshes.Detail.Near, state);
            figure.transform.SetPositionAndRotation(eyes + new Vector3(0f, -0.22f, FigureCloseM), Quaternion.identity);
            figure.Show(look);
            shot.Centre = figure.transform.position + new Vector3(0f, 0.08f, 0f);
            AddFigureCorners(shot, figure);

            if (figure.Triangles > KitFigureMeshes.NearBudget)
                shot.Failures.Add("the near figure uses " + figure.Triangles + " triangles, over " + KitFigureMeshes.NearBudget);
            var farTriangles = KitFigureMeshes.Body(KitFigureMeshes.Detail.Far).triangles.Length / 3
                + KitFigureMeshes.Head(KitFigureFeature.Antenna, KitFigureMeshes.Detail.Far).triangles.Length / 3
                + 2 * (KitFigureMeshes.Eye(false).triangles.Length / 3)
                + KitFigureMeshes.Pool().triangles.Length / 3;
            if (farTriangles > KitFigureMeshes.FarBudget)
                shot.Failures.Add("the far figure uses " + farTriangles + " triangles, over " + KitFigureMeshes.FarBudget);

            if (figure.ShowPool != look.Moves)
                shot.Failures.Add("the ground projection active state (" + figure.ShowPool + ") does not match look.Moves (" + look.Moves + ")");
            if (state == "27-figure-checks-passed" && (!look.Happy || !look.Eyes.Equals(KitFigureLook.PassedTone)))
                shot.Failures.Add("Checks Passed did not show the happy passed eyes");
            if (state != "27-figure-checks-passed" && (look.Happy || look.Eyes.Equals(KitFigureLook.PassedTone)))
                shot.Failures.Add(state + " showed the passed check's eyes or happy arcs without the computer's passed check");
            if (state == "27-figure-unknown" && !(look.RightEyeShorter > 0.25 && Math.Abs(look.RightEyeRollDegrees) > 5))
                shot.Failures.Add("Status Unknown did not tilt and shorten the right eye");
            if (state == "27-figure-last-known" && (look.Moves || figure.ShowPool || look.Lightness > 0.6 || !look.Eyes.Equals(KitFigureLook.LostTone)))
                shot.Failures.Add("Last Known did not dim the body, turn off the ground projection, and set the eyes to n4");

            shot.AfterEye = (camera, eye, failures) =>
            {
                failures.AddRange(EyeCentres(figure, look, look.Eyes, camera, eye));
                failures.AddRange(EyeContrast(figure, look.Eyes, camera, eye));
                failures.AddRange(BodyShading(figure, look, camera, eye));
                failures.AddRange(PoolCheck(figure, look, camera, eye));
            };
            Debug.Log("Halcyonic: spatial kit: " + state + ": " + figure.Triangles + " near triangles (" + farTriangles + " far); eyes " + look.Eyes
                + ", lids " + look.Lids.ToString("0.00", CultureInfo.InvariantCulture) + ", pool " + (figure.ShowPool ? KitFigure.ActiveToneFor(look).ToString() : "off") + ".");
            return shot;
        }

        private static Shot FigureFeatures(Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "ROBOT6 character family · six identity hues, synthetic data", Yaw = 0f, Elevation = -6.0f };
            var figures = new List<(KitFigure Figure, KitFigureLook Look, GlazeColor Hue)>();
            var look = KitFigureLook.Of(WorkState.Working, false);
            for (var i = 0; i < 6; i++)
            {
                var feature = KitFigureLook.FeatureFor(i);
                var hue = SpatialKit.FigureFamily(feature);
                var figure = KitFigure.Create(root, feature, KitFigureMeshes.Detail.Near, "Feature " + feature);
                var x = (i - 2.5f) * 0.26f;
                figure.transform.SetPositionAndRotation(eyes + new Vector3(x, -0.24f, 1.28f), Quaternion.Euler(0f, -x * 10f, 0f));
                figure.SetIdentityToneOverride(hue);
                figure.Show(look);
                if (figure.Triangles > KitFigureMeshes.NearBudget)
                    shot.Failures.Add(feature + " uses " + figure.Triangles + " triangles, over " + KitFigureMeshes.NearBudget);
                figures.Add((figure, look, hue));
                AddFigureCorners(shot, figure);
            }
            shot.Centre = eyes + new Vector3(0f, -0.10f, 1.28f);

            var distinctHues = figures.Select(entry => entry.Hue).Distinct().Count();
            if (distinctHues != 6)
                shot.Failures.Add("the six family figures do not have six distinct identity hues (" + distinctHues + ")");

            shot.AfterEye = (camera, eye, failures) =>
            {
                foreach (var (figure, figLook, hue) in figures)
                    failures.AddRange(EyeCentres(figure, figLook, hue, camera, eye));
            };
            return shot;
        }

        private static Shot RoomFigure(Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "ROBOT6 character beside its moment card and seven companions, synthetic data" };
            var card = KitMomentCard.Create(root);
            card.Show(MomentSlot("21-moment-question"));
            shot.Yaw = 0f;
            shot.Elevation = -2f;
            shot.Centre = Place(card.transform, eyes, 9f, 2f);
            card.Pop(1f);
            var carriers = card.Carriers.ToList();
            Stand(shot, card.transform, eyes, carriers, card.Size);
            shot.Targets.Add(("moment", card.transform, card.Targets));
            shot.Faces.AddRange(carriers.Select(carrier => carrier.Face));

            var figure = KitFigure.Create(root, KitFigureFeature.Antenna, KitFigureMeshes.Detail.Near, "Room character");
            figure.transform.SetPositionAndRotation(eyes + new Vector3(-0.26f, -0.06f, 0.82f), Quaternion.Euler(0f, 14f, 0f));
            var running = KitFigureLook.Of(WorkState.Working, false);
            figure.Show(running);
            AddFigureCorners(shot, figure);

            // Stand seven far-detail figures across the lower room so eight characters render at once within the draw-call budget.
            var states = new[]
            {
                WorkState.NotStarted, WorkState.Starting, WorkState.CheckingItsWork, WorkState.WaitingForYou,
                WorkState.FinishedThisRound, WorkState.ChecksFailed, WorkState.CantTellYet,
            };
            var eight = new List<KitFigure> { figure };
            for (var i = 0; i < 7; i++)
            {
                var extra = KitFigure.Create(root, KitFigureLook.FeatureFor(i + 1), KitFigureMeshes.Detail.Far, "Crowd " + (i + 1));
                var x = (i - 3f) * 0.30f;
                extra.transform.SetPositionAndRotation(eyes + new Vector3(x, -0.82f, 2.15f), Quaternion.Euler(0f, -x * 8f, 0f));
                extra.Show(KitFigureLook.Of(states[i], i == 4));
                eight.Add(extra);
            }

            shot.AfterEye = (camera, eye, failures) =>
            {
                failures.AddRange(EyeCentres(figure, running, running.Eyes, camera, eye));
                failures.AddRange(EightFigureCost(eight));
            };
            return shot;
        }

        private static IEnumerable<string> EightFigureCost(IReadOnlyList<KitFigure> figures)
        {
            var failures = new List<string>();
            if (figures.Count != 8) failures.Add("expected 8 figures for the draw-call check, had " + figures.Count);
            var drawn = figures.SelectMany(figure => figure.Renderers).Where(each => each.gameObject.activeInHierarchy).ToList();
            var draws = drawn.Sum(each => each.GetComponent<MeshFilter>().sharedMesh.subMeshCount * each.sharedMaterial.passCount);
            var passes = drawn.Select(each => each.sharedMaterial.shader).Distinct().Sum(shader => shader.passCount);
            var materials = drawn.Select(each => each.sharedMaterial).Distinct().Count();
            if (!drawn.All(each => each.shadowCastingMode == ShadowCastingMode.Off && !each.receiveShadows))
                failures.Add("a figure renderer casts or takes shadows");
            Debug.Log("Halcyonic: spatial kit: 8 figures rendered in at most " + draws + " draw calls (" + passes
                + " shader passes, " + materials + " shared materials).");
            if (draws <= 0 || draws > 40)
                failures.Add("8 figures took " + draws + " draw calls (budget 40)");
            if (passes <= 0 || passes > 24)
                failures.Add("8 figures took " + passes + " shader passes (budget 24)");
            if (materials > 3)
                failures.Add("8 figures used " + materials + " shared materials (expected 2)");
            return failures;
        }

        private static IEnumerable<string> EyeCentres(KitFigure figure, KitFigureLook look, GlazeColor expected, Camera camera, Color32[] eye)
        {
            for (var i = 0; i < 2; i++)
            {
                var open = look.Happy ? 1f : Mathf.Max(0.08f, 1f - (float)look.Lids);
                var shorter = i == 1 ? 1f - (float)look.RightEyeShorter : 1f;
                var expectedScaleY = KitFigureMeshes.EyeHeightM * open * shorter;
                if (Mathf.Abs(figure.Eyes[i].localScale.y - expectedScaleY) > 1e-4f)
                    yield return figure.name + " eye " + i + " vertical scale is " + figure.Eyes[i].localScale.y + ", expected " + expectedScaleY;

                // When lids are nearly shut (Stopped, Lids >= 0.7), the slit is held by the scale check above.
                if (!look.Happy && look.Lids >= 0.7) continue;

                var world = figure.Eyes[i].TransformPoint(Vector3.zero);
                if (!TrySample(camera, eye, world, out var seen))
                {
                    yield return figure.name + " eye " + i + " is outside the frame";
                    continue;
                }
                if (seen.a < 245 || !Near(seen, expected, 10))
                    yield return figure.name + " eye " + i + " is (" + seen.r + ", " + seen.g + ", " + seen.b + ", " + seen.a
                        + "), expected " + expected;
                var wrong = expected.Equals(KitFigureLook.LiveTone) ? SpatialKit.Attention : KitFigureLook.LiveTone;
                if (Near(seen, wrong, 10))
                    yield return figure.name + " eye " + i + " matched the wrong state tone " + wrong;
            }
        }

        private static IEnumerable<string> EyeContrast(KitFigure figure, GlazeColor expected, Camera camera, Color32[] eye)
        {
            var room = new Color32(0xF6, 0xF0, 0xE6, 255);
            var eyeLum = expected.Luminance;
            var visorCenter = figure.Head.TransformPoint(new Vector3(0f, KitFigureMeshes.EyeUpM, -0.059f));
            if (!TrySample(camera, eye, visorCenter, out var visorPixel))
            {
                yield return figure.name + " visor sample is outside the frame";
                yield break;
            }
            var over = CompositePixel(visorPixel, room);
            var lum = Luminance(over);
            var ratio = (Math.Max(eyeLum, lum) + 0.05) / (Math.Min(eyeLum, lum) + 0.05);
            if (ratio < 3.0)
                yield return figure.name + " eye contrast reaches only " + ratio.ToString("0.00", CultureInfo.InvariantCulture)
                    + ":1 against the recessed gloss-black visor over the bright room (floor 3:1)";
        }

        private static IEnumerable<string> BodyShading(KitFigure figure, KitFigureLook look, Camera camera, Color32[] eye)
        {
            var topWorld = figure.Lifted.TransformPoint(new Vector3(0f, 0.125f, -0.058f));
            var lowWorld = figure.Lifted.TransformPoint(new Vector3(0f, 0.028f, -0.030f));
            var leftArmWorld = figure.Lifted.TransformPoint(new Vector3(-0.081f, 0.084f, -0.008f));
            var rightArmWorld = figure.Lifted.TransformPoint(new Vector3(0.081f, 0.084f, -0.008f));
            var leftGapWorld = figure.Lifted.TransformPoint(new Vector3(-0.061f, 0.084f, 0f));

            if (!TrySample(camera, eye, topWorld, out var top) || !TrySample(camera, eye, lowWorld, out var low))
            {
                yield return figure.name + " torso shading sample is outside the frame";
                yield break;
            }
            if (top.a < 254 || low.a < 254)
                yield return figure.name + " porcelain torso is not opaque (" + top.a + ", " + low.a + ")";
            if (Luminance(top) <= Luminance(low) + 0.02)
                yield return figure.name + " porcelain torso is flat (" + Luminance(top).ToString("0.000", CultureInfo.InvariantCulture)
                    + " top vs " + Luminance(low).ToString("0.000", CultureInfo.InvariantCulture) + " underside)";

            var minBrightness = look.Moves ? 0.45 : 0.12;
            var maxBrightness = look.Moves ? 1.00 : 0.42;
            if (Luminance(top) < minBrightness || Luminance(top) > maxBrightness)
                yield return figure.name + " porcelain shoulder luminance " + Luminance(top).ToString("0.000", CultureInfo.InvariantCulture)
                    + " is outside [" + minBrightness.ToString("0.00", CultureInfo.InvariantCulture) + ", " + maxBrightness.ToString("0.00", CultureInfo.InvariantCulture) + "]";

            if (!TrySample(camera, eye, leftArmWorld, out var leftArm) || !TrySample(camera, eye, rightArmWorld, out var rightArm))
            {
                yield return figure.name + " capsule-wing arm sample is outside the frame";
                yield break;
            }
            if (leftArm.a < 254 || rightArm.a < 254)
                yield return figure.name + " detached capsule-wing arms are not opaque (" + leftArm.a + ", " + rightArm.a + ")";
            if (TrySample(camera, eye, leftGapWorld, out var gap) && gap.a > 16)
                yield return figure.name + " 11 mm arm-to-torso air gap is occluded (alpha " + gap.a + ")";
        }

        private static IEnumerable<string> PoolCheck(KitFigure figure, KitFigureLook look, Camera camera, Color32[] eye)
        {
            var reflWorld = figure.transform.TransformPoint(new Vector3(0f, -KitFigureMeshes.PoolBelowM, -0.0296f));
            var ringWorld = figure.transform.TransformPoint(new Vector3(0f, -KitFigureMeshes.PoolBelowM, -0.0570f));
            var outsideWorld = figure.transform.TransformPoint(new Vector3(0.0855f, -KitFigureMeshes.PoolBelowM, -0.0304f));

            if (!TrySample(camera, eye, reflWorld, out var reflSample) || !TrySample(camera, eye, ringWorld, out var ringSample))
            {
                yield return figure.name + " ground projection sample is outside the frame";
                yield break;
            }

            if (!look.Moves)
            {
                if (reflSample.a > 2 || ringSample.a > 2)
                    yield return figure.name + " drew a ground projection (alpha " + reflSample.a + ", " + ringSample.a + ") in Last Known";
                yield break;
            }

            if (reflSample.a < 8 || ringSample.a < 15)
            {
                yield return figure.name + " ground projection is missing (reflection alpha " + reflSample.a + ", inner ring alpha " + ringSample.a + ")";
                yield break;
            }

            if (TrySample(camera, eye, outsideWorld, out var outsideSample) && outsideSample.a > 2)
                yield return figure.name + " ground projection leaks blurry smudge outside the outer ring (alpha " + outsideSample.a + ")";

            var expectedTone = KitFigure.ActiveToneFor(look);
            var dark = new Color32(18, 20, 24, 255);
            var overDark = CompositePixel(reflSample, dark);
            if (!ChannelMatches(overDark, dark, expectedTone))
                yield return figure.name + " ground projection over the dark room (" + overDark.r + ", " + overDark.g + ", " + overDark.b
                    + ") does not carry the tone " + expectedTone;

            var bright = new Color32(0xD8, 0xCE, 0xBE, 255);
            var overBright = CompositePixel(ringSample, bright);
            if (!PoolLiftsBright(overBright, bright))
                yield return figure.name + " ground projection inner ring disappears over the bright room";
        }

        private static bool ChannelMatches(Color32 over, Color32 room, GlazeColor tone)
        {
            var dr = over.r - room.r;
            var dg = over.g - room.g;
            var db = over.b - room.b;
            if (Math.Max(dr, Math.Max(dg, db)) < 6) return false;
            if (tone.R > tone.B + 25) return dr > db;
            if (tone.B > tone.R + 25) return db > dr;
            if (tone.G > tone.R + 25) return dg > dr;
            return dr >= 4 && dg >= 4 && db >= 4;
        }

        private static bool PoolLiftsBright(Color32 over, Color32 bright) =>
            Math.Abs(over.r - bright.r) + Math.Abs(over.g - bright.g) + Math.Abs(over.b - bright.b) >= 8;

        private static Color32 CompositePixel(Color32 premul, Color32 under)
        {
            float Decode(byte c)
            {
                var s = c / 255f;
                return s <= 0.04045f ? s / 12.92f : Mathf.Pow((s + 0.055f) / 1.055f, 2.4f);
            }
            byte Encode(float l)
            {
                l = Mathf.Clamp01(l);
                var s = l <= 0.0031308f ? l * 12.92f : 1.055f * Mathf.Pow(l, 1f / 2.4f) - 0.055f;
                return (byte)Mathf.RoundToInt(Mathf.Clamp01(s) * 255f);
            }
            var a = premul.a / 255f;
            var r = Decode(premul.r) + Decode(under.r) * (1f - a);
            var g = Decode(premul.g) + Decode(under.g) * (1f - a);
            var b = Decode(premul.b) + Decode(under.b) * (1f - a);
            return new Color32(Encode(r), Encode(g), Encode(b), 255);
        }

        private static bool TrySample(Camera camera, Color32[] pixels, Vector3 world, out Color32 colour)
        {
            var screen = camera.WorldToScreenPoint(world);
            var x = Mathf.RoundToInt(screen.x);
            var y = Mathf.RoundToInt(screen.y);
            if (screen.z <= 0f || x < 0 || y < 0 || x >= Width || y >= Height)
            {
                colour = default;
                return false;
            }
            colour = pixels[y * Width + x];
            return true;
        }

        private static bool Near(Color32 seen, GlazeColor expected, int tolerance) =>
            Math.Abs(seen.r - expected.R) <= tolerance
            && Math.Abs(seen.g - expected.G) <= tolerance
            && Math.Abs(seen.b - expected.B) <= tolerance;

        private static void AddFigureCorners(Shot shot, KitFigure figure)
        {
            foreach (var x in new[] { -0.11f, 0.11f })
            foreach (var y in new[] { -0.02f, 0.27f })
            foreach (var z in new[] { -0.08f, 0.08f })
                shot.Corners.Add(figure.transform.TransformPoint(new Vector3(x, y, z)));
        }
    }
}
