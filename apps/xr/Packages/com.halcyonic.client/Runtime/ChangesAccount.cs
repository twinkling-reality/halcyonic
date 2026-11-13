using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The understanding's account of a change as later parts of the result's one Changes page (SCREENS, the lead's
    /// 2026-11-12T1115Z and 1256Z): after the files, what changed, why and how it was built, each the brief answer XR
    /// computes from the understanding (<see cref="UnderstandingPresenter"/>, <see cref="AnswerDepth.Brief"/>), a part a
    /// card: the answer's lead line its description, whole, the agent's or a model's words alone, said to be theirs first, as
    /// the card's state under its title (the speech bubble, RoomKind.Says, and Agent Says or Model Says; FIELD's 17:34Z; the
    /// critic, 2026-11-13: a slot beneath the words read as a button and came after them); the rest as rows,
    /// each with its kind and how it is known as its quiet note in our words (the product's chips join facts on a dot, so
    /// they are never drawn here). The files stay first and nothing the page offers moves behind it. An answer with no lines
    /// is no part. The foot says where the answers come from in FIELD's words, From the Agent's Records, only where every answer
    /// shown was read from the agent app's own records, never a stand-in's (a simulated brief says no source); the brief's own
    /// provenance names the provider's product, which never stands on a person's screen, so it is not drawn.
    /// Where the answer's full lines are given beside its brief (SCREENS's 2026-11-13T0533Z proposal, FIELD's and XR's yes), its
    /// rows are every one of them, as the desk shows them, so Next Page reads every reason and step and the brief's "And N
    /// more" is gone: a line's detail beneath it (the files changed after a reason, a step's "Explained by a model") is its
    /// row's quieter line under it, and the lead, where the full lines hold it, stands once in its own place among them, not
    /// again as the description. Why and How It's Built are given whole; What Changed stays brief, since in full it is the
    /// files the page has already shown.
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

        /// <summary>The foot's source words (FIELD's, 2026-11-12T1734Z): the understanding is read from the agent app's own records.</summary>
        public const string FromAgentsRecords = "From the Agent's Records";

        private readonly List<(UnderstandPrompt Prompt, SectionLine? Lead, RoomCardStatus? Who, IReadOnlyList<(SectionLine Line, LineWords? Under)> Rows)> answers = new();
        private readonly List<(int Answer, int From)> parts = new();

        /// <param name="whatChanged">The brief answer to what changed, or null where none was read.</param>
        /// <param name="whyFull">Why's full lines from the same read, its rows where given (every reason and the files changed after it); the brief's otherwise.</param>
        /// <param name="howBuiltFull">How It's Built's full lines from the same read: every step under its heading, then the evidence.</param>
        public ChangesAccount(SectionPresentation? whatChanged, SectionPresentation? why, SectionPresentation? howBuilt,
            SectionPresentation? whyFull = null, SectionPresentation? howBuiltFull = null)
        {
            var simulated = false;
            foreach (var (prompt, brief, full) in new[]
            {
                (UnderstandPrompt.WhatChanged, whatChanged, (SectionPresentation?)null), (UnderstandPrompt.WhyChanged, why, whyFull),
                (UnderstandPrompt.HowBuilt, howBuilt, howBuiltFull),
            })
            {
                if (brief == null || brief.Lines.Count == 0) continue;
                simulated |= brief.Simulated;
                // The lead stands whole as the description where its knowing can be said before it; an inferred or planned
                // lead stays a row, its note saying so.
                var lead = brief.Lines[0];
                var leads = Leads(lead);
                var who = leads ? WhoSaid(lead) : null;
                List<(SectionLine Line, LineWords? Under)> rows;
                if (full != null && full.Lines.Count > 0)
                {
                    rows = Rows(full.Lines);
                    // The lead is said once: where the full lines hold it, it stands in its own place among them (the latest
                    // reason with its files, the how under its step), never again as the description.
                    if (leads && rows.Any(row => row.Line.Text == lead.Text)) leads = false;
                }
                else
                {
                    rows = brief.Lines.Skip(leads ? 1 : 0).Select(line => (line, (LineWords?)null)).ToList();
                }
                answers.Add((prompt, leads ? lead : null, who, rows));
                // Five rows a part, as every card's list (RoomCard.ListAPage); a long answer takes the parts it needs.
                parts.Add((answers.Count - 1, 0));
                for (var from = RoomCard.ListAPage; from < rows.Count; from += RoomCard.ListAPage) parts.Add((answers.Count - 1, from));
            }
            // A stand-in's answers come from no agent app's records, so they name none (the recorded notice says what they are).
            Source = answers.Count > 0 && !simulated ? LineWords.Ours(FromAgentsRecords) : null;
        }

        /// <summary>FIELD's source line, drawn quiet at each part's foot; null for a stand-in's answers or none.</summary>
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
        /// A line's kind on its row: a changed file its file's mark, what the agent said the speech bubble (RoomKind.Says), any
        /// other line none, which draws no mark. How it is known is the row's quiet note (<see cref="NoteOf"/>), never joined to it.
        /// </summary>
        public static RoomKind KindOf(SectionLine line) =>
            line.File != null ? RoomKind.File : line.Evidence == Evidence.Reported ? RoomKind.Says : RoomKind.None;

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
        /// agent's or a model's words, whose the card's state then says (<see cref="WhoSaid"/>); an inferred or planned line stays a row.
        /// </summary>
        public static bool Leads(SectionLine line) => line.Evidence switch
        {
            Evidence.Observed or Evidence.Measured or Evidence.Halcyonic or Evidence.Reported or Evidence.Explained => true,
            Evidence.Inferred or Evidence.Planned => false,
            _ => false,
        };

        /// <summary>
        /// The card's state saying whose a lead's words are, under the title and before them: the speech bubble and Agent Says,
        /// or Model Says, quiet; none for our own or observed words.
        /// </summary>
        public static RoomCardStatus? WhoSaid(SectionLine line) => line.Evidence switch
        {
            Evidence.Reported => new RoomCardStatus(AgentSays, null, RoomTone.Quiet, RoomKind.Says),
            Evidence.Explained => new RoomCardStatus(ModelSays, null, RoomTone.Quiet, RoomKind.Says),
            _ => null,
        };

        /// <summary>The account's <paramref name="part"/>th card (0 the first after the files): its answer's title and its rows.</summary>
        public RoomCard Card(int part)
        {
            if (part < 0 || part >= parts.Count) throw new ArgumentOutOfRangeException(nameof(part), part, "The account has no such part.");
            var (answer, from) = parts[part];
            var (prompt, lead, who, lines) = answers[answer];
            // Whose the words are is said once, by the byline on every part of the answer (FIELD's 2026-11-13T0631Z words): a row
            // the byline already names carries neither the bubble nor the note again; any other row keeps its own.
            bool Named(SectionLine line) => who is RoomCardStatus byline && NoteOf(line) == byline.Title;
            var rows = lines.Skip(from).Take(RoomCard.ListAPage).Select(row => new RoomListRow(
                Named(row.Line) && KindOf(row.Line) == RoomKind.Says ? RoomKind.None : KindOf(row.Line), row.Line.Runs,
                Named(row.Line) ? null : NoteOf(row.Line), under: row.Under)).ToList();
            // The lead on the answer's first part alone; a later part of a long answer carries on with its rows.
            var first = from == 0 && lead != null;
            return new RoomCard(LineWords.Ours(TitleOf(prompt)), first ? lead!.Runs : null, status: who, list: rows, more: Source);
        }

        /// <summary>
        /// A full answer's lines as rows: each line its row, a detail line under the line before it (the files changed after a
        /// reason, a step's "Explained by a model") that row's quieter line under it; a blank line is none.
        /// </summary>
        private static List<(SectionLine Line, LineWords? Under)> Rows(IReadOnlyList<SectionLine> lines)
        {
            var rows = new List<(SectionLine Line, LineWords? Under)>();
            foreach (var line in lines)
            {
                if (line.Text.Length == 0) continue;
                if (line.Detail && rows.Count > 0 && rows[rows.Count - 1].Under == null) rows[rows.Count - 1] = (rows[rows.Count - 1].Line, line.Runs);
                else rows.Add((line, null));
            }
            return rows;
        }
    }
}
