#nullable enable
using System;
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Whether Play in Headset is offered for a project now, and if not, the one fact that stops it.</summary>
    public enum PlayInHeadset
    {
        /// <summary>The preview of the latest check runs and a way reaches it from the headset: open its address.</summary>
        Offered,
        /// <summary>Everything holds but no preview of the latest check runs: start one, then open it.</summary>
        NoPreviewOfCheck,
        /// <summary>The host has recorded no check of the project.</summary>
        NoCheck,
        /// <summary>The project's files changed since the latest check, or whether they did is unknown (T2).</summary>
        FilesNotChecked,
        /// <summary>The latest check did not pass.</summary>
        CheckNotPassed,
        /// <summary>The latest check did not find every read of the page confined to the project (reads_confined).</summary>
        NotConfined,
        /// <summary>The headset reaches the computer over the network and nothing on it serves the preview yet.</summary>
        NoWayToHeadset,
        /// <summary>No control plane is configured.</summary>
        NoControlPlane,
        /// <summary>A demonstration shows (the recorded run, or one for a computer that cannot be reached): nothing of it opens.</summary>
        Demonstration,
    }

    /// <summary>
    /// Play in Headset (gap 1 of the parity map): a project's running preview opened in Quest Browser on the headset,
    /// read only from the project's view as the host sent it and from how this headset reaches the computer. Quest
    /// Browser always opens a loopback address, <c>http://127.0.0.1:{port}/</c>, since only a loopback page counts as a
    /// secure context there without a certificate, so WebXR can be offered. Which port depends on the way:
    /// <list type="bullet">
    /// <item>Over the cable (<see cref="ControlPlaneTarget.Pairing"/> is null: the control plane is reached at a
    /// loopback address the computer's <c>adb reverse</c> carries): the preview's own port, which the computer reverses
    /// to its own preview. The app cannot see a reverse; it reads the cable from its target alone.</item>
    /// <item>Over the pairing (a paired target): the port the app's own relay serves on the headset's loopback, which
    /// forwards to the computer's preview over the paired, pinned connection; null until the relay serves it.</item>
    /// </list>
    /// The host decides; this only keeps the action from offering what cannot open. No real instruction is sent.
    /// </summary>
    public static class HeadsetPlay
    {
        /// <summary>The action's words.</summary>
        public const string ActionWords = "Play in Headset";

        /// <summary>The refusal where the check did not find every read confined: the preview stays on the computer.</summary>
        public const string PreviewOnYourComputer = "Preview on Your Computer";

        /// <summary>The refusal where the headset is paired and no relay serves the preview yet.</summary>
        public const string ConnectByCable = "Connect by Cable";

        /// <summary>
        /// Whether Play in Headset is offered for <paramref name="project"/>, given the control plane this headset
        /// reaches (<paramref name="target"/>), whether a demonstration of any kind shows (<paramref name="demonstration"/>,
        /// as the menu's director reads it: the recorded run, or one for a computer that cannot be reached; its projects
        /// and their preview ports are not the person's, so nothing of them opens), and the loopback port the app's relay
        /// serves this project's preview on, if it does (<paramref name="relayPort"/>).
        /// </summary>
        public static PlayInHeadset For(ProjectView project, ControlPlaneTarget? target, bool demonstration, int? relayPort = null)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (demonstration) return PlayInHeadset.Demonstration;
            if (target == null) return PlayInHeadset.NoControlPlane;
            var check = project.Check;
            if (check == null) return PlayInHeadset.NoCheck;
            if (check.Files != ProjectViewCheckFiles.Current) return PlayInHeadset.FilesNotChecked;
            if (!check.Findings.Passed) return PlayInHeadset.CheckNotPassed;
            if (!check.Findings.ReadsConfined) return PlayInHeadset.NotConfined;
            if (target.Pairing != null && relayPort == null) return PlayInHeadset.NoWayToHeadset;
            if (ProjectPlayFacts.PreviewOfLatestCheck(project) == null) return PlayInHeadset.NoPreviewOfCheck;
            return PlayInHeadset.Offered;
        }

        /// <summary>Whether the action stands where <paramref name="play"/> holds: offered, or offered once a preview starts.</summary>
        public static bool Stands(PlayInHeadset play) =>
            play == PlayInHeadset.Offered || play == PlayInHeadset.NoPreviewOfCheck;

        /// <summary>
        /// The words shown in the action's place where it does not stand and the person can do something about it, else
        /// null (the action is simply not shown, as Try Build is not).
        /// </summary>
        public static string? Refusal(PlayInHeadset play) => play switch
        {
            PlayInHeadset.NotConfined => PreviewOnYourComputer,
            PlayInHeadset.NoWayToHeadset => ConnectByCable,
            _ => null,
        };

        /// <summary>
        /// The address Quest Browser opens for <paramref name="project"/>, or null where Play in Headset is not
        /// <see cref="PlayInHeadset.Offered"/>.
        /// </summary>
        public static Uri? AddressOf(ProjectView project, ControlPlaneTarget? target, bool demonstration, int? relayPort = null)
        {
            if (For(project, target, demonstration, relayPort) != PlayInHeadset.Offered) return null;
            var port = target!.Pairing == null ? ProjectPlayFacts.PreviewOfLatestCheck(project)!.Port : relayPort!.Value;
            return LoopbackAddress(port);
        }

        /// <summary>The command a press sends for <paramref name="project"/>: the preview's start where none runs, else null (the address opens).</summary>
        public static CommandEnvelope? CommandFor(ProjectView project, ControlPlaneTarget? target, bool demonstration, CommandFactory commands, int? relayPort = null)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            return For(project, target, demonstration, relayPort) == PlayInHeadset.NoPreviewOfCheck
                ? commands.StartPreview(project.ProjectId)
                : null;
        }

        /// <summary>Whether <paramref name="address"/> is one Play in Headset may open: http on 127.0.0.1 with a port, the root path, nothing else.</summary>
        public static bool IsOpenable(Uri? address) =>
            address != null
            && address.IsAbsoluteUri
            && address.Scheme == Uri.UriSchemeHttp
            && address.Host == "127.0.0.1"
            && !address.IsDefaultPort
            && address.Port > 0
            && address.AbsolutePath == "/"
            && address.Query.Length == 0
            && address.Fragment.Length == 0
            && address.UserInfo.Length == 0;

        private static Uri LoopbackAddress(long port)
        {
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "A preview's port is between 1 and 65535.");
            return new Uri("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/");
        }
    }

    /// <summary>
    /// A Play in Headset press waiting on the preview it started: once the project's view shows the preview of its
    /// latest check, the address opens once; any other answer ends the wait. One project at a time.
    /// </summary>
    public sealed class HeadsetPlayWait
    {
        /// <summary>The project whose preview this headset waits to open, or null.</summary>
        public string? ProjectId { get; private set; }

        /// <summary>Records that a press sent <paramref name="command"/>; only a preview's start begins a wait.</summary>
        public void Sent(CommandEnvelope? command)
        {
            if (command is ProjectPreviewStartCommand start) ProjectId = start.Payload.ProjectId;
        }

        /// <summary>Ends the wait without opening anything.</summary>
        public void Clear() => ProjectId = null;

        /// <summary>
        /// The address to open now, once, from <paramref name="state"/>; null while the preview is still starting or
        /// when the wait ended without one (the project gone, or Play in Headset refused).
        /// </summary>
        public Uri? Advance(ClientProjection? state, ControlPlaneTarget? target, bool demonstration, int? relayPort = null)
        {
            if (ProjectId == null || state == null) return null;
            if (!state.Projects.TryGetValue(ProjectId, out var project))
            {
                ProjectId = null;
                return null;
            }
            var play = HeadsetPlay.For(project, target, demonstration, relayPort);
            if (play == PlayInHeadset.NoPreviewOfCheck) return null;
            ProjectId = null;
            return play == PlayInHeadset.Offered ? HeadsetPlay.AddressOf(project, target, demonstration, relayPort) : null;
        }
    }
}
