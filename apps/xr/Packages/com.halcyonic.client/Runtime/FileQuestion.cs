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
        public PromptMeasure(int questionRows, IReadOnlyList<int> answerRows)
        {
            QuestionRows = Math.Max(1, questionRows);
            AnswerRows = answerRows ?? throw new ArgumentNullException(nameof(answerRows));
        }

        public int QuestionRows { get; }

        public IReadOnlyList<int> AnswerRows { get; }
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
        private int rows;
        private readonly Dictionary<int, HashSet<int>> partsDrawn = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, HashSet<int>> answersRead = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, string> typedRead = new Dictionary<int, string>();
        private readonly Dictionary<int, (string Words, int Rows)> typedRows = new Dictionary<int, (string Words, int Rows)>();
        private IReadOnlyList<int> reviewRows = Array.Empty<int>();
        private IReadOnlyList<string> reviewWords = Array.Empty<string>();
        private List<List<int>> reviewPages = new List<List<int>> { new List<int>() };
        private readonly HashSet<int> reviewDrawn = new HashSet<int>();
        private readonly HashSet<int> sideDrawn = new HashSet<int>();
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

        /// <summary>The prompt's question is longer than <see cref="QuestionRows"/> rows, so it shows first on pages of its own.</summary>
        public bool QuestionCut(int prompt) => measures[prompt].QuestionRows > QuestionRows;

        /// <summary>The rows the prompt's whole question wraps to.</summary>
        public int QuestionMeasured(int prompt) => measures[prompt].QuestionRows;

        /// <summary>The rows of a long question each of its own pages shows: the page less the part's row and the reason.</summary>
        public int QuestionPartRows => Math.Max(1, rows - 2);

        /// <summary>How many pages of its own a long question takes; none for a short one.</summary>
        public int QuestionParts(int prompt) => QuestionCut(prompt) ? FileScreen.PartsOf(QuestionMeasured(prompt), QuestionPartRows) : 0;

        /// <summary>The rows the question shows heading its answers: all of a short one, the first row of a long one.</summary>
        public int HeadRows(int prompt) => QuestionCut(prompt) ? 1 : QuestionMeasured(prompt);

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
        /// measure for every prompt and every answer, on pages of <paramref name="contentRows"/> rows, the
        /// source line's already taken: from its first prompt when it is another question, else where the
        /// person was. Laid out anew, the page showing is the one holding what was chosen, or the choice
        /// is cleared; a long question partly read is read again, and one read whole stays read.
        /// </summary>
        public void Show(QuestionDraft answering, IReadOnlyList<PromptMeasure> measured, int contentRows)
        {
            if (measured == null || measured.Count != answering.Prompts.Count)
            {
                throw new ArgumentException("Every prompt is measured, once.", nameof(measured));
            }
            for (var prompt = 0; prompt < measured.Count; prompt++)
            {
                if (measured[prompt].AnswerRows.Count != answering.Prompts[prompt].Options.Count)
                {
                    throw new ArgumentException("Every answer a prompt offers is measured.", nameof(measured));
                }
            }
            var another = !ReferenceEquals(answering, draft);
            var relaid = !another && (contentRows != rows || !SameMeasures(measures, measured));
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
                sideDrawn.Clear();
                drawnAt = null;
            }
            measures = measured;
            rows = Math.Max(1, contentRows);
            pages = Enumerable.Range(0, answering.Prompts.Count).Select(prompt => Lay(answering, prompt)).ToList();
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

        private static bool SameMeasures(IReadOnlyList<PromptMeasure> a, IReadOnlyList<PromptMeasure> b) =>
            a.Count == b.Count && a.Zip(b, (x, y) => x.QuestionRows == y.QuestionRows && x.AnswerRows.SequenceEqual(y.AnswerRows)).All(same => same);

        /// <summary>
        /// A prompt's answers in pages: what is left of the rows under the question's head, beside the
        /// typed answer's row, the row on to the next question and the reason Send answer waits, filled in
        /// the agent's order; with a row for more answers where they take more than one page.
        /// </summary>
        private List<List<int>> Lay(QuestionDraft answering, int prompt)
        {
            var asked = answering.Prompts[prompt];
            var options = Enumerable.Range(0, asked.Options.Count).ToList();
            var room = rows - HeadRows(prompt) - (asked.FreeText ? 1 : 0) - (answering.Prompts.Count > 1 ? 1 : 0) - 1;
            var total = options.Sum(option => AnswerShows(prompt, option));
            if (total > room) room--;
            var laid = new List<List<int>> { new List<int>() };
            var used = 0;
            foreach (var option in options)
            {
                var takes = AnswerShows(prompt, option);
                if (used > 0 && used + takes > room)
                {
                    laid.Add(new List<int>());
                    used = 0;
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
            var room = rows - 1;
            var total = Enumerable.Range(0, draft.Prompts.Count).Sum(ReviewShows);
            if (total > room) room--;
            var used = 0;
            for (var prompt = 0; prompt < draft.Prompts.Count; prompt++)
            {
                var takes = ReviewShows(prompt);
                if (used > 0 && used + takes > room)
                {
                    reviewPages.Add(new List<int>());
                    used = 0;
                }
                reviewPages[reviewPages.Count - 1].Add(prompt);
                used += takes;
            }
        }

        /// <summary>The layout measured the typed answer as it reads now: it wraps to <paramref name="measured"/> rows.</summary>
        public void MeasureTyped(int prompt, int measured)
        {
            if (draft?.Typed(prompt) is string typed) typedRows[prompt] = (typed, Math.Max(1, measured));
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
            draft.Choose(Prompt, draft.Prompts[Prompt].Options[option].Label);
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
        /// The view reports the side panel showing a chosen answer's whole words, the typed one where
        /// <paramref name="option"/> is the prompt's option count, drew part <paramref name="part"/> of
        /// <paramref name="parts"/>: once every part has, the answer counts as read.
        /// </summary>
        public void SideDrawn(int option, int part, int parts)
        {
            if (draft == null || Reviewing || QuestionPart != null) return;
            sideDrawn.Add(part);
            if (sideDrawn.Count < Math.Max(1, parts)) return;
            var asked = draft.Prompts[Prompt];
            if (option == asked.Options.Count)
            {
                if (draft.Typed(Prompt) is string typed) typedRead[Prompt] = typed;
            }
            else if (option >= 0 && option < asked.Options.Count && draft.IsChosen(Prompt, asked.Options[option].Label))
            {
                if (!answersRead.TryGetValue(Prompt, out var read)) answersRead[Prompt] = read = new HashSet<int>();
                read.Add(option);
            }
            sideDrawn.Clear();
        }

        /// <summary>A side panel opened or closed: its parts are counted afresh.</summary>
        public void SideChanged() => sideDrawn.Clear();

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
        }

        /// <summary>The row on to the next question, or after the last, to the person's answers.</summary>
        public void NextQuestion(DateTimeOffset now)
        {
            if (draft == null || draft.Prompts.Count < 2 || Reviewing || QuestionPart != null || !Settled(now)) return;
            Prompt++;
            Page = 0;
            QuestionPart = !Reviewing && QuestionParts(Prompt) > 0 && !draft.WasShownWhole(Prompt) ? 0 : (int?)null;
            Turned();
        }

        /// <summary>From the person's answers back to a prompt, at its first page of answers.</summary>
        public void GoTo(int prompt)
        {
            if (draft == null || prompt < 0 || prompt >= draft.Prompts.Count) return;
            Prompt = prompt;
            Page = 0;
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
