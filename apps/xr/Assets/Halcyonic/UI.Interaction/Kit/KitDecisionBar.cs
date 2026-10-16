#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The spatial kit's decision bar, along the slate's lowest edge, nearest the hands: Close at the far left, the
    /// question in Halcyonic's words beside it, and at the right Cancel then the confirmation, "Yes, {verb}", which
    /// stands there only once every part of what it confirms has been read; until then the bar says what to read
    /// first and offers no Yes. Places never move between states, so a hand that knows where Yes stands never meets
    /// another control there. It decides nothing itself: each press asks its owner, whose existing confirmation
    /// rules decide. Laid from its top left.
    /// </summary>
    public sealed class KitDecisionBar : MonoBehaviour
    {
        private KitButton close = null!;
        private KitButton cancel = null!;
        private KitButton yes = null!;
        private TextMeshPro question = null!;
        private Vector2 size;

        public event Action? ClosePressed;
        public event Action? CancelPressed;
        public event Action? YesPressed;

        public KitButton Close => close;
        public KitButton Cancel => cancel;
        public KitButton Yes => yes;
        public TextMeshPro Question => question;
        public Vector2 Size => size;

        public static KitDecisionBar Create(Transform parent, string name = "Decision bar")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bar = go.AddComponent<KitDecisionBar>();
            bar.close = KitButton.Create(go.transform, "Close", KitButtonRole.Secondary);
            bar.cancel = KitButton.Create(go.transform, "Cancel", KitButtonRole.Secondary);
            bar.yes = KitButton.Create(go.transform, "Yes", KitButtonRole.Primary);
            bar.question = KitType.Create(go.transform, "Question", KitRole.Body, KitType.Text, KitType.Order.Words, TextAlignmentOptions.Left);
            bar.question.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.close.Pressed += () => bar.ClosePressed?.Invoke();
            bar.cancel.Pressed += () => bar.CancelPressed?.Invoke();
            bar.yes.Pressed += () => bar.YesPressed?.Invoke();
            return bar;
        }

        /// <summary>
        /// Shows the bar <paramref name="width"/> wide asking <paramref name="ask"/>; with <paramref name="read"/> the
        /// confirmation <paramref name="confirm"/> stands at the right, else <paramref name="readFirst"/> says what to
        /// read. The bar stands with its top left at <paramref name="topLeft"/>; returns its height.
        /// </summary>
        public float Show(Vector2 topLeft, float width, string ask, string confirm, bool read, string readFirst)
        {
            transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var height = KitButton.Height;
            var y = -height / 2f;
            var closeWidth = close.Measure("Close", GlazeIcon.Close);
            close.Show("Close", GlazeIcon.Close, new Vector2(closeWidth / 2f, y), closeWidth);
            var right = width;
            if (read)
            {
                var yesWidth = yes.Measure(confirm, GlazeIcon.Approve);
                yes.Show(confirm, GlazeIcon.Approve, new Vector2(right - yesWidth / 2f, y), yesWidth);
                right -= yesWidth + gap;
            }
            else yes.Hide();
            var cancelWidth = cancel.Measure("Cancel", null);
            cancel.Show("Cancel", null, new Vector2(right - cancelWidth / 2f, y), cancelWidth);
            right -= cancelWidth + gap;
            var left = closeWidth + gap;
            KitType.Size(question, KitRole.Body);
            question.color = read ? KitType.Text : KitType.Secondary;
            GlazeText.SetLiteral(question, read ? ask : readFirst);
            KitType.Lay(question, Mathf.Max(KitType.Units(4f), right - left), 2);
            question.transform.localPosition = new Vector3(left, y, -0.0005f);
            size = new Vector2(width, height);
            return height;
        }
    }
}
