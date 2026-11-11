#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class StageRender
    {
        /// <summary>
        /// The redesign's shelf (DESIGN 2.2), at both text sizes: a character's name tag in the panel's material, one line
        /// of its task's name 0.8 degrees under its feet, the older label not standing, and the picked one ringed; a task that
        /// needs the person says so beside its name (FIELD's dot and Needs You). The
        /// renders are in Builds/RoomShelfRenders for a look beside DESIGN's page.
        /// </summary>
        public static void RoomShelfCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(RoomShelf);
            foreach (var failure in failures) Debug.LogError("Halcyonic: room shelf: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: room shelf: every check passed.");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>The tag's words with runs of spaces as one: the renderer sets a space apart before a separator after outside words.</summary>
        private static string Squeezed(string words) => System.Text.RegularExpressions.Regex.Replace(words, " {2,}", " ");

        private static List<string> RoomShelf(string variant)
        {
            var failures = new List<string>();
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "RoomShelfRenders", variant));
            Directory.CreateDirectory(folder);
            var was = RoomDesign.Enabled;
            var previousField = ViewField.Current;
            try
            {
                RoomDesign.Enabled = true;
                ViewField.Current = FieldChecks.Quest3S;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (var (name, picked, needs) in new[] { ("rest", false, false), ("picked", true, false), ("needs", false, true) })
                {
                    var root = new GameObject("Room shelf " + name);
                    var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                    try
                    {
                        var eyes = new Vector3(0f, EyeHeight, 0f);
                        var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                        var plane = CharacterConsole(root.transform, eyes);
                        // A task waiting on two approvals: its need stands in words on the tag, not by its light alone (ADR 0023).
                        var shown = needs
                            ? new CharacterPresentation("room-shelf-needs", "A task", CharacterActivity.WaitingForHuman,
                                CharacterPresenter.LabelOf(CharacterActivity.WaitingForHuman), AttentionLevel.ActionRequired, Array.Empty<string>(), 2, false, false, false)
                            : Shown("room-shelf-witness", CharacterActivity.Idle);
                        var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, shown, failures, name);
                        var view = character.View;
                        var nameTag = RoomNameTag.On(view);
                        view.Show(shown);
                        nameTag.Picked = picked;
                        view.PlaceShelfLabel(eyes);
                        Advance(view, 1f);
                        WorkspaceRender.ForceMeshes(root);
                        if (nameTag.Tag is not RoomTag tag) { failures.Add(name + ": no name tag stands under the character"); continue; }
                        var reads = needs ? shown.Title + " · Needs You" : shown.Title;
                        if (Squeezed(tag.Words.GetParsedText()) != reads) failures.Add(name + ": the tag reads \"" + tag.Words.GetParsedText() + "\", not \"" + reads + "\"");
                        if (tag.NeedsYou != needs) failures.Add(name + ": the tag's dot " + (needs ? "does not stand though the task needs the person" : "stands though nothing waits"));
                        if (tag.Picked != picked) failures.Add(name + ": the tag " + (picked ? "wears no ring though picked" : "wears the ring though not picked"));
                        if (tag.Words.isTextTruncated || tag.Words.textInfo.lineCount != 1) failures.Add(name + ": the tag's words are not one whole line");
                        foreach (var label in view.GetComponentsInChildren<CharacterLabelView>(true))
                            if (label.gameObject.activeInHierarchy) failures.Add(name + ": the older label still stands beside the tag");
                        // Under the feet: the tag's top stands below the character's body.
                        var top = tag.transform.TransformPoint(new Vector3(0f, tag.Size.y / 2f, 0f));
                        if (top.y >= view.transform.position.y) failures.Add(name + ": the tag's top stands at or above the character's feet");
                        // The frame shows the shelf alone, looked at: the console is not part of the redesign's shelf.
                        plane.gameObject.SetActive(false);
                        camera.transform.LookAt(view.transform.position);
                        camera.fieldOfView = 30f;
                        var image = WorkspaceRender.Render(camera, texture);
                        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                        texture.Release();
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                RoomDesign.Enabled = was;
                ViewField.Current = previousField;
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            return failures;
        }
    }
}
