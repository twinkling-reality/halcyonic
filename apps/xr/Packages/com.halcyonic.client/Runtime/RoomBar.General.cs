#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// Whether the bar lays everything its reader was given, uncut, with every page of its own shown: the plane reports the
        /// frame drawn, and so read, only while this holds (AUDIT's condition 3, 2026-11-13). A specific reader that shows its
        /// frame whole leaves it true; the general reader sets it false where a part could not be laid whole (more rows than a
        /// page, more actions than the column).
        /// </summary>
        public bool Whole { get; internal set; } = true;

        /// <summary>
        /// The general reader (the lead, 2026-11-13T16:40Z: no whole screen in the older view, by one mechanism): any frame no
        /// specific reader takes is drawn here in the kit's own parts, at Standard and Larger, and it never declines a frame
        /// with anything in it. It takes every part as the frame hands it apart and never splits words on any mark:
        /// <list type="bullet">
        /// <item>A side panel stands in the panel's place: its subject the bar's words, each fact a row (its name the label
        /// above, its value the words), each line a row, its source the last row; the frame's prompts stay in the column with
        /// the side panel's own Close.</item>
        /// <item>A line that states is a row of its own: its words, its label where it has one, and a fact beside it as the
        /// row's words under the line's own as the label (a value is never a note, so it wraps whole). A line cut in parts is
        /// the row of its part. A line of outside words or a claim stands on the band; a second one is a row, theirs.</item>
        /// <item>An input line is the kit's field, its words and the caret.</item>
        /// <item>A line that acts is an action of the column, an answer with its mark, its fact or its quieter line as the
        /// button's detail. One that cannot act now is a row of its own, never dropped, and the frame's reason says why.</item>
        /// <item>The column: the main prompt first, then the lines that act, the other prompts (a held one keeps its hold),
        /// the sections reached, and the ways out last. The frame's reason is the sentence.</item>
        /// </list>
        /// Where the rows pass a page or the actions pass the column, it pages them itself (<see cref="Turn"/>), the main action
        /// and the ways out on every page, and holds <see cref="Whole"/> only once every page was shown. While a side panel stands,
        /// the column holds only what acts on it: a prompt safe in its place, its pager and its own Close.
        /// </summary>
        private static RoomBar? General(MenuFrame frame, VoiceStage voice, RoomForm form)
        {
            var rows = new List<RoomRow>();
            string? band = null;
            string? sentence = null;
            KitControl? field = null;
            var body = LineWords.None;
            var acting = new List<RoomAction>();
            LineWords? words = null;

            if (frame.Side is SidePanel side)
            {
                // The side panel, which the person opened, stands in the page's place; the page is one Close away.
                words = side.SubjectRuns;
                foreach (var fact in side.Facts) rows.Add(new RoomRow(fact.ValueRuns, label: fact.Name));
                foreach (var line in side.Lines) rows.Add(Stating(line));
                if (side.SourceRuns is LineWords source) rows.Add(new RoomRow(source));
            }
            else
            {
                foreach (var line in frame.Lines)
                {
                    if (line.Control == LineControl.Input)
                    {
                        // The field: its words and the caret; it opens the keyboard by the line's own action.
                        body = line.Runs;
                        if (line.Action != null)
                            field = new KitControl(KitPlace.Field, frame, line.Action, line.Key, line.Words, line.Icon, line.Available, null, false);
                        continue;
                    }
                    if (line.Action != null)
                    {
                        if (!line.Available)
                        {
                            // An action that cannot act now stands as a row, never dropped; why it waits is the frame's reason, the sentence.
                            rows.Add(new RoomRow(line.Runs, label: line.Under?.Text));
                            continue;
                        }
                        var place = line.Choice ? KitPlace.Choice : KitPlace.Step;
                        var control = new KitControl(place, frame, line.Action, line.Key, line.Words, line.Icon, true, null, false, line.Chosen, line.AnswerMark);
                        acting.Add(new RoomAction(control, main: false, icon: line.Choice ? (GlazeIcon?)null : line.Icon ?? GlazeIcon.Next, mark: line.AnswerMark,
                            chosen: line.Chosen, detail: line.FactRuns?.Text ?? line.Fact ?? line.Under?.Text));
                        continue;
                    }
                    // Outside words, however the frame marks them: one on the band, any more as rows of their own.
                    if ((line.Claim || line.Runs.AllTheirs || (line.WordsAreData && !line.Runs.AnyTheirs)) && line.FromRow == null && line.Fact == null && band == null)
                    {
                        band = line.Words;
                        continue;
                    }
                    if (!line.Runs.IsEmpty) rows.Add(Stating(line));
                }
            }

            RoomAction? main = null;
            var others = new List<RoomAction>();
            var leaving = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                // While a side panel stands in the page's place, only what acts on what it shows stays (AUDIT's G): a prompt safe in
                // its place, the pager that turns its parts, and the ways out; the page's other actions return with Close.
                if (frame.Side != null && !prompt.SafeInPlace && prompt.Kind is not (PromptKind.NextPage or PromptKind.Close or PromptKind.Cancel)) continue;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var place = prompt.Holds ? KitPlace.Talk
                    : prompt.Kind switch { PromptKind.Close => KitPlace.Close, PromptKind.Cancel => KitPlace.Cancel, PromptKind.Yes => KitPlace.Yes, _ => KitPlace.Step };
                var leads = main == null && !leaves && !prompt.Holds && (prompt.Main || prompt.Kind == PromptKind.Yes);
                // A confirmation's Yes stands behind its lock, as every Confirm of the redesign does.
                var action = new RoomAction(new KitControl(place, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, prompt.Holds), main: leads,
                    locks: prompt.Kind == PromptKind.Yes, icon: prompt.Icon);
                if (leads) main = action;
                else if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            if (frame.Side != null)
            {
                // The side panel's own Close is its one way out; the page's Close comes back with the page.
                leaving.Clear();
                leaving.Add(new RoomAction(new KitControl(KitPlace.Close, frame, SidePanel.Close, null, SidePanel.Footer.All.First().Prompt.Words, GlazeIcon.Close, true, null, false),
                    main: false, icon: GlazeIcon.Close));
            }
            var sections = frame.Side != null ? new List<RoomAction>() : frame.Sections.Where(section => !section.Chosen && section.Reached)
                .Select(section => new RoomAction(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false),
                    main: false, icon: GlazeIcon.Next)).ToList();
            var turning = new List<RoomAction>();
            turning.AddRange(acting);
            turning.AddRange(others);
            turning.AddRange(sections);

            // The frame's reason is the sentence. A confirmation (a setting's, or one no specific reader took) is drawn as the frame gives
            // it, every row whole and its Yes behind the lock (AUDIT's 16:45Z (ii)).
            sentence = frame.Reason;
            if (rows.Count == 0 && band == null && turning.Count == 0 && main == null && leaving.Count == 0 && field == null)
            {
                // Nothing in it: it says what it is, never a blank bar.
                rows.Add(new RoomRow(frame.SubjectRuns));
            }

            // Its own pages (AUDIT's MUST I): rows a page at a time, and the actions that turn a column's room at a time, the main action
            // first and the ways out last on every page, with the pager between; nothing is dropped. A frame with its own parts never
            // reaches here with more than a page, since its parts page it.
            var fixedCount = (main != null ? 1 : 0) + leaving.Count;
            // At least one turning action and the pager a page, however many ways out a frame gives (AUDIT's SHOULD, 16:56Z): such a
            // frame's column may then pass six, which the kit lays rather than lose an action.
            var room = System.Math.Max(2, ColumnMost - fixedCount);
            var actionPages = turning.Count <= room ? 1 : (turning.Count + room - 2) / (room - 1);
            // Rows a page by the form it will be laid in (the plane measures it and asks again with fewer where a page passes the field).
            var rowsAPage = form.RowsAPage;
            var rowPages = rows.Count <= 0 ? 1 : (rows.Count + rowsAPage - 1) / rowsAPage;
            var pages = System.Math.Max(rowPages, actionPages);
            var each = pages > 1 ? room - 1 : room;
            var parts = new List<GeneralPage>();
            for (var page = 0; page < pages; page++)
            {
                var column = new List<RoomAction>();
                if (main != null) column.Add(main);
                column.AddRange(turning.Skip(page * each).Take(each));
                if (pages > 1)
                {
                    var last = page == pages - 1;
                    column.Add(new RoomAction(new KitControl(KitPlace.Step, frame, TurnPage, null, Footer.NextPageWords(page, pages), GlazeIcon.Next, true, null, false),
                        main: false, icon: GlazeIcon.Next, detail: last ? null : (page + 2) + " of " + pages));
                }
                column.AddRange(leaving);
                parts.Add(new GeneralPage(page < rowPages ? rows.Skip(page * rowsAPage).Take(rowsAPage).ToList() : new List<RoomRow>(), column));
            }

            var first = parts[0];
            var shown = first.Rows.ToList();
            // One plain line is the panel's words, as a page of one line always was; more, or any with a label, are rows.
            if (pages == 1 && field == null && shown.Count == 1 && shown[0].Label == null && shown[0].Lines == null)
            {
                body = shown[0].Words;
                shown.Clear();
            }
            var bar = new RoomBar(field != null ? RoomBarState.Draft : RoomBarState.Page, words: words, body: body, caret: field != null, sentence: sentence,
                column: first.Column, field: field, band: band, wide: shown.Count == 0 && body.IsEmpty && field == null && pages == 1,
                lines: shown.Count > 0 ? shown.Select(row => row.Words).ToList() : null);
            bar.generalPages = pages > 1 ? parts : null;
            bar.Whole = pages == 1;
            return shown.Count > 0 ? bar.WithFacts(shown) : bar;
        }

        /// <summary>The form this bar is read for (<see cref="RoomForm"/>): what the plane will lay it in.</summary>
        public RoomForm Form { get; private set; } = RoomForm.Full;

        /// <summary>The general reader's own pager: what its Next Page raises. The plane turns the bar (<see cref="Turn"/>) and draws it again.</summary>
        public const string TurnPage = "room-general-turn-page";

        /// <summary>The general reader's pages of a frame that passed a page or the column; null where it holds on one.</summary>
        private List<GeneralPage>? generalPages;

        private int generalAt;

        private HashSet<int>? generalSeen;

        /// <summary>The general reader's page showing, from 0, and how many it made (1 where it made none).</summary>
        public (int Page, int Pages) GeneralPart => (generalAt, generalPages?.Count ?? 1);

        /// <summary>
        /// Turns the general reader's pages (its <see cref="TurnPage"/>): the next page, or the first after the last. The bar holds
        /// <see cref="Whole"/> once every page has been shown. False where the bar has no pages of its own.
        /// </summary>
        public bool Turn()
        {
            if (generalPages == null) return false;
            generalSeen ??= new HashSet<int> { 0 };
            generalAt = (generalAt + 1) % generalPages.Count;
            generalSeen.Add(generalAt);
            var page = generalPages[generalAt];
            Column = page.Column;
            Facts = page.Rows.Count > 0 ? page.Rows : null;
            Lines = page.Rows.Count > 0 ? page.Rows.Select(row => row.Words).ToList() : null;
            Whole = generalSeen.Count == generalPages.Count;
            return true;
        }

        private sealed class GeneralPage
        {
            public GeneralPage(IReadOnlyList<RoomRow> rows, IReadOnlyList<RoomAction> column)
            {
                Rows = rows;
                Column = column;
            }

            public IReadOnlyList<RoomRow> Rows { get; }

            public IReadOnlyList<RoomAction> Column { get; }
        }

        /// <summary>A line that states, as a row: its own label where it has one, a fact beside it as the words under the line's own.</summary>
        private static RoomRow Stating(PageLine line)
        {
            if (line.FactRuns is LineWords fact) return new RoomRow(fact, label: line.Words, fromLine: 0);
            if (line.Fact is string said) return new RoomRow(LineWords.Ours(said), label: line.Words);
            if (line.FromRow is int from) return new RoomRow(line.Runs, label: line.RowLabel, fromLine: from, lines: line.Rows);
            return new RoomRow(line.Runs, label: line.RowLabel);
        }
    }

    /// <summary>
    /// The form a bar is laid in, decided by the plane before any reader measures (the lead, 2026-11-13T17:16Z; XR's rule: the
    /// widest form that clears every shelf and character inside the field): narrow or full, and how many of the general reader's
    /// rows stand on a page, as the plane measured them in that form (fewer where a page would pass the field).
    /// </summary>
    public readonly struct RoomForm
    {
        public RoomForm(bool narrow, int rowsAPage)
        {
            Narrow = narrow;
            RowsAPage = rowsAPage < 1 ? 1 : rowsAPage > RoomRows.APage ? RoomRows.APage : rowsAPage;
        }

        /// <summary>The full width, eight rows a page: the form where nothing asks for less.</summary>
        public static RoomForm Full => new RoomForm(false, RoomRows.APage);

        /// <summary>Whether the bar is laid in the kit's narrow form (its panel above its column, a column wide).</summary>
        public bool Narrow { get; }

        /// <summary>The general reader's rows a page, one to eight.</summary>
        public int RowsAPage { get; }

        /// <summary>This form with one row fewer a page, never fewer than one: the plane's next ask where a page passed the field.</summary>
        public RoomForm Fewer() => new RoomForm(Narrow, RowsAPage - 1);
    }
}
