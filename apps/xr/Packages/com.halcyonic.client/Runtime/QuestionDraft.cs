#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The person's answers to one question an agent asked, before they are sent (ADR 0022). It keeps
    /// to the rules the control plane admits answers by, so a send is never refused for its shape:
    /// every prompt answered once, chosen labels among those offered, at most one unless the prompt
    /// takes several, typed text only where the prompt allows it, and for a prompt that takes one
    /// answer, a label or text, never both. Nothing here sends anything; the person's deliberate
    /// Send answer does (<see cref="WorkspaceSteering.SendAnswer"/>).
    /// </summary>
    /// <remarks>
    /// The person must also have been shown each prompt's question whole (<see cref="ShownWhole"/>),
    /// as an approval's request must be read before it is approved: the question is the agent's
    /// words, and an answer to half of it could mean something else. Focus going to another window
    /// keeps every choice; only a send waits for a new press.
    /// </remarks>
    public sealed class QuestionDraft
    {
        private readonly List<HashSet<string>> chosen = new List<HashSet<string>>();
        private readonly List<string?> typed = new List<string?>();
        private readonly List<bool> shownWhole = new List<bool>();

        public QuestionDraft(string executionId, QuestionView question)
        {
            ExecutionId = executionId;
            Question = question;
            foreach (var _ in question.Prompts)
            {
                chosen.Add(new HashSet<string>(StringComparer.Ordinal));
                typed.Add(null);
                shownWhole.Add(false);
            }
        }

        public string ExecutionId { get; }

        public QuestionView Question { get; }

        public string QuestionId => Question.QuestionId;

        public IReadOnlyList<QuestionPrompt> Prompts => Question.Prompts;

        /// <summary>Whether this draft answers the question shown now, the same one by id.</summary>
        public bool Answers(string executionId, QuestionView? question) =>
            question != null && executionId == ExecutionId && question.QuestionId == QuestionId;

        public bool IsChosen(int prompt, string label) => chosen[prompt].Contains(label);

        /// <summary>The text typed for a prompt, or null.</summary>
        public string? Typed(int prompt) => typed[prompt];

        /// <summary>
        /// Chooses or unchooses an offered label. A prompt that takes one answer keeps only this
        /// label, and drops any typed text; one that takes several toggles it. A label that reads the same
        /// as another answer's is never chosen (<see cref="WorkspaceText.ReadsAlike"/>).
        /// </summary>
        public void Choose(int prompt, string label)
        {
            var options = Prompts[prompt].Options;
            var index = options.FindIndex(option => option.Label == label);
            if (index < 0) throw new ArgumentException("Choose a label the prompt offers.", nameof(label));
            if (WorkspaceText.ReadsAlike(Prompts[prompt], index)) return;
            var set = chosen[prompt];
            if (Prompts[prompt].Multiple)
            {
                if (!set.Remove(label)) set.Add(label);
                return;
            }
            var was = set.Contains(label);
            set.Clear();
            if (!was) set.Add(label);
            typed[prompt] = null;
        }

        /// <summary>
        /// Takes typed text for a prompt that allows it; blank text clears it. A prompt that takes one
        /// answer drops its chosen label. Returns why the text was not taken, or null: text that is not
        /// whole characters (a lone surrogate) would be refused as an invalid command, so it is
        /// refused here, in words; and words that read the same as an answer that can't be chosen, which
        /// the agent could take for it, are refused, saying Stop where <paramref name="stopOnPage"/>.
        /// </summary>
        public string? Type(int prompt, string? text, bool stopOnPage = false)
        {
            if (!Prompts[prompt].FreeText) return "This question takes only the answers shown. Choose one of them.";
            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                typed[prompt] = null;
                return null;
            }
            if (!IsWhole(trimmed!)) return "The typed answer has a character that cannot be sent. Type it again.";
            if (WorkspaceText.ReadsLikeOneThatCantBeChosen(Prompts[prompt], trimmed!)) return WorkspaceText.TypedReadsAlike(stopOnPage);
            typed[prompt] = trimmed;
            if (!Prompts[prompt].Multiple) chosen[prompt].Clear();
            return null;
        }

        /// <summary>Unchooses every label chosen for a prompt, keeping its typed text, as when the page of its answers turns.</summary>
        public void ClearChosen(int prompt) => chosen[prompt].Clear();

        /// <summary>The person has been shown the whole of a prompt's question.</summary>
        public void ShownWhole(int prompt) => shownWhole[prompt] = true;

        public bool WasShownWhole(int prompt) => shownWhole[prompt];

        /// <summary>
        /// The text size changed, so every question is laid out anew: none counts as shown whole until it is
        /// drawn whole at the new size, and the answers can't be sent until then.
        /// </summary>
        public void ReadAgain()
        {
            for (var prompt = 0; prompt < shownWhole.Count; prompt++) shownWhole[prompt] = false;
        }

        public bool IsAnswered(int prompt) => chosen[prompt].Count > 0 || typed[prompt] != null;

        /// <summary>Why it cannot be sent yet, in words, or null when it can.</summary>
        public string? Problem
        {
            get
            {
                if (!WorkspaceText.Answerable(Question)) return WorkspaceText.CannotAnswer(Question);
                var unanswered = Enumerable.Range(0, Prompts.Count).Count(prompt => !IsAnswered(prompt));
                if (unanswered > 0)
                {
                    return Prompts.Count == 1 ? "Choose or type an answer first."
                        : "Answer every question first: " + Count(Prompts.Count - unanswered) + " of " + Count(Prompts.Count) + " answered.";
                }
                if (Enumerable.Range(0, Prompts.Count).Any(prompt => !shownWhole[prompt]))
                {
                    return "Read each question to the end first. Press Next to see the rest.";
                }
                return null;
            }
        }

        /// <summary>The answers as they stand, prompt by prompt: its key, its chosen labels in the prompt's order, and its typed words.</summary>
        public IReadOnlyList<(string Key, string[] Chosen, string? Typed)> AnswersNow => Enumerable.Range(0, Prompts.Count)
            .Select(prompt => (Prompts[prompt].Key, Prompts[prompt].Options.Select(option => option.Label).Where(chosen[prompt].Contains).ToArray(), typed[prompt]))
            .ToList();

        /// <summary>
        /// Whether two takes of <see cref="AnswersNow"/> hold the same answers, compared field by field, so no
        /// words or labels, whatever characters they hold, can pass for others.
        /// </summary>
        public static bool SameAnswers(IReadOnlyList<(string Key, string[] Chosen, string? Typed)>? first, IReadOnlyList<(string Key, string[] Chosen, string? Typed)>? second)
        {
            if (first == null || second == null || first.Count != second.Count) return false;
            for (var prompt = 0; prompt < first.Count; prompt++)
            {
                if (first[prompt].Key != second[prompt].Key || first[prompt].Typed != second[prompt].Typed
                    || !first[prompt].Chosen.SequenceEqual(second[prompt].Chosen)) return false;
            }
            return true;
        }

        /// <summary>The answers to send, one per prompt, in the prompts' order; only when <see cref="Problem"/> is null.</summary>
        public List<QuestionAnswer> Build()
        {
            if (Problem is string problem) throw new InvalidOperationException(problem);
            var answers = new List<QuestionAnswer>();
            for (var prompt = 0; prompt < Prompts.Count; prompt++)
            {
                // In the order the prompt offers them, not the order they were pressed.
                var selected = Prompts[prompt].Options.Select(option => option.Label).Where(chosen[prompt].Contains).ToList();
                answers.Add(new QuestionAnswer { Key = Prompts[prompt].Key, Selected = selected, Text = typed[prompt] });
            }
            return answers;
        }

        private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static bool IsWhole(string text)
        {
            for (var index = 0; index < text.Length; index++)
            {
                if (char.IsHighSurrogate(text[index]))
                {
                    if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1])) return false;
                    index++;
                }
                else if (char.IsLowSurrogate(text[index])) return false;
            }
            return true;
        }
    }
}
