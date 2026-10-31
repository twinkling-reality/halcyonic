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
    /// Renders a task's file as the director shows it (ADR 0026): <see cref="FileScreens"/>' frames laid
    /// on one plane facing the eyes as the menu's plane lays a file and its side panel alone, at each text size, into
    /// apps/xr/Builds/FileRenders, which git ignores. The agent's question with four answers of two
    /// rows each, one of them chosen and cut with its side panel, the same under a question of two rows
    /// that takes its own page, an approval's request in parts, the
    /// work's activity, and what changed with a line's side panel open. Each must lay its lines within
    /// the rows a page holds as the view wraps them, beside its reason and its source line, and raise
    /// the view's Drawn once it settles; a chosen cut answer must bring its side panel.
    /// </summary>
    public static class FileRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;
        private const string Time = "2026-10-01T09:00:00.000Z";

        [MenuItem("Halcyonic/Render a Task's File")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = GlazeChecks.AtEachTextSize(Run);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("File render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        public static void Check()
        {
            var failures = GlazeChecks.AtEachTextSize(Run);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static TextSize TextSizeNow => GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard;

        public static List<string> PalettePass(GlazePalette palette) => GlazeChecks.AtEachTextSize(variant => Run(variant, palette.Key));

        private static List<string> Run(string variant) => Run(variant, "");

        private static List<string> Run(string variant, string paletteFolder)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "FileRenders", paletteFolder, variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(Question(folder));
                failures.AddRange(LongQuestion(folder));
                failures.AddRange(Approval(folder));
                failures.AddRange(Activity(folder));
                failures.AddRange(Changes(folder));
                failures.AddRange(SectionMeasurements());
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: file render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: file render: every check passed; the renders are in " + folder);
            return failures;
        }

        /// <summary>One shot: a file's frame on its plane, seen from the eyes, saved, and held to the page's rows.</summary>
        private static IEnumerable<string> Shoot(string folder, string name, MenuFrame frame, bool sideExpected = false, FileScreen? reading = null, DateTimeOffset? at = null)
        {
            var failures = new List<string>();
            var root = new GameObject("File render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var file = MenuFrameView.Create(root.transform, "File column");
                var side = MenuFrameView.Create(root.transform, "Side panel");
                var drawn = 0;
                var sideDrawn = 0;
                file.Drawn += _ =>
                {
                    drawn++;
                    reading?.Question.Drawn(at ?? DateTimeOffset.UtcNow);
                };
                side.Drawn += _ =>
                {
                    sideDrawn++;
                    reading?.Question.SideDrawn(at ?? DateTimeOffset.UtcNow);
                };
                Lay(frame, file, side, eyes);
                var image = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);

                if (drawn != 1) failures.Add(name + ": the file's column raised Drawn " + drawn.ToString(CultureInfo.InvariantCulture) + " times as it settled, not once.");
                if (sideExpected && frame.Side == null) failures.Add(name + ": a chosen cut answer brought no side panel.");
                if (frame.Side != null && sideDrawn != 1) failures.Add(name + ": the side panel raised Drawn " + sideDrawn.ToString(CultureInfo.InvariantCulture) + " times, not once.");
                // The page's lines as the view wraps them, priced by height against what a lone file's page holds.
                var budget = HeightBudget.Of(TextSizeNow, MenuFrameView.TitleRows(frame.Subject, Glaze.Menu.FileColumnDegrees), focused: frame.Focused);
                var height = Height(frame, budget);
                Debug.Log("Halcyonic: file render " + name + ": its lines stand " + Degrees(height) + " of the " + Degrees(budget.Room) + " a page holds beside its source line.");
                if (height > budget.Room + 1e-4f) failures.Add(name + ": its lines stand " + Degrees(height) + ", past the " + Degrees(budget.Room) + " a page holds.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>
        /// The file's column, and its side panel beside it where it has one, as one composition on a plane
        /// facing <paramref name="eyes"/>, its top <see cref="MenuPage.TopDegrees"/> below eye level, each
        /// column settled as the plane has it, which raises its view's Drawn.
        /// </summary>
        internal static void Lay(MenuFrame frame, MenuFrameView file, MenuFrameView side, Vector3 eyes)
        {
            var subject = MenuFrameView.SubjectHeight(frame.Subject, Glaze.Menu.FileColumnDegrees, pillRoom: true);
            if (frame.Side != null) subject = Mathf.Max(subject, MenuFrameView.SubjectHeight(frame.Side.Subject, Glaze.Menu.SideColumnDegrees, pillRoom: true));
            file.Show(frame, Glaze.Menu.FileColumnDegrees, subject, pillRoom: true);
            var columns = new List<MenuFrameView> { file };
            if (frame.Side != null)
            {
                side.Show(frame.Side, Glaze.Menu.SideColumnDegrees, subject, pillRoom: true);
                columns.Add(side);
            }
            else side.Hide();
            var composition = new PlaneComposition(columns.Select(view => new PlaneColumn(view.Width, view.Heights.ToArray())).ToList(), GlazeText.Scale);
            var half = Mathf.Atan(composition.Height / 2f) * Mathf.Rad2Deg;
            var direction = new PanelDirection(0f, -MenuPage.TopDegrees - half, true, false);
            for (var column = 0; column < columns.Count; column++)
            {
                var placed = composition.Parts.Where(part => part.Column == column).OrderBy(part => part.Index).ToList();
                for (var index = 0; index < placed.Count; index++)
                {
                    PlaneLayout.Lay(columns[column].Parts[index], eyes, direction, placed[index], composition.Zoom);
                    if (index == placed.Count - 1) columns[column].Settle(placed[index], composition.Zoom);
                }
            }
        }

        private static string Degrees(float units) => GlazeChecks.Degrees(2f * Mathf.Atan(units / 2f) * Mathf.Rad2Deg);

        /// <summary>A page's height as its budget prices it, each line in the rows the view wraps it to, at most its own: words, targets, their gaps, and the reason.</summary>
        private static float Height(MenuFrame frame, PageBudget budget)
        {
            var total = 0f;
            PageLine? before = null;
            foreach (var line in frame.Lines)
            {
                // A line beside the one before it shares that one's row and target.
                if (before?.BesideNext == true) continue;
                var rows = Mathf.Min(line.Rows, MenuFrameView.RowsOf(line, Glaze.Menu.FileColumnDegrees));
                var target = line.Action != null;
                if (before != null)
                {
                    // As the view lays them: 12 mm between two targets, a grid step between any other two.
                    total += target && before.Action != null ? budget.TargetGap : budget.LineGap;
                }
                total += target ? budget.Target(rows) : budget.Words(rows);
                before = line;
            }
            return total + (frame.Reason != null ? budget.Reason : 0f);
        }

        private static IReadOnlyList<PromptMeasure> Measure(QuestionDraft draft) => draft.Prompts.Select((prompt, index) =>
        {
            // Measured as the file column measures them, from the lines the page draws.
            return new PromptMeasure(
                MenuFrameView.RowsOf(new PageLine(FileScreens.QuestionWords(prompt)), Glaze.Menu.FileColumnDegrees),
                Enumerable.Range(0, prompt.Options.Count).Select(option => MenuFrameView.RowsOf(FileScreens.OptionRow(prompt, option), Glaze.Menu.FileColumnDegrees)).ToList(),
                prompt.Options.Select(option => MenuFrameView.RowsOf(FileScreens.SideAnswerLine(option, null), Glaze.Menu.SideColumnDegrees)).ToList(),
                MenuFrameView.FitsHalf(FileScreens.InputNavigation(true), Glaze.Menu.FileColumnDegrees)
                    && MenuFrameView.FitsHalf(FileScreens.ChoicePager(), Glaze.Menu.FileColumnDegrees)
                    && MenuFrameView.FitsHalf(FileScreens.ChoicePager(first: true), Glaze.Menu.FileColumnDegrees),
                MenuFrameView.FitsHalf(FileScreens.InputNavigation(true, edit: true), Glaze.Menu.FileColumnDegrees)
                    && MenuFrameView.FitsHalf(FileScreens.ChoicePager(), Glaze.Menu.FileColumnDegrees)
                    && MenuFrameView.FitsHalf(FileScreens.ChoicePager(first: true), Glaze.Menu.FileColumnDegrees),
                MenuFrameView.RowsOf(new PageLine(FileScreens.QuestionCaption(index, draft.Prompts.Count, prompt)), Glaze.Menu.FileColumnDegrees),
                MenuFrameView.RowsOf(new PageLine(FileScreens.QuestionCaption(index, draft.Prompts.Count, prompt, Math.Max(0, prompt.Options.Count - 1), Math.Max(1, prompt.Options.Count))), Glaze.Menu.FileColumnDegrees));
        }).ToList();

        /// <summary>The agent's question with four answers of two rows each, then one chosen whose words run past two rows.</summary>
        private static IEnumerable<string> Question(string folder)
        {
            var failures = new List<string>();
            var question = new QuestionView
            {
                QuestionId = "render-question",
                Answerable = true,
                AskedAt = Time,
                Prompts = new List<QuestionPrompt>
                {
                    new QuestionPrompt
                    {
                        Key = "q0", Header = "Lockout", Text = "How long should a sign-in lockout last?",
                        Options = LockoutAnswers(),
                        Multiple = false, FreeText = true, Secret = false,
                    },
                },
            };
            var work = WorkspaceRender.Work.Asking(question);
            var workspace = work.Present();
            var draft = new QuestionDraft("render-execution", question);
            var screen = new FileScreen { Section = FileSection.Waiting, Speak = true };
            var titleRows = MenuFrameView.TitleRows(workspace.Character.Title, Glaze.Menu.FileColumnDegrees);
            var budget = HeightBudget.Of(TextSizeNow, titleRows, focused: true);
            var sideBudget = HeightBudget.Of(TextSizeNow, titleRows);
            screen.ReadQuestion(draft, Measure(draft), budget, sideBudget);
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-render", Version = "0", DeviceLabel = "editor" }));
            var room = new AnswerRoom(MenuFrame.RowsAPage(TextSizeNow, sourceLine: false));
            var at = DateTimeOffset.UtcNow;
            failures.AddRange(Shoot(folder, "waiting-four-answers", FileScreens.Screen(workspace, steering, screen, room), reading: screen, at: at));

            // Follow only the actual visible footer/choice controls. Read evidence comes from the
            // rendered views settling, including the last choice page after a page turn.
            for (var step = 0; step < 20; step++)
            {
                var frame = FileScreens.Screen(workspace, steering, screen, room);
                failures.AddRange(Shoot(folder, "waiting-choice-page-" + step.ToString(CultureInfo.InvariantCulture), frame, reading: screen, at: at));
                if (frame.Lines.Any(line => line.Action == FileScreens.Choose && line.Key == "3")) break;
                at = at.AddSeconds(1);
                if (frame.Footer[PromptSlot.FarRight]?.Id == FileScreens.NextPart) screen.Question.NextPart(at);
                else if (frame.Lines.Any(line => line.Action == FileScreens.MoreAnswers)) screen.Question.MoreAnswers(at);
                else break;
            }
            var choices = FileScreens.Screen(workspace, steering, screen, room);
            // Rereading may remove the question's missing-read reason and reveal more of this
            // page's option. Render that rebuilt page too; the previous snapshot proves only its rows.
            failures.AddRange(Shoot(folder, "waiting-choice-ready", choices, reading: screen, at: at));
            if (!choices.Lines.Any(line => line.Action == FileScreens.Choose && line.Key == "3"))
            {
                failures.Add("waiting-answer-chosen: no visible page of answers holds the fourth answer.");
                return failures;
            }
            screen.Question.Choose(3);
            if (!draft.IsChosen(0, question.Prompts[0].Options[3].Label)) failures.Add("waiting-answer-chosen: its visible answer did not become selected.");
            var cut = screen.Question.AnswerCut(0, 3);
            failures.AddRange(Shoot(folder, "waiting-answer-chosen", FileScreens.Screen(workspace, steering, screen, room), sideExpected: cut, reading: screen, at: at));
            for (var part = 0; part < screen.Question.SideParts && screen.Question.SideOption != null; part++)
            {
                var frame = FileScreens.Screen(workspace, steering, screen, room);
                failures.AddRange(Shoot(folder, "waiting-answer-detail-" + part.ToString(CultureInfo.InvariantCulture), frame, sideExpected: true, reading: screen, at: at));
                if (screen.Question.SidePart + 1 >= screen.Question.SideParts) break;
                at = at.AddSeconds(1);
                screen.Question.NextSidePart(at);
            }
            screen.Question.CloseSide();
            var read = FileScreens.Screen(workspace, steering, screen, room);
            failures.AddRange(Shoot(folder, "waiting-answer-read", read, reading: screen, at: at));
            if (read.Footer[PromptSlot.FarRight]?.Id != FileScreens.NextQuestion || read.Footer[PromptSlot.FarRight]?.Available != true)
                failures.Add("waiting-answer-read: the whole selected answer did not offer Review answers.");
            if (read.Footer.All.Any(each => each.Prompt.Id == FileScreens.SendAnswer)) failures.Add("waiting-answer-read: Send answer bypasses mandatory review.");
            at = at.AddSeconds(1);
            screen.Question.NextQuestion(at);
            if (!screen.Question.Reviewing) failures.Add("waiting-answer-review: Review answers did not open the review.");
            screen.Question.MeasureReview(Enumerable.Range(0, draft.Prompts.Count)
                .Select(prompt => MenuFrameView.RowsOf(FileScreens.ReviewRow(draft, prompt), Glaze.Menu.FileColumnDegrees)).ToList());
            for (var page = 0; page < screen.Question.Pages; page++)
            {
                var review = FileScreens.Screen(workspace, steering, screen, room);
                failures.AddRange(Shoot(folder, "waiting-answer-review-" + page.ToString(CultureInfo.InvariantCulture), review, reading: screen, at: at));
                if (review.Footer[PromptSlot.FarRight]?.Id == FileScreens.SendAnswer) break;
                if (review.Footer[PromptSlot.FarRight]?.Id != FileScreens.MoreAnswers) break;
                at = at.AddSeconds(1);
                screen.Question.MoreAnswers(at);
            }
            if (FileScreens.WhySendWaits(screen) != null) failures.Add("waiting-answer-review: all rendered review parts did not unlock Send answer: " + FileScreens.WhySendWaits(screen));
            return failures;
        }

        private static List<QuestionOption> LockoutAnswers() => new List<QuestionOption>
        {
            new QuestionOption { Label = "15 minutes", Description = "Short enough that a person who mistyped can try again over a coffee break" },
            new QuestionOption { Label = "1 hour", Description = "Slows a guessing attack a great deal while still letting people back in the same day" },
            new QuestionOption { Label = "Until reset", Description = "Locks the account until the person resets their password from the email we send" },
            new QuestionOption { Label = "Grows each time", Description = "Starts at one minute and doubles after each failed attempt, up to a day at most, then resets after a successful sign-in from a known device" },
        };

        /// <summary>
        /// The same four answers under a two-row question. A bounded Read question target costs more
        /// than its complete text, so separating it cannot save pages at the standard size.
        /// </summary>
        private static IEnumerable<string> LongQuestion(string folder)
        {
            var failures = new List<string>();
            var question = new QuestionView
            {
                QuestionId = "render-long-question",
                Answerable = true,
                AskedAt = Time,
                Prompts = new List<QuestionPrompt>
                {
                    new QuestionPrompt
                    {
                        Key = "q0", Header = "Lockout",
                        Text = "How long should a sign-in lockout last once someone has mistyped their password five times in a row?",
                        Options = LockoutAnswers(),
                        Multiple = false, FreeText = true, Secret = false,
                    },
                },
            };
            var work = WorkspaceRender.Work.Asking(question);
            var workspace = work.Present();
            var draft = new QuestionDraft("render-execution", question);
            var screen = new FileScreen { Section = FileSection.Waiting, Speak = true };
            var titleRows = MenuFrameView.TitleRows(workspace.Character.Title, Glaze.Menu.FileColumnDegrees);
            var budget = HeightBudget.Of(TextSizeNow, titleRows, focused: true);
            var sideBudget = HeightBudget.Of(TextSizeNow, titleRows);
            screen.ReadQuestion(draft, Measure(draft), budget, sideBudget);
            // At standard size the two-row question stays whole: its bounded one-row reread
            // target plus target gap is taller than the plain complete question plus line gap.
            // Longer questions at larger text retain their mandatory question-only parts.
            var standard = TextSizeNow == TextSize.Standard && titleRows == 1;
            if (standard && screen.Question.QuestionMeasured(0) != 2) failures.Add("waiting-long-question: the question wraps to " + screen.Question.QuestionMeasured(0) + " rows, not 2.");
            else if (standard)
            {
                var wholeHead = budget.Words(2) + budget.LineGap;
                var contextHead = budget.Target(1) + budget.TargetGap;
                if (contextHead < wholeHead) failures.Add("waiting-long-question: the bounded reread target unexpectedly costs less than complete two-row context.");
                var measure = Measure(draft)[0];
                var below = budget.Words(measure.PagedCaptionRows) + budget.LineGap + budget.Target() + budget.TargetGap + budget.Reason
                    + (measure.InputPagerFitsHalf ? 0f : budget.Target() + budget.TargetGap);
                int PagesWith(float head)
                {
                    var roomForChoices = budget.Room - below - head;
                    var pages = 1;
                    var used = 0f;
                    foreach (var rows in measure.AnswerRows)
                    {
                        var target = budget.Target(Math.Min(FileQuestion.AnswerRows, rows));
                        if (target > roomForChoices + 1e-6f) { failures.Add("waiting-long-question: a complete two-row choice exceeds the measured room."); return int.MaxValue; }
                        var takes = target + (used > 0f ? budget.TargetGap : 0f);
                        if (used > 0f && used + takes > roomForChoices + 1e-6f) { pages++; used = 0f; takes = target; }
                        used += takes;
                    }
                    return pages;
                }
                var wholePages = PagesWith(wholeHead);
                var contextPages = PagesWith(contextHead);
                var questionOnlyParts = (int)Math.Ceiling(2d / screen.Question.QuestionPartRows);
                if (wholePages > questionOnlyParts + (long)contextPages) failures.Add("waiting-long-question: separating the measured bounded context unexpectedly saves pages.");
                if ((screen.Question.QuestionParts(0), screen.Question.HeadRows(0), screen.Question.Pages) != (0, 2, wholePages))
                    failures.Add("waiting-long-question: the whole question must choose the measured minimum; separating it adds a question-only part without improving bounded-context capacity.");
            }
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-render", Version = "0", DeviceLabel = "editor" }));
            var room = new AnswerRoom(MenuFrame.RowsAPage(TextSizeNow, sourceLine: false));
            var at = DateTimeOffset.UtcNow;
            failures.AddRange(Shoot(folder, "waiting-long-question", FileScreens.Screen(workspace, steering, screen, room), reading: screen, at: at));
            for (var step = 0; step < screen.Question.QuestionParts(0) && screen.Question.QuestionPart != null; step++)
            {
                var frame = FileScreens.Screen(workspace, steering, screen, room);
                failures.AddRange(Shoot(folder, "waiting-long-question-part-" + step.ToString(CultureInfo.InvariantCulture), frame, reading: screen, at: at));
                if (frame.Footer[PromptSlot.FarRight]?.Id != FileScreens.NextPart) break;
                at = at.AddSeconds(1);
                screen.Question.NextPart(at);
            }
            failures.AddRange(Shoot(folder, "waiting-long-question-answers", FileScreens.Screen(workspace, steering, screen, room), reading: screen, at: at));
            return failures;
        }

        /// <summary>An approval of a long command, Approve pressed: its request in parts, the first showing.</summary>
        private static IEnumerable<string> Approval(string folder)
        {
            var work = WorkspaceRender.Work.Approval("psql \"$DATABASE_URL\" -c \"CREATE INDEX CONCURRENTLY IF NOT EXISTS sign_in_attempts_by_address ON sign_in_attempts (address, attempted_at DESC)\" && npm run migrate -- --to 0012_sign_in_attempts");
            var workspace = work.Present();
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-render", Version = "0", DeviceLabel = "editor" }));
            var screen = new FileScreen { Section = FileSection.Waiting };
            var room = new AnswerRoom(MenuFrame.RowsAPage(TextSizeNow, sourceLine: false));
            var failures = new List<string>(Shoot(folder, "approval", FileScreens.Screen(workspace, steering, screen, room)));
            steering.Press(WorkspaceAction.Approve, workspace);
            var request = steering.Request(workspace)!;
            var budget = HeightBudget.Of(TextSizeNow, MenuFrameView.TitleRows(workspace.Character.Title, Glaze.Menu.FileColumnDegrees));
            screen.ReadRequest(request, MenuFrameView.RowsOf(new PageLine(request, wordsAreData: true), Glaze.Menu.FileColumnDegrees),
                FileColumn.RequestPartRows(steering.Prompts().Select(asking => MenuFrameView.RowsOf(asking, Glaze.Menu.FileColumnDegrees)).Max(), budget), steering);
            var frame = FileScreens.Screen(workspace, steering, screen, room);
            if (frame.Footer[PromptSlot.Free] != null && screen.RequestParts > 1) failures.Add("approval-request: Yes shows before the request's last part was drawn.");
            failures.AddRange(Shoot(folder, "approval-request", frame));
            return failures;
        }

        /// <summary>The work running, with Tell it as the main action.</summary>
        private static IEnumerable<string> Activity(string folder)
        {
            var workspace = WorkspaceRender.Work.Running().Present();
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-render", Version = "0", DeviceLabel = "editor" }));
            var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
            return Shoot(folder, "activity", FileScreens.Screen(workspace, steering, screen, new AnswerRoom(MenuFrame.RowsAPage(TextSizeNow, sourceLine: false))));
        }

        /// <summary>The same measurement inputs as FileScreens.Line, without its presentation-only tone and actions.</summary>
        private static int SectionRows(SectionLine line)
        {
            var icon = line.File is FileKind kind ? FileScreens.Icon(kind) : (GlazeIcon?)null;
            var drawn = line.Runs.AnyTheirs
                ? new PageLine(line.Runs, icon: icon, chip: line.Chip)
                : new PageLine(line.Words, wordsAreData: line.Source || line.Evidence != Evidence.Halcyonic, icon: icon, chip: line.Chip);
            return MenuFrameView.RowsOf(drawn, Glaze.Menu.FileColumnDegrees);
        }

        /// <summary>Compare the render's measure to the production screen, including mixed runs, chips and file icons.</summary>
        private static IEnumerable<string> SectionMeasurements()
        {
            var failures = new List<string>();
            var checkedLines = 0;
            foreach (var icon in new FileKind?[] { null, FileKind.Code })
            foreach (var chip in new string?[] { null, "Agent says" })
            foreach (var outside in new[] { false, true })
            {
                var words = LineWords.Ours("Checks · ") + (outside ? LineWords.Theirs(new string('W', 80)) : LineWords.Ours(new string('W', 80)));
                var line = new SectionLine("", words.Text, SectionTone.Normal, rows: 20, said: words, chip: chip, file: icon);
                var answer = new SectionPresentation(SectionKind.Understanding, "From the render", SectionTone.Secondary,
                    new[] { new SectionLine("", "Changes", SectionTone.Normal), line }, simulated: true);
                var screen = new FileScreen { Section = FileSection.Changes, WhatChanged = new FileAnswer(answer, answer) };
                var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "render", Version = "0", DeviceLabel = "editor" }));
                var frame = FileScreens.Screen(WorkspaceRender.Work.Running().Present(), steering, screen, AnswerRoom.Unlimited);
                var drawn = frame.Lines.Single(shown => shown.Words == line.Words);
                var expected = MenuFrameView.RowsOf(drawn, Glaze.Menu.FileColumnDegrees);
                if (SectionRows(line) != expected) failures.Add("section measurement: the render counts " + SectionRows(line) + " rows, the production screen " + expected + ".");
                checkedLines++;
            }
            Debug.Log("Halcyonic: file render: compared " + checkedLines + " section measurements to production screens.");
            return failures;
        }

        /// <summary>What changed in the recorded demonstration, its first line's side panel open.</summary>
        private static IEnumerable<string> Changes(string folder)
        {
            var asset = Resources.Load<TextAsset>("HalcyonicDemonstration");
            if (asset == null) throw new InvalidOperationException("The demonstration is missing from Resources.");
            var recording = DemonstrationRecording.Parse(asset.text);
            var beginning = recording.Nodes[0];
            var answered = beginning.BranchesAfter(beginning.Events.Count).First(branch => branch.Answer.Kind == DemonstrationAnswerKind.Answer).Node;
            var atApproval = recording.Nodes[answered];
            var approve = atApproval.BranchesAfter(atApproval.Events.Count).First(branch => branch.Answer.Kind == DemonstrationAnswerKind.Approve);
            var executionId = approve.Answer.ExecutionId;
            var understanding = recording.UnderstandingAt(executionId, approve.Node, recording.Nodes[approve.Node].Events.Count)
                ?? throw new InvalidOperationException("The demonstration holds no understanding there.");
            var read = new IntelligenceRead<UnderstandingResponse>(understanding.Response, understanding.ReadAt, recorded: true);
            var now = DateTimeOffset.UtcNow;
            var room = new AnswerRoom(MenuFrame.RowsAPage(TextSizeNow, sourceLine: false), line =>
                SectionRows(line));
            // Each character the committed static atlas lacks, as the minus sign in "(+71 −0)", swapped for
            // one it has, for the render only: drawn from the dynamic fallback in the editor, it would be
            // written into the committed fallback font asset, which no render may change.
            var font = TMP_Settings.defaultFontAsset;
            var swapped = new SortedSet<char>();
            FileAnswer Answer(UnderstandPrompt prompt) => new FileAnswer(
                WorkspaceRender.InStaticAtlas(UnderstandingPresenter.Present(prompt, executionId, read, false, null, now, TimeZoneInfo.Local, room, AnswerDepth.Brief), font, swapped),
                WorkspaceRender.InStaticAtlas(UnderstandingPresenter.Present(prompt, executionId, read, false, null, now, TimeZoneInfo.Local, AnswerRoom.Unlimited, AnswerDepth.Full), font, swapped));
            var screen = new FileScreen
            {
                Section = FileSection.Changes,
                WhatChanged = Answer(UnderstandPrompt.WhatChanged),
                WhyChanged = Answer(UnderstandPrompt.WhyChanged),
                HowBuilt = Answer(UnderstandPrompt.HowBuilt),
            };
            screen.Chosen = FileScreens.WhatChangedKey;
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-render", Version = "0", DeviceLabel = "editor" }));
            return Shoot(folder, "changes-side-panel", FileScreens.Screen(WorkspaceRender.Work.Running().Present(), steering, screen, room));
        }
    }
}
