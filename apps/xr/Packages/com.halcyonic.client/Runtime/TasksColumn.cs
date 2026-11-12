#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Tasks for all projects or the explicitly selected project, with actual state and provenance on
    /// every row. Project details and saved drafts remain reachable through offered controls. Paging
    /// reserves the All projects row and the measured room beside a retained file.
    /// </summary>
    public sealed class TasksColumn : IMenuColumn
    {
        /// <summary>What pressing a task's row raises, with its workstream's id as the key.</summary>
        public const string OpenTask = "tasks-open-task";

        private readonly IMenuHost host;
        private readonly Func<int>? savedViews;
        private readonly Action? openSavedViews;
        private readonly Func<string?>? project;
        private readonly Action? openProjectDetails;
        private readonly Action? allProjects;
        private string? measuredProject;
        private string? drawnNewTaskProject, drawnNewTaskJournal;
        public const string SavedViews = "tasks-saved-views";
        public const string ProjectDetails = "tasks-project-details";
        public const string AllProjects = "tasks-all-projects";
        public const string NewTask = "tasks-new-task";

        /// <summary>Ask the assistant about this project, or about all work where no project is chosen (gap 7).</summary>
        public const string Ask = "tasks-ask";
        private int rows;
        private TextSize rowsAt;
        private bool rowsWithNewTask;
        private int page;
        private string? showing;
        private long position = -1;

        /// <param name="ask">Opens an Ask about the chosen project, or about all work (gap 7); no Ask line where null.</param>
        public TasksColumn(IMenuHost host, Func<int>? savedViews = null, Action? openSavedViews = null,
            Func<string?>? project = null, Action? openProjectDetails = null, Action? allProjects = null, Action<AskScope>? ask = null)
        {
            this.host = host;
            this.ask = ask;
            this.savedViews = savedViews;
            this.openSavedViews = openSavedViews;
            this.project = project;
            this.openProjectDetails = openProjectDetails;
            this.allProjects = allProjects;
            Measure();
        }

        /// <summary>As many rows as fit beside a file on this stage at the text size now, at most a list's page.</summary>
        private void Measure()
        {
            var height = host.PageHeight(1, besideMenu: true);
            var reserved = (project?.Invoke() != null && allProjects != null ? 1 : 0) + (NewTaskProject() != null ? 1 : 0) + (AskOffered ? 1 : 0);
            var most = Math.Max(1, host.PageRows(sourceLine: false) - reserved);
            rows = Math.Max(1, Enumerable.Range(1, most).LastOrDefault(count => MenuPage.Rows(count + reserved) <= height));
            rowsAt = host.TextSize;
            rowsWithNewTask = NewTaskProject() != null;
        }

        public event Action? Changed;

        public event Action? Closed;

        private readonly Action<AskScope>? ask;

        /// <summary>
        /// Whether the Ask line stands: where the director gave a way to open one, the computer can be asked, and the page
        /// still holds at least one task row beside the other rows that act, as New Task's own room is judged.
        /// </summary>
        private bool AskOffered
        {
            get
            {
                if (ask == null || !host.Connected || host.Demonstration) return false;
                // Under the redesign a project's own list stands as a card whose actions are its column's, not page rows, so Ask
                // stands at every text size (the critic of 2026-11-12: it showed at Standard and not at Larger).
                if (RoomDesign.Enabled && project?.Invoke() != null) return true;
                var needed = (project?.Invoke() != null && allProjects != null ? 1 : 0) + (NewTaskProject() != null ? 1 : 0) + 2;
                return host.PageRows(false) >= needed && MenuPage.Rows(needed) <= host.PageHeight(1, besideMenu: true);
            }
        }

        /// <summary>The rows a page holds on this stage, read again when the text size changes, so a page never stands taller than fits.</summary>
        public int Rows
        {
            get
            {
                if (rowsAt != host.TextSize || rowsWithNewTask != (NewTaskProject() != null)) Measure();
                return rows;
            }
        }

        /// <summary>
        /// The menu's bar as the session stands: <paramref name="chosen"/> lit, the amber dot on Tasks while
        /// any task waits for the person, and the closed line saying how many, or that nothing is waiting,
        /// or, while the computer's state is known and has no task at all, that nothing is running yet;
        /// before the first snapshot nothing is known, so it says only that nothing is waiting.
        /// </summary>
        public static MenuBar Bar(MenuPlace chosen, ClientProjection? state)
        {
            if (state?.Journal != null && state.Workstreams.Count == 0) return new MenuBar(chosen, TasksText.NothingRunning);
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
                var projects = tasks.Select(task => task.ProjectId).Distinct().Count();
                var lines = tasks.Skip(page * rows).Take(rows).Select(task => Row(task, projects > 1)).ToList();
                if (lines.Count == 0) lines.Add(new PageLine(TasksText.None, tone: LineTone.Secondary));
                // Ask first among the rows that act: about the chosen project, or about all work.
                if (AskOffered) lines.Insert(0, new PageLine(TasksText.Ask, icon: GlazeIcon.TellIt, action: Ask, key: SelectedProject()?.ProjectId));
                var close = new Prompt(Footer.Close, TasksText.Close, GlazeIcon.Close, PromptKind.Close);
                var footer = pages > 1
                    ? new Footer(close).WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage))
                    : new Footer(close);
                if (savedViews?.Invoke() > 0 && openSavedViews != null) footer = new Footer(close,
                    rare: new Prompt(SavedViews, "Saved Drafts", GlazeIcon.Next), farRight: footer[PromptSlot.FarRight]);
                if (project?.Invoke() != null)
                {
                    footer = new Footer(close, rare: footer[PromptSlot.Rare],
                        secondary: openProjectDetails != null ? new Prompt(ProjectDetails, "Project Details", GlazeIcon.Next) : null,
                        farRight: footer[PromptSlot.FarRight]);
                    var selected = NewTaskProject();
                    if (selected != null) lines.Insert(0, new PageLine(TasksText.NewTask, icon: GlazeIcon.AddTask, action: NewTask, key: selected.ProjectId));
                    // These navigation/action rows reserve real measured room, independent of the agent shelf's display filter.
                    if (allProjects != null) lines.Insert(0, new PageLine("All Projects", icon: GlazeIcon.Back, action: AllProjects));
                    return new MenuFrame("Tasks", footer, lines: lines);
                }
                return new MenuFrame("Tasks", footer, lines: lines);
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
                case SavedViews when savedViews?.Invoke() > 0:
                    openSavedViews?.Invoke();
                    break;
                case ProjectDetails when project?.Invoke() != null:
                    openProjectDetails?.Invoke();
                    break;
                case AllProjects when project?.Invoke() != null:
                    allProjects?.Invoke();
                    break;
                case Ask when AskOffered:
                {
                    var chosen = SelectedProject();
                    // Only the project the page shows, as the press named it; else all work.
                    if (key != (chosen?.ProjectId)) return;
                    ask!(chosen == null ? AskScope.AllWork() : AskScope.Project(chosen.ProjectId, chosen.Name));
                    return;
                }
                case NewTask:
                    var selected = SelectedProject();
                    if (selected == null || key == null || key != drawnNewTaskProject || key != selected.ProjectId
                        || drawnNewTaskJournal == null || drawnNewTaskJournal != host.State?.Journal?.JournalId
                        || !host.Connected || host.Demonstration) return;
                    drawnNewTaskProject = drawnNewTaskJournal = null;
                    host.OpenNewProject(selected.ProjectId, selected.Name);
                    return;
                case OpenTask when key != null && Tasks().Any(task => task.WorkstreamId == key):
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

        public void Drawn(MenuFrame drawn, Footer? sidePanel)
        {
            var selected = NewTaskProject();
            drawnNewTaskProject = selected != null && drawn.Lines.Any(line => line.Action == NewTask && line.Key == selected.ProjectId && line.Available)
                ? selected.ProjectId : null;
            drawnNewTaskJournal = drawnNewTaskProject == null ? null : host.State?.Journal?.JournalId;
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
            var selected = project?.Invoke();
            if (!host.Connected || host.Demonstration) drawnNewTaskProject = drawnNewTaskJournal = null;
            if (now == position && measuredProject == selected) return;
            drawnNewTaskProject = drawnNewTaskJournal = null;
            position = now;
            if (measuredProject != selected) { measuredProject = selected; page = 0; Measure(); }
            Changed?.Invoke();
        }

        public void FocusLeft()
        {
            drawnNewTaskProject = drawnNewTaskJournal = null;
        }

        private ProjectView? NewTaskProject()
        {
            var needed = (allProjects != null ? 1 : 0) + 2; // Navigation, new task and at least one task/empty row.
            return host.Connected && !host.Demonstration && host.PageRows(false) >= needed
                && MenuPage.Rows(needed) <= host.PageHeight(1, besideMenu: true) ? SelectedProject() : null;
        }

        private ProjectView? SelectedProject() => project?.Invoke() is string id && host.State?.Projects.TryGetValue(id, out var selected) == true
            ? selected : null;

        private List<WorkstreamView> Tasks()
        {
            var tasks = host.State?.Workstreams.Values.ToList() ?? new List<WorkstreamView>();
            if (project?.Invoke() is string selected) tasks.RemoveAll(task => task.ProjectId != selected);
            tasks.Sort(CharacterLineup.Compare);
            return tasks;
        }

        private PageLine Row(WorkstreamView task, bool withProject)
        {
            var state = host.State!;
            var presentation = CharacterPresenter.Present(task, state, host.Connected && !host.Demonstration);
            var badge = StateLanguage.BadgeOf(presentation);
            var tone = badge.Tone switch
            {
                GlazeTone.Attention => LineTone.Waiting,
                GlazeTone.Success => LineTone.Good,
                GlazeTone.Failure => LineTone.Problem,
                _ => LineTone.Primary,
            };
            var marks = StateLanguage.MarksOf(presentation);
            var fact = LineWords.Ours(badge.Text + (marks.Count > 0 ? " · " + string.Join(" · ", marks.Select(mark => mark.Word)) : ""));
            if (withProject && state.Projects.TryGetValue(task.ProjectId, out var view)) fact = fact.Then(LineWords.Ours(" · ")).Then(LineWords.TheirName(view.Name));
            // A row all of outside text: the title and its project's name lean, the state's icon stays Halcyonic's.
            var room = badge.Tone switch
            {
                GlazeTone.Attention => RoomTone.Attention,
                GlazeTone.Success => RoomTone.Good,
                GlazeTone.Failure => RoomTone.Bad,
                GlazeTone.Active => RoomTone.Working,
                _ => RoomTone.Quiet,
            };
            var projectName = state.Projects.TryGetValue(task.ProjectId, out var its) ? its.Name : null;
            return new PageLine(LineWords.TheirName(task.Title), icon: badge.Icon, fact: fact, tone: tone,
                action: OpenTask, key: task.WorkstreamId, opens: true, chosen: task.WorkstreamId == showing)
                .WithTaskState(new TaskRowState(badge.Word, room, marks.Count > 0 ? marks[0].Word : null, task.ProjectId, projectName, project?.Invoke() != null));
        }
    }

    /// <summary>A task row's facts apart (R3), so a card draws them by kind and never parses the older view's fact.</summary>
    public sealed class TaskRowState
    {
        public TaskRowState(string word, RoomTone tone, string? mark, string projectId, string? projectName, bool projectsList = false)
        {
            ProjectsList = projectsList;
            Word = word;
            Tone = tone;
            Mark = mark;
            ProjectId = projectId;
            ProjectName = projectName;
        }

        /// <summary>The state's word, Halcyonic's ("Needs You", "Working").</summary>
        public string Word { get; }

        /// <summary>The state's tone, as its badge's.</summary>
        public RoomTone Tone { get; }

        /// <summary>"Practice", "Demo" or "Recorded" where the task is not a real run; null for a real one.</summary>
        public string? Mark { get; }

        public string ProjectId { get; }

        /// <summary>Its project's name, theirs; null where the project is not known.</summary>
        public string? ProjectName { get; }

        /// <summary>The row stands on one project's own list of tasks, not on all work's.</summary>
        public bool ProjectsList { get; }
    }

    /// <summary>Tasks' own words (WORDS.md).</summary>
    public static class TasksText
    {
        public const string Close = "Close";
        public const string NewTask = "New Task";
        public const string Ask = "Ask";

        /// <summary>A page with no tasks on it.</summary>
        public const string None = "No Tasks";

        /// <summary>The closed bar while the computer has no task at all, as the first question asks what to work on.</summary>
        public const string NothingRunning = "Nothing Running";

        /// <summary>The subject: what waits for the person, or that nothing does.</summary>
        public static string Waiting(int count) => count switch
        {
            0 => "Nothing Waiting",
            1 => "Needs You · 1 Task",
            _ => "Needs You · " + count + " Tasks",
        };
    }
}
