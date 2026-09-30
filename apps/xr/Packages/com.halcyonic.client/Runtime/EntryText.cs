#nullable enable
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>
        /// Where the project's files live cannot be chosen from the headset yet; runtimes that need a
        /// folder refuse to start without one. One line, so choosing a location replaces it alone.
        /// </summary>
        public const string LocationNotBuilt = "Where its files live: not chosen here yet. A runtime that needs a folder will refuse to start.";

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

        /// <summary>A project's line in Connect projects and on its rail chip: whether it shows, then its work.</summary>
        public static string ProjectDetail(ProjectSummary project) => (project.Shown ? "Shown" : "Hidden") + " · " + Counts(project);

        /// <summary>The rail's Connect projects detail: how many projects show.</summary>
        public static string ConnectDetail(WorkOverview overview)
        {
            var known = overview.Projects.Count;
            if (known == 0) return "none known yet";
            var shown = overview.ShownProjects;
            return shown == known ? (known == 1 ? "1 shown" : "all " + Count(known) + " shown") : Count(shown) + " of " + Count(known) + " shown";
        }

        /// <summary>The rail's More work detail: how much has no character, and how much of it needs the person.</summary>
        public static string MoreWorkDetail(WorkOverview overview)
        {
            var line = Count(overview.OffStage.Count) + " off the stage";
            var needing = overview.NeedsYouOffStage;
            return needing > 0 ? line + " · " + Count(needing) + " needs you" : line;
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
            BuildStepKind.CreateWorkstream => newProject ? "Create its first work" : "Create the work",
            _ => "Start the work",
        };

        /// <summary>How a step of Start building went: sent is not done, and only a completed record confirms.</summary>
        public static string StepStatus(BuildStep step) => step.Status switch
        {
            BuildStepStatus.NotYet => "Not yet",
            BuildStepStatus.Waiting => "Sent, waiting for the result",
            BuildStepStatus.Confirmed => "Confirmed",
            BuildStepStatus.Refused => "Could not do that: " + (step.Reason ?? "no reason given"),
            BuildStepStatus.Failed => "Could not do that: " + (step.Reason ?? "no reason given")
                + (step.EffectUnknown ? " It may have taken effect anyway; check the work before trying again." : ""),
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

        /// <summary>A runtime as a choice: its name as it arrives, and whether its work is simulated.</summary>
        public static string RuntimeName(RuntimeDescriptor runtime) => LabelText.Plain(runtime.DisplayName) + (runtime.Synthetic ? " (simulated)" : "");

        /// <summary>How the work runs, for the recap: the runtime and the model, or what is still to choose.</summary>
        public static string RunsWith(NewWorkDraft draft)
        {
            if (draft.Runtime == null) return "Runs with: not chosen yet";
            var line = "Runs with: " + RuntimeName(draft.Runtime);
            if (draft.Runtime.ModelChoice == ModelChoice.None) return line + ", its own model choice";
            return draft.Model == null ? line + ", " + (draft.ModelProblem ?? "choose a model") : line + ", " + LabelText.Plain(draft.Model.DisplayName);
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
