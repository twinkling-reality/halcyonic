#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuDirector
    {
        /// <summary>SCREENS's file of Changes read, one per task while its File page is open (<see cref="FileChangeColumn"/>).</summary>
        private readonly Dictionary<string, FileChangeColumn> fileReads = new Dictionary<string, FileChangeColumn>();

        /// <summary>The task's file reading, wrapped at the person's text size now.</summary>
        private FileChangeColumn? FileReadFor(string task)
        {
            if (!fileReads.TryGetValue(task, out var reading)) return null;
            reading.AtScale(FileTextScale);
            return reading;
        }

        /// <summary>The person's text size as a scale (<see cref="Comfort.TextScale"/>'s values): the reading wraps to FIELD's columns at it.</summary>
        private float FileTextScale => TextSize == TextSize.Larger ? Comfort.LargerTextScale : 1f;

        /// <summary>
        /// A press of the file reading's own (<see cref="RoomResult.FileActions"/>): a Changes row read (its key the path),
        /// its pages, Whole File, Try Again and Back. The reads go through the control plane's own authorized GET; nothing is
        /// sent. False where the press no longer stands.
        /// </summary>
        private bool OnFileActed(string action, string? key, string task, string? run, string projectId)
        {
            var reading = FileReadFor(task);
            switch (action)
            {
                // Never where no computer can be asked: the row is not drawn pressable then (RoomResult.ReadsFiles).
                case RoomResult.ShowFile when key != null && run != null && Api != null:
                    var observed = setup.Observed(run).LastOrDefault();
                    var row = observed == null ? (RoomChange?)null : new RoomResult(false, false, false, null, observed, null).Changes.Where(change => change.Path == key).Cast<RoomChange?>().FirstOrDefault();
                    if (row is not RoomChange change) return false;
                    var opened = new FileChangeColumn(projectId, change, FileTextScale);
                    fileReads[task] = opened;
                    setup.Presentation.ShowResultPage(task, run, RoomResultPage.File);
                    ReadChange(opened, run, observed!.TurnId);
                    return true;
                case RoomResult.FileNextPage when reading != null:
                    return reading.NextPage();
                case RoomResult.FilePreviousPage when reading != null:
                    return reading.PreviousPage();
                case RoomResult.FileWhole when reading != null:
                    if (reading.OpenWhole()) ReadWholeFile(reading);
                    return true;
                case RoomResult.FileTryAgain when reading != null && reading.Stage == FileChangeStage.Unreadable:
                    if (reading.Reread()) ReadWholeFile(reading);
                    else if (run != null) ReadChange(reading, run, setup.Observed(run).LastOrDefault()?.TurnId);
                    return true;
                case RoomResult.FileBack:
                    if (reading != null && reading.Back()) return true;
                    fileReads.Remove(task);
                    setup.Presentation.ShowResultPage(task, run, RoomResultPage.Changes);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>SCREENS's waiting instruction whose page shows, one per task; none where the list shows.</summary>
        private readonly Dictionary<string, string> waitingOpened = new Dictionary<string, string>();

        /// <summary>Instructions not sent that the person went Back from, so the result shows again.</summary>
        private readonly HashSet<string> unsentDismissed = new HashSet<string>();

        /// <summary>The task's run's instructions waiting for its turn to end, with the words this headset sent; null with no run.</summary>
        private WaitingInstructions? WaitingFor(WorkspacePresentation now)
        {
            if (now.Execution is not ExecutionView run) return null;
            var opened = waitingOpened.TryGetValue(now.Character.WorkstreamId, out var id) ? id : null;
            var waiting = new WaitingInstructions(run.ExecutionId, run.QueuedInstructions, setup.InstructionWords, opened,
                UnsentInstruction.Of(State, run.ExecutionId, setup.InstructionWords, unsentDismissed.Contains));
            // One sent, failed or dropped since its page opened: back to the list.
            if (opened != null && waiting.Opened == null) waitingOpened.Remove(now.Character.WorkstreamId);
            return waiting;
        }

        /// <summary>A waiting row opened, Drop (nothing was sent, so it asks nothing; the list loses the row once the host records it), or Back.</summary>
        private bool OnWaitingActed(string action, string? key, string task, string? run)
        {
            switch (action)
            {
                case RoomResult.WaitingOpen when key != null:
                    waitingOpened[task] = key;
                    return true;
                case RoomResult.WaitingDrop when key != null && run != null && waitingOpened.TryGetValue(task, out var opened) && opened == key:
                    if (!SendCommand(setup.Commands.DropQueuedInstruction(run, key))) return false;
                    waitingOpened.Remove(task);
                    return true;
                case RoomResult.WaitingBack:
                    // From a waiting row's page back to the list; from Not Sent back to the result.
                    if (!waitingOpened.Remove(task) && key != null) unsentDismissed.Add(key);
                    return true;
                case RoomResult.WaitingAgain when key != null && run != null && setup.InstructionWords(key) is string words:
                    // Send Again: the same words as a new instruction (CORE: safe after any of these failures).
                    if (!SendCommand(setup.Commands.SendInstruction(run, words))) return false;
                    unsentDismissed.Add(key);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Finds the versions around the observed turn in the project's history, then reads the file's change between them.</summary>
        private async void ReadChange(FileChangeColumn reading, string run, string? turnId)
        {
            if (Api is not ControlPlaneApi api || State?.Journal?.JournalId is not string journal)
            {
                // Nothing was asked: never "did not answer", and no Try Again.
                reading.ReadFailed(answered: true);
                dirty = true;
                return;
            }
            try
            {
                var files = new FileChangeReader(api.GetDocumentAsync);
                var versions = await files.FindTurnAsync(reading.ProjectId, journal, run, turnId);
                dirty = true;
                if (reading.Located(versions)) reading.Changed(await files.FileChangeAsync(reading.ProjectId, journal, versions.From!, versions.To!, reading.Path));
            }
            catch (Exception error) { reading.ReadFailed(answered: FileChangeColumn.Answered(error)); }
            dirty = true;
        }

        /// <summary>Reads the whole file as it is now.</summary>
        private async void ReadWholeFile(FileChangeColumn reading)
        {
            if (Api is not ControlPlaneApi api || State?.Journal?.JournalId is not string journal)
            {
                reading.ReadFailed(whole: true, answered: true);
                dirty = true;
                return;
            }
            try { reading.FileRead(await new FileChangeReader(api.GetDocumentAsync).FileAsync(reading.ProjectId, journal, reading.Path)); }
            catch (Exception error) { reading.ReadFailed(whole: true, answered: FileChangeColumn.Answered(error)); }
            dirty = true;
        }
    }
}
