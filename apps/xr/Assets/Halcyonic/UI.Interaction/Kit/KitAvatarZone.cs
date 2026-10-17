#nullable enable
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The selected task's place on the instrument: its real character standing on the slate's top edge at its right,
    /// <see cref="TallDegrees"/> tall, so it goes wherever the instrument goes and, the slate growing down from a fixed
    /// top, never moves with the text size. Its role is the task's presence, never progress: it turns to the person
    /// while the task needs them and back to its work when it does not, and only on such a change of real state, over
    /// one slide, or at once while things are kept still. Where no character can stand, the zone says why in a band
    /// of Halcyonic's words with the one real way back, never a placeholder figure. It takes no press itself.
    /// </summary>
    public sealed class KitAvatarZone : MonoBehaviour
    {
        /// <summary>How tall the character stands, as an angle at the eyes.</summary>
        public const float TallDegrees = 4.5f;

        /// <summary>How far it turns from the person toward its work, about its own up.</summary>
        public const float AwayDegrees = 40f;

        private const float PadDegrees = 0.6f;
        private const float GapDegrees = 0.6f;

        private CharacterView? character;
        private Surface band = null!;
        private TextMeshPro words = null!;
        private KitButton action = null!;
        private float turn;
        private float turnFrom;
        private float turnTo;
        private float turnElapsed = 1f;
        private Vector2 size;

        public CharacterView? Character => character;
        public TextMeshPro Words => words;
        public KitButton Action => action;
        public Vector2 Size => size;

        /// <summary>Whether the absence's words were cut to fit: they never should be.</summary>
        public bool Cut { get; private set; }

        /// <summary>How far it now stands turned from the person toward its work, in degrees.</summary>
        public float Turn => turn;

        public static KitAvatarZone Create(Transform slate, string name = "Task's place")
        {
            var go = new GameObject(name);
            go.transform.SetParent(slate, false);
            var zone = go.AddComponent<KitAvatarZone>();
            zone.band = Surface.Create(go.transform, "Why it is absent", KitType.Order.Carrier + 3);
            zone.words = KitType.Create(go.transform, "Words", KitRole.Body, KitType.Text, KitType.Order.Words);
            zone.action = KitButton.Create(go.transform, "Way back", KitButtonRole.Secondary);
            return zone;
        }

        /// <summary>
        /// Stands the task's character for <paramref name="presentation"/> on the top edge of a slate
        /// <paramref name="slate"/> big, at its right. A change of whether the task needs the person turns it.
        /// </summary>
        public void ShowPresent(CharacterPresentation presentation, Vector2 slate)
        {
            band.gameObject.SetActive(false);
            words.gameObject.SetActive(false);
            action.Hide();
            if (character == null)
            {
                character = CharacterView.Create(transform, presentation.WorkstreamId);
                character.BodyOnlyPresentation = true;
                // It stands rather than hovers: its body keeps to its footing on the edge.
                character.Grounded = true;
            }
            character.gameObject.SetActive(true);
            var wasShown = character.Presentation != null;
            character.Show(presentation);
            var scale = KitType.Units(TallDegrees) / (2f * CharacterView.BodyRadius);
            character.transform.localScale = Vector3.one * scale;
            var half = CharacterView.BodyRadius * scale;
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            // Its body rests on the edge: its drawn lowest point, wherever its mesh puts it, at the slate's top.
            transform.localPosition = new Vector3(slate.x / 2f - pad - half, slate.y / 2f, -KitType.Units(0.5f));
            var lowest = float.MaxValue;
            foreach (var shape in character.GetComponentsInChildren<MeshRenderer>())
            {
                if (!shape.enabled) continue;
                var bounds = shape.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x, (corner & 2) == 0 ? bounds.min.y : bounds.max.y, (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    lowest = Mathf.Min(lowest, transform.parent.InverseTransformPoint(point).y);
                }
            }
            if (lowest < float.MaxValue) transform.localPosition += new Vector3(0f, slate.y / 2f - lowest, 0f);
            size = new Vector2(2f * half, 2f * half);
            var target = presentation.Attention == AttentionLevel.ActionRequired ? 0f : AwayDegrees;
            if (!wasShown || GlazeMotion.Still) TurnAt(target, target, 1f);
            else if (target != turnTo) TurnAt(turn, target, 0f);
        }

        /// <summary>
        /// No character stands: <paramref name="why"/>, Halcyonic's words, in a band on the slate's top edge at its
        /// right, with <paramref name="wayBack"/>, the existing action that brings it back, where there is one.
        /// </summary>
        public void ShowAbsent(string why, string? wayBack, GlazeIcon? icon, Vector2 slate, float width)
        {
            if (character != null) character.gameObject.SetActive(false);
            band.gameObject.SetActive(true);
            words.gameObject.SetActive(true);
            var pad = KitType.Scaled(PadDegrees);
            var gap = KitType.Scaled(GapDegrees);
            var buttonWidth = wayBack == null ? 0f : action.Measure(wayBack, icon);
            KitType.Size(words, KitRole.Body);
            words.color = KitType.Secondary;
            GlazeText.SetLiteral(words, why);
            var room = width - 2f * pad - (wayBack == null ? 0f : buttonWidth + gap);
            var (_, _, wordsHeight) = KitType.Lay(words, room, 3);
            Cut = words.isTextTruncated;
            var height = Mathf.Max(wordsHeight, wayBack == null ? 0f : KitButton.Height) + 2f * pad;
            size = new Vector2(width, height);
            var outside = KitType.Units(SpatialKit.PaddingDegrees);
            transform.localPosition = new Vector3(slate.x / 2f - outside - width / 2f, slate.y / 2f + height / 2f, -KitType.Units(0.05f));
            band.Draw(size, KitType.Units(SpatialKit.ControlRadiusDegrees), KitType.Colour(SpatialKit.Side));
            band.transform.localPosition = Vector3.zero;
            words.transform.localPosition = new Vector3(-width / 2f + pad, wordsHeight / 2f, -0.0005f);
            if (wayBack != null) action.Show(wayBack, icon, new Vector2(width / 2f - pad - buttonWidth / 2f, 0f), buttonWidth);
            else action.Hide();
        }

        /// <summary>Advances the turn by <paramref name="seconds"/>: one eased slide, at once while things are kept still.</summary>
        public void Advance(float seconds)
        {
            turnElapsed = GlazeMotion.Still ? 1f : Mathf.Min(1f, turnElapsed + seconds / Glaze.SlideSeconds);
            turn = Mathf.Lerp(turnFrom, turnTo, Glaze.EaseInOut(turnElapsed));
            if (character != null) character.transform.localRotation = Quaternion.Euler(0f, 180f + turn, 0f);
        }

        private void Update() => Advance(Time.deltaTime);

        private void TurnAt(float from, float to, float elapsed)
        {
            turnFrom = from;
            turnTo = to;
            turnElapsed = elapsed;
            Advance(0f);
        }
    }
}
