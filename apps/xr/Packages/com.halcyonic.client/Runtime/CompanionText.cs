#nullable enable
using System.Globalization;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The words around Create's companion (ADR 0025, the owner's table, with the host as
    /// <see cref="HostText"/> says it). The companion's own words are model text: they show only
    /// quoted and tagged as its, through <see cref="LabelText.Plain"/>, and never in Halcyonic's voice;
    /// its view of an idea is said as its opinion. Halcyonic never names the model here.
    /// </summary>
    public static class CompanionText
    {
        /// <summary>Help me figure it out's second line when the companion can be asked.</summary>
        public const string TalkItThrough = "Talk It Through";

        /// <summary>The main action while Talk it through with the companion is chosen (ADR 0026).</summary>
        public const string TalkItThroughShort = "Talk It Through";

        /// <summary>Over the exchange: what the companion is, and that it can be wrong.</summary>
        public const string Note = "Local AI · May Be Wrong · Answers Editable";

        /// <summary>
        /// The same on a side panel showing what the companion suggested, where <see cref="Note"/> needs
        /// three rows of a 26 degree column and a note has two; the side panel's Change stands beside it
        /// (the coordinator, 2026-10-03).
        /// </summary>
        public const string SideNote = "Suggested · Local AI · May Be Wrong";

        /// <summary>The companion's line, quoted and tagged as its own.</summary>
        public static string Says(string line) => "Companion · " + LabelText.Plain(line);

        public const string ThinksUnclear = "Companion · Unclear";
        public const string ThinksNotBuildable = "Companion · Not Buildable as Software";

        /// <summary>The companion's view, as its opinion, or null when it found the idea clear.</summary>
        public static string? View(CompanionView view) => view switch
        {
            CompanionView.Unclear => ThinksUnclear,
            CompanionView.NotBuildable => ThinksNotBuildable,
            _ => null,
        };

        public const string Waiting = "Awaiting Companion";

        /// <summary>Under <see cref="Waiting"/> once the wait is long: its model may be finishing a step of a task's work.</summary>
        public const string WaitingLong = "Model May Be Busy with a Task";

        /// <summary>How long a wait is before <see cref="WaitingLong"/> shows.</summary>
        public const double WaitingLongSeconds = 5;

        public const string MakeTheRecap = "Make Recap";

        /// <summary>
        /// The main prompt while Go on without it is chosen: the recap from the person's own words,
        /// without asking the companion (the coordinator, 2026-10-02).
        /// </summary>
        public const string MakeTheRecapFromMyWords = "Recap My Words";

        /// <summary>The same, where a footer measures too narrow for it beside Close and Hold to talk.</summary>
        public const string RecapFromMyWords = "Recap My Words";

        /// <summary>Why Send answer can't be pressed in the demonstration until its recorded answer is chosen: a question's own words on a file.</summary>
        public const string ChooseOne = "Choose One";

        /// <summary>Why Make the recap can't be pressed yet: the person has said nothing for the companion to work from.</summary>
        public const string AnswerFirst = "Answer One First";

        public const string GoOnWithout = "Continue Without";
        public const string UseMyWords = "Use My Words";
        public const string TypeAnswer = "Type Answer";

        /// <summary>Under a name or first task the companion proposed, until the person changes it.</summary>
        public const string Suggested = "Suggested · Companion";

        /// <summary>The small fact beside a recap fact the companion suggested, while its suggestion stands (ADR 0026).</summary>
        public const string SuggestedShort = "Suggested";

        /// <summary>The person's own words, beside the first task the companion suggested in its place.</summary>
        public const string YourOwnWords = "Your Words";

        public const string NotSetUp = "Companion Not Set Up · Type Idea or Answer Questions";
        public const string CantRun = "Companion Unavailable · Type Idea or Answer Questions";
        public const string TooSlow = "Companion Timed Out · Model May Be Busy · Retry or Continue Without";
        public const string Unreadable = "Answer Unreadable · Not Shown · Retry or Continue Without";
        public const string Busy = "Companion Busy · Retry Soon";
        public const string Unreached = "Computer Unreachable · Retry or Continue Without";
        public const string TooMany = "Too Many Questions · Retry Soon";
        public const string CouldNotAsk = "Companion Not Asked · Retry or Continue Without";

        /// <summary>The person's words are longer than the companion takes.</summary>
        public static string TooLong(int limit) => "Too Long · Under " + limit.ToString("N0", CultureInfo.InvariantCulture) + " Characters";

        /// <summary>The exchange holds all it can: only the recap is left.</summary>
        public const string Full = "Companion Full · Make Recap or Continue Without";

        /// <summary>Why the companion can't be asked, from the Mac's code (<c>GET /api/companion</c>), never from its message.</summary>
        public static string Unavailable(string? code) => code == "companion_not_set_up" ? NotSetUp : CantRun;

        /// <summary>Why a turn got no reply, from its code, never from the Mac's message; null for one the person ended.</summary>
        public static string? Failure(string? code) => code switch
        {
            "companion_cancelled" => null,
            "companion_not_set_up" => NotSetUp,
            "companion_not_running" or "companion_model_missing" or "companion_model_not_local" => CantRun,
            "companion_too_slow" => TooSlow,
            "companion_unreadable" => Unreadable,
            "companion_busy" or "companion_busy_on_mac" => Busy,
            "rate_limited" => TooMany,
            CompanionExchange.Unreachable => Unreached,
            _ => CouldNotAsk,
        };

        /// <summary>In the demonstration: the companion's replies were recorded, and nothing is asked.</summary>
        public const string Recorded = "Recorded Replies · Companion Not Asked";
    }
}
