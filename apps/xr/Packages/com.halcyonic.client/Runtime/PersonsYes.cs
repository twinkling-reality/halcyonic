#nullable enable
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The person's own yes the host offers on an assistant's proposal (ADR 0055, amended 2026-11-16; AUDIT3's O1 to O5):
    /// today Allow Test Servers on the own-check step. Its label and its line are the host's, said whole beside the switch,
    /// never the client's own. Off until the person turns it on; the press then carries the option's own digest with the
    /// yes, and the plain digest without it, never a mix (O2).
    /// </summary>
    public sealed class PersonsYes
    {
        /// <summary>The one field the host offers today.</summary>
        public const string OpenPorts = "open_ports";

        private readonly WorkspaceRequestOption option;

        private PersonsYes(WorkspaceRequestOption option) => this.option = option;

        /// <summary>The host's option on <paramref name="proposal"/>, or null where it offers none this client knows.</summary>
        public static PersonsYes? Of(WorkspaceRequestStepProposal? proposal)
        {
            var offered = proposal?.Options?.FirstOrDefault(each => each.Field == OpenPorts);
            return offered == null || string.IsNullOrWhiteSpace(offered.Review) ? null : new PersonsYes(offered);
        }

        /// <summary>The host's label for the switch.</summary>
        public string Label => option.Label;

        /// <summary>The host's line, said whole beside the switch before the press (O5).</summary>
        public string Line => option.Line;

        /// <summary>Whether the person turned it on.</summary>
        public bool On { get; private set; }

        /// <summary>The person's own press on the switch.</summary>
        public void Toggle() => On = !On;

        /// <summary>
        /// The one press on <paramref name="proposal"/>'s step: with the yes on, the option's digest and <c>open_ports</c>;
        /// otherwise the plain digest the card was drawn from.
        /// </summary>
        public WorkspaceRequestResumeCommand Confirm(CommandFactory commands, WorkspaceRequestStepProposal proposal, string requestId, string stepId) =>
            On ? commands.ConfirmRequest(requestId, stepId, option.Review, openPorts: true) : commands.ConfirmRequest(requestId, stepId, proposal.Review!);
    }
}
