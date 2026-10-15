#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        public static void ReadingVisibilityCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant => {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ReadingVisibilityRenders", variant));
                Directory.CreateDirectory(folder);
                try { return RenderReadingVisibility(folder); }
                catch (Exception error) { return new List<string> { error.ToString() }; }
                finally { KeepFontAssetsAsCommitted(); }
            });
            foreach (var failure in failures) Debug.LogError("Halcyonic: reading visibility: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: reading visibility: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
        private static List<string> RenderReadingVisibility(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Actual development reading comparison");
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            ReadingVisibilityColumn? column = null;
            try
            {
                var assets = ReadingVisibilityInDevelopmentBuilds.AddTo(root.scene);
                var eyes = new Vector3(0, EyeHeight, 0);
                var camera = MakeCamera(root.transform, eyes, target); camera.transform.rotation = Quaternion.identity; camera.backgroundColor = Color.clear;
                var plane = MenuPlane.Create(root.transform); plane.SpatialWorkspace = true;
                column = new ReadingVisibilityColumn(assets.Opaque, assets.Mask);
                var frame = column.Frame;
                plane.Show(new MenuBar(MenuPlace.Settings, "Development check"), null, frame, null, Array.Empty<CharacterTarget>(), eyes, Vector3.forward, null, immediately: true, fileColumn: column);
                plane.Advance(MenuPlane.OpeningSeconds);
                column.Drawn(frame, null); ForceMeshes(root); column.Paint(plane); ForceMeshes(root);
                var image = Render(camera, target);
                try
                {
                    foreach (var expected in new[] { (Name: "Production opaque witness", Row: "reading-face", Alpha: 1f), (Name: "Opaque black", Row: "reading-opaque", Alpha: 1f), (Name: "Opaque white", Row: "reading-opaque", Alpha: 1f), (Name: "Known .15 alpha mask", Row: "reading-room", Alpha: .15f) })
                    {
                        var shape = root.GetComponentsInChildren<Transform>(false).Single(part => part.name == expected.Name);
                        var row = plane.Shown.Single().View.ShownLines.Single(line => line.Model.Key == expected.Row);
                        var lineHeight = GlazeText.LineHeight(row.Words);
                        foreach (var vertex in shape.GetComponent<MeshFilter>().sharedMesh.vertices)
                        {
                            var local = row.Words.transform.InverseTransformPoint(shape.TransformPoint(vertex));
                            if (local.x < 0 || local.x > row.Words.rectTransform.rect.width || local.y > 0 || local.y < -lineHeight)
                                failures.Add(expected.Name + ": actual patch escaped its allocated label row");
                        }
                        var patchLeft = row.Words.rectTransform.rect.width - .16f - MenuPage.Grid;
                        if (row.Words.textBounds.max.x + MenuPage.Grid >= patchLeft)
                            failures.Add(expected.Name + ": actual label glyphs overlap patch allocation");
                        var point = camera.WorldToScreenPoint(shape.position);
                        if (point.z <= 0 || point.x < 0 || point.y < 0 || point.x >= Size || point.y >= Size) { failures.Add(expected.Name + ": witness outside camera target"); continue; }
                        var pixel = image.GetPixel((int)point.x, (int)point.y);
                        Debug.Log("Halcyonic: reading visibility actual pixel " + expected.Name + ": alpha=" + pixel.a.ToString("F4") + ", rgb=" + pixel.r.ToString("F4") + "/" + pixel.g.ToString("F4") + "/" + pixel.b.ToString("F4"));
                        if (Mathf.Abs(pixel.a - expected.Alpha) > .015f) failures.Add(expected.Name + ": actual target alpha differs from control");
                        if (expected.Name == "Opaque black" && Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) > .01f) failures.Add("independent black reference is not black");
                        if (expected.Name == "Opaque white" && Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b)) < .98f) failures.Add("independent white reference is not white");
                    }
                    var composite = CompositeRoom(image);
                    try { File.WriteAllBytes(Path.Combine(folder, "actual-reading-comparison.png"), composite.EncodeToPNG()); }
                    finally { UnityEngine.Object.DestroyImmediate(composite); }
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
                var closed = 0; column.Closed += () => closed++;
                column.Act(Footer.Close, null);
                if (closed != 1) failures.Add("explicit Return did not close drawn local diagnostic");
                column.FocusLeft();
                if (root.GetComponentsInChildren<Transform>(true).Any(part => part.name == "Synthetic reading comparison patches")) failures.Add("leaving retained diagnostic patches");
                if (root.GetComponentsInChildren<Camera>(true).Length != 1) failures.Add("diagnostic changed normal camera ownership");
                UnityEngine.Object.DestroyImmediate(assets.gameObject);
            }
            finally { column?.FocusLeft(); UnityEngine.Object.DestroyImmediate(root); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            return failures;
        }
    }
}
