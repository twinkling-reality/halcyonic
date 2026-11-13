#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The redesign's drawn parts, each alone (DESIGN's BUILD-SPEC-v4): rendered from the eyes with a Quest 3S's field at both
    /// text sizes, as the eye buffer and laid over a white wall, a dark room and the bright synthetic room, and checked in the
    /// spirit of the spec's section 6. First the agent's words band (1.6): every word whole in the one type size, at most
    /// three lines a part with the product's way on, nothing on it but the words given, its ground within 6 of 255 of the page
    /// over white and over dark, its words at 4.5 to 1, inside the field where the spec stands it, and its motion at fixed
    /// times. Then the working field (2.3), in KitRender.Parts.Field.cs, and the
    /// agent's cursor and its label, in KitRender.Parts.Cursor.cs, and DESIGN's glyph set, in KitRender.Parts.Glyphs.cs. Every word shown is synthetic. In the editor: Halcyonic > Render the Redesign's Parts. In batch mode, with
    /// graphics: -executeMethod Halcyonic.XR.Workspace.Editor.KitRender.PartsCheck; it exits 1 when a check fails, and 2 on a
    /// clean pass narrowed by HALCYONIC_KIT_ONLY. Images go to apps/xr/Builds/PartsRenders, which git ignores; they carry no
    /// caption, so a part can be held beside the page's own state.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] BandStates = { "40-band-question", "40-band-result", "40-band-long", "40-band-parts", "40-band-parts-next", "40-band-arriving" };

        /// <summary>The design's own sample words, synthetic: a question's prompt, a short report, the page's longer one, and one long enough for several parts.</summary>
        private const string BandQuestion = "Which colour scheme should the game use?";
        private const string BandResult = "I added the golden balloon and removed the old backup file.";
        private const string BandLong = "I added a golden balloon worth five points that pops with a gold burst. I also removed backup/old-game.js, since nothing used it, and moved the score code into its own file.";
        private const string BandParts = BandLong + " Next I can add a sound for the gold burst, or make the golden balloon rarer as the score climbs. I can also draw the score larger, so it reads from across the room. Tell me which you would like first.";

        [MenuItem("Halcyonic/Render the Redesign's Parts")]
        public static void PartsMenu() => RunParts(exitWhenDone: false);

        /// <summary>The parts' one entry point in batch mode: renders and checks every part's states, then exits 1 on any failure.</summary>
        public static void PartsCheck() => RunParts(exitWhenDone: true);

        private static void RunParts(bool exitWhenDone)
        {
            var failures = new List<string>();
            var previousField = ViewField.Current;
            var previousStill = GlazeMotion.Still;
            var only = Environment.GetEnvironmentVariable("HALCYONIC_KIT_ONLY");
            var narrowed = !string.IsNullOrWhiteSpace(only);
            try
            {
                if (!narrowed)
                {
                    failures.AddRange(BandMotion());
                    failures.AddRange(FieldMotion());
                    failures.AddRange(CursorMotion());
                    failures.AddRange(UnderlayHolds());
                    failures.AddRange(BandKeepsWords());
                }
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    var size = variant.Length == 0 ? "Standard" : variant;
                    var found = new List<string>();
                    var shown = 0;
                    foreach (var state in BandStates.Concat(FieldStates).Concat(CursorStates).Concat(GlyphStates).Concat(AgentStates).Concat(RowsStates).Concat(TagStates).Concat(CardStates).Concat(RecapStates).Concat(StartFromStates).Concat(PairingStates).Concat(AskColumnStates))
                    {
                        if (narrowed && !state.Contains(only!, StringComparison.OrdinalIgnoreCase)) continue;
                        shown++;
                        try { found.AddRange(RenderPart(state, size)); }
                        catch (Exception error) { found.Add(state + " threw " + error); }
                    }
                    if (shown == 0) found.Add("HALCYONIC_KIT_ONLY=" + only + " matched no state");
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("the parts render threw " + error); }
            finally
            {
                ViewField.Current = previousField;
                GlazeMotion.Still = previousStill;
                KeepFontsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: parts: " + failure);
            if (failures.Count == 0 && narrowed)
                Debug.LogWarning("Halcyonic: parts: narrowed by HALCYONIC_KIT_ONLY=" + only + " (iteration only; exits 2 so a narrowed run is never a full gate pass).");
            else if (failures.Count == 0) Debug.Log("Halcyonic: parts: every check passed.");
            if (exitWhenDone) EditorApplication.Exit(failures.Count > 0 ? 1 : narrowed ? 2 : 0);
        }

        /// <summary>
        /// The words' underlay presets (BUILD-SPEC-v4 1.2, 1.4), checked without a render: each is its face's own material with
        /// TextMeshPro's underlay on, black at 28 percent (35 on an action's words), no offset, on the face's own atlas; the band's
        /// and the cursor's words carry it; and it changes no line's width.
        /// </summary>
        private static List<string> UnderlayHolds()
        {
            var failures = new List<string>();
            void Require(bool condition, string why) { if (!condition) failures.Add("the words' underlay: " + why); }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Underlay");
            try
            {
                foreach (var (name, role, alpha) in new[] { ("KitReading Underlay", KitRole.Body, 0.28f), ("KitReading Underlay Action", KitRole.Body, 0.35f), ("KitMono Underlay", KitRole.Mono, 0.28f) })
                {
                    var preset = Resources.Load<Material>("HalcyonicUI/" + name);
                    var face = KitType.Face(role);
                    if (preset == null || face == null)
                    {
                        failures.Add("the words' underlay: " + name + " or its face is missing");
                        continue;
                    }
                    Require(preset.shader == face.material.shader && preset.mainTexture == face.material.mainTexture, name + " is not its face's own material on its atlas");
                    Require(preset.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay), name + " has no underlay on");
                    var colour = preset.GetColor(ShaderUtilities.ID_UnderlayColor);
                    Require(colour.r == 0f && colour.g == 0f && colour.b == 0f && Mathf.Abs(colour.a - alpha) < 1e-3f, name + "'s underlay is " + colour + ", not black at " + alpha);
                    Require(preset.GetFloat(ShaderUtilities.ID_UnderlayOffsetX) == 0f && preset.GetFloat(ShaderUtilities.ID_UnderlayOffsetY) == 0f, name + "'s underlay is offset");
                    Require(preset.GetFloat(ShaderUtilities.ID_UnderlaySoftness) == 1f, name + "'s underlay is not as soft as its atlas allows");
                    Require(preset.GetFloat(ShaderUtilities.ID_GradientScale) == face.material.GetFloat(ShaderUtilities.ID_GradientScale), name + " has another gradient scale than its face");
                }
                var band = RoomBand.Create(root.transform);
                band.Show(BandQuestion);
                band.Settle();
                Require(band.Label.fontSharedMaterial != null && band.Label.fontSharedMaterial.name == "KitReading Underlay", "the band's words do not carry it");
                var cursor = RoomCursor.Create(root.transform);
                cursor.Show("a", "Running Tests");
                Require(cursor.Label.fontSharedMaterial != null && cursor.Label.fontSharedMaterial.name == "KitReading Underlay", "the cursor's words do not carry it");
                var plain = RoomType.Create(root.transform, "Plain", Color.white, 0);
                var under = RoomType.Create(root.transform, "Under", Color.white, 0);
                Require(RoomBand.Underlay(under) && RoomBand.Underlay(under, action: true), "a label in the reading face takes no underlay");
                foreach (var each in new[] { plain, under })
                {
                    each.textWrappingMode = TextWrappingModes.NoWrap;
                    GlazeText.SetLiteral(each, BandResult);
                    each.rectTransform.sizeDelta = new Vector2(10f, 1f);
                    each.ForceMeshUpdate(true);
                }
                Require(Mathf.Abs(RoomCursor.WordsWidth(plain, 0f) - RoomCursor.WordsWidth(under, -1f)) < 1e-5f, "the underlay changes a line's width");
            }
            catch (Exception error) { failures.Add("the words' underlay threw " + error); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            if (failures.Count == 0) Debug.Log("Halcyonic: parts: the words' underlay presets hold.");
            return failures;
        }

        /// <summary>
        /// The band never breaks a word at its hyphen (F6b; JUDGE's frame read "sign-" / "ins?"): for texts that push a
        /// hyphenated word across a line's end one letter at a time, a plain label of the band's own face, size and measure
        /// does break it at the hyphen (so the case is real), and the band gives the same words with every line ending at a
        /// space or the text's own break, the word whole on the next line.
        /// </summary>
        private static List<string> BandKeepsWords()
        {
            var failures = new List<string>();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Hyphens");
            try
            {
                var band = RoomBand.Create(root.transform);
                var arose = 0;
                var most = 0;
                for (var pad = 1; pad <= 70; pad++)
                {
                    var said = "Should " + new string('o', pad) + " the game keep the players' saved sign-ins? Yes - keep them, or re-enter each time.";
                    band.Show(said);
                    band.Settle();
                    var label = band.Label;
                    var laid = RoomWords.Laid(said);
                    // The plain label: the band's own, copied, given the words as they came.
                    var plain = UnityEngine.Object.Instantiate(label, label.transform.parent);
                    plain.text = laid;
                    plain.ForceMeshUpdate(true);
                    bool AtHyphen(TMP_TextInfo info)
                    {
                        for (var index = 0; index + 1 < info.lineCount; index++)
                        {
                            var line = info.lineInfo[index];
                            if (line.characterCount > 0 && line.lastCharacterIndex == line.lastVisibleCharacterIndex && info.characterInfo[line.lastVisibleCharacterIndex].character == '-'
                                && line.lastCharacterIndex + 1 < info.characterCount && !char.IsWhiteSpace(info.characterInfo[line.lastCharacterIndex + 1].character)) return true;
                        }
                        return false;
                    }
                    var broke = AtHyphen(plain.textInfo);
                    UnityEngine.Object.DestroyImmediate(plain.gameObject);
                    if (broke) arose++;
                    label.ForceMeshUpdate(true);
                    if (AtHyphen(label.textInfo)) failures.Add("the band breaks a word at its hyphen with " + pad + " letters before it: \"" + label.text.Replace("\n", " / ") + "\"");
                    if (label.text.Replace('\n', ' ') != laid.Replace('\n', ' ') || band.Text != laid) failures.Add("the band's words changed with " + pad + " letters before them");
                    if (label.text.Contains("sign-\n") || label.text.Contains("re-\n")) failures.Add("the band's text ends a line inside a word with " + pad + " letters before it");
                    most = Mathf.Max(most, label.textInfo.lineCount);
                }
                if (arose < 2) failures.Add("the plain label broke at a hyphen in only " + arose + " of 70 texts, so the check proves nothing");
                if (failures.Count == 0) Debug.Log("Halcyonic: parts: the band keeps a hyphenated word whole: a plain label broke one in " + arose + " of 70 texts, the band in none (at most " + most + " lines).");
            }
            catch (Exception error) { failures.Add("the band's hyphens threw " + error); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        /// <summary>What one part's render needs: where the head turns, what failed while it was built, and what to read in the pixels.</summary>
        private sealed class PartShot
        {
            public float Elevation;

            /// <summary>The dark ground the render is laid over: the fixture's night room, or the design's own dark room where a part is fit to it.</summary>
            public Color Dark = DarkRoom;
            public List<string> Failures = new List<string>();

            /// <summary>Reads the render: the camera, the eye buffer, and it laid over white, the dark room and the bright room.</summary>
            public Action<Camera, Color32[], Color32[], Color32[], Color32[], List<string>>? Read;
        }

        private static List<string> RenderPart(string state, string size)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ViewField.Current = Quest3S;
            // The parts move by their own clocks, which each shot fixes.
            GlazeMotion.Still = false;
            var name = state + " (" + size + ")";
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "PartsRenders", size));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = new GameObject("Part: " + state);
            var fixture = new GameObject("Fixture camera");
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var eyes = new Vector3(0f, EyeHeight, 0f);
            try
            {
                var shot = FieldStates.Contains(state) ? FieldShot(state, root.transform, eyes, name)
                    : CursorStates.Contains(state) ? CursorShot(state, root.transform, eyes, name)
                    : GlyphStates.Contains(state) ? GlyphShot(state, root.transform, eyes, name)
                    : AgentStates.Contains(state) ? AgentShot(state, root.transform, eyes, name)
                    : RowsStates.Contains(state) ? RowsShot(state, root.transform, eyes, name)
                    : TagStates.Contains(state) ? TagShot(state, root.transform, eyes, name)
                    : CardStates.Contains(state) ? CardShot(state, root.transform, eyes, name)
                    : RecapStates.Contains(state) ? RecapShot(state, root.transform, eyes, name)
                    : StartFromStates.Contains(state) ? StartFromShot(state, root.transform, eyes, name)
                    : PairingStates.Contains(state) ? PairingShot(state, root.transform, eyes, name)
                    : AskColumnStates.Contains(state) ? AskColumnShot(state, root.transform, eyes, name)
                    : BandShot(state, root.transform, eyes, name);
                failures.AddRange(shot.Failures.Select(failure => name + ": " + failure));
                SetLayer(root, KitLayer);
                // Every label built again, as a font atlas change builds them: the part must stand through it.
                ForceMeshes(root);
                failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
                var camera = MakeCamera(fixture.transform, eyes, target, 0f, shot.Elevation);
                SetLayer(fixture, KitLayer);
                var image = Render(camera, target);
                Texture2D? white = null, dark = null, bright = null;
                try
                {
                    white = Composite(image, null, Color.white);
                    dark = Composite(image, null, shot.Dark);
                    bright = Composite(image, BrightRoom(Width, Height, camera), Color.white);
                    if (shot.Read != null)
                    {
                        var read = new List<string>();
                        shot.Read(camera, image.GetPixels32(), white.GetPixels32(), dark.GetPixels32(), bright.GetPixels32(), read);
                        failures.AddRange(read.Select(failure => name + ": " + failure));
                    }
                    File.WriteAllBytes(Path.Combine(folder, state + "-rgba.png"), image.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, state + "-white.png"), white.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, state + "-dark.png"), dark.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, state + "-bright.png"), bright.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(image);
                    if (white != null) UnityEngine.Object.DestroyImmediate(white);
                    if (dark != null) UnityEngine.Object.DestroyImmediate(dark);
                    if (bright != null) UnityEngine.Object.DestroyImmediate(bright);
                }
                Debug.Log("Halcyonic: parts: " + name + " rendered " + Width + " by " + Height + ", head turned " + Degrees(shot.Elevation) + " up.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(fixture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
            return failures;
        }

        /// <summary>
        /// The agent's words band alone, square to the eyes, at a fixed clock: a question's prompt, a short report, the page's
        /// longer report, words of several parts on their first part and on their second, and a band 240 ms into its arrival.
        /// </summary>
        private static PartShot BandShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = -26f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var said = state switch
            {
                "40-band-question" or "40-band-arriving" => BandQuestion,
                "40-band-result" => BandResult,
                "40-band-long" => BandLong,
                _ => BandParts,
            };
            var arriving = state == "40-band-arriving";
            const float ArrivingSeconds = 0.24f;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var band = RoomBand.Create(plane);
            var clock = 10f;
            band.Clock = () => clock;
            band.Show(said);
            if (state == "40-band-parts-next")
            {
                clock += 5f;
                band.Draw();
                Require(band.Next(), "words of several parts have no next part");
            }
            clock += arriving ? ArrivingSeconds : 5f;
            band.Draw();

            var unit = RoomType.Units(1f);
            var scale = GlazeText.Scale;
            const float Tolerance = 1e-4f;
            var label = band.Label;
            var info = label.textInfo;
            var laid = RoomWords.Laid(said);
            if (band.Words is not RoomWords words)
            {
                shot.Failures.Add("the band holds no words");
                return shot;
            }

            // The band holds the words given, and nothing else: one label, one ground, the one type size, white, never cut.
            Require(band.Text == laid && label.text.Replace('\n', ' ') == laid.Replace('\n', ' '), "the band's words are not the words given");
            var labels = band.GetComponentsInChildren<TMP_Text>(false);
            Require(labels.Length == 1 && labels[0] == label, "the band holds " + labels.Length + " labels, not its words alone");
            Require(band.GetComponentsInChildren<Renderer>(false).Length == 2, "the band draws more than its ground and its words");
            Require(band.Ground.sharedMaterial != null && band.Ground.sharedMaterial.shader.name == "Halcyonic/Room Band", "the ground is not drawn in the band's fade");
            Require(Mathf.Approximately(label.fontSize, GlazeTokens.FontSize(RoomType.Scaled(RoomGeometry.TypeDp))), "the words are not in the one type size");
            Require(KitType.IsRoomFace(label.font) && Mathf.Approximately(label.characterSpacing, RoomWords.Tracking), "the words are not in the room's face at its tracking");
            Require(label.color == Color.white && !label.richText && label.overflowMode == TextOverflowModes.Overflow, "the words are not plain white, read without markup, never cut");
            Require(Mathf.Abs(KitType.LinePitch(label) - RoomGeometry.LineDp * unit * scale) < 0.02f * unit, "the lines are not 28 dp apart");

            // Paging: the lines as laid, three a part, the product's way on, every line in exactly one part.
            Require(info.characterCount == laid.Length, "the label laid " + info.characterCount + " of the words' " + laid.Length + " characters");
            Require(info.lineCount == words.Lines, "the paging counts " + words.Lines + " lines where " + info.lineCount + " are laid");
            Require(words.Count >= 1 && words.Count <= RoomWords.LinesAPart, "a part holds " + words.Count + " lines");
            switch (state)
            {
                case "40-band-parts":
                    Require(words.Parts >= 2 && words.Part == 0 && words.Next == EntryText.NextPartLabel && words.NextCount == "2 of " + words.Parts.ToString(CultureInfo.InvariantCulture),
                        "the first of " + words.Parts + " parts offers \"" + words.Next + "\" and \"" + words.NextCount + "\"");
                    break;
                case "40-band-parts-next":
                    Require(words.Part == 1 && words.First == RoomWords.LinesAPart, "Next Part turned to part " + (words.Part + 1));
                    Require((words.Next, words.NextCount) == (words.Parts > 2 ? (EntryText.NextPartLabel, "3 of " + words.Parts.ToString(CultureInfo.InvariantCulture)) : ((string?)null, (string?)null)),
                        "the second of " + words.Parts + " parts offers \"" + words.Next + "\" and \"" + words.NextCount + "\"");
                    break;
                case "40-band-question":
                case "40-band-arriving":
                    Require(words.Lines == 1 && words.Parts == 1 && words.Next == null, "the question takes " + words.Lines + " lines in " + words.Parts + " parts");
                    break;
                default:
                    Require((words.Next, words.NextCount) == (words.Parts > 1 ? (EntryText.NextPartLabel, "2 of " + words.Parts.ToString(CultureInfo.InvariantCulture)) : ((string?)null, (string?)null)),
                        "the way on does not follow the parts");
                    break;
            }

            // Size: 880 dp wide whatever the text size, 26 dp above and below its lines.
            var height = RoomWords.HeightDp(words.Count, scale) * unit;
            Require(Mathf.Abs(band.Size.x - RoomWords.WidthDp * unit) < Tolerance && Mathf.Abs(band.Size.y - height) < Tolerance,
                "the band is " + (band.Size.x / unit).ToString("0.0") + " by " + (band.Size.y / unit).ToString("0.0") + " dp, not 880 by " + (height / unit).ToString("0.0"));
            Require(Mathf.Abs(band.Height - height) < Tolerance, "the ground is drawn " + (band.Height / unit).ToString("0.0") + " dp tall");

            // Motion at the fixed time: the ground and each line where the formulas put them.
            var groundExpected = arriving ? RoomMotion.BandArrives(ArrivingSeconds) : 1f;
            Require(Mathf.Abs(band.GroundShown - groundExpected) < Tolerance, "the ground is " + band.GroundShown + " into view, not " + groundExpected);
            Require(band.Moving == arriving, arriving ? "the band has stopped mid-arrival" : "the band still moves five seconds on");
            for (var line = 0; line < words.Count; line++)
            {
                var expected = arriving ? RoomMotion.LineArrives(ArrivingSeconds - RoomMotion.BandLinesAfterSeconds, line) : RoomMove.Settled;
                var move = band.LineMove(line);
                Require(Mathf.Abs(move.Shown - expected.Shown) < Tolerance && Mathf.Abs(move.LowerDp - expected.LowerDp) < Tolerance,
                    "line " + line + " is " + move.Shown + " into view and " + move.LowerDp + " dp low, not " + expected.Shown + " and " + expected.LowerDp);
            }

            // Stood for the render: square to the eyes, its middle on the line of sight.
            band.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            Place(plane, eyes, 0f, shot.Elevation);

            // Where the spec stands it, on the level plane with its top edge at pitch -24.5, it is inside the field.
            var level = new GameObject("Level plane").transform;
            level.SetParent(root, false);
            Place(level, eyes, 0f, 0f);
            var top = RoomWords.TopPitch * RoomGeometry.DpPerDegree * unit;
            var halfWidth = RoomWords.WidthDp * unit / 2f;
            var corners = new[] { new Vector3(-halfWidth, top, 0f), new Vector3(halfWidth, top, 0f), new Vector3(-halfWidth, top - height, 0f), new Vector3(halfWidth, top - height, 0f) }
                .Select(corner => level.TransformPoint(corner)).ToList();
            shot.Failures.AddRange(GlazeChecks.InsideField(name, corners, eyes, level.position, 0f, Quest3S));

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                // Every word whole after every label was built again: the part's letters inside the measure and the band,
                // as far into view as their line, and every other part's letters folded away.
                var inside = 0;
                var folded = 0;
                var labelAt = label.transform.localPosition;
                var slack = 4f * unit * scale;
                for (var index = 0; index < info.characterCount; index++)
                {
                    var character = info.characterInfo[index];
                    if (!character.isVisible) continue;
                    var mesh = info.meshInfo[character.materialReferenceIndex];
                    var a = mesh.vertices[character.vertexIndex];
                    var c = mesh.vertices[character.vertexIndex + 2];
                    var line = character.lineNumber - words.First;
                    if (line < 0 || line >= words.Count)
                    {
                        if (a != c) failures.Add("a letter of another part is drawn: \"" + character.character + "\" on line " + character.lineNumber);
                        folded++;
                        continue;
                    }
                    inside++;
                    var left = labelAt.x + Mathf.Min(a.x, c.x);
                    var right = labelAt.x + Mathf.Max(a.x, c.x);
                    var low = labelAt.y + Mathf.Min(a.y, c.y);
                    var high = labelAt.y + Mathf.Max(a.y, c.y);
                    if (left < -RoomWords.MeasureDp * unit / 2f - slack || right > RoomWords.MeasureDp * unit / 2f + slack)
                        failures.Add("\"" + character.character + "\" on line " + character.lineNumber + " stands outside the 600 dp measure");
                    if (high > Tolerance || low < -height - Tolerance) failures.Add("\"" + character.character + "\" on line " + character.lineNumber + " stands outside the band");
                    var alpha = mesh.colors32[character.vertexIndex].a;
                    var expected = Mathf.RoundToInt(255f * band.LineMove(line).Shown);
                    if (Mathf.Abs(alpha - expected) > 1) failures.Add("\"" + character.character + "\" is drawn at alpha " + alpha + ", not " + expected);
                }
                if (inside == 0) failures.Add("no letter of the part is drawn");

                // The ground against the page: its alpha times the fade there, mixed in sRGB as the page mixes, within 6 of 255.
                Color32 At(Color32[] pixels, Vector2 local, out bool seen)
                {
                    var screen = camera.WorldToScreenPoint(band.transform.TransformPoint(new Vector3(local.x, local.y, 0f)));
                    var x = Mathf.RoundToInt(screen.x);
                    var y = Mathf.RoundToInt(screen.y);
                    seen = screen.z > 0f && x >= 0 && y >= 0 && x < Width && y < Height;
                    return seen ? pixels[y * Width + x] : default;
                }
                var ground = (Color32)Halcyonic.XR.UI.RoomSurface.Hex(RoomWords.GroundColour);
                var night = (Color32)DarkRoom;
                var edge = RoomWords.EdgeFadeDp * scale;
                // Clear of every letter: between the top fade and the first line, left of the measure at mid height, and the fades' middles.
                var points = new (string What, Vector2 Dp)[]
                {
                    ("the middle, above the first line", new Vector2(RoomWords.WidthDp / 2f, (edge + RoomWords.PadDp * scale) / 2f)),
                    ("beside the measure", new Vector2(RoomWords.SideFadeDp + 10f, height / unit / 2f)),
                    ("half way up the left fade", new Vector2(RoomWords.SideFadeDp / 2f, height / unit / 2f)),
                    ("half way up the right fade", new Vector2(RoomWords.WidthDp - RoomWords.SideFadeDp / 2f, height / unit / 2f)),
                    ("half way down the top fade", new Vector2(RoomWords.WidthDp / 2f, edge / 2f)),
                    ("just inside the left edge", new Vector2(1f, height / unit / 2f)),
                };
                var worst = 0;
                foreach (var (what, dp) in points)
                {
                    var page = RoomWords.GroundAlpha * RoomWords.Ground(dp.x, dp.y, RoomWords.WidthDp, height / unit, edge) * band.GroundShown;
                    var local = new Vector2((dp.x - RoomWords.WidthDp / 2f) * unit, -dp.y * unit);
                    foreach (var (over, pixels, under) in new[] { ("white", white, new Color32(255, 255, 255, 255)), ("the dark room", dark, night) })
                    {
                        var drawn = At(pixels, local, out var seen);
                        if (!seen) { failures.Add(what + " is out of the frame"); continue; }
                        var r = Mathf.RoundToInt(page * ground.r + (1f - page) * under.r);
                        var g = Mathf.RoundToInt(page * ground.g + (1f - page) * under.g);
                        var b = Mathf.RoundToInt(page * ground.b + (1f - page) * under.b);
                        var off = Mathf.Max(Mathf.Abs(drawn.r - r), Mathf.Max(Mathf.Abs(drawn.g - g), Mathf.Abs(drawn.b - b)));
                        worst = Mathf.Max(worst, off);
                        if (off > 6) failures.Add("the ground " + what + " over " + over + " is (" + drawn.r + ", " + drawn.g + ", " + drawn.b + ") where the page is (" + r + ", " + g + ", " + b + ")");
                    }
                }
                var middle = At(eye, new Vector2(0f, -(edge + RoomWords.PadDp * scale) / 2f * unit), out _);
                var linear = RoomWords.LinearAlpha(RoomWords.GroundAlpha * band.GroundShown, (ground.r + ground.g + ground.b) / 3f / 255f);
                if (Mathf.Abs(middle.a - 255f * linear) > 3f) failures.Add("the eye buffer holds alpha " + middle.a + " in the band's middle, not " + (255f * linear).ToString("0"));

                // The words against their ground, as rendered: 4.5 to 1 over white and over the bright room, once they are whole.
                var weakest = double.MaxValue;
                if (!arriving)
                {
                    var half = RoomWords.MeasureDp * unit / 2f;
                    var first = -RoomWords.PadDp * scale * unit;
                    var a = camera.WorldToScreenPoint(band.transform.TransformPoint(new Vector3(-half, first, 0f)));
                    var c = camera.WorldToScreenPoint(band.transform.TransformPoint(new Vector3(half, first - words.Count * RoomGeometry.LineDp * scale * unit, 0f)));
                    var rect = new RectInt(Mathf.FloorToInt(Mathf.Min(a.x, c.x)), Mathf.FloorToInt(Mathf.Min(a.y, c.y)), 0, 0);
                    rect.xMax = Mathf.CeilToInt(Mathf.Max(a.x, c.x));
                    rect.yMax = Mathf.CeilToInt(Mathf.Max(a.y, c.y));
                    if (rect.xMin < 0 || rect.yMin < 0 || rect.xMax > Width || rect.yMax > Height) failures.Add("the words leave the frame");
                    else
                    {
                        foreach (var (over, pixels) in new[] { ("white", white), ("the bright room", bright), ("the dark room", dark) })
                        {
                            var under = Luminance(MostCommon(pixels, rect));
                            var strongest = 1.0;
                            for (var y = rect.yMin; y < rect.yMax; y++)
                            for (var x = rect.xMin; x < rect.xMax; x++)
                            {
                                var l = Luminance(pixels[y * Width + x]);
                                strongest = Math.Max(strongest, (Math.Max(l, under) + 0.05) / (Math.Min(l, under) + 0.05));
                            }
                            weakest = Math.Min(weakest, strongest);
                            if (strongest < 4.5) failures.Add("the words reach " + strongest.ToString("0.00", CultureInfo.InvariantCulture) + " to 1 over " + over);
                        }
                    }
                }
                Debug.Log("Halcyonic: parts: " + name + ": " + words.Lines + " lines in " + words.Parts + " parts, part " + (words.Part + 1) + " drawn with " + inside + " letters (" + folded
                    + " folded away); the band " + (band.Size.x / unit).ToString("0.0", CultureInfo.InvariantCulture) + " by " + (band.Size.y / unit).ToString("0.0", CultureInfo.InvariantCulture)
                    + " dp; the ground at most " + worst + " of 255 from the page over white and dark; the eye buffer's middle alpha " + middle.a
                    + (arriving ? "; mid-arrival" : "; the weakest words " + weakest.ToString("0.00", CultureInfo.InvariantCulture) + " to 1") + "; way on: " + (words.Next ?? "none") + ".");
            };
            return shot;
        }

        /// <summary>
        /// The band's own motion, checked without a render at fixed clock times: it arrives by the formulas, the same words
        /// again move nothing, newer words send the old lines out before the height eases and the new lines come in, Next
        /// Part walks every part once, it leaves by fading, and kept still each of these is there at once.
        /// </summary>
        private static List<string> BandMotion()
        {
            var failures = new List<string>();
            void Require(bool condition, string why) { if (!condition) failures.Add("the band's motion: " + why); }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Band motion");
            var stillWas = GlazeMotion.Still;
            try
            {
                GlazeMotion.Still = false;
                var band = RoomBand.Create(root.transform);
                var clock = 1f;
                band.Clock = () => clock;
                var unit = RoomType.Units(1f);
                const float Close = 1e-4f;
                int Labels() => band.GetComponentsInChildren<TMP_Text>(false).Length;
                void After(float seconds)
                {
                    clock += seconds;
                    band.Draw();
                }

                Require(!band.gameObject.activeSelf && band.Words == null && band.Size.y == 0f, "a new band is not hidden");
                band.Show(BandQuestion);
                Require(band.gameObject.activeSelf && band.Moving && band.GroundShown < Close && band.LineMove(0).Shown < Close, "it does not start from nothing");
                var one = RoomWords.HeightDp(1, GlazeText.Scale) * unit;
                Require(Mathf.Abs(band.Height - one) < Close, "it does not arrive at its own height");
                After(0.12f);
                Require(Mathf.Abs(band.GroundShown - RoomMotion.BandArrives(0.12f)) < Close && band.LineMove(0).Shown < Close, "120 ms in, the ground is " + band.GroundShown + " and the line " + band.LineMove(0).Shown);
                After(0.12f);
                Require(Mathf.Abs(band.LineMove(0).Shown - 0.875f) < Close && Mathf.Abs(band.LineMove(0).LowerDp - 1f) < Close, "240 ms in, the line is " + band.LineMove(0).Shown + " into view, " + band.LineMove(0).LowerDp + " dp low");
                After(0.14f);
                Require(Mathf.Abs(band.GroundShown - 1f) < Close && band.LineMove(0).Shown == 1f && !band.Moving, "380 ms in, a band of one line has not arrived");

                band.Show(BandQuestion);
                Require(!band.Moving && Labels() == 1, "the same words again moved it");

                // Newer words: known at once, the old lines out first, then the height, then the new lines.
                band.Show(BandParts);
                var parts = band.Words;
                Require(band.Moving && band.Text == RoomWords.Laid(BandParts) && parts != null && parts.Parts >= 2 && parts.Next != null, "newer words are not paged at once");
                var three = RoomWords.HeightDp(RoomWords.LinesAPart, GlazeText.Scale) * unit;
                Require(Labels() == 2 && Mathf.Abs(band.Height - one) < Close && band.LineMove(0).Shown < Close, "the old line does not leave before the new ones come");
                After(0.06f);
                Require(Labels() == 2 && Mathf.Abs(band.Height - one) < Close, "60 ms in, the old line is gone or the height moved");
                After(0.07f);
                Require(Labels() == 1, "130 ms in, the old line still stands");
                After(0.11f);
                Require(Mathf.Abs(band.Height - RoomMotion.BandHeight(0.24f, one, three)) < Close && band.LineMove(0).Shown < Close, "240 ms in, the height is not easing with the new lines still out");
                After(0.24f);
                Require(Mathf.Abs(band.Height - three) < Close && Mathf.Abs(band.LineMove(0).Shown - 0.875f) < Close && band.LineMove(2).Shown < Close,
                    "480 ms in, the first new line is " + band.LineMove(0).Shown + " and the third " + band.LineMove(2).Shown);
                After(1f);
                Require(!band.Moving && band.LineMove(2).Shown == 1f && Mathf.Abs(band.Size.y - three) < Close, "the newer words did not settle");

                // Next Part walks every part once and stops on the last.
                var seen = parts?.Count ?? 0;
                var turns = 0;
                while (band.Next())
                {
                    Require(band.Moving, "Next Part did not move");
                    After(1f);
                    seen += band.Words?.Count ?? 0;
                    if (++turns > 50) break;
                }
                Require(parts != null && turns == parts.Parts - 1 && seen == parts.Lines && band.Words?.Next == null, "Next Part walked " + turns + " turns and " + seen + " lines of " + parts?.Lines);

                // It leaves by fading, its words with it, and holds none after.
                band.Leave();
                Require(band.Moving && band.Words == null, "leaving, it still offers its words");
                After(0.18f);
                Require(Mathf.Abs(band.GroundShown - RoomMotion.BandLeaves(0.18f)) < Close && Mathf.Abs(band.LineMove(0).Shown - band.GroundShown) < Close, "180 ms into leaving, the ground is " + band.GroundShown);
                After(0.2f);
                Require(!band.gameObject.activeSelf && !band.Moving, "380 ms on, it has not left");

                // Kept still, nothing travels: it is there, replaced or gone at once.
                GlazeMotion.Still = true;
                band.Show(BandResult);
                Require(band.gameObject.activeSelf && !band.Moving && band.GroundShown == 1f && band.LineMove(0).Shown == 1f && band.LineMove(0).LowerDp == 0f, "kept still, it did not arrive at once");
                band.Show(BandQuestion);
                Require(!band.Moving && Labels() == 1 && band.Text == RoomWords.Laid(BandQuestion), "kept still, newer words did not replace the old at once");
                band.Show("  \n ");
                Require(!band.gameObject.activeSelf, "no words did not make it leave");
            }
            catch (Exception error) { failures.Add("the band's motion threw " + error); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                GlazeMotion.Still = stillWas;
            }
            if (failures.Count == 0) Debug.Log("Halcyonic: parts: the band's motion holds at every fixed time.");
            return failures;
        }
    }
}
