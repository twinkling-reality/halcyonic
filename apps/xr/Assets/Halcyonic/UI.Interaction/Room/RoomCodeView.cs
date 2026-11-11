#nullable enable
using Halcyonic.Client;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A code to scan (<see cref="RoomCode"/>) as the card draws it: its dark cells, square and unfiltered, on one quad the
    /// code's own size; the light well and its margin are the card's tile under it. It only draws the host's cells, never
    /// reads or makes a code. Its origin is its middle; its size is in its parent's units.
    /// </summary>
    public sealed class RoomCodeView : MonoBehaviour
    {
        private const string MaterialPath = "HalcyonicUI/RoomCode";
        private static readonly int CellsId = Shader.PropertyToID("_Cells");
        private static readonly int ColourId = Shader.PropertyToID("_Colour");
        private static readonly int SideId = Shader.PropertyToID("_Side");
        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer codeRenderer = null!;
        private MaterialPropertyBlock block = null!;
        private Texture2D? cells;
        private RoomCode? drawn;

        /// <summary>The code as last drawn; null before.</summary>
        public RoomCode? Code => drawn;

        /// <summary>How wide and how tall the cells stand as last drawn, in the parent's units, margin not counted.</summary>
        public float Side { get; private set; }

        public MeshRenderer Renderer => codeRenderer;

        /// <summary>A code's cells under <paramref name="parent"/>, drawn at <paramref name="order"/>, hidden until drawn.</summary>
        public static RoomCodeView Create(Transform parent, string name, int order)
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
            var view = go.AddComponent<RoomCodeView>();
            view.codeRenderer = renderer;
            view.block = new MaterialPropertyBlock();
            go.SetActive(false);
            return view;
        }

        /// <summary>Draws <paramref name="code"/> <paramref name="side"/> across, its dark cells in <paramref name="ink"/> (sRGB).</summary>
        public void Show(RoomCode code, float side, Color ink)
        {
            gameObject.SetActive(true);
            Side = side;
            transform.localScale = new Vector3(side, side, 1f);
            if (!ReferenceEquals(code, drawn))
            {
                if (cells == null || cells.width != code.Side)
                {
                    Release(cells);
                    cells = new Texture2D(code.Side, code.Side, TextureFormat.Alpha8, false)
                    {
                        name = "Code cells",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        anisoLevel = 0,
                    };
                }
                // A texture's first row is its bottom; the code's is its top.
                var bytes = new byte[code.Side * code.Side];
                for (var row = 0; row < code.Side; row++)
                for (var column = 0; column < code.Side; column++)
                    bytes[(code.Side - 1 - row) * code.Side + column] = code.IsDark(column, row) ? (byte)255 : (byte)0;
                cells.SetPixelData(bytes, 0);
                cells.Apply(false, false);
                drawn = code;
            }
            block.SetTexture(CellsId, cells);
            block.SetVector(ColourId, new Vector4(ink.r, ink.g, ink.b, ink.a));
            block.SetFloat(SideId, code.Side);
            codeRenderer.SetPropertyBlock(block);
        }

        public void Hide() => gameObject.SetActive(false);

        private void OnDestroy() => Release(cells);

        private static void Release(Texture2D? texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                material = Resources.Load<Material>(MaterialPath);
                if (material == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so a code to scan cannot render.");
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
                quad = new Mesh { name = "Code" };
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
