#nullable enable
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
    /// A moment's arrival frame by frame (the art direction's choreography, 2026-10-30; <see cref="KitMomentChoreography"/>):
    /// a question at 60, 160, 300, 460, 700, 1400 and 2000 ms, a pass at 700 ms with its check drawing, and a question under
    /// Keep Still. Each frame holds: the bloom is the clock's and nothing else's, and none outside the pop (AUDIT's A2-S2); the
    /// parts come in reading order; nothing moves under Keep Still; and once settled the card is exactly the static card, on
    /// which every moment check has already run. Synthetic data.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ChoreographyStates =
        {
            "24-arrive-0060", "24-arrive-0160", "24-arrive-0300", "24-arrive-0460", "24-arrive-0700", "24-arrive-1400", "24-arrive-2000",
            "24-arrive-passed-0700", "24-arrive-still-0060",
        };

        private static Shot ChoreographyShot(string state, Transform root, Vector3 eyes)
        {
            var ms = double.Parse(state.Substring(state.Length - 4), CultureInfo.InvariantCulture);
            var still = state.Contains("still");
            var passed = state.Contains("passed");
            // The card at rest first, with every moment check run on it as it stands.
            var shot = MomentShot(passed ? "21-moment-longest" : "21-moment-question", root, eyes);
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var card = root.GetComponentInChildren<KitMomentCard>();
            var restBody = (card.Body.localPosition, card.Body.localScale);
            var parts = card.Parts;
            var restParts = parts.SelectMany(group => group).ToDictionary(part => part, part => part.localPosition);
            var restColours = card.GetComponentsInChildren<TMP_Text>(true).ToDictionary(label => label, label => label.color);

            // A stand-in for the character's head, to the card's lower left, as the room places it.
            card.GrowsFromLeft = true;
            var head = card.transform.TransformPoint(new Vector3(-card.Size.x * 0.75f, -card.Size.y * 0.7f, 0f));
            var choreography = KitMomentChoreography.For(card);
            choreography.Arrive(head, still);
            choreography.Seek(ms);
            var settled = ms >= KitMomentTimeline.ArrivalMs(parts.Count);
            shot.Moving = !settled;
            shot.Caption += " · its arrival at " + ms.ToString("0", CultureInfo.InvariantCulture) + " ms" + (still ? ", kept still" : "");
            if (!settled) shot.AfterEye = null;

            // The bloom: the clock's, and none outside the pop or under Keep Still (AUDIT's A2-S2).
            var glass = card.Carrier.Glass!;
            var expected = (float)KitMomentTimeline.Bloom(ms, still);
            Require(Mathf.Abs(glass.Bloom - expected) < 1e-4f, "the bloom is " + glass.Bloom.ToString("0.000") + " where the clock says " + expected.ToString("0.000"));
            if (still || ms < KitMomentTimeline.OpenAtMs || ms >= KitMomentTimeline.OpenAtMs + KitMomentTimeline.BloomMs)
                Require(glass.Bloom == 0f, "the bloom stands outside the pop");

            // The parts come in reading order: none stands more shown than the one before it.
            float Shown(IReadOnlyList<Transform> group)
            {
                // The pass's check is left out: it draws itself on its own clock, after the parts.
                var label = group.SelectMany(part => part.GetComponentsInChildren<TMP_Text>(true)).FirstOrDefault(each => each != card.Tone && restColours.ContainsKey(each) && restColours[each].a > 0f);
                return label == null ? 1f : label.color.a / restColours[label].a;
            }
            for (var index = 0; index + 1 < parts.Count; index++)
                Require(Shown(parts[index]) + 1e-4f >= Shown(parts[index + 1]), "part " + (index + 1) + " shows before part " + index + " at " + ms + " ms");

            // Under Keep Still nothing moves: no scale, no rise, no line.
            if (still)
            {
                Require(card.Body.localScale == Vector3.one && card.Body.localPosition == restBody.Item1, "the card scales under Keep Still");
                Require(restParts.All(pair => pair.Key.localPosition == pair.Value), "a part rises under Keep Still");
                Require(!choreography.Line.gameObject.activeSelf, "the light line shows under Keep Still");
            }

            // A pass's check draws itself as the card settles.
            if (passed && card.Tone.gameObject.activeSelf)
            {
                var drawn = (float)KitMomentTimeline.CheckDrawn(ms, still);
                Require(Mathf.Abs(card.Tone.color.a - restColours[card.Tone].a * drawn) < 0.02f, "the check is not drawn as far as the clock says");
            }

            // Settled, the card is exactly the static card the moment checks ran on.
            if (settled)
            {
                Require(card.Body.localScale == restBody.Item2 && card.Body.localPosition == restBody.Item1, "the settled card is not where it was laid out");
                Require(restParts.All(pair => pair.Key.localPosition == pair.Value), "a settled part is not where it was laid out");
                Require(restColours.All(pair => pair.Key.color == pair.Value), "a settled word is not as it was drawn");
                Require(glass.Bloom == 0f && !choreography.Line.gameObject.activeSelf && !choreography.Playing, "the arrival has not settled");
            }
            Debug.Log("Halcyonic: spatial kit: arrival " + state + ": bloom " + glass.Bloom.ToString("0.000") + ", scale " + card.Body.localScale.x.ToString("0.000")
                + ", line " + (choreography.Line.gameObject.activeSelf ? "shown" : "hidden") + ", parts " + string.Join(" ", parts.Select(group => Shown(group).ToString("0.00"))) + ".");
            return shot;
        }
    }
}
