#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Draws a laid-out plane on the workspace's one gentle curve (ADR 0034, <see cref="PlaneCurve"/>): layout stays
    /// flat, and every shape and label under this object is drawn where the curve puts it. The curve is a vertical
    /// cylinder about the plane's own up through the eyes where the plane was placed, so it moves with the plane and
    /// never with the head. Shapes become strips of <see cref="ColumnDegrees"/> columns (<see cref="Surface"/>);
    /// labels bend in their one ordered hook, after any lean (<see cref="GlazeRuns"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="Enabled"/> is the one switch between the curve and the plane, the fallback. It is on: the plane's
    /// targets lie on the same curve (<c>PointerTarget.DrawOn</c>), so the ray and the poke meet what is drawn.
    /// </remarks>
    public sealed class CurvedDrawing : MonoBehaviour
    {
        /// <summary>Whether planes draw on the curve, as they do unless switched off; off, everything draws flat as laid out.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>The widest column a bent shape takes: its chord sags 0.018 mm at 0.46 m, far under a pixel.</summary>
        public const float ColumnDegrees = 1f;

        private readonly PlaneCurve curve = new PlaneCurve(PlaneComposition.Distance);
        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private Vector3 eyes;
        private Quaternion facing = Quaternion.identity;
        private bool placed;
        private bool drawnOn;

        /// <summary>Whether this plane draws on the curve now: switched on and placed.</summary>
        public bool On => Enabled && placed;

        /// <summary>The curve's own mapping, in meters from the eyes.</summary>
        public PlaneCurve Curve => curve;

        /// <summary>Changes whenever the curve is placed anew, so anything bent for an earlier place bends again.</summary>
        public int Version { get; private set; }

        /// <summary>Where the eyes stood when the plane was placed: the curve's axis runs through them.</summary>
        public Vector3 Eyes => eyes;

        /// <summary>The plane's rotation where it was placed: its forward from the eyes, its up the curve's axis.</summary>
        public Quaternion Facing => facing;

        /// <summary>
        /// Places the curve: its axis through <paramref name="eyes"/> along <paramref name="facing"/>'s up, the flat
        /// plane <see cref="PlaneComposition.Distance"/> ahead along its forward, as <see cref="PlaneLayout"/> lays it.
        /// </summary>
        public void Place(Vector3 eyes, Quaternion facing)
        {
            if (placed && eyes == this.eyes && facing == this.facing) return;
            this.eyes = eyes;
            this.facing = facing;
            placed = true;
            Version++;
        }

        /// <summary>
        /// Where the curve draws a point the plane lays out at <paramref name="world"/>. A point the curve cannot hold,
        /// at or behind the eyes or past half a turn round them, draws where it was laid out rather than stop a frame.
        /// </summary>
        public Vector3 Bend(Vector3 world)
        {
            var local = Quaternion.Inverse(facing) * (world - eyes);
            if (!(local.z > 0f) || !(Mathf.Abs(local.x) < Mathf.PI * curve.Radius)) return world;
            var bent = curve.Bend(new System.Numerics.Vector3(local.x, local.y, local.z));
            return eyes + facing * new Vector3(bent.X, bent.Y, bent.Z);
        }

        /// <summary>Where the plane laid out a point the curve draws at <paramref name="world"/>: the inverse of <see cref="Bend"/>.</summary>
        public Vector3 Straighten(Vector3 world)
        {
            var local = Quaternion.Inverse(facing) * (world - eyes);
            var flat = curve.Unmap(new System.Numerics.Vector3(local.x, local.y, local.z));
            return eyes + facing * new Vector3(flat.X, flat.Y, flat.Z + curve.Radius);
        }

        /// <summary>
        /// Draws every shown shape and label under this object on the curve, or flat again once the switch is off;
        /// whatever has not changed since it was last bent is left alone. Anything under <paramref name="bodies"/>,
        /// a character standing on the plane, is a body rather than a page and is left to its owner.
        /// </summary>
        public void Refresh(IReadOnlyList<Transform>? bodies = null)
        {
            var on = On;
            if (!on && !drawnOn) return;
            GetComponentsInChildren(false, surfaces);
            foreach (var surface in surfaces)
                if (!Within(surface.transform, bodies)) surface.DrawOn(on ? this : null);
            GetComponentsInChildren(false, labels);
            foreach (var label in labels)
                if (!Within(label.transform, bodies)) GlazeRuns.Of(label).DrawOn(on ? this : null);
            drawnOn = on;
        }

        /// <summary>What the plane draws now, for the renders' records: shapes, their vertices with rims, labels with words, and visible glyphs.</summary>
        public (int Surfaces, int SurfaceVertices, int Labels, int Glyphs) Counts(IReadOnlyList<Transform>? bodies = null)
        {
            int shapes = 0, vertices = 0, words = 0, glyphs = 0;
            GetComponentsInChildren(false, surfaces);
            foreach (var surface in surfaces)
            {
                if (Within(surface.transform, bodies)) continue;
                shapes++;
                vertices += surface.DrawnVertexCount;
            }
            GetComponentsInChildren(false, labels);
            foreach (var label in labels)
            {
                if (Within(label.transform, bodies) || string.IsNullOrEmpty(label.text) || label.textInfo == null) continue;
                words++;
                for (var index = 0; index < label.textInfo.characterCount; index++)
                    if (label.textInfo.characterInfo[index].isVisible) glyphs++;
            }
            return (shapes, vertices, words, glyphs);
        }

        private static bool Within(Transform transform, IReadOnlyList<Transform>? bodies)
        {
            if (bodies == null) return false;
            foreach (var body in bodies)
                if (body != null && transform.IsChildOf(body)) return true;
            return false;
        }
    }
}
