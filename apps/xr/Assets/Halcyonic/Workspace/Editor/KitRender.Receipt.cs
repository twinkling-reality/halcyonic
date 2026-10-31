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
    /// An answered question frame by frame (the art direction's Answered and Receipt; <see cref="KitMomentChoreography.Answer"/>):
    /// at 150 ms the taken answer stands while the others have gone; at 240 and 260 ms the card shrinks from its top, no longer
    /// lit, past parts not yet faded out; at
    /// 1000 ms the receipt stands, "Sent · Awaiting Start" or "Sent · Declined"; under Keep Still at 60 ms; and 100 ms after a
    /// retirement or an arrival begun at 800 ms, while the answer still plays (AUDIT's A3b2a M1). Each frame holds:
    /// the glass as tall and as lit as the clock says, no part the glass has passed still shown (no words on the room), the
    /// receipt never before the shrink, its words exact with "Sent" in the primary text, never "Started", and the glass's truth;
    /// once cut short, the receipt as it stood, never the question back: no answer, no Title or well, the glass unlit at the
    /// receipt's height, the receipt fading with the card.
    /// Synthetic data.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ReceiptStates =
        {
            "26-answered-0150", "26-answered-0240", "26-answered-0260", "26-receipt-run-1000", "26-receipt-declined-1000", "26-answered-still-0060",
            "26-receipt-retired-0800", "26-receipt-retired-still-0800", "26-receipt-arrived-0800",
        };

        /// <summary>How long after a retirement or arrival cut the answer short its frame is taken.</summary>
        private const double CutShortMs = 100;

        private static Shot ReceiptShot(string state, Transform root, Vector3 eyes)
        {
            var ms = double.Parse(state.Substring(state.Length - 4), CultureInfo.InvariantCulture);
            var still = state.Contains("still");
            var ran = !state.Contains("declined");
            var retired = state.Contains("retired");
            var arrived = state.Contains("arrived");
            var kept = ran ? KitMomentAction.YesRunIt : KitMomentAction.No;
            // The question at rest first, with every moment check run on it as it stands.
            var shot = MomentShot("21-moment-question", root, eyes);
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var card = root.GetComponentInChildren<KitMomentCard>();
            Require(card.Shown.Any(each => each.Action == kept), "the question offers no " + kept + " to take");
            var restColours = card.GetComponentsInChildren<TMP_Text>(true).ToDictionary(label => label, label => label.color);
            var keptButton = card.Shown.First(each => each.Action == kept).Button;
            var others = card.Shown.Where(each => each.Action != kept).Select(each => each.Button).ToList();
            // The lowest point of a drawing on the card's body, where the shrinking glass first passes it.
            float Bottom(Renderer drawn)
            {
                var b = drawn.localBounds;
                return new[] { b.min, b.max }.Min(corner => card.Body.InverseTransformPoint(drawn.transform.TransformPoint(corner)).y);
            }
            var keptBottom = keptButton.GetComponentsInChildren<Renderer>(false).Min(Bottom);

            // The taken answer takes a press at rest, and none once the answer is given (AUDIT's A3b2a S2).
            var presses = 0;
            keptButton.Pressed += () => presses++;
            keptButton.Press.SettleForRender();
            keptButton.Press.PressForRender(0);
            Require(presses == 1, "the taken answer took " + presses + " presses at rest, so its refusal after the answer proves nothing");

            card.GrowsFromLeft = true;
            var head = card.transform.TransformPoint(new Vector3(-card.Size.x * 0.75f, -card.Size.y * 0.7f, 0f));
            var choreography = KitMomentChoreography.For(card);
            choreography.Answer(kept, ran, head, still);
            choreography.Seek(ms);
            // Cut short while the answer plays: the next moment retires the card, or arrives on it, 100 ms before this frame.
            if (retired) choreography.Retire(still);
            if (arrived) choreography.Arrive(head, still);
            if (retired || arrived) choreography.Seek(CutShortMs);
            keptButton.Press.SettleForRender();
            keptButton.Press.PressForRender(1);
            Require(presses == 1, "the taken answer took a press after the answer");
            Require(card.Shown.All(each => each.Button.Spent), "an answer on the answered card still takes presses");
            var frame = KitMomentTimeline.Answered(ms, still);
            var resting = !retired && !arrived && frame.ReceiptOpacity >= 1 && frame.Shrink >= 1;
            shot.Moving = !resting;
            if (!resting) shot.AfterEye = null;
            shot.Caption = "An answered question: " + (ran ? "Run" : "Decline") + " taken, at " + ms.ToString("0", CultureInfo.InvariantCulture) + " ms"
                + (retired ? ", retired 100 ms ago" : arrived ? ", arrived on 100 ms ago" : "") + (still ? ", kept still" : "") + ", synthetic data";

            // The glass: as tall as the shrink says, from the card's top, and lit only while the question stands.
            var height = Mathf.Lerp(card.Size.y, card.ReceiptHeight, (float)frame.Shrink);
            Require(Mathf.Abs(card.Drawn.y - height) < 1e-5f, "the glass stands " + card.Drawn.y.ToString("0.0000") + " tall where the shrink says " + height.ToString("0.0000"));
            Require((card.Carrier.Glass?.Tone != null) == frame.Lit, "the glass is " + (frame.Lit ? "not " : "") + "lit at " + ms + " ms");

            // The taken answer stands while the others go.
            float Alpha(KitButton button) => button.gameObject.activeSelf ? button.Words.color.a / restColours[button.Words].a : 0f;
            // Where the glass has passed it, the taken answer is hidden however much of its fade is left.
            var faceFoot = card.Carrier.transform.localPosition.y - (card.Carrier.Glass != null ? card.Carrier.Glass.FaceSize.y / 2f : 0f);
            var keptExpected = keptBottom < faceFoot - 1e-4f ? 0f : (float)frame.KeptOpacity;
            Require(Mathf.Abs(Alpha(keptButton) - keptExpected) < 0.02f, "the taken answer shows at " + Alpha(keptButton).ToString("0.00") + ", not " + keptExpected.ToString("0.00"));
            foreach (var other in others)
                Require(Alpha(other) <= (float)(frame.OthersOpacity * frame.BodyOpacity) + 0.02f, "an answer not taken still shows at " + ms + " ms");

            // Nothing the glass has passed still shows: no words, no capsule, on the room.
            foreach (var drawn in card.Body.GetComponentsInChildren<Renderer>(false))
            {
                if (drawn.transform.IsChildOf(card.Carrier.transform) || drawn.transform.IsChildOf(card.Earlier.transform)) continue;
                Require(Bottom(drawn) >= faceFoot - 1e-4f, drawn.name + " stands below the shrinking glass, on the room");
            }

            // The receipt: never before the shrink; at rest its words exact, "Sent" in the primary text, the rest secondary.
            Require(!card.ReceiptLead.gameObject.activeSelf || frame.Shrink > 0, "the receipt stands before the card shrinks");
            if (resting)
            {
                var (lead, then) = KitMomentWords.Receipt(ran);
                Require(card.ReceiptLead.gameObject.activeSelf && card.ReceiptLead.text == lead && card.ReceiptThen.text == KitMomentWords.Dot.TrimStart() + then,
                    "the receipt reads \"" + card.ReceiptLead.text + " " + card.ReceiptThen.text + "\"");
                Require(SameColour(card.ReceiptLead.color, KitType.Text) && SameColour(card.ReceiptThen.color, KitType.Secondary), "the receipt's \"Sent\" is not the primary text, or the rest not secondary");
                Require(Mathf.Abs(card.Drawn.y - card.ReceiptHeight) < 1e-5f, "the card is not its receipt's height");
                Require(card.Eyebrow.gameObject.activeSelf && card.Eyebrow.color.a >= restColours[card.Eyebrow].a - 1e-3f, "the receipt lost its eyebrow");
            }
            if (retired || arrived)
            {
                // The receipt as it stood, never the question back: no answer, no Title or well, the glass unlit at the receipt's height.
                foreach (var each in card.Shown) Require(!each.Button.gameObject.activeSelf, each.Action + " shows again after the answer");
                Require(!card.Title.gameObject.activeSelf && !card.Well.gameObject.activeSelf, "the question's Title or well shows again after the answer");
                Require(card.Carrier.Glass?.Tone == null, "the glass is lit again after the answer");
                Require(Mathf.Abs(card.Drawn.y - card.ReceiptHeight) < 1e-5f, "the card is not its receipt's height after the answer");
                // Retired: the receipt fades with the card; arrived: it rises with the card's first parts.
                var cut = retired ? KitMomentTimeline.Retirement(CutShortMs, still) : KitMomentTimeline.Arrival(CutShortMs, still);
                var receiptAlpha = card.ReceiptLead.gameObject.activeSelf ? card.ReceiptLead.color.a / restColours[card.ReceiptLead].a : 0f;
                var expected = (float)(cut.Part(1).Opacity * cut.CardOpacity);
                Require(Mathf.Abs(receiptAlpha - expected) < 0.02f, "the receipt shows at " + receiptAlpha.ToString("0.00") + ", not " + expected.ToString("0.00") + ", " + CutShortMs + " ms after the card was cut short");
            }
            Require(!card.Words.Any(label => label.text.IndexOf("Started", System.StringComparison.OrdinalIgnoreCase) >= 0), "an answered card says Started");

            // The glass's truth on every frame: words of ours for the light it shows, every word on its face.
            var panels = new List<(KitCarrier, IEnumerable<TMP_Text>)> { (card.Carrier, card.Words.Cast<TMP_Text>().ToList()) };
            if (card.Earlier.gameObject.activeSelf) panels.Add((card.Earlier, new TMP_Text[0]));
            foreach (var failure in GlassTruth(panels)) shot.Failures.Add(failure);
            Debug.Log("Halcyonic: spatial kit: answered " + state + ": glass " + (card.Drawn.y / card.Size.y).ToString("0.00") + " of its height, lit " + frame.Lit
                + ", taken " + Alpha(keptButton).ToString("0.00") + ", receipt " + (card.ReceiptLead.gameObject.activeSelf ? card.ReceiptLead.color.a.ToString("0.00") : "hidden") + ".");
            return shot;
        }
    }
}
