#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of the project rail and the entry panel: the welcome, Connect projects, More work and
    /// Create a project, so the XR layer only lays them out. Actions are plain verbs; statuses are
    /// written out, never left to color; nothing claims more than the state shows: Connect lists the
    /// projects Halcyonic's journal knows and discovers nothing, Help me figure it out is fixed
    /// questions and says so, and a step reads as confirmed only by its completed record. Text
    /// Halcyonic did not write, such as a project's name, is passed in as <see cref="LabelText.Plain"/>
    /// shows it.
    /// </summary>
    public static class EntryText
    {
        public const string ConnectProjects = "Connect projects";
        public const string CreateProject = "Create a project";
        public const string MoreWork = "More work";
        public const string ContinueCreating = "Continue creating";

        public const string WelcomeTitle = "Welcome";
        public const string WelcomeLine = "Bring in projects Halcyonic knows, or make a new one. Work already running keeps going.";
        public const string ConnectInvite = "Choose which projects show";
        public const string CreateInvite = "Start from an idea";
        public const string NotNow = "Not now";

        public const string ConnectLine = "Projects on your Mac that Halcyonic knows. Choose which to show.";
        public const string NoProjects = "Halcyonic knows no projects yet. Create one here, or start work from your Mac.";
        public const string LastKnownProjects = "Last known: your Mac is not connected right now.";
        public const string ExampleProjects = "The interactive example's projects. Nothing here reaches an agent.";
        public const string ShowAll = "Show all";
        public const string AddWork = "Add work";

        public const string MoreWorkLine = "Work without a character on the stage, what needs you first. Choose one to bring it forward and open it.";
        public const string AllOnStage = "All work is on the stage.";

        public const string IdeaPrompt = "What would you like to make?";
        public const string WorkPrompt = "What should this work do?";
        public const string TypeIdea = "Type my idea";
        public const string TypeIdeaInvite = "In your own words";
        public const string HelpMe = "Help me figure it out";
        public const string HelpMeInvite = "A few fixed questions";
        public const string NothingStartsYet = "Nothing starts until you choose Start building.";
        public const string GuideNote = "Fixed questions, not an AI. You can change every answer.";
        public const string Back = "Back";

        public const string RecapTitle = "Check your project";
        public const string WorkRecapTitle = "Check your new work";
        public const string StartBuilding = "Start building";
        public const string MoreOptions = "More options";
        public const string ChooseHowItRuns = "Choose how it runs";

        public const string FolderTitle = "Where its files live";
        public const string FolderLine = "Folders your Mac lets Halcyonic use. A new folder starts empty.";
        public const string ReadingFolders = "Reading the folders your Mac allows.";
        public const string NoFolders = "Your Mac doesn't allow any folder yet. Allow one on your Mac, then choose again.";
        public const string FoldersCut = "Your Mac lists only the first 200 folders in a place.";
        public const string NewFolderPrompt = "Name the new folder";
        public const string NewFolderRule = "Use letters, digits, dots, dashes or underscores, starting with a letter or digit, up to 64.";
        public const string ChooseFolder = "Choose";
        public const string UseThatFolder = "Use that folder";
        public const string ChooseAnotherFolder = "Choose where its files live";
        public const string RebindWarning = "All later work in this project runs in the new folder. Work already running keeps its folder.";

        /// <summary>
        /// Where the project's files live, for the recap: the folder chosen, the project's own, or
        /// what is still to choose. <paramref name="current"/> is an existing project's folder;
        /// <paramref name="needed"/> says whether the chosen runtime works in a project folder.
        /// </summary>
        public static string FolderRecap(ProjectLocation? current, ProjectFolder? chosen, bool needed)
        {
            if (chosen != null && current != null) return "Where its files live: " + LabelText.Plain(current.Name) + " now; " + chosen.Describe() + " after this";
            if (chosen != null) return "Where its files live: " + chosen.Describe();
            if (current != null) return "Where its files live: " + LabelText.Plain(current.Name) + ", the project's folder";
            return needed ? "Where its files live: not chosen yet" : "Where its files live: none needed for this runtime";
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
                return "Your Mac can't use that folder right now: it may have been moved, renamed or deleted, or can't be read. Choose it again, or fix it on your Mac.";
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
        public const string OptionsLine = "Choose what runs the work. Where each model runs is shown beside it.";
        public const string NoRuntimes = "No runtime on your Mac can start work right now.";
        public const string Done = "Done";

        public const string ConfirmStart = "Yes, start building";
        public const string Change = "Change";

        public const string SendingTitle = "Starting your work";
        public const string TryAgain = "Try again";
        public const string Started = "Started: the runtime confirmed it. Its character is on the stage.";

        public const string PreviousRequestTitle = "A previous request";
        public const string ICheckedTheWork = "I checked the work";
        public const string ClearAfterChecking = "Yes, clear after checking";

        public const string OpenNow = "Open now";
        public const string KeepCreating = "Keep creating";

        public const string ResetPosition = "Reset position";
        public const string Close = "Close";

        /// <summary>Where the Move button sends the panel next, from where it is: center, then right, then left.</summary>
        public static string Move(int side) => side == 0 ? "Move right" : side > 0 ? "Move left" : "Move back";

        public static string CreateTitle(string? existingProject) => existingProject == null ? CreateProject : "New work in " + existingProject;

        public static string Question(int index, int count) =>
            "Question " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of " + count.ToString(CultureInfo.InvariantCulture);

        public static string ReviewTitle(int page, int pages) =>
            "Check before starting, part " + (page + 1).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture);

        public static string ProjectLine(string name) => "Project: " + name;

        public static string TaskLine(string task) => "First task: " + task;

        public static string NeedsYouNow(string title) => title + " needs you.";

        /// <summary>A project's work in words, what needs the person first, for example "1 needs you · 2 active".</summary>
        public static string Counts(ProjectSummary project)
        {
            var parts = new List<string>();
            if (project.NeedsYou > 0) parts.Add(Count(project.NeedsYou) + " needs you");
            if (project.Notice > 0) parts.Add(Count(project.Notice) + " to check");
            if (project.Active > 0) parts.Add(Count(project.Active) + " active");
            if (parts.Count > 0) return string.Join(" · ", parts);
            return project.Work == 0 ? "no work yet" : Count(project.Work) + " at rest";
        }

        /// <summary>A project's line in Connect projects: whether it shows, then its work.</summary>
        public static string ProjectDetail(ProjectSummary project) => (project.Shown ? "Shown" : "Hidden") + " · " + Counts(project);

        /// <summary>
        /// A project's rail chip, short enough for its small button: hidden or not, then only what
        /// matters most, what needs the person first. Connect projects shows every count.
        /// </summary>
        public static string ChipDetail(ProjectSummary project)
        {
            var most = project.NeedsYou > 0 ? Count(project.NeedsYou) + " needs you"
                : project.Notice > 0 ? Count(project.Notice) + " to check"
                : project.Active > 0 ? Count(project.Active) + " active"
                : project.Work == 0 ? "no work yet" : "at rest";
            return project.Shown ? most : "Hidden · " + most;
        }

        /// <summary>The rail's Connect projects detail: how many projects show.</summary>
        public static string ConnectDetail(WorkOverview overview)
        {
            var known = overview.Projects.Count;
            if (known == 0) return "none known yet";
            var shown = overview.ShownProjects;
            return shown == known ? (known == 1 ? "1 shown" : "all " + Count(known) + " shown") : Count(shown) + " of " + Count(known) + " shown";
        }

        /// <summary>The rail's More work detail: how much of the work without a character needs the person, else how much there is.</summary>
        public static string MoreWorkDetail(WorkOverview overview)
        {
            var needing = overview.NeedsYouOffStage;
            return needing > 0 ? Count(needing) + " needs you" : Count(overview.OffStage.Count) + " off the stage";
        }

        /// <summary>A More work row's second line: its status, its project, and why it has no character.</summary>
        public static string OffStageDetail(OffStageWork work, bool live)
        {
            var status = CharacterPresenter.LabelOf(CharacterPresenter.ActivityOf(work.Workstream.Status));
            if (!live) status = "Last known: " + status;
            var project = work.ProjectName.Length == 0 ? "" : " · " + work.ProjectName;
            return status + project + (work.Reason == OffStageReason.ProjectHidden ? " · project hidden" : " · stage full");
        }

        public static string StepName(BuildStepKind kind, bool newProject) => kind switch
        {
            BuildStepKind.CreateProject => "Create the project",
            BuildStepKind.BindFolder => "Move the project to its folder",
            BuildStepKind.CreateWorkstream => newProject ? "Create its first work" : "Create the work",
            _ => "Start the work",
        };

        /// <summary>
        /// How a step of Start building went: sent is not done, and only a completed record confirms.
        /// A refusal or failure about a folder says what to do, from its code; any other shows the
        /// control plane's reason, as <see cref="LabelText.Plain"/> shows it.
        /// </summary>
        public static string StepStatus(BuildStep step) => step.Status switch
        {
            BuildStepStatus.NotYet => "Not yet",
            BuildStepStatus.Waiting => "Sent, waiting for the result",
            BuildStepStatus.Confirmed => "Confirmed",
            BuildStepStatus.Refused => "Could not do that: " + (FolderProblem(step.Refusal, step.Failure) ?? LabelText.Plain(step.Reason ?? "no reason given")),
            // A failure that may have had an effect is never put in words that say nothing happened.
            BuildStepStatus.Failed when step.EffectUnknown => "Could not do that: " + LabelText.Plain(step.Reason ?? "no reason given")
                + " It may have taken effect anyway; check the work before trying again.",
            BuildStepStatus.Failed => "Could not do that: " + (FolderProblem(step.Refusal, step.Failure) ?? LabelText.Plain(step.Reason ?? "no reason given")),
            BuildStepStatus.Unknown => "Effect unknown. Check the work before trying again.",
            BuildStepStatus.NotSent => "Not sent: your Mac is not connected.",
            _ => "Unexpected result. Check the work before trying again.",
        };

        /// <summary>Where a model runs, which decides where the person's code and instructions go.</summary>
        public static string Served(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "Runs on your Mac",
            ModelServed.Remote => "Runs on a remote service: your code and instructions go there",
            _ => "Where it runs is not known",
        };

        /// <summary>
        /// The line between a runtime's models on this Mac and the rest, which take a second press:
        /// where they run decides where the person's code and instructions go.
        /// </summary>
        public static string ElsewhereDivider(IEnumerable<RuntimeModel> elsewhere) =>
            elsewhere.All(model => model.Served == ModelServed.Remote)
                ? "Runs on a remote service: your code and instructions go there."
                : "Not known to run on your Mac: your code and instructions may go elsewhere.";

        /// <summary>The first press on a model that runs elsewhere, under its name: what choosing it means, and how.</summary>
        public static string ConfirmElsewhere(RuntimeModel model) => Served(model.Served) + ". Press again to use it.";

        public static string ServedShort(ModelServed served) => served switch
        {
            ModelServed.ThisMac => "on your Mac",
            ModelServed.Remote => "remote",
            _ => "location unknown",
        };

        public static string Tools(ModelToolCalling value) => value switch
        {
            ModelToolCalling.Declared => "tools declared",
            ModelToolCalling.NotDeclared => "tools not declared",
            _ => "tools unknown",
        };

        /// <summary>The recap's note after focus left a review whose final press waited.</summary>
        public const string ReviewAfresh = "You went to another window, so nothing was started. Press Start building to check it again.";

        /// <summary>A simulated runtime in a live session, named for what it does, so no one takes it for a real one.</summary>
        public const string PracticeRun = "Practice run: builds nothing";

        /// <summary>Under <see cref="PracticeRun"/> in the choice: what it is, in a few words.</summary>
        public const string PracticeDetail = "simulated: no agent, no files";

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
        /// What runs the work, for the recap: the runtime, then the model and where it runs, as in
        /// "Runs with: OpenCode 2.0.18, qwen3.6 (Ollama), on your Mac"; or what is still to choose.
        /// </summary>
        public static string RunsWith(NewWorkDraft draft, bool live = false)
        {
            if (draft.Runtime == null) return "Runs with: not chosen yet";
            var line = "Runs with: " + RuntimeName(draft.Runtime, live);
            if (draft.Runtime.ModelChoice != ModelChoice.Listed) return line;
            var model = draft.Model;
            return model == null ? line + ", no model yet" : line + ", " + LabelText.Plain(model.DisplayName) + ", " + ServedShort(model.Served);
        }

        /// <summary>
        /// The model under the runtime in the recap, and what choosing it means: where it runs, which
        /// decides where the person's code and instructions go, and whether it declares tool calling.
        /// </summary>
        public static string ModelLine(NewWorkDraft draft)
        {
            var runtime = draft.Runtime;
            if (runtime == null) return "Choose what runs it in More options. Nothing is chosen for you.";
            if (runtime.ModelChoice == ModelChoice.None)
            {
                return "The runtime chooses its model." + (runtime.Synthetic ? " Simulated: nothing is built." : "");
            }
            var model = draft.Model;
            if (model == null) return "Model: " + (draft.ModelProblem ?? "choose one in More options.");
            // The model's name and where it runs are on the line above (RunsWith); this says what that means.
            if (draft.ModelPreselected) return "Chosen for you: it runs on your Mac; " + Tools(model.ToolCalling) + ".";
            return Served(model.Served) + "; " + Tools(model.ToolCalling) + ".";
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
