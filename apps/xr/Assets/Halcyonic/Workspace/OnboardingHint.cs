#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The first-time hint: above the character that needs the person, a thumb and index finger
    /// closing into a pinch, with three words. It shows until the person first opens a workspace, and
    /// never again on this device, so it teaches the one gesture and then gets out of the way.
    /// </summary>
    public sealed class OnboardingHint : MonoBehaviour
    {
        private const string OpenedKey = "Halcyonic.WorkspaceOpened";
        private const int CurvePoints = 12;
        /// <summary>Above the body, clear of its halo and of the labels underneath it, in the character's units.</summary>
        private const float Above = CharacterView.BodyRadius * 2.2f;
        private const float LineWidth = 0.005f;

        private LineRenderer index = null!;
        private LineRenderer thumb = null!;
        private TextMeshPro label = null!;
        private CharacterTarget? character;

        /// <summary>Whether the person has not yet opened a workspace on this device.</summary>
        public static bool Needed => PlayerPrefs.GetInt(OpenedKey, 0) == 0;

        /// <summary>The person opened a workspace: the hint is no longer needed.</summary>
        public static void Learned()
        {
            if (!Needed) return;
            PlayerPrefs.SetInt(OpenedKey, 1);
            PlayerPrefs.Save();
        }

        public static OnboardingHint Create(Transform parent)
        {
            var go = new GameObject("Onboarding hint");
            go.transform.SetParent(parent, false);
            var hint = go.AddComponent<OnboardingHint>();
            hint.index = Finger(go.transform, "Index");
            hint.thumb = Finger(go.transform, "Thumb");
            hint.label = WorkspaceVisuals.Text(go.transform, "Label", WorkspaceVisuals.PeekSize * 0.8f, WorkspaceVisuals.TextColor,
                new Vector2(0.5f, 0.04f), TextAlignmentOptions.MidlineLeft);
            hint.label.text = WorkspaceText.OpenHint;
            hint.label.rectTransform.localPosition = new Vector3(0.035f, 0.02f, 0f);
            go.SetActive(false);
            return hint;
        }

        public void Show(CharacterTarget target)
        {
            character = target;
            gameObject.SetActive(true);
            LateUpdate();
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
            var body = character.BodyPosition + Vector3.up * (Above * character.Scale);
            var scale = WorkspaceVisuals.ScaleFor(body, WorkspaceVisuals.PeekDistance);
            transform.SetPositionAndRotation(body, WorkspaceVisuals.FacingPerson(body));
            transform.localScale = Vector3.one * scale;
            index.widthMultiplier = thumb.widthMultiplier = LineWidth * scale;

            // The fingertips meet and part about once a second.
            var closing = Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
            var gap = Mathf.Lerp(0.014f, 0f, closing);
            Curve(index, new Vector2(-0.03f, 0.034f), new Vector2(0.004f, 0.04f), new Vector2(0.016f, gap / 2f + 0.002f));
            Curve(thumb, new Vector2(-0.03f, -0.02f), new Vector2(-0.002f, -0.026f), new Vector2(0.016f, -gap / 2f - 0.002f));
        }

        private static LineRenderer Finger(Transform parent, string name)
        {
            var line = WorkspaceVisuals.Line(parent, name, CurvePoints, LineWidth, loop: false);
            // Local positions, so the drawing turns and scales with the hint.
            line.useWorldSpace = false;
            line.numCapVertices = 4;
            line.startColor = line.endColor = WorkspaceVisuals.TextColor;
            return line;
        }

        /// <summary>A quadratic curve from the knuckle to the fingertip, in the hint's plane.</summary>
        private static void Curve(LineRenderer line, Vector2 from, Vector2 bend, Vector2 tip)
        {
            for (var point = 0; point < CurvePoints; point++)
            {
                var t = point / (CurvePoints - 1f);
                var position = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * bend + t * t * tip;
                line.SetPosition(point, new Vector3(position.x, position.y, -0.001f));
            }
        }
    }
}
