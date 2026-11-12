#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// What a host code says to the person: a title and, where it has one, a line apart (R3; FIELD's rule of 2026-11-12): never
    /// joined on a dot, the line a phrase with no final stop that says the fact and a next step only where no button does it.
    /// A drawer that has a status draws them apart (<see cref="Status"/>); a slot that holds one string takes them as one
    /// phrase (<see cref="Phrase"/>). Every table's default arm is <see cref="Unknown"/>: unknown in plain words, never another
    /// case's words.
    /// </summary>
    public readonly struct Notice
    {
        public Notice(string title, string? line = null)
        {
            Title = title;
            Line = line;
        }

        /// <summary>A code with no words of its own.</summary>
        public static Notice Unknown { get; } = new Notice("Reason Unknown");

        public string Title { get; }

        public string? Line { get; }

        /// <summary>The title and the line as one phrase for a slot of one string: "Sent, waiting for your computer".</summary>
        public string Phrase => Line == null || Line.Length == 0 ? Title : Title + ", " + char.ToLowerInvariant(Line[0]) + Line.Substring(1);

        /// <summary>The notice as a card's status, in <paramref name="tone"/>.</summary>
        public RoomCardStatus Status(RoomTone tone) => new RoomCardStatus(Title, Line, tone);

        public override string ToString() => Phrase;
    }
}
