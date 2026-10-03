#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// The menu's plane held by a file's subject and dragged (ADR 0026): the whole plane turns round the
    /// eyes with the point the hand took hold of, at its distance and still facing them
    /// (<see cref="PanelDrag"/>), as an offset on where the stage placed it, which the next hold starts
    /// from and Reset position clears. A step that would take the plane outside the headset's measured
    /// field, in front of a character's body or label, or where the host's own condition fails, as the
    /// light line crossing another character, is not taken: the plane stays where it last was, so a drag
    /// never leaves it where placement would not.
    /// </summary>
    public sealed class MenuDrag
    {
        private readonly PanelDrag drag;
        private readonly PanelDirection placed;
        private readonly PlaneComposition composition;
        private readonly IReadOnlyList<BodyInView> bodies;
        private readonly ViewField? field;
        private readonly Func<(float Yaw, float Elevation), bool>? holds;

        /// <param name="placed">Where the stage placed the plane's centre.</param>
        /// <param name="moved">How far it was dragged from there before this hold, yaw to the right and elevation up, in degrees.</param>
        /// <param name="grabYaw">The point the hand took hold of, as seen from the eyes.</param>
        /// <param name="composition">The plane as laid.</param>
        /// <param name="bodies">Every character on the stage, as seen from the eyes.</param>
        /// <param name="surfaceDrop">How far below the eyes the surface under the characters is, in meters, when they stand on one.</param>
        /// <param name="field">The headset's measured field of view; null where none is measured, as in the editor.</param>
        /// <param name="holds">What else must hold with the plane moved so far, last of all, as its light line crossing no other character; null for nothing.</param>
        public MenuDrag(PanelDirection placed, (float Yaw, float Elevation) moved, float grabYaw, float grabElevation, PlaneComposition composition,
            IReadOnlyList<BodyInView> bodies, float? surfaceDrop = null, ViewField? field = null, Func<(float Yaw, float Elevation), bool>? holds = null)
        {
            this.placed = placed;
            this.composition = composition;
            this.bodies = bodies;
            this.field = field;
            this.holds = holds;
            Moved = moved;
            var at = Turned(placed, moved);
            drag = new PanelDrag(at.Yaw, at.Elevation, grabYaw, grabElevation, composition.Size, surfaceDrop, field);
        }

        /// <summary>How far the plane stands from where the stage placed it, yaw to the right and elevation up, in degrees.</summary>
        public (float Yaw, float Elevation) Moved { get; private set; }

        /// <summary>Where the plane's centre stands now.</summary>
        public PanelDirection At => Turned(placed, Moved);

        /// <summary>
        /// The held point moved to <paramref name="yaw"/> and <paramref name="elevation"/>, as seen from the
        /// eyes: the plane follows, unless where it would go leaves the field or stands in front of a body
        /// or a label. True when it moved.
        /// </summary>
        public bool Follow(float yaw, float elevation)
        {
            var (toYaw, toElevation) = drag.Follow(yaw, elevation);
            var moved = (WorkspacePlacement.DeltaAngle(placed.Yaw, toYaw), toElevation - placed.Elevation);
            if (moved == Moved) return false;
            var to = Turned(placed, moved);
            if (!WorkspacePlacement.Clears(to, bodies, composition.Size)) return false;
            if (field is ViewField measured && !MenuPage.Inside(composition, to, measured)) return false;
            if (holds != null && !holds(moved)) return false;
            Moved = moved;
            return true;
        }

        /// <summary>
        /// As much of <paramref name="moved"/> as <paramref name="allows"/> takes, as the plane is laid
        /// anew under a drag's offset (a side panel opening, larger text, a taller subject): the whole of
        /// it, else scaled back toward the stage's placement a tenth at a time, else none, so a drag's
        /// rules hold on every later draw, not only while the hand moves.
        /// </summary>
        public static (float Yaw, float Elevation) Kept((float Yaw, float Elevation) moved, Func<(float Yaw, float Elevation), bool> allows)
        {
            if (moved == default) return default;
            for (var tenths = 10; tenths > 0; tenths--)
            {
                var scaled = (moved.Yaw * tenths / 10f, moved.Elevation * tenths / 10f);
                if (allows(scaled)) return scaled;
            }
            return default;
        }

        /// <summary><paramref name="placed"/> moved by <paramref name="moved"/>: where a drag left the plane's centre.</summary>
        public static PanelDirection Turned(PanelDirection placed, (float Yaw, float Elevation) moved) =>
            new PanelDirection(WorkspacePlacement.DeltaAngle(0f, placed.Yaw + moved.Yaw), placed.Elevation + moved.Elevation, placed.Clear, placed.Above);
    }
}
