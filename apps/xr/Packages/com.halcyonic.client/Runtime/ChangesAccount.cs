using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The understanding's account of a change as later parts of the result's one Changes page (SCREENS, the lead's
    /// 2026-11-12T1115Z and 1256Z): after the files, what changed, why and how it was built, each the brief answer XR
    /// computes from the understanding (<see cref="UnderstandingPresenter"/>, <see cref="AnswerDepth.Brief"/>), a part a
    /// card: the answer's lead line its description, whole, the agent's or a model's words alone with a slot of whose they are
    /// (the agent's mark and Agent Says, or Model Says), so ours never run into theirs; the rest as rows,
    /// each with its kind and how it is known as its quiet note in our words (the product's chips join facts on a dot, so
    /// they are never drawn here). The files stay first and nothing the page offers moves behind it. An answer with no lines
    /// is no part. The source line is FIELD's words, given whole (<paramref name="source"/>):
    /// the brief's own provenance names the provider's product, which never stands on a person's screen, so it is not drawn.
    /// </summary>
    public sealed class ChangesAccount
    {
        /// <summary>The answer's card title, FIELD's to word (Title Case, three words at most).</summary>
        public const string WhatChangedTitle = "What Changed";
        public const string WhyTitle = "Why";
        public const string HowBuiltTitle = "How It's Built";

        /// <summary>A row's note for how it is known, our words with no dot; none for what was observed or is Halcyonic's own.</summary>
        public const string AgentSays = "Agent Says";
        public const string ModelSays = "Model Says";
        public const string Inferred = "Inferred";
        public const string Planned = "Planned";

        private readonly List<(UnderstandPrompt Prompt, SectionLine? Lead, IReadOnlyList<SectionLine> Rows)> answers = new();
        private readonly List<(int Answer, int From)> parts = new();

        /// <param name="whatChanged">The brief answer to what changed, or null where none was read.</param>
        /// <param name="source">Where the answers come from, in FIELD's words; none is drawn where null.</param>
        public ChangesAccount(SectionPresentation? whatChanged, SectionPresentation? why, SectionPresentation? howBuilt, LineWords? source = null)
        {
            Source = source is LineWords given && !given.IsEmpty ? given : (LineWords?)null;
            foreach (var (prompt, brief) in new[] { (UnderstandPrompt.WhatChanged, whatChanged), (UnderstandPrompt.WhyChanged, why), (UnderstandPrompt.HowBuilt, howBuilt) })
            {
                if (brief == null || brief.Lines.Count == 0) continue;
                // The lead stands whole as the description where its knowing can be said before it; an inferred or planned
                // lead stays a row, its note saying so.
                var lead = brief.Lines[0];
                var leads = Leads(lead);
                var rows = brief.Lines.Skip(leads ? 1 : 0).ToList();
                answers.Add((prompt, leads ? lead : null, rows));
                // Five rows a part, as every card's list (RoomCard.ListAPage); a long answer takes the parts it needs.
                parts.Add((answers.Count - 1, 0));
                for (var from = RoomCard.ListAPage; from < rows.Count; from += RoomCard.ListAPage) parts.Add((answers.Count - 1, from));
            }
        }

        /// <summary>FIELD's source line, drawn quiet at each part's foot; null where none was given.</summary>
        public LineWords? Source { get; }

        /// <summary>How many parts the account adds after the files: none where no answer has a line.</summary>
        public int Parts => parts.Count;

        /// <summary>The answers that have lines, in the order the page shows them.</summary>
        public IReadOnlyList<UnderstandPrompt> Prompts => answers.Select(answer => answer.Prompt).ToList();

        public static string TitleOf(UnderstandPrompt prompt) => prompt switch
        {
            UnderstandPrompt.WhatChanged => WhatChangedTitle,
            UnderstandPrompt.WhyChanged => WhyTitle,
            UnderstandPrompt.HowBuilt => HowBuiltTitle,
            _ => throw new ArgumentOutOfRangeException(nameof(prompt), prompt, "Unhandled prompt."),
        };

        /// <summary>
        /// A line's kind on its row: a changed file its file's mark, what the agent said the agent's, any other line none. The
        /// chip ("Agent says", "Inferred", "Planned") is the row's quiet note, its words alone, never joined to the line.
        /// </summary>
        public static RoomKind KindOf(SectionLine line) =>
            line.File != null ? RoomKind.File : line.Evidence == Evidence.Reported ? RoomKind.Agent : RoomKind.None;

        /// <summary>How a row's line is known, its note; null where it was observed, measured or is Halcyonic's own.</summary>
        public static string? NoteOf(SectionLine line) => line.Evidence switch
        {
            Evidence.Reported => AgentSays,
            Evidence.Explained => ModelSays,
            Evidence.Inferred => Inferred,
            Evidence.Planned => Planned,
            _ => null,
        };

        /// <summary>
        /// Whether a line can lead as the description, whole: what was observed or measured, Halcyonic's own words, or the
        /// agent's or a model's words, whose a slot then says (<see cref="WhoSaid"/>); an inferred or planned line stays a row.
        /// </summary>
        public static bool Leads(SectionLine line) => line.Evidence switch
        {
            Evidence.Observed or Evidence.Measured or Evidence.Halcyonic or Evidence.Reported or Evidence.Explained => true,
            Evidence.Inferred or Evidence.Planned => false,
            _ => false,
        };

        /// <summary>The slot saying whose a lead's words are: the agent's mark and Agent Says, or Model Says; none for our own or observed words.</summary>
        public static RoomSlot? WhoSaid(SectionLine line) => line.Evidence switch
        {
            Evidence.Reported => new RoomSlot(RoomKind.Agent, LineWords.Ours(AgentSays)),
            Evidence.Explained => new RoomSlot(RoomKind.Agent, LineWords.Ours(ModelSays)),
            _ => null,
        };

        /// <summary>The account's <paramref name="part"/>th card (0 the first after the files): its answer's title and its rows.</summary>
        public RoomCard Card(int part)
        {
            if (part < 0 || part >= parts.Count) throw new ArgumentOutOfRangeException(nameof(part), part, "The account has no such part.");
            var (answer, from) = parts[part];
            var (prompt, lead, lines) = answers[answer];
            var rows = lines.Skip(from).Take(RoomCard.ListAPage).Select(line => new RoomListRow(KindOf(line), line.Runs, NoteOf(line))).ToList();
            // The lead on the answer's first part alone; a later part of a long answer carries on with its rows.
            var first = from == 0 && lead != null;
            var who = first ? WhoSaid(lead!) : null;
            return new RoomCard(LineWords.Ours(TitleOf(prompt)), first ? lead!.Runs : null, slots: who is RoomSlot slot ? new[] { slot } : null, list: rows, more: Source);
        }
    }
}
