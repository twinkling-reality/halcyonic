#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// The offered instructions (DESIGN 2.1), every action the file's own control: each instruction a choice, by the
        /// product's short name where it is one of its own (Continue, Summarize, Run Checks Again), else its words, the one
        /// chosen marked; the file's row to more of them; Review Instruction, the main action, once one is chosen (it opens
        /// the draft with the very words, which send only from there); and Cancel. A notice of the file's stands in the panel.
        /// </summary>
        private static RoomBar? Choose(KitFileBinding binding, VoiceStage voice, RoomBar tasks)
        {
            // An approval or a question comes first: the page of offered instructions never stands over either.
            if (binding.Model.Need != null || binding.Model.Ask != null) return null;
            var offered = binding.Controls.Where(control => control.Place == KitPlace.Choice && control.Action == FileScreens.Preset).ToList();
            if (offered.Count == 0) return null;
            var column = new List<RoomAction>();
            foreach (var control in offered.Where(control => control.Available))
            {
                var own = WorkspaceText.PresetInstructions.FirstOrDefault(preset => WorkspaceText.OneLine(preset.Text) == control.Words);
                column.Add(new RoomAction(control, main: false, own?.Label, mark: AnswerMark.Single, chosen: control.Chosen));
            }
            foreach (var step in binding.Controls.Where(control => control.Place == KitPlace.Step && control.Available))
                column.Add(new RoomAction(step, main: false, icon: step.Icon ?? GlazeIcon.Next));
            if (binding[KitPlace.Field] is { Available: true } review) column.Add(new RoomAction(review, main: true));
            if (binding[KitPlace.Back] is { Available: true } cancel) column.Add(new RoomAction(cancel, main: false));
            var says = binding.Model.Said.LastOrDefault(line => line.Action == null);
            return new RoomBar(RoomBarState.Choose, body: says?.Runs, column: column, wide: says == null);
        }
    }
}
