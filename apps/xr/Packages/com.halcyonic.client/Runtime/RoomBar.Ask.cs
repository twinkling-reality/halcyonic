#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// Ask beside the menu (<see cref="AskMenuColumn"/>; gap 7), in A17's language: the Ask's card in the panel's place, the
        /// assistant's words on the band (the frame's one claim line), and the column the frame's actions: the main one first
        /// (Send, or Send Answer), Hold to Talk, Type, Stop, and Close last. An action that cannot act is not drawn. Null for any
        /// other frame, a side panel, or a frame whose lines are not the column's own.
        /// </summary>
        private static RoomBar? Ask(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Subject != AskMenuColumn.Subject || frame.Card is not RoomCard card || frame.Side != null) return null;
            string? band = null;
            var typing = new List<RoomAction>();
            foreach (var line in frame.Lines)
            {
                if (line.Claim)
                {
                    if (band != null) return null;
                    band = line.Words;
                }
                else if (line.Action == AskMenuColumn.Type)
                {
                    if (!line.Available) return null;
                    typing.Add(new RoomAction(new KitControl(KitPlace.Step, frame, line.Action, line.Key, line.Words, line.Icon, true, null, false),
                        main: false, icon: line.Icon ?? GlazeIcon.Type));
                }
                // The person's answer as written or heard: theirs, which the card's panel does not hold; it stands on its line.
                else if (line.Action != null || !line.Runs.AllTheirs) return null;
            }
            RoomAction? main = null;
            RoomAction? talk = null;
            var others = new List<RoomAction>();
            var leaving = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                if (prompt.Holds)
                {
                    var control = new KitControl(KitPlace.Talk, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, holds: true);
                    talk = voice switch
                    {
                        VoiceStage.Listening => new RoomAction(control, main: true, VoiceText.ListeningWords),
                        VoiceStage.WritingDown => new RoomAction(control, main: true, VoiceText.WritingDownWords),
                        _ => new RoomAction(control, main: false),
                    };
                    continue;
                }
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var leads = main == null && !leaves && prompt.Main;
                var action = new RoomAction(new KitControl(leaves ? KitPlace.Close : KitPlace.Step, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false),
                    main: leads, icon: prompt.Icon);
                if (leads) main = action;
                else if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            var column = new List<RoomAction>();
            if (main != null) column.Add(main);
            if (talk != null) column.Add(talk);
            column.AddRange(typing);
            column.AddRange(others);
            column.AddRange(leaving);
            var answer = frame.Lines.FirstOrDefault(line => line.Action == null && !line.Claim);
            return new RoomBar(RoomBarState.Page, body: answer?.Runs ?? LineWords.None, column: column, band: band) { Card = card };
        }
    }
}
