#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Text, plates and lines for the peek and the workspace, built at runtime.
    ///
    /// Sizes are chosen for the Quest 3, about 25 pixels per degree at the lens center, at a design
    /// distance: <see cref="PanelDistance"/> for the workspace, <see cref="PeekDistance"/> for the
    /// peek. Both are scaled by their actual distance over the design distance, so they keep the
    /// same angular size wherever the stage puts its characters. A TextMeshPro font size is the em
    /// height in decimeters, so 0.24 draws a 24 mm em. Liberation Sans has an x-height of about half
    /// an em, so body text has an x-height of about 0.55 degrees, roughly 14 pixels; the smallest
    /// text, section captions, keeps about 10 pixels. Buttons are 72 mm tall at the design
    /// distance, about 3 degrees, well above the jitter of a hand ray.
    /// </summary>
    internal static class WorkspaceVisuals
    {
        /// <summary>The distance the workspace's sizes are designed for: comfortable to read without turning.</summary>
        public const float PanelDistance = 1.3f;

        /// <summary>The distance the peek's size is designed for.</summary>
        public const float PeekDistance = 1.6f;

        public const float TitleSize = 0.34f;
        public const float BodySize = 0.24f;
        public const float DetailSize = 0.20f;
        public const float CaptionSize = 0.18f;

        /// <summary>At <see cref="PeekDistance"/>, a 30 mm em keeps a 14 pixel x-height.</summary>
        public const float PeekSize = 0.30f;

        // Transparent renderers draw in sorting order before distance. The workspace, the nearest
        // thing to the person, draws after everything at the characters' distance, and its plate is
        // opaque, so neither a peek nor a character's body or label behind it shows through.
        public const int PlateOrder = 0;
        public const int ControlOrder = 1;
        public const int TextOrder = 2;
        public const int PanelPlateOrder = 10;
        public const int PanelControlOrder = 11;
        public const int PanelTextOrder = 12;

        /// <summary>
        /// Opaque. At 95 percent, and blended in the project's linear color space, a white label
        /// behind the workspace lifted its dark plate by up to 54 of 255 levels: readable through it.
        /// </summary>
        public static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.11f, 1f);
        public static readonly Color TextColor = new Color(0.93f, 0.95f, 0.96f);
        public static readonly Color SecondaryColor = new Color(0.64f, 0.69f, 0.74f);
        public static readonly Color ClaimColor = new Color(0.80f, 0.85f, 0.90f);
        public static readonly Color AttentionColor = new Color(0.96f, 0.77f, 0.32f);
        public static readonly Color ProblemColor = new Color(0.94f, 0.52f, 0.49f);
        public static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.21f, 0.27f, 1f);
        public static readonly Color ButtonHoverColor = new Color(0.25f, 0.32f, 0.40f, 1f);
        public static readonly Color ButtonPressColor = new Color(0.36f, 0.46f, 0.58f, 1f);
        public static readonly Color ConfirmColor = new Color(0.96f, 0.77f, 0.32f, 1f);
        public static readonly Color ConfirmHoverColor = new Color(1f, 0.86f, 0.50f, 1f);
        public static readonly Color ConfirmTextColor = new Color(0.10f, 0.09f, 0.07f);
        public static readonly Color LinkColor = new Color(0.55f, 0.72f, 0.95f, 0.85f);

        /// <summary>
        /// Sprites/Default is in the project's always-included shaders (Graphics settings), so plates
        /// and lines render in a player build; a shader only the editor has renders magenta there.
        /// </summary>
        private const string FlatShader = "Sprites/Default";

        private static Material? flatMaterial;
        private static Sprite? roundedSprite;

        public static Material FlatMaterial
        {
            get
            {
                if (flatMaterial == null)
                {
                    var shader = Shader.Find(FlatShader);
                    if (shader == null) Debug.LogError("Halcyonic: the shader " + FlatShader + " is not in the build; keep it in Graphics settings' Always Included Shaders.");
                    flatMaterial = new Material(shader);
                }
                return flatMaterial;
            }
        }

        /// <summary>
        /// World-space text that never interprets markup, since it shows text from agents and tools.
        /// Escape parsing stays on, so a doubled backslash shows as one: text goes in through
        /// <see cref="SetLiteral"/>, which doubles every backslash.
        /// </summary>
        public static TextMeshPro Text(Transform parent, string name, float size, Color color, Vector2 box, TextAlignmentOptions alignment,
            bool wrap = false, int order = TextOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.pivot = new Vector2(0f, 1f);
            text.rectTransform.sizeDelta = box;
            text.richText = false;
            text.parseCtrlCharacters = true;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.sortingOrder = order;
            return text;
        }

        /// <summary>
        /// Shows text on a label literally and completely, by the one rule for text Halcyonic did not
        /// write (<see cref="LabelText"/>): no markup, backslashes as they are, and what would not
        /// show as itself as its code point. Every label that can show such text gets it this way.
        /// </summary>
        public static void SetLiteral(TMP_Text label, string text)
        {
            label.richText = false;
            label.parseCtrlCharacters = true;
            label.text = LabelText.ForTextMeshPro(text);
        }

        /// <summary><see cref="SetLiteral"/> for a label of several lines: each line by the rule, one under the other.</summary>
        public static void SetLiteralLines(TMP_Text label, IEnumerable<string> lines)
        {
            label.richText = false;
            label.parseCtrlCharacters = true;
            label.text = string.Join("\n", lines.Select(LabelText.ForTextMeshPro));
        }

        /// <summary>A rounded rectangle centered on its transform, facing the person like the text.</summary>
        public static SpriteRenderer Plate(Transform parent, string name, Vector2 size, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var plate = go.AddComponent<SpriteRenderer>();
            plate.sharedMaterial = FlatMaterial;
            plate.sprite = RoundedSprite;
            plate.drawMode = SpriteDrawMode.Sliced;
            plate.size = size;
            plate.color = color;
            plate.sortingOrder = order;
            return plate;
        }

        public static LineRenderer Line(Transform parent, string name, int points, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = FlatMaterial;
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = ControlOrder;
            return line;
        }

        /// <summary>The person's eyes, or a standing eye height at the origin when there is no camera.</summary>
        public static Transform? Head => Camera.main != null ? Camera.main.transform : null;

        public static Vector3 HeadPosition => Head != null ? Head.position : new Vector3(0f, 1.6f, 0f);

        /// <summary>
        /// The scale that gives something designed for <paramref name="designDistance"/> the same
        /// angular size at <paramref name="position"/>, within reason.
        /// </summary>
        public static float ScaleFor(Vector3 position, float designDistance) =>
            Mathf.Clamp(Vector3.Distance(HeadPosition, position) / designDistance, 0.4f, 2.5f);

        /// <summary>A rotation whose forward points from the person to <paramref name="position"/>, level, so text there faces the person.</summary>
        public static Quaternion FacingPerson(Vector3 position)
        {
            var away = position - HeadPosition;
            away.y = 0f;
            return away.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>A 20 mm corner radius at any size: a small texture drawn nine-sliced.</summary>
        private static Sprite RoundedSprite
        {
            get
            {
                if (roundedSprite != null) return roundedSprite;
                const int size = 64;
                const float radius = 20f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Halcyonic rounded plate",
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
                        var outside = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                        var alpha = Mathf.Clamp01(0.5f - outside);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                // 1000 pixels per meter: the 20 pixel radius is 20 mm, the 24 pixel border 24 mm.
                roundedSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 1000f, 0,
                    SpriteMeshType.FullRect, new Vector4(24f, 24f, 24f, 24f));
                roundedSprite.name = "Halcyonic rounded plate";
                return roundedSprite;
            }
        }
    }
}
