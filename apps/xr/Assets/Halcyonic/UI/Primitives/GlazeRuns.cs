#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Words from outside on any label (lane V's treatment, ADR 0026): a line's runs set on one label,
    /// cut inside outside text before Halcyonic's words, the letters of outside text slanted by the
    /// font's own italic amount and Halcyonic's left upright, and a ground only Halcyonic draws under
    /// each run where it meets Halcyonic's words. The menu's lines use it, and so can the stage's
    /// banner, plate and glance: one label, one ground a run on each row it reaches.
    /// Its hook is the label's one ordered change to its drawn mesh: lean in flat coordinates, then bend onto
    /// the plane's curve while one is on (<see cref="CurvedDrawing"/>).
    /// </summary>
    public sealed class GlazeRuns : MonoBehaviour
    {
        private TMP_Text label = null!;
        private bool identity;
        private IReadOnlyList<(int Start, int End)> theirs = Array.Empty<(int, int)>();

        /// <summary>The runs a label leans, made the first time a label needs it.</summary>
        public static GlazeRuns Of(TMP_Text label)
        {
            var runs = label.GetComponent<GlazeRuns>();
            if (runs != null) return runs;
            runs = label.gameObject.AddComponent<GlazeRuns>();
            runs.label = label;
            label.OnPreRenderText += runs.Drawn;
            return runs;
        }

        /// <summary>Where outside text stands in the label's text as shown, as its first character and the one past its last.</summary>
        public IReadOnlyList<(int Start, int End)> Theirs => theirs;

        public bool IsNamedIdentity => identity;
        public bool IsAttributedProse => prose;
        private bool prose;

        /// <summary>Explicit candidate prose treatment; never changes literal text or outside ranges.
        /// Every ordinary Set and identity Set resets this treatment for pooled label reuse.</summary>
        public void SetProseMode(bool value)
        {
            SetIdentityMode(false);
            if (prose == value) return;
            prose = value;
            label.havePropertiesChanged = true;
        }

        /// <summary>
        /// Sets <paramref name="words"/> on the label with rich text off and escape parsing on, cut
        /// inside outside text where Halcyonic's words follow it and the whole doesn't
        /// <paramref name="fits"/> (<see cref="LineWords.CutToFit"/>), and leans its outside text.
        /// Returns where outside text stands in the label's text as shown.
        /// </summary>
        public IReadOnlyList<(int Start, int End)> Set(LineWords words, Func<string, bool>? fits = null)
        {
            SetProseMode(false);
            return SetWords(words, fits);
        }

        /// <summary>
        /// A named task or project beneath a separate, host-owned identity caption. The name remains
        /// outside text with literal escaping and tracked ranges; only its italic mesh shear differs.
        /// Never use this for agent prose, decisions, tool output or action labels.
        /// </summary>
        public IReadOnlyList<(int Start, int End)> SetIdentity(string name, Func<string, bool>? fits = null)
        {
            SetProseMode(false);
            SetIdentityMode(true);
            return SetWords(LineWords.TheirName(name), fits);
        }

        private void SetIdentityMode(bool value)
        {
            if (identity == value) return;
            identity = value;
            label.havePropertiesChanged = true;
        }

        private IReadOnlyList<(int Start, int End)> SetWords(LineWords words, Func<string, bool>? fits)
        {
            label.richText = false;
            label.parseCtrlCharacters = true;
            if (fits != null && words.OursAfterTheirs)
            {
                words = words.CutToFit(tried =>
                {
                    var text = tried.ForTextMeshPro().Text;
                    label.text = text;
                    return fits(text);
                });
            }
            var (shown, ranges) = words.ForTextMeshPro();
            label.text = shown;
            Lean(ranges);
            return ranges;
        }

        /// <summary>Leans the label's characters in <paramref name="ranges"/>, by their place in its text; a change draws it again.</summary>
        public void Lean(IReadOnlyList<(int Start, int End)> ranges)
        {
            if (theirs.SequenceEqual(ranges)) return;
            theirs = ranges;
            label.havePropertiesChanged = true;
        }

        /// <summary>
        /// The colour outside text takes where it stands on a ground, or null for the label's own: words
        /// on a ground hold 4.5:1 over the glass on white only in the primary text colour, so a secondary
        /// or toned line's outside text takes it while Halcyonic's words keep the line's colour.
        /// </summary>
        public Color32? Grounded
        {
            get => grounded;
            set
            {
                if (Nullable.Equals(grounded, value)) return;
                grounded = value;
                label.havePropertiesChanged = true;
            }
        }

        private Color32? grounded;

        /// <summary>The label's one hook, as its mesh is made: its outside text leans, in flat coordinates, and then the whole bends.</summary>
        private void Drawn(TMP_TextInfo info)
        {
            LeanTheirs(info);
            BendText(info);
        }

        private void LeanTheirs(TMP_TextInfo info)
        {
            if (theirs.Count == 0) return;
            // The room's faces carry no slant (ART's quiet glass, 2026-10-30): outside text there is told by its place and the mono face.
            if (!identity && !prose && !KitType.IsRoomFace(label.font)) GlazeText.Lean(info, theirs);
            if (!(grounded is Color32 colour)) return;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible || !GlazeText.InRun(info, index, theirs)) continue;
                var colours = info.meshInfo[character.materialReferenceIndex].colors32;
                for (var corner = character.vertexIndex; corner < character.vertexIndex + 4; corner++) colours[corner] = colour;
            }
        }

        private CurvedDrawing? curvedOn;
        private Vector3[][] flat = Array.Empty<Vector3[]>();
        private int[] flatCounts = Array.Empty<int>();
        private bool bent;
        private Matrix4x4 bentAt;
        private int bentVersion = -1;
        private (int Version, Matrix4x4 At, string Text)? tried;

        /// <summary>Whether the label's mesh is drawn on its plane's curve now.</summary>
        public bool Bent => bent;

        /// <summary>Where a vertex of the drawn mesh was laid out, leaned, before the bend, in the label's units, for checks.</summary>
        public Vector3 FlatVertex(int material, int index) => flat[material][index];

        private CurvedDrawing? Curve => curvedOn != null && curvedOn.On ? curvedOn : null;

        /// <summary>
        /// Draws the label on <paramref name="curve"/>'s curve while it is on, or flat with none: called by the plane
        /// after each step. A label that only moved bends again from the flat mesh it kept, without making its text
        /// again; one not yet bent, or whose mesh changed since, is made again, and its hook bends it.
        /// </summary>
        public void DrawOn(CurvedDrawing? curve)
        {
            curvedOn = curve;
            var on = Curve;
            if (on == null)
            {
                tried = null;
                if (!bent) return;
                bent = false;
                label.ForceMeshUpdate();
                return;
            }
            if (!label.isActiveAndEnabled || string.IsNullOrEmpty(label.text)) return;
            var at = label.transform.localToWorldMatrix;
            if (bent && bentVersion == on.Version && bentAt == at) return;
            if (!bent || label.geometrySortingOrder != VertexSortingOrder.Normal || !Kept(label.textInfo))
            {
                // A label whose text makes no mesh never calls its hook: try it once per place and text.
                if (tried is (int version, Matrix4x4 was, string text) && version == on.Version && was == at && text == label.text) return;
                tried = (on.Version, at, label.text);
                label.ForceMeshUpdate();
                return;
            }
            Write(label.textInfo, on);
            label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }

        private void BendText(TMP_TextInfo info)
        {
            var curve = Curve;
            if (curve == null) { bent = false; return; }
            if (flat.Length < info.materialCount)
            {
                Array.Resize(ref flat, info.materialCount);
                Array.Resize(ref flatCounts, info.materialCount);
            }
            for (var material = 0; material < info.materialCount; material++)
            {
                var mesh = info.meshInfo[material];
                var count = mesh.vertices == null ? 0 : mesh.vertexCount;
                if (flat[material] == null || flat[material].Length < count) flat[material] = new Vector3[Mathf.Max(count, 4)];
                if (count > 0) Array.Copy(mesh.vertices!, flat[material], count);
                flatCounts[material] = count;
            }
            Write(info, curve);
        }

        /// <summary>Whether the kept flat mesh still matches the label's: the same vertex count in each of its meshes.</summary>
        private bool Kept(TMP_TextInfo? info)
        {
            if (info == null || flat.Length < info.materialCount) return false;
            for (var material = 0; material < info.materialCount; material++)
                if (flat[material] == null || info.meshInfo[material].vertices == null || flatCounts[material] != info.meshInfo[material].vertexCount) return false;
            return true;
        }

        /// <summary>Writes the kept flat mesh, bent, into the label's: each vertex where the curve puts it, in the label's units.</summary>
        private void Write(TMP_TextInfo info, CurvedDrawing curve)
        {
            var t = label.transform;
            var at = t.localToWorldMatrix;
            var toLocal = t.worldToLocalMatrix;
            for (var material = 0; material < info.materialCount; material++)
            {
                var vertices = info.meshInfo[material].vertices;
                var kept = flat[material];
                for (var index = 0; index < flatCounts[material]; index++)
                    vertices[index] = toLocal.MultiplyPoint3x4(curve.Bend(at.MultiplyPoint3x4(kept[index])));
            }
            bent = true;
            bentAt = at;
            bentVersion = curve.Version;
            tried = null;
        }

        private void OnDestroy()
        {
            if (label != null) label.OnPreRenderText -= Drawn;
        }

        /// <summary>The ground's fill: white, faint on the glass, so the words on it read first.</summary>
        public static Color GroundFill => GlazeTokens.ColorOf(Glaze.Palette.Ground, Glaze.Menu.GroundOpacity);

        /// <summary>The ground's own hairline, so both its edges show.</summary>
        public static Color GroundEdge => GlazeTokens.ColorOf(Glaze.Palette.Ground, Glaze.Menu.GroundHairlineOpacity);

        /// <summary>How far a ground reaches past the words on it each side.</summary>
        public static float GroundPadding => GlazeTokens.Units(Glaze.Menu.GroundPaddingDegrees);

        /// <summary>Draws <paramref name="ground"/> as a ground <paramref name="size"/> big, centred on <paramref name="at"/> in its parent's units, behind words at the same place.</summary>
        public static void DrawGround(Surface ground, Vector2 size, Vector3 at)
        {
            ground.gameObject.SetActive(true);
            ground.Draw(size, Mathf.Min(GlazeTokens.Units(Glaze.Menu.GroundRadiusDegrees), size.y / 2f), GroundFill, GroundEdge,
                GlazeTokens.Units(Glaze.Menu.HairlineDegrees));
            ground.transform.localPosition = at;
        }

        /// <summary>
        /// A ground hugging each run of outside text on <paramref name="label"/>, on each row it reaches, its padding
        /// stopping halfway to a letter of Halcyonic's beside it (<see cref="GlazeText.RunGrounds"/>),
        /// from <paramref name="pool"/>, made under <paramref name="parent"/> at <paramref name="order"/> as
        /// needed; the label and its parent share units. The rest of the pool hides. Returns the grounds drawn.
        /// </summary>
        public static int TokenGrounds(TMP_Text label, IReadOnlyList<(int Start, int End)> theirs, List<Surface> pool, Transform parent, int order)
        {
            var boxes = theirs.Count == 0 ? new List<Rect>() : GlazeText.RunGrounds(label, theirs, GroundPadding);
            while (pool.Count < boxes.Count) pool.Add(Surface.Create(parent, label.name + " ground " + pool.Count, order));
            var at = label.transform.localPosition;
            for (var index = 0; index < pool.Count; index++)
            {
                if (index >= boxes.Count)
                {
                    pool[index].gameObject.SetActive(false);
                    continue;
                }
                var box = boxes[index];
                DrawGround(pool[index], box.size, new Vector3(at.x + box.center.x, at.y + box.center.y, at.z + GlazeTokens.Units(0.01f)));
            }
            return boxes.Count;
        }
    }
}
