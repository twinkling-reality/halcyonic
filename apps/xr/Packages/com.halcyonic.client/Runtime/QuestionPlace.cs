#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where the person is in an agent's question as the workspace shows it (ADR 0022): a step at a
    /// time, prompt after prompt. Each prompt takes a step for each part of its text, as the panel
    /// measured it, then one for each further page of its answers, two to a page, Type an answer
    /// among them where typing is allowed; the last part of its text stays in view while its answers
    /// page. Next and Previous step on and back across prompts. A prompt counts as shown whole
    /// (<see cref="QuestionDraft.ShownWhole"/>) once the last part of its text has shown. A question
    /// Halcyonic can't answer pages only through its text. Another draft is another question, or the
    /// same asked again, and starts from its first step; the same draft keeps the person's place.
    /// </summary>
    public sealed class QuestionPlace
    {
        public const int AnswersPerPage = 2;

        private QuestionDraft? draft;
        private int[] textParts = Array.Empty<int>();
        private bool measured;

        /// <summary>The step showing, from 0, across every prompt.</summary>
        public int Step { get; private set; }

        /// <summary>The question being read, or null before one shows.</summary>
        public QuestionDraft? Draft => draft;

        /// <summary>How many steps the whole question takes.</summary>
        public int Steps
        {
            get
            {
                var steps = 0;
                for (var prompt = 0; prompt < textParts.Length; prompt++) steps += StepsOf(prompt);
                return Math.Max(1, steps);
            }
        }

        /// <summary>The prompt showing, from 0.</summary>
        public int Prompt => Locate().Prompt;

        /// <summary>The part of the prompt's text showing, from 0: its last while its answers page.</summary>
        public int TextPart
        {
            get
            {
                var (prompt, within) = Locate();
                return Math.Min(within, PartsOf(prompt) - 1);
            }
        }

        /// <summary>The page of the prompt's answers showing, from 0.</summary>
        public int AnswerPage
        {
            get
            {
                var (prompt, within) = Locate();
                return Math.Max(0, within - (PartsOf(prompt) - 1));
            }
        }

        /// <summary>
        /// The answers on the page showing, as indexes into the prompt's options, where the option
        /// count stands for Type an answer; none for a question Halcyonic can't answer.
        /// </summary>
        public IReadOnlyList<int> Answers
        {
            get
            {
                var shown = new List<int>();
                if (draft == null || !WorkspaceText.Answerable(draft.Question)) return shown;
                var prompt = Prompt;
                var count = AnswerCount(prompt);
                for (var index = AnswerPage * AnswersPerPage; index < count && shown.Count < AnswersPerPage; index++) shown.Add(index);
                return shown;
            }
        }

        /// <summary>Shows <paramref name="answering"/>: from its first step when it is another draft, else where the person was.</summary>
        public void Show(QuestionDraft answering)
        {
            if (answering == null) throw new ArgumentNullException(nameof(answering));
            if (!ReferenceEquals(draft, answering))
            {
                Step = 0;
                measured = false;
                textParts = new int[answering.Prompts.Count];
                for (var prompt = 0; prompt < textParts.Length; prompt++) textParts[prompt] = 1;
            }
            draft = answering;
            Settle();
        }

        /// <summary>
        /// The panel measured each prompt's text at its width: how many parts each takes, in the
        /// prompts' order. The step showing is kept, within the question.
        /// </summary>
        public void Measured(IReadOnlyList<int> parts)
        {
            if (draft == null) throw new InvalidOperationException("Show a question first.");
            if (parts.Count != draft.Prompts.Count) throw new ArgumentException("One count for each prompt.", nameof(parts));
            for (var prompt = 0; prompt < parts.Count; prompt++) textParts[prompt] = Math.Max(1, parts[prompt]);
            measured = true;
            Settle();
        }

        /// <summary>Steps on, or back when <paramref name="by"/> is negative, within the question.</summary>
        public void Turn(int by)
        {
            if (draft == null) return;
            Step += by;
            Settle();
        }

        /// <summary>
        /// Keeps the step within the question and, once the text is measured, marks the prompt showing
        /// as seen whole when its text's last part shows: before, no part is known to be the last.
        /// </summary>
        private void Settle()
        {
            Step = Math.Max(0, Math.Min(Step, Steps - 1));
            if (draft == null || draft.Prompts.Count == 0 || !measured) return;
            var (prompt, within) = Locate();
            if (within >= PartsOf(prompt) - 1) draft.ShownWhole(prompt);
        }

        /// <summary>The prompt the step showing belongs to, and the step within it.</summary>
        private (int Prompt, int Within) Locate()
        {
            var remaining = Step;
            for (var prompt = 0; prompt < textParts.Length; prompt++)
            {
                var steps = StepsOf(prompt);
                if (remaining < steps) return (prompt, remaining);
                remaining -= steps;
            }
            return (Math.Max(0, textParts.Length - 1), 0);
        }

        private int PartsOf(int prompt) => prompt < textParts.Length ? textParts[prompt] : 1;

        private int StepsOf(int prompt) => PartsOf(prompt) + AnswerPages(prompt) - 1;

        /// <summary>Pages of a prompt's answers: one, holding none, for a question Halcyonic can't answer.</summary>
        private int AnswerPages(int prompt)
        {
            if (draft == null || !WorkspaceText.Answerable(draft.Question)) return 1;
            return Math.Max(1, (AnswerCount(prompt) + AnswersPerPage - 1) / AnswersPerPage);
        }

        /// <summary>A prompt's labels, and Type an answer where typing is allowed.</summary>
        private int AnswerCount(int prompt)
        {
            var asked = draft!.Prompts[prompt];
            return asked.Options.Count + (asked.FreeText ? 1 : 0);
        }
    }
}
