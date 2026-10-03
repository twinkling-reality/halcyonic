#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// A folder on the host that no project uses yet, which Connect projects offers to connect: a
    /// folder directly inside one of the host's project roots, or a root that is itself a repository.
    /// Everything about it is what the host read from the folder's own entry when asked
    /// (<c>GET /api/locations</c>), never from inside its files, and its name is untrusted text shown
    /// by <see cref="LabelText"/>'s rule. Connecting sends the root and the name exactly as listed;
    /// the host checks both again (ADR 0020).
    /// </summary>
    public sealed class ConnectableFolder
    {
        internal ConnectableFolder(LocationRoot root, LocationFolder? folder)
        {
            Root = root;
            Folder = folder;
            RawName = folder?.Name ?? root.Name;
            Repository = folder != null ? folder.Repository : root.Repository;
            var changed = folder != null ? folder.ChangedAt : root.ChangedAt;
            ChangedAt = changed != null && DateTimeOffset.TryParse(changed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : (DateTimeOffset?)null;
        }

        public LocationRoot Root { get; }

        /// <summary>The folder inside the root, or null when the root itself is offered.</summary>
        public LocationFolder? Folder { get; }

        /// <summary>The folder's name as the file system has it: send it, never show it as it is.</summary>
        public string RawName { get; }

        /// <summary>The name to show, by <see cref="LabelText"/>'s rule.</summary>
        public string Name => LabelText.Name(RawName);

        /// <summary>The root's name to show, where the folder is.</summary>
        public string RootName => LabelText.Name(Root.Name);

        /// <summary>A <c>.git</c> entry sits directly inside it; null when the host could not look.</summary>
        public bool? Repository { get; }

        /// <summary>The latest change the host saw at the folder's top level; null when unknown.</summary>
        public DateTimeOffset? ChangedAt { get; }

        /// <summary>What <c>project.create</c> carries: the root and the folder's name exactly as listed.</summary>
        public ProjectFolder Choice => ProjectFolder.Existing(Root, Folder);

        /// <summary>The project's name: the folder's own, as <see cref="FolderConnect.ProjectNameOf"/> fits it.</summary>
        public string ProjectName => FolderConnect.ProjectNameOf(RawName);

        /// <summary>
        /// Another offer's name looks the same once shown (spacing, case, compatibility forms and
        /// characters shown by their code point aside), so the person is told to check which is which.
        /// </summary>
        public bool LooksLikeAnother { get; internal set; }

        /// <summary>
        /// Which row this is, for a press to name it: the root's absolute path and the folder's name as the
        /// file system has it. Never shown, and kept out of every log.
        /// </summary>
        public string Key => FolderConnect.KeyOf(Root, Folder);
    }

    /// <summary>
    /// What Connect projects offers to connect, from the host's listing: every folder in an available
    /// root that no project uses (the host says which by the folder's identity, so no spelling of a
    /// path decides it here), and a root itself only when it is a repository no project uses, as a
    /// root can be one repository (ADR 0020). The most recently changed come first.
    /// </summary>
    public static class FolderConnect
    {
        /// <summary>The longest project name <c>project.create</c> takes.</summary>
        public const int MaxProjectName = 200;

        /// <summary>A project's name when its folder's name has nothing but spaces in it.</summary>
        public const string FallbackProjectName = "Project";

        /// <param name="otherNames">
        /// Names a free folder may imitate beyond the listing's own, as the projects' names; the folders
        /// the listing says are in use count too.
        /// </param>
        public static IReadOnlyList<ConnectableFolder> Offers(LocationsResponse listing, IEnumerable<string>? otherNames = null)
        {
            var offers = new List<ConnectableFolder>();
            foreach (var root in listing.Roots)
            {
                if (root.Status != LocationRootStatus.Available) continue;
                if (root.Repository == true && root.UsedBy.Count == 0) offers.Add(new ConnectableFolder(root, null));
                foreach (var folder in root.Folders)
                {
                    if (folder.UsedBy.Count == 0) offers.Add(new ConnectableFolder(root, folder));
                }
            }
            // A root listed twice gives the same folders twice: one row each.
            offers = offers.GroupBy(offer => offer.Key, StringComparer.Ordinal).Select(group => group.First()).ToList();
            foreach (var alike in offers.GroupBy(offer => Likeness(offer.Name), StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                foreach (var offer in alike) offer.LooksLikeAnother = true;
            }
            // A free folder that imitates a project's name, or a folder in use, is marked as well.
            var taken = new HashSet<string>((otherNames ?? Enumerable.Empty<string>()).Select(name => Likeness(LabelText.Name(name))), StringComparer.Ordinal);
            foreach (var root in listing.Roots.Where(root => root.Status == LocationRootStatus.Available))
            {
                if (root.UsedBy.Count > 0) taken.Add(Likeness(LabelText.Name(root.Name)));
                foreach (var folder in root.Folders.Where(folder => folder.UsedBy.Count > 0)) taken.Add(Likeness(LabelText.Name(folder.Name)));
            }
            foreach (var offer in offers.Where(offer => taken.Contains(Likeness(offer.Name)))) offer.LooksLikeAnother = true;
            return offers
                .OrderByDescending(offer => offer.ChangedAt.HasValue)
                .ThenByDescending(offer => offer.ChangedAt ?? DateTimeOffset.MinValue)
                .ThenBy(offer => offer.RawName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(offer => offer.RawName, StringComparer.Ordinal)
                .ThenBy(offer => offer.Root.Path, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The offer a press named, in a listing read again since, or null when it is no longer offered.</summary>
        public static ConnectableFolder? Find(IReadOnlyList<ConnectableFolder> offers, string key) =>
            offers.FirstOrDefault(offer => string.Equals(offer.Key, key, StringComparison.Ordinal));

        /// <summary>Some root lists more folders than the host shows, so a folder may be missing from the offers.</summary>
        public static bool AnyCut(LocationsResponse listing) =>
            listing.Roots.Any(root => root.Status == LocationRootStatus.Available && root.FoldersTruncated);

        /// <summary>
        /// A project's name from its folder's: as the file system has it, without spaces at either end,
        /// cut to <see cref="MaxProjectName"/> characters without splitting a character in two, or
        /// <see cref="FallbackProjectName"/> when nothing but spaces is left. It is shown by
        /// <see cref="LabelText"/>'s rule wherever a project's name is.
        /// </summary>
        public static string ProjectNameOf(string folderName)
        {
            var name = Trimmed(folderName ?? "");
            if (name.Length > MaxProjectName)
            {
                var cut = MaxProjectName;
                if (char.IsLowSurrogate(name[cut]) && char.IsHighSurrogate(name[cut - 1])) cut--;
                name = Trimmed(name.Substring(0, cut));
            }
            // The control plane takes a name only with something in it other than whitespace, by its own rule.
            return name.Length == 0 || name.All(character => char.IsWhiteSpace(character) || JsSpace(character)) ? FallbackProjectName : name;
        }

        private static string Trimmed(string text) => text.Trim().Trim('\uFEFF').Trim();

        /// <summary>
        /// What a shown name looks like, for telling look-alikes apart: compatibility forms folded, case
        /// and every space dropped, and a code point shown as "‹U+200B›" taken as the character it names
        /// would be invisible. Letters of other scripts that look alike are not caught.
        /// </summary>
        internal static string Likeness(string shown)
        {
            var text = System.Text.RegularExpressions.Regex.Replace(shown, @"\u2039U\+[0-9A-F]{4,6}\u203A", "");
            var folded = text.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
            var kept = new System.Text.StringBuilder(folded.Length);
            foreach (var character in folded)
            {
                if (!char.IsWhiteSpace(character) && !JsSpace(character)) kept.Append(character);
            }
            return kept.ToString();
        }

        /// <summary>What the control plane's "something other than whitespace" rule takes as whitespace, beyond .NET's own.</summary>
        private static bool JsSpace(char character) => character == '\uFEFF';

        internal static string KeyOf(LocationRoot root, LocationFolder? folder) =>
            root.Path + "\u0000" + (folder == null ? "" : "/" + folder.Name);
    }

    /// <summary>
    /// Connecting one folder: an ordinary <c>project.create</c> bound to it as an existing folder,
    /// and nothing else, so no new kind of thing is made and nothing in the folder changes. The
    /// project counts as connected only once the control plane recorded the command completed with
    /// the project it made; a projected record wins over a missing or lost acknowledgement
    /// (<see cref="NewWorkSubmission"/>). While the outcome is unknown, or it failed with an effect
    /// that cannot be ruled out, its id stays in <see cref="Unresolved"/> and it is not sent again: a
    /// blind retry could make a second project for the folder.
    /// </summary>
    public sealed class FolderConnection
    {
        private readonly CommandFactory commands;

        public FolderConnection(ConnectableFolder folder, CommandFactory commands)
        {
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Step = new BuildStep(BuildStepKind.CreateProject);
        }

        public ConnectableFolder Folder { get; }

        /// <summary>How the one command went, in the words Start building's steps use.</summary>
        public BuildStep Step { get; }

        /// <summary>The command in flight, or null before it and once it ended.</summary>
        public NewWorkSubmission? Current { get; private set; }

        /// <summary>The project made for the folder, once its record arrived.</summary>
        public string? ProjectId { get; private set; }

        public bool Connected => ProjectId != null;

        /// <summary>It was refused, failed, not sent, or ended unexpectedly.</summary>
        public bool Stopped { get; private set; }

        /// <summary>The command that may have run without this headset knowing its result.</summary>
        public string? Unresolved { get; private set; }

        /// <summary>It stopped, and nothing of it can have run, so it can be sent again.</summary>
        public bool CanRetry => Stopped && Unresolved == null;

        public CommandEnvelope Begin()
        {
            if (Current != null || Stopped || Connected) throw new InvalidOperationException("The connection has begun.");
            return Send();
        }

        public CommandEnvelope Retry()
        {
            if (!CanRetry) throw new InvalidOperationException("Only a command that cannot have run is sent again.");
            Stopped = false;
            Step.Reason = null;
            Step.Refusal = null;
            Step.Failure = null;
            Step.EffectUnknown = false;
            return Send();
        }

        public void Acknowledged(CommandAckMessage ack) => Current?.Acknowledge(ack);

        public void AcknowledgementLost(Exception error) => Current?.LostAcknowledgement(error);

        /// <summary>Brings the command up to date with the projected state and what the session reported.</summary>
        public void Advance(ClientProjection? state)
        {
            var current = Current;
            if (current == null) return;
            if (state != null && state.Commands.TryGetValue(current.Command.CommandId, out var projected)) current.Observe(projected);
            switch (current.State)
            {
                case NewWorkSubmissionState.Waiting:
                    Step.Status = BuildStepStatus.Waiting;
                    return;
                case NewWorkSubmissionState.OutcomeUnknown:
                    Step.Status = BuildStepStatus.Unknown;
                    return;
                case NewWorkSubmissionState.NotSent:
                    Step.Status = BuildStepStatus.NotSent;
                    Unresolved = null;
                    Stop();
                    return;
                case NewWorkSubmissionState.Rejected:
                    Step.Status = BuildStepStatus.Refused;
                    Step.Reason = current.EffectiveRecord?.Rejection?.Message;
                    Step.Refusal = current.EffectiveRecord?.Rejection?.Code;
                    Unresolved = null;
                    Stop();
                    return;
                case NewWorkSubmissionState.Failed:
                    var failure = current.EffectiveRecord?.Failure;
                    Step.Status = BuildStepStatus.Failed;
                    Step.Reason = failure?.Message;
                    Step.Failure = failure?.Code;
                    Step.EffectUnknown = failure?.Effect == FailureEffect.Unknown;
                    if (!Step.EffectUnknown) Unresolved = null;
                    Stop();
                    return;
            }
            if (!current.HasExpectedResult)
            {
                Step.Status = BuildStepStatus.Unexpected;
                Stop();
                return;
            }
            Step.Status = BuildStepStatus.Confirmed;
            Unresolved = null;
            ProjectId = ((ProjectCreatedResult)current.EffectiveRecord!.Result!).ProjectId;
            Current = null;
        }

        private CommandEnvelope Send()
        {
            var command = commands.CreateProject(Folder.ProjectName, Folder.Choice.ToContract());
            Current = new NewWorkSubmission(command);
            Unresolved = command.CommandId;
            Step.Status = BuildStepStatus.Waiting;
            return command;
        }

        private void Stop()
        {
            Current = null;
            Stopped = true;
        }
    }
}
