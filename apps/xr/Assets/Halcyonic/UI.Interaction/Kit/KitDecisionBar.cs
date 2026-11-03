#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The spatial kit's decision bar, in the composer's place along the slate's lowest edge, nearest the hands: two fixed
    /// places, equal halves of the slate's width 12 mm apart, as the composer's own footer below the same slate is laid. The
    /// left place is the way back, the right place the way on, in every stage (the owner's fixed places, 2026-11-03): asked,
    /// Deny left and Approve right; once either is pressed, the question in Halcyonic's words on its own line above, Cancel
    /// left and Confirm right with the answer's icon, so the primary never leaves the right and the hand never crosses the
    /// slate. Confirm arrives locked, drawn as a locked answer, until the whole request and its notes have been read (until
    /// then the question line says what to read first) and then for <see cref="ConfirmLockSeconds"/> more; a press while
    /// it is locked is refused, never queued, and once it unlocks a press already under way is still dropped by the settle
    /// (<see cref="KitPress"/>). For a Deny, a second press where the first landed is a Cancel. It decides nothing: each
    /// press asks its owner, whose existing confirmation rules decide. Laid from its top left.
    /// </summary>
    public sealed class KitDecisionBar : MonoBehaviour
    {
        /// <summary>How long Confirm stands locked once it could be pressed: its arrival's length, so the press that answered is never a Confirm.</summary>
        public const float ConfirmLockSeconds = 0.7f;

        private KitButton deny = null!;
        private KitButton approve = null!;
        private KitButton cancel = null!;
        private KitButton yes = null!;
        private TextMeshPro question = null!;
        private Vector2 size;
        private bool confirming;
        private bool confirmRead;
        private float readAt = float.PositiveInfinity;
        private float aged;

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
        /// Shows the answers production offers, <paramref name="denyWords"/> in the left place and <paramref name="approveWords"/>
        /// in the right, across a bar <paramref name="width"/> wide with its top left at <paramref name="topLeft"/>; returns its
        /// height. An answer not offered is not drawn and its place stays empty, so the other never moves into it.
        /// </summary>
        public float ShowAsked(Vector2 topLeft, float width, string denyWords, string approveWords, bool denyOffered, bool approveOffered)
        {
            var (left, right) = Lay(topLeft, width);
            if (denyOffered) deny.Show(denyWords, GlazeIcon.Deny, left.Centre, left.Width);
            else deny.Hide();
            if (approveOffered) approve.Show(approveWords, GlazeIcon.Approve, right.Centre, right.Width);
            else approve.Hide();
            cancel.Hide();
            yes.Hide();
            question.gameObject.SetActive(false);
            confirming = false;
            confirmRead = false;
            readAt = float.PositiveInfinity;
            aged = 0f;
            return size.y;
        }

        /// <summary>
        /// Shows the confirmation of the answer pressed, approving where <paramref name="approving"/>: <paramref name="ask"/> on its
        /// own line above where <paramref name="read"/>, else <paramref name="readFirst"/>; Cancel in the left place and
        /// <paramref name="confirm"/> in the right with the answer's icon, locked until read and for <see cref="ConfirmLockSeconds"/>
        /// after. Returns its height.
        /// </summary>
        public float ShowConfirming(Vector2 topLeft, float width, string denyWords, string approveWords, bool approving,
            string ask, string confirm, bool read, string readFirst)
        {
            // The question on its own line above the two places, whole.
            question.gameObject.SetActive(true);
            question.alignment = TextAlignmentOptions.TopLeft;
            question.rectTransform.pivot = new Vector2(0f, 1f);
            KitType.Size(question, KitRole.Body);
            question.color = read ? KitType.Text : KitType.Secondary;
            GlazeText.SetLiteral(question, read ? ask : readFirst);
            var (_, _, questionHeight) = KitType.Lay(question, width, 2);
            question.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            var (left, right) = Lay(topLeft, width, questionHeight + KitType.Scaled(0.5f));
            deny.Hide();
            approve.Hide();
            cancel.Show("Cancel", null, left.Centre, left.Width);
            // The lock starts when Confirm could first be pressed: on arrival when the request is read already, else once it is.
            if (read && (!confirming || !confirmRead))
            {
                readAt = Time.unscaledTime;
                aged = 0f;
            }
            if (!read) readAt = float.PositiveInfinity;
            confirming = true;
            confirmRead = read;
            yes.Available = LockPassed;
            yes.Show(confirm, approving ? GlazeIcon.Approve : GlazeIcon.Deny, right.Centre, right.Width);
            return size.y;
        }

        public void Hide()
        {
            confirming = false;
            confirmRead = false;
            readAt = float.PositiveInfinity;
            aged = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>For renders, which have no clock: as if <paramref name="seconds"/> more had passed since Confirm could first be pressed.</summary>
        public void AgeForRender(float seconds)
        {
            aged += seconds;
            Unlock();
            // Renders run no frames, so the button's own Update never repaints it: Confirm is drawn as it now is.
            yes.PaintForRender(false, false);
        }

        private void Update() => Unlock();

        /// <summary>
        /// Whether Confirm's lock has passed: <see cref="ConfirmLockSeconds"/> since the request was read, a render's age counted. The
        /// time since is taken first, so an unchanged clock gives exactly zero (an absolute deadline aged back could round above it).
        /// </summary>
        private bool LockPassed => Time.unscaledTime - readAt + aged >= ConfirmLockSeconds;

        /// <summary>Confirm becomes pressable once its lock has passed; becoming available unsettles it, so a press under way is dropped.</summary>
        private void Unlock()
        {
            if (confirming && !yes.Available && LockPassed) yes.Available = true;
        }

        /// <summary>The two places, fixed for every stage: equal halves of the bar's width, a target's gap between them.</summary>
        private ((Vector2 Centre, float Width) Left, (Vector2 Centre, float Width) Right) Lay(Vector2 topLeft, float width, float rowTop = 0f)
        {
            gameObject.SetActive(true);
            transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var half = (width - gap) / 2f;
            var y = -rowTop - KitButton.Height / 2f;
            size = new Vector2(width, rowTop + KitButton.Height);
            return ((new Vector2(half / 2f, y), half), (new Vector2(width - half / 2f, y), half));
        }
    }
}
