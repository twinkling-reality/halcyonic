#nullable enable
using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Something a hand can point at with a ray and pinch, or reach and poke, and, for characters,
    /// look at, through the Meta Interaction SDK's interactables. The SDK's ray and poke interactors
    /// are on the rig in Stage.unity, and the workspace's gaze hover adds a gaze interactor; this adds
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
        private readonly HashSet<int> pressing = new HashSet<int>();
        private BoundsClipper? clipper;
        private PokeInteractable? poke;
        private RayInteractable? ray;
        private bool drags;
        private Vector3? held;
        private float heldAlongRay;
        private string logKind = "control";
        private string? logId;

        /// <summary>A hand or the gaze started or stopped hovering.</summary>
        public event Action? HoverChanged;

        /// <summary>A pinch on the ray, or a poke that pressed through the surface.</summary>
        public event Action? Selected;

        /// <summary>
        /// A press that <see cref="Selected"/> reported ended: let go (false), or cancelled (true), as when
        /// the interactable is disabled.
        /// </summary>
        public event Action<bool>? Released;

        /// <summary>A look and pinch: the gaze interactor selected this, on a pinch <see cref="GazeHover"/> allowed.</summary>
        public event Action? GazeSelected;

        /// <summary>While a press holds it, after <see cref="EnableDrag"/>: the point the hand holds moved, to here in the world.</summary>
        public event Action<Vector3>? Dragged;

        /// <summary>The point the hand took hold of, or holds now, in the world, while a press is on it after <see cref="EnableDrag"/>.</summary>
        public Vector3? HeldPoint => held;

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
                target.ray = rayInteractable;
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

        /// <summary>
        /// How the interaction log names this target when a ray enters or leaves it: a character by
        /// its work's id; anything not named, a control by its instance.
        /// </summary>
        public void LogAs(string kind, string id)
        {
            logKind = kind;
            logId = id;
        }

        /// <summary>
        /// Lets a press drag: a ray then keeps hold of the point it hit, at that distance along it, as
        /// the Interaction SDK moves what a ray selects (its <c>MoveFromTargetProvider</c>, whose pose is
        /// the ray's origin, verified in 207.0.0's RayInteractor); a poke holds the point it touches.
        /// Every move of either raises <see cref="Dragged"/>.
        /// </summary>
        public void EnableDrag()
        {
            if (drags) return;
            drags = true;
            if (ray != null) ray.InjectOptionalMovementProvider(gameObject.AddComponent<MoveFromTargetProvider>());
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
            held = null;
            if (pressing.Count > 0)
            {
                pressing.Clear();
                Released?.Invoke(true);
            }
            if (hands.Count == 0 && gazes.Count == 0) return;
            handsOnTargets -= hands.Count;
            hands.Clear();
            gazes.Clear();
            HoverChanged?.Invoke();
        }

        private void OnHand(PointerEvent pointer) => OnPointer(pointer, fromRay: false);

        private void OnPointer(PointerEvent pointer, bool fromRay)
        {
            var before = hands.Count;
            if (Track(hands, pointer))
            {
                handsOnTargets += hands.Count - before;
                HoverChanged?.Invoke();
            }
            if (pointer.Type == PointerEventType.Select && !FocusGuard.InputSuspended)
            {
                pressing.Add(pointer.Identifier);
                if (drags) held = TakeHold(pointer.Pose, fromRay);
                Selected?.Invoke();
            }
            else if (pointer.Type == PointerEventType.Move && drags && held != null && pressing.Contains(pointer.Identifier))
            {
                var point = fromRay ? pointer.Pose.position + pointer.Pose.forward * heldAlongRay : pointer.Pose.position;
                held = point;
                Dragged?.Invoke(point);
            }
            else if (pointer.Type == PointerEventType.Unselect && pressing.Remove(pointer.Identifier))
            {
                held = null;
                Released?.Invoke(false);
            }
            else if (pointer.Type == PointerEventType.Cancel && pressing.Remove(pointer.Identifier))
            {
                held = null;
                Released?.Invoke(true);
            }
        }

        /// <summary>
        /// The point a press takes hold of: a poke's on the surface; a ray's, whose pose is its origin
        /// while it drags, where it meets this target's plane, kept at that distance along it.
        /// </summary>
        private Vector3 TakeHold(Pose pose, bool fromRay)
        {
            if (!fromRay) return pose.position;
            var normal = transform.forward;
            var across = Vector3.Dot(pose.forward, normal);
            heldAlongRay = Mathf.Abs(across) > 1e-4f ? Vector3.Dot(transform.position - pose.position, normal) / across : -1f;
            if (heldAlongRay <= 0f) heldAlongRay = Vector3.Distance(pose.position, transform.position);
            return pose.position + pose.forward * heldAlongRay;
        }

        private void OnRay(PointerEvent pointer)
        {
            var changed = Track(rays, pointer);
            OnPointer(pointer, fromRay: true);
            if (!changed) return;
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this,
                "Halcyonic interaction: ray target {0} {1} {2}",
                pointer.Type == PointerEventType.Hover ? "entered" : "left", logKind, logId ?? GetInstanceID().ToString());
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
