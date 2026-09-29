#nullable enable
using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Something a hand can point at with a ray and pinch, or reach and poke, through the Meta
    /// Interaction SDK's interactables. The SDK's ray and poke interactors are on the rig in
    /// Stage.unity; this adds the targets they look for. The SDK components check their
    /// dependencies in Start, so they are injected right after being added, in the same frame.
    /// Pointer events are ignored while <see cref="FocusGuard.InputSuspended"/>.
    /// </summary>
    public sealed class PointerTarget : MonoBehaviour
    {
        private readonly HashSet<int> hovering = new HashSet<int>();
        private BoundsClipper? clipper;

        /// <summary>A pointer started hovering, or the last one stopped.</summary>
        public event Action<bool>? HoverChanged;

        /// <summary>A pinch on the ray, or a poke that pressed through the surface.</summary>
        public event Action? Selected;

        public bool Hovered => hovering.Count > 0;

        /// <summary>
        /// A flat rectangle on <paramref name="host"/>'s XY plane, facing the way text does (the
        /// host's -Z, toward the person): pointable with a ray, pokeable, or both.
        /// </summary>
        public static PointerTarget Rectangle(GameObject host, Vector2 size, bool ray, bool poke)
        {
            var target = host.AddComponent<PointerTarget>();
            // Facing Backward, the plane's normal is -Z: toward the person.
            var plane = host.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            target.clipper = host.AddComponent<BoundsClipper>();
            target.Resize(size);
            var patch = host.AddComponent<ClippedPlaneSurface>();
            patch.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { target.clipper });
            if (ray)
            {
                var rayInteractable = host.AddComponent<RayInteractable>();
                rayInteractable.InjectAllRayInteractable(patch);
                rayInteractable.WhenPointerEventRaised += target.OnPointer;
            }
            if (poke)
            {
                var pokeInteractable = host.AddComponent<PokeInteractable>();
                pokeInteractable.InjectAllPokeInteractable(patch);
                pokeInteractable.WhenPointerEventRaised += target.OnPointer;
            }
            return target;
        }

        /// <summary>A sphere that a ray can point at; a poke needs a flat surface, so it gets a <see cref="Rectangle"/>.</summary>
        public static PointerTarget Sphere(GameObject host, float radius)
        {
            var target = host.AddComponent<PointerTarget>();
            // ColliderSurface raycasts this collider alone. It has no rigidbody, so it never moves
            // anything, like the collider a primitive body already carries.
            var collider = host.AddComponent<SphereCollider>();
            collider.radius = radius;
            var surface = host.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(collider);
            var ray = host.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);
            ray.WhenPointerEventRaised += target.OnPointer;
            return target;
        }

        public void Resize(Vector2 size)
        {
            if (clipper != null) clipper.Size = new Vector3(size.x, size.y, 0.1f);
        }

        private void OnDisable()
        {
            // A disabled interactable cancels its pointers; forget them so no hover outlives it.
            if (hovering.Count == 0) return;
            hovering.Clear();
            HoverChanged?.Invoke(false);
        }

        private void OnPointer(PointerEvent pointer)
        {
            switch (pointer.Type)
            {
                case PointerEventType.Hover:
                    if (FocusGuard.InputSuspended) return;
                    if (hovering.Add(pointer.Identifier) && hovering.Count == 1) HoverChanged?.Invoke(true);
                    break;
                case PointerEventType.Unhover:
                case PointerEventType.Cancel:
                    if (hovering.Remove(pointer.Identifier) && hovering.Count == 0) HoverChanged?.Invoke(false);
                    break;
                case PointerEventType.Select:
                    if (FocusGuard.InputSuspended) return;
                    Selected?.Invoke();
                    break;
            }
        }
    }
}
