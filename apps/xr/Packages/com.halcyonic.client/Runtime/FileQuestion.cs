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
        /// <param name="answerRows">The rows each answer offered wraps to across the page, in the prompt's order.</param>
        public PromptMeasure(int questionRows, IReadOnlyList<int> answerRows)
        {
            QuestionRows = Math.Max(1, questionRows);
            AnswerRows = answerRows ?? throw new ArgumentNullException(nameof(answerRows));
        }

        public int QuestionRows { get; }

        public IReadOnlyList<int> AnswerRows { get; }
    }

    /// <summary>
    /// Where the person is in the agent's question on a file's Waiting page (ADR 0026, lane V's call of
    /// 2026-10-02): one prompt at a time, its question heading every page in at most
    /// <see cref="QuestionRows"/> rows, its answers in the agent's order paged by a row at the page's
    /// end, none left out; then, for a question of several prompts, the page of the person's answers,
    /// which is the only place it sends from, so nothing sent is ever out of view. Turning a prompt's
    /// page clears what was chosen for that prompt, so what is sent is what is on the page in view;
    /// the text typed for it is kept, as its row stands on every page. A question cut short counts as
    /// read whole only once its side panel has shown all of it.
    /// </summary>
    public sealed class FileQuestion
    {
        /// <summary>The most rows the question shows heading a page; a longer one is cut and opens its side panel.</summary>
        public const int QuestionRows = 2;

        /// <summary>The most rows an answer shows; a longer one is cut, and choosing it shows all its words beside the page.</summary>
        public const int AnswerRows = 2;

        private QuestionDraft? draft;
        private IReadOnlyList<PromptMeasure> measures = Array.Empty<PromptMeasure>();
        private List<List<List<int>>> pages = new List<List<List<int>>>();

        public QuestionDraft? Draft => draft;

        /// <summary>The prompt showing, from 0; for a question of several prompts, their count while the person's answers show.</summary>
        public int Prompt { get; private set; }

        /// <summary>The page of the prompt's answers showing, from 0.</summary>
        public int Page { get; private set; }

        /// <summary>The page of the person's answers shows: a question of several prompts, after its last.</summary>
        public bool Reviewing => draft != null && draft.Prompts.Count > 1 && Prompt == draft.Prompts.Count;

        /// <summary>How many pages the prompt's answers take.</summary>
        public int Pages => draft == null || Reviewing ? 1 : pages[Prompt].Count;

        /// <summary>The answers on the page showing, by their index among the prompt's options; the typed answer's row is apart.</summary>
        public IReadOnlyList<int> Answers => draft == null || Reviewing ? Array.Empty<int>() : pages[Prompt][Page];

        /// <summary>The prompt's question is longer than <see cref="QuestionRows"/> rows, so it is cut and opens its side panel.</summary>
        public bool QuestionCut(int prompt) => prompt < measures.Count && measures[prompt].QuestionRows > QuestionRows;

        /// <summary>The rows the prompt's whole question wraps to, as its side panel shows it.</summary>
        public int QuestionMeasured(int prompt) => prompt < measures.Count ? measures[prompt].QuestionRows : QuestionRows;

        /// <summary>The rows the prompt's question shows heading its pages.</summary>
        public int QuestionShows(int prompt) => prompt < measures.Count ? Math.Min(QuestionRows, measures[prompt].QuestionRows) : QuestionRows;

        /// <summary>The answer's words are longer than <see cref="AnswerRows"/> rows, so it is cut.</summary>
        public bool AnswerCut(int prompt, int option) =>
            prompt < measures.Count && option < measures[prompt].AnswerRows.Count && measures[prompt].AnswerRows[option] > AnswerRows;

        /// <summary>The rows the answer shows.</summary>
        public int AnswerShows(int prompt, int option) =>
            prompt < measures.Count && option < measures[prompt].AnswerRows.Count ? Math.Max(1, Math.Min(AnswerRows, measures[prompt].AnswerRows[option])) : AnswerRows;

        /// <summary>
        /// Shows <paramref name="answering"/>, each prompt laid out as <paramref name="measured"/> says on
        /// pages of <paramref name="rows"/> content rows: from its first prompt when it is another
        /// question, else where the person was. Every answer is on some page, at least one a page.
        /// </summary>
        public void Show(QuestionDraft answering, IReadOnlyList<PromptMeasure> measured, int rows)
        {
            if (!ReferenceEquals(answering, draft))
            {
                draft = answering;
                Prompt = 0;
                Page = 0;
            }
            measures = measured;
            pages = Enumerable.Range(0, answering.Prompts.Count).Select(prompt => Lay(answering, prompt, rows)).ToList();
            Prompt = Math.Min(Prompt, answering.Prompts.Count > 1 ? answering.Prompts.Count : 0);
            Page = Math.Min(Page, Pages - 1);
        }

        /// <summary>
        /// A prompt's answers in pages: what is left of the rows under its question, beside the typed
        /// answer's row, the row on to the next question and the reason Send answer waits, filled in the
        /// agent's order; with a row for more answers where they take more than one page.
        /// </summary>
        private List<List<int>> Lay(QuestionDraft answering, int prompt, int rows)
        {
            var asked = answering.Prompts[prompt];
            var options = Enumerable.Range(0, asked.Options.Count).ToList();
            var room = rows - QuestionShows(prompt) - (asked.FreeText ? 1 : 0) - (answering.Prompts.Count > 1 ? 1 : 0) - 1;
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

        /// <summary>
        /// The row for more answers: the prompt's next page, or from the last the first. What was
        /// chosen for this prompt is cleared, so Send answer only ever sends what is on the page in
        /// view; the typed answer stays, as its row is on every page.
        /// </summary>
        public void MoreAnswers()
        {
            if (draft == null || Reviewing || Pages < 2) return;
            draft.ClearChosen(Prompt);
            Page = (Page + 1) % Pages;
        }

        /// <summary>The row on to the next question, or after the last, to the person's answers.</summary>
        public void NextQuestion()
        {
            if (draft == null || draft.Prompts.Count < 2 || Reviewing) return;
            Prompt++;
            Page = 0;
        }

        /// <summary>From the person's answers back to a prompt, at its first page.</summary>
        public void GoTo(int prompt)
        {
            if (draft == null || prompt < 0 || prompt >= draft.Prompts.Count) return;
            Prompt = prompt;
            Page = 0;
        }

        /// <summary>
        /// The prompt's question has shown: whole, when it was not cut; when cut, only once
        /// <paramref name="sidePanel"/> shows all of it.
        /// </summary>
        public void Shown(int prompt, bool sidePanel)
        {
            if (draft == null || prompt >= draft.Prompts.Count) return;
            if (sidePanel || !QuestionCut(prompt)) draft.ShownWhole(prompt);
        }
    }
}
