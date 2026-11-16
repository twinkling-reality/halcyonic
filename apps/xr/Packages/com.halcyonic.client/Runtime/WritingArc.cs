#nullable enable
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where one file of Watch It Work stands (the merged SHAPE of 2026-11-16, XR's 03:06Z): its text changing as the turn
    /// runs, settled at the turn's end, or not shown, and why. The words for each come from AUDIT2's lines (WorkspaceText),
    /// never from these names.
    /// </summary>
    public enum WritingState
    {
        Changing,
        Settled,
        KeptPrivate,
        NotText,
        TooLarge,
        Unreadable,
    }

    /// <summary>
    /// One file's panel in Watch It Work, as the headset's side hands it to the kit's view: data only, filled by WatchBuild
    /// (XR) from the host's observed list and its reads. Lines and Lit are the window's rows after wrapping at the reading
    /// measure (FIELD's 12 rows), so Lit indexes those rows.
    /// </summary>
    public sealed class WritingPanel
    {
        public WritingPanel(string path, LineWords name, LineWords? folder, WritingState state, IReadOnlyList<string> lines, IReadOnlyList<int> lit,
            long? size, bool isNew)
        {
            Path = path;
            Name = name;
            Folder = folder;
            State = state;
            Lines = lines;
            Lit = lit;
            Size = size;
            New = isNew;
        }

        /// <summary>The file's path in the project's folder, as the host named it: the panel's key.</summary>
        public string Path { get; }

        /// <summary>The file's own name, the path's last part, theirs.</summary>
        public LineWords Name { get; }

        /// <summary>The folders it stands in, theirs; null at the folder's top.</summary>
        public LineWords? Folder { get; }

        public WritingState State { get; }

        /// <summary>The window's rows, wrapped at the reading measure; empty where the text is not shown.</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>The rows of <see cref="Lines"/> a later read changed, by index; empty on a first read.</summary>
        public IReadOnlyList<int> Lit { get; }

        /// <summary>The file's size in bytes, as the read answered, for a file too large to show; null otherwise.</summary>
        public long? Size { get; }

        /// <summary>The file did not exist before the turn.</summary>
        public bool New { get; }
    }

    /// <summary>
    /// Watch It Work's arc as the kit draws it: at most three panels, newest last; every file named this turn as rows, in the
    /// result's Changes order, the panels' files included (AUDIT's MUST 3), so the settle renames and reorders nothing; and the
    /// host's count of the files that changed (paths_total), never more than it saw.
    /// </summary>
    public sealed class WritingArc
    {
        /// <summary>The most panels standing at once.</summary>
        public const int MostPanels = 3;

        public WritingArc(IReadOnlyList<WritingPanel> panels, IReadOnlyList<WritingPanel> soFar, int count)
        {
            Panels = panels;
            SoFar = soFar;
            Count = count;
        }

        public IReadOnlyList<WritingPanel> Panels { get; }

        public IReadOnlyList<WritingPanel> SoFar { get; }

        public int Count { get; }
    }
}
