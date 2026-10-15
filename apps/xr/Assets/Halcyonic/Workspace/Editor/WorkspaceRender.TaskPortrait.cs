#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        public static void TaskPortraitCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "TaskPortraitRenders", variant));
                Directory.CreateDirectory(folder);
                try { return RenderTaskPortrait(folder); }
                catch (Exception error) { return new List<string> { error.ToString() }; }
                finally { KeepFontAssetsAsCommitted(); }
            });
            foreach (var failure in failures) Debug.LogError("Halcyonic: task portrait: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: task portrait: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static float RasterDifference(Color left, Color right) =>
            Mathf.Max(Mathf.Abs(left.r - right.r), Mathf.Max(Mathf.Abs(left.g - right.g), Mathf.Abs(left.b - right.b)));

        private static List<string> RenderTaskPortrait(string folder)
        {
            var failures = new List<string>(); var root = new GameObject("Actual task portrait state witnesses");
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var priorStill = GlazeMotion.Still;
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, target); camera.transform.rotation = Quaternion.identity;
                var panel = new GameObject("Actual heading allocation"); panel.transform.SetParent(root.transform, false);
                panel.transform.position = eyes + Vector3.forward * PlaneComposition.Distance;
                panel.transform.localScale = Vector3.one * PlaneComposition.Distance * GlazeText.Scale;
                var face = Surface.Create(panel.transform, "Opaque actual heading support", 16);
                face.DrawCoatedInstrument(new Vector2(.6f, .2f), .01f, GlazeTokens.ColorOf(Glaze.Panel));
                var avatar = TaskIdentityAvatarView.Create(panel.transform);
                var slot = new Vector2(GlazeTokens.Units(3.5f), GlazeTokens.Units(5f));
                var title = GlazeText.Create(panel.transform, "Literal real task name", GlazeType.Body, GlazeTokens.Text, TMPro.TextAlignmentOptions.Left, 23);
                var status = GlazeText.Create(panel.transform, "Written task state", GlazeType.Caption, GlazeTokens.TextSecondary, TMPro.TextAlignmentOptions.Left, 23);
                title.transform.localPosition = new Vector3(-.12f, .02f, -.001f);
                status.transform.localPosition = new Vector3(-.12f, -.025f, -.001f);
                var centre = new Vector2(-.22f, 0f);
                var id = Work.Running().Workstream.WorkstreamId;
                CharacterPresentation State(CharacterActivity activity, string task = "") => new CharacterPresentation(
                    string.IsNullOrEmpty(task) ? id : task, "Add rate limiting", activity, "", AttentionLevel.None,
                    Array.Empty<string>(), 0, false, false, false);
                foreach (var activity in new[] { CharacterActivity.Idle, CharacterActivity.Working, CharacterActivity.WaitingForHuman, CharacterActivity.Unknown })
                {
                    var presentation = State(activity);
                    avatar.Show(presentation, null, centre, slot);
                    GlazeText.SetLiteral(title, presentation.Title); GlazeText.Lay(title, .4f, 1);
                    GlazeText.SetLiteral(status, StateLanguage.BadgeOf(presentation).Text); GlazeText.Lay(status, .4f, 1);
                    var character = avatar.Character!;
                    if (avatar.WorkstreamId != id || character.Presentation != presentation || !character.BodyOnlyPresentation)
                        failures.Add(activity + ": portrait lost real identity/state binding");
                    if (character.Label.gameObject.activeInHierarchy || character.GetComponentsInChildren<PointerTarget>(true).Length != 0
                        || character.GetComponentsInChildren<Collider>(true).Length != 0)
                        failures.Add(activity + ": portrait acquired duplicate labels or stage interaction");
                    GlazeMotion.Still = false;
                    var movingPositions = new List<Vector3>();
                    var motionStrip = new Texture2D(128 * 4, 128, TextureFormat.RGBA32, false);
                    var samplePoint = camera.WorldToScreenPoint(avatar.transform.position);
                    var sampleRect = new RectInt(Mathf.Clamp((int)samplePoint.x - 64, 0, Size - 128), Mathf.Clamp((int)samplePoint.y - 64, 0, Size - 128), 128, 128);
                    var rasterFrames = new List<Color[]>();
                    for (var tick = 0; tick < 180; tick++)
                    {
                        character.AdvanceForRender(1f / 60f);
                        movingPositions.Add(character.Body.localPosition);
                        if (tick == 30 || tick == 60 || tick == 90 || tick == 120)
                        {
                            var rendered = Render(camera, target);
                            try
                            {
                                var pixels = rendered.GetPixels(sampleRect.x, sampleRect.y, 128, 128);
                                motionStrip.SetPixels(rasterFrames.Count * 128, 0, 128, 128, pixels);
                                rasterFrames.Add(pixels);
                            }
                            finally { UnityEngine.Object.DestroyImmediate(rendered); }
                        }
                        foreach (var mesh in character.GetComponentsInChildren<MeshFilter>(false))
                        {
                            if (mesh.GetComponent<Renderer>() is not Renderer renderer || !renderer.enabled) continue;
                            foreach (var vertex in mesh.sharedMesh.vertices)
                            {
                                var p = panel.transform.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                                if (Mathf.Abs(p.x - centre.x) > slot.x / 2f + .0001f || Mathf.Abs(p.y - centre.y) > slot.y / 2f + .0001f || p.z >= -.0001f)
                                { failures.Add(activity + ": actual animated mesh escaped heading slot or crossed face"); break; }
                            }
                        }
                    }
                    if ((activity == CharacterActivity.Idle || activity == CharacterActivity.Working)
                        && movingPositions.All(p => Vector3.Distance(p, movingPositions[0]) < .00001f))
                        failures.Add(activity + ": actual live portrait motion never advanced");
                    motionStrip.Apply();
                    try { File.WriteAllBytes(Path.Combine(folder, activity + "-actual-motion-frames.png"), motionStrip.EncodeToPNG()); }
                    finally { UnityEngine.Object.DestroyImmediate(motionStrip); }
                    var changedPixels = rasterFrames.Skip(1).Sum(frame => frame.Where((pixel, index) => RasterDifference(pixel, rasterFrames[0][index]) > 2f / 255f).Count());
                    Debug.Log("Halcyonic: task portrait actual raster motion " + activity + ": changed-pixels=" + changedPixels + ", frames=" + rasterFrames.Count);
                    if ((activity == CharacterActivity.Idle || activity == CharacterActivity.Working) && changedPixels == 0)
                        failures.Add(activity + ": actual rendered live frames never changed");
                    var beforeHue = character.BodyColourForRender;
                    avatar.Show(presentation, CharacterColourSettings.Hue("violet"), centre, slot);
                    if (character.BodyColourForRender == beforeHue || avatar.Character != character)
                        failures.Add(activity + ": local colour failed to update same identity in place");
                    var image = Render(camera, target);
                    try { File.WriteAllBytes(Path.Combine(folder, activity + ".png"), image.EncodeToPNG()); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                    GlazeMotion.Still = true;
                    for (var tick = 0; tick < 240; tick++) character.AdvanceForRender(1f / 60f);
                    var stillPosition = character.Body.localPosition; var stillRotation = character.Body.localRotation; var stillScale = character.Body.localScale;
                    var stillPose = character.PoseForRender;
                    var heldBefore = Render(camera, target);
                    var heldPixels = heldBefore.GetPixels(sampleRect.x, sampleRect.y, 128, 128);
                    UnityEngine.Object.DestroyImmediate(heldBefore);
                    for (var tick = 0; tick < 120; tick++) character.AdvanceForRender(1f / 60f);
                    if (Vector3.Distance(stillPosition, character.Body.localPosition) > .00001f
                        || Quaternion.Angle(stillRotation, character.Body.localRotation) > .001f || stillScale != character.Body.localScale
                        || stillPose != character.PoseForRender)
                        failures.Add(activity + ": Still portrait visibly changed after settling");
                    var still = Render(camera, target);
                    try
                    {
                        var afterPixels = still.GetPixels(sampleRect.x, sampleRect.y, 128, 128);
                        var changedStill = afterPixels.Where((pixel, index) => RasterDifference(pixel, heldPixels[index]) > 1f / 255f).Count();
                        Debug.Log("Halcyonic: task portrait actual Still raster " + activity + ": changed-pixels=" + changedStill);
                        if (changedStill != 0) failures.Add(activity + ": actual Still raster changed after settling");
                        File.WriteAllBytes(Path.Combine(folder, activity + "-still.png"), still.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(still); }
                }
                var old = avatar.Character!;
                avatar.Show(State(CharacterActivity.Idle, "other-real-task"), null, centre, slot);
                if (old != null || avatar.WorkstreamId != "other-real-task" || root.GetComponentsInChildren<CharacterView>(false).Length != 1)
                    failures.Add("task switch retained the previous identity or duplicated its live body");
                avatar.Hide();
                if (avatar.Character != null || avatar.WorkstreamId != null || root.GetComponentsInChildren<CharacterView>(true).Length != 0)
                    failures.Add("explicit Hide retained an identity or body");
                avatar.Show(State(CharacterActivity.Working), null, centre, slot);
                if (!avatar.gameObject.activeSelf || avatar.WorkstreamId != id) failures.Add("explicit Show did not restore selected task identity");
            }
            finally { GlazeMotion.Still = priorStill; UnityEngine.Object.DestroyImmediate(root); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            return failures;
        }
    }
}
