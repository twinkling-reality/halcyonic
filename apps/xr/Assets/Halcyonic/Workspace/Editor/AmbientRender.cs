#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders the stage beside a window-sized plate, as a browser video or a Mac's Virtual Display
    /// would stand in front of the person, and the large panels folded and restored as focus goes and
    /// comes back. It checks that with the lineup turned aside (<see cref="CharacterStage.AsideDegrees"/>)
    /// the window covers fewer characters' bodies than in front, and that a folded panel leaves nothing
    /// of itself and comes back pixel for pixel as it was. The window is a plate of a typical size,
    /// 1.4 by 0.79 m at 1.6 m, centered at eye level: Halcyonic cannot see a real window, so this shows
    /// what the placement can do, not what a headset will show. It saves each render in
    /// apps/xr/Builds/AmbientRenders, which git ignores. In the editor: Halcyonic > Render the Stage
    /// Beside a Window. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1
    /// when a check fails.
    /// </summary>
    public static class AmbientRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;
        private const float WindowDistance = 1.6f;
        private static readonly Vector2 WindowSize = new Vector2(1.4f, 0.79f);

        [MenuItem("Halcyonic/Render the Stage Beside a Window")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = Run();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Ambient render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "AmbientRenders"));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(BesideAWindow(folder));
                failures.AddRange(FoldAndRestore(folder));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                FocusGuard.FoldForRender(null);
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: ambient render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: ambient render: every check passed; the renders are in " + folder);
            return failures;
        }

        /// <summary>Six characters 2.4 m away, the stage's own geometry, in front or turned aside, behind a window.</summary>
        private static IEnumerable<string> BesideAWindow(string folder)
        {
            var failures = new List<string>();
            var covered = new Dictionary<bool, int>();
            foreach (var aside in new[] { false, true })
            {
                var name = aside ? "aside" : "front";
                var root = new GameObject("Ambient render " + name);
                var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                try
                {
                    var eyes = new Vector3(0f, EyeHeight, 0f);
                    var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                    var characters = Characters(root.transform, eyes, aside ? CharacterStage.AsideDegrees : 0f);
                    var window = Window(root.transform, eyes);
                    WorkspaceRender.ForceMeshes(root);
                    var render = WorkspaceRender.Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, "window-" + name + ".png"), render.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(render);
                    var outline = Outline(camera, window.transform);
                    var count = 0;
                    var labels = 0;
                    foreach (var target in characters)
                    {
                        var body = camera.WorldToScreenPoint(target.BodyPosition);
                        if (Inside(outline, new Vector2(body.x, body.y))) count++;
                        if (Behind(outline, WorkspaceRender.LabelRect(camera, target.View))) labels++;
                    }
                    covered[aside] = count;
                    Debug.Log("Halcyonic: ambient render: with the lineup " + (aside ? "turned aside" : "in front") + ", a window covers "
                        + count + " of " + characters.Count + " characters' bodies and " + labels + " of their labels.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            if (covered[true] >= covered[false]) failures.Add("turning the lineup aside does not uncover any character.");
            return failures;
        }

        /// <summary>The entry panel and the Usage left panel, open, then folded, then restored.</summary>
        private static IEnumerable<string> FoldAndRestore(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Ambient render fold");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var characters = Characters(root.transform, eyes, 0f);
                var state = EntryRender.Portfolio(hostile: false, needsYouNow: false);
                var visibility = new StageVisibility();
                var lineup = new CharacterLineup(6);
                lineup.Update(state.Workstreams.Values);
                var overview = WorkOverview.Of(state, visibility, id => lineup.SlotOf(id) >= 0);

                var entry = EntryPanel.ForRender(root.transform, state, overview, characters, null);
                entry.ShowForRender(EntryPanel.Screen.Connect);
                failures.AddRange(Fold("entry", folder, root, camera, texture, entry.Root.gameObject, entry.ApplyFold));
                entry.Root.gameObject.SetActive(false);

                var rail = ProjectRail.ForRender(root.transform, overview, null);
                rail.ResetPosition();
                var glance = UsageLeftGlance.ForRender(rail);
                glance.ShowForRender(UsageLeftPresenter.Message(UsageLeftPresenter.NotSetUp), characters, null);
                rail.Root.gameObject.SetActive(false);
                failures.AddRange(Fold("usage-left", folder, root, camera, texture, glance.Panel.gameObject, glance.ApplyFold));
            }
            finally
            {
                FocusGuard.FoldForRender(null);
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        private static IEnumerable<string> Fold(string name, string folder, GameObject root, Camera camera, RenderTexture texture, GameObject panel, Action apply)
        {
            var failures = new List<string>();
            FocusGuard.FoldForRender(false);
            apply();
            WorkspaceRender.ForceMeshes(root);
            var open = WorkspaceRender.Render(camera, texture);
            FocusGuard.FoldForRender(true);
            apply();
            var folded = WorkspaceRender.Render(camera, texture);
            if (panel.activeInHierarchy) failures.Add(name + ": the panel still shows while folded.");
            // Folded, it no longer covers the stage's banner, which then says what waits for the person.
            if (AmbientCover.Any) failures.Add(name + ": something still covers the stage's banner while the panel is folded.");
            FocusGuard.FoldForRender(false);
            apply();
            WorkspaceRender.ForceMeshes(root);
            var restored = WorkspaceRender.Render(camera, texture);
            var whole = new RectInt(0, 0, Size, Size);
            var (gone, _) = WorkspaceRender.Compare(open, folded, whole);
            var (moved, largest) = WorkspaceRender.Compare(open, restored, whole);
            File.WriteAllBytes(Path.Combine(folder, name + "-open.png"), open.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, name + "-folded.png"), folded.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, name + "-restored.png"), restored.EncodeToPNG());
            if (gone == 0) failures.Add(name + ": folding changed nothing on the render.");
            if (moved > 0) failures.Add(name + ": " + moved + " pixels differ after restoring (largest " + largest.ToString("0.000", CultureInfo.InvariantCulture) + ").");
            Debug.Log("Halcyonic: ambient render: " + name + " folds away " + gone + " pixels and comes back " + (moved == 0 ? "exactly." : "with " + moved + " pixels changed."));
            UnityEngine.Object.DestroyImmediate(open);
            UnityEngine.Object.DestroyImmediate(folded);
            UnityEngine.Object.DestroyImmediate(restored);
            return failures;
        }

        /// <summary>Six characters on the stage's arc, at the stage's default distance and height, as CharacterStage stands them.</summary>
        private static List<CharacterTarget> Characters(Transform parent, Vector3 eyes, float turn) =>
            WorkspaceRender.Lineup(parent, eyes, CharacterStage.DefaultDistance, null, WorkspaceRender.Presentation, turn).ConvertAll(character => character.Target);

        /// <summary>
        /// The window: an opaque quad of <see cref="WindowSize"/> straight ahead at eye level, drawn
        /// over everything Halcyonic draws, as the system draws a window over the app.
        /// </summary>
        private static GameObject Window(Transform parent, Vector3 eyes)
        {
            var window = GameObject.CreatePrimitive(PrimitiveType.Quad);
            window.name = "Window";
            UnityEngine.Object.DestroyImmediate(window.GetComponent<Collider>());
            window.transform.SetParent(parent, false);
            window.transform.SetPositionAndRotation(eyes + Vector3.forward * WindowDistance, Quaternion.identity);
            window.transform.localScale = new Vector3(WindowSize.x, WindowSize.y, 1f);
            var material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.18f, 0.2f, 0.24f, 1f) };
            var renderer = window.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 100;
            return window;
        }

        /// <summary>The window's outline on the render, a trapezoid, its corners counterclockwise.</summary>
        private static Vector2[] Outline(Camera camera, Transform window)
        {
            var unit = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            var corners = new Vector2[4];
            for (var index = 0; index < 4; index++) corners[index] = camera.WorldToScreenPoint(window.TransformPoint(unit[index]));
            return corners;
        }

        /// <summary>Whether a point of the render falls inside the window's outline.</summary>
        private static bool Inside(Vector2[] outline, Vector2 point)
        {
            for (var edge = 0; edge < outline.Length; edge++)
            {
                var from = outline[edge];
                var to = outline[(edge + 1) % outline.Length];
                if ((to.x - from.x) * (point.y - from.y) - (to.y - from.y) * (point.x - from.x) < 0f) return false;
            }
            return true;
        }

        /// <summary>Whether any part of <paramref name="label"/>, a rectangle on the render, falls inside the window's outline, sampled at its corners, edges and middle.</summary>
        private static bool Behind(Vector2[] outline, Rect label)
        {
            for (var x = 0; x <= 2; x++)
            {
                for (var y = 0; y <= 2; y++)
                {
                    if (Inside(outline, new Vector2(Mathf.Lerp(label.xMin, label.xMax, x / 2f), Mathf.Lerp(label.yMin, label.yMax, y / 2f)))) return true;
                }
            }
            return false;
        }
    }
}
