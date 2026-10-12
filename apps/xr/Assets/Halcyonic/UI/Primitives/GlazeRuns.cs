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
    /// </summary>
    public sealed class GlazeRuns : MonoBehaviour
    {
        private TMP_Text label = null!;
        private IReadOnlyList<(int Start, int End)> theirs = Array.Empty<(int, int)>();

        /// <summary>The runs a label leans, made the first time a label needs it.</summary>
        public static GlazeRuns Of(TMP_Text label)
        {
            var runs = label.GetComponent<GlazeRuns>();
            if (runs != null) return runs;
            runs = label.gameObject.AddComponent<GlazeRuns>();
            runs.label = label;
            label.OnPreRenderText += runs.LeanTheirs;
            return runs;
        }

        /// <summary>Where outside text stands in the label's text as shown, as its first character and the one past its last.</summary>
        public IReadOnlyList<(int Start, int End)> Theirs => theirs;

        /// <summary>
        /// Sets <paramref name="words"/> on the label with rich text off and escape parsing on, cut
        /// inside outside text where Halcyonic's words follow it and the whole doesn't
        /// <paramref name="fits"/> (<see cref="LineWords.CutToFit"/>), and leans its outside text.
        /// Returns where outside text stands in the label's text as shown.
        /// </summary>
        public IReadOnlyList<(int Start, int End)> Set(LineWords words, Func<string, bool>? fits = null)
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

        private void LeanTheirs(TMP_TextInfo info)
        {
            if (theirs.Count == 0) return;
            GlazeText.Lean(info, theirs);
            if (!(grounded is Color32 colour)) return;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible || !GlazeText.InRun(info, index, theirs)) continue;
                var colours = info.meshInfo[character.materialReferenceIndex].colors32;
                for (var corner = character.vertexIndex; corner < character.vertexIndex + 4; corner++) colours[corner] = colour;
            }
        }

        private void OnDestroy()
        {
            if (label != null) label.OnPreRenderText -= LeanTheirs;
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
