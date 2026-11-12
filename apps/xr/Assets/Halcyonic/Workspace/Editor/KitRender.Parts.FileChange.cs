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
    /// <summary>
    /// A file of Changes read (SCREENS, gaps 5 and 10, <see cref="FileChangeColumn"/>): every state's card drawn by FIELD's
    /// card view with its page of reading, at both text sizes, over white, the dark room and the bright room. Each card is
    /// made by the column itself from a synthetic diff or file of a synthetic project, so what is drawn is what the headset
    /// would show; each is measured and read as every card state is (<c>CardShot</c>: no word cut or joined on a dot, every
    /// block inside the card, each line at its indent with its band and mark, every label's words in the pixels). The
    /// column's actions are not drawn: the column beside the panel is XR's. In batch mode:
    /// -executeMethod Halcyonic.XR.Workspace.Editor.KitRender.FileChangeCheck; it exits 1 when a check fails. Images go to
    /// apps/xr/Builds/PartsRenders/FileChange, which git ignores.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] FileChangeStates =
        {
            "61-file-change", "61-file-change-gap", "61-file-change-next", "61-file-change-cut", "61-file-new", "61-file-whole", "61-file-picture", "61-file-private",
            "61-file-too-large", "61-file-empty", "61-file-nothing-to-compare", "61-file-first-version", "61-file-no-history", "61-file-not-read",
            "61-file-unknown",
            // SCREENS's run start on the result's card (RunStart): a task that is a plan, a run not kept to its folder.
            "62-result-plan-ready", "62-result-not-kept",
            // SCREENS's instructions waiting for the turn to end (WaitingInstructions).
            "63-waiting-list", "63-waiting-page", "63-not-sent",
            // SCREENS's one review card (ReviewCard), any command's review from a stand-in of the host's shape.
            "65-review-add", "65-review-send", "65-review-send-next", "65-review-publish", "65-review-said",
            // SCREENS's understanding's account on the one Changes page (ChangesAccount): what changed, then why, after the files.
            "66-account-what", "66-account-why", "66-account-unsourced",
        };

        /// <summary>The understanding's account's part for a state, from brief answers of the presenter's shape (<see cref="ChangesAccount"/>).</summary>
        private static RoomCard AccountCard(string state)
        {
            SectionLine Line(string tag, LineWords said) => new SectionLine(tag, said.Text, SectionTone.Normal, said: said);
            SectionPresentation Brief(params SectionLine[] lines) => new SectionPresentation(SectionKind.Understanding, "From Salidium 0.6.1", SectionTone.Secondary, lines, simulated: false);
            // As UnderstandingPresenter's brief answers stand: the count and whether a check followed; the latest reason quoted and how many more.
            var what = Brief(
                Line("observed", LineWords.Ours("3 files changed: 1 new, 2 edited")),
                Line("inferred", LineWords.Ours("All checked after the last change")));
            var why = Brief(
                new SectionLine("reported", "\u201cI split the tail into six segments so it moves like cloth, not a stick.\u201d", SectionTone.Claim, rows: 2,
                    said: LineWords.Theirs("I split the tail into six segments so it moves like cloth, not a stick.")),
                Line("", LineWords.Ours("And 2 more reasons it gave")));
            var source = state == "66-account-unsourced" ? (LineWords?)null : LineWords.Ours("From Its Records");
            var account = new ChangesAccount(what, why, null, source);
            return account.Card(state == "66-account-why" ? 1 : 0);
        }

        /// <summary>The one review card for a review state, from a synthetic host review (<see cref="ActionReview"/>).</summary>
        private static (RoomCard Card, ActionReview Review, int Page) ReviewOf(string state)
        {
            ActionReviewItem Entry(ReviewKind kind, string path, string note) => new ActionReviewItem { Kind = kind, Name = path, Note = note, Theirs = true };
            ActionReviewFact Fact(ReviewKind kind, string value) => new ActionReviewFact { Kind = kind, Value = value, Theirs = true };
            ActionReview Review(CommandType command, string confirm, string line, List<ActionReviewFact>? facts = null, List<ActionReviewItem>? items = null,
                List<ActionReviewWarning>? warnings = null, List<ActionReviewSaid>? said = null) => new ActionReview
            {
                CommandType = command, Digest = "sha256:" + new string('0', 64), ConfirmWords = confirm, WhatHappens = line,
                Facts = facts ?? new List<ActionReviewFact>(), Items = items ?? new List<ActionReviewItem>(), ItemsTotal = items?.Count ?? 0,
                Warnings = warnings ?? new List<ActionReviewWarning>(), Said = said ?? new List<ActionReviewSaid>(),
            };
            switch (state)
            {
                case "65-review-add":
                {
                    var add = Review(CommandType.WorkstreamAddToProject, "Add 3 Files", "Its work goes into the project, and you can undo it",
                        facts: new List<ActionReviewFact> { Fact(ReviewKind.Folder, "kite-run") },
                        items: new List<ActionReviewItem> { Entry(ReviewKind.FileChanged, "src/game/kite.js", "Edited"), Entry(ReviewKind.FileAdded, "src/game/tail.js", "New"), Entry(ReviewKind.FileRemoved, "src/game/old-wind.js", "Removed") });
                    return (ReviewCard.Card(add, "Add to Project"), add, 0);
                }
                case "65-review-said":
                {
                    // Words given long enough to take more than one part, shown whole before the list and the press.
                    var words = "Make the kite's tail longer and let it ripple in the wind, then give it six segments that each sway a little later than the one before, "
                        + "so the tail reads as cloth and not as a stick. Keep the colours as they are, and add a short note to the README saying how the tail moves.";
                    var said = Review(CommandType.WorkstreamCreate, "Start Task", "A new task starts in the project with these words",
                        facts: new List<ActionReviewFact> { Fact(ReviewKind.Folder, "kite-run") }, said: new List<ActionReviewSaid> { new ActionReviewSaid { Label = "First Task", Words = words } });
                    return (ReviewCard.Card(said, "New Task", 0, saidPages: 2), said, 0);
                }
                case "65-review-publish":
                {
                    var publish = Review(CommandType.ProjectPublish, "Publish", "Your checked game goes online at a lasting address",
                        warnings: new List<ActionReviewWarning> { new ActionReviewWarning { Code = "public", Line = "Anyone with its address can play it, even when its repository is private" } });
                    return (ReviewCard.Card(publish, "Publish"), publish, 0);
                }
                default:
                {
                    var paths = new[] { "src/game/kite.js", "src/game/tail.js", "src/sky/clouds.js", "src/sky/wind.js", "assets/kite-paper.png", "README.md", "package.json" };
                    var send = Review(CommandType.ProjectSend, "Send 7 Files", "Your files go to the project's repository as a new branch",
                        facts: new List<ActionReviewFact> { Fact(ReviewKind.Repository, "glimmer/kite-run"), Fact(ReviewKind.Branch, "kite-tail-ripple") },
                        items: paths.Select(path => Entry(ReviewKind.FileChanged, path, "Edited")).ToList());
                    var page = state == "65-review-send-next" ? 1 : 0;
                    return (ReviewCard.Card(send, "Send to Repository", page), send, page);
                }
            }
        }

        /// <summary>The result's card for a run-start state (<see cref="RunStart"/>), as the room's result draws it.</summary>
        private static RoomCard RunStartCard(string state)
        {
            if (state.StartsWith("63-", StringComparison.Ordinal))
            {
                var queued = new List<ExecutionViewQueuedInstructionsItem>
                {
                    new ExecutionViewQueuedInstructionsItem { CommandId = "c1", QueuedAt = "2026-11-11T18:00:00.000Z" },
                    new ExecutionViewQueuedInstructionsItem { CommandId = "c2", QueuedAt = "2026-11-11T18:01:00.000Z" },
                };
                string? Words(string id) => id == "c1" ? "Make the kite's tail longer, and let it ripple in the wind." : "Then make it blue.";
                if (state == "63-not-sent") return new UnsentInstruction("c1", Words("c1"), "queued_stopped").Card();
                var waiting = new WaitingInstructions("e1", queued, Words, opened: state == "63-waiting-page" ? "c1" : null);
                return waiting.PageCard() ?? waiting.ListCard(item => new KitControl(KitPlace.Step,
                    new MenuFrame(LineWords.Ours("Kite Run"), new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close))),
                    RoomResult.WaitingOpen, item.CommandId, item.Words ?? WaitingText.NotKept, GlazeIcon.Next, true, null, false));
            }
            var start = state == "62-result-plan-ready" ? new RunStart(plan: true, notConfinedReason: null, atRest: true, canCarryOut: true)
                : new RunStart(plan: false, notConfinedReason: "Halcyonic's sandbox is on macOS only, so this run is not confined.");
            var result = new RoomResult(false, false, false, state == "62-result-plan-ready" ? (bool?)null : true, null, null, start: start);
            return RoomPanelCards.Result(result, LineWords.TheirName("Kite Run")) ?? throw new InvalidOperationException(state + " has no card");
        }

        [MenuItem("Halcyonic/Render a File's Change and the Whole File")]
        public static void FileChangeMenu() => RunFileChange(exitWhenDone: false);

        /// <summary>The file reading's one entry point in batch mode: renders and checks every state's card, then exits 1 on any failure.</summary>
        public static void FileChangeCheck() => RunFileChange(exitWhenDone: true);

        private static void RunFileChange(bool exitWhenDone)
        {
            var failures = new List<string>();
            var previousField = ViewField.Current;
            var previousStill = GlazeMotion.Still;
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    var size = variant.Length == 0 ? "Standard" : variant;
                    var found = new List<string>();
                    foreach (var state in FileChangeStates)
                    {
                        try { found.AddRange(RenderFileChange(state, size)); }
                        catch (Exception error) { found.Add(state + " threw " + error); }
                    }
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("the file reading's render threw " + error); }
            finally
            {
                ViewField.Current = previousField;
                GlazeMotion.Still = previousStill;
                KeepFontsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: file reading: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: file reading: every check passed.");
            if (exitWhenDone) EditorApplication.Exit(failures.Count > 0 ? 1 : 0);
        }

        /// <summary>A synthetic file of a synthetic kite game: imports, an indented body, one line longer than a row holds.</summary>
        private const string FileChangeDiff =
            "diff --git a/src/game/kite.js b/src/game/kite.js\n--- a/src/game/kite.js\n+++ b/src/game/kite.js\n" +
            "@@ -1,9 +1,10 @@\n" +
            " import { Wind } from './wind.js';\n" +
            " import { Tail } from './tail.js';\n" +
            "-const LIFT = 3;\n" +
            "+const LIFT = 4;\n" +
            "+const TAIL_SEGMENTS = 6;\n" +
            " \n" +
            " export function fly(kite, wind) {\n" +
            "-  kite.y += wind.speed * LIFT;\n" +
            "+  kite.y += wind.speed * LIFT - kite.tail.weight(TAIL_SEGMENTS, wind.gust);\n" +
            "   return kite;\n" +
            " }\n" +
            "@@ -40,4 +41,4 @@ export function land(kite) {\n" +
            "   kite.landed = true;\n" +
            "-  kite.score = 0;\n" +
            "+  kite.score = Math.max(0, kite.score - 1);\n" +
            "   return kite;\n";

        /// <summary>The same synthetic file whole, as it is after the change.</summary>
        private const string FileChangeWhole =
            "import { Wind } from './wind.js';\nimport { Tail } from './tail.js';\nconst LIFT = 4;\nconst TAIL_SEGMENTS = 6;\n\nexport function fly(kite, wind) {\n" +
            "  kite.y += wind.speed * LIFT - kite.tail.weight(TAIL_SEGMENTS, wind.gust);\n  return kite;\n}\n\nexport function land(kite) {\n  kite.landed = true;\n" +
            "  kite.score = Math.max(0, kite.score - 1);\n  return kite;\n}\n\nexport function climb(kite, hand) {\n  kite.target = hand.height;\n  return kite;\n}\n";

        private static string LongDiff(int lines) =>
            "@@ -1," + lines + " +1," + lines + " @@\n" + string.Concat(Enumerable.Range(1, lines).Select(n => (n % 4 == 0 ? "+" : " ") + "  const cloud" + n + " = sky.cloud(" + n + ");\n"));

        /// <summary>The column standing in <paramref name="state"/> at the text size drawn now.</summary>
        private static FileChangeColumn FileChangeOf(string state)
        {
            const string project = "0193b3a0-0000-7000-8000-0000000000a1";
            const string path = "src/game/kite.js";
            var kind = state == "61-file-new" ? RoomChangeKind.Made : RoomChangeKind.Changed;
            var column = new FileChangeColumn(project, new RoomChange(kind, state == "61-file-private" ? ".env.local" : state == "61-file-new" ? "src/game/tail.js" : state == "61-file-picture" ? "art/kite.png" : state == "61-file-empty" ? "src/game/notes.txt"
                : state == "61-file-too-large" ? "assets/sky-map.json"
                : state is "61-file-change-next" or "61-file-change-cut" ? "src/sky/clouds.js" : path, 12, 13), GlazeText.Scale);
            ProjectFileChangeResponse Change(string? diff, bool complete = true, bool binary = false, bool withheld = false) => new ProjectFileChangeResponse
            {
                ProjectId = project, From = "a", To = "b", Path = column.Path, Change = kind == RoomChangeKind.Made ? FileChangeKind.Added : FileChangeKind.Changed,
                Binary = binary, Withheld = withheld, Diff = diff, Complete = complete,
            };
            ProjectFileResponse File(string? text, ProjectFileKind fileKind = ProjectFileKind.Text) => new ProjectFileResponse
            {
                ProjectId = project, Path = column.Path, Kind = fileKind, SizeBytes = text?.Length ?? 300_000, Text = fileKind == ProjectFileKind.Text ? text : null,
                Lines = fileKind == ProjectFileKind.Text ? text?.Split('\n').Length : null, ModifiedAt = "2026-11-11T18:40:00.000Z",
            };
            switch (state)
            {
                case "61-file-nothing-to-compare":
                    column.Located(new TurnVersions(TurnVersionsFound.None));
                    return column;
                case "61-file-first-version":
                    column.Located(new TurnVersions(TurnVersionsFound.First));
                    return column;
                case "61-file-no-history":
                    column.Located(new TurnVersions(TurnVersionsFound.NotSetUp));
                    return column;
            }
            column.Located(new TurnVersions(TurnVersionsFound.Found, "a", "b"));
            switch (state)
            {
                case "61-file-not-read":
                    column.ReadFailed();
                    break;
                case "61-file-unknown":
                    // The computer answered, but not with a reading this headset can show: the reason is unknown, and no Try Again.
                    column.ReadFailed(answered: true);
                    break;
                case "61-file-change-gap":
                    // The page that holds the gap between the change's two parts, which says how many lines it skips.
                    column.Changed(Change(FileChangeDiff));
                    while (!column.Rows.Any(row => row.Kind == RoomLineKind.Gap && row.Number is int) && column.NextPage()) { }
                    break;
                case "61-file-change-next":
                    column.Changed(Change(LongDiff(30)));
                    column.NextPage();
                    break;
                case "61-file-change-cut":
                    column.Changed(Change(LongDiff(16), complete: false));
                    // Its last page, at whichever size: that is where the cut is said.
                    while (column.NextPage()) { }
                    break;
                case "61-file-new":
                    column.Changed(Change("@@ -0,0 +1,5 @@\n+export class Tail {\n+  constructor(segments) {\n+    this.segments = segments;\n+  }\n+}\n"));
                    break;
                case "61-file-picture":
                    column.Changed(Change(null, binary: true));
                    break;
                case "61-file-private":
                    column.Changed(Change(null, withheld: true));
                    break;
                case "61-file-whole":
                case "61-file-too-large":
                case "61-file-empty":
                    column.Changed(Change(FileChangeDiff));
                    column.OpenWhole();
                    column.FileRead(state == "61-file-too-large" ? File(null, ProjectFileKind.TooLarge)
                        : state == "61-file-empty" ? File("")
                        : File(FileChangeWhole));
                    break;
                default:
                    column.Changed(Change(FileChangeDiff));
                    break;
            }
            return column;
        }

        private static List<string> RenderFileChange(string state, string size)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ViewField.Current = Quest3S;
            GlazeMotion.Still = false;
            var name = state + " (" + size + ")";
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "PartsRenders", "FileChange", size));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = new GameObject("File reading: " + state);
            var fixture = new GameObject("Fixture camera");
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var eyes = new Vector3(0f, EyeHeight, 0f);
            try
            {
                if (state.StartsWith("65-", StringComparison.Ordinal))
                {
                    var (reviewCard, review, page) = ReviewOf(state);
                    var reviewShot = CardShot(reviewCard, root.transform, eyes, name);
                    var actions = ReviewCard.Actions(review, page, saidPages: 2);
                    var mainId = CommandReview.Said(review).Count > 0 && page + 1 < ReviewCard.Parts(review, 2) ? ReviewCard.NextPart : ReviewCard.Confirm;
                    if (actions.Count(action => action.Main) != 1 || actions[0].Id != mainId || (mainId == ReviewCard.Confirm && actions[0].Words != review.ConfirmWords))
                        reviewShot.Failures.Add("the review's one main action is not its Confirm: " + string.Join(" / ", actions.Select(action => action.Words)));
                    if (reviewCard.Words().Any(words => words.Contains(review.CommandType.ToString(), StringComparison.Ordinal)))
                        reviewShot.Failures.Add("the card shows the command's type");
                    failures.AddRange(reviewShot.Failures.Select(failure => name + ": " + failure));
                    failures.AddRange(RenderShot(state, reviewShot, root, fixture, target, eyes, folder, name));
                    Debug.Log("Halcyonic: file reading: " + name + " rendered: review, actions " + string.Join(" / ", actions.Select(action => action.Words + (action.Main ? "*" : ""))));
                    return failures;
                }
                if (state.StartsWith("66-", StringComparison.Ordinal))
                {
                    var told = AccountCard(state);
                    var toldShot = CardShot(told, root.transform, eyes, name);
                    if (told.Words().Concat(told.List.Select(row => row.Name.Text)).Any(words => words.Contains("Salidium", StringComparison.Ordinal)))
                        toldShot.Failures.Add("the account names the provider's product");
                    if ((state == "66-account-unsourced") != (told.More == null)) toldShot.Failures.Add("the foot's source line stands where it should not, or is missing");
                    failures.AddRange(toldShot.Failures.Select(failure => name + ": " + failure));
                    failures.AddRange(RenderShot(state, toldShot, root, fixture, target, eyes, folder, name));
                    return failures;
                }
                if (state.StartsWith("62-", StringComparison.Ordinal) || state.StartsWith("63-", StringComparison.Ordinal))
                {
                    var started = RunStartCard(state);
                    var startShot = CardShot(started, root.transform, eyes, name);
                    if (state == "62-result-plan-ready" && started.Status?.Title != RunStart.PlanReady) startShot.Failures.Add("the card does not say Plan Ready");
                    if (state == "62-result-not-kept" && !started.Slots.Any(slot => slot.Value.Text == RunStart.NotKeptToFolder)) startShot.Failures.Add("the card does not say Not Kept to Folder");
                    failures.AddRange(startShot.Failures.Select(failure => name + ": " + failure));
                    failures.AddRange(RenderShot(state, startShot, root, fixture, target, eyes, folder, name));
                    return failures;
                }
                var column = FileChangeOf(state);
                // Reading shows only the column's sentence, which is XR's: every state drawn here has a card.
                var card = column.Card() ?? throw new InvalidOperationException(state + " has no card: " + column.Sentence);
                var expected = state switch
                {
                    "61-file-nothing-to-compare" => FileChangeStage.NoVersion,
                    "61-file-no-history" => FileChangeStage.NotSetUp,
                    "61-file-not-read" or "61-file-unknown" => FileChangeStage.Unreadable,
                    "61-file-first-version" => FileChangeStage.NoVersion,
                    "61-file-whole" or "61-file-too-large" or "61-file-empty" => FileChangeStage.File,
                    _ => FileChangeStage.Change,
                };
                var shot = CardShot(card, root.transform, eyes, name);
                if (column.Stage != expected) shot.Failures.Add("the column stands at " + column.Stage + ", not " + expected);
                var lined = state is "61-file-change" or "61-file-change-gap" or "61-file-change-next" or "61-file-change-cut" or "61-file-new" or "61-file-whole";
                if (lined != (card.Reading.Count > 0)) shot.Failures.Add(lined ? "the card shows no lines" : "a card that says why it shows no lines shows some");
                if (state == "61-file-change" && !card.Reading.Any(row => row.Continued)) shot.Failures.Add("the long line did not wrap onto a continued row");
                if (state == "61-file-change-gap" && !card.Reading.Any(row => row.Kind == RoomLineKind.Gap && row.Number is int)) shot.Failures.Add("no page shows the gap that counts the lines it skips");
                failures.AddRange(shot.Failures.Select(failure => name + ": " + failure));
                failures.AddRange(RenderShot(state, shot, root, fixture, target, eyes, folder, name));
                Debug.Log("Halcyonic: file reading: " + name + " rendered: " + card.Reading.Count + " rows, actions "
                    + string.Join(" / ", column.Actions().Select(action => action.Words + (action.Main ? "*" : ""))));
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
    
        /// <summary>Lays out, checks as seen and renders a shot over white, the dark room and the bright room, then reads it in the pixels.</summary>
        private static List<string> RenderShot(string state, PartShot shot, GameObject root, GameObject fixture, RenderTexture target, Vector3 eyes, string folder, string name)
        {
            var failures = new List<string>();
            SetLayer(root, KitLayer);
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
                File.WriteAllBytes(Path.Combine(folder, state + "-dark.png"), dark.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, state + "-bright.png"), bright.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, state + "-white.png"), white.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
                if (white != null) UnityEngine.Object.DestroyImmediate(white);
                if (dark != null) UnityEngine.Object.DestroyImmediate(dark);
                if (bright != null) UnityEngine.Object.DestroyImmediate(bright);
            }
            return failures;
        }
    }
}
