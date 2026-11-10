#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// A reader of a frame the task's binding does not bind (a page of the file with no place of its own, or the frame of
    /// another column that stands beside the menu, as New Project's steps): it answers with the redesign's view of the
    /// frame, or null where the frame is not its. Every action it shows is a control made from the frame's own line or
    /// prompt, so a press raises the frame's own action and nothing else.
    /// </summary>
    public delegate RoomBar? RoomFrameMoment(MenuFrame frame, VoiceStage voice);

    public sealed partial class RoomBar
    {
        /// <summary>
        /// The readers, asked in this order; the first that answers draws the frame. A reader lives in a file of its own
        /// (a partial of this class holding one static function) and has one line here, above the general rule, which
        /// stays last.
        /// </summary>
        private static readonly RoomFrameMoment[] Frames =
        {
            Agent,
            NewProject,
            Page,
        };

        /// <summary>The redesign's view of <paramref name="frame"/>, a frame the task's binding does not bind; null where no reader takes it, and the older view draws it.</summary>
        public static RoomBar? OfFrame(MenuFrame frame, VoiceStage voice)
        {
            foreach (var reader in Frames)
                if (reader(frame, voice) is RoomBar own)
                {
                    own.FromMoment = true;
                    return own;
                }
            return null;
        }
    }
}
