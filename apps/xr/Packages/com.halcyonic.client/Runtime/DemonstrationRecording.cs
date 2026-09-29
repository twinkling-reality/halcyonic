#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>One recorded event message, and when it was sent after the demonstration started.</summary>
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

    /// <summary>
    /// A demonstration the control plane recorded (<c>pnpm demonstration:record</c>): the welcome,
    /// snapshot and event messages a client receives from <c>pnpm replay</c> of a trace, and when.
    /// The control plane computed every state in it, so playing it derives nothing. Only a recording
    /// of a fixture journal is accepted, so everything it shows is labeled as recorded.
    /// </summary>
    public sealed class DemonstrationRecording
    {
        /// <summary>The version of the recording format this client plays.</summary>
        public const int FormatVersion = 1;

        private DemonstrationRecording(string source, WelcomeMessage welcome, SnapshotMessage snapshot, IReadOnlyList<RecordedEvent> events)
        {
            Source = source;
            Welcome = welcome;
            Snapshot = snapshot;
            Events = events;
        }

        /// <summary>The trace it was recorded from, relative to the repository root.</summary>
        public string Source { get; }

        public WelcomeMessage Welcome { get; }

        public SnapshotMessage Snapshot { get; }

        /// <summary>Every event message, in journal order.</summary>
        public IReadOnlyList<RecordedEvent> Events { get; }

        /// <summary>When the last event is sent.</summary>
        public TimeSpan Duration => Events.Count == 0 ? TimeSpan.Zero : Events[Events.Count - 1].At;

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
            var entries = document["events"] as JArray ?? throw Missing("events");

            var journal = welcome.Journal;
            if (journal.Origin != JournalOrigin.Fixture)
            {
                throw new InvalidDataException("A demonstration must come from a recorded (fixture) journal, so that it is labeled as recorded.");
            }
            if (snapshot.Snapshot.Journal.JournalId != journal.JournalId || snapshot.Snapshot.Journal.Origin != journal.Origin)
            {
                throw new InvalidDataException("The demonstration's snapshot comes from another journal than its welcome.");
            }
            if (welcome.Protocol != ContractVersions.RealtimeProtocol)
            {
                throw new InvalidDataException("The demonstration speaks realtime protocol " + welcome.Protocol + ".");
            }

            var events = new List<RecordedEvent>(entries.Count);
            var position = snapshot.Snapshot.Position;
            var at = TimeSpan.Zero;
            foreach (var entry in entries)
            {
                var atMs = entry.Value<long?>("at_ms") ?? throw Missing("at_ms");
                var message = entry["message"]?.ToObject<EventMessage>(serializer) ?? throw Missing("message");
                var next = TimeSpan.FromMilliseconds(atMs);
                if (message.Position <= position || next < at)
                {
                    throw new InvalidDataException(
                        "The demonstration's event at position " + message.Position + " is out of order.");
                }
                position = message.Position;
                at = next;
                events.Add(new RecordedEvent(at, message));
            }
            return new DemonstrationRecording(source, welcome, snapshot, events);
        }

        private static InvalidDataException Missing(string property) =>
            new InvalidDataException("The demonstration has no " + property + ".");
    }
}
