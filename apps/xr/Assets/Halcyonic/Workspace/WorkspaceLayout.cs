#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Where the open workspace goes, in Unity's terms: the characters as the person sees them, handed
    /// to <see cref="WorkspacePlacement"/> in the client core, and its answer turned into a pose.
    /// </summary>
    public static class WorkspaceLayout
    {
        /// <summary>
        /// About two feet from the eyes: within a seated person's reach, so the workspace's buttons
        /// can be poked without leaning or standing, and near enough to read at its scaled size.
        /// </summary>
        public const float Reach = 0.6f;

        /// <summary>The workspace is scaled to keep the angular size it was designed with.</summary>
        public static float Scale => Reach / WorkspaceVisuals.PanelDistance;

        /// <summary>
        /// The workspace's pose for <paramref name="opened"/>, with the person's eyes at
        /// <paramref name="eyes"/> looking along <paramref name="looking"/>, every character on the
        /// stage in <paramref name="all"/>, and the surface they stand on at
        /// <paramref name="surfaceHeight"/>, if any. <paramref name="scratch"/> is filled and reused.
        /// </summary>
        public static (Pose Pose, PanelDirection Direction) Place(CharacterTarget opened, IEnumerable<CharacterTarget> all, Vector3 eyes,
            Vector3 looking, float? surfaceHeight, List<BodyInView> scratch)
        {
            scratch.Clear();
            foreach (var other in all)
            {
                if (other != null) scratch.Add(InView(other, eyes));
            }
            var size = new PanelSize(Reach, WorkspacePanel.Width / 2f * Scale, WorkspacePanel.Height / 2f * Scale);
            var direction = WorkspacePlacement.Place(Mathf.Atan2(looking.x, looking.z) * Mathf.Rad2Deg, InView(opened, eyes), scratch, size,
                surfaceHeight.HasValue ? eyes.y - surfaceHeight.Value : (float?)null);
            var forward = Quaternion.Euler(-direction.Elevation, direction.Yaw, 0f) * Vector3.forward;
            return (new Pose(eyes + forward * Reach, Quaternion.LookRotation(forward, Vector3.up)), direction);
        }

        /// <summary>
        /// The pose of a panel of the workspace's size that belongs to no character, such as the
        /// entry panel: where the person looks, and clear of every character's body as the workspace
        /// is. It is placed as if beside the character nearest where the person looks, so it goes
        /// below the characters 2.4 m away and above a desk lineup; with no characters it opens 15
        /// degrees below eye level.
        /// </summary>
        public static (Pose Pose, PanelDirection Direction) PlaceForeground(IEnumerable<CharacterTarget> all, Vector3 eyes, Vector3 looking,
            float? surfaceHeight, List<BodyInView> scratch)
        {
            scratch.Clear();
            var lookYaw = Mathf.Atan2(looking.x, looking.z) * Mathf.Rad2Deg;
            var size = new PanelSize(Reach, WorkspacePanel.Width / 2f * Scale, WorkspacePanel.Height / 2f * Scale);
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
            var direction = WorkspacePlacement.Place(lookYaw, opened, scratch, size, surfaceHeight.HasValue ? eyes.y - surfaceHeight.Value : (float?)null);
            var forward = Quaternion.Euler(-direction.Elevation, direction.Yaw, 0f) * Vector3.forward;
            return (new Pose(eyes + forward * Reach, Quaternion.LookRotation(forward, Vector3.up)), direction);
        }

        /// <summary>A character's body as seen from the eyes: its direction, and how far around it the body reaches.</summary>
        public static BodyInView InView(CharacterTarget target, Vector3 eyes)
        {
            var toBody = target.BodyPosition - eyes;
            var level = new Vector2(toBody.x, toBody.z).magnitude;
            var reach = CharacterView.BodyExtent * target.Scale / Mathf.Max(toBody.magnitude, 0.05f);
            return new BodyInView(
                Mathf.Atan2(toBody.x, toBody.z) * Mathf.Rad2Deg,
                Mathf.Atan2(toBody.y, Mathf.Max(level, 0.01f)) * Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp01(reach)) * Mathf.Rad2Deg);
        }
    }
}
