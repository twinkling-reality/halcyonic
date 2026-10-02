#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json;

namespace Halcyonic.Client
{
    /// <summary>
    /// One Create draft as this device keeps it across an app restart (ADR 0025): the person's idea or
    /// answers, the recap, the exchange with the companion, the choices made, and what the Mac already
    /// made of it, for the computer whose journal it was made with. It holds the person's own words
    /// and the companion's, and never a credential; it lives only in the app's private storage.
    /// </summary>
    public sealed class CreationDraft
    {
        /// <summary>How long a draft is kept without a change (the owner's choice, ADR 0025).</summary>
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        /// <summary>The journal of the computer the draft was made with: drafts never cross to another.</summary>
        [JsonProperty("journal_id")]
        public string JournalId { get; set; } = "";

        /// <summary>A project's id when the draft adds a task to it, or empty for a new project.</summary>
        [JsonProperty("place")]
        public string Place { get; set; } = "";

        [JsonProperty("changed_at")]
        public DateTimeOffset ChangedAt { get; set; }

        [JsonProperty("existing_project_id")]
        public string? ExistingProjectId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("name_typed")]
        public bool NameTyped { get; set; }

        [JsonProperty("name_suggested")]
        public bool NameSuggested { get; set; }

        [JsonProperty("first_task")]
        public string FirstTask { get; set; } = "";

        [JsonProperty("task_suggested")]
        public bool TaskSuggested { get; set; }

        [JsonProperty("own_words")]
        public string? OwnWords { get; set; }

        [JsonProperty("guided")]
        public bool Guided { get; set; }

        [JsonProperty("question")]
        public int Question { get; set; }

        [JsonProperty("answers")]
        public List<string?> Answers { get; set; } = new List<string?>();

        [JsonProperty("folder")]
        public KeptFolder? Folder { get; set; }

        [JsonProperty("companion")]
        public KeptExchange? Companion { get; set; }

        /// <summary>A project the Mac made for this draft before its work started, so Start building never makes it twice.</summary>
        [JsonProperty("made_project_id")]
        public string? MadeProjectId { get; set; }

        /// <summary>The project's name as it was made, shown when the draft comes back as a task for it.</summary>
        [JsonProperty("made_project_name")]
        public string? MadeProjectName { get; set; }

        /// <summary>A task the Mac made for this draft before its work started.</summary>
        [JsonProperty("made_workstream_id")]
        public string? MadeWorkstreamId { get; set; }

        /// <summary>The agent app chosen, by id, chosen again only if the Mac still offers it.</summary>
        [JsonProperty("runtime_id")]
        public string? RuntimeId { get; set; }

        /// <summary>The model chosen, by the runtime's reference, chosen again only if it is still listed and runs on the Mac.</summary>
        [JsonProperty("model_ref")]
        public string? ModelRef { get; set; }

        /// <summary>
        /// Keeps <paramref name="idea"/> as it is now, with what <paramref name="sequence"/> already made,
        /// or null when there is nothing worth keeping: no recap, no questions begun, no exchange, or work
        /// already started.
        /// </summary>
        public static CreationDraft? Of(string journalId, string place, ProjectIdea idea, BuildSequence? sequence, NewWorkDraft draft,
            DateTimeOffset now)
        {
            if (sequence?.Started == true) return null;
            if (!idea.HasRecap && !idea.Guided && idea.Companion == null) return null;
            var kept = idea.Keep();
            kept.JournalId = journalId;
            kept.Place = place;
            kept.ChangedAt = now;
            kept.RuntimeId = draft.Runtime?.RuntimeId;
            kept.ModelRef = draft.Model?.ModelRef;
            // Only what the Mac confirmed it made; a command still on its way is kept apart, by its id.
            if (idea.ExistingProjectId == null && sequence?.ProjectId != null && sequence.Steps.Any(step =>
                    step.Kind == BuildStepKind.CreateProject && step.Status == BuildStepStatus.Confirmed))
            {
                kept.MadeProjectId = sequence.ProjectId;
                kept.MadeProjectName = idea.Name;
            }
            if (sequence?.WorkstreamId != null) kept.MadeWorkstreamId = sequence.WorkstreamId;
            return kept;
        }

        /// <summary>
        /// The idea again, or null when what was kept can't make one. A new project the Mac already made
        /// comes back as a task for that project, which is what it now is.
        /// </summary>
        public ProjectIdea? ToIdea()
        {
            try
            {
                return ProjectIdea.Restore(this);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The project the work belongs to after a restart: the one the Mac made, or the one the draft added to.</summary>
        public string? ProjectAfterRestart => MadeProjectId ?? ExistingProjectId;

        public bool ExpiredAt(DateTimeOffset now) => now - ChangedAt > Retention;
    }

    /// <summary>A folder choice as kept: the root and the name exactly as the host listed them.</summary>
    public sealed class KeptFolder
    {
        [JsonProperty("root_path")]
        public string RootPath { get; set; } = "";

        [JsonProperty("root_name")]
        public string RootName { get; set; } = "";

        [JsonProperty("folder_name")]
        public string? FolderName { get; set; }

        [JsonProperty("is_new")]
        public bool IsNew { get; set; }
    }

    /// <summary>An exchange with the companion as kept: how it began and every turn, in the contract's shape.</summary>
    public sealed class KeptExchange
    {
        [JsonProperty("start")]
        public CompanionStart Start { get; set; }

        [JsonProperty("max_questions")]
        public int MaxQuestions { get; set; } = CompanionExchange.DefaultMaxQuestions;

        [JsonProperty("turns")]
        public List<CompanionExchangeTurn> Turns { get; set; } = new List<CompanionExchangeTurn>();
    }

    /// <summary>Where the device keeps its Create drafts. The Unity layer chooses the place: app-internal storage on the headset.</summary>
    public interface ICreationDraftStore
    {
        /// <summary>Every draft kept, for every computer; an unreadable file reads as none.</summary>
        IReadOnlyList<CreationDraft> Load();

        /// <summary>Replaces what is kept with <paramref name="drafts"/>; none removes the file.</summary>
        void Save(IReadOnlyList<CreationDraft> drafts);
    }

    /// <summary>The drafts as one JSON file, replaced whole on every save, as the pairing is.</summary>
    public sealed class FileCreationDraftStore : ICreationDraftStore
    {
        /// <summary>The largest file read: far more than twenty drafts with full exchanges need.</summary>
        public const long MaxBytes = 4 * 1024 * 1024;

        private readonly string path;

        public FileCreationDraftStore(string path)
        {
            this.path = path;
        }

        public IReadOnlyList<CreationDraft> Load()
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return Array.Empty<CreationDraft>();
                var file = JsonConvert.DeserializeObject<DraftFile>(File.ReadAllText(path), HalcyonicJson.Tolerant);
                if (file == null || file.Version != DraftFile.CurrentVersion) return Array.Empty<CreationDraft>();
                return file.Drafts.Where(draft => draft != null).ToList();
            }
            catch (Exception error) when (error is JsonException || error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is InvalidOperationException)
            {
                return Array.Empty<CreationDraft>();
            }
        }

        public void Save(IReadOnlyList<CreationDraft> drafts)
        {
            if (drafts.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = path + ".new";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(new DraftFile { Drafts = drafts.ToList() }, Formatting.None, HalcyonicJson.Tolerant));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private sealed class DraftFile
        {
            public const int CurrentVersion = 1;

            [JsonProperty("version")]
            public int Version { get; set; } = CurrentVersion;

            [JsonProperty("drafts")]
            public List<CreationDraft> Drafts { get; set; } = new List<CreationDraft>();
        }
    }

    /// <summary>
    /// The drafts of one computer kept in a store with the others' untouched: what to resume when its
    /// journal connects, and what to write back after a change. Expired drafts are dropped as they are read.
    /// </summary>
    public sealed class CreationDrafts
    {
        private readonly ICreationDraftStore store;
        private readonly Func<DateTimeOffset> now;
        private string? lastWritten;

        public CreationDrafts(ICreationDraftStore store, Func<DateTimeOffset>? now = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>The drafts kept for <paramref name="journalId"/>, newest first, unexpired.</summary>
        public IReadOnlyList<CreationDraft> For(string journalId)
        {
            var at = now();
            return store.Load()
                .Where(draft => draft.JournalId == journalId && !draft.ExpiredAt(at))
                .OrderByDescending(draft => draft.ChangedAt)
                .ToList();
        }

        /// <summary>
        /// Keeps <paramref name="current"/> as this computer's drafts, in place of what was kept for it,
        /// and leaves other computers' unexpired drafts as they were. Writes only when something changed.
        /// Returns whether it wrote.
        /// </summary>
        public bool Keep(string journalId, IEnumerable<CreationDraft> current)
        {
            var at = now();
            var mine = current.Where(draft => draft != null).ToList();
            var others = store.Load().Where(draft => draft.JournalId != journalId && !draft.ExpiredAt(at));
            var all = others.Concat(mine).ToList();
            // Compared without the times, so an unchanged draft is not written again.
            var shape = JsonConvert.SerializeObject(all.Select(draft => (draft.JournalId, draft.Place, Content: Content(draft))), HalcyonicJson.Tolerant);
            if (shape == lastWritten) return false;
            store.Save(all);
            lastWritten = shape;
            return true;
        }

        private static string Content(CreationDraft draft)
        {
            var changedAt = draft.ChangedAt;
            draft.ChangedAt = default;
            var content = JsonConvert.SerializeObject(draft, HalcyonicJson.Tolerant);
            draft.ChangedAt = changedAt;
            return content;
        }
    }
}
