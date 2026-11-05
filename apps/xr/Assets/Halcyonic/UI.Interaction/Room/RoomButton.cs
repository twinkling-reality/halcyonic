#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The one button of the redesign: a choice (quiet or main) with its words, or a round control with its glyph alone
    /// (the bar's mic and Send). A plain placeholder shape until DESIGN's finish lands, never a pill inside a squircle; this
    /// is the one place the button's look changes then. It takes a press by <see cref="KitPress"/>'s rules and shows it
    /// while held: scale 0.97, brightness 84 percent (SYSTEM-v4's Motion).
    /// </summary>
    public sealed class RoomButton : MonoBehaviour
    {
        /// <summary>A choice's height and a round control's diameter, in dp: Meta's hand target (<see cref="SpatialKit.TargetDegrees"/>).</summary>
        public static float HeightDp => SpatialKit.TargetDegrees / SpatialKit.Degrees(1f);

        /// <summary>A round control as drawn, in dp, centred in its target (DESIGN, 2026-11-05): 52 dp, corner 22.</summary>
        public const float RoundDp = 52f;

        public const float RoundCornerDp = 22f;

        public const float GlyphDp = 22f;

        /// <summary>A choice's words' inset from its sides, in dp.</summary>
        public const float PadDp = 16f;

        private const float PressedScale = 0.97f;
        private const float PressedBrightness = 0.84f;

        private RoomSurface plate = null!;
        private TextMeshPro words = null!;
        private TextMeshPro glyph = null!;
        private KitPress press = null!;
        private bool round;
        private bool main;
        private bool held;
        private Vector2 size;

        public event Action? Pressed;

        public KitPress Press => press;

        public TextMeshPro Words => words;

        public RoomSurface Plate => plate;

        public Vector2 Size => press.Size;

        public bool Main => main;

        public bool Available
        {
            get => press.Available;
            set => press.Available = value;
        }

        /// <summary>Whether a hand holds the button now, for hold to talk.</summary>
        public bool Held => held;

        /// <param name="round">A round control, its glyph alone at its centre.</param>
        public static RoomButton Create(Transform parent, string name, bool round, int order = KitType.Order.Control)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var button = go.AddComponent<RoomButton>();
            button.round = round;
            button.plate = RoomSurface.Create(go.transform, "Plate", RoomSurfaceKind.Choice, order);
            if (round) button.plate.CornerOverrideDp = RoundCornerDp;
            button.words = RoomType.Create(go.transform, "Words", Color.white, order + 2, TextAlignmentOptions.Left);
            button.words.rectTransform.pivot = new Vector2(0f, 0.5f);
            button.words.textWrappingMode = TextWrappingModes.NoWrap;
            button.glyph = GlazeIcons.Create(go.transform, "Glyph", SpatialKit.IconDegrees, Color.white, order + 2);
            var height = RoomType.Units(HeightDp);
            button.press = KitPress.Create(go, new Vector2(height, height));
            button.press.Pressed += () => button.Pressed?.Invoke();
            button.press.Target.Selected += () => button.held = true;
            button.press.Target.Released += _ => button.held = false;
            go.SetActive(false);
            return button;
        }

        /// <summary>How wide a choice is with these words at the person's text size, in the plane's units.</summary>
        public float Measure(string text)
        {
            if (round) return RoomType.Units(RoundDp);
            RoomType.Size(words);
            return 2f * RoomType.Units(PadDp) + words.GetPreferredValues(LabelText.ForTextMeshPro(text)).x;
        }

        /// <summary>Shows the button centred at <paramref name="centre"/>, a choice <paramref name="width"/> wide (a round one its own size).</summary>
        public void Show(string text, GlazeIcon? icon, bool isMain, bool available, Vector2 centre, float width)
        {
            var literal = LabelText.ForTextMeshPro(text);
            var changed = !gameObject.activeSelf || words.text != literal || main != isMain;
            main = isMain;
            // The target is Meta's hand target whatever is drawn; a round control draws its 52 dp at the target's centre.
            var height = RoomType.Units(HeightDp);
            var size = round ? new Vector2(height, height) : new Vector2(Mathf.Max(width, height), height);
            plate.SetKind(isMain ? RoomSurfaceKind.MainChoice : RoomSurfaceKind.Choice);
            this.size = round ? Vector2.one * RoomType.Units(RoundDp) : size;
            RoomType.Size(words);
            GlazeText.SetLiteral(words, text);
            words.color = RoomSurface.Words(plate.Kind);
            glyph.color = words.color;
            words.gameObject.SetActive(!round);
            glyph.gameObject.SetActive(icon.HasValue);
            // A 22 dp glyph (DESIGN, 2026-11-05).
            var iconEm = RoomType.Units(GlyphDp);
            if (icon is GlazeIcon shown)
            {
                glyph.fontSize = GlazeTokens.FontSize(iconEm);
                glyph.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                GlazeIcons.Show(glyph, shown);
                glyph.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            }
            if (!round)
            {
                var (_, wordsWidth, _) = KitType.Lay(words, size.x - 2f * RoomType.Units(PadDp), 1);
                words.transform.localPosition = new Vector3(-wordsWidth / 2f, 0f, -0.0005f);
            }
            press.Resize(size);
            press.Available = available;
            transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(1f));
            if (changed) press.Unsettle();
            gameObject.SetActive(true);
            Paint();
        }

        public void Hide() => gameObject.SetActive(false);

        private void Update() => Paint();

        private void Paint()
        {
            var pressing = held || press.Pressing;
            var scale = pressing ? PressedScale : 1f;
            if (!Mathf.Approximately(transform.localScale.x, scale)) transform.localScale = new Vector3(scale, scale, 1f);
            var fill = RoomSurface.Fill(plate.Kind);
            if (pressing) fill = new Color(fill.r * PressedBrightness, fill.g * PressedBrightness, fill.b * PressedBrightness, fill.a);
            if (!press.Available) fill = Color.Lerp(fill, RoomSurface.Fill(RoomSurfaceKind.Choices), 0.5f);
            plate.Draw(size, fill);
        }
    }
}
