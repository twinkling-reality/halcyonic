#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Watch It Work, the headset's half (lead 2026-11-16 03:03Z; XR's SHAPE 03:06Z, merged with FIELD's and CORE's): which
    /// files a running turn is changing, as the host itself observes them (<see cref="ExecutionView.Writing"/>, never an agent
    /// app's report), and what each looks like now, read through the project's one file reader for that task. It decides
    /// what to read and when, keeps each file's last read, and lays the panels and the list FIELD's arc draws
    /// (<see cref="WritingArc"/>). Nothing is invented: a panel shows only lines the host read, and stands only once its
    /// first read is in (AUDIT's MUST 1); until then the file is a finished row of the list.
    /// </summary>
    public sealed class WatchBuild
    {
        /// <summary>The rows of code a panel shows (FIELD's window, at the Changes reading's measure).</summary>
        public const int WindowRows = 12;

        /// <summary>
        /// The title of the list at the arc's left, the files that stepped back from it (AUDIT2's 08:12Z): with the count line over it,
        /// "Changed 3 Files So Far", the two read as one. The one constant the headset and its renders draw.
        /// </summary>
        public const string ListWords = "Files So Far";

        /// <summary>The least time between two reads of one file, in seconds (CORE's host coalesces at the same pace).</summary>
        public const double ReadEverySeconds = 2;

        /// <summary>The most reads a second for one task, as the host answers (CORE's W4 bound; past it the host refuses).</summary>
        public const int ReadsPerSecond = 4;

        /// <summary>
        /// The most line edits a read is diffed for; past it the read is not lit at all rather than lit wrongly (a file
        /// rewritten whole says nothing by its lit rows).
        /// </summary>
        public const int MostEdits = 400;

        private readonly Func<string, IReadOnlyList<string>> wrap;
        private readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>(StringComparer.Ordinal);
        private readonly List<string> named = new List<string>();
        private readonly Queue<double> reads = new Queue<double>();
        private string? at;
        private int count;
        private bool settled;
        private IReadOnlyList<string>? order;

        /// <param name="wrap">One line of a file as the rows it lays at the reading measure (FIELD's view measures them).</param>
        public WatchBuild(Func<string, IReadOnlyList<string>> wrap)
        {
            this.wrap = wrap ?? throw new ArgumentNullException(nameof(wrap));
        }

        /// <summary>
        /// Whether Watch It Work is offered on this task: only on live work whose turn runs and whose host observes the folder
        /// it writes (a recorded run and the demonstration hold no file text: the lead's 03:52Z ruling), never filled with
        /// invented code.
        /// </summary>
        public static bool Offered(ExecutionView? execution, bool live) =>
            live && execution != null && execution.Writing != null
            && execution.Status is ExecutionStatus.Running or ExecutionStatus.Verifying;

        /// <summary>
        /// The files to read now, newest first, from the host's latest list: only the newest <see cref="WritingArc.MostPanels"/>
        /// (the panels' files), each read again only when the list moved since its last read and at least
        /// <see cref="ReadEverySeconds"/> after it, and never more than <see cref="ReadsPerSecond"/> in any second. Each
        /// path returned counts as asked; answer it with <see cref="Read"/> or <see cref="Refused"/>.
        /// </summary>
        public IReadOnlyList<string> Due(ExecutionWritingView? writing, double now)
        {
            if (writing == null || settled) return Array.Empty<string>();
            at = writing.At;
            count = (int)Math.Min(int.MaxValue, Math.Max(writing.PathsTotal, writing.Paths.Count));
            for (var index = writing.Paths.Count - 1; index >= 0; index--)
            {
                var path = writing.Paths[index];
                if (!named.Contains(path)) named.Add(path);
            }
            while (reads.Count > 0 && now - reads.Peek() >= 1) reads.Dequeue();
            var due = new List<string>();
            foreach (var path in writing.Paths.Take(WritingArc.MostPanels))
            {
                if (reads.Count >= ReadsPerSecond) break;
                if (seen.TryGetValue(path, out var file))
                {
                    if (file.Asking || file.ReadAt is double last && (now - last < ReadEverySeconds || file.ListAt == at)) continue;
                }
                else
                {
                    file = new Seen();
                    seen[path] = file;
                }
                file.Asking = true;
                file.ListAt = at;
                reads.Enqueue(now);
                due.Add(path);
            }
            return due;
        }

        /// <summary>The host's answer for <paramref name="path"/>: its text, or why there is none.</summary>
        public void Read(string path, ProjectFileResponse response, double now)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            var file = Track(path);
            file.Asking = false;
            file.ReadAt = now;
            file.Size = response.SizeBytes;
            switch (response.Kind)
            {
                case ProjectFileKind.Text:
                    var lines = Lines(response.Text);
                    file.Lit = file.Text == null ? Array.Empty<int>() : Changed(file.Text, lines);
                    file.Text = lines;
                    file.State = WritingState.Changing;
                    break;
                case ProjectFileKind.Withheld:
                    Without(file, WritingState.KeptPrivate);
                    break;
                case ProjectFileKind.Binary:
                    Without(file, WritingState.NotText);
                    break;
                case ProjectFileKind.TooLarge:
                    Without(file, WritingState.TooLarge);
                    break;
                default:
                    Without(file, WritingState.Unreadable);
                    break;
            }
            file.FirstState ??= file.State;
        }

        /// <summary>
        /// The host refused or could not answer the read of <paramref name="path"/>. A refusal for pace (too many reads) is
        /// never shown: the file keeps what it showed and is read again later. Anything else says the file couldn't be read.
        /// </summary>
        public void Refused(string path, ControlPlaneRequestException error, double now)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            var file = Track(path);
            file.Asking = false;
            if (error.Status == 429 || error.Code == "too_many_requests")
            {
                file.ListAt = null;
                return;
            }
            file.ReadAt = now;
            Without(file, WritingState.Unreadable);
            file.FirstState ??= file.State;
        }

        /// <summary>
        /// The turn ended: every panel settles, and the list takes the order of the result's Changes page, the computer's own
        /// observation of the turn (<paramref name="observed"/>: created, changed, then deleted), so the settle is one motion
        /// into that page with nothing renamed (AUDIT's MUST 3). A file the observation does not name keeps its place after them.
        /// </summary>
        public void Settle(IReadOnlyList<ObservedFile>? observed)
        {
            settled = true;
            foreach (var file in seen.Values)
            {
                file.Asking = false;
                if (file.State == WritingState.Changing) file.State = WritingState.Settled;
            }
            if (observed == null) return;
            // Only the computer's own observation says a file is new to the project; while the turn runs nothing claims it.
            foreach (var made in observed.Where(file => file.Change == ObservedChange.Created)) Track(made.Path).New = true;
            var paths = observed.Select(file => file.Path).ToList();
            foreach (var path in Listed()) if (!paths.Contains(path)) paths.Add(path);
            order = paths;
        }

        /// <summary>What FIELD's arc draws now: the newest files read so far as panels (newest last), every file named this turn as the list, and the host's count.</summary>
        public WritingArc Arc
        {
            get
            {
                var list = Listed().Select(Panel).ToList();
                var shown = named.Where(path => seen.TryGetValue(path, out var file) && file.State != null)
                    .Reverse().Take(WritingArc.MostPanels).Reverse().Select(Panel).ToList();
                return new WritingArc(shown, list, Math.Max(count, list.Count));
            }
        }

        private IEnumerable<string> Listed()
        {
            if (order != null)
            {
                foreach (var path in order) yield return path;
                foreach (var path in named) if (!order.Contains(path)) yield return path;
                yield break;
            }
            foreach (var path in named.OrderBy(path => path, StringComparer.Ordinal)) yield return path;
        }

        private WritingPanel Panel(string path)
        {
            seen.TryGetValue(path, out var file);
            var cut = path.LastIndexOf('/');
            var name = LineWords.TheirName(cut < 0 ? path : path.Substring(cut + 1));
            LineWords? folder = cut <= 0 ? (LineWords?)null : LineWords.Theirs(path.Substring(0, cut));
            var state = file?.State ?? (settled ? WritingState.Settled : WritingState.Changing);
            var (rows, lit, above, below, code) = file?.Text != null ? Window(file, path) : (Array.Empty<string>(), Array.Empty<int>(), 0, 0, (CodeRows?)null);
            return new WritingPanel(path, name, folder, state, rows, lit, file?.Size, file?.New ?? false, above, below, code);
        }

        /// <summary>
        /// How many rows a panel may show at most: the view sets it from what stays inside the field at the person's text size
        /// (the lead's 2026-11-17T03:09Z ruling); <see cref="WindowRows"/> where it says nothing.
        /// </summary>
        public Func<int> MostRows { get; set; } = () => WindowRows;

        /// <summary>
        /// The rows a panel shows: from the file's first row on its first read; after a change, at most <see cref="MostRows"/>
        /// rows ending at the last row that changed, so the window follows the newest change. It starts and ends on whole lines,
        /// and counts the file's own lines it leaves out above and below, never an estimate (AUDIT2, 2026-11-17T03:09Z).
        /// <paramref name="changed"/> names lines; the lit indexes returned name rows of the window.
        /// </summary>
        private (IReadOnlyList<string> Rows, IReadOnlyList<int> Lit, int LinesAbove, int LinesBelow, CodeRows? Code) Window(Seen file, string path)
        {
            var lines = file.Text!;
            var changed = file.Lit;
            var most = Math.Max(1, MostRows());
            var rows = new List<string>();
            var lineOf = new List<int>();
            var litRows = new List<int>();
            var lit = new HashSet<int>(changed);
            for (var index = 0; index < lines.Count; index++)
            {
                var laid = wrap(lines[index]);
                if (laid.Count == 0) laid = new[] { string.Empty };
                foreach (var row in laid)
                {
                    if (lit.Contains(index)) litRows.Add(rows.Count);
                    rows.Add(row);
                    lineOf.Add(index);
                }
            }
            if (rows.Count == 0) return (rows, Array.Empty<int>(), 0, 0, null);
            var end = litRows.Count > 0 ? litRows[litRows.Count - 1] + 1 : Math.Min(rows.Count, most);
            var start = Math.Max(0, end - most);
            // Whole lines only: a line cut at the window's top starts below it; one cut at its foot ends above it, unless the
            // window would hold nothing, where the line's first rows stand.
            while (start > 0 && start < end && lineOf[start] == lineOf[start - 1]) start++;
            var whole = end;
            while (whole > start && whole < rows.Count && lineOf[whole] == lineOf[whole - 1]) whole--;
            if (whole > start) end = whole;
            var window = rows.Skip(start).Take(end - start).ToList();
            var windowLit = litRows.Where(row => row >= start && row < end).Select(row => row - start).ToList();
            var above = lineOf[start];
            var below = lines.Count - 1 - lineOf[end - 1];
            return (window, windowLit, above, below, CodeOf(file, path, lines, rows, lineOf, start, end));
        }

        /// <summary>
        /// The window's rows traced to the file's lines, with what the lines above it left open (AUDIT3's CC-a): worked out
        /// from the file's first line once per text and window start, never per frame.
        /// </summary>
        private CodeRows CodeOf(Seen file, string path, IReadOnlyList<string> lines, List<string> rows, List<int> lineOf, int start, int end)
        {
            var language = CodeTokens.LanguageOf(path);
            var firstLine = lineOf[start];
            if (!(file.Code is CodeRows known && ReferenceEquals(file.CodeText, lines) && file.CodeFirst == firstLine && file.CodeStart == start && file.CodeEnd == end))
            {
                var carry = CodeCarry.None;
                for (var line = 0; line < firstLine; line++) CodeTokens.Of(lines[line], language, ref carry);
                var shownLines = new List<string>();
                var rowLine = new List<int>();
                var rowStart = new List<int>();
                var cursor = 0;
                for (var row = start; row < end; row++)
                {
                    var line = lineOf[row] - firstLine;
                    if (line >= shownLines.Count) { shownLines.Add(lines[lineOf[row]]); cursor = 0; }
                    // A row is its line's indent (and a continued row's hang) as spaces, then the next run of the line's own text.
                    var content = rows[row].TrimStart(' ');
                    var at = content.Length == 0 ? cursor : shownLines[line].IndexOf(content, cursor, StringComparison.Ordinal);
                    if (at < 0) at = cursor;
                    rowLine.Add(line);
                    rowStart.Add(at);
                    cursor = at + content.Length;
                }
                file.Code = known = new CodeRows(language, carry, shownLines, rowLine, rowStart);
                file.CodeText = lines;
                file.CodeFirst = firstLine;
                file.CodeStart = start;
                file.CodeEnd = end;
            }
            return known;
        }

        private Seen Track(string path)
        {
            if (!seen.TryGetValue(path, out var file))
            {
                file = new Seen();
                seen[path] = file;
            }
            if (!named.Contains(path)) named.Add(path);
            return file;
        }

        private static void Without(Seen file, WritingState state)
        {
            file.State = state;
            file.Text = null;
            file.Lit = Array.Empty<int>();
        }

        private static IReadOnlyList<string> Lines(string? text) =>
            string.IsNullOrEmpty(text) ? Array.Empty<string>() : text!.Replace("\r\n", "\n").Split('\n');

        /// <summary>
        /// The lines of <paramref name="now"/> that are new or changed since <paramref name="before"/>, by the shortest line
        /// edit (Myers), bounded by <see cref="MostEdits"/>; empty where the edit is longer, so nothing is lit wrongly.
        /// </summary>
        public static IReadOnlyList<int> Changed(IReadOnlyList<string> before, IReadOnlyList<string> now)
        {
            var n = before.Count;
            var m = now.Count;
            var max = Math.Min(n + m, MostEdits);
            var offset = max + 1;
            var v = new int[2 * max + 3];
            var trace = new List<int[]>();
            for (var d = 0; d <= max; d++)
            {
                trace.Add((int[])v.Clone());
                for (var k = -d; k <= d; k += 2)
                {
                    var x = k == -d || k != d && v[offset + k - 1] < v[offset + k + 1] ? v[offset + k + 1] : v[offset + k - 1] + 1;
                    var y = x - k;
                    while (x < n && y < m && before[x] == now[y]) { x++; y++; }
                    v[offset + k] = x;
                    if (x >= n && y >= m) return Inserted(trace, offset, d, n, m);
                }
            }
            return Array.Empty<int>();
        }

        private static IReadOnlyList<int> Inserted(List<int[]> trace, int offset, int depth, int n, int m)
        {
            var inserted = new List<int>();
            var x = n;
            var y = m;
            for (var d = depth; d > 0; d--)
            {
                var v = trace[d];
                var k = x - y;
                var down = k == -d || k != d && v[offset + k - 1] < v[offset + k + 1];
                var previousK = down ? k + 1 : k - 1;
                var previousX = v[offset + previousK];
                var previousY = previousX - previousK;
                while (x > previousX && y > previousY) { x--; y--; }
                if (down) inserted.Add(y - 1);
                x = previousX;
                y = previousY;
            }
            inserted.Reverse();
            return inserted.Where(line => line >= 0).ToList();
        }

        private sealed class Seen
        {
            public bool Asking;
            public double? ReadAt;
            public string? ListAt;
            public WritingState? State;
            public WritingState? FirstState;
            public IReadOnlyList<string>? Text;
            public IReadOnlyList<int> Lit = Array.Empty<int>();
            public long? Size;
            public bool New;
            public CodeRows? Code;
            public IReadOnlyList<string>? CodeText;
            public int CodeFirst = -1;
            public int CodeStart = -1;
            public int CodeEnd = -1;
        }
    }
}
