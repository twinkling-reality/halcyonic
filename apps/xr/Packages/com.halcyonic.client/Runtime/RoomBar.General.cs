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
        /// <item>The lines that act stand in the panel as a card's pressed rows (the lead's 2026-11-13T21:11Z): a choice with its
        /// mark and chosen state, a value the note, a quieter line under it, a label above, five a page or fewer where measured; the
        /// card's title is where the person is, the one other stating line its description and that line's quieter line its foot.
        /// A frame that states more keeps its rows, and its lines that act stand in the column, a value or a quieter line the
        /// button's detail. Lines handed in one group (<see cref="PageLine.Group"/>) never stand on two pages, on either path; where
        /// a group cannot stand whole in the column the frame takes the card after all, its stating rows unpressed rows first. One
        /// that cannot act now is a row of its own, never dropped, and the frame's reason says why.</item>
        /// <item>The column: the main prompt first, then the frame's other prompts (a held one keeps its hold), the sections
        /// reached, and the ways out last, four in all a page. The frame's reason is the sentence.</item>
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
            // The lines that act, in the frame's order, each with its own control: the card's pressed rows, or the column's buttons.
            var lineActs = new List<(PageLine Line, KitControl Control)>();
            // A stating row's quieter line, by the row's place in rows: a row of its own under it, or the card's foot under its description.
            var unders = new Dictionary<int, LineWords>();
            // Stating rows a frame marks as headings (PageLine.AsHeading: by kind, never by words): they head the rows under them.
            var headings = new HashSet<int>();
            // The frame's own order of its stating rows and its lines that act, so a card that holds both keeps it (a heading over what it heads).
            var order = new List<(bool Acting, int At)>();
            var lost = false;
            LineWords? words = null;
            // The panel's first row is its title (where am I), kept with the row after it when they page.
            var titled = false;

            if (frame.Side is SidePanel side)
            {
                // The side panel, which the person opened, stands in the page's place; the page is one Close away.
                words = side.SubjectRuns;
                // What the panel is about stands as its first row, so a person sees where they are (the lead, 16:46Z): the bar's
                // own words are not drawn where rows stand (gen-n2, 2026-11-13: "Text Size" and "Tests" unseen).
                rows.Add(new RoomRow(side.SubjectRuns));
                titled = true;
                foreach (var fact in side.Facts) rows.Add(new RoomRow(fact.ValueRuns, label: fact.Name));
                foreach (var line in side.Lines)
                {
                    rows.Add(Stating(line));
                    if (line.Under != null) unders[rows.Count - 1] = line.Under;
                }
                if (side.SourceRuns is LineWords source) rows.Add(new RoomRow(source));
            }
            else
            {
                // Where am I (the lead, 16:46Z; the critic, 17:49Z: Settings and paged lists had no title): the frame's subject stands
                // as the panel's first row, unless its first line already says it or the frame is a task's or a project's, whose
                // name the room already stands on its plate.
                // Nor where a line, or a line's value, already says it (the recap's reading page: the person's long words never twice).
                if (frame.IdentityKind == null && !frame.Lines.Any(line => line.Words == frame.Subject || (line.FactRuns?.Text ?? line.Fact) == frame.Subject))
                {
                    rows.Add(new RoomRow(frame.SubjectRuns));
                    titled = true;
                }
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
                            order.Add((false, rows.Count - 1));
                            continue;
                        }
                        var place = line.Choice ? KitPlace.Choice : KitPlace.Step;
                        lineActs.Add((line, new KitControl(place, frame, line.Action, line.Key, line.Words, line.Icon, true, null, false, line.Chosen, line.AnswerMark)));
                        order.Add((true, lineActs.Count - 1));
                        continue;
                    }
                    // Outside words, however the frame marks them: one on the band, any more as rows of their own.
                    if ((line.Claim || line.Runs.AllTheirs || (line.WordsAreData && !line.Runs.AnyTheirs)) && line.FromRow == null && line.Fact == null && band == null)
                    {
                        band = line.Words;
                        continue;
                    }
                    if (!line.Runs.IsEmpty)
                    {
                        rows.Add(Stating(line));
                        order.Add((false, rows.Count - 1));
                        if (line.Under != null) unders[rows.Count - 1] = line.Under;
                        if (line.Heading) headings.Add(rows.Count - 1);
                    }
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
                var action = new RoomAction(new KitControl(place, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, prompt.Holds, destructive: prompt.Destructive), main: leads,
                    locks: prompt.Kind == PromptKind.Yes, icon: prompt.Icon);
                if (leads) main = action;
                else if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            // A confirmation's one way out is its Cancel, last (the critic, 17:49Z: Close and Cancel side by side made a person wonder
            // whether they differ at the moment they must not guess).
            if (frame.Footer.All.Any(each => each.Prompt.Kind == PromptKind.Yes) && leaving.Any(action => action.Control.Place == KitPlace.Cancel))
                leaving.RemoveAll(action => action.Control.Place == KitPlace.Close);
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
            // A frame that pages itself (its own Next Page) keeps one pager: where the general reader pages it too, the frame's own
            // stands on the reader's last page in the place of First Page, so a person meets one Next Page throughout (gen-n2:
            // Usage at Larger drew two).
            var ownPager = others.FirstOrDefault(action => frame.Footer.All.Any(each => each.Prompt.Kind == PromptKind.NextPage && each.Prompt.Id == action.Control.Action));
            // A frame that carries its own card (a task's Checks with an answer, a specific reader's card it declined): the card stands in
            // the panel in place of the page's lines, which say the same for the older view, and the line that opens the full answer
            // goes with them, since the card shows the summary whole; the frame's own Next Page pages those lines, so it goes too. The
            // column pages as every column does (the lead's 19:47Z: Checks with an answer had seven actions and fell to the older view).
            // A line carries words to read on a button (a value, a quieter line, a label): only a card's row holds them all.
            bool Valued(PageLine line) => !line.Choice && ((line.FactRuns?.Text ?? line.Fact) != null || line.Under != null || line.RowLabel != null);
            var ownCard = frame.Side == null && !lineActs.Any(each => Valued(each.Line)) ? frame.Card : null;
            if (ownCard != null)
            {
                lineActs.Clear();
                rows.Clear();
                unders.Clear();
                others.RemoveAll(action => frame.Footer.All.Any(each => each.Prompt.Kind == PromptKind.NextPage && each.Prompt.Id == action.Control.Action));
                ownPager = null;
            }
            // The frame's own pager holds the pager's place on every page (the reader's Next Page before the last, the frame's own on it).
            if (ownPager != null) others.Remove(ownPager);

            // Its own pages (AUDIT's MUST I): rows a page at a time, and the actions that turn a column's room at a time, the main action
            // first and the ways out last on every page, with the pager between; nothing is dropped. A frame with its own parts never
            // reaches here with more than a page, since its parts page it.
            var fixedCount = (main != null ? 1 : 0) + leaving.Count;
            // At least one turning action and the pager a page, however many ways out a frame gives (AUDIT's SHOULD, 16:56Z).
            // At most the form's actions on a page IN ALL (the spec's four, BUILD-SPEC-v4 1.1: "Choices column ... at most 4"; the lead's
            // 19:41Z; AUDIT's 19:43Z): the main action, the ways out and the pager count among them, and at least one other action
            // stands on each page so none is out of reach.
            var room = System.Math.Max(1, System.Math.Min(form.ActionsAPage, ColumnMost) - fixedCount);
            var roomPaged = System.Math.Max(1, System.Math.Min(form.ActionsAPage, ColumnMost) - fixedCount - 1);

            // The lines that act stand in the PANEL as the card's pressed rows (the lead's 2026-11-13T21:11Z: a picker of three colours
            // paged one a page, a shelf's nine moves one a page): a choice with its mark and its chosen state, a value the note, a
            // quieter line under it, a label above; the column keeps the frame's own prompts under the four in all. Lines handed in one
            // group (PageLine.Group) stand on one page together, Left beside Right, on every path.
            var groups = new List<List<int>>();
            var groupAt = new Dictionary<int, int>();
            for (var at = 0; at < lineActs.Count; at++)
            {
                var group = lineActs[at].Line.Group;
                if (at > 0 && group != null && group == lineActs[at - 1].Line.Group) groups[groups.Count - 1].Add(at);
                else groups.Add(new List<int> { at });
                groupAt[at] = groups.Count - 1;
            }
            // The card holds a title, one description and a foot: the description's quieter line, or a second line that states. A frame
            // that marks a heading keeps its rows (a heading stands over the lines it heads: never a card's description over other
            // lines, nor its foot under them; Projects' Folders over its folders); the shelf's two
            // instructions (XR's a46dc4651, each a line of its own) stand on every page of its moves. A frame that states more keeps its rows,
            // and its lines that act stand in the column, its pages breaking only between groups; where a group cannot stand whole
            // beside the main action, the ways out and the pager, the frame takes the card after all, its stating rows the card's
            // unpressed rows before the pressed ones (AUDIT's 21:14Z: no group split on any path).
            var cardable = rows.All(row => row.Label == null && row.Lines == null) && unders.Keys.All(at => at == 1)
                && headings.Count == 0 && (rows.Count <= 2 || rows.Count == 3 && unders.Count == 0);
            LineWords? description = cardable && rows.Count >= 2 ? rows[1].Words : null;
            LineWords? foot = !cardable ? null : unders.TryGetValue(1, out var under1) ? under1 : rows.Count == 3 ? rows[2].Words : null;
            // A line that carries words to read (an answer's description, a value, a label) is a choice the person reads before pressing:
            // it stands in the panel at the panel's width, never on a button in the column, whose width never changes with its words (the
            // lead's 2026-11-13T22:04Z: a described answer made the column as wide as the panel).
            var carriesWords = lineActs.Any(each => Valued(each.Line) || each.Line.Under != null);
            var forced = !cardable && lineActs.Count > 0
                && (carriesWords || lineActs.Count + others.Count + sections.Count > room && groups.Any(group => group.Count > roomPaged));
            var carded = lineActs.Count > 0 && (cardable || forced);
            var cardUnits = new List<List<RoomListRow>>();
            var groupOf = new Dictionary<RoomAction, int>();
            if (carded)
            {
                List<RoomListRow> Pressed(List<int> group) => group.Select(at => lineActs[at]).Select(each => AnswerRow(each.Line.Runs, each.Line.Under,
                    each.Line.Choice ? (each.Line.AnswerMark == AnswerMark.None ? AnswerMark.Single : each.Line.AnswerMark) : AnswerMark.None, each.Line.Chosen,
                    each.Control, each.Line.FactRuns?.Text ?? each.Line.Fact, each.Line.RowLabel, each.Line.Kind, each.Line.Swatch)).ToList();
                if (forced)
                {
                    // In the frame's own order: each stating row unpressed, each group of lines that act together.
                    var laid = new HashSet<int>();
                    foreach (var (actsHere, at) in order)
                    {
                        if (!actsHere) cardUnits.Add(new List<RoomListRow> { new RoomListRow(RoomKind.None, rows[at].Words, label: rows[at].Label, under: unders.TryGetValue(at, out var under) ? under : null) });
                        else if (laid.Add(groupAt[at])) cardUnits.Add(Pressed(groups[groupAt[at]]));
                    }
                }
                else foreach (var group in groups) cardUnits.Add(Pressed(group));
            }
            else
                for (var index = 0; index < groups.Count; index++)
                    foreach (var at in groups[index])
                    {
                        var (line, control) = lineActs[at];
                        var value = line.FactRuns?.Text ?? line.Fact;
                        // On a button: an answer's description, or a value (its quieter line too where it has no value), its detail;
                        // where it carries two of them, or a label, a part is lost and the bar is not whole.
                        var action = Valued(line)
                            ? new RoomAction(control, main: false, icon: GlazeIcon.Next, chosen: line.Chosen, detail: value ?? line.Under?.Text)
                            : new RoomAction(control, main: false, icon: line.Choice ? (GlazeIcon?)null : line.Icon ?? GlazeIcon.Next, mark: line.AnswerMark,
                                chosen: line.Chosen, detail: line.Under?.Text);
                        if (Valued(line) && (value != null && line.Under != null || line.RowLabel != null)) lost = true;
                        acting.Add(action);
                        groupOf[action] = index;
                    }
            RoomCard CardOf(IReadOnlyList<RoomListRow> list) => new RoomCard(rows.Count > 0 ? rows[0].Words : frame.SubjectRuns,
                description, list: list, more: foot);
            var turning = new List<RoomAction>();
            turning.AddRange(acting);
            turning.AddRange(others);
            turning.AddRange(sections);

            // The frame's reason is the sentence. A confirmation (a setting's, or one no specific reader took) is drawn as the frame gives
            // it, every row whole and its Yes behind the lock (AUDIT's 16:45Z (ii)).
            sentence = frame.Reason;
            if (rows.Count == 0 && band == null && turning.Count == 0 && main == null && leaving.Count == 0 && field == null && !carded)
            {
                // Nothing in it: it says what it is, never a blank bar.
                rows.Add(new RoomRow(frame.SubjectRuns));
            }

            // The column's turning actions in units: a group of the frame's lines together, every other action alone.
            var turnUnits = new List<List<RoomAction>>();
            foreach (var action in turning)
            {
                if (turnUnits.Count > 0 && groupOf.TryGetValue(action, out var group) && groupOf.TryGetValue(turnUnits[^1][0], out var before) && before == group)
                    turnUnits[^1].Add(action);
                else turnUnits.Add(new List<RoomAction> { action });
            }
            // A card's rows page by the form it will be laid in, by count and by the plane's line measure (PackCard; FIELD's F100: Usage's
            // card narrow passed its footprint at one row a page; the lead's 22:04Z: under the eight lines a page).
            var cardPages = carded ? PackCard(cardUnits, form, rows.Count > 0 ? rows[0].Words : frame.SubjectRuns, description, foot)
                : new List<List<RoomListRow>> { new List<RoomListRow>() };
            var pressPages = cardPages.Count;
            // A stating row's quieter line stands as a row of its own under it, never dropped (AUDIT's 21:14Z: the plain rows lost it).
            var laidRows = new List<RoomRow>();
            for (var at = 0; at < rows.Count; at++)
            {
                laidRows.Add(rows[at]);
                if (unders.TryGetValue(at, out var under)) laidRows.Add(new RoomRow(under));
            }
            var rowPagesOf = carded ? new List<IReadOnlyList<RoomRow>> { new List<RoomRow>() } : PageRows(laidRows, form, titled);
            var rowPages = rowPagesOf.Count;
            var contentPages = System.Math.Max(rowPages, pressPages);
            var turnPages = ownPager == null && contentPages == 1 && turning.Count <= room ? new List<List<RoomAction>> { turning } : Pack(turnUnits, roomPaged);
            var pages = System.Math.Max(contentPages, turnPages.Count);
            // Rows that fit one page stand on every page while the actions turn; one plain line is the panel's words on all of them.
            // A confirmation's Yes stands only once every line of what it does was shown (gen-n8, 2026-11-13: narrow at the larger
            // size drew Forget Computer beside the title alone, what forgetting does on the next page); Cancel stands on every page.
            var confirming = main?.Control.Place == KitPlace.Yes;
            var contentLast = carded ? pressPages - 1 : ownCard != null ? 0 : rowPages - 1;
            var oneLine = !carded && rowPages == 1 && field == null && rowPagesOf[0].Count == 1 && rowPagesOf[0][0].Label == null && rowPagesOf[0][0].Lines == null;
            var parts = new List<GeneralPage>();
            for (var page = 0; page < pages; page++)
            {
                var column = new List<RoomAction>();
                if (main != null && (!confirming || page >= contentLast)) column.Add(main);
                if (page < turnPages.Count) column.AddRange(turnPages[page]);
                if (pages > 1 || ownPager != null)
                {
                    var last = page == pages - 1;
                    if (last && ownPager != null) column.Add(ownPager);
                    // The pager says its action alone, never its count under it (AUDIT2's M2; AUDIT's 14:59Z on Settings' column).
                    else column.Add(new RoomAction(new KitControl(KitPlace.Step, frame, TurnPage, null, Footer.NextPageWords(page, pages), GlazeIcon.Next, true, null, false),
                        main: false, icon: GlazeIcon.Next));
                }
                column.AddRange(leaving);
                parts.Add(ownCard != null ? new GeneralPage(new List<RoomRow>(), column, ownCard)
                    : carded
                    // A card that fits one page stands on every page while the actions turn, as rows that fit one page do (gen-n11).
                    ? new GeneralPage(new List<RoomRow>(), column, CardOf(cardPages.Count == 1 ? cardPages[0] : page < cardPages.Count ? cardPages[page] : new List<RoomListRow>()))
                    : new GeneralPage(rowPages == 1 ? (oneLine ? new List<RoomRow>() : rowPagesOf[0]) : page < rowPages ? rowPagesOf[page] : new List<RoomRow>(), column));
            }

            var first = parts[0];
            var shown = first.Rows.ToList();
            // One plain line is the panel's words, as a page of one line always was; more, or any with a label, are rows.
            if (oneLine) body = rowPagesOf[0][0].Words;
            var bar = new RoomBar(field != null ? RoomBarState.Draft : RoomBarState.Page, words: words, body: body, caret: field != null, sentence: sentence,
                column: first.Column, field: field, band: band, wide: rows.Count == 0 && body.IsEmpty && field == null && ownCard == null && !carded,
                lines: shown.Count > 0 ? shown.Select(row => row.Words).ToList() : null);
            bar.generalPages = pages > 1 ? parts : null;
            // A frame counts as read once every page that holds something of it to read was shown. Pages that only turn bare actions
            // hide nothing (the rows stand on every page), so they never hold a frame back (JUDGE's 20:08Z shelf walk: the shelf's
            // Move buttons paged one a page and its Apply waited for a draw that never came).
            // An action that carries words to read counts as content too (AUDIT's 20:10Z): a button with a detail (an answer's
            // description, a value) on a page makes that page one to be shown; a bare action (a move, a section, Next) does not.
            var toRead = new HashSet<int> { 0 };
            for (var page = 0; page < parts.Count; page++)
                // A card's page holds words to read where a row states (no press) or carries more than its name (a note, a quieter line,
                // a label); a page of bare moves or choices is turned, never read, so it holds nothing back (JUDGE's 20:08Z shelf walk).
                if ((ownCard == null && carded && page < cardPages.Count && cardPages[page].Any(row => row.Press == null || row.Note != null || row.Under != null || row.Label != null))
                    || (!carded && ownCard == null && rowPages > 1 && page < rowPages)
                    || parts[page].Column.Any(action => action.Detail != null && action.Control.Action != TurnPage))
                    toRead.Add(page);
            bar.readPages = toRead;
            bar.Whole = toRead.Count == 1 && !lost;
            bar.lostPart = lost;
            if (first.Card != null) bar.Card = first.Card;
            return shown.Count > 0 ? bar.WithFacts(shown) : bar;
        }

        /// <summary>
        /// The panel's rows in pages (the lead's 2026-11-13T20:15Z: eight lines means eight lines). With the plane's line measure
        /// (<see cref="RoomForm.LinesOf"/>), a page holds rows to the form's lines (a row's label counted with its words), at most
        /// its rows a page; a row longer than a page stands in its own parts, each on its own page with the row's label kept,
        /// nothing cut and nothing taller. Without a measure, the form's rows a page.
        /// </summary>
        private static List<IReadOnlyList<RoomRow>> PageRows(IReadOnlyList<RoomRow> rows, RoomForm form, bool titled = false)
        {
            var pages = new List<IReadOnlyList<RoomRow>>();
            if (form.LinesOf is not System.Func<RoomRow, bool, int> measure)
            {
                for (var at = 0; at < rows.Count; at += form.RowsAPage) pages.Add(rows.Skip(at).Take(form.RowsAPage).ToList());
                if (pages.Count == 0) pages.Add(new List<RoomRow>());
                return pages;
            }
            var budget = form.LinesAPage;
            // A page indents every row's words where any of its rows carries a mark (RoomRows.Marked): measured at the indented width
            // wherever the frame holds a marked row, so a row is never counted at a width wider than it is laid (AUDIT's 20:20Z MUST).
            var indented = RoomRows.Marked(rows);
            var page = new List<RoomRow>();
            var used = 0;
            void Close()
            {
                if (page.Count == 0) return;
                pages.Add(page);
                page = new List<RoomRow>();
                used = 0;
            }
            foreach (var row in rows)
            {
                // The row as it is laid (its label, words and note, at the page's width: XR's and FIELD's RowLines); its words' share
                // is the row measured without its label, so a part counts only its words beside the label it keeps.
                var whole = System.Math.Max(1, measure(row, indented));
                var lines = row.Lines ?? (row.Label == null ? whole : System.Math.Max(1, measure(new RoomRow(row.Words, row.Mark, note: row.Note), indented)));
                var label = row.Label == null ? 0 : System.Math.Max(0, whole - lines);
                if (row.Lines != null && row.Label != null) label = System.Math.Max(1, measure(new RoomRow(LineWords.Ours(row.Label)), indented));
                // A row that must be split anyway (longer than any page), or the row after the title (gen-n8), starts on the page with room
                // for some of it: its first lines fill that page under its label, the rest follow in parts, the last part opening the next
                // page so the rows after it may share it (a fresh reader, 2026-11-14T02:00Z: the recap's page held the name alone).
                var keepsWithTitle = titled && pages.Count == 0 && page.Count == 1;
                if (row.Lines == null && page.Count > 0 && page.Count < form.RowsAPage && used + label + lines > budget && budget - used - label >= 1
                    && (keepsWithTitle || label + lines > budget))
                {
                    var beside = budget - used - label;
                    page.Add(new RoomRow(row.Words, row.Mark, fromLine: 0, lines: beside, label: row.Label, note: row.Note));
                    Close();
                    var rest = System.Math.Max(1, budget - label);
                    for (var from = beside; from < lines; from += rest)
                    {
                        var part = new RoomRow(row.Words, row.Mark, fromLine: from, lines: System.Math.Min(rest, lines - from), label: row.Label);
                        if (from + rest >= lines)
                        {
                            page.Add(part);
                            used = label + part.Lines!.Value;
                        }
                        else pages.Add(new List<RoomRow> { part });
                    }
                    continue;
                }
                if (row.Lines == null && label + lines > budget)
                {
                    // Longer than a page: its own parts, each on a page of its own, the label kept on every part.
                    Close();
                    var each = System.Math.Max(1, budget - label);
                    for (var from = 0; from < lines; from += each)
                        pages.Add(new List<RoomRow> { new RoomRow(row.Words, row.Mark, fromLine: from, lines: System.Math.Min(each, lines - from), label: row.Label,
                            note: from == 0 ? row.Note : null) });
                    continue;
                }
                if (page.Count > 0 && (used + label + lines > budget || page.Count >= form.RowsAPage)) Close();
                page.Add(row);
                used += label + lines;
            }
            Close();
            if (pages.Count == 0) pages.Add(new List<RoomRow>());
            return pages;
        }

        /// <summary>The form this bar is read for (<see cref="RoomForm"/>): what the plane will lay it in.</summary>
        public RoomForm Form { get; private set; } = RoomForm.Full;

        /// <summary>The general reader's own pager: what its Next Page raises. The plane turns the bar (<see cref="Turn"/>) and draws it again.</summary>
        public const string TurnPage = "room-general-turn-page";

        /// <summary>The general reader's pages of a frame that passed a page or the column; null where it holds on one.</summary>
        private List<GeneralPage>? generalPages;

        private int generalAt;

        private bool lostPart;

        /// <summary>
        /// A part of a frame the general reader could not lay at all (a pressed row's label and quieter line both given, until the
        /// kit's row takes a label; a pressed line's quieter line on a button): the part is not on screen, told apart from a page not
        /// yet turned (AUDIT's 18:00Z), so the plane logs it and the renders and walks fail on it. The bar is then never whole.
        /// </summary>
        public bool LostPart => lostPart;

        private HashSet<int>? generalSeen;

        /// <summary>The general reader's pages that hold something of the frame to read; the rest only turn bare actions.</summary>
        private HashSet<int> readPages = new HashSet<int> { 0 };

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
            if (page.Card != null) Card = page.Card;
            Lines = page.Rows.Count > 0 ? page.Rows.Select(row => row.Words).ToList() : null;
            Whole = readPages.All(generalSeen.Contains) && !lostPart;
            return true;
        }

        private sealed class GeneralPage
        {
            public GeneralPage(IReadOnlyList<RoomRow> rows, IReadOnlyList<RoomAction> column, RoomCard? card = null)
            {
                Rows = rows;
                Column = column;
                Card = card;
            }

            /// <summary>The page's card where its lines that act carry a value (the pressed rows); null where it has rows instead.</summary>
            public RoomCard? Card { get; }

            public IReadOnlyList<RoomRow> Rows { get; }

            public IReadOnlyList<RoomAction> Column { get; }
        }

        /// <summary>A line that states, as a row: its own label where it has one, a fact beside it as the words under the line's own.</summary>
        // Units laid a page at a time, never split across a page break unless one is longer than a whole page.
        // Where a weight is given, a page holds at most its budget of lines too.
        private static List<List<T>> Pack<T>(IReadOnlyList<List<T>> units, int capacity, System.Func<T, int>? weight = null, int budget = int.MaxValue)
        {
            var packed = new List<List<T>>();
            var page = new List<T>();
            var used = 0;
            foreach (var unit in units)
            {
                var unitWeight = weight == null ? 0 : unit.Sum(weight);
                if (page.Count > 0 && (page.Count + unit.Count > capacity || used + unitWeight > budget))
                {
                    packed.Add(page);
                    page = new List<T>();
                    used = 0;
                }
                foreach (var item in unit)
                {
                    var itemWeight = weight?.Invoke(item) ?? 0;
                    if (page.Count == capacity || page.Count > 0 && used + itemWeight > budget && unit.Count > 1)
                    {
                        packed.Add(page);
                        page = new List<T>();
                        used = 0;
                    }
                    page.Add(item);
                    used += itemWeight;
                }
            }
            if (page.Count > 0 || packed.Count == 0) packed.Add(page);
            return packed;
        }

        /// <summary>A card's list row's lines on <paramref name="form"/>: the kit's own count where the plane gives it, else the panel row's; 0 with no measure.</summary>
        private static int CardRowLines(RoomForm form, RoomListRow row) => form.ListLinesOf is System.Func<RoomListRow, int> listed ? System.Math.Max(1, listed(row))
            : form.LinesOf is System.Func<RoomRow, bool, int> lines
            ? System.Math.Max(1, lines(new RoomRow(row.Name, label: row.Label, note: row.Note), false)) + (row.Under is LineWords under ? System.Math.Max(1, lines(new RoomRow(under), false)) : 0)
            : 0;

        /// <summary>
        /// A card's rows in pages (the general reader's and the question reader's alike): five at most, fewer where the plane measured
        /// fewer rows a page, and by the plane's LINE measure where it gives one, eight lines a page with the card's title, description
        /// and foot counted on every page (the lead's 2026-11-13T22:04Z); units (a group of rows used together) never split.
        /// </summary>
        private static List<List<RoomListRow>> PackCard(IReadOnlyList<List<RoomListRow>> units, RoomForm form, params LineWords?[] head)
        {
            var perCard = System.Math.Min(RoomCard.ListAPage, form.RowsAPage);
            if (form.LinesOf == null && form.ListLinesOf == null) return Pack(units, perCard);
            var headLines = form.LinesOf is System.Func<RoomRow, bool, int> lines ? head.Where(words => words != null).Sum(words => System.Math.Max(1, lines(new RoomRow(words!), false))) : 0;
            return Pack(units, perCard, row => CardRowLines(form, row), System.Math.Max(1, form.LinesAPage - headLines));
        }

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
        public RoomForm(bool narrow, int rowsAPage, int actionsAPage = SpecActions, System.Func<RoomRow, bool, int>? linesOf = null, int linesAPage = SpecLines,
            float panelDp = RoomGeometry.PanelDp, System.Func<RoomListRow, int>? listLinesOf = null)
        {
            ListLinesOf = listLinesOf;
            Narrow = narrow;
            PanelDp = panelDp > RoomGeometry.PanelDp ? RoomGeometry.PanelDp : panelDp < PlaneWidth.NarrowestDp ? PlaneWidth.NarrowestDp : panelDp;
            RowsAPage = rowsAPage < 1 ? 1 : rowsAPage > RoomRows.APage ? RoomRows.APage : rowsAPage;
            ActionsAPage = actionsAPage < 1 ? 1 : actionsAPage > RoomBar.ColumnMost ? RoomBar.ColumnMost : actionsAPage;
            LinesOf = linesOf;
            LinesAPage = linesAPage < 1 ? 1 : linesAPage > SpecLines ? SpecLines : linesAPage;
        }

        /// <summary>The lines a panel holds at most (BUILD-SPEC-v4 1.1; the lead's 20:15Z: eight lines means eight lines).</summary>
        public const int SpecLines = 8;

        /// <summary>
        /// The plane's line measure: how many lines a row takes as the rows view lays it (its label, words and note, the mark's
        /// indent where the second argument says its page carries a mark), at the width the bar will be laid: the kit's own lay
        /// path (FIELD's RowLines, XR wires it), never a count of characters. Null: the reader pages by rows alone.
        /// </summary>
        public System.Func<RoomRow, bool, int>? LinesOf { get; }

        /// <summary>
        /// The plane's line measure of a card's list row as the card draws it (its mark, label, name, note and quieter line, at the
        /// panel's width; AUDIT's 2026-11-13T22:06Z: one measure for the file and the card); null where the plane gives none, and the
        /// reader then counts the row by <see cref="LinesOf"/>.
        /// </summary>
        public System.Func<RoomListRow, int>? ListLinesOf { get; }

        /// <summary>This form with the plane's measure of a card's list row, the rest kept.</summary>
        public RoomForm MeasuredList(System.Func<RoomListRow, int> listLinesOf) => new RoomForm(Narrow, RowsAPage, ActionsAPage, LinesOf, LinesAPage, PanelDp, listLinesOf);

        /// <summary>The lines a page of the panel holds, one to eight; fewer where the plane measured fewer to keep inside the field.</summary>
        public int LinesAPage { get; }

        /// <summary>This form with its line measure (the plane's), the rest kept.</summary>
        public RoomForm Measured(System.Func<RoomRow, bool, int> linesOf) => new RoomForm(Narrow, RowsAPage, ActionsAPage, linesOf, LinesAPage, PanelDp, ListLinesOf);

        /// <summary>
        /// The width the plane lays the panel at, in dp, the column beside it (the lead's 18:07Z: a width, not two forms): the full 440
        /// where the room has room, never under the floor of the text size (<see cref="PlaneWidth"/>).
        /// </summary>
        public float PanelDp { get; }

        /// <summary>This form with its panel laid <paramref name="panelDp"/> wide, the rest kept.</summary>
        public RoomForm AtWidth(float panelDp) => new RoomForm(Narrow, RowsAPage, ActionsAPage, LinesOf, LinesAPage, panelDp, ListLinesOf);

        /// <summary>The actions a choices column holds at most in all, its main action, ways out and pager among them (BUILD-SPEC-v4 1.1, the lead's 19:41Z).</summary>
        public const int SpecActions = 4;

        /// <summary>The full width, eight rows and four actions a page: the form where nothing asks for less.</summary>
        public static RoomForm Full => new RoomForm(false, RoomRows.APage);

        /// <summary>Whether the bar is laid in the kit's narrow form (its panel above its column, a column wide).</summary>
        public bool Narrow { get; }

        /// <summary>The general reader's rows a page, one to eight.</summary>
        public int RowsAPage { get; }

        /// <summary>The general reader's actions a page in all, one to the column's six; four by the spec.</summary>
        public int ActionsAPage { get; }

        /// <summary>This form with one row and one line fewer a page, never fewer than one: the plane's next ask where a page passed the field.</summary>
        public RoomForm Fewer() => new RoomForm(Narrow, RowsAPage - 1, ActionsAPage, LinesOf, LinesAPage - 1, PanelDp, ListLinesOf);

        /// <summary>This form with one action fewer a page, never fewer than one: the plane's ask where the column passed the field.</summary>
        public RoomForm FewerActions() => new RoomForm(Narrow, RowsAPage, ActionsAPage - 1, LinesOf, LinesAPage, PanelDp, ListLinesOf);
    }
}
