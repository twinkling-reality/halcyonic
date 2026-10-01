#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders the workspace open over the stage, in the editor, where the characters stand 2.4 m away
    /// and where they stand on a desk half a meter away, and checks two things the headset showed
    /// wrong: that nothing behind the workspace shows through it, and that it opens clear of every
    /// character's body, in the comfortable band. It then renders its Understanding and Evaluation
    /// sections with the demonstration's answers, checks that they are as opaque, that every line fits
    /// on one line, and that source text shows exactly as written on a real TextMeshPro label. It
    /// renders an approval's confirmation for a very long shell command, whose whole request must
    /// show in parts before the approval can be confirmed; and it puts hostile text (markup, backslash
    /// sequences, an end of text character, bidirectional and invisible characters) on every label that
    /// shows text Halcyonic did not write, and fails when one interprets any of it or drops the end of
    /// a line without an ellipsis. The Meta XR Simulator renders nothing on the development Mac, so
    /// this is the check short of a headset. It saves each render as a PNG in
    /// apps/xr/Builds/WorkspaceRenders, which git ignores.
    /// In the editor: Halcyonic > Render the Workspace Over the Stage. In batch mode, with the editor
    /// closed, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1 when a check fails.
    /// </summary>
    public static class WorkspaceRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;

        /// <summary>A Quest 3's resolution near the middle of its lenses.</summary>
        private const float QuestPixelsPerDegree = 25f;

        /// <summary>Two channels' worth of rounding, out of 255, before a pixel counts as changed.</summary>
        private const float Tolerance = 2f / 255f;

        /// <summary>Pixels kept off the workspace's outline, where its rounded corners blend.</summary>
        private const int Inset = 12;

        /// <summary>Markup that starts every hostile text; a label that took it as markup would show only "b".</summary>
        private const string Marker = "<b>b</b>";

        private const string Backslash = "\\";

        private const char Ellipsis = '…';

        [MenuItem("Halcyonic/Render the Workspace Over the Stage")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = Run();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Workspace render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WorkspaceRenders"));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // The stage's default: 2.4 m away, 0.45 m below the eyes; the character that needs
                // the person, in the middle, opened.
                failures.AddRange(RenderStage("far", folder, radius: 2.4f, bodyDrop: 0.45f, surfaceDrop: null));
                // A desk 0.46 m below the eyes, the lineup 0.55 m ahead, its bodies about 0.1 m above it.
                failures.AddRange(RenderStage("desk", folder, radius: 0.55f, bodyDrop: 0.36f, surfaceDrop: 0.46f));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: workspace render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: workspace render: every check passed; the renders are in " + folder);
            return failures;
        }

        private static IEnumerable<string> RenderStage(string name, string folder, float radius, float bodyDrop, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("Workspace render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);

                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var slots = new[] { -30f, -18f, -6f, 6f, 18f, 30f };
                for (var slot = 0; slot < slots.Length; slot++)
                {
                    var id = "render-" + slot.ToString(CultureInfo.InvariantCulture);
                    var view = CharacterView.Create(root.transform, id);
                    var level = Quaternion.Euler(0f, slots[slot], 0f) * Vector3.forward;
                    view.transform.SetPositionAndRotation(eyes + level * radius + Vector3.down * bodyDrop, Quaternion.LookRotation(-level, Vector3.up));
                    view.transform.localScale = Vector3.one * radius;
                    view.Show(Presentation(id, slot));
                    characters.Add((view, CharacterTarget.Attach(view, id)));
                }

                // Opened the way the director opens it: the person looking at the character that needs them.
                var opened = characters[3].Target;
                var scratch = new List<BodyInView>();
                var targets = characters.ConvertAll(character => character.Target);
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var (pose, direction) = WorkspaceLayout.Place(opened, targets, eyes, opened.BodyPosition - eyes, surface, scratch);
                var panel = WorkspacePanel.Create(root.transform);
                panel.transform.SetPositionAndRotation(pose.position, pose.rotation);
                panel.transform.localScale = Vector3.one * WorkspaceLayout.Scale;
                panel.Show(Content());
                // The tabs, with the activity chosen, as a workspace opens.
                var sections = WorkspaceSections.Attach(panel, () => null, () => null);
                foreach (var text in root.GetComponentsInChildren<TextMeshPro>(true)) text.ForceMeshUpdate();

                var both = Render(camera, texture);
                foreach (var (view, _) in characters) view.gameObject.SetActive(false);
                var panelAlone = Render(camera, texture);
                panel.gameObject.SetActive(false);
                var nothing = Render(camera, texture);
                foreach (var (view, _) in characters) view.gameObject.SetActive(true);
                var stageAlone = Render(camera, texture);
                panel.gameObject.SetActive(true);

                File.WriteAllBytes(Path.Combine(folder, name + ".png"), both.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, name + "-workspace.png"), panelAlone.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, name + "-stage.png"), stageAlone.EncodeToPNG());
                var activityCloseUp = CloseUp(camera, texture, panel.transform);
                File.WriteAllBytes(Path.Combine(folder, name + "-closeup.png"), activityCloseUp.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(activityCloseUp);

                var rect = ScreenRect(camera, panel.transform);
                var (changed, largest) = Compare(both, panelAlone, rect);
                // Where the stage draws something inside the workspace's outline when the workspace is not there.
                var passedOver = Compare(stageAlone, nothing, rect).Changed;
                UnityEngine.Object.DestroyImmediate(nothing);
                Debug.Log("Halcyonic: workspace render " + name + ": the workspace's center is " + Degrees(direction.Elevation) + " degrees from eye level, "
                    + (direction.Above ? "above" : "below") + " the characters it passes; " + passedOver + " of its pixels have the stage behind them, and "
                    + changed + " change when the stage is drawn (largest change " + Degrees(largest * 255f) + " of 255).");
                if (passedOver == 0) failures.Add(name + ": nothing of the stage is behind the workspace, so the render checks nothing; move the camera or the stage.");
                if (changed > 0) failures.Add(name + ": " + changed + " pixels of the workspace change when the stage behind it is drawn.");
                if (!direction.Clear) failures.Add(name + ": the workspace covers a character's body.");
                if (direction.Elevation < WorkspacePlacement.LowestDegrees - 0.01f || direction.Elevation > WorkspacePlacement.HighestDegrees + 0.01f)
                {
                    failures.Add(name + ": the workspace's center is outside the comfortable band.");
                }
                foreach (var (view, target) in characters)
                {
                    if (Covered(camera, target, rect)) failures.Add(name + ": " + view.WorkstreamId + "'s body is behind the workspace in the render.");
                }
                UnityEngine.Object.DestroyImmediate(both);
                UnityEngine.Object.DestroyImmediate(panelAlone);
                UnityEngine.Object.DestroyImmediate(stageAlone);

                // The Understanding and Evaluation sections under the tabs, as the demonstration shows them.
                var swapped = new SortedSet<char>();
                foreach (var (suffix, shown) in DemonstrationSections())
                {
                    var section = InStaticAtlas(shown, sections.View.Labels.First().font, swapped);
                    sections.ShowFixed(section);
                    foreach (var text in root.GetComponentsInChildren<TextMeshPro>(true)) text.ForceMeshUpdate();
                    var withStage = Render(camera, texture);
                    foreach (var (view, _) in characters) view.gameObject.SetActive(false);
                    var sectionAlone = Render(camera, texture);
                    foreach (var (view, _) in characters) view.gameObject.SetActive(true);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + ".png"), withStage.EncodeToPNG());
                    var closeUp = CloseUp(camera, texture, panel.transform);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + "-closeup.png"), closeUp.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(closeUp);
                    var (sectionChanged, _) = Compare(withStage, sectionAlone, rect);
                    if (sectionChanged > 0) failures.Add(name + ": " + sectionChanged + " pixels of the " + suffix + " section change when the stage behind it is drawn.");
                    failures.AddRange(Fits(sections.View, name + " " + suffix, section));
                    UnityEngine.Object.DestroyImmediate(withStage);
                    UnityEngine.Object.DestroyImmediate(sectionAlone);
                }
                if (swapped.Count > 0)
                {
                    Debug.Log("Halcyonic: workspace render " + name + ": drawn in the renders from the static atlas instead, since a headset draws them from the dynamic fallback: "
                        + string.Join(", ", swapped.Select(character => "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture))) + ".");
                }
                failures.AddRange(AnswersTheQuestions(name, folder, camera, texture, root, panel, sections, characters, rect));
                failures.AddRange(ShowsTextAsWritten(sections.View, name));
                failures.AddRange(ShowsTheWholeRequest(name, folder, camera, texture, root, panel, sections));
                failures.AddRange(ShowsUntrustedTextLiterally(name, folder, camera, texture, root, panel, sections, characters));
                failures.AddRange(OffersHoldToTalk(name, folder, camera, texture, root, panel));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>The person's eyes, looking 18 degrees down, with about a Quest 3's view.</summary>
        internal static Camera MakeCamera(Transform parent, Vector3 eyes, RenderTexture texture)
        {
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, Quaternion.Euler(18f, 0f, 0f));
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = 90f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            return camera;
        }

        internal static Texture2D Render(Camera camera, RenderTexture texture)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        /// <summary>The workspace's outline on the render, inset from its rounded edge.</summary>
        internal static RectInt ScreenRect(Camera camera, Transform panel)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            {
                var world = panel.TransformPoint(new Vector3(corner.x * WorkspacePanel.Width / 2f, corner.y * WorkspacePanel.Height / 2f, 0f));
                var screen = camera.WorldToScreenPoint(world);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }
            var left = Mathf.Clamp(Mathf.CeilToInt(minX) + Inset, 0, Size);
            var bottom = Mathf.Clamp(Mathf.CeilToInt(minY) + Inset, 0, Size);
            var right = Mathf.Clamp(Mathf.FloorToInt(maxX) - Inset, 0, Size);
            var top = Mathf.Clamp(Mathf.FloorToInt(maxY) - Inset, 0, Size);
            return new RectInt(left, bottom, Mathf.Max(0, right - left), Mathf.Max(0, top - bottom));
        }

        /// <summary>How many pixels inside a rectangle differ between two renders, and the largest difference in any channel.</summary>
        internal static (int Changed, float Largest) Compare(Texture2D a, Texture2D b, RectInt rect)
        {
            var changed = 0;
            var largest = 0f;
            for (var y = rect.yMin; y < rect.yMax; y++)
            {
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    var p = a.GetPixel(x, y);
                    var q = b.GetPixel(x, y);
                    var difference = Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b)));
                    largest = Mathf.Max(largest, difference);
                    if (difference > Tolerance) changed++;
                }
            }
            return (changed, largest);
        }

        /// <summary>Whether any part of a character's body falls inside the workspace's outline on the render.</summary>
        internal static bool Covered(Camera camera, CharacterTarget target, RectInt rect)
        {
            var center = camera.WorldToScreenPoint(target.BodyPosition);
            var edge = camera.WorldToScreenPoint(target.BodyPosition + camera.transform.up * (CharacterView.BodyExtent * target.Scale));
            var radius = Vector2.Distance(center, edge);
            var nearestX = Mathf.Clamp(center.x, rect.xMin - Inset, rect.xMax + Inset);
            var nearestY = Mathf.Clamp(center.y, rect.yMin - Inset, rect.yMax + Inset);
            return center.z > 0f && Vector2.Distance(new Vector2(center.x, center.y), new Vector2(nearestX, nearestY)) < radius;
        }

        /// <summary>
        /// The workspace as a Quest 3 shows it, to judge legibility: the eyes turned to its center, at
        /// about the headset's 25 pixels per degree near the middle of its lenses.
        /// </summary>
        internal static Texture2D CloseUp(Camera camera, RenderTexture texture, Transform panel)
        {
            var rotation = camera.transform.rotation;
            var fieldOfView = camera.fieldOfView;
            camera.transform.rotation = Quaternion.LookRotation(panel.position - camera.transform.position, Vector3.up);
            camera.fieldOfView = Size / QuestPixelsPerDegree;
            var image = Render(camera, texture);
            camera.transform.rotation = rotation;
            camera.fieldOfView = fieldOfView;
            return image;
        }

        /// <summary>
        /// The directed work's sections as the bundled demonstration shows them: at its approval, and
        /// once the approved turn has ended with a failing test.
        /// </summary>
        private static IEnumerable<(string Suffix, SectionPresentation Section)> DemonstrationSections()
        {
            var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
            if (asset == null) throw new InvalidOperationException("The demonstration is missing from Resources.");
            var recording = DemonstrationRecording.Parse(asset.text);
            var beginning = recording.Nodes[0];
            var approve = beginning.BranchesAfter(beginning.Events.Count).First(branch => branch.Answer.Kind == DemonstrationAnswerKind.Approve);
            var executionId = approve.Answer.ExecutionId;
            var now = DateTimeOffset.UtcNow;
            var points = new[]
            {
                ("", 0, beginning.Events.Count),
                ("-after-approving", approve.Node, recording.Nodes[approve.Node].Events.Count),
            };
            foreach (var (suffix, node, played) in points)
            {
                var understanding = recording.UnderstandingAt(executionId, node, played)
                    ?? throw new InvalidOperationException("The demonstration holds no understanding there.");
                var evaluation = recording.EvaluationAt(executionId, node, played)
                    ?? throw new InvalidOperationException("The demonstration holds no evaluation there.");
                yield return ("understanding" + suffix, UnderstandingPresenter.Present(executionId,
                    new IntelligenceRead<UnderstandingResponse>(understanding.Response, understanding.ReadAt, recorded: true),
                    false, null, now, TimeZoneInfo.Local, WorkspaceSections.UnderstandingLines));
                yield return ("evaluation" + suffix, EvaluationPresenter.Present(executionId,
                    new IntelligenceRead<EvaluationResponse>(evaluation.Response, evaluation.ReadAt, recorded: true),
                    false, null, now, TimeZoneInfo.Local));
            }
        }

        /// <summary>
        /// The section with each character the committed static atlas lacks, such as the minus sign in
        /// "(+71 −0)", swapped for one it has, for the render only. A headset draws such a character from
        /// the dynamic fallback font asset at runtime; in the editor that would write the glyph into the
        /// committed fallback asset, which this check must never change.
        /// </summary>
        private static SectionPresentation InStaticAtlas(SectionPresentation section, TMP_FontAsset font, SortedSet<char> swapped)
        {
            string Swap(string text)
            {
                var characters = text.ToCharArray();
                for (var index = 0; index < characters.Length; index++)
                {
                    var character = characters[index];
                    if (char.IsWhiteSpace(character) || font.HasCharacter(character)) continue;
                    swapped.Add(character);
                    characters[index] = character == '−' ? '-' : '?';
                }
                return new string(characters);
            }
            return new SectionPresentation(section.Kind, Swap(section.Provenance), section.ProvenanceTone,
                section.Lines.Select(line => new SectionLine(Swap(line.Tag), Swap(line.Text), line.Tone, line.Detail)).ToList(), section.Simulated);
        }

        /// <summary>
        /// What do you need from me? under the tabs, as it shows while a request waits, over the stage:
        /// as opaque as the rest, nothing of it cut short, and then, with a section chosen, every
        /// question whole on its tab, in two lines, with Refresh beside them in the same row.
        /// </summary>
        private static IEnumerable<string> AnswersTheQuestions(string name, string folder, Camera camera, RenderTexture texture, GameObject root,
            WorkspacePanel panel, WorkspaceSections sections, List<(CharacterView View, CharacterTarget Target)> characters, RectInt rect)
        {
            var failures = new List<string>();
            sections.ShowFixedNeed(new NeedAnswer("It asks for approval to use shell:", "Run make migrate", new[]
            {
                "Approve lets it go ahead. Deny refuses; it may try another way.",
                "Either answer counts once the runtime confirms it.",
                "Approve or Deny shows the whole request before you confirm.",
            }));
            ForceMeshes(root);
            var withStage = Render(camera, texture);
            foreach (var (view, _) in characters) view.gameObject.SetActive(false);
            var alone = Render(camera, texture);
            foreach (var (view, _) in characters) view.gameObject.SetActive(true);
            File.WriteAllBytes(Path.Combine(folder, name + "-need.png"), withStage.EncodeToPNG());
            var closeUp = CloseUp(camera, texture, panel.transform);
            File.WriteAllBytes(Path.Combine(folder, name + "-need-closeup.png"), closeUp.EncodeToPNG());
            var (changed, _) = Compare(withStage, alone, rect);
            if (changed > 0) failures.Add(name + ": " + changed + " pixels of What do you need from me? change when the stage behind it is drawn.");
            UnityEngine.Object.DestroyImmediate(closeUp);
            UnityEngine.Object.DestroyImmediate(withStage);
            UnityEngine.Object.DestroyImmediate(alone);
            foreach (var label in sections.Need.Labels)
            {
                if (label.gameObject.activeInHierarchy && label.isTextTruncated) failures.Add(name + ": What do you need from me? cuts " + label.name + " short.");
                if (label.gameObject.activeInHierarchy && label.rectTransform.localPosition.y - label.textInfo.lineCount * label.fontSize * 0.115f < WorkspacePanel.DetailsBottom - 0.002f)
                {
                    failures.Add(name + ": What do you need from me? runs " + label.name + " past the bottom of the workspace.");
                }
            }

            // A section chosen while a request waits: four questions and Refresh share the row.
            sections.ShowFixed(new SectionPresentation(SectionKind.Understanding, "From the understanding source, read just now", SectionTone.Secondary,
                new[] { new SectionLine("observed", "Two files changed.", SectionTone.Normal) }, simulated: true));
            ForceMeshes(root);
            var tabs = sections.Tabs.ToList();
            if (tabs.Count != 4) failures.Add(name + ": " + tabs.Count + " questions show on the tabs while a request waits, not four.");
            var right = WorkspacePanel.DetailsLeft;
            foreach (var tab in tabs)
            {
                tab.Label.ForceMeshUpdate();
                if (tab.Label.isTextTruncated) failures.Add(name + ": the question " + tab.name + " is cut short on its tab.");
                if (tab.Label.textInfo.lineCount != 2) failures.Add(name + ": the question " + tab.name + " takes " + tab.Label.textInfo.lineCount + " lines on its tab.");
                right = Mathf.Max(right, tab.transform.localPosition.x + tab.Width / 2f);
            }
            var refresh = panel.transform.Find("Refresh");
            var refreshLeft = refresh != null && refresh.gameObject.activeSelf
                ? refresh.localPosition.x - refresh.GetComponent<PanelButton>().Width / 2f
                : WorkspacePanel.DetailsLeft + WorkspacePanel.DetailsWidth;
            if (refresh == null || !refresh.gameObject.activeSelf) failures.Add(name + ": Refresh does not show beside the questions.");
            if (right > refreshLeft - 0.004f) failures.Add(name + ": the questions run into Refresh.");
            Debug.Log("Halcyonic: workspace render " + name + ": the four questions end at " + right.ToString("0.000", CultureInfo.InvariantCulture)
                + " and Refresh starts at " + refreshLeft.ToString("0.000", CultureInfo.InvariantCulture) + " of the panel's width.");
            var tabsCloseUp = CloseUp(camera, texture, panel.transform);
            File.WriteAllBytes(Path.Combine(folder, name + "-questions-closeup.png"), tabsCloseUp.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tabsCloseUp);
            return failures;
        }

        /// <summary>
        /// Every line of a section is shown inside the details area, a claim on one row and a part's
        /// availability, coverage and freshness on at most two, never cut short.
        /// </summary>
        private static IEnumerable<string> Fits(SectionView view, string what, SectionPresentation section)
        {
            var failures = new List<string>();
            var lines = view.Labels.Where(label => label.name.StartsWith("Line ", StringComparison.Ordinal) && label.gameObject.activeSelf).ToList();
            if (lines.Count != section.Lines.Count) failures.Add(what + ": " + lines.Count + " of its " + section.Lines.Count + " lines fit.");
            var cut = 0;
            for (var index = 0; index < lines.Count; index++)
            {
                var label = lines[index];
                var rows = Mathf.RoundToInt(label.rectTransform.sizeDelta.y / SectionView.Pitch);
                if (label.textInfo.lineCount > rows) failures.Add(what + ": " + label.name + " needs more rows than it has.");
                if (label.rectTransform.localPosition.y - label.rectTransform.sizeDelta.y < WorkspacePanel.DetailsBottom - 0.001f)
                {
                    failures.Add(what + ": " + label.name + " runs past the bottom of the workspace.");
                }
                if (!label.isTextTruncated) continue;
                cut++;
                if (index < section.Lines.Count && section.Lines[index].Detail) failures.Add(what + ": a part's own statement is cut short: " + label.name);
            }
            Debug.Log("Halcyonic: workspace render " + what + ": " + lines.Count + " lines, " + cut + " ending in an ellipsis.");
            return failures;
        }

        /// <summary>
        /// Source text on a real label shows exactly as written: no markup, and backslash sequences as
        /// they are. The same text unescaped shows how TextMeshPro would have changed it. A quote cut
        /// short ends in an ellipsis.
        /// </summary>
        private static IEnumerable<string> ShowsTextAsWritten(SectionView view, string name)
        {
            var failures = new List<string>();
            // Every character is in the static atlas, so drawing it, escaped or not, adds no glyph anywhere.
            var plain = IntelligenceText.Plain("<b>b</b> <sprite=0> " + Backslash + "n " + Backslash + "u0041 " + Backslash + "U00000042 "
                + Backslash + Backslash + " z" + Char(0x202E) + Char(0x200B));
            view.Show(new SectionPresentation(SectionKind.Understanding, plain, SectionTone.Secondary,
                new[] { new SectionLine("reported", plain, SectionTone.Claim) }, simulated: true));
            foreach (var label in view.Labels)
            {
                if (label.richText) failures.Add(name + ": the section label " + label.name + " interprets markup.");
            }
            var line = view.Labels.First(label => label.name == "Line 0");
            line.ForceMeshUpdate();
            var escaped = line.textInfo.characterCount;
            if (escaped != plain.Length || line.textInfo.lineCount != 1)
            {
                failures.Add(name + ": source text of " + plain.Length + " characters shows as " + escaped + " on " + line.textInfo.lineCount + " lines.");
            }
            var shown = line.text;
            line.text = plain;
            line.ForceMeshUpdate();
            Debug.Log("Halcyonic: workspace render " + name + ": source text of " + plain.Length + " characters shows as " + escaped
                + " escaped, and as " + line.textInfo.characterCount + " on " + line.textInfo.lineCount + " lines unescaped.");
            line.text = shown;

            // A quote too long for its line must end in an ellipsis, so it never reads as all that was said.
            view.Show(new SectionPresentation(SectionKind.Understanding, "Provenance", SectionTone.Secondary,
                new[] { new SectionLine("reported", LongQuote(), SectionTone.Claim) }, simulated: true));
            line.ForceMeshUpdate();
            if (LastVisible(line) != Ellipsis) failures.Add(name + ": a quote cut short does not end in an ellipsis.");
            return failures;
        }

        /// <summary>
        /// An approval's confirmation for a very long shell command, as the director shows it: the
        /// question fits the actions row, the whole request shows under it in parts that together hold
        /// every character of it, each part shows only its own, and the confirmation appears only once
        /// the last part shows. The demonstration's short request fits one part and is confirmed at once.
        /// </summary>
        private static IEnumerable<string> ShowsTheWholeRequest(string name, string folder, Camera camera, RenderTexture texture, GameObject root,
            WorkspacePanel panel, WorkspaceSections sections)
        {
            var failures = new List<string>();
            var command = LongCommand();
            var request = WorkspaceText.Request(Approval(command));
            sections.ShowRequest(request);
            var reader = sections.Request;
            if (reader.Parts < 2) failures.Add(name + ": a request of " + request.Length + " characters takes one part, so the render checks no parts.");
            var parts = new List<string>();
            for (var part = 1; part <= reader.Parts; part++)
            {
                if (part > 1) reader.Turn(1);
                var last = part == reader.Parts;
                panel.Show(ApprovalContent(command, canConfirm: last));
                ForceMeshes(root);
                if (reader.Part != part) failures.Add(name + ": the request did not turn to part " + part + ".");
                var own = new StringBuilder();
                var others = 0;
                var info = reader.Text.textInfo;
                for (var index = 0; index < info.characterCount; index++)
                {
                    var character = info.characterInfo[index];
                    if (character.pageNumber == part - 1) own.Append(character.character);
                    else if (character.isVisible) others++;
                }
                parts.Add(own.ToString());
                if (others > 0) failures.Add(name + ": part " + part + " also shows " + others + " characters of other parts.");
                var confirm = panel.transform.Find("Confirm");
                if (confirm == null || confirm.gameObject.activeSelf != last)
                {
                    failures.Add(name + ": on part " + part + " of " + reader.Parts + " the confirmation " + (last ? "does not show." : "shows already."));
                }
                var question = Label(panel, "Controls text");
                if (question.isTextTruncated) failures.Add(name + ": the confirmation's question is cut short: " + question.text);
                failures.AddRange(ShowsLiterally(reader.Text, name + " request part " + part));
                failures.AddRange(ShowsLiterally(question, name + " request part " + part));
                failures.AddRange(ShowsLiterally(Label(panel, "Answer"), name + " request part " + part));
                if (part > 1 && !last) continue;
                var suffix = last ? "approval-last" : "approval";
                if (!last)
                {
                    var whole = Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + ".png"), whole.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(whole);
                }
                var closeUp = CloseUp(camera, texture, panel.transform);
                File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + "-closeup.png"), closeUp.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(closeUp);
            }
            if (string.Concat(parts) != LabelText.Plain(request)) failures.Add(name + ": the parts of the request, together, are not the whole request.");
            Debug.Log("Halcyonic: workspace render " + name + ": a request of " + request.Length + " characters shows in " + reader.Parts + " parts of "
                + string.Join(", ", parts.Select(part => part.Length.ToString(CultureInfo.InvariantCulture))) + " characters, the confirmation only on the last.");

            // The demonstration's request fits one part, so the approval can be confirmed as it shows.
            const string migrate = "Run make migrate";
            sections.ShowRequest(WorkspaceText.Request(Approval(migrate)));
            panel.Show(ApprovalContent(migrate, canConfirm: reader.Parts == 1));
            ForceMeshes(root);
            if (reader.Parts != 1) failures.Add(name + ": the demonstration's request takes " + reader.Parts + " parts.");
            var shortConfirm = panel.transform.Find("Confirm");
            if (shortConfirm == null || !shortConfirm.gameObject.activeSelf) failures.Add(name + ": the demonstration's approval cannot be confirmed as it shows.");
            var shortCloseUp = CloseUp(camera, texture, panel.transform);
            File.WriteAllBytes(Path.Combine(folder, name + "-approval-short-closeup.png"), shortCloseUp.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(shortCloseUp);

            sections.EndRequest();
            panel.Show(Content());
            return failures;
        }

        /// <summary>
        /// Hold to talk at the end of the action row, for work that takes instructions (ADR 0021):
        /// whole, on the row, touching no action; and left out, never squeezed in, where the actions
        /// fill the row.
        /// </summary>
        private static IEnumerable<string> OffersHoldToTalk(string name, string folder, Camera camera, RenderTexture texture, GameObject root,
            WorkspacePanel panel)
        {
            var failures = new List<string>();
            foreach (var (actions, expected) in new[]
            {
                (new[] { WorkspaceAction.Interrupt, WorkspaceAction.Instruct }, true),
                (new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt, WorkspaceAction.Instruct }, (bool?)null),
            })
            {
                var content = Content();
                content.Actions = actions;
                content.Speak = true;
                panel.Show(content);
                ForceMeshes(root);
                var what = name + " hold to talk beside " + actions.Length + " actions";
                var speak = panel.transform.Find("Hold to talk")?.GetComponent<PanelButton>();
                var shown = speak != null && speak.gameObject.activeSelf;
                if (expected == true && !shown) failures.Add(what + ": it does not show.");
                if (!shown)
                {
                    Debug.Log("Halcyonic: workspace render " + what + ": left out");
                    continue;
                }
                failures.AddRange(EntryRender.NothingOfOursCut(new Component[] { speak! }, what));
                var right = WorkspacePanel.Width / 2f - 0.035f;
                var speakRect = Extent(speak!);
                if (speakRect.xMax > right + 0.001f) failures.Add(what + ": it runs past the panel's margin.");
                for (var index = 0; index < actions.Length; index++)
                {
                    var action = panel.transform.Find("Action " + index)?.GetComponent<PanelButton>();
                    if (action != null && action.gameObject.activeSelf && EntryRender.Overlap(Extent(action), speakRect))
                    {
                        failures.Add(what + ": it touches " + WorkspaceText.Label(actions[index]) + ".");
                    }
                }
                if (expected == true)
                {
                    var closeUp = CloseUp(camera, texture, panel.transform);
                    File.WriteAllBytes(Path.Combine(folder, name + "-hold-to-talk-closeup.png"), closeUp.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(closeUp);
                }
                Debug.Log("Halcyonic: workspace render " + what + ": shown");
            }
            panel.Show(Content());
            return failures;

            static Rect Extent(PanelButton button)
            {
                var center = button.transform.localPosition;
                return new Rect(center.x - button.Width / 2f, center.y - PanelButton.Height / 2f, button.Width, PanelButton.Height);
            }
        }

        /// <summary>
        /// Hostile text on every label that shows text Halcyonic did not write, through the code that
        /// shows it: the workspace's title, execution, objective, what needs the person, question,
        /// requests, activity caption and lines, the whole request, preset buttons, a section, the peek,
        /// and a character's title and notes. Every label must interpret none of it and show what the
        /// rule made of it, all of it or cut short with an ellipsis; none may use TextMeshPro's italics
        /// or bold, and a claim leans by itself and keeps its ellipsis.
        /// </summary>
        private static IEnumerable<string> ShowsUntrustedTextLiterally(string name, string folder, Camera camera, RenderTexture texture, GameObject root,
            WorkspacePanel panel, WorkspaceSections sections, List<(CharacterView View, CharacterTarget Target)> characters)
        {
            var failures = new List<string>();
            var peek = PeekLabel.Create(root.transform);
            peek.Show(characters[3].Target, characters.ConvertAll(character => character.Target), Hostile("peek"), 1f);

            // Confirming: the question, and the whole request in place of the tabs and the details.
            panel.Show(HostileContent(ControlsMode.Confirm));
            sections.ShowRequest(Hostile("request"));
            failures.AddRange(AllShowLiterally(root, name + " confirming"));
            failures.AddRange(Carry(root, name + " confirming", "Title", "Execution", "Goal", "Answer", "Controls text", "Whole request", "Line"));
            var closeUp = CloseUp(camera, texture, panel.transform);
            File.WriteAllBytes(Path.Combine(folder, name + "-untrusted-closeup.png"), closeUp.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(closeUp);
            sections.EndRequest();

            // The requests and the activity under the tabs, where claims lean.
            panel.Show(HostileContent(ControlsMode.Actions));
            failures.AddRange(AllShowLiterally(root, name + " activity"));
            failures.AddRange(Carry(root, name + " activity", "Request 0", "Request 1", "Activity caption", "Activity 0", "Activity 1", "Activity 2"));
            for (var index = 0; index < 4; index++)
            {
                var line = Label(panel, "Activity " + index);
                var claim = index % 2 == 1;
                if (Leans(line) != claim) failures.Add(name + ": the activity line " + index + (claim ? " is a claim but does not lean." : " leans but is no claim."));
            }
            var longClaim = Label(panel, "Activity 3");
            if (!longClaim.isTextTruncated || LastVisible(longClaim) != Ellipsis) failures.Add(name + ": a leaning claim cut short does not end in an ellipsis.");
            var activityCloseUp = CloseUp(camera, texture, panel.transform);
            File.WriteAllBytes(Path.Combine(folder, name + "-untrusted-activity-closeup.png"), activityCloseUp.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(activityCloseUp);

            panel.Show(HostileContent(ControlsMode.Presets));
            failures.AddRange(AllShowLiterally(root, name + " presets"));
            failures.AddRange(Carry(root, name + " presets", "Label"));

            sections.ShowFixed(HostileSection());
            failures.AddRange(AllShowLiterally(root, name + " section"));
            failures.AddRange(Carry(root, name + " section", "Provenance", "Class 0", "Line 0"));

            sections.ShowFixedNeed(new NeedAnswer("It asks for approval to use " + Hostile("tool") + ":", Hostile("need"), new[] { Hostile("note") }));
            failures.AddRange(AllShowLiterally(root, name + " need"));
            failures.AddRange(Carry(root, name + " need", "Asks", "Request", "What answers do"));

            failures.AddRange(CharacterShowsLiterally(root, name, camera));
            UnityEngine.Object.DestroyImmediate(peek.gameObject);
            return failures;
        }

        /// <summary>Every TextMeshPro label that shows now shows its text literally.</summary>
        internal static IEnumerable<string> AllShowLiterally(GameObject root, string what)
        {
            ForceMeshes(root);
            var failures = new List<string>();
            var count = 0;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                count++;
                failures.AddRange(ShowsLiterally(label, what));
            }
            Debug.Log("Halcyonic: workspace render " + what + ": checked " + count + " labels, " + failures.Count + " failing.");
            return failures;
        }

        /// <summary>
        /// A label shows its text literally: it interprets no markup, its text went in through the one
        /// rule exactly once, and the characters it laid out are that text, all of them, or, cut short,
        /// the first of them and an ellipsis. It never uses italics or bold, for which this font has no
        /// ellipsis, so TextMeshPro would cut text short without one for good.
        /// </summary>
        internal static IEnumerable<string> ShowsLiterally(TMP_Text label, string what)
        {
            var failures = new List<string>();
            var name = what + ": " + PathOf(label.transform);
            if (label.richText) failures.Add(name + " interprets markup.");
            if (!label.parseCtrlCharacters) failures.Add(name + " parses no escapes, so its doubled backslashes show double and other backslash sequences still change.");
            if ((label.fontStyle & (FontStyles.Italic | FontStyles.Bold)) != 0) failures.Add(name + " is set in italics or bold.");
            if (label.overflowMode != TextOverflowModes.Ellipsis && label.overflowMode != TextOverflowModes.Page)
            {
                failures.Add(name + " cuts text short without an ellipsis (" + label.overflowMode + ").");
            }
            foreach (var line in label.text.Split('\n'))
            {
                if (LabelText.ForTextMeshPro(Unescaped(line)) == line) continue;
                failures.Add(name + " shows text that did not go through the rule exactly once: " + Codes(line));
                break;
            }
            label.ForceMeshUpdate();
            var expected = Unescaped(label.text);
            var laidOut = LaidOut(label);
            if (label.isTextTruncated)
            {
                var cut = LastVisibleIndex(label);
                if (cut < 0 || label.textInfo.characterInfo[cut].character != Ellipsis) failures.Add(name + " is cut short without an ellipsis: " + Codes(laidOut));
                else if (!expected.StartsWith(laidOut.Substring(0, cut), StringComparison.Ordinal)) failures.Add(name + " shows " + Codes(laidOut) + " for " + Codes(expected));
            }
            else if (laidOut != expected)
            {
                failures.Add(name + " shows " + Codes(laidOut) + " for " + Codes(expected));
            }
            return failures;
        }

        /// <summary>
        /// The labels named, among those that show now, show the hostile text, so the check reached
        /// them: at least the tag that starts it, which a narrow label cuts after.
        /// </summary>
        private static IEnumerable<string> Carry(GameObject root, string what, params string[] names)
        {
            var labels = root.GetComponentsInChildren<TMP_Text>(false);
            var failures = new List<string>();
            foreach (var name in names)
            {
                if (!labels.Any(label => label.name == name && LaidOut(label).IndexOf(Marker.Substring(0, 3), StringComparison.Ordinal) >= 0))
                {
                    failures.Add(what + ": no label " + name + " shows the hostile text, so the check did not reach it.");
                }
            }
            return failures;
        }

        /// <summary>
        /// A character's title and notes, in Unity's TextMesh, show the hostile text by the rule and
        /// draw every character of it, as wide as their advances add up to: rich text would have taken
        /// its tags as markup and drawn it narrower.
        /// </summary>
        private static IEnumerable<string> CharacterShowsLiterally(GameObject root, string name, Camera camera)
        {
            var failures = new List<string>();
            // Upright in front of the eyes, at the scale of one meter, so its bounds measure its lines.
            var view = CharacterView.Create(root.transform, "render-hostile");
            view.transform.SetPositionAndRotation(camera.transform.position + Vector3.forward * 1.2f, Quaternion.identity);
            view.Show(new CharacterPresentation(view.WorkstreamId, Hostile("title"), CharacterActivity.Failed, "Failed", AttentionLevel.ActionRequired,
                new[] { Hostile("note") }, 0, true, false, false));
            camera.Render();
            var labels = view.GetComponentsInChildren<TextMesh>(true);
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var what = name + " character: " + PathOf(label.transform);
                if (label.richText) failures.Add(what + " interprets markup.");
                foreach (var line in label.text.Split('\n'))
                {
                    if (LabelText.Plain(line) == line) continue;
                    failures.Add(what + " shows text that did not go through the rule: " + Codes(line));
                    break;
                }
            }
            var title = labels.First(label => label.name == "Title");
            if (title.text.IndexOf(Marker, StringComparison.Ordinal) < 0) failures.Add(name + ": the character's title does not show the hostile text.");
            var advances = title.text.Split('\n').Max(line => Advance(title, line));
            var drawn = title.GetComponent<MeshRenderer>().bounds.size.x;
            if (Mathf.Abs(drawn - advances) > 0.01f * advances + 0.0005f)
            {
                failures.Add(name + ": the character's title draws " + drawn.ToString("0.0000", CultureInfo.InvariantCulture) + " m wide for "
                    + advances.ToString("0.0000", CultureInfo.InvariantCulture) + " m of characters, so it took some of them as markup.");
            }
            Debug.Log("Halcyonic: workspace render " + name + ": a character's hostile title draws " + drawn.ToString("0.0000", CultureInfo.InvariantCulture)
                + " m wide, its characters' advances " + advances.ToString("0.0000", CultureInfo.InvariantCulture) + " m.");
            UnityEngine.Object.DestroyImmediate(view.gameObject);
            return failures;
        }

        /// <summary>How wide a TextMesh draws a line: its characters' advances in the font, in world units.</summary>
        private static float Advance(TextMesh label, string line)
        {
            label.font.RequestCharactersInTexture(line, label.fontSize, label.fontStyle);
            var pixels = 0;
            foreach (var character in line)
            {
                if (label.font.GetCharacterInfo(character, out var info, label.fontSize, label.fontStyle)) pixels += info.advance;
            }
            // TextMesh draws a font pixel as a tenth of a unit at a character size of one.
            return pixels * label.characterSize * 0.1f * label.transform.lossyScale.x;
        }

        /// <summary>Whether a label's letters lean, as a claim's do.</summary>
        private static bool Leans(TMP_Text label)
        {
            var info = label.textInfo;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible || character.character == ' ') continue;
                var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                var bottomLeft = vertices[character.vertexIndex];
                var topLeft = vertices[character.vertexIndex + 1];
                return topLeft.x - bottomLeft.x > 0.2f * (topLeft.y - bottomLeft.y);
            }
            return false;
        }

        /// <summary>
        /// What a command, an agent, a tool or a server could write where <paramref name="field"/>
        /// shows: markup, backslash sequences, an end of text character that would end a label there, a
        /// carriage return and a line break, a bidirectional override, a zero width space, a tag
        /// character and half a surrogate pair. Once the rule has shown it, every character is in the
        /// font's static atlas, so the check writes no glyph into the committed fallback font.
        /// </summary>
        internal static string Hostile(string field) =>
            Marker + " " + field + " <alpha=#00>hidden</alpha><sprite=0><br>" + Char(0x0003) + "after the end " + Backslash + "u0041" + Backslash + "n"
            + Char(0x202E) + "desrever" + Char(0x200B) + Char(0xE0041) + "\r\n" + (char)0xD800 + " tail";

        private static PanelContent HostileContent(ControlsMode mode) => new PanelContent
        {
            Title = Hostile("title"),
            Status = "Needs you · simulated",
            Execution = "On " + Hostile("runtime"),
            Goal = "Goal: " + Hostile("objective"),
            Answer = new[] { "Approval needed to use shell: " + Hostile("command"), "Failed: " + Hostile("failure") },
            AnswerColor = new Color(0.96f, 0.77f, 0.32f),
            Mode = mode,
            Prompt = "Send this instruction? “" + Hostile("instruction") + "”",
            ConfirmLabel = WorkspaceText.ConfirmLabel(WorkspaceAction.Instruct),
            Presets = new[] { new PresetInstruction(Hostile("preset"), "Continue."), new PresetInstruction("Continue", "Continue.") },
            Notice = "Not sent: " + Hostile("setup problem"),
            Feedback = new[] { "Refused: " + Hostile("refusal") },
            ActivityCaption = "Recent activity · history unavailable: " + Hostile("exception"),
            Activity = new[]
            {
                ("09:00:01  " + Hostile("tool"), false),
                ("09:00:02  Agent says: “" + Hostile("message") + "”", true),
                ("09:00:03  Approval requested to use shell: " + Hostile("approval"), false),
                ("09:00:04  " + LongQuote(), true),
            },
        };

        private static SectionPresentation HostileSection() => new SectionPresentation(SectionKind.Understanding, Hostile("provenance"), SectionTone.Secondary,
            new[]
            {
                new SectionLine(Hostile("tag"), Hostile("claim"), SectionTone.Claim),
                new SectionLine("", Hostile("detail"), SectionTone.Secondary, detail: true),
            }, simulated: true);

        /// <summary>A shell command as long as an approval's summary may be, whose dangerous part comes last.</summary>
        private static string LongCommand()
        {
            var builds = Enumerable.Range(1, 22)
                .Select(index => "pnpm --filter @shop/package-" + index.ToString("00", CultureInfo.InvariantCulture) + " run build -- --mode=production");
            var command = "cd /Users/dev/projects/checkout-service && git fetch --all --prune && git checkout -B release/2026-10 origin/main && "
                + string.Join(" && ", builds)
                + " && tar -czf /tmp/release.tgz dist && curl -fsS -X POST --data-binary @/tmp/release.tgz https://uploads.example.invalid/release && rm -rf ~/.ssh";
            if (command.Length > 2000) throw new InvalidOperationException("The render's command is longer than an approval's summary may be.");
            return command;
        }

        private static ApprovalView Approval(string summary) => new ApprovalView
        {
            ApprovalId = "render-approval",
            Subject = new ToolUseSubject { ToolName = "shell", Summary = summary },
            RequestedAt = "2026-09-30T09:00:00.000Z",
        };

        /// <summary>An approval of <paramref name="command"/> waiting for its confirmation, as the director shows it.</summary>
        private static PanelContent ApprovalContent(string command, bool canConfirm) => new PanelContent
        {
            Title = "Release the checkout service",
            Status = "Needs you · simulated",
            Execution = "On Simulated agent (render), simulated work · 1 turn",
            Goal = "Goal: Build every package and upload the release.",
            Answer = new[] { "Approval needed to use shell: " + command },
            AnswerColor = new Color(0.96f, 0.77f, 0.32f),
            Mode = ControlsMode.Confirm,
            Prompt = canConfirm ? WorkspaceText.ConfirmationPrompt(WorkspaceAction.Approve, null) : WorkspaceText.ReadRequestFirst,
            ConfirmLabel = WorkspaceText.ConfirmLabel(WorkspaceAction.Approve),
            CanConfirm = canConfirm,
            ActivityCaption = "Recent activity",
        };

        private static string LongQuote() =>
            "Agent says: “" + string.Join(" ", Enumerable.Repeat("The migration ran and the limit works per address.", 4)) + "”";

        /// <summary>
        /// What a TextMeshPro label with escape parsing on shows for <paramref name="text"/>, as
        /// TMP_Text.PopulateTextProcessingArray reads it in com.unity.ugui 2.0.0: two backslashes as
        /// one, a backslash with n, r, t or v as that control character, and a backslash with u and
        /// four hex digits, or U and eight, as the character they name.
        /// </summary>
        private static string Unescaped(string text)
        {
            var result = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character == '\\' && index < text.Length - 1)
                {
                    var next = text[index + 1];
                    var control = next switch
                    {
                        '\\' => '\\',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'v' => '\v',
                        _ => '\0',
                    };
                    if (control != '\0')
                    {
                        result.Append(control);
                        index++;
                        continue;
                    }
                    if (next == 'u' && text.Length > index + 5 && IsHex(text, index + 2, 4))
                    {
                        result.Append((char)int.Parse(text.Substring(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 5;
                        continue;
                    }
                    if (next == 'U' && text.Length > index + 9 && IsHex(text, index + 2, 8))
                    {
                        result.Append(char.ConvertFromUtf32(int.Parse(text.Substring(index + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                        index += 9;
                        continue;
                    }
                }
                result.Append(character);
            }
            return result.ToString();
        }

        private static bool IsHex(string text, int start, int length)
        {
            for (var index = start; index < start + length; index++)
            {
                if (!Uri.IsHexDigit(text[index])) return false;
            }
            return true;
        }

        /// <summary>The characters a label laid out, shown or not, in order.</summary>
        private static string LaidOut(TMP_Text label)
        {
            var info = label.textInfo;
            var text = new StringBuilder(info.characterCount);
            for (var index = 0; index < info.characterCount; index++) text.Append(info.characterInfo[index].character);
            return text.ToString();
        }

        private static int LastVisibleIndex(TMP_Text label)
        {
            var last = -1;
            for (var index = 0; index < label.textInfo.characterCount; index++)
            {
                if (label.textInfo.characterInfo[index].isVisible) last = index;
            }
            return last;
        }

        private static TMP_Text Label(Component parent, string name) =>
            parent.GetComponentsInChildren<TMP_Text>(true).First(label => label.name == name);

        internal static void ForceMeshes(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        }

        /// <summary>A label's place among its parents, for a failure's message.</summary>
        private static string PathOf(Transform transform)
        {
            var path = transform.name;
            for (var parent = transform.parent; parent != null && parent.parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        /// <summary>Text for a failure's message, with every character that is not printable ASCII as its code.</summary>
        private static string Codes(string text)
        {
            var result = new StringBuilder();
            foreach (var character in text.Take(100))
            {
                result.Append(character >= 0x20 && character < 0x7F ? character.ToString() : "[U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture) + "]");
            }
            return result.ToString();
        }

        private static string Char(int codePoint) => char.ConvertFromUtf32(codePoint);

        private static char LastVisible(TMP_Text label)
        {
            var last = '\0';
            for (var index = 0; index < label.textInfo.characterCount; index++)
            {
                var character = label.textInfo.characterInfo[index];
                if (character.isVisible) last = character.character;
            }
            return last;
        }

        internal static string Degrees(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>
        /// Drawing TextMeshPro text in the editor upgrades the committed font asset to the current
        /// format in memory and marks it changed, and the editor would save it on the way out: a
        /// change to Unity's resources that this check must not make.
        /// </summary>
        internal static void KeepFontAssetsAsCommitted()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (!AssetDatabase.Contains(font)) continue;
                EditorUtility.ClearDirty(font);
                if (font.material != null) EditorUtility.ClearDirty(font.material);
                if (font.atlasTextures == null) continue;
                foreach (var atlas in font.atlasTextures)
                {
                    if (atlas != null) EditorUtility.ClearDirty(atlas);
                }
            }
        }

        /// <summary>Characters with the demonstration's kind of titles, the middle one needing the person.</summary>
        internal static CharacterPresentation Presentation(string id, int slot)
        {
            var titles = new[]
            {
                "Paginate the order history endpoint",
                "Send an order confirmation email",
                "Refresh the checkout copy",
                "Add rate limiting to the sign-in endpoint",
                "Upgrade the image pipeline",
                "Tidy the release notes",
            };
            return slot switch
            {
                3 => new CharacterPresentation(id, titles[slot], CharacterActivity.WaitingForHuman, "Needs you", AttentionLevel.ActionRequired,
                    new[] { "Approval needed to use shell: Run make migrate" }, 1, true, false, false),
                1 or 4 => new CharacterPresentation(id, titles[slot], CharacterActivity.TurnFinished, "Turn finished", AttentionLevel.None,
                    Array.Empty<string>(), 0, true, false, false),
                _ => new CharacterPresentation(id, titles[slot], CharacterActivity.Working, "Working", AttentionLevel.None,
                    Array.Empty<string>(), 0, true, false, false),
            };
        }

        private static PanelContent Content() => new PanelContent
        {
            Title = "Add rate limiting to the sign-in endpoint",
            Status = "Needs you · simulated",
            Execution = "On Simulated agent (demonstration), simulated work · 1 turn",
            Goal = "Goal: Limit sign-in attempts per address and per account.",
            Answer = new[] { "Approval needed to use shell: Run make migrate" },
            AnswerColor = new Color(0.96f, 0.77f, 0.32f),
            Actions = new[] { WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt },
            ActivityCaption = "Recent activity",
            Activity = new[]
            {
                ("09:00:01  Turn started", false),
                ("09:00:03  Agent says: “Adding a limiter in front of the sign-in handler.”", true),
                ("09:00:06  edit: src/auth/rate-limit.ts", false),
                ("09:00:09  shell: Run make migrate", false),
            },
        };
    }
}
