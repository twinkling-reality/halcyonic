#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuDirector
    {
        /// <summary>How long the panels take to settle into the result's Changes before the view closes (FIELD's fold, 600 ms, and a beat).</summary>
        private const double WatchSettleSeconds = 1.2;

        private WatchBuild? watch;
        private string? watchTask;
        private int watchShown = -1;
        private int watchVersion;
        private double? watchClosesAt;

        /// <summary>
        /// Watch It Work pressed on the working bar (RoomBar.Watch): opens the view for the task beside the menu, or closes it where it
        /// stands open for that task. The model (WatchBuild) decides what to read; this director reads it, as the file's own reads go.
        /// </summary>
        private bool ToggleWatch()
        {
            if (navigator.Beside is not FileColumn file || file.Now is not WorkspacePresentation now) return false;
            if (watchTask == now.Character.WorkstreamId)
            {
                CloseWatch();
                return true;
            }
            watch = new WatchBuild(RoomWritingPanel.Wrap);
            RoomWritingArc.ListTitle = _ => WatchBuild.ListWords;
            watchTask = now.Character.WorkstreamId;
            watchShown = -1;
            watchClosesAt = null;
            plane.Kit.WatchOpen = true;
            plane.Kit.WatchEyes = () => plane.Eyes;
            dirty = true;
            return true;
        }

        private void CloseWatch()
        {
            watch = null;
            watchTask = null;
            watchClosesAt = null;
            plane.Kit.WatchOpen = false;
            plane.Kit.ShowWatch(null);
            dirty = true;
        }

        /// <summary>
        /// Each frame while the view stands: the host's latest list of the files the turn changes (ExecutionView.Writing) tells the model
        /// what to read; at the turn's end the panels settle into the computer's own observation of the turn, then the view closes.
        /// The arc is laid again only when the model changed, so a quiet frame allocates nothing (ADR 0027).
        /// </summary>
        private void TickWatch()
        {
            if (watch == null || watchTask == null) return;
            if (State == null || !State.Workstreams.TryGetValue(watchTask, out var work) || Api is not ControlPlaneApi api || State.Journal is not JournalInfo journal)
            {
                CloseWatch();
                return;
            }
            var now = Time.unscaledTimeAsDouble;
            var execution = State.CurrentExecution(work);
            if (execution?.Status is not (ExecutionStatus.Running or ExecutionStatus.Verifying))
            {
                if (watchClosesAt == null)
                {
                    watch.Settle(execution != null ? setup.Observed(execution.ExecutionId).LastOrDefault()?.Files : null);
                    watchVersion++;
                    watchClosesAt = now + WatchSettleSeconds;
                    plane.Kit.SettleWatch();
                }
                else if (now >= watchClosesAt) { CloseWatch(); return; }
            }
            else
            {
                foreach (var path in watch.Due(execution.Writing, now))
                    _ = ReadWatched(api, work.ProjectId, journal.JournalId, watchTask, path, watch);
            }
            if (watchShown != watchVersion)
            {
                watchShown = watchVersion;
                plane.Kit.ShowWatch(watch.Arc);
            }
            // The arc keeps facing the eyes as the plane follows the head.
            plane.Kit.FaceWatch();
        }

        /// <summary>One read the model asked for, answered to the model that asked, if it still stands; a refusal for pace is never shown.</summary>
        private async Task ReadWatched(ControlPlaneApi api, string projectId, string journalId, string workstreamId, string path, WatchBuild asking)
        {
            try
            {
                var read = await new FileChangeReader(api.GetDocumentAsync).FileAsync(projectId, journalId, path, workstreamId);
                if (!ReferenceEquals(watch, asking)) return;
                asking.Read(path, read, Time.unscaledTimeAsDouble);
            }
            catch (ControlPlaneRequestException refused)
            {
                if (!ReferenceEquals(watch, asking)) return;
                asking.Refused(path, refused, Time.unscaledTimeAsDouble);
            }
            catch (Exception) when (ReferenceEquals(watch, asking))
            {
                asking.Refused(path, new ControlPlaneRequestException("The file could not be read.", (string?)null), Time.unscaledTimeAsDouble);
            }
            watchVersion++;
        }
    }
}
