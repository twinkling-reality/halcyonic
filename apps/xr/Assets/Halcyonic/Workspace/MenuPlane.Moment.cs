#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuPlane
    {
        private Transform? momentHolder;

        /// <summary>The task's moment beside its character or task mark (ADR 0040, rule 4), while one is shown.</summary>
        public KitMomentCard? Moment { get; private set; }

        /// <summary>Where <see cref="RoomLayout.MomentSlot"/> stood the moment, in the room's metres.</summary>
        public RoomPose? MomentPose { get; private set; }

        /// <summary>Whether the card's growing lower corner is on the side of what it comes out of.</summary>
        public bool MomentGrowsTowardAnchor { get; private set; }

        /// <summary>
        /// Shows <paramref name="slot"/>'s moment in the one slot beside <paramref name="anchor"/>: the task's character
        /// at rest, or the task mark standing in its place, which reaches <paramref name="anchorHalfDegrees"/> either side
        /// with its label (<see cref="BodyInView.HalfWidth"/>). The slot is on its side toward
        /// <paramref name="lookBearing"/>, <see cref="SpatialKit.MomentDegrees"/> wide, as high as the card's words need,
        /// scaled by its true distance. The kit draws and pops the card; this only stands it.
        /// </summary>
        public void ShowMoment(KitMomentSlot slot, Vector3 eyes, float lookBearing, Vector3 anchor, float anchorHalfDegrees)
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
                (MomentPose, MomentGrowsTowardAnchor) = (null, false);
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
            MomentPose = pose;
            Moment.Pop(popped);
        }

        /// <summary>Takes the moment away.</summary>
        public void HideMoment()
        {
            if (momentHolder != null)
            {
                momentHolder.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(momentHolder.gameObject);
                else DestroyImmediate(momentHolder.gameObject);
            }
            (momentHolder, Moment, MomentPose, MomentGrowsTowardAnchor) = (null, null, null, false);
        }
    }
}
