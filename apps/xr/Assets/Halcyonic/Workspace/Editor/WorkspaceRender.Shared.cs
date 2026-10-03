#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>What several renders share: a portfolio of work to stand on the stage, and checks of what shows.</summary>
    public static partial class WorkspaceRender
    {
        private const string PortfolioJournal = "01a0dcf1-5a80-7000-8000-000000000001";
        private const string PortfolioTime = "2026-09-30T09:00:00.000Z";

        /// <summary>
        /// Three projects with the demonstration's kind of work: ten workstreams, of which six stand on
        /// the stage; one project hidden, with work that needs the person. With <paramref name="needsYouNow"/>
        /// another comes to need the person, for the banner; <paramref name="hostile"/> names every
        /// project and titles every workstream with text from outside at its worst.
        /// </summary>
        internal static ClientProjection Portfolio(bool hostile, bool needsYouNow)
        {
            string Named(string plain, string field) => hostile ? Hostile(field) : plain;
            var projects = new List<ProjectView>
            {
                new()
                {
                    ProjectId = "p-store", Name = Named("Storefront API", "project"), CreatedAt = PortfolioTime, UpdatedAt = PortfolioTime,
                    Location = new ProjectLocation { Path = "/Users/person/Projects/storefront-api", Name = Named("storefront-api", "folder"), Created = false },
                },
                new() { ProjectId = "p-docs", Name = Named("Docs site", "project"), CreatedAt = PortfolioTime, UpdatedAt = Time },
                new() { ProjectId = "p-recipes", Name = Named("Recipe tracker", "project"), CreatedAt = PortfolioTime, UpdatedAt = Time },
            };
            var work = new List<WorkstreamView>
            {
                PortfolioWork("w-a", "p-store", Named("Add rate limiting to sign-in", "title"), needsYouNow ? WorkstreamStatus.WaitingForHuman : WorkstreamStatus.Running, 1),
                PortfolioWork("w-b", "p-store", Named("Paginate the order history", "title"), WorkstreamStatus.Running, 2),
                PortfolioWork("w-c", "p-store", Named("Send order confirmations", "title"), WorkstreamStatus.Verifying, 3),
                PortfolioWork("w-d", "p-store", Named("Refresh the checkout copy", "title"), WorkstreamStatus.Completed, 4),
                PortfolioWork("w-e", "p-store", Named("Upgrade the image pipeline", "title"), WorkstreamStatus.Failed, 5),
                PortfolioWork("w-f", "p-docs", Named("Rewrite the quick start", "title"), WorkstreamStatus.Running, 6),
                PortfolioWork("w-g", "p-docs", Named("Fix broken links", "title"), WorkstreamStatus.Completed, 7),
                PortfolioWork("w-h", "p-docs", Named("Add a search page", "title"), WorkstreamStatus.Created, 8),
                PortfolioWork("w-i", "p-recipes", Named("Import recipes from a file", "title"), WorkstreamStatus.WaitingForHuman, 9),
                PortfolioWork("w-j", "p-recipes", Named("Plan the week's dinners", "title"), WorkstreamStatus.Completed, 10),
            };
            var state = new ClientProjection();
            state.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = PortfolioJournal, Origin = JournalOrigin.Live },
                Position = 1,
                Projects = projects,
                Workstreams = work,
                Executions = new List<ExecutionView>(),
                Commands = new List<CommandView>(),
                Runtimes = new List<RuntimeDescriptor>
                {
                    PortfolioRuntime("mock", "Simulated agent (render)", synthetic: true, ModelChoice.None),
                    PortfolioRuntime("local", "Local agent (render)", synthetic: false, ModelChoice.Listed),
                },
            }, new StateChanges());
            return state;
        }

        private static WorkstreamView PortfolioWork(string id, string project, string title, WorkstreamStatus status, int minute) => new()
        {
            WorkstreamId = id,
            ProjectId = project,
            Title = title,
            Objective = null,
            Status = status,
            Attention = new Attention
            {
                Level = status == WorkstreamStatus.WaitingForHuman ? AttentionLevel.ActionRequired
                    : status == WorkstreamStatus.Failed ? AttentionLevel.Notice : AttentionLevel.None,
                Reasons = new List<AttentionReason>(),
            },
            CurrentExecutionId = null,
            ExecutionIds = new List<string>(),
            CreatedAt = PortfolioTime,
            UpdatedAt = "2026-09-30T09:" + minute.ToString("00", CultureInfo.InvariantCulture) + ":00.000Z",
        };

        private static RuntimeDescriptor PortfolioRuntime(string id, string name, bool synthetic, ModelChoice choice) => new()
        {
            RuntimeId = id,
            Kind = id,
            DisplayName = name,
            Synthetic = synthetic,
            ModelChoice = choice,
            // A real runtime works in the project's folder; the simulated one needs none.
            UsesProjectLocation = !synthetic,
            Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, Interrupt = true },
        };

        /// <summary>
        /// Nothing showing is cut short, with the render's short names and titles: only text from
        /// outside that a row or a title says is data may end in an ellipsis, as a long first task does.
        /// </summary>
        internal static IEnumerable<string> NothingOfOursCut(IEnumerable<Component> parts, string what, PanelFrame? frame = null)
        {
            var failures = new List<string>();
            foreach (var part in parts)
            {
                var labels = part is GlazeButton button ? new TMP_Text?[] { button.Label, button.Detail, button.Overline, button.End } : new[] { part as TMP_Text };
                foreach (var label in labels)
                {
                    if (label == null || !label.gameObject.activeInHierarchy) continue;
                    if (frame != null && frame.HoldsData(label)) continue;
                    label.ForceMeshUpdate();
                    if (!label.isTextTruncated || label.name == "First task") continue;
                    failures.Add(what + ": " + part.name + " cuts its words short: " + label.text);
                }
            }
            return failures;
        }

        internal static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;

        /// <summary>World bounds on the render, as a rectangle.</summary>
        internal static Rect ScreenBounds(Camera camera, Bounds bounds)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                var screen = camera.WorldToScreenPoint(point);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }
}
