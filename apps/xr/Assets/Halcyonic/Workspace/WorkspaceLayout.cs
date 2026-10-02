#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Where the open workspace goes, in Unity's terms: the characters as the person sees them, handed
    /// to <see cref="WorkspacePlacement"/> in the client core, and its answer turned into a pose.
    /// </summary>
    public static class WorkspaceLayout
    {
        /// <summary>A frame's size as placement takes it: at touch distance, half its width and height in meters.</summary>
        public static PanelSize FrameSize =>
            new PanelSize(PanelFrame.Distance, PanelFrame.UnitSize.x / 2f * PanelFrame.Distance, PanelFrame.UnitSize.y / 2f * PanelFrame.Distance);

        /// <summary>
        /// The workspace's pose for <paramref name="opened"/>, with the person's eyes at
        /// <paramref name="eyes"/> looking along <paramref name="looking"/>, every character on the
        /// stage in <paramref name="all"/>, and the surface they stand on at
        /// <paramref name="surfaceHeight"/>, if any: at touch distance, the frame's size, beside its
        /// character and clear of every other and its label. <paramref name="scratch"/> is filled and reused.
        /// </summary>
        public static (Pose Pose, PanelDirection Direction) Place(CharacterTarget opened, IEnumerable<CharacterTarget> all, Vector3 eyes,
            Vector3 looking, float? surfaceHeight, List<BodyInView> scratch)
        {
            scratch.Clear();
            foreach (var other in all)
            {
                if (other != null) scratch.Add(InView(other, eyes));
            }
            var size = FrameSize;
            var direction = WorkspacePlacement.Place(Mathf.Atan2(looking.x, looking.z) * Mathf.Rad2Deg, InView(opened, eyes), scratch, size,
                surfaceHeight.HasValue ? eyes.y - surfaceHeight.Value : (float?)null, ViewField.Current);
            var forward = Quaternion.Euler(-direction.Elevation, direction.Yaw, 0f) * Vector3.forward;
            return (new Pose(eyes + forward * size.Distance, Quaternion.LookRotation(forward, Vector3.up)), direction);
        }

        /// <summary>
        /// The pose of a panel that belongs to no character, such as the entry panel or Usage left, of
        /// the size it gives: where the person looks, and clear of every character and its label as
        /// the workspace is. It is placed as if beside the character nearest where the person looks,
        /// so it goes below the labels of the characters 2.4 m away and above a desk lineup; with no
        /// characters it opens 15 degrees below eye level.
        /// </summary>
        public static (Pose Pose, PanelDirection Direction) PlaceForeground(IEnumerable<CharacterTarget> all, Vector3 eyes, Vector3 looking,
            float? surfaceHeight, List<BodyInView> scratch, PanelSize size)
        {
            scratch.Clear();
            var lookYaw = Mathf.Atan2(looking.x, looking.z) * Mathf.Rad2Deg;
            BodyInView? nearest = null;
            foreach (var other in all)
            {
                if (other == null) continue;
                var body = InView(other, eyes);
                scratch.Add(body);
                if (nearest == null || Mathf.Abs(Mathf.DeltaAngle(lookYaw, body.Yaw)) < Mathf.Abs(Mathf.DeltaAngle(lookYaw, nearest.Value.Yaw))) nearest = body;
            }
            // Without characters, a point just above where its upper edge would be at rest: it opens below it.
            var opened = nearest ?? new BodyInView(lookYaw, WorkspacePlacement.NaturalDegrees + size.HalfHeightDegrees + WorkspacePlacement.ClearanceDegrees, 0f);
            var direction = WorkspacePlacement.Place(lookYaw, opened, scratch, size, surfaceHeight.HasValue ? eyes.y - surfaceHeight.Value : (float?)null, ViewField.Current);
            var forward = Quaternion.Euler(-direction.Elevation, direction.Yaw, 0f) * Vector3.forward;
            return (new Pose(eyes + forward * size.Distance, Quaternion.LookRotation(forward, Vector3.up)), direction);
        }

        /// <summary>
        /// A character as seen from the eyes: its body's direction and how far around it the body
        /// reaches, and how low and how wide its label reaches, so a panel clears both (ADR 0023).
        /// </summary>
        public static BodyInView InView(CharacterTarget target, Vector3 eyes)
        {
            var toBody = target.BodyPosition - eyes;
            var level = new Vector2(toBody.x, toBody.z).magnitude;
            var reach = CharacterView.BodyExtent * target.Scale / Mathf.Max(toBody.magnitude, 0.05f);
            var elevation = Mathf.Atan2(toBody.y, Mathf.Max(level, 0.01f)) * Mathf.Rad2Deg;
            var radius = Mathf.Asin(Mathf.Clamp01(reach)) * Mathf.Rad2Deg;
            // The label hangs under the character's place, not its body, which rises and hops.
            var view = target.View;
            var scale = view.transform.lossyScale.y;
            var toBottom = view.transform.position + Vector3.up * (view.LabelBottom * scale) - eyes;
            var bottomLevel = Mathf.Max(new Vector2(toBottom.x, toBottom.z).magnitude, 0.01f);
            var lowest = Mathf.Atan2(toBottom.y, bottomLevel) * Mathf.Rad2Deg;
            var halfWidth = Mathf.Atan2(view.LabelHalfWidth * scale, bottomLevel) * Mathf.Rad2Deg;
            return new BodyInView(Mathf.Atan2(toBody.x, toBody.z) * Mathf.Rad2Deg, elevation, radius,
                Mathf.Min(lowest, elevation - radius), Mathf.Max(halfWidth, radius));
        }
    }
}
