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

        /// <summary>Whether Compact and Workspace went through the kit's own switch and the existing routes in this pass (D3.5).</summary>
        private static bool kitCompactWitnessed;

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
                kitShots = 0; kitContextWitnessed = false; kitCompactWitnessed = false; kitComposerWitness = composer; kitFallbacks = 0;
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
                if (!composer && !kitCompactWitnessed) found.Add("kit: Compact and Workspace never went through the kit's switch");
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
            if (director.Plane.KitShows || director.Plane.KitCompactShows) director.Plane.Kit.DrawnForRender();
        }

        private static void KitDraw(MenuDirector director)
        {
            for (var turn = 0; turn < 4; turn++)
            {
                director.UpdateForRender(); director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds);
                // The switches' thumbs travel in their own Update, which the Editor never runs: on by the same time.
                director.Plane.Kit.View.Spine.Switch.Advance(MenuPlane.OpeningSeconds);
                director.Plane.Kit.Presence.Switch.Advance(MenuPlane.OpeningSeconds);
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
            failures.AddRange(KitTargets(name, plane));
            failures.AddRange(KitStaleContext(name, director));
            camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
            var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, name + "-kit.png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            if (kitComposerWitness && director.Navigator.Beside is FileColumn file && file.Screen.Instruction.Open)
                failures.AddRange(KitComposerFallback(name, director, file));
            if (name == "spatial-waiting-approval") failures.AddRange(KitContextPress(name, director));
            if (name == "spatial-working-direction" && !kitComposerWitness) failures.AddRange(KitCompactAndOpen(name, folder, camera, texture, director));
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

        /// <summary>
        /// The kit's reading model holds as the journey placed it, as TextInBands holds the file's views: the kit's laid
        /// bands read at the floor, and every glyph of its words stands inside one of them at no smaller em than that band's,
        /// unless it is small text that reads at the floor by itself at its farthest corner. Then an actual kit label moved
        /// off every face, and an actual kit label shrunk below the reading floor, must each be caught, and are put back.
        /// </summary>
        private static List<string> KitTextInBands(string name, MenuPlane plane)
        {
            const float Tolerance = 0.0001f;
            var failures = new List<string>();
            var bands = plane.ContextTextBands.Concat(plane.ReaderTextBands).ToList();
            if (bands.Count == 0) { failures.Add(name + ": the kit was laid with no text bands"); return failures; }
            // The compact presence stands in the bar's place, with no composition: its zoom is the text size's.
            var zoom = plane.Composition?.Zoom ?? GlazeText.Scale;
            var least = MenuPage.LeastEm(bands, zoom);
            if (least < Glaze.MinimumTextDegrees)
                failures.Add(name + ": the kit's laid text bands read under the floor, least em " + least.ToString("0.000", CultureInfo.InvariantCulture));
            var (_, rightAxis, upAxis) = PlaneComposition.Axes(plane.Direction);
            var right = new Vector3(rightAxis.X, rightAxis.Y, rightAxis.Z);
            var up = new Vector3(upAxis.X, upAxis.Y, upAxis.Z);
            Vector2 OnPlane(Vector3 world) => new Vector2(Vector3.Dot(world - plane.Eyes, right), Vector3.Dot(world - plane.Eyes, up)) / PlaneComposition.Distance;
            bool Drawn(TMP_Text label) => !string.IsNullOrEmpty(label.text) && !(label.GetComponent<MeshRenderer>() is MeshRenderer renderer && !renderer.enabled);
            string? Misplaced(TMP_Text label)
            {
                label.ForceMeshUpdate();
                // As GlazeChecks.TextSeen measures a label's em, in units of the plane's distance.
                var em = label.fontSize * 0.1f * label.transform.lossyScale.y / PlaneComposition.Distance;
                var icon = GlazeIcons.IsIcon(label);
                for (var index = 0; index < label.textInfo.characterCount; index++)
                {
                    var glyph = label.textInfo.characterInfo[index];
                    if (!glyph.isVisible) continue;
                    var corners = new[] { glyph.bottomLeft, glyph.topLeft, glyph.topRight, glyph.bottomRight }
                        .Select(corner => OnPlane(label.transform.TransformPoint(corner))).ToArray();
                    float left = corners.Min(corner => corner.x), rightmost = corners.Max(corner => corner.x);
                    float bottom = corners.Min(corner => corner.y), top = corners.Max(corner => corner.y);
                    var holding = bands.Where(band => band.Left - Tolerance <= left && rightmost <= band.Right + Tolerance
                        && band.Bottom - Tolerance <= bottom && top <= band.Top + Tolerance).ToList();
                    if (holding.Count == 0)
                        return name + ": a glyph of " + PathOf(label.transform) + " stands outside the kit's text bands at "
                            + left.ToString("0.0000", CultureInfo.InvariantCulture) + ".." + rightmost.ToString("0.0000", CultureInfo.InvariantCulture) + " x "
                            + bottom.ToString("0.0000", CultureInfo.InvariantCulture) + ".." + top.ToString("0.0000", CultureInfo.InvariantCulture);
                    if (icon || holding.Any(band => em >= GlazeTokens.Units(band.EmDegrees) * zoom * 0.9999f)) continue;
                    // Small text keeps its caption size only where it reads there by itself.
                    if (MenuPage.LeastEm(new[] { new TextBand(left, rightmost, bottom, top, Mathf.Atan(em / zoom) * Mathf.Rad2Deg) }, zoom) < Glaze.MinimumTextDegrees)
                        return name + ": kit text " + PathOf(label.transform) + " is smaller than its band's em and reads under the floor by itself";
                }
                return null;
            }
            var labels = plane.Kit.GetComponentsInChildren<TMP_Text>(false).Where(Drawn).ToList();
            failures.AddRange(labels.Select(Misplaced).OfType<string>());
            if (failures.Count > 0) return failures;
            // The witnesses, on an actual kit label that reads in place.
            if (labels.FirstOrDefault(label => !GlazeIcons.IsIcon(label)) is not TMP_Text witness)
            {
                failures.Add(name + ": the kit shows no words to witness its text bands with");
                return failures;
            }
            var position = witness.transform.position;
            try
            {
                // Left of every band the kit was laid with, where no face stands.
                witness.transform.position = PlaneLayout.PointOf(plane.Eyes, plane.Direction, bands.Min(band => band.Left) - 0.05f, 0f);
                if (Misplaced(witness) == null)
                    failures.Add(name + ": the kit's text-band containment accepted an actual kit label moved off every face");
            }
            finally { witness.transform.position = position; }
            var size = witness.fontSize;
            try
            {
                witness.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(Glaze.MinimumTextDegrees * 0.8f));
                if (Misplaced(witness) == null)
                    failures.Add(name + ": the kit's text em check accepted an actual kit label shrunk below the reading floor");
            }
            finally { witness.fontSize = size; witness.ForceMeshUpdate(); }
            return failures;
        }

        /// <summary>
        /// Every target the kit shows is a whole ordinary hand target as the eyes see it, as the plane placed it, and every two
        /// stand 12 mm apart, edge to edge: the rules KitRender holds the kit to, with the switch standing in the spine (D3.5).
        /// </summary>
        private static List<string> KitTargets(string name, MenuPlane plane)
        {
            var failures = new List<string>();
            var targets = plane.Kit.GetComponentsInChildren<KitPress>(false).ToList();
            if (targets.Count == 0) { failures.Add(name + ": the kit shows no target"); return failures; }
            static string Degrees(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
            foreach (var press in targets)
            {
                var distance = GlazeChecks.PlaneDistance(plane.Eyes, press.transform);
                var scale = press.transform.lossyScale;
                float tall = Glaze.DegreesOf(press.Size.y * scale.y, distance), wide = Glaze.DegreesOf(press.Size.x * scale.x, distance);
                if (Mathf.Min(tall, wide) < SpatialKit.TargetDegrees * 0.96f - 0.01f)
                    failures.Add(name + ": the kit's " + PathOf(press.transform) + " is " + Degrees(wide) + " by " + Degrees(tall) + " degrees, under " + Degrees(SpatialKit.TargetDegrees));
            }
            var space = plane.Kit.transform;
            var rects = targets.Select(press =>
            {
                var half = press.Size / 2f;
                var a = space.InverseTransformPoint(press.transform.TransformPoint(new Vector3(-half.x, -half.y, 0f)));
                var b = space.InverseTransformPoint(press.transform.TransformPoint(new Vector3(half.x, half.y, 0f)));
                return (press, Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }).ToList();
            for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var (first, a) = rects[i];
                var (second, b) = rects[j];
                var dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
                var dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
                var apart = Mathf.Sqrt(dx * dx + dy * dy) * space.lossyScale.x;
                if (apart < Glaze.TargetGapMeters - 0.0003f)
                    failures.Add(name + ": the kit's " + first.name + " and " + second.name + " are " + (apart * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart, under 12");
            }
            return failures;
        }

        private static readonly FieldInfo DirectorTaskPortraits = typeof(MenuDirector).GetField("showTaskPortraits", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>
        /// Compact and Workspace through the kit's own switch (D3.5), from a task's file the kit draws: a Compact the route
        /// refuses (away from the headset) reaches nothing and moves no thumb. With the task's portrait on, as by default,
        /// Compact leaves the closed bar standing, which the kit can't yet stand in for, and the bar's Open brings the kit
        /// back. With it off, Compact reaches the existing Minimize route and the compact presence stands where the closed
        /// bar would, naming the task and its state, its words in its band at the floor, its targets whole, both thumbs at
        /// Compact; Workspace on the presence reaches the bar's Open, and the task's file comes back through the kit with
        /// its draft and both thumbs at Workspace.
        /// </summary>
        private static List<string> KitCompactAndOpen(string name, string folder, Camera camera, RenderTexture texture, MenuDirector director)
        {
            var failures = new List<string>();
            var plane = director.Plane;
            kitCompactWitnessed = true;
            var spine = plane.Kit.View.Spine.Switch;
            var presence = plane.Kit.Presence;
            if (!spine.gameObject.activeInHierarchy || !spine.CompactTarget.gameObject.activeInHierarchy)
            {
                failures.Add(name + ": the kit's spine shows no switch to Compact");
                return failures;
            }
            var file = director.Navigator.Beside as FileColumn;
            var draft = file?.Screen.Instruction.Words;
            var title = plane.KitBinding?.Model.TaskTitle;
            // Refused by the route: away from the headset, Compact reaches nothing, and no thumb moves on a press alone.
            KitTick(director);
            FocusGuard.FoldForRender(true);
            try { spine.CompactTarget.PressForRender(961); }
            finally { FocusGuard.FoldForRender(null); }
            if (director.IsCompact || plane.KitCompactShows || spine.ConfirmedCompact || presence.Switch.ConfirmedCompact || spine.ThumbPosition != 0f)
                failures.Add(name + ": a Compact the route refused moved a thumb or compacted the workspace");
            KitTick(director);
            spine.CompactTarget.PressForRender(962);
            KitDraw(director);
            if (!director.IsCompact || plane.KitCompactShows || plane.Bar is not MenuBarView dock)
                failures.Add(name + ": with the task's portrait on, Compact on the kit's switch did not leave the closed bar standing");
            else
            {
                dock.Shown = 1f;
                dock.Target.SettleForRender();
                dock.Target.Target.PointerForRender(964, Oculus.Interaction.PointerEventType.Select, dock.Target.transform.position);
                dock.Target.Target.PointerForRender(964, Oculus.Interaction.PointerEventType.Unselect, dock.Target.transform.position);
                KitDraw(director);
                if (director.IsCompact || !plane.KitShows || spine.ConfirmedCompact)
                    failures.Add(name + ": the closed bar's Open did not bring the task's file back through the kit at Workspace");
            }
            var portraits = (bool)DirectorTaskPortraits.GetValue(director)!;
            DirectorTaskPortraits.SetValue(director, false);
            try { failures.AddRange(KitPresenceLeg(name, folder, camera, texture, director, draft, title)); }
            finally { DirectorTaskPortraits.SetValue(director, portraits); KitDraw(director); }
            return failures;
        }

        /// <summary>The compact presence's half of <see cref="KitCompactAndOpen"/>, with no portrait for the bar to show.</summary>
        private static List<string> KitPresenceLeg(string name, string folder, Camera camera, RenderTexture texture, MenuDirector director, string? draft, string? title)
        {
            var failures = new List<string>();
            var plane = director.Plane;
            var spine = plane.Kit.View.Spine.Switch;
            var presence = plane.Kit.Presence;
            KitDraw(director);
            KitTick(director);
            spine.CompactTarget.PressForRender(962);
            KitDraw(director);
            var compact = name + "-compact";
            if (!director.IsCompact || !plane.KitCompactShows || plane.KitShows || plane.Bar != null || plane.Shown.Count != 0 || plane.Chrome.Visible)
                failures.Add(compact + ": Compact on the kit's switch did not leave the presence alone in the closed bar's place: compact " + director.IsCompact
                    + ", presence " + plane.KitCompactShows + ", file " + plane.KitShows + ", bar " + (plane.Bar != null) + ", views " + plane.Shown.Count + ", chrome " + plane.Chrome.Visible);
            else
            {
                if (!spine.ConfirmedCompact || !presence.Switch.ConfirmedCompact || presence.Switch.ThumbPosition != 1f)
                    failures.Add(compact + ": the confirmed presentation did not bring both thumbs to Compact, the presence's at "
                        + presence.Switch.ThumbPosition.ToString("0.00", CultureInfo.InvariantCulture));
                if (title == null || !presence.Title.text.Contains(title.Split(' ')[0]) || !presence.State.gameObject.activeSelf)
                    failures.Add(compact + ": the presence does not name the retained task and its state as the closed bar does");
                ForceMeshes(plane.gameObject);
                failures.AddRange(KitTextInBands(compact, plane));
                failures.AddRange(KitTargets(compact, plane));
                camera.transform.rotation = PlaneLayout.Facing(plane.Direction);
                var image = Render(camera, texture); File.WriteAllBytes(Path.Combine(folder, compact + "-kit.png"), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            }
            KitTick(director);
            presence.Switch.WorkspaceTarget.PressForRender(963);
            KitDraw(director);
            if (director.IsCompact || plane.KitCompactShows || !plane.KitShows)
                failures.Add(name + ": Workspace on the presence did not bring the task's file back through the kit");
            if (spine.ConfirmedCompact || presence.Switch.ConfirmedCompact || spine.ThumbPosition != 0f)
                failures.Add(name + ": the confirmed restore did not bring both thumbs to Workspace, the spine's at "
                    + spine.ThumbPosition.ToString("0.00", CultureInfo.InvariantCulture));
            if ((director.Navigator.Beside as FileColumn)?.Screen.Instruction.Words != draft)
                failures.Add(name + ": Compact and Workspace through the kit lost the task's draft");
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
