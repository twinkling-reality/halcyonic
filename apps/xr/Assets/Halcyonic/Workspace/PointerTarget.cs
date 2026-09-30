#nullable enable
using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Something a hand can point at with a ray and pinch, or reach and poke, and, for characters,
    /// look at, through the Meta Interaction SDK's interactables. The SDK's ray and poke interactors
    /// are on the rig in Stage.unity, and <see cref="GazeHover"/> adds a gaze interactor; this adds
    /// the targets they look for. The SDK components check their dependencies in Start, so they are
    /// injected right after being added, in the same frame. Pointer events are ignored while
    /// <see cref="FocusGuard.InputSuspended"/>.
    /// </summary>
    public sealed class PointerTarget : MonoBehaviour
    {
        /// <summary>Hand pointers on any target, counted across all of them.</summary>
        private static int handsOnTargets;

        private readonly HashSet<int> hands = new HashSet<int>();
        private readonly HashSet<int> rays = new HashSet<int>();
        private readonly HashSet<int> gazes = new HashSet<int>();
        private BoundsClipper? clipper;
        private PokeInteractable? poke;

        /// <summary>A hand or the gaze started or stopped hovering.</summary>
        public event Action? HoverChanged;

        /// <summary>A pinch on the ray, or a poke that pressed through the surface.</summary>
        public event Action? Selected;

        /// <summary>A look and pinch: the gaze interactor selected this, on a pinch <see cref="GazeHover"/> allowed.</summary>
        public event Action? GazeSelected;

        /// <summary>A hand ray or a finger is on some target: a character, the workspace, a button.</summary>
        public static bool AnyHandOnTarget => handsOnTargets > 0;

        /// <summary>A hand ray or a finger is on it.</summary>
        public bool HandHovered => hands.Count > 0;

        /// <summary>The person is looking at it.</summary>
        public bool GazeHovered => gazes.Count > 0;

        public bool Hovered => HandHovered || GazeHovered;

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
                rayInteractable.WhenPointerEventRaised += target.OnRay;
            }
            if (poke)
            {
                var pokeInteractable = host.AddComponent<PokeInteractable>();
                pokeInteractable.InjectAllPokeInteractable(patch);
                pokeInteractable.WhenPointerEventRaised += target.OnHand;
                target.poke = pokeInteractable;
            }
            return target;
        }

        /// <summary>
        /// A sphere that a ray can point at and, with <paramref name="gaze"/>, the gaze can rest on;
        /// a poke needs a flat surface, so it gets a <see cref="Rectangle"/>.
        /// </summary>
        public static PointerTarget Sphere(GameObject host, float radius, bool gaze)
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
            ray.WhenPointerEventRaised += target.OnRay;
            if (gaze)
            {
                var gazeInteractable = host.AddComponent<GazeInteractable>();
                gazeInteractable.InjectAllGazeInteractable(surface);
                gazeInteractable.WhenPointerEventRaised += target.OnGaze;
            }
            return target;
        }

        public void Resize(Vector2 size)
        {
            if (clipper != null) clipper.Size = new Vector3(size.x, size.y, 0.1f);
        }

        /// <summary>How close, along the surface's normal, a fingertip hovers the poke and stops hovering it, in meters.</summary>
        public void SetPokeReach(float enter, float exit)
        {
            if (poke == null) return;
            poke.EnterHoverNormal = enter;
            poke.ExitHoverNormal = exit;
        }

        private void OnDisable()
        {
            // A disabled interactable cancels its pointers; forget them so no hover outlives it.
            rays.Clear();
            if (hands.Count == 0 && gazes.Count == 0) return;
            handsOnTargets -= hands.Count;
            hands.Clear();
            gazes.Clear();
            HoverChanged?.Invoke();
        }

        private void OnHand(PointerEvent pointer)
        {
            var before = hands.Count;
            if (Track(hands, pointer))
            {
                handsOnTargets += hands.Count - before;
                HoverChanged?.Invoke();
            }
            if (pointer.Type == PointerEventType.Select && !FocusGuard.InputSuspended) Selected?.Invoke();
        }

        private void OnRay(PointerEvent pointer)
        {
            var changed = Track(rays, pointer);
            OnHand(pointer);
            if (!changed) return;
            var character = GetComponent<CharacterTarget>();
            var kind = character != null ? "character" : "control";
            var id = character != null ? character.WorkstreamId : GetInstanceID().ToString();
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                "Halcyonic interaction: ray target {0} {1} {2}",
                pointer.Type == PointerEventType.Hover ? "entered" : "left", kind, id);
        }

        private void OnGaze(PointerEvent pointer)
        {
            if (Track(gazes, pointer)) HoverChanged?.Invoke();
            if (pointer.Type == PointerEventType.Select && !FocusGuard.InputSuspended) GazeSelected?.Invoke();
        }

        /// <summary>Records a hover or its end; returns whether that changed anything.</summary>
        private static bool Track(HashSet<int> pointers, PointerEvent pointer) => pointer.Type switch
        {
            PointerEventType.Hover => !FocusGuard.InputSuspended && pointers.Add(pointer.Identifier),
            PointerEventType.Unhover => pointers.Remove(pointer.Identifier),
            PointerEventType.Cancel => pointers.Remove(pointer.Identifier),
            _ => false,
        };

        /// <summary>Forgets the count when play mode starts without a domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => handsOnTargets = 0;
    }
}
