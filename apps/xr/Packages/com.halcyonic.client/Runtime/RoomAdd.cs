#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Where a task's own work stands against its project (ADR 0045; the addendum's A13).</summary>
    public enum RoomAddState
    {
        /// <summary>Nothing to say: no copy of its own, none read yet, or nothing to add.</summary>
        None,

        /// <summary>Work of the task's own waits to be added: its count a slot of the result, Add to Project the one main action.</summary>
        Ready,

        /// <summary>Its work was added, and nothing newer waits: "Added to Project", Undo.</summary>
        Added,

        /// <summary>The project changed the same files: a status, "Not Added", their names as a list, Let Agent Fix.</summary>
        Conflict,
    }

    /// <summary>
    /// What the headset says of a task's own copy (ADR 0045; the addendum's A13 in A17's language, the lead's words of
    /// 2339Z, 0000Z and 0417Z): the files waiting to be added as a slot of the result, the review before the add as a
    /// card naming the task and listing its files, what was added as a status, and the files the project changed too as
    /// a status over their list. Never a fact joined to another on a dot. File names are an agent's words: they stand as
    /// theirs, never as Halcyonic's. A person never reads the mechanism (copy, branch, merge, conflict). Nothing here
    /// sends: the command is the file's to build.
    /// </summary>
    public sealed class RoomAdd
    {
        private RoomAdd(RoomAddState state, long count, IReadOnlyList<string> paths, IReadOnlyList<ConflictedFile> both)
        {
            State = state;
            Count = count;
            Paths = paths;
            Both = both;
        }

        public RoomAddState State { get; }

        /// <summary>How many files: waiting to be added, or changed in both. The whole count, even where the list holds fewer.</summary>
        public long Count { get; }

        /// <summary>The paths waiting to be added, as the copy lists them.</summary>
        public IReadOnlyList<string> Paths { get; }

        /// <summary>The files changed in both, as the copy lists them.</summary>
        public IReadOnlyList<ConflictedFile> Both { get; }

        /// <summary>The words of the main action that opens the review, of the review's confirming action, and of the ways on.</summary>
        public const string AddToProject = "Add to Project";
        public const string LetAgentFix = "Let Agent Fix";
        public const string Undo = "Undo";
        public const string AddedWords = "Added to Project";
        public const string UndoLine = "You can undo this.";
        public const string NotAdded = "Not Added";

        /// <summary>What the person's task copy says now; <see cref="RoomAddState.None"/> while the agent works, where nothing is settled.</summary>
        public static RoomAdd Of(WorkstreamCopy? copy, bool working)
        {
            var none = new RoomAdd(RoomAddState.None, 0, new string[0], new ConflictedFile[0]);
            if (working || copy is not OwnCopy own) return none;
            if (own.Conflict is ConflictedFiles both && both.Total > 0)
                return new RoomAdd(RoomAddState.Conflict, both.Total, new string[0], both.Files.ToList());
            if (own.ToAdd is PathList waiting && waiting.Total > 0)
                return new RoomAdd(RoomAddState.Ready, waiting.Total, waiting.Paths.ToList(), new ConflictedFile[0]);
            if (own.LastAddedAt != null) return new RoomAdd(RoomAddState.Added, 0, new string[0], new ConflictedFile[0]);
            return none;
        }

        /// <summary>"1 File", "4 Files".</summary>
        public static string Files(long count) => count + (count == 1 ? " File" : " Files");

        /// <summary>The result's slot where work waits: the files' glyph over "4 Files". Null where nothing waits.</summary>
        public RoomSlot? Slot => State == RoomAddState.Ready ? new RoomSlot(RoomKind.ToAdd, LineWords.Ours(Files(Count))) : (RoomSlot?)null;

        /// <summary>
        /// The review before the add (the addendum's A17): the task's name as its title, where its work goes and that it can
        /// be undone as its line ("Into Kite Run. You can undo this."), and the files waiting as its list, each its own name.
        /// The confirming action names the act, <see cref="AddToProject"/>; the title never repeats it.
        /// </summary>
        public RoomCard ReviewCard(LineWords task, string? project)
        {
            var line = project == null ? LineWords.Ours(UndoLine) : LineWords.Ours("Into ") + LineWords.TheirName(project) + LineWords.Ours(". " + UndoLine);
            return new RoomCard(task, line, list: Paths.Select(path => new RoomListRow(RoomKind.ToAdd, LineWords.Theirs(path), mono: true)).ToList(),
                more: Paths.Count < Count ? "And " + Files(Count - Paths.Count) + " More" : null);
        }

        /// <summary>The status once its work was added and nothing newer waits; null otherwise.</summary>
        public RoomCard? AddedCard => State == RoomAddState.Added ? new RoomCard(status: new RoomCardStatus(AddedWords, UndoLine, RoomTone.Good)) : null;

        /// <summary>
        /// The files the project changed too (the addendum's A17): "Not Added" over how many of them, then each as a row, its
        /// name, the two passing arrows, and its kind as the quiet note. Null where nothing conflicts.
        /// </summary>
        public RoomCard? ConflictCard
        {
            get
            {
                if (State != RoomAddState.Conflict) return null;
                var line = "The project changed " + (Count == 1 ? "this file" : Files(Count).ToLowerInvariant()) + " too";
                return new RoomCard(status: new RoomCardStatus(NotAdded, line, RoomTone.Bad),
                    list: Both.Select(file => new RoomListRow(file.Kind == ConflictKind.Deleted ? RoomKind.Removed : RoomKind.Conflict, LineWords.Theirs(file.Path), KindWords(file.Kind), mono: true)).ToList(),
                    more: Both.Count < Count ? "And " + Files(Count - Both.Count) + " More" : null);
            }
        }

        /// <summary>The review's confirming action, named for its act.</summary>
        public string ConfirmWords => AddToProject;

        /// <summary>The sentence while the add is sent and not yet confirmed: "Adding 4 Files".</summary>
        public string SendingWords => "Adding " + Files(Count);

        /// <summary>What a refused add says, by the refusal's code: "Not Added" over why; null for a code the review does not answer for.</summary>
        public static RoomCard? RefusedCard(string? code) => code switch
        {
            "nothing_to_add" => new RoomCard(status: new RoomCardStatus(NotAdded, "Nothing new to add", RoomTone.Quiet)),
            "folder_changed" => new RoomCard(status: new RoomCardStatus(NotAdded, "The project just changed", RoomTone.Bad)),
            _ => null,
        };

        /// <summary>A conflicted file's kind in plain words, as a row's note: changed in both, a picture or data file, removed in the project.</summary>
        public static string? KindWords(ConflictKind kind) => kind switch
        {
            ConflictKind.Text => "Edited in Both",
            ConflictKind.Binary => "Picture or Data File",
            ConflictKind.Deleted => "Removed in Project",
            _ => null,
        };
    }
}
