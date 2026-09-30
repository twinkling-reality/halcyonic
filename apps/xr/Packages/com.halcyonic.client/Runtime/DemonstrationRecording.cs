#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>One recorded event message, and when it was sent after its node began.</summary>
    public sealed class RecordedEvent
    {
        public RecordedEvent(TimeSpan at, EventMessage message)
        {
            At = at;
            Message = message;
        }

        public TimeSpan At { get; }

        public EventMessage Message { get; }
    }

    public enum DemonstrationAnswerKind
    {
        Approve,
        Deny,
        Interrupt,
        Instruct,
    }

    /// <summary>An answer a person can give in the workspace, for which the recording holds a continuation.</summary>
    public sealed class DemonstrationAnswer
    {
        public DemonstrationAnswer(DemonstrationAnswerKind kind, string executionId, string? approvalId, string? text, string? label)
        {
            Kind = kind;
            ExecutionId = executionId;
            ApprovalId = approvalId;
            Text = text;
            Label = label;
        }

        public DemonstrationAnswerKind Kind { get; }

        public string ExecutionId { get; }

        /// <summary>The approval an approve or deny answers.</summary>
        public string? ApprovalId { get; }

        /// <summary>Exactly the instruction the recording sent, for an instruct.</summary>
        public string? Text { get; }

        /// <summary>What a button offering the instruction says, for an instruct.</summary>
        public string? Label { get; }
    }

    /// <summary>Where the recording continues when a person gives an answer.</summary>
    public sealed class DemonstrationBranch
    {
        public DemonstrationBranch(int after, DemonstrationAnswer answer, int node)
        {
            After = after;
            Answer = answer;
            Node = node;
        }

        /// <summary>How many of its node's events have played when the answer is offered.</summary>
        public int After { get; }

        public DemonstrationAnswer Answer { get; }

        /// <summary>The node the recording continues with.</summary>
        public int Node { get; }
    }

    /// <summary>
    /// A stretch of the recording between decisions. Its events play at their times from the moment
    /// the node begins; after <see cref="DemonstrationBranch.After"/> of them, each branch listed
    /// there continues with its own node instead. After the last event the ending snapshot, if any,
    /// is sent, and the final state holds for <see cref="Hold"/>, or until an answer when that is
    /// null, before the demonstration starts again.
    /// </summary>
    public sealed class DemonstrationNode
    {
        private static readonly DemonstrationBranch[] None = new DemonstrationBranch[0];
        private readonly Dictionary<int, List<DemonstrationBranch>> byAfter = new Dictionary<int, List<DemonstrationBranch>>();

        public DemonstrationNode(IReadOnlyList<RecordedEvent> events, IReadOnlyList<DemonstrationBranch> branches, SnapshotMessage? endingSnapshot, TimeSpan? hold)
        {
            Events = events;
            Branches = branches;
            EndingSnapshot = endingSnapshot;
            Hold = hold;
            foreach (var branch in branches)
            {
                if (!byAfter.TryGetValue(branch.After, out var list)) byAfter[branch.After] = list = new List<DemonstrationBranch>();
                list.Add(branch);
            }
        }

        public IReadOnlyList<RecordedEvent> Events { get; }

        public IReadOnlyList<DemonstrationBranch> Branches { get; }

        /// <summary>
        /// Sent after the last event: the recording's control plane started again without runtimes, so
        /// the final state holds with no action offered. Null where the node holds for an answer.
        /// </summary>
        public SnapshotMessage? EndingSnapshot { get; }

        /// <summary>How long the final state holds before the demonstration starts again; null until an answer.</summary>
        public TimeSpan? Hold { get; }

        /// <summary>The answers offered once <paramref name="played"/> of the node's events have played.</summary>
        public IReadOnlyList<DemonstrationBranch> BranchesAfter(int played) =>
            byAfter.TryGetValue(played, out var list) ? (IReadOnlyList<DemonstrationBranch>)list : None;
    }

    /// <summary>
    /// A demonstration the control plane recorded (<c>pnpm demonstration:record</c>): the realtime
    /// messages it sent while its scripted operator gave each answer the recording offers, as a tree
    /// of nodes that shares its beginning. The control plane computed every state in it, so playing it
    /// derives nothing. Only a recording of a fixture journal is accepted, so everything it shows is
    /// labeled as recorded. Keys this client does not know, such as recorded REST answers a later
    /// version may carry, are ignored.
    /// </summary>
    public sealed class DemonstrationRecording
    {
        /// <summary>The version of the recording format this client plays.</summary>
        public const int FormatVersion = 2;

        private DemonstrationRecording(string source, WelcomeMessage welcome, SnapshotMessage snapshot, IReadOnlyList<DemonstrationNode> nodes)
        {
            Source = source;
            Welcome = welcome;
            Snapshot = snapshot;
            Nodes = nodes;
        }

        /// <summary>The plan it was recorded from, relative to the repository root.</summary>
        public string Source { get; }

        public WelcomeMessage Welcome { get; }

        /// <summary>The state at the beginning, sent at the start and each time the demonstration starts again.</summary>
        public SnapshotMessage Snapshot { get; }

        /// <summary>The recorded stretches; the first is the beginning, and every other one is an answer's.</summary>
        public IReadOnlyList<DemonstrationNode> Nodes { get; }

        /// <summary>Reads a recording and checks that it can be played truthfully.</summary>
        /// <exception cref="InvalidDataException">The text is not a demonstration this client can play.</exception>
        public static DemonstrationRecording Parse(string json)
        {
            try
            {
                return Read(json);
            }
            catch (Exception error) when (!(error is InvalidDataException))
            {
                throw new InvalidDataException("The demonstration is not readable: " + error.Message, error);
            }
        }

        private static DemonstrationRecording Read(string json)
        {
            JObject document;
            // Timestamps stay strings, as everywhere in the contracts.
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
            {
                document = JObject.Load(reader);
            }
            var version = document.Value<int?>("version");
            if (version != FormatVersion)
            {
                throw new InvalidDataException(
                    "The demonstration has format version " + (version?.ToString() ?? "none") + "; this client plays version " + FormatVersion + ".");
            }
            var serializer = JsonSerializer.Create(HalcyonicJson.Tolerant);
            var source = document.Value<string>("source") ?? throw Missing("source");
            var welcome = document["welcome"]?.ToObject<WelcomeMessage>(serializer) ?? throw Missing("welcome");
            var snapshot = document["snapshot"]?.ToObject<SnapshotMessage>(serializer) ?? throw Missing("snapshot");
            var entries = document["nodes"] as JArray ?? throw Missing("nodes");

            var journal = welcome.Journal;
            if (journal.Origin != JournalOrigin.Fixture)
            {
                throw new InvalidDataException("A demonstration must come from a recorded (fixture) journal, so that it is labeled as recorded.");
            }
            CheckJournal(snapshot, journal, "beginning");
            if (welcome.Protocol != ContractVersions.RealtimeProtocol)
            {
                throw new InvalidDataException("The demonstration speaks realtime protocol " + welcome.Protocol + ".");
            }
            if (snapshot.Snapshot.Position != welcome.Head)
            {
                throw new InvalidDataException("The demonstration's beginning is not where its welcome says the journal stands.");
            }
            if (entries.Count == 0) throw Missing("beginning");

            var nodes = new List<DemonstrationNode>(entries.Count);
            foreach (var entry in entries) nodes.Add(ReadNode(entry, serializer, journal, nodes.Count, entries.Count));
            CheckTree(nodes, snapshot.Snapshot.Position);
            return new DemonstrationRecording(source, welcome, snapshot, nodes);
        }

        private static DemonstrationNode ReadNode(JToken entry, JsonSerializer serializer, JournalInfo journal, int index, int count)
        {
            var eventEntries = entry["events"] as JArray ?? throw Missing("events of node " + index);
            var events = new List<RecordedEvent>(eventEntries.Count);
            var at = TimeSpan.Zero;
            long position = -1;
            foreach (var item in eventEntries)
            {
                var atMs = item.Value<long?>("at_ms") ?? throw Missing("at_ms");
                var message = item["message"]?.ToObject<EventMessage>(serializer) ?? throw Missing("message");
                var next = TimeSpan.FromMilliseconds(atMs);
                if (next < at || message.Position <= position)
                {
                    throw new InvalidDataException("The demonstration's event at position " + message.Position + " is out of order.");
                }
                at = next;
                position = message.Position;
                events.Add(new RecordedEvent(at, message));
            }

            var branches = new List<DemonstrationBranch>();
            foreach (var item in entry["answers"] as JArray ?? throw Missing("answers of node " + index))
            {
                var after = item.Value<int?>("after") ?? throw Missing("after");
                var node = item.Value<int?>("node") ?? throw Missing("node");
                var answer = item["answer"] ?? throw Missing("answer");
                if (after < 1 || after > events.Count || (after < events.Count && events[after].At == events[after - 1].At))
                {
                    throw new InvalidDataException("An answer of node " + index + " is offered where no instant of the recording ends.");
                }
                if (node <= index || node >= count)
                {
                    throw new InvalidDataException("An answer of node " + index + " continues with no later node.");
                }
                branches.Add(new DemonstrationBranch(after, ReadAnswer(answer), node));
            }

            var ending = entry["ending"] ?? throw Missing("ending of node " + index);
            var endingSnapshot = ending["snapshot"]?.Type == JTokenType.Object ? ending["snapshot"]!.ToObject<SnapshotMessage>(serializer) : null;
            if (endingSnapshot != null)
            {
                CheckJournal(endingSnapshot, journal, "ending");
                if (events.Count > 0 && endingSnapshot.Snapshot.Position != position)
                {
                    throw new InvalidDataException("The demonstration's ending is not where its last event left the journal.");
                }
            }
            var holdMs = ending.Value<long?>("hold_ms");
            if (holdMs < 0) throw new InvalidDataException("A hold cannot be negative.");
            return new DemonstrationNode(events, branches, endingSnapshot, holdMs == null ? (TimeSpan?)null : TimeSpan.FromMilliseconds(holdMs.Value));
        }

        private static DemonstrationAnswer ReadAnswer(JToken answer)
        {
            var kind = answer.Value<string>("kind") switch
            {
                "approve" => DemonstrationAnswerKind.Approve,
                "deny" => DemonstrationAnswerKind.Deny,
                "interrupt" => DemonstrationAnswerKind.Interrupt,
                "instruct" => DemonstrationAnswerKind.Instruct,
                var other => throw new InvalidDataException("The demonstration offers an unknown answer: " + (other ?? "none") + "."),
            };
            var executionId = answer.Value<string>("execution_id") ?? throw Missing("execution_id");
            var approvalId = answer.Value<string>("approval_id");
            var text = answer.Value<string>("text");
            var label = answer.Value<string>("label");
            if ((kind == DemonstrationAnswerKind.Approve || kind == DemonstrationAnswerKind.Deny) && approvalId == null) throw Missing("approval_id");
            if (kind == DemonstrationAnswerKind.Instruct && (text == null || label == null)) throw Missing("the instruction's text and label");
            return new DemonstrationAnswer(kind, executionId, approvalId, text, label);
        }

        /// <summary>
        /// Every node but the beginning is exactly one answer's, and each continues the journal from
        /// where its answer was given.
        /// </summary>
        private static void CheckTree(List<DemonstrationNode> nodes, long beginning)
        {
            var reached = new bool[nodes.Count];
            var startsAfter = new long[nodes.Count];
            startsAfter[0] = beginning;
            reached[0] = true;
            for (var index = 0; index < nodes.Count; index++)
            {
                if (!reached[index]) throw new InvalidDataException("Node " + index + " is no answer's continuation.");
                var node = nodes[index];
                if (node.Events.Count > 0 && node.Events[0].Message.Position <= startsAfter[index])
                {
                    throw new InvalidDataException("Node " + index + " goes back in the journal.");
                }
                foreach (var branch in node.Branches)
                {
                    if (reached[branch.Node]) throw new InvalidDataException("Node " + branch.Node + " continues more than one answer.");
                    reached[branch.Node] = true;
                    startsAfter[branch.Node] = node.Events[branch.After - 1].Message.Position;
                }
            }
        }

        private static void CheckJournal(SnapshotMessage snapshot, JournalInfo journal, string which)
        {
            if (snapshot.Snapshot.Journal.JournalId != journal.JournalId || snapshot.Snapshot.Journal.Origin != journal.Origin)
            {
                throw new InvalidDataException("The demonstration's " + which + " snapshot comes from another journal than its welcome.");
            }
        }

        private static InvalidDataException Missing(string property) =>
            new InvalidDataException("The demonstration has no " + property + ".");
    }
}
