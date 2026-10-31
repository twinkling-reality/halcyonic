#nullable enable
using System;
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
        private bool answering;
        private bool sentSounded;
        private Transform? keptAnswer;
        private IReadOnlyList<Transform> answered = new List<Transform>();
        private readonly Dictionary<Transform, bool> wasActive = new Dictionary<Transform, bool>();
        private readonly Dictionary<Transform, float> bottoms = new Dictionary<Transform, float>();

        /// <summary>The answered moment is playing: its answers' fade, its shrink to the receipt, then its retirement.</summary>
        public bool Answering => answering;

        /// <summary>The time since the arrival or retirement began, in milliseconds.</summary>
        public double Elapsed => ms;

        public bool Playing => playing;

        /// <summary>The light line, for checks.</summary>
        public Surface Line => line;

        /// <summary>
        /// The player the arrival's cue plays through, where the owner gives one; silent without (the lead's call, 2026-10-30:
        /// the room's cues sound at the card once the room is wired into MenuPlane, and the stage stays silent for that event).
        /// </summary>
        public KitSoundPlayer? Sound { get; set; }

        /// <summary>The cue the arrival plays at 300 ms: a question's, a pass's or a failure's; none for any other moment.</summary>
        public KitSound? Cue { get; private set; }

        private bool sounded;

        /// <summary>
        /// A moment's cue by the tone its glass is lit in: a question's, a pass's or a failure's, each named; no other tone, and
        /// no tone at all, sounds (AUDIT's A3C-S1: a later accent never sounds like a failure).
        /// </summary>
        public static KitSound? CueOf(GlazeColor? tone)
        {
            if (tone is not GlazeColor lit) return null;
            if (lit.Equals(SpatialKit.Attention)) return KitSound.Question;
            if (lit.Equals(SpatialKit.Passed)) return KitSound.Passed;
            if (lit.Equals(SpatialKit.Failure)) return KitSound.Failed;
            return null;
        }

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
        /// character's head, in the world); under <paramref name="keepStill"/> it lands as a short fade. A card still its receipt
        /// arrives as its receipt: only the next Show brings a question.
        /// </summary>
        public void Arrive(Vector3 headAt, bool keepStill)
        {
            Settle();
            head = headAt;
            still = keepStill;
            retiring = false;
            card.Pop(1f);
            sounded = false;
            var tone = card.Carrier.Glass?.Tone;
            Cue = CueOf(tone);
            bodyAt = card.Body.localPosition;
            parts = card.ReceiptLead.gameObject.activeSelf ? card.ReceiptParts : card.Parts;
            placed.Clear();
            colours.Clear();
            foreach (var part in parts.SelectMany(group => group))
                placed[part] = part.localPosition;
            foreach (var label in card.GetComponentsInChildren<TMP_Text>(true))
                colours[label] = label.color;
            playing = true;
            Seek(0);
        }

        /// <summary>
        /// Begins an answered moment (the handoff's Answered; <see cref="KitMomentTimeline.Answered"/>): the answers not taken
        /// fade while <paramref name="kept"/> stays; the card shrinks to its receipt (<see cref="KitMomentCard.ShowReceipt"/>)
        /// from its top, no longer lit, and every part the glass passes is hidden, so no words stand on the room; the receipt
        /// fades in with the Sent cue through the player the owner gives (silent without); at 1600 ms the card retires to
        /// <paramref name="headAt"/>. Under <paramref name="keepStill"/> it lands as fades.
        /// </summary>
        public void Answer(KitMomentAction kept, bool ran, Vector3 headAt, bool keepStill)
        {
            Settle();
            head = headAt;
            still = keepStill;
            retiring = false;
            card.Pop(1f);
            card.ShowReceipt(ran);
            foreach (var each in card.Shown) each.Button.Spent = true;
            keptAnswer = card.Shown.Where(each => each.Action == kept).Select(each => each.Button.transform).FirstOrDefault();
            bodyAt = card.Body.localPosition;
            var all = card.Parts;
            answered = all.Skip(1).SelectMany(group => group).ToList();
            parts = card.ReceiptParts;
            placed.Clear();
            colours.Clear();
            wasActive.Clear();
            bottoms.Clear();
            foreach (var part in all.SelectMany(group => group).Concat(parts.SelectMany(group => group)))
            {
                placed[part] = part.localPosition;
                wasActive[part] = part.gameObject.activeSelf;
                bottoms[part] = BottomOf(part);
            }
            foreach (var label in card.GetComponentsInChildren<TMP_Text>(true))
                colours[label] = label.color;
            answering = true;
            sentSounded = false;
            playing = true;
            Seek(0);
        }

        /// <summary>The lowest point of a part's drawing on the card's body, where the shrinking glass first passes it.</summary>
        private float BottomOf(Transform part)
        {
            var low = float.MaxValue;
            foreach (var drawn in part.GetComponentsInChildren<Renderer>(false))
            {
                var b = drawn.localBounds;
                foreach (var corner in new[] { b.min, b.max, new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.max.x, b.min.y, b.max.z) })
                    low = Mathf.Min(low, card.Body.InverseTransformPoint(drawn.transform.TransformPoint(corner)).y);
            }
            return low;
        }

        /// <summary>One frame of the answer before the card retires.</summary>
        private void ShowAnswered(KitAnsweredFrame frame)
        {
            var height = Mathf.Lerp(card.Size.y, card.ReceiptHeight, (float)frame.Shrink);
            card.DrawHeight(height, frame.Lit);
            var glass = card.Carrier.Glass;
            glass?.Fade(1f);
            glass?.SetBloom(0f);
            // The glass's face from the card's top: a part below it would stand on the room.
            var faceFoot = -height / 2f - (glass != null ? glass.FaceSize.y / 2f : height / 2f);
            foreach (var part in answered)
            {
                var opacity = part == keptAnswer ? frame.KeptOpacity : card.Shown.Any(each => each.Button.transform == part) ? frame.OthersOpacity * frame.BodyOpacity : frame.BodyOpacity;
                var shows = wasActive[part] && opacity > 0.001 && bottoms[part] >= faceFoot - 1e-5f;
                part.gameObject.SetActive(shows);
                if (shows) Fade(part, (float)opacity);
            }
            foreach (var part in parts[parts.Count - 1])
            {
                part.gameObject.SetActive(frame.ReceiptOpacity > 0.001);
                Fade(part, (float)frame.ReceiptOpacity);
            }
            line.gameObject.SetActive(false);
        }

        /// <summary>
        /// Begins the card's retirement: it scales down and fades as the line drops back, then hides. Begun while an answer
        /// plays, the answer lands at its receipt at once and the receipt retires.
        /// </summary>
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
            if (answering)
            {
                // The Sent cue as the receipt comes in, once, from the card's place (at once under Keep Still: sound is no motion).
                if (!sentSounded && (still || ms >= KitMomentTimeline.SentAtMs))
                {
                    sentSounded = true;
                    Sound?.Play(KitSound.Sent, card.Carrier.transform.position);
                }
                ShowAnswered(KitMomentTimeline.Answered(Math.Min(ms, KitMomentTimeline.AnsweredRetireAtMs - 1e-3), still));
                if (ms < KitMomentTimeline.AnsweredRetireAtMs) return;
            }
            // An answered card retires from its receipt, from 1600 ms.
            var local = answering ? ms - KitMomentTimeline.AnsweredRetireAtMs : ms;
            var leaving = retiring || answering;
            var frame = leaving ? KitMomentTimeline.Retirement(local, still) : KitMomentTimeline.Arrival(local, still);
            var unit = KitType.Units(SpatialKit.Degrees(1f));

            // The card opens (or retires) about 14 percent across its bottom edge on the character's side.
            var size = card.Drawn;
            var side = card.GrowsFromLeft ? -1f : 1f;
            var pivot = new Vector3(side * (size.x / 2f - 0.14f * size.x), -size.y, 0f);
            var scale = (float)frame.CardScale;
            card.Body.localScale = new Vector3(scale, scale, 1f);
            card.Body.localPosition = bodyAt + (1f - scale) * pivot;

            // The glass: its face's opacity, and the bloom from the clock alone.
            foreach (var carrier in card.Carriers)
                if (carrier.Glass is KitGlass glass) glass.Fade((float)frame.CardOpacity);
            if (card.Carrier.Glass is KitGlass lit) lit.SetBloom(leaving ? 0f : (float)frame.Bloom);

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
            // Its sound from its place, once, as its parts begin to rise (at once under Keep Still: sound is no motion).
            if (!leaving && !sounded && Cue is KitSound cue && (still || ms >= KitMomentTimeline.PartsAtMs))
            {
                sounded = true;
                Sound?.Play(cue, card.Carrier.transform.position);
            }

            // A pass's check draws itself as the card settles.
            if (card.Tone.gameObject.activeSelf && colours.TryGetValue(card.Tone, out var check))
                card.Tone.color = new Color(check.r, check.g, check.b, check.a * (float)(leaving ? frame.CardOpacity : KitMomentTimeline.CheckDrawn(ms, still) * frame.CardOpacity));

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

            var end = leaving ? KitMomentTimeline.RetireMs : KitMomentTimeline.ArrivalMs(parts.Count);
            if (local >= end)
            {
                if (leaving) card.gameObject.SetActive(false);
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
            if (answering)
            {
                // The answer stays given: the card stays its receipt, unlit, its answered parts hidden, until the next Show lays a
                // question; never the question back, lit with its answers (AUDIT's A3b2a M1).
                answering = false;
                foreach (var part in answered) if (part != null) part.gameObject.SetActive(false);
                card.DrawHeight(card.ReceiptHeight, false);
                card.ReceiptLead.gameObject.SetActive(true);
                card.ReceiptThen.gameObject.SetActive(true);
                if (!sentSounded)
                {
                    sentSounded = true;
                    Sound?.Play(KitSound.Sent, card.Carrier.transform.position);
                }
            }
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
