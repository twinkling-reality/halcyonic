#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>One option of an agent's question (ADR 0022).</summary>
        Answer,
    }

    /// <summary>An answer a person can give in the workspace, for which the recording holds a continuation.</summary>
    public sealed class DemonstrationAnswer
    {
        public DemonstrationAnswer(DemonstrationAnswerKind kind, string executionId, string? approvalId, string? text, string? label,
            string? questionId = null, IReadOnlyList<QuestionAnswer>? answers = null)
        {
            Kind = kind;
            ExecutionId = executionId;
            ApprovalId = approvalId;
            Text = text;
            Label = label;
            QuestionId = questionId;
            Answers = answers ?? Array.Empty<QuestionAnswer>();
        }

        public DemonstrationAnswerKind Kind { get; }

        public string ExecutionId { get; }

        /// <summary>The approval an approve or deny answers.</summary>
        public string? ApprovalId { get; }

        /// <summary>Exactly the instruction the recording sent, for an instruct.</summary>
        public string? Text { get; }

        /// <summary>What a button offering the instruction says, for an instruct; the option chosen, for an answer.</summary>
        public string? Label { get; }

        /// <summary>The question an answer is for.</summary>
        public string? QuestionId { get; }

        /// <summary>Exactly the answers the recording sent, for an answer; empty otherwise.</summary>
        public IReadOnlyList<QuestionAnswer> Answers { get; }
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
    /// A REST answer the recording's control plane gave about an execution: the answer once
    /// <see cref="After"/> of node <see cref="Node"/>'s events had played. It holds from there along
    /// every path through that point until the next answer recorded for the execution.
    /// </summary>
    public sealed class RecordedAnswer<T> where T : class
    {
        public RecordedAnswer(int node, int after, DateTimeOffset readAt, T response)
        {
            Node = node;
            After = after;
            ReadAt = readAt;
            Response = response;
        }

        public int Node { get; }

        public int After { get; }

        /// <summary>When the recording's control plane gave the answer, by the recording's clock.</summary>
        public DateTimeOffset ReadAt { get; }

        public T Response { get; }
    }

    /// <summary>
    /// A demonstration the control plane recorded (<c>pnpm demonstration:record</c>): the realtime
    /// messages it sent while its scripted operator gave each answer the recording offers, as a tree
    /// of nodes that shares its beginning, and its REST answers about each execution's understanding
    /// and evaluation along the way, read from stand-ins for the sources and marked as simulated
    /// (ADR 0019). The control plane computed every state in it, so playing it derives nothing. Only
    /// a recording of a fixture journal is accepted, so everything it shows is labeled as recorded,
    /// and only answers a stand-in gave, so none passes for a real source's. Keys this client does
    /// not know are ignored.
    /// </summary>
    public sealed class DemonstrationRecording
    {
        /// <summary>The version of the recording format this client plays.</summary>
        public const int FormatVersion = 2;

        private static readonly Dictionary<string, IReadOnlyList<RecordedAnswer<UnderstandingResponse>>> NoUnderstanding =
            new Dictionary<string, IReadOnlyList<RecordedAnswer<UnderstandingResponse>>>();

        private static readonly Dictionary<string, IReadOnlyList<RecordedAnswer<EvaluationResponse>>> NoEvaluation =
            new Dictionary<string, IReadOnlyList<RecordedAnswer<EvaluationResponse>>>();

        /// <summary>For every node but the beginning, the node it continues and how far that one had played.</summary>
        private readonly (int Node, int After)[] parents;

        private DemonstrationRecording(
            string source,
            WelcomeMessage welcome,
            SnapshotMessage snapshot,
            IReadOnlyList<DemonstrationNode> nodes,
            (int Node, int After)[] parents)
        {
            Source = source;
            Welcome = welcome;
            Snapshot = snapshot;
            Nodes = nodes;
            this.parents = parents;
        }

        /// <summary>The plan it was recorded from, relative to the repository root.</summary>
        public string Source { get; }

        public WelcomeMessage Welcome { get; }

        /// <summary>The state at the beginning, sent at the start and each time the demonstration starts again.</summary>
        public SnapshotMessage Snapshot { get; }

        /// <summary>The recorded stretches; the first is the beginning, and every other one is an answer's.</summary>
        public IReadOnlyList<DemonstrationNode> Nodes { get; }

        /// <summary>The control plane's understanding answers, by execution id, as the playback reaches them.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<RecordedAnswer<UnderstandingResponse>>> Understanding { get; private set; } = NoUnderstanding;

        /// <summary>The control plane's evaluation answers, by execution id, as the playback reaches them.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<RecordedAnswer<EvaluationResponse>>> Evaluation { get; private set; } = NoEvaluation;

        /// <summary>
        /// The understanding answer in force for an execution once <paramref name="played"/> of node
        /// <paramref name="node"/>'s events have played: the latest recorded on the way there, or null
        /// where the recording holds none, as before the execution exists.
        /// </summary>
        public RecordedAnswer<UnderstandingResponse>? UnderstandingAt(string executionId, int node, int played) =>
            InForce(Understanding, executionId, node, played);

        /// <summary>The evaluation answer in force for an execution at a point of the playback, as <see cref="UnderstandingAt"/>.</summary>
        public RecordedAnswer<EvaluationResponse>? EvaluationAt(string executionId, int node, int played) =>
            InForce(Evaluation, executionId, node, played);

        private RecordedAnswer<T>? InForce<T>(IReadOnlyDictionary<string, IReadOnlyList<RecordedAnswer<T>>> answers, string executionId, int node, int played)
            where T : class
        {
            if (node < 0 || node >= Nodes.Count || !answers.TryGetValue(executionId, out var recorded)) return null;
            while (true)
            {
                RecordedAnswer<T>? found = null;
                foreach (var answer in recorded)
                {
                    if (answer.Node == node && answer.After <= played && (found == null || answer.After > found.After)) found = answer;
                }
                if (found != null) return found;
                if (node == 0) return null;
                (node, played) = parents[node];
            }
        }

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
            var parents = CheckTree(nodes, snapshot.Snapshot.Position);
            var recording = new DemonstrationRecording(source, welcome, snapshot, nodes, parents);
            recording.Understanding = ReadAnswers<UnderstandingResponse>(document["understanding"], "understanding", serializer, nodes,
                (response, key) => response.ExecutionId == key && Simulated(response.Result));
            recording.Evaluation = ReadAnswers<EvaluationResponse>(document["evaluation"], "evaluation", serializer, nodes,
                (response, key) => response.ExecutionId == key && Simulated(response.Result));
            return recording;
        }

        /// <summary>
        /// Recorded answers by execution id. Each must name its execution and be one the demonstration
        /// may show: a stand-in's, marked synthetic, or the control plane's word that the runtime has not
        /// named its session yet. A real source's answer, or a refusal naming a source without saying it
        /// is simulated, is refused, so nothing in a recording passes for Salidium's or Seorak's.
        /// </summary>
        private static IReadOnlyDictionary<string, IReadOnlyList<RecordedAnswer<T>>> ReadAnswers<T>(
            JToken? token,
            string name,
            JsonSerializer serializer,
            IReadOnlyList<DemonstrationNode> nodes,
            Func<T, string, bool> acceptable)
            where T : class
        {
            var answers = new Dictionary<string, IReadOnlyList<RecordedAnswer<T>>>();
            if (token == null) return answers;
            if (!(token is JObject byExecution)) throw new InvalidDataException("The demonstration's " + name + " answers are not keyed by execution.");
            foreach (var property in byExecution.Properties())
            {
                var list = new List<RecordedAnswer<T>>();
                foreach (var item in property.Value as JArray ?? throw Missing(name + " answers of " + property.Name))
                {
                    var index = item.Value<int?>("node") ?? throw Missing("node of an answer");
                    var after = item.Value<int?>("after") ?? throw Missing("after of an answer");
                    var readAt = item.Value<string>("read_at") ?? throw Missing("read_at of an answer");
                    var response = item["response"]?.ToObject<T>(serializer) ?? throw Missing("response of an answer");
                    if (index < 0 || index >= nodes.Count) throw new InvalidDataException("An answer names no node of the demonstration.");
                    var events = nodes[index].Events;
                    if (after < 1 || after > events.Count || (after < events.Count && events[after].At == events[after - 1].At))
                    {
                        throw new InvalidDataException("A recorded " + name + " answer holds from where no instant of the recording ends.");
                    }
                    var previous = list.Count == 0 ? null : list[list.Count - 1];
                    if (previous != null && previous.Node == index && previous.After >= after)
                    {
                        throw new InvalidDataException("The recorded " + name + " answers of " + property.Name + " are out of order.");
                    }
                    if (!acceptable(response, property.Name))
                    {
                        throw new InvalidDataException("A recorded " + name + " answer is not a simulated source's answer about " + property.Name + ".");
                    }
                    if (!DateTimeOffset.TryParse(readAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                    {
                        throw new InvalidDataException("A recorded " + name + " answer has no time it was read.");
                    }
                    list.Add(new RecordedAnswer<T>(index, after, at, response));
                }
                answers[property.Name] = list;
            }
            return answers;
        }

        private static bool Simulated(UnderstandingResult result) => result switch
        {
            AvailableUnderstanding available => available.Understanding.Source.Synthetic,
            NotFoundUnderstanding notFound => notFound.Reason.Code == NativeIdUnknown,
            _ => false,
        };

        private static bool Simulated(EvaluationResult result) => result switch
        {
            AvailableEvaluation available => available.Evaluation.Source.Synthetic,
            NotFoundEvaluation notFound => notFound.Reason.Code == NativeIdUnknown,
            _ => false,
        };

        /// <summary>The control plane's own answer while a runtime has not named its session.</summary>
        private const string NativeIdUnknown = "native_id_unknown";

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
                branches.Add(new DemonstrationBranch(after, ReadAnswer(answer, serializer), node));
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

        private static DemonstrationAnswer ReadAnswer(JToken answer, JsonSerializer serializer)
        {
            var kind = answer.Value<string>("kind") switch
            {
                "approve" => DemonstrationAnswerKind.Approve,
                "deny" => DemonstrationAnswerKind.Deny,
                "interrupt" => DemonstrationAnswerKind.Interrupt,
                "instruct" => DemonstrationAnswerKind.Instruct,
                "answer" => DemonstrationAnswerKind.Answer,
                var other => throw new InvalidDataException("The demonstration offers an unknown answer: " + (other ?? "none") + "."),
            };
            var executionId = answer.Value<string>("execution_id") ?? throw Missing("execution_id");
            var approvalId = answer.Value<string>("approval_id");
            var text = answer.Value<string>("text");
            var label = answer.Value<string>("label");
            if ((kind == DemonstrationAnswerKind.Approve || kind == DemonstrationAnswerKind.Deny) && approvalId == null) throw Missing("approval_id");
            if (kind == DemonstrationAnswerKind.Instruct && (text == null || label == null)) throw Missing("the instruction's text and label");
            if (kind != DemonstrationAnswerKind.Answer) return new DemonstrationAnswer(kind, executionId, approvalId, text, label);
            var questionId = answer.Value<string>("question_id");
            var answers = answer["answers"]?.Type == JTokenType.Array ? answer["answers"]!.ToObject<List<QuestionAnswer>>(serializer) : null;
            if (questionId == null || label == null || answers == null || answers.Count == 0) throw Missing("the answer's question, option and answers");
            return new DemonstrationAnswer(kind, executionId, null, null, label, questionId, answers);
        }

        /// <summary>
        /// Every node but the beginning is exactly one answer's, and each continues the journal from
        /// where its answer was given. Returns, for each node, the node it continues and how far that
        /// one had played.
        /// </summary>
        private static (int Node, int After)[] CheckTree(List<DemonstrationNode> nodes, long beginning)
        {
            var reached = new bool[nodes.Count];
            var startsAfter = new long[nodes.Count];
            var parents = new (int Node, int After)[nodes.Count];
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
                    parents[branch.Node] = (index, branch.After);
                }
            }
            return parents;
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
