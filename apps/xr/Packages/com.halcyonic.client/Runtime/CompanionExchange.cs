#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// One exchange with Create's companion (ADR 0025): the person's words and the companion's replies,
    /// in order, kept only on this device, and sent whole with each request, since the Mac keeps
    /// nothing. It asks one reply at a time, only after the person said something or asked for the
    /// recap, and stays inside the request's bounds, so the Mac never has to refuse one for its size.
    /// Every reply is model text: untrusted, reported, and shown only as the companion's; what it
    /// proposes becomes work only through the recap and the review, never from here.
    /// </summary>
    public sealed class CompanionExchange
    {
        /// <summary>The most messages one request carries (the contract's limit).</summary>
        public const int MaxMessages = 20;

        /// <summary>The longest message of the person's (the contract's limit).</summary>
        public const int PersonLimit = 2000;

        /// <summary>
        /// The most characters an exchange holds here: the Mac's 24,000, less room for the ways two
        /// JSON writers may spell the same reply.
        /// </summary>
        public const int CharacterLimit = 23000;

        /// <summary>Room kept for the reply that follows the person's words, so a recap can still be asked for after it.</summary>
        public const int ReplyRoom = 2500;

        /// <summary>The questions the companion asks before it only proposes, unless the Mac said otherwise.</summary>
        public const int DefaultMaxQuestions = 4;

        /// <summary>The failure's code when the Mac could not be reached at all, so it gave none.</summary>
        public const string Unreachable = "unreachable";

        private readonly List<CompanionExchangeTurn> turns = new List<CompanionExchangeTurn>();
        private int characters;

        public CompanionExchange(CompanionStart start, int maxQuestions = DefaultMaxQuestions)
        {
            Start = start;
            MaxQuestions = Math.Max(0, maxQuestions);
        }

        /// <summary>From the person's own idea, or from Help me figure it out.</summary>
        public CompanionStart Start { get; }

        public int MaxQuestions { get; }

        public IReadOnlyList<CompanionExchangeTurn> Turns => turns;

        /// <summary>A reply has been asked for and not yet come back.</summary>
        public bool Waiting { get; private set; }

        /// <summary>The person went on without the companion, or back; saying something again takes it up again.</summary>
        public bool Left { get; private set; }

        /// <summary>Why the last request got no reply, by the Mac's code, or <see cref="Unreachable"/>; null otherwise.</summary>
        public string? Failure { get; private set; }

        /// <summary>The model that answered last, as the Mac named it: kept for the record, never shown.</summary>
        public string? Model { get; private set; }

        /// <summary>What the last request asked for, so Try again asks the same.</summary>
        public CompanionWant LastWant { get; private set; } = CompanionWant.Next;

        /// <summary>Counts requests, so a reply to one the person has left behind is dropped.</summary>
        public int Generation { get; private set; }

        /// <summary>The companion's latest reply, or null before its first.</summary>
        public CompanionReply? Latest => turns.LastOrDefault() is CompanionTurn turn ? turn.Reply : null;

        /// <summary>The latest reply, when it proposes the recap.</summary>
        public ProposeReply? Proposal => Latest as ProposeReply;

        public int QuestionsAsked => turns.Count(turn => turn is CompanionTurn { Reply: AskReply });

        /// <summary>The person has said something, so the companion has something to work from.</summary>
        public bool PersonSpoke => turns.Any(turn => turn is PersonTurn);

        /// <summary>
        /// The person may say something now: nothing is on its way, the companion asked a question last
        /// (or nothing has been said yet), and the exchange has room for the words and a reply after
        /// them. Once it has proposed, the recap is where the person changes things.
        /// </summary>
        public bool CanSay => !Waiting && (turns.Count == 0 || turns[turns.Count - 1] is CompanionTurn { Reply: AskReply }) && turns.Count + 1 <= MaxMessages - 1;

        /// <summary>The person may ask for the recap now: they have said something, and nothing is on its way.</summary>
        public bool CanAskForRecap => !Waiting && PersonSpoke && turns.Count <= MaxMessages && Proposal == null;

        /// <summary>
        /// Adds the person's words: typed, a choice pressed, or a spoken draft they sent. Refused,
        /// changing nothing, when they are blank, longer than <see cref="PersonLimit"/>, or there is no
        /// room or turn for them.
        /// </summary>
        public bool Say(string? text)
        {
            var words = (text ?? "").Trim();
            if (words.Length == 0 || words.Length > PersonLimit || !CanSay) return false;
            if (characters + words.Length + ReplyRoom > CharacterLimit) return false;
            turns.Add(new PersonTurn { Text = words });
            characters += words.Length;
            Failure = null;
            Left = false;
            return true;
        }

        /// <summary>
        /// The request for the next reply, or null when none may be asked now. <paramref name="want"/>
        /// is <see cref="CompanionWant.Next"/> after the person's words, or when Help me figure it out
        /// has only begun, and <see cref="CompanionWant.Proposal"/> when the person asked for the recap.
        /// It marks the exchange waiting until <see cref="Replied"/> or <see cref="Failed"/>.
        /// </summary>
        public CompanionRepliesRequest? Ask(CompanionWant want)
        {
            if (Waiting) return null;
            var last = turns.LastOrDefault();
            if (want == CompanionWant.Next)
            {
                var opening = Start == CompanionStart.Help && turns.Count == 0;
                if (!opening && !(last is PersonTurn)) return null;
            }
            else if (!PersonSpoke || turns.Count > MaxMessages || last is CompanionTurn { Reply: ProposeReply })
            {
                return null;
            }
            Waiting = true;
            Failure = null;
            Left = false;
            LastWant = want;
            Generation++;
            return new CompanionRepliesRequest { Start = Start, Want = want, Messages = turns.ToList() };
        }

        /// <summary>Asks again what the last request asked, after a failure.</summary>
        public CompanionRepliesRequest? Retry() => Failure == null ? null : Ask(LastWant);

        /// <summary>The reply to request <paramref name="generation"/>; one to a request left behind changes nothing.</summary>
        public bool Replied(int generation, CompanionReplyResponse response)
        {
            if (!Waiting || generation != Generation) return false;
            // A recap asked for that comes back as a question, or a reply outside its bounds, is not shown.
            if ((LastWant == CompanionWant.Proposal && !(response.Reply is ProposeReply)) || !WithinBounds(response.Reply))
            {
                return Failed(generation, "companion_unreadable");
            }
            Waiting = false;
            Failure = null;
            Model = response.Companion.Name;
            turns.Add(new CompanionTurn { Reply = response.Reply });
            characters += Measure(response.Reply);
            return true;
        }

        /// <summary>Request <paramref name="generation"/> got no reply: why, by the Mac's code, or null when it could not be reached.</summary>
        public bool Failed(int generation, string? code)
        {
            if (!Waiting || generation != Generation) return false;
            Waiting = false;
            Failure = code ?? Unreachable;
            return true;
        }

        /// <summary>The person went on without the companion: a reply still on its way is dropped when it comes.</summary>
        public void Leave()
        {
            if (Waiting) Generation++;
            Waiting = false;
            Left = true;
        }

        /// <summary>
        /// An exchange as the device kept it (<see cref="CreationDraft"/>): the same turns, never
        /// waiting, since no request outlives the app. Turns that break the order or the bounds are
        /// dropped from the first that does, so a damaged file never becomes a request the Mac refuses.
        /// </summary>
        public static CompanionExchange Restore(CompanionStart start, int maxQuestions, IEnumerable<CompanionExchangeTurn> kept)
        {
            var exchange = new CompanionExchange(start, maxQuestions);
            foreach (var turn in kept)
            {
                if (turn is PersonTurn person)
                {
                    if (!exchange.Say(person.Text)) break;
                }
                else if (turn is CompanionTurn companion && companion.Reply != null)
                {
                    var last = exchange.turns.LastOrDefault();
                    var proposes = companion.Reply is ProposeReply;
                    // Twice in a row only to propose, as when the person asked for the recap after a question.
                    if ((last is CompanionTurn && !proposes) || (last == null && start == CompanionStart.Idea)) break;
                    // A final proposal may follow a full request, as one asked for with 20 messages does.
                    if (exchange.turns.Count + 1 > MaxMessages + (proposes ? 1 : 0)) break;
                    if (!WithinBounds(companion.Reply)) break;
                    var size = Measure(companion.Reply);
                    if (exchange.characters + size > CharacterLimit) break;
                    exchange.turns.Add(new CompanionTurn { Reply = companion.Reply });
                    exchange.characters += size;
                    if (proposes) break;
                }
                else break;
            }
            return exchange;
        }

        /// <summary>
        /// Whether a reply keeps to the contract's bounds: the Mac checks every reply it sends, so one
        /// that does not came from somewhere else, such as a damaged file.
        /// </summary>
        public static bool WithinBounds(CompanionReply? reply)
        {
            static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value!.Length <= max;
            if (reply == null || !Text(reply.Line, 300) || !Enum.IsDefined(typeof(CompanionView), reply.View)) return false;
            return reply switch
            {
                AskReply ask => ask.Question != null && Text(ask.Question.Text, 160) && ask.Question.Choices != null
                    && ask.Question.Choices.Count <= 4 && ask.Question.Choices.All(choice => Text(choice, 48)),
                ProposeReply propose => propose.Proposal != null && Text(propose.Proposal.ProjectName, 60) && Text(propose.Proposal.FirstTask, 1000),
                _ => false,
            };
        }

        /// <summary>
        /// The failure's code for an exception from asking the companion: the Mac's own code when it gave
        /// one; too slow when this side's time ran out; unreachable when the Mac could not be reached;
        /// unreadable for an answer that is not one.
        /// </summary>
        public static string CodeOf(Exception? error) => error switch
        {
            ControlPlaneRequestException { Code: string code } => code,
            ControlPlaneRequestException { InnerException: System.Net.Http.HttpRequestException _ } => Unreachable,
            OperationCanceledException _ => "companion_too_slow",
            Newtonsoft.Json.JsonException _ => "companion_unreadable",
            _ => "companion_failed",
        };

        /// <summary>A reply's size as the Mac counts it: its JSON.</summary>
        private static int Measure(CompanionReply reply) => HalcyonicJson.Serialize(reply).Length;
    }
}
