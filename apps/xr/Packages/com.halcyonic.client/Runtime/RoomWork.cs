#nullable enable
using System;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The step under way, as the agent's cursor label says it (DESIGN's BUILD-SPEC-v4 2.3): a short status, one line,
    /// never a record. Halcyonic's verb for the tool, then at most the file's own name.
    /// </summary>
    public sealed class RoomWorkStep
    {
        public RoomWorkStep(string key, string verb, string? name)
        {
            Key = key;
            Verb = verb;
            Name = name;
        }

        /// <summary>The tool call's or the test run's id: a new key is a new step, and the cursor's next stop.</summary>
        public string Key { get; }

        /// <summary>Halcyonic's word for what the tool does.</summary>
        public string Verb { get; }

        /// <summary>The file's name as the runtime gave it, the path's last part only, read by the outside-text rule; null where the step names none.</summary>
        public string? Name { get; }

        /// <summary>
        /// The label's words: the verb and the name where <paramref name="fits"/> says the whole line fits, else the verb
        /// alone. Never a cut word, never an ellipsis.
        /// </summary>
        public string Words(Func<string, bool> fits)
        {
            if (Name == null) return Verb;
            var whole = Verb + " " + Name;
            return fits(whole) ? whole : Verb;
        }
    }

    /// <summary>
    /// What the runtime reports under way, for the agent's cursor (DESIGN's BUILD-SPEC-v4 2.3; the lead's ruling of
    /// 2026-11-10): from the execution's own view, its test run and its tools under way, which the runtime's tool events
    /// start and end by their shared id. Tests read "Running Tests", with no count and no label. A command reads "Running
    /// Command", never its line. Reading and editing read their verb and the file's name. Any other tool reads the
    /// product's word for work under way. Nothing under way, or a runtime that reports no tools, gives no step: the cursor
    /// never shows what the system does not know. The same for a live run and a recorded one.
    /// </summary>
    public static class RoomWork
    {
        public const string RunningTests = "Running Tests";
        public const string RunningCommand = "Running Command";
        public const string Reading = "Reading";
        public const string Editing = "Editing";

        /// <summary>A tool with no verb of its own: the product's word for a task at work (<see cref="StateLanguage"/>).</summary>
        public const string Running = "Running";

        /// <summary>The tools that read a file, and the ones that change one, as the agent apps name them.</summary>
        private static readonly string[] ReadingTools = { "read", "notebookread" };
        private static readonly string[] EditingTools = { "edit", "write", "multiedit", "notebookedit", "patch", "apply_patch", "filechange" };

        /// <summary>The step under way in <paramref name="execution"/>, or null where none is reported.</summary>
        public static RoomWorkStep? Of(ExecutionView? execution)
        {
            if (execution == null) return null;
            if (execution.ActiveTestRun is TestRunView tests) return new RoomWorkStep(tests.TestRunId, RunningTests, null);
            ToolActivityView? newest = null;
            foreach (var tool in execution.ActiveTools)
            {
                if (tool == null || string.IsNullOrEmpty(tool.ToolCallId)) continue;
                // The one that started last; of two started together, the later in the list.
                if (newest == null || string.CompareOrdinal(tool.StartedAt ?? "", newest.StartedAt ?? "") >= 0) newest = tool;
            }
            if (newest == null) return null;
            var name = newest.ToolName ?? "";
            if (WorkspaceText.RunsCommand(name)) return new RoomWorkStep(newest.ToolCallId, RunningCommand, null);
            if (Is(ReadingTools, name)) return new RoomWorkStep(newest.ToolCallId, Reading, FileName(newest.Title, name));
            if (Is(EditingTools, name)) return new RoomWorkStep(newest.ToolCallId, Editing, FileName(newest.Title, name));
            return new RoomWorkStep(newest.ToolCallId, Running, null);
        }

        private static bool Is(string[] tools, string name)
        {
            foreach (var tool in tools)
                if (string.Equals(tool, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The file's name from a tool call's title: the path's last part, and nothing else. A title of several lines names
        /// several files or none, so it gives no name. A file change's title leads with what is done to the file ("update
        /// src/game.js"): that word goes, and a move, which names two files, gives none. Never a place inside a file.
        /// </summary>
        public static string? FileName(string? title, string toolName = "")
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var path = title!.Trim();
            if (path.IndexOf('\n') >= 0 || path.IndexOf('\r') >= 0) return null;
            if (string.Equals(toolName, "fileChange", StringComparison.OrdinalIgnoreCase))
            {
                var space = path.IndexOf(' ');
                if (space <= 0) return null;
                var kind = path.Substring(0, space);
                if (kind != "add" && kind != "update" && kind != "delete") return null;
                path = path.Substring(space + 1).Trim();
            }
            path = path.TrimEnd('/', '\\');
            var cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            var name = LabelText.Plain(cut >= 0 ? path.Substring(cut + 1) : path);
            return name.Length == 0 ? null : name;
        }
    }
}
