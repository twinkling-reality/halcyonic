#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// A computer the headset found with its pairing open (ADR 0017, amendment of 2026-11-11), as the pairing card offers it:
    /// what the finder answered, never trusted. The Unity layer makes one from each <c>FoundComputer</c>.
    /// </summary>
    public readonly struct PairingChoice
    {
        public PairingChoice(string name, string address, bool sharesItsName = false)
        {
            Name = name;
            Address = address;
            SharesItsName = sharesItsName;
        }

        /// <summary>The name it announced, from outside: it leans.</summary>
        public string Name { get; }

        /// <summary>What pairing dials: <c>name.local:port</c>.</summary>
        public string Address { get; }

        /// <summary>Another answer came under the same name: both are offered, and the row says so.</summary>
        public bool SharesItsName { get; }
    }

    /// <summary>
    /// Pairing as cards in the card language (the addendum's A17): looking for the computer, one found, several found, none
    /// found with typing its address as the way on, the code, paired, and refused. A found computer is a neutral list row
    /// with the computer's mark and its name, marked only where its name came twice; a state is a title word over a quieter
    /// line; the tries left are pips. Host and port stay off screen; the code's eight places are the field's (FIELD's
    /// answers, 2026-11-11). No
    /// card joins two facts on a dot. The actions (pick a row, Type Address, Try Again, Cancel) are the bar's, wired by the
    /// Unity layer; the card only says where pairing is.
    /// </summary>
    public static class PairingCards
    {
        public const string LookingTitle = "Looking";
        public const string LookingLine = "For your computer on this Wi-Fi";

        public const string OneFoundTitle = "Your Computer";
        public const string OneFoundLine = "Pick it, then type the code it shows.";

        public const string SeveralFoundTitle = "Pick Your Computer";
        public const string SeveralFoundLine = "Then type the code it shows.";

        public const string NoneFoundTitle = "None Found";
        public const string NoneFoundLine = "Press Pair Headset on your computer, or type its address.";

        /// <summary>The note on a row whose name another answer also used: one of them is not the person's computer.</summary>
        public const string SameNameNote = "Same Name Twice";

        /// <summary>Said under the list where two answered under one name.</summary>
        public const string SameNameMore = "Two answered with one name. Check the code on your computer.";

        public const string CodeLine = "Type the eight-digit code it shows.";

        public const string PairedTitle = "Paired";
        public const string PairedLine = "Connecting over Wi-Fi";

        /// <summary>The tries a pairing window allows before it closes (the control plane's three failed attempts).</summary>
        public const int Tries = 3;

        /// <summary>Looking for computers with pairing open, for the two seconds the finder listens.</summary>
        public static RoomCard Looking() =>
            new RoomCard(status: new RoomCardStatus(LookingTitle, LookingLine, RoomTone.Working));

        /// <summary>
        /// What the finder found: one computer, its name as a row; several, each its own row in the order given; none, a
        /// status whose line says the way on. A name that came twice is marked on each of its rows.
        /// </summary>
        public static RoomCard Found(IReadOnlyList<PairingChoice> found)
        {
            if (found.Count == 0) return new RoomCard(status: new RoomCardStatus(NoneFoundTitle, NoneFoundLine, RoomTone.Quiet));
            var rows = found.Take(RoomCard.ListAPage).Select(Row).ToList();
            var shared = found.Any(choice => choice.SharesItsName);
            return found.Count == 1
                ? new RoomCard(title: LineWords.Ours(OneFoundTitle), description: LineWords.Ours(OneFoundLine), list: rows)
                : new RoomCard(title: LineWords.Ours(SeveralFoundTitle), description: LineWords.Ours(SeveralFoundLine), list: rows,
                    more: shared ? LineWords.Ours(SameNameMore) : null);
        }

        /// <summary>
        /// The code being typed for <paramref name="name"/>, a found computer's announced name, or null where the person
        /// typed <paramref name="address"/>: the name, or the address as typed, is the title. Nothing else of the host or
        /// the port shows; the code's eight places are the field's.
        /// </summary>
        public static RoomCard Code(string? name, string address) =>
            new RoomCard(title: Named(name, address), description: LineWords.Ours(CodeLine));

        /// <summary>Paired: a good status, the computer's name or the address as typed as the title.</summary>
        public static RoomCard Paired(string? name, string address) =>
            new RoomCard(status: new RoomCardStatus(PairedTitle, PairedLine, RoomTone.Good), title: Named(name, address));

        /// <summary>
        /// Pairing refused or broken, by the refusal's code (<see cref="PairingException.Code"/>), never its message, which
        /// whatever answered wrote. A wrong code shows the window's tries as pips, the used ones filled, where the computer
        /// said how many are left.
        /// </summary>
        public static RoomCard Refused(string? code, long? attemptsLeft = null)
        {
            var (title, line) = Why(code);
            var status = new RoomCardStatus(title, line.Length == 0 ? null : line, RoomTone.Bad);
            if (code == "wrong_code" && attemptsLeft is long left && left >= 0 && left < Tries)
                return new RoomCard(status: status, trackAt: Tries - (int)left, trackOf: Tries);
            return new RoomCard(status: status);
        }

        /// <summary>
        /// A refusal's title and its quieter line, by its code: each code the client or the computer really sends has its own
        /// words (FIELD's, 2026-11-12), and a code nobody named reads "Reason Unknown" with no line, never another case's
        /// words (R3). The line is empty where there is none.
        /// </summary>
        public static (string Title, string Line) Why(string? code) => code switch
        {
            "unreachable" => ("Computer Not Found", "Nothing answered at that address; check it, and that both are on one network"),
            "invalid_code" => ("Code Incomplete", "The code is the eight digits on your computer; type it again"),
            "refused" => ("Pairing Refused", "Your computer turned it away; press Pair Headset there, then try again"),
            "pairing_closed" => ("Pairing Closed", "Press Pair Headset on your computer, then try again"),
            "wrong_code" => ("Code Refused", "Check the code on your computer and type it again"),
            "too_many_requests" => ("Pairing Paused", "Too many tries; try again in a minute"),
            "busy" => ("Computer Busy", "Try again in a moment"),
            "timeout" => ("Timed Out", "Pairing took too long"),
            "connection_lost" => ("Connection Lost", "Check the address, and that pairing is open"),
            "protocol_error" => ("Can't Pair", "What answered doesn't pair the way your computer does"),
            "not_secure" => ("No Secure Connection", "Check the address and port, then try again in a minute"),
            "server_not_proven" => ("Not Verified", "Nothing was paired; check the address"),
            _ => (ReasonUnknown, ""),
        };

        /// <summary>The words of a code nobody named: unknown, said plainly.</summary>
        public const string ReasonUnknown = "Reason Unknown";

        /// <summary>A refusal's words as one spoken message: its title and its line as two sentences, never joined on a dot.</summary>
        public static string Said(string? code)
        {
            var (title, line) = Why(code);
            return line.Length == 0 ? title + "." : title + ". " + char.ToUpperInvariant(line[0]) + line.Substring(1) + ".";
        }

        private static RoomListRow Row(PairingChoice choice) =>
            choice.SharesItsName
                ? new RoomListRow(RoomKind.Computer, LineWords.TheirName(choice.Name), SameNameNote, RoomTone.Bad)
                : new RoomListRow(RoomKind.Computer, LineWords.TheirName(choice.Name));

        private static LineWords Named(string? name, string address) =>
            string.IsNullOrWhiteSpace(name) ? LineWords.Theirs(address) : LineWords.TheirName(name);
    }
}
