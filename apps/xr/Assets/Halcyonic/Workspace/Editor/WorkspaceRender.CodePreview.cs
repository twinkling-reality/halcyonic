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
                : kind == CodeTokenKind.String ? new Color32(180, 255, 160, 255) : (Color32?)null;
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
                    var rows = RoomBar.OfFrame(column.Frame!, VoiceStage.Idle)?.Facts;
                    Require(rows != null && rows.Count(row => row.Code == CodeLanguage.Script) == CodeThemes.Example.Length, "the preview's rows do not carry the code mark");
                    if (rows == null) continue;
                    var host = new GameObject("Code preview");
                    var view = RoomRowsView.Create(host.transform);
                    view.Show(rows, RoomType.Units(RoomGeometry.PanelDp));
                    var code = Enumerable.Range(0, view.Count).Select(view.Label).Where(label => CodeThemes.Example.Contains(label.text)).ToList();
                    var words = Enumerable.Range(0, view.Count).Select(view.Label).Except(code).ToList();
                    Require(code.Count == CodeThemes.Example.Length, "the example is not drawn as its rows: " + string.Join(" | ", Enumerable.Range(0, view.Count).Select(index => view.Label(index).text)));
                    foreach (var label in code.Concat(words)) label.ForceMeshUpdate(true);
                    Require(code.All(label => !label.richText && label.text == label.GetParsedText()), "a code row is not literal, or colouring changed its text");
                    var constRow = code.FirstOrDefault(label => label.text.StartsWith("const", StringComparison.Ordinal));
                    var logRow = code.FirstOrDefault(label => label.text.StartsWith("console", StringComparison.Ordinal));
                    Require(constRow != null && GlazeRuns.Of(constRow).Painted.Any(range => range.Start == 0 && range.End == 5), "the keyword const is not painted");
                    Require(logRow != null && GlazeRuns.Of(logRow).Painted.Any(range => logRow.text.Substring(range.Start, range.End - range.Start) == "\"Done:\""), "the string is not painted");
                    Require(words.All(label => GlazeRuns.Of(label).Painted.Count == 0), "a row of words is painted");
                    var sizeBefore = view.Size;
                    var positions = code.Select(label => label.transform.localPosition).ToList();
                    CodeThemes.Set(CodeTheme.Plain);
                    Require(code.All(label => GlazeRuns.Of(label).Painted.Count == 0), "choosing Plain leaves a run painted");
                    CodeThemes.Set(CodeTheme.Halora);
                    Require(constRow != null && GlazeRuns.Of(constRow).Painted.Count > 0, "a theme change did not repaint the preview");
                    Require(view.Size == sizeBefore && code.Select(label => label.transform.localPosition).SequenceEqual(positions), "a theme change laid the rows again");
                    column.Act(AppearanceColumn.Cancel, null);
                    Require(CodeThemes.Current == CodeTheme.Plain, "Cancel did not put the kept colours back");
                    UnityEngine.Object.DestroyImmediate(host);
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
