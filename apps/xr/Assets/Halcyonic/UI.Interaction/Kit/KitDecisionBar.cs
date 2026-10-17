#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The spatial kit's decision bar, in the composer's place along the slate's lowest edge, nearest the hands. Asked,
    /// it offers the two answers the work waits on, Deny then Approve at its right. Once one is pressed, its place
    /// becomes Cancel, the other's stands empty, and the confirmation, "Yes, {verb}", stands where no control stood a
    /// moment before, under the question in Halcyonic's words on its own line above, and only once the whole request and its notes have been
    /// read; until then the bar says what to read first and offers no Yes (WORDS: confirmation, locked final press).
    /// Places never move, so a second press where the first one landed is a Cancel, never a Yes. It decides nothing:
    /// each press asks its owner, whose existing confirmation rules decide. Laid from its top left.
    /// </summary>
    public sealed class KitDecisionBar : MonoBehaviour
    {
        private KitButton deny = null!;
        private KitButton approve = null!;
        private KitButton cancel = null!;
        private KitButton yes = null!;
        private TextMeshPro question = null!;
        private Vector2 size;

        public event Action? DenyPressed;
        public event Action? ApprovePressed;
        public event Action? CancelPressed;
        public event Action? YesPressed;

        public KitButton Deny => deny;
        public KitButton Approve => approve;
        public KitButton Cancel => cancel;
        public KitButton Yes => yes;
        public TextMeshPro Question => question;
        public Vector2 Size => size;

        public static KitDecisionBar Create(Transform parent, string name = "Decision bar")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bar = go.AddComponent<KitDecisionBar>();
            bar.deny = KitButton.Create(go.transform, "Deny", KitButtonRole.Secondary);
            bar.approve = KitButton.Create(go.transform, "Approve", KitButtonRole.Primary);
            bar.cancel = KitButton.Create(go.transform, "Cancel", KitButtonRole.Secondary);
            bar.yes = KitButton.Create(go.transform, "Yes", KitButtonRole.Primary);
            bar.question = KitType.Create(go.transform, "Question", KitRole.Body, KitType.Text, KitType.Order.Words);
            bar.deny.Pressed += () => bar.DenyPressed?.Invoke();
            bar.approve.Pressed += () => bar.ApprovePressed?.Invoke();
            bar.cancel.Pressed += () => bar.CancelPressed?.Invoke();
            bar.yes.Pressed += () => bar.YesPressed?.Invoke();
            go.SetActive(false);
            return bar;
        }

        /// <summary>
        /// Shows the two answers, <paramref name="denyWords"/> then <paramref name="approveWords"/> at the right of a bar
        /// <paramref name="width"/> wide with its top left at <paramref name="topLeft"/>; returns its height.
        /// </summary>
        public float ShowAsked(Vector2 topLeft, float width, string denyWords, string approveWords)
        {
            var (denyAt, approveAt) = Lay(topLeft, width, denyWords, approveWords);
            deny.Show(denyWords, GlazeIcon.Deny, denyAt.Centre, denyAt.Width);
            approve.Show(approveWords, GlazeIcon.Approve, approveAt.Centre, approveAt.Width);
            cancel.Hide();
            yes.Hide();
            question.gameObject.SetActive(false);
            return size.y;
        }

        /// <summary>
        /// Shows the confirmation of the answer pressed, approving where <paramref name="approving"/>: Cancel in its
        /// place, the other answer's place empty, and <paramref name="ask"/> at the left with <paramref name="confirm"/>
        /// after it where <paramref name="read"/>, else <paramref name="readFirst"/> in Yes's stead. Returns its height.
        /// </summary>
        public float ShowConfirming(Vector2 topLeft, float width, string denyWords, string approveWords, bool approving,
            string ask, string confirm, bool read, string readFirst)
        {
            // The question on its own line above the answers' row, whole.
            question.gameObject.SetActive(true);
            question.alignment = TextAlignmentOptions.TopLeft;
            question.rectTransform.pivot = new Vector2(0f, 1f);
            KitType.Size(question, KitRole.Body);
            question.color = read ? KitType.Text : KitType.Secondary;
            GlazeText.SetLiteral(question, read ? ask : readFirst);
            var (_, _, questionHeight) = KitType.Lay(question, width, 2);
            question.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            var rowTop = questionHeight + KitType.Scaled(0.5f);
            var (denyAt, approveAt) = Lay(topLeft, width, denyWords, approveWords, rowTop);
            deny.Hide();
            approve.Hide();
            var pressed = approving ? approveAt : denyAt;
            cancel.Show("Cancel", null, pressed.Centre, pressed.Width);
            // Yes stands left of both answers' places, where no control stood a moment before.
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var leftOfAnswers = denyAt.Centre.x - denyAt.Width / 2f - gap;
            if (read)
            {
                var yesWidth = yes.Measure(confirm, approving ? GlazeIcon.Approve : GlazeIcon.Deny);
                yes.Show(confirm, approving ? GlazeIcon.Approve : GlazeIcon.Deny, new Vector2(leftOfAnswers - yesWidth / 2f, denyAt.Centre.y), yesWidth);
            }
            else yes.Hide();
            return size.y;
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Where the two answers stand, fixed for every stage: Approve rightmost, Deny a target's gap before it.</summary>
        private ((Vector2 Centre, float Width) Deny, (Vector2 Centre, float Width) Approve) Lay(Vector2 topLeft, float width, string denyWords, string approveWords,
            float rowTop = 0f)
        {
            gameObject.SetActive(true);
            transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var height = rowTop + KitButton.Height;
            var y = -rowTop - KitButton.Height / 2f;
            var approveWidth = approve.Measure(approveWords, GlazeIcon.Approve);
            var denyWidth = deny.Measure(denyWords, GlazeIcon.Deny);
            size = new Vector2(width, height);
            return ((new Vector2(width - approveWidth - gap - denyWidth / 2f, y), denyWidth), (new Vector2(width - approveWidth / 2f, y), approveWidth));
        }
    }
}
