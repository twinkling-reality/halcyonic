#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Fills the spatial kit's models from what production already presents and words: the kit draws these words,
    /// it never words them. Every sentence comes from <see cref="WorkspaceText"/>, <see cref="ConnectionText"/>,
    /// <see cref="EntryText"/> or <see cref="StateLanguage"/>, and every record from the presentation unchanged;
    /// where the kit cannot draw something whole, it is given nothing, never part of it.
    /// </summary>
    public static class KitPresenter
    {
        /// <summary>The confirmation of Approve or Deny: production's question about the request above and its Yes, locked until the request's <paramref name="parts"/> are read.</summary>
        public static KitConfirmation ConfirmationOf(WorkspaceAction action, bool read, int parts) => new KitConfirmation(
            action == WorkspaceAction.Approve, WorkspaceText.ConfirmationPrompt(action, null), WorkspaceText.ConfirmLabel(action), read, EntryText.ReadToPart(parts));

        /// <summary>
        /// The approval the work waits on, in <see cref="WorkspaceText.NeedFromYou"/>'s words, or null while none
        /// waits. Where an approved command runs stands apart only where NeedFromYou says it, so for commands alone.
        /// Each answer is offered only where production offers it, and a confirmation of one it doesn't is dropped.
        /// </summary>
        public static KitNeed? NeedOf(WorkspacePresentation workspace, KitConfirmation? confirming = null)
        {
            var need = WorkspaceText.NeedFromYou(workspace);
            if (need == null) return null;
            var denyOffered = workspace.Actions.Contains(WorkspaceAction.Deny);
            var approveOffered = workspace.Actions.Contains(WorkspaceAction.Approve);
            if (confirming != null && !(confirming.Approving ? approveOffered : denyOffered)) confirming = null;
            var where = WorkspaceText.ApprovedCommandNote(workspace.Runtime);
            var scope = where != null && need.Notes.Contains(where) ? where : null;
            var notes = need.Notes.Where(note => scope == null || note != scope).ToList();
            return new KitNeed(need.AsksRuns, need.Request, scope, notes, WorkspaceText.Label(WorkspaceAction.Deny), WorkspaceText.Label(WorkspaceAction.Approve),
                denyOffered, approveOffered, confirming);
        }

        /// <summary>
        /// The question the work waits on, under <see cref="WorkspaceText.PromptHeadingRuns"/>: only one the kit draws
        /// whole, a single answerable prompt answered by typing. Several prompts, options or a secret give null.
        /// </summary>
        public static KitAsk? AskOf(WorkspacePresentation workspace)
        {
            var question = workspace.QuestionToAnswer;
            if (question == null || question.Prompts.Count != 1 || !WorkspaceText.Answerable(question)) return null;
            var prompt = question.Prompts[0];
            if (prompt.Options.Count > 0 || !prompt.FreeText) return null;
            return new KitAsk(WorkspaceText.PromptHeadingRuns(question, 0), prompt.Text);
        }

        /// <summary>The newest recent command whose effect can't be told, in <see cref="WorkspaceText.NotSure"/>'s words, or null.</summary>
        public static KitNoticeModel? NoticeOf(WorkspacePresentation workspace)
        {
            var unknown = workspace.Commands.FirstOrDefault(command => command.Text == WorkspaceText.NotSure(command.CommandType));
            return unknown == null ? null : new KitNoticeModel(GlazeIcon.CantTellYet, GlazeTone.Unknown, LineWords.Ours(unknown.Text));
        }

        /// <summary>Why nothing can be sent while the connection is not live, in <see cref="ConnectionText"/>'s words; null while live.</summary>
        public static string? WhyNotSendOf(ConnectionStatus connection) => connection.Phase switch
        {
            ConnectionPhase.Live => null,
            ConnectionPhase.WaitingToRetry => ConnectionText.Retrying(connection),
            _ => ConnectionText.Phase(connection),
        };

        /// <summary>The agent's latest message, a reported claim, read in the slate; null where it has said nothing yet.</summary>
        public static ActivityEntry? ReportOf(IReadOnlyList<ActivityEntry> activity) =>
            activity.LastOrDefault(entry => entry.Kind == ActivityKind.Message && entry.Reported);

        /// <summary>
        /// The evidence rail: every recorded entry but the agent's messages, oldest first, unchanged. An entry carries
        /// no outcome of its own, so none is toned as one; its words say what happened.
        /// </summary>
        public static IReadOnlyList<KitTrailEntry> TrailOf(IReadOnlyList<ActivityEntry> activity) =>
            activity.Where(entry => entry.Kind != ActivityKind.Message).Select(entry => new KitTrailEntry(entry, IconOf(entry.Kind))).ToList();

        /// <summary>The icon of what kind of record an entry is, never of its outcome (the DESIGN lane's choice, 2026-10-28).</summary>
        public static GlazeIcon IconOf(ActivityKind kind) => kind switch
        {
            ActivityKind.Lifecycle => GlazeIcon.StartBuilding,
            ActivityKind.Turn => GlazeIcon.Next,
            ActivityKind.Tool => GlazeIcon.ScriptFile,
            ActivityKind.TestRun => GlazeIcon.CheckingItsWork,
            ActivityKind.Approval => GlazeIcon.WaitingForYou,
            ActivityKind.Command => GlazeIcon.TellIt,
            ActivityKind.Connection => GlazeIcon.LastKnown,
            _ => GlazeIcon.Quote,
        };

        /// <summary>A task's row: its title upright as a named identity, its state in icon and words as production derives it, and where pressing it goes.</summary>
        public static KitRowModel TaskRowOf(CharacterPresentation task, bool selected = false, bool opensMore = true, string? next = null, int wordLines = 2)
        {
            var badge = StateLanguage.BadgeOf(task);
            return new KitRowModel(task.WorkstreamId, LineWords.TheirName(task.Title), badge.Icon, badge.Tone, badge.Text, badge.Tone,
                end: next, endTone: badge.State == WorkState.WaitingForYou ? GlazeTone.Attention : GlazeTone.Accent,
                opensMore: opensMore, selected: selected, wordLines: wordLines, isName: true);
        }

        /// <summary>The compact presence of the retained task, with how many tasks wait for the person as known now.</summary>
        public static KitPresenceModel PresenceOf(CharacterPresentation retained, int waiting) =>
            new KitPresenceModel(retained.Title, StateLanguage.BadgeOf(retained), waiting);

        /// <summary>
        /// The focused work from the presentation: its state and marks, the agent's report, the rail, what it waits on,
        /// and Send offered only where the connection is live and production takes an instruction, else why not in
        /// <see cref="ConnectionText"/>'s or <see cref="FileScreens.WhyNoInstruction"/>'s words.
        /// </summary>
        public static KitWorkModel WorkOf(WorkspacePresentation workspace, string projectName, string draft, bool voiceOffered, ConnectionStatus connection,
            IReadOnlyList<KitSpineSection> spine, KitRowModel? spineFoot = null, KitConfirmation? confirming = null, KitAvatar? avatar = null)
        {
            var whyNot = WhyNotSendOf(connection) ?? FileScreens.WhyNoInstruction(workspace);
            var sendable = whyNot == null;
            return new KitWorkModel(projectName, workspace.Character.Title, StateLanguage.BadgeOf(workspace.Character), StateLanguage.MarksOf(workspace.Character),
                ReportOf(workspace.Activity), TrailOf(workspace.Activity), draft, voiceOffered, sendable, whyNot, spine, spineFoot,
                NeedOf(workspace, confirming), AskOf(workspace), NoticeOf(workspace), avatar);
        }
    }
}
