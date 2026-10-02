#nullable enable

namespace Halcyonic.Client
{
    /// <summary>A file's Waiting page (ADR 0026), from the work's own state alone, never an answer still being read.</summary>
    public static partial class FileScreens
    {
        /// <summary>Said, with the page's source line, while nothing waits for the person.</summary>
        public const string NothingWaits = "Nothing is waiting for you.";

        private static Page Waiting(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, AnswerRoom room) =>
            new Page(new[] { new PageLine(NothingWaits) }, RuntimeSource(workspace), new Footer(CloseFile));
    }
}
