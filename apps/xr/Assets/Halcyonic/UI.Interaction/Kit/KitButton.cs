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
        /// <summary>The one action its region leads to, filled with the accent.</summary>
        Primary,

        /// <summary>Any other action, on a raised fill.</summary>
        Secondary,

        /// <summary>A local action that changes only what shows, such as Hide composer: its outline alone at rest.</summary>
        Quiet,
    }

    /// <summary>
    /// A button of the spatial kit: one whole hand target (<see cref="SpatialKit.TargetDegrees"/> tall) whose visible
    /// boundary is its target, its words in a verb first, an icon of Halcyonic's before them where the action has one,
    /// never in their place. It says each state: at rest by its role, pointed at by a white frame, pressed by a deeper
    /// fill, unavailable by quiet words on its outline alone (taking no press), and set aside while the app lacks
    /// focus. Its press is <see cref="KitPress"/>'s: it asks its owner, it decides nothing. Centred on its transform.
    /// </summary>
    public sealed class KitButton : MonoBehaviour
    {
        private const float PadDegrees = 1.2f;
        private const float GapDegrees = 0.45f;
        private const float AwayOpacity = 0.45f;

        private Surface plate = null!;
        private TextMeshPro words = null!;
        private TextMeshPro icon = null!;
        private KitPress press = null!;
        private KitButtonRole role;
        private Vector2 size;
        private int painted = -1;

        public KitPress Press => press;

        public TextMeshPro Words => words;

        public Surface Plate => plate;

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
            button.plate = Surface.Create(go.transform, "Boundary", order);
            button.words = KitType.Create(go.transform, "Words", KitRole.Strong, KitType.Text, order + 2, TextAlignmentOptions.Left, room: room);
            button.words.rectTransform.pivot = new Vector2(0f, 0.5f);
            button.words.textWrappingMode = TextWrappingModes.NoWrap;
            button.icon = GlazeIcons.Create(go.transform, "Icon", SpatialKit.IconDegrees, KitType.Text, order + 2);
            button.press = KitPress.Create(go, new Vector2(Height, Height));
            button.press.Pressed += () => button.Pressed?.Invoke();
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide the button is with these words and icon at the person's text size.</summary>
        public float Measure(string text, GlazeIcon? glyph)
        {
            KitType.Size(words, KitRole.Strong);
            var iconRoom = glyph.HasValue ? KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale) + KitType.Scaled(GapDegrees) : 0f;
            return Mathf.Max(Height, 2f * KitType.Units(PadDegrees) + iconRoom + words.GetPreferredValues(LabelText.ForTextMeshPro(text)).x);
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
            var (_, wordsWidth, _) = KitType.Lay(words, size.x - 2f * KitType.Units(PadDegrees) - iconRoom, 1);
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
            var radius = KitType.Units(SpatialKit.ControlRadiusDegrees);
            Color fill, edge, text;
            var edgeWidth = hairline;
            if (!available)
            {
                fill = Color.clear;
                edge = KitType.Colour(SpatialKit.Edge);
                text = KitType.Secondary;
            }
            else
            {
                switch (role)
                {
                    case KitButtonRole.Primary:
                        // The room's one accent (ADR 0040): deeper pressed, lifter pointed, its ink on every fill.
                        fill = KitType.Colour(pressed ? SpatialKit.PressedAccent : hovered ? SpatialKit.HoveredAccent : SpatialKit.Accent);
                        edge = hovered ? Color.white : fill;
                        text = KitType.Colour(SpatialKit.AccentInk);
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
                if (hovered) edgeWidth = frame;
            }
            var opacity = away ? AwayOpacity : 1f;
            plate.Draw(size, radius, fill, edge, edgeWidth);
            plate.Fade(opacity);
            plate.Selection = hovered ? SurfaceSelection.Pointed : role == KitButtonRole.Primary && available ? SurfaceSelection.MainCap : SurfaceSelection.None;
            words.color = new Color(text.r, text.g, text.b, opacity);
            icon.color = words.color;
        }
    }
}
