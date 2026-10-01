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
        public const string MoreTasks = "More tasks";
        public const string ContinueCreating = "Continue creating";

        /// <summary>The rail's button for the tasks without a character, a verb first (ADR 0023).</summary>
        public const string SeeOtherTasks = "See other tasks";

        public const string WelcomeTitle = "Welcome";
        public const string WelcomeLine = "Show projects from your Mac, or make a new one. Work already running keeps going.";
        public const string ConnectInvite = "Choose which projects show";
        public const string CreateInvite = "Start from an idea";
        public const string NotNow = "Not now";

        public const string ConnectLine = "Projects already set up on your Mac. Choose which ones show on the stage.";
        public const string NoProjects = "No projects on your Mac yet. Create one here, or start one from your Mac.";
        public const string LastKnownProjects = "Last known: your Mac isn't connected right now.";
        public const string ExampleProjects = "Demo projects. Nothing here reaches an agent.";
        public const string ShowAll = "Show all";
        public const string AddTask = "Add a task";
        public const string WaitingForMac = "Waiting for your Mac.";

        public const string MoreTasksLine = "Tasks that aren't on the stage right now. Anything waiting for you is at the top. Choose one to open it.";
        public const string AllOnStage = "Every task is on the stage.";

        public const string IdeaPrompt = "What would you like to make?";
        public const string WorkPrompt = "What should this task do?";
        public const string TypeIdea = "Type my idea";
        public const string TypeIdeaInvite = "In your own words";
        public const string HelpMe = "Help me figure it out";
        public const string HelpMeInvite = "A few fixed questions";
        public const string NothingStartsYet = "Nothing starts until you choose Start building.";
        public const string GuideNote = "Fixed questions, not an AI. You can change every answer.";
        public const string Back = "Back";

        /// <summary>Under the answer given before, and under every choice made in a list: one word marks a choice everywhere.</summary>
        public const string Chosen = "Chosen";

        /// <summary>The editor has no system keyboard; on the headset this never shows.</summary>
        public const string NoKeyboard = "There's no keyboard here, so typing isn't possible. Use the headset to type.";

        public const string RecapTitle = "Check your project";
        public const string WorkRecapTitle = "Check your new task";
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
        public const string FolderLine = "Folders your Mac allows for projects. A new folder starts empty.";
        public const string ReadingFolders = "Reading the folders your Mac allows…";
        public const string NoFolders = "Your Mac doesn't allow any folder yet. Allow one on your Mac, then press Try again.";
        public const string FoldersCut = "Your Mac lists only the first 200 folders in a place.";
        public const string NewFolderPrompt = "Name the new folder";
        public const string NewFolderRule = "Use up to 64 letters, digits, dots, dashes or underscores. Start with a letter or digit.";
        public const string ChooseFolder = "Choose";
        public const string UseThatFolder = "Use that folder";
        public const string ChooseAnotherFolder = "Choose a folder";
        public const string RebindWarning = "Every later task in this project uses the new folder. Tasks already running keep theirs.";

        /// <summary>The folders couldn't be read: what went wrong, as it arrived, and what to do.</summary>
        public static string FoldersUnread(string reason) => "Couldn't read your Mac's folders: " + LabelText.Plain(reason) + " Press Try again.";

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
                return "This project has no folder on your Mac yet. Choose where its files live, then try again.";
            if (refusal == RejectionCode.LocationMissing || failure == "location_missing")
                // The host also answers this for a folder or place that is there but cannot be read.
                return "Your Mac can't use that folder right now: it may have moved, or it can't be read. Choose it again, or fix it on your Mac.";
            if (refusal == RejectionCode.LocationNotAllowed || failure == "location_not_allowed")
                return "Your Mac doesn't let agents work there. Choose a folder it lists.";
            if (refusal == RejectionCode.LocationExists || failure == "location_exists")
                return "There's already a folder with that name. Use that folder, or choose another name.";
            if (failure == "location_not_created")
                return "Your Mac couldn't make that folder, so nothing was created. Choose another name or place.";
            return null;
        }

        /// <summary>The step stopped over its folder, so choosing another is the next action.</summary>
        public static bool AboutFolder(BuildStep step) => !step.EffectUnknown && FolderProblem(step.Refusal, step.Failure) != null;

        public const string OptionsTitle = "How it runs";
        public const string OptionsLine = "Choose the agent app that does the work, then its model. Each model says where it runs.";
        public const string NoRuntimes = "No agent app on your Mac can start work right now. Set one up on your Mac, then open this again.";
        public const string ChangeAgentApp = "Change agent app";
        public const string ListsModels = "You choose its model";
        public const string ChoosesModel = "It chooses its model";
        public const string NoModels = "This agent app offers no models. Choose another agent app.";
        public const string ChosenForYou = "Chosen for you";

        /// <summary>A simulated agent app in the recorded demonstration, which keeps its name.</summary>
        public const string Practice = "Practice";
        public const string Done = "Done";

        public const string ReviewTitle = "Check before starting";
        public const string ReviewLine = "This is exactly what will be sent.";
        public const string ConfirmStart = "Yes, start building";
        public const string Change = "Change";

        /// <summary>The final press, locked in its place until the person has seen the last part.</summary>
        public static string ReadToPart(int pages) => "Read part " + Count(pages) + " of " + Count(pages);

        /// <summary>The pager's words, everywhere in the entry panel: a word with its direction, never Back, which leaves the screen.</summary>
        public const string Previous = "Previous";
        public const string Next = "Next";

        public static string Page(int page, int pages) => Count(page + 1) + " of " + Count(pages);

        public static string Part(int page, int pages) => "Part " + Count(page + 1) + " of " + Count(pages);

        public const string SendingTitle = "Starting your work";
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
            null => "Your Mac hasn't confirmed it yet.",
            CommandStatus.Accepted => "Your Mac took it, but it isn't confirmed yet.",
            CommandStatus.Completed => "Confirmed: it went through.",
            CommandStatus.Rejected => "Your Mac refused it, so it didn't happen.",
            CommandStatus.Failed => "It didn't go through, but it may have changed something. Check the tasks on the stage.",
            _ => "Your Mac answered in a way this app didn't expect. Check the tasks on the stage.",
        };

        public const string OpenNow = "Open now";
        public const string KeepCreating = "Keep creating";

        public const string Move = "Move";
        public const string ResetPosition = "Reset position";
        public const string Close = "Close";

        /// <summary>Why Start building can't go ahead, for each check the recap makes, in its order.</summary>
        public const string DemoCannotStart = "The demo can't start new work. Connect your Mac to start real work.";
        public const string ChooseHowItRuns = "Choose how it runs in More options.";
        public const string ChooseAgain = "What you chose in More options isn't available now. Choose again.";
        public const string FinishChoosing = "Finish choosing how it runs in More options.";
        public const string ProjectGone = "This project isn't on your Mac any more. Close this, then choose a project in Connect projects.";
        public const string ChooseWhereFilesLive = "Choose where its files live.";

        public static string CreateTitle(string? existingProject) => existingProject == null ? CreateProject : "New task in " + existingProject;

        public static string Question(int index, int count) => "Question " + Count(index + 1) + " of " + Count(count);

        /// <summary>The banner while creating, naming the work that came to wait for the person.</summary>
        public static string WaitingNow(string title) => "“" + title + "” is waiting for you.";

        /// <summary>How many tasks wait for the person, said as a person would: "1 task is waiting for you", "2 tasks are waiting for you".</summary>
        public static string WaitingForYou(int count) => Tasks(count) + (count == 1 ? " is" : " are") + " waiting for you";

        /// <summary>
        /// A project's work in words, what waits for the person first: one kind as a sentence, "1 task
        /// is waiting for you"; several as a list that names tasks once, "1 task waiting for you, 1
        /// finished, 2 running", short enough for a row.
        /// </summary>
        public static string Counts(ProjectSummary project)
        {
            var kinds = (project.NeedsYou > 0 ? 1 : 0) + (project.Notice > 0 ? 1 : 0) + (project.Active > 0 ? 1 : 0);
            if (kinds == 0) return project.Work == 0 ? "no work yet" : Paused(project.Work);
            if (kinds == 1)
            {
                return project.NeedsYou > 0 ? WaitingForYou(project.NeedsYou) : project.Notice > 0 ? Finished(project.Notice) : Running(project.Active);
            }
            var parts = new List<string>();
            if (project.NeedsYou > 0) parts.Add(Tasks(project.NeedsYou) + " waiting for you");
            if (project.Notice > 0) parts.Add((parts.Count == 0 ? Tasks(project.Notice) : Count(project.Notice)) + " finished");
            if (project.Active > 0) parts.Add((parts.Count == 0 ? Tasks(project.Active) : Count(project.Active)) + " running");
            return string.Join(", ", parts);
        }

        private static string Tasks(int count) => Count(count) + (count == 1 ? " task" : " tasks");

        private static string Finished(int count) => Tasks(count) + " finished, ready to look at";

        private static string Running(int count) => Tasks(count) + " running";

        private static string Paused(int count) => Tasks(count) + " paused";

        /// <summary>A project's line in Connect projects: whether it shows, then its work.</summary>
        public static string ProjectDetail(ProjectSummary project) => (project.Shown ? "Shown" : "Hidden") + " · " + Counts(project);

        /// <summary>
        /// A project's rail chip, short enough for its small button, about 20 characters: only what
        /// matters most, what waits for the person first, "1 task waiting". A hidden project's chip
        /// says so first, which leaves no room for "task": "Hidden · 1 waiting". Connect projects says
        /// it in full where it has room.
        /// </summary>
        public static string ChipDetail(ProjectSummary project)
        {
            string Many(int count) => project.Shown ? Tasks(count) : Count(count);
            var most = project.NeedsYou > 0 ? Many(project.NeedsYou) + " waiting"
                : project.Notice > 0 ? Many(project.Notice) + " finished"
                : project.Active > 0 ? Many(project.Active) + " running"
                : project.Work == 0 ? "no work yet" : Many(project.Work) + " paused";
            return project.Shown ? most : "Hidden · " + most;
        }

        /// <summary>
        /// A project's rail chip in full, where the chip has room for it: its work as Connect projects
        /// says it, "1 task is waiting for you", after "Hidden · " when its work is not on the stage.
        /// Where it does not fit, the rail says <see cref="ChipDetail"/>.
        /// </summary>
        public static string ChipDetailInFull(ProjectSummary project) => (project.Shown ? "" : "Hidden · ") + Counts(project);

        /// <summary>
        /// The rail's See other tasks detail in full, where it has room: how many of the tasks not on
        /// the stage wait for the person, "1 task is waiting for you", else how many there are, "3
        /// tasks not on the stage". Where it does not fit, the rail says <see cref="MoreWorkDetail"/>.
        /// </summary>
        public static string MoreWorkDetailInFull(WorkOverview overview)
        {
            var needing = overview.NeedsYouOffStage;
            return needing > 0 ? WaitingForYou(needing) : Tasks(overview.OffStage.Count) + " not on the stage";
        }

        /// <summary>The rail's Connect projects detail: how many projects show.</summary>
        public static string ConnectDetail(WorkOverview overview)
        {
            var known = overview.Projects.Count;
            if (known == 0) return "none known yet";
            var shown = overview.ShownProjects;
            return shown == known ? (known == 1 ? "1 shown" : "all " + Count(known) + " shown") : Count(shown) + " of " + Count(known) + " shown";
        }

        /// <summary>
        /// The rail's More tasks detail, under its label and as short as a chip's: how many of the tasks
        /// not on the stage wait for the person, "1 task waiting", else how many there are, "3 tasks".
        /// </summary>
        public static string MoreWorkDetail(WorkOverview overview)
        {
            var needing = overview.NeedsYouOffStage;
            return needing > 0 ? Tasks(needing) + " waiting" : Tasks(overview.OffStage.Count);
        }

        /// <summary>A More tasks row's second line: its status, its project, and why it has no character.</summary>
        public static string OffStageDetail(OffStageWork work, bool live)
        {
            var status = CharacterPresenter.LabelOf(CharacterPresenter.ActivityOf(work.Workstream.Status));
            if (!live) status = "Last known: " + status;
            var project = work.ProjectName.Length == 0 ? "" : " · " + work.ProjectName;
            return status + project + (work.Reason == OffStageReason.ProjectHidden ? " · its project is hidden" : " · no room on the stage");
        }

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
            BuildStepStatus.Waiting => step.Kind == BuildStepKind.StartWork ? "Sent. Waiting for the agent…" : "Sent. Waiting for your Mac…",
            BuildStepStatus.Confirmed => "Confirmed",
            BuildStepStatus.Refused => "Couldn't do that: " + (FolderProblem(step.Refusal, step.Failure) ?? LabelText.Plain(step.Reason ?? "no reason given")),
            // A failure that may have had an effect is never put in words that say nothing happened.
            BuildStepStatus.Failed when step.EffectUnknown => NotSureItHappened,
            BuildStepStatus.Failed => "Couldn't do that: " + (FolderProblem(step.Refusal, step.Failure) ?? LabelText.Plain(step.Reason ?? "no reason given")),
            BuildStepStatus.Unknown => NotSureItHappened,
            BuildStepStatus.NotSent => "Couldn't send: your Mac isn't connected. Try again when it is.",
            _ => NotSureItHappened,
        };

        /// <summary>Where a model runs, which decides where the person's code and instructions go.</summary>
        public static string Served(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "Runs on your Mac",
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
                : "These may not run on your Mac: your code and instructions may go elsewhere.";

        /// <summary>The first press on a model that runs elsewhere, under its name: what choosing it means, and how.</summary>
        public static string ConfirmElsewhere(RuntimeModel model) => Served(model.Served) + ". Press again to use it.";

        /// <summary>Where a model runs, in a model's row.</summary>
        public static string ServedShort(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "Runs on your Mac",
            ModelServed.Remote => "Runs on a remote service",
            _ => "Where it runs isn't known",
        };

        /// <summary>Where a model runs, inside a sentence of the whole request.</summary>
        public static string ServedInSentence(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "on your Mac",
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
                ModelServed.ThisMac => "On your Mac",
                ModelServed.Remote => "On a remote service",
                _ => "Not known if on your Mac",
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
                ModelServed.ThisMac => "It runs on your Mac.",
                ModelServed.Remote => "It runs on a remote service: your code and instructions go there.",
                _ => "Where it runs isn't known: your code and instructions may go elsewhere.",
            };
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
