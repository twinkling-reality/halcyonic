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
        private ClippedPlaneSurface? flatPatch;
        private CylinderClipper? curveClipper;
        private ClippedCylinderSurface? curvePatch;
        private ISurfacePatch? patch;
        private Vector2 size;
        private PokeInteractable? poke;
        private RayInteractable? ray;
        private bool drags;
        private Vector3? held;

        /// <summary>
        /// The pointer whose press a dragging target follows: another's press meanwhile is no press, only its
        /// moves drag, and only its release ends the press, so a second hand never orphans or ends a hold.
        /// </summary>
        private int? holder;
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

        /// <summary>The rectangle's size, as laid out on its host's plane; zero for a sphere.</summary>
        public Vector2 Size => size;

        /// <summary>Whether this is a rectangle, which can lie on its plane's curve.</summary>
        public bool IsRectangle => flatPatch != null;

        /// <summary>Whether the ray and the poke meet it on its plane's curve now, rather than on its flat rectangle.</summary>
        public bool OnCurve => curvePatch != null && ReferenceEquals(patch, curvePatch);

        /// <summary>The patch of the curve the rectangle lies on, once it has lain there, for checks.</summary>
        public ClippedCylinderSurface? CurvedPatch => curvePatch;

        /// <summary>The flat rectangle's patch, for checks; null for a sphere.</summary>
        public ClippedPlaneSurface? FlatPatch => flatPatch;

        /// <summary>The surface the ray meets, and the patch the poke meets, now, for checks.</summary>
        public (ISurface? Ray, ISurfacePatch? Poke) Meets => (ray?.Surface, poke?.SurfacePatch);

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
            target.flatPatch = patch;
            target.patch = patch;
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
            this.size = size;
            if (clipper != null) clipper.Size = new Vector3(size.x, size.y, 0.1f);
        }

        /// <summary>
        /// Lays the rectangle on <paramref name="curve"/>'s curve while it is on, as the plane draws it there (ADR 0034):
        /// the ray and the poke then meet the patch of <paramref name="cylinder"/> that the laid-out rectangle bends onto,
        /// centred at its centre's arc, as wide as its width's arc and as high as it is. With no curve on, they meet the
        /// flat rectangle again. Called by the plane after each step; presses and their rules are the same either way.
        /// </summary>
        public void DrawOn(CurvedDrawing? curve, CylinderSurface? cylinder)
        {
            if (flatPatch == null) return;
            if (curve == null || !curve.On || cylinder == null)
            {
                Meet(flatPatch);
                return;
            }
            if (curvePatch == null)
            {
                curveClipper = gameObject.AddComponent<CylinderClipper>();
                curvePatch = gameObject.AddComponent<ClippedCylinderSurface>();
                curvePatch.InjectAllClippedCylinderSurface(cylinder, new ICylinderClipper[] { curveClipper });
            }
            else if (!ReferenceEquals(curvePatch.BackingSurface, cylinder)) curvePatch.InjectCylinderSurface(cylinder);
            // The rectangle as laid out, in the curve's frame: x across the flat plane, y up its axis.
            float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            var toCurve = Quaternion.Inverse(curve.Facing);
            foreach (var x in new[] { -0.5f, 0.5f })
            foreach (var y in new[] { -0.5f, 0.5f })
            {
                var laid = toCurve * (transform.TransformPoint(new Vector3(size.x * x, size.y * y, 0f)) - curve.Eyes);
                left = Mathf.Min(left, laid.x); right = Mathf.Max(right, laid.x);
                bottom = Mathf.Min(bottom, laid.y); top = Mathf.Max(top, laid.y);
            }
            var segment = curve.Curve.Segment(left, right, bottom, top);
            curveClipper!.Rotation = segment.RotationDegrees;
            curveClipper.ArcDegrees = segment.ArcDegrees;
            curveClipper.Bottom = segment.Bottom;
            curveClipper.Top = segment.Top;
            Meet(curvePatch);
        }

        /// <summary>Has the ray and the poke meet <paramref name="next"/>; the interactables read it on every query.</summary>
        private void Meet(ISurfacePatch next)
        {
            if (ReferenceEquals(patch, next)) return;
            patch = next;
            if (ray != null)
            {
                ray.InjectSurface(next);
                ray.InjectOptionalSelectSurface(next);
            }
            if (poke != null) poke.InjectSurfacePatch(next);
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
            holder = null;
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
                // A target that drags follows one press at a time.
                if (drags && holder != null) return;
                pressing.Add(pointer.Identifier);
                if (drags)
                {
                    holder = pointer.Identifier;
                    held = TakeHold(pointer.Pose, fromRay);
                }
                Selected?.Invoke();
            }
            else if (pointer.Type == PointerEventType.Move && drags && held != null && holder == pointer.Identifier)
            {
                var point = fromRay ? pointer.Pose.position + pointer.Pose.forward * heldAlongRay : pointer.Pose.position;
                held = point;
                Dragged?.Invoke(point);
            }
            else if (pointer.Type == PointerEventType.Unselect && pressing.Remove(pointer.Identifier))
            {
                LetGo(pointer.Identifier);
                Released?.Invoke(false);
            }
            else if (pointer.Type == PointerEventType.Cancel && pressing.Remove(pointer.Identifier))
            {
                LetGo(pointer.Identifier);
                Released?.Invoke(true);
            }
        }

        /// <summary>A press ended: the point held is let go of, and for a target that drags, its one press.</summary>
        private void LetGo(int pointer)
        {
            held = null;
            if (holder == pointer) holder = null;
        }

        /// <summary>For the editor's renders, which have no hands: a finger's pointer event at <paramref name="at"/>, as the SDK raises it.</summary>
        public void PointerForRender(int identifier, PointerEventType type, Vector3 at)
        {
            OnPointer(new PointerEvent(identifier, type, new Pose(at, Quaternion.identity)), fromRay: false);
        }

        /// <summary>
        /// The point a press takes hold of: a poke's on the surface; a ray's, whose pose is its origin
        /// while it drags, where it meets this target's plane, or its patch of the curve while it lies on
        /// one, kept at that distance along it.
        /// </summary>
        private Vector3 TakeHold(Pose pose, bool fromRay)
        {
            if (!fromRay) return pose.position;
            if (OnCurve && curvePatch!.Raycast(new Ray(pose.position, pose.forward), out var hit))
            {
                heldAlongRay = Vector3.Distance(pose.position, hit.Point);
                return hit.Point;
            }
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
        private static void ResetStatics() => handsOnTargets = 0;
    }
}
