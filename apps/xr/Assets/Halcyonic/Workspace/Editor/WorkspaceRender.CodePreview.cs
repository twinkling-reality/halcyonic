#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>
        /// The Code Colours preview's example as the room draws it (AUDIT3's 15:20Z: ours, literal, painted): the frame's rows through
        /// the room's reader into the rows view, with a check-only palette (FIELD's own are judged in the parts' 47-build-colours-halora and -high), at both text sizes. Its
        /// keyword and string are painted, its text unchanged, Plain clears it, and a theme change alone repaints with nothing laid
        /// again. No preferences are read or saved.
        /// </summary>
        public static void CodePreviewCheck()
        {
            var failures = new List<string>();
            var wasNow = CodeColouring.Now;
            var wasTheme = CodeThemes.Current;
            var wasHalora = CodeColouring.Palettes.TryGetValue(CodeTheme.Halora, out var halora) ? halora : null;
            var wasScale = GlazeText.Scale;
            CodeColouring.Palette probe = kind => kind == CodeTokenKind.Keyword ? new Color32(120, 200, 255, 255)
                : kind == CodeTokenKind.String ? new Color32(180, 255, 160, 255)
                : kind == CodeTokenKind.Comment ? new Color32(200, 200, 210, 255) : (Color32?)null;
            try
            {
                foreach (var scale in new[] { 1f, RoomGeometry.LargerScale })
                {
                    var size = scale == 1f ? "standard" : "larger";
                    void Require(bool held, string failure) { if (!held) failures.Add(size + ": " + failure); }
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    GlazeText.SetScale(scale);
                    CodeColouring.Palettes[CodeTheme.Halora] = probe;
                    CodeThemes.Set(CodeTheme.Plain);
                    // A whole kept value, as AppearanceSettings saves one: a value missing its panels part loads as the default.
                    var settings = AppearanceSettings.Load("version=3;theme=glaze;panels=dark;code=plain");
                    Require(settings.Code == CodeTheme.Plain, "the kept value did not load as Plain");
                    var column = new AppearanceColumn(settings, () => { }, codeOnly: true);
                    column.Act(AppearanceColumn.ChooseCode, CodeThemes.Key(CodeTheme.Halora));
                    column.Act(AppearanceColumn.PreviewCode, null);
                    Require(CodeThemes.Current == CodeTheme.Halora, "the preview did not put the chosen colours in force");
                    // The preview is a card whose code the card's own reading lays, as Watch It Work's card lays code (the lead's 18:47Z).
                    var bar = RoomBar.OfFrame(column.Frame!, VoiceStage.Idle);
                    Require(bar?.Card != null && bar.Card.Sample.SequenceEqual(CodeThemes.Example) && bar.Card.Label == CodeThemes.PreviewLabel, "the preview is not the card with the example");
                    if (bar?.Card == null) continue;
                    var host = new GameObject("Code preview");
                    var view = RoomCardView.Create(host.transform);
                    // At the full panel and at the column's width (the narrow form), where a line wraps.
                    foreach (var (form, widthDp) in new[] { ("full", RoomGeometry.PanelDp - 2f * RoomGeometry.PanelPadDp), ("narrow", RoomGeometry.ColumnDp - 2f * RoomGeometry.PanelPadDp) })
                    {
                        view.Show(bar.Card, RoomType.Units(widthDp));
                        var code = view.ReadingShown.ToList();
                        foreach (var label in code) label.ForceMeshUpdate(true);
                        var drawnText = string.Join("", code.Select(label => label.text.Replace(" ", "")));
                        Require(drawnText == string.Join("", CodeThemes.Example).Replace(" ", ""), form + ": the example is not drawn whole: " + string.Join(" | ", code.Select(label => label.text)));
                        Require(code.All(label => !label.richText && label.text == label.GetParsedText() && label.font != null && label.font.name.Contains("Mono")),
                            form + ": a code row is not literal mono, or colouring changed its text");
                        Require(view.Plates.All(each => each.What != "reading well"), form + ": a well stands under the code");
                        var constRow = code.FirstOrDefault(label => label.text.StartsWith("const", StringComparison.Ordinal));
                        Require(constRow != null && GlazeRuns.Of(constRow).Painted.Any(range => range.Start == 0 && range.End == 5), form + ": the keyword const is not painted");
                        Require(code.Any(label => GlazeRuns.Of(label).Painted.Any(range => label.text.Substring(range.Start, range.End - range.Start).Contains("Shown:"))), form + ": the string is not painted");
                        // PV1: by the logical line, never the visual row. The comment wraps in the narrow form, and every row of it keeps the comment's colour.
                        var commentRows = code.TakeWhile(label => !label.text.StartsWith("const", StringComparison.Ordinal)).ToList();
                        if (form == "narrow") Require(commentRows.Count >= 2, "narrow: the comment does not wrap, so the logical-line colour is unproven: " + commentRows.Count + " row");
                        Require(commentRows.Count > 0 && commentRows.All(label => GlazeRuns.Of(label).Painted.Any(range => range.Start == 0 && range.End >= label.text.TrimEnd().Length)),
                            form + ": a row of the comment is not painted whole as the comment");
                        var sizeBefore = view.Size;
                        var positions = code.Select(label => label.transform.localPosition).ToList();
                        CodeThemes.Set(CodeTheme.Plain);
                        Require(code.All(label => GlazeRuns.Of(label).Painted.Count == 0), form + ": choosing Plain leaves a run painted");
                        CodeThemes.Set(CodeTheme.Halora);
                        Require(constRow != null && GlazeRuns.Of(constRow).Painted.Count > 0, form + ": a theme change did not repaint the preview");
                        Require(view.Size == sizeBefore && code.Select(label => label.transform.localPosition).SequenceEqual(positions), form + ": a theme change laid the card again");
                    }
                    UnityEngine.Object.DestroyImmediate(host);
                    column.Act(AppearanceColumn.Cancel, null);
                    Require(CodeThemes.Current == CodeTheme.Plain, "Cancel did not put the kept colours back");
                }
                // CC-d: a code row is never held at its last two words. At a width where the same words as a row of words end on one word
                // and are held (a no-break space between the last two), the code row stays as laid, its text unchanged.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GlazeText.SetScale(1f);
                const string codeLine = "const finished = tasks.filter(task => task.done).map(task => task.title).slice(0, 3) ok;";
                var holder = new GameObject("Code hold");
                var held = RoomRowsView.Create(holder.transform);
                var found = false;
                for (var dp = 120f; dp <= 440f && !found; dp += 4f)
                {
                    held.Show(new[] { new RoomRow(LineWords.Ours(codeLine)) }, RoomType.Units(dp));
                    if (!held.Label(0).text.Contains('\u00A0')) continue;
                    found = true;
                    held.Show(new[] { new RoomRow(LineWords.Ours(codeLine), code: CodeLanguage.Script) }, RoomType.Units(dp));
                    if (held.Label(0).text.Contains('\u00A0')) failures.Add("a code row was held at its last two words at " + dp + " dp: " + held.Label(0).text.Replace('\u00A0', '~'));
                }
                if (!found) failures.Add("no width from 120 to 440 dp ends the words' row on one word alone, so the code row's guard is unproven");
                UnityEngine.Object.DestroyImmediate(holder);
                // A view destroyed without OnDestroy (here, in the Editor) never fails a later theme change, and lets go of it.
                var never = RoomRowsView.Create(new GameObject("Never shown").transform);
                UnityEngine.Object.DestroyImmediate(never.transform.parent.gameObject);
                CodeThemes.Set(CodeTheme.Halora);
                CodeThemes.Set(CodeTheme.Plain);
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                if (wasHalora != null) CodeColouring.Palettes[CodeTheme.Halora] = wasHalora; else CodeColouring.Palettes.Remove(CodeTheme.Halora);
                CodeThemes.Set(wasTheme);
                CodeColouring.Now = wasNow;
                GlazeText.SetScale(wasScale);
                KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: code preview: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: code preview: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
    }
}
