#nullable enable
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Pairing
{
    /// <summary>
    /// The pairing controls' line of text and its plate, built at runtime to look like the workspace's
    /// and the room's (whose helpers are internal to their assemblies): TextMeshPro that never parses
    /// markup, with escape parsing on for text that went through
    /// <see cref="Halcyonic.Client.LabelText.ForTextMeshPro"/>, on a rounded plate drawn with
    /// <c>Sprites/Default</c>, an always-included shader.
    /// </summary>
    internal static class PairingVisuals
    {
        /// <summary>The distance the sizes are designed for, as the workspace's buttons are.</summary>
        public const float DesignDistance = 1.3f;

        /// <summary>A 20 mm em at the design distance, the workspace's detail size.</summary>
        public const float LineSize = 0.20f;

        // The workspace's panel orders, so the controls draw after everything at the characters' distance.
        public const int PlateOrder = 10;
        public const int TextOrder = 12;

        public static readonly Color PlateColor = new Color(0.06f, 0.08f, 0.11f, 0.95f);
        public static readonly Color TextColor = new Color(0.93f, 0.95f, 0.96f);

        private const string FlatShader = "Sprites/Default";

        private static Material? flatMaterial;
        private static Sprite? roundedSprite;

        public static TextMeshPro Text(Transform parent, string name, Vector2 box)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = box;
            text.richText = false;
            text.parseCtrlCharacters = true;
            text.fontSize = LineSize;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.sortingOrder = TextOrder;
            return text;
        }

        /// <summary>A rounded rectangle centered on its transform, facing the person like the text.</summary>
        public static SpriteRenderer Plate(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var plate = go.AddComponent<SpriteRenderer>();
            plate.sharedMaterial = FlatMaterial;
            plate.sprite = RoundedSprite;
            plate.drawMode = SpriteDrawMode.Sliced;
            plate.color = PlateColor;
            plate.sortingOrder = PlateOrder;
            return plate;
        }

        private static Material FlatMaterial
        {
            get
            {
                if (flatMaterial != null) return flatMaterial;
                var shader = Shader.Find(FlatShader);
                if (shader == null) Debug.LogError("Halcyonic: the shader " + FlatShader + " is not in the build; keep it in Graphics settings' Always Included Shaders.");
                flatMaterial = new Material(shader);
                return flatMaterial;
            }
        }

        /// <summary>A 20 mm corner radius at any size: a small texture drawn nine-sliced, at 1000 pixels per meter.</summary>
        private static Sprite RoundedSprite
        {
            get
            {
                if (roundedSprite != null) return roundedSprite;
                const int size = 64;
                const float radius = 20f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Halcyonic pairing plate",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        // Distance outside the rounded rectangle, antialiased over one pixel.
                        var dx = Mathf.Max(Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius), 0f);
                        var dy = Mathf.Max(Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius), 0f);
                        var alpha = Mathf.Clamp01(0.5f - (Mathf.Sqrt(dx * dx + dy * dy) - radius));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                roundedSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 1000f, 0,
                    SpriteMeshType.FullRect, new Vector4(24f, 24f, 24f, 24f));
                roundedSprite.name = "Halcyonic pairing plate";
                return roundedSprite;
            }
        }
    }
}
