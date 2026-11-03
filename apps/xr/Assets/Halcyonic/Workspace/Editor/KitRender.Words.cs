#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The kit's words read whole wherever they stand, in every state at rest (the owner, 2026-11-03: "it's cut off"): the
    /// cadence's separators (<see cref="SeparatorsHold"/>); a task's name in a row is never cut (the XR baseline's defect 20,
    /// "to the sign-in ..." at Larger); a row's words never wrap beside its end where the row's own width holds them on one
    /// line (defect 23, "Earlier / Activity" beside "3 More"); a chip's path never breaks beside a hyphen where its name fits a
    /// row whole, nor inside a name's letters (21, "rate-" / "limit.ts"); the rail's heads bracket nothing (22, "Tests Failed
    /// (Reported)"); and a well's rows break where the reader's would, never inside a name (11, "apps/xr/dotnet/Halcyonic.Clien").
    /// </summary>
    public static partial class KitRender
    {
        /// <summary>Every check of the kit's words read whole under <paramref name="root"/>; one failure a fault.</summary>
        private static IEnumerable<string> KitWordsHold(Transform root, string name) =>
            SeparatorsHold(root, name).Concat(NamesWhole(root, name)).Concat(RowsUnsqueezed(root, name)).Concat(ChipsUnbroken(root, name))
                .Concat(HeadsUnbracketed(root, name)).Concat(WellRowsWhole(root, name)).Concat(ReasonsOnTheControl(root, name)).ToList();

        /// <summary>
        /// A reason to read first is the locked control's own word (18: "Read Full Instruction" and "Read Full Question" stood bare
        /// above the footer and read as buttons): no composer's reason line says one.
        /// </summary>
        private static IEnumerable<string> ReasonsOnTheControl(Transform root, string name)
        {
            foreach (var composer in root.GetComponentsInChildren<KitComposer>(false))
            {
                var line = composer.Reason;
                if (line.gameObject.activeInHierarchy && (line.text == LabelText.ForTextMeshPro(FileScreens.InstructionNotRead) || line.text == LabelText.ForTextMeshPro(FileScreens.ReadTheQuestion)))
                    yield return name + ": \"" + line.text + "\" stands as a line above the footer, not on the locked control";
            }
        }

        /// <summary>The lines a label shows, each its characters from first to last (spaces at either end included), top first.</summary>
        private static List<string> LinesOf(TMP_Text label)
        {
            label.ForceMeshUpdate(true);
            var info = label.textInfo;
            var lines = new List<string>();
            for (var line = 0; line < info.lineCount; line++)
            {
                var each = info.lineInfo[line];
                var text = new System.Text.StringBuilder();
                for (var at = each.firstCharacterIndex; at <= each.lastCharacterIndex && at < info.characterCount; at++) text.Append(info.characterInfo[at].character);
                lines.Add(text.ToString());
            }
            return lines;
        }

        /// <summary>The word labels under <paramref name="part"/>, icons aside, top to bottom, then left to right.</summary>
        private static List<TMP_Text> WordLabels(Transform part, string? except = null) => part.GetComponentsInChildren<TMP_Text>(false)
            .Where(label => !GlazeIcons.IsIcon(label) && label.name != except && !string.IsNullOrEmpty(label.text))
            .OrderByDescending(label => part.InverseTransformPoint(label.transform.position).y).ThenBy(label => part.InverseTransformPoint(label.transform.position).x)
            .ToList();

        /// <summary>
        /// A chip's rows break where a reader's would: never beside a hyphen where the name around it fits a row whole (21,
        /// "src/auth/rate-" / "limit.ts", "rate-limit.ts" fitting), and never inside a name's letters where the row holds a hyphen
        /// to break at ("sign-in.test.t" / "s"). A name longer than a row may break beside its hyphen: nothing better holds it.
        /// </summary>
        private static IEnumerable<string> ChipsUnbroken(Transform root, string name)
        {
            const string ends = " /?&=,;:";
            foreach (var chip in root.GetComponentsInChildren<KitChip>(false))
            {
                var labels = WordLabels(chip.transform);
                var rows = labels.SelectMany(LinesOf).ToList();
                if (rows.Count < 2) continue;
                var room = labels[0].rectTransform.sizeDelta.x;
                for (var row = 0; row + 1 < rows.Count; row++)
                {
                    var here = rows[row];
                    var next = rows[row + 1];
                    if (here.Length == 0 || next.Length == 0 || ends.IndexOf(here[here.Length - 1]) >= 0 || next[0] == ' ') continue;
                    // The name the break falls in: from the row's last space or mark to the next row's first, a mark kept.
                    var tail = here.Substring(here.LastIndexOfAny(ends.ToCharArray()) + 1);
                    var cut = next.IndexOfAny(ends.ToCharArray());
                    var whole = tail + (cut < 0 ? next : next.Substring(0, next[cut] == ' ' ? cut : cut + 1));
                    var shown = Clip(string.Concat(rows));
                    if (here.EndsWith("-") || next.StartsWith("-"))
                    {
                        if (labels[0].GetPreferredValues(whole).x <= room + 1e-6f)
                            yield return name + ": the chip \"" + shown + "\" breaks beside a hyphen though \"" + whole + "\" fits a row (\"" + here + "\" / \"" + next + "\")";
                    }
                    else if (tail.Contains("-"))
                        yield return name + ": the chip \"" + shown + "\" breaks inside the name \"" + whole + "\" though it holds a hyphen to break at (\"" + here + "\" / \"" + next + "\")";
                }
            }
        }

        /// <summary>The rail's heads, Halcyonic's words over their evidence, bracket nothing: the kit says "Tests Failed · Reported".</summary>
        private static IEnumerable<string> HeadsUnbracketed(Transform root, string name)
        {
            foreach (var trail in root.GetComponentsInChildren<KitTrail>(false))
                foreach (var label in trail.GetComponentsInChildren<TMP_Text>(false).Where(label => !GlazeIcons.IsIcon(label) && label.GetComponentInParent<KitChip>() == null))
                    if (label.text.Contains("(") || label.text.Contains(")")) yield return name + ": the rail's head \"" + Clip(label.text) + "\" brackets its words";
        }

        /// <summary>
        /// A well's rows break where the reader's would: a row that ends inside a name (the next starting at once, with no space
        /// between, or a cut well's last shown row, whose next is hidden) ends after a "/", "?", "&", "=", ",", ";" or ":" wherever
        /// the row holds one, never mid-name.
        /// </summary>
        private static IEnumerable<string> WellRowsWhole(Transform root, string name)
        {
            const string breaks = "/?&=,;:";
            foreach (var well in root.GetComponentsInChildren<KitWell>(false))
            {
                var rows = WordLabels(well.transform, except: "Cut").SelectMany(LinesOf).ToList();
                for (var row = 0; row < rows.Count; row++)
                {
                    var here = rows[row];
                    var last = row == rows.Count - 1;
                    if (last && !well.Cut) continue;
                    if (here.EndsWith(" ") || here.Length == 0 || (!last && rows[row + 1].StartsWith(" "))) continue;
                    if (breaks.IndexOf(here[here.Length - 1]) >= 0) continue;
                    if (here.Trim().IndexOfAny(breaks.ToCharArray()) >= 0 || here.Trim().Contains(" "))
                        yield return name + ": the well's row \"" + Clip(here) + "\" ends inside a name where it could break after a mark or a space";
                }
            }
        }

        /// <summary>A task's or a project's name in a row, shown, never ends cut.</summary>
        private static IEnumerable<string> NamesWhole(Transform root, string name)
        {
            foreach (var row in root.GetComponentsInChildren<KitRow>(false))
            {
                if (row.Model?.IsName != true || !row.Words.gameObject.activeInHierarchy) continue;
                row.Words.ForceMeshUpdate(true);
                if (row.Words.isTextTruncated) yield return name + ": the name \"" + Clip(row.Model.Words.Text) + "\" is cut in its row";
            }
        }

        /// <summary>
        /// A row's words, shown beside its end (as "3 More"), take more lines there only where the row's whole width would not
        /// hold them on one: the room from the words' left to the end's right, the row's own padding kept.
        /// </summary>
        private static IEnumerable<string> RowsUnsqueezed(Transform root, string name)
        {
            foreach (var row in root.GetComponentsInChildren<KitRow>(false))
            {
                var end = row.GetComponentsInChildren<TextMeshPro>(false).FirstOrDefault(label => label.name == "End");
                if (end == null || row.Model?.End == null || !row.Words.gameObject.activeInHierarchy) continue;
                row.Words.ForceMeshUpdate(true);
                if (row.Words.textInfo.lineCount < 2) continue;
                var room = end.transform.localPosition.x - row.Words.transform.localPosition.x;
                var whole = row.Words.GetPreferredValues(row.Words.text).x;
                if (whole <= room)
                    yield return name + ": \"" + Clip(row.Model.Words.Text) + "\" wraps beside \"" + row.Model.End + "\" though its row holds it on one line";
            }
        }
    }
}
