#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A command of theirs set apart in a well (the art direction's handoff, 2026-10-30): their monospace in n7 on n0, radius
    /// 12 dp, padded 12 by 14, in rows by the reader's rule (<see cref="KitReading"/>: at its spaces, else after a "/", "?", "&",
    /// "=", ",", ";" or ":", never mid-name where one of those fits, a wrapped row hanging two characters in), one label a row.
    /// Shown whole where it fits its rows; otherwise cut, its last shown row fading out, and what is left counted beneath in
    /// Label n5, so a cut command never reads as the whole of it. Its top left is on its transform.
    /// </summary>
    public sealed class KitWell : MonoBehaviour
    {
        /// <summary>The lines a well shows before it cuts: two lines of Mono, the handoff's 48 dp.</summary>
        public const int VisibleLines = 2;

        /// <summary>The fade's bands over the last shown line, n0 at these opacities from its top down.</summary>
        private static readonly float[] FadeSteps = { 0.15f, 0.35f, 0.55f, 0.75f };

        private Surface plate = null!;
        private TextMeshPro words = null!;
        private TextMeshPro rest = null!;
        private readonly List<TextMeshPro> rows = new List<TextMeshPro>();
        private readonly List<Surface> fade = new List<Surface>();

        public TextMeshPro Words => words;

        /// <summary>"Cut · {n} More Lines" under a cut command; hidden for a whole one.</summary>
        public TextMeshPro Rest => rest;

        public Surface Plate => plate;

        public Vector2 Size { get; private set; }

        /// <summary>The command did not fit its lines: what shows is its start.</summary>
        public bool Cut { get; private set; }

        /// <summary>The command's lines past those shown.</summary>
        public int HiddenLines { get; private set; }

        public static KitWell Create(Transform parent, string name, int order = KitType.Order.Plate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var well = go.AddComponent<KitWell>();
            well.plate = Surface.Create(go.transform, "Well", order);
            well.words = KitType.Create(go.transform, "Command", KitRole.Mono, KitType.Text, KitType.Order.Words, room: true);
            // Cut by its lines, never sideways: its long lines wrap, and only the fade and the cut line say what is hidden (ART).
            well.words.overflowMode = TextOverflowModes.Truncate;
            well.rest = KitType.Create(go.transform, "Cut", KitRole.Label, KitType.Colour(SpatialKit.TextTertiary), KitType.Order.Words, room: true);
            well.rest.textWrappingMode = TextWrappingModes.NoWrap;
            return well;
        }

        /// <summary>Lays <paramref name="command"/> <paramref name="width"/> wide and returns the size it takes, its cut line included.</summary>
        public Vector2 Show(string command, float width)
        {
            var padX = KitType.Units(SpatialKit.Degrees(SpatialKit.WellPadXDp));
            var padY = KitType.Units(SpatialKit.Degrees(SpatialKit.WellPadYDp));
            var inner = width - 2f * padX;
            KitType.Size(words, KitRole.Mono);
            var indent = words.GetPreferredValues("MM").x;
            var reading = new KitReading(command, (row, continued) => Within(row, continued ? inner - indent : inner));
            var all = reading.Rows.Count;
            Cut = all > VisibleLines;
            HiddenLines = Cut ? all - VisibleLines : 0;
            var shown = Mathf.Min(all, VisibleLines);
            foreach (var extra in rows) extra.gameObject.SetActive(false);
            var rowPitch = KitType.LinePitch(words);
            for (var row = 0; row < shown; row++)
            {
                var label = row == 0 ? words : RowAt(row - 1);
                label.gameObject.SetActive(true);
                KitType.Size(label, KitRole.Mono);
                GlazeRuns.Of(label).Set(reading.RowWords(row));
                var hang = reading.Rows[row].Continued ? indent : 0f;
                KitType.Lay(label, inner - hang, 1);
                label.transform.localPosition = new Vector3(padX + hang, -padY - row * rowPitch, -0.0005f);
            }
            var textHeight = shown * rowPitch;
            var wellHeight = textHeight + 2f * padY;
            plate.Draw(new Vector2(width, wellHeight), KitType.Units(SpatialKit.Degrees(SpatialKit.InsetRadiusDp)), KitType.Colour(SpatialKit.Well));
            plate.transform.localPosition = new Vector3(width / 2f, -wellHeight / 2f, 0f);

            // Cut: the last shown line fades into the well, so its end reads as a start, never as the whole.
            var pitch = KitType.LinePitch(words);
            var band = pitch / FadeSteps.Length;
            while (fade.Count < FadeSteps.Length) fade.Add(Surface.Create(transform, "Fade " + fade.Count, KitType.Order.Words + 1));
            for (var step = 0; step < fade.Count; step++)
            {
                fade[step].gameObject.SetActive(Cut);
                if (!Cut) continue;
                fade[step].Draw(new Vector2(inner, band), 0f, KitType.Colour(SpatialKit.Well, FadeSteps[step]));
                fade[step].transform.localPosition = new Vector3(width / 2f, -padY - textHeight + pitch - band * (step + 0.5f), -0.0007f);
            }
            rest.gameObject.SetActive(Cut);
            var height = wellHeight;
            if (Cut)
            {
                KitType.Size(rest, KitRole.Label);
                GlazeText.SetLiteral(rest, KitMomentWords.CutMore(HiddenLines));
                var (_, _, restHeight) = KitType.Lay(rest, width, 1);
                var gap = KitType.Units(SpatialKit.Degrees(SpatialKit.CutGapDp));
                rest.transform.localPosition = new Vector3(0f, -wellHeight - gap, -0.0005f);
                height += gap + restHeight;
            }
            Size = new Vector2(width, height);
            gameObject.SetActive(true);
            return Size;
        }

        /// <summary>Whether <paramref name="row"/> fits <paramref name="room"/> on one line of the command's face.</summary>
        private bool Within(LineWords row, float room)
        {
            GlazeRuns.Of(words).Set(row);
            return words.GetPreferredValues(words.text).x <= room + 1e-6f;
        }

        /// <summary>A row label after the first, in the command's look; made hidden.</summary>
        private TextMeshPro RowAt(int index)
        {
            while (rows.Count <= index)
            {
                var made = KitType.Create(transform, "Command row " + (rows.Count + 1), KitRole.Mono, KitType.Text, KitType.Order.Words, room: true);
                made.overflowMode = TextOverflowModes.Truncate;
                made.gameObject.SetActive(false);
                rows.Add(made);
            }
            return rows[index];
        }

        /// <summary>Whether the cut line itself reads whole.</summary>
        public bool RestWhole => !Cut || !rest.isTextTruncated;

        public void Hide() => gameObject.SetActive(false);
    }
}
