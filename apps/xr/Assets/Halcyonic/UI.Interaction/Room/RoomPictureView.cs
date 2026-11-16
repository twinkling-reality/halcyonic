#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The still of a photo or video given to a task, as a card's picture head draws it: the picture filling a square, cropped to
    /// its middle so it is never stretched, its corners the card's tile corners. It only draws a texture it is handed; reading and
    /// decoding are the director's (TaskPictures). Its origin is its middle; its size is in its parent's units.
    /// </summary>
    public sealed class RoomPictureView : MonoBehaviour
    {
        private const string MaterialPath = "HalcyonicUI/RoomPicture";
        private static readonly int PictureId = Shader.PropertyToID("_Picture");
        private static readonly int CropId = Shader.PropertyToID("_Crop");
        private static readonly int CornerId = Shader.PropertyToID("_Corner");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer pictureRenderer = null!;
        private MaterialPropertyBlock block = null!;

        /// <summary>The picture as last drawn; null before.</summary>
        public Texture2D? Picture { get; private set; }

        /// <summary>How wide and tall it stands as last drawn, in the parent's units.</summary>
        public float Side { get; private set; }

        public MeshRenderer Renderer => pictureRenderer;

        public static RoomPictureView Create(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = order;
            var view = go.AddComponent<RoomPictureView>();
            view.pictureRenderer = renderer;
            view.block = new MaterialPropertyBlock();
            go.SetActive(false);
            return view;
        }

        /// <summary>The share of the picture shown so it fills a square: its middle, the longer side cropped.</summary>
        public static Vector4 Crop(int width, int height)
        {
            if (width <= 0 || height <= 0) return new Vector4(1f, 1f, 0f, 0f);
            var x = width > height ? (float)height / width : 1f;
            var y = height > width ? (float)width / height : 1f;
            return new Vector4(x, y, (1f - x) / 2f, (1f - y) / 2f);
        }

        /// <summary>Draws <paramref name="picture"/> <paramref name="side"/> across, its corners <paramref name="corner"/> round (both in the parent's units).</summary>
        public void Show(Texture2D picture, float side, float corner, float alpha = 1f)
        {
            gameObject.SetActive(true);
            Picture = picture;
            Side = side;
            transform.localScale = new Vector3(side, side, 1f);
            block.SetTexture(PictureId, picture);
            block.SetVector(CropId, Crop(picture.width, picture.height));
            block.SetFloat(CornerId, side > 0f ? Mathf.Clamp(corner / side, 0f, 0.5f) : 0f);
            block.SetFloat(AlphaId, Mathf.Clamp01(alpha));
            pictureRenderer.SetPropertyBlock(block);
        }

        public void Hide() => gameObject.SetActive(false);

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                material = Resources.Load<Material>(MaterialPath);
                if (material == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so a task's picture cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    material = new Material(fallback != null ? fallback : Shader.Find("Sprites/Default"));
                }
                return material;
            }
        }

        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Picture" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                quad.UploadMeshData(true);
                return quad;
            }
        }
    }
}
