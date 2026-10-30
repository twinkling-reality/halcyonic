#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Plays a moment's arrival and retirement on a shown card (the art direction's choreography, 2026-10-30;
    /// <see cref="KitMomentTimeline"/>): the light line from the character's head to the card's growing corner, the card opening
    /// about 14 percent across its bottom edge on the spring, its parts rising into place in reading order, its bloom, and a
    /// pass's check drawing itself. Every value is read from the time since it began and nothing else; the bloom never stands
    /// for progress (AUDIT's A2-S2). Keep Still lands it as a short fade. At rest the card is exactly as it was laid out.
    /// </summary>
    public sealed class KitMomentChoreography : MonoBehaviour
    {
        private KitMomentCard card = null!;
        private Surface line = null!;
        private Vector3 head;
        private bool still;
        private bool retiring;
        private double ms;
        private bool playing;
        private Vector3 bodyAt;
        private IReadOnlyList<IReadOnlyList<Transform>> parts = new List<IReadOnlyList<Transform>>();
        private readonly Dictionary<Transform, Vector3> placed = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<TMP_Text, Color> colours = new Dictionary<TMP_Text, Color>();

        /// <summary>The time since the arrival or retirement began, in milliseconds.</summary>
        public double Elapsed => ms;

        public bool Playing => playing;

        /// <summary>The light line, for checks.</summary>
        public Surface Line => line;

        public static KitMomentChoreography For(KitMomentCard card)
        {
            var choreography = card.GetComponent<KitMomentChoreography>();
            if (choreography == null) choreography = card.gameObject.AddComponent<KitMomentChoreography>();
            choreography.card = card;
            if (choreography.line == null)
            {
                choreography.line = Surface.Create(card.transform, "Light line", KitType.Order.Carrier - 1);
                choreography.line.gameObject.SetActive(false);
            }
            return choreography;
        }

        /// <summary>
        /// Begins the arrival of the card as it is laid out now, its light line rising from <paramref name="headAt"/> (the
        /// character's head, in the world); under <paramref name="keepStill"/> it lands as a short fade.
        /// </summary>
        public void Arrive(Vector3 headAt, bool keepStill)
        {
            Settle();
            head = headAt;
            still = keepStill;
            retiring = false;
            card.Pop(1f);
            bodyAt = card.Body.localPosition;
            parts = card.Parts;
            placed.Clear();
            colours.Clear();
            foreach (var part in parts.SelectMany(group => group))
                placed[part] = part.localPosition;
            foreach (var label in card.GetComponentsInChildren<TMP_Text>(true))
                colours[label] = label.color;
            playing = true;
            Seek(0);
        }

        /// <summary>Begins the card's retirement: it scales down and fades as the line drops back, then hides.</summary>
        public void Retire(bool keepStill)
        {
            if (!playing) Arrive(head, keepStill);
            Settle();
            playing = true;
            retiring = true;
            still = keepStill;
            Seek(0);
        }

        /// <summary>Shows the frame <paramref name="atMs"/> after the arrival or retirement began; the renders seek frames this way.</summary>
        public void Seek(double atMs)
        {
            ms = atMs;
            var frame = retiring ? KitMomentTimeline.Retirement(ms, still) : KitMomentTimeline.Arrival(ms, still);
            var unit = KitType.Units(SpatialKit.Degrees(1f));

            // The card opens (or retires) about 14 percent across its bottom edge on the character's side.
            var size = card.Size;
            var side = card.GrowsFromLeft ? -1f : 1f;
            var pivot = new Vector3(side * (size.x / 2f - 0.14f * size.x), -size.y, 0f);
            var scale = (float)frame.CardScale;
            card.Body.localScale = new Vector3(scale, scale, 1f);
            card.Body.localPosition = bodyAt + (1f - scale) * pivot;

            // The glass: its face's opacity, and the bloom from the clock alone.
            foreach (var carrier in card.Carriers)
                if (carrier.Glass is KitGlass glass) glass.Fade((float)frame.CardOpacity);
            if (card.Carrier.Glass is KitGlass lit) lit.SetBloom(retiring ? 0f : (float)frame.Bloom);

            // The parts rise into place and fade in, in reading order.
            for (var index = 0; index < parts.Count; index++)
            {
                var (rise, opacity) = frame.Part(index);
                foreach (var part in parts[index])
                {
                    part.localPosition = placed[part] - new Vector3(0f, (float)rise * unit, 0f);
                    Fade(part, (float)(opacity * frame.CardOpacity));
                }
            }
            // A pass's check draws itself as the card settles.
            if (card.Tone.gameObject.activeSelf && colours.TryGetValue(card.Tone, out var check))
                card.Tone.color = new Color(check.r, check.g, check.b, check.a * (float)(retiring ? frame.CardOpacity : KitMomentTimeline.CheckDrawn(ms, still) * frame.CardOpacity));

            // The light line: from the character's head to the card's growing corner, 2 dp, the tone from 15 percent to full.
            var showLine = frame.LineOpacity > 0.001 && frame.LineProgress > 0.001;
            line.gameObject.SetActive(showLine);
            if (showLine)
            {
                var from = card.transform.InverseTransformPoint(head);
                var corner = card.Body.localPosition + card.Body.localScale.x * pivot;
                var to = Vector3.Lerp(from, corner, (float)frame.LineProgress);
                var length = Vector3.Distance(from, to);
                var width = KitType.Units(SpatialKit.Degrees(2f));
                var tone = card.Carrier.Glass?.Tone ?? SpatialKit.Text;
                line.Draw(new Vector2(width, Mathf.Max(length, width)), width / 2f, KitType.Colour(tone, (float)frame.LineOpacity));
                line.transform.localPosition = (from + to) / 2f + new Vector3(0f, 0f, 0.0006f);
                line.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
            }

            var end = retiring ? KitMomentTimeline.RetireMs : KitMomentTimeline.ArrivalMs(parts.Count);
            if (ms >= end)
            {
                if (retiring) card.gameObject.SetActive(false);
                Settle();
            }
        }

        /// <summary>Puts every part back exactly as it was laid out and stops.</summary>
        private void Settle()
        {
            if (!playing) return;
            playing = false;
            card.Body.localScale = Vector3.one;
            card.Body.localPosition = bodyAt;
            foreach (var pair in placed) pair.Key.localPosition = pair.Value;
            foreach (var pair in colours) if (pair.Key != null) pair.Key.color = pair.Value;
            foreach (var surface in card.GetComponentsInChildren<Surface>(true)) surface.Fade(1f);
            foreach (var carrier in card.Carriers)
                if (carrier.Glass is KitGlass glass)
                {
                    glass.Fade(1f);
                    glass.SetBloom(0f);
                }
            line.gameObject.SetActive(false);
        }

        private void Fade(Transform part, float opacity)
        {
            foreach (var label in part.GetComponentsInChildren<TMP_Text>(true))
                if (colours.TryGetValue(label, out var colour)) label.color = new Color(colour.r, colour.g, colour.b, colour.a * opacity);
            foreach (var surface in part.GetComponentsInChildren<Surface>(true)) surface.Fade(opacity);
        }

        private void Update()
        {
            if (playing) Seek(ms + Time.deltaTime * 1000.0);
        }
    }
}
