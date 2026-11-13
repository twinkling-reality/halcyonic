#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of the project rail and the entry panel: the welcome, Connect projects, More tasks and
    /// Create a project, so the XR layer only lays them out. Actions are plain verbs; statuses are
    /// written out, never left to color; nothing claims more than the state shows: Connect lists the
    /// projects the Mac already has and discovers nothing, Help me figure it out is fixed questions and
    /// says so, and a step reads as confirmed only by its completed record. Text Halcyonic did not
    /// write, such as a project's name, is passed in as <see cref="LabelText.Plain"/> shows it.
    /// </summary>
    public static class EntryText
    {
        public const string ConnectProjects = "Connect Projects";
        public const string CreateProject = "New Project";
        public const string CreateInvite = "Start from Idea";
        public const string NotNow = "Not Now";

        public const string ShowAll = "Show All";
        public const string AddTask = "Add Task";
        public const string WaitingForMac = "Awaiting Computer";

        /// <summary>Why Start task can't be pressed while a build is on its way (the coordinator, 2026-10-02).</summary>
        public const string AlreadyStarting = "Starting · Awaiting Result";

        public const string IdeaPrompt = "Your Idea";
        public const string WorkPrompt = "Task Goal";
        public const string TypeIdea = "Type Idea";

        /// <summary>New project's row for the fixed questions, and the main action while it is chosen (ADR 0026).</summary>
        public const string AnswerQuestions = "Answer Questions";
        public const string StartQuestions = "Start Questions";

        /// <summary>The fixed questions' main action, and why it can't be pressed before an answer is chosen (the coordinator, 2026-10-02).</summary>
        public const string NextQuestion = "Next Question";
        public const string ChooseOrTypeFirst = "Choose or Type First";

        /// <summary>Typing a name: the fixed name question's own answer, and the words page's row before there is one.</summary>
        public const string TypeName = "Type Name";

        /// <summary>What a name or first task must be, said where it can't be kept as it is.</summary>
        public const string NameRule = "Name · Up to 200 Characters";
        public const string DescribeTask = "Describe First Task";
        public const string ShortenTask = "First Task · Up to 4,000 Characters";

        /// <summary>The words page's line for a new folder: the folder list's own words for it.</summary>
        public static string NewFolderIn(LocationRoot root) => ProjectFolder.NewFolderLabel(root);

        /// <summary>Typing a first task of one's own in place of the companion's suggestion and one's earlier words.</summary>
        public const string TypeMyOwn = "Type My Own";
        public const string NothingStartsYet = "Nothing Starts Yet";
        public const string GuideNote = "Fixed Questions · Not AI · Answers Editable";
        public const string Back = "Back";

        /// <summary>Under the answer given before, and under every choice made in a list: one word marks a choice everywhere.</summary>
        public const string Chosen = "Chosen";


        public const string RecapLine = "Editable Before Start";
        public const string StartBuilding = "Start Task";
        public const string StartOver = "Start Over";
        public const string ConfirmStartOver = "Confirm";
        public const string MoreOptions = "More Options";
        public const string ProjectName = "Project Name";
        public const string FirstTask = "First Task";
        public const string NotNamedYet = "Not Named";
        public const string HowItRuns = "Run Settings";
        public const string NameTheProject = "Name Project";
        public const string WhatFirstTask = "First Task Goal";

        public const string FolderTitle = "Folder";
        public const string FolderLine = "Allowed Folders · New Folder Starts Empty";
        public const string ReadingFolders = "Reading Folders";
        public const string NoFolders = "No Folders Allowed · Allow on Computer · Try Again";
        public const string FoldersCut = "First 200 Folders Only";
        public const string NewFolderPrompt = "New Folder Name";
        public const string NewFolderRule = "Up to 64 letters, digits, dots, dashes or underscores, starting with a letter or digit";
        public const string UseThatFolder = "Use Folder";
        public const string ChooseAnotherFolder = "Choose Folder";

        /// <summary>The recap's main action where Start Building waits on run settings: the choice itself, one press to its page.</summary>
        public const string ChooseAgentApp = "Choose Agent App";
        public const string ChooseModel = "Choose Model";
        public const string RebindWarning = "Later Tasks Use New Folder · Running Tasks Keep Theirs";

        /// <summary>
        /// The folders couldn't be read: this line, then why on the next, in Halcyonic's own words from
        /// <see cref="WhyFoldersUnread"/>, else <see cref="PressTryAgain"/> (settled by the coordinator, 2026-10-07).
        /// </summary>
        public const string FoldersUnread = "Folders Unreadable";

        /// <summary>
        /// Why a read of the folders failed, by the refusal's code or a request that never got a reply,
        /// never the error's message, which is the control plane's own and can hold an address; null when
        /// nothing a person can act on is known. <paramref name="accessRefused"/> is what a refused credential
        /// means for how this headset reaches the computer, as its connection says it (<see cref="ControlPlaneApi.AccessRefused"/>)
        /// (settled by the coordinator, 2026-10-07).
        /// </summary>
        public static string? WhyFoldersUnread(Exception? error, string accessRefused) => error switch
        {
            // What answered isn't your computer, or didn't prove it: never "didn't answer" (the review's L5).
            _ when Within<CertificateMismatchException>(error) != null => ConnectionText.NotThePairedComputer,
            // Over USB, the proof that found nothing answering: nothing answered (WORDS.md's F1).
            _ when Within<TokenNotSentException>(error) is TokenNotSentException notSent => notSent.Outcome switch
            {
                LoopbackProofOutcome.Unproved => FoldersUnproved,
                LoopbackProofOutcome.Unreachable => FoldersUnanswered,
                _ => null,
            },
            // Something answered with what isn't HTTP, or too much of it (PinnedHttpHandler, LoopbackProofHandler):
            // never "didn't answer".
            _ when Within<System.IO.InvalidDataException>(error) != null || Within<FormatException>(error) != null || Within<OverflowException>(error) != null => null,
            // Something took the connection and set up no secure one (PinnedConnection): never "didn't answer".
            _ when Within<HandshakeFailedException>(error) != null => null,
            // A code counts only with the status that carries it: a 500 naming device_revoked is no revoked pairing.
            ControlPlaneRequestException { Code: "device_revoked", Status: 401 } => ConnectionText.PairingRefused,
            ControlPlaneRequestException { Code: "unauthorized", Status: 401 } => accessRefused,
            ControlPlaneRequestException { Code: "too_many_requests", Status: 429 } => FoldersTurnedAway,
            ControlPlaneRequestException { Code: null, InnerException: System.Net.Http.HttpRequestException } => FoldersUnanswered,
            _ => null,
        };

        /// <summary>The folders' why when what answered couldn't prove it holds the access code (settled by the coordinator, 2026-10-07).</summary>
        public const string FoldersUnproved = "Host Unverified · Code Not Sent · Check App · Try Again";

        /// <summary>The first exception of this kind in the chain of inner ones, or null.</summary>
        private static T? Within<T>(Exception? error) where T : Exception
        {
            for (var each = error; each != null; each = each.InnerException)
            {
                if (each is T found) return found;
            }
            return null;
        }

        public const string FoldersUnanswered = "No Answer · Check App · Try Again";
        public const string FoldersTurnedAway = "Computer Busy · Too Many Tries · Retry in 1 min";

        public const string FoldersNotConnected = "Not Connected · Try Again";

        public const string PressTryAgain = "Try Again";

        /// <summary>
        /// Where the project's files live, for the recap, under its own heading: the folder chosen, the
        /// project's own, or what is still to choose. <paramref name="current"/> is an existing
        /// project's folder; <paramref name="needed"/> says whether what runs it works in a project folder.
        /// </summary>
        /// <summary>
        /// The folder fact apart, for a card's slots (FIELD's A17): what kind of choice it is, the folder's name, the place it
        /// is in and, where the request moves the project, the folder it is in now. Names lean; nothing is joined.
        /// </summary>
        public static FolderFactParts FolderFactPartsOf(ProjectLocation? current, ProjectFolder? chosen, bool needed)
        {
            var now = current != null && chosen != null ? LineWords.TheirName(current.Name) : (LineWords?)null;
            if (chosen != null)
            {
                var kind = chosen.IsNew ? (string.IsNullOrEmpty(chosen.StarterId) ? FolderFactKind.NewFolder : FolderFactKind.NewStarter)
                    : chosen.FolderName == null ? FolderFactKind.Directly : FolderFactKind.Existing;
                var place = chosen.PlaceGone ? LineWords.Ours(ProjectFolder.PlaceUnlisted) : LineWords.TheirName(chosen.RootName);
                return new FolderFactParts(kind, chosen.FolderName == null ? null : LineWords.TheirName(chosen.FolderName), place, now);
            }
            if (current != null) return new FolderFactParts(FolderFactKind.ProjectFolder, LineWords.TheirName(current.Name), null, null);
            return new FolderFactParts(needed ? FolderFactKind.NotChosen : FolderFactKind.NotNeeded, null, null, null);
        }

        /// <summary>The folder fact as runs (<see cref="FolderFact"/>): Halcyonic's words upright and first, names leaning.</summary>
        public static LineWords FolderFactRuns(ProjectLocation? current, ProjectFolder? chosen, bool needed)
        {
            if (chosen != null && current != null) return LineWords.Ours("Next · ") + chosen.DescribeRuns() + LineWords.Ours(" · Now · ") + LineWords.TheirName(current.Name);
            if (chosen != null) return chosen.DescribeRuns(startOfLine: true);
            if (current != null) return LineWords.TheirName(current.Name) + LineWords.Ours(" · Project Folder");
            return LineWords.Ours(needed ? "Not Chosen" : "Not Needed");
        }

        public static string FolderFact(ProjectLocation? current, ProjectFolder? chosen, bool needed)
        {
            // Halcyonic's own words first, so a long name never pushes them past the line's end (settled by the coordinator, 2026-10-08).
            if (chosen != null && current != null) return "Next · " + chosen.Describe() + " · Now · " + LabelText.Plain(current.Name);
            if (chosen != null) return chosen.Describe(startOfLine: true);
            if (current != null) return LabelText.Plain(current.Name) + " · Project Folder";
            return needed ? "Not Chosen" : "Not Needed";
        }

        /// <summary>
        /// What to do after a refusal or failure about a folder, from its code, never from the control
        /// plane's own message; null for any other code.
        /// </summary>
        public static string? FolderProblem(RejectionCode? refusal, string? failure)
        {
            if (refusal == RejectionCode.LocationRequired || failure == "location_required")
                return "No Folder · Choose Folder";
            if (refusal == RejectionCode.LocationMissing || failure == "location_missing")
                // The host also answers this for a folder or place that is there but cannot be read.
                return "Folder Unusable · Choose Again or Fix on Computer";
            if (refusal == RejectionCode.LocationNotAllowed || failure == "location_not_allowed")
                return "Folder Not Allowed · Choose Listed Folder";
            if (refusal == RejectionCode.LocationExists || failure == "location_exists")
                return "Folder Exists · Use It or Rename";
            if (failure == "location_not_created")
                return "Folder Not Made · Nothing Changed · Choose Another";
            return null;
        }

        /// <summary>The step stopped over its folder, so choosing another is the next action.</summary>
        public static bool AboutFolder(BuildStep step) => !step.EffectUnknown && FolderProblem(step.Refusal, step.Failure) != null;

        public const string OptionsLine = "Choose an agent app, then its model";
        public const string NoRuntimes = "No Agent App Available · Set Up on Computer";
        public const string ChangeAgentApp = "Change Agent App";
        public const string ListsModels = "Model · Your Choice";
        public const string ChoosesModel = "Model · App's Choice";
        public const string NoModels = "No Models Offered · Change Agent App";

        /// <summary>
        /// Why the agent app's models couldn't be read, by the reason's code, never its message, which is the
        /// agent app's or the control plane's own words: one that didn't start or answer points to its
        /// setup, a version not supported to another agent app, any other to choosing it again (settled by
        /// the coordinator, 2026-10-07).
        /// </summary>
        public static string ModelsUnread(string? code) => code switch
        {
            "runtime_unreachable" or "runtime_closed" or "runtime_unavailable" or "runtime_start_failed" or "runtime_exited" or "runtime_timeout" or "timeout" => ModelsUnanswered,
            "runtime_version_unsupported" => ModelsUnsupported,
            _ => ModelsUnreadable,
        };

        public const string ModelsUnanswered = "Models Unreadable · Agent App Silent · Check Setup";
        /// <summary>The headset gave up waiting for your computer, past the 40 s a models read may take (settled by the coordinator, 2026-10-07).</summary>
        public const string ModelsComputerSilent = "Models Unreadable · No Answer · Choose Again to Retry";
        public const string ModelsUnreadable = "Models Unreadable · Choose Again to Retry";
        public const string ModelsUnsupported = "Models Unreadable · Version Unsupported · Choose Another";
        public const string ModelsNotConnected = "Models Unreadable · Not Connected · Retry When Connected";
        public const string ChosenForYou = "Chosen for You";

        /// <summary>A simulated agent app in the recorded demonstration, which keeps its name.</summary>
        public const string Practice = "Practice";
        public const string Done = "Done";

        /// <summary>Under the review's title: the items are Halcyonic's own account of what will be sent, so it asks to check them, never claims to be the command.</summary>
        public const string ReviewLine = "Review Every Part";
        public const string ConfirmStart = "Confirm";
        public const string Change = "Change";

        /// <summary>The final press, locked in its place until the person has seen the last part: what is left to do, not a part's name.</summary>
        public static string ReadToPart(int pages) => "Read to Part " + Count(pages);

        /// <summary>New project's row to the review's next part, which replaces <see cref="ReadToPart"/> (ADR 0026).</summary>
        public static string NextPart(int part, int parts) => "Next Part · " + Count(part) + " of " + Count(parts);

        /// <summary>A pager's words where its count is given apart (JUDGE's dot list, 2026-11-13T1203Z, item 2): the action alone.</summary>
        public const string NextPartLabel = "Next Part";

        /// <summary>A pager's count, given apart from its words: "2 of 7".</summary>
        public static string PartOf(int part, int parts) => Count(part) + " of " + Count(parts);

        /// <summary>A row to part <paramref name="part"/> as this design draws it: under the redesign "Next Part" alone, its count apart (<see cref="NextPartCount"/>).</summary>
        public static string NextPartShown(int part, int parts) => RoomDesign.Enabled ? NextPartLabel : NextPart(part, parts);

        /// <summary>That row's count, given apart under the redesign ("2 of 7"); none in the older view, whose words hold it.</summary>
        public static LineWords? NextPartCount(int part, int parts) => RoomDesign.Enabled ? LineWords.Ours(PartOf(part, parts)) : null;

        /// <summary>
        /// What starting over clears, as Start over's side panel on New project's recap and its
        /// confirmation both say: for a new project its idea, answers, name, first task and folder; for a
        /// task added to a project, which keeps its name and folder, the task's idea, answers, first task
        /// and a folder chosen in place of the project's. The agent app and model are the draft's, kept.
        /// </summary>
        public static string StartOverClears(bool forTask) => forTask
            ? "Clears the idea, answers, first task and folder change. Keeps the project and run settings."
            : "Clears the idea, answers, name, first task and folder. Keeps the run settings.";

        /// <summary>
        /// New project's row at the end of a page that needs more than one, on page <paramref name="page"/>
        /// (from 0) of <paramref name="pages"/>: the page it turns to, or First page on the last, as the
        /// menu's pager says (<see cref="Footer.NextPageWords"/>) and the review's row counts.
        /// </summary>
        public static string NextPage(int page, int pages) => page == pages - 1 ? "First Page" : "Next Page · " + Count(page + 2) + " of " + Count(pages);

        /// <summary>The pager's words, everywhere in the entry panel: a word with its direction, never Back, which leaves the screen.</summary>
        public const string Previous = "Previous";
        public const string Next = "Next";

        public static string Page(int page, int pages) => Count(page + 1) + " of " + Count(pages);

        public static string Part(int page, int pages) => "Part " + Count(page + 1) + " of " + Count(pages);

        public const string SendingLine = "Running Work Continues";
        public const string TryAgain = "Try Again";
        public const string Started = "Confirmed · Started · On Stage";

        /// <summary>The started task's own file, where it works and asks (the lead's first journey, 2026-11-12); FIELD's words to come.</summary>
        public const string OpenTask = "Open Task";

        public const string PreviousRequestTitle = "Start Unconfirmed";
        public const string PreviousRequestLine = "Last Start May Have Run · Check Stage First · Retry Could Repeat It";

        /// <summary>On Starting your work, where it only opens Not sure it happened.</summary>
        public const string CheckFirst = "Next";

        /// <summary>The first of the two presses that clear a start whose outcome is unknown.</summary>
        public const string Clear = "Clear";
        public const string ClearOnceChecked = "Clear After Checking";
        public const string ClearOnlyAfterChecking = "Check Stage, Then Clear";
        public const string ConfirmClear = "Confirm";
        public const string Cancel = "Cancel";
        public const string Cleared = "Cleared · Start from Idea";

        /// <summary>The unknown start's note after its words changed while Yes, clear waited.</summary>
        public const string ChangedBeforeClear = "Not Cleared · Changed · Reread";

        /// <summary>The command whose outcome is unknown, by its id.</summary>
        public static string Reference(string commandId) => "Reference · " + commandId;

        /// <summary>What the Mac recorded of a start whose outcome is unknown, in words: a failure may still have changed something.</summary>
        public static string Recorded(CommandStatus? status) => status switch
        {
            null => "Unconfirmed",
            CommandStatus.Accepted => "Accepted · Unconfirmed",
            CommandStatus.Completed => "Confirmed",
            CommandStatus.Rejected => "Refused · Nothing Happened",
            CommandStatus.Failed => "Failed · May Have Changed Something · Check Stage",
            _ => "Unexpected Answer · Check Stage",
        };

        public const string OpenNow = "Open Now";
        public const string KeepCreating = "Keep Creating";

        public const string Move = "Move";
        public const string ResetPosition = "Reset Position";
        public const string Close = "Close";

        /// <summary>Why Start task can't go ahead, for each check the recap makes, in its order.</summary>
        public const string DemoCannotStart = "Demo · No New Work";
        public const string ChooseHowItRuns = "Choose Run Settings";
        public const string ChooseAgain = "Run Settings Unavailable · Choose Again";
        public const string FinishChoosing = "Finish Run Settings";
        public const string ProjectGone = "Project Missing · Choose Another";
        public const string ChooseWhereFilesLive = "Choose Folder";

        public static string CreateTitle(string? existingProject) => existingProject == null ? CreateProject : "New Task · " + existingProject;

        /// <summary>New project's subject for a project that exists, as runs: "New task in " upright, its name leaning, last.</summary>
        public static LineWords CreateTitleRuns(string? existingProject) =>
            existingProject == null ? LineWords.Ours(CreateProject) : LineWords.Ours("New Task · ") + LineWords.TheirName(existingProject);

        public static string Question(int index, int count) => "Question " + Count(index + 1) + " of " + Count(count);


        /// <summary>How many tasks wait for the person, said as a person would: "1 task is waiting for you", "2 tasks are waiting for you".</summary>
        public static string WaitingForYou(int count) => "Needs You · " + Tasks(count);

        private static string Tasks(int count) => Count(count) + (count == 1 ? " Task" : " Tasks");

        public static string StepName(BuildStepKind kind, bool newProject) => kind switch
        {
            BuildStepKind.CreateProject => "Create Project",
            BuildStepKind.BindFolder => "Move to Folder",
            BuildStepKind.CreateWorkstream => newProject ? "Create First Task" : "Create Task",
            _ => "Start Task",
        };

        /// <summary>A start whose effect is unknown is never said to have had none.</summary>
        public const string NotSureItHappened = "It may have happened. Look for the task in the room to see.";

        /// <summary>
        /// How a step of Start task went: sent is not done, and only a completed record confirms.
        /// A refusal says why and what to do from its code, and so does a failure about a folder; never the
        /// control plane's or an agent app's message (<see cref="WorkspaceText.WhyRefused"/>).
        /// </summary>
        public static string StepStatus(BuildStep step) => StepStatusOf(step.Status, step.Kind, step.Refusal, step.Failure, step.EffectUnknown);

        /// <summary>A step's state from its parts, as <see cref="StepStatus"/> says it (public so every state and reason can be walked).</summary>
        public static string StepStatusOf(BuildStepStatus status, BuildStepKind kind, RejectionCode? refusal, string? failure, bool effectUnknown) => status switch
        {
            BuildStepStatus.NotYet => "Not Sent",
            // Sent is not done: the step waits on whoever answers it, said plainly, never on a dot (FIELD and JUDGE, 2026-11-13: the
            // step under way is the bar's sentence, which is heard whole).
            BuildStepStatus.Waiting => kind == BuildStepKind.StartWork ? "Waiting on Agent" : "Waiting on Computer",
            BuildStepStatus.Confirmed => "Confirmed",
            // One sentence for a revoked pairing, alone, within the step's two rows (settled by the coordinator, 2026-10-07).
            BuildStepStatus.Refused when refusal == RejectionCode.DeviceRevoked => "This headset's pairing was revoked. Forget this computer, then pair again.",
            // A refused or failed step says what stopped it and the way on in a sentence of New Project's own (AUDIT's MUST, 2026-11-13T1258Z):
            // the shared reasons (WorkspaceText) are other flows' and stay theirs. The row's cross says it stopped.
            BuildStepStatus.Refused => StepRefused(refusal) ?? StepRefusedOther,
            // A failure that may have had an effect is never put in words that say nothing happened.
            BuildStepStatus.Failed when effectUnknown => NotSureItHappened,
            BuildStepStatus.Failed => StepFolder(refusal, failure) ?? StepFailed(failure) ?? StepFailedOther,
            BuildStepStatus.Unknown => NotSureItHappened,
            BuildStepStatus.NotSent => StepNotSent,
            _ => NotSureItHappened,
        };

        /// <summary>A step the host refused for a reason it gives: what stopped it, then the way on, as New Project says it.</summary>
        private static string? StepRefused(RejectionCode? refusal)
        {
            if (StepFolder(refusal, null) is string folder) return folder;
            if (refusal == RejectionCode.ProjectNotFound) return "The project is no longer there. Choose another.";
            if (refusal == RejectionCode.WorkstreamNotFound) return "The task is no longer there.";
            if (refusal == RejectionCode.RuntimeNotFound) return "The agent app isn't available. Set it up on the computer, then try again.";
            if (refusal == RejectionCode.ModelRequired) return "It needs a model. Choose one in More Options.";
            if (refusal == RejectionCode.InvalidRuntimeOptions) return "The computer refused the run settings. Choose them again.";
            if (refusal == RejectionCode.CapabilityUnsupported) return "The agent app can't do this. Change the agent app.";
            if (refusal == RejectionCode.InvalidState) return "That isn't possible right now. Check the stage.";
            return null;
        }

        /// <summary>A step refused for a reason New Project has no words for: that it was refused, and that nothing changed.</summary>
        public const string StepRefusedOther = "The computer refused this, and nothing changed. Check the stage.";

        /// <summary>A step stopped over its folder, from the host's refusal or a failure's code.</summary>
        private static string? StepFolder(RejectionCode? refusal, string? failure)
        {
            if (refusal == RejectionCode.LocationRequired || failure == "location_required") return "No folder is chosen. Choose one.";
            if (refusal == RejectionCode.LocationMissing || failure == "location_missing") return "The folder can't be used. Choose another, or fix it on the computer.";
            if (refusal == RejectionCode.LocationNotAllowed || failure == "location_not_allowed") return "That folder isn't allowed. Choose one from the list.";
            if (refusal == RejectionCode.LocationExists || failure == "location_exists") return "A folder with that name is already there. Use it, or rename.";
            if (failure == "location_not_created") return "The folder wasn't made, and nothing changed. Choose another.";
            return null;
        }

        /// <summary>A step that failed with no effect, from a code every agent app's adapter shares.</summary>
        private static string? StepFailed(string? failure)
        {
            if (failure == "runtime_unreachable" || failure == "runtime_closed") return "The agent app stopped answering. Check it, then try again.";
            if (failure == "capability_unimplemented") return "The agent app can't do this. Change the agent app.";
            if (failure == "runtime_unavailable") return "The agent app isn't available. Check its setup, then try again.";
            if (failure == "runtime_version_unsupported") return "This version of the agent app isn't supported. Change the agent app.";
            if (failure == "model_unavailable") return "It needs a model. Choose one in More Options.";
            return null;
        }

        /// <summary>A step that failed with no effect and no cause a person can act on.</summary>
        public const string StepFailedOther = "It failed, and nothing changed. Try again.";

        /// <summary>A step not sent because this headset is not connected.</summary>
        public const string StepNotSent = "It wasn't sent: this headset isn't connected. Try again.";

        /// <summary>Where a model runs, which decides where the person's code and instructions go.</summary>
        public static string Served(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "On Your Computer",
            ModelServed.Remote => RemoteLeaves,
            _ => UnknownMayLeave,
        };

        /// <summary>Where a remote model runs, and what that means for the person's code, as a sentence (JUDGE, 2026-11-13T0851Z: never on a dot).</summary>
        public const string RemoteLeaves = "Runs on a remote service: your code and instructions leave this computer";

        /// <summary>A model or an agent app whose model runs where Halcyonic cannot tell.</summary>
        public const string UnknownMayLeave = "Where it runs is unknown: your code may leave this computer";

        /// <summary>
        /// The line between a runtime's models on this Mac and the rest, which take a second press:
        /// where they run decides where the person's code and instructions go.
        /// </summary>
        public static string ElsewhereDivider(IEnumerable<RuntimeModel> elsewhere) =>
            elsewhere.All(model => model.Served == ModelServed.Remote)
                ? "These run on a remote service: your code and instructions leave this computer"
                : "These may not run on this computer: your code may leave it";

        /// <summary>The first press on a model that runs elsewhere, under its name: what choosing it means, and how.</summary>
        public static string ConfirmElsewhere(RuntimeModel model) => Served(model.Served) + ". Press again to use it.";

        /// <summary>Where a model runs, in a model's row.</summary>
        public static string ServedShort(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "On Your Computer",
            ModelServed.Remote => "Remote Service",
            _ => "Location Unknown",
        };

        /// <summary>Where a model runs, inside a sentence of the whole request.</summary>
        public static string ServedInSentence(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "On Your Computer",
            ModelServed.Remote => "Remote Service",
            _ => "Location Unknown",
        };

        /// <summary>Whether a model says it can use tools, inside a row's line or the whole request.</summary>
        public static string Tools(ModelToolCalling value) => value switch
        {
            ModelToolCalling.Declared => "Uses Tools",
            ModelToolCalling.NotDeclared => "Tool Use Unstated",
            _ => "Tool Use Unknown",
        };

        /// <summary>The recap's note after focus left a review whose final press waited.</summary>
        public const string ReviewAfresh = "Not Started · Window Changed · Recheck";

        /// <summary>A simulated runtime in a live session, named for what it does, so no one takes it for a real one.</summary>
        public const string PracticeRun = "Practice Run";

        /// <summary>Under <see cref="PracticeRun"/> in the choice: what it is, in a few words.</summary>
        public const string PracticeDetail = "Builds nothing: no agent and no files";

        /// <summary>
        /// A runtime as a choice: its name as it arrives, and whether its work is simulated. In a live
        /// session a simulated runtime is named <see cref="PracticeRun"/>; the recorded demonstration,
        /// where nothing starts, keeps its runtimes' names.
        /// </summary>
        /// <param name="plain">Shows the runtime's own name through <see cref="LabelText.Name"/>, which never leaves it empty; false leaves it as it is, for a review that spells it itself.</param>
        /// <summary>An agent app's name as runs: "Simulated: " upright first, its name leaning; the practice run Halcyonic's own words.</summary>
        public static LineWords RuntimeNameRuns(RuntimeDescriptor runtime, bool live = false) =>
            runtime.Synthetic && live ? LineWords.Ours(PracticeRun) : (runtime.Synthetic ? LineWords.Ours("Simulated · ") : LineWords.None) + LineWords.TheirName(CaptureNames.AgentName(runtime.DisplayName));

        /// <summary>How it runs as runs (<see cref="RunsWith"/>): an agent app's name leaning.</summary>
        public static LineWords RunsWithRuns(NewWorkDraft draft, bool live = false) =>
            draft.Runtime is RuntimeDescriptor runtime && (runtime.Synthetic || runtime.ModelChoice != ModelChoice.Listed)
                ? RuntimeNameRuns(runtime, live)
                : LineWords.Written(RunsWith(draft, live));

        /// <summary>A new folder's words over its name as runs: "New folder in " upright, the place's name leaning.</summary>
        public static LineWords NewFolderInRuns(LocationRoot root) => ProjectFolder.NewFolderRuns(root);

        public static string RuntimeName(RuntimeDescriptor runtime, bool live = false, bool plain = true) =>
            runtime.Synthetic && live ? PracticeRun : (runtime.Synthetic ? "Simulated · " : "") + (plain ? LabelText.Name(CaptureNames.AgentName(runtime.DisplayName)) : CaptureNames.AgentName(runtime.DisplayName));

        /// <summary>The runtimes offered in Create: real ones first, by name, and simulated ones after them.</summary>
        public static List<RuntimeDescriptor> RuntimeChoices(IEnumerable<RuntimeDescriptor> runtimes) =>
            runtimes.Where(runtime => runtime.Capabilities.StartExecution)
                .OrderBy(runtime => runtime.Synthetic)
                .ThenBy(runtime => runtime.DisplayName, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// How it runs, for the recap, under its own heading: where the work's code and instructions go,
        /// or what is still to choose. The agent app's and the model's names show in More options and in
        /// the whole request; a real agent app that picks its own model is named, since where its model
        /// runs isn't known here.
        /// </summary>
        public static string RunsWith(NewWorkDraft draft, bool live = false)
        {
            var runtime = draft.Runtime;
            if (runtime == null) return "Not Chosen";
            if (runtime.Synthetic) return RuntimeName(runtime, live);
            if (runtime.ModelChoice != ModelChoice.Listed) return RuntimeName(runtime, live);
            var model = draft.Model;
            if (model == null) return "Unfinished";
            return model.Served switch
            {
                ModelServed.ThisMac => "On Your Computer",
                ModelServed.Remote => "Remote Service",
                _ => "Location Unknown",
            };
        }

        /// <summary>
        /// Under <see cref="RunsWith"/> in the recap: what that means for the person's code and
        /// instructions, said every time, or where to finish choosing. Nothing is chosen for the person
        /// but a model on their Mac, and that says so.
        /// </summary>
        public static string ModelLine(NewWorkDraft draft)
        {
            var runtime = draft.Runtime;
            if (runtime == null) return ChooseHowItRuns;
            if (runtime.Synthetic) return "A practice run: no agent and no files";
            if (runtime.ModelChoice != ModelChoice.Listed) return UnknownMayLeave;
            var model = draft.Model;
            if (model == null) return FinishChoosing;
            if (draft.ModelPreselected) return ModelChosenForYou;
            return model.Served switch
            {
                ModelServed.ThisMac => "Runs on Your Computer",
                ModelServed.Remote => RemoteLeaves,
                _ => UnknownMayLeave,
            };
        }

        /// <summary>A model taken for the person, said so, and where to change it.</summary>
        public const string ModelChosenForYou = "Chosen for you. Change it in More Options.";

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>What a folder fact says the project's folder is (<see cref="EntryText.FolderFactPartsOf"/>).</summary>
    public enum FolderFactKind
    {
        /// <summary>A folder the host makes when the project is created.</summary>
        NewFolder,
        /// <summary>A new folder made from the VR starter.</summary>
        NewStarter,
        /// <summary>A folder the host listed.</summary>
        Existing,
        /// <summary>Directly in a place, no folder inside it.</summary>
        Directly,
        /// <summary>The project's own folder, unchanged.</summary>
        ProjectFolder,
        NotChosen,
        NotNeeded,
    }

    /// <summary>A folder fact apart: its kind, the folder's name, its place and the folder now where it moves (A17).</summary>
    public sealed class FolderFactParts
    {
        public FolderFactParts(FolderFactKind kind, LineWords? name, LineWords? place, LineWords? now)
        {
            Kind = kind;
            Name = name;
            Place = place;
            Now = now;
        }

        public FolderFactKind Kind { get; }

        /// <summary>Whether the host makes the folder.</summary>
        public bool IsNew => Kind is FolderFactKind.NewFolder or FolderFactKind.NewStarter;

        /// <summary>The folder's name, leaning; null directly in a place or where none is chosen.</summary>
        public LineWords? Name { get; }

        /// <summary>The place it is in, leaning (or Place Unlisted, ours); null where there is no choice.</summary>
        public LineWords? Place { get; }

        /// <summary>Where the request moves the project: the folder it is in now, leaning; null otherwise.</summary>
        public LineWords? Now { get; }
    }
}
