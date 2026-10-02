#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// A foreground panel moved by hand (ADR 0023): while Move is held, the panel turns round the eyes
    /// with the point the hand took hold of, keeping its distance, so it stays at touch distance and
    /// facing the person, and its center stays in the comfortable band, inside the headset's measured
    /// field and above any surface under the characters (<see cref="WorkspacePlacement"/>). Angles are in degrees from the eyes: yaw to the
    /// right, elevation up.
    /// </summary>
    public sealed class PanelDrag
    {
        private readonly float panelYaw;
        private readonly float panelElevation;
        private readonly float grabYaw;
        private readonly float grabElevation;
        private readonly float floor;

        /// <param name="panelYaw">The panel's center when the hold began.</param>
        /// <param name="grabYaw">The point the hand took hold of, then.</param>
        /// <param name="surfaceDrop">How far below the eyes the surface under the characters is, in meters, when they stand on one.</param>
        /// <param name="field">The headset's measured field of view, which the panel stays inside (<see cref="WorkspacePlacement.Lowest"/>).</param>
        public PanelDrag(float panelYaw, float panelElevation, float grabYaw, float grabElevation, PanelSize size, float? surfaceDrop = null,
            ViewField? field = null)
        {
            this.panelYaw = panelYaw;
            this.panelElevation = panelElevation;
            this.grabYaw = grabYaw;
            this.grabElevation = grabElevation;
            floor = WorkspacePlacement.Lowest(size, field);
            if (surfaceDrop.HasValue) floor = Math.Max(floor, WorkspacePlacement.LowestAboveSurface(size, surfaceDrop.Value));
        }

        /// <summary>Where the panel's center goes with the held point at <paramref name="yaw"/> and <paramref name="elevation"/>.</summary>
        public (float Yaw, float Elevation) Follow(float yaw, float elevation)
        {
            var turned = panelYaw + WorkspacePlacement.DeltaAngle(grabYaw, yaw);
            var raised = panelElevation + (elevation - grabElevation);
            return (WorkspacePlacement.DeltaAngle(0f, turned), Math.Clamp(raised, Math.Min(floor, WorkspacePlacement.HighestDegrees), WorkspacePlacement.HighestDegrees));
        }
    }
}
