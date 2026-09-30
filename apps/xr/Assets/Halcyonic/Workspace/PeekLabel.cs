#nullable enable
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The peek: one line beside a character while a hand points at it, saying what it needs from the
    /// person or what it did last. It sits on the side facing the middle of the person's view, a
    /// little in front of the character so a neighbour never hides it.
    /// </summary>
    public sealed class PeekLabel : MonoBehaviour
    {
        private const float MaxWidth = 0.9f;
        private const float LineHeight = 0.045f;
        private const float Beside = 0.19f;
        private const float Forward = 0.08f;

        private TextMeshPro text = null!;
        private SpriteRenderer plate = null!;
        private CharacterTarget? character;
        private float width;
        private float side = 1f;

        public static PeekLabel Create(Transform parent)
        {
            var go = new GameObject("Peek");
            go.transform.SetParent(parent, false);
            var peek = go.AddComponent<PeekLabel>();
            peek.plate = WorkspaceVisuals.Plate(go.transform, "Plate", new Vector2(0.1f, 0.06f), WorkspaceVisuals.PanelColor, WorkspaceVisuals.PlateOrder);
            peek.text = WorkspaceVisuals.Text(go.transform, "Line", WorkspaceVisuals.PeekSize, WorkspaceVisuals.TextColor,
                new Vector2(MaxWidth, LineHeight), TextAlignmentOptions.MidlineLeft);
            go.SetActive(false);
            return peek;
        }

        public void Show(CharacterTarget target, string line)
        {
            if (target != character)
            {
                // Chosen once per peek, so it does not flip sides while the person looks around.
                side = SideFacingTheMiddle(target.BodyPosition);
            }
            character = target;
            text.text = line;
            width = Mathf.Min(MaxWidth, text.GetPreferredValues(line).x + 0.01f);
            gameObject.SetActive(true);
            Place();
        }

        public void Hide()
        {
            character = null;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (character == null)
            {
                Hide();
                return;
            }
            Place();
        }

        /// <summary>
        /// +1 for the person's right of <paramref name="position"/>, -1 for its left: toward the middle
        /// of the view, so a character right of where the person looks gets its peek on its left.
        /// </summary>
        public static float SideFacingTheMiddle(Vector3 position)
        {
            var toward = WorkspaceVisuals.FacingPerson(position) * Vector3.forward;
            var head = WorkspaceVisuals.Head;
            var looking = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : toward;
            return Vector3.SignedAngle(looking, toward, Vector3.up) > 0f ? -1f : 1f;
        }

        private void Place()
        {
            if (character == null) return;
            var body = character.BodyPosition;
            var rotation = WorkspaceVisuals.FacingPerson(body);
            // The same angular size at any distance, like the characters themselves.
            var scale = WorkspaceVisuals.ScaleFor(body, WorkspaceVisuals.PeekDistance);
            transform.localScale = Vector3.one * scale;
            transform.SetPositionAndRotation(body - rotation * Vector3.forward * (Forward * scale), rotation);
            var left = side > 0f ? Beside : -Beside - width;
            text.rectTransform.localPosition = new Vector3(left, LineHeight / 2f, -0.001f);
            text.rectTransform.sizeDelta = new Vector2(width, LineHeight);
            plate.transform.localPosition = new Vector3(left + width / 2f, 0f, 0f);
            plate.size = new Vector2(width + 0.05f, LineHeight + 0.02f);
        }
    }
}
