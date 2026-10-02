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
        public const string TalkItThrough = "Talk it through with the companion";

        /// <summary>The main action while Talk it through with the companion is chosen (ADR 0026).</summary>
        public const string TalkItThroughShort = "Talk it through";

        /// <summary>Over the exchange: what the companion is, and that it can be wrong.</summary>
        public const string Note = "The companion is an AI on " + HostText.Your + ". It can be wrong, and you can change everything before you start.";

        /// <summary>The companion's line, quoted and tagged as its own.</summary>
        public static string Says(string line) => "The companion says: “" + LabelText.Plain(line) + "”";

        public const string ThinksUnclear = "The companion thinks this is unclear.";
        public const string ThinksNotBuildable = "The companion thinks this can't be built as software.";

        /// <summary>The companion's view, as its opinion, or null when it found the idea clear.</summary>
        public static string? View(CompanionView view) => view switch
        {
            CompanionView.Unclear => ThinksUnclear,
            CompanionView.NotBuildable => ThinksNotBuildable,
            _ => null,
        };

        public const string Waiting = "Waiting for the companion…";

        /// <summary>Under <see cref="Waiting"/> once the wait is long: its model may be finishing a step of a task's work.</summary>
        public const string WaitingLong = HostText.YourStart + "'s model may be busy with a task.";

        /// <summary>How long a wait is before <see cref="WaitingLong"/> shows.</summary>
        public const double WaitingLongSeconds = 5;

        public const string MakeTheRecap = "Make the recap";

        /// <summary>
        /// The main prompt while Go on without it is chosen: the recap from the person's own words,
        /// without asking the companion (the coordinator, 2026-10-02).
        /// </summary>
        public const string MakeTheRecapFromMyWords = "Make the recap from my words";

        /// <summary>The same, where a footer measures too narrow for it beside Close and Hold to talk.</summary>
        public const string RecapFromMyWords = "Recap from my words";

        /// <summary>Why Send answer can't be pressed in the demonstration until its recorded answer is chosen: a question's own words on a file.</summary>
        public const string ChooseOne = "Choose one.";

        /// <summary>Why Make the recap can't be pressed yet: the person has said nothing for the companion to work from.</summary>
        public const string AnswerFirst = "Answer a question first, or go on without it.";

        public const string GoOnWithout = "Go on without it";
        public const string UseMyWords = "Use my words";
        public const string TypeAnswer = "Type my answer";

        /// <summary>Under a name or first task the companion proposed, until the person changes it.</summary>
        public const string Suggested = "Suggested by the companion";

        /// <summary>The small fact beside a recap fact the companion suggested, while its suggestion stands (ADR 0026).</summary>
        public const string SuggestedShort = "Suggested";

        /// <summary>The person's own words, beside the first task the companion suggested in its place.</summary>
        public const string YourOwnWords = "Your own words";

        public const string NotSetUp = "The companion isn't set up on " + HostText.Your + ". Type your idea, or answer a few fixed questions.";
        public const string CantRun = "The companion can't run on " + HostText.Your + " right now. Type your idea, or answer a few fixed questions.";
        public const string TooSlow = "The companion took too long. " + HostText.YourStart + "'s model may be busy with a task. Try again, or go on without it.";
        public const string Unreadable = "The companion's answer didn't make sense, so it isn't shown. Try again, or go on without it.";
        public const string Busy = "The companion is answering someone else. Try again in a moment.";
        public const string Unreached = "Couldn't reach " + HostText.Your + ". Try again, or go on without it.";
        public const string TooMany = "That's a lot of questions in a minute. Wait a moment, then try again.";
        public const string CouldNotAsk = "Couldn't ask the companion. Try again, or go on without it.";

        /// <summary>The person's words are longer than the companion takes.</summary>
        public static string TooLong(int limit) => "That's too long for the companion. Keep it under " + limit.ToString("N0", CultureInfo.InvariantCulture) + " characters.";

        /// <summary>The exchange holds all it can: only the recap is left.</summary>
        public const string Full = "The companion can't take in any more. Make the recap, or go on without it.";

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
        public const string Recorded = "Recorded replies. Nothing here asks the companion.";
    }
}
