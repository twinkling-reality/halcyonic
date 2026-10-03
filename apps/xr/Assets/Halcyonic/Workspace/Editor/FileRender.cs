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
    /// rows each, one of them chosen and cut with its side panel, an approval's request in parts, the
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

        private static List<string> Run(string variant)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "FileRenders", variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(Question(folder));
                failures.AddRange(Approval(folder));
                failures.AddRange(Activity(folder));
                failures.AddRange(Changes(folder));
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
        private static IEnumerable<string> Shoot(string folder, string name, MenuFrame frame, bool sideExpected = false)
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
                file.Drawn += _ => drawn++;
                side.Drawn += _ => sideDrawn++;
                Lay(frame, file, side, eyes);
                var image = WorkspaceRender.Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);

                if (drawn != 1) failures.Add(name + ": the file's column raised Drawn " + drawn.ToString(CultureInfo.InvariantCulture) + " times as it settled, not once.");
                if (sideExpected && frame.Side == null) failures.Add(name + ": a chosen cut answer brought no side panel.");
                if (frame.Side != null && sideDrawn != 1) failures.Add(name + ": the side panel raised Drawn " + sideDrawn.ToString(CultureInfo.InvariantCulture) + " times, not once.");
                // The page's lines as the view wraps them, priced by height against what a lone file's page holds.
                var budget = HeightBudget.Of(TextSizeNow, MenuFrameView.TitleRows(frame.Subject, Glaze.Menu.FileColumnDegrees));
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
        private static void Lay(MenuFrame frame, MenuFrameView file, MenuFrameView side, Vector3 eyes)
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

        private static IReadOnlyList<PromptMeasure> Measure(QuestionDraft draft) => draft.Prompts.Select(prompt =>
        {
            var answers = prompt.Options.Select(FileScreens.AnswerWords).ToList();
            return new PromptMeasure(
                MenuFrameView.RowsOf(new PageLine("“" + WorkspaceText.OneLine(prompt.Text) + "”", wordsAreData: true), Glaze.Menu.FileColumnDegrees),
                answers.Select(words => MenuFrameView.RowsOf(new PageLine(words, wordsAreData: true, action: FileScreens.Choose, key: "0", choice: true), Glaze.Menu.FileColumnDegrees)).ToList(),
                answers.Select(words => MenuFrameView.RowsOf(new PageLine(words, wordsAreData: true), Glaze.Menu.SideColumnDegrees)).ToList());
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
                        Options = new List<QuestionOption>
                        {
                            new QuestionOption { Label = "15 minutes", Description = "Short enough that a person who mistyped can try again over a coffee break" },
                            new QuestionOption { Label = "1 hour", Description = "Slows a guessing attack a great deal while still letting people back in the same day" },
                            new QuestionOption { Label = "Until reset", Description = "Locks the account until the person resets their password from the email we send" },
                            new QuestionOption { Label = "Grows each time", Description = "Starts at one minute and doubles after each failed attempt, up to a day at most, then resets after a successful sign-in from a known device" },
                        },
                        Multiple = false, FreeText = true, Secret = false,
                    },
                },
            };
            var work = WorkspaceRender.Work.Asking(question);
            var workspace = work.Present();
            var draft = new QuestionDraft("render-execution", question);
            var screen = new FileScreen { Section = FileSection.Waiting, Speak = true };
            var budget = HeightBudget.Of(TextSizeNow, MenuFrameView.TitleRows(workspace.Character.Title, Glaze.Menu.FileColumnDegrees));
            screen.ReadQuestion(draft, Measure(draft), budget, budget);
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-render", Version = "0", DeviceLabel = "editor" }));
            var room = new AnswerRoom(MenuFrame.RowsAPage(TextSizeNow, sourceLine: false));
            failures.AddRange(Shoot(folder, "waiting-four-answers", FileScreens.Screen(workspace, steering, screen, room)));

            // The longest answer, on whichever page holds it, chosen: it brings its side panel. The
            // question's own pages first, where it has them, each drawn and stood a second.
            var at = DateTimeOffset.UtcNow;
            for (var step = 0; step < 20 && !screen.Question.Answers.Contains(3); step++)
            {
                screen.Question.Drawn(at);
                at = at.AddSeconds(1);
                if (screen.Question.QuestionPart != null) screen.Question.NextPart(at);
                else screen.Question.MoreAnswers(at);
            }
            if (!screen.Question.Answers.Contains(3)) failures.Add("waiting-answer-chosen: no page of answers holds the fourth answer.");
            screen.Question.Choose(3);
            var cut = screen.Question.AnswerCut(0, 3);
            failures.AddRange(Shoot(folder, "waiting-answer-chosen", FileScreens.Screen(workspace, steering, screen, room), sideExpected: cut));
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
                FileColumn.RequestPartRows(MenuFrameView.RowsOf(steering.Prompt(workspace) ?? "", Glaze.Menu.FileColumnDegrees), budget), steering);
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
                MenuFrameView.RowsOf(new PageLine(line.Words, wordsAreData: true, chip: line.Chip), Glaze.Menu.FileColumnDegrees));
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
