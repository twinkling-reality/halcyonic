#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuPlane
    {
        private Transform? momentHolder;
        private readonly Dictionary<KitButton, Action> momentHandlers = new Dictionary<KitButton, Action>();

        /// <summary>The task's moment beside its character or task mark (ADR 0040, rule 4), while one is shown.</summary>
        public KitMomentCard? Moment { get; private set; }

        /// <summary>The workstream id whose moment is currently shown, or null.</summary>
        public string? MomentWorkstreamId { get; private set; }

        /// <summary>Where <see cref="RoomLayout.MomentSlot"/> stood the moment, in the room's metres.</summary>
        public RoomPose? MomentPose { get; private set; }

        /// <summary>Whether the card's growing lower corner is on the side of what it comes out of.</summary>
        public bool MomentGrowsTowardAnchor { get; private set; }

        /// <summary>Raised when a button on <see cref="Moment"/> is pressed, with the action and current <see cref="MomentWorkstreamId"/>.</summary>
        public event Action<KitMomentAction, string?>? MomentActed;

        /// <summary>
        /// Shows <paramref name="slot"/>'s moment in the one slot beside <paramref name="anchor"/>: the task's character
        /// at rest, or the task mark standing in its place, which reaches <paramref name="anchorHalfDegrees"/> either side
        /// with its label (<see cref="BodyInView.HalfWidth"/>). The slot is on its side toward
        /// <paramref name="lookBearing"/>, <see cref="SpatialKit.MomentDegrees"/> wide, as high as the card's words need,
        /// scaled by its true distance. The kit draws and pops the card; this only stands it.
        /// </summary>
        public void ShowMoment(KitMomentSlot slot, Vector3 eyes, float lookBearing, Vector3 anchor, float anchorHalfDegrees, string? workstreamId = null)
        {
            if (momentHolder == null)
            {
                momentHolder = new GameObject("Moment slot").transform;
                momentHolder.SetParent(transform, false);
            }
            if (Moment == null) Moment = KitMomentCard.Create(momentHolder);
            // The slot stands on the anchor's side toward the look, so the anchor is on the card's left when it is left of the look.
            var anchorBearing = Mathf.Atan2(anchor.x - eyes.x, anchor.z - eyes.z) * Mathf.Rad2Deg;
            Moment.GrowsFromLeft = Mathf.DeltaAngle(lookBearing, anchorBearing) < 0f;
            Moment.Show(slot);
            if (!Moment.gameObject.activeSelf)
            {
                UnwireMoment();
                (MomentWorkstreamId, MomentPose, MomentGrowsTowardAnchor) = (null, null, false);
                return;
            }
            // Measured whole: the card lays out in units of its distance and grows from its corner by its own scale.
            var popped = Moment.Popped;
            Moment.Pop(1f);
            var at = new System.Numerics.Vector3(eyes.x, eyes.y, eyes.z);
            var pose = RoomLayout.MomentSlot(at, lookBearing, new System.Numerics.Vector3(anchor.x, anchor.y, anchor.z),
                anchorHalfDegrees, SpatialKit.MomentDegrees, Moment.Size.x / Moment.Size.y);
            momentHolder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            momentHolder.localScale = Vector3.one;
            Moment.transform.localPosition = Vector3.zero;
            // The card's carrier stands on the slot's middle, wherever the card keeps its origin.
            Moment.transform.localPosition = -momentHolder.InverseTransformPoint(Moment.Carrier.Face.transform.position);
            Stand(momentHolder, pose, pose.Width / Moment.Size.x);
            MomentGrowsTowardAnchor = Mathf.DeltaAngle(pose.Bearing, anchorBearing) < 0f == Moment.GrowsFromLeft;
            MomentWorkstreamId = workstreamId;
            MomentPose = pose;
            Moment.Pop(popped);
            WireMoment();
        }

        private void UnwireMoment()
        {
            foreach (var pair in momentHandlers)
            {
                if (pair.Key != null) pair.Key.Pressed -= pair.Value;
            }
            momentHandlers.Clear();
        }

        private void WireMoment()
        {
            UnwireMoment();
            if (Moment == null) return;
            foreach (var (action, button) in Moment.Shown)
            {
                if (button == null) continue;
                var captured = action;
                Action handler = () => MomentActed?.Invoke(captured, MomentWorkstreamId);
                momentHandlers[button] = handler;
                button.Pressed += handler;
            }
        }

        /// <summary>
        /// The other places' Needs You cards (the lead's 2026-11-17T19:17Z limit #2): beside <see cref="Moment"/>, one more card for each
        /// further place whose tasks wait, each beside its own task's character at that place, keyed by the task. Each stands as the
        /// first does (<see cref="ShowMoment"/>'s slot), never over another.
        /// </summary>
        public IReadOnlyDictionary<string, KitMomentCard> PlaceMoments => placeMomentCards;

        private readonly Dictionary<string, KitMomentCard> placeMomentCards = new Dictionary<string, KitMomentCard>(StringComparer.Ordinal);
        private readonly Dictionary<string, Transform> placeMomentHolders = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<KitButton, Action> placeMomentHandlers = new Dictionary<KitButton, Action>();

        /// <summary>The "{n} More Waiting" line under each card, keyed by the card's task; the first card's under <see cref="Moment"/>'s key.</summary>
        public IReadOnlyDictionary<string, TMPro.TextMeshPro> MoreWaiting => moreWaiting;

        private readonly Dictionary<string, TMPro.TextMeshPro> moreWaiting = new Dictionary<string, TMPro.TextMeshPro>(StringComparer.Ordinal);

        /// <summary>
        /// Stands every further place's card: <paramref name="moments"/>, each with its slot, its character's rest place and half width,
        /// and its "{n} More Waiting" words (null for none), as <see cref="ShowMoment"/> stands the first. Cards for tasks no longer given go.
        /// </summary>
        public void ShowPlaceMoments(IReadOnlyList<(string WorkstreamId, KitMomentSlot Slot, Vector3 Anchor, float AnchorHalfDegrees, string? MoreWaitingWords)> moments,
            Vector3 eyes, float lookBearing)
        {
            var given = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (workstreamId, slot, anchor, half, more) in moments)
            {
                given.Add(workstreamId);
                if (!placeMomentHolders.TryGetValue(workstreamId, out var holder))
                {
                    holder = new GameObject("Place moment " + workstreamId).transform;
                    holder.SetParent(transform, false);
                    placeMomentHolders[workstreamId] = holder;
                    placeMomentCards[workstreamId] = KitMomentCard.Create(holder);
                }
                var card = placeMomentCards[workstreamId];
                var anchorBearing = Mathf.Atan2(anchor.x - eyes.x, anchor.z - eyes.z) * Mathf.Rad2Deg;
                // Each place's card faces its own character, as the first faces its: the look decides the side only for the first.
                card.GrowsFromLeft = Mathf.DeltaAngle(lookBearing, anchorBearing) < 0f;
                card.Show(slot);
                if (!card.gameObject.activeSelf) continue;
                var popped = card.Popped;
                card.Pop(1f);
                var pose = RoomLayout.MomentSlot(new System.Numerics.Vector3(eyes.x, eyes.y, eyes.z), anchorBearing,
                    new System.Numerics.Vector3(anchor.x, anchor.y, anchor.z), half, SpatialKit.MomentDegrees, card.Size.x / card.Size.y);
                holder.gameObject.SetActive(true);
                holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                holder.localScale = Vector3.one;
                card.transform.localPosition = Vector3.zero;
                card.transform.localPosition = -holder.InverseTransformPoint(card.Carrier.Face.transform.position);
                Stand(holder, pose, pose.Width / card.Size.x);
                ShowMoreWaiting(workstreamId, holder, card, more);
                card.Pop(popped);
            }
            foreach (var gone in placeMomentHolders.Keys.Where(id => !given.Contains(id)).ToList()) HidePlaceMoment(gone);
            WirePlaceMoments();
        }

        /// <summary>The first card's "{n} More Waiting" line, under <see cref="Moment"/>; null words take it away.</summary>
        public void ShowMomentMoreWaiting(string? words)
        {
            if (Moment == null || momentHolder == null || MomentWorkstreamId == null) return;
            ShowMoreWaiting(MomentWorkstreamId, momentHolder, Moment, words);
        }

        private void ShowMoreWaiting(string key, Transform holder, KitMomentCard card, string? words)
        {
            if (!moreWaiting.TryGetValue(key, out var line) || line == null)
            {
                line = KitType.Create(holder, "More waiting", KitRole.Body, KitType.Secondary, KitType.Order.Words, TMPro.TextAlignmentOptions.TopLeft, room: true);
                line.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                moreWaiting[key] = line;
            }
            line.gameObject.SetActive(words != null);
            if (words == null) return;
            if (line.transform.parent != holder) line.transform.SetParent(holder, false);
            KitType.Size(line, KitRole.Body);
            GlazeText.SetLiteral(line, words);
            var (_, wide, tall) = KitType.Lay(line, card.Size.x, 1);
            // Under the card's content, at its left edge, a pad below (AUDIT2's 19:18Z: under the first card's content).
            var face = card.Carrier.Face.transform.localPosition + card.transform.localPosition;
            line.transform.localPosition = new Vector3(face.x - card.Size.x / 2f + wide / 2f, face.y - card.Size.y / 2f - tall / 2f - KitType.Units(SpatialKit.TargetGapDegrees), face.z);
        }

        private void WirePlaceMoments()
        {
            foreach (var pair in placeMomentHandlers) if (pair.Key != null) pair.Key.Pressed -= pair.Value;
            placeMomentHandlers.Clear();
            foreach (var (workstreamId, card) in placeMomentCards)
            {
                if (card == null || !card.gameObject.activeSelf) continue;
                foreach (var (action, button) in card.Shown)
                {
                    if (button == null) continue;
                    var (capturedAction, capturedId) = (action, workstreamId);
                    Action handler = () => MomentActed?.Invoke(capturedAction, capturedId);
                    placeMomentHandlers[button] = handler;
                    button.Pressed += handler;
                }
            }
        }

        private void HidePlaceMoment(string workstreamId)
        {
            if (placeMomentHolders.TryGetValue(workstreamId, out var holder) && holder != null)
            {
                holder.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(holder.gameObject);
                else DestroyImmediate(holder.gameObject);
            }
            placeMomentHolders.Remove(workstreamId);
            placeMomentCards.Remove(workstreamId);
            moreWaiting.Remove(workstreamId);
        }

        /// <summary>Takes every further place's card away.</summary>
        public void HidePlaceMoments()
        {
            foreach (var id in placeMomentHolders.Keys.ToList()) HidePlaceMoment(id);
            foreach (var pair in placeMomentHandlers) if (pair.Key != null) pair.Key.Pressed -= pair.Value;
            placeMomentHandlers.Clear();
        }

        /// <summary>Takes the moment away.</summary>
        public void HideMoment()
        {
            UnwireMoment();
            if (momentHolder != null)
            {
                momentHolder.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(momentHolder.gameObject);
                else DestroyImmediate(momentHolder.gameObject);
            }
            if (MomentWorkstreamId != null) moreWaiting.Remove(MomentWorkstreamId);
            (momentHolder, Moment, MomentWorkstreamId, MomentPose, MomentGrowsTowardAnchor) = (null, null, null, null, false);
        }
    }
}
