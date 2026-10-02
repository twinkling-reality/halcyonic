#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>
    /// The companion's part of the recorded demonstration (ADR 0012, ADR 0025): one exchange recorded
    /// once from the real companion (<c>pnpm companion:record</c>), played with no computer and no
    /// model. The person presses the answers that were recorded, in order, and asks for the recap where
    /// the recording did; every reply is the recorded one, said to be recorded, and nothing is asked.
    /// The model's name is kept in the file for the record and never shown.
    /// </summary>
    public sealed class CompanionRecording
    {
        /// <summary>The resource the headset loads, beside the demonstration's own recording.</summary>
        public const string ResourceName = "HalcyonicCompanionDemonstration";

        public const int Version = 1;

        /// <summary>The name a played reply carries in place of the model's, which is never shown.</summary>
        public const string RecordedModel = "recorded";

        private readonly List<CompanionExchangeTurn> turns;

        private CompanionRecording(string idea, List<CompanionExchangeTurn> turns)
        {
            Idea = idea;
            this.turns = turns;
        }

        /// <summary>The idea the recording starts from.</summary>
        public string Idea { get; }

        /// <summary>Every turn, as recorded: the idea, the companion's replies and the answers pressed.</summary>
        public IReadOnlyList<CompanionExchangeTurn> Turns => turns;

        /// <summary>The recording's proposal, which Start building would carry if the demo could start work.</summary>
        public CompanionProposal Proposal => ((ProposeReply)((CompanionTurn)turns[turns.Count - 1]).Reply).Proposal;

        /// <summary>
        /// Reads a recording, or throws <see cref="FormatException"/> when it breaks the rules it is
        /// recorded under: the idea first, the exchange's order, every answer one of the choices just
        /// offered, and a proposal at the end and nowhere else.
        /// </summary>
        public static CompanionRecording Parse(string json)
        {
            RecordingFile? file;
            try
            {
                file = JsonConvert.DeserializeObject<RecordingFile>(json, HalcyonicJson.Tolerant);
            }
            catch (JsonException error)
            {
                throw new FormatException("The companion's recording is not JSON in its shape.", error);
            }
            if (file == null || file.Version != Version) throw new FormatException("The companion's recording has an unknown version.");
            var turns = file.Turns ?? new List<CompanionExchangeTurn>();
            if (turns.Count < 3 || !(turns[0] is PersonTurn first) || string.IsNullOrWhiteSpace(first.Text))
            {
                throw new FormatException("The companion's recording starts from an idea and holds at least a reply and a proposal.");
            }
            for (var index = 1; index < turns.Count; index++)
            {
                var turn = turns[index];
                var before = turns[index - 1];
                var last = index == turns.Count - 1;
                switch (turn)
                {
                    case PersonTurn person:
                        if (!(before is CompanionTurn { Reply: AskReply ask }) || !ask.Question.Choices.Contains(person.Text))
                            throw new FormatException("Each answer in the companion's recording is one of the choices just offered.");
                        break;
                    case CompanionTurn { Reply: ProposeReply _ } when !last:
                        throw new FormatException("The companion's recording proposes only at its end.");
                    case CompanionTurn { Reply: AskReply _ } when last:
                        throw new FormatException("The companion's recording ends with a proposal.");
                    case CompanionTurn { Reply: AskReply _ } when before is CompanionTurn:
                        throw new FormatException("The companion asks only after the person's words.");
                    case CompanionTurn { Reply: null }:
                    case null:
                        throw new FormatException("A turn of the companion's recording is empty.");
                }
            }
            if (!(turns[turns.Count - 1] is CompanionTurn { Reply: ProposeReply }))
                throw new FormatException("The companion's recording ends with a proposal.");
            return new CompanionRecording(first.Text, turns);
        }

        /// <summary>Begins the played exchange: the recorded idea in the recap, and the companion's first recorded reply.</summary>
        public CompanionExchange Begin(ProjectIdea idea)
        {
            idea.UseIdea(Idea);
            var exchange = idea.BeginCompanion(CompanionStart.Idea);
            Play(exchange);
            return exchange;
        }

        /// <summary>The answer recorded at this point of the exchange, the only one a press plays; null where the recording asks for the recap.</summary>
        public string? RecordedAnswer(CompanionExchange exchange)
        {
            var next = Next(exchange);
            return next is PersonTurn person ? person.Text : null;
        }

        /// <summary>The recording asks for the recap at this point, so Make the recap plays its proposal.</summary>
        public bool RecapHere(CompanionExchange exchange) => Next(exchange) is CompanionTurn { Reply: ProposeReply };

        /// <summary>Plays a press of <paramref name="answer"/>: only the recorded one, which brings the recorded reply.</summary>
        public bool Press(CompanionExchange exchange, string answer)
        {
            if (RecordedAnswer(exchange) != answer || !exchange.Say(answer)) return false;
            Play(exchange);
            return true;
        }

        /// <summary>Plays Make the recap, where the recording asked for it: its proposal.</summary>
        public bool AskForRecap(CompanionExchange exchange)
        {
            if (!RecapHere(exchange)) return false;
            if (exchange.Ask(CompanionWant.Proposal) == null) return false;
            return exchange.Replied(exchange.Generation, Recorded(((CompanionTurn)Next(exchange, waiting: true)!).Reply));
        }

        /// <summary>The recorded turn after those the exchange holds, or null at the end or when the exchange left the recording.</summary>
        private CompanionExchangeTurn? Next(CompanionExchange exchange, bool waiting = false)
        {
            var held = exchange.Turns;
            if (held.Count >= turns.Count) return null;
            for (var index = 0; index < held.Count; index++)
            {
                if (!Same(held[index], turns[index])) return null;
            }
            return waiting || !exchange.Waiting ? turns[held.Count] : null;
        }

        /// <summary>Plays the recorded reply that follows the person's words.</summary>
        private void Play(CompanionExchange exchange)
        {
            if (!(Next(exchange) is CompanionTurn reply) || exchange.Ask(CompanionWant.Next) == null) return;
            exchange.Replied(exchange.Generation, Recorded(reply.Reply));
        }

        private static CompanionReplyResponse Recorded(CompanionReply reply) => new CompanionReplyResponse
        {
            Reply = reply,
            Provenance = "reported",
            Companion = new CompanionModel { Name = RecordedModel, Served = "this_mac" },
        };

        private static bool Same(CompanionExchangeTurn held, CompanionExchangeTurn recorded) => (held, recorded) switch
        {
            (PersonTurn a, PersonTurn b) => a.Text == b.Text,
            (CompanionTurn a, CompanionTurn b) => ReferenceEquals(a.Reply, b.Reply) || HalcyonicJson.Serialize(a.Reply) == HalcyonicJson.Serialize(b.Reply),
            _ => false,
        };

        private sealed class RecordingFile
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("turns")]
            public List<CompanionExchangeTurn>? Turns { get; set; }
        }
    }
}
