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
        public const string ConnectProjects = "Connect projects";
        public const string CreateProject = "Create a project";
        public const string CreateInvite = "Start from an idea";
        public const string NotNow = "Not now";

        public const string ShowAll = "Show all";
        public const string AddTask = "Add a task";
        public const string WaitingForMac = "Waiting for " + HostText.Your + ".";

        /// <summary>Why Start building can't be pressed while a build is on its way (the coordinator, 2026-10-02).</summary>
        public const string AlreadyStarting = "Already starting. Wait to hear how it went.";

        public const string IdeaPrompt = "What would you like to make?";
        public const string WorkPrompt = "What should this task do?";
        public const string TypeIdea = "Type my idea";

        /// <summary>New project's row for the fixed questions, and the main action while it is chosen (ADR 0026).</summary>
        public const string AnswerQuestions = "Answer a few questions";
        public const string StartQuestions = "Start the questions";

        /// <summary>The fixed questions' main action, and why it can't be pressed before an answer is chosen (the coordinator, 2026-10-02).</summary>
        public const string NextQuestion = "Next question";
        public const string ChooseOrTypeFirst = "Choose or type an answer first.";

        /// <summary>Typing a name: the fixed name question's own answer, and the words page's row before there is one.</summary>
        public const string TypeName = "Type a name";

        /// <summary>What a name or first task must be, said where it can't be kept as it is.</summary>
        public const string NameRule = "Name the project in at most 200 characters.";
        public const string DescribeTask = "Describe the first task.";
        public const string ShortenTask = "Shorten the first task to at most 4,000 characters.";

        /// <summary>The words page's line for a new folder: the folder list's own words for it.</summary>
        public static string NewFolderIn(LocationRoot root) => ProjectFolder.NewFolderLabel(root);

        /// <summary>Typing a first task of one's own in place of the companion's suggestion and one's earlier words.</summary>
        public const string TypeMyOwn = "Type my own";
        public const string NothingStartsYet = "Nothing starts until you choose Start building.";
        public const string GuideNote = "Fixed questions, not an AI. You can change every answer.";
        public const string Back = "Back";

        /// <summary>Under the answer given before, and under every choice made in a list: one word marks a choice everywhere.</summary>
        public const string Chosen = "Chosen";


        public const string RecapLine = "Change anything before you start building.";
        public const string StartBuilding = "Start building";
        public const string StartOver = "Start over";
        public const string StartOverQuestion = "This clears your idea and every choice here.";
        public const string ConfirmStartOver = "Yes, start over";
        public const string MoreOptions = "More options";
        public const string ProjectName = "Project name";
        public const string FirstTask = "First task";
        public const string NotNamedYet = "not named yet";
        public const string HowItRuns = "How it runs";
        public const string NameTheProject = "Name the project";
        public const string WhatFirstTask = "What should the first task be?";

        public const string FolderTitle = "Where its files live";
        public const string FolderLine = "Folders " + HostText.Your + " allows for projects. A new folder starts empty.";
        public const string ReadingFolders = "Reading the folders " + HostText.Your + " allows…";
        public const string NoFolders = HostText.YourStart + " doesn't allow any folder yet. Allow one on " + HostText.Your + ", then press Try again.";
        public const string FoldersCut = HostText.YourStart + " lists only the first 200 folders in a place.";
        public const string NewFolderPrompt = "Name the new folder";
        public const string NewFolderRule = "Use up to 64 letters, digits, dots, dashes or underscores. Start with a letter or digit.";
        public const string UseThatFolder = "Use that folder";
        public const string ChooseAnotherFolder = "Choose a folder";
        public const string RebindWarning = "Every later task in this project uses the new folder. Tasks already running keep theirs.";

        /// <summary>The folders couldn't be read: what went wrong, as it arrived, and what to do.</summary>
        public static string FoldersUnread(string reason) => "Couldn't read " + HostText.Your + "'s folders: " + LabelText.Plain(reason) + " Press Try again.";

        /// <summary>
        /// Where the project's files live, for the recap, under its own heading: the folder chosen, the
        /// project's own, or what is still to choose. <paramref name="current"/> is an existing
        /// project's folder; <paramref name="needed"/> says whether what runs it works in a project folder.
        /// </summary>
        public static string FolderFact(ProjectLocation? current, ProjectFolder? chosen, bool needed)
        {
            if (chosen != null && current != null) return LabelText.Plain(current.Name) + " now, " + chosen.Describe() + " from now on";
            if (chosen != null) return chosen.Describe(startOfLine: true);
            if (current != null) return LabelText.Plain(current.Name) + ", the project's folder";
            return needed ? "Not chosen yet" : "Not needed";
        }

        /// <summary>
        /// What to do after a refusal or failure about a folder, from its code, never from the control
        /// plane's own message; null for any other code.
        /// </summary>
        public static string? FolderProblem(RejectionCode? refusal, string? failure)
        {
            if (refusal == RejectionCode.LocationRequired || failure == "location_required")
                return "This project has no folder on " + HostText.Your + " yet. Choose where its files live, then try again.";
            if (refusal == RejectionCode.LocationMissing || failure == "location_missing")
                // The host also answers this for a folder or place that is there but cannot be read.
                return HostText.YourStart + " can't use that folder right now: it may have moved, or it can't be read. Choose it again, or fix it on " + HostText.Your + ".";
            if (refusal == RejectionCode.LocationNotAllowed || failure == "location_not_allowed")
                return HostText.YourStart + " doesn't let agents work there. Choose a folder it lists.";
            if (refusal == RejectionCode.LocationExists || failure == "location_exists")
                return "There's already a folder with that name. Use that folder, or choose another name.";
            if (failure == "location_not_created")
                return HostText.YourStart + " couldn't make that folder, so nothing was created. Choose another name or place.";
            return null;
        }

        /// <summary>The step stopped over its folder, so choosing another is the next action.</summary>
        public static bool AboutFolder(BuildStep step) => !step.EffectUnknown && FolderProblem(step.Refusal, step.Failure) != null;

        public const string OptionsLine = "Choose the agent app that does the work, then its model. Each model says where it runs.";
        public const string NoRuntimes = "No agent app on " + HostText.Your + " can start work right now. Set one up on " + HostText.Your + ", then open this again.";
        public const string ChangeAgentApp = "Change agent app";
        public const string ListsModels = "You choose its model";
        public const string ChoosesModel = "It chooses its model";
        public const string NoModels = "This agent app offers no models. Choose another agent app.";
        public const string ChosenForYou = "Chosen for you";

        /// <summary>A simulated agent app in the recorded demonstration, which keeps its name.</summary>
        public const string Practice = "Practice";
        public const string Done = "Done";

        /// <summary>Under the review's title: the items are Halcyonic's own account of what will be sent, so it asks to check them, never claims to be the command.</summary>
        public const string ReviewLine = "Check every part before you start.";
        public const string ConfirmStart = "Yes, start building";
        public const string Change = "Change";

        /// <summary>The final press, locked in its place until the person has seen the last part: what is left to do, not a part's name.</summary>
        public static string ReadToPart(int pages) => "Read to part " + Count(pages) + " first";

        /// <summary>New project's row to the review's next part, which replaces <see cref="ReadToPart"/> (ADR 0026).</summary>
        public static string NextPart(int part, int parts) => "Next part, " + Count(part) + " of " + Count(parts);

        /// <summary>The pager's words, everywhere in the entry panel: a word with its direction, never Back, which leaves the screen.</summary>
        public const string Previous = "Previous";
        public const string Next = "Next";

        public static string Page(int page, int pages) => Count(page + 1) + " of " + Count(pages);

        public static string Part(int page, int pages) => "Part " + Count(page + 1) + " of " + Count(pages);

        public const string SendingLine = "Work already running keeps going.";
        public const string TryAgain = "Try again";
        public const string Started = "Confirmed: it started. Find it on the stage.";

        public const string PreviousRequestTitle = "Not sure it happened";
        public const string PreviousRequestLine = "Your last start may have gone through. Check the tasks on the stage first: starting again could do it twice.";

        /// <summary>On Starting your work, where it only opens Not sure it happened.</summary>
        public const string CheckFirst = "Next";

        /// <summary>The first of the two presses that clear a start whose outcome is unknown.</summary>
        public const string Clear = "Clear";
        public const string ClearOnceChecked = "Clear this once you've checked.";
        public const string ClearOnlyAfterChecking = "Clear only after you've checked the tasks on the stage.";
        public const string ConfirmClear = "Yes, clear";
        public const string Cancel = "Cancel";
        public const string Cleared = "Cleared. Start again from your idea.";

        /// <summary>The command whose outcome is unknown, by its id.</summary>
        public static string Reference(string commandId) => "Reference: " + commandId;

        /// <summary>What the Mac recorded of a start whose outcome is unknown, in words: a failure may still have changed something.</summary>
        public static string Recorded(CommandStatus? status) => status switch
        {
            null => HostText.YourStart + " hasn't confirmed it yet.",
            CommandStatus.Accepted => HostText.YourStart + " took it, but it isn't confirmed yet.",
            CommandStatus.Completed => "Confirmed: it went through.",
            CommandStatus.Rejected => HostText.YourStart + " refused it, so it didn't happen.",
            CommandStatus.Failed => "It didn't go through, but it may have changed something. Check the tasks on the stage.",
            _ => HostText.YourStart + " answered in a way this app didn't expect. Check the tasks on the stage.",
        };

        public const string OpenNow = "Open now";
        public const string KeepCreating = "Keep creating";

        public const string Move = "Move";
        public const string ResetPosition = "Reset position";
        public const string Close = "Close";

        /// <summary>Why Start building can't go ahead, for each check the recap makes, in its order.</summary>
        public const string DemoCannotStart = "The demo can't start new work. Real work runs on " + HostText.Your + ".";
        public const string ChooseHowItRuns = "Choose how it runs in More options.";
        public const string ChooseAgain = "What you chose in More options isn't available now. Choose again.";
        public const string FinishChoosing = "Finish choosing how it runs in More options.";
        public const string ProjectGone = "This project isn't on " + HostText.Your + " any more. Close this, then choose another in Projects.";
        public const string ChooseWhereFilesLive = "Choose where its files live.";

        public static string CreateTitle(string? existingProject) => existingProject == null ? CreateProject : "New task in " + existingProject;

        public static string Question(int index, int count) => "Question " + Count(index + 1) + " of " + Count(count);


        /// <summary>How many tasks wait for the person, said as a person would: "1 task is waiting for you", "2 tasks are waiting for you".</summary>
        public static string WaitingForYou(int count) => Tasks(count) + (count == 1 ? " is" : " are") + " waiting for you";

        private static string Tasks(int count) => Count(count) + (count == 1 ? " task" : " tasks");

        public static string StepName(BuildStepKind kind, bool newProject) => kind switch
        {
            BuildStepKind.CreateProject => "Create the project",
            BuildStepKind.BindFolder => "Move the project to its folder",
            BuildStepKind.CreateWorkstream => newProject ? "Create its first task" : "Create the task",
            _ => "Start the task",
        };

        /// <summary>A start whose effect is unknown is never said to have had none.</summary>
        public const string NotSureItHappened = "Not sure it happened. Check the tasks on the stage before you try again.";

        /// <summary>
        /// How a step of Start building went: sent is not done, and only a completed record confirms.
        /// A refusal or failure about a folder says what to do, from its code; any other shows the
        /// control plane's reason, as <see cref="LabelText.Plain"/> shows it.
        /// </summary>
        public static string StepStatus(BuildStep step) => step.Status switch
        {
            BuildStepStatus.NotYet => "Not sent yet",
            BuildStepStatus.Waiting => step.Kind == BuildStepKind.StartWork ? "Sent. Waiting for the agent…" : "Sent. Waiting for " + HostText.Your + "…",
            BuildStepStatus.Confirmed => "Confirmed",
            BuildStepStatus.Refused => "Couldn't do that: " + (FolderProblem(step.Refusal, step.Failure) ?? LabelText.Plain(step.Reason ?? "no reason given")),
            // A failure that may have had an effect is never put in words that say nothing happened.
            BuildStepStatus.Failed when step.EffectUnknown => NotSureItHappened,
            BuildStepStatus.Failed => "Couldn't do that: " + (FolderProblem(step.Refusal, step.Failure) ?? LabelText.Plain(step.Reason ?? "no reason given")),
            BuildStepStatus.Unknown => NotSureItHappened,
            BuildStepStatus.NotSent => "Couldn't send: " + HostText.Your + " isn't connected. Try again when it is.",
            _ => NotSureItHappened,
        };

        /// <summary>Where a model runs, which decides where the person's code and instructions go.</summary>
        public static string Served(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "Runs on " + HostText.Your,
            ModelServed.Remote => "Runs on a remote service: your code and instructions go there",
            _ => "Where it runs isn't known: your code and instructions may go elsewhere",
        };

        /// <summary>
        /// The line between a runtime's models on this Mac and the rest, which take a second press:
        /// where they run decides where the person's code and instructions go.
        /// </summary>
        public static string ElsewhereDivider(IEnumerable<RuntimeModel> elsewhere) =>
            elsewhere.All(model => model.Served == ModelServed.Remote)
                ? "Runs on a remote service: your code and instructions go there."
                : "These may not run on " + HostText.Your + ": your code and instructions may go elsewhere.";

        /// <summary>The first press on a model that runs elsewhere, under its name: what choosing it means, and how.</summary>
        public static string ConfirmElsewhere(RuntimeModel model) => Served(model.Served) + ". Press again to use it.";

        /// <summary>Where a model runs, in a model's row.</summary>
        public static string ServedShort(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "Runs on " + HostText.Your,
            ModelServed.Remote => "Runs on a remote service",
            _ => "Where it runs isn't known",
        };

        /// <summary>Where a model runs, inside a sentence of the whole request.</summary>
        public static string ServedInSentence(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "on " + HostText.Your,
            ModelServed.Remote => "on a remote service",
            _ => "not known",
        };

        /// <summary>Whether a model says it can use tools, inside a row's line or the whole request.</summary>
        public static string Tools(ModelToolCalling value) => value switch
        {
            ModelToolCalling.Declared => "can use tools",
            ModelToolCalling.NotDeclared => "tool use not stated",
            _ => "tool use not known",
        };

        /// <summary>The recap's note after focus left a review whose final press waited.</summary>
        public const string ReviewAfresh = "You went to another window, so nothing was started. Press Start building to check it again.";

        /// <summary>A simulated runtime in a live session, named for what it does, so no one takes it for a real one.</summary>
        public const string PracticeRun = "Practice run: builds nothing";

        /// <summary>Under <see cref="PracticeRun"/> in the choice: what it is, in a few words.</summary>
        public const string PracticeDetail = "No agent, no files";

        /// <summary>
        /// A runtime as a choice: its name as it arrives, and whether its work is simulated. In a live
        /// session a simulated runtime is named <see cref="PracticeRun"/>; the recorded demonstration,
        /// where nothing starts, keeps its runtimes' names.
        /// </summary>
        /// <param name="plain">Shows the runtime's own name through <see cref="LabelText.Plain"/>; false leaves it as it is, for a review that spells it itself.</param>
        public static string RuntimeName(RuntimeDescriptor runtime, bool live = false, bool plain = true) =>
            runtime.Synthetic && live ? PracticeRun : (plain ? LabelText.Plain(runtime.DisplayName) : runtime.DisplayName) + (runtime.Synthetic ? " (simulated)" : "");

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
            if (runtime == null) return "Not chosen yet";
            if (runtime.Synthetic) return RuntimeName(runtime, live);
            if (runtime.ModelChoice != ModelChoice.Listed) return RuntimeName(runtime, live);
            var model = draft.Model;
            if (model == null) return "Not finished choosing";
            return model.Served switch
            {
                ModelServed.ThisMac => "On " + HostText.Your,
                ModelServed.Remote => "On a remote service",
                _ => "Not known if on " + HostText.Your,
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
            if (runtime == null) return "Choose how it runs in More options. Nothing is chosen for you.";
            if (runtime.Synthetic) return "Practice: no agent, no files.";
            if (runtime.ModelChoice != ModelChoice.Listed) return "Where it runs isn't known: your code and instructions may go elsewhere.";
            var model = draft.Model;
            if (model == null) return FinishChoosing;
            if (draft.ModelPreselected) return "Chosen for you. Change it in More options.";
            return model.Served switch
            {
                ModelServed.ThisMac => "It runs on " + HostText.Your + ".",
                ModelServed.Remote => "It runs on a remote service: your code and instructions go there.",
                _ => "Where it runs isn't known: your code and instructions may go elsewhere.",
            };
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
