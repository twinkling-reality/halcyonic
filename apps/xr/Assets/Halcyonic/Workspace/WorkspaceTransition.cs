#nullable enable
using System;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Makes the character and its workspace read as the same work at two levels of detail
    /// (docs/internal/product/PRODUCT.md): the panel grows out of the character's body to its place
    /// beside it, a ring marks the opened character, and a line links the two while open. Collapsing
    /// reverses it into the body. The character itself stays where the stage put it. Kept separate
    /// from the panel so the characters lane can refine it.
    /// </summary>
    public sealed class WorkspaceTransition : MonoBehaviour
    {
        private const float OpenSeconds = 0.32f;
        private const float CollapseSeconds = 0.22f;
        private const float StartScale = 0.04f;
        private const float RingRadius = 0.17f;
        private const int RingPoints = 48;
        private const float LinkWidth = 0.003f;
        private const float RingWidth = 0.004f;

        private CharacterTarget character = null!;
        private Pose target;
        private float scale = 1f;
        private float progress;
        private float direction = 1f;
        private Action? collapsed;
        private LineRenderer link = null!;
        private LineRenderer ring = null!;
        private Transform decorations = null!;
        private bool finished;

        /// <summary>The panel reached its place and is not collapsing.</summary>
        public bool Open => direction > 0f && progress >= 1f;

        /// <summary>Where the character's body was when the panel was placed.</summary>
        public Vector3 PlacedBeside { get; private set; }

        /// <summary>Moves the panel to a new place, for example after the stage moved its character.</summary>
        public void MoveTo(Pose place, float newScale)
        {
            target = place;
            scale = newScale;
            PlacedBeside = character.BodyPosition;
        }

        /// <summary>Grows <paramref name="panel"/> out of <paramref name="from"/> to <paramref name="place"/>, at <paramref name="scale"/>.</summary>
        public static WorkspaceTransition Begin(GameObject panel, CharacterTarget from, Pose place, float scale)
        {
            var transition = panel.AddComponent<WorkspaceTransition>();
            transition.character = from;
            transition.target = place;
            transition.scale = scale;
            transition.PlacedBeside = from.BodyPosition;
            // Lines are not children of the panel, whose scale changes while it grows.
            transition.decorations = new GameObject("Workspace link").transform;
            transition.decorations.SetParent(panel.transform.parent, false);
            transition.link = WorkspaceVisuals.Line(transition.decorations, "Link", 2, LinkWidth, loop: false);
            transition.ring = WorkspaceVisuals.Line(transition.decorations, "Ring", RingPoints, RingWidth, loop: true);
            transition.Apply();
            return transition;
        }

        /// <summary>Shrinks the panel back into the character and destroys it, at once when <paramref name="immediately"/>.</summary>
        public void Collapse(bool immediately = false, Action? done = null)
        {
            direction = -1f;
            collapsed = done;
            if (immediately) Finish();
        }

        private void LateUpdate()
        {
            if (finished) return;
            if (character == null)
            {
                // The workstream left the stage; there is nothing to collapse into.
                Finish();
                return;
            }
            progress = Mathf.Clamp01(progress + direction * Time.unscaledDeltaTime / (direction > 0f ? OpenSeconds : CollapseSeconds));
            Apply();
            if (direction < 0f && progress <= 0f) Finish();
        }

        private void Apply()
        {
            var eased = Mathf.SmoothStep(0f, 1f, progress);
            var body = character.BodyPosition;
            transform.SetPositionAndRotation(Vector3.Lerp(body, target.position, eased), target.rotation);
            transform.localScale = Vector3.one * (scale * Mathf.Lerp(StartScale, 1f, eased));

            var color = WorkspaceVisuals.LinkColor;
            color.a *= eased;
            link.startColor = link.endColor = color;
            ring.startColor = ring.endColor = color;

            // The link joins the body to the nearest point of the panel's edge on the character's side.
            var local = transform.InverseTransformPoint(body);
            var edge = new Vector3(
                Mathf.Sign(local.x) * WorkspacePanel.Width / 2f,
                Mathf.Clamp(local.y, -WorkspacePanel.Height / 2f + 0.03f, WorkspacePanel.Height / 2f - 0.03f),
                0f);
            link.SetPosition(0, body);
            link.SetPosition(1, transform.TransformPoint(edge));

            link.widthMultiplier = LinkWidth * scale;
            ring.widthMultiplier = RingWidth * scale;
            var facing = WorkspaceVisuals.FacingPerson(body);
            var radius = RingRadius * character.Scale;
            for (var index = 0; index < RingPoints; index++)
            {
                var angle = index * Mathf.PI * 2f / RingPoints;
                ring.SetPosition(index, body + facing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            var done = collapsed;
            collapsed = null;
            if (decorations != null) Destroy(decorations.gameObject);
            Destroy(gameObject);
            done?.Invoke();
        }
    }
}
