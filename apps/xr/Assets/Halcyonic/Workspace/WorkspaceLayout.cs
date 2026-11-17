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
            new PanelSize(PanelFrame.Distance, PanelFrame.UnitSize.x / 2f * PanelFrame.Scale, PanelFrame.UnitSize.y / 2f * PanelFrame.Scale);

        /// <summary>
        /// The workspace's pose for <paramref name="opened"/>, with the person's eyes at
        /// <paramref name="eyes"/> looking along <paramref name="looking"/>, every character on the
        /// stage in <paramref name="all"/>, and the surface they stand on at
        /// <paramref name="surfaceHeight"/>, if any: at touch distance, the frame's size, beside its
        /// character and clear of every other and its label. <paramref name="scratch"/> is filled and reused.
        /// </summary>
        public static (Pose Pose, PanelDirection Direction) Place(CharacterTarget opened, IEnumerable<CharacterTarget> all, Vector3 eyes,
            Vector3 looking, float? surfaceHeight, List<BodyInView> scratch) => Place(opened, all, eyes, looking, surfaceHeight, scratch, FrameSize);

        /// <summary>The same for a panel of <paramref name="size"/>, as the menu's plane is placed beside a file's character.</summary>
        public static (Pose Pose, PanelDirection Direction) Place(CharacterTarget opened, IEnumerable<CharacterTarget> all, Vector3 eyes,
            Vector3 looking, float? surfaceHeight, List<BodyInView> scratch, PanelSize size)
        {
            scratch.Clear();
            foreach (var other in all)
            {
                if (other != null) scratch.Add(InView(other, eyes));
            }
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
        /// The pose of a panel of <paramref name="size"/> centred where the person looks, never turned
        /// toward a character, and clear of every character and its label all the same: the menu's plane
        /// beside a window straight ahead, between the characters either side of it, opening under the
        /// window's lane (<see cref="CharacterStage.WindowLaneHalfHeightDegrees"/>) as under a label (ADR 0026).
        /// </summary>
        public static (Pose Pose, PanelDirection Direction) PlaceAhead(IEnumerable<CharacterTarget> all, Vector3 eyes, Vector3 looking,
            float? surfaceHeight, List<BodyInView> scratch, PanelSize size)
        {
            scratch.Clear();
            foreach (var other in all)
            {
                if (other != null) scratch.Add(InView(other, eyes));
            }
            var lookYaw = Mathf.Atan2(looking.x, looking.z) * Mathf.Rad2Deg;
            // The window's lane straight ahead, which it opens under as under a label.
            var under = -CharacterStage.WindowLaneHalfHeightDegrees;
            var ahead = new BodyInView(lookYaw, under, 0f, under, CharacterStage.WindowLaneHalfWidthDegrees);
            var direction = WorkspacePlacement.Place(lookYaw, ahead, scratch, size, surfaceHeight.HasValue ? eyes.y - surfaceHeight.Value : (float?)null, ViewField.Current);
            var forward = Quaternion.Euler(-direction.Elevation, direction.Yaw, 0f) * Vector3.forward;
            return (new Pose(eyes + forward * size.Distance, Quaternion.LookRotation(forward, Vector3.up)), direction);
        }

        /// <summary>
        /// A character as seen from the eyes: its body's direction and how far around it the body
        /// reaches, and how low and how wide its label reaches, so a panel clears both (ADR 0023).
        /// </summary>
        public static BodyInView InView(CharacterTarget target, Vector3 eyes)
        {
            // At rest: the body's hops and rises never move what is placed against it.
            var toBody = target.RestPosition - eyes;
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
            var yaw = Mathf.Atan2(toBody.x, toBody.z) * Mathf.Rad2Deg;
            // The label as drawn, the older label or the redesign's tag, whichever stands: every corner of its renderers' bounds as the
            // eyes see them, its edge stroke and a leaning plate's nearer lower corners included, which one half-width at the bottom's
            // middle misses; under the redesign the older label is hidden and the tag stands instead, which that measure never saw
            // (FIELD's read of b208's appearance, 2026-11-16). The measure above stands where it reaches further.
            // Under the redesign alone: the older views keep the label measure their layout was fitted with (showhere at Larger, 2026-11-17).
            if (RoomDesign.Enabled && DrawnLabel(view, eyes, yaw) is (float drawnHalf, float drawnLowest))
            {
                halfWidth = Mathf.Max(halfWidth, drawnHalf);
                lowest = Mathf.Min(lowest, drawnLowest);
            }
            return new BodyInView(yaw, elevation, radius, Mathf.Min(lowest, elevation - radius), Mathf.Max(halfWidth, radius));
        }

        private static readonly List<Renderer> Drawn = new();

        /// <summary>
        /// The label drawn under <paramref name="view"/> as the eyes at <paramref name="eyes"/> see it: how far either side of
        /// <paramref name="yaw"/> its bounds' corners reach and how low, in degrees; null where nothing stands there. Allocates nothing.
        /// </summary>
        private static (float HalfWidth, float Lowest)? DrawnLabel(CharacterView view, Vector3 eyes, float yaw)
        {
            if (view.Labels == null) return null;
            view.Labels.GetComponentsInChildren(false, Drawn);
            float half = 0f, low = float.PositiveInfinity;
            foreach (var renderer in Drawn)
            {
                if (!renderer.enabled) continue;
                var bounds = renderer.bounds;
                if (bounds.size.sqrMagnitude <= 0f) continue;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(corner % 2 == 0 ? bounds.min.x : bounds.max.x, corner < 4 ? bounds.min.y : bounds.max.y,
                        corner % 4 < 2 ? bounds.min.z : bounds.max.z) - eyes;
                    half = Mathf.Max(half, Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg)));
                    low = Mathf.Min(low, Mathf.Atan2(point.y, Mathf.Max(new Vector2(point.x, point.z).magnitude, 0.01f)) * Mathf.Rad2Deg);
                }
            }
            Drawn.Clear();
            return float.IsPositiveInfinity(low) ? null : (half, low);
        }
    }
}
