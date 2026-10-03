#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>How one prompt of the agent's question lays out on a file's page, as the layout measured it.</summary>
    public sealed class PromptMeasure
    {
        /// <param name="questionRows">The rows the prompt's text wraps to across the page.</param>
        /// <param name="answerRows">The rows each answer offered wraps to across the page, in the prompt's order, one for every answer.</param>
        /// <param name="answerSideRows">The rows each answer wraps to across a side panel, where a cut one shows whole, one for every answer.</param>
        public PromptMeasure(int questionRows, IReadOnlyList<int> answerRows, IReadOnlyList<int> answerSideRows)
        {
            QuestionRows = Math.Max(1, questionRows);
            AnswerRows = answerRows ?? throw new ArgumentNullException(nameof(answerRows));
            AnswerSideRows = answerSideRows ?? throw new ArgumentNullException(nameof(answerSideRows));
        }

        public int QuestionRows { get; }

        public IReadOnlyList<int> AnswerRows { get; }

        public IReadOnlyList<int> AnswerSideRows { get; }
    }

    /// <summary>
    /// Where the person is in the agent's question on a file's Waiting page (ADR 0026, lane V's calls of
    /// 2026-10-02), and what of it they have read. One prompt at a time: a question longer than
    /// <see cref="QuestionRows"/> rows first on pages of its own, in parts, its last part ending in a
    /// row on to the answers; then its answers in the agent's order, none left out, paged by a row at
    /// the page's end, the question heading each page, whole where it is short and by its first row
    /// where it is long. For a question of several prompts, then the person's answers, each whole,
    /// which is the only page it sends from.
    /// </summary>
    /// <remarks>
    /// Nothing counts as read until the view reports it drawn (<see cref="Drawn"/>,
    /// <see cref="SideDrawn"/>), never by building a page or turning to it: a question once every part
    /// of it, or the page heading its answers when it is short; a cut answer or a typed answer longer
    /// than its row once its side panel has shown all of it; the person's answers once every page of
    /// them. Send answer sends only what is on the page in view: a choice is taken only from the page
    /// showing, turning a prompt's page clears it, and laid out anew, the page shows the choice or the
    /// choice is cleared. A part's row turns nothing until the part showing has been drawn and stood
    /// for <see cref="TurnGuard"/>.
    /// </remarks>
    public sealed class FileQuestion
    {
        /// <summary>The most rows the question shows heading its answers; a longer one shows first on pages of its own.</summary>
        public const int QuestionRows = 2;

        /// <summary>The most rows an answer shows; a longer one is cut, and choosing it shows all its words beside the page.</summary>
        public const int AnswerRows = 2;

        /// <summary>A part's row turns nothing this soon after its part was first drawn.</summary>
        public static readonly TimeSpan TurnGuard = TimeSpan.FromSeconds(0.4);

        private QuestionDraft? draft;
        private IReadOnlyList<PromptMeasure> measures = Array.Empty<PromptMeasure>();
        private List<List<List<int>>> pages = new List<List<List<int>>>();
        private PageBudget page = new RowBudget(1);
        private readonly Dictionary<int, HashSet<int>> partsDrawn = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, HashSet<int>> answersRead = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, string> typedRead = new Dictionary<int, string>();
        private readonly Dictionary<int, (string Words, int Rows, int SideRows)> typedRows = new Dictionary<int, (string Words, int Rows, int SideRows)>();
        private PageBudget side = new RowBudget(1);
        private int? sideOption;
        private readonly Dictionary<(int Prompt, int Option), HashSet<int>> sidePartsDrawn = new Dictionary<(int Prompt, int Option), HashSet<int>>();
        private DateTimeOffset? sideDrawnAt;
        private IReadOnlyList<int> reviewRows = Array.Empty<int>();
        private IReadOnlyList<string> reviewWords = Array.Empty<string>();
        private List<List<int>> reviewPages = new List<List<int>> { new List<int>() };
        private readonly HashSet<int> reviewDrawn = new HashSet<int>();
        private DateTimeOffset? drawnAt;

        public QuestionDraft? Draft => draft;

        /// <summary>The prompt showing, from 0; for a question of several prompts, their count while the person's answers show.</summary>
        public int Prompt { get; private set; }

        /// <summary>The part of a long question showing on a page of its own, from 0; null while its answers or the person's show.</summary>
        public int? QuestionPart { get; private set; }

        /// <summary>The page of the prompt's answers, or of the person's answers, showing, from 0.</summary>
        public int Page { get; private set; }

        /// <summary>The page of the person's answers shows: a question of several prompts, after its last.</summary>
        public bool Reviewing => draft != null && draft.Prompts.Count > 1 && Prompt == draft.Prompts.Count;

        /// <summary>How many pages the prompt's answers, or the person's answers, take.</summary>
        public int Pages => draft == null ? 1 : Reviewing ? reviewPages.Count : pages[Prompt].Count;

        /// <summary>The answers on the page showing, by their index among the prompt's options; none while the question's own pages show.</summary>
        public IReadOnlyList<int> Answers => draft == null || Reviewing || QuestionPart != null ? Array.Empty<int>() : pages[Prompt][Page];

        /// <summary>The prompts listed on the page of the person's answers showing.</summary>
        public IReadOnlyList<int> Reviewed => Reviewing ? reviewPages[Page] : Array.Empty<int>();

        /// <summary>
        /// The prompt's question shows first on pages of its own: it is longer than
        /// <see cref="QuestionRows"/> rows, or can't share a page with an answer, the typed answer's row,
        /// the row on to the next question and the reason Send answer waits, or that leaves fewer pages
        /// in all, its answers' pages then holding more answers each under its first line (ADR 0026).
        /// On a tie it stays with its answers, read beside them.
        /// </summary>
        public bool QuestionCut(int prompt)
        {
            var rows = measures[prompt].QuestionRows;
            if (rows > QuestionRows || !HeadFits(prompt, rows)) return true;
            if (draft == null || rows < 2) return false;
            var cut = FileScreen.PartsOf(rows, QuestionPartRows) + Lay(draft, prompt, HeadFits(prompt, 1) ? 1 : 0).Count;
            return cut < Lay(draft, prompt, rows).Count;
        }

        /// <summary>
        /// A head of <paramref name="headRows"/> rows leaves room for at least one answer beside the typed
        /// answer's row, the row on, the reason, and, where the answers take more than one page, the row
        /// for more answers.
        /// </summary>
        private bool HeadFits(int prompt, int headRows)
        {
            if (draft == null) return true;
            var asked = draft.Prompts[prompt];
            if (asked.Options.Count == 0) return true;
            var several = draft.Prompts.Count > 1;
            var room = page.Room - Head(headRows) - Below(asked.FreeText, several);
            if (page.Targets(Enumerable.Range(0, asked.Options.Count).Select(option => AnswerShows(prompt, option)).ToArray()) <= room + 1e-6f) return true;
            var least = Enumerable.Range(0, asked.Options.Count).Min(option => AnswerShows(prompt, option));
            return page.Target(least) + MoreRow(asked.FreeText, several) <= room + 1e-6f;
        }

        /// <summary>A head of words and the gap after it, the question and its answers being one group; none for no head.</summary>
        private float Head(int rows) => rows <= 0 ? 0f : page.Words(rows) + page.LineGap;

        /// <summary>
        /// What a page of answers takes besides its head and answers: Type my answer, sharing its row with
        /// the paging row after it (lane V's rule 4), the row on to the next question, and their gaps; and
        /// for a question of several prompts the reason Send answer waits, which always shows there. A
        /// question of one prompt draws no reason while it waits only for a choice, or while the page
        /// explains it (<see cref="Prompt.PageExplains"/>).
        /// </summary>
        private float Below(bool typed, bool several) =>
            (typed || several ? page.Target() + page.TargetGap : 0f) + (several ? page.Reason : 0f);

        /// <summary>The row for more answers, where a page needs it: beside Type my answer it takes no row of its own.</summary>
        private float MoreRow(bool typed, bool several) => typed && !several ? 0f : page.Target() + page.TargetGap;

        /// <summary>The rows the prompt's whole question wraps to.</summary>
        public int QuestionMeasured(int prompt) => measures[prompt].QuestionRows;

        /// <summary>The rows of a long question each of its own pages shows: as many as fit beside the part's row and the reason.</summary>
        public int QuestionPartRows => page.WordsIn(page.Room - page.LineGap - page.Target() - page.Reason);

        /// <summary>How many pages of its own a long question takes; none for a short one.</summary>
        public int QuestionParts(int prompt) => QuestionCut(prompt) ? FileScreen.PartsOf(QuestionMeasured(prompt), QuestionPartRows) : 0;

        /// <summary>
        /// The rows the question shows heading its answers: all of a short one, the first row of a long one
        /// where that leaves room for an answer, else none.
        /// </summary>
        public int HeadRows(int prompt) => !QuestionCut(prompt) ? QuestionMeasured(prompt) : HeadFits(prompt, 1) ? 1 : 0;

        /// <summary>The answer's words are longer than <see cref="AnswerRows"/> rows, so it is cut.</summary>
        public bool AnswerCut(int prompt, int option) => measures[prompt].AnswerRows[option] > AnswerRows;

        /// <summary>The rows the answer shows on the page.</summary>
        public int AnswerShows(int prompt, int option) => Math.Max(1, Math.Min(AnswerRows, measures[prompt].AnswerRows[option]));

        /// <summary>The rows the answer's whole words wrap to, as its side panel shows them.</summary>
        public int AnswerMeasured(int prompt, int option) => measures[prompt].AnswerRows[option];

        /// <summary>The typed answer is longer than its row's <see cref="AnswerRows"/> rows, so its row is cut.</summary>
        public bool TypedCut(int prompt) => draft?.Typed(prompt) != null && (!TypedMeasuredNow(prompt) || TypedMeasured(prompt) > AnswerRows);

        /// <summary>The typed answer was measured as it reads now; one unmeasured, or measured before it changed, counts as cut and unread.</summary>
        private bool TypedMeasuredNow(int prompt) =>
            draft?.Typed(prompt) is string typed && typedRows.TryGetValue(prompt, out var measured) && measured.Words == typed;

        /// <summary>The rows the typed answer wraps to, as the layout measured it; as many as its row holds until it is measured as it reads now.</summary>
        public int TypedMeasured(int prompt) => TypedMeasuredNow(prompt) ? typedRows[prompt].Rows : AnswerRows;

        /// <summary>The rows each prompt's answer takes on the page of the person's answers, as the layout measured it.</summary>
        public int ReviewShows(int prompt) => prompt < reviewRows.Count ? Math.Max(1, reviewRows[prompt]) : 1;

        /// <summary>
        /// Shows <paramref name="answering"/>, each prompt laid out as <paramref name="measured"/> says, one
        /// measure for every prompt and every answer, on pages as <paramref name="pageBudget"/> holds them
        /// and side panels as <paramref name="sideBudget"/> does, each source line already taken,
        /// source line's already taken: from its first prompt when it is another question, else where the
        /// person was. Laid out anew, the page showing is the one holding what was chosen, or the choice
        /// is cleared; a long question partly read is read again, and one read whole stays read.
        /// </summary>
        public void Show(QuestionDraft answering, IReadOnlyList<PromptMeasure> measured, PageBudget pageBudget, PageBudget sideBudget)
        {
            if (measured == null || measured.Count != answering.Prompts.Count)
            {
                throw new ArgumentException("Every prompt is measured, once.", nameof(measured));
            }
            for (var prompt = 0; prompt < measured.Count; prompt++)
            {
                if (measured[prompt].AnswerRows.Count != answering.Prompts[prompt].Options.Count
                    || measured[prompt].AnswerSideRows.Count != answering.Prompts[prompt].Options.Count)
                {
                    throw new ArgumentException("Every answer a prompt offers is measured.", nameof(measured));
                }
            }
            var another = !ReferenceEquals(answering, draft);
            var relaid = !another && (!Same(page, pageBudget) || !Same(side, sideBudget) || !SameMeasures(measures, measured));
            if (another)
            {
                draft = answering;
                Prompt = 0;
                Page = 0;
                partsDrawn.Clear();
                answersRead.Clear();
                typedRead.Clear();
                typedRows.Clear();
                reviewDrawn.Clear();
                sidePartsDrawn.Clear();
                sideOption = null;
                drawnAt = null;
            }
            measures = measured;
            draft = answering;
            page = pageBudget;
            side = sideBudget;
            pages = Enumerable.Range(0, answering.Prompts.Count).Select(prompt => Lay(answering, prompt, HeadRows(prompt))).ToList();
            LayReview();
            if (another)
            {
                QuestionPart = QuestionParts(0) > 0 ? 0 : (int?)null;
                return;
            }
            Prompt = Math.Min(Prompt, answering.Prompts.Count > 1 ? answering.Prompts.Count : 0);
            if (!relaid)
            {
                Page = Math.Min(Page, Pages - 1);
                return;
            }
            drawnAt = null;
            reviewDrawn.Clear();
            // A side panel's parts read so far no longer map to its new parts: what is unread is read again.
            sidePartsDrawn.Clear();
            SidePart = 0;
            sideDrawnAt = null;
            for (var prompt = 0; prompt < answering.Prompts.Count; prompt++)
            {
                // A long question partly read no longer maps to its new parts: it is read again.
                if (!answering.WasShownWhole(prompt)) partsDrawn.Remove(prompt);
            }
            if (Reviewing)
            {
                Page = 0;
                return;
            }
            var parts = QuestionParts(Prompt);
            // A question that has parts and isn't read shows its first part, whatever showed before,
            // so laid out longer it is never left with no way to read it and Send answer waiting.
            if (parts > 0 && !answering.WasShownWhole(Prompt))
            {
                QuestionPart = 0;
                Page = 0;
                return;
            }
            if (QuestionPart != null)
            {
                QuestionPart = parts == 0 ? (int?)null : parts - 1;
                Page = 0;
                if (QuestionPart == null) Land(answering);
                return;
            }
            Land(answering);
        }

        /// <summary>Lands on the page holding what is chosen for the prompt showing; chosen across pages, the choice is cleared.</summary>
        private void Land(QuestionDraft answering)
        {
            var asked = answering.Prompts[Prompt];
            var chosen = Enumerable.Range(0, asked.Options.Count).Where(option => answering.IsChosen(Prompt, asked.Options[option].Label)).ToList();
            var holding = pages[Prompt].Select((page, index) => (page, index)).Where(each => chosen.Any(each.page.Contains)).Select(each => each.index).ToList();
            if (holding.Count == 1) Page = holding[0];
            else
            {
                if (holding.Count > 1) answering.ClearChosen(Prompt);
                Page = Math.Min(Page, pages[Prompt].Count - 1);
            }
        }

        private static bool Same(PageBudget a, PageBudget b) => a.GetType() == b.GetType() && Math.Abs(a.Room - b.Room) < 1e-6f;

        private static bool SameMeasures(IReadOnlyList<PromptMeasure> a, IReadOnlyList<PromptMeasure> b) =>
            a.Count == b.Count && a.Zip(b, (x, y) => x.QuestionRows == y.QuestionRows && x.AnswerRows.SequenceEqual(y.AnswerRows)
                && x.AnswerSideRows.SequenceEqual(y.AnswerSideRows)).All(same => same);

        /// <summary>
        /// A prompt's answers in pages: what is left of the rows under the question's head, beside the
        /// typed answer's row, the row on to the next question and the reason Send answer waits, filled in
        /// the agent's order; with a row for more answers where they take more than one page. The head
        /// is <paramref name="headRows"/> rows of the question.
        /// </summary>
        private List<List<int>> Lay(QuestionDraft answering, int prompt, int headRows)
        {
            var asked = answering.Prompts[prompt];
            var options = Enumerable.Range(0, asked.Options.Count).ToList();
            var several = answering.Prompts.Count > 1;
            var room = page.Room - Head(headRows) - Below(asked.FreeText, several);
            if (page.Targets(options.Select(option => AnswerShows(prompt, option)).ToArray()) > room + 1e-6f) room -= MoreRow(asked.FreeText, several);
            var laid = new List<List<int>> { new List<int>() };
            var used = 0f;
            foreach (var option in options)
            {
                var takes = page.Target(AnswerShows(prompt, option)) + (laid[laid.Count - 1].Count > 0 ? page.TargetGap : 0f);
                if (laid[laid.Count - 1].Count > 0 && used + takes > room + 1e-6f)
                {
                    laid.Add(new List<int>());
                    used = 0f;
                    takes = page.Target(AnswerShows(prompt, option));
                }
                laid[laid.Count - 1].Add(option);
                used += takes;
            }
            return laid;
        }

        /// <summary>The person's answers in pages of whole answers, beside the reason and, where they page, the row to the next page.</summary>
        private void LayReview()
        {
            reviewPages = new List<List<int>> { new List<int>() };
            if (draft == null || draft.Prompts.Count < 2) return;
            var room = page.Room - page.Reason;
            if (page.Targets(Enumerable.Range(0, draft.Prompts.Count).Select(ReviewShows).ToArray()) > room + 1e-6f) room -= page.Target() + page.TargetGap;
            var used = 0f;
            for (var prompt = 0; prompt < draft.Prompts.Count; prompt++)
            {
                var current = reviewPages[reviewPages.Count - 1];
                var takes = page.Target(ReviewShows(prompt)) + (current.Count > 0 ? page.TargetGap : 0f);
                if (current.Count > 0 && used + takes > room + 1e-6f)
                {
                    reviewPages.Add(new List<int>());
                    used = 0f;
                    takes = page.Target(ReviewShows(prompt));
                }
                reviewPages[reviewPages.Count - 1].Add(prompt);
                used += takes;
            }
        }

        /// <summary>
        /// The layout measured the typed answer as it reads now: it wraps to <paramref name="measured"/>
        /// rows on the page and <paramref name="sideMeasured"/> in a side panel. Longer than its row, its
        /// side panel shows all of it.
        /// </summary>
        public void MeasureTyped(int prompt, int measured, int sideMeasured)
        {
            if (!(draft?.Typed(prompt) is string typed)) return;
            typedRows[prompt] = (typed, Math.Max(1, measured), Math.Max(1, sideMeasured));
            if (prompt != Prompt || Reviewing || QuestionPart != null) return;
            var index = draft.Prompts[prompt].Options.Count;
            if (TypedCut(prompt)) OpenSide(index);
            else if (sideOption == index) sideOption = null;
        }

        /// <summary>
        /// The layout measured each prompt's answer as the page of the person's answers shows it whole
        /// (<see cref="FileScreens.ReviewWords"/>), as it reads now: should an answer change, or a prompt
        /// go unmeasured, the page counts as unread until measured again.
        /// </summary>
        public void MeasureReview(IReadOnlyList<int> measured)
        {
            var words = ReviewWordsNow();
            if (reviewRows.SequenceEqual(measured) && reviewWords.SequenceEqual(words)) return;
            reviewRows = measured;
            reviewWords = words;
            reviewDrawn.Clear();
            LayReview();
            if (Reviewing) Page = Math.Min(Page, Pages - 1);
        }

        /// <summary>The person chose an answer: only one on the page showing is taken, so nothing out of view is sent.</summary>
        public void Choose(int option)
        {
            if (draft == null || !Answers.Contains(option)) return;
            var asked = draft.Prompts[Prompt];
            draft.Choose(Prompt, asked.Options[option].Label);
            if (draft.IsChosen(Prompt, asked.Options[option].Label) && AnswerCut(Prompt, option)) OpenSide(option);
            else if (sideOption is int open && !SideStillChosen(open)) sideOption = null;
        }

        /// <summary>
        /// The answer whose side panel shows all its words: a chosen cut answer, or the typed one where it
        /// is the prompt's option count, when it is longer than its row; null while none shows.
        /// </summary>
        public int? SideOption => sideOption is int open && draft != null && !Reviewing && QuestionPart == null && SideStillChosen(open) ? open : (int?)null;

        /// <summary>The rows of the side panel's answer each of its parts shows.</summary>
        public int SidePartRows => side.WordsIn(side.Room);

        /// <summary>The part of the side panel's answer showing, from 0.</summary>
        public int SidePart { get; private set; }

        /// <summary>How many parts the side panel's answer takes, worked out from its own measurement.</summary>
        public int SideParts => SideOption is int open ? FileScreen.PartsOf(SideMeasured(Prompt, open), SidePartRows) : 0;

        /// <summary>The rows an answer, or the typed one at the option count, wraps to across a side panel.</summary>
        public int SideMeasured(int prompt, int option) =>
            option < measures[prompt].AnswerSideRows.Count ? Math.Max(1, measures[prompt].AnswerSideRows[option])
            : typedRows.TryGetValue(prompt, out var typed) ? typed.SideRows : 1;

        private void OpenSide(int option)
        {
            sideOption = option;
            SidePart = 0;
            sideDrawnAt = null;
        }

        private bool SideStillChosen(int option)
        {
            if (draft == null || Prompt >= draft.Prompts.Count) return false;
            var asked = draft.Prompts[Prompt];
            return option == asked.Options.Count ? TypedCut(Prompt) : option < asked.Options.Count && draft.IsChosen(Prompt, asked.Options[option].Label);
        }

        /// <summary>
        /// The view reports the page showing was drawn at <paramref name="now"/>: a part of a long question
        /// counts as read, and the question once every part has; a short question once the page heading its
        /// answers has; a page of the person's answers.
        /// </summary>
        public void Drawn(DateTimeOffset now)
        {
            if (draft == null) return;
            drawnAt ??= now;
            if (Reviewing)
            {
                reviewDrawn.Add(Page);
                return;
            }
            if (QuestionPart is int part)
            {
                if (!partsDrawn.TryGetValue(Prompt, out var drawn)) partsDrawn[Prompt] = drawn = new HashSet<int>();
                drawn.Add(part);
                if (drawn.Count >= QuestionParts(Prompt)) draft.ShownWhole(Prompt);
                return;
            }
            if (!QuestionCut(Prompt)) draft.ShownWhole(Prompt);
        }

        /// <summary>
        /// The view reports it drew the side panel showing at <paramref name="now"/>: the part showing of
        /// the answer it holds counts as read, and the answer once every part has. Which answer and how
        /// many parts it takes are this model's own, from the answer's measurement, never the view's.
        /// </summary>
        public void SideDrawn(DateTimeOffset now)
        {
            if (draft == null || !(SideOption is int open)) return;
            sideDrawnAt ??= now;
            var key = (Prompt, open);
            if (!sidePartsDrawn.TryGetValue(key, out var drawn)) sidePartsDrawn[key] = drawn = new HashSet<int>();
            drawn.Add(SidePart);
            if (drawn.Count < SideParts) return;
            var asked = draft.Prompts[Prompt];
            if (open == asked.Options.Count)
            {
                if (draft.Typed(Prompt) is string typed) typedRead[Prompt] = typed;
            }
            else
            {
                if (!answersRead.TryGetValue(Prompt, out var read)) answersRead[Prompt] = read = new HashSet<int>();
                read.Add(open);
            }
        }

        /// <summary>The footer's Next page while the side panel's answer is in parts: its next part, or from the last the first.</summary>
        public void NextSidePart(DateTimeOffset now)
        {
            if (SideParts < 2 || !(sideDrawnAt is DateTimeOffset at) || now - at < TurnGuard) return;
            SidePart = (SidePart + 1) % SideParts;
            sideDrawnAt = null;
        }

        private bool Settled(DateTimeOffset now) => drawnAt is DateTimeOffset at && now - at >= TurnGuard;

        private void Turned() => drawnAt = null;

        /// <summary>The row ending a long question's part: its next part, or after the last, its answers.</summary>
        public void NextPart(DateTimeOffset now)
        {
            if (draft == null || !(QuestionPart is int part) || !Settled(now)) return;
            QuestionPart = part + 1 < QuestionParts(Prompt) ? part + 1 : (int?)null;
            Page = 0;
            Turned();
        }

        /// <summary>
        /// The row for more answers: the prompt's next page, or from the last the first. What was chosen
        /// for this prompt is cleared, so Send answer only ever sends what is on the page in view; the
        /// typed answer stays, as its row is on every page. On the person's answers, their next page.
        /// </summary>
        public void MoreAnswers(DateTimeOffset now)
        {
            if (draft == null || QuestionPart != null || Pages < 2 || !Settled(now)) return;
            if (!Reviewing) draft.ClearChosen(Prompt);
            Page = (Page + 1) % Pages;
            Turned();
            sideOption = null;
        }

        /// <summary>The row on to the next question, or after the last, to the person's answers.</summary>
        public void NextQuestion(DateTimeOffset now)
        {
            if (draft == null || draft.Prompts.Count < 2 || Reviewing || QuestionPart != null || !Settled(now)) return;
            Prompt++;
            Page = 0;
            sideOption = null;
            QuestionPart = !Reviewing && QuestionParts(Prompt) > 0 && !draft.WasShownWhole(Prompt) ? 0 : (int?)null;
            Turned();
        }

        /// <summary>From the person's answers back to a prompt, at its first page of answers.</summary>
        public void GoTo(int prompt)
        {
            if (draft == null || prompt < 0 || prompt >= draft.Prompts.Count) return;
            Prompt = prompt;
            Page = 0;
            sideOption = null;
            QuestionPart = QuestionParts(prompt) > 0 && !draft.WasShownWhole(prompt) ? 0 : (int?)null;
            Land(draft);
            Turned();
        }

        /// <summary>Every answer chosen or typed for the prompt has been read whole: a cut one or a long typed one in its side panel.</summary>
        public bool AnswersRead(int prompt)
        {
            if (draft == null) return false;
            var asked = draft.Prompts[prompt];
            for (var option = 0; option < asked.Options.Count; option++)
            {
                if (draft.IsChosen(prompt, asked.Options[option].Label) && AnswerCut(prompt, option)
                    && !(answersRead.TryGetValue(prompt, out var read) && read.Contains(option)))
                {
                    return false;
                }
            }
            return !TypedCut(prompt) || (typedRead.TryGetValue(prompt, out var typed) && typed == draft.Typed(prompt));
        }

        /// <summary>Every chosen answer of the prompt showing is on the page in view.</summary>
        public bool ChosenInView
        {
            get
            {
                if (draft == null || Reviewing) return true;
                var asked = draft.Prompts[Prompt];
                return Enumerable.Range(0, asked.Options.Count)
                    .Where(option => draft.IsChosen(Prompt, asked.Options[option].Label))
                    .All(option => QuestionPart == null && Answers.Contains(option));
            }
        }

        /// <summary>Every page of the person's answers has been drawn.</summary>
        public bool ReviewRead => Reviewing && ReviewMeasuredNow && Enumerable.Range(0, reviewPages.Count).All(reviewDrawn.Contains);

        /// <summary>Every prompt's answer was measured as the page of the person's answers shows it now.</summary>
        private bool ReviewMeasuredNow => draft != null && reviewRows.Count == draft.Prompts.Count && reviewWords.SequenceEqual(ReviewWordsNow());

        private IReadOnlyList<string> ReviewWordsNow() =>
            draft == null ? Array.Empty<string>() : Enumerable.Range(0, draft.Prompts.Count).Select(prompt => FileScreens.ReviewWords(draft, prompt)).ToList();
    }
}
