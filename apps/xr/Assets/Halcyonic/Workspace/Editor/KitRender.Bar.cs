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
            "31-bar-sent",
        };

        /// <summary>
        /// The redesign's approval (DESIGN 2.4; unit U2) on the same host: asked, with Approve and Deny; its confirmation with
        /// Confirm arriving behind its lock, then the main action; a long request before its answer (its first eight lines) and
        /// under its confirmation (the file's first part, the next part the main action); a request that can only be denied; and
        /// Sending, the column emptied, while the answer sent may still take effect.
        /// </summary>
        private static readonly string[] AskStates =
        {
            "33-ask", "33-ask-confirm-arriving", "33-ask-confirm", "33-ask-long", "33-ask-long-part", "33-ask-deny-only", "33-ask-sending", "33-ask-tool",
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
        private static readonly string[] WorkStates = { "36-work", "36-work-row", "36-stop-confirm", "36-work-recorded", "36-work-fixing" };

        /// <summary>A page of the file with no moment of its own, drawn by the general rule (RoomBar.OfFrame): Checks while its answer is read.</summary>
        private static readonly string[] PageStates = { "37-page" };

        /// <summary>The workspace with only <paramref name="actions"/> offered, as a recorded run offers none.</summary>
        private static WorkspacePresentation WithActions(WorkspacePresentation workspace, IReadOnlyList<WorkspaceAction> actions) =>
            new WorkspacePresentation(workspace.Character, workspace.Objective, workspace.Execution, workspace.Runtime, actions,
                actions.Where(workspace.RequiresConfirmation).ToList(), workspace.Commands, workspace.Activity);

        /// <summary>
        /// The result at rest (DESIGN 2.5 to 2.8): what the computer knows over the row, the agent's newest message on the
        /// band; a long message; Changes and its second page's way on; Checks passed and failed; and a project with a
        /// check and no observed turn.
        /// </summary>
        private static readonly string[] ResultStates = { "38-result", "38-result-long", "38-changes", "38-changes-long", "38-checks", "38-checks-failed", "38-project", "38-result-agent", "38-result-stopped", "38-result-play", "38-result-play-refused", "38-result-play-not-in-build", "38-result-add", "38-add", "38-add-last", "38-conflict" };

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

        /// <summary>What the computer knows in each of the result's states, synthetic.</summary>
        private static RoomResult? ResultFor(string state)
        {
            CheckStepOutcome Step(CheckStepKind step, CheckStepResult result) => new CheckStepOutcome(step, result);
            var passing = new[] { Step(CheckStepKind.TypeCheck, CheckStepResult.Passed), Step(CheckStepKind.Build, CheckStepResult.Passed), Step(CheckStepKind.PageDrawn, CheckStepResult.Passed), Step(CheckStepKind.VrOffered, CheckStepResult.Passed) };
            var failing = new[] { Step(CheckStepKind.TypeCheck, CheckStepResult.Passed), Step(CheckStepKind.Build, CheckStepResult.Failed), Step(CheckStepKind.PageDrawn, CheckStepResult.NotRun), Step(CheckStepKind.VrOffered, CheckStepResult.NotRun) };
            return state switch
            {
                "38-result" or "38-result-long" or "38-result-agent" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing),
                "38-changes" => new RoomResult(false, true, false, true, ResultTurn(false), passing, RoomResultPage.Changes),
                "38-changes-long" => new RoomResult(false, true, false, true, ResultTurn(true), passing, RoomResultPage.Changes),
                "38-checks" => new RoomResult(isVr: true, canMakeVr: false, canTry: true, passed: true, ResultTurn(false), passing, RoomResultPage.Checks),
                "38-checks-failed" => new RoomResult(false, true, false, false, ResultTurn(false), failing, RoomResultPage.Checks),
                "38-result-stopped" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: false, ResultTurn(false), failing),
                "38-result-play" => new RoomResult(isVr: false, canMakeVr: true, canTry: true, passed: true, ResultTurn(false), passing, play: PlayInHeadset.Offered),
                "38-result-play-refused" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, play: PlayInHeadset.NotConfined),
                "38-result-play-not-in-build" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, play: PlayInHeadset.NotInThisBuild),
                "38-project" => new RoomResult(isVr: false, canMakeVr: true, canTry: true, passed: true, null, passing),
                "38-result-add" => new RoomResult(isVr: false, canMakeVr: true, canTry: false, passed: true, ResultTurn(false), passing, add: CopyFor(10, conflicted: false), addReview: "synthetic-review"),
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

        /// <summary>A heard instruction holding backslashes, synthetic: the confirmation must read exactly the words that will be sent.</summary>
        private const string BarConfirmWords = @"Add a golden balloon worth five points in games\balloon.js, matching \d+ points.";

        /// <summary>A heard instruction long enough for the file to cut in parts, with a backslash in its first, synthetic.</summary>
        private const string BarLongWords = @"Add a golden balloon worth five points in games\balloon.js, then a silver one worth three, then a bomb that ends the round "
            + "when it is popped. Keep the score in the top corner, count each pop once, play the short pop sound for every balloon and the low one for the bomb, "
            + "and show the best score of the day under it. When the round ends, show the three best scores with the time each was made, let the player start again "
            + "with one press, and keep every balloon inside the play area at both screen sizes. Then remove the old backup file and run the tests again.";

        private static Shot BarShot(string state, Transform root, Vector3 eyes)
        {
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
                "38-checks-failed" => (VoiceStage.Idle, false, RoomBarState.Rest, "Checks failed: the steps the check reached, Write Instruction and Back, synthetic data"),
                "38-result-agent" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the agent offers never ask: the agent and its level in the row beside it, synthetic data"),
                "38-result-stopped" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the agent's tries at a failed check ran out: that line first, Write Instruction leading, synthetic data"),
                "38-result-add" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the task's own copy holds work to add: Add to Project leading, synthetic data"),
                "38-add" => (VoiceStage.Idle, false, RoomBarState.Rest, "Add to Project's review: the task, where its work goes, the first five of ten files, Next Page and Back, synthetic data"),
                "38-add-last" => (VoiceStage.Idle, false, RoomBarState.Rest, "Add to Project's review, its last page: the last five files, Add to Project behind its lock, and Back, synthetic data"),
                "38-conflict" => (VoiceStage.Idle, false, RoomBarState.Rest, "Where the project changed the same file: FIELD's card naming it, Let Agent Fix and Back, synthetic data"),
                "38-result-play" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the project's checked preview can open on the headset: Play in Headset beside Try Build, synthetic data"),
                "38-result-play-refused" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result where the preview reads outside its project: no Play in Headset, its reason at the card's foot, synthetic data"),
                "38-result-play-not-in-build" => (VoiceStage.Idle, false, RoomBarState.Rest, "The result on a paired release build, which carries no relay: no Play in Headset, its reason at the card's foot, synthetic data"),
                "36-work-fixing" => (VoiceStage.Idle, false, RoomBarState.Hidden, "At work while the agent's first try at a failed check runs: the failure and the try in the panel beside Stop, synthetic data"),
                "38-project" => (VoiceStage.Idle, false, RoomBarState.Rest, "A project with a check and no observed turn: Build Passed, Make VR Version and Try Build, synthetic data"),
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
                "31-bar-sent" => (VoiceStage.Idle, true, RoomBarState.Sent, "Sent, the run not started yet, synthetic data"),
                _ => (VoiceStage.Idle, false, RoomBarState.Rest, "At rest: Hold to Talk and Type, synthetic data"),
            };
            var shot = new Shot { Caption = caption };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var factory = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
            var workspace = state switch
            {
                "33-ask-long" or "33-ask-long-part" => SelectedWorkspace(waiting: true, command: AskLongCommand),
                "33-ask-deny-only" => SelectedWorkspace(waiting: true, command: AskCommand + " [truncated]", approvable: false),
                "33-ask-tool" or "33-ask-tool-confirm" => SelectedWorkspace(waiting: true, command: AskToolRequest, tool: AskToolName),
                _ when asking => SelectedWorkspace(waiting: true, command: AskCommand),
                _ when questioning => SelectedWorkspace(waiting: true, asked: QuestionFor(state), confirmAnswers: state == "34-question-confirm"),
                // At work, on an agent that takes no instruction while it works, and on one that does.
                "36-work" or "36-stop-confirm" or "36-work-fixing" => WithActivity(SelectedWorkspace(waiting: false, instructWhileRunning: false), ShortReport()),
                // Nothing to press: no Stop and no instruction while it works, as a recorded run offers.
                "36-work-recorded" => WithActions(WithActivity(SelectedWorkspace(waiting: false, instructWhileRunning: false), ShortReport()), new WorkspaceAction[0]),
                "36-work-row" => Running(ShortReport()),
                // The result: at rest, the agent's newest message its one line of activity.
                _ when ResultStates.Contains(state) => WithActivity(SelectedWorkspace(waiting: false, status: ExecutionStatus.Completed),
                    new[] { new ActivityEntry(1, Day + "09:04:00.000Z", ActivityKind.Message, state == "38-result-long" ? ResultSaysLong : ResultSays, true) }),
                // Every other moment of the row is at rest: the run has ended, and its next instruction starts the next one.
                _ => WithActivity(SelectedWorkspace(waiting: false, status: ExecutionStatus.Completed), ShortReport()),
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
            // The host stands first: under a confirmation the file counts what is read by the panel's own lines, as the director has it do.
            var host = KitWorkspaceHost.Create(root);
            int? requestRows = null;
            void Read(string request, bool instruction, int part)
            {
                var (words, mono) = RoomBar.Reading(FileScreens.RequestLine(steering, request), workspace, instruction);
                screen.ReadRequest(request, host.Bar.Lines(words, mono), part, steering);
                screen.RequestDrawn(0, steering, DateTimeOffset.UtcNow);
                requestRows = screen.RequestRows;
            }
            if (state is "33-ask-confirm-arriving" or "33-ask-confirm" or "33-ask-long-part" or "33-ask-tool-confirm")
            {
                // Approve pressed, as the person presses it: the file measures the request, shows its first part, and that part is drawn.
                if (steering.Press(WorkspaceAction.Approve, workspace).Step != SteeringStep.Confirm) shot.Failures.Add("Approve did not ask for its confirmation");
                Read(steering.ToRead(workspace)!, instruction: false, RoomBar.RequestLines);
                if (state == "33-ask-long-part" && screen.RequestParts < 2) shot.Failures.Add("the long request is one part of " + screen.RequestRows + " lines");
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
            if (state is "31-bar-confirm" or "31-bar-confirm-part")
            {
                var heard = state == "31-bar-confirm" ? BarConfirmWords : BarLongWords;
                if (steering.Spoken(heard, workspace).Step != SteeringStep.Confirm) shot.Failures.Add("the heard instruction did not arm its confirmation");
                var request = steering.ToRead(workspace)!;
                // A part is eight of the panel's own lines, as the director gives the file while the redesign draws it.
                Read(request, instruction: true, RoomBar.RequestLines);
                if (state == "31-bar-confirm-part" && screen.RequestParts < 2) shot.Failures.Add("the long instruction is one part of " + screen.RequestRows + " lines");
                armedWords = request;
            }
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
            var binding = KitFileBinding.Of(frame, steering.Armed, workspace, screen.Instruction.Open ? screen.Instruction.Words : "", new ConnectionStatus(ConnectionPhase.Live),
                SelectedProject, ProductionSpine(workspace), new KitRowModel("workspace", LineWords.Ours("Move Workspace"), GlazeIcon.Move), roomDesign: true,
                draftSpoken: screen.Instruction.Open && screen.Instruction.Spoken, requestRows: requestRows, chooses: choosing,
                asking: screen.Question.Draft != null && !screen.Question.Reviewing ? screen.Question.Prompt : (int?)null,
                writing: screen.Question.Writing ? screen.Question.Input : null, result: ResultFor(state),
                selectors: state == "38-result-agent" ? new KitSelectors("This Mac", false, "Synthetic Agent", agentChangeable: true, offersNeverAsk: true, neverAsk: true) : null);
            // As the director does: a frame the task's binding has no place for is carried plain where a reader of the redesign takes it.
            if (binding == null && state == "37-page" && RoomBar.OfFrame(frame, VoiceStage.Idle) != null) binding = KitFileBinding.PlainOf(frame);
            if (binding == null)
            {
                shot.Failures.Add("the kit has no binding to this frame");
                return shot;
            }
            host.Accepting = () => true;
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
            }
            finally { RoomDesign.Enabled = was; }
            var bar = host.Bar;
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
            // The old kit is not drawn at all beside the redesign (the owner, 2026-11-05).
            Require(!host.View.gameObject.activeInHierarchy, "the old kit still draws beside the redesign");
            var actions = new List<RoomButton>();
            if (bar.Shown is RoomBar shown)
            {
                if (shown.Row.Count > 0)
                {
                    // The row: home-size actions in its order, the main one first, its core the hand target itself, 32 dp apart.
                    var row = shown.Row.Select(action => action.Place == KitPlace.Talk ? bar.Talk : action.Place == KitPlace.Field ? bar.Type : bar.More).ToList();
                    actions.AddRange(row);
                    Require(row.All(button => button.gameObject.activeSelf), "a row action is not drawn");
                    Require(!bar.Type.gameObject.activeSelf || shown.Row.Any(action => action.Place == KitPlace.Field), "Type stands while it listens");
                    Require(bar.More.gameObject.activeSelf == shown.Row.Any(action => action.Place != KitPlace.Talk && action.Place != KitPlace.Field), "the row's third action stands where the model gives none, or does not where it gives one");
                    for (var index = 0; index < row.Count; index++)
                    {
                        Require(row[index].LookNow() == (shown.Row[index].Main ? RoomButtonLook.Main : RoomButtonLook.Rest), row[index].name + " shows " + row[index].LookNow());
                        // Hold to Talk and Type are home size; a third action is action size, its target still Meta's.
                        if (row[index] != bar.More) Require(Mathf.Abs(row[index].Core.y - RoomType.Units(RoomButton.HeightDp)) < Tolerance, row[index].name + "'s core is not the hand target");
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
                var working = state is "36-work" or "36-work-row" or "36-work-recorded" or "36-work-fixing";
                // No frame leaves the plane a part of no height (JUDGE's walk 9): the host's footprint is never empty.
                Require(host.Size.x > 0f && host.Size.y > 0f, "the host stands " + host.Size.x + " by " + host.Size.y + ": a part of no size");
                Require(shown.Working == working, working ? "the agent works and the redesign does not say so" : "at work is shown where nothing works");
                Require(host.Field.gameObject.activeSelf == working && (host.Field.Shown > 0.999f) == working, working ? "the working field does not stand" : "the working field stands where nothing works");
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
                    else Require(!bar.Panel.gameObject.activeSelf && host.Band.Words == null, "a panel or the band stands while the agent works");
                    var stop = bar.Column[0];
                    if (state == "36-work-recorded") Require(shown.Column.Count == 0 && shown.Row.Count == 0 && bar.Column.All(button => !button.gameObject.activeSelf), "an action stands where nothing can be pressed");
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
                    // The field's inner box: the panel's place, 12 degrees up from the foot line.
                    var zoom = GlazeText.Scale;
                    var middle = (host.Field.transform.localPosition - bar.transform.localPosition) * zoom;
                    var inner = new Vector2(RoomType.Units(RoomGeometry.PanelDp), RoomType.Units(RoomGeometry.FieldTallDp));
                    Require(Mathf.Abs(middle.x + inner.x / 2f - bar.PanelRight) < Tolerance && Mathf.Abs(middle.y - inner.y / 2f - bar.Foot) < Tolerance,
                        "the field's inner box does not end at the panel's place and stand on the foot line");
                    Require(Vector2.Distance(host.Field.Size, RoomField.Around(inner)) < Tolerance, "the field is " + (host.Field.Size / unit) + " dp, not the design's size round its box");
                    Require(host.Cursor.transform.localPosition == host.Field.transform.localPosition, "the cursor does not count its stops from the field's middle");
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
                        Require(shown.Band == message && host.Band.Words != null && host.Band.Text.Length > 0, "the agent's newest message does not stand on the band");
                        foreach (var label in bar.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                            Require(!(label.text ?? "").Contains(message.Substring(0, 16)), "the agent's words stand off the band, in " + label.name);
                        Require((bar.BandNextButton != null) == (state == "38-result-long"), state == "38-result-long" ? "no way to the long message's next part" : "a way to a next part the band does not have");
                        if (state == "38-result-long") Require(bar.BandNextButton!.LookNow() == RoomButtonLook.Rest && shown.Column.Any(action => action.Main), "the message's next part is the main action over the result's own");
                        // FIELD's result card (A17): the check and the files as slots, an ended chain of tries as its status; no
                        // sentence, nothing joined on a dot, every label of the card whole.
                        var stopped = state == "38-result-stopped";
                        var slots = state == "38-project" ? new[] { "Passed" } : stopped ? new[] { "Failed", "4 Files" } : state == "38-result-add" ? new[] { "Passed", "10 Files" } : new[] { "Passed", "4 Files" };
                        Require(shown.Card != null && !shown.Card.AnyDot && shown.Card.Slots.Select(slot => slot.Value.Text).SequenceEqual(slots) && shown.Sentence == null
                            && (shown.Card.Status?.Title == (stopped ? RoomBar.StoppedTitle : null)),
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
                        else if (state == "38-result-add") Require(shown.Column[0].Control.Action == RoomResult.ShowAdd && shown.Column[0].Main && shown.Column.Count(action => action.Main) == 1,
                            "Add to Project does not lead where the task holds work to add");
                        else Require(shown.Column[0].Control.Action == RoomResult.MakeVrVersion && shown.Column[0].Main, "Make VR Version does not lead");
                    }
                    else
                    {
                        // Each page is FIELD's card (A17): Changes a list five a page, Checks the steps as nodes, A13's review and
                        // the files changed in both; no band, no sentence, nothing joined on a dot, every label of the card whole.
                        Require(shown.Band == null && host.Band.Words == null && shown.Sentence == null, "a band or a sentence stands on a page");
                        Require(shown.Card != null && !shown.Card.AnyDot && shown.Facts == null, "the page is not a card, or joins facts on a dot");
                        if (state is "38-changes" or "38-changes-long")
                            Require(shown.Card!.List.Count == (state == "38-changes-long" ? RoomCard.ListAPage : known.Changes.Count), "the Changes card lists " + shown.Card.List.Count + " files");
                        if (state is "38-checks" or "38-checks-failed")
                            Require(shown.Card!.Steps.Count == known.Steps.Count && shown.Card.Steps.Count(step => step.Tone == RoomTone.Bad) == (state == "38-checks-failed" ? 1 : 0),
                                "the Checks card's steps are not the check's, the failed one marked");
                        CardWhole();
                        // Back, then the file's own Close last: a task's file always has its way back to Projects (FIELD's parts note).
                        Require(shown.Column.Count >= 2 && shown.Column[shown.Column.Count - 2].Control.Action == RoomResult.Back && shown.Column.Last().Control.Place == KitPlace.Close,
                            "Back and then Close do not stand last");
                        Require(shown.Column[0].Main == (state != "38-changes") && shown.Column[0].Control.Action == state switch
                        {
                            "38-changes-long" => RoomResult.NextPage, "38-checks" => RoomResult.TryBuild, "38-checks-failed" => FileScreens.TellIt,
                            "38-add" => RoomResult.NextPage, "38-add-last" => RoomResult.AddConfirm, "38-conflict" => RoomResult.Fit,
                            "38-changes" => RoomResult.ShowHistory, _ => RoomResult.Back,
                        }, "the page's first action is " + shown.Column[0].Words);
                        // DEVICE's History stands before Back on Changes (the lead's order, 2026-11-11): first on a page of its own,
                        // second after Next Page on a long list.
                        if (state == "38-changes-long")
                            Require(shown.Column.Count > 1 && shown.Column[1].Control.Action == RoomResult.ShowHistory && !shown.Column[1].Main, "the long list's second action is " + shown.Column[1].Words);
                        // A13: the add confirms behind its lock, named by its act (FIELD's A13 in A17's language).
                        if (state == "38-add-last") Require(shown.Column[0].Locks && shown.Column[0].Words == RoomAdd.AddToProject, "Add to Project does not stand behind its lock");
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
                    Require(shown.Column.Count == 5 && shown.Column[0].Control.Action == FileScreens.Refresh && shown.Column[4].Control.Action == Footer.Close
                        && shown.Column.Count(action => action.Place == KitPlace.Section) == 3 && !shown.Column.Any(action => action.Main), "the column holds " + string.Join(", ", shown.Column.Select(action => action.Words)));
                    Require(shown.Column.All(action => ReferenceEquals(action.Control.Frame, frame)), "an action is not the frame's own");
                }
                if (state == "36-stop-confirm")
                    Require(shown.Sentence + KitMomentWords.Dot + bar.Body.GetParsedText() == steering.Prompt(workspace) && shown.Column.Count == 2 && shown.Column[0].Locks,
                        "Stop's confirmation reads \"" + bar.Body.GetParsedText() + "\" under \"" + shown.Sentence + "\" with " + shown.Column.Count + " actions");
                if (shown.Pair)
                {
                    // The panel: DESIGN's translucent tonal material, 440 dp wide, the person's words whole, the sentence in its bar.
                    var panel = bar.Panel;
                    Require(panel.gameObject.activeSelf != shown.Wide, shown.Wide ? "a panel stands with nothing from the computer to say" : "the panel is not drawn");
                    Require(shown.Wide || panel.Glass.gameObject.activeSelf && panel.Glass.sharedMaterial != null
                        && panel.Glass.sharedMaterial.shader.name == "Halcyonic/Room Glass", "the panel is not drawn in the room's material");
                    var look = Halcyonic.XR.UI.RoomSurface.Looks(panel.Kind);
                    Require(look.FillTop.a < 1f && !look.Shadow && look.EdgeDp > 0f, "the panel is not DESIGN's translucent tonal layer with a hairline and no shadow");
                    Require(shown.Wide || Mathf.Abs(panel.Size.x - RoomType.Units(RoomGeometry.PanelDp)) < Tolerance, "the panel is not 440 dp wide");
                    Whole(bar.Body, "the panel's words");
                    // A draft and an instruction's confirmation hold the person's own words; Stop's confirmation holds the file's question.
                    Require(shown.State is not (RoomBarState.Draft or RoomBarState.Confirm) || state == "36-stop-confirm" || bar.Body.text.Length == 0
                        || bar.Body.text.Contains(BarDraft.Substring(0, 20)), "the panel does not hold the person's words");
                    if (choosing)
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
                            Require(prompt.StartsWith("Dark", StringComparison.Ordinal) && prompt.Contains(" / "), "the band does not hold the answers that read alike: \"" + prompt + "\"");
                            Require(bar.Body.text == LabelText.ForTextMeshPro("Same Name · Not Selectable") && shown.Sentence == FileScreens.AlikeCaption, "the panel holds \"" + bar.Body.text + "\" under \"" + shown.Sentence + "\"");
                        }
                        if (state == "34-question-writing")
                        {
                            Require(shown.Caret && bar.Body.GetParsedText() == QuestionWritten, "the panel is not the field holding the answer as written: \"" + bar.Body.GetParsedText() + "\"");
                            Require(shown.Column.Any(action => action.Main && action.Control.Action == FileScreens.UseInput), "Use Answer has not arrived with the answer written and drawn");
                            Require(shown.Field?.Action == FileScreens.EditInput, "a press on the panel does not open the keyboard again");
                        }
                        var band = host.Band;
                        Require(prompt.Length >= 16 && shown.Band == prompt && band.Words != null && RoomWords.Laid(prompt).Replace("\n", " ").StartsWith(band.Text.Replace("\n", " ").Trim(), StringComparison.Ordinal) && band.Text.Length > 0,
                            "the band reads \"" + band.Text + "\", not the prompt");
                        foreach (var label in bar.GetComponentsInChildren<TMPro.TextMeshPro>(false))
                            Require(!(label.text ?? "").Contains(prompt.Substring(0, 16)), "the agent's words stand off the band, in " + label.name);
                        Require(band.transform.localPosition.y < bar.transform.localPosition.y - bar.Size.y / GlazeText.Scale / 2f + 1e-4f, "the band does not hang under the pair's foot line");
                        var parted = state is "34-question-long" or "34-question-tall" or "34-question-stop";
                        // Stopping never waits for reading, yet the frame is not reported drawn for it: the navigator takes Stop alone
                        // from the frame as it shows (MenuNavigator.Taking), and everything else waits for the band's last part.
                        var stops = state == "34-question-stop";
                        Require((bar.BandNextButton != null) == parted, parted ? "no way to the band's next part" : "a way to a next part the band does not have");
                        Require((drawnFrames == 0) == parted, parted ? "a question still holding an unread part was reported drawn" : "the question's frame was not reported drawn");
                        if (stops) Require(shown.Column.Any(action => action.Place == KitPlace.Stop), "no Stop on a question that cannot be answered");
                        if (parted) Require(bar.BandNextButton!.LookNow() == RoomButtonLook.Main && band.Words!.Next == bar.BandNextButton.Words.text.Replace("\\\\", "\\"), "the next part is not the way on, in the band's words");
                    }
                    if (armedWords != null)
                    {
                        // The confirmation reads exactly the words that will be sent: as the label parses them, each backslash once (AUDIT, 2026-11-10).
                        bar.Body.ForceMeshUpdate(true);
                        var read = bar.Body.GetParsedText();
                        Require(read.Contains("\\") && !read.Contains("\\\\"), "the panel reads \"" + read + "\", its backslashes not as written");
                        Require(state == "31-bar-confirm" ? read == armedWords : read.Length > 0 && armedWords.StartsWith(read.TrimEnd(), StringComparison.Ordinal),
                            "the panel reads \"" + read + "\", not the armed words \"" + armedWords + "\"");
                        Require(bar.Body.textInfo.lineCount <= RoomBar.RequestLines && (state == "31-bar-confirm" || bar.Body.textInfo.lineCount == RoomBar.RequestLines),
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
                        Require(!read.Contains("\\\\") && (!longer || read.Contains("\\")), "the request reads \"" + read + "\", its backslashes not as written");
                        Require(longer ? read.Length > 0 && command.StartsWith(read.TrimEnd(), StringComparison.Ordinal) : read == command,
                            "the panel reads \"" + read + "\", not the request \"" + command + "\" from its start");
                        Require(request.textInfo.lineCount <= RoomBar.RequestLines, "the request stands " + request.textInfo.lineCount + " lines, past the panel's eight");
                        if (longer) Require(request.textInfo.lineCount == RoomBar.RequestLines, "a long request does not show eight lines of it");
                    }
                    Require(bar.SentenceWaits == (shown.Waits && shown.Sentence != null), "the sentence's sweep does not follow the state under way");
                    Require((panel.InnerBar != null) == (shown.Sentence != null), "the sentence bar does not follow the sentence");
                    if (shown.Sentence != null)
                    {
                        Whole(bar.Sentence, "the sentence");
                        Require(bar.Sentence.text == LabelText.ForTextMeshPro(shown.Sentence), "the sentence reads \"" + bar.Sentence.text + "\", not \"" + shown.Sentence + "\"");
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
                    if (drawn.Count > 0 && !shown.Wide && bar.BandNextButton == null)
                    {
                        var lastFoot = drawn[drawn.Count - 1].transform.localPosition.y - drawn[drawn.Count - 1].Core.y / 2f;
                        var panelFoot = panel.transform.localPosition.y - panel.Size.y / 2f;
                        Require(Mathf.Abs(lastFoot - panelFoot) < 1e-3f, "the column's lowest action does not stand on the panel's foot line");
                        var lefts = drawn.Select(button => button.transform.localPosition.x - button.Core.x / 2f).ToList();
                        Require(lefts.Max() - lefts.Min() < Tolerance, "the column's actions are not left-aligned");
                    }
                    for (var index = 1; index < drawn.Count; index++)
                    {
                        var gap = (drawn[index - 1].transform.localPosition.y - drawn[index - 1].Size.y / 2f) - (drawn[index].transform.localPosition.y + drawn[index].Size.y / 2f);
                        Require(gap >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, "the column's targets stand under 12 mm apart");
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
                    Require(bar.Alone.text == LabelText.ForTextMeshPro(shown.Words.Text), "the words read \"" + bar.Alone.text + "\", not \"" + shown.Words.Text + "\"");
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
            // What must stand inside the field: the instruction, and with a question the band under it (the host's whole extent).
            var whole = questioning || ResultStates.Contains(state);
            var half = whole ? host.Size / 2f : bar.Size / 2f;
            var extent = whole ? host.transform : bar.transform;
            foreach (var corner in new[] { new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f), new Vector3(-half.x, half.y, 0f), new Vector3(half.x, half.y, 0f) })
                shot.Corners.Add(extent.TransformPoint(corner));
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
                foreach (var button in bar.Column.Where(button => button.gameObject.activeSelf).ToList())
                {
                    // Talk Again is held, not pressed: it starts the hold, as Hold to Talk does.
                    if (bar.ControlOf(button) is not KitControl control || control.Place == KitPlace.Talk) continue;
                    button.Press.PressForRender(press++);
                    // Confirm behind its lock takes no press: nothing is confirmed by the press that answered.
                    if (ReferenceEquals(bar.Locking, button))
                    {
                        if (acted.Contains(control.Action)) failures.Add(state + ": Confirm took a press behind its lock");
                        continue;
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
    }
}
