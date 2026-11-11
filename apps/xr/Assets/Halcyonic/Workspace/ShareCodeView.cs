#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// A live share's code to scan (ADR 0054; <see cref="ShareCells"/>): its cells as a point-filtered texture, black on
    /// white with the light margin round it, on a sprite (Sprites/Default, which every build carries), so a phone or
    /// another headset can read it from the panel. It draws only the cells the computer encoded: no address is ever
    /// written here, and a malformed code never reaches it.
    /// </summary>
    public sealed class ShareCodeView : MonoBehaviour
    {
        private SpriteRenderer? sprite;
        private Texture2D? texture;

        /// <summary>The renderer, for checks.</summary>
        public SpriteRenderer? Renderer => sprite;

        /// <summary>The side drawn, margin included, in the parent's units.</summary>
        public float Side { get; private set; }

        public static ShareCodeView Create(Transform parent, int order)
        {
            var host = new GameObject("Share code");
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<ShareCodeView>();
            view.sprite = host.AddComponent<SpriteRenderer>();
            view.sprite.sortingOrder = order;
            return view;
        }

        /// <summary>Draws <paramref name="cells"/> <paramref name="side"/> units a side, margin included, centred on the view.</summary>
        public void Show(ShareCells cells, float side)
        {
            var drawn = cells.Drawn;
            if (texture == null || texture.width != drawn)
            {
                if (texture != null) DestroyNow(texture);
                texture = new Texture2D(drawn, drawn, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Share code cells" };
            }
            var pixels = new Color32[drawn * drawn];
            var light = new Color32(255, 255, 255, 255);
            var dark = new Color32(0, 0, 0, 255);
            for (var y = 0; y < drawn; y++)
            for (var x = 0; x < drawn; x++)
            {
                // The texture's rows run bottom up; the code's run top down.
                var row = drawn - 1 - y - ShareCells.Margin;
                var column = x - ShareCells.Margin;
                var inside = row >= 0 && row < cells.Size && column >= 0 && column < cells.Size;
                pixels[y * drawn + x] = inside && cells.Dark(row, column) ? dark : light;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            if (sprite!.sprite != null) DestroyNow(sprite.sprite);
            sprite.sprite = Sprite.Create(texture, new Rect(0f, 0f, drawn, drawn), new Vector2(0.5f, 0.5f), drawn / side, 0, SpriteMeshType.FullRect);
            Side = side;
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        private void OnDestroy()
        {
            if (sprite != null && sprite.sprite != null) DestroyNow(sprite.sprite);
            if (texture != null) DestroyNow(texture);
        }

        private static void DestroyNow(Object thing)
        {
            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }
    }
}
