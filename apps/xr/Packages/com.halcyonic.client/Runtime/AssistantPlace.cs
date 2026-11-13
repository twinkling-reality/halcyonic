#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the Usage card's Assistant row reads (ADR 0030, 2026-11-12): the model the assistant runs on and where, or the
    /// host's own words for why it cannot run. <see cref="Model"/> and <see cref="Provider"/> are theirs (a model id, the
    /// provider's declared name); <see cref="Reason"/> is the host's. A row with no model and no reason has not been read.
    /// </summary>
    public sealed class AssistantRow
    {
        public AssistantRow(string? model, string? provider, string? reason)
        {
            Model = model;
            Provider = provider;
            Reason = reason;
        }

        /// <summary>The model id it runs on, or null where it cannot run or was not read.</summary>
        public string? Model { get; }

        /// <summary>The hosted provider's declared name, or null on this computer.</summary>
        public string? Provider { get; }

        /// <summary>Why it cannot run, in the host's own words (never a key or a provider's text), or null.</summary>
        public string? Reason { get; }

        public bool OnThisComputer => Model != null && Provider == null;
    }

    /// <summary>One place the assistant may run, a row of the host's list as read: its index is what a press carries back.</summary>
    public sealed class AssistantOption
    {
        public AssistantOption(int index, string label, string? detail, bool theirs, bool choosable)
        {
            Index = index;
            Label = label;
            Detail = detail;
            Theirs = theirs;
            Choosable = choosable;
        }

        public int Index { get; }

        /// <summary>"This Computer's Model" (ours) or a model id (theirs, <see cref="Theirs"/>).</summary>
        public string Label { get; }

        /// <summary>"On &lt;provider&gt;", this computer's model's name, or the reason it cannot be chosen; null where none.</summary>
        public string? Detail { get; }

        public bool Theirs { get; }

        /// <summary>False where the host offers nothing on the row (its key refused or unusable): show it, press nothing.</summary>
        public bool Choosable { get; }
    }

    /// <summary>
    /// The assistant's place in the Usage card, read from the host and never decided here: where it runs
    /// (<c>GET /api/workspace/agent</c>), the host's list of where it may run (<see cref="ControlPlaneApi.GetAssistantModelsAsync"/>),
    /// and the review a press waits on. The press is the host's entry Use Model, sent as <see cref="CommandFactory.UseModel"/>;
    /// the host answers with a request whose step waits for the review card, confirmed by <see cref="CommandFactory.ConfirmRequest"/>.
    /// </summary>
    public static class AssistantPlace
    {
        public const string ListName = "assistant_models";
        public const string UseModelCommand = "assistant.model.choose";
        /// <summary>The host's own title for the entry (COMMAND_SAYING), the words a request made by the press carries.</summary>
        public const string UseModelTitle = "Use Model";

        /// <summary>The row for a status read: the model and where, or the host's reason; an unread status reads as nothing yet.</summary>
        public static AssistantRow Row(WorkspaceAgentStatus? status) => status switch
        {
            AvailableWorkspaceAgent available when available.Served == AssistantServed.Remote =>
                // A remote assistant that does not say where it runs is not shown as running anywhere.
                string.IsNullOrWhiteSpace(available.Provider)
                    ? new AssistantRow(null, null, null)
                    : new AssistantRow(available.Model, available.Provider, null),
            AvailableWorkspaceAgent available => new AssistantRow(available.Model, null, null),
            UnavailableWorkspaceAgent unavailable => new AssistantRow(null, null, unavailable.Reason.Message),
            _ => new AssistantRow(null, null, null),
        };

        /// <summary>The list's rows as the person sees them, in the host's own order; none for an unread or other list.</summary>
        public static IReadOnlyList<AssistantOption> Options(ChoicesResponse? list) =>
            list == null || list.List != ChoiceList.AssistantModels
                ? new AssistantOption[0]
                : list.Rows.Select(row => new AssistantOption(
                    (int)row.Index, row.Label, row.Detail, row.Theirs, row.Actions.Contains(CommandType.AssistantModelChoose))).ToList();

        /// <summary>
        /// The step of a request made by a Use Model press that waits for the person's one press on its review card, or
        /// null. Its proposal's <c>WhatHappens</c> and <c>Said</c> are what the card shows, its <c>Review</c> what the confirm
        /// carries back.
        /// </summary>
        public static WorkspaceRequestStep? WaitingReview(WorkspaceRequestView? view) => WaitingStep(view);

        /// <summary>
        /// Whether the waiting step is one press with no review: the host's own offer of this computer's model after a
        /// refused key (confirm none). A hosted choice is reviewed first (confirm review).
        /// </summary>
        public static bool OnePress(WorkspaceRequestStep? step) =>
            step?.Proposal?.Confirm is { Type: Newtonsoft.Json.Linq.JTokenType.String } confirm && confirm.ToString() == "none";

        /// <summary>
        /// Whether a request made by a press has ended with nothing waiting: refused, failed or done. Stop reading it then;
        /// while it is still running and no step waits, read it again.
        /// </summary>
        public static bool Ended(WorkspaceRequestView? view)
        {
            if (view == null || WaitingStep(view) != null) return false;
            var status = view.Status?.Type == Newtonsoft.Json.Linq.JTokenType.String ? view.Status.ToString() : "";
            return status != "running" && status != "queued";
        }

        private static WorkspaceRequestStep? WaitingStep(WorkspaceRequestView? view) =>
            view?.Steps.LastOrDefault(step =>
                step.Proposal != null && step.Proposal.CommandType == CommandType.AssistantModelChoose && step.Proposal.Review != null
                && step.Status.Type == Newtonsoft.Json.Linq.JTokenType.String
                && step.Status.ToString() == "waiting");
    }
}
