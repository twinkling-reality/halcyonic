#nullable enable
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// A placeholder character: a sphere whose motion follows the activity, with the status always
    /// written out, so no state depends on color or motion alone.
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        /// <summary>
        /// A primitive's default material uses the Standard shader, which a player build leaves out
        /// when no asset in the build uses it; the body then renders magenta. This shader is in the
        /// project's always-included shaders (Graphics settings).
        /// </summary>
        private const string BodyShader = "Legacy Shaders/Diffuse";

        private Transform body = null!;
        private Material bodyMaterial = null!;
        private TextMesh title = null!;
        private TextMesh status = null!;
        private TextMesh notes = null!;
        private CharacterPresentation? presentation;
        private float phase;

        public static CharacterView Create(Transform parent, string workstreamId)
        {
            var root = new GameObject("Character " + workstreamId);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<CharacterView>();
            view.Build();
            return view;
        }

        public void Show(CharacterPresentation next)
        {
            presentation = next;
            title.text = next.Title;
            status.text = StatusLine(next);
            notes.text = string.Join("\n", next.AttentionNotes);
            bodyMaterial.color = ColorOf(next);
        }

        private void Build()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Body";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * 0.22f;
            body = sphere.transform;
            var bodyRenderer = sphere.GetComponent<Renderer>();
            var shader = Shader.Find(BodyShader);
            if (shader == null)
            {
                Debug.LogError("Halcyonic: the shader " + BodyShader + " is not in the build, so characters render magenta. Keep it in Graphics settings' Always Included Shaders.");
                shader = bodyRenderer.sharedMaterial.shader;
            }
            bodyMaterial = new Material(shader);
            bodyRenderer.sharedMaterial = bodyMaterial;
            title = Labels.Create(transform, "Title", new Vector3(0f, 0.24f, 0f), 0.003f);
            status = Labels.Create(transform, "Status", new Vector3(0f, -0.2f, 0f), 0.0025f);
            notes = Labels.Create(transform, "Notes", new Vector3(0f, -0.3f, 0f), 0.0018f);
            phase = Random.value * Mathf.PI * 2f;
        }

        private void OnDestroy() => Destroy(bodyMaterial);

        private void Update()
        {
            if (presentation == null) return;
            float amplitude;
            float speed;
            switch (presentation.Activity)
            {
                case CharacterActivity.Working:
                case CharacterActivity.Verifying:
                    amplitude = 0.03f;
                    speed = 4f;
                    break;
                case CharacterActivity.Starting:
                case CharacterActivity.WaitingForHuman:
                    amplitude = 0.02f;
                    speed = 2f;
                    break;
                default:
                    amplitude = 0.008f;
                    speed = 0.8f;
                    break;
            }
            // A character that needs a decision rises toward the user's eye line.
            var lift = presentation.Attention == AttentionLevel.ActionRequired ? 0.1f : 0f;
            body.localPosition = new Vector3(0f, lift + Mathf.Sin(Time.time * speed + phase) * amplitude, 0f);
        }

        private static string StatusLine(CharacterPresentation character)
        {
            var line = character.StatusLabel;
            if (character.PendingApprovals > 1) line += " (" + character.PendingApprovals + " approvals)";
            if (character.Synthetic) line += " · simulated";
            if (character.Recorded) line += " · recorded";
            if (character.Stale) line += " · last known";
            return line;
        }

        private static Color ColorOf(CharacterPresentation character)
        {
            Color color;
            switch (character.Activity)
            {
                case CharacterActivity.Working:
                case CharacterActivity.Verifying:
                case CharacterActivity.Starting:
                    color = new Color(0.35f, 0.62f, 0.95f);
                    break;
                case CharacterActivity.WaitingForHuman:
                    color = new Color(0.98f, 0.76f, 0.3f);
                    break;
                case CharacterActivity.TurnFinished:
                    color = new Color(0.45f, 0.8f, 0.55f);
                    break;
                case CharacterActivity.Failed:
                    color = new Color(0.9f, 0.35f, 0.35f);
                    break;
                case CharacterActivity.Unknown:
                    color = new Color(0.6f, 0.6f, 0.65f);
                    break;
                default:
                    color = new Color(0.8f, 0.8f, 0.85f);
                    break;
            }
            return character.Stale ? Color.Lerp(color, Color.gray, 0.6f) : color;
        }
    }
}
