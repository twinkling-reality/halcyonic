#nullable enable
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Text, plates and lines not yet drawn by the interface's components (ADR 0023): the onboarding
    /// hint, the head gaze reticle, and the link and ring of a workspace opening, built at runtime;
    /// and where the person's eyes are.
    ///
    /// The hint's size is chosen for the Quest 3, about 25 pixels per degree at the lens center, at
    /// <see cref="PeekDistance"/>, and scaled by its actual distance over that, so it keeps the same
    /// angular size wherever the stage puts its characters. A TextMeshPro font size is the em height
    /// in decimeters, so 0.30 draws a 30 mm em.
    /// </summary>
    internal static class WorkspaceVisuals
    {
        /// <summary>The distance the peek's size is designed for.</summary>
        public const float PeekDistance = 1.6f;

        /// <summary>At <see cref="PeekDistance"/>, a 30 mm em keeps a 14 pixel x-height.</summary>
        public const float PeekSize = 0.30f;

        // Transparent renderers draw in sorting order before distance; these draw before every panel.
        public const int ControlOrder = 1;
        public const int TextOrder = 2;

        public static readonly Color TextColor = new Color(0.93f, 0.95f, 0.96f);
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

        /// <summary>World-space text that never interprets markup.</summary>
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
