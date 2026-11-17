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
    public static partial class StageRender
    {
        /// <summary>The fewest amber pixels the waiting character's floor ring shows at its dim end in the shelf's 1024 px frame.</summary>
        private const int NeedsYouRingAmberFloor = 150;

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
                        // The name alone in the tag's words, its state under it in the label role (the one language, tile f1): the
                        // state's word is the badge's own, Needs You where the task needs the person; no chip stands beside the name.
                        var reads = shown.Title;
                        var stateWord = StateLanguage.BadgeOf(shown).Word;
                        if (tag.StateWords is not TMPro.TextMeshPro state || !string.Equals(state.GetParsedText().Trim(), stateWord, System.StringComparison.OrdinalIgnoreCase)
                            || (needs && stateWord != StateLanguage.WordOf(WorkState.WaitingForYou)))
                            failures.Add(name + ": the tag does not say its state \"" + stateWord + "\" under the name");
                        if (tag.GetComponentsInChildren<RoomShape>(false).Any(shape => shape.transform.parent == tag.transform && shape != tag.Ground && shape != tag.Stem && shape != tag.Diamond))
                            failures.Add(name + ": a chip stands beside the name though the state says it");
                        // Waiting on the person: a soft amber ring on the floor under it, breathing, and no light behind its body (the lead's
                        // 2026-11-17T02:02Z); nothing on the floor otherwise.
                        if (needs != (view.NeedsYouRing != null)) failures.Add(name + ": the floor ring " + (needs ? "does not stand though the task needs the person" : "stands though nothing waits"));
                        if (Squeezed(tag.Words.GetParsedText()) != reads) failures.Add(name + ": the tag reads \"" + tag.Words.GetParsedText() + "\", not \"" + reads + "\"");
                        if (tag.NeedsYou != needs) failures.Add(name + ": the tag's dot " + (needs ? "does not stand though the task needs the person" : "stands though nothing waits"));
                        if (tag.Picked != picked) failures.Add(name + ": the tag " + (picked ? "wears no ring though picked" : "wears the ring though not picked"));
                        if (tag.Words.isTextTruncated || tag.Words.textInfo.lineCount != 1) failures.Add(name + ": the tag's words are not one whole line");
                        foreach (var label in view.GetComponentsInChildren<CharacterLabelView>(true))
                            if (label.gameObject.activeInHierarchy) failures.Add(name + ": the older label still stands beside the tag");
                        // Under the feet: the tag's top stands below the character's body.
                        var top = tag.transform.TransformPoint(new Vector3(0f, tag.Size.y / 2f, 0f));
                        if (top.y >= view.transform.position.y) failures.Add(name + ": the tag's top stands at or above the character's feet");
                        // And under the body as the eyes see it (AUDIT 2026-11-16 13:58Z): its top lower in view than every point of the
                        // character's drawn body, so the tag never stands over the character's face.
                        float Up(Vector3 point) { var away = point - eyes; return Mathf.Atan2(away.y, new Vector2(away.x, away.z).magnitude) * Mathf.Rad2Deg; }
                        var lowestBody = view.BodyCorners().Min(Up);
                        if (Up(top) >= lowestBody - 0.3f)
                            failures.Add(name + ": the tag's top stands " + Up(top).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " degrees up, over the character's body down to "
                                + lowestBody.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ", as the eyes see it");
                        // The frame shows the shelf alone, looked at: the console is not part of the redesign's shelf.
                        plane.gameObject.SetActive(false);
                        camera.transform.LookAt(view.transform.position);
                        camera.fieldOfView = 30f;
                        var image = WorkspaceRender.Render(camera, texture);
                        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        if (needs)
                        {
                            // Four seconds of the waiting character at 12 frames a second (the lead's 02:02Z: a still and a 4 s clip both
                            // read calm): the floor ring's breath over two of its cycles, for AUDIT's eye. The ring's alpha must move.
                            var clip = Path.Combine(folder, "needs-clip");
                            Directory.CreateDirectory(clip);
                            var seen = new List<float>();
                            var amber = new List<int>();
                            for (var frame = 0; frame < 48; frame++)
                            {
                                Advance(view, 1f / 12f);
                                WorkspaceRender.ForceMeshes(root);
                                var still = WorkspaceRender.Render(camera, texture);
                                // The ring in the pixels: warm pixels, red over blue by 25 or more with green between (the amber's hue as it
                                // lies over the shelf's teal), never the character's own pink (blue over green) or the tag's white.
                                amber.Add(still.GetPixels32().Count(pixel => pixel.r > 60 && pixel.r - pixel.b >= 25 && pixel.g > pixel.b && pixel.g < pixel.r));
                                File.WriteAllBytes(Path.Combine(clip, "frame-" + frame.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ".png"), still.EncodeToPNG());
                                UnityEngine.Object.DestroyImmediate(still);
                                if (view.NeedsYouRing is MeshRenderer ring)
                                {
                                    var block = new MaterialPropertyBlock();
                                    ring.GetPropertyBlock(block);
                                    seen.Add(block.GetColor("_Color").a);
                                }
                            }
                            if (seen.Count < 48 || seen.Max() - seen.Min() < 0.2f)
                                failures.Add(name + ": the floor ring does not breathe over four seconds (" + (seen.Count == 0 ? "never drawn" : seen.Min().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " to " + seen.Max().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)) + ")");
                            // Seen at a glance at its dim end, and plainly rising and falling (the lead's 2026-11-17T03:24Z).
                            Debug.Log("Halcyonic: room shelf: " + name + ": the floor ring's amber pixels per frame " + string.Join(" ", amber));
                            if (amber.Min() < NeedsYouRingAmberFloor) failures.Add(name + ": the floor ring is not seen at its dim end: " + amber.Min() + " amber pixels, under " + NeedsYouRingAmberFloor);
                            if (amber.Max() < amber.Min() * 1.3f) failures.Add(name + ": the floor ring's amber does not rise and fall: " + amber.Min() + " to " + amber.Max() + " pixels");
                        }
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
