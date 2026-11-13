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
    /// the card's state under its title (the speech bubble, RoomKind.Says, and Agent Says or Model's Explanation; FIELD's 17:34Z; the
    /// critic, 2026-11-13: a slot beneath the words read as a button and came after them); the rest as rows,
    /// each with its kind and how it is known as its quiet note in our words (the product's chips join facts on a dot, so
    /// they are never drawn here). The files stay first and nothing the page offers moves behind it. An answer with no lines
    /// is no part. The foot says where the answers come from in FIELD's words, From the Task's Records, only where every answer
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
        /// <summary>A subagent's words, and words whose author the source does not know: said so, never given to the agent.</summary>
        public const string SubagentSays = "Subagent Says";
        public const string AuthorUnknown = "Author Unknown";
        /// <summary>A model's words are its explanation, written, never said, and never taken for the agent's (critic 9).</summary>
        public const string ModelSays = "Model's Explanation";
        public const string Inferred = "Inferred";
        public const string Planned = "Planned";

        /// <summary>
        /// The foot's source words (FIELD's, 2026-11-13T0903Z): the understanding is read from the task's own records, whoever
        /// spoke in them (the agent, a subagent, a model), so a subagent's or a model's part never reads as the agent's.
        /// </summary>
        public const string FromTasksRecords = "From the Task's Records";

        private readonly List<(UnderstandPrompt Prompt, SectionLine? Lead, RoomCardStatus? Who, List<(SectionLine Line, LineWords? Under)> Rows)> parts = new();

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
                List<(SectionLine Line, LineWords? Under)> rows;
                if (full != null && full.Lines.Count > 0)
                {
                    // How It's Built begins with how the work does it (critic 9: it began with why, repeating the Why card), the
                    // other steps after it, then the evidence, as the explanation gives them.
                    rows = Rows(prompt == UnderstandPrompt.HowBuilt ? HowFirst(full.Lines) : full.Lines);
                    // The lead is said once: where the full lines hold it, it stands in its own place among them (the latest
                    // reason with its files, the how under its step), never again as the description.
                    if (leads && rows.Any(row => row.Line.Text == lead.Text)) leads = false;
                }
                else
                {
                    rows = brief.Lines.Skip(leads ? 1 : 0).Select(line => (line, (LineWords?)null)).ToList();
                }
                // A part a speaker and a step (critic 7, 2026-11-13: one byline over another's words, and a step read from its
                // middle): a part begins where the words' author changes, where a step begins, or after five rows (RoomCard.ListAPage).
                // Its byline names the one who said its words; a part of observed words (the evidence) names no one.
                (UnderstandPrompt Prompt, SectionLine? Lead, RoomCardStatus? Who, List<(SectionLine Line, LineWords? Under)> Rows)? part = null;
                void Start(SectionLine? opening, RoomCardStatus? who)
                {
                    part = (prompt, opening, who, new List<(SectionLine, LineWords?)>());
                    parts.Add(part.Value);
                }
                if (leads) Start(lead, WhoSaid(lead));
                // The step a row belongs to: its heading heads every part of the step, as it heads every page of it at the desk.
                (SectionLine Line, LineWords? Under)? heading = null;
                foreach (var row in rows)
                {
                    if (row.Line.StartsPage) heading = row.Line.Repeats ? row : null;
                    var said = WhoSaid(row.Line);
                    var current = part;
                    var full_ = current != null && current.Value.Rows.Count >= RoomCard.ListAPage;
                    var otherSpeaker = current?.Who is RoomCardStatus speaking && said is RoomCardStatus speaker && speaker.Title != speaking.Title;
                    // Our own line after someone's words never stands under their byline (critic 9, 2026-11-13: "No reason given
                    // before KiteTests.cs changed" read as the subagent's): it begins a part of our own, with no one's name over it.
                    var oursAfterTheirs = current?.Who != null && said == null && !row.Line.StartsPage;
                    if (current == null || full_ || otherSpeaker || oursAfterTheirs || (row.Line.StartsPage && current.Value.Rows.Count > 0))
                    {
                        Start(null, said);
                        if (!row.Line.StartsPage && !oursAfterTheirs && heading is { } again) part!.Value.Rows.Add(again);
                    }
                    else if (current.Value.Who == null && said != null)
                    {
                        // The part's first words of anyone's set its byline.
                        part = (current.Value.Prompt, current.Value.Lead, said, current.Value.Rows);
                        parts[parts.Count - 1] = part.Value;
                    }
                    part!.Value.Rows.Add(row);
                }
            }
            // A stand-in's answers come from no agent app's records, so they name none (the recorded notice says what they are).
            Source = parts.Count > 0 && !simulated ? LineWords.Ours(FromTasksRecords) : null;
        }

        /// <summary>FIELD's source line, drawn quiet at each part's foot; null for a stand-in's answers or none.</summary>
        public LineWords? Source { get; }

        /// <summary>How many parts the account adds after the files: none where no answer has a line.</summary>
        public int Parts => parts.Count;

        /// <summary>The answers that have lines, in the order the page shows them.</summary>
        public IReadOnlyList<UnderstandPrompt> Prompts => parts.Select(part => part.Prompt).Distinct().ToList();

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
            Evidence.Reported => Who(line),
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
        /// Who said reported words, as the presenter's chip names them: the agent, a subagent, or an author the source does not
        /// know, never the agent by default.
        /// </summary>
        private static string Who(SectionLine line) =>
            line.Chip == SectionLine.SubagentChip ? SubagentSays : line.Chip == SectionLine.AuthorUnknownChip ? AuthorUnknown : AgentSays;

        /// <summary>
        /// The card's state saying whose a lead's words are, under the title and before them: the speech bubble and who said them
        /// (Agent Says, Subagent Says, Author Unknown) or Model's Explanation, quiet; none for our own or observed words.
        /// </summary>
        public static RoomCardStatus? WhoSaid(SectionLine line) => line.Evidence switch
        {
            Evidence.Reported => new RoomCardStatus(Who(line), null, RoomTone.Quiet, RoomKind.Says),
            Evidence.Explained => new RoomCardStatus(ModelSays, null, RoomTone.Quiet, RoomKind.Says),
            _ => null,
        };

        /// <summary>The account's <paramref name="part"/>th card (0 the first after the files): its answer's title, its lead and its rows.</summary>
        public RoomCard Card(int part)
        {
            if (part < 0 || part >= parts.Count) throw new ArgumentOutOfRangeException(nameof(part), part, "The account has no such part.");
            var (prompt, lead, who, lines) = parts[part];
            // Whose the words are is said once, by the part's byline (FIELD's 2026-11-13T0631Z words): a row it names carries no
            // note again, and the bubble stands on the byline alone, so a row's words are never pushed in for a mark (n38).
            bool Named(SectionLine line) => who is RoomCardStatus byline && NoteOf(line) == byline.Title;
            var rows = lines.Select(row => new RoomListRow(
                who != null && KindOf(row.Line) == RoomKind.Says ? RoomKind.None : KindOf(row.Line), row.Line.Runs,
                Named(row.Line) ? null : NoteOf(row.Line), under: who?.Title == ModelSays ? UnderstandingPresenter.Currency(row.Under) : row.Under)).ToList();
            return new RoomCard(LineWords.Ours(TitleOf(prompt)), lead?.Runs, status: who, list: rows, more: Source);
        }

        /// <summary>The explanation's steps with its How step first, each step whole (its heading and every line to the next heading).</summary>
        private static IReadOnlyList<SectionLine> HowFirst(IReadOnlyList<SectionLine> lines)
        {
            var steps = new List<List<SectionLine>>();
            foreach (var line in lines)
            {
                if (line.StartsPage || steps.Count == 0) steps.Add(new List<SectionLine>());
                steps[steps.Count - 1].Add(line);
            }
            var how = steps.FindIndex(step => step[0].StartsPage && step[0].Text == UnderstandingPresenter.HowStep);
            if (how > 0)
            {
                var first = steps[how];
                steps.RemoveAt(how);
                steps.Insert(0, first);
            }
            return steps.SelectMany(step => step).ToList();
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
