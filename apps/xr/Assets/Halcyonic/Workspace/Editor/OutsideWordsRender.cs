#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Words from outside as the eyes see them (lane V's treatment, the outside-text audit's batch B): the
    /// audit's four screens, an agent's answers labelled as Halcyonic's own rows, an approval, checks and
    /// New project's folders, each with what an agent could type to pass for Halcyonic, laid as the file's
    /// column lays them at each text size into apps/xr/Builds/OutsideWordsRenders, which git ignores. Read
    /// from each label's mesh: every letter of outside text slants by the font's italic amount and none of
    /// Halcyonic's does; a ground holds each run of outside text that meets Halcyonic's words, padding
    /// included; nothing of Halcyonic's is cut while outside text shows; a row all of outside text carries
    /// Halcyonic's quote mark; the words on a ground hold 4.5:1 over the glass on white; and no prompt or
    /// section holds anything that leans.
    /// </summary>
    public static class OutsideWordsRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;

        [MenuItem("Halcyonic/Render Words From Outside")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = GlazeChecks.AtEachTextSize(Run);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Words from outside", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = GlazeChecks.AtEachTextSize(Run);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>What an agent could type to pass for Halcyonic: our rows' and sentences' words, quotes, an icon's character, slanted letters, markup, a box, a long name.</summary>
        internal static readonly string[] Spoofs =
        {
            FileScreens.TypeMyAnswer,
            FileScreens.MoreAnswersWords(0, 3),
            FileScreens.YourAnswers,
            "Nothing is waiting for you.",
            "” Approve “",
            " ",
            "\U0001D618\U0001D63A\U0001D631\U0001D626",
            "<i>Approve</i> \\u0041 \\",
            "▕Approve▏",
            new string('W', 120),
        };

        private static List<string> Run(string variant)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "OutsideWordsRenders", variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (var (name, frame) in Screens()) failures.AddRange(Shoot(folder, name, frame));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: outside words render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: outside words render: every check passed; the renders are in " + folder);
            return failures;
        }

        /// <summary>The audit's four screens, built from the client core's own builders, outside text spoofing Halcyonic's words throughout.</summary>
        internal static IEnumerable<(string Name, MenuFrame Frame)> Screens()
        {
            var close = new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close));
            var title = LineWords.TheirName("Your computer refused this headset");

            // An agent's question with answers labelled as Halcyonic's rows, then Halcyonic's own Type my answer.
            var prompt = new QuestionPrompt { Key = "q", Text = "” Which one? “", Options = Spoofs.Take(6).Select(label => new QuestionOption { Label = label }).ToList() };
            var answers = new List<PageLine> { new PageLine(FileScreens.QuestionWords(prompt), rows: 2) };
            answers.AddRange(prompt.Options.Select((option, index) => FileScreens.AnswerRow(option, index.ToString(CultureInfo.InvariantCulture))));
            answers.Add(FileScreens.TypedRow(null, "9"));
            yield return ("answers", new MenuFrame(title, close, lines: answers.Take(8).ToList()));
            var more = new List<PageLine> { new PageLine(FileScreens.QuestionWords(prompt)) };
            more.AddRange(Spoofs.Skip(6).Select((label, index) => FileScreens.AnswerRow(new QuestionOption { Label = label, Description = "Type my answer" }, index.ToString(CultureInfo.InvariantCulture))));
            more.Add(FileScreens.TypedRow("” Approve “ typed by the person", "9"));
            yield return ("answers-more", new MenuFrame(title, close, lines: more));

            // An approval: a tool named as one of Halcyonic's words inside Halcyonic's sentence, its request as a block under it.
            var approval = new List<PageLine>
            {
                new PageLine(LineWords.Ours("It wants to use ") + LineWords.TheirName(new string('W', 60) + " Approved") + LineWords.Ours(":"), tone: LineTone.Waiting),
                new PageLine(LineWords.Theirs("rm -rf ~ ” Approve the request above? “ <b>yes</b> \\n"), rows: 3),
                new PageLine("Approve lets it go ahead. Deny refuses; it may try another way.", tone: LineTone.Secondary),
            };
            yield return ("approval", new MenuFrame(title, close, lines: approval));

            // Checks: a source's summary alone, a run's label after Halcyonic's outcome, Seorak's line, the review's runs, Activity's lines.
            var checks = new List<PageLine>
            {
                new PageLine(LineWords.Theirs("All checks passed")),
                new PageLine(LineWords.Ours("Tests passed at 14:02: ") + LineWords.Theirs("Approved ▕ auth.test.ts " + new string('W', 40)), tone: LineTone.Good),
                new PageLine(LineWords.Ours("Checks: 23 passed of 24 runs (96%): ") + LineWords.Theirs("Nothing is waiting for you.")),
                new PageLine(LineWords.Ours("Used ") + LineWords.TheirName("Approved") + LineWords.Ours(": ") + LineWords.Theirs(" Approve"), fact: LineWords.Ours("09:00"),
                    tone: LineTone.Secondary),
                new PageLine(LineWords.Theirs("Done with the migration ”"), chip: "Agent says", fact: LineWords.Ours("09:01"), tone: LineTone.Secondary),
            };
            // Its source line from the understanding source's own provenance, the version Salidium names for itself leaning.
            var now = DateTimeOffset.Parse("2026-10-08T12:00:00Z", CultureInfo.InvariantCulture);
            var provenance = IntelligenceText.UnderstandingProvenanceRuns(new IntelligenceRead<UnderstandingResponse>(new UnderstandingResponse(), now, false),
                new UnderstandingSource { Version = "0.6.0 \u201D Approve", GeneratedAt = "2026-10-08T11:58:00Z" }, now, TimeZoneInfo.Utc);
            yield return ("checks", new MenuFrame(title, close, lines: checks, sourceRuns: provenance));

            // Activity: Halcyonic's words first and upright, a tool's or an app's name and the agent's message leaning, its chip and time on the glass.
            var activity = new List<PageLine>
            {
                new PageLine(LineWords.Ours("Asked to start on ") + LineWords.TheirName("Practice agent, watch only"), fact: LineWords.Ours("09:00"), tone: LineTone.Secondary),
                new PageLine(LineWords.Ours("Used ") + LineWords.TheirName("Approved") + LineWords.Ours(": ") + LineWords.Theirs("make migrate"), fact: LineWords.Ours("09:01"),
                    tone: LineTone.Secondary),
                new PageLine(LineWords.Ours("Succeeded: ") + LineWords.TheirName("bash"), fact: LineWords.Ours("09:02"), tone: LineTone.Secondary),
                new PageLine(LineWords.TheirName("Done with the migration. Nothing is waiting for you."), chip: "Agent says", fact: LineWords.Ours("09:03"), tone: LineTone.Secondary),
            };
            yield return ("activity", new MenuFrame(title, close, lines: activity));

            // Your answers: each prompt's answer whole, a header and labels from outside leaning beside Halcyonic's quote mark, a colon no ground.
            var asked = new QuestionView
            {
                QuestionId = "render", Answerable = true, AskedAt = "2026-10-08T12:00:00Z",
                Prompts = new List<QuestionPrompt>
                {
                    new QuestionPrompt { Key = "a", Header = "Lockout", Text = "How long?", Options = new List<QuestionOption> { new QuestionOption { Label = "15 minutes" } } },
                    new QuestionPrompt { Key = "b", Header = "Your answers", Text = "Which?", FreeText = true, Options = new List<QuestionOption> { new QuestionOption { Label = "Type my answer" } } },
                    new QuestionPrompt { Key = "c", Header = null, Text = "And?", Options = new List<QuestionOption> { new QuestionOption { Label = "Yes" } } },
                },
            };
            var draft = new QuestionDraft("render", asked);
            draft.Choose(0, "15 minutes");
            draft.Choose(1, "Type my answer");
            yield return ("your-answers", new MenuFrame(title, close, lines: Enumerable.Range(0, 3).Select(prompt => FileScreens.ReviewRow(draft, prompt)).ToList()));

            // New project's folders: folders named as Halcyonic's options, under a place named as one of its sentences.
            var listing = new LocationsResponse
            {
                Roots = new List<LocationRoot>
                {
                    new LocationRoot
                    {
                        Path = "/p", Name = "a place your computer no longer lists", Status = LocationRootStatus.Available,
                        Folders = new List<LocationFolder>
                        {
                            new LocationFolder { Name = "New folder in a place your computer no longer lists", Path = "/p/a" },
                            new LocationFolder { Name = "Directly in a place your computer no longer lists", Path = "/p/b" },
                            new LocationFolder { Name = new string('f', 90), Path = "/p/c" },
                        },
                    },
                },
            };
            var folders = ProjectFolder.Options(listing).Select((option, index) => new PageLine(option.LabelRuns,
                icon: option.Kind == FolderOptionKind.Folder ? GlazeIcon.Folder : (GlazeIcon?)null, fact: option.DetailRuns,
                action: NewProjectScreens.ChooseFolder, key: index.ToString(CultureInfo.InvariantCulture), choice: true)).ToList();
            yield return ("folders", new MenuFrame(EntryText.CreateTitleRuns("Your answers"), close, lines: folders));
        }

        private static IEnumerable<string> Shoot(string folder, string name, MenuFrame frame)
        {
            var failures = new List<string>();
            var root = new GameObject("Outside words render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var file = MenuFrameView.Create(root.transform, "File column");
                var side = MenuFrameView.Create(root.transform, "Side panel");
                FileRender.Lay(frame, file, side, eyes);
                var image = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                failures.AddRange(Checks(file, name));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>The checks on one column as laid, from its labels' meshes.</summary>
        internal static IEnumerable<string> Checks(MenuFrameView view, string name)
        {
            var failures = new List<string>();
            var glassOnWhite = GlazeTokens.ColorOf(Glaze.Menu.GlassOverWhite);
            var ground = GlazeChecks.Over(Color.white, Glaze.Menu.GroundOpacity, glassOnWhite);
            var leaningRuns = 0;
            PageLine? before = null;
            foreach (var (model, words, icon, fact, chip, grounds) in view.ShownLines)
            {
                // A block's next line stands on its first line's ground, under one quote mark.
                var joined = before != null && Block(before) && Block(model);
                before = model;
                var what = name + ": \"" + model.Words + "\"";
                failures.AddRange(Slants(words, model.Runs, what));
                leaningRuns += model.Runs.Runs.Count(run => run.Theirs);
                // Halcyonic's words stand whole on the label while outside text shows.
                var shown = words.text;
                var at = 0;
                foreach (var run in model.Runs.Runs.Where(run => !run.Theirs))
                {
                    var ours = run.Text.Replace("\\", "\\\\");
                    var found = shown.IndexOf(ours, at, StringComparison.Ordinal);
                    if (found < 0 && model.FromRow == null) failures.Add(what + ": Halcyonic's \"" + run.Text + "\" doesn't show whole beside the outside text.");
                    else if (found >= 0) at = found + ours.Length;
                }
                if (model.Runs.OursAfterTheirs && words.isTextTruncated) failures.Add(what + ": cut at its end, where Halcyonic's words stand.");
                // A row all of outside text carries Halcyonic's quote mark where no icon of Halcyonic's stands.
                if (!joined && model.Runs.AllTheirs && model.Icon == GlazeIcon.Quote && (icon == null || icon.text != GlazeIconGlyphs.Of(GlazeIcon.Quote)))
                {
                    failures.Add(what + ": all outside text, yet Halcyonic's quote mark doesn't show beside it.");
                }
                if (model.Runs.AllTheirs && model.Icon == null) failures.Add(what + ": all outside text with no mark of Halcyonic's in its icon column.");
                // Each run that meets Halcyonic's words stands on a ground holding it and its padding.
                if (!model.Runs.AllTheirs && model.Runs.AnyTheirs)
                {
                    failures.AddRange(Grounded(words, GlazeRuns.Of(words).Theirs, grounds, what));
                }
                // Outside text on a ground, a token's or a block's, holds 4.5:1 there over the glass on white, in the colour it is drawn in.
                var onGround = model.Runs.AnyTheirs && !(model.Action != null && model.Runs.AllTheirs);
                foreach (var (label, grounded) in new[] { ((TMP_Text)words, onGround), ((TMP_Text?)fact, fact != null && GlazeRuns.Of(fact).Theirs.Count > 0
                    && !(model.Action != null && model.Runs.AllTheirs && model.FactRuns?.AllTheirs == true)) })
                {
                    if (label == null || !grounded) continue;
                    var drawn = (Color)(GlazeRuns.Of(label).Grounded ?? (Color32)label.color);
                    if (Contrast(drawn, ground) < 4.5f) failures.Add(what + ": its outside words hold " + Contrast(drawn, ground).ToString("0.0", CultureInfo.InvariantCulture) + ":1 on their ground over white, under 4.5.");
                    if (GlazeRuns.Of(label).Grounded == null) failures.Add(what + ": its outside words stand on a ground in the line's own colour, not the primary text's.");
                }
                if (Block(model) && !joined && grounds.Count == 0) failures.Add(what + ": a block of outside text with no ground under it.");
                // A ground holds only theirs: a chip or a time beside the words stands on the glass.
                foreach (var beside in new[] { fact, chip })
                {
                    if (beside == null) continue;
                    var at = beside.transform.localPosition;
                    var bounds = beside.textBounds;
                    var area = Rect.MinMaxRect(at.x + bounds.min.x, at.y + bounds.min.y, at.x + bounds.max.x, at.y + bounds.max.y);
                    foreach (var ground in grounds)
                    {
                        if (!ground.gameObject.activeSelf || model.FactRuns?.AnyTheirs == true && beside == fact) continue;
                        var centre = ground.transform.localPosition;
                        var placed = new Rect(centre.x - ground.Size.x / 2f, centre.y - ground.Size.y / 2f, ground.Size.x, ground.Size.y);
                        if (placed.Overlaps(area)) failures.Add(what + ": a ground runs under \"" + beside.text + "\", which is Halcyonic's.");
                    }
                }
                if (fact != null && model.FactRuns != null) failures.AddRange(Slants(fact, model.FactRuns, what + "'s fact"));
            }
            if (leaningRuns == 0) failures.Add(name + ": no outside text shown, so nothing here is checked.");
            // A ground lifts the glass by at most a third of what a chosen row's fill does, so it never reads as chosen (lane V),
            // both blended over the glass on black in linear space, as Unity blends.
            var glass = GlazeTokens.ColorOf(Glaze.Panel.Over(GlazeColor.Hex(0x000000), Glaze.Menu.GlassOpacity));
            var groundLift = Contrast(GlazeChecks.Over(Color.white, Glaze.Menu.GroundOpacity, glass), glass) - 1f;
            var chosenLift = Contrast(GlazeChecks.Over(Color.white, Glaze.Menu.LitFillOpacity, glass), glass) - 1f;
            if (groundLift > chosenLift / 3f) failures.Add(name + ": a ground lifts the glass by " + (groundLift / chosenLift).ToString("0.00", CultureInfo.InvariantCulture) + " of what a chosen row's fill does, over a third.");
            // Pills, prompts and sections hold Halcyonic's words alone: nothing on them leans.
            foreach (var ours in view.OursOnly)
            {
                if (ours.GetComponent<GlazeRuns>() is GlazeRuns runs && runs.Theirs.Count > 0) failures.Add(name + ": \"" + ours.text + "\" leans, on a prompt or a section.");
            }
            var subject = view.SubjectShown.Title;
            if (view.Frame?.SubjectRuns is LineWords subjectRuns) failures.AddRange(Slants(subject, subjectRuns, name + "'s subject"));
            if (view.SourceShown is TMP_Text source && view.Frame?.SourceRuns is LineWords sourceRuns) failures.AddRange(Slants(source, sourceRuns, name + "'s source line"));
            return failures;
        }

        private static float Contrast(Color text, Color under) => GlazeChecks.Contrast(text, under);

        /// <summary>A line all of outside text that only says something, which stands on a ground of its own.</summary>
        private static bool Block(PageLine model) => model.Action == null && model.Runs.AllTheirs;

        /// <summary>
        /// Every visible letter of outside text in <paramref name="runs"/> on <paramref name="label"/> slants
        /// by its font's italic amount, read from the label's mesh, and none of Halcyonic's does.
        /// </summary>
        private static IEnumerable<string> Slants(TMP_Text label, LineWords runs, string what)
        {
            var failures = new List<string>();
            label.ForceMeshUpdate(true);
            var info = label.textInfo;
            var theirs = GlazeRuns.Of(label).Theirs;
            var expected = runs.Runs.Any(run => run.Theirs);
            if (expected && theirs.Count == 0) failures.Add(what + ": holds outside text, yet nothing on it leans.");
            if (!expected && theirs.Count > 0) failures.Add(what + ": Halcyonic's words alone, yet something on it leans.");
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                var bottomLeft = vertices[character.vertexIndex];
                var topLeft = vertices[character.vertexIndex + 1];
                var height = topLeft.y - bottomLeft.y;
                if (height <= 0f) continue;
                var slant = (topLeft.x - bottomLeft.x) / height;
                var leans = GlazeText.InRun(info, index, theirs);
                var italic = character.fontAsset.italicStyle * 0.01f;
                if (leans && Mathf.Abs(slant - italic) > 0.02f)
                {
                    failures.Add(what + ": its outside letter '" + character.character + "' slants " + slant.ToString("0.00", CultureInfo.InvariantCulture) + ", not the italic's " + italic.ToString("0.00", CultureInfo.InvariantCulture) + ".");
                    break;
                }
                if (!leans && Mathf.Abs(slant) > 0.01f)
                {
                    failures.Add(what + ": Halcyonic's letter '" + character.character + "' slants.");
                    break;
                }
            }
            return failures;
        }

        /// <summary>
        /// Each run's box on each row it reaches stands inside a ground drawn under it, with the ground's padding
        /// or halfway to Halcyonic's letter beside it, and no letter of Halcyonic's on the label touches a ground.
        /// </summary>
        private static IEnumerable<string> Grounded(TMP_Text label, IReadOnlyList<(int Start, int End)> theirs, IReadOnlyList<Surface> grounds, string what)
        {
            var failures = new List<string>();
            var at = label.transform.localPosition;
            var info = label.textInfo;
            Rect Placed(Surface ground)
            {
                var centre = ground.transform.localPosition;
                return new Rect(centre.x - ground.Size.x / 2f, centre.y - ground.Size.y / 2f, ground.Size.x, ground.Size.y);
            }
            var expected = GlazeText.RunGrounds(label, theirs, GlazeRuns.GroundPadding);
            foreach (var box in GlazeText.RunBoxes(label, theirs))
            {
                var needed = Rect.MinMaxRect(at.x + box.xMin + 1e-4f, at.y + box.yMin + 1e-4f, at.x + box.xMax - 1e-4f, at.y + box.yMax - 1e-4f);
                if (!grounds.Any(ground => Placed(ground).Contains(needed.min) && Placed(ground).Contains(needed.max))) failures.Add(what + ": a run of outside text stands off its ground.");
            }
            foreach (var ground in expected)
            {
                // Padding each side: the full padding, or halfway to Halcyonic's letter beside it.
                var placed = Rect.MinMaxRect(at.x + ground.xMin, at.y + ground.yMin, at.x + ground.xMax, at.y + ground.yMax);
                if (!grounds.Any(drawn => Mathf.Abs(Placed(drawn).xMin - placed.xMin) < 1e-3f && Mathf.Abs(Placed(drawn).xMax - placed.xMax) < 1e-3f))
                {
                    failures.Add(what + ": a ground lacks its padding.");
                }
            }
            var boxes = GlazeText.RunBoxes(label, theirs);
            // A word space at the label's own size.
            var word = label.GetPreferredValues("a a").x - label.GetPreferredValues("aa").x;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible || GlazeText.InRun(info, index, theirs)) continue;
                var glyph = Rect.MinMaxRect(at.x + character.bottomLeft.x, at.y + character.bottomLeft.y, at.x + character.topRight.x, at.y + character.topRight.y);
                if (grounds.Any(ground => ground.gameObject.activeSelf && Placed(ground).Overlaps(glyph)))
                {
                    failures.Add(what + ": Halcyonic's letter '" + character.character + "' touches a ground under outside text.");
                    break;
                }
                // A ground sits in the sentence as a word does: a word space between Halcyonic's letter and its edge,
                // none needed before punctuation (lane V).
                foreach (var box in boxes)
                {
                    var placedBox = Rect.MinMaxRect(at.x + box.xMin, at.y + box.yMin, at.x + box.xMax, at.y + box.yMax);
                    if (glyph.center.y < placedBox.yMin || glyph.center.y > placedBox.yMax) continue;
                    var before = glyph.xMax <= placedBox.xMin;
                    var after = glyph.xMin >= placedBox.xMax;
                    if (!before && !after) continue;
                    var nearest = grounds.Where(ground => ground.gameObject.activeSelf && Placed(ground).Overlaps(placedBox))
                        .Select(ground => before ? Placed(ground).xMin - glyph.xMax : glyph.xMin - Placed(ground).xMax).DefaultIfEmpty(float.MaxValue).Min();
                    if (nearest > 3f * word) continue;
                    var needed = after && char.IsPunctuation(character.character) ? 0f : 0.9f * word;
                    if (nearest < needed - 1e-4f)
                    {
                        failures.Add(what + ": a ground stands closer than a word space to Halcyonic's letter '" + character.character + "'.");
                        index = info.characterCount;
                        break;
                    }
                }
            }
            return failures;
        }
    }
}
