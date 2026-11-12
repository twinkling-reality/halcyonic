#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// A new person's first page, the folders this computer allows with no project yet (C; FIELD's card, 2026-11-12): one
        /// list card titled by its heading ("Folders"), each folder a row with the folder's mark, its name set as a name to
        /// type (mono), its own note while connecting, its press the frame's own (it opens the folder's details, as today,
        /// until connecting is one press). A problem with the folders stands as the card's status in their place, with Try
        /// Again the main action. The column: the main action, Next Page where the rows run on, Settings and Usage, Close
        /// last. Known by its lines' kinds (a heading, folder rows, a problem's status; R3), never by its words. Any other
        /// line (a sentence while the folders are read, none free, not connected) falls to the older view, as before.
        /// </summary>
        private static RoomBar? Folders(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Side != null || frame.Lines.Count < 2 || !frame.Lines[0].Heading || frame.Lines.Any(line => line.ProjectIdentity)) return null;
            var rows = new List<RoomListRow>();
            RoomCardStatus? status = null;
            foreach (var line in frame.Lines.Skip(1))
            {
                if (line.CardStatus is RoomCardStatus problem && status == null && rows.Count == 0) status = problem;
                else if (line.Action == ProjectsScreens.ChooseFolder && line.Available && status == null)
                    rows.Add(new RoomListRow(RoomKind.Folder, line.Runs, line.RowNote, RoomTone.Quiet, mono: true,
                        press: new KitControl(KitPlace.Choice, frame, line.Action, line.Key, line.Words, line.Icon, true, null, false, line.Chosen),
                        chosen: line.Chosen));
                else return null;
            }
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
            var places = new[] { MenuPlace.Settings, MenuPlace.Usage }
                .SelectMany(place => frame.Sections.Where(section => !section.Chosen && section.Reached && section.Key == place.ToString()))
                .Select(section => new RoomAction(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false),
                    main: false, icon: GlazeIcon.Next));
            var column = new List<RoomAction>();
            if (main != null) column.Add(main);
            column.AddRange(others);
            column.AddRange(places);
            column.AddRange(leaving);
            var title = frame.Lines[0].Runs;
            return new RoomBar(RoomBarState.Page, body: title, column: column)
            {
                Card = status is RoomCardStatus said ? new RoomCard(title, status: said) : new RoomCard(title, list: rows),
            };
        }
    }
}
