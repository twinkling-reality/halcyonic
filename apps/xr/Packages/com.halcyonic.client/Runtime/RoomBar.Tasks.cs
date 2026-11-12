#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>The card's title for all work's tasks, Halcyonic's word (FIELD: there is no one thing to name).</summary>
        public const string TasksTitle = "Tasks";

        /// <summary>
        /// A project's tasks, the page a press on a project opens (the census of 2026-11-12: a person's second press), in
        /// A17's language (FIELD, 04:49Z): the project's name with its emblem as the title; each task a row with the task's
        /// mark, its name as theirs and its state's word as the note in its tone, a press opening it. A practice or recorded
        /// task's note is its mark's word ("Recorded"), and where every task carries the same mark, each note is its state
        /// and the mark stands once as the card's foot. Known by its rows' task states (R3), never by its title. Its rows
        /// that act (New Task, Ask, All Projects) and its prompts stand in the column: New Task the main action, the ways
        /// out last. Rows of several projects wait for FIELD's second line under a row, and fall to the older view till
        /// then; so do a side panel and a line it has no part for.
        /// </summary>
        private static RoomBar? Tasks(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Side != null || !frame.Lines.Any(line => line.TaskState != null)) return null;
            var rows = new List<RoomListRow>();
            var acting = new List<RoomAction>();
            RoomAction? main = null;
            foreach (var line in frame.Lines)
            {
                if (line.TaskState is TaskRowState task)
                {
                    if (line.Action == null || !line.Available) return null;
                    rows.Add(new RoomListRow(RoomKind.Task, line.Runs, task.Word, task.Tone,
                        press: new KitControl(KitPlace.Choice, frame, line.Action, line.Key, line.Words, line.Icon, true, null, false, line.Chosen),
                        // A row that opens its task may show a long title's lead, cut at a word, the whole a press away (FIELD's row, 07:19Z).
                        chosen: line.Chosen, excerpt: true));
                }
                else if (line.Action != null && line.Available && line.Fact == null && line.Control != LineControl.Input)
                {
                    var action = new RoomAction(new KitControl(KitPlace.Step, frame, line.Action, line.Key, line.Words, line.Icon, true, null, false),
                        main: main == null && line.Action == TasksColumn.NewTask, icon: line.Icon ?? GlazeIcon.Next);
                    if (action.Main) main = action;
                    else acting.Add(action);
                }
                else return null;
            }
            var states = frame.Lines.Where(line => line.TaskState != null).Select(line => line.TaskState!).ToList();
            if (states.Select(state => state.ProjectId).Distinct().Count() > 1) return null;
            // Every task the same mark: the mark once at the foot; otherwise a marked task's note is its mark.
            var shared = states.Select(state => state.Mark).Distinct().Count() == 1 ? states[0].Mark : null;
            if (shared == null)
                rows = rows.Zip(states, (row, state) => state.Mark == null ? row
                    : new RoomListRow(row.Kind, row.Name, state.Mark, RoomTone.Quiet, row.Mono, row.Press, row.Chosen)).ToList();
            var others = new List<RoomAction>();
            var leaving = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                if (prompt.Holds) return null;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var action = new RoomAction(new KitControl(leaves ? KitPlace.Close : KitPlace.Step, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false),
                    main: false, icon: prompt.Icon);
                if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            var column = new List<RoomAction>();
            if (main != null) column.Add(main);
            column.AddRange(acting);
            column.AddRange(others);
            column.AddRange(leaving);
            var project = states[0].ProjectsList ? states[0].ProjectName : null;
            var title = project == null ? LineWords.Ours(TasksTitle) : LineWords.TheirName(project);
            return new RoomBar(RoomBarState.Page, body: title, column: column)
            {
                Card = new RoomCard(title, head: project == null ? RoomCardHead.None : RoomCardHead.Project, list: rows,
                    more: shared == null ? null : LineWords.Ours(shared)),
            };
        }
    }
}
