#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Tasks, the menu's first place (ADR 0026): every task as a row, what waits for the person first,
    /// then what runs, then what has ended, in the stage's own order (<see cref="CharacterLineup.Compare"/>);
    /// its icon its state's, the tone on the icon and, where it waits, on its fact; its words its title;
    /// its one kind of small fact its project's name where there is more than one project; and a chevron,
    /// since pressing it opens its file beside the menu, where its row stays chosen. Its subject says
    /// what waits, in the waiting colour, or that nothing is waiting. A page holds the rows that fit
    /// beside a file on this stage, decided when the menu opens, so opening a file never packs it again.
    /// </summary>
    public sealed class TasksColumn : IMenuColumn
    {
        /// <summary>What pressing a task's row raises, with its workstream's id as the key.</summary>
        public const string OpenTask = "tasks-open-task";

        private readonly IMenuHost host;
        private int rows;
        private TextSize rowsAt;
        private int page;
        private string? showing;
        private long position = -1;

        public TasksColumn(IMenuHost host)
        {
            this.host = host;
            Measure();
        }

        /// <summary>As many rows as fit beside a file on this stage at the text size now, at most a list's page.</summary>
        private void Measure()
        {
            var height = host.PageHeight(1, besideMenu: true);
            var most = host.PageRows(sourceLine: false);
            rows = Math.Max(1, Enumerable.Range(1, most).LastOrDefault(count => MenuPage.Rows(count) <= height));
            rowsAt = host.TextSize;
        }

        public event Action? Changed;

        public event Action? Closed;

        /// <summary>The rows a page holds on this stage, read again when the text size changes, so a page never stands taller than fits.</summary>
        public int Rows
        {
            get
            {
                if (rowsAt != host.TextSize) Measure();
                return rows;
            }
        }

        /// <summary>
        /// The menu's bar as the session stands: <paramref name="chosen"/> lit, the amber dot on Tasks while
        /// any task waits for the person, and the closed line saying how many, or that nothing is waiting.
        /// </summary>
        public static MenuBar Bar(MenuPlace chosen, ClientProjection? state)
        {
            var waiting = state?.Workstreams.Values.Count(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou) ?? 0;
            return waiting > 0 ? new MenuBar(chosen, TasksText.Waiting(waiting), MenuPlace.Tasks) : new MenuBar(chosen, TasksText.Waiting(0));
        }

        public MenuFrame? Frame
        {
            get
            {
                var tasks = Tasks();
                var rows = Rows;
                var pages = Math.Max(1, (tasks.Count + rows - 1) / rows);
                if (page >= pages) page = 0;
                var waiting = tasks.Count(task => CharacterLineup.TierOf(task) == LineupTier.NeedsYou);
                var projects = tasks.Select(task => task.ProjectId).Distinct().Count();
                var lines = tasks.Skip(page * rows).Take(rows).Select(task => Row(task, projects > 1)).ToList();
                if (lines.Count == 0) lines.Add(new PageLine(TasksText.None, tone: LineTone.Secondary));
                var close = new Prompt(Footer.Close, TasksText.Close, GlazeIcon.Close, PromptKind.Close);
                var footer = pages > 1
                    ? new Footer(close).WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage))
                    : new Footer(close);
                return new MenuFrame(TasksText.Waiting(waiting), footer, subjectWaits: waiting > 0, lines: lines);
            }
        }

        /// <summary>The task whose file stands beside the menu, whose row stays chosen; the director tells it.</summary>
        public void Showing(string? workstreamId)
        {
            if (showing == workstreamId) return;
            showing = workstreamId;
            Changed?.Invoke();
        }

        public void Act(string id, string? key)
        {
            switch (id)
            {
                case OpenTask when key != null && host.State?.Workstreams.ContainsKey(key) == true:
                    host.OpenFile(key);
                    break;
                case Footer.NextPage:
                    page++;
                    Changed?.Invoke();
                    break;
                case Footer.Close:
                    Closed?.Invoke();
                    break;
            }
        }

        public void Drawn(MenuFrame drawn, bool sidePanel)
        {
        }

        public void HoldStarted(string id)
        {
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        public void Heard(string text)
        {
        }

        public void Said(string words)
        {
        }

        /// <summary>Raises <see cref="Changed"/> when the session's journal has moved, as a task's state or a new task does.</summary>
        public void Tick()
        {
            var now = host.State?.Position ?? -1;
            if (now == position) return;
            position = now;
            Changed?.Invoke();
        }

        public void FocusLeft()
        {
        }

        private List<WorkstreamView> Tasks()
        {
            var tasks = host.State?.Workstreams.Values.ToList() ?? new List<WorkstreamView>();
            tasks.Sort(CharacterLineup.Compare);
            return tasks;
        }

        private PageLine Row(WorkstreamView task, bool withProject)
        {
            var state = host.State!;
            var badge = StateLanguage.BadgeOf(CharacterPresenter.Present(task, state, host.Connected && !host.Demonstration));
            var tone = badge.Tone switch
            {
                GlazeTone.Attention => LineTone.Waiting,
                GlazeTone.Success => LineTone.Good,
                GlazeTone.Failure => LineTone.Problem,
                _ => LineTone.Primary,
            };
            var project = withProject && state.Projects.TryGetValue(task.ProjectId, out var view) ? view.Name : null;
            return new PageLine(task.Title, wordsAreData: true, icon: badge.Icon, fact: project, factIsData: project != null, tone: tone,
                action: OpenTask, key: task.WorkstreamId, opens: true, chosen: task.WorkstreamId == showing);
        }
    }

    /// <summary>Tasks' own words (WORDS.md).</summary>
    public static class TasksText
    {
        public const string Close = "Close";

        /// <summary>A page with no tasks on it.</summary>
        public const string None = "No tasks yet.";

        /// <summary>The subject: what waits for the person, or that nothing does.</summary>
        public static string Waiting(int count) => count switch
        {
            0 => "Nothing is waiting for you.",
            1 => "1 task is waiting for you",
            _ => count + " tasks are waiting for you",
        };
    }
}
