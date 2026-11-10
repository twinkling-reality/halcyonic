#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The agent's own words low in the view (DESIGN's BUILD-SPEC-v4 1.6, FINISH-2 2026-11-05): game dialogue. One faded
    /// ground (<c>Halcyonic/Room Band</c>: #121416, gone at its edges, whole under every word), 880 dp wide, and on it the
    /// words alone, white, centred in a 600 dp measure, in the one type size, at most three lines a part
    /// (<see cref="RoomWords"/>). It holds nothing else: no name, no chip, no time. Its origin is the middle of its top edge
    /// and it grows downward, so a line never moves the ones above it. The ground fades in over 360 ms and each line then
    /// rises 8 dp into place, 60 ms after the one above; a next part or newer words send the old lines up and out, ease the
    /// height, then bring the new lines in; it leaves by fading (<see cref="RoomMotion"/>). Kept still
    /// (<see cref="GlazeMotion.Still"/>), each of these is there at once. It moves by its own clock and draws again only
    /// while something moves. Lengths are dp on the plane, each 1 dp long at the plane's middle.
    /// </summary>
    public sealed class RoomBand : MonoBehaviour
    {
        private enum Phase
        {
            Hidden,
            Moving,
            Standing,
            Leaving,
        }

        private const string MaterialPath = "HalcyonicUI/RoomBand";
        private static readonly int BandId = Shader.PropertyToID("_Band");
        private static readonly int GroundId = Shader.PropertyToID("_Ground");
        private static readonly int ShownId = Shader.PropertyToID("_Shown");
        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer ground = null!;
        private MaterialPropertyBlock block = null!;

        /// <summary>The words that stand or arrive, and the ones they replace while those leave.</summary>
        private Leaf front = null!;
        private Leaf back = null!;
        private Phase phase;
        private float since;

        /// <summary>Where the move under way started from: the ground's and the old lines' place in view, and the height.</summary>
        private float groundFrom;
        private float backFrom;
        private float frontFrom;
        private float heightFrom;
        private float heightTo;

        /// <summary>How long after the move's start the new lines' turn starts, and whether the height waits for old lines.</summary>
        private float linesAfter;
        private bool replacing;

        private float groundShown;
        private float height;
        private int laidVersion;
        private (float Shown, float Height)? painted;

        /// <summary>The seconds every move is timed by; a render fixes it.</summary>
        public Func<float> Clock { get; set; } = () => Time.unscaledTime;

        /// <summary>The paging of the words it holds: its part, its parts, and the product's way to the next one; null while it holds none.</summary>
        public RoomWords? Words => phase == Phase.Hidden || phase == Phase.Leaving ? null : front.Words;

        /// <summary>The words it holds, as laid (<see cref="RoomWords.Laid"/>); empty while it holds none.</summary>
        public string Text => phase == Phase.Hidden || phase == Phase.Leaving ? "" : front.Laid;

        /// <summary>The band as it stands once every move ends, in its parent's units: it hangs from its origin, its top edge's middle.</summary>
        public Vector2 Size => new Vector2(RoomWords.WidthDp * Dp, phase == Phase.Hidden || phase == Phase.Leaving ? 0f : heightTo);

        /// <summary>Whether anything of it moves now.</summary>
        public bool Moving => phase == Phase.Moving || phase == Phase.Leaving;

        /// <summary>What draws the ground, and the label that draws the words.</summary>
        public MeshRenderer Ground => ground;

        public TextMeshPro Label => front.Label;

        /// <summary>How far into view the ground is now, 0 to 1, and how tall it is drawn, in its parent's units.</summary>
        public float GroundShown => groundShown;

        public float Height => height;

        /// <summary>How line <paramref name="line"/> of the part that shows stands now.</summary>
        public RoomMove LineMove(int line) => front.Moves[line];

        /// <summary>One dp on the plane, in the plane's units.</summary>
        private static float Dp => RoomType.Units(1f);

        private static readonly Dictionary<string, Material?> underlays = new Dictionary<string, Material?>();

        /// <summary>
        /// Puts the words' underlay on a label drawn in one of the room's faces (DESIGN's BUILD-SPEC-v4 1.2 and 1.4):
        /// TextMeshPro's own underlay, black at 28 percent, or 35 on an <paramref name="action"/>'s words, with no offset,
        /// as soft as the face's atlas allows. It is a preset of the face's material (Resources/HalcyonicUI, the face's name
        /// then "Underlay" or "Underlay Action"), shared by every label that takes it. False where the face has none.
        /// </summary>
        public static bool Underlay(TMP_Text label, bool action = false)
        {
            if (label.font == null) return false;
            var name = label.font.name + (action ? " Underlay Action" : " Underlay");
            if (!underlays.TryGetValue(name, out var preset)) underlays[name] = preset = Resources.Load<Material>("HalcyonicUI/" + name);
            if (preset == null) return false;
            if (label.fontSharedMaterial != preset) label.fontSharedMaterial = preset;
            return true;
        }

        /// <summary>A band under <paramref name="parent"/>, hidden, its origin the middle of its top edge; the ground draws at <paramref name="order"/> and the words over it.</summary>
        public static RoomBand Create(Transform parent, int order = KitType.Order.Plate)
        {
            var go = new GameObject("Agent words");
            go.transform.SetParent(parent, false);
            var band = go.AddComponent<RoomBand>();
            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(go.transform, false);
            groundGo.AddComponent<MeshFilter>().sharedMesh = Quad;
            band.ground = groundGo.AddComponent<MeshRenderer>();
            band.ground.sharedMaterial = Material;
            band.ground.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            band.ground.receiveShadows = false;
            band.ground.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            band.ground.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            band.ground.sortingOrder = order;
            band.block = new MaterialPropertyBlock();
            band.front = new Leaf(go.transform, "Words", order + 2);
            band.back = new Leaf(go.transform, "Words leaving", order + 2);
            go.SetActive(false);
            return band;
        }

        /// <summary>
        /// Shows what the agent said, or its question's prompt, from its first part: arriving where the band holds nothing,
        /// replacing what it holds otherwise. The same words again change nothing; no words at all make it leave.
        /// </summary>
        public void Show(string? said)
        {
            var laid = RoomWords.Laid(said);
            if (laid.Length == 0)
            {
                Leave();
                return;
            }
            if (phase != Phase.Hidden && phase != Phase.Leaving && front.Laid == laid) return;
            Turn(laid, 0);
        }

        /// <summary>Turns to the words' next part, the old lines leaving first; false where there is none.</summary>
        public bool Next()
        {
            if (Words is not RoomWords words || !words.More) return false;
            Turn(front.Laid, words.Part + 1);
            return true;
        }

        /// <summary>Fades the band out with its words, as when Send is pressed or a run starts.</summary>
        public void Leave()
        {
            if (phase == Phase.Hidden || phase == Phase.Leaving) return;
            groundFrom = groundShown;
            frontFrom = front.Moves[0].Shown;
            back.Clear();
            phase = Phase.Leaving;
            since = Clock();
            Draw();
        }

        /// <summary>Gone at once, with no move.</summary>
        public void Hide()
        {
            phase = Phase.Hidden;
            front.Clear();
            back.Clear();
            groundShown = 0f;
            painted = null;
            gameObject.SetActive(false);
        }

        /// <summary>Every move under way taken to its end at once: for renders, and for a first frame that must be whole.</summary>
        public void Settle()
        {
            if (phase == Phase.Hidden) return;
            since = float.NegativeInfinity;
            Draw();
        }

        private void Turn(string laid, int part)
        {
            var now = Clock();
            var held = phase != Phase.Hidden;
            if (!held) gameObject.SetActive(true);
            if (held && front.Shows)
            {
                // The lines that show leave upward; the newer ones wait for them and for the height.
                (front, back) = (back, front);
                front.Label.name = "Words";
                back.Label.name = "Words leaving";
                backFrom = back.Moves[0].Shown;
                front.Lay(laid, part);
                groundFrom = groundShown;
                heightFrom = height;
                heightTo = HeightOf(front);
                replacing = true;
                linesAfter = RoomMotion.ReplaceLinesAfterSeconds;
                since = now;
            }
            else if (held && phase == Phase.Moving)
            {
                // No line shows yet: the newer words take the waiting ones' place, and the move goes on.
                front.Lay(laid, part);
                if (!replacing) heightFrom = HeightOf(front);
                heightTo = HeightOf(front);
            }
            else
            {
                // Onto nothing: the ground fades in at its height and the lines follow.
                front.Lay(laid, part);
                back.Clear();
                groundFrom = held ? groundShown : 0f;
                heightFrom = heightTo = HeightOf(front);
                replacing = false;
                linesAfter = RoomMotion.BandLinesAfterSeconds;
                since = now;
            }
            laidVersion = GlazeText.Version;
            phase = Phase.Moving;
            Draw();
        }

        private static float HeightOf(Leaf leaf) => RoomWords.HeightDp(leaf.Words?.Count ?? 0, GlazeText.Scale) * Dp;

        private void Update() => Draw();

        /// <summary>Draws the band as it stands at its clock's time; called every frame, and by a render at the times it draws.</summary>
        public void Draw()
        {
            if (phase == Phase.Hidden) return;
            if (laidVersion != GlazeText.Version && phase != Phase.Leaving)
            {
                // The person changed their text size: the words reflow at once, on the part they were on.
                laidVersion = GlazeText.Version;
                front.Lay(front.Laid, front.Words?.Part ?? 0);
                heightFrom = heightTo = HeightOf(front);
                painted = null;
                if (phase == Phase.Standing) phase = Phase.Moving;
            }
            if (phase == Phase.Standing) return;
            var seconds = GlazeMotion.Still ? float.PositiveInfinity : Mathf.Max(0f, Clock() - since);
            if (phase == Phase.Leaving)
            {
                var left = RoomMotion.BandLeaves(seconds);
                groundShown = groundFrom * left;
                front.Set(new RoomMove(frontFrom * left, 0f));
                front.Upload();
                Paint();
                if (seconds >= RoomMotion.BandSeconds) Hide();
                return;
            }
            groundShown = Mathf.Lerp(groundFrom, 1f, RoomMotion.BandArrives(seconds));
            height = replacing ? RoomMotion.BandHeight(seconds, heightFrom, heightTo) : heightTo;
            var count = front.Words?.Count ?? 0;
            for (var line = 0; line < RoomWords.LinesAPart; line++)
                front.Moves[line] = line < count ? RoomMotion.LineArrives(seconds - linesAfter, line) : RoomMove.Gone;
            front.Upload();
            if (back.Shows || back.Label.gameObject.activeSelf)
            {
                var leaves = RoomMotion.LineLeaves(seconds);
                if (leaves.Shown <= 0f || backFrom <= 0f) back.Clear();
                else
                {
                    back.Set(new RoomMove(backFrom * leaves.Shown, leaves.LowerDp));
                    back.Upload();
                }
            }
            Paint();
            var ends = Mathf.Max(RoomMotion.BandSeconds, linesAfter + Mathf.Max(0, count - 1) * RoomMotion.LineApartSeconds + RoomMotion.LineSeconds);
            if (seconds >= ends) phase = Phase.Standing;
        }

        /// <summary>The ground at its size and its place in view, drawn again only when either changed.</summary>
        private void Paint()
        {
            if (painted is var (shown, tall) && Mathf.Approximately(shown, groundShown) && Mathf.Approximately(tall, height)) return;
            painted = (groundShown, height);
            var width = RoomWords.WidthDp * Dp;
            ground.transform.localScale = new Vector3(width, Mathf.Max(height, 1e-5f), 1f);
            ground.transform.localPosition = new Vector3(0f, -height / 2f, RoomType.Units(0.02f));
            var colour = RoomSurface.Hex(RoomWords.GroundColour);
            block.SetVector(BandId, new Vector4(RoomWords.WidthDp, height / Dp, RoomWords.SideFadeDp, RoomWords.EdgeFadeDp * GlazeText.Scale));
            block.SetVector(GroundId, new Vector4(colour.r, colour.g, colour.b, RoomWords.GroundAlpha));
            block.SetFloat(ShownId, groundShown);
            ground.SetPropertyBlock(block);
        }

        /// <summary>
        /// One laying of words: a label that holds all of them, laid once at the measure, of which only the part that shows
        /// is drawn. The other lines' letters are folded away, and the part's are lifted to the band's first rows and moved
        /// and faded line by line in the mesh TextMeshPro already built, so a move adds no mesh and allocates nothing a frame.
        /// </summary>
        private sealed class Leaf
        {
            public readonly TextMeshPro Label;
            public readonly RoomMove[] Moves = new RoomMove[RoomWords.LinesAPart];
            public RoomWords? Words;
            public string Laid = "";

            public Leaf(Transform parent, string name, int order)
            {
                Label = RoomType.Create(parent, name, Color.white, order, TextAlignmentOptions.Top);
                Label.textWrappingMode = TextWrappingModes.Normal;
                // Never cut: what does not fit a part is the next part.
                Label.overflowMode = TextOverflowModes.Overflow;
                Label.characterSpacing = RoomWords.Tracking;
                Underlay(Label);
                // Whenever TextMeshPro builds the mesh again, the part is placed in it before it is drawn.
                Label.OnPreRenderText += info => Place(info, everything: true);
                Label.gameObject.SetActive(false);
            }

            /// <summary>Whether any of its lines is in view at all.</summary>
            public bool Shows
            {
                get
                {
                    if (Words == null || !Label.gameObject.activeSelf) return false;
                    for (var line = 0; line < Words.Count; line++)
                        if (Moves[line].Shown > 0f) return true;
                    return false;
                }
            }

            public void Set(RoomMove move)
            {
                for (var line = 0; line < Moves.Length; line++) Moves[line] = move;
            }

            public void Clear()
            {
                Words = null;
                Laid = "";
                Set(RoomMove.Gone);
                if (Label.gameObject.activeSelf) Label.gameObject.SetActive(false);
            }

            /// <summary>Lays <paramref name="laid"/> at the measure and the person's text size and turns to <paramref name="part"/>, its lines not in view yet.</summary>
            public void Lay(string laid, int part)
            {
                Laid = laid;
                Words = null;
                Set(RoomMove.Gone);
                Label.gameObject.SetActive(true);
                RoomType.Size(Label);
                Label.richText = false;
                Label.parseCtrlCharacters = true;
                Label.text = laid;
                var pitch = KitType.LinePitch(Label);
                var box = new Vector2(RoomWords.MeasureDp * Dp, RoomWords.LinesAPart * pitch);
                if (Label.rectTransform.sizeDelta != box) Label.rectTransform.sizeDelta = box;
                // The first line's box starts under the band's padding: a line's letters stand in the middle of its 28 dp, as the page lays them.
                var above = RoomWords.PadDp * Dp * GlazeText.Scale + (pitch - GlazeText.LineHeight(Label)) / 2f;
                Label.transform.localPosition = new Vector3(-box.x / 2f, -above, -RoomType.Units(0.5f));
                Label.ForceMeshUpdate(true);
                Words = RoomWords.Of(Label.textInfo.characterCount == 0 ? 0 : Label.textInfo.lineCount, part);
                Place(Label.textInfo, everything: true);
                Push();
            }

            /// <summary>The part's lines where their moves put them now, sent to the mesh.</summary>
            public void Upload()
            {
                if (Words == null || !Label.gameObject.activeSelf) return;
                Place(Label.textInfo, everything: false);
                Push();
            }

            private void Push() => Label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);

            /// <summary>
            /// Puts the part's lines in the label's mesh: each lifted to its row from the band's top, lowered by its move and
            /// faded by it. With <paramref name="everything"/>, every other line's letters are folded to nothing first.
            /// </summary>
            private void Place(TMP_TextInfo info, bool everything)
            {
                if (info == null || info.characterCount == 0) return;
                var first = Words?.First ?? 0;
                var count = Words?.Count ?? 0;
                if (everything)
                {
                    for (var index = 0; index < info.characterCount; index++)
                    {
                        var character = info.characterInfo[index];
                        var line = character.lineNumber - first;
                        if (!character.isVisible || (line >= 0 && line < count)) continue;
                        var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                        for (var corner = 0; corner < 4; corner++) vertices[character.vertexIndex + corner] = Vector3.zero;
                    }
                }
                if (count == 0 || first >= info.lineCount) return;
                var lift = info.lineInfo[0].baseline - info.lineInfo[first].baseline;
                var dp = Dp * GlazeText.Scale;
                for (var line = 0; line < count && first + line < info.lineCount; line++)
                {
                    var row = info.lineInfo[first + line];
                    var move = Moves[line];
                    var shift = new Vector3(0f, lift - move.LowerDp * dp, 0f);
                    var shown = Mathf.Clamp01(move.Shown);
                    for (var index = row.firstCharacterIndex; index <= row.lastCharacterIndex && index < info.characterCount; index++)
                    {
                        var character = info.characterInfo[index];
                        if (!character.isVisible) continue;
                        var mesh = info.meshInfo[character.materialReferenceIndex];
                        var at = character.vertexIndex;
                        mesh.vertices[at] = character.vertex_BL.position + shift;
                        mesh.vertices[at + 1] = character.vertex_TL.position + shift;
                        mesh.vertices[at + 2] = character.vertex_TR.position + shift;
                        mesh.vertices[at + 3] = character.vertex_BR.position + shift;
                        var own = character.color;
                        var colour = new Color32(own.r, own.g, own.b, (byte)Mathf.RoundToInt(own.a * shown));
                        for (var corner = 0; corner < 4; corner++) mesh.colors32[at + corner] = colour;
                    }
                }
            }
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                var loaded = Resources.Load<Material>(MaterialPath);
                if (loaded == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the agent's words band cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    material = new Material(fallback != null ? fallback : Shader.Find("Sprites/Default"));
                    return material;
                }
                material = loaded;
                return material;
            }
        }

        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Room band" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                quad.UploadMeshData(true);
                return quad;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            material = null;
            quad = null;
            underlays.Clear();
        }
    }
}
