#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The agent's cursor and its label (DESIGN's BUILD-SPEC-v4 2.3): while the agent works, an arrow (about 26 by 33 dp,
    /// #59CFF1 with a #141416 edge) stands in the working field with one line beside it saying the step under way
    /// (<see cref="RoomWorkStep"/>): the label, in the darker voice glass, its top left 26 dp right of and 30 dp below the
    /// cursor's corner, its words in the one type size under the wait's sweep. Only the label means anything: the arrow
    /// points at nothing. At each new step it glides to its next fixed stop over 600 ms, and between steps it drifts by at
    /// most 0.35 degrees (<see cref="RoomMotion"/>). It fades in and out with the field's 600 ms. Kept still
    /// (<see cref="GlazeMotion.Still"/>), it stands on its stop and is there or gone at once. The arrow and the label's
    /// ground are one quad (<c>Halcyonic/Room Cursor</c>); a frame moves one transform and allocates nothing. Lengths are
    /// dp on the plane; its stops are counted from its own transform, which its owner puts at the field's middle.
    /// </summary>
    public sealed class RoomCursor : MonoBehaviour
    {
        private enum Phase
        {
            Hidden,
            Arriving,
            Standing,
            Leaving,
        }

        /// <summary>The arrow's tip, in from the cursor's corner on both axes; the arrow hangs 26 by 33 dp below and right of it.</summary>
        public const float TipDp = 2.4f;

        /// <summary>The label's top left from the cursor's corner, its padding round its words, and its corner.</summary>
        public const float LabelRightDp = 26f;
        public const float LabelBelowDp = 30f;
        public const float LabelPadXDp = 16f;
        public const float LabelPadYDp = 6f;
        public const float LabelCornerDp = 14f;

        /// <summary>The widest the label's words may run; a longer line is its verb alone.</summary>
        public const float WordsMostDp = 240f;

        /// <summary>The words' colour under the sweep: #E6EAEC.</summary>
        public const int WordsColour = 0xE6EAEC;

        /// <summary>
        /// The design's four stops, in dp from the middle of the design's field, x right and y up: yaw and pitch
        /// (-19, -9), (-13, -12.5), (-20.5, -13) and (-12, -9.5) degrees about a field whose middle is at (-12.84, -11.63).
        /// </summary>
        public static readonly IReadOnlyList<Vector2> DesignStops = new[]
        {
            new Vector2(-98.56f, 42.08f), new Vector2(-2.56f, -13.92f), new Vector2(-122.56f, -21.92f), new Vector2(13.44f, 34.08f),
        };

        private const string MaterialPath = "HalcyonicUI/RoomCursor";
        private const float QuadMarginDp = 4f;
        private static readonly int QuadId = Shader.PropertyToID("_Quad");
        private static readonly int LabelId = Shader.PropertyToID("_Label");
        private static readonly int CornerId = Shader.PropertyToID("_Corner");
        private static readonly int ShownId = Shader.PropertyToID("_Shown");
        private static Material? material;
        private static Mesh? quad;

        private Transform pointer = null!;
        private MeshRenderer ground = null!;
        private MaterialPropertyBlock block = null!;
        private TextMeshPro label = null!;
        private GlazeShimmer sweep = null!;
        private Phase phase;
        private string? key;
        private string words = "";
        private int stop;
        private Vector2 from;
        private float stepSince = float.NegativeInfinity;
        private float fadeSince;
        private float born;
        private float shownFrom;
        private float shown;
        private Vector2 labelSize;
        private int laidVersion;
        private (float Shown, Vector2 Label)? painted;
        private float textShown = -1f;

        /// <summary>The seconds its glide, its drift and its fade are timed by; a render fixes it.</summary>
        public Func<float> Clock { get; set; } = () => Time.unscaledTime;

        /// <summary>Its stops, in dp from its own transform, x right and y up; each new step goes to the next, round again.</summary>
        public IReadOnlyList<Vector2> Stops { get; set; } = DesignStops;

        /// <summary>The label's words now; empty while it is hidden.</summary>
        public string Words => phase == Phase.Hidden ? "" : words;

        /// <summary>Which stop it stands on or glides to, and where its corner is now, in dp from its transform.</summary>
        public int Stop => stop;

        public Vector2 Corner => new Vector2(pointer.localPosition.x, pointer.localPosition.y) / Dp;

        /// <summary>How far into view it is, 0 to 1.</summary>
        public float Shown => shown;

        /// <summary>The label's ground as laid, in dp.</summary>
        public Vector2 LabelSize => labelSize;

        /// <summary>The label that draws the words, their sweep, and what draws the arrow and the label's ground.</summary>
        public TextMeshPro Label => label;

        public GlazeShimmer Sweep => sweep;

        public MeshRenderer Ground => ground;

        /// <summary>One dp on the plane, in the plane's units.</summary>
        private static float Dp => RoomType.Units(1f);

        /// <summary>A cursor under <paramref name="parent"/>, hidden; its stops are counted from its transform. Drawn at <paramref name="order"/>, its words over it.</summary>
        public static RoomCursor Create(Transform parent, int order = KitType.Order.Control)
        {
            var go = new GameObject("Agent cursor");
            go.transform.SetParent(parent, false);
            var cursor = go.AddComponent<RoomCursor>();
            cursor.pointer = new GameObject("Pointer").transform;
            cursor.pointer.SetParent(go.transform, false);
            var groundGo = new GameObject("Arrow and label");
            groundGo.transform.SetParent(cursor.pointer, false);
            groundGo.AddComponent<MeshFilter>().sharedMesh = Quad;
            cursor.ground = groundGo.AddComponent<MeshRenderer>();
            cursor.ground.sharedMaterial = Material;
            cursor.ground.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cursor.ground.receiveShadows = false;
            cursor.ground.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            cursor.ground.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            cursor.ground.sortingOrder = order;
            cursor.block = new MaterialPropertyBlock();
            cursor.label = RoomType.Create(cursor.pointer, "Words", RoomSurface.Hex(WordsColour), order + 2);
            cursor.label.textWrappingMode = TextWrappingModes.NoWrap;
            cursor.label.overflowMode = TextOverflowModes.Overflow;
            cursor.label.characterSpacing = RoomWords.Tracking;
            cursor.sweep = GlazeShimmer.On(cursor.label);
            cursor.sweep.Waits = true;
            go.SetActive(false);
            return cursor;
        }

        /// <summary>
        /// Shows the step under way: its verb and its file's name where the whole line fits, else its verb alone. A step
        /// with a new key sends the cursor to its next stop. No step makes it leave.
        /// </summary>
        public void Show(RoomWorkStep? step)
        {
            if (step == null)
            {
                Leave();
                return;
            }
            Show(step.Key, step.Words(Fits));
        }

        /// <summary>Whether a line of words fits the label whole at the person's text size.</summary>
        public bool Fits(string line)
        {
            RoomType.Size(label);
            return label.GetPreferredValues(LabelText.ForTextMeshPro(line)).x <= WordsMostDp * Dp;
        }

        /// <summary>Shows Halcyonic's own <paramref name="line"/> for the step <paramref name="stepKey"/>; a new key is a new step.</summary>
        public void Show(string stepKey, string line)
        {
            var now = Clock();
            if (phase == Phase.Hidden)
            {
                gameObject.SetActive(true);
                born = now;
                stop = 0;
                from = StopAt(0);
                stepSince = float.NegativeInfinity;
                shown = 0f;
            }
            else if (key != stepKey)
            {
                // From where it is on its way, to the next stop round.
                from = Glided(now);
                stop = (stop + 1) % Mathf.Max(1, Stops.Count);
                stepSince = now;
            }
            key = stepKey;
            if (phase == Phase.Hidden || phase == Phase.Leaving)
            {
                shownFrom = shown;
                fadeSince = now;
                phase = Phase.Arriving;
            }
            if (line != words || laidVersion != GlazeText.Version || labelSize == Vector2.zero) Lay(line);
            Draw();
        }

        /// <summary>Fades the cursor out, as when the run ends.</summary>
        public void Leave()
        {
            if (phase == Phase.Hidden || phase == Phase.Leaving) return;
            shownFrom = shown;
            fadeSince = Clock();
            phase = Phase.Leaving;
            Draw();
        }

        /// <summary>Gone at once.</summary>
        public void Hide()
        {
            phase = Phase.Hidden;
            key = null;
            shown = 0f;
            painted = null;
            textShown = -1f;
            gameObject.SetActive(false);
        }

        /// <summary>Its fade and its glide taken to their ends at once: for renders, and for a first frame that must be whole.</summary>
        public void Settle()
        {
            if (phase == Phase.Hidden) return;
            fadeSince = float.NegativeInfinity;
            stepSince = float.NegativeInfinity;
            Draw();
        }

        private Vector2 StopAt(int index) => Stops.Count == 0 ? Vector2.zero : Stops[index % Stops.Count];

        /// <summary>Where its corner is on its glide at <paramref name="now"/>, in dp, its drift apart.</summary>
        private Vector2 Glided(float now) =>
            Vector2.Lerp(from, StopAt(stop), GlazeMotion.Still ? 1f : RoomMotion.CursorGlides(Mathf.Max(0f, now - stepSince)));

        /// <summary>The words in the one type size on one line, and the ground their size with its padding.</summary>
        private void Lay(string line)
        {
            words = line;
            laidVersion = GlazeText.Version;
            var scale = GlazeText.Scale;
            RoomType.Size(label);
            GlazeText.SetLiteral(label, line);
            var width = label.GetPreferredValues(label.text).x;
            var pitch = KitType.LinePitch(label);
            label.rectTransform.sizeDelta = new Vector2(width + Dp, pitch);
            label.ForceMeshUpdate(true);
            width = WordsWidth(label, width);
            labelSize = new Vector2(width / Dp + 2f * LabelPadXDp * scale, (RoomGeometry.LineDp + 2f * LabelPadYDp) * scale);
            // A line's letters stand in the middle of its 28 dp, as the page lays them.
            var above = (LabelBelowDp + LabelPadYDp * scale) * Dp + (pitch - GlazeText.LineHeight(label)) / 2f;
            label.transform.localPosition = new Vector3((LabelRightDp + LabelPadXDp * scale) * Dp, -above, -RoomType.Units(0.5f));
            painted = null;
        }

        /// <summary>
        /// A laid line's own width, from its first letter's start to its last letter's advance, as a page measures a line;
        /// <paramref name="otherwise"/> where it holds no letter. TextMeshPro's preferred width runs a few dp past it.
        /// </summary>
        public static float WordsWidth(TMP_Text line, float otherwise)
        {
            var info = line.textInfo;
            if (info == null || info.characterCount == 0) return otherwise;
            return info.characterInfo[info.characterCount - 1].xAdvance - info.characterInfo[0].origin;
        }

        private void Update() => Draw();

        /// <summary>Draws the cursor where it stands at its clock's time; called every frame, and by a render at the times it draws.</summary>
        public void Draw()
        {
            if (phase == Phase.Hidden) return;
            if (laidVersion != GlazeText.Version) Lay(words);
            var now = Clock();
            var still = GlazeMotion.Still;
            var passed = still ? float.PositiveInfinity : Mathf.Max(0f, now - fadeSince);
            if (phase == Phase.Arriving)
            {
                shown = Mathf.Lerp(shownFrom, 1f, RoomMotion.FieldArrives(passed));
                if (passed >= RoomMotion.FieldSeconds) phase = Phase.Standing;
            }
            else if (phase == Phase.Leaving)
            {
                shown = shownFrom * RoomMotion.FieldLeaves(passed);
                if (passed >= RoomMotion.FieldSeconds)
                {
                    Hide();
                    return;
                }
            }
            else shown = 1f;

            var at = Glided(now);
            if (!still)
            {
                var (right, up) = RoomMotion.CursorDrift(Mathf.Max(0f, now - born));
                at += new Vector2(right, up) * RoomGeometry.DpPerDegree;
            }
            var place = new Vector3(at.x * Dp, at.y * Dp, 0f);
            if (pointer.localPosition != place) pointer.localPosition = place;
            if (!Mathf.Approximately(textShown, shown))
            {
                textShown = shown;
                GlazeText.Show(label, shown);
            }
            Paint();
        }

        /// <summary>The quad round the arrow and the label's ground, set again only when the label's size or the fade changed.</summary>
        private void Paint()
        {
            if (painted is var (lastShown, lastLabel) && Mathf.Approximately(lastShown, shown) && lastLabel == labelSize) return;
            painted = (shown, labelSize);
            var left = -QuadMarginDp;
            var right = LabelRightDp + labelSize.x + QuadMarginDp;
            var top = QuadMarginDp;
            var bottom = -Mathf.Max(40f, LabelBelowDp + labelSize.y) - QuadMarginDp;
            ground.transform.localScale = new Vector3((right - left) * Dp, (top - bottom) * Dp, 1f);
            ground.transform.localPosition = new Vector3((left + right) / 2f * Dp, (top + bottom) / 2f * Dp, RoomType.Units(0.02f));
            block.SetVector(QuadId, new Vector4(left, bottom, right - left, top - bottom));
            block.SetVector(LabelId, new Vector4(LabelRightDp, -LabelBelowDp, labelSize.x, labelSize.y));
            block.SetFloat(CornerId, LabelCornerDp);
            block.SetFloat(ShownId, shown);
            ground.SetPropertyBlock(block);
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                var loaded = Resources.Load<Material>(MaterialPath);
                if (loaded == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the agent's cursor cannot render.");
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
                quad = new Mesh { name = "Room cursor" };
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
        }
    }
}
