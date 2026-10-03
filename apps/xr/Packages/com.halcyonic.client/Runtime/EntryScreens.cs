#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The checks and the whole request New project builds on (<see cref="NewProjectFlow"/>), kept from
    /// the entry panel it replaced, whose screens the menu's Projects and New project took over (ADR
    /// 0026). Nothing here sends anything.
    /// </summary>
    public static class EntryScreens
    {
        /// <summary>
        /// What the recap says first when the companion has just proposed it: its view, when it found
        /// the idea unclear or not buildable, then its line, quoted and tagged as its own.
        /// </summary>
        public static string Proposed(ProposeReply reply)
        {
            var view = CompanionText.View(reply.View);
            return (view == null ? "" : view + " ") + CompanionText.Says(reply.Line);
        }

        /// <summary>
        /// Why Start building can't go ahead now, or null: the demonstration, no Mac, the idea itself,
        /// nothing chosen to run it, a choice no longer there, no model, the project gone, no folder,
        /// checked in that order.
        /// </summary>
        /// <param name="sequence">The build already sent, if any: while it is on its way, nothing more starts.</param>
        public static string? StartProblem(bool demonstration, ClientProjection? state, bool connected, ProjectIdea? idea, NewWorkDraft draft,
            ProjectLocation? currentFolder, BuildSequence? sequence = null)
        {
            if (demonstration) return EntryText.DemoCannotStart;
            if (sequence?.InFlight == true) return EntryText.AlreadyStarting;
            if (state == null || !connected) return EntryText.WaitingForMac;
            if (idea?.Problem is string ideaProblem) return ideaProblem;
            // A place the computer no longer lists: it would refuse the folder, so nothing is sent.
            if (idea?.Folder?.PlaceGone == true) return EntryText.ChooseWhereFilesLive;
            var runtime = draft.Runtime;
            if (runtime == null) return EntryText.ChooseHowItRuns;
            if (!state.Runtimes.Any(each => each.RuntimeId == runtime.RuntimeId)) return EntryText.ChooseAgain;
            if (runtime.ModelChoice == ModelChoice.Listed && draft.Model == null) return EntryText.FinishChoosing;
            if (idea?.ExistingProjectId != null && !state.Projects.ContainsKey(idea.ExistingProjectId)) return EntryText.ProjectGone;
            if (runtime.UsesProjectLocation && idea?.Folder == null && currentFolder == null) return EntryText.ChooseWhereFilesLive;
            return null;
        }

        /// <summary>
        /// The whole request as it will be sent, from the draft: names and the person's words reach the
        /// review as they are, so it spells each once; a project that exists and moves shows its folder
        /// now and from now on.
        /// </summary>
        public static NewWorkReview ReviewOf(ProjectIdea idea, NewWorkDraft draft, ProjectLocation? currentFolder, bool live)
        {
            // Reviewing changes nothing: the draft takes the first task only when its Yes is pressed.
            var model = draft.Model;
            var moves = Moves(idea, currentFolder);
            var sends = FolderSent(idea, currentFolder);
            // The review spells what Halcyonic did not write by its code points, so it is given those names as they are.
            var folder = sends?.Describe(name => name) ?? currentFolder?.Name ?? "none";
            var before = moves ? currentFolder?.Name ?? "none" : null;
            var (title, titleCut) = NewWorkDraft.TitleSourceOf(idea.FirstTask);
            return new NewWorkReview(
                idea.Name,
                title,
                EntryText.RuntimeName(draft.Runtime!, live, plain: false),
                model?.DisplayName ?? "chosen by the agent app",
                model == null ? "the agent app chooses" : EntryText.ServedInSentence(model.Served) + "; " + EntryText.Tools(model.ToolCalling),
                model?.ModelRef ?? "none",
                idea.FirstTask,
                folder,
                before,
                titleCut,
                sends?.ToContract());
        }

        /// <summary>
        /// The folder a send carries, decided once for the review, the first send and Try again alike:
        /// a new project's, a real move's, or none when the project is already where the choice points.
        /// </summary>
        public static ProjectFolder? FolderSent(ProjectIdea idea, ProjectLocation? currentFolder) =>
            idea.ExistingProjectId == null || Moves(idea, currentFolder) ? idea.Folder : null;

        /// <summary>
        /// The request moves a project that exists to another folder: one is chosen, and the project is
        /// not already there, as a project made under that choice before a later step stopped is.
        /// </summary>
        public static bool Moves(ProjectIdea idea, ProjectLocation? currentFolder) =>
            idea.ExistingProjectId != null && idea.Folder != null && !idea.Folder.IsAt(currentFolder?.Path);
    }
}
