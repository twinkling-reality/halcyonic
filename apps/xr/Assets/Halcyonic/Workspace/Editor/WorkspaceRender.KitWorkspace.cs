#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Shots in this pass where the kit drew the task's file.</summary>
        private static int kitShots;

        /// <summary>Whether a context row was pressed through the kit's spine, and reached its handler, in this pass.</summary>
        private static bool kitContextWitnessed;

        /// <summary>Whether this pass also takes the composer past the kit's rows and back (<see cref="KitActivityComposerCheck"/>).</summary>
        private static bool kitComposerWitness;

        /// <summary>Times the kit was seen to give the file back to its views where it can't draw the frame whole, and take it again.</summary>
        private static int kitFallbacks;

        /// <summary>
        /// The Spatial journey's running and approval routes with the spatial kit standing in for the task's file (D3.4),
        /// at both reading sizes. Every press on the file goes through the kit's own controls; every shot where the kit
        /// draws checks it in the plane's terms: one column, the file's views and the context column hidden, the spine the
        /// context column's offers in its words, text in the kit's bands, everything on the curve, and a context row with
        /// a stale owner refused. The approval route ends pressing Tasks on the spine, which must reach its place.
        /// </summary>
        public static void KitSpatialCheck() => CheckKitWorkspace("KitWorkspaceRenders", composer: false);

        /// <summary>
        /// The running route with the kit, its composer taken past the rows the kit shows: the file's own views must
        /// draw it then, and the kit take it again once the draft fits; the composer's and Activity's budgets are the
        /// kit's rows while it draws.
        /// </summary>
        public static void KitActivityComposerCheck() => CheckKitWorkspace("KitComposerRenders", composer: true);

        private static void CheckKitWorkspace(string renders, bool composer)
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", renders, variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                spatialProductGaps.Clear();
                kitShots = 0; kitContextWitnessed = false; kitComposerWitness = composer; kitFallbacks = 0;
                var found = new List<string>();
                var was = KitWorkspaceHost.Enabled;
                KitWorkspaceHost.Enabled = true;
                try
                {
                    found.AddRange(RenderSpatialWorkspace(folder, Work.Running(), false, kit: true));
                    if (!composer) found.AddRange(RenderSpatialWorkspace(folder, Work.Approval("Run make migrate"), true, kit: true));
                }
                finally { KitWorkspaceHost.Enabled = was; kitComposerWitness = false; ViewField.Current = null; KeepFontAssetsAsCommitted(); }
                if (kitShots == 0) found.Add("kit: the kit never drew a task's file");
                if (!composer && !kitContextWitnessed) found.Add("kit: no context row was pressed through the kit's spine");
                if (composer && kitFallbacks == 0) found.Add("kit: the composer never went past the kit's rows and back");
                foreach (var failure in found) Debug.LogError("Halcyonic: kit workspace: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: kit workspace: every check passed; " + kitShots + " shots drawn by the kit; " + folder
                    + (spatialProductGaps.Count == 0 ? "" : "; product gaps, not passes: " + string.Join("; ", spatialProductGaps)));
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>
        /// For the Editor, which runs no frames: the kit's controls settled and its frame, as Update would, reported drawn
        /// (<see cref="KitWorkspaceHost.Drawn"/>), so the file's frame gets its draw only through the kit.
        /// </summary>
        private static void KitTick(MenuDirector director)
        {
            if (director.Plane.KitShows) director.Plane.Kit.DrawnForRender();
        }

        private static void KitDraw(MenuDirector director)
        {
            for (var turn = 0; turn < 4; turn++)
            {
                director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds);
                KitTick(director);
            }
        }

        /// <summary>
        /// Presses <paramref name="action"/> on the kit's own control for it, where the kit draws the file and binds that
        /// action: the way a hand would, through the control's pointer target. False where the kit has no such control.
        /// </summary>
        private static bool PressOnKit(MenuDirector director, string action, string? key)
        {
            var plane = director.Plane;
            if (!plane.KitShows || plane.KitBinding is not KitFileBinding binding) return false;
            var control = binding.Controls.FirstOrDefault(each => each.Action == action && (key == null || each.Key == key));
            if (control == null) return false;
            var press = KitPressOf(plane.Kit.View, control);
            if (press == null) return false;
            press.SettleForRender();
            press.PressForRender(960);
            return true;
        }

        /// <summary>The kit's press for a bound control, as the kit lays it (KitRender's Shows), or null where it shows none.</summary>
        private static KitPress? KitPressOf(KitWorkView view, KitControl control)
        {
            var composer = view.Composer;
            KitButton? button = control.Place switch
            {
                KitPlace.Close => composer.Close,
                KitPlace.Stop or KitPlace.Discard => composer.Rare,
                KitPlace.Talk => composer.Talk,
                KitPlace.Send => composer.Send,
                KitPlace.Deny => view.Decision.Deny,
                KitPlace.Approve => view.Decision.Approve,
                KitPlace.Cancel => view.Decision.Cancel,
                KitPlace.Yes => view.Decision.Yes,
                KitPlace.Section => view.Tabs.FirstOrDefault(tab => tab.Words.text == LabelText.ForTextMeshPro(control.Words)),
                _ => null,
            };
            if (control.Place == KitPlace.Field) return composer.Field.gameObject.activeInHierarchy ? composer.Field : null;
            return button != null && button.gameObject.activeInHierarchy ? button.Press : null;
        }

        /// <summary>
        /// One shot with the kit on. Where the kit draws the file: the plane's composition, the spine, text in the kit's
        /// bands, everything on the curve, a stale context press refused, and for the composer pass, the fallback to the
        /// file's views and back. Where it doesn't, the file's own checks, and a failure if the kit could have drawn it.
        /// </summary>
        private static IEnumerable<string> KitState(string name, string folder, GameObject root, Camera camera, RenderTexture texture,
            MenuDirector director, List<(CharacterView View, CharacterTarget Target)> characters, Vector3 eyes)
        {
            var failures = new List<string>();
            var plane = director.Plane;
            KitDraw(director);
            if (!plane.KitShows)
            {
                if (plane.KitBinding is KitFileBinding unshown && plane.Kit.Holds(unshown))
                    failures.Add(name + ": the kit can draw the file's frame whole but its views draw it");
                // Why the file's views draw it: no binding (what the frame offers that the kit has no place for), or too much to read whole.
                if (director.Navigator.Beside is FileColumn beside && beside.Frame is MenuFrame frame)
                    Debug.Log("Halcyonic: kit workspace: " + name + ": the file's views draw it; "
                        + (plane.KitBinding == null
                            ? "no binding: side " + (frame.Side != null) + ", lines " + string.Join(", ", frame.Lines.Where(line => line.Action != null).Select(line => line.Action))
                                + ", footer " + string.Join(", ", frame.Footer.All.Select(slot => slot.Prompt.Id + "/" + slot.Prompt.Kind))
                            : "the kit can't hold it whole"));
                failures.AddRange(SpatialState(name, folder, root, camera, texture, director, characters, eyes));
                return failures;
            }
            kitShots++;
            var binding = plane.KitBinding!;
            ForceMeshes(root);
            if (plane.Composition?.Columns.Count != 1) failures.Add(name + ": the kit does not stand alone in the composition");
            if (plane.Context.gameObject.activeSelf || plane.Chrome.Visible || plane.ActivityContext != null
                || plane.GetComponentsInChildren<MenuFrameView>(false).Any())
                failures.Add(name + ": a file view, the context column or the chrome still shows beside the kit");
            if (plane.Showing(MenuColumn.File) is not MenuFrame record || !ReferenceEquals(record, binding.Frame))
                failures.Add(name + ": the file's column of record is not the frame the kit draws");
            failures.AddRange(KitSpine(name, plane, binding));
            failures.AddRange(KitTextInBands(name, plane));
            failures.AddRange(OnTheCurve(name, plane, record: false));
            failures.AddRange(TargetsOnTheCurve(name, plane, record: false));
            failures.AddRange(KitStaleContext(name, director));
            camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
            var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, name + "-kit.png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            if (kitComposerWitness && director.Navigator.Beside is FileColumn file && file.Screen.Instruction.Open)
                failures.AddRange(KitComposerFallback(name, director, file));
            if (name == "spatial-waiting-approval") failures.AddRange(KitContextPress(name, director));
            return failures;
        }

        /// <summary>
        /// The spine is the context column's offers in its words: the connection state under Projects, the same up to three
        /// projects in the same order, the selected one lit, More projects only past three, Tasks, and Workspace at its foot;
        /// and those words are the context column's own.
        /// </summary>
        private static List<string> KitSpine(string name, MenuPlane plane, KitFileBinding binding)
        {
            var failures = new List<string>();
            var spine = binding.Model.Spine;
            var choices = plane.ContextProjects;
            var expected = choices.Take(3).Select(project => ("project:" + project.ProjectId, project.Name, project.ProjectId == plane.ContextSelectedProject)).ToList();
            if (spine.Count != 2 || spine[0].Heading != "Projects" || spine[0].Note != plane.ConnectionState
                || !spine[0].Rows.Take(expected.Count).Select(row => (row.Key, row.Words.Text, row.Selected)).SequenceEqual(expected)
                || spine[0].Rows.Count != expected.Count + (choices.Count > 3 ? 1 : 0)
                || choices.Count > 3 && (spine[0].Rows[^1].Key != "place:Projects" || spine[0].Rows[^1].Words.Text != "More projects")
                || spine[1].Rows.Count != 1 || spine[1].Rows[0].Key != "place:Tasks" || spine[1].Rows[0].Words.Text != "Tasks"
                || binding.Model.SpineFoot?.Key != "workspace" || binding.Model.SpineFoot.Words.Text != "Workspace")
                failures.Add(name + ": the kit's spine is not the context column's offers");
            // The context column's own words, as it lays them for the same choices.
            plane.Context.Show(choices, plane.ContextSelectedProject, plane.ContextBinding!, plane.ConnectionState);
            string? Words(string path) => plane.Context.transform.Find(path)?.GetComponentInChildren<TMP_Text>(true)?.text;
            var context = new[] { Words("Projects heading"), Words("Tasks/Label"), Words("Workspace/Label") };
            plane.Context.Hide();
            if (!context.SequenceEqual(new[] { "Projects", "Tasks", "Workspace" }))
                failures.Add(name + ": the context column's words are not the spine's: " + string.Join(", ", context));
            return failures;
        }

        /// <summary>Every glyph of the kit's words stands inside one of the kit's text bands, as the plane laid them.</summary>
        private static List<string> KitTextInBands(string name, MenuPlane plane)
        {
            const float Tolerance = 0.0001f;
            var failures = new List<string>();
            var bands = plane.ContextTextBands.Concat(plane.ReaderTextBands).ToList();
            var (_, rightAxis, upAxis) = PlaneComposition.Axes(plane.Direction);
            var right = new Vector3(rightAxis.X, rightAxis.Y, rightAxis.Z);
            var up = new Vector3(upAxis.X, upAxis.Y, upAxis.Z);
            Vector2 OnPlane(Vector3 world) => new Vector2(Vector3.Dot(world - plane.Eyes, right), Vector3.Dot(world - plane.Eyes, up)) / PlaneComposition.Distance;
            foreach (var label in plane.Kit.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text) || label.GetComponent<MeshRenderer>() is MeshRenderer drawn && !drawn.enabled) continue;
                label.ForceMeshUpdate();
                for (var index = 0; index < label.textInfo.characterCount; index++)
                {
                    var glyph = label.textInfo.characterInfo[index];
                    if (!glyph.isVisible) continue;
                    var corners = new[] { glyph.bottomLeft, glyph.topRight }.Select(corner => OnPlane(label.transform.TransformPoint(corner))).ToArray();
                    float left = Mathf.Min(corners[0].x, corners[1].x), rightmost = Mathf.Max(corners[0].x, corners[1].x);
                    float bottom = Mathf.Min(corners[0].y, corners[1].y), top = Mathf.Max(corners[0].y, corners[1].y);
                    if (!bands.Any(band => band.Left - Tolerance <= left && rightmost <= band.Right + Tolerance && band.Bottom - Tolerance <= bottom && top <= band.Top + Tolerance))
                    {
                        failures.Add(name + ": a glyph of " + PathOf(label.transform) + " stands outside the kit's text bands at "
                            + left.ToString("0.0000", CultureInfo.InvariantCulture) + ".." + rightmost.ToString("0.0000", CultureInfo.InvariantCulture) + " x "
                            + bottom.ToString("0.0000", CultureInfo.InvariantCulture) + ".." + top.ToString("0.0000", CultureInfo.InvariantCulture));
                        break;
                    }
                }
            }
            return failures;
        }

        private static readonly MethodInfo DirectorKitContext = typeof(MenuDirector).GetMethod("OnKitContext", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>A context row raised with an owner the kit was not shown for changes nothing (ContextStanding).</summary>
        private static List<string> KitStaleContext(string name, MenuDirector director)
        {
            var failures = new List<string>();
            var (place, beside) = (director.Navigator.Place, director.Navigator.Beside);
            DirectorKitContext.Invoke(director, new object[] { "place:" + MenuPlace.Tasks, new object() });
            if (director.Navigator.Place != place || !ReferenceEquals(director.Navigator.Beside, beside))
                failures.Add(name + ": a context row with a stale owner navigated");
            return failures;
        }

        /// <summary>Tasks pressed on the kit's spine, the way a hand would, reaches the context column's own handler: the Tasks place.</summary>
        private static List<string> KitContextPress(string name, MenuDirector director)
        {
            var failures = new List<string>();
            var row = director.Plane.Kit.View.Spine.Rows.FirstOrDefault(each => each.Model?.Key == "place:" + MenuPlace.Tasks);
            if (row == null) { failures.Add(name + ": the kit's spine has no Tasks row"); return failures; }
            row.Press.SettleForRender();
            row.Press.PressForRender(961);
            KitDraw(director);
            if (director.Navigator.Place != MenuPlace.Tasks || director.Navigator.Beside != null)
                failures.Add(name + ": Tasks on the kit's spine did not open the Tasks place");
            else kitContextWitnessed = true;
            return failures;
        }

        /// <summary>
        /// The composer with words past the rows the kit's well shows: the file's views draw it then, the kit keeping
        /// nothing; with the person's words back, the kit draws it again. While the kit draws, the instruction's parts are
        /// measured by the kit's rows.
        /// </summary>
        private static List<string> KitComposerFallback(string name, MenuDirector director, FileColumn file)
        {
            var failures = new List<string>();
            var plane = director.Plane;
            var instruction = file.Screen.Instruction;
            if (instruction.PartRows > plane.Kit.ComposerRows)
                failures.Add(name + ": the instruction is measured in " + instruction.PartRows + " rows, more than the kit's " + plane.Kit.ComposerRows);
            var (execution, words, spoken, part) = (instruction.ExecutionId!, instruction.Words, instruction.Spoken, instruction.Part);
            var longer = string.Join(" ", Enumerable.Repeat("Add a regression test for the rate limit, then run the whole suite.", 12));
            instruction.Restore(execution, longer, false, 0);
            file.Reflow();
            KitDraw(director);
            if (plane.KitShows) failures.Add(name + ": the kit kept a draft longer than its well");
            else if (!plane.GetComponentsInChildren<MenuFrameView>(false).Any()) failures.Add(name + ": nothing drew the draft the kit gave back");
            instruction.Restore(execution, words, spoken, part);
            file.Reflow();
            KitDraw(director);
            if (!plane.KitShows) failures.Add(name + ": the kit did not take the file again once the draft fitted");
            else kitFallbacks++;
            return failures;
        }
    }
}
