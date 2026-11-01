#nullable enable
using System;
using System.Collections.Generic;
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
            (momentHolder, Moment, MomentWorkstreamId, MomentPose, MomentGrowsTowardAnchor) = (null, null, null, null, false);
        }
    }
}
