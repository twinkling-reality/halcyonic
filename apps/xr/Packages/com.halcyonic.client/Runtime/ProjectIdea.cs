#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>One of the fixed questions of Help me figure it out.</summary>
    public sealed class GuidedQuestion
    {
        public GuidedQuestion(string prompt, IReadOnlyList<string> choices, string typeLabel, string? skipLabel)
        {
            Prompt = prompt;
            Choices = choices;
            TypeLabel = typeLabel;
            SkipLabel = skipLabel;
        }

        public string Prompt { get; }

        /// <summary>Answers offered as buttons; the first-task question offers some for the kind chosen.</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>The button that opens the keyboard for an answer of the person's own.</summary>
        public string TypeLabel { get; }

        /// <summary>The button that leaves the question unanswered, or null when it needs an answer.</summary>
        public string? SkipLabel { get; }
    }

    /// <summary>
    /// What the person wants to make, before anything is sent: an idea in their own words, or the
    /// answers to a few fixed questions (Help me figure it out). Both end in the same editable recap,
    /// a project name and a first task, which the person can change until they choose Start building.
    /// Only then does the entry panel send the ordinary project, workstream and execution commands.
    /// </summary>
    /// <remarks>
    /// The fixed questions involve no model: the questions, the answers offered and the way answers
    /// become the first task are fixed here, so the same answers always give the same recap, and they
    /// are never presented as an assistant's reply. A precise idea goes straight to the recap; a vague
    /// one is shaped by the questions, or by the companion (<see cref="Companion"/>, ADR 0025), whose
    /// proposal fills the recap marked as its suggestion until the person changes it, with the person's
    /// own typed words one press away. The device keeps the idea across an app restart
    /// (<see cref="CreationDraft"/>).
    /// </remarks>
    public sealed class ProjectIdea
    {
        /// <summary>The longest project name the control plane accepts.</summary>
        public const int NameLimit = 200;

        /// <summary>The longest first task, sent as the workstream's objective and first instruction.</summary>
        public const int TaskLimit = 4000;

        /// <summary>How long a name taken from an idea's first words may be.</summary>
        public const int SuggestedNameLength = 40;

        public const int KindQuestion = 0;
        public const int AudienceQuestion = 1;
        public const int FirstStepQuestion = 2;
        public const int NameQuestion = 3;

        private static readonly string[] Kinds = { "A website", "An app", "A tool or script" };
        private static readonly string[] Audiences = { "Just me", "My team", "Other people" };

        private static readonly IReadOnlyDictionary<string, string[]> FirstSteps = new Dictionary<string, string[]>
        {
            ["A website"] = new[] { "Show one page that says what it is", "Let visitors leave their email" },
            ["An app"] = new[] { "Do its main job on one screen", "Keep a list that I can add to" },
            ["A tool or script"] = new[] { "Read a file and print a summary", "Rename files by a pattern" },
        };

        private static readonly IReadOnlyList<GuidedQuestion> Fixed = new[]
        {
            new GuidedQuestion("What kind of thing is it?", Kinds, "Something else", null),
            new GuidedQuestion("Who is it for?", Audiences, "Someone else", null),
            new GuidedQuestion("What should it do first?", Array.Empty<string>(), "Type it myself", null),
            new GuidedQuestion("What should it be called?", Array.Empty<string>(), EntryText.TypeName, "Name it later"),
        };

        private readonly string?[] answers = new string?[Fixed.Count];

        /// <param name="existingProjectId">The project to add work to, or null to create one.</param>
        /// <param name="existingProjectName">That project's name, shown as it is; never sent.</param>
        public ProjectIdea(string? existingProjectId = null, string? existingProjectName = null)
        {
            ExistingProjectId = existingProjectId;
            if (existingProjectId != null) Name = existingProjectName ?? "";
        }

        /// <summary>The fixed questions, in order.</summary>
        public static IReadOnlyList<GuidedQuestion> Questions => Fixed;

        /// <summary>The project the work goes to, or null when Start building creates one.</summary>
        public string? ExistingProjectId { get; private set; }

        /// <summary>
        /// The project the computer made for this idea, when a later step was refused or failed: the
        /// work goes there, under the name it was made with, which is no longer changed here and is
        /// what the recap and the review show from now on.
        /// </summary>
        public void ProjectMade(string projectId, string madeName)
        {
            if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("A made project has its id.", nameof(projectId));
            ExistingProjectId = projectId;
            if (!string.IsNullOrWhiteSpace(madeName)) Name = madeName;
            NameSuggested = false;
        }

        /// <summary>The project's name: typed, taken from the idea, or from the answers. Fixed for an existing project.</summary>
        public string Name { get; private set; } = "";

        /// <summary>The first task, sent as the workstream's objective and the execution's first instruction.</summary>
        public string FirstTask { get; private set; } = "";

        /// <summary>The person typed the name, so a later idea does not replace it.</summary>
        public bool NameTyped { get; private set; }

        /// <summary>Guided questions were started, so Back returns to them.</summary>
        public bool Guided { get; private set; }

        /// <summary>The question being asked, from 0, or <see cref="Questions"/>' count once all are answered.</summary>
        public int Question { get; private set; }

        /// <summary>
        /// Where its files live, chosen from what the host listed, or null while not chosen: for a new
        /// project, the folder it is created in; for an existing one, the folder it moves to.
        /// </summary>
        public ProjectFolder? Folder { get; private set; }

        /// <summary>Chooses where the project's files live, or clears the choice with null.</summary>
        public void ChooseFolder(ProjectFolder? folder) => Folder = folder;

        /// <summary>The exchange with the companion, once the person began one; null for the fixed questions or a typed idea alone.</summary>
        public CompanionExchange? Companion { get; private set; }

        /// <summary>The idea as the person typed or said it, kept when the companion's proposal takes its place in the recap.</summary>
        public string? OwnWords { get; private set; }

        /// <summary>The name in the recap is the companion's suggestion, unchanged.</summary>
        public bool NameSuggested { get; private set; }

        /// <summary>The first task in the recap is the companion's suggestion, unchanged.</summary>
        public bool TaskSuggested { get; private set; }

        /// <summary>
        /// Begins an exchange with the companion: from the idea already typed, which is its first
        /// message, or from Help me figure it out. A new exchange replaces an earlier one.
        /// </summary>
        public CompanionExchange BeginCompanion(CompanionStart start, int maxQuestions = CompanionExchange.DefaultMaxQuestions)
        {
            Companion = new CompanionExchange(start, maxQuestions);
            if (start == CompanionStart.Idea && OwnWords != null) Companion.Say(OwnWords);
            return Companion;
        }

        /// <summary>Takes back an exchange the device kept, as it was.</summary>
        public void RestoreCompanion(CompanionExchange? exchange) => Companion = exchange;

        /// <summary>
        /// The companion's proposal into the recap, marked as its suggestion: its first task, and its
        /// name unless the person typed one or the work goes to an existing project. What the person
        /// typed stays in <see cref="OwnWords"/>.
        /// </summary>
        public void UseProposal(CompanionProposal proposal)
        {
            var task = (proposal.FirstTask ?? "").Trim();
            if (task.Length == 0) return;
            FirstTask = task;
            TaskSuggested = true;
            var name = (proposal.ProjectName ?? "").Trim();
            if (ExistingProjectId == null && !NameTyped && name.Length > 0)
            {
                Name = name;
                NameSuggested = true;
            }
        }

        /// <summary>Puts the person's own words back as the first task, and a name from them in place of a suggested one.</summary>
        public bool UseOwnWords()
        {
            if (OwnWords == null) return false;
            FirstTask = OwnWords;
            TaskSuggested = false;
            if (NameSuggested)
            {
                Name = NameFrom(OwnWords);
                NameSuggested = false;
            }
            return true;
        }

        /// <summary>The recap exists: an idea was typed or every question was answered.</summary>
        public bool HasRecap => FirstTask.Length > 0;

        /// <summary>The answer given to a question, or null.</summary>
        public string? AnswerTo(int question) => answers[question];

        /// <summary>The answers offered as buttons for the question being asked.</summary>
        public IReadOnlyList<string> Choices
        {
            get
            {
                if (Question >= Fixed.Count) return Array.Empty<string>();
                if (Question != FirstStepQuestion) return Fixed[Question].Choices;
                return answers[KindQuestion] is string kind && FirstSteps.TryGetValue(kind, out var steps) ? steps : Array.Empty<string>();
            }
        }

        /// <summary>Takes an idea in the person's own words as the first task, and names the project from it unless they typed a name.</summary>
        public void UseIdea(string idea)
        {
            FirstTask = (idea ?? "").Trim();
            OwnWords = FirstTask.Length == 0 ? null : FirstTask;
            TaskSuggested = false;
            if (!NameTyped && ExistingProjectId == null)
            {
                Name = NameFrom(FirstTask);
                NameSuggested = false;
            }
        }

        /// <summary>Starts the fixed questions from the first, keeping any answers already given.</summary>
        public void BeginGuide()
        {
            Guided = true;
            Question = 0;
            Unchoose();
        }

        /// <summary>
        /// The answer chosen for the fixed question being asked, not yet given (ADR 0026): one offered,
        /// or the person's own words; null while none is. Choosing only lights an answer, and
        /// <see cref="NextQuestion"/> gives it.
        /// </summary>
        public string? GuideChosen { get; private set; }

        /// <summary>The person's own answer to the question being asked, typed or heard; it stays while an offered one is chosen.</summary>
        public string? GuideWritten { get; private set; }

        /// <summary><see cref="GuideWritten"/> is what the computer heard, for the person to check.</summary>
        public bool GuideWrittenHeard { get; private set; }

        /// <summary>The question's skip is chosen, on a question that can be skipped.</summary>
        public bool GuideSkipChosen { get; private set; }

        /// <summary>
        /// The answer that stands for the question being asked: the one chosen, else, coming back to it,
        /// the one given before; null for none, or while the skip is chosen.
        /// </summary>
        public string? GuideAnswer => GuideSkipChosen || Question >= Fixed.Count ? null : GuideChosen ?? answers[Question];

        /// <summary>Next question can be pressed: an answer stands for the question being asked, or its skip is chosen.</summary>
        public bool CanGoOn => Question < Fixed.Count && (GuideSkipChosen || GuideAnswer != null);

        /// <summary>The question being asked is the last one asked: the name, or for a project that exists, its first step.</summary>
        public bool LastQuestion => Question == Fixed.Count - 1 || (ExistingProjectId != null && Question == NameQuestion - 1);

        /// <summary>Lights one of the answers offered for the question being asked; nothing is given yet.</summary>
        public bool ChooseGuideAnswer(string? answer)
        {
            if (Question >= Fixed.Count || answer == null || !Choices.Contains(answer)) return false;
            GuideChosen = answer;
            GuideSkipChosen = false;
            return true;
        }

        /// <summary>Keeps the person's own answer, typed or <paramref name="heard"/>, and lights it; a blank or overlong one is refused.</summary>
        public bool WriteGuideAnswer(string? text, bool heard = false)
        {
            var words = (text ?? "").Trim();
            if (Question >= Fixed.Count || words.Length == 0 || words.Length > TaskLimit) return false;
            GuideWritten = words;
            GuideWrittenHeard = heard;
            GuideChosen = words;
            GuideSkipChosen = false;
            return true;
        }

        /// <summary>Lights the skip, on a question that can be skipped.</summary>
        public bool ChooseGuideSkip()
        {
            if (Question >= Fixed.Count || Fixed[Question].SkipLabel == null) return false;
            GuideChosen = null;
            GuideSkipChosen = true;
            return true;
        }

        /// <summary>Next question: gives the answer that stands, or the skip, and moves on; refused while none is chosen.</summary>
        public bool NextQuestion()
        {
            if (!CanGoOn) return false;
            return GuideSkipChosen ? Skip() : Answer(GuideAnswer);
        }

        private void Unchoose()
        {
            GuideChosen = null;
            GuideWritten = null;
            GuideWrittenHeard = false;
            GuideSkipChosen = false;
        }

        /// <summary>
        /// Answers the question being asked, with a choice or typed text, and moves to the next. A
        /// blank answer is refused, except to a question that can be skipped. Once the last question
        /// is answered, the recap is composed from the answers.
        /// </summary>
        public bool Answer(string? answer)
        {
            if (Question >= Fixed.Count) return false;
            var text = (answer ?? "").Trim();
            if (text.Length == 0 && Fixed[Question].SkipLabel == null) return false;
            // A new kind changes the first steps offered, so an earlier first step no longer fits.
            if (Question == KindQuestion && answers[KindQuestion] != text) answers[FirstStepQuestion] = null;
            answers[Question] = text.Length == 0 ? null : text;
            Unchoose();
            Question++;
            // The name is asked only when creating a project.
            if (Question == NameQuestion && ExistingProjectId != null) Question++;
            if (Question >= Fixed.Count) Compose();
            return true;
        }

        /// <summary>Leaves a question that can be skipped unanswered.</summary>
        public bool Skip() => Question < Fixed.Count && Fixed[Question].SkipLabel != null && Answer(null);

        /// <summary>Returns to the previous question. Returns false at the first.</summary>
        public bool Back()
        {
            if (Question == 0) return false;
            Unchoose();
            Question--;
            if (Question == NameQuestion && ExistingProjectId != null) Question--;
            return true;
        }

        /// <summary>Changes the project's name in the recap; a blank name is refused.</summary>
        public bool Rename(string? name)
        {
            if (ExistingProjectId != null) return false;
            var text = (name ?? "").Trim();
            if (text.Length == 0) return false;
            Name = text;
            NameTyped = true;
            NameSuggested = false;
            return true;
        }

        /// <summary>Changes the first task in the recap; a blank task is refused.</summary>
        public bool Rewrite(string? task)
        {
            var text = (task ?? "").Trim();
            if (text.Length == 0) return false;
            FirstTask = text;
            TaskSuggested = false;
            return true;
        }

        /// <summary>Why Start building cannot go ahead yet, or null.</summary>
        public string? Problem
        {
            get
            {
                if (ExistingProjectId == null && (Name.Length == 0 || Name.Length > NameLimit)) return EntryText.NameRule;
                if (FirstTask.Length == 0) return EntryText.DescribeTask;
                if (FirstTask.Length > TaskLimit) return EntryText.ShortenTask;
                return null;
            }
        }

        /// <summary>The idea as the device keeps it (<see cref="CreationDraft"/>), without where or for which computer.</summary>
        public CreationDraft Keep() => new CreationDraft
        {
            ExistingProjectId = ExistingProjectId,
            Name = Name,
            NameTyped = NameTyped,
            NameSuggested = NameSuggested,
            FirstTask = FirstTask,
            TaskSuggested = TaskSuggested,
            OwnWords = OwnWords,
            Guided = Guided,
            Question = Question,
            Answers = answers.ToList(),
            Folder = Folder == null
                ? null
                : new KeptFolder { RootPath = Folder.RootPath, RootName = Folder.RootName, FolderName = Folder.FolderName, IsNew = Folder.IsNew },
            Companion = Companion == null
                ? null
                : new KeptExchange
                {
                    Start = Companion.Start,
                    MaxQuestions = Companion.MaxQuestions,
                    Turns = Companion.Turns.ToList(),
                    Answer = Companion.Written,
                    AnswerHeard = Companion.WrittenHeard,
                },
        };

        /// <summary>
        /// The idea a device kept, as it was. A new project the Mac already made comes back as a task
        /// for that project, its folder already bound. What can't be an idea is refused with an
        /// <see cref="ArgumentException"/>; a part that can't be kept, such as a folder name the host
        /// would refuse, is left out.
        /// </summary>
        public static ProjectIdea Restore(CreationDraft kept)
        {
            if (kept == null) throw new ArgumentNullException(nameof(kept));
            foreach (var id in new[] { kept.ExistingProjectId, kept.MadeProjectId, kept.MadeWorkstreamId })
            {
                if (id != null && !CreationDraft.IsId(id)) throw new ArgumentException("A kept id is not one the computer gives.", nameof(kept));
            }
            var projectId = kept.MadeProjectId ?? kept.ExistingProjectId;
            var idea = projectId == null
                ? new ProjectIdea()
                : new ProjectIdea(projectId, kept.MadeProjectId != null ? kept.MadeProjectName ?? kept.Name : kept.Name);
            if (projectId == null)
            {
                idea.Name = Kept(kept.Name, NameLimit);
                idea.NameTyped = kept.NameTyped;
                idea.NameSuggested = kept.NameSuggested;
            }
            idea.FirstTask = Kept(kept.FirstTask, TaskLimit);
            idea.TaskSuggested = kept.TaskSuggested && idea.FirstTask.Length > 0;
            idea.OwnWords = string.IsNullOrWhiteSpace(kept.OwnWords) ? null : Kept(kept.OwnWords!, TaskLimit);
            idea.Guided = kept.Guided;
            if ((kept.Answers?.Count ?? 0) > idea.answers.Length) throw new ArgumentException("More answers were kept than there are questions.", nameof(kept));
            for (var index = 0; index < (kept.Answers?.Count ?? 0); index++)
            {
                var answer = kept.Answers![index];
                idea.answers[index] = string.IsNullOrWhiteSpace(answer) ? null : Kept(answer!, TaskLimit);
            }
            idea.Question = Math.Max(0, Math.Min(Fixed.Count, kept.Question));
            if (idea.Question == NameQuestion && projectId != null) idea.Question++;
            // A project already made has its folder; another choice would move it.
            if (kept.MadeProjectId == null && kept.Folder is KeptFolder folder)
            {
                idea.Folder = ProjectFolder.Restore(folder.RootPath, folder.RootName, folder.FolderName, folder.IsNew);
            }
            if (kept.Companion is KeptExchange exchange && projectId == null)
            {
                if (!Enum.IsDefined(typeof(CompanionStart), exchange.Start)) throw new ArgumentException("A kept exchange began in no known way.", nameof(kept));
                idea.Companion = CompanionExchange.Restore(exchange.Start, exchange.MaxQuestions, exchange.Turns ?? new List<CompanionExchangeTurn>());
                // An answer not yet sent comes back chosen, as it was left; one that could not be sent now is dropped.
                if (exchange.Answer != null) idea.Companion.Write(Kept(exchange.Answer, CompanionExchange.PersonLimit), exchange.AnswerHeard);
            }
            return idea;
        }

        /// <summary>
        /// Kept text as it was, never cut: one longer than what could be sent shows the recap's own
        /// problem until the person shortens it, and one far past that is no draft at all.
        /// </summary>
        private static string Kept(string? text, int limit)
        {
            var value = (text ?? "").Trim();
            if (value.Length > limit * 4) throw new ArgumentException("Kept text is far longer than anything the headset makes.");
            return value;
        }

        /// <summary>
        /// A name from an idea's first words: its first line, cut at a word within
        /// <see cref="SuggestedNameLength"/> characters, without closing punctuation.
        /// </summary>
        public static string NameFrom(string idea)
        {
            var line = (idea ?? "").Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var name = new StringBuilder();
            foreach (var word in words)
            {
                if (name.Length > 0 && name.Length + 1 + word.Length > SuggestedNameLength) break;
                if (name.Length == 0 && word.Length > SuggestedNameLength)
                {
                    var end = char.IsHighSurrogate(word[SuggestedNameLength - 1]) ? SuggestedNameLength - 1 : SuggestedNameLength;
                    name.Append(word, 0, end);
                    break;
                }
                if (name.Length > 0) name.Append(' ');
                name.Append(word);
            }
            var text = name.ToString().TrimEnd('.', ',', ';', ':', '!', '?');
            return text.Length == 0 ? "New project" : text;
        }

        /// <summary>
        /// The recap from the answers: for example "Make a website for my team. First, show one page
        /// that says what it is." Typed answers go in as typed.
        /// </summary>
        /// <summary>
        /// The first task is the one composed from the fixed answers, unchanged since: its Change walks
        /// the questions again rather than letting the person's own words be composed over.
        /// </summary>
        public bool TaskFromAnswers => Guided && Question >= Fixed.Count && FirstTask.Length > 0 && FirstTask == ComposedTask();

        private void Compose()
        {
            FirstTask = ComposedTask();
            TaskSuggested = false;
            if (ExistingProjectId != null || NameTyped) return;
            NameSuggested = false;
            Name = answers[NameQuestion] ?? (answers[KindQuestion] ?? "something") switch
            {
                "A website" => "New website",
                "An app" => "New app",
                "A tool or script" => "New tool",
                _ => "New project",
            };
        }

        /// <summary>The first task the fixed answers make.</summary>
        private string ComposedTask()
        {
            var kind = answers[KindQuestion] ?? "something";
            var audience = answers[AudienceQuestion] switch
            {
                "Just me" => "me",
                "My team" => "my team",
                "Other people" => "other people",
                null => null,
                var typed => typed,
            };
            var first = answers[FirstStepQuestion];
            // Only the fixed answers are made lowercase; what the person typed goes in as typed.
            var task = new StringBuilder("Make ").Append(Kinds.Contains(kind) ? LowerFirst(kind) : kind);
            if (audience != null) task.Append(" for ").Append(audience);
            task.Append('.');
            if (first != null)
            {
                var offered = FirstSteps.Values.Any(steps => steps.Contains(first));
                task.Append(" First, ").Append(offered ? LowerFirst(first) : first.TrimEnd('.')).Append('.');
            }
            return task.ToString();
        }

        private static string LowerFirst(string text) => char.ToLowerInvariant(text[0]) + text.Substring(1);
    }
}
