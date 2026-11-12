#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>What a review's confirmation asks, and Start Over's (the addendum's A2; the "{action}?" form of "Approve Request?").</summary>
        public const string StartTaskAsks = "Start Task?";
        public const string StartOverAsks = "Start Over?";

        /// <summary>The page of what can be changed, as a list card: its title where the page names no project.</summary>
        public const string ChangeTitle = "Change";

        /// <summary>
        /// The panel's words as rows parted by hairlines, each with its mark (<see cref="RoomRows"/>), where a moment
        /// gives them so: the recap's facts, a start's steps. <see cref="Lines"/> holds the same words for a view that
        /// draws no rows. Null where the panel is one block of words.
        /// </summary>
        public IReadOnlyList<RoomRow>? Facts { get; private set; }

        private RoomBar WithFacts(IReadOnlyList<RoomRow> facts)
        {
            Facts = facts;
            return this;
        }

        /// <summary>Halcyonic's own lines that only fill a page: the redesign leaves them out (rule 23).</summary>
        private static readonly HashSet<string> NewProjectFiller = new HashSet<string>(System.StringComparer.Ordinal)
        {
            EntryText.NothingStartsYet, EntryText.RecapLine, EntryText.ReviewLine, EntryText.SendingLine,
        };

        /// <summary>
        /// New Project as the redesign draws it (the addendum's A2), every action the page's own, the main one first:
        /// Your Idea as the field with its sentence; a question with its answers as marks, the local AI's own question on
        /// the band; the recap's facts as rows, with Start Task and Change; the page of what can be changed in the
        /// panel's place; the review as the same rows under "Start Task?" with Confirm behind its lock and Cancel; the
        /// start's steps as rows, a check on each confirmed and the one under way sweeping under the hairline; a start
        /// whose outcome is unknown, and a chooser's page, as their lines and actions. An action that cannot act is not
        /// drawn, and its reason stands as the panel's sentence. Null for any page that is not New Project's.
        /// </summary>
        public static RoomBar? NewProject(MenuFrame frame, VoiceStage voice)
        {
            string Key(NewProjectStep step) => NewProjectScreens.Key(step);
            if (!new[] { NewProjectStep.YourIdea, NewProjectStep.Questions, NewProjectStep.Recap, NewProjectStep.Build }.All(step => frame.Sections.Any(section => section.Key == Key(step))))
                return null;
            var chosen = frame.Sections.FirstOrDefault(section => section.Chosen)?.Key;
            var plain = frame.Lines.Where(line => line.Action == null && !NewProjectFiller.Contains(line.Words)).ToList();
            var prompts = frame.Footer.All.Select(each => each.Prompt).ToList();
            Prompt? Offered(string id) => prompts.FirstOrDefault(prompt => prompt.Id == id && prompt.Available);
            var main = prompts.FirstOrDefault(prompt => prompt.Main && prompt.Kind == PromptKind.Action);
            var yes = prompts.FirstOrDefault(prompt => prompt.Kind == PromptKind.Yes);
            var cancel = prompts.FirstOrDefault(prompt => prompt.Kind == PromptKind.Cancel);
            var close = prompts.FirstOrDefault(prompt => prompt.Kind == PromptKind.Close);
            // Hold to Talk where the page offers it: held, it records, as the row's does (the view raises it as the held Talk).
            var hold = prompts.FirstOrDefault(prompt => prompt.Holds && prompt.Available);
            // Why the page's main action or its Confirm cannot be taken: the panel's sentence where one stands.
            var why = frame.Reason;

            RoomAction Of(Prompt prompt, KitPlace place, bool isMain = false, bool locks = false) =>
                new RoomAction(new KitControl(place, frame, prompt.Id, null, prompt.Words, prompt.Icon, available: true, reason: null, holds: prompt.Holds), isMain, locks: locks);
            RoomAction Line(PageLine line, string? words = null, GlazeIcon? icon = null)
            {
                var turns = line.Action is NewProjectScreens.NextPage or NewProjectScreens.MoreAnswers or NewProjectScreens.NextPart;
                var place = line.Choice ? KitPlace.Choice : turns ? KitPlace.Step : KitPlace.Route;
                var control = new KitControl(place, frame, line.Action!, line.Key, words ?? line.Words, icon ?? line.Icon ?? (turns ? GlazeIcon.Next : (GlazeIcon?)null), available: true,
                    reason: null, holds: false, chosen: line.Chosen, mark: line.Choice ? AnswerMark.Single : AnswerMark.None);
                return new RoomAction(control, main: false, mark: control.Mark, chosen: control.Chosen);
            }
            List<RoomAction> Column(bool withClose, params Prompt?[] after)
            {
                var column = new List<RoomAction>();
                if (main is { Available: true }) column.Add(Of(main, KitPlace.Forward, isMain: true));
                if (hold != null) column.Add(Of(hold, KitPlace.Talk));
                foreach (var line in frame.Lines.Where(line => line.Action != null && line.Available)) column.Add(Line(line));
                foreach (var prompt in after)
                    if (prompt is { Available: true } && prompt != main && prompt != hold) column.Add(Of(prompt, KitPlace.Route));
                if (withClose && close is { Available: true }) column.Add(Of(close, KitPlace.Close));
                return column;
            }
            RoomBar Rows(RoomBarState state, IReadOnlyList<RoomRow> rows, string? sentence, List<RoomAction> column, bool waits = false) =>
                new RoomBar(state, sentence: sentence, column: column, waits: waits, lines: rows.Select(row => row.Words).ToList()).WithFacts(rows);
            List<RoomAction> Confirming()
            {
                var answers = new List<RoomAction>();
                if (yes is { Available: true }) answers.Add(Of(yes, KitPlace.Yes, isMain: true, locks: true));
                if (cancel != null) answers.Add(Of(cancel, KitPlace.Cancel));
                return answers;
            }

            if (chosen == Key(NewProjectStep.Build))
            {
                var first = frame.Lines.FirstOrDefault()?.Words;
                // The review shows what will be sent and nothing else: its page ends in Cancel, with Confirm where it may be taken.
                var reviewing = first == EntryText.ReviewLine
                    || first != EntryText.SendingLine && first != EntryText.PreviousRequestTitle && cancel != null && prompts.All(prompt => prompt.Kind != PromptKind.Action)
                        && frame.Lines.Any(line => line.FromRow != null);
                if (reviewing)
                {
                    // Each of the part's items a row. Where the flow cut the parts by the rows' own lines (the page then does
                    // not start with the older line), a row shows exactly its part of an item, so nothing is read twice;
                    // where it cut them for the older page, an item stands whole in every part it is in, never less than
                    // its part. Until the last part is read the one action on is the flow's own Next Part, and Confirm,
                    // behind its lock, stands only where the flow offers it.
                    var cut = first != EntryText.ReviewLine;
                    var items = plain.Select(line => cut && line.FromRow is int from ? new RoomRow(line.Runs, fromLine: from, lines: line.Rows) : new RoomRow(line.Runs)).ToList();
                    if (frame.Lines.FirstOrDefault(line => line.Action == NewProjectScreens.NextPart && line.Available) is PageLine next)
                    {
                        var reading = new List<RoomAction> { new RoomAction(Line(next).Control, main: true) };
                        if (cancel != null) reading.Add(Of(cancel, KitPlace.Cancel));
                        return Rows(RoomBarState.Confirm, items, null, reading);
                    }
                    return Rows(RoomBarState.Confirm, items, yes == null ? null : yes.Available ? StartTaskAsks : yes.Reason ?? why, Confirming());
                }
                if (first == EntryText.SendingLine)
                {
                    // A start's steps: each a row of its name and how it went, a check where it is confirmed and a cross where
                    // it stopped; the one under way is the sentence, sweeping.
                    var rows = new List<RoomRow>();
                    string? under = null;
                    for (var index = 0; index < plain.Count; index++)
                    {
                        var line = plain[index];
                        var status = index + 1 < plain.Count && plain[index].Tone == LineTone.Primary && plain[index].Rows == 1 && plain[index + 1].Rows == 3 ? plain[index + 1] : null;
                        if (status == null)
                        {
                            rows.Add(new RoomRow(line.Runs, line.Tone == LineTone.Good ? RoomRowMark.Check : line.Tone == LineTone.Problem ? RoomRowMark.Cross : RoomRowMark.None));
                            continue;
                        }
                        index++;
                        var words = line.Runs + LineWords.Ours(KitMomentWords.Dot) + status.Runs;
                        if (status.Waits && under == null) under = words.Text;
                        else rows.Add(new RoomRow(words, status.Tone == LineTone.Good ? RoomRowMark.Check : status.Tone == LineTone.Problem ? RoomRowMark.Cross : RoomRowMark.None));
                    }
                    var acts = main != null || prompts.Any(prompt => prompt.Kind == PromptKind.Action);
                    return Rows(RoomBarState.Choose, rows, under, Column(withClose: acts || under == null, frame.Footer[PromptSlot.Secondary]), waits: under != null);
                }
                // A start whose outcome is unknown: its lines as rows under what it is, Clear and its Confirm.
                var title = plain.FirstOrDefault()?.Words;
                var facts = plain.Skip(1).Select(line => new RoomRow(line.Runs)).ToList();
                if (yes != null) return Rows(RoomBarState.Confirm, facts, title, Confirming());
                return Rows(RoomBarState.Choose, facts, why ?? title, Column(withClose: true, Offered(NewProjectScreens.Clear)));
            }

            if (chosen == Key(NewProjectStep.Recap))
            {
                if (prompts.Any(prompt => prompt.Id == NewProjectScreens.ChangeRecap))
                {
                    // The facts: the recap's card where the page gives one (A17), else rows that say what they are; Start Task
                    // first, then Change.
                    var facts = plain.Select(line => new RoomRow(line.Runs)).ToList();
                    var recap = Rows(RoomBarState.Choose, facts, main is { Available: false } ? why : null, Column(withClose: true, Offered(NewProjectScreens.ChangeRecap)));
                    if (frame.Card != null) recap.Card = frame.Card;
                    return recap;
                }
                if (yes?.Id == NewProjectScreens.ConfirmStartOver)
                    return new RoomBar(RoomBarState.Confirm, body: plain.LastOrDefault()?.Runs, sentence: StartOverAsks, column: Confirming());
                if (prompts.Any(prompt => prompt.Id == NewProjectScreens.BackToRecap))
                {
                    // What can be changed as a list picked from (A17, FIELD's call of 2026-11-11): each thing a row, its kind's
                    // mark, its words and a chevron, five a page (NewProjectFlow.ChangesPage pages the rest), so the page never
                    // runs past a column's six. The levels are answers, so they stand in the column, marked, as A1's moment has
                    // them; then Next Page where the things run past a page, Start Over and Back.
                    var changes = frame.Lines.Where(line => line.Action != null && line.Available && !line.Choice && line.Action != NewProjectScreens.NextPage).ToList();
                    var rows = changes.Take(RoomCard.ListAPage).Select(line => new RoomListRow(line.Kind, line.Runs, null, press: Line(line).Control)).ToList();
                    var column = frame.Lines.Where(line => line.Action != null && line.Available && line.Choice).Select(line => Line(line)).ToList();
                    if (frame.Lines.FirstOrDefault(line => line.Action == NewProjectScreens.NextPage) is PageLine next) column.Add(Line(next));
                    if (Offered(NewProjectScreens.StartOver) is Prompt over) column.Add(Of(over, KitPlace.Route));
                    column.Add(Of(prompts.First(prompt => prompt.Id == NewProjectScreens.BackToRecap), KitPlace.Back));
                    var page = new RoomBar(RoomBarState.Choose, column: column, lines: changes.Select(line => line.Runs).ToList());
                    page.Card = new RoomCard(frame.Card?.Title ?? LineWords.Ours(ChangeTitle), list: rows);
                    return page;
                }
                // A page that changes one fact: what it is in the panel, its choices and actions in the column, Done last.
                var done = Offered(NewProjectScreens.Done);
                var actions = Column(withClose: done == null);
                if (done != null && done != main) actions.Add(Of(done, KitPlace.Back));
                return new RoomBar(RoomBarState.Choose, body: plain.FirstOrDefault()?.Runs, sentence: main is { Available: false } ? why : null, column: actions,
                    lines: plain.Count > 1 ? plain.Select(line => line.Runs).ToList() : null);
            }

            if (chosen == Key(NewProjectStep.Questions))
            {
                // A fixed question is Halcyonic's own and stands in the panel, which of them it is under the hairline; the local
                // AI's question is its own words and stands on the band, the panel saying whose words those are.
                var asked = plain.FirstOrDefault();
                var theirs = frame.Source != null && frame.Source != EntryText.GuideNote;
                var waiting = prompts.Any(prompt => prompt.Waits);
                var column = Column(withClose: true);
                if (theirs)
                    return new RoomBar(RoomBarState.Choose, body: LineWords.Ours(waiting ? why ?? frame.Source! : frame.Source!), column: column, waits: waiting,
                        band: waiting ? null : asked?.Words);
                return new RoomBar(RoomBarState.Choose, body: asked?.Runs, sentence: asked?.Fact, column: column);
            }

            if (chosen == Key(NewProjectStep.YourIdea))
            {
                // Your Idea: the field, its caret after the words, what it asks for under the hairline; Type opens the keyboard.
                var typing = frame.Lines.FirstOrDefault(line => line.Action == NewProjectScreens.TypeIdea);
                var worded = typing != null && typing.Rows > 1;
                var column = new List<RoomAction>();
                if (main is { Available: true }) column.Add(Of(main, KitPlace.Forward, isMain: true));
                KitControl? field = null;
                if (typing != null)
                {
                    var type = Line(typing, Type, GlazeIcon.Type);
                    field = type.Control;
                    column.Add(new RoomAction(type.Control, main: column.Count == 0));
                }
                if (hold != null) column.Add(Of(hold, KitPlace.Talk));
                foreach (var line in frame.Lines.Where(line => line.Action != null && line.Available && line != typing)) column.Add(Line(line));
                if (close is { Available: true }) column.Add(Of(close, KitPlace.Close));
                var said = plain.Skip(1).FirstOrDefault()?.Words;
                return new RoomBar(RoomBarState.Draft, body: worded ? typing!.Runs : LineWords.None, caret: typing != null, sentence: said ?? plain.FirstOrDefault()?.Words,
                    column: column, field: field);
            }
            return null;
        }
    }
}
