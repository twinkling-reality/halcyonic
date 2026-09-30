#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// on one line, and that source text shows exactly as written on a real TextMeshPro label. The
    /// Meta XR Simulator renders nothing on the development Mac, so this is the check short of a
    /// headset. It saves each render as a PNG in apps/xr/Builds/WorkspaceRenders, which git ignores.
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
                failures.AddRange(ShowsTextAsWritten(sections.View, name));
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
        private static Camera MakeCamera(Transform parent, Vector3 eyes, RenderTexture texture)
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

        private static Texture2D Render(Camera camera, RenderTexture texture)
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
        private static RectInt ScreenRect(Camera camera, Transform panel)
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
        private static (int Changed, float Largest) Compare(Texture2D a, Texture2D b, RectInt rect)
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
        private static bool Covered(Camera camera, CharacterTarget target, RectInt rect)
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
        private static Texture2D CloseUp(Camera camera, RenderTexture texture, Transform panel)
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
        /// they are. The same text unescaped shows how TextMeshPro would have changed it.
        /// </summary>
        private static IEnumerable<string> ShowsTextAsWritten(SectionView view, string name)
        {
            var failures = new List<string>();
            // Every character is in the static atlas, so drawing it, escaped or not, adds no glyph anywhere.
            var plain = IntelligenceText.Plain("<b>b</b> <sprite=0> \\n \\u0041 \\U00000042 \\\\ z‮​");
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
            line.text = plain;
            line.ForceMeshUpdate();
            Debug.Log("Halcyonic: workspace render " + name + ": source text of " + plain.Length + " characters shows as " + escaped
                + " escaped, and as " + line.textInfo.characterCount + " on " + line.textInfo.lineCount + " lines unescaped.");

            // A quote too long for its line must end in an ellipsis, so it never reads as all that was said.
            var quote = "Agent says: “" + string.Join(" ", Enumerable.Repeat("The migration ran and the limit works per address.", 4)) + "”";
            view.Show(new SectionPresentation(SectionKind.Understanding, "Provenance", SectionTone.Secondary,
                new[] { new SectionLine("reported", quote, SectionTone.Claim) }, simulated: true));
            line.ForceMeshUpdate();
            if (LastVisible(line) != '…') failures.Add(name + ": a quote cut short does not end in an ellipsis.");
            line.fontStyle = FontStyles.Italic;
            line.ForceMeshUpdate();
            Debug.Log("Halcyonic: workspace render " + name + ": the same quote in italics ends in U+"
                + ((int)LastVisible(line)).ToString("X4", CultureInfo.InvariantCulture) + (LastVisible(line) == '…' ? ", an ellipsis." : ", not an ellipsis."));
            line.fontStyle = FontStyles.Normal;
            return failures;
        }

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

        private static string Degrees(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>
        /// Drawing TextMeshPro text in the editor upgrades the committed font asset to the current
        /// format in memory and marks it changed, and the editor would save it on the way out: a
        /// change to Unity's resources that this check must not make.
        /// </summary>
        private static void KeepFontAssetsAsCommitted()
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
        private static CharacterPresentation Presentation(string id, int slot)
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
            Objective = "Objective: Limit sign-in attempts per address and per account.",
            Attention = new[] { "Approval needed to use shell: Run make migrate" },
            AttentionColor = new Color(0.96f, 0.77f, 0.32f),
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
