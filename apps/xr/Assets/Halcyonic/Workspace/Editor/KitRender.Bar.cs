#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The redesign's instruction (DESIGN's FINISH-3, 2026-11-05; unit 1d) on the workspace's real host, bound to the frame a
    /// task's file draws, with the redesign on (<see cref="RoomDesign.Enabled"/>) and the old kit not drawn: the row at rest,
    /// listening with and without a level, a typed draft empty and worded, a heard instruction's confirmation, and Sent. Each
    /// says the product's words whole in the one type size, stands its actions at Meta's hand target 12 mm apart, draws its
    /// panel in the translucent tonal material, and takes its presses as the file's own actions.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] BarStates =
        {
            "31-bar-rest", "31-bar-listening", "31-bar-listening-no-level", "31-bar-typing", "31-bar-typed", "31-bar-heard", "31-bar-confirm", "31-bar-confirm-part",
            "31-bar-confirm-escapes", "31-bar-confirm-part-escapes", "31-bar-sent", "31-bar-rest-agent",
        };

        /// <summary>
        /// The redesign's approval (DESIGN 2.4; unit U2) on the same host: asked, with Approve and Deny; its confirmation with
        /// Confirm arriving behind its lock, then the main action; a long request before its answer (its first eight lines) and
        /// under its confirmation (the file's first part, the next part the main action); a request that can only be denied; and
        /// Sending, the column emptied, while the answer sent may still take effect.
        /// </summary>
        private static readonly string[] AskStates =
        {
            "33-ask", "33-ask-confirm-arriving", "33-ask-confirm", "33-ask-long", "33-ask-long-part", "33-ask-long-part-2", "33-ask-deny-only", "33-ask-sending", "33-ask-tool",
            "33-ask-tool-confirm",
        };

        /// <summary>A request of a tool that is no command, synthetic: its name leads the request wherever it stands.</summary>
        private const string AskToolName = "Edit";

        private const string AskToolRequest = "src/game.js";

        /// <summary>
        /// The redesign's question (DESIGN 2.9; unit U2) on the same host, its prompt on the band of the agent's words: one
        /// prompt before an answer is chosen, the answers in the panel's place; the answer given over Send Answer; several
        /// prompts, the panel holding which it is and the choices in the column with Choose Any's marks; more answers than a
        /// page holds; the answers' confirmation; Sending; and a prompt longer than the band's three lines, read by parts.
        /// </summary>
        private static readonly string[] QuestionStates =
        {
            "34-question", "34-question-review", "34-question-several", "34-question-more", "34-question-confirm", "34-question-sending", "34-question-long",
            "34-question-tall", "34-question-stop", "34-question-writing", "34-question-alike",
        };

        /// <summary>
        /// The redesign's offered instructions (DESIGN 2.1) on the same host: the row where no keyboard is offered (Choose
        /// Instruction in Type's place), the instructions to choose from, and one chosen with Review Instruction.
        /// </summary>
        private static readonly string[] ChooseStates = { "35-choose-rest", "35-choose", "35-choose-chosen" };

        /// <summary>
        /// At work (DESIGN 2.3): the working field and the agent's cursor with Stop alone; the same where the runtime still
        /// takes an instruction, the row under the foot line; and Stop's own confirmation.
        /// </summary>
        private static readonly string[] WorkStates = { "36-work", "36-work-row", "36-stop-confirm", "36-work-recorded", "36-work-fixing", "36-work-watch", "36-work-steps" };

        /// <summary>A page of the file with no moment of its own, drawn by the general rule (RoomBar.OfFrame): Checks while its answer is read.</summary>
        private static readonly string[] PageStates = { "37-page", "37-more" };

        /// <summary>The suffix of a moment played as a recording (<see cref="ReplayedStates"/>).</summary>
        private const string Replayed = "-replayed";

        /// <summary>
        /// The moments the judges' path meets while a recording plays (JUDGE's reader, 2026-11-13: the cursor's words, an
        /// answer and a button stood over the notice), each played as recorded: at work with the cursor, the question with its
        /// answers in the panel's place, one chosen, the answers' review, the instructions offered and one chosen.
        /// </summary>
        private static readonly string[] ReplayedStates =
            new[] { "36-work", "34-question", "34-question-review", "34-question-confirm", "35-choose", "35-choose-chosen" }.Select(state => state + Replayed).ToArray();

        /// <summary>
        /// The workspace as the demonstration plays it, its work recorded on the synthetic agent: the bar carries the product's
        /// line over all it shows, here at work with no panel (the lead, 2026-11-12: the notice where no panel stands).
        /// </summary>
        private static WorkspacePresentation Recorded(WorkspacePresentation workspace)
        {
            var c = workspace.Character;
            var character = new CharacterPresentation(c.WorkstreamId, c.Title, c.Activity, c.StatusLabel, c.Attention, c.AttentionNotes, c.PendingApprovals,
                c.Synthetic, recorded: true, c.Stale, c.AttentionDetails, c.AttentionNoteRuns, c.AttentionDetailRuns);
            return new WorkspacePresentation(character, workspace.Objective, workspace.Execution, workspace.Runtime, workspace.Actions.ToList(),
                workspace.Actions.Where(workspace.RequiresConfirmation).ToList(), workspace.Commands, workspace.Activity);
        }

        /// <summary>
        /// Every part drawn under <paramref name="root"/> whose drawn bounds cross the notice's <paramref name="plate"/>, as both
        /// stand in <paramref name="space"/>'s plane (x and y; a touch of less than half a dp is not a crossing): its name.
        /// </summary>
        private static IEnumerable<string> NoticeCrossings(Transform root, Transform space, Transform plate)
        {
            Rect? Drawn(Renderer part)
            {
                var bounds = part.localBounds;
                if (!part.enabled || bounds.size == Vector3.zero) return null;
                var to = space.worldToLocalMatrix * part.transform.localToWorldMatrix;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                for (var corner = 0; corner < 8; corner++)
                {
                    var at = to.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x, (corner & 2) == 0 ? bounds.min.y : bounds.max.y, (corner & 4) == 0 ? bounds.min.z : bounds.max.z));
                    min = Vector2.Min(min, at);
                    max = Vector2.Max(max, at);
                }
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            Rect? notice = null;
            foreach (var part in plate.GetComponentsInChildren<Renderer>(false))
                if (Drawn(part) is Rect drawn) notice = notice is Rect was ? Rect.MinMaxRect(Mathf.Min(was.xMin, drawn.xMin), Mathf.Min(was.yMin, drawn.yMin), Mathf.Max(was.xMax, drawn.xMax), Mathf.Max(was.yMax, drawn.yMax)) : drawn;
            if (notice is not Rect band) yield break;
            var touch = RoomType.Units(0.5f);
            foreach (var part in root.GetComponentsInChildren<Renderer>(false))
            {
                if (part.transform.IsChildOf(plate) || Drawn(part) is not Rect drawn) continue;
                if (drawn.xMin < band.xMax - touch && drawn.xMax > band.xMin + touch && drawn.yMin < band.yMax - touch && drawn.yMax > band.yMin + touch)
                    yield return part.gameObject.name;
            }
        }

        /// <summary>The workspace with only <paramref name="actions"/> offered, as a recorded run offers none.</summary>
        private static WorkspacePresentation WithActions(WorkspacePresentation workspace, IReadOnlyList<WorkspaceAction> actions) =>
            new WorkspacePresentation(workspace.Character, workspace.Objective, workspace.Execution, workspace.Runtime, actions,
                actions.Where(workspace.RequiresConfirmation).ToList(), workspace.Commands, workspace.Activity);

        /// <summary>
        /// The result at rest (DESIGN 2.5 to 2.8): what the computer knows over the row, the agent's newest message on the
        /// band; a long message; Changes and its second page's way on; Checks passed and failed; and a project with a
        /// check and no observed turn.
        /// </summary>
        private static readonly string[] ResultStates = { "38-result", "38-result-long", "38-changes", "38-changes-long", "38-checks", "38-checks-failed", "38-checks-partly", "38-project", "38-result-agent", "38-result-stopped", "38-result-play", "38-result-play-refused", "38-result-play-not-in-build", "38-result-add", "38-add", "38-add-last", "38-conflict", "38-result-checking", "38-result-simulated", "38-result-unchecked", "38-result-full", "38-result-starter", "38-result-review" };

        /// <summary>The agent's newest message in the result's states, DESIGN's own, synthetic.</summary>
        private const string ResultSays = "I added the golden balloon and removed the old backup file.";

        private const string ResultSaysLong = "I added a golden balloon worth five points that pops with a gold burst. I also removed backup/old-game.js, since nothing used it, "
            + "and moved the score code into its own file. The menu shows the best score of the day now, and every balloon stays inside the play area at both screen sizes.";

        /// <summary>The turn the computer observed in the result's states: DESIGN's four files, or eleven for the page that turns; synthetic.</summary>
        private static ObservedTurn ResultTurn(bool many)
        {
            var created = "{\"path\":\"assets/golden-balloon.png\",\"lines\":null,\"bytes\":9200}";
            var changed = "{\"path\":\"src/balloons.js\",\"lines_before\":41,\"lines_after\":54,\"bytes\":2100},{\"path\":\"src/score.js\",\"lines_before\":18,\"lines_after\":21,\"bytes\":800}";
            if (many)
            {
                created += ",{\"path\":\"assets/pop-gold.wav\",\"lines\":null,\"bytes\":40100}";
                changed += string.Concat(new[] { ("src/sounds.js", 12, 15), ("src/menu.js", 88, 90), ("README.md", 30, 34), ("src/levels.js", 60, 72), ("src/input.js", 25, 27), ("src/pause.js", 14, 19) }
                    .Select(file => ",{\"path\":\"" + file.Item1 + "\",\"lines_before\":" + file.Item2 + ",\"lines_after\":" + file.Item3 + ",\"bytes\":900}"));
            }
            var json = "{\"turn_id\":\"synthetic-turn\",\"created\":{\"files\":[" + created + "],\"total\":" + (many ? 2 : 1) + "},\"changed\":{\"files\":[" + changed + "],\"total\":" + (many ? 8 : 2)
                + "},\"deleted\":{\"files\":[{\"path\":\"backup/old-game.js\",\"lines_before\":120}],\"total\":1},\"not_read\":0,\"skipped\":null}";
            return new ObservedTurn(1, Day + "09:05:00.000Z", HalcyonicJson.Deserialize<FilesObserved>(json));
        }

        /// <summary>A task's own copy with <paramref name="files"/> to add, or where the project changed the same places (A13), synthetic.</summary>
        private static RoomAdd CopyFor(int files, bool conflicted) => RoomAdd.Of(new OwnCopy
        {
            Directory = "/synthetic/copy", MadeAt = Day + "09:00:00.000Z", BaseTree = "base",
            ToAdd = new PathList { Paths = Enumerable.Range(1, files).Select(index => "src/part" + index + ".ts").ToList(), Total = files },
            ToAddReadAt = Day + "09:04:00.000Z", ToAddReview = "synthetic-review",
            Conflict = conflicted ? new ConflictedFiles { Files = new List<ConflictedFile> { new() { Path = "src/game.ts", Kind = ConflictKind.Text } }, Total = 1 } : null,
        }, working: false);

        /// <summary>
        /// The host's finding of a Partly Checked check (AUDIT2's words of 04:51Z, so it reads whole at Larger): the same text as
        /// the host's OWN_LOOK_NOT_KNOWN, so the render proves the words the host sends.
        /// </summary>
        private const string PartlyFinding = "The app's own look is not known: the starter's picture to compare with could not be made. Checking again may make it.";

        /// <summary>The demonstration's simulated checks as its Checks page says them (CheckedPresenter.SimulatedStatus's form), synthetic.</summary>
        private static readonly RoomCardStatus SimulatedChecks = new RoomCardStatus(RoomPanelCards.AllPassing, "Simulated at 05:00:12", RoomTone.Good);

        /// <summary>A check of the project's own folder that found only the starter's picture, as RoomResult.Of says it: Starter Only, quiet.</summary>
        private static readonly RoomCardStatus StarterChecks = new RoomCardStatus(CheckSteps.StarterOnlyWord, CheckSteps.StarterOnlyLine, RoomTone.Quiet);

        /// <summary>What the computer knows in each of the result's states, synthetic.</summary>
        private static RoomResult? ResultFor(string state)
        {
            CheckStepOutcome Step(CheckStepKind step, CheckStepResult result) => new CheckStepOutcome(step, result);
            var passing = new[] { Step(CheckStepKind.TypeCheck, CheckStepResult.Passed), Step(CheckStepKind.Build, CheckStepResult.Passed), Step(CheckStepKind.PageDrawn, CheckStepResult.Passed), Step(CheckStepKind.VrOffered, CheckStepResult.Passed) };
            var partly = new[] { Step(CheckStepKind.TypeCheck, CheckStepResult.Passed), Step(CheckStepKind.Build, CheckStepResult.Passed), Step(CheckStepKind.PageDrawn, CheckStepResult.Unknown), Step(CheckStepKind.VrOffered, CheckStepResult.NotRun) };
            var failing = new[] { Step(CheckStepKind.TypeCheck, CheckStepResult.Passed), Step(CheckStepKind.Build, CheckStepResult.Failed), Step(CheckStepKind.PageDrawn, CheckStepResult.NotRun), Step(CheckStepKind.VrOffered, CheckStepResult.NotRun) };
            return state switch
            {
                "38-result" or "38-result-long" or "38-result-agent" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing),
                "38-changes" => new RoomResult(false, true, false, true, ResultTurn(false), passing, RoomResultPage.Changes),
                "38-changes-long" => new RoomResult(false, true, false, true, ResultTurn(true), passing, RoomResultPage.Changes),
                "38-checks" => new RoomResult(isVr: true, canMakeVr: false, canTry: true, passed: true, ResultTurn(false), passing, RoomResultPage.Checks),
                "38-checks-failed" => new RoomResult(false, true, false, false, ResultTurn(false), failing, RoomResultPage.Checks),
                // A check whose verdict is not known (VRIFY): served, so Try Build stands; AUDIT2's finding, its longest form, under the line.
                "38-checks-partly" => new RoomResult(false, true, true, null, ResultTurn(false), partly, RoomResultPage.Checks, partlyChecked: true, finding: PartlyFinding),
                "38-result-stopped" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: false, ResultTurn(false), failing),
                "38-result-play" => new RoomResult(isVr: false, canMakeVr: true, canTry: true, passed: true, ResultTurn(false), passing, play: PlayInHeadset.Offered),
                // Every action a result offers at once: the column's six with Close, so the lowest acting one goes behind More (AUDIT's 13:41Z (b)).
                "38-result-full" => new RoomResult(isVr: false, canMakeVr: true, canTry: true, passed: true, ResultTurn(false), passing, play: PlayInHeadset.Offered, canShare: true),
                "38-result-play-refused" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, play: PlayInHeadset.NotConfined),
                "38-result-play-not-in-build" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, play: PlayInHeadset.NotInThisBuild),
                "38-project" => new RoomResult(isVr: false, canMakeVr: true, canTry: true, passed: true, null, passing),
                // The card says what its Checks page says (JUDGE's ALERT 05:04Z): a check under way, a demonstration's simulated
                // checks as simulated, and with neither, no outcome at all, never "Nothing checked yet" over a check.
                "38-result-checking" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: null, ResultTurn(false), null, checkRunning: true),
                "38-result-simulated" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: null, ResultTurn(false), null, simulated: SimulatedChecks),
                "38-result-unchecked" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: null, ResultTurn(false), null),
                "38-result-starter" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: null, ResultTurn(false), null, simulated: StarterChecks),
                "38-result-add" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, add: CopyFor(10, conflicted: false), addReview: "synthetic-review"),
                // The review card as take 3 films it (TASKS): a VR project's passed result with work to add, Try Build and Add to Project.
                "38-result-review" => new RoomResult(isVr: true, canMakeVr: false, canTry: true, passed: true, ResultTurn(false), passing, add: CopyFor(3, conflicted: false), addReview: "synthetic-review"),
                "38-add" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, RoomResultPage.Add, add: CopyFor(10, conflicted: false), addReview: "synthetic-review"),
                "38-add-last" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, RoomResultPage.Add, part: 1, add: CopyFor(10, conflicted: false), addReview: "synthetic-review"),
                "38-conflict" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, RoomResultPage.Conflict, add: CopyFor(3, conflicted: true)),
                _ => null,
            };
        }

        /// <summary>The file the agent edits in the at-work state, synthetic.</summary>
        private const string WorkFile = "src/score.js";

        /// <summary>The design's own question, synthetic.</summary>
        private const string QuestionPromptWords = "Which colour scheme should the game use?";

        /// <summary>An answer the person writes, synthetic.</summary>
        private const string QuestionWritten = "Teal, with a gold string";

        private const string QuestionLongWords = "Which colour scheme should the game use? The menu and the score board follow the same choice, the balloons keep their own "
            + "colours either way, and the pause screen is dimmed in both. I can also follow the headset's own setting, which changes with the room's light, if neither "
            + "fits: say which you want, and whether the choice should hold for every level or only for this one, before I change the files.";

        private static QuestionView QuestionFor(string state)
        {
            QuestionOption Option(string label, string? description = null) => new QuestionOption { Label = label, Description = description };
            var scheme = new QuestionPrompt
            {
                Key = "scheme", Header = "Colour scheme", Text = state is "34-question-long" or "34-question-tall" ? QuestionLongWords : QuestionPromptWords,
                Options = new List<QuestionOption> { Option("Light", "Dark text on a light background"), Option("Dark", "Light text on a dark background") },
            };
            if (state == "34-question-more")
                scheme.Options = new List<QuestionOption> { Option("Light"), Option("Dark"), Option("Sepia", "Warm paper tones"), Option("High Contrast"), Option("Pastel"), Option("Follow the Headset") };
            // The tallest page: four answers that each wrap to three lines, never cut, under a prompt the band reads by parts,
            // with the band's way to its next part. The pair stands on its foot line and grows upward, so it holds them.
            if (state == "34-question-tall")
                scheme.Options = new List<QuestionOption>
                {
                    Option("Light", "Dark text on a light background, bright balloons, and the score board in the same light colours as the menu"),
                    Option("Dark", "Light text on a dark background, dimmer balloons, and the pause screen a shade darker than the game"),
                    Option("Follow the Headset", "Whichever the headset is set to when the game starts, changing with the room's light as it does"),
                    Option("Ask Each Time", "A choice on the first screen of every game, kept for that game only and asked again at the next one"),
                };
            // A question Halcyonic cannot answer (it asks for a secret), its prompt longer than the band's three lines: Stop is the way on.
            if (state == "34-question-stop")
                scheme = new QuestionPrompt { Key = "token", Header = "Token", Text = QuestionLongWords, FreeText = true, Secret = true };
            // An answer may be written; and two answers that read alike, which cannot be chosen.
            if (state == "34-question-writing") scheme.FreeText = true;
            if (state == "34-question-alike")
                scheme.Options = new List<QuestionOption> { Option("Dark", "Light text on a dark background"), Option("Dark", "The headset's own dark theme"), Option("Light") };
            var prompts = new List<QuestionPrompt> { scheme };
            if (state == "34-question-several")
                prompts.Add(new QuestionPrompt
                {
                    Key = "screens", Header = "Screens", Text = "Which screens should change?", Multiple = true,
                    Options = new List<QuestionOption> { Option("Menu"), Option("Score Board", "The best scores of the day"), Option("Pause Screen") },
                });
            return new QuestionView { QuestionId = "synthetic-question-scheme", Answerable = true, AskedAt = Day + "09:03:10.000Z", Prompts = prompts };
        }

        /// <summary>The design's own request, synthetic.</summary>
        private const string AskCommand = "rm backup/old-game.js";

        /// <summary>A request past eight lines of mono in the panel, synthetic.</summary>
        private const string AskLongCommand = "grep -rnE \"balloon\\s+\\d+\" src/game.js; for file in backup/old-game.js backup/old-score.js backup/old-sprites.png backup/old-levels.json; do "
            + "if test -f \"$file\"; then echo \"removing $file\"; rm \"$file\"; else echo \"no $file here\"; fi; done; "
            + "npm test -- --grep \"golden balloon\" --reporter spec; git status --short; git diff --stat; echo finished cleaning the old backup files";

        /// <summary>What a microphone recorded, 50 ms a step, rising then falling: real samples the level is taken from, as hold to talk takes it.</summary>
        private static LevelHistory RecordedLevel()
        {
            var history = new LevelHistory();
            var samples = new float[800];
            for (var step = 0; step < LevelHistory.Bars; step++)
            {
                var amplitude = 0.004f + 0.25f * Mathf.Sin(Mathf.PI * step / (LevelHistory.Bars - 1));
                for (var index = 0; index < samples.Length; index++) samples[index] = amplitude * Mathf.Sin(index * 0.3f);
                history.Add(samples, samples.Length);
            }
            return history;
        }

        /// <summary>The design's own draft, synthetic.</summary>
        private const string BarDraft = "Add a golden balloon worth five points, and remove the old backup file.";

        /// <summary>A heard instruction in plain words, synthetic: the confirmation a person and a fresh reader see, telling the balloon story.</summary>
        private const string BarConfirmPlain = "Add a golden balloon worth five points that pops with a short sound.";

        /// <summary>A long heard instruction in plain words, synthetic: the same story, long enough for the file to cut in parts.</summary>
        private const string BarLongPlain = "Add a golden balloon worth five points to the game, then a silver one worth three, then a bomb that ends the round "
            + "when it is popped. Keep the score in the top corner, count each pop once, play the short pop sound for every balloon and the low one for the bomb, "
            + "and show the best score of the day under it. When the round ends, show the three best scores with the time each was made, let the player start again "
            + "with one press, and keep every balloon inside the play area at both screen sizes. Then remove the old backup file and run the tests again.";

        /// <summary>
        /// A heard instruction holding backslashes, synthetic: the confirmation must read exactly the words that will be sent. Its own
        /// state (31-bar-confirm-escapes, the same checks), so the frames a person reads tell the plain story (AUDIT2 and the lead, 2026-11-16).
        /// </summary>
        private const string BarConfirmWords = @"Add a golden balloon worth five points in games\balloon.js, matching \d+ points.";

        /// <summary>A heard instruction long enough for the file to cut in parts, with a backslash in its first, synthetic.</summary>
        private const string BarLongWords = @"Add a golden balloon worth five points in games\balloon.js, then a silver one worth three, then a bomb that ends the round "
            + "when it is popped. Keep the score in the top corner, count each pop once, play the short pop sound for every balloon and the low one for the bomb, "
            + "and show the best score of the day under it. When the round ends, show the three best scores with the time each was made, let the player start again "
            + "with one press, and keep every balloon inside the play area at both screen sizes. Then remove the old backup file and run the tests again.";

        private static Shot BarShot(string state, Transform root, Vector3 eyes)
        {
            // A moment as a recording plays it: the same moment, its work recorded, so the notice stands over all it shows.
            var replayed = state.EndsWith(Replayed, StringComparison.Ordinal);
            if (replayed) state = state.Substring(0, state.Length - Replayed.Length);
            var asking = AskStates.Contains(state);
            var questioning = QuestionStates.Contains(state);
            var choosing = ChooseStates.Contains(state);
            var (voice, sent, expected, caption) = state switch
            {
                "38-result" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result at rest: the files the computer saw change, its check, the agent's message on the band, synthetic data"),
                "38-result-long" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result with a long message: read by parts on the band, the row not waiting for it, synthetic data"),
                "38-changes" => (VoiceStage.Idle, false, RoomBarState.Rest, "Changes: a row a file the computer saw change, and Back, synthetic data"),
                "38-changes-long" => (VoiceStage.Idle, false, RoomBarState.Rest, "Changes with more than a page: eight rows, Next Page and Back, synthetic data"),
                "38-checks" => (VoiceStage.Idle, false, RoomBarState.Rest, "Checks passed: a row a step of the computer's check, Try Build and Back, synthetic data"),
                "38-checks-partly" => (VoiceStage.Idle, false, RoomBarState.Rest, "Checks partly checked: Partly Checked, its line, the host's finding whole under it, Try Build and Back, synthetic data"),
                "38-checks-failed" => (VoiceStage.Idle, false, RoomBarState.Rest, "Checks failed: the steps the check reached, Changes and Back, the instruction the row's Type, synthetic data"),
                "38-result-agent" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the agent offers never ask: the agent and its level in the row beside it, synthetic data"),
                "38-result-stopped" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the agent's tries at a failed check ran out: that line first, Write Instruction leading, synthetic data"),
                "38-result-add" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the task's own copy holds work to add: Make VR Copy leading, Add to Project beside it, synthetic data"),
                "38-result-review" => (VoiceStage.Idle, false, RoomBarState.Rest, "The review card as take 3 films it: All Passing, 4 of 4 Checks Passed and 3 Files one press each, Try Build leading, Add to Project, synthetic data"),
                "38-add" => (VoiceStage.Idle, false, RoomBarState.Rest, "Add to Project's review: the task, where its work goes, the first five of ten files, Add to Project, Next Page and Back, synthetic data"),
                "38-add-last" => (VoiceStage.Idle, false, RoomBarState.Rest, "Add to Project's review, its last page: the last five files, Add to Project, one press, and Back, synthetic data"),
                "38-conflict" => (VoiceStage.Idle, false, RoomBarState.Rest, "Where the project changed the same file: FIELD's card naming it, Let Agent Fix and Back, synthetic data"),
                "38-result-full" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result offering every action at once: Close kept last in the column, Checks and Changes the card's facts, synthetic data"),
                "38-result-play" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the project's checked preview can open on the headset: Play in Headset beside Try Build, synthetic data"),
                "38-result-play-refused" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the preview reads outside its project: no Play in Headset, its reason at the card's foot, synthetic data"),
                "38-result-play-not-in-build" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result on a paired release build, which carries no relay: no Play in Headset, its reason at the card's foot, synthetic data"),
                "36-work-steps" => (VoiceStage.Idle, false, RoomBarState.Hidden, "At work: the task's steps list in the panel's place beside Stop, Working now, synthetic data"),
                "36-work-watch" => (VoiceStage.Idle, false, RoomBarState.Hidden, "Watch It Work open on live work: Done Watching over Stop, the turn's three files on the arc in the panel's place, synthetic data"),
                "36-work-fixing" => (VoiceStage.Idle, false, RoomBarState.Hidden, "At work while the agent's first try at a failed check runs: the failure and the try in the panel beside Stop, synthetic data"),
                "38-result-checking" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result while the computer's check is under way: Checking on the card, as the Checks page says, synthetic data"),
                "38-result-simulated" => (VoiceStage.Idle, false, RoomBarState.Rest, "The demonstration's result: its simulated checks on the card as its Checks page says them, synthetic data"),
                "38-result-unchecked" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result with no check of the computer's: the files only, no outcome claimed, synthetic data"),
                "38-result-starter" => (VoiceStage.Idle, false, RoomBarState.Rest, "A fresh app's result: the check found only the starter's picture, Starter Only and its line, quiet, never failed, synthetic data"),
                "38-project" => (VoiceStage.Idle, false, RoomBarState.Rest, "A project with a check and no observed turn: Build Passed, Make VR Copy and Try Build, synthetic data"),
                "37-more" => (VoiceStage.Idle, false, RoomBarState.Page, "More pressed at rest: the task's routes in the panel's place, each with its chevron, then Back, synthetic data"),
                "37-page" => (VoiceStage.Idle, false, RoomBarState.Page, "A page with no moment of its own: its line in the panel, its actions in the column, synthetic data"),
                "36-work" => (VoiceStage.Idle, false, RoomBarState.Hidden, "At work: the field, the agent's cursor saying its step, Stop alone, synthetic data"),
                "36-work-recorded" => (VoiceStage.Idle, false, RoomBarState.Hidden, "At work with nothing to press or name, as a recorded run plays: the field alone, synthetic data"),
                "36-work-row" => (VoiceStage.Idle, false, RoomBarState.Rest, "At work where the agent still takes an instruction: the row under the foot line, synthetic data"),
                "36-stop-confirm" => (VoiceStage.Idle, false, RoomBarState.Confirm, "Stop pressed: the file's question, Confirm behind its lock, and Cancel, synthetic data"),
                "35-choose-rest" => (VoiceStage.Idle, false, RoomBarState.Rest, "At rest with no keyboard: Hold to Talk and Choose Instruction, synthetic data"),
                "35-choose" => (VoiceStage.Idle, false, RoomBarState.Choose, "The instructions to choose from, none chosen, and Cancel, synthetic data"),
                "35-choose-chosen" => (VoiceStage.Idle, false, RoomBarState.Choose, "One instruction chosen: Review Instruction, synthetic data"),
                "34-question" => (VoiceStage.Idle, false, RoomBarState.Question, "A question: its prompt on the band, the answers in the panel's place, synthetic data"),
                "34-question-review" => (VoiceStage.Idle, false, RoomBarState.Question, "The answer given, over Send Answer, synthetic data"),
                "34-question-several" => (VoiceStage.Idle, false, RoomBarState.Question, "The second of two questions, any number to choose: which it is in the panel, the choices beside it, synthetic data"),
                "34-question-more" => (VoiceStage.Idle, false, RoomBarState.Question, "More answers than a page holds: More Choices, synthetic data"),
                "34-question-confirm" => (VoiceStage.Idle, false, RoomBarState.Question, "Send Answer pressed: Send Answers?, Confirm behind its lock, and Cancel, synthetic data"),
                "34-question-sending" => (VoiceStage.Idle, false, RoomBarState.Question, "The answer sent: Sending, no action, synthetic data"),
                "34-question-long" => (VoiceStage.Idle, false, RoomBarState.Question, "A long question read by parts on the band: Next Part, synthetic data"),
                "34-question-tall" => (VoiceStage.Idle, false, RoomBarState.Question, "A tall page: four answers that wrap under a long prompt read by parts, synthetic data"),
                "34-question-stop" => (VoiceStage.Idle, false, RoomBarState.Question, "A question that cannot be answered, its long prompt unread: Stop stands, the frame not yet read, synthetic data"),
                "34-question-writing" => (VoiceStage.Idle, false, RoomBarState.Question, "An answer being written: the panel is the field, Use Answer and Cancel, synthetic data"),
                "34-question-alike" => (VoiceStage.Idle, false, RoomBarState.Question, "Two answers that read alike: the answers on the band, why in the panel, synthetic data"),
                "33-ask" => (VoiceStage.Idle, false, RoomBarState.Request, "An approval asked: the request in mono, Run Command?, Approve and Deny, synthetic data"),
                "33-ask-confirm-arriving" => (VoiceStage.Idle, false, RoomBarState.Request, "Approve pressed: Confirm on its arrival behind its lock, and Cancel, synthetic data"),
                "33-ask-confirm" => (VoiceStage.Idle, false, RoomBarState.Request, "Approve pressed, the lock passed: Confirm and Cancel, synthetic data"),
                "33-ask-long" => (VoiceStage.Idle, false, RoomBarState.Request, "A long request asked: its first eight lines, Approve and Deny, synthetic data"),
                "33-ask-long-part" => (VoiceStage.Idle, false, RoomBarState.Request, "A long request under its confirmation: its first part, Next Part and Cancel, synthetic data"),
                "33-ask-long-part-2" => (VoiceStage.Idle, false, RoomBarState.Request, "A long request under its confirmation: its second part, its own rows, synthetic data"),
                "33-ask-deny-only" => (VoiceStage.Idle, false, RoomBarState.Request, "A request that can only be denied: why in the sentence bar, Deny alone, synthetic data"),
                "33-ask-sending" => (VoiceStage.Idle, false, RoomBarState.Request, "The answer sent: Sending, the column emptied, synthetic data"),
                "33-ask-tool" => (VoiceStage.Idle, false, RoomBarState.Request, "A tool that is no command asked: its name leads the request, synthetic data"),
                "33-ask-tool-confirm" => (VoiceStage.Idle, false, RoomBarState.Request, "That tool's confirmation: its name still leads the request, synthetic data"),
                "31-bar-listening" => (VoiceStage.Listening, false, RoomBarState.Listening, "Hold to Talk held: Listening, the level from recorded samples, synthetic data"),
                "31-bar-listening-no-level" => (VoiceStage.Listening, false, RoomBarState.Listening, "Hold to Talk held where nothing records a level, synthetic data"),
                "31-bar-typing" => (VoiceStage.Idle, false, RoomBarState.Draft, "Type pressed: the panel is the field, only Cancel until a word, synthetic data"),
                "31-bar-typed" => (VoiceStage.Idle, false, RoomBarState.Draft, "A typed draft read: Send Instruction and Cancel, no sentence bar, synthetic data"),
                "31-bar-heard" => (VoiceStage.Idle, false, RoomBarState.Draft, "A heard draft read: Send Heard Instruction?, Send Instruction, Talk Again and Cancel, synthetic data"),
                "31-bar-confirm" => (VoiceStage.Idle, false, RoomBarState.Confirm, "An instruction's confirmation read: Confirm and Cancel, synthetic data"),
                "31-bar-confirm-part" => (VoiceStage.Idle, false, RoomBarState.Confirm, "A long instruction's confirmation, its first part: Next Part and Cancel, synthetic data"),
                "31-bar-confirm-escapes" => (VoiceStage.Idle, false, RoomBarState.Confirm, "An instruction's confirmation, its words holding backslashes: Confirm and Cancel, synthetic data"),
                "31-bar-confirm-part-escapes" => (VoiceStage.Idle, false, RoomBarState.Confirm, "A long instruction's confirmation with a backslash in its first part: Next Part and Cancel, synthetic data"),
                "31-bar-sent" => (VoiceStage.Idle, true, RoomBarState.Sent, "Sent, the run not started yet, synthetic data"),
                "31-bar-rest-agent" => (VoiceStage.Idle, false, RoomBarState.Rest, "At rest with the agent and its level in the row beside Hold to Talk and Type, each in a place of its own, synthetic data"),
                _ => (VoiceStage.Idle, false, RoomBarState.Rest, "At rest: Hold to Talk and Type, synthetic data"),
            };
            var shot = new Shot { Caption = caption };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var factory = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
            var workspace = state switch
            {
                "33-ask-long" or "33-ask-long-part" or "33-ask-long-part-2" => SelectedWorkspace(waiting: true, command: AskLongCommand),
                "33-ask-deny-only" => SelectedWorkspace(waiting: true, command: AskCommand + " [truncated]", approvable: false),
                "33-ask-tool" or "33-ask-tool-confirm" => SelectedWorkspace(waiting: true, command: AskToolRequest, tool: AskToolName),
                _ when asking => SelectedWorkspace(waiting: true, command: AskCommand),
                _ when questioning => SelectedWorkspace(waiting: true, asked: QuestionFor(state), confirmAnswers: state == "34-question-confirm"),
                // At work, on an agent that takes no instruction while it works, and on one that does.
                "36-work" or "36-stop-confirm" or "36-work-fixing" or "36-work-watch" or "36-work-steps" => WithActivity(SelectedWorkspace(waiting: false, instructWhileRunning: false), ShortReport()),
                // Nothing to press: no Stop and no instruction while it works, as a recorded run offers.
                "36-work-recorded" => Recorded(WithActions(WithActivity(SelectedWorkspace(waiting: false, instructWhileRunning: false), ShortReport()), new WorkspaceAction[0])),
                "36-work-row" => Running(ShortReport()),
                // The result: at rest, the agent's newest message its one line of activity.
                _ when ResultStates.Contains(state) => WithActivity(SelectedWorkspace(waiting: false, status: ExecutionStatus.Completed),
                    new[] { new ActivityEntry(1, Day + "09:04:00.000Z", ActivityKind.Message, state == "38-result-long" ? ResultSaysLong : ResultSays, true) }),
                // More at rest on a finished task with no log of its own, so the routes stand on the file's first page as the director has them.
                "37-more" => WithActivity(SelectedWorkspace(waiting: false, status: ExecutionStatus.Completed), new ActivityEntry[0]),
                // Every other moment of the row is at rest: the run has ended, and its next instruction starts the next one.
                _ => WithActivity(SelectedWorkspace(waiting: false, status: ExecutionStatus.Completed), ShortReport()),
            };
            if (replayed) workspace = Recorded(workspace);
            // The host lists the files the turn changes, as it does where it observes the task's folder: Watch It Work is offered.
            if (state == "36-work-watch")
                workspace.Execution!.Writing = new ExecutionWritingView
                {
                    Paths = Enumerable.Range(0, 3).Select(index => BuildFilePanel(index).Path).Reverse().ToList(), PathsTotal = 3, TotalComplete = true, At = Day + "09:04:00.000Z",
                };
            if (state == "36-work")
            {
                // The runtime reports the file it edits: the cursor says so.
                workspace.Execution!.ActiveTools = new List<ToolActivityView> { new() { ToolCallId = "synthetic-tool", ToolName = "Edit", Title = WorkFile, StartedAt = Day + "09:04:00.000Z" } };
                workspace.Execution.ToolActivity = ExecutionViewToolActivity.Running;
            }
            // The task's failed check back with its agent on its own (ADR 0044): the first try under way, or both tries spent.
            if (state is "36-work-fixing" or "38-result-stopped")
                workspace.Execution!.AutoFix = new AutoFixView
                {
                    State = state == "36-work-fixing" ? AutoFixViewState.Fixing : AutoFixViewState.Ended, CheckId = "0193b3a0-0000-7000-8000-000000000001",
                    Attempt = state == "36-work-fixing" ? 1 : 2, Cap = 2, Reason = state == "36-work-fixing" ? null : AutoFixEndReason.CapReached,
                };
            var steering = new WorkspaceSteering(factory, () => DateTimeOffset.UtcNow);
            if (state == "36-stop-confirm" && steering.Press(WorkspaceAction.Interrupt, workspace).Step != SteeringStep.Confirm) shot.Failures.Add("Stop did not ask for its confirmation");
            var screen = new FileScreen { Section = asking || questioning ? FileSection.Waiting : state == "37-page" ? FileSection.Checks : FileSection.Activity, Speak = true };
            // The task's routes the room offers, as the director offers them where each is reachable: More stands at rest for them.
            if (state == "37-more")
            {
                screen.ShowTaskHereOffered = true;
                screen.WorkspaceRequestOffered = true;
                screen.CharacterColourOffered = true;
                screen.ShowScreenOffered = true;
            }
            // The host stands first: under a confirmation the file counts what is read by the panel's own lines, as the director has it do.
            var host = KitWorkspaceHost.Create(root);
            int? requestRows = null;
            // The second part's own rows, measured on their own: what the panel must read on part 2 (AUDIT3's 2026-11-16 BLOCK).
            string? partTwo = null;
            void Read(string request, bool instruction, int part)
            {
                var (words, mono) = RoomBar.Reading(FileScreens.RequestLine(steering, request), workspace, instruction);
                screen.ReadRequest(request, host.Bar.Lines(words, mono), part, steering);
                screen.RequestDrawn(0, steering, DateTimeOffset.UtcNow);
                requestRows = screen.RequestRows;
            }
            if (state is "33-ask-confirm-arriving" or "33-ask-confirm" or "33-ask-long-part" or "33-ask-long-part-2" or "33-ask-tool-confirm")
            {
                // Approve pressed, as the person presses it: the file measures the request, shows its first part, and that part is drawn.
                if (steering.Press(WorkspaceAction.Approve, workspace).Step != SteeringStep.Confirm) shot.Failures.Add("Approve did not ask for its confirmation");
                Read(steering.ToRead(workspace)!, instruction: false, RoomBar.RequestLines);
                if (state is "33-ask-long-part" or "33-ask-long-part-2" && screen.RequestParts < 2) shot.Failures.Add("the long request is one part of " + screen.RequestRows + " lines");
                if (state == "33-ask-long-part-2")
                {
                    // On to the second part, as Next Part takes the person: its rows are the ones the panel must show, none of the first.
                    // A minute on: the first part drawn and stood past its turn guard, as a person who read it presses Next Part.
                    screen.NextRequestPart(steering, DateTimeOffset.UtcNow.AddMinutes(1));
                    var (partWords, partMono) = RoomBar.Reading(FileScreens.RequestLine(steering, steering.ToRead(workspace)!), workspace, false);
                    partTwo = LabelText.FromTextMeshPro(host.Bar.PartRows(partWords, partMono, screen.RequestPart * screen.RequestPartRows, screen.RequestPartRows));
                    if (screen.RequestPart != 1) shot.Failures.Add("Next Part did not turn the request to its second part");
                }
            }
            if (questioning)
            {
                // The question laid as the file lays it while the redesign draws it: every word whole, four choices a page; then
                // read, answered and turned as the person does it, each page drawn before its press.
                var question = workspace.QuestionToAnswer!;
                var draft = new QuestionDraft(workspace.Execution!.ExecutionId, question);
                var ones = question.Prompts.Select(prompt => new PromptMeasure(1, prompt.Options.Select(_ => 1).ToList(), prompt.Options.Select(_ => 1).ToList())).ToList();
                var page = new ChoiceBudget(RoomBar.ChoicesAPage);
                var at = DateTimeOffset.UtcNow;
                void Lay()
                {
                    screen.ReadQuestion(draft, ones, page, page);
                    screen.Question.MeasureReview(question.Prompts.Select(_ => 1).ToList());
                    if (screen.Question.Writing) screen.Question.MeasureInput(1);
                    if (screen.Question.ExplainingAmbiguity) screen.Question.MeasureAmbiguity(1);
                }
                void Seen()
                {
                    Lay();
                    FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
                    screen.Question.Drawn(at);
                    at += TimeSpan.FromSeconds(1);
                }
                Lay();
                if (state is "34-question-review" or "34-question-several" or "34-question-confirm" or "34-question-sending")
                {
                    Seen();
                    screen.Question.Choose(1);
                    Seen();
                    screen.Question.NextQuestion(at);
                    Seen();
                }
                if (state == "34-question-several")
                {
                    screen.Question.Choose(1);
                    Seen();
                }
                if (state == "34-question-writing")
                {
                    // Write Answer pressed, the words typed and the page drawn: Use Answer has arrived.
                    Seen();
                    if (!screen.Question.BeginInput()) shot.Failures.Add("the question takes no written answer");
                    screen.Question.ChangeInput(QuestionWritten);
                    Seen();
                }
                if (state == "34-question-alike")
                {
                    Seen();
                    screen.Question.ExplainAmbiguity(0);
                    if (!screen.Question.ExplainingAmbiguity) shot.Failures.Add("the page of answers that read alike did not open");
                    Seen();
                }
                if (state == "34-question-confirm" && steering.SendAnswer(screen, workspace).Step != SteeringStep.Confirm) shot.Failures.Add("Send Answer did not ask for its confirmation");
                if (state == "34-question-sending")
                {
                    var going = steering.SendAnswer(screen, workspace);
                    if (going.Step != SteeringStep.Send) shot.Failures.Add("Send Answer did not send: " + going.Step + " " + going.Message);
                    else
                    {
                        var submissions = new CommandSubmissions();
                        _ = submissions.SubmitAsync(_ => new System.Threading.Tasks.TaskCompletionSource<CommandAckMessage>().Task, going.Command!, workspace.Execution.ExecutionId);
                        workspace = SelectedWorkspace(waiting: true, asked: question, submissions: submissions);
                    }
                }
            }
            if (state is "35-choose" or "35-choose-chosen")
            {
                // Tell it pressed where no keyboard is offered: the product's own instructions, each a place of the page's four.
                screen.Presets = WorkspaceText.PresetInstructions;
                screen.MeasurePresets(WorkspaceText.PresetInstructions.Select(_ => 1).ToList(), new ChoiceBudget(RoomBar.ChoicesAPage));
                if (state == "35-choose-chosen") screen.ChoosePreset(1);
            }
            if (state == "33-ask-sending")
            {
                // The answer went and no acknowledgement came back yet: it may still take effect.
                var submissions = new CommandSubmissions();
                _ = submissions.SubmitAsync(_ => new System.Threading.Tasks.TaskCompletionSource<CommandAckMessage>().Task,
                    factory.RespondToApproval(workspace.Execution!.ExecutionId, workspace.ApprovalToAnswer!.ApprovalId, ApprovalDecision.Approve), workspace.Execution.ExecutionId);
                workspace = SelectedWorkspace(waiting: true, command: AskCommand, submissions: submissions);
            }
            if (state == "31-bar-typing") screen.Instruction.Restore(workspace.Execution!.ExecutionId, "", false, 0);
            if (state == "31-bar-typed")
            {
                // Typed, and drawn once so it counts as read: Send Instruction has arrived.
                screen.Instruction.Restore(workspace.Execution!.ExecutionId, BarDraft, false, 0);
                screen.Instruction.Drawn(DateTimeOffset.UtcNow);
            }
            if (state == "31-bar-heard")
            {
                // Heard, and drawn once so it counts as read: Send Instruction stands.
                screen.Instruction.Restore(workspace.Execution!.ExecutionId, BarDraft, true, 0);
                screen.Instruction.Drawn(DateTimeOffset.UtcNow);
            }
            // What the confirmation's panel must read: the armed words, or the part of them the file shows first.
            string? armedWords = null;
            if (state is "31-bar-confirm" or "31-bar-confirm-part" or "31-bar-confirm-escapes" or "31-bar-confirm-part-escapes")
            {
                var heard = state switch
                {
                    "31-bar-confirm" => BarConfirmPlain,
                    "31-bar-confirm-part" => BarLongPlain,
                    "31-bar-confirm-escapes" => BarConfirmWords,
                    _ => BarLongWords,
                };
                if (steering.Spoken(heard, workspace).Step != SteeringStep.Confirm) shot.Failures.Add("the heard instruction did not arm its confirmation");
                var request = steering.ToRead(workspace)!;
                // A part is eight of the panel's own lines, as the director gives the file while the redesign draws it.
                Read(request, instruction: true, RoomBar.RequestLines);
                if (state is "31-bar-confirm-part" or "31-bar-confirm-part-escapes" && screen.RequestParts < 2) shot.Failures.Add("the long instruction is one part of " + screen.RequestRows + " lines");
                armedWords = request;
            }
            // A question's page laid as the redesign lays it, as the headset always does: each answer's label and description apart
            // (FileScreens.AnswerRow, KitControl.Under), never the older page's words joined on a dot (SCREENS, FIELD's 03:32Z Check).
            var designBefore = RoomDesign.Enabled;
            // More's page reads the routes from the page the kit binds, which the redesign stands first (2c2a6f938): built as the
            // product builds it, with the redesign on, so the page holds every route the file offers.
            if (questioning || state == "37-more") RoomDesign.Enabled = true;
            MenuFrame frame;
            try { frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4)); }
            finally { RoomDesign.Enabled = designBefore; }
            var binding = KitFileBinding.Of(frame, steering.Armed, workspace, screen.Instruction.Open ? screen.Instruction.Words : "", new ConnectionStatus(ConnectionPhase.Live),
                SelectedProject, ProductionSpine(workspace), new KitRowModel("workspace", LineWords.Ours("Move Workspace"), GlazeIcon.Move), roomDesign: true,
                draftSpoken: screen.Instruction.Open && screen.Instruction.Spoken, requestRows: requestRows, chooses: choosing,
                asking: screen.Question.Draft != null && !screen.Question.Reviewing ? screen.Question.Prompt : (int?)null,
                writing: screen.Question.Writing ? screen.Question.Input : null, result: ResultFor(state),
                selectors: state is "38-result-agent" or "31-bar-rest-agent" ? new KitSelectors("This Mac", false, "Synthetic Agent", agentChangeable: true, offersNeverAsk: true, neverAsk: true) : null,
                steps: state == "36-work-steps" ? StepsOf("46-steps-working") : null,
                stepsUsage: state == "36-work-steps" ? WorkStepsUsage : null);
            // As the director does: a frame the task's binding has no place for is carried plain where a reader of the redesign takes it.
            if (binding == null && state == "37-page" && RoomBar.OfFrame(frame, VoiceStage.Idle) != null) binding = KitFileBinding.PlainOf(frame);
            if (binding == null)
            {
                shot.Failures.Add("the kit has no binding to this frame");
                return shot;
            }
            host.Accepting = () => true;
            host.WatchOpen = state == "36-work-watch";
            host.WatchEyes = state == "36-work-watch" ? () => eyes : null;
            host.Voice = () => voice;
            host.AwaitingStart = () => sent;
            var recorded = state == "31-bar-listening" ? RecordedLevel() : null;
            host.MicLevel = () => recorded;
            var acted = new List<string>();
            host.Acted += (_, action, _, _, _) => acted.Add(action);
            var drawnFrames = 0;
            host.Drawn += (_, _) => drawnFrames++;
            var was = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            try
            {
                Require(host.Holds(binding), "the host does not hold the frame");
                host.Show(binding, MenuColumn.File, new object());
                // A question's frame is reported drawn as a frame would report it, with the redesign still on: not while the band holds an unread part.
                if (questioning || ResultStates.Contains(state)) host.DrawnForRender();
                else host.SettleForRender();
                if (state == "36-work-watch")
                {
                    // The turn's files arrive one a second, as the director lays them from its reads (FIELD's 47-build-three), each move settled.
                    var titleWas = RoomWritingArc.ListTitle;
                    RoomWritingArc.ListTitle = _ => BuildListWords;
                    try
                    {
                        var clock = 100f;
                        host.ShowWatch(BuildArcOf(1));
                        if (host.WatchArc != null) host.WatchArc.Clock = () => clock;
                        for (var files = 1; files <= 3; files++)
                        {
                            host.ShowWatch(BuildArcOf(files));
                            clock += 1f;
                            host.WatchArc?.Settle();
                        }
                        clock += 5f;
                        host.ShowWatch(BuildArcOf(3));
                    }
                    finally { RoomWritingArc.ListTitle = titleWas; }
                    host.SettleForRender();
                }
            }
            finally { RoomDesign.Enabled = was; }
            var bar = host.Bar;
            if (state is "38-checks" or "38-checks-failed" or "38-checks-partly")
            {
                // Each check standing as a row: its name's ink on its node's middle, read in the eye's pixels (AUDIT's 10:51Z MUST: the
                // names stood 0.35 of a tick low). No font metrics: the drawn letters against the drawn node.
                var stepCard = bar.Card;
                var previousEye = shot.AfterEye;
                shot.AfterEye = (camera, eye, failures) =>
                {
                    previousEye?.Invoke(camera, eye, failures);
                    if (stepCard == null) return;
                    int Luma(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                    foreach (var (node, what) in stepCard.Plates.Where(each => each.What.StartsWith("step ", StringComparison.Ordinal)))
                    {
                        var stepName = what.Substring("step ".Length);
                        var label = stepCard.Shown.FirstOrDefault(each => each.gameObject.activeInHierarchy && each.GetParsedText().Trim() == stepName);
                        if (label == null || label.textInfo.lineCount != 1) continue;
                        // Only where the steps stand as rows: the name beside its node, not under it.
                        if (label.transform.localPosition.x < node.transform.localPosition.x + node.Size.x / 2f) continue;
                        var nodeScreen = camera.WorldToScreenPoint(node.transform.position);
                        var oneDp = camera.WorldToScreenPoint(node.transform.parent.TransformPoint(node.transform.localPosition + new Vector3(0f, RoomType.Units(1f), 0f))).y - nodeScreen.y;
                        var bounds = label.textBounds;
                        var low = camera.WorldToScreenPoint(label.transform.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, 0f)));
                        var high = camera.WorldToScreenPoint(label.transform.TransformPoint(new Vector3(bounds.max.x, bounds.max.y, 0f)));
                        // The node's own band and a tenth beyond (0.6 of its height each way), so a neighbouring row's letters never count.
                        var band = 0.6f * Mathf.Abs(camera.WorldToScreenPoint(node.transform.parent.TransformPoint(node.transform.localPosition + new Vector3(0f, node.Size.y, 0f))).y - nodeScreen.y);
                        double sum = 0, count = 0;
                        for (var x = Mathf.FloorToInt(low.x); x <= Mathf.CeilToInt(high.x); x += 2)
                        for (var y = Mathf.FloorToInt(nodeScreen.y - band); y <= Mathf.CeilToInt(nodeScreen.y + band); y++)
                        {
                            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                            if (Luma(eye[y * Width + x]) > 150) { sum += y; count++; }
                        }
                        if (count < 10 || Mathf.Abs(oneDp) < 1e-4f) continue;
                        var inkDp = (float)((sum / count - nodeScreen.y) / oneDp);
                        Debug.Log("Halcyonic: kit: " + state + ": " + stepName + "'s ink stands " + inkDp.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dp from its node's middle, read in the pixels.");
                        if (Mathf.Abs(inkDp) > 1.5f) failures.Add(state + ": " + stepName + "'s letters stand " + inkDp.ToString("0.0") + " dp off its node's middle, read in the pixels");
                    }
                };
            }
            if (state == "37-more")
            {
                // More pressed at rest, as a person presses it (SCREENS's SHAPE 11): the task's page opens in the panel's place.
                var moreButton = bar.GetComponentsInChildren<RoomButton>(false).FirstOrDefault(button => bar.ControlOf(button)?.Action == RoomBar.More);
                Require(moreButton != null, "More does not stand at rest where the file offers " + RoomBar.MoreRoutes(binding).Count + " routes");
                if (moreButton != null)
                {
                    var designWas = RoomDesign.Enabled;
                    RoomDesign.Enabled = true;
                    try
                    {
                        Require(bar.ControlOf(moreButton) is KitControl moreControl && moreControl.Words == RoomBar.MoreWords && moreButton.Glyph.Glyph == RoomGlyph.More,
                            "More does not read More with its three dots");
                        // Pressed once the bar has settled, as a person's press counts only then.
                        host.SettleForRender();
                        moreButton.Press.PressForRender(480);
                        host.SettleForRender();
                    }
                    finally { RoomDesign.Enabled = designWas; }
                }
            }
            // Confirm's lock: on its arrival the ring is part filled and it takes no press; 0.7 s on it is the main action.
            if (state == "33-ask-confirm-arriving") bar.AgeLockForRender(0.42f * RoomBar.ConfirmLockSeconds);
            if (state is "33-ask-confirm" or "34-question-confirm" or "33-ask-tool-confirm" or "36-stop-confirm" or "38-add-last")
            {
                // The lock lifted is a new action, which waits as any other before it takes a press: settled again for the render.
                bar.AgeLockForRender(RoomBar.ConfirmLockSeconds);
                host.SettleForRender();
            }
            const float Tolerance = 1e-4f;
            // FIELD's card in the panel, every one of its labels laid whole.
            void CardWhole()
            {
                Require(host.Bar.Card != null && host.Bar.Card.gameObject.activeSelf, "the card does not stand in the panel");
                if (host.Bar.Card == null) return;
                foreach (var label in host.Bar.Card.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                {
                    label.ForceMeshUpdate(true);
                    Require(!label.isTextTruncated, "the card's \"" + label.text + "\" is cut");
                }
            }
            var typeSize = GlazeTokens.FontSize(RoomType.Scaled(RoomGeometry.TypeDp));
            void Whole(TMPro.TextMeshPro words, string what)
            {
                if (!words.gameObject.activeInHierarchy) return;
                words.ForceMeshUpdate(true);
                Require(!words.isTextTruncated, what + " \"" + words.text + "\" is cut");
                Require(Mathf.Approximately(words.fontSize, typeSize), what + " is not in the one type size");
            }

            // The level is the microphone's: each bar the level of its 50 ms, and none where nothing records one.
            Require(bar.LevelShown == (recorded != null), recorded != null ? "no level stands while the microphone records" : "a level stands where nothing records one");
            if (recorded != null)
            {
                for (var index = 0; index < LevelHistory.Bars; index++)
                    Require(Mathf.Abs(bar.LevelBar(index) - (0.2f + 0.8f * recorded[index])) < 1e-4f, "level bar " + index + " does not follow what was recorded");
                // Inside Listening's halo, after its words.
                var core = bar.Talk.Core;
                var talkX = bar.Talk.transform.localPosition.x;
                Require(bar.Talk.WordsEnd + talkX < bar.transform.Find("Level").localPosition.x && talkX + core.x / 2f >= bar.transform.Find("Level").localPosition.x + RoomType.Units(RoomBarView.LevelWidthDp) - Tolerance,
                    "the level does not stand after Listening's words inside its halo");
            }
            Require(bar.gameObject.activeSelf && bar.Shown?.State == expected, "it shows " + (bar.Shown?.State.ToString() ?? "nothing") + ", not " + expected);
            // No word alone on the band's last line where its words run four or more (AUDIT's 11:18Z MUST: "file." alone at the
            // larger size under the result).
            if (host.Band.Words != null && host.Band.Label.gameObject.activeInHierarchy)
            {
                var bandLabel = host.Band.Label;
                bandLabel.ForceMeshUpdate(true);
                var bandWords = bandLabel.GetParsedText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                Require(!(bandWords >= 4 && bandLabel.textInfo.lineCount >= 2 && RoomCardView.LastLineIsOneWord(bandLabel)),
                    "the band's last line holds one word alone: \"" + bandLabel.GetParsedText() + "\"");
            }
            // Nor on the last line of the card's title or the panel's words where they run four words or more, the person's words
            // included (AUDIT 2026-11-16T15:23Z, "points" alone under the result's title; 16:57Z, "chosen" alone in the panel).
            if (bar.Shown is RoomBar lonely)
            {
                var lonelyWords = new[] { lonely.Card?.Title?.Text, lonely.Mono ? null : lonely.Body?.Text }.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
                foreach (var label in bar.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                {
                    label.ForceMeshUpdate(true);
                    var read = string.Join(" ", label.GetParsedText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    if (!lonelyWords.Contains(read) || read.Split(' ').Length < 4 || label.textInfo.lineCount < 2) continue;
                    Require(!RoomCardView.LastLineIsOneWord(label), "the words \"" + read + "\" leave one word alone on their last line");
                }
            }
            if (state == "37-more" && bar.Shown is RoomBar morePage)
            {
                // Its title the task's name; a row per route the file offers, in its order, each with its chevron; Back last; never the results line.
                var routes = RoomBar.MoreRoutes(binding);
                Require(morePage.Body.Text == binding.Model.TaskTitle, "the More page's title is \"" + morePage.Body.Text + "\", not the task's name");
                // Drawn as the page's title, on its card (AUDIT's 10:39Z: the page stood with no title), its top level with the first
                // route's (AUDIT's 11:09Z: it stood centred on the column).
                Require(bar.Card != null && bar.Card.Shown.Any(label => label.GetParsedText() == binding.Model.TaskTitle),
                    "the More page draws no title card with the task's name");
                var standing = bar.Column.Where(button => button.gameObject.activeSelf).ToList();
                if (standing.Count > 0)
                {
                    var panelTop = bar.Panel.transform.localPosition.y + bar.Panel.Size.y / 2f;
                    var firstTop = standing[0].transform.localPosition.y + standing[0].Size.y / 2f;
                    Require(Mathf.Abs(panelTop - firstTop) < RoomType.Units(1f), "the More page's title card stands "
                        + ((firstTop - panelTop) / RoomType.Units(1f)).ToString("0.0") + " dp under its first route's top");
                    // The card hugs its words (AUDIT's 11:57Z: grown to the column it stood an empty slab): no run of empty height
                    // over a line and a half under its last drawn word, beyond the panel's own padding.
                    if (bar.Card != null && bar.Card.Shown.Count > 0)
                    {
                        var lowestWord = bar.Card.Shown.Min(label => bar.transform.InverseTransformPoint(label.bounds.min).y);
                        var panelBottom = bar.Panel.transform.localPosition.y - bar.Panel.Size.y / 2f;
                        var slack = lowestWord - panelBottom - RoomType.Scaled(RoomGeometry.PanelPadDp);
                        Require(slack <= 1.5f * RoomType.Scaled(RoomGeometry.LineDp), "the More page's card stands "
                            + (slack / RoomType.Units(1f)).ToString("0") + " dp empty under its words");
                    }
                }
                Require(standing.Count == routes.Count + 1, "the More page stands " + standing.Count + " rows, not " + routes.Count + " routes and Back");
                // Every route the file offers here, the long ones too, whole at both sizes (AUDIT's 10:53Z).
                Require(routes.Count == 4, "the More page lists " + routes.Count + " of the four routes the file offers");
                foreach (var button in standing)
                {
                    button.Words.ForceMeshUpdate(true);
                    Require(!button.Words.isTextTruncated, "the More page's \"" + button.Words.text + "\" is cut");
                }
                for (var index = 0; index < standing.Count && index < routes.Count; index++)
                    Require(bar.ControlOf(standing[index])?.Action == routes[index].Action && standing[index].Glyph.Glyph == RoomGlyph.Opens,
                        "the More page's row " + index + " is not the route " + routes[index].Words + " with its chevron");
                Require(standing.Count > 0 && bar.ControlOf(standing[standing.Count - 1])?.Action == RoomBar.MoreBack && bar.ControlOf(standing[standing.Count - 1])?.Words == RoomBar.BackWords,
                    "the More page does not end with Back");
                Require(standing.All(button => bar.ControlOf(button)?.Action != FileScreens.OpenResults), "the More page offers the results line");
                Debug.Log("Halcyonic: kit: " + state + ": More opened the task's page: " + string.Join(", ", standing.Select(button => bar.ControlOf(button)?.Words)) + ".");
            }
            // The old kit is not drawn at all beside the redesign (the owner, 2026-11-05).
            Require(!host.View.gameObject.activeInHierarchy, "the old kit still draws beside the redesign");
            // The agent's words on the band exactly as written, every stop kept (AUDIT's 11:09Z, AUDIT2's 11:10Z: WORDS.md rule 8):
            // the band's words, joined and their spaces read as one, equal the reported message after LabelText.Plain.
            if (!questioning && !asking && bar.Shown?.Band != null && host.Band.Text.Length > 0 && KitPresenter.ReportOf(workspace.Activity) is ActivityEntry reported
                && bar.Shown.Band == reported.Text)
            {
                static string Spaced(string text) => System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
                Require(Spaced(LabelText.FromTextMeshPro(host.Band.Text)) == Spaced(LabelText.Plain(reported.Text)),
                    "the band reads \"" + host.Band.Text + "\", not the agent's words as written");
            }
            var actions = new List<RoomButton>();
            if (bar.Shown is RoomBar shown)
            {
                if (shown.Row.Count > 0)
                {
                    // The row: home-size actions in its order, the main one first, its core the hand target itself, 32 dp apart.
                    // The row's third and fourth places take the actions after Hold to Talk and Type, in order (More, then Projects).
                    var others = 0;
                    var row = shown.Row.Select(action => action.Place == KitPlace.Talk ? bar.Talk : action.Place == KitPlace.Field ? bar.Type
                        : others < bar.RowOthers.Count ? bar.RowOthers[others++] : null).Where(button => button != null).Select(button => button!).ToList();
                    Require(row.Count == shown.Row.Count, "the row draws " + row.Count + " of its " + shown.Row.Count + " actions");
                    if (state == "31-bar-rest-agent")
                    {
                        Debug.Log("Halcyonic: spatial kit: " + state + " row: " + string.Join(", ", shown.Row.Select(action => action.Words)));
                        // The synthetic file offers no route behind More and no way back at rest, so the row is Hold to Talk, Type and the
                        // agent; every action past Type still stands in a place of its own (the checks above), whatever their count.
                        Require(shown.Row.Any(action => action.Control.Action == AgentColumn.Open) && row.Count == shown.Row.Count,
                            "the row at rest does not hold the agent in a place of its own");
                    }
                    actions.AddRange(row);
                    Require(row.All(button => button.gameObject.activeSelf), "a row action is not drawn");
                    Require(!bar.Type.gameObject.activeSelf || shown.Row.Any(action => action.Place == KitPlace.Field), "Type stands while it listens");
                    for (var other = 0; other < bar.RowOthers.Count; other++)
                        Require(bar.RowOthers[other].gameObject.activeSelf == other < others,
                            "the row's action " + (other + 3) + " stands where the model gives none, or does not where it gives one");
                    // Each action in a place of its own (SCREENS's 13:41Z (a): a fifth once drew into More's place): no two drawn boxes meet, and each raises its own control.
                    for (var index = 0; index < row.Count; index++)
                        for (var next = index + 1; next < row.Count; next++)
                            Require(Mathf.Abs(row[index].transform.localPosition.x - row[next].transform.localPosition.x) >= (row[index].Core.x + row[next].Core.x) / 2f - Tolerance,
                                row[index].name + " and " + row[next].name + " are drawn in one place");
                    for (var index = 0; index < row.Count; index++)
                        if (row[index] != bar.Talk) Require(bar.ControlOf(row[index]) == shown.Row[index].Control, shown.Row[index].Words + " does not raise its own action");
                    for (var index = 0; index < row.Count; index++)
                    {
                        Require(row[index].LookNow() == (shown.Row[index].Main ? RoomButtonLook.Main : RoomButtonLook.Rest), row[index].name + " shows " + row[index].LookNow());
                        // Hold to Talk and Type are home size; a third action is action size, its target still Meta's.
                        if (!bar.RowOthers.Contains(row[index])) Require(Mathf.Abs(row[index].Core.y - RoomType.Units(RoomButton.HeightDp)) < Tolerance, row[index].name + "'s core is not the hand target");
                        Whole(row[index].Words, row[index].name + "'s words");
                        Require(row[index].Words.text == LabelText.ForTextMeshPro(shown.Row[index].Words), row[index].name + " reads \"" + row[index].Words.text + "\"");
                        // Where no keyboard is offered, the action that would open it says what it opens instead.
                        if (shown.Row[index].Place == KitPlace.Field) Require(shown.Row[index].Words == (choosing ? RoomBar.ChooseInstruction : RoomBar.Type), "the row's second action reads \"" + shown.Row[index].Words + "\"");
                    }
                    for (var index = 1; index < row.Count; index++)
                    {
                        var gap = (row[index].transform.localPosition.x - row[index].Core.x / 2f) - (row[index - 1].transform.localPosition.x + row[index - 1].Core.x / 2f);
                        Require(Mathf.Abs(gap - RoomType.Units(RoomGeometry.RowGapDp)) < Tolerance, "the row's actions stand " + (gap / RoomType.Units(1f)).ToString("0.0") + " dp apart, not 32");
                    }
                }
                // The working field and the agent's cursor stand only at work; Stop's confirmation and every other moment have neither.
                var working = state is "36-work" or "36-work-row" or "36-work-recorded" or "36-work-fixing" or "36-work-watch" or "36-work-steps";
                // The steps list holds the panel's place at work, as Watch It Work's arc does: the field and the cursor give it up.
                var listing = state == "36-work-steps";
                // Watch It Work's arc holds the panel's place: the field and the cursor give it up.
                var watching = state == "36-work-watch";
                // No frame leaves the plane a part of no height (JUDGE's walk 9): the host's footprint is never empty.
                Require(host.Size.x > 0f && host.Size.y > 0f, "the host stands " + host.Size.x + " by " + host.Size.y + ": a part of no size");
                Require(shown.Working == working, working ? "the agent works and the redesign does not say so" : "at work is shown where nothing works");
                var fielded = working && !watching && !listing;
                Require((host.Field.gameObject.activeSelf && host.Field.Shown > 0.001f) == fielded && (host.Field.Shown > 0.999f) == fielded,
                    watching ? "the working field stands under Watch It Work's arc" : listing ? "the working field stands under the steps list"
                    : working ? "the working field does not stand" : "the working field stands where nothing works");
                if (listing)
                {
                    var list = bar.StepsShown;
                    Require(list != null && list.Count == StepsOf("46-steps-working").Count && bar.Panel.gameObject.activeSelf,
                        "the steps list does not stand on the panel at work: " + (list == null ? "none" : list.Count + " steps"));
                    if (list != null)
                    {
                        // What stands in the field's place (AUDIT's 11:05Z): every step's word and line whole, and the usage figure
                        // under the last step where the agent app reports it, its label and value apart, all inside the panel.
                        for (var index = 0; index < list.Count; index++)
                        {
                            var word = list.Word(index);
                            word.ForceMeshUpdate(true);
                            Require(!word.isTextTruncated && word.text.Length > 0, "the steps list's word " + index + " \"" + word.text + "\" is cut or empty");
                            if (list.Line(index) is TMPro.TextMeshPro line)
                            {
                                line.ForceMeshUpdate(true);
                                Require(!line.isTextTruncated, "the steps list's line " + index + " \"" + line.text + "\" is cut");
                            }
                        }
                        // Nothing but the list on its panel: no caret, no words of the pair's, no card, and the agent's cursor gone
                        // (AUDIT's 12:19Z: a caret stood inside "index.html", and the cursor in the gap at Larger).
                        var onPanel = new[] { "Words", "Request", "Sentence", "Caret", "Card" }
                            .Where(part => bar.Panel.transform.Find(part) is Transform drawn && drawn.gameObject.activeSelf).ToList();
                        Require(onPanel.Count == 0, "the steps' panel also draws " + string.Join(", ", onPanel));
                        Require(!host.Cursor.gameObject.activeSelf || host.Cursor.Words.Length == 0 && host.Cursor.Shown < 0.001f, "the agent's cursor is drawn over the steps list");
                        Require(list.UsageLabel != null && list.UsageValue != null && list.UsageLabel.text == WorkStepsUsage.Label
                            && list.UsageValue.text == WorkStepsUsage.Value, "the usage figure does not stand under the steps, label and value apart");
                        var listTop = bar.Panel.transform.localPosition.y + bar.Panel.Size.y / 2f;
                        foreach (var part in list.GetComponentsInChildren<Renderer>(false))
                        {
                            if (!part.enabled) continue;
                            var at = bar.transform.InverseTransformPoint(part.bounds.min).y;
                            Require(at >= bar.Panel.transform.localPosition.y - bar.Panel.Size.y / 2f - Tolerance, part.name + " stands under the steps' panel");
                            Require(bar.transform.InverseTransformPoint(part.bounds.max).y <= listTop + Tolerance, part.name + " stands over the steps' panel");
                        }
                        // On the panel, inside it, beside the column: the panel's foot on the foot line.
                        var panelFoot = bar.Panel.transform.localPosition.y - bar.Panel.Size.y / 2f;
                        Require(Mathf.Abs(panelFoot - bar.Foot) < Tolerance, "the list's panel does not stand on the foot line");
                        Require(bar.Panel.transform.localPosition.x + bar.Panel.Size.x / 2f <= bar.PanelRight + Tolerance, "the list's panel reaches past the panel's place");
                    }
                }
                Require(host.Cursor.Words == (state == "36-work" ? RoomWork.Editing + " score.js" : ""), "the cursor reads \"" + host.Cursor.Words + "\"");
                if (working)
                {
                    // No panel, save while a try at a failed check runs; the band says nothing; Stop alone where the column stands, a
                    // rest action, its core on the foot line.
                    if (state == "36-work-fixing")
                    {
                        Require(bar.Panel.gameObject.activeSelf && host.Band.Words == null, "the panel does not stand while a try runs, or the band does");
                        // The fixing card (A17): "Fixing" over its line, the first of two tries a filled pip under its mark, no sentence.
                        var card = bar.Card;
                        Require(card != null && card.Shown.Count >= 1 && card.Shown.Any(label => label.GetParsedText() == RoomBar.FixingTitle) && !bar.Sentence.gameObject.activeSelf,
                            "the panel is not the fixing card: \"" + (card == null ? "no card" : string.Join(" / ", card.Shown.Select(label => label.GetParsedText()))) + "\"");
                        if (card != null)
                            Require(card.Plates.Count(each => each.What == "pip used") == 1 && card.Plates.Count(each => each.What == "pip") == 1, "the card's pips are not the first of two tries");
                    }
                    else if (!listing) Require(!bar.Panel.gameObject.activeSelf && host.Band.Words == null, "a panel or the band stands while the agent works");
                    var stop = bar.Column[0];
                    if (watching) WatchChecks(state, host, shown, eyes, actions, Require, Tolerance);
                    else if (state == "36-work-recorded") Require(shown.Column.Count == 0 && shown.Row.Count == 0 && bar.Column.All(button => !button.gameObject.activeSelf), "an action stands where nothing can be pressed");
                    else
                    {
                    actions.Add(stop);
                    Require(bar.Column.Count(button => button.gameObject.activeSelf) == 1 && shown.Column.Count == 1 && shown.Column[0].Place == KitPlace.Stop
                        && stop.LookNow() == RoomButtonLook.Rest, "Stop does not stand alone as a rest action");
                    Whole(stop.Words, "Stop's words");
                    Require(Mathf.Abs(stop.transform.localPosition.y - stop.Core.y / 2f - bar.Foot) < Tolerance, "Stop's core does not stand on the foot line");
                    Require(Mathf.Abs(stop.transform.localPosition.x - stop.Core.x / 2f - (bar.PanelRight + RoomType.Units(RoomGeometry.PairGapDp))) < Tolerance, "Stop does not stand where the column stands");
                    }
                    var unit = RoomType.Units(1f);
                    if (!watching && !listing)
                    {
                    // The field's inner box: the panel's place, 12 degrees up from the foot line.
                    var zoom = GlazeText.Scale;
                    var middle = (host.Field.transform.localPosition - bar.transform.localPosition) * zoom;
                    var inner = new Vector2(RoomType.Units(RoomGeometry.PanelDp), RoomType.Units(RoomGeometry.FieldTallDp));
                    Require(Mathf.Abs(middle.x + inner.x / 2f - bar.PanelRight) < Tolerance && Mathf.Abs(middle.y - inner.y / 2f - bar.Foot) < Tolerance,
                        "the field's inner box does not end at the panel's place and stand on the foot line");
                    Require(Vector2.Distance(host.Field.Size, RoomField.Around(inner)) < Tolerance, "the field is " + (host.Field.Size / unit) + " dp, not the design's size round its box");
                    Require(host.Cursor.transform.localPosition == host.Field.transform.localPosition, "the cursor does not count its stops from the field's middle");
                    }
                    // The row stands only where the file still offers an instruction, its feet 94.4 dp under the foot line.
                    Require((shown.Row.Count > 0) == (state == "36-work-row"), state == "36-work-row" ? "the row does not stand though the agent takes an instruction" : "a row stands though nothing can be sent");
                    if (shown.Row.Count > 0)
                        Require(Mathf.Abs(bar.Foot - (bar.Talk.transform.localPosition.y - RoomType.Units(RoomButton.HeightDp) / 2f) - RoomType.Units(RoomBarView.RowFeetDp)) < Tolerance, "the row's feet are not 94.4 dp under the foot line");
                }
                if (ResultStates.Contains(state))
                {
                    var known = ResultFor(state)!;
                    Require(shown.Result && shown.Pair, "the result does not stand over the row");
                    // The row stands under the pair, its feet 94.4 dp under the foot line; the frame is drawn though the band may hold more.
                    Require(shown.Row.Count > 0 && Mathf.Abs(bar.Foot - (bar.Talk.transform.localPosition.y - RoomType.Units(RoomButton.HeightDp) / 2f) - RoomType.Units(RoomBarView.RowFeetDp)) < Tolerance,
                        "the row's feet are not 94.4 dp under the pair's foot line");
                    Require(drawnFrames == 1, "the result's frame was reported drawn " + drawnFrames + " times: a message on the band is not read before the row takes a press");
                    var message = state == "38-result-long" ? ResultSaysLong : ResultSays;
                    // Where the agent offers never ask the row names it, its level the action's chip beside it (A17), and the result still stands over it.
                    if (state == "38-result-agent")
                        Require(shown.Row.Any(action => action.Control.Action == AgentColumn.Open && action.Chip == AgentColumn.NeverAskWords) && bar.More.gameObject.activeSelf
                            && bar.More.Aside == AgentColumn.NeverAskWords,
                            "the agent and its level do not stand in the row beside the result");
                    if (known.Page == RoomResultPage.Result)
                    {
                        // FIELD's f6 (TASKS): the agent's own first sentence stands on the review card under THE AGENT SAYS, theirs, and the
                        // band does not say it again; where the result's card is how a run stands, the band holds the turn's words as before.
                        if (shown.Card?.Says is LineWords says)
                            Require(shown.Band == null && says.AllTheirs && says.Text == RoomBar.FirstSentence(message) && shown.AsDialog && shown.DialogTitle != null
                                && shown.DialogTitle.AllTheirs && shown.Card.Title == null,
                                "the review is not one dialog with the task's name over the agent's first sentence: " + says.Text);
                        else Require(shown.Band == message && host.Band.Words != null && host.Band.Text.Length > 0, "the agent's newest message does not stand on the band");
                        Require(bar.BandNextButton == null || shown.Card?.Says == null, "a way to a band's next part beside the review card");
                        // The review card (TASKS, take 3): the check's outcome its status, its one large line; its checks and the files as
                        // facts, each one press from the result; an ended chain of tries as its status; no sentence, nothing joined on a
                        // dot, every label of the card whole.
                        var stopped = state == "38-result-stopped";
                        var unclaimed = state is "38-result-checking" or "38-result-simulated" or "38-result-unchecked" or "38-result-starter";
                        // The outcome in the Checks page's own word, said once, as the status (AUDIT2 10:05Z W1, W2); the checks as how many passed (AUDIT2 23:51Z).
                        var all = RoomPanelCards.AllPassing;
                        var slots = state == "38-project" ? new[] { all } : stopped ? new[] { RoomPanelCards.ChecksCount(1, 4), "4 Files" }
                            : state == "38-result-add" ? new[] { all, "10 Files" } : state == "38-result-review" ? new[] { all, "3 Files" }
                            : unclaimed ? new[] { "4 Files" } : new[] { all, "4 Files" };
                        var statusTitle = stopped ? RoomBar.StoppedTitle : state == "38-result-checking" ? TaskSteps.Checking : state == "38-result-simulated" ? SimulatedChecks.Title
                            : state == "38-result-starter" ? StarterChecks.Title : null;
                        // FIELD's f6: each fact labelled (CHECKS, CHANGES) and one press to its page; the outcome its own line, the count under it.
                        if (!unclaimed)
                            Require(shown.Card?.Slots.Count > 0 && shown.Card.Slots.All(slot => slot.Press != null && slot.Label != null), "a fact on the review card is not labelled, or not one press to its page");
                        if (!unclaimed && !stopped)
                            Require(shown.Card!.Slots[0].Line?.Text == RoomPanelCards.ChecksCount(4, 4), "the checks fact's line under All Passing reads " + shown.Card.Slots[0].Line?.Text);
                        Require(shown.Card != null && !shown.Card.AnyDot && shown.Card.Slots.Select(slot => slot.Value.Text).SequenceEqual(slots) && shown.Sentence == null
                            && (shown.Card.Status?.Title == statusTitle) && (state != "38-result-simulated" || shown.Card.Status?.Line == SimulatedChecks.Line)
                            && (state != "38-result-starter" || shown.Card.Status?.Line == StarterChecks.Line),
                            "the result's card reads " + string.Join(", ", shown.Card?.Slots.Select(slot => slot.Value.Text) ?? new string[0]) + " under " + shown.Card?.Status?.Title);
                        CardWhole();
                        // DEVICE's Play in Headset: a pressable action where it stands, nothing where it cannot act.
                        if (state is "38-result-play" or "38-result-play-refused" or "38-result-play-not-in-build")
                        {
                            var at = shown.Column.ToList().FindIndex(action => action.Control.Action == RoomResult.PlayInHeadset);
                            if (state != "38-result-play")
                            {
                                Require(at < 0, "Play in Headset stands where it cannot act");
                                // Its reason stands at the card's foot instead, whole, Halcyonic's own sentence.
                                var why = HeadsetPlay.RefusalLine(state == "38-result-play-refused" ? PlayInHeadset.NotConfined : PlayInHeadset.NotInThisBuild);
                                Require(shown.Card?.More?.Text == why, "the result's card does not say why Play in Headset is not here: " + shown.Card?.More?.Text);
                            }
                            else
                            {
                                Require(at >= 0 && shown.Column[at].Words == HeadsetPlay.ActionWords && bar.Column[at].gameObject.activeSelf && bar.Column[at].Available,
                                    "Play in Headset does not stand as a pressable action");
                                if (at >= 0) Whole(bar.Column[at].Words, "Play in Headset's words");
                            }
                        }
                        if (stopped) Require(shown.Column[0].Place == KitPlace.Field && shown.Column[0].Main && shown.Column.Count(action => action.Main) == 1, "Write Instruction does not lead where the tries ran out");
                        else if (state == "38-result-review") Require(shown.Column[0].Control.Action == RoomResult.ShowAdd && shown.Column[0].Main && shown.Column.Count(action => action.Main) == 1
                            && shown.Column.Any(action => action.Control.Action == RoomResult.TryBuild && !action.Main), "Add to Project does not lead the review card with Try Build quiet beside it");
                        else Require(shown.Column[0].Control.Action == RoomResult.MakeVrVersion && shown.Column[0].Main, "Make VR Copy does not lead");
                        if (state == "38-result-add") Require(shown.Column.Any(action => action.Control.Action == RoomResult.ShowAdd && !action.Main), "Add to Project does not stand where the task holds work to add");
                        if (state == "38-result-full")
                        {
                            var drawnColumn = bar.Column.Where(button => button.gameObject.activeSelf).ToList();
                            Require(shown.Column[shown.Column.Count - 1].Control.Place == KitPlace.Close
                                && drawnColumn.Count >= shown.Column.Count && bar.ControlOf(drawnColumn[shown.Column.Count - 1]) == shown.Column[shown.Column.Count - 1].Control,
                                "a full result does not draw Close last in its column: " + string.Join(", ", shown.Column.Select(action => action.Words)));
                            Require(shown.Column.Any(action => action.Control.Action == RoomResult.ShowShare) && shown.Overflow.Count == 0,
                                "a full result's acts do not stand in its column with its facts on the card");
                        }
                    }
                    else
                    {
                        // Each page is FIELD's card (A17): Changes a list five a page, Checks the steps as nodes, A13's review and
                        // the files changed in both; no band, no sentence, nothing joined on a dot, every label of the card whole.
                        Require(shown.Band == null && host.Band.Words == null && shown.Sentence == null, "a band or a sentence stands on a page");
                        Require(shown.Card != null && !shown.Card.AnyDot && shown.Facts == null, "the page is not a card, or joins facts on a dot");
                        if (state is "38-changes" or "38-changes-long")
                            Require(shown.Card!.List.Count == (state == "38-changes-long" ? RoomCard.ListAPage : known.Changes.Count), "the Changes card lists " + shown.Card.List.Count + " files");
                        // A wrapped path breaks only after a slash or a dot (or, a piece no line holds at the floor, at its finer
                        // places), never inside a name (SCREENS's PathWrap; JUDGE's "welcome.uikitm" / "l"; AUDIT2's "assets/golden-").
                        if (state is "38-changes" or "38-changes-long")
                            foreach (var label in host.Bar.Card!.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                            {
                                label.ForceMeshUpdate(true);
                                var info = label.textInfo;
                                if (info.lineCount < 2 || !shown.Card.List.Any(row => row.Name.AllTheirs && row.Name.Text.Contains('/') && label.GetParsedText().Replace("\n", "") == row.Name.Text)) continue;
                                for (var line = 1; line < info.lineCount; line++)
                                {
                                    var first = info.lineInfo[line].firstCharacterIndex;
                                    var before = first - 1;
                                    while (before >= 0 && info.characterInfo[before].character == '\n') before--;
                                    if (before < 0) continue;
                                    var end = info.characterInfo[before].character;
                                    var start = info.characterInfo[first].character;
                                    var allowed = end == '/' || end == '.' || end == '-' || end == '_' || (char.IsLower(end) && char.IsUpper(start));
                                    Require(allowed, "the path \"" + label.GetParsedText().Replace("\n", "") + "\" breaks inside a name, between '" + end + "' and '" + start + "'");
                                }
                            }
                        if (state is "38-checks" or "38-checks-failed" or "38-checks-partly")
                            Require(shown.Card!.Steps.Count == known.Steps.Count && shown.Card.Steps.Count(step => step.Tone == RoomTone.Bad) == (state == "38-checks-failed" ? 1 : 0),
                                "the Checks card's steps are not the check's, the failed one marked");
                        // The status's line, our own words, never leaves a word alone on its last line (SCREENS's reader 3: "stopped on a / step").
                        if (shown.Card!.Status?.Line is string statusLine)
                            foreach (var label in host.Bar.Card!.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                            {
                                label.ForceMeshUpdate(true);
                                if (string.Join(" ", label.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries)) == statusLine)
                                    Require(!RoomCardView.LastLineIsOneWord(label), "the status's line \"" + statusLine + "\" leaves a word alone on its last line");
                            }
                        // Partly Checked: its label and line as the status, never the bad tone, the host's finding under it (laid whole by CardWhole).
                        if (state == "38-checks-partly")
                            Require(shown.Card!.Status is RoomCardStatus partlyStatus && partlyStatus.Title == KitMomentWords.PartlyChecked && partlyStatus.Line == KitMomentWords.PartlyCheckedLine
                                && partlyStatus.Tone != RoomTone.Bad && shown.Card.Description?.Text == PartlyFinding, "the Partly Checked card lost its label, its line or the finding");
                        CardWhole();
                        // Back, then the file's own Close last: a task's file always has its way back to Projects (FIELD's parts note).
                        Require(shown.Column.Count >= 2 && shown.Column[shown.Column.Count - 2].Control.Action == RoomResult.Back && shown.Column.Last().Control.Place == KitPlace.Close,
                            "Back and then Close do not stand last");
                        // A failed check's page leads with nothing of its own: its way to an instruction is the row's Type (AUDIT2's W6).
                        Require(shown.Column[0].Main == (state is not ("38-changes" or "38-checks-failed")) && shown.Column[0].Control.Action == state switch
                        {
                            "38-changes-long" => RoomResult.NextPage, "38-checks" or "38-checks-partly" => RoomResult.TryBuild, "38-checks-failed" => RoomResult.ShowChanges,
                            "38-add" or "38-add-last" => RoomResult.AddConfirm, "38-conflict" => RoomResult.Fit,
                            "38-changes" => RoomResult.ShowHistory, _ => RoomResult.Back,
                        }, "the page's first action is " + shown.Column[0].Words);
                        // DEVICE's History stands before Back on Changes (the lead's order, 2026-11-11): first on a page of its own,
                        // second after Next Page on a long list.
                        if (state == "38-changes-long")
                            Require(shown.Column.Count > 1 && shown.Column[1].Control.Action == RoomResult.ShowHistory && !shown.Column[1].Main, "the long list's second action is " + shown.Column[1].Words);
                        // A13 under the lead's R1 (2026-11-12): the add is one press on every page of its review, named by its act, with no
                        // lock and no forced paging; Next Page reads on beside it on the first page.
                        if (state is "38-add" or "38-add-last") Require(!shown.Column[0].Locks && shown.Column[0].Words == RoomAdd.AddToProject, "Add to Project is not the one press");
                        if (state == "38-add") Require(shown.Column.Count > 1 && shown.Column[1].Control.Action == RoomResult.NextPage && !shown.Column[1].Main, "Next Page does not read on beside the add");
                        // Nothing can undo an add on the headset yet: the review never says it can (AUDIT, 2026-11-11).
                        if (state is "38-add" or "38-add-last")
                            Require(shown.Card!.List.Count == RoomCard.ListAPage && !shown.Card.Words().Any(words => words.Contains("undo")), "the review does not show five files, or speaks of undoing");
                    }
                }
                if (state == "37-page")
                {
                    // The page's own line in the panel and nothing else; its prompt, the file's other sections and Close in the column, each the frame's own.
                    Require(binding.Plain && shown.FromMoment, "the page is not read by the general rule");
                    Require(bar.Body.GetParsedText() == FileScreens.StillReading(FileSection.Checks) && shown.Band == null && shown.Sentence == null, "the panel reads \"" + bar.Body.GetParsedText() + "\"");
                    // Four in all (the lead's 2026-11-14T00:24Z), as strong as the check it replaces (AUDIT's 03:35Z), by the reader's own rule:
                    // on EVERY page at most four, none main, Close last, every action the frame's own, and the page turn exactly once where
                    // there is more than one page and never on one page alone (AUDIT's 04:56Z rule); the file's
                    // own actions packed across the pages, so Refresh stands first on the first page and it and the three sections each
                    // stand exactly once across them (F122: the reader never repeats a packed action). Turned all the way round, it
                    // stands again on its first.
                    var packedSeen = new List<string>();
                    var readerPages = shown.GeneralPart.Pages;
                    for (var page = 0; page < readerPages; page++)
                    {
                        var column = shown.Column;
                        Require(column.Count <= RoomForm.Full.ActionsAPage && column.Count >= 2 && column[^1].Control.Action == Footer.Close
                            && !column.Any(action => action.Main) && (page > 0 || column[0].Control.Action == FileScreens.Refresh)
                            && column.Count(action => action.Control.Action == RoomBar.TurnPage) == (readerPages > 1 ? 1 : 0)
                            && column.All(action => ReferenceEquals(action.Control.Frame, frame)),
                            "page " + (page + 1) + " of the column holds " + string.Join(", ", column.Select(action => action.Words)));
                        packedSeen.AddRange(column.Where(action => action.Place == KitPlace.Section || action.Control.Action == FileScreens.Refresh)
                            .Select(action => action.Control.Key ?? action.Words));
                        if (readerPages > 1) shown.Turn();
                    }
                    Require(packedSeen.Count == 4 && packedSeen.Distinct().Count() == 4, "Refresh and the file's three sections do not each stand once across the pages: "
                        + string.Join(", ", packedSeen));
                    Require(shown.Column.Count > 0 && shown.Column[0].Control.Action == FileScreens.Refresh, "turned all the way round, the reader does not stand on its first page");
                    Require(shown.Column.All(action => ReferenceEquals(action.Control.Frame, frame)), "an action is not the frame's own");
                }
                if (state == "36-stop-confirm")
                    Require(shown.Sentence + KitMomentWords.Dot + bar.Body.GetParsedText() == steering.Prompt(workspace) && shown.Column.Count == 2 && shown.Column[0].Locks,
                        "Stop's confirmation reads \"" + bar.Body.GetParsedText() + "\" under \"" + shown.Sentence + "\" with " + shown.Column.Count + " actions");
                // Recorded or demonstration work says so over everything the bar shows, a panel or none (at work, the row, the
                // words alone), standing, its words whole (the lead's honesty rule, 2026-11-12); live work carries no notice.
                Require((shown.Notice != null) == (bar.NoticePlate != null), shown.Notice != null ? "recorded work does not say it is recorded" : "a notice stands over live work");
                if (bar.NoticeWords is TMPro.TextMeshPro said)
                {
                    Whole(said, "the notice's words");
                    Require(said.GetParsedText().Replace("\n", " ").Contains("Nothing here is live"), "the notice does not say that nothing here is live");
                    Require(shown.Pair || bar.NoticePlate!.transform.localPosition.y > 0f, "with no panel the notice does not stand over the bar");
                    // The words stand inside their plate with its pad on every side (JUDGE's reader, 2026-11-13: first letters on the
                    // plate's edge, the first line over its top), as the plate's own space measures what the words draw.
                    var plateSize = bar.NoticePlate!.Size;
                    said.ForceMeshUpdate(true);
                    var drawn = said.textBounds;
                    var to = bar.NoticePlate.transform.worldToLocalMatrix * said.transform.localToWorldMatrix;
                    var low = to.MultiplyPoint3x4(drawn.min);
                    var high = to.MultiplyPoint3x4(drawn.max);
                    var insets = (left: low.x + plateSize.x / 2f, right: plateSize.x / 2f - high.x, bottom: low.y + plateSize.y / 2f, top: plateSize.y / 2f - high.y);
                    var least = RoomType.Scaled(RoomBarView.NoticePadDp) / 2f;
                    Debug.Log("Halcyonic: spatial kit: " + state + (GlazeText.Scale > 1.001f ? " (Larger)" : " (Standard)") + ": the notice's words stand in from its plate "
                        + string.Join(", ", new[] { ("left", insets.left), ("right", insets.right), ("top", insets.top), ("bottom", insets.bottom) }
                            .Select(each => each.Item1 + " " + (each.Item2 / RoomType.Units(1f)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dp")));
                    Require(insets.left >= least && insets.right >= least && insets.top >= least && insets.bottom >= least, "the notice's words do not stand inside their plate with its pad");
                }
                // Its band is its own (the lead's ruling, 2026-11-13): no part the host draws, the bar's, the band's, the working
                // field's or the cursor's, crosses it. A line drawn under another part is not said.
                if (bar.NoticePlate is Halcyonic.XR.UI.RoomSurface plate)
                    foreach (var crossing in NoticeCrossings(host.transform, bar.transform, plate.transform)) Require(false, crossing + " stands over the notice");
                // The heard draft as the codec's subtitle (FIELD's composer, AUDIT's yes of 2026-11-16 23:47Z) stands no panel: judged apart below.
                var subtitled = shown.AsSubtitle && bar.SubtitleWords > 0f;
                if (state == "31-bar-heard") Require(subtitled, "the heard draft does not stand as the subtitle");
                if (shown.Pair && !subtitled)
                {
                    // The panel: DESIGN's translucent tonal material, 440 dp wide, the person's words whole, the sentence in its bar.
                    var panel = bar.Panel;
                    Require(panel.gameObject.activeSelf != shown.Wide, shown.Wide ? "a panel stands with nothing from the computer to say" : "the panel is not drawn");
                    Require(shown.Wide || panel.Glass.gameObject.activeSelf && panel.Glass.sharedMaterial != null
                        && panel.Glass.sharedMaterial.shader.name == "Halcyonic/Room Glass", "the panel is not drawn in the room's material");
                    var look = Halcyonic.XR.UI.RoomSurface.Looks(panel.Kind);
                    // The owner's reference glass (the lead's 13:33Z): translucent, a hairline, one long soft shadow, no sheen.
                    Require(look.FillTop.a < 1f && look.Shadow == Halcyonic.XR.UI.RoomSurface.DrawsShadow && look.EdgeDp > 0f && RoomSheen.Peak == 0f,
                        "the panel is not the reference's translucent glass with a hairline and no sheen, its shadow as RoomSurface.DrawsShadow says");
                    Require(shown.Wide || Mathf.Abs(panel.Size.x - RoomType.Units(RoomGeometry.PanelDp)) < Tolerance, "the panel is not 440 dp wide");
                    Whole(bar.Body, "the panel's words");
                    if (bar.NoticePlate != null)
                        Require(bar.NoticePlate.transform.localPosition.y > panel.transform.localPosition.y, "the notice does not stand over the panel");
                    // A draft and an instruction's confirmation hold the person's own words; Stop's confirmation holds the file's question.
                    Require(shown.State is not (RoomBarState.Draft or RoomBarState.Confirm) || state == "36-stop-confirm" || bar.Body.text.Length == 0
                        || bar.Body.text.Contains(BarDraft.Substring(0, 20)), "the panel does not hold the person's words");
                    // At rest before Choose Instruction (35-choose-rest) the panel holds the finished run's card (the lead's decision of
                    // 12:04Z: what the run made and what to do with it come first after a run). ASSERTED, never skipped: the card stands
                    // whole, nothing is offered to pick yet, and the row's Hold to Talk and Choose Instruction both stand, Choose
                    // Instruction raising the one control that opens the list (Tell It where no keyboard is offered: 35-choose).
                    if (choosing && shown.State != RoomBarState.Choose)
                    {
                        Require(shown.Card != null && bar.Card != null && bar.Card.LaidWhole, "at rest the panel does not hold the finished run's card whole");
                        Require(shown.Column.All(action => action.Mark == AnswerMark.None), "at rest an instruction is offered to pick before Choose Instruction");
                        Require(shown.Row.Any(action => action.Place == KitPlace.Talk), "at rest Hold to Talk does not stand on the row");
                        Require(shown.Row.Any(action => action.Place == KitPlace.Field && action.Words == RoomBar.ChooseInstruction && action.Control.Action == FileScreens.TellIt),
                            "at rest Choose Instruction does not stand on the row, raising what opens the list");
                    }
                    // The offered instructions where the bar shows the list (35-choose, 35-choose-chosen), by what it draws, never a state's name.
                    if (choosing && shown.State == RoomBarState.Choose)
                    {
                        // The product's own instructions by their short names, each with its mark, the chosen one checked; the way on only once one is chosen.
                        var names = shown.Column.Where(action => action.Mark != AnswerMark.None).Select(action => action.Words).ToList();
                        Require(names.SequenceEqual(WorkspaceText.PresetInstructions.Select(preset => preset.Label)), "the instructions offered read " + string.Join(", ", names));
                        Require(shown.Wide && shown.Band == null && host.Band.Words == null, "the offered instructions stand beside a panel or a band");
                        Require(shown.Column.Any(action => action.Main) == (state == "35-choose-chosen"), state == "35-choose-chosen" ? "no way on with an instruction chosen" : "a way on before an instruction is chosen");
                        Require(shown.Column.Count(action => action.Chosen) == (state == "35-choose-chosen" ? 1 : 0), "the chosen mark does not follow the choice");
                    }
                    Require(bar.BodyWaits == (shown.Waits && shown.Sentence == null && !shown.Mono), "the panel's one line does not sweep with the state under way");
                    if (questioning)
                    {
                        // The agent's words stand on the band and nowhere else; the panel holds only the computer's own.
                        // On the page of answers that read alike the band holds those answers, the agent's words too.
                        var prompt = state == "34-question-alike" ? shown.Band ?? "" : workspace.QuestionToAnswer!.Prompts[state == "34-question-several" ? 1 : 0].Text;
                        if (state == "34-question-alike")
                        {
                            Require(prompt.StartsWith("Dark\n", StringComparison.Ordinal) && prompt.Split('\n').Length == 3 && !prompt.Contains(" / ") && !prompt.Contains(", "),
                                "the band does not hold the answers that read alike, the label once and each description: \"" + prompt + "\"");
                            Require(bar.Body.text == LabelText.ForTextMeshPro(FileScreens.AlikeLine) && shown.Sentence == FileScreens.AlikeCaption, "the panel holds \"" + bar.Body.text + "\" under \"" + shown.Sentence + "\"");
                        }
                        if (state == "34-question-writing")
                        {
                            Require(shown.Caret && bar.Body.GetParsedText() == QuestionWritten, "the panel is not the field holding the answer as written: \"" + bar.Body.GetParsedText() + "\"");
                            Require(shown.Column.Any(action => action.Main && action.Control.Action == FileScreens.UseInput), "Use Answer has not arrived with the answer written and drawn");
                            Require(shown.Field?.Action == FileScreens.EditInput, "a press on the panel does not open the keyboard again");
                        }
                        if (shown.AsDialog)
                        {
                            // The question as one dialog (FIELD's question moment): the agent's words whole at the panel's top, exactly as
                            // written, the panel's largest text at 28 dp or more and never one word alone on their last line; no band beside
                            // it; the answers whole under the words. The actions are judged as one row below (XR, stricter than the band's).
                            var words = bar.DialogWords;
                            words.ForceMeshUpdate(true);
                            Require(words.gameObject.activeInHierarchy && !words.isTextTruncated && words.textInfo.characterCount > 0, "the agent's words do not stand whole at the dialog's top");
                            string OneSpaced(string text) => System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
                            Require(OneSpaced(LabelText.FromTextMeshPro(words.text)) == OneSpaced(LabelText.Plain(prompt)), "the dialog reads \"" + words.text + "\", not the agent's words as written");
                            Require(words.fontSize >= typeSize * RoomBarView.DialogWordsDp / RoomGeometry.TypeDp - 1e-4f, "the agent's words stand under the dialog's 28 dp");
                            foreach (var label in bar.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                                if (!ReferenceEquals(label, words) && label.gameObject.activeInHierarchy && (label.text ?? "").Length > 0)
                                    Require(label.fontSize <= words.fontSize + 1e-4f, label.name + " stands larger than the agent's words");
                            Require(words.textInfo.lineCount < 2 || !RoomCardView.LastLineIsOneWord(words)
                                || words.GetParsedText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 4, "the agent's words leave one word alone on their last line");
                            var inPanel = panel.transform;
                            var low = inPanel.InverseTransformPoint(words.transform.TransformPoint(words.textBounds.min));
                            var high = inPanel.InverseTransformPoint(words.transform.TransformPoint(words.textBounds.max));
                            Require(Mathf.Min(low.x, high.x) >= -panel.Size.x / 2f - 1e-3f && Mathf.Max(low.x, high.x) <= panel.Size.x / 2f + 1e-3f
                                && Mathf.Max(low.y, high.y) <= panel.Size.y / 2f + 1e-3f && Mathf.Min(low.y, high.y) >= -panel.Size.y / 2f - 1e-3f, "the agent's words stand outside the dialog's panel");
                            Require(host.Band.Words == null || !host.Band.Label.gameObject.activeInHierarchy, "the band stands beside the dialog");
                            Require(bar.Card != null && bar.Card.gameObject.activeInHierarchy, "the dialog's answers do not stand under the agent's words");
                            if (bar.Card != null)
                                foreach (var row in bar.Card.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                                {
                                    row.ForceMeshUpdate(true);
                                    Require(!row.isTextTruncated, "an answer \"" + row.text + "\" is cut in the dialog");
                                }
                        }
                        else
                        {
                        var band = host.Band;
                            Require(prompt.Length >= 16 && shown.Band == prompt && band.Words != null && RoomWords.Laid(prompt).Replace("\n", " ").StartsWith(band.Text.Replace("\n", " ").Trim(), StringComparison.Ordinal) && band.Text.Length > 0,
                                "the band reads \"" + band.Text + "\", not the prompt");
                            foreach (var label in bar.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                                Require(!(label.text ?? "").Contains(prompt.Substring(0, 16)), "the agent's words stand off the band, in " + label.name);
                            Require(band.transform.localPosition.y < bar.transform.localPosition.y - bar.Size.y / GlazeText.Scale / 2f + 1e-4f, "the band does not hang under the pair's foot line");
                            var parted = state is "34-question-long" or "34-question-tall" or "34-question-stop";
                            // Stopping never waits for reading, yet the frame is not reported drawn for it: the navigator takes Stop and the
                            // ways out from the frame as it shows (MenuNavigator.Taking), and every deciding press waits for the band's last part.
                            var stops = state == "34-question-stop";
                            Require((bar.BandNextButton != null) == parted, parted ? "no way to the band's next part" : "a way to a next part the band does not have");
                            Require((drawnFrames == 0) == parted, parted ? "a question still holding an unread part was reported drawn" : "the question's frame was not reported drawn");
                            if (stops) Require(shown.Column.Any(action => action.Place == KitPlace.Stop), "no Stop on a question that cannot be answered");
                            if (parted) Require(bar.BandNextButton!.LookNow() == RoomButtonLook.Main && band.Words!.Next == bar.BandNextButton.Words.text.Replace("\\\\", "\\") && band.Words.Where == "Part 1 of " + band.Words.Parts.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                && band.Where != null && band.Where.text == band.Words.Where, "the next part is not the way on, in the band's words");
                        }
                    }
                    if (armedWords != null)
                    {
                        // The confirmation reads exactly the words that will be sent: as the label parses them, each backslash once (AUDIT, 2026-11-10).
                        bar.Body.ForceMeshUpdate(true);
                        var read = bar.Body.GetParsedText();
                        var readWhole = state is "31-bar-confirm" or "31-bar-confirm-escapes";
                        if (state.EndsWith("-escapes", StringComparison.Ordinal))
                            Require(read.Contains("\\") && !read.Contains("\\\\"), "the panel reads \"" + read + "\", its backslashes not as written");
                        Require(readWhole ? read == armedWords : read.Length > 0 && armedWords.StartsWith(read.TrimEnd(), StringComparison.Ordinal),
                            "the panel reads \"" + read + "\", not the armed words \"" + armedWords + "\"");
                        Require(bar.Body.textInfo.lineCount <= RoomBar.RequestLines && (readWhole || bar.Body.textInfo.lineCount == RoomBar.RequestLines),
                            "the instruction's part stands " + bar.Body.textInfo.lineCount + " lines, not the panel's eight at most");
                    }
                    Require(bar.Request.gameObject.activeSelf == shown.Mono, shown.Mono ? "the request does not stand in mono" : "mono stands where no request is");
                    Require(!shown.Mono || !bar.Body.gameObject.activeSelf, "prose stands beside the request");
                    if (shown.Mono)
                    {
                        // The request: exact, in mono 19 dp, whole lines, at most eight before its confirmation, the tool's name never in it,
                        // and read back from the label exactly as it will run, each backslash once.
                        var request = bar.Request;
                        request.ForceMeshUpdate(true);
                        Require(!request.isTextTruncated, "the request is cut inside a line");
                        Require(Mathf.Approximately(request.fontSize, GlazeTokens.FontSize(RoomType.Scaled(RoomGeometry.MonoTypeDp))), "the request is not in mono's size");
                        Require(request.font == KitType.Face(KitRole.Mono), "the request is not in the mono face");
                        var longer = state.StartsWith("33-ask-long", StringComparison.Ordinal);
                        var command = longer ? AskLongCommand : state == "33-ask-deny-only" ? AskCommand + " [truncated]"
                            : state.StartsWith("33-ask-tool", StringComparison.Ordinal) ? AskToolName + ": " + AskToolRequest : AskCommand;
                        var read = request.GetParsedText();
                        // A long command's backslashes stand in its first lines; its second part (judged against its own rows just below)
                        // carries none, so there only the doubled backslash is refused.
                        Require(!read.Contains("\\\\") && (!longer || state == "33-ask-long-part-2" || read.Contains("\\")), "the request reads \"" + read + "\", its backslashes not as written");
                        if (state == "33-ask-long-part-2")
                            Require(partTwo != null && read.TrimEnd() == partTwo.TrimEnd() && !command.StartsWith(read.TrimEnd(), StringComparison.Ordinal),
                                "on its second part the panel reads \"" + read + "\", not that part's own rows \"" + partTwo + "\"");
                        else Require(longer ? read.Length > 0 && command.StartsWith(read.TrimEnd(), StringComparison.Ordinal) : read == command,
                            "the panel reads \"" + read + "\", not the request \"" + command + "\" from its start");
                        Require(request.textInfo.lineCount <= RoomBar.RequestLines, "the request stands " + request.textInfo.lineCount + " lines, past the panel's eight");
                        if (longer && state != "33-ask-long-part-2") Require(request.textInfo.lineCount == RoomBar.RequestLines, "a long request does not show eight lines of it");
                    }
                    Require(bar.SentenceWaits == (shown.Waits && shown.Sentence != null), "the sentence's sweep does not follow the state under way");
                    if (shown.AsDialog && !questioning)
                    {
                        // The approval as one dialog (FIELD's approval moment): our question (Run Command?) is the dialog's words at its top,
                        // whole and its largest text, inside the panel; no sentence bar; the command in mono under it (judged above).
                        var asks = bar.DialogWords;
                        asks.ForceMeshUpdate(true);
                        Require(panel.InnerBar == null, "a sentence bar stands in the approval's dialog");
                        // The finished turn's review (TASKS, f6) takes the task's name, theirs, as written; an approval our question.
                        var askedWords = shown.DialogTitle != null
                            ? OneSpaced(LabelText.FromTextMeshPro(asks.text)) == OneSpaced(LabelText.Plain(shown.DialogTitle.Text))
                            : asks.text == LabelText.ForTextMeshPro(shown.Sentence!);
                        Require(asks.gameObject.activeInHierarchy && !asks.isTextTruncated && askedWords,
                            "the dialog reads \"" + asks.text + "\" at its top, not \"" + (shown.DialogTitle?.Text ?? shown.Sentence) + "\"");
                        Require(asks.fontSize >= typeSize * RoomBarView.DialogWordsDp / RoomGeometry.TypeDp - 1e-4f, "the approval's question stands under the dialog's 28 dp");
                        foreach (var label in bar.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                            if (!ReferenceEquals(label, asks) && label.gameObject.activeInHierarchy && (label.text ?? "").Length > 0)
                                Require(label.fontSize <= asks.fontSize + 1e-4f, label.name + " stands larger than the approval's question");
                        var askLow = panel.transform.InverseTransformPoint(asks.transform.TransformPoint(asks.textBounds.min));
                        var askHigh = panel.transform.InverseTransformPoint(asks.transform.TransformPoint(asks.textBounds.max));
                        Require(Mathf.Min(askLow.x, askHigh.x) >= -panel.Size.x / 2f - 1e-3f && Mathf.Max(askLow.x, askHigh.x) <= panel.Size.x / 2f + 1e-3f
                            && Mathf.Max(askLow.y, askHigh.y) <= panel.Size.y / 2f + 1e-3f && Mathf.Min(askLow.y, askHigh.y) >= -panel.Size.y / 2f - 1e-3f, "the approval's question stands outside its dialog");
                        if (shown.Mono) Require(bar.Request.transform.position.y < asks.transform.position.y, "the command does not stand under the approval's question");
                    }
                    else if (!shown.AsDialog)
                    {
                        Require((panel.InnerBar != null) == (shown.Sentence != null), "the sentence bar does not follow the sentence");
                        if (shown.Sentence != null)
                        {
                            Whole(bar.Sentence, "the sentence");
                            Require(bar.Sentence.text == LabelText.ForTextMeshPro(shown.Sentence).Replace(" · ", "\n"), "the sentence reads \"" + bar.Sentence.text + "\", not \"" + shown.Sentence + "\"");
                        }
                    }
                    Require(bar.CaretShown == shown.Caret, "the caret does not follow the field");
                    // The column: its actions left-aligned, the main one first, the last one's foot on the panel's foot.
                    var drawn = bar.Column.Where(button => button.gameObject.activeSelf && !ReferenceEquals(button, bar.BandNextButton)).ToList();
                    actions.AddRange(drawn);
                    Require(drawn.Count == shown.Column.Count, drawn.Count + " actions drawn for " + shown.Column.Count);
                    for (var index = 0; index < drawn.Count && index < shown.Column.Count; index++)
                    {
                        Require(ReferenceEquals(bar.ControlOf(drawn[index]), shown.Column[index].Control), drawn[index].Words.text + " raises another control");
                        // Behind its lock Confirm is a rest action with its ring, taking no press; the lock passed, it is the main one.
                        var locked = ReferenceEquals(bar.Locking, drawn[index]);
                        Require(locked == (state == "33-ask-confirm-arriving" && shown.Column[index].Locks), drawn[index].Words.text + (locked ? " stands locked" : " stands unlocked"));
                        if (locked) Require(drawn[index].LockProgress is float ring && Mathf.Abs(ring - 0.42f) < 1e-3f && !drawn[index].Available, "Confirm's ring is not part filled, or it takes a press");
                        else Require(drawn[index].LockProgress == null && drawn[index].Available, drawn[index].Words.text + " keeps a ring or takes no press");
                        var marked = shown.Column[index].Mark != AnswerMark.None;
                        Require(drawn[index].Mark == shown.Column[index].Mark && drawn[index].Chosen == shown.Column[index].Chosen, drawn[index].Words.text + " carries another mark");
                        Require(drawn[index].Glyph.gameObject.activeSelf == (!marked || shown.Column[index].Chosen), drawn[index].Words.text + (marked ? " shows a check it has not got" : " has no glyph in its chip"));
                        Require(drawn[index].LookNow() == (shown.Column[index].Main && !locked ? RoomButtonLook.Main : RoomButtonLook.Rest), drawn[index].Words.text + " shows " + drawn[index].LookNow());
                        Whole(drawn[index].Words, drawn[index].Words.text);
                    }
                    if (shown.AsDialog && drawn.Count > 0)
                    {
                        // The dialog's actions: one row inside the panel's foot, Meta's 12 mm apart, the main one last and the only accent; or, only
                        // where one row cannot fit the words' measure (the larger size, the most actions: AUDIT3's F1), the main one alone on the
                        // foot's row and the others on a row over it, 12 mm clear of it.
                        var rows = drawn.GroupBy(button => Mathf.Round(button.transform.localPosition.y / Tolerance)).OrderBy(row => row.First().transform.localPosition.y).Select(row => row.ToList()).ToList();
                        var dialogMains = drawn.Where((button, index) => index < shown.Column.Count && shown.Column[index].Main).ToList();
                        Require(rows.Count == 1 || rows.Count == 2 && dialogMains.Count == 1 && rows[0].Count == 1 && ReferenceEquals(rows[0][0], dialogMains[0])
                            && rows[1].Sum(button => button.Size.x + KitType.Units(SpatialKit.TargetGapDegrees)) + dialogMains[0].Size.x > RoomType.Units(RoomGeometry.PanelDp) - 2f * RoomType.Scaled(RoomGeometry.PanelPadDp),
                            "the dialog's actions stand in " + rows.Count + " rows, not one (or two only where one cannot fit, the main one alone at the foot)");
                        if (rows.Count == 2)
                        {
                            var rowGap = (rows[1][0].transform.localPosition.y - rows[1][0].Size.y / 2f) - (rows[0][0].transform.localPosition.y + rows[0][0].Size.y / 2f);
                            Require(rowGap >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, "the dialog's two rows stand under 12 mm apart");
                        }
                        foreach (var row in rows)
                        {
                            var inRow = row.OrderBy(button => button.transform.localPosition.x).ToList();
                            for (var index = 1; index < inRow.Count; index++)
                            {
                                var between = (inRow[index].transform.localPosition.x - inRow[index].Size.x / 2f) - (inRow[index - 1].transform.localPosition.x + inRow[index - 1].Size.x / 2f);
                                Require(between >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, "the dialog's actions stand under 12 mm apart: " + inRow[index - 1].Words.text + ", " + inRow[index].Words.text);
                            }
                        }
                        var byX = rows[0].OrderBy(button => button.transform.localPosition.x).ToList();
                        var dialogFoot = panel.transform.localPosition.y - panel.Size.y / 2f;
                        var dialogLeft = panel.transform.localPosition.x - panel.Size.x / 2f;
                        var dialogRight = panel.transform.localPosition.x + panel.Size.x / 2f;
                        foreach (var button in drawn)
                            Require(button.transform.localPosition.y - button.Core.y / 2f >= dialogFoot - 1e-3f && button.transform.localPosition.x - button.Core.x / 2f >= dialogLeft - 1e-3f
                                && button.transform.localPosition.x + button.Core.x / 2f <= dialogRight + 1e-3f, button.Words.text + " stands outside the dialog's panel");
                        var mains = drawn.Where((button, index) => index < shown.Column.Count && shown.Column[index].Main).ToList();
                        Require(mains.Count <= 1 && (mains.Count == 0 || ReferenceEquals(byX[byX.Count - 1], mains[0])), "the dialog's main action is not the last in the foot's row");
                        Require(drawn.Count(button => button.LookNow() == RoomButtonLook.Main) <= 1, "more than one of the dialog's actions stands in the accent");
                    }
                    if (drawn.Count > 0 && !shown.Wide && bar.BandNextButton == null && !shown.AsDialog)
                    {
                        var lastFoot = drawn[drawn.Count - 1].transform.localPosition.y - drawn[drawn.Count - 1].Core.y / 2f;
                        var panelFoot = panel.transform.localPosition.y - panel.Size.y / 2f;
                        if (bar.PanelHung)
                        {
                            // A page whose card is shorter than its column: the card hugs its words and hangs level with the first
                            // action's top (AUDIT's 11:57Z), and the column's lowest action still stands on the foot line, under it.
                            var firstTop = drawn[0].transform.localPosition.y + drawn[0].Size.y / 2f;
                            var panelTop = panel.transform.localPosition.y + panel.Size.y / 2f;
                            Require(shown.State == RoomBarState.Page && Mathf.Abs(firstTop - panelTop) < RoomType.Units(1f) && panelFoot >= lastFoot - 1e-3f,
                                "the page's card does not hang level with its first action over the column's foot");
                            Require(Mathf.Abs(lastFoot - bar.Foot) < 1e-3f, "the column's lowest action does not stand on the foot line");
                        }
                        else Require(Mathf.Abs(lastFoot - panelFoot) < 1e-3f, "the column's lowest action does not stand on the panel's foot line");
                        var lefts = drawn.Select(button => button.transform.localPosition.x - button.Core.x / 2f).ToList();
                        Require(lefts.Max() - lefts.Min() < Tolerance, "the column's actions are not left-aligned");
                    }
                    for (var index = 1; index < drawn.Count && !shown.AsDialog; index++)
                    {
                        var gap = (drawn[index - 1].transform.localPosition.y - drawn[index - 1].Size.y / 2f) - (drawn[index].transform.localPosition.y + drawn[index].Size.y / 2f);
                        Require(gap >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, "the column's targets stand under 12 mm apart");
                    }
                }
                else if (subtitled)
                {
                    // No panel; "YOU" over the person's heard words, whole on the band in two lines at most; the band's ground the stronger
                    // one, reaching behind "YOU" and the actions (4.5 to 1 over the brightest room); the actions in one centred row under the
                    // words, Meta's 12 mm apart, the main one last and the only accent.
                    static string OneSpace(string text) => System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
                    var band = host.Band;
                    Require(!bar.Panel.gameObject.activeSelf, "a panel stands behind the subtitle");
                    Require(band.Words is { Parts: 1 } heard && heard.Count <= RoomWords.SubtitleMostLines
                        && OneSpace(LabelText.FromTextMeshPro(band.Text)) == OneSpace(shown.Body.Text), "the subtitle does not read the heard words whole in two lines at most: \"" + band.Text + "\"");
                    Require(Mathf.Approximately(band.GroundStrength, RoomWords.SubtitleGroundAlpha) && band.GroundAbove > 0f && band.GroundBelow > 0f,
                        "the subtitle's ground is not the stronger one reaching behind its speaker and its actions");
                    var bandTop = band.transform.position.y;
                    var bandFoot = band.transform.TransformPoint(new Vector3(0f, -band.Size.y, 0f)).y;
                    var groundTop = band.transform.TransformPoint(new Vector3(0f, band.GroundAbove, 0f)).y;
                    var groundFoot = band.transform.TransformPoint(new Vector3(0f, -band.Size.y - band.GroundBelow, 0f)).y;
                    var who = bar.Who;
                    Require(who != null && who.text == "YOU" && who.fontSize >= typeSize * 14f / RoomGeometry.TypeDp - 1e-4f, "no YOU at comfort size stands over the subtitle");
                    if (who != null)
                    {
                        who.ForceMeshUpdate(true);
                        var whoFoot = who.transform.TransformPoint(who.textBounds.min).y;
                        var whoTop = who.transform.TransformPoint(who.textBounds.max).y;
                        Require(whoFoot >= bandTop - 1e-4f && whoTop <= groundTop + 1e-4f, "YOU does not stand over the heard words on their ground");
                    }
                    var drawn = bar.Column.Where(button => button.gameObject.activeSelf && !ReferenceEquals(button, bar.BandNextButton)).ToList();
                    actions.AddRange(drawn);
                    Require(drawn.Count == shown.Column.Count, drawn.Count + " actions drawn for " + shown.Column.Count);
                    for (var index = 0; index < drawn.Count && index < shown.Column.Count; index++)
                    {
                        Require(ReferenceEquals(bar.ControlOf(drawn[index]), shown.Column[index].Control), drawn[index].Words.text + " raises another control");
                        Require(drawn[index].LookNow() == (shown.Column[index].Main ? RoomButtonLook.Main : RoomButtonLook.Rest), drawn[index].Words.text + " shows " + drawn[index].LookNow());
                        Whole(drawn[index].Words, drawn[index].Words.text);
                        var top = bar.transform.TransformPoint(drawn[index].transform.localPosition + new Vector3(0f, drawn[index].Size.y / 2f, 0f)).y;
                        var foot = bar.transform.TransformPoint(drawn[index].transform.localPosition - new Vector3(0f, drawn[index].Size.y / 2f, 0f)).y;
                        Require(top <= bandFoot + 1e-4f, drawn[index].Words.text + " stands over the heard words");
                        Require(foot >= groundFoot - 1e-4f, drawn[index].Words.text + " stands off the subtitle's ground");
                    }
                    if (drawn.Count > 0)
                    {
                        var rowY = drawn[0].transform.localPosition.y;
                        Require(drawn.All(button => Mathf.Abs(button.transform.localPosition.y - rowY) < Tolerance), "the subtitle's actions do not stand in one row");
                        var byX = drawn.OrderBy(button => button.transform.localPosition.x).ToList();
                        for (var index = 1; index < byX.Count; index++)
                        {
                            var between = (byX[index].transform.localPosition.x - byX[index].Size.x / 2f) - (byX[index - 1].transform.localPosition.x + byX[index - 1].Size.x / 2f);
                            Require(between >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, "the subtitle's actions stand under 12 mm apart: " + byX[index - 1].Words.text + ", " + byX[index].Words.text);
                        }
                        var middle = (byX[0].transform.localPosition.x - byX[0].Size.x / 2f + byX[byX.Count - 1].transform.localPosition.x + byX[byX.Count - 1].Size.x / 2f) / 2f;
                        Require(Mathf.Abs(middle) < RoomType.Units(1f), "the subtitle's actions do not stand centred under the words");
                        var mains = drawn.Where((button, index) => index < shown.Column.Count && shown.Column[index].Main).ToList();
                        Require(mains.Count <= 1 && (mains.Count == 0 || ReferenceEquals(byX[byX.Count - 1], mains[0])), "the subtitle's main action is not the last in its row");
                        Require(drawn.Count(button => button.LookNow() == RoomButtonLook.Main) <= 1, "more than one of the subtitle's actions stands in the accent");
                    }
                }
                if (shown.State is RoomBarState.Sent or RoomBarState.Words)
                {
                    Whole(bar.Alone, "the words alone");
                    // On their plate, in its middle: with nothing under them they are lost over a bright wall or a lit screen.
                    bar.Alone.ForceMeshUpdate(true);
                    var wordsMiddle = bar.Alone.transform.localPosition + bar.Alone.textBounds.center;
                    Require(bar.AlonePlate.gameObject.activeSelf && Mathf.Abs(wordsMiddle.x) < RoomType.Units(1f) && Mathf.Abs(wordsMiddle.y) < RoomType.Units(3f),
                        "the words alone stand " + (wordsMiddle.x / RoomType.Units(1f)).ToString("0.0") + " dp across and " + (wordsMiddle.y / RoomType.Units(1f)).ToString("0.0") + " dp up from their plate's middle");
                    Require(bar.Alone.text == LabelText.ForTextMeshPro(shown.Words.Text).Replace(" · ", "\n"), "the words read \"" + bar.Alone.text + "\", not \"" + shown.Words.Text + "\"");
                }
                foreach (var button in actions)
                {
                    var renderer = button.GetComponentInChildren<MeshRenderer>();
                    Require(renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "Halcyonic/Room Halo", button.name + " is not drawn in the room's halo");
                    Require(button.Size.y >= RoomType.Units(RoomButton.HeightDp) - 1e-5f, button.name + "'s target is under Meta's hand target");
                }
            }

            // Placed as the plane places the redesign's host: its foot line on DESIGN's pitch, scaled for the text size.
            shot.Elevation = RoomGeometry.FeetPitch - SpatialKit.DegreesTo(host.FootLine * GlazeText.Scale);
            shot.Centre = Place(host.transform, eyes, 0f, shot.Elevation);
            host.transform.localScale *= GlazeText.Scale;
            var footAt = host.transform.TransformPoint(new Vector3(0f, host.FootLine, 0f)) - eyes;
            var footPitch = Mathf.Atan2(footAt.y, new Vector2(footAt.x, footAt.z).magnitude) * Mathf.Rad2Deg;
            Require(Mathf.Abs(footPitch - RoomGeometry.FeetPitch) < 0.05f, "the foot line stands at pitch " + footPitch.ToString("0.00") + ", not " + RoomGeometry.FeetPitch);
            Stand(shot, host.transform, eyes, new KitCarrier[0], host.Size * GlazeText.Scale);
            // Watch It Work's arc faces the eyes where the plane now stands, and is measured there.
            if (state == "36-work-watch") WatchArcChecks(state, host, eyes, Require, Tolerance);
            // What must stand inside the field: the instruction, and with a question the band under it (the host's whole extent).
            var whole = questioning || ResultStates.Contains(state);
            var half = whole ? host.Size / 2f : bar.Size / 2f;
            var extent = whole ? host.transform : bar.transform;
            foreach (var corner in new[] { new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f), new Vector3(-half.x, half.y, 0f), new Vector3(half.x, half.y, 0f) })
                shot.Corners.Add(extent.TransformPoint(corner));
            // The band hangs under the bar's anchored foot line, outside the host's box (the lead's 2026-11-14T00:24Z): its own
            // corners, from the middle of its top edge, stand inside the field too.
            if (whole && host.Band.Words != null && host.Band.Size.y > 0f)
            {
                var band = host.Band;
                foreach (var corner in new[] { new Vector3(-band.Size.x / 2f, 0f, 0f), new Vector3(band.Size.x / 2f, 0f, 0f), new Vector3(-band.Size.x / 2f, -band.Size.y, 0f), new Vector3(band.Size.x / 2f, -band.Size.y, 0f) })
                    shot.Corners.Add(band.transform.TransformPoint(corner));
            }
            // The bar a person reads never moves for what hangs under it: it stands on the host's middle.
            Require(bar.transform.localPosition == Vector3.zero, "the bar moved off the host's middle (" + bar.transform.localPosition + ") for what hangs under it");
            // The plane's handle (the lead's ruling 3): centred at its fixed depth under the foot line whatever the band says, under the
            // band at its tallest, the hold area Meta's target tall, so it never moves while a person reads and never meets the band.
            var handleDown = RoomType.Units(KitWorkspaceHost.HandleUnderFootDp(GlazeText.Scale) - RoomHandle.HoldHeightDp / 2f) / GlazeText.Scale;
            Require(host.Handle.gameObject.activeSelf && Mathf.Abs(host.Handle.transform.localPosition.x) < 1e-5f
                && Mathf.Abs(host.Handle.transform.localPosition.y - (host.FootLine - handleDown)) < RoomType.Units(0.5f),
                "the plane's handle is not centred at its fixed depth under the foot line: " + host.Handle.transform.localPosition);
            // Its whole hold area stands inside what a person sees at head level (AUDIT's 2026-11-14T01:30Z condition 1): its corners
            // join the field's corners, at both sizes, whatever the band says.
            var holdHalf = new Vector2(RoomType.Units(RoomHandle.HoldWidthDp), RoomType.Units(RoomHandle.HoldHeightDp)) / 2f;
            foreach (var corner in new[] { new Vector3(-holdHalf.x, -holdHalf.y, 0f), new Vector3(holdHalf.x, -holdHalf.y, 0f), new Vector3(-holdHalf.x, holdHalf.y, 0f), new Vector3(holdHalf.x, holdHalf.y, 0f) })
                shot.Corners.Add(host.Handle.transform.TransformPoint(corner));
            if (host.Band.Words != null && host.Band.Size.y > 0f)
            {
                var bandBottom = host.Band.transform.localPosition.y - host.Band.Size.y * host.Band.transform.localScale.y;
                Require(host.Handle.transform.localPosition.y + RoomType.Units(RoomHandle.HoldHeightDp / 2f) / GlazeText.Scale <= bandBottom + RoomType.Units(0.5f),
                    "the plane's handle meets the band");
            }
            shot.Targets.Add(("the instruction", bar.transform, bar.Presses.ToList()));
            // The one sheen: a 2 s pass with its peak at the middle, then 8 s of rest; none kept still.
            var stillWas = GlazeMotion.Still;
            GlazeMotion.Still = false;
            var (_, passing) = RoomSheen.At(1f);
            var (_, resting) = RoomSheen.At(5f);
            GlazeMotion.Still = true;
            var (_, kept) = RoomSheen.At(1f);
            GlazeMotion.Still = stillWas;
            Require(Mathf.Abs(passing - RoomSheen.Peak) < 1e-4f && resting == 0f && kept == 0f,
                "the sheen is " + passing + " mid-pass, " + resting + " at rest and " + kept + " kept still, not its peak, none and none");
            shot.AfterRender = failures =>
            {
                // Its presses are the file's own: Type opens the instruction (Tell it), the panel's field the keyboard, each column action its control.
                var expectedActions = new List<string>();
                if (bar.Type.gameObject.activeSelf && bar.ControlOf(bar.Type) is KitControl typing)
                {
                    bar.Type.Press.PressForRender(501);
                    expectedActions.Add(typing.Action);
                }
                if (bar.Field.gameObject.activeInHierarchy && bar.Shown?.Field is KitControl field)
                {
                    bar.Field.PressForRender(502);
                    expectedActions.Add(field.Action);
                }
                var press = 503;
                // Every page of the general reader is swept (AUDIT's 03:35Z): its actions pressed on each, the reader's own Next Page
                // turning to the next (it turns the bar; it is never the file's to take), Close pressed once, last.
                var swept = new List<RoomButton>();
                RoomButton? closing = null;
                var pagesOf = bar.Shown?.GeneralPart.Pages ?? 1;
                for (var page = 0; page < pagesOf; page++)
                {
                    var onPage = bar.Column.Where(button => button.gameObject.activeSelf).ToList();
                    var turner = onPage.FirstOrDefault(button => bar.ControlOf(button) is KitControl turnControl && turnControl.Action == RoomBar.TurnPage);
                    foreach (var button in onPage)
                    {
                        if (button == turner) continue;
                        if (bar.ControlOf(button) is KitControl closeControl && closeControl.Action == Footer.Close && pagesOf > 1) { closing = button; continue; }
                        swept.Add(button);
                    }
                    if (page == pagesOf - 1 || turner == null) break;
                    var was = bar.Shown?.GeneralPart.Page;
                    // This page's own presses first, then the turn.
                    foreach (var button in swept.Where(button => onPage.Contains(button)).ToList()) SweepPress(button);
                    swept.RemoveAll(button => onPage.Contains(button));
                    host.SettleForRender();
                    turner.Press.PressForRender(press++);
                    host.SettleForRender();
                    foreach (var settling in bar.Presses) settling.SettleForRender();
                    if (bar.Shown?.GeneralPart.Page == was) { failures.Add(state + ": the reader's Next Page did not turn its page"); break; }
                }
                if (closing != null) swept.Add(closing);
                foreach (var button in swept) SweepPress(button);
                void SweepPress(RoomButton button)
                {
                    // Talk Again is held, not pressed: it starts the hold, as Hold to Talk does.
                    // The More page's Back is the bar's own, as the reader's pager is: it goes back here, never the file's to take.
                    if (bar.ControlOf(button) is not KitControl control || control.Place == KitPlace.Talk || control.Action == RoomBar.MoreBack) return;
                    button.Press.PressForRender(press++);
                    // Confirm behind its lock takes no press: nothing is confirmed by the press that answered.
                    if (ReferenceEquals(bar.Locking, button))
                    {
                        if (acted.Contains(control.Action)) failures.Add(state + ": Confirm took a press behind its lock");
                        return;
                    }
                    expectedActions.Add(control.Action);
                }
                if (state is "33-ask-sending" or "34-question-sending" && bar.Column.Any(button => button.gameObject.activeSelf)) failures.Add(state + ": an action stands while the answer is on its way");
                if (bar.BandNextButton is RoomButton turning)
                {
                    // The band's next part is pressed as any action: the band turns, and with its last part showing the frame counts as drawn.
                    var designWas = RoomDesign.Enabled;
                    RoomDesign.Enabled = true;
                    try
                    {
                        turning.Press.PressForRender(590);
                        host.DrawnForRender();
                    }
                    finally { RoomDesign.Enabled = designWas; }
                    if (host.Band.Words is not { Part: 1 }) failures.Add(state + ": the press did not turn the band to its next part");
                    if (host.Band.Words is { More: false } && drawnFrames == 0) failures.Add(state + ": the question read to its last part was not reported drawn");
                }
                foreach (var action in expectedActions)
                    if (!acted.Contains(action)) failures.Add(state + ": a press did not take the file's " + action + "; took " + string.Join(", ", acted));
                Debug.Log("Halcyonic: spatial kit: " + state + ": " + bar.Shown?.State + ", " + bar.Size.x.ToString("0.0000") + " by " + bar.Size.y.ToString("0.0000")
                    + ", presses took " + (acted.Count == 0 ? "nothing" : string.Join(", ", acted)) + ".");
            };
            return shot;
        }

        /// <summary>The usage figure 36-work-steps lays under its steps: the agent app reports usage and has counted none yet.</summary>
        private static readonly TaskUsageFigure WorkStepsUsage = TaskUsage.Tokens(null, true);

        /// <summary>
        /// Watch It Work open at work (36-work-watch): Done Watching, the one accent, over Stop, plain, its core on the foot line, both
        /// where the column stands; the arc drawn in the panel's place, every part of it a gap over the bar's top and never over the bar
        /// (AUDIT's 07:33Z), its two newest files whole. Its place in degrees is said at each size, for the shelf's clearance.
        /// </summary>
        private static void WatchChecks(string state, KitWorkspaceHost host, RoomBar shown, Vector3 eyes, List<RoomButton> actions, Action<bool, string> require, float tolerance)
        {
            var bar = host.Bar;
            var unit = RoomType.Units(1f);
            require(shown.Column.Count == 2 && shown.Column[0].Control.Action == RoomBar.Watch && shown.Column[0].Words == RoomBar.WatchDoneWords && shown.Column[0].Main
                && shown.Column[1].Place == KitPlace.Stop && !shown.Column[1].Main,
                "the column is not Done Watching, the accent, over Stop, plain: " + string.Join(", ", shown.Column.Select(action => action.Words + (action.Main ? " (main)" : ""))));
            require(bar.Column.Count(button => button.gameObject.activeSelf) == 2, bar.Column.Count(button => button.gameObject.activeSelf) + " column actions stand, not Done Watching and Stop");
            if (shown.Column.Count != 2) return;
            var (watch, stop) = (bar.Column[0], bar.Column[1]);
            actions.Add(watch);
            actions.Add(stop);
            require(watch.LookNow() == RoomButtonLook.Main && stop.LookNow() == RoomButtonLook.Rest, "Done Watching shows " + watch.LookNow() + " and Stop " + stop.LookNow());
            foreach (var (button, words) in new[] { (watch, RoomBar.WatchDoneWords), (stop, shown.Column[1].Words) })
            {
                button.Words.ForceMeshUpdate(true);
                require(!button.Words.isTextTruncated && button.Words.text == LabelText.ForTextMeshPro(words), button.name + " reads \"" + button.Words.text + "\"");
                require(Mathf.Abs(button.transform.localPosition.x - button.Core.x / 2f - (bar.PanelRight + RoomType.Units(RoomGeometry.PairGapDp))) < tolerance,
                    button.name + " does not stand where the column stands");
            }
            require(Mathf.Abs(stop.transform.localPosition.y - stop.Core.y / 2f - bar.Foot) < tolerance, "Stop's core does not stand on the foot line");
            var between = (watch.transform.localPosition.y - watch.Core.y / 2f) - (stop.transform.localPosition.y + stop.Core.y / 2f);
            require(between >= KitType.Units(SpatialKit.TargetGapDegrees) - tolerance, "Done Watching stands " + (between / unit).ToString("0.0") + " dp over Stop, closer than a target's gap");
            require(!host.Cursor.gameObject.activeSelf || string.IsNullOrEmpty(host.Cursor.Words), "the agent's cursor still speaks under the arc");
        }

        /// <summary>
        /// Watch It Work's arc once the host stands where the plane stands it: every drawn part of it a gap over the bar's top and never
        /// over the bar (AUDIT's 07:33Z), its two newest files drawn, tipped to face the eyes; its place in degrees said at each size.
        /// </summary>
        private static void WatchArcChecks(string state, KitWorkspaceHost host, Vector3 eyes, Action<bool, string> require, float tolerance)
        {
            var bar = host.Bar;
            var unit = RoomType.Units(1f);
            var watch = bar.Column[0];
            host.FaceWatch();
            var arc = host.WatchArc;
            require(arc != null && arc.gameObject.activeSelf && arc.Panels.Count(panel => panel.gameObject.activeSelf) == RoomWritingArc.MostShown,
                "the arc does not stand with its " + RoomWritingArc.MostShown + " newest files");
            if (arc == null) return;
            // In the bar's own space (the arc is its child): the arc's lowest part against the bar's top.
            var into = bar.transform.worldToLocalMatrix;
            float low = float.PositiveInfinity, high = float.NegativeInfinity, lowPitch = float.PositiveInfinity, highPitch = float.NegativeInfinity, left = float.PositiveInfinity, right = float.NegativeInfinity;
            foreach (var part in arc.GetComponentsInChildren<Renderer>(false))
            {
                if (!part.enabled) continue;
                var bounds = part.localBounds;
                if (bounds.size == Vector3.zero) continue;
                var toBar = into * part.transform.localToWorldMatrix;
                for (var corner = 0; corner < 8; corner++)
                {
                    var at = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x, (corner & 2) == 0 ? bounds.min.y : bounds.max.y, (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var local = toBar.MultiplyPoint3x4(at);
                    low = Mathf.Min(low, local.y);
                    high = Mathf.Max(high, local.y);
                    var seen = part.transform.localToWorldMatrix.MultiplyPoint3x4(at) - eyes;
                    var flat = Mathf.Sqrt(seen.x * seen.x + seen.z * seen.z);
                    lowPitch = Mathf.Min(lowPitch, Mathf.Atan2(seen.y, flat) * Mathf.Rad2Deg);
                    highPitch = Mathf.Max(highPitch, Mathf.Atan2(seen.y, flat) * Mathf.Rad2Deg);
                    left = Mathf.Min(left, Mathf.Atan2(seen.x, seen.z) * Mathf.Rad2Deg);
                    right = Mathf.Max(right, Mathf.Atan2(seen.x, seen.z) * Mathf.Rad2Deg);
                }
            }
            var top = bar.Size.y / 2f;
            var gap = RoomType.Units(RoomWritingArc.GapDp);
            require(low >= top + gap - 10f * tolerance, "the arc's lowest part stands " + ((low - top) / unit).ToString("0.0") + " dp over the bar's top, not its " + RoomWritingArc.GapDp + " dp gap: over the bar");
            var watchTop = watch.transform.localPosition.y + watch.Core.y / 2f;
            var footSeen = bar.transform.localToWorldMatrix.MultiplyPoint3x4(new Vector3(0f, bar.Foot, 0f)) - eyes;
            var footPitch = Mathf.Atan2(footSeen.y, Mathf.Sqrt(footSeen.x * footSeen.x + footSeen.z * footSeen.z)) * Mathf.Rad2Deg;
            string Deg(float value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            Debug.Log("Halcyonic: spatial kit: " + state + (GlazeText.Scale > 1.001f ? " (Larger)" : " (Standard)") + ": the arc stands " + Deg((low - top) / unit)
                + " dp over the bar's top (Done Watching's top " + Deg((top - watchTop) / unit) + " dp under it), pitch " + Deg(lowPitch) + " to " + Deg(highPitch)
                + " degrees, yaw " + Deg(left) + " to " + Deg(right) + "; the foot line at pitch " + Deg(footPitch) + ", " + Deg((high - low) / unit) + " dp tall.");
        }
    }
}
