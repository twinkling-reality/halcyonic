#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>The card's title for the Projects place, Halcyonic's own.</summary>
        public const string ProjectsTitle = "Projects";

        /// <summary>The heads over the Projects list (FIELD's sheet, tile f1): each project's name, then its state.</summary>
        public const string ProjectHead = "Project";

        public const string StateHead = "State";

        /// <summary>The Projects panel's Menu at its top right (tile f1), the bar's own press: it opens the workspace's own column
        /// (Move, Reset Position, Minimize, Other Apps), as Move does; it is no action of the frame's.</summary>
        public const string OpenMenu = "room-open-menu";

        public const string MenuWords = "Menu";

        /// <summary>The bar's own Menu where its panel carries one at its top right (the Projects place); null elsewhere.</summary>
        public RoomAction? PanelMenu { get; private set; }

        /// <summary>
        /// The column's actions stand in one line at the panel's foot, inside its glass, the main action lit, the panel widening to
        /// hold them inside the bar's envelope (FIELD's sheet, tile f1; the lead's 2026-11-17T02:57Z); false where the column stands
        /// beside the panel.
        /// </summary>
        public bool ActionsAtFoot { get; private set; }

        /// <summary>
        /// The Projects place, the first page a person sees (the census, JUDGE 2026-11-11), in the one language (FIELD's sheet,
        /// tile f1): one glass panel, HALORA over its title, its Menu at the top right, a list under the heads PROJECT and STATE,
        /// each project a row with its name as theirs and its state word in the label role, in the attention tone where it waits
        /// on the person; the task count is a fact of the state apart, never joined. The actions in one row at the panel's foot:
        /// New Project, the main way on, then the place's other prompts, the menu's other places, Close last, each the frame's
        /// own control.
        /// Only the list of known projects: with folders to connect, a chosen row's side panel, or anything else on the
        /// page, the older view draws it.
        /// </summary>
        private static RoomBar? Projects(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Subject != ProjectsText.Subject || frame.Side != null || frame.Lines.Count == 0
                || frame.Lines.Any(line => !line.ProjectIdentity || line.ProjectState == null || line.Action == null || !line.Available)) return null;
            var rows = frame.Lines.Select(line => new RoomListRow(RoomKind.None, line.Runs, line.ProjectState!.Word,
                line.ProjectState.NeedsYou ? RoomTone.Attention : RoomTone.Quiet,
                press: new KitControl(KitPlace.Choice, frame, line.Action!, line.Key, line.Words, line.Icon, true, null, false, line.Chosen),
                chosen: line.Chosen)).ToList();
            RoomAction? main = null;
            var others = new List<RoomAction>();
            var leaving = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                if (prompt.Holds) return null;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var leads = main == null && !leaves && prompt.Main;
                var action = new RoomAction(new KitControl(leaves ? KitPlace.Close : KitPlace.Step, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false),
                    main: leads, icon: prompt.Icon);
                if (leads) main = action;
                else if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            // Settings, then Usage, the menu's own place (the lead, 2026-11-12: every place one press from Projects); Tasks is the
            // shelf's and a project's. In that order, wherever the menu offers each.
            var places = new[] { MenuPlace.Settings, MenuPlace.Usage }
                .SelectMany(place => frame.Sections.Where(section => !section.Chosen && section.Reached && section.Key == place.ToString()))
                .Select(section => new RoomAction(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false),
                    main: false, icon: GlazeIcon.Next));
            // The column (FIELD's parts note): New Project, the main action; the place's paging where it has more; Settings, Usage; Close last.
            var column = new List<RoomAction>();
            if (main != null) column.Add(main);
            // New Project where it is not the main action (Pair Computer leads with no computer reached), then the paging.
            column.AddRange(others.Where(action => action.Control.Action is ProjectsScreens.NewProject or Footer.NextPage));
            column.AddRange(places);
            column.AddRange(leaving);
            return new RoomBar(RoomBarState.Page, body: LineWords.Ours(ProjectsTitle), column: column)
            {
                Card = new RoomCard(LineWords.Ours(ProjectsTitle), list: rows, label: WorkspaceText.Product, heads: new RoomListHeads(ProjectHead, StateHead)),
                PanelMenu = new RoomAction(new KitControl(KitPlace.Step, frame, OpenMenu, null, MenuWords, null, true, null, false), main: false),
                ActionsAtFoot = true,
            };
        }
    }
}
