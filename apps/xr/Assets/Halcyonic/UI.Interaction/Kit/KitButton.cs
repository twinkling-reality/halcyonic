#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What a kit button does, which sets its look: roles, never screens, choose colours.</summary>
    public enum KitButtonRole
    {
        /// <summary>The one action its region leads to: filled with light, n7 with its word in n0.</summary>
        Primary,

        /// <summary>Any other action, on a raised fill.</summary>
        Secondary,

        /// <summary>A local action that changes only what shows, such as Hide composer: its outline alone at rest.</summary>
        Quiet,

        /// <summary>
        /// An answer that is its word alone (the art direction's handoff): no shape at rest, Body Strong in n6; pointed, a soft
        /// capsule with a quiet ring and the word in n7; pressed, a deeper capsule.
        /// </summary>
        Word,
    }

    /// <summary>
    /// A button of the spatial kit: one whole hand target (<see cref="SpatialKit.TargetDegrees"/> tall) whose visible
    /// boundary is its target, its words in a verb first, an icon of Halcyonic's before them where the action has one,
    /// never in their place. It says each state: at rest by its role, pointed at by a white frame (an answer by its ring or
    /// soft capsule), pressed by a deeper fill, unavailable by quiet words on its outline alone (taking no press), and set
    /// aside while the app lacks focus. The answers, the primary and a word, are capsules. Its press is <see cref="KitPress"/>'s: it asks its owner, it decides nothing. Centred on its transform.
    /// </summary>
    public sealed class KitButton : MonoBehaviour
    {
        private const float PadDegrees = 1.2f;
        private const float GapDegrees = 0.45f;
        private const float AwayOpacity = 0.45f;

        private Surface plate = null!;
        private Surface ring = null!;
        private TextMeshPro words = null!;
        private TextMeshPro icon = null!;
        private KitPress press = null!;
        private KitButtonRole role;
        private Vector2 size;
        private int painted = -1;

        public KitPress Press => press;

        public TextMeshPro Words => words;

        public Surface Plate => plate;

        /// <summary>A pointed primary's ring outside its capsule; hidden otherwise.</summary>
        public Surface Ring => ring;

        /// <summary>The answers, the primary and a word, are capsules (the art direction's "one style everywhere").</summary>
        private bool Capsule => role == KitButtonRole.Primary || role == KitButtonRole.Word;

        private float Pad => KitType.Units(role switch
        {
            KitButtonRole.Primary => SpatialKit.Degrees(SpatialKit.PrimaryPadDp),
            KitButtonRole.Word => SpatialKit.Degrees(SpatialKit.WordPadDp),
            _ => PadDegrees,
        });

        public Vector2 Size => size;

        public KitButtonRole Role
        {
            get => role;
            set
            {
                if (role == value) return;
                role = value;
                painted = -1;
                press.Unsettle();
            }
        }

        public bool Available
        {
            get => press.Available;
            set
            {
                if (press.Available == value) return;
                press.Available = value;
                painted = -1;
            }
        }

        public event Action? Pressed;

        public static float Height => KitType.Units(SpatialKit.TargetDegrees);

        /// <summary>A button of <paramref name="role"/>; a <paramref name="room"/> button's words are in the room's faces (<see cref="KitType.Create"/>).</summary>
        public static KitButton Create(Transform parent, string name, KitButtonRole role, int order = KitType.Order.Control, bool room = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<KitButton>();
            button.role = role;
            button.ring = Surface.Create(go.transform, "Pointed ring", order - 1);
            button.ring.gameObject.SetActive(false);
            button.plate = Surface.Create(go.transform, "Boundary", order);
            button.words = KitType.Create(go.transform, "Words", KitRole.Strong, KitType.Text, order + 2, TextAlignmentOptions.Left, room: room);
            button.words.rectTransform.pivot = new Vector2(0f, 0.5f);
            button.words.textWrappingMode = TextWrappingModes.NoWrap;
            button.icon = GlazeIcons.Create(go.transform, "Icon", SpatialKit.IconDegrees, KitType.Text, order + 2);
            button.press = KitPress.Create(go, new Vector2(Height, Height));
            button.press.Pressed += () =>
            {
                // An answer's release sounds Press from the answer (the art direction's sound); the stage does not hear kit presses.
                if (button.Capsule) KitSoundPlayer.TryPlay(KitSound.Press, button.transform.position);
                button.Pressed?.Invoke();
            };
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide the button is with these words and icon at the person's text size.</summary>
        public float Measure(string text, GlazeIcon? glyph)
        {
            KitType.Size(words, KitRole.Strong);
            var iconRoom = glyph.HasValue ? KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale) + KitType.Scaled(GapDegrees) : 0f;
            return Mathf.Max(Height, 2f * Pad + iconRoom + words.GetPreferredValues(LabelText.ForTextMeshPro(text)).x);
        }

        /// <summary>Shows the button centred at <paramref name="centre"/>, <paramref name="width"/> wide, its words and icon centred together.</summary>
        public void Show(string text, GlazeIcon? glyph, Vector2 centre, float width)
        {
            var changed = !gameObject.activeSelf || words.text != LabelText.ForTextMeshPro(text);
            KitType.Size(words, KitRole.Strong);
            GlazeText.SetLiteral(words, text);
            size = new Vector2(Mathf.Max(width, Height), Height);
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            var iconRoom = glyph.HasValue ? iconEm + KitType.Scaled(GapDegrees) : 0f;
            var (_, wordsWidth, _) = KitType.Lay(words, size.x - 2f * Pad - iconRoom, 1);
            var left = -(iconRoom + wordsWidth) / 2f;
            icon.gameObject.SetActive(glyph.HasValue);
            if (glyph is GlazeIcon shown)
            {
                icon.fontSize = GlazeTokens.FontSize(iconEm);
                icon.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                GlazeIcons.Show(icon, shown);
                icon.transform.localPosition = new Vector3(left + iconEm / 2f, 0f, -0.0005f);
            }
            words.transform.localPosition = new Vector3(left + iconRoom, 0f, -0.0005f);
            press.Resize(size);
            transform.localPosition = new Vector3(centre.x, centre.y, -KitType.Units(0.05f));
            if (changed) press.Unsettle();
            painted = -1;
            gameObject.SetActive(true);
            Paint(press.Hovered, press.Pressing);
        }

        public void Hide() => gameObject.SetActive(false);

        private void Update() => Paint(press.Hovered, press.Pressing);

        /// <summary>For renders, which have no hands: paints the button pointed at or pressed until the next frame paints it as it is.</summary>
        public void PaintForRender(bool pointed, bool pressed)
        {
            painted = -1;
            Paint(pointed, pressed);
        }

        private void Paint(bool pointed, bool pressing)
        {
            var available = press.Available;
            var hovered = pointed && available;
            var pressed = pressing && available;
            var away = FocusGuard.InputSuspended;
            var state = (hovered ? 1 : 0) | (pressed ? 2 : 0) | (away ? 4 : 0) | (available ? 8 : 0) | ((int)role << 4);
            if (state == painted) return;
            painted = state;
            var hairline = KitType.Units(SpatialKit.HairlineDegrees);
            var frame = KitType.Units(0.09f);
            var radius = Capsule ? size.y / 2f : KitType.Units(SpatialKit.ControlRadiusDegrees);
            var answerRing = KitType.Units(SpatialKit.Degrees(SpatialKit.AnswerRingDp));
            Color fill, edge, text;
            var edgeWidth = hairline;
            var ringShown = false;
            var scale = 1f;
            if (!available)
            {
                // Locked: a 2 dp n4 ring round a quiet word, its reason being its word; a word answer keeps no shape.
                fill = Color.clear;
                edge = role == KitButtonRole.Word ? Color.clear : KitType.Colour(SpatialKit.Edge);
                text = Capsule ? KitType.Colour(SpatialKit.TextTertiary) : KitType.Secondary;
                if (role == KitButtonRole.Primary) edgeWidth = answerRing;
            }
            else
            {
                switch (role)
                {
                    case KitButtonRole.Primary:
                        // The one primary is filled with light (ADR 0040 as amended 2026-10-30): n7, pressed to n6 and 97 percent,
                        // its word in n0; pointed, 2.5 percent larger with a 3 dp ring outside it, 3:1 or more on every face (AUDIT's
                        // A1C-S1). The accent is only for live work.
                        fill = KitType.Colour(pressed ? SpatialKit.TextSecondary : SpatialKit.Text);
                        edge = fill;
                        text = KitType.Colour(SpatialKit.OnFill);
                        ringShown = hovered && !pressed;
                        scale = pressed ? 0.97f : hovered ? 1.025f : 1f;
                        break;
                    case KitButtonRole.Word:
                        fill = pressed ? KitType.Colour(SpatialKit.WordPressed) : hovered ? KitType.Colour(SpatialKit.WordPointed) : Color.clear;
                        edge = hovered || pressed ? KitType.Colour(SpatialKit.WordRing) : Color.clear;
                        edgeWidth = answerRing;
                        text = hovered || pressed ? KitType.Text : KitType.Secondary;
                        scale = pressed ? 0.97f : 1f;
                        break;
                    case KitButtonRole.Secondary:
                        // Its raised fill is its boundary; only a pointed one is framed.
                        fill = KitType.Colour(pressed ? SpatialKit.Control : SpatialKit.Raised);
                        edge = hovered ? Color.white : fill;
                        text = KitType.Text;
                        break;
                    default:
                        fill = pressed ? KitType.Colour(SpatialKit.Raised) : Color.clear;
                        edge = hovered ? Color.white : KitType.Colour(SpatialKit.Edge);
                        text = KitType.Text;
                        break;
                }
                if (hovered && !Capsule) edgeWidth = frame;
            }
            var opacity = away ? AwayOpacity : 1f;
            plate.Draw(size, radius, fill, edge, edgeWidth);
            plate.Fade(opacity);
            ring.gameObject.SetActive(ringShown);
            if (ringShown)
            {
                var reach = KitType.Units(SpatialKit.Degrees(SpatialKit.PrimaryRingDp));
                ring.Draw(size + 2f * reach * Vector2.one, radius + reach, KitType.Colour(SpatialKit.PrimaryRing));
                ring.transform.localPosition = new Vector3(0f, 0f, 0.0001f);
                ring.Fade(opacity);
            }
            transform.localScale = Vector3.one * scale;
            plate.Selection = hovered ? SurfaceSelection.Pointed : role == KitButtonRole.Primary && available ? SurfaceSelection.MainCap : SurfaceSelection.None;
            words.color = new Color(text.r, text.g, text.b, opacity);
            icon.color = words.color;
        }
    }
}
