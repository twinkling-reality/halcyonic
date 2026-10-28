#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>A miniature of the selected real task, attached to its heading. It shares the
    /// character's identity, colour, state cues and Still behaviour. It never creates a room agent,
    /// stage slot, interaction target or task. The caller supplies an allocated heading slot and
    /// explicitly shows or hides the portrait independently of room-agent visibility.</summary>
    public sealed class TaskIdentityAvatarView : MonoBehaviour
    {
        private CharacterView? character;
        private Transform? pivot;
        public CharacterView? Character => character;
        public string? WorkstreamId => character != null ? character.WorkstreamId : null;
        public Vector2 Size { get; private set; }

        /// <summary>The complete reserved motion volume, including depth, in world coordinates.</summary>
        public IEnumerable<Vector3> MotionEnvelopeCorners
        {
            get
            {
                if (!gameObject.activeInHierarchy || character == null) yield break;
                var depth = CharacterView.BodyRadius * character.transform.localScale.x;
                foreach (var x in new[] { -.5f, .5f }) foreach (var y in new[] { -.5f, .5f }) foreach (var z in new[] { -1f, 1f })
                    yield return transform.TransformPoint(new Vector3(Size.x * x, Size.y * y, depth * z));
            }
        }

        public static TaskIdentityAvatarView Create(Transform parent)
        {
            var root = new GameObject("Selected task portrait");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<TaskIdentityAvatarView>();
            root.SetActive(false);
            return view;
        }

        public void Show(CharacterPresentation presentation, double? hue, Vector2 centre, Vector2 size)
        {
            if (size.x <= 0f || size.y <= 0f) { Hide(); return; }
            if (character == null || character.WorkstreamId != presentation.WorkstreamId)
            {
                ReleaseCharacter();
                character = CharacterView.Create(Pivot, presentation.WorkstreamId);
                character.BodyOnlyPresentation = true;
                character.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            Size = size;
            // Reserve the complete animated shell/feature envelope, including the waiting lift.
            // The portrait's labels, halo and sweep ring are absent; the task's textual badge is
            // adjacent. Geometry never grows into its title or changes the heading's hit areas.
            var envelopeHeight = CharacterView.BodyRadius + CharacterView.HighestReach;
            var scale = Mathf.Min(size.x / (2f * CharacterView.BodyRadius), size.y / envelopeHeight);
            character.transform.localScale = Vector3.one * scale;
            character.transform.localPosition = new Vector3(0f, -(CharacterView.HighestReach - CharacterView.BodyRadius) * scale / 2f, 0f);
            // Stand entirely ahead of the opaque face; front/back shell depth fits the same radius.
            transform.localPosition = new Vector3(centre.x, centre.y, -CharacterView.BodyRadius * scale - 0.001f);
            character.SetIdentityHue(hue);
            character.Show(presentation);
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Stands the character where <paramref name="curve"/>'s curve puts the portrait's centre, turned with it, while
        /// the curve is on; with none, as laid out. The portrait's own place and the character's within it are kept.
        /// </summary>
        public void StandOn(CurvedDrawing? curve)
        {
            if (pivot == null) return;
            if (curve == null || !curve.On || !gameObject.activeInHierarchy)
            {
                pivot.localPosition = Vector3.zero;
                pivot.localRotation = Quaternion.identity;
                return;
            }
            var at = transform.position;
            var bent = curve.Bend(at);
            var along = curve.Bend(at + transform.right * 0.001f) - bent;
            pivot.SetPositionAndRotation(bent, Quaternion.FromToRotation(transform.right, along.normalized) * transform.rotation);
        }

        /// <summary>What the character stands on: where the curve puts the portrait, or the portrait itself.</summary>
        private Transform Pivot
        {
            get
            {
                if (pivot != null) return pivot;
                var go = new GameObject("Stands on the curve");
                go.transform.SetParent(transform, false);
                pivot = go.transform;
                return pivot;
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            Size = Vector2.zero;
            ReleaseCharacter();
        }

        private void ReleaseCharacter()
        {
            if (character == null) return;
            var old = character;
            character = null;
            // Deactivate before deferred destruction so rapid task switching cannot leave a second
            // live identity or animation in the same heading during this frame.
            old.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }
    }
}
